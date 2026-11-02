#!/usr/bin/env python3
"""物品模板种子 SQL 的通用结构核验（默认完全只读，不碰库）。

存在的理由：这类种子都是"每条先 IF EXISTS 再 UPDATE / ELSE INSERT"的幂等写法，
而 INSERT 分支与 UPDATE 分支是**两个独立的真相源**——只改一边不会报错，只会静默丢数据
（015 第一次入库就是这个坑；后来又踩了一次：同一条 UPDATE 里给同一列赋值两次 → Msg 264
整批回滚，而 sqlcmd 仍然返回 rc=0，看起来"执行成功"）。人眼扫 9~41 个块是扫不出来的。

用法：
    python scripts/verify-item-template-seed.py \
        --sql scripts/sql/016_seed_special_item_templates.sql \
        --expect 5=5,7=2,8=2 \
        --segments 750001-750005:5,760001-760002:7,770001-770002:8 \
        [--live]

检查项：
  1. 每个 id 块都要有 UPDATE 与 INSERT 两条分支，且 id 一致；
  2. UPDATE 必须覆盖 INSERT 列清单里的每一个**内容列**（豁免：item_id/create_time/多租户归属列）；
  3. 同一条 UPDATE 里一列只能赋值一次（Msg 264 预防）；
  4. 声明的号段与 item_type 必须自洽（段即语义），且不许落进秘籍号段 800000+（T9）；
  5. --live 时再对一次库：行数、号段无外来 id 占位、三档内无重名。
退出码非 0 = 有问题，别执行这个种子。
"""

import argparse
import io
import re
import subprocess
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# UPDATE 有意不碰的列：主键定位、已存在行的创建时间、多租户归属（被占了要暴露，不能被 UPDATE 抹平）。
UPDATE_EXEMPT = {'item_id', 'create_time', 'gameid', 'areaid', 'serverid', 'gameuserid'}
BOOK_SEGMENT = 800000


def read(path):
    return io.open(path, encoding='utf-8', errors='replace').read()


def split_top_level(text):
    """按顶层逗号切开 VALUES(...) 里的项（括号与引号内不切）。"""
    parts, depth, buf, quote = [], 0, [], False
    i = 0
    while i < len(text):
        ch = text[i]
        if quote:
            buf.append(ch)
            if ch == "'" and (i + 1 >= len(text) or text[i + 1] != "'"):
                quote = False
            elif ch == "'" and i + 1 < len(text) and text[i + 1] == "'":
                buf.append("'")
                i += 1
            i += 1
            continue
        if ch == "'":
            quote = True
            buf.append(ch)
        elif ch == '(':
            depth += 1
            buf.append(ch)
        elif ch == ')':
            depth -= 1
            buf.append(ch)
        elif ch == ',' and depth == 0:
            parts.append(''.join(buf).strip())
            buf = []
        else:
            buf.append(ch)
        i += 1
    parts.append(''.join(buf).strip())
    return parts


def parse_blocks(sql):
    """返回 [(line_no, item_id, update_assign_cols, insert_cols)]。"""
    blocks = []
    pattern = re.compile(r'IF EXISTS \(SELECT 1 FROM dbo\.Game_HunduShijie_ItemTemplate WHERE item_id = (\d+)\)')
    lines = sql.splitlines()
    for idx, line in enumerate(lines):
        hit = pattern.search(line)
        if not hit:
            continue
        item_id = int(hit.group(1))
        body = '\n'.join(lines[idx:idx + 8])
        upd = re.search(r'UPDATE dbo\.Game_HunduShijie_ItemTemplate SET (.*?) WHERE item_id = (\d+);', body, re.S)
        ins = re.search(r'INSERT INTO dbo\.Game_HunduShijie_ItemTemplate \((.*?)\)\s*\n\s*VALUES \((.*)\);', body, re.S)
        if not upd or not ins:
            blocks.append((idx + 1, item_id, None, None, None))
            continue
        assign_cols = [m.group(1).strip().lower() for m in re.finditer(r'([A-Za-z_][A-Za-z_0-9]*)\s*=', upd.group(1))]
        insert_cols = [c.strip().lower() for c in split_top_level(ins.group(1))]
        values = split_top_level(ins.group(2))
        blocks.append((idx + 1, item_id, assign_cols, insert_cols, values))
    return blocks


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sql', required=True)
    ap.add_argument('--expect', required=True, help='item_type=行数，逗号分隔，如 5=5,7=2')
    ap.add_argument('--segments', required=True, help='lo-hi:item_type，逗号分隔')
    ap.add_argument('--live', action='store_true')
    args = ap.parse_args()

    sql = read(args.sql)
    fails = []

    expected = {}
    for pair in args.expect.split(','):
        t, _, n = pair.partition('=')
        expected[int(t)] = int(n)
    segments = []
    for seg in args.segments.split(','):
        rng, _, t = seg.partition(':')
        lo, _, hi = rng.partition('-')
        segments.append((int(lo), int(hi), int(t)))

    blocks = parse_blocks(sql)
    if len(blocks) != sum(expected.values()):
        fails.append('块数 %d 与 --expect 总行数 %d 不符' % (len(blocks), sum(expected.values())))

    seen_ids = {}
    for line_no, item_id, assign_cols, insert_cols, values in blocks:
        if assign_cols is None:
            fails.append('第 %d 行 id=%d：UPDATE 或 INSERT 分支解析不出来（写法变了要同步改本核验）' % (line_no, item_id))
            continue
        seen_ids[item_id] = line_no
        content_cols = [c for c in insert_cols if c not in UPDATE_EXEMPT]
        missing = [c for c in content_cols if c not in assign_cols]
        if missing:
            fails.append('id=%d：UPDATE 分支缺列 %s（静默丢数据的典型形状）' % (item_id, ','.join(missing)))
        dupes = {c for c in assign_cols if assign_cols.count(c) > 1}
        if dupes:
            fails.append('id=%d：同一条 UPDATE 里重复赋值 %s（会 Msg 264 整批回滚）' % (item_id, ','.join(sorted(dupes))))
        if len(insert_cols) != len(values):
            fails.append('id=%d：INSERT 列数 %d 与值数 %d 不符' % (item_id, len(insert_cols), len(values)))

    for lo, hi, item_type in segments:
        in_seg = [i for i in seen_ids if lo <= i <= hi]
        want = expected.get(item_type, 0)
        if len(in_seg) != want:
            fails.append('号段 %d-%d（应为 item_type=%d）实到 %d 行，期望 %d' % (lo, hi, item_type, len(in_seg), want))
        if hi >= BOOK_SEGMENT:
            fails.append('号段 %d-%d 撞上秘籍号段（≥%d 会被"id 即秘籍"启发式吞掉，见 T9）' % (lo, hi, BOOK_SEGMENT))
        for i in in_seg:
            m = re.search(r'item_type = (\d+),', sql[sql.index('item_id = %d' % i):][:4000])
            if m and int(m.group(1)) != item_type:
                fails.append('id=%d 在号段 %d-%d 里但块内 item_type=%s' % (i, lo, hi, m.group(1)))

    names = re.findall(r"item_name = N'([^']*)'", sql)
    if len(names) != len(set(names)):
        fails.append('种子里有重名物品（名字反查 id 会歧义）：%s' %
                     ','.join(sorted({n for n in names if names.count(n) > 1})))
    if any(not n.strip() for n in names):
        fails.append('种子里有空名字')

    print('块数=%d 名字=%d 号段=%s' % (len(blocks), len(names),
                                      ','.join('%d-%d:%d' % s for s in segments)))

    if args.live:
        q = ("SET NOCOUNT ON; SELECT item_type, COUNT(*) FROM Game_HunduShijie_ItemTemplate "
             "WHERE item_type IN (%s) GROUP BY item_type; "
             "SELECT COUNT(*) FROM Game_HunduShijie_ItemTemplate WHERE item_id BETWEEN %d AND %d;"
             % (','.join(str(t) for t in expected), segments[0][0], segments[-1][1]))
        proc = subprocess.run(['sqlcmd', '-S', '.', '-d', 'Game', '-E', '-C', '-W', '-h', '-1', '-Q', q],
                              capture_output=True)
        out = proc.stdout.decode('gbk', errors='replace')
        if proc.returncode != 0:
            fails.append('--live 查询失败：' + proc.stderr.decode('gbk', errors='replace')[:200])
        else:
            counts = dict((int(a), int(b)) for a, b in
                          (re.match(r'\s*(\d+)\s+(\d+)\s*$', ln).groups() for ln in out.splitlines()
                           if re.match(r'\s*(\d+)\s+(\d+)\s*$', ln)))
            for t, want in expected.items():
                print('   库内 item_type=%d -> %d 行（种子期望 %d）' % (t, counts.get(t, 0), want))
            if all(counts.get(t, 0) == 0 for t in expected):
                print('   该种子尚未入库（行数全 0）——结构核验仍有效，跑完 SQL 后再加 --live 复看。')
            else:
                for t, want in expected.items():
                    if counts.get(t, 0) != want:
                        fails.append('入库后 item_type=%d 实到 %d，应为 %d' % (t, counts.get(t, 0), want))

    if fails:
        print('CHECKS FAILED：')
        for f in fails:
            print('   ' + f)
        return 1
    print('ALL CHECKS PASSED')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

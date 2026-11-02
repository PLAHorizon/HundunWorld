#!/usr/bin/env python3
"""任务奖励的"客户端叫得出名字"覆盖率普查（只读库与源码；只在 docs/ 下产出一份明细 JSON）。

为什么要这条：目标写的是"奖励物品关联**所有**游戏物品（装备、技能书、材料、特殊物品）"。
发放链路不挑类型（服务端压根没有运行时物品表），所以"能不能选中、能不能到账"一直是真的。
但玩家**看见**的是什么，取决于客户端那份硬编码目录 `ItemCatalog`：它按
"数字段 → 装备表 → 材料表 → 未知回退"解析，落不进任何一张表的 id 就显示成占位名。
T9 里那句"15 件五行素材/信物显示成无名残页"是按 id 区段推出来的，不是全量核过。
这条脚本把 171 行逐个数一遍，给出确切的"叫得出名字 / 叫不出"清单，让 T9/T11 的取舍有数字可依。

判定口径（与 ItemCatalog.Build + 客户端技能模板下行对齐）：
  1) id >= 800000 走秘籍分支：客户端 `ItemCatalog.FromBook` 反查 `SkillDatabase.GetSkill(id-800000)`，
     而 `SkillDatabase` 会被**服务端下行的技能模板覆盖/补齐**（`SkillDatabase` 接收
     `IList<SkillTemplateEntryInfo>` 的那条合并入口），所以运行态的真值源是库里的技能模板表，
     不是客户端硬编码那批种子技能。判据 = (id - 800000) 是否在 `Game_HunduShijie_SkillTemplate.skill_id` 里。
     ——我第一版就是在这里判断错了：拿客户端静态技能表数一遍，得出"91 本秘籍全叫不出名字"，
       而真值是能反查到。（教训：先弄清运行态的数据源，再谈"覆盖不全"。）
  2) 装备表 GetEquipment 的 switch 命中 → 真名。
  3) 材料表 GetMaterial 的 switch 命中 → 真名。
  4) ItemCatalog 里 const 登记的丹药/宝石 → 真名。
  5) 其余 → 占位名（"神秘物品"/"无名残页"），算叫不出。
"""

import io
import json
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLIENT = os.path.join(REPO, 'HundunWorld', 'Source', 'Game')
TABLE = 'Game_HunduShijie_ItemTemplate'
BOOK_BASE = 800000

sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def read(path):
    return io.open(path, encoding='utf-8', errors='replace').read()


def switch_ids(path, anchor):
    """取 anchor 之后第一个 switch 块里 `1234 =>` 形式的 case 常量。"""
    text = read(path)
    at = text.index(anchor)
    open_idx = text.index('{', text.index('switch', at))
    depth = 0
    close = open_idx
    for i in range(open_idx, len(text)):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                close = i
                break
    body = text[open_idx:close]
    return set(int(m) for m in re.findall(r'^\s*(\d{2,})\s*=>', body, re.M))


def skill_ids():
    """运行态真值：库里的技能模板（会被下行到客户端并合并进 SkillDatabase）∪ 客户端静态种子。"""
    text = read(os.path.join(CLIENT, 'Combat', 'Skills', 'SkillDatabase.cs'))
    static_ids = set(int(m) for m in re.findall(r'SkillId\s*=\s*(\d+)', text))
    proc = subprocess.run(['sqlcmd', '-S', '.', '-d', 'Game', '-E', '-C', '-W', '-h', '-1',
                           '-Q', 'SET NOCOUNT ON; SELECT skill_id FROM Game_HunduShijie_SkillTemplate;'],
                          capture_output=True)
    out = proc.stdout.decode('gbk', errors='replace')
    assert proc.returncode == 0, '技能模板查询失败：' + proc.stderr.decode('gbk', errors='replace')[:200]
    db_ids = set(int(line.strip()) for line in out.splitlines() if line.strip().isdigit())
    assert len(db_ids) > 50, '技能模板行数异常：%d' % len(db_ids)
    return static_ids | db_ids, db_ids, static_ids


def catalog_constants():
    text = read(os.path.join(CLIENT, 'Inventory', 'ItemCatalog.cs'))
    ids = set()
    for name in ('HealingSalveTemplateId', 'EnergyPillTemplateId', 'GemMetalId', 'GemWoodId',
                 'GemWaterId', 'GemFireId', 'GemEarthId'):
        m = re.search(name + r'\s*=\s*(\d+)', text)
        if m:
            ids.add(int(m.group(1)))
    return ids


def query_db():
    sql = ("SET NOCOUNT ON; SELECT item_id, item_type FROM %s ORDER BY item_id;" % TABLE)
    proc = subprocess.run(['sqlcmd', '-S', '.', '-d', 'Game', '-E', '-C', '-W', '-h', '-1', '-Q', sql],
                          capture_output=True)
    out = proc.stdout.decode('gbk', errors='replace')
    if proc.returncode != 0:
        raise SystemExit('sqlcmd 失败：' + proc.stderr.decode('gbk', errors='replace')[:300])
    rows = []
    for line in out.splitlines():
        parts = line.split()
        if len(parts) == 2 and all(p.isdigit() for p in parts):
            rows.append((int(parts[0]), int(parts[1])))
    return rows


def main():
    equipment = switch_ids(os.path.join(CLIENT, 'Equipment', 'EquipmentDatabase.cs'), 'GetEquipment')
    materials = switch_ids(os.path.join(CLIENT, 'Equipment', 'Material', 'MaterialData.cs'), 'GetMaterial')
    consts = catalog_constants()
    skills, db_skill_ids, static_skill_ids = skill_ids()

    rows = query_db()
    assert len(rows) > 100, '库里物品行异常：%d' % len(rows)

    named, placeholder, books_need_downlink = [], [], []
    for item_id, item_type in rows:
        if item_id >= BOOK_BASE:
            skill_id = item_id - BOOK_BASE
            ok = skill_id in skills
            if ok and skill_id not in static_skill_ids:
                books_need_downlink.append(item_id)   # 只能靠服务端技能模板下行才叫得出名字
        else:
            ok = item_id in equipment or item_id in materials or item_id in consts
        (named if ok else placeholder).append((item_id, item_type))

    by_type = {}
    for item_id, item_type in rows:
        by_type.setdefault(item_type, [0, 0])
        by_type[item_type][0] += 1
    for item_id, item_type in placeholder:
        by_type[item_type][1] += 1

    type_names = {0: '武器', 1: '防具', 2: '饰品', 3: '消耗品', 4: '材料', 5: '任务物品', 6: '秘籍', 7: '配方', 8: '宝箱'}
    print('物品模板 %d 行；客户端叫得出名字 %d 行，占位 %d 行' % (len(rows), len(named), len(placeholder)))
    print('按档位（档位 总数 / 叫不出）：')
    for t in sorted(by_type):
        total, bad = by_type[t]
        print('   %s(%d): %d / %d' % (type_names.get(t, '未知'), t, total, bad))
    print('叫不出名字的 id 全清单：', ','.join(str(i) for i, _ in placeholder) or '（无）')
    print('其中只能靠服务端技能模板下行才叫得出名字的秘籍：%d 本' % len(books_need_downlink))

    # 硬不变式：item_type=6（秘籍）的行必须能从技能模板里反查到——否则玩家拿到的就是"无名残页"。
    orphan_books = sorted(i for i, t in placeholder if t == 6)
    if orphan_books:
        print('CHECKS FAILED：这些秘籍反查不到技能，会显示成"无名残页"：', orphan_books)
        return 1

    report = {
        'total': len(rows),
        'named': len(named),
        'placeholder': [i for i, _ in placeholder],
        'by_type': {str(t): by_type[t] for t in sorted(by_type)},
    }
    out_path = os.path.join(REPO, 'docs', 'quest_reward_display_coverage.json')
    io.open(out_path, 'w', encoding='utf-8').write(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('明细已落盘：docs/quest_reward_display_coverage.json')

    # 现状闸门：占位集合只许是这批（它们是 T9 点名的五行素材/信物）。
    # 哪天新加物品忘了同步客户端目录，这里就会变红，而不是等玩家看到"神秘物品"才发现。
    expected = {910001, 910002, 910003, 910004, 910005, 910006, 910007, 910008, 910009, 910010,
                910011, 910012, 910013, 910014, 910015}
    got = set(i for i, _ in placeholder)
    if got == expected:
        print('ALL CHECKS PASSED：叫不出的正是 T9 登记的 15 件，没有新增。')
    else:
        print('CHECKS FAILED：新增叫不出的 %s；已修复叫得出的 %s' % (sorted(got - expected), sorted(expected - got)))
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

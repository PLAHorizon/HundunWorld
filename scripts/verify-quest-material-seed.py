#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""015 材料种子的入库前核验（只读；连库部分只跑 CLI 的 catalog，不写任何东西）。

为什么要有这个脚本：`015_seed_material_item_templates.sql` 一旦执行就是往生产口径的物品模板表里
写 41 行，而它的正确性依赖三处**互不引用**的真相：SQL 自己的双分支、客户端 MaterialData 的 id/名称、
以及库里现有的 item_id 空间。这三处任何一处漂移都不会让 SQL 报错，只会让现场"奖励发成别的物品"。

核验项：
1. 行数=41、id 连续且等于 700001..700041、无重复
2. INSERT 列清单与**已在真库执行过的 006 装备种子**逐列一致（同一张表，006 能跑通就是这张表的写法凭证）
3. 每行 VALUES 的值个数 == 列个数（少一个值就是错位写入，SQL 照样"成功"）
4. UPDATE 分支覆盖全部内容列（幂等双分支是两个真相源，漏列会静默丢数据）
5. 种子的 (id, 名称) 与客户端 MaterialData 一一对应（"改材料两边一起改"这条不变量的机器版）
6. 连库时：41 个 id 与现有 item_id 无重叠；并如实报当前 item_type=4 的行数

用法：python scripts/verify-quest-material-seed.py [--offline]
  --offline 跳过第 6 项（不跑 CLI、不连库），其余照常。
退出码：0 全部通过；1 有问题（逐条打印）。
"""
import io
import json
import os
import re
import subprocess
import sys
import collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SEED = os.path.join(ROOT, "scripts", "sql", "015_seed_material_item_templates.sql")
EQUIP = os.path.join(ROOT, "scripts", "sql", "006_seed_equipment_item_templates.sql")
MATERIAL = os.path.join(ROOT, "HundunWorld", "Source", "Game",
                        "Equipment", "Material", "MaterialData.cs")

# UPDATE 有意不碰的列：主键、创建时间、多租户归属键（理由见 015 文件头）
EXEMPT = {"item_id", "create_time", "gameid", "areaid", "serverid", "gameuserid"}


def read(path):
    return io.open(path, encoding="utf-8").read()


def insert_columns(text):
    m = re.search(r"INSERT INTO (?:dbo\.)?Game_HunduShijie_ItemTemplate \(([^)]*)\)", text)
    if not m:
        raise AssertionError("找不到 INSERT 列清单")
    return [c.strip() for c in m.group(1).split(",")]


def split_top_level(text):
    """按顶层逗号切分 SQL 值列表（尊重单引号与括号）。"""
    parts, buf, depth, in_str = [], [], 0, False
    for ch in text:
        if ch == "'":
            in_str = not in_str
        if not in_str:
            if ch in "([":
                depth += 1
            elif ch in ")]":
                depth -= 1
            elif ch == "," and depth == 0:
                parts.append("".join(buf).strip())
                buf = []
                continue
        buf.append(ch)
    parts.append("".join(buf).strip())
    return parts


def main():
    offline = "--offline" in sys.argv
    problems = []

    seed = read(SEED)
    equip = read(EQUIP)
    mat = read(MATERIAL)

    cols = insert_columns(seed)
    cols_006 = insert_columns(equip)
    if cols != cols_006:
        problems.append("列清单与 006 不一致：只在015=%s 只在006=%s"
                        % ([c for c in cols if c not in cols_006],
                           [c for c in cols_006 if c not in cols]))

    rows = re.findall(r"VALUES \((.*?)\);", seed, re.S)
    ids = [int(m) for m in re.findall(r"VALUES \((\d+),", seed)]
    if len(rows) != 41:
        problems.append("INSERT 行数=%d，期望 41" % len(rows))
    dups = [k for k, v in collections.Counter(ids).items() if v > 1]
    if dups:
        problems.append("id 重复：%s" % dups)
    if sorted(ids) != list(range(700001, 700042)):
        problems.append("id 段不连续/越界：%s … %s" % (sorted(ids)[:2], sorted(ids)[-2:]))

    for i, block in enumerate(rows):
        values = split_top_level(block)
        if len(values) != len(cols):
            problems.append("第 %d 行值数=%d 但列数=%d（会错位写入）"
                            % (i + 1, len(values), len(cols)))
            break

    updates = re.findall(r"UPDATE (?:dbo\.)?Game_HunduShijie_ItemTemplate SET ([\s\S]*?) WHERE", seed)
    if len(updates) != len(rows):
        problems.append("UPDATE 分支数=%d 与 INSERT 行数=%d 不等价" % (len(updates), len(rows)))
    # 同一个 UPDATE 里给同一列赋值两次 → SQL Server Msg 264，整批回滚，而 sqlcmd 仍返回 rc=0。
    # （015 第一次补列覆盖时就踩到了：末尾多追加了一次 update_time，静默 0 行入库。）
    for index, body in enumerate(updates):
        names = re.findall(r"([A-Za-z_]\w*)\s*=", body)
        twice = sorted({name for name in names if names.count(name) > 1})
        if twice:
            problems.append("第 %d 条 UPDATE 重复赋值这些列（会 Msg 264 并整批回滚）：%s" % (index + 1, twice))
            break
    set_cols = set()
    for body in updates:
        for name in re.findall(r"([A-Za-z_]\w*)\s*=", body):
            set_cols.add(name.lower())
    missing = [c for c in cols if c.lower() not in set_cols and c.lower() not in EXEMPT]
    if missing:
        problems.append("UPDATE 分支漏了这些列（重跑会静默丢数据）：%s" % missing)

    code_ids = sorted(int(m) for m in re.findall(r"MaterialId = (\d+),", mat))
    if code_ids != sorted(ids):
        problems.append("客户端 MaterialData 的 id 与种子不一致：code=%d seed=%d 对称差=%s"
                        % (len(code_ids), len(ids), sorted(set(code_ids) ^ set(ids))))

    code_names = re.findall(r'MaterialName = "([^"]+)"', mat) or re.findall(r'Name = "([^"]+)"', mat)
    seed_names = re.findall(r"VALUES \(70\d{4}, N'([^']+)'", seed)
    if len(code_names) != len(seed_names):
        problems.append("名称条数不一致：code=%d seed=%d" % (len(code_names), len(seed_names)))
    else:
        mismatched = [(a, b) for a, b in zip(code_names, seed_names) if a != b]
        if mismatched:
            problems.append("按序名称不匹配（前 5 条）：%s" % mismatched[:5])

    if not offline:
        try:
            proc = subprocess.run(
                ["dotnet", "run", "--project", "Horizon.QuestCli", "-c", "Debug",
                 "-p:BaseOutputPath=bin/Verify/", "--", "catalog"],
                cwd=ROOT, capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=900)
            line = [l for l in proc.stdout.splitlines() if l.startswith("{")][-1]
            payload = json.loads(line)
            if not payload.get("ok"):
                problems.append("CLI catalog 返回失败：%s" % payload.get("message"))
            else:
                items = payload["data"]["items"]
                live_ids = set(i["templateId"] for i in items)
                collide = sorted(set(ids) & live_ids)
                type4 = len([i for i in items if i["itemType"] == 4])
                print("库实况：物品模板 %d 行，item_type=4（材料）%d 行，id 上界 %d"
                      % (len(live_ids), type4, max(live_ids)))
                if len(collide) == len(ids) and type4 == len(ids):
                    # 41 个 id 全在、材料行数正好 41 → 种子已经落过库。
                    # 这不是错误：本脚本是幂等的，重跑走 UPDATE 分支（列覆盖已核验过）。
                    print("015 已入库：重跑只会按 UPDATE 分支原地刷新，不会再新增行")
                elif collide:
                    problems.append("部分材料 id 已在库但材料行数=%d（期望 %d）：%s"
                                    % (type4, len(ids), collide[:10]))
                else:
                    print("41 个材料 id 与现有 id 无重叠（尚未入库）")
        except Exception as ex:
            problems.append("读取库实况失败：%s" % ex)
    else:
        print("已跳过连库核验（--offline）")

    print("静态核验：行数=%d 列数=%d UPDATE分支=%d" % (len(rows), len(cols), len(updates)))
    if problems:
        for p in problems:
            print("FAIL: " + p)
        return 1
    print("ALL CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())

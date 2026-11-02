"""020 种子脚本的列名对账：种子引用的每个列名，必须在 019 建表脚本里真的存在。

为什么要有这个闸门：020 **只生成不执行**（写库等用户点头），于是拼错一个列名不会有任何人立刻发现——
它会在几个月后用户第一次跑种子时报"列名无效"，而那时没人记得是哪一列。
本脚本用 019（EF 生成的权威 DDL）当参照，把 020 里出现的列标识符逐个核对。
"""
import io
import os
import re
import sys

SQL_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "scripts", "sql")
DDL = os.path.join(SQL_DIR, "019_monster_editor_tables.sql")
SEED = os.path.join(SQL_DIR, "020_seed_monster_templates.sql")

# 这些标识符是表名/别名/SQL 关键字，不参与列名对账
IGNORE = {
    "Game_HunduShijie_MonsterTemplate", "Game_HunduShijie_MonsterBehavior",
    "Game_HunduShijie_MonsterSpawnPoint", "Game_HunduShijie_MonsterSkillMount",
    "Game_HunduShijie_MonsterPatrolRoute", "Game_HunduShijie_MonsterPatrolPoint",
    "Game_HunduShijie_MonsterLootBinding", "Game_HunduShijie_MonsterDropEntry",
    "t", "s", "m", "sp", "b", "VALUES", "SELECT", "COUNT", "CASE", "WHEN",
    "UPDATE", "INSERT", "MERGE", "INTO", "USING", "ON", "SET", "NULL",
}

# 别名.列 里的"列"才是我们要核对的对象；同时收集 s(...) 派生表的列（它们不属于任何物理表）
derived = {"monster_id", "name", "level", "model_key", "wuxing_domain", "rank",
           "spawn_point_id", "shard_id", "x"}


def parse_ddl_columns(text):
    """从 CREATE TABLE 块里取列名；返回 {表名: [列名]}。"""
    out = {}
    for m in re.finditer(r"CREATE TABLE \[(\w+)\]\s*\((.*?)\n\);", text, re.S):
        table, body = m.group(1), m.group(2)
        cols = []
        for line in body.splitlines():
            line = line.strip().rstrip(",")
            cm = re.match(r"^\[(\w+)\]", line)
            if cm and not line.upper().startswith(("CONSTRAINT", "PRIMARY", "FOREIGN", "UNIQUE")):
                cols.append(cm.group(1))
        out[table] = cols
    return out


def main():
    ddl_text = io.open(DDL, encoding="utf-8-sig").read()
    seed_text = io.open(SEED, encoding="utf-8-sig").read()
    tables = parse_ddl_columns(ddl_text)

    if len(tables) != 8:
        print("FAIL: 019 里解析到 %d 张表，期望 8（解析器或建表脚本坏了）" % len(tables))
        return 1

    universe = set()
    for cols in tables.values():
        universe |= set(cols)

    # 收集 020 里"表.列"与 INSERT 括号里的裸列名
    refs = set()
    for m in re.finditer(r"(?:\bt|\bm|\bsp|\bb)\.(\w+)", seed_text):
        refs.add(m.group(1))
    for m in re.finditer(r"INSERT \(([^)]*)\)", seed_text, re.S):
        for token in m.group(1).split(","):
            token = token.strip()
            if token:
                refs.add(token)

    checked = sorted(r for r in refs if r not in IGNORE and r not in derived)
    missing = [r for r in checked if r not in universe]

    print("019 解析表数: %d" % len(tables))
    print("020 引用的列名（去重后参与核对）: %d" % len(checked))
    print("MonsterTemplate 列数: %d / MonsterBehavior 列数: %d / MonsterSpawnPoint 列数: %d"
          % (len(tables.get("Game_HunduShijie_MonsterTemplate", [])),
             len(tables.get("Game_HunduShijie_MonsterBehavior", [])),
             len(tables.get("Game_HunduShijie_MonsterSpawnPoint", []))))

    # 防空转断言：核对数量必须落到一个下限上，否则"0 条重名"也可能是解析器什么都没抓到
    if len(checked) < 40:
        print("FAIL: 只核对了 %d 个列名，低于 40 的下限 —— 解析器很可能什么都没抓到" % len(checked))
        return 1

    if missing:
        print("FAIL: 以下列在 019 的 DDL 中不存在: %s" % ", ".join(missing))
        return 1

    print("OK: 020 引用的全部列名都在 019 的 DDL 中存在")
    return 0


if __name__ == "__main__":
    sys.exit(main())

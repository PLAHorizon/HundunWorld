#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""客户端两张表（外观 / 文本）不许比它们的源陈旧 —— 一次真导出对账。

为什么需要这条：两张客户端表的键是 **(modelKey, …)**，而写侧的源一张在库里（外观的 prefab 路径）、
一张在旁挂文件里（喊话文案）。有人改了源而没重导，表现是**游戏里静默用旧的那份**：
外观改了路径而怪还是占位人形、文案改了而怪念的是上一句。这与「没配」在玩家那边同形，
而这条链上原本没有任何一处会因此出声（`export-*` 一直是手动动词）。
**本机第一跑就抓到一条真分歧**：盘上那份外观表与排练库 `Game_MonsterRT` 逐条对上，
而与生产 `Game` 不同步（生产里 1 号已经带上了 prefab 路径，而盘上那份记的是另一套模型键）。

判据的取法（这条是本脚本的全部根据）：
  **另跑一次真导出，把产物与盘上那份按同一套键对账**。两边同形（都是 modelKey 那套键），
  所以能逐条比「新增了谁、少了谁」；按「文件里有没有 modelKey 这个字符串」去数会把两张异形的东西
  混成一句「看着都在」，那是坏尺（工程里栽过：镜像清单对账要先命名同构）。

三档结论（沿用 verify-monster-content-audit.py 的分档纪律）：
  · 硬失败：连不上 CLI / 没给库 / 盘上那份不存在 / **两边键集合不一致** / problems 清单漂了。
  · 对账降级并如实打印：这次导出零条 ⇒ 这张表今天确实没内容，只比 problems，不判红
    （「没内容」是 §11.2 的 backlog，不是这条检查该拦的事；每填必响会把真问题埋成噪音）。
  · 红之后**自动追一步**：拿仓里已知的其它库各导一次，说清盘上那份到底是哪个库的产物。
    只报「不同步」会让人以为重导就行，而真正的问题可能是**两张表来自两个不同的库**——
    那种情况下拿生产的那份去覆盖会把另一个会话的内容盖掉，判据要先把这件事说出口。

用法：
  python scripts/verify-monster-client-tables.py --db Game
  python scripts/verify-monster-client-tables.py --db Game_MonsterRT --only texts
"""
import argparse
import json
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(REPO, "Horizon.MonsterCli", "bin", "Debug", "net10.0", "Horizon.MonsterCli.exe")
DATA_DIR = os.path.join(REPO, "HundunWorld", "Data", "Monsters")

# 对账失败时自动试一遍的库（名字来自仓内既有脚本的用法，不是猜的）。
FALLBACK_DBS = ("Game", "Game_MonsterRT")

# 两张表一起守：同一条失败形状（改了源没重导 ⇒ 游戏用旧的），只是键不同。
# key 里带的字段都是**会被玩家看见的那几格**：漂了就是内容变了，与「重跑一次时间戳」无关。
TABLES = {
    "texts": {
        "verb": "export-texts",
        "committed": os.path.join(DATA_DIR, "client_texts.json"),
        "source": os.path.join(DATA_DIR, "monster_texts.json"),
        "array": "texts",
        "key": ("modelKey", "sayId"),
        "label": "喊话文本表",
    },
    "visuals": {
        "verb": "export-visuals",
        "committed": os.path.join(DATA_DIR, "client_visuals.json"),
        "source": None,
        "array": "visuals",
        "key": ("modelKey", "prefabPath"),
        "label": "外观表",
    },
}


def load_table(path):
    """读一份客户端表，返回 (整份文档, problems 列表)；文件不在返回 None。"""
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as handle:
        doc = json.load(handle)
    return doc, doc.get("problems") or []


def keys_of(doc, spec):
    out = set()
    for row in doc.get(spec["array"]) or []:
        out.add(tuple(str(row.get(field, "")) for field in spec["key"]))
    return out


def conn_string(db):
    return "Server=localhost;Database=" + db + ";Integrated Security=True;TrustServerCertificate=True"


def export_fresh(spec, cli, tmp_dir, db=None):
    """真跑一次动词，把产物写到临时目录（绝不碰盘上那份）。给了 db 就只改这次子进程的环境。"""
    out_path = os.path.join(tmp_dir, "fresh_" + spec["verb"] + "_" + (db or "env") + ".json")
    env = None
    if db:
        env = dict(os.environ)
        env["DB_GAME_CONNECTION_STRING"] = conn_string(db)
    proc = subprocess.run([cli, spec["verb"], "--out", out_path], capture_output=True, env=env)
    raw = proc.stdout.decode("utf-8", "replace")
    start = raw.find("{")
    if start < 0:
        return None, "CLI 没有 JSON 输出（退出码 " + str(proc.returncode) + "）：" + raw[:200]
    try:
        receipt = json.loads(raw[start:])
    except json.JSONDecodeError as error:
        return None, "输出不是合法 JSON：" + str(error)
    if not receipt.get("ok"):
        return None, str(receipt.get("message") or "导出失败")
    if not os.path.exists(out_path):
        return None, "回执说成功而产物不在盘上：" + out_path
    with open(out_path, encoding="utf-8") as handle:
        doc = json.load(handle)
    return doc, None


def probe_other_dbs(spec, cli, tmp_dir, committed_keys, committed_problems, given_db):
    """红之后追一步：盘上那份到底是哪个库的产物（只试给定库之外的那几个已知库）。"""
    hits = []
    for other in FALLBACK_DBS:
        if other == given_db:
            continue
        doc, error = export_fresh(spec, cli, tmp_dir, other)
        if error:
            hits.append(other + "：跑不起来（" + error + "）")
            continue
        if keys_of(doc, spec) == committed_keys and sorted(doc.get("problems") or []) == sorted(committed_problems):
            hits.append(other + "：逐条对上 ⇒ 盘上那份就是这个库的产物"
                                   "（要重导先定「游戏现在该读哪个库」，别顺手覆盖另一个会话的内容）")
        else:
            hits.append(other + "：也不对上（那边 " + str(len(keys_of(doc, spec))) + " 条）")
    return hits


def warn_if_source_untracked(name, spec):
    """旁挂源文件在不在版本控制里：喊话文案的本体就那一份，没历史＝删了就没有。"""
    source = spec.get("source")
    if not source or not os.path.exists(source):
        return None
    probe = subprocess.run(["git", "ls-files", "--", source], cwd=REPO,
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    if (probe.stdout or "").strip():
        return None
    return ("  · 提醒：" + os.path.basename(source) + " 不在版本控制里 ⇒ 这份手写文案没有历史，"
            "误删/误改不可回退（要不要入库是仓库口径，这里只把它说出口）")


def check(name, spec, cli, tmp_dir, given_db, committed_path):
    """一张表的对账。返回 (是否失败, 打印的行)。

    `committed_path` 单独传进来：反证链要拿**临时目录里的副本**跑（改完不必再"复原并验证复原"，
    那类自复原链一旦中途中断就会把突变留在产品文件里——工程里栽过）。
    """
    lines, failed = [], False

    loaded = load_table(committed_path)
    if loaded is None:
        return True, ["FAIL " + name + "：盘上那份不在 ⇒ 从没导出过：" + committed_path]

    committed_doc, committed_problems = loaded
    fresh_doc, export_error = export_fresh(spec, cli, tmp_dir)
    if export_error:
        return True, ["FAIL " + name + "：这次真导出没跑成 ⇒ 对账无从做起：" + export_error]

    committed_keys = keys_of(committed_doc, spec)
    fresh_keys = keys_of(fresh_doc, spec)
    fresh_problems = fresh_doc.get("problems") or []

    if len(fresh_keys) == 0:
        # 零条时「两边键集合相等」是空话（0==0 谁都对），所以这一档只比 problems 并明说降级。
        lines.append("  " + name + "：按 " + given_db + " 导出 0 条 ⇒ " + spec["label"] +
                     "今天没有内容，键集合对账**降级**（只比 problems）")
    else:
        missing = sorted(committed_keys - fresh_keys)   # 盘上的有、新导出的没有
        added = sorted(fresh_keys - committed_keys)     # 新导出的有、盘上的还是旧的
        if missing or added:
            failed = True
            lines.append("FAIL " + name + "：盘上那份与源不同步（盘上 " + str(len(committed_keys)) +
                         " 条，按 " + given_db + " 今天的源该有 " + str(len(fresh_keys)) + " 条）")
            for key in added[:12]:
                lines.append("  · 源里新增而表里没有：" + " / ".join(key) +
                             " ⇒ 游戏里不会用这条（重跑 " + spec["verb"] + "）")
            for key in missing[:12]:
                lines.append("  · 表里还留着而源已删：" + " / ".join(key) +
                             " ⇒ 游戏里念/加载的是已经不存在的这条")
            if len(added) + len(missing) > 24:
                lines.append("  · …另有 " + str(len(added) + len(missing) - 24) + " 条未列出")
            for hit in probe_other_dbs(spec, cli, tmp_dir, committed_keys, committed_problems, given_db):
                lines.append("  · 拿去别的库对：" + hit)
        else:
            lines.append("  " + name + "：键集合逐条对上（" + str(len(fresh_keys)) + " 条，库 " + given_db + "）")

    # problems 也是产物的一部分：一条「路径在内容树里查不到」从有到无/从无到有都是内容变化。
    if sorted(committed_problems) != sorted(fresh_problems):
        failed = True
        lines.append("FAIL " + name + "：表里记的 problems 与这次导出不一致（盘上 " +
                     str(len(committed_problems)) + " 条 vs 新 " + str(len(fresh_problems)) + " 条）")
        for text in sorted(set(fresh_problems) - set(committed_problems))[:6]:
            lines.append("  · 新出现的：" + text)
        for text in sorted(set(committed_problems) - set(fresh_problems))[:6]:
            lines.append("  · 已经消失的：" + text)

    note = warn_if_source_untracked(name, spec)
    if note:
        lines.append(note)
    return failed, lines


def db_label(explicit, env_value):
    if explicit:
        return explicit
    if env_value and "Database=" in env_value:
        return env_value.split("Database=")[1].split(";")[0]
    return "<环境里的连接串>"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=None,
                    help="显式数据库名（两张表都要从库里投影 modelKey；不给就用环境里的连接串）")
    ap.add_argument("--cli", default=CLI, help="Horizon.MonsterCli.exe 的路径")
    ap.add_argument("--only", default="both", choices=["both", "texts", "visuals"])
    ap.add_argument("--data-dir", default=DATA_DIR,
                    help="两份客户端表所在目录（默认 HundunWorld/Data/Monsters；反证链指临时副本用）")
    args = ap.parse_args()

    if args.db:
        os.environ["DB_GAME_CONNECTION_STRING"] = conn_string(args.db)
    env_value = os.environ.get("DB_GAME_CONNECTION_STRING", "")
    if not env_value:
        print("FAIL: 既没有 --db 也没有 DB_GAME_CONNECTION_STRING ⇒ 不知道该对账哪个库（不猜）")
        return 2
    if not os.path.exists(args.cli):
        print("FAIL: 找不到 CLI：" + args.cli + " ⇒ 先编 Horizon.MonsterCli（没编成就等于没跑）")
        return 2

    # 装配陈旧检查：这条判据的「参照」是 CLI 自己产出的那份键集合。
    # 有人改了 MonsterTextsExport / MonsterVisualsExport 的键名或判据而没重编，
    # 拿旧二进制生成的参照去对新表 ⇒ 差异会被算到我头上（工程里栽过：门的引用目录陈旧）。
    newest_src = max(
        os.path.getmtime(os.path.join(REPO, "Horizon.MonsterCli", f))
        for f in ("MonsterTextsExport.cs", "MonsterVisualsExport.cs", "Program.cs"))
    newest_bin = max(
        os.path.getmtime(os.path.join(os.path.dirname(args.cli), f))
        for f in ("Horizon.MonsterCli.dll", os.path.basename(args.cli)))
    if newest_src > newest_bin:
        print("FAIL: Horizon.MonsterCli 比这三份源文件旧 ⇒ 这次的对账参照来自上一次装配。"
              "先 dotnet build Horizon.MonsterCli 再跑。")
        return 1

    tmp_dir = os.path.join(os.environ.get("TEMP", "."), "hzn-client-table-check")
    os.makedirs(tmp_dir, exist_ok=True)
    given = db_label(args.db, env_value)
    print("库：" + given)

    names = ["texts", "visuals"] if args.only == "both" else [args.only]
    failed = False
    for name in names:
        committed_path = os.path.join(args.data_dir, os.path.basename(TABLES[name]["committed"]))
        bad, lines = check(name, TABLES[name], args.cli, tmp_dir, given, committed_path)
        failed = failed or bad
        for line in lines:
            print(line)

    if failed:
        print("FAIL：客户端表与它们的源不同步 ⇒ 先定「游戏该读哪个库」，再跑 "
              "MonsterCli export-texts / export-visuals（游戏读的就是 HundunWorld/Data/Monsters 那两份 JSON）")
        return 1
    print("PASS：" + "、".join(names) + " 客户端表与今天的源逐条对上")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""怪物编辑器全套闸门的**一条命令回执**（给人接手时用，不替代任何单条闸门）。

为什么要有它：这套检查散在 4 个 `scripts/verify-monster-*.py` 里，每次交接都要人肉敲 4 条命令、
再自己记住"严格档今天应该是红的，红在哪一句才是对的"。这里只做三件事：
  1. 按固定顺序把 4 条必过闸门跑一遍，逐条给 PASS/FAIL 与一行末句；
  2. 再跑一遍**严格档内容审计**——它今天预期是红的（内容侧还有 backlog），所以只报不判；
  3. 全部必过项通过才退出 0，否则退出 1。

判据不住在这里：每条闸门自己的判据仍住在各自脚本里（这里一句都没有重写），
本脚本只做"跑了没有 / 过没过"的汇总，避免第二个真相源。

用法：
  python scripts/verify-monster-gates.py                 # 用环境里的 DB_GAME_CONNECTION_STRING
  python scripts/verify-monster-gates.py --db Game
"""
import argparse
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PY = sys.executable or "python"


def decode(raw):
    """逐行解码：有的闸门把 stdout 钉成 UTF-8，有的直接吃 Windows 控制台默认的 GBK。
    整段按一个编码解会把其中一种解成乱码（回执读不懂等于没有回执）。"""
    out = []
    for line in raw.split(b"\n"):
        try:
            out.append(line.decode("utf-8"))
        except UnicodeDecodeError:
            out.append(line.decode("gbk", "replace"))
    return "\n".join(out)


def run(script, extra):
    p = subprocess.run([PY, os.path.join(REPO, script)] + extra, cwd=REPO, capture_output=True)
    text = decode(p.stdout + p.stderr)
    lines = [ln.strip() for ln in text.splitlines() if ln.strip()]
    return p.returncode, lines


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=None, help="显式数据库名，会转给支持 --db 的两条闸门")
    args = ap.parse_args()
    db = ["--db", args.db] if args.db else []

    required = [
        ("内容审计（默认档）", "scripts/verify-monster-content-audit.py", db),
        ("判据/文档引用", "scripts/verify-monster-doc-refs.py", []),
        ("面板 122 列", "scripts/verify-monster-panel-columns.py", []),
        ("客户端两表", "scripts/verify-monster-client-tables.py", db),
    ]

    failed = []
    print("=" * 72)
    print("怪物编辑器闸门回执" + (f"（库 {args.db}）" if args.db else ""))
    print("=" * 72)
    for name, script, extra in required:
        code, lines = run(script, extra)
        last = lines[-1] if lines else "<无输出>"
        # 面板闸门没有一行总结，它的成功信号是逐条"自检通过"——把条数念出来，
        # 否则那行会被读成"最后一条自检叫什么名字"而不是"几条都过了"。
        if name.startswith("面板"):
            detail = f"{sum(1 for ln in lines if ln.startswith('自检通过'))} 条自检全过"
        else:
            detail = last
        print(f"[{'PASS' if code == 0 else 'FAIL'}] {name}")
        print(f"       {detail}")
        if code != 0:
            failed.append(name)

    # 严格档：预期今天仍红（内容 backlog），只报不判，避免把"还没做完"伪装成"全套绿"。
    code, lines = run("scripts/verify-monster-content-audit.py", db + ["--require-content"])
    print(f"[{'PASS' if code == 0 else 'RED '}] 内容审计（严格档，只报不判）")
    print(f"       {lines[-1] if lines else '<无输出>'}")

    print("-" * 72)
    if failed:
        print(f"结论：{len(failed)} 条必过闸门失败 —— " + "、".join(failed))
        return 1
    print(f"结论：{len(required)} 条必过闸门全部通过（严格档读数见上，红不判失败）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

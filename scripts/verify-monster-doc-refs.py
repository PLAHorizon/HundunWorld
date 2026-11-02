#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""§12 判据列里的每个名字都必须在盘上找得到（防止文档点名一个不存在的用例/脚本）。

为什么要有这一条：§12 的判据栏是完成审计的底稿，读的人会拿它当"这些都能复跑"。
一旦某个名字只是**简述**（比如把长用例名缩成四个字），它在代码里找不到，
"复跑"就成了空话；而文档不会因此变红——只有这一层专门去对才看得见。

用法：python scripts/verify-monster-doc-refs.py [--doc 路径] [--root 仓库根]
退出码：0 = 全部对得上；1 = 有点名却找不到的；2 = 文档结构变了（找不到 §12 表或判据列）
"""
import argparse
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# 必须是 raw 串：普通串里的 "\bin" 是**退格符** + "in"，永远匹配不到目录名——
# 那样这一层会去扫 bin/obj 里生成出来的代码，既慢又可能"找到"一个只在产物里出现的名字。
SKIP_DIRS = (r"\bin", r"\obj", r"\.git", "node_modules", "AssetsCache", r"Cache\Intermediate")
SCAN_EXT = (".cs", ".py", ".sh")


def load_corpus(root):
    parts = []
    for dirpath, _, files in os.walk(root):
        if any(s in dirpath for s in SKIP_DIRS):
            continue
        for f in files:
            if f.endswith(SCAN_EXT):
                try:
                    parts.append(open(os.path.join(dirpath, f), encoding="utf-8", errors="ignore").read())
                except OSError:
                    pass
    return "\n".join(parts)


def doc_refs(doc_text):
    try:
        sec = doc_text[doc_text.index("## 12. 目标对照"):doc_text.index("### 12.1 ")]
    except ValueError:
        return None
    names = []
    rows = 0
    for line in sec.splitlines():
        if not line.startswith("| ") or "目标原文" in line:
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 5:
            continue
        rows += 1
        # 第 4 列（下标 3）= 判据（可复跑）；只有含中文的反引号串才是"点名的判据"。
        for m in re.findall(r"`([^`]+)`", cells[3]):
            if re.search(r"[\u4e00-\u9fff]", m) and "…" not in m:
                names.append(m)
    return rows, names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--doc", default=os.path.join(REPO, "docs", "MONSTER_EDITOR_PLUGIN_DESIGN.md"))
    ap.add_argument("--root", default=REPO)
    args = ap.parse_args()

    if not os.path.exists(args.doc):
        print(f"FAIL: 找不到设计文档 {args.doc}")
        return 2
    parsed = doc_refs(open(args.doc, encoding="utf-8").read())
    if not parsed:
        print("FAIL: 文档里找不到 §12 表或 §12.1（结构变了 ⇒ 这一层对不上任何东西）")
        return 2
    rows, names = parsed
    uniq = list(dict.fromkeys(names))
    if not uniq:
        print("FAIL: 判据列一个反引号名字都没提取到（整列被改了写法？）")
        return 2

    corpus = load_corpus(args.root)
    missing = [n for n in uniq if n not in corpus]
    print(f"§12 表 {rows} 行；判据列引用 {len(uniq)} 个名字；盘上找不到 {len(missing)} 个")
    for m in missing:
        print("  ! 文档点名但盘上没有：" + m)
    if missing:
        print("FAIL：这些名字是简述或已改名 ⇒ 拿它去复跑只会扑空（改成真实用例/脚本名）")
        return 1
    print("PASS：判据列点名的每一个名字都能在盘上找到（用例名或脚本名）")
    return 0


if __name__ == "__main__":
    sys.exit(main())

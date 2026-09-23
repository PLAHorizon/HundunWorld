#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""示例数据「值级」审计：区分「值已存在（仅字面形式不同）」与「值确实缺失」。

设计稿示例数值常被实现侧以插值/拼接生成（"Lv." + 20、"2400/3200"、"12,450"→12450），
文案字面比对会误报为缺口。本脚本把数值归一化为纯数字串后在实现文件中查找。

输出: .compile-temp/data-value-gap.txt
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
UI = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")
OUT = os.path.join(ROOT, ".compile-temp", "data-value-gap.txt")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_gap import MAP  # noqa


def digits(s):
    return re.sub(r"\D", "", s)


def main():
    gap_txt = io.open(os.path.join(ROOT, ".compile-temp", "design-gap.txt"), encoding="utf-8").read()
    blocks = re.split(r"\n(?=\S+\s+cov=)", gap_txt)
    out_rows = []
    for b in blocks:
        m = re.match(r"^(\S+)\s+cov=", b)
        if not m:
            continue
        page = m.group(1)
        dm = re.search(r"示例数据\(\d+\): (.*)", b)
        if not dm or not dm.group(1).strip():
            continue
        items = [x.strip() for x in dm.group(1).split(" | ") if x.strip()]

        src = ""
        for rel in MAP[page]:
            p = os.path.join(UI, rel)
            if os.path.exists(p):
                src += io.open(p, encoding="utf-8", errors="replace").read()
        # 归一化实现侧：去逗号/空格/引号，便于 "12,450" 与 12450 互相匹配
        norm_src = re.sub(r"[\s,\"']", "", src)
        norm_src_nocomma = re.sub(r"[^\d]", "", src)

        missing, form_only = [], []
        for it in items:
            d = digits(it)
            if not d:
                continue
            if d in norm_src or d in norm_src_nocomma:
                form_only.append(it)
            else:
                missing.append(it)
        out_rows.append((len(missing), page, missing, form_only))

    out_rows.sort(reverse=True)
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("值级审计：值缺失 = 设计稿数值在实现文件中完全找不到；形式差异 = 值已存在，仅字面拼接形式不同\n")
        for n, page, missing, form_only in out_rows:
            fh.write("\n%-28s 值缺失 %2d / 形式差异 %2d\n" % (page, n, len(form_only)))
            if missing:
                fh.write("    值缺失: %s\n" % " | ".join(missing))
            if form_only:
                fh.write("    形式差异: %s\n" % " | ".join(form_only))
    print("值班缺失页面数:", sum(1 for r in out_rows if r[0]))
    for n, page, missing, form_only in out_rows:
        if n:
            print("  %-28s 值缺失 %2d: %s" % (page, n, " | ".join(missing[:12])))
    print("wrote", OUT)


if __name__ == "__main__":
    main()

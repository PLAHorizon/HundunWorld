#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""示例数据「就地」审计：把设计稿里的数值与它周围的标签上下文一起打出，
再列出实现文件里现存的数值/字面量，方便逐个判断「是否已落为组件初值」。

用法:
    python .compile-temp/data_init_scan.py menu-battle-pass menu-livelihood ...
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DESIGN = os.path.join(ROOT, "hundun-yy-ui", "pages")
UI = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_extract import parse  # noqa
from design_gap import MAP, is_data_like  # noqa


def design_rows(page):
    html = io.open(os.path.join(DESIGN, page + ".html"), encoding="utf-8", errors="replace").read()
    body = re.sub(r"(?is)<(script|style|head)\b.*?</\1>", " ", html)
    body = re.sub(r"(?is)<!--.*?-->", " ", body)
    # 按标签切分，保留文本片段顺序
    parts = re.split(r"(?s)(<[^>]+>)", body)
    texts = []
    for p in parts:
        if p.startswith("<"):
            continue
        t = re.sub(r"\s+", " ", p).strip()
        if t:
            texts.append(t)
    out = []
    for i, t in enumerate(texts):
        if not re.search(r"\d", t):
            continue
        ctx = " / ".join(x for x in texts[max(0, i - 2):i] if len(x) < 30)
        out.append((ctx, t))
    return out


def impl_literals(page):
    lits, nums = [], []
    for rel in MAP.get(page, []):
        p = os.path.join(UI, rel)
        if not os.path.exists(p):
            continue
        src = io.open(p, encoding="utf-8", errors="replace").read()
        for m in re.finditer(r'([A-Za-z_][\w.]*)\s*=\s*"((?:[^"\\]|\\.)*)"', src):
            if re.search(r"\d", m.group(2)):
                lits.append("%s = \"%s\"" % (m.group(1).split(".")[-1], m.group(2)))
        for m in re.finditer(r"=\s*(-?\d+(?:\.\d+)?)\s*([;,f\)])", src):
            nums.append(m.group(1))
    return lits, nums


def main():
    pages = sys.argv[1:]
    for page in pages:
        print("=" * 78)
        print("PAGE", page, "->", ", ".join(MAP.get(page, [])))
        print("-- 设计稿数值（上下文 | 值） --")
        for ctx, t in design_rows(page):
            if is_data_like(t) or re.match(r"^[\d\s,./%+:×xX\-—→]+$", t):
                print("   %-46s | %s" % (ctx[:46], t[:60]))
        lits, nums = impl_literals(page)
        uniq = []
        for n in nums:
            if n not in uniq:
                uniq.append(n)
        print("-- 实现侧：含数字的字符串字面量 --")
        for l in lits:
            print("   ", l[:110])
        print("-- 实现侧：出现过的数字字面量 --")
        print("   ", " ".join(uniq[:120]))


if __name__ == "__main__":
    main()

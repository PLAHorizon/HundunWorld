#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""严格版「示例数据缺口」审计。

只考察设计稿里的「多位数」示例值（金额/属性/人数/进度/倒计时等），
因为 1~2 位数字多数由运行时数据插值生成，字面比对必失真。

判定：
  exact   — 设计稿token（如 "12,450" 或 "ISO 400"）在实现文件中出现；
  digits  — 去掉非数字后的串在实现文件里出现（形式差异，值已落地）；
  MISSING — 两者都不成立，才是真缺口。
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DESIGN = os.path.join(ROOT, "hundun-yy-ui", "pages")
UI = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_gap import MAP  # noqa

TOKEN = re.compile(r"\d[\d,]*(?:\.\d+)?\s*(?:%|mm|ms|m|s|秒|分|时|天|人|件|级|点|GB|fps|°)?")


def pages_text(page):
    html = io.open(os.path.join(DESIGN, page + ".html"), encoding="utf-8", errors="replace").read()
    body = re.sub(r"(?is)<(script|style|head)\b.*?</\1>", " ", html)
    parts = re.split(r"(?s)(<[^>]+>)", body)
    out = []
    for p in parts:
        if p.startswith("<"):
            continue
        t = re.sub(r"\s+", " ", p).strip()
        if t:
            out.append(t)
    return out


def impl_src(page):
    s = ""
    for rel in MAP.get(page, []):
        p = os.path.join(UI, rel)
        if os.path.exists(p):
            s += io.open(p, encoding="utf-8", errors="replace").read()
    return s


def main():
    rows = []
    for page in sorted(MAP):
        texts = pages_text(page)
        src = impl_src(page)
        src_digits = re.sub(r"\D", "", src)
        src_norm = re.sub(r"[ \u00a0'']", "", src)
        toks = []
        for t in texts:
            for m in TOKEN.finditer(t):
                v = m.group(0).strip()
                if v not in toks:
                    toks.append(v)
        missing = []
        for v in toks:
            d = re.sub(r"\D", "", v)
            if len(d) < 3:
                continue
            if v in src_norm:
                continue
            if d in src_digits:
                continue
            missing.append(v)
        if missing:
            rows.append((len(missing), page, missing))
    rows.sort(reverse=True)
    lines = []
    for n, page, miss in rows:
        lines.append("%-28s 真缺口 %2d: %s" % (page, n, " | ".join(miss)))
    txt = "\n".join(lines) or "（无）"
    io.open(os.path.join(ROOT, ".compile-temp", "data-gap-precise.txt"), "w", encoding="utf-8", newline="\n").write(txt + "\n")
    print(txt)


if __name__ == "__main__":
    main()

#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""设计稿交互锚点（data-dom-id / id）在 C# UI 树中的落地覆盖报告。

输出: .compile-temp/domid-gap.txt
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DESIGN = os.path.join(ROOT, "hundun-yy-ui", "pages")
BASE = os.path.join(ROOT, "HundunWorld", "Source")
OUT = os.path.join(ROOT, ".compile-temp", "domid-gap.txt")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_extract import parse  # noqa

# 纯装饰/容器类 id 不算交互锚点
DECO = re.compile(r"(overlay|backdrop|scrim|glow|bg-|background|::|^page$)", re.I)


def main():
    src = {}
    for dp, _, fs in os.walk(BASE):
        for f in fs:
            if f.endswith(".cs"):
                p = os.path.join(dp, f)
                src[os.path.relpath(p, BASE).replace(os.sep, "/")] = io.open(
                    p, encoding="utf-8", errors="replace").read()

    rows = []
    for f in sorted(os.listdir(DESIGN)):
        if not f.endswith(".html"):
            continue
        page = f[:-5]
        p = parse(os.path.join(DESIGN, f))
        ids = sorted({d for d, _ in p.domids if not DECO.search(d)})
        if not ids:
            continue
        found, missing = [], []
        for i in ids:
            hit = [k for k, v in src.items() if '"%s"' % i in v or "'%s'" % i in v]
            (found if hit else missing).append((i, hit[0] if hit else None))
        rows.append((page, len(ids), len(found), missing))

    rows.sort(key=lambda r: (-len(r[3]), r[0]))
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("设计页 | 交互锚点 | 已落地 | 缺失锚点\n")
        for page, n, ok, missing in rows:
            fh.write("\n%-28s %3d 个锚点, 落地 %3d, 缺失 %3d\n" % (page, n, ok, len(missing)))
            if missing:
                fh.write("    缺: %s\n" % " | ".join(m[0] for m in missing))
    total = sum(r[1] for r in rows)
    got = sum(r[2] for r in rows)
    print("pages=%d anchors=%d landed=%d (%.1f%%)" % (len(rows), total, got, 100.0 * got / total))
    print("wrote", OUT)


if __name__ == "__main__":
    main()

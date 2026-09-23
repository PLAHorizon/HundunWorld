#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""从 hundun-yy-ui 设计 HTML 提取「结构大纲 + 文案清单」，供与 C# Ink 页面比对。

用法:
    python .compile-temp/design_extract.py                # 写 .compile-temp/design/<page>.md
    python .compile-temp/design_extract.py --copy         # 只打印每页去重文案（一行一页）
    python .compile-temp/design_extract.py --outline NAME # 打印单页大纲
"""
import io
import os
import re
import sys
from html.parser import HTMLParser

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
DESIGN = os.path.join(ROOT, "hundun-yy-ui", "pages")
OUTDIR = os.path.join(ROOT, ".compile-temp", "design")

VOID = {"br", "hr", "img", "input", "meta", "link", "source", "path", "circle", "line", "rect", "use", "svg"}
SKIP = {"script", "style", "head", "title", "meta", "link"}
# 结构上有意义的标签
STRUCT = {"div", "section", "header", "footer", "nav", "aside", "main", "article", "ul", "ol", "li",
          "button", "a", "h1", "h2", "h3", "h4", "h5", "h6", "span", "p", "label", "input", "table",
          "tr", "td", "th", "canvas", "select", "option", "textarea"}
MARKER_CLASS = re.compile(
    r"(panel|card|title|btn|button|badge|tag|tab|bar|list|grid|row|col|item|header|footer|sidebar|nav|"
    r"hud|popup|modal|dialog|toast|slot|icon|ring|avatar|portrait|progress|rail|banner|section|group|"
    r"label|value|stat|attr|cell|chip|footer|top|bottom|left|right|center)", re.I)


class Outline(HTMLParser):
    def __init__(self):
        HTMLParser.__init__(self, convert_charrefs=True)
        self.stack = []
        self.lines = []
        self.text_buf = []
        self.buf_owner = None
        self.copy = []          # (depth, tag, text)
        self.domids = []        # (id or data-dom-id, tag, text-ish)
        self.pending_dom = []

    def _attrs(self, attrs):
        d = {}
        for k, v in attrs:
            d[k.lower()] = v if v is not None else ""
        return d

    def handle_starttag(self, tag, attrs):
        if tag in SKIP:
            self.stack.append(("!" + tag, None, len(self.lines)))
            return
        a = self._attrs(attrs)
        cls = a.get("class", "")
        keep = (tag in STRUCT) or bool(cls) or "id" in a
        self.stack.append((tag, (a, keep), len(self.lines)))
        if not keep or tag in VOID:
            return
        dom = a.get("data-dom-id") or a.get("id") or ""
        aria = a.get("aria-label", "")
        indent = "  " * min(len(self.stack), 40)
        label = tag
        if dom:
            label += " #" + dom
        if cls:
            short = " ".join(c for c in cls.split() if MARKER_CLASS.search(c)) or ""
            if short:
                label += " ." + short[:80]
        if aria:
            label += ' [aria="%s"]' % aria
        self.lines.append(indent + label)
        if dom:
            self.domids.append((dom, tag))
        self.buf_owner = len(self.lines) - 1

    def handle_endtag(self, tag):
        while self.stack:
            t, meta, lineidx = self.stack.pop()
            if t == tag or t == "!" + tag:
                break

    def handle_data(self, data):
        if not self.stack:
            return
        if self.stack[-1][0].startswith("!"):
            return
        txt = re.sub(r"\s+", " ", data).strip()
        if not txt:
            return
        if self.buf_owner is not None and self.buf_owner < len(self.lines) and not self.lines[self.buf_owner].endswith("|" + txt):
            self.lines[self.buf_owner] += " | " + txt
        depth = len(self.stack)
        if re.search(r"[\u4e00-\u9fff]", txt) or len(txt) > 3:
            self.copy.append((depth, txt))


def parse(path):
    src = io.open(path, encoding="utf-8", errors="replace").read()
    # 只取 body 内容，跳过 head 里的大段 CSS
    m = re.search(r"<body[^>]*>(.*)</body>", src, re.S | re.I)
    body = m.group(1) if m else src
    p = Outline()
    p.feed(body)
    return p


def main():
    args = sys.argv[1:]
    files = sorted(f for f in os.listdir(DESIGN) if f.endswith(".html"))
    if "--outline" in args:
        name = args[args.index("--outline") + 1]
        p = parse(os.path.join(DESIGN, name))
        for l in p.lines:
            print(l)
        return
    if "--copy" in args:
        for f in files:
            p = parse(os.path.join(DESIGN, f))
            seen, out = set(), []
            for _, t in p.copy:
                if t not in seen:
                    seen.add(t)
                    out.append(t)
            print("## %s :: %s" % (f[:-5], " | ".join(out)))
        return
    os.makedirs(OUTDIR, exist_ok=True)
    for f in files:
        p = parse(os.path.join(DESIGN, f))
        page = f[:-5]
        with io.open(os.path.join(OUTDIR, page + ".outline.txt"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("## page: %s\n" % page)
            fh.write("## dom-ids: %s\n" % ", ".join(sorted({d for d, _ in p.domids})))
            fh.write("\n".join(p.lines))
            seen, out = set(), []
            for _, t in p.copy:
                if t not in seen:
                    seen.add(t)
                    if len(t) <= 120:
                        out.append(t)
            fh.write("\n\n## COPY (%d)\n" % len(out))
            fh.write(" | ".join(out))
            fh.write("\n")
        print("wrote", page, len(p.lines), "lines")


if __name__ == "__main__":
    main()

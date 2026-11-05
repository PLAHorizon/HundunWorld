"""重算回执键的两侧清单（生产侧=近景驱动，消费侧=planet-session-check.py）。

**什么时候要跑它**：驱动新加一条 `Say("…")`，或者判据器新加一格 `find(lines, "…")` 之后。
网关判据 `PlanetReceiptMirrorTests` 会盯住双向对账，但它只会红给你看"哪几个键不对"；
这份工具把两边的完整清单打出来，好让人决定<em>新键该被判据器读、还是登记成诊断</em>。
`KnownDiagnostics` 那份名单就是从这里量出来的，不是凭记忆写的。

用法：python Tools/planet-receipt-keys.py
提取规则与判据那边一致：只认第一条实参就是字面量的 Say（`Say("k …")` / `Say("k …" + x)` /
`Say(string.Format(Inv, "k …", …))`），`Say(e.Message)`、`Path.Combine(…, "Saved", …)` 里刮到的
引号串是内容不是键。
"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:/Works/GitHubProjects/HundunWorld"
DRIVER = "HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs"
CHECKER = "Tools/planet-session-check.py"


def strip_comments(text):
    out = []
    for line in text.split("\n"):
        s = line.strip()
        if s.startswith("*") or s.startswith("/*"):
            continue
        cut = line.find("//")
        out.append(line if cut < 0 else line[:cut])
    return "\n".join(out)


def head(literal):
    m = re.match(r"\s*([A-Za-z_][A-Za-z0-9_]*)", literal)
    return m.group(1) if m else None


def read(path):
    return strip_comments(io.open(ROOT + "/" + path, encoding="utf-8-sig", errors="replace").read())


produced = set()
for m in re.finditer(r'Say\(\s*(?:"([^"\n]*)"|string\.Format\(\s*Inv\s*,\s*"([^"\n]*)")', read(DRIVER)):
    k = head(m.group(1) if m.group(1) is not None else m.group(2))
    if k:
        produced.add(k)

consumed = {k for k in (head(m) for m in re.findall(r'find\(lines,\s*"([^"]*)"', read(CHECKER))) if k}

diagnostics = sorted(produced - consumed)
print("消费侧（判据器在找的）%d 个：%s" % (len(consumed), ", ".join(sorted(consumed))))
print()
print("生产侧共 %d 个，其中被判据器读的 %d 个、纯诊断 %d 个" % (len(produced), len(produced & consumed), len(diagnostics)))
print()
print("# 只有诊断（要判就挪到上面，不要留成没人读的键）——KnownDiagnostics 现在应该是：")
print(", ".join('"%s"' % k for k in diagnostics))
print()
print("消费侧找了但生产侧不打的键（判据器在找不存在的东西）：%s"
      % (", ".join(sorted(consumed - produced)) or "（无）"))

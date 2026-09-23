import os, re

d = r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Logs"
pat = re.compile(r"自动取景完成：|身高=|蒙皮模型加载成功|静态模型加载成功|SkinnedModel 已加载")
files = sorted([f for f in os.listdir(d) if f.endswith(".txt")],
               key=lambda f: os.path.getmtime(os.path.join(d, f)))
for f in files:
    p = os.path.join(d, f)
    try:
        t = open(p, encoding="utf-16", errors="replace").read()
    except Exception as e:
        print(f, "ERR", e)
        continue
    hits = [l.strip()[:220] for l in t.splitlines() if "自动取景完成" in l]
    if hits:
        print("=" * 22, f)
        seen = set()
        for h in hits:
            if h in seen:
                continue
            seen.add(h)
            print("   ", h)

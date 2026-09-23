import os, re

d = r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Logs"
pat = re.compile(r"LoadScene|Scene loaded|场景.*加载|LoadSceneAsync|\.scene|SceneAsset|UICharacterCameraSystem|RoleScene|GameSceneManager", re.I)
files = sorted([f for f in os.listdir(d) if f.endswith(".txt")],
               key=lambda f: os.path.getmtime(os.path.join(d, f)), reverse=True)[:1]
for f in files:
    print("=" * 22, f)
    t = open(os.path.join(d, f), encoding="utf-16", errors="replace").read()
    for l in t.splitlines():
        if pat.search(l) and ("scene" in l.lower() or "场景" in l):
            s = l.strip()[:200]
            print("  ", s)

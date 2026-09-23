import os, re, sys

d = r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Logs"
pats = re.compile(r"CharacterPreview3D|skm_uefn_mannequin|EquipmentDatabase|待机|取景|SkinnedModel|AnimationGraph|ModelStage|FaceCustomize")
files = sorted([f for f in os.listdir(d) if f.endswith('.txt')],
               key=lambda f: os.path.getmtime(os.path.join(d, f)), reverse=True)[:2]
for f in files:
    p = os.path.join(d, f)
    print("=" * 25, f)
    t = open(p, encoding='utf-16', errors='replace').read()
    lines = [l for l in t.splitlines() if pats.search(l)]
    print("matched lines:", len(lines))
    for l in lines[-70:]:
        print(l.strip()[:320])

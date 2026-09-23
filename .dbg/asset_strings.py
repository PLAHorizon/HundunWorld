import re, binascii, os

files = {
    "Animation Graph.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\Animation Graph.flax",
    "skm_uefn_mannequin.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax",
    "Idle.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\Idle.flax",
}
for name, p in files.items():
    print("=" * 30, name)
    data = open(p, "rb").read()
    strs = re.findall(rb"[\x20-\x7e]{6,}", data)
    seen = []
    for s in strs[:40]:
        t = s.decode()
        if t not in seen:
            seen.append(t)
    for t in seen[:25]:
        print("   ", t)
    # 类型名关键串
    for kw in (b"AnimationGraph", b"SkinnedModel", b"FlaxEngine.", b"Animation"):
        c = data.count(kw)
        if c:
            print(f"    [count] {kw.decode()} x{c}")

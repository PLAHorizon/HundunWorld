import struct, collections

files = {
    "损坏版 (Scale=0.0001, SkinnedModel)": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax",
    "参照版 (Scale=1.0, Model) _import_test": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\_import_test\skm_uefn_mannequin.flax",
}

for name, p in files.items():
    data = open(p, "rb").read()
    hits = collections.Counter()
    for off in range(0, len(data) - 4, 4):
        (v,) = struct.unpack_from("<f", data, off)
        if 0.5 < v < 5000 and v == v:
            hits[round(v, 4)] += 1
    top = [(v, c) for v, c in hits.most_common(400) if c >= 3]
    top.sort(key=lambda x: -x[1])
    print("=" * 30, name, f"({len(data)} bytes)")
    for v, c in top[:28]:
        print(f"   {v:>12.4f}   x{c}")

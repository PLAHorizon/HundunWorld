import struct, collections, os

files = {
    "修复后 (Scale=1.0, SkinnedModel) 22:07": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax",
    "参照 (Scale=1.0, Model) _import_test": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\_import_test\skm_uefn_mannequin.flax",
    "历史备份 (Scale=1.0, Model)": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax.backup",
}

for name, p in files.items():
    data = open(p, "rb").read()
    hits = collections.Counter()
    for off in range(0, len(data) - 4, 4):
        (v,) = struct.unpack_from("<f", data, off)
        if 1.0 < v < 1.0e7 and v == v:
            hits[round(v, 3)] += 1
    top = sorted(hits.items(), key=lambda x: -x[1])[:12]
    print("=" * 26, name, f"({os.path.getsize(p)} B)")
    for v, c in top:
        print(f"   {v:>14.3f}   x{c}")

import re

files = {
    "当前 skm_uefn_mannequin.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax",
    "备份 skm_uefn_mannequin.flax.backup": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax.backup",
    "_import_test/skm_uefn_mannequin.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\_import_test\skm_uefn_mannequin.flax",
    "Idle.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\Idle.flax",
}

def field(txt, key, brace=False):
    if brace:
        m = re.search(r'"%s":\{([^{}]*)\}' % key, txt)
        return m.group(1) if m else None
    m = re.search(r'"%s":([^,}]+)' % key, txt)
    return m.group(1) if m else None

for name, p in files.items():
    data = open(p, "rb").read()
    m = re.search(rb'\{"ImportPath".*?"SubAssetFolder":"[^"]*"\}', data, re.S)
    txt = m.group(0).decode("utf-8", "replace") if m else ""
    print("=" * 30)
    print(name)
    for k in ["ImportPath", "Type", "Scale", "ImportLODs", "ImportMaterials"]:
        print(f"   {k} = {field(txt, k)}")
    for k in ["Rotation", "Translation"]:
        print(f"   {k} = {{{field(txt, k, True)}}}")

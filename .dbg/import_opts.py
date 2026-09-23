import json, re

files = {
    "当前 skm_uefn_mannequin.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax",
    "备份 skm_uefn_mannequin.flax.backup": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax.backup",
    "_import_test/skm_uefn_mannequin.flax": r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\_import_test\skm_uefn_mannequin.flax",
}

keys = ["ImportPath", "Type", "Scale", "Rotation", "Translation", "ImportLODs", "SubAssetFolder", "ImportMaterials"]

for name, p in files.items():
    print("=" * 34)
    print(name)
    try:
        data = open(p, "rb").read()
    except Exception as e:
        print("  ERR", e)
        continue
    m = re.search(rb'\{"ImportPath".*?\}', data, re.S)
    if not m:
        m = re.search(rb'\{[^{}]*"ImportPath".*?\}', data, re.S)
    if not m:
        print("  未找到导入参数 JSON")
        continue
    txt = m.group(0).decode("utf-8", "replace")
    try:
        cfg = json.loads(txt)
    except Exception as e:
        print("  JSON 解析失败:", e, txt[:200])
        continue
    for k in keys:
        print(f"   {k} = {cfg.get(k)}")
    print(f"   (全部键数 {len(cfg)})")

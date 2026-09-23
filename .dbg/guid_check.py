import os, binascii, struct

base = r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models"

def guid_variants(hexstr):
    b = binascii.unhexlify(hexstr)
    # FlaxGuid 常见布局：DWORD-WORD-WORD-8 BYTES，内部小端存储
    v = list(b)
    v[0:4] = v[0:4][::-1]
    v[4:6] = v[4:6][::-1]
    v[6:8] = v[6:8][::-1]
    return {hexstr: b, hexstr + "(le-mixed)": bytes(v)}

targets = {
    "skm 模型 GUID (EquipmentDatabase)": "bb21309c62654de9a63cdf826ed3116e",
    "CharacterPreview3D AnimationGraph GUID": "ceded67f4bb2623f40b4dcb493b0d419",
    "_import_test 模型 GUID": "d4ee9cbd4a820ee3850162b7d960284e",
    "日志中无效 AnimationGraph GUID": "ceded67f623f4bb2b4dcb44019d4b093",
}
files = [
    os.path.join(base, "skm_uefn_mannequin.flax"),
    os.path.join(base, "Animation Graph.flax"),
    os.path.join(base, "Idle.flax"),
    os.path.join(base, "_import_test", "skm_uefn_mannequin.flax"),
]

for f in files:
    print("=" * 30)
    print(f, os.path.exists(f), os.path.getsize(f) if os.path.exists(f) else "-")
    if not os.path.exists(f):
        continue
    data = open(f, "rb").read(256)
    print("head hex:", binascii.hexlify(data[:48]).decode())
    full = open(f, "rb").read()
    for label, gh in targets.items():
        for vname, vb in guid_variants(gh).items():
            idx = full.find(vb)
            if idx >= 0:
                print(f"  命中 {label}  ->  {vname}  @offset {idx}")

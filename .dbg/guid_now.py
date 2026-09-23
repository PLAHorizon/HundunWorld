import binascii

p = r"C:\Works\GitHubProjects\HundunWorld\HundunWorld\Content\Character\Models\skm_uefn_mannequin.flax.backup"
raw = open(p, "rb").read(64)
g = raw[28:44]
v = list(g)
v[0:4] = v[0:4][::-1]
v[4:6] = v[4:6][::-1]
v[6:8] = v[6:8][::-1]
b = bytes(v)
s = binascii.hexlify(b).decode()
print("header bytes :", binascii.hexlify(g).decode())
print("Guid(N)      :", s)
print("Guid(D)      :", f"{s[0:8]}-{s[8:12]}-{s[12:16]}-{s[16:20]}-{s[20:32]}")

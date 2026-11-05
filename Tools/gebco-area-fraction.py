import numpy as np

RAW = r"C:/Works/GitHubProjects/HundunWorld/Data/GEBCO_2026/derived/gebco_2026_15s_i16le.raw"
COLS, ROWS = 86400, 43200
NODATA = -32767
STRIDE = 480                                    # 2°：90 行 × 180 列，与 C# 判据将用的格一致

mm = np.memmap(RAW, dtype='<i2', mode='r', shape=(ROWS, COLS), offset=0)
sub = np.asarray(mm[::STRIDE, ::STRIDE])
rows = np.arange(0, ROWS, STRIDE)
cols = np.arange(0, COLS, STRIDE)
# 像元中心纬度：row r 的中心在 90 - (r+0.5)*15/3600
lat = 90.0 - (rows + 0.5) * 15.0 / 3600.0
w = np.cos(np.deg2rad(lat))                     # 面积权重，逐行
valid = (sub != NODATA)
land = (sub > 0) & valid
wmat = np.broadcast_to(w[:, None], sub.shape)
den = (valid * wmat).sum()
num = (land * wmat).sum()
print("samples=%d stride=%d" % (sub.size, STRIDE))
print("area_weighted_land_fraction=%.6f" % (float(num / den)))
print("unweighted_land_fraction=%.6f" % float((sub[valid] > 0).mean()))
# 逐 15° 带看一下，确认不是某个带把总数带偏
for k in range(0, 90, 15):
    seg = slice(k // 1, (k + 15) // 1)
    pass

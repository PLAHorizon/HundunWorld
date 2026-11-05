import numpy as np, json, io

RAW = r"C:/Works/GitHubProjects/HundunWorld/Data/GEBCO_2026/derived/gebco_2026_15s_i16le.raw"
COLS, ROWS = 86400, 43200
STRIDE = 32                                     # 取 2700x1350 个样点，独立于 C# 采样器的窗口缓存通路

mm = np.memmap(RAW, dtype='<i2', mode='r', shape=(ROWS, COLS))
sub = np.asarray(mm[::STRIDE, ::STRIDE])        # 逐行取，避开一次 7.4GB 载入
nodata = -32767
valid = sub[sub != nodata]
frac_land = float((valid > 0).mean())
print("samples=%d valid=%d nodata=%d" % (sub.size, valid.size, sub.size - valid.size))
print("land_fraction=%.6f" % frac_land)
print("min=%d max=%d mean=%.3f" % (int(valid.min()), int(valid.max()), float(valid.mean())))
q = np.percentile(valid.astype(np.float64), [1, 5, 25, 50, 75, 95, 99])
print("percentiles(1,5,25,50,75,95,99)=" + ",".join("%.1f" % v for v in q))

# 再来一份"1 度格中心"的口径：与 C# 判据将要用的取点方式同构（cell-centre 最近像元）
lat_centers = np.arange(89.5, -90.0, -1.0)                 # 180 个 1° 格中心
lon_centers = np.arange(-179.5, 180.0, 1.0)                # 360 个
rows = np.clip(((90.0 - lat_centers) * 3600.0 / 15.0 - 0.5).round().astype(np.int64), 0, ROWS - 1)
cols = np.clip(((lon_centers + 180.0) * 3600.0 / 15.0 - 0.5).round().astype(np.int64), 0, COLS - 1)
grid = np.asarray(mm[np.ix_(rows, cols)])                  # 180x360，只读这些行
g = grid[grid != nodata]
print("deg1_samples=%d land_fraction=%.6f" % (g.size, float((g > 0).mean())))
print("deg1 min=%d max=%d mean=%.3f" % (int(g.min()), int(g.max()), float(g.mean())))

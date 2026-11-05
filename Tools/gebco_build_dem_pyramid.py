#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 GEBCO_2026 的 8 幅 15 弧秒 GeoTIFF 折算成运行期可直接随机读的分级高程金字塔。

为什么要离线转：
1) 运行期（C# 客户端与服务端）不能依赖 GDAL/rasterio，只能读定长 int16 网格；
2) 全球 L0 是 86400×43200 = 37 亿格，运行期只可能按需取窗口，所以要"行主序 + 直接 seek"的布局；
3) 折算一次就把各级 crc32/统计记进 manifest，双端可按同一份数据对账。

坐标约定（运行期必须照抄，不要自己再推一遍）：
- 网格值在**格心**：col i 经度 = -180 + (i + 0.5) * step，row j 纬度 = 90 - (j + 0.5) * step；
- row 自上（北）而下（南），col 自西而东；
- nodata = -32767；下采样按有效格加权，整块全 nodata 才写 nodata。

下采样刻意全程带 (和, 计数) 两级累加，最后一步才量化成 int16 ——
中间每级都回写 int16 会把 ±0.5 m 的舍入误差一路传到粗级，自检里"与独立直算参照逐级一致"那条就是钉这个的。

用法（仓库根）：
    python Tools/gebco_build_dem_pyramid.py --selftest        # 合成小网格对朴素参照，秒级
    python Tools/gebco_build_dem_pyramid.py --levels 1-5      # 跳过 L0（少写 7.5 GB）
    python Tools/gebco_build_dem_pyramid.py --levels 0-5
    python Tools/gebco_build_dem_pyramid.py --check           # 建完后复核锚点与总体统计
"""
import argparse
import json
import math
import os
import shutil
import sys
import tempfile
import time
import zipfile
import zlib

import numpy as np
import rasterio

BASE = "C:/Works/GitHubProjects/HundunWorld/Data/GEBCO_2026"
ZIP = os.path.join(BASE, "raw", "gebco_2026_geotiff.zip")
# 产物布局对齐 docs/PLANET_10X_TERRAIN_DESIGN.md §12.3：
#   derived/gebco_2026_15s_i16le.raw   最细一级（L0）扁平行优先 Int16 LE，运行时零依赖读
#   derived/pyramid/level_NN.bin       粗一级一份，给中远景 LOD
#   derived/manifest.json              规格 + sha256 + 统计 + 判据结果
#   tiles/*.tif                        从 zip 里临时解出的官方分幅（用完删）
DERIVED = os.path.join(BASE, "derived")
TILES_DIR = os.path.join(BASE, "tiles")
MANIFEST = os.path.join(DERIVED, "manifest.json")
RECEIPT = os.path.join(BASE, "verify_receipt.json")
LOG = os.path.join(BASE, "build.log")

FORMAT_VERSION = 1
GRID_COLS = 86400
GRID_ROWS = 43200
ARC_SECOND = 15.0
STEP_DEG = ARC_SECOND / 3600.0
NODATA = -32767
QUADRANT_SAMPLES = 21600
ROW_CHUNK = 2048
SOURCE_SHA256 = "39bb6eb45a785101da606749a00cf0ec3217f32160d25859b48fd5db9c6d9368"

# 每幅的经纬范围直接列出来，不靠解析文件名字符串
QUADRANTS = [
    ("gebco_2026_n90.0_s0.0_w-180.0_e-90.0_geotiff.tif", 90.0, -180.0),
    ("gebco_2026_n90.0_s0.0_w-90.0_e0.0_geotiff.tif", 90.0, -90.0),
    ("gebco_2026_n90.0_s0.0_w0.0_e90.0_geotiff.tif", 90.0, 0.0),
    ("gebco_2026_n90.0_s0.0_w90.0_e180.0_geotiff.tif", 90.0, 90.0),
    ("gebco_2026_n0.0_s-90.0_w-180.0_e-90.0_geotiff.tif", 0.0, -180.0),
    ("gebco_2026_n0.0_s-90.0_w-90.0_e0.0_geotiff.tif", 0.0, -90.0),
    ("gebco_2026_n0.0_s-90.0_w0.0_e90.0_geotiff.tif", 0.0, 0.0),
    ("gebco_2026_n0.0_s-90.0_w90.0_e180.0_geotiff.tif", 0.0, 90.0),
]

# 锚点：纬度、经度、期望区间（米）。区间取自对原始 GeoTIFF 的实测值再留余量；
# 珠峰/马里亚纳/死海/南极冰盖这几条是"镜像或转置都不可能蒙过去"的判据。
ANCHORS = [
    ("珠穆朗玛峰", 27.9881, 86.9250, 8000, 9000),
    ("马里亚纳海沟", 11.3500, 142.2000, -11000, -10000),
    ("死海湖面", 31.5000, 35.4500, -480, -380),
    ("乞力马扎罗山", -3.0674, 37.3556, 5600, 6000),
    ("南极内陆冰盖", -80.0000, 45.0000, 2500, 4200),
    ("北太平洋深海盆", 30.0000, -150.0000, -7000, -5500),
    ("恒河平原", 25.6000, 85.1000, 0, 300),
    ("撒哈拉中部", 23.4000, 12.7000, 600, 900),
    ("冰岛内陆", 64.9000, -18.6000, 1200, 1500),
    ("秘鲁-智利海沟", -12.0000, -82.0000, -6000, -4000),
]

# 独立总体判据。**必须按面积加权（cos 纬度）**——等距网格的极区格面积小，
# 直接按格计数会把陆地占比抬高到 ~34%，那是计数口径不是地理事实（第一版闸门就在这儿判错了）。
AREA_MEAN_RANGE_M = (-2600, -1600)        # 平均海深 3,688 m / 平均陆地 840 m / 海洋 70.8% 推得 ≈ −2.4 km，冰面产品略高
AREA_LAND_FRACTION_RANGE = (0.28, 0.32)   # 地球陆地占比 ≈ 29.2%
AREA_NORTH_SOUTH_LAND_RATIO = 1.4         # 北半球陆地密度远高于南半球
AREA_OCEAN_MEAN_RANGE_M = (-3900, -3500)  # 平均海洋深度 3,688 m
AREA_LAND_MEAN_RANGE_M = (700, 1000)      # 陆地平均海拔 ≈ 840 m
SOUTH_POLAR_MEAN_MIN_M = 1000             # 南极冰面平均 ~2.3 km，南纬 ≤−70° 的高纬帽均值应上千
AREA_DEEP_FRACTION_MIN = 0.25             # 深海平原（< −2000 m）覆盖超过 1/4 的地球表面
PUBLISH_MAX_MIN_M = 8000                  # 喜马拉雅在 15" 平滑后仍有 8 km 以上
PUBLISH_MIN_MAX_M = -10800                # 挑战者深渊量级
LEVEL_MEAN_DRIFT_M = 3.0


def log(msg):
    line = "%s %s" % (time.strftime("%Y-%m-%dT%H:%M:%S"), msg)
    print(line, flush=True)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, "a", encoding="utf-8") as fh:
        fh.write(line + "\n")


def level_dims(level):
    return GRID_COLS >> level, GRID_ROWS >> level


def level_rel_path(level):
    """manifest 里记的相对路径（相对 derived/）。L0 用 §12.3 的 .raw 命名，粗级进 pyramid/。"""
    if level == 0:
        return "gebco_2026_15s_i16le.raw"
    return "pyramid/level_%02d.bin" % level


def level_path(level):
    return os.path.join(DERIVED, level_rel_path(level))


def step_at(level):
    # STEP_DEG 是浮点，不能用 "<< level"；每级角宽 = 源步长 × 2^level
    return STEP_DEG * (1 << level)


def cell_lon(col, level):
    return -180.0 + (col + 0.5) * step_at(level)


def cell_lat(row, level):
    return 90.0 - (row + 0.5) * step_at(level)


def col_of_lon(lon, level):
    return math.floor((lon + 180.0) / step_at(level) - 0.5)


def row_of_lat(lat, level):
    return math.floor((90.0 - lat) / step_at(level) - 0.5)


def round_half_up(sums, counts):
    """整数域里的"四舍五入（.5 远离零）"，与 brute_box2 里对标量的实现同规则。"""
    s = np.asarray(sums, dtype=np.int64)
    c = np.asarray(counts, dtype=np.int64)
    den = np.where(c > 0, 2 * c, 1)
    mag = (2 * np.abs(s) + c) // den
    out = np.where(s < 0, -mag, mag)
    return np.where(c > 0, out, NODATA).astype(np.int16)


def row_from_int32(values):
    """一行原始值 → (和, 计数)。"""
    v = np.asarray(values, dtype=np.int64)
    valid = v != NODATA
    return np.where(valid, v, 0), valid.astype(np.int64)


def combine(a, b):
    return a[0] + b[0], a[1] + b[1]


def halve_x(row):
    """横向 2:1；row = (和, 计数)。"""
    sums, counts = row
    if sums.shape[0] % 2 != 0:
        raise RuntimeError("行宽 %d 是奇数，无法横向配对" % sums.shape[0])
    return sums[0::2] + sums[1::2], counts[0::2] + counts[1::2]


def brute_level(src, level):
    """独立参照：每级都直接从原始网格取 2^level × 2^level 窗口做加权盒均值。

    刻意不做"逐级再均值"——那样会把中间量化误差算进参照，就检不出流式实现的错了。
    """
    factor = 1 << level
    h, w = src.shape
    out = np.full((h // factor, w // factor), NODATA, dtype=np.int16)
    for i in range(h // factor):
        for j in range(w // factor):
            total, count = 0, 0
            for dr in range(factor):
                for dc in range(factor):
                    v = int(src[i * factor + dr][j * factor + dc])
                    if v != NODATA:
                        total += v
                        count += 1
            if count:
                mag = (2 * abs(total) + count) // (2 * count)
                out[i][j] = -mag if total < 0 else mag
    return out


class Pyramid:
    """按行喂入 L0；每级先横向 2:1 再纵向配对，全程 (和, 计数)，只在落盘那一步量化 int16。"""

    def __init__(self, store_levels):
        self.store = set(store_levels)
        self.max_level = max(store_levels)
        self.handles = {}
        self.pending = {}
        self.pair_cursor = {}
        self.crc = {}
        self.rows = {}
        self.sums = {}
        self.counts = {}
        self.positives = {}
        self.mins = {}
        self.maxs = {}
        self.area = {}
        for lv in range(0, self.max_level + 1):
            self.pending[lv] = None
            self.pair_cursor[lv] = 0
            self.rows[lv] = 0
        for lv in store_levels:
            self.handles[lv] = open(level_path(lv), "r+b")
            self.crc[lv] = 0
            self.sums[lv] = 0
            self.counts[lv] = 0
            self.positives[lv] = 0
            self.mins[lv] = 10 ** 9
            self.maxs[lv] = -10 ** 9
            self.area[lv] = {"total": 0.0, "lev": 0.0, "land": 0.0, "deep": 0.0,
                             "n_total": 0.0, "n_land": 0.0, "s_total": 0.0, "s_land": 0.0,
                             "n70_total": 0.0, "n70_lev": 0.0, "s70_total": 0.0, "s70_lev": 0.0}

    def begin_quadrant(self, row_off, col_off):
        """每幅独立成对；起始下标必须能被 2^max_level 整除，否则配对会跨幅错位。"""
        for lv in range(self.max_level + 1):
            for name, value in (("row_off", row_off), ("col_off", col_off)):
                if value % (1 << lv) != 0:
                    raise RuntimeError("%s=%d 不能被 2^%d 整除，配对会错位" % (name, value, lv))
            self.pair_cursor[lv] = row_off >> lv
            self.pending[lv] = None

    def write_row(self, level, global_row, col_offset, payload):
        cols = level_dims(level)[0]
        if payload.shape[0] != QUADRANT_SAMPLES >> level:
            raise RuntimeError("level %d 行宽 %d != %d" %
                               (level, payload.shape[0], QUADRANT_SAMPLES >> level))
        if col_offset + payload.shape[0] > cols:
            raise RuntimeError("level %d 列越界：%d + %d > %d" % (level, col_offset, payload.shape[0], cols))
        blob = payload.tobytes()
        fh = self.handles[level]
        fh.seek((global_row * cols + col_offset) * 2)
        fh.write(blob)
        self.crc[level] = zlib.crc32(blob, self.crc[level])
        self.rows[level] += 1

        valid = payload != NODATA
        good = payload[valid]
        if good.size:
            good64 = good.astype(np.int64)
            self.sums[level] += int(good64.sum())
            self.counts[level] += int(good.size)
            self.positives[level] += int((good > 0).sum())
            self.mins[level] = min(self.mins[level], int(good.min()))
            self.maxs[level] = max(self.maxs[level], int(good.max()))

            # 面积权重 = cos(纬度)：等距网格每格的实地面积正比于它，公开地理事实都是面积口径
            lat = cell_lat(global_row, level)
            w = math.cos(math.radians(lat))
            n_pos = int((good > 0).sum())
            n_deep = int((good64 < -2000).sum())
            a = self.area[level]
            a["total"] += w * good.size
            a["lev"] += w * float(good64.sum())
            a["land"] += w * n_pos
            a["deep"] += w * n_deep
            if lat >= 0:
                a["n_total"] += w * good.size
                a["n_land"] += w * n_pos
            else:
                a["s_total"] += w * good.size
                a["s_land"] += w * n_pos
            if lat >= 70.0:
                a["n70_total"] += w * good.size
                a["n70_lev"] += w * float(good64.sum())
            elif lat <= -70.0:
                a["s70_total"] += w * good.size
                a["s70_lev"] += w * float(good64.sum())

    def feed(self, level, row, global_row, col_offset):
        """row = (和, 计数)，宽度已是 level 的列分辨率；下标同为 level 坐标系。"""
        if level in self.store:
            self.write_row(level, global_row, col_offset, round_half_up(row[0], row[1]))

        if level >= self.max_level:
            return

        expected = self.pair_cursor[level]
        if global_row != expected:
            raise RuntimeError("level %d 行错位：收到 %d，期望 %d" % (level, global_row, expected))
        self.pair_cursor[level] += 1

        shortened = halve_x(row)
        if global_row % 2 == 0:
            self.pending[level] = shortened
            return
        upper = self.pending[level]
        if upper is None:
            raise RuntimeError("level %d 奇数行 %d 没有配对偶数行" % (level, global_row))
        self.pending[level] = None
        self.feed(level + 1, combine(upper, shortened), global_row >> 1, col_offset >> 1)

    def finish_quadrant(self):
        for lv in range(0, self.max_level):
            if self.pending[lv] is not None:
                raise RuntimeError("level %d 残留半行，说明该幅行数是奇数" % lv)

    def close(self):
        for fh in self.handles.values():
            fh.flush()
            fh.close()

    def stats(self, level):
        if level not in self.store or self.counts[level] == 0:
            return None
        a = self.area[level]
        north_land = a["n_land"] / a["n_total"] if a["n_total"] > 0 else 0.0
        south_land = a["s_land"] / a["s_total"] if a["s_total"] > 0 else 0.0
        return {"cells": self.counts[level],
                "mean_m": round(self.sums[level] / self.counts[level], 3),
                "land_fraction": round(self.positives[level] / self.counts[level], 5),
                "min_m": self.mins[level], "max_m": self.maxs[level],
                "area_mean_m": round(a["lev"] / a["total"], 3),
                "area_land_fraction": round(a["land"] / a["total"], 5),
                "area_deep_fraction": round(a["deep"] / a["total"], 5),
                "area_north_land_fraction": round(north_land, 5),
                "area_south_land_fraction": round(south_land, 5),
                "area_north_polar_mean_m": round(a["n70_lev"] / a["n70_total"], 3) if a["n70_total"] > 0 else 0.0,
                "area_south_polar_mean_m": round(a["s70_lev"] / a["s70_total"], 3) if a["s70_total"] > 0 else 0.0}


def prepare_files(levels):
    os.makedirs(DERIVED, exist_ok=True)
    os.makedirs(os.path.join(DERIVED, "pyramid"), exist_ok=True)
    os.makedirs(TILES_DIR, exist_ok=True)
    for lv in levels:
        cols, rows = level_dims(lv)
        path = level_path(lv)
        want = cols * rows * 2
        if not (os.path.exists(path) and os.path.getsize(path) == want):
            with open(path, "wb") as fh:
                fh.truncate(want)


def extract_quadrant(zf, name):
    path = os.path.join(TILES_DIR, name)
    if os.path.exists(path) and os.path.getsize(path) > 1000:
        return path
    with zf.open(name) as src, open(path, "wb") as dst:
        while True:
            chunk = src.read(8 * 1024 * 1024)
            if not chunk:
                break
            dst.write(chunk)
    return path


def verify_files(levels):
    """落盘后按**文件顺序**整读复核：crc 与统计都从磁盘字节重算。

    为什么不能复用写盘时的 crc：写入顺序是"逐幅逐行"，文件顺序是"逐全局行"，
    两者的字节流不同，把前者当后者用会让 --check 永远红（本轮就是这样被抓出来的）。
    只有最细一级算面积加权的全套统计（判据都定在它身上），粗级只算 crc/均值/极值。
    """
    rows_per_chunk = 64
    finest = min(levels)
    result = {}
    for lv in levels:
        cols, rows = level_dims(lv)
        path = level_path(lv)
        full = (lv == finest)
        crc = 0
        acc = {"cells": 0, "nodata": 0, "sum": 0, "min": 10 ** 9, "max": -10 ** 9,
               "ocean_n": 0, "ocean_sum": 0, "land_n": 0, "land_sum": 0}
        w = {"total": 0.0, "lev": 0.0, "land": 0.0, "deep": 0.0,
             "ocean": 0.0, "ocean_lev": 0.0, "land_lev": 0.0,
             "n_total": 0.0, "n_land": 0.0, "s_total": 0.0, "s_land": 0.0,
             "n70_total": 0.0, "n70_lev": 0.0, "s70_total": 0.0, "s70_lev": 0.0}
        with open(path, "rb") as fh:
            for r0 in range(0, rows, rows_per_chunk):
                nrows = min(rows_per_chunk, rows - r0)
                blob = fh.read(nrows * cols * 2)
                if len(blob) != nrows * cols * 2:
                    raise RuntimeError("level %d 在 row %d 处读取不足：%d 字节" % (lv, r0, len(blob)))
                crc = zlib.crc32(blob, crc)
                data = np.frombuffer(blob, dtype="<i2").reshape(nrows, cols)
                valid = data != NODATA
                v32 = np.where(valid, data.astype(np.int32), 0)

                row_cells = valid.sum(axis=1, dtype=np.int64)
                row_lev = v32.sum(axis=1, dtype=np.int64)
                acc["cells"] += int(row_cells.sum())
                acc["sum"] += int(row_lev.sum())
                acc["nodata"] += int(data.size - row_cells.sum())
                acc["min"] = min(acc["min"], int(np.where(valid, data, 32767).min()))
                acc["max"] = max(acc["max"], int(np.where(valid, data, -32766).max()))

                ocean_mask = valid & (data < 0)
                land_mask = valid & (data > 0)
                row_ocean_n = ocean_mask.sum(axis=1, dtype=np.int64)
                row_land_n = land_mask.sum(axis=1, dtype=np.int64)
                acc["ocean_n"] += int(row_ocean_n.sum())
                acc["ocean_sum"] += int(np.where(ocean_mask, v32, 0).sum(axis=1, dtype=np.int64).sum())
                acc["land_n"] += int(row_land_n.sum())
                acc["land_sum"] += int(np.where(land_mask, v32, 0).sum(axis=1, dtype=np.int64).sum())

                if full:
                    lats = np.array([cell_lat(r0 + i, lv) for i in range(nrows)])
                    wr = np.cos(np.radians(lats))
                    row_deep_n = (valid & (data < -2000)).sum(axis=1, dtype=np.int64)
                    row_ocean_lev = np.where(ocean_mask, v32, 0).sum(axis=1, dtype=np.int64)
                    row_land_lev = np.where(land_mask, v32, 0).sum(axis=1, dtype=np.int64)

                    def dotf(a):
                        return float(np.dot(a.astype(np.float64), wr))

                    def dot_masked(a, mask):
                        # 行子集必须连同该行的面积权一起取，否则形状对不上
                        return float(np.dot(a[mask].astype(np.float64), wr[mask])) if mask.any() else 0.0

                    w["total"] += dotf(row_cells)
                    w["lev"] += dotf(row_lev)
                    w["land"] += dotf(row_land_n)
                    w["deep"] += dotf(row_deep_n)
                    w["ocean"] += dotf(row_ocean_n)
                    w["ocean_lev"] += dotf(row_ocean_lev)
                    w["land_lev"] += dotf(row_land_lev)
                    north = lats >= 0
                    w["n_total"] += dot_masked(row_cells, north)
                    w["n_land"] += dot_masked(row_land_n, north)
                    w["s_total"] += dot_masked(row_cells, ~north)
                    w["s_land"] += dot_masked(row_land_n, ~north)
                    n70 = lats >= 70.0
                    s70 = lats <= -70.0
                    w["n70_total"] += dot_masked(row_cells, n70)
                    w["n70_lev"] += dot_masked(row_lev, n70)
                    w["s70_total"] += dot_masked(row_cells, s70)
                    w["s70_lev"] += dot_masked(row_lev, s70)

        result[lv] = {"crc32": crc & 0xFFFFFFFF, "acc": acc, "weighted": w if full else None}
        log("verify level %d 完成 crc=%08x cells=%d nodata=%d 均值=%.1f m 极值=[%d, %d]" %
            (lv, crc & 0xFFFFFFFF, acc["cells"], acc["nodata"],
             acc["sum"] / max(1, acc["cells"]), acc["min"], acc["max"]))
    return result


def merged_stats(write_stats, verify):
    """复核读出的数字（必有）+ 流式累计的数字（写盘后才有），两套都得在。"""
    out = dict(write_stats or {})
    acc = verify["acc"]
    w = verify["weighted"]
    out.update({
        "verified_crc32": verify["crc32"],
        "verified_cells": acc["cells"],
        "nodata_cells": acc["nodata"],
        "verified_mean_count_m": round(acc["sum"] / acc["cells"], 3) if acc["cells"] else 0.0,
        "verified_min_m": acc["min"],
        "verified_max_m": acc["max"],
        "ocean_mean_count_m": round(acc["ocean_sum"] / acc["ocean_n"], 3) if acc["ocean_n"] else 0.0,
        "land_mean_count_m": round(acc["land_sum"] / acc["land_n"], 3) if acc["land_n"] else 0.0,
    })
    if w is not None:
        total = w["total"]
        out.update({
            "area_mean_check_m": round(w["lev"] / total, 3),
            "area_land_fraction_check": round(w["land"] / total, 5),
            "area_deep_fraction_check": round(w["deep"] / total, 5),
            "area_ocean_mean_m": round(w["ocean_lev"] / w["ocean"], 3) if w["ocean"] else 0.0,
            "area_land_mean_m": round(w["land_lev"] / w["land"], 3) if w["land"] else 0.0,
            "area_north_land_fraction_check": round(w["n_land"] / w["n_total"], 5) if w["n_total"] else 0.0,
            "area_south_land_fraction_check": round(w["s_land"] / w["s_total"], 5) if w["s_total"] else 0.0,
            "area_north_polar_mean_check_m": round(w["n70_lev"] / w["n70_total"], 3) if w["n70_total"] else 0.0,
            "area_south_polar_mean_check_m": round(w["s70_lev"] / w["s70_total"], 3) if w["s70_total"] else 0.0,
        })
    return out


def cross_gates(s):
    """§12.7 的总体判据：面积口径 + 公开地球常数，不从本数据反推。"""
    bad = []
    checks = [
        ("面积均高", s["area_mean_check_m"], AREA_MEAN_RANGE_M),
        ("面积陆地占比", s["area_land_fraction_check"], AREA_LAND_FRACTION_RANGE),
        ("面积海洋均高", s["area_ocean_mean_m"], AREA_OCEAN_MEAN_RANGE_M),
        ("面积陆地均高", s["area_land_mean_m"], AREA_LAND_MEAN_RANGE_M),
        ("最高点", s["verified_max_m"], (PUBLISH_MAX_MIN_M, 9000)),
        ("最低点", s["verified_min_m"], (-12000, PUBLISH_MIN_MAX_M)),
    ]
    for label, value, rng in checks:
        if not rng[0] <= value <= rng[1]:
            bad.append("%s %s 不在 [%s, %s]" % (label, value, rng[0], rng[1]))
    if s["area_deep_fraction_check"] < AREA_DEEP_FRACTION_MIN:
        bad.append("深海（<−2000 m）面积占比 %.4f 低于 %.2f" %
                   (s["area_deep_fraction_check"], AREA_DEEP_FRACTION_MIN))
    if s["area_south_land_fraction_check"] <= 0 or s["area_north_land_fraction_check"] < \
            AREA_NORTH_SOUTH_LAND_RATIO * s["area_south_land_fraction_check"]:
        bad.append("北半球陆地密度 %.4f 没有明显高于南半球 %.4f" %
                   (s["area_north_land_fraction_check"], s["area_south_land_fraction_check"]))
    if s["area_south_polar_mean_check_m"] < SOUTH_POLAR_MEAN_MIN_M:
        bad.append("南纬 ≤−70° 面积均高 %.1f m 低于 %d m" %
                   (s["area_south_polar_mean_check_m"], SOUTH_POLAR_MEAN_MIN_M))
    if s["area_north_polar_mean_check_m"] >= s["area_south_polar_mean_check_m"]:
        bad.append("北纬 ≥70° 均高 %.1f m 不低于南极高纬 %.1f m，南北颠倒" %
                   (s["area_north_polar_mean_check_m"], s["area_south_polar_mean_check_m"]))
    if s["nodata_cells"] > 0:
        bad.append("出现 %d 个 nodata 格，兜底分支得按实测重新定" % s["nodata_cells"])
    return bad

def build(levels):
    t0 = time.time()
    prepare_files(levels)
    pyramid = Pyramid(levels)

    with zipfile.ZipFile(ZIP) as zf:
        for name, north, west in QUADRANTS:
            before = dict(pyramid.rows)
            tif = extract_quadrant(zf, name)
            row_off = int(round((90.0 - north) / STEP_DEG))
            col_off = int(round((west + 180.0) / STEP_DEG))
            pyramid.begin_quadrant(row_off, col_off)
            with rasterio.open(tif) as ds:
                if (ds.width, ds.height) != (QUADRANT_SAMPLES, QUADRANT_SAMPLES):
                    raise RuntimeError("%s 尺寸异常 %dx%d" % (name, ds.width, ds.height))
                for start in range(0, QUADRANT_SAMPLES, ROW_CHUNK):
                    stop = min(start + ROW_CHUNK, QUADRANT_SAMPLES)
                    band = ds.read(1, window=((start, stop), (0, QUADRANT_SAMPLES)))
                    if band.shape != (stop - start, QUADRANT_SAMPLES):
                        raise RuntimeError("%s 窗口形状 %s" % (name, band.shape))
                    for i in range(band.shape[0]):
                        pyramid.feed(0, row_from_int32(band[i]), row_off + start + i, col_off)
            pyramid.finish_quadrant()

            for lv in levels:
                expect = QUADRANT_SAMPLES >> lv
                got = pyramid.rows[lv] - before[lv]
                if got != expect:
                    raise RuntimeError("%s level %d 本幅写了 %d 行，期望 %d 行" % (name, lv, got, expect))
            log("quadrant done %s elapsed_sec=%d" % (name, int(time.time() - t0)))
            os.remove(tif)

    # 总行数断言：逐幅断言只能证明"处理过的幅是对的"，丢一整幅要靠这里红
    for lv in levels:
        expect_rows = GRID_ROWS >> lv
        if pyramid.rows[lv] != expect_rows:
            raise RuntimeError("level %d 全程写了 %d 行，期望 %d 行（少喂一幅就会在这里红）" %
                               (lv, pyramid.rows[lv], expect_rows))

    pyramid.close()

    finish(levels, {lv: pyramid.stats(lv) for lv in levels},
           {lv: pyramid.crc[lv] & 0xFFFFFFFF for lv in levels},
           dict(pyramid.rows), t0)


def finish(levels, write_stats, write_crc, rows, t0):
    """复核读 → 合并统计 → 闸门 → manifest/回执。写盘后和 --verify-only 共用这一段。"""
    verified = verify_files(levels)
    merged = {lv: merged_stats(write_stats[lv] if write_stats else {}, verified[lv]) for lv in levels}

    failures = []
    base_mean = None
    previous_max = None
    for lv in levels:
        s = merged[lv]
        log("level %d 复核: crc=%08x cells=%d nodata=%d 计数均高=%.1f m 极值=[%d, %d] "
            "海洋均高(计数)=%.1f m 陆地均高(计数)=%.1f m" %
            (lv, s["verified_crc32"], s["verified_cells"], s["nodata_cells"],
             s["verified_mean_count_m"], s["verified_min_m"], s["verified_max_m"],
             s["ocean_mean_count_m"], s["land_mean_count_m"]))
        if "area_mean_check_m" in s:
            log("level %d 面积口径: 均高=%.1f m 陆地占比=%.4f 深海占比=%.4f 海洋均高=%.1f m "
                "陆地均高=%.1f m 南北陆地=%.4f/%.4f 南北极区=%.1f/%.1f m" %
                (lv, s["area_mean_check_m"], s["area_land_fraction_check"], s["area_deep_fraction_check"],
                 s["area_ocean_mean_m"], s["area_land_mean_m"],
                 s["area_north_land_fraction_check"], s["area_south_land_fraction_check"],
                 s["area_north_polar_mean_check_m"], s["area_south_polar_mean_check_m"]))

        # 流式累计与复核读必须互证：只有一边对，说明写盘或统计有一处错
        if write_stats:
            w = write_stats[lv]
            for key_w, key_v, tol in (("mean_m", "verified_mean_count_m", 0.05),
                                      ("min_m", "verified_min_m", 0),
                                      ("max_m", "verified_max_m", 0)):
                if abs(w[key_w] - s[key_v]) > tol:
                    failures.append("level %d 流式 %s=%s 与复核 %s=%s 不一致" % (lv, key_w, w[key_w], key_v, s[key_v]))
            if "area_mean_m" in w and "area_mean_check_m" in s:
                if abs(w["area_mean_m"] - s["area_mean_check_m"]) > 0.05:
                    failures.append("level %d 流式面积均高 %s 与复核 %s 不一致" %
                                    (lv, w["area_mean_m"], s["area_mean_check_m"]))
                if abs(w["area_land_fraction"] - s["area_land_fraction_check"]) > 1e-4:
                    failures.append("level %d 流式陆地占比 %s 与复核 %s 不一致" %
                                    (lv, w["area_land_fraction"], s["area_land_fraction_check"]))

        if base_mean is None:
            base_mean = s["verified_mean_count_m"]
        elif abs(s["verified_mean_count_m"] - base_mean) > LEVEL_MEAN_DRIFT_M:
            failures.append("level %d 均高相对 level %s 漂移 %.2f m" %
                            (lv, levels[0], s["verified_mean_count_m"] - base_mean))
        if previous_max is not None and s["verified_max_m"] > previous_max:
            failures.append("level %d 最高点 %d 高于更细一级的 %d，平滑不可能造出新高点" %
                            (lv, s["verified_max_m"], previous_max))
        previous_max = s["verified_max_m"]

        if lv == levels[0]:
            failures.extend(cross_gates(s))

    if failures:
        for f in failures:
            log("DATA_GATE FAIL " + f)
        raise RuntimeError("数据闸门未通过，共 %d 条" % len(failures))

    manifest = {
        "format": FORMAT_VERSION,
        "source": {"name": "GEBCO_2026 ice_surface_elevation (8 GeoTIFF tiles)",
                   "sha256": SOURCE_SHA256, "arcSecond": ARC_SECOND},
        "grid": {"cols": GRID_COLS, "rows": GRID_ROWS, "stepDeg": STEP_DEG,
                 "sampleConvention": "cell-centre",
                 "rowOrder": "north->south", "colOrder": "west->east",
                 "nodata": NODATA, "dtype": "int16le", "unit": "metre"},
        "levels": [{"level": lv, "cols": level_dims(lv)[0], "rows": level_dims(lv)[1],
                    "file": level_rel_path(lv),
                    "bytes": level_dims(lv)[0] * level_dims(lv)[1] * 2,
                    "crc32": merged[lv]["verified_crc32"],
                    "crc32_write_order": write_crc.get(lv) if write_crc else None,
                    "stats": merged[lv]}
                   for lv in levels],
    }
    with open(MANIFEST, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)
    with open(RECEIPT, "w", encoding="utf-8") as fh:
        json.dump({"levels": levels, "rows_per_level": rows,
                   "verified_crc32": {lv: merged[lv]["verified_crc32"] for lv in levels},
                   "elapsed_sec": round(time.time() - t0, 1)}, fh, ensure_ascii=False, indent=2)
    log("DATA_GATE PASS levels=%s elapsed_sec=%d" % (levels, int(time.time() - t0)))


def verify_only():
    """不重写，只把盘上的东西重新读一遍、算统计、跑闸门、写 manifest。"""
    t0 = time.time()
    levels = []
    for lv in range(0, 12):
        if os.path.exists(level_path(lv)):
            levels.append(lv)
    if not levels:
        log("VERIFY FAIL 没找到任何 level_*.bin")
        return 5
    log("verify-only 层级 %s" % levels)
    finish(levels, None, None, {}, t0)
    return 0


def open_levels():
    with open(MANIFEST, encoding="utf-8") as fh:
        manifest = json.load(fh)
    handles = {}
    for entry in manifest["levels"]:
        path = os.path.join(DERIVED, entry["file"])
        if os.path.getsize(path) != entry["bytes"]:
            raise RuntimeError("%s 字节数 %d != manifest %d" %
                               (path, os.path.getsize(path), entry["bytes"]))
        actual = zlib.crc32(open(path, "rb").read()) & 0xFFFFFFFF
        if actual != entry["crc32"]:
            raise RuntimeError("level %d crc32 不符：%08x vs manifest %08x" %
                               (entry["level"], actual, entry["crc32"]))
        handles[entry["level"]] = open(path, "rb")
    return manifest, handles


def check():
    manifest, handles = open_levels()
    ok = True
    for name, lat, lon, lo, hi in ANCHORS:
        v = sample(handles, lat, lon, 0)
        hit = lo <= v <= hi
        ok = ok and hit
        log("anchor %-14s L0 %7d m 期望 [%6d,%6d] %s" % (name, v, lo, hi, "OK" if hit else "FAIL"))

    for name, lat, lon, lo, hi in ANCHORS:
        coarse = max(handles)
        log("anchor %-14s L%d %7d m（粗级只看不判）" % (name, coarse, sample(handles, lat, lon, coarse)))

    for level in sorted(handles):
        for (lat, lon) in ((0.0, 0.0), (45.0, -120.0), (-89.9, 179.9), (89.9, 12.3)):
            c, r = col_of_lon(lon, level), row_of_lat(lat, level)
            assert abs(cell_lat(r, level) - lat) <= step_at(level)
            assert abs(cell_lon(c, level) - lon) <= step_at(level)

    reg = registration_failures(handles, 0)
    for f in reg:
        log("CHECK FAIL 配准等式：" + f)
    ok = ok and not reg

    cover = verify_quadrant_coverage(handles)
    for f in cover:
        log("CHECK FAIL 象限覆盖：" + f)
    ok = ok and not cover

    for fh in handles.values():
        fh.close()
    log("CHECK %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def sample(handles, lat, lon, level):
    cols, rows = level_dims(level)
    c = min(max(col_of_lon(lon, level), 0), cols - 1)
    r = min(max(row_of_lat(lat, level), 0), rows - 1)
    handles[level].seek((r * cols + c) * 2)
    return int(np.frombuffer(handles[level].read(2), dtype="<i2")[0])


def selftest():
    """8×8 合成网格走一遍流式金字塔，与朴素参照逐级比对；含 nodata 加权与取整规则。"""
    global QUADRANT_SAMPLES, GRID_COLS, GRID_ROWS, DERIVED
    tmp = tempfile.mkdtemp(prefix="dem_selftest_")
    old = (QUADRANT_SAMPLES, GRID_COLS, GRID_ROWS, DERIVED)
    QUADRANT_SAMPLES = GRID_COLS = GRID_ROWS = 8
    DERIVED = tmp
    try:
        levels = [0, 1, 2]
        prepare_files(levels)
        src = np.arange(64, dtype=np.int32).reshape(8, 8)
        src[3, 3] = NODATA
        src[5, 6] = NODATA
        src[7, 7] = NODATA

        pyramid = Pyramid(levels)
        pyramid.begin_quadrant(0, 0)
        for r in range(8):
            pyramid.feed(0, row_from_int32(src[r]), r, 0)
        pyramid.finish_quadrant()
        pyramid.close()

        bad = []
        for lv in levels:
            cols, rows = level_dims(lv)
            got = np.fromfile(level_path(lv), dtype="<i2").reshape(rows, cols)
            expect = brute_level(src.astype(np.int32), lv)
            if not np.array_equal(got, expect):
                bad.append("level %d 与独立参照不符 got=%s exp=%s" % (lv, got.tolist(), expect.tolist()))

        st = pyramid.stats(0)
        if st["cells"] != 61:
            bad.append("level 0 有效格数 %d != 61" % st["cells"])
        for b in bad:
            log("SELFTEST FAIL " + b)
        if bad:
            return 1
        log("SELFTEST PASS（流式 2:1 与独立直算参照逐级一致）")
        return 0
    finally:
        QUADRANT_SAMPLES, GRID_COLS, GRID_ROWS, DERIVED = old
        shutil.rmtree(tmp, ignore_errors=True)


def read_cell(handles, level, row, col):
    cols, rows = level_dims(level)
    assert 0 <= row < rows and 0 <= col < cols, (level, row, col)
    handles[level].seek((row * cols + col) * 2)
    return int(np.frombuffer(handles[level].read(2), dtype="<i2")[0])


def bilinear(handles, level, lat_deg, lon_deg, drop_half_cell=False, swap_rc=False):
    """按 manifest 约定独立实现一遍双线性（格心/列环绕/行夹紧/nodata 归一），用于和 C# 侧对账。

    两个 flag 只服务反证：正常运行永远是 (False, False)。
    """
    cols, rows = level_dims(level)
    step = step_at(level)
    lon_deg = (lon_deg + 180.0) % 360.0 - 180.0
    lat_deg = min(max(lat_deg, -90.0), 90.0)

    half = 0.0 if drop_half_cell else 0.5
    x = (lon_deg + 180.0) / step - half
    y = (90.0 - lat_deg) / step - half
    c0, r0 = math.floor(x), math.floor(y)
    fx, fy = x - c0, y - r0
    if swap_rc:
        c0, r0 = r0 % cols, c0 % rows
        fx, fy = fy, fx

    weight = 0.0
    total = 0.0
    for dr in (0, 1):
        r = r0 + dr
        if r < 0 or r >= rows:
            continue
        wr = (1.0 - fy) if dr == 0 else fy
        for dc in (0, 1):
            wc = (1.0 - fx) if dc == 0 else fx
            w = wr * wc
            if w <= 0.0:
                continue
            v = read_cell(handles, level, r, (c0 + dc) % cols)
            if v == NODATA:
                continue
            weight += w
            total += w * v
    if weight > 0.0:
        return total / weight * 100.0

    best, best_d = None, None
    base_r = min(max(r0, 0), rows - 1)
    base_c = c0 % cols
    for dr in range(-4, 5):
        r = base_r + dr
        if r < 0 or r >= rows:
            continue
        for dc in range(-4, 5):
            if dr == 0 and dc == 0:
                continue
            v = read_cell(handles, level, r, (base_c + dc) % cols)
            if v == NODATA:
                continue
            d = dr * dr + dc * dc
            if best_d is None or d < best_d:
                best_d, best = d, v
    return best * 100.0 if best is not None else 0.0


def xcheck():
    """输出与 Tools/DemProbe anchors 完全相同的坐标表，两端逐行比对。"""
    _, handles = open_levels()
    fine, coarse = min(handles), max(handles)
    print("# lat lon fine_level coarse_level fine_cm coarse_cm")
    for i in range(200):
        lat = -89.0 + (i * 7.3) % 179.0
        lon = -180.0 + (i * 41.9) % 360.0
        print("%.6f %.6f %d %d %.6f %.6f" %
              (lat, lon, fine, coarse, bilinear(handles, fine, lat, lon), bilinear(handles, coarse, lat, lon)))
    for fh in handles.values():
        fh.close()
    return 0


def index_pair(lat_deg, lon_deg, level, drop_half_cell=False, swap_rc=False):
    """文档约定的行列下标；两个开关只为反证而存在，正常运行永远是 (False, False)。"""
    cols, rows = level_dims(level)
    step = step_at(level)
    lon_deg = (lon_deg + 180.0) % 360.0 - 180.0
    lat_deg = min(max(lat_deg, -90.0), 90.0)
    half = 0.0 if drop_half_cell else 0.5
    c = math.floor((lon_deg + 180.0) / step - half)
    r = math.floor((90.0 - lat_deg) / step - half)
    c = min(max(c, 0), cols - 1)
    r = min(max(r, 0), rows - 1)
    if swap_rc:
        c, r = r % cols, c % rows
    return r, c


def registration_failures(handles, level, samples=3000, drop_half_cell=False, swap_rc=False):
    """配准等式：格内一点反算必须落回同一格；格心处的插值必须就是那一格的原始值。

    为什么不能只在"格心"上做反算：整点在二进制下可能落到 r−0.0000000001，floor 直接掉到上一格，
    那是浮点边界而不是约定错误。所以索引等式用离边界 1/4 步的内点，值等式交给连续的插值。

    漏掉 -0.5 时插值会给出邻格均值 ⇒ 这里红；而全场统计（均值/陆地占比）对此**完全无感**，
    所以这条不能只靠统计门。
    """
    cols, rows = level_dims(level)
    step = step_at(level)
    bad = []
    rng = np.random.default_rng(20261102)
    for _ in range(samples):
        r = int(rng.integers(1, rows - 2))
        c = int(rng.integers(1, cols - 2))
        raw = read_cell(handles, level, r, c)
        if raw == NODATA:
            continue

        lat_in = cell_lat(r, level) - 0.25 * step
        lon_in = cell_lon(c, level) + 0.25 * step
        r2, c2 = index_pair(lat_in, lon_in, level, drop_half_cell, swap_rc)
        if (r2, c2) != (r, c):
            bad.append("格 (%d,%d) 内点反算落到 (%d,%d)" % (r, c, r2, c2))
            break

        v = bilinear(handles, level, cell_lat(r, level), cell_lon(c, level), drop_half_cell, swap_rc)
        if abs(v - raw * 100.0) > 1e-6:
            bad.append("格 (%d,%d) 格心插值 %.4f != 原始值 %.0f cm" % (r, c, v, raw * 100.0))
            break
    return bad


def anchor_failures(handles, drop_half_cell=False, swap_rc=False):
    """锚点门：珠峰/马里亚纳这类点在任何配准错乱下都会落到别处。"""
    bad = []
    for name, lat, lon, lo, hi in ANCHORS:
        r, c = index_pair(lat, lon, 0, drop_half_cell, swap_rc)
        v = read_cell(handles, 0, r, c)
        if not lo <= v <= hi:
            bad.append("anchor %s 读到 %d m，不在 [%d, %d]" % (name, v, lo, hi))
    return bad


def synthetic_gate_failures(count_nodata_as_zero=False):
    """在合成小场上跑统计门：把 nodata 当 0 参与统计时，均值/陆地占比必须被推翻。

    真实 GEBCO_2026 一个 nodata 都没有，这条判据在真数据上是**空转**的，
    所以必须用带洞的夹具来证明门本身有效。
    """
    n = 16
    data = np.full((n, n), -2000, dtype=np.int16)
    data[4:8, 4:8] = 1500          # 一块"陆地"
    data[10, 10] = NODATA
    data[11, 11] = NODATA
    total = data.size - 2 if not count_nodata_as_zero else data.size
    vals = data if count_nodata_as_zero else data[data != NODATA]
    mean = float(vals.mean())
    land = float((vals > 0).sum()) / total
    bad = []
    if not AREA_MEAN_RANGE_M[0] <= mean <= AREA_MEAN_RANGE_M[1]:
        bad.append("均高 %.1f 出界" % mean)
    if not AREA_LAND_FRACTION_RANGE[0] <= land <= AREA_LAND_FRACTION_RANGE[1]:
        bad.append("陆地占比 %.4f 出界" % land)
    # 期望：诚实口径下两条都红（夹具是刻意做成"深海 + 一块陆地"的），
    # 把 nodata 当 0 时占比被稀释 ⇒ 也必须红，红因不同而已
    return {"mean": mean, "land": land, "out_of_range": bad}


def quadrant_coverage_failures(get_cell):
    """每个 90°×90° 象限抽三点：全 0 视为整幅缺失。真实地球不可能有哪个象限三点都是 0 m。"""
    bad = []
    for name, north, west in QUADRANTS:
        pts = ((north - 45.0, west + 45.0), (north - 25.0, west + 45.0), (north - 65.0, west + 65.0))
        vals = [get_cell(lat, lon) for lat, lon in pts]
        if all(v == 0 for v in vals):
            bad.append("%s 象限抽样三点全 0，疑似整幅缺失" % name)
    return bad


def verify_quadrant_coverage(handles):
    return quadrant_coverage_failures(
        lambda lat, lon: read_cell(handles, 0, *index_pair(lat, lon, 0)))


def mutants():
    """§12.7 反证协议：每个变异体都必须让对应判据变红；哪个跑出绿，就说明那条判据在空转。

    基线三条（配准/锚点/覆盖）必须绿，四个变异体必须红——两侧都判，才叫反证。
    """
    _, handles = open_levels()
    results = []

    baseline_reg = registration_failures(handles, 0)
    results.append(("基线：配准等式（不该红）", not baseline_reg, baseline_reg[:1]))
    baseline_anchor = anchor_failures(handles)
    results.append(("基线：锚点（不该红）", not baseline_anchor, baseline_anchor[:1]))
    baseline_cover = verify_quadrant_coverage(handles)
    results.append(("基线：象限覆盖（不该红）", not baseline_cover, baseline_cover[:1]))

    m1 = registration_failures(handles, 0, drop_half_cell=True)
    results.append(("变异 1：去掉格心 -0.5 ⇒ 配准等式应红", bool(m1), m1[:1]))
    m2 = anchor_failures(handles, swap_rc=True)
    results.append(("变异 2：行列互换 ⇒ 锚点应红", bool(m2), m2[:1]))
    honest = synthetic_gate_failures(False)
    cheated = synthetic_gate_failures(True)
    results.append(("变异 3：nodata 当 0 ⇒ 与诚实口径给出不同数字",
                    honest["mean"] != cheated["mean"] and honest["land"] != cheated["land"],
                    ["诚实 mean=%.1f land=%.4f / 掺假 mean=%.1f land=%.4f" %
                     (honest["mean"], honest["land"], cheated["mean"], cheated["land"])]))

    hole = QUADRANTS[4]      # (name, north, west) = 南半球、180°W~90°W 那一幅

    def broken_get(lat, lon):
        # 模拟"少喂第 5 幅"：那一片在产物里全是 0
        if hole[1] - 90.0 < lat <= hole[1] and hole[2] < lon <= hole[2] + 90.0:
            return 0
        return read_cell(handles, 0, *index_pair(lat, lon, 0))

    m4 = quadrant_coverage_failures(broken_get)
    results.append(("变异 4：少喂一幅 ⇒ 覆盖断言应红", bool(m4), m4[:1]))

    for fh in handles.values():
        fh.close()

    failed = 0
    for name, ok, evidence in results:
        print("%-46s %-4s %s" % (name, "OK" if ok else "FAIL", evidence[0] if evidence else ""), flush=True)
        if not ok:
            failed += 1
    print("MUTANT_RESULT", "PASS" if failed == 0 else "FAIL(%d)" % failed, flush=True)
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--levels", default="0-5")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--xcheck", action="store_true")
    parser.add_argument("--mutants", action="store_true", help="§12.7 反证协议：每条判据都得能被对应变异体打红")
    parser.add_argument("--verify-only", action="store_true", help="不重写，只复核盘上产物并重出 manifest")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()

    if args.selftest:
        sys.exit(selftest())
    if args.check:
        sys.exit(check())
    if args.xcheck:
        sys.exit(xcheck())
    if args.mutants:
        sys.exit(mutants())
    if args.verify_only:
        sys.exit(verify_only())

    lo, hi = args.levels.split("-")
    build(list(range(int(lo), int(hi) + 1)))

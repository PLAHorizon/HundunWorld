#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#18：整球截图的像素门禁——把 §9 给 P2 的"地平线/曲率"判据做成能算对错的一组数。

用法：python scripts/verify-planet-globe-silhouette.py
需要现场产物（gitignore 内，由 PlanetMeshProbe 的 virtual_globe 模式产出）：
    HundunWorld/Saved/planet_mesh/shot_empty_stage.png
    HundunWorld/Saved/planet_mesh/shot_virtual_globe_30_120_3_8_600_1.png   (relief_gain=1)
    HundunWorld/Saved/planet_mesh/shot_virtual_globe_30_120_3_8_600_0.png   (relief_gain=0)

三条判据各自有独立参考的预测值，不是"看起来是圆的"：
  A 平坦球的轮廓半径 = 448.6 px，由 asin(stage/dist) / (fov/2) * 半屏高 直接算出来；
  B 起伏球的轮廓**中心**相对平坦球位移 = |g_面内| * R_px ≈ 23.1 px，方向 (-0.947, -0.320)。
    为什么是中心而不是半径：R(dir) = R0(1 + g·dir) 一阶等价于"把球平移 R0*g"，
    平移的球轮廓还是圆、半径不变，只是圆心挪了。第一版判据在半径上找偶极，
    实测偶极幅度 0.0044（平坦）对 0.0043（起伏）——分不开，不是起伏没进几何，是量错了地方。
  C 起伏球半径变化 +1.2%（预测 +0.7%：沿视轴那分量 g_z 把球拉近了 10.4 cm），
    以及噪声底：轮廓残差 std ≈ 0.6%，这就是这条量法的分辨率上限。
"""
import os
import sys

import numpy as np
from PIL import Image

STAGE_CM = 600.0          # 探针里的 stageRadius
CAM_DIST_CM = 1400.0      # 两次跑共用同一相机（CameraDistance）
FOV_DEG = 60.0            # 引擎回执里的 camera fov
SCREEN_H_PX = 1059        # 截图实际高度
# g = 0.05*east + 0.02*south，在 lat=30/lon=120 的 L14 区域基下（离线用 Horizon.Planet 复算得到，
# 与探针回执里 globe_scaled_radius 的 (max-min)/(2*stage)=0.1077 = 2|g| 相互印证）
G = np.array([-0.0483, -0.01634, -0.01732])
G_INPLANE = float(np.hypot(G[0], G[1]))
G_DIR = np.array([G[0], G[1]]) / G_INPLANE

D = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                 "HundunWorld", "Saved", "planet_mesh")
FLAT = "shot_virtual_globe_30_120_3_8_600_0.png"
RELIEF = "shot_virtual_globe_30_120_3_8_600_1.png"
BASE = "shot_empty_stage.png"


def clean_runs(mask, min_run=20):
    out = np.zeros_like(mask)
    for y in range(mask.shape[0]):
        xs = np.nonzero(mask[y])[0]
        if xs.size == 0:
            continue
        splits = np.nonzero(np.diff(xs) > 1)[0]
        starts = np.concatenate(([0], splits + 1))
        ends = np.concatenate((splits, [xs.size - 1]))
        for s, e in zip(starts, ends):
            if e - s + 1 >= min_run:
                out[y, xs[s]:xs[e] + 1] = True
    return out


def convex_edge(mask):
    ys, xs = [], []
    for y in range(mask.shape[0]):
        row = np.nonzero(mask[y])[0]
        if row.size == 0:
            continue
        ys.append(y); xs.append(row[0])
        ys.append(y); xs.append(row[-1])
    return np.column_stack([np.array(ys), np.array(xs)])


def kasa_circle(pts):
    x = pts[:, 1].astype(float)
    y = pts[:, 0].astype(float)
    A = np.column_stack([x, y, np.ones_like(x)])
    sol, *_ = np.linalg.lstsq(A, x * x + y * y, rcond=None)
    cx, cy = sol[0] / 2.0, sol[1] / 2.0
    return cx, cy, float(np.sqrt(sol[2] + cx * cx + cy * cy))


def residual_pct(cx, cy, edge, bins=720):
    x = edge[:, 1].astype(float) - cx
    y = edge[:, 0].astype(float) - cy
    ang = np.degrees(np.arctan2(y, x)) % 360.0
    rad = np.hypot(x, y)
    idx = np.digitize(ang, np.linspace(0, 360, bins + 1)) - 1
    r = np.full(bins, np.nan)
    cnt = np.zeros(bins)
    for i in range(bins):
        sel = idx == i
        cnt[i] = int(sel.sum())
        if cnt[i]:
            r[i] = rad[sel].max()
    ok = ~np.isnan(r) & (cnt >= 2)
    th = np.radians(np.arange(bins)[ok] * 360.0 / bins)
    rr = r[ok]
    mean = float(rr.mean())
    design = np.column_stack([np.ones_like(th), np.cos(th), np.sin(th),
                              np.cos(2 * th), np.sin(2 * th)])
    coef, *_ = np.linalg.lstsq(design, rr / mean, rcond=None)
    resid = rr / mean - design @ coef
    return mean, float(resid.std()), float(np.hypot(coef[1], coef[2]))


def measure(name):
    base = np.asarray(Image.open(os.path.join(D, BASE)).convert("RGB")).astype(np.int16)
    a = np.asarray(Image.open(os.path.join(D, name)).convert("RGB")).astype(np.int16)
    mask = clean_runs(np.abs(a - base).sum(axis=2) > 24)
    edge = convex_edge(mask)
    cx, cy, rfit = kasa_circle(edge)
    mean, resid, dipole = residual_pct(cx, cy, edge)
    return cx, cy, mean, resid, dipole, int(mask.sum())


def main():
    for f in (BASE, FLAT, RELIEF):
        if not os.path.exists(os.path.join(D, f)):
            print("缺少现场产物：%s" % os.path.join(D, f))
            print("（需要先在引擎里跑 PlanetMeshProbe 的 virtual_globe 两种增益）")
            return 2
    fx, fy, fr, fres, fdip, farea = measure(FLAT)
    rx, ry, rr_, rres, rdip, rarea = measure(RELIEF)

    ang = np.degrees(np.arcsin(min(1.0, STAGE_CM / CAM_DIST_CM)))
    expect_r = ang / (FOV_DEG / 2.0) * (SCREEN_H_PX / 2.0)
    expect_shift = G_INPLANE * fr

    print("平坦球：面积 %d px，轮廓半径 %.2f px（预测 %.2f，差 %.2f%%），残差 %.2f%%，偶极 %.5f"
          % (farea, fr, expect_r, 100 * (fr - expect_r) / expect_r, 100 * fres, fdip))
    print("起伏球：面积 %d px，轮廓半径 %.2f px（比平坦 %.2f%%），残差 %.2f%%，偶极 %.5f"
          % (rarea, rr_, 100 * (rr_ - fr) / fr, 100 * rres, rdip))
    dx, dy = rx - fx, -(ry - fy)          # 图像 y 向下，屏幕 y 向上
    shift = float(np.hypot(dx, dy))
    cosang = (dx * G_DIR[0] + dy * G_DIR[1]) / max(shift, 1e-9)
    print("中心位移：实测 %.2f px（预测 %.2f，差 %.1f%%），方向余弦 %.4f（夹角 %.1f°）"
          % (shift, expect_shift, 100 * (shift - expect_shift) / expect_shift,
             cosang, np.degrees(np.arccos(max(-1.0, min(1.0, cosang))))))

    checks = [
        ("A 半径对上透视几何预测（1% 内）", abs(fr - expect_r) / expect_r < 0.01),
        ("B 起伏让轮廓中心位移（20% 内）且方向对上（±10°）",
         abs(shift - expect_shift) / expect_shift < 0.20 and cosang > 0.985),
        ("C 起伏没有把轮廓变成非圆（残差 < 1.5%）", max(fres, rres) < 0.015),
        ("D 起伏确实改了尺寸（半径变化 > 噪声底 0.6%）", abs(rr_ - fr) / fr > 0.006),
    ]
    print()
    bad = 0
    for label, ok in checks:
        print("  [%s] %s" % ("PASS" if ok else "FAIL", label))
        bad += 0 if ok else 1
    print("\n噪声底：轮廓残差 std ≈ %.2f%%（这就是这条量法的分辨率上限）" % (100 * max(fres, rres)))
    if bad:
        print("结论：不通过（%d 条）" % bad)
        return 1
    print("结论：通过——整球截图的曲率与起伏在像素侧被独立预测值钉住了")
    return 0


if __name__ == "__main__":
    sys.exit(main())

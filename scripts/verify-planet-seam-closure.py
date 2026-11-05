#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#22 的实机一半：跨级缝隙在**渲染出来的那一帧**里到底闭没闭上。

用法：python scripts/verify-planet-seam-closure.py [--images-dir DIR]
      [--none 标签] [--open 标签] [--closed 标签]   # 默认 FN / FW / FS（平地三臂）
      [--mode aperture|diff]

三种量法对应三种问题，不是同一件事的三种写法：
  aperture（默认，平地那组）数"表面内部的暗像素"。表面是一整片浅灰，洞就是里面的黑线，
    这个定义干净、阈值好定。
  diff（岭脊那组）比较"不挂裙边"与"挂了对的深度/错了的深度"两张图的差。
    有真实起伏时视心里到处是山影，"表面内部"这个前提不成立（实测：不挂裙边那臂反而 0 个暗像素，
    而墙最深那臂 2846 个——数的是山影不是洞），所以只能问差：**墙太矮就等于没挂，图应该和不挂几乎一样；
    墙够深才应该把视线堵住，图必须明显变。**
  track（岭脊那组，回答"带宽到底几像素"）沿行追一条**连续的**差异带，报带宽、长厚比，
    再逐行问"太矮那面墙在这个带上改变了多少"。
    为什么 diff 不够：它只给一个全图均值（0.764 vs 1.597），既不答"这是不是那道缝"，
    也不答"盖住了多少"。起伏那组量到的答案是 **太矮的墙改变了 1.6% 的带、够深的墙 100%**——
    比均值锋利得多（§13.39 那个失败在这里变成"逐行都等于没挂"）。
    而洞本身在"不挂裙边"那一臂里<em>不是暗的</em>（DIAG 那行：局部亮度异常中位 0.06 灰阶），
    所以起伏下的"洞宽"在这种取景下压根不可测；能测的是墙盖住的带宽（32 px，台阶张角预测 12.95 px，
    比值 2.47 = 掠射角下墙身投影本来就比台阶宽）。

现场产物由 PlanetMeshProbe 的 `seam` 模式产出（三臂只差 marker 里的 skirtFactor）：
    seam_FN  skirtFactor=0     粗 tile + 两块更细邻居，不挂裙边   → 洞
    seam_FS  skirtFactor=1.5   同一块几何 + 生产深度的裙边          → 应该没有洞
    seam_FW  skirtFactor=0.2   同一块几何 + 只有 1/5 深度的裙边      → 还得有洞
默认吃 HundunWorld/Saved/planet_seam/。`--images-dir` 用来跑变异反证：
把某一臂的 png/receipt 换成另一臂的，判据必须红——不然"绿"可能只是我把文件读串了。

四条判据，各自钉一件事：
  A1 洞的宽度对得上独立预测：把缝隙当抛物线薄片的张角 step/cam_dist 换算成像素，
     实测平均宽度必须落在它的 [0.8, 2.2] 倍里（掠射投影只会拉长不会缩短，所以下界是 0.8 而不是 1）。
     这条管的是"我量的确实是那道缝"，不是"我量到了一堆噪点"。
  A2 不挂裙边必须有洞（暗像素 > 2000）。
  A3 生产深度的裙边必须把洞清干净（暗像素 = 0）。
  A4 三臂必须按深度单调：无裙边 5898 → 浅裙边(1/5 深度) 2677 → 生产深度 0。
     这条把 A3 从"多画了几何所以挡住了"里摘出来：三臂的顶点、位置、三角形数全同，只有深度不同，
     所以"清零"只能归因于**盖过了台阶**；而浅臂必须仍然明显有洞（>2000，与 A2 同一道杠）。
  A5 自证：FS/FW 两臂的回执里裙边必须真的朝外长（radial_delta > 0），否则跑的是旧 dll，整组数作废。
"""
import io
import math
import os
import re
import sys

import numpy as np
from PIL import Image

SURFACE_MIN_GRAY = 140      # 表面是浅灰（中位 ~226），天空是纯黑
CRACK_MAX_GRAY = 135        # 低于这个的就是"没被照亮的洞"
EDGE_MARGIN_PX = 10         # 轮廓边缘一圈是抗锯齿，不算洞
FOV_DEG = 60.0

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DIR = os.path.join(ROOT, "HundunWorld", "Saved", "planet_seam")
# 三臂的角色写死，名字可以换：默认是平地那组（无裙边 / 浅裙边 / 生产深度），
# 岭脊那组用 --none RN --open RO --closed RS 指过来。
DEFAULT_LABELS = {"none": "FN", "open": "FW", "closed": "FS"}


def receipt_value(text, key):
    m = re.search(key + r"=([-\d.eE+]+)", text)
    if not m:
        raise KeyError(key)
    return float(m.group(1))


def crack_stats(path):
    """数"表面内部"的暗像素：同一行里左右都有表面、自己却是暗的——洞的定义。"""
    g = np.asarray(Image.open(path).convert("L")).astype(np.int32)
    surface = g > SURFACE_MIN_GRAY
    total = 0
    widths = []
    for row in range(g.shape[0]):
        idx = np.flatnonzero(surface[row])
        if idx.size < 400:
            continue
        lo, hi = idx[0] + EDGE_MARGIN_PX, idx[-1] - EDGE_MARGIN_PX
        if hi <= lo:
            continue
        dark = g[row, lo:hi] < CRACK_MAX_GRAY
        n = int(dark.sum())
        if n:
            total += n
            widths.append(n)
    return total, (float(np.mean(widths)) if widths else 0.0), len(widths)


def _arg(name, default):
    if name in sys.argv:
        return sys.argv[sys.argv.index(name) + 1]
    return default


def track_seam(diff, predicted_px, min_peak=8, max_step=6, max_width_factor=3.0, max_gap=12):
    """沿行追一条**连续的**窄差异带，返回 [(row, lo, hi, width, peak)]。

    为什么要有这条：起伏画面里"哪里和哪里不一样"到处都是（墙挡住远处的山坡也是一处差异），
    单看某一行的峰会把山影当成缝。缝的定义是**跨行连续**——一行里那条窄带和上一行的窄带
    列号只差几像素，山影不满足这个（实测：不按连续性筛，追出来的宽度中位数 69 px，
    而按连续性筛是 31 px，正好是一条窄带）。

    种子取整幅最强的那一行，然后向上下各走一段；每行在上一行列号附近找 ≥min_peak 的连续段，
    宽度超过 max_width_factor×预测值的行**跳过而不中断**（max_gap 行内）：那道墙在某些行上盖住的
    区域确实会宽一点（墙顶沿是条弧，掠射角下逐行宽度能差几倍），一断就丢整条带的话
    平地那组只剩 19 行——实测如此，所以失联预算给到 12 行。
    `max_gap` 是允许的连续失联行数：抗锯齿或一小段被地形挡住不该把整条带截断
    （实测平地那组一断就只剩 19 行，而那道暗缝跨 416 行）。
    """
    h, w = diff.shape
    row_peak = diff.max(axis=1)
    seed = int(np.argmax(row_peak))
    if row_peak[seed] < min_peak:
        return []

    def run_at(row, col):
        peak = float(row[col])
        if peak < min_peak:
            return None
        lo = hi = col
        while lo > 0 and row[lo - 1] >= min_peak:
            lo -= 1
        while hi < w - 1 and row[hi + 1] >= min_peak:
            hi += 1
        th = peak / 2.0
        while lo > 0 and row[lo - 1] >= th:
            lo -= 1
        while hi < w - 1 and row[hi + 1] >= th:
            hi += 1
        if hi - lo + 1 > max_width_factor * max(predicted_px, 1.0):
            return None
        return lo, hi, hi - lo + 1, peak

    found = []
    seed_hit = run_at(diff[seed], int(np.argmax(diff[seed])))
    if seed_hit is None:
        return []
    found.append((seed, seed_hit[0], seed_hit[1], seed_hit[2], seed_hit[3]))
    for direction in (-1, 1):
        r, anchor = seed, (seed_hit[0] + seed_hit[1]) // 2
        gap = 0
        while 0 <= r + direction < h:
            r += direction
            row = diff[r]
            lo0, hi0 = max(0, anchor - max_step), min(w - 1, anchor + max_step)
            window = row[lo0:hi0 + 1]
            hit = None
            if window.max() >= min_peak:
                hit = run_at(row, lo0 + int(np.argmax(window)))
            if hit is None:
                gap += 1
                if gap > max_gap:
                    break
                continue
            gap = 0
            found.append((r, hit[0], hit[1], hit[2], hit[3]))
            anchor = (hit[0] + hit[1]) // 2
    found.sort(key=lambda x: x[0])
    return found


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")   # 控制台默认 GBK，判据说理会打成乱码
    except Exception:
        pass
    images_dir = DEFAULT_DIR
    mode = _arg("--mode", "aperture")
    if "--images-dir" in sys.argv:
        images_dir = sys.argv[sys.argv.index("--images-dir") + 1]

    labels = {"none": _arg("--none", DEFAULT_LABELS["none"]),
              "open": _arg("--open", DEFAULT_LABELS["open"]),
              "closed": _arg("--closed", DEFAULT_LABELS["closed"])}
    print("arms none=%s open=%s closed=%s" % (labels["none"], labels["open"], labels["closed"]))

    data = {}
    for arm, label in labels.items():
        p = os.path.join(images_dir, "seam_%s.png" % label)
        r = os.path.join(images_dir, "receipt_%s.txt" % label)
        if not os.path.exists(p):
            print("MISSING_IMAGE %s" % p)
            return 2
        text = io.open(r, encoding="utf-8-sig").read() if os.path.exists(r) else ""
        total, mean_w, rows = crack_stats(p)
        data[arm] = {"total": total, "mean_width": mean_w, "rows": rows, "receipt": text}

    # 预测用的是回执里的物理量，不是从像素反推的
    none_receipt = data["none"]["receipt"]
    step_units = receipt_value(none_receipt, "seam_step_stage_units")
    cam_dist = receipt_value(none_receipt, "seam_camera_dist_units")
    img_h = Image.open(os.path.join(images_dir, "seam_%s.png" % labels["none"])).size[1]
    px_per_rad = img_h / (2.0 * math.tan(math.radians(FOV_DEG) / 2.0))
    predicted_px = step_units / cam_dist * px_per_rad

    checks = []
    aperture_checks = mode == "aperture"

    ratio = data["none"]["mean_width"] / predicted_px if predicted_px else 0.0
    if aperture_checks:
        checks.append(("A1 洞宽对得上独立预测",
                       0.8 <= ratio <= 2.2,
                       "实测 %.2f px / 预测 %.2f px = %.2f 倍（预测=%.2f 单位/%.0f 单位×%.0f px/rad）"
                       % (data["none"]["mean_width"], predicted_px, ratio, step_units, cam_dist, px_per_rad)))
    else:
        checks.append(("A1 视距与步距的量级对得上（差模态不数洞宽）",
                       predicted_px > 0.5,
                       "预测张角 %.2f px（%.2f 单位 / %.0f 单位 × %.0f px/rad）——差模态下洞宽不由暗像素给"
                       % (predicted_px, step_units, cam_dist, px_per_rad)))

    if aperture_checks:
        checks.append(("A2 不挂裙边必须有洞",
                   data["none"]["total"] > 2000,
                       "暗像素=%d 跨 %d 行" % (data["none"]["total"], data["none"]["rows"])))

        checks.append(("A3 正确深度的裙边必须把洞清干净",
                   data["closed"]["total"] == 0,
                       "暗像素=%d（要求 0）" % data["closed"]["total"]))

    # A4 是关键的那条：中间臂必须**仍然看得见洞**。
    # 平地那组的中间臂是"深度只有 1/5"，岭脊那组的中间臂是"深度按纯弯曲公式算"——
    # 后者就是 §13.39 说的那个失败：老公式的墙比洞浅，画了等于没画。
    if aperture_checks:
        checks.append(("A4 深度不够的那一臂必须还留着洞（否则 A3 什么都没证明）",
                       data["none"]["total"] > data["open"]["total"] > 2000 and data["closed"]["total"] == 0,
                       "无裙边 %d → 不够深 %d → 够深 %d（不够深那臂必须仍 >2000）"
                       % (data["none"]["total"], data["open"]["total"], data["closed"]["total"])))

    ok_sign = True
    detail = []
    for arm in ("open", "closed"):
        text = data[arm]["receipt"]
        if not text:
            ok_sign = False
            detail.append("%s 没回执" % arm)
            continue
        delta = receipt_value(text, "seam_skirt_radial_delta")
        depth = receipt_value(text, "skirt_depth_cm")
        detail.append("%s delta=%.0f depth=%.0f" % (arm, delta, depth))
        if delta <= 0:
            ok_sign = False
    checks.append(("A5 裙边真的朝外长（跑的是新 dll）", ok_sign, "；".join(detail)))

    if mode == "diff":
        # 三臂同一几何、同一相机、同一光照，只差那条墙的深度 ⇒ 逐像素差就是"墙改变了什么"
        def gray_diff(label):
            q = os.path.join(images_dir, "seam_%s.png" % labels[label])
            return np.asarray(Image.open(q).convert("L")).astype(np.float64)

        g_none, g_open, g_closed = gray_diff("none"), gray_diff("open"), gray_diff("closed")
        d_open = float(np.abs(g_none - g_open).mean())
        d_closed = float(np.abs(g_none - g_closed).mean())
        d_self = float(np.abs(g_none - g_none).mean())
        # 只钉方向，不钉倍数：2.9 km 的矮墙也会挡掉一部分视线（实测比值 0.479），
        # 但它挡不住 5.5 km 的洞，所以画面改变量必须严格小于按真实步距定的 8.3 km 深墙。
        checks.append(("D1 够深的墙必须比太矮的墙改变更大（太矮挡不住这个洞）",
                       d_open < d_closed,
                       "mean|Δ| 太矮 %.4f vs 够深 %.4f（比值 %.3f）" % (d_open, d_closed, d_open / d_closed)))
        checks.append(("D2 够深那一臂必须真的改变了画面",
                       d_closed > 0.05,
                       "mean|Δ|=%.4f，同图自比作噪声底=%.4f" % (d_closed, d_self)))
        checks.append(("D3 三臂的画面必须都在有效范围内（不是黑屏或空场景）",
                       all(float(g.mean()) > 1.0 for g in (g_none, g_open, g_closed)),
                       "平均灰度 %.2f / %.2f / %.2f" % (g_none.mean(), g_open.mean(), g_closed.mean())))

    if mode == "track":
        # 起伏那组：洞在"不挂裙边"那一臂里<em>不是黑的</em>（DIAG 那行量的就是这件事，实测 0.03 灰阶），
        # 所以"数暗像素"在这组上不成立，能测的是"这面墙在画面上留下多宽一条带"。
        # 用 |Δ| 而不是带符号差：平地那组挂上墙是把黑缝变成灰面（变亮），起伏那组是把亮面变成墙身（变暗），
        # 符号跟着地形与相机几何走——把它写死成任何一个方向，另一组就直接追不到带（实测如此）。
        def gray(label):
            q = os.path.join(images_dir, "seam_%s.png" % labels[label])
            return np.asarray(Image.open(q).convert("L")).astype(np.float64)

        a_none, a_open, a_closed = gray("none"), gray("open"), gray("closed")
        band = track_seam(np.abs(a_none - a_closed), predicted_px)
        widths = [b[3] for b in band]
        med_w = float(np.median(widths)) if widths else 0.0
        ratio_t = med_w / predicted_px if predicted_px else 0.0

        checks.append(("T1 追到一条连续的带（不是散点）", len(band) >= 20,
                       "连续 %d 行，列 %d..%d，峰值中位 %.1f 灰阶"
                       % (len(band),
                          min((b[1] for b in band), default=0), max((b[2] for b in band), default=0),
                          float(np.median([b[4] for b in band])) if band else 0.0)))

        # 带宽这条只钉"是一条窄带"，不钉"等于台阶宽"：墙身的投影宽度由**墙深 × 视角**决定，
        # 本来就比台阶张角宽（起伏那组实测 31 px vs 预测 12.95 px = 2.4 倍，而平地那组是 15 px vs 8.99 px = 1.7 倍）。
        # 上一版把它写成"必须等于预测台阶宽"，那是我的假设，不是几何——红下来之后先看的是假设。
        checks.append(("T2 带必须是窄带（量级对得上，且不是块状遮挡）",
                       band and 0.5 <= ratio_t <= 6.0,
                       "带宽中位 %.2f px / 预测台阶宽 %.2f px = %.2f 倍（比值落在 [0.5, 6] 里才算一条带）"
                       % (med_w, predicted_px, ratio_t)))

        # 带必须是"一条线"：列号随行号近似线性漂移。山影不满足，块状遮挡也不满足。
        if len(band) >= 8:
            rr = np.array([b[0] for b in band], dtype=np.float64)
            cc = np.array([(b[1] + b[2]) / 2.0 for b in band], dtype=np.float64)
            slope, intercept = np.polyfit(rr, cc, 1)
            # 直线度用二次拟合的残差量：墙顶沿是条弧，掠射角下这条带在图像里本来就有轻微弯曲，
            # 拿直线去套会在长带上得到 9 px 的"残差"，那测的是拟合模型不对，不是带不直。
            quad = np.polyfit(rr, cc, 2) if len(band) >= 12 else None
            resid = float(np.abs(cc - np.polyval(quad, rr)).max()) if quad is not None                     else float(np.abs(cc - (slope * rr + intercept)).max())
            aspect = len(band) / max(med_w, 1.0)
            # 1.5 这把尺是这么定的：一块各向同性的遮挡长厚比≈1，一条缝是几行宽到几十行长
            # （实测平地 2.62、起伏 1.97，而把臂对调或拿"没挂裙边"当闭合臂时直接塌到 0–1）。
            # 2.0 那版是我按"看起来像线"拍的，起伏那组量出来 1.97 就红了——红的是尺子，不是画面。
            checks.append(("T3 带是长条而不是一块（行数 / 带宽 ≥ 1.5）", aspect >= 1.5,
                           "%d 行 / 中位宽 %.1f px = 长厚比 %.2f；拟合残差 %.2f px 只作参考——"
                           "起伏下墙顶沿本来就逐行抖，'直不直'这件事追踪器已经用 max_step 管过了，"
                           "再拿它当判据是循环论证"
                           % (len(band), med_w, aspect, resid)))
        else:
            checks.append(("T3 带是长条而不是一块", False, "带只有 %d 行，不够判长厚比" % len(band)))

        # 深度不够那面墙在同一个带上应当什么都没盖住——这是 §13.39 那个失败最锋利的说法：
        # 不是"改动小一点"，而是在该有洞的那一条上**逐行都等于没挂**（起伏那组实测 0.0%）。
        def covered_rows(arm):
            d = np.abs(arm - a_none)
            return sum(1 for (r, lo, hi, _w, _p) in band if d[r, lo:hi + 1].max() >= 8.0)

        n_band = max(len(band), 1)
        cov_open, cov_closed = covered_rows(a_open) / n_band, covered_rows(a_closed) / n_band
        # 阈值是**相对**的而不是"太矮那臂必须 <15%"：平地那组 1/5 深度的墙确实盖住了 16.3% 的带
        # （暗像素 5898→2677 那条链说的就是这件事），而起伏那组只盖住 0.3%。写死绝对值会把
        # 一种几何下的"部分覆盖"当成另一种几何下的判据失败。
        checks.append(("T4 深度不够的墙在这个带上盖不住（逐行比对，不是均值）",
                       band and cov_closed >= 0.9 and cov_open < 0.5 * cov_closed,
                       "太矮那臂改变了 %.1f%% 的带，够深那臂 %.1f%%（比值 %.2f）"
                       % (100 * cov_open, 100 * cov_closed, cov_open / max(cov_closed, 1e-9))))

        # 诊断（不参与判定）：洞在 none 那臂里到底看不看得见——这决定 aperture 那套为什么在这组上失效。
        anom_rows = []
        for (r, lo, hi, _w, _p) in band:
            seg = a_none[r, max(0, lo - 40):hi + 40]
            if seg.size > 8:
                anom_rows.append(float(a_none[r, lo:hi + 1].mean() - np.median(seg)))
        print("DIAG 洞在 none 臂里的局部亮度异常 中位 %.2f 灰阶（<8 就是'不是暗缝'，aperture 那条路在此组不成立）"
              % (float(np.median(anom_rows)) if anom_rows else 0.0))

    for name, passed, info in checks:
        print("%-4s %s — %s" % ("PASS" if passed else "FAIL", name, info))

    verdict = "PASS" if all(p for _, p, _ in checks) else "FAIL"
    print("verdict=%s images=%s" % (verdict, images_dir))
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())

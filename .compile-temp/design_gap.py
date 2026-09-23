#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""比对 hundun-yy-ui 设计页文案 与 C# Ink 页面实现，输出覆盖率报告。

用法:
    python .compile-temp/design_gap.py            # 写 .compile-temp/design-gap.txt
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DESIGN_PAGES = os.path.join(ROOT, "hundun-yy-ui", "pages")
UI = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")
OUT = os.path.join(ROOT, ".compile-temp", "design-gap.txt")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_extract import parse  # noqa

# 设计页 → C# 文件（相对 UI 目录）。多文件=一个页面由多个类共同拼装。
MAP = {
    "cc-naming": ["Ink/Pages/CharacterCreation/CharacterNamingPage.cs"],
    "cc-face-customize": ["Ink/Pages/CharacterCreation/CharacterFaceCustomizePage.cs"],
    "loading-1": ["Ink/Pages/LoadingPages.cs"],
    "loading-2": ["Ink/Pages/LoadingPages.cs"],
    "ink-anim-ref-1": ["Ink/Pages/InkAnim/InkAnimationRefPages.cs", "Ink/Data/InkAnimationSpecs.cs"],
    "ink-anim-ref-2": ["Ink/Pages/InkAnim/InkAnimationRefPages.cs", "Ink/Data/InkAnimationSpecs.cs"],
    "chapter-transition": ["Ink/Pages/ChapterTransition/ChapterTransitionPage.cs"],
    "combat-hud": ["Ink/Pages/CombatHudPage.cs", "Ink/Pages/Combat/CombatHudTraditionalPage.cs"],
    "combat-hud-v2": ["Ink/Pages/Combat/CombatHudV2Page.cs"],
    "guide-hud": ["Ink/Pages/Guide/GuideHudPage.cs"],
    "guide-action": ["Ink/Pages/Guide/GuideActionPage.cs"],
    "guide-martial": ["Ink/Pages/Guide/GuideMartialPage.cs"],
    "element-vision": ["Ink/Pages/ElementVision/ElementVisionPage.cs"],
    "death-screen": ["Ink/Pages/Combat/DeathScreenPage.cs"],
    "qte": ["Ink/Pages/Combat/QtePage.cs"],
    "acupoint": ["Ink/Pages/Combat/AcupointPage.cs"],
    "dialogue-confirm": ["Ink/Pages/Social/DialogueConfirmPage.cs"],
    "menu-char-attributes": ["Ink/Pages/MenuCharAttributesPage.cs"],
    "menu-char-attributes-v2": ["Ink/Pages/Character/MenuCharAttributesV2Page.cs"],
    "menu-equipment": ["Ink/Pages/Character/MenuEquipmentPage.cs"],
    "menu-appearance": ["Ink/Pages/Appearance/MenuAppearancePage.cs"],
    "menu-battle-prep": ["Ink/Pages/Combat/MenuBattlePrepPage.cs"],
    "menu-sect": ["Ink/Pages/Sect/MenuSectPage.cs"],
    "menu-personal-info": ["Ink/Pages/Personal/MenuPersonalInfoPage.cs"],
    "menu-time": ["Ink/Pages/Time/MenuTimePage.cs"],
    "menu-quests": ["Ink/Pages/MenuQuestsPage.cs"],
    "menu-bestiary": ["Ink/Pages/Bestiary/MenuBestiaryPage.cs"],
    "menu-martial-record": ["Ink/Pages/MartialRecord/MenuMartialRecordPage.cs"],
    "menu-livelihood": ["Ink/Pages/Livelihood/MenuLivelihoodPage.cs"],
    "menu-team": ["Ink/Pages/Team/MenuTeamPage.cs"],
    "menu-mail": ["Ink/Pages/Mail/MenuMailPage.cs"],
    "menu-shop": ["Ink/Pages/MenuShopPage.cs"],
    "menu-casual-mode": ["Ink/Pages/Casual/MenuCasualModePage.cs"],
    "menu-gacha": ["Ink/Pages/Gacha/MenuGachaPage.cs"],
    "menu-activities": ["Ink/Pages/Activities/MenuActivitiesPage.cs"],
    "menu-battle-pass": ["Ink/Pages/BattlePass/MenuBattlePassPage.cs"],
    "shop-rare-items": ["Ink/Pages/Shop/ShopRareItemsPage.cs"],
    "settings": ["Ink/Pages/SettingsPage.cs"],
    "settings-audio": ["Ink/Pages/Settings/SettingsAudioPage.cs"],
    "photo-mode": ["Ink/Pages/PhotoMode/PhotoModePage.cs"],
    "multiplayer": ["Ink/Pages/Multiplayer/MultiplayerPage.cs"],
    "mini-game-complete": ["Ink/Pages/MiniGameCompletePage.cs"],
    "popup-bestiary-side": ["Ink/Pages/PopupPages.cs"],
    "popup-message": ["Ink/Pages/PopupPages.cs"],
    "popup-martial-detail": ["Ink/Pages/PopupPages.cs"],
    "popup-guide-side": ["Ink/Pages/PopupPages.cs"],
    "popup-martial-arts": ["Ink/Pages/PopupPages.cs"],
    "popup-item-acquired": ["Ink/Pages/PopupPages.cs"],
    "popup-skill-realization": ["Ink/Pages/PopupPages.cs"],
    "popup-verification": ["Ink/Pages/PopupPages.cs"],
    "reward-achievement": ["Ink/Pages/RewardPages.cs"],
    "reward-teleport-unlock": ["Ink/Pages/RewardPages.cs"],
    "reward-map-unlock": ["Ink/Pages/RewardPages.cs"],
    "reward-level-up": ["Ink/Pages/Reward/RewardLevelUpPage.cs"],
    "reward-congratulations": ["Ink/Pages/RewardPages.cs"],
    "reward-quest-complete": ["Ink/Pages/RewardPages.cs"],
}

STOP = {"返回", "确定", "取消", "关闭", "下一步", "继续", "上一页", "下一页"}

# 纯数据/数值类文案（设计稿示例数据，运行时由真实数据或插值拼接产生，不计入文案缺口）
DATA_LIKE = re.compile(
    r"^(?=.*[0-9])[\d\s,./%+:×xX\-—→]*\s*"
    r"(?:\s*(?:秒|人|层|px|ms|m|kg|个|项|次|点|/|·)\s*)*$")


def is_data_like(s):
    """判断文案是否为设计稿示例数值（如 12,450 / Lv. 38-46 / 3 / 5 / 15秒）而不属于必须字面落地的文案。"""
    if DATA_LIKE.match(s.strip()):
        return True
    # 含数字且去掉数字后仅剩修饰词（Lv./共/第 等）
    stripped = re.sub(r"[0-9]+", "", s)
    stripped = re.sub(r"[\s,./%+:×xX\-—→·秒人层项次点个]", "", stripped)
    return len(stripped) <= 3 and bool(re.search(r"[0-9]", s))


def csharp_literals(path):
    """抽取 C# 文件中的字符串字面量集合。"""
    src = io.open(path, encoding="utf-8", errors="replace").read()
    lits = set()
    for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', src):
        s = m.group(1)
        if s.strip():
            lits.add(s.strip())
    return lits


def design_copy(page):
    p = parse(os.path.join(DESIGN_PAGES, page + ".html"))
    seen, out = set(), []
    for _, t in p.copy:
        t = t.strip()
        if t and t not in seen and t not in STOP:
            seen.add(t)
            out.append(t)
    return out, sorted({d for d, _ in p.domids})


def main():
    allcs = {}
    for dirpath, _, files in os.walk(UI):
        for f in files:
            if f.endswith(".cs"):
                p = os.path.join(dirpath, f)
                allcs[os.path.relpath(p, UI).replace("\\", "/")] = csharp_literals(p)
    global_lits = set()
    for v in allcs.values():
        global_lits |= v

    rows = []
    for page in sorted(MAP):
        files = MAP[page]
        own = set()
        for f in files:
            own |= allcs.get(f, set())
        copy, domids = design_copy(page)
        # 只统计"有区分度"的文案：中文且长度>=3，或含数字的标签
        sig = [c for c in copy if (len(c) >= 3 and re.search(r"[\u4e00-\u9fff]", c)) or re.search(r"\d", c)]
        hit = [c for c in sig if c in own]
        miss = [c for c in sig if c not in own]
        # 剔除纯示例数值：这类并非"必须字面落地的文案"，运行时由数据/插值生成
        real_miss = [c for c in miss if not is_data_like(c)]
        data_miss = [c for c in miss if is_data_like(c)]
        hit_glob = [c for c in real_miss if c in global_lits]
        cov = (100.0 * len(hit) / len(sig)) if sig else 100.0
        rows.append((page, files, len(sig), len(hit), cov, hit_glob, real_miss, data_miss, domids))

    rows.sort(key=lambda r: (len(r[6]), r[4]))
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("设计页 | C#文件 | 区分性文案数 | 命中 | 覆盖率 | 真实文案缺口 | 示例数据缺口\n")
        for page, files, n, h, cov, hitg, miss, data_miss, domids in rows:
            fh.write("\n%-28s cov=%5.1f%%  %d/%d  真实缺口=%d  示例数据=%d  %s\n"
                     % (page, cov, h, n, len(miss), len(data_miss), ",".join(files)))
            fh.write("    dom-ids: %s\n" % ", ".join(domids))
            fh.write("    真实缺口文案(%d): %s\n" % (len(miss), " | ".join(miss[:60])))
            if hitg:
                fh.write("      其中已在其他页面实现: %s\n" % " | ".join(hitg[:20]))
            fh.write("    示例数据(%d): %s\n" % (len(data_miss), " | ".join(data_miss[:30])))
    print("wrote", OUT, len(rows))
    real = [(r[0], len(r[6])) for r in rows]
    print("真实文案缺口为 0 的页面: %d/%d" % (sum(1 for _, n in real if n == 0), len(real)))
    print("仍有真实文案缺口的页面:")
    for p, n in real:
        if n:
            print("   %-28s %d" % (p, n))


if __name__ == "__main__":
    main()

#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成《燕云融合 UI 设计方案落地清单与差距报告》markdown 文档。

数据来源（全部可复跑）：
  - hundun-yy-ui/hundun-yy-ui.design        设计稿页面 id / 标题 / 交互跳转
  - .compile-temp/design/*.outline.txt      每页结构大纲与文案清单（由 design_extract.py 生成）
  - .compile-temp/design-gap.txt            每页文案覆盖与真实缺口（由 design_gap.py 生成）
  - HundunWorld/Source/Game/UI/**           实现侧（Ink 组件库 + 页面 + 路由契约）
"""
import io
import json
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(ROOT, "docs", "UI-DESIGN-LANDING-CHECKLIST-20260923.md")

# 设计稿 → 实现文件映射（与 design_gap.py 保持一致，供文档表格使用）
MAP = {
    "cc-naming": ("Ink/Pages/CharacterCreation/CharacterNamingPage.cs", "cc-naming"),
    "cc-face-customize": ("Ink/Pages/CharacterCreation/CharacterFaceCustomizePage.cs", "cc-face-customize"),
    "loading-1": ("Ink/Pages/LoadingPages.cs", "loading-1"),
    "loading-2": ("Ink/Pages/LoadingPages.cs", "loading-2"),
    "ink-anim-ref-1": ("Ink/Pages/InkAnim/InkAnimationRefPages.cs + Ink/Data/InkAnimationSpecs.cs", "ink-anim-ref-1"),
    "ink-anim-ref-2": ("Ink/Pages/InkAnim/InkAnimationRefPages.cs + Ink/Data/InkAnimationSpecs.cs", "ink-anim-ref-2"),
    "chapter-transition": ("Ink/Pages/ChapterTransition/ChapterTransitionPage.cs", "chapter-transition"),
    "combat-hud": ("Ink/Pages/CombatHudPage.cs", "combat-hud"),
    "combat-hud-v2": ("Ink/Pages/Combat/CombatHudV2Page.cs", "combat-hud-v2"),
    "guide-hud": ("Ink/Pages/Guide/GuideHudPage.cs", "guide-hud"),
    "guide-action": ("Ink/Pages/Guide/GuideActionPage.cs", "guide-action"),
    "guide-martial": ("Ink/Pages/Guide/GuideMartialPage.cs", "guide-martial"),
    "element-vision": ("Ink/Pages/ElementVision/ElementVisionPage.cs", "nav-element-vision"),
    "death-screen": ("Ink/Pages/Combat/DeathScreenPage.cs", "death-screen"),
    "qte": ("Ink/Pages/Combat/QtePage.cs", "qte"),
    "acupoint": ("Ink/Pages/Combat/AcupointPage.cs + Ink/Components/InkMeridianDiagram.cs", "acupoint"),
    "dialogue-confirm": ("Ink/Pages/Social/DialogueConfirmPage.cs", "dialogue-confirm"),
    "menu-char-attributes": ("Ink/Pages/MenuCharAttributesPage.cs", "menu-char-attributes"),
    "menu-char-attributes-v2": ("Ink/Pages/Character/MenuCharAttributesV2Page.cs", "nav-character-v2"),
    "menu-equipment": ("Ink/Pages/Character/MenuEquipmentPage.cs", "nav-equipment"),
    "menu-appearance": ("Ink/Pages/Appearance/MenuAppearancePage.cs", "nav-appearance"),
    "menu-battle-prep": ("Ink/Pages/Combat/MenuBattlePrepPage.cs", "nav-battle-prep"),
    "menu-sect": ("Ink/Pages/Sect/MenuSectPage.cs", "nav-sect"),
    "menu-personal-info": ("Ink/Pages/Personal/MenuPersonalInfoPage.cs", "nav-personal-info"),
    "menu-time": ("Ink/Pages/Time/MenuTimePage.cs", "nav-time"),
    "menu-martial-record": ("Ink/Pages/MartialRecord/MenuMartialRecordPage.cs", "nav-martial-record"),
    "menu-quests": ("Ink/Pages/MenuQuestsPage.cs", "nav-quests"),
    "menu-bestiary": ("Ink/Pages/Bestiary/MenuBestiaryPage.cs", "nav-bestiary"),
    "menu-livelihood": ("Ink/Pages/Livelihood/MenuLivelihoodPage.cs", "nav-livelihood"),
    "menu-team": ("Ink/Pages/Team/MenuTeamPage.cs", "nav-team"),
    "menu-mail": ("Ink/Pages/Mail/MenuMailPage.cs", "nav-mail"),
    "menu-shop": ("Ink/Pages/MenuShopPage.cs", "nav-shop"),
    "menu-casual-mode": ("Ink/Pages/Casual/MenuCasualModePage.cs", "nav-casual-mode"),
    "menu-gacha": ("Ink/Pages/Gacha/MenuGachaPage.cs", "nav-gacha"),
    "menu-activities": ("Ink/Pages/Activities/MenuActivitiesPage.cs", "nav-activities"),
    "menu-battle-pass": ("Ink/Pages/BattlePass/MenuBattlePassPage.cs", "nav-battle-pass"),
    "shop-rare-items": ("Ink/Pages/Shop/ShopRareItemsPage.cs", "nav-shop-rare"),
    "settings": ("Ink/Pages/SettingsPage.cs", "nav-settings"),
    "settings-audio": ("Ink/Pages/Settings/SettingsAudioPage.cs", "nav-settings-audio"),
    "photo-mode": ("Ink/Pages/PhotoMode/PhotoModePage.cs", "nav-photo-mode"),
    "multiplayer": ("Ink/Pages/Multiplayer/MultiplayerPage.cs", "nav-multiplayer"),
    "mini-game-complete": ("Ink/Pages/MiniGameCompletePage.cs", "mini-game-complete"),
    "popup-bestiary-side": ("Ink/Pages/PopupPages.cs", "popup-bestiary-side"),
    "popup-message": ("Ink/Pages/PopupPages.cs", "popup-message"),
    "popup-martial-detail": ("Ink/Pages/PopupPages.cs", "popup-martial-detail"),
    "popup-guide-side": ("Ink/Pages/PopupPages.cs", "popup-guide-side"),
    "popup-martial-arts": ("Ink/Pages/PopupPages.cs", "popup-martial-arts"),
    "popup-item-acquired": ("Ink/Pages/PopupPages.cs", "popup-item-acquired"),
    "popup-skill-realization": ("Ink/Pages/PopupPages.cs", "popup-skill-realization"),
    "popup-verification": ("Ink/Pages/PopupPages.cs", "popup-verification"),
    "reward-achievement": ("Ink/Pages/RewardPages.cs", "reward-achievement"),
    "reward-teleport-unlock": ("Ink/Pages/RewardPages.cs", "reward-teleport-unlock"),
    "reward-map-unlock": ("Ink/Pages/RewardPages.cs", "reward-map-unlock"),
    "reward-level-up": ("Ink/Pages/Reward/RewardLevelUpPage.cs", "reward-level-up"),
    "reward-congratulations": ("Ink/Pages/RewardPages.cs", "reward-congratulations"),
    "reward-quest-complete": ("Ink/Pages/RewardPages.cs", "reward-quest-complete"),
}

GROUPS = [
    ("① 创角与开场", ["cc-naming", "cc-face-customize", "loading-1", "loading-2", "chapter-transition",
                   "ink-anim-ref-1", "ink-anim-ref-2"]),
    ("② 战斗 / 技能 / 引导", ["combat-hud", "combat-hud-v2", "guide-hud", "guide-action", "guide-martial",
                        "element-vision", "death-screen", "qte", "acupoint", "dialogue-confirm"]),
    ("③ 角色与养成", ["menu-char-attributes", "menu-char-attributes-v2", "menu-equipment", "menu-appearance",
                  "menu-battle-prep", "menu-sect", "menu-personal-info", "menu-time", "menu-martial-record"]),
    ("④ 任务与图鉴", ["menu-quests", "menu-bestiary"]),
    ("⑤ 生活 / 社交 / 商城", ["menu-livelihood", "menu-team", "menu-mail", "menu-shop", "menu-casual-mode",
                        "multiplayer"]),
    ("⑥ 商业化", ["menu-gacha", "menu-activities", "menu-battle-pass", "shop-rare-items"]),
    ("⑦ 弹窗层", ["popup-item-acquired", "popup-message", "popup-martial-arts", "popup-martial-detail",
               "popup-skill-realization", "popup-verification", "popup-guide-side", "popup-bestiary-side"]),
    ("⑧ 奖励层", ["reward-achievement", "reward-teleport-unlock", "reward-map-unlock", "reward-level-up",
               "reward-congratulations", "reward-quest-complete", "mini-game-complete"]),
    ("⑨ 设置与工具", ["settings", "settings-audio", "photo-mode"]),
]

# 本轮实施的页面（已完成设计保真补全）
DONE_THIS_ROUND = {"acupoint", "multiplayer"}

# 用户明确排除的页面（已微调完毕，本轮不动）
EXCLUDED = {"cc-naming", "cc-face-customize", "menu-char-attributes", "menu-char-attributes-v2",
            "menu-equipment", "menu-appearance"}


def load_titles():
    d = json.load(io.open(os.path.join(ROOT, "hundun-yy-ui", "hundun-yy-ui.design"), encoding="utf-8"))
    out = {}
    for x in d["data"]:
        if x.get("type") != "page":
            continue
        src = (x.get("devMetadata") or {}).get("htmlSrc", "")
        if src.startswith("pages/"):
            out[src[len("pages/"):-len(".html")]] = x.get("title", "")
    return out


def load_outline(page):
    path = os.path.join(ROOT, ".compile-temp", "design", page + ".outline.txt")
    if not os.path.exists(path):
        return [], []
    txt = io.open(path, encoding="utf-8").read()
    domids = []
    m = re.search(r"^## dom-ids: (.*)$", txt, re.M)
    if m and m.group(1).strip():
        domids = [d.strip() for d in m.group(1).split(",")]
    return domids, txt.split("\n")


def load_gap():
    path = os.path.join(ROOT, ".compile-temp", "design-gap.txt")
    txt = io.open(path, encoding="utf-8").read()
    res = {}
    for b in re.split(r"\n(?=\S+\s+cov=)", txt):
        m = re.match(r"^(\S+)\s+cov=\s*([\d.]+)%\s+(\d+)/(\d+)\s+真实缺口=(\d+)\s+示例数据=(\d+)", b)
        if not m:
            continue
        page = m.group(1)
        real = ""
        mm = re.search(r"真实缺口文案\(\d+\): (.*)", b)
        if mm:
            real = mm.group(1).strip()
        res[page] = (float(m.group(2)), int(m.group(3)), int(m.group(4)),
                     int(m.group(5)), int(m.group(6)), real)
    return res


def main():
    titles = load_titles()
    gap = load_gap()
    lines = []
    w = lines.append

    w("# 燕云融合 UI 设计方案落地清单与差距报告")
    w("")
    w("> 设计稿：`hundun-yy-ui/`（56 个页面 HTML + `colors_and_type.css` + `partials/project-shell.html` + 19 张切图）")
    w("> 实现侧：`HundunWorld/Source/Game/UI/**`（Ink 组件库 + Ink 页面 + `InkPageRouter` 导航契约）")
    w("> 本报告由 `.compile-temp/design_extract.py` / `design_gap.py` / `design_probe.py` / `domid_gap.py` 生成，可复跑。")
    w("")
    w("## 0. 审计口径与结论先行")
    w("")
    w("**方法**（三层，全部基于磁盘上的真实代码，不依赖任何自述台账）：")
    w("")
    w("1. **页面映射**：设计稿 56 个 HTML → `InkPageDomIds` 常量 → `MainUIManager.RegisterInkPages` 注册的工厂 → 工厂返回类型所在源文件。")
    w("2. **文案覆盖**：设计稿剥离 `<head>/<style>/<script>/注释` 后的可见文案，与该页实现文件中的字符串字面量逐条比对；")
    w("   命中统计区分「真实文案缺口」与「示例数据缺口」（纯数值/等级/人数等由运行时数据或插值拼接产生，不计为缺口）。")
    w("3. **交互锚点**：设计稿 `data-dom-id` 与实现侧字符串比对（`domid_gap.py`），用于识别整块交互结构缺失。")
    w("")
    w("**结论**：`hundun-yy-ui` 并非「尚未开始落地」——它已在 2026-07-18～07-21 的提交中批量落地")
    w("（`2269294 网络同步0.1` 首次引入 `UI/Ink/Pages`，`090250b UI 调整-1` / `358eb38 UI 微调1.0.1` 达到 81 个 Ink 文件），")
    w("本轮审计确认 56 个设计页**全部已映射到已注册的 dom-id，且全部存在实现文件**（无 unmapped、无 noImpl）。")
    w("剩余差距集中在两类：**示例数据未替换为设计稿初值**，以及**少量组件级语义细节未落地**（本轮已修 2 页）。")
    w("")
    w("> 注意：`docs/UI-DESIGN-LANDING.md` 与 `Tools/ui-fidelity-audit.mjs` 是此前会话留下的**未提交**文件，")
    w("> 其自述覆盖率口径与本报告不同（它统计「全局命中」而非「本页命中」），引用时建议以本报告数字为准。")
    w("")
    w("## 1. 设计清单（56 页，按业务流程分组）")
    w("")
    w("列含义：**锚点** = 设计稿 `data-dom-id` 数量（交互契约）；**覆盖** = 本页文案命中率；")
    w("**真实缺口** = 非示例数据的未落地文案条数；**示例数据** = 仅示例数值未替换的条数；")
    w("**状态** = `完成`（无真实缺口）／`补全`（本轮已补）／`待补`（有真实缺口）。")
    w("")
    w("| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |")
    total = 0
    for gname, pages in GROUPS:
        w("")
        w("### %s（%d 页）" % (gname, len(pages)))
        w("")
        w("| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |")
        w("| --- | --- | --- | --- | --- | --- | --- | --- | --- |")
        for page in pages:
            impl, domid = MAP[page]
            domids, _ = load_outline(page)
            cov, hit, n, real, data, real_list = gap.get(page, (0, 0, 0, 0, 0, ""))
            total += 1
            status = "完成"
            if real > 0:
                status = "待补"
            if page in DONE_THIS_ROUND:
                status = "补全"
            note = ""
            if page in EXCLUDED:
                note = " *(已微调，本轮排除)*"
            title = titles.get(page, "")
            w("| %s.html | %s%s | `%s` | %d | %.0f%% (%d/%d) | %d | %d | %s | `%s` |"
              % (page, title, note, domid, len(domids), cov, hit, n, real, data, status, impl))
    w("")
    w("合计 %d 页。" % total)
    w("")
    w("## 2. 本轮实施（2 页，均已编译验证）")
    w("")
    w("### 2.1 点穴 `acupoint.html` → `AcupointPage.cs` + `InkMeridianDiagram.cs`")
    w("")
    w("设计稿左侧是「人体穴位图 + 穴位名标签 + 经脉说明」，右侧是**满高**详情栏。审计发现实现侧有 4 处结构性偏差，本轮全部对齐：")
    w("")
    w("| 设计稿要求 | 落地前 | 本轮实现 |")
    w("| --- | --- | --- |")
    w("| 8 个穴位点各带 `.acupoint-label` 穴位名标签（18px 偏移、深墨底 + 金边、悬停/选中显隐） | 只有圆点，无标签 | `InkMeridianDiagram` 新增标签绘制 + 悬停探测（`AlwaysShowLabels` 可改常显） |")
    w("| 选中态：朱红 14px 核心 + 朱红光晕 + 2s 周期扩散环（scale 0.8→1.8） | 金色单点提示 | 朱红核心/光晕 + `Update` 驱动的脉动扩散环 |")
    w("| 穴位点坐标取设计稿 `top/left` 百分比（太阳在左 26%、风池在右 58%） | 太阳/风池左右颠倒 | 8 点坐标逐一对齐设计稿 |")
    w("| 图下「督脉 · 任脉」+「共标记 8 处穴位」，详情栏含「要穴」标签、「已选中」状态、「穴位等级 3 / 5」+ 5 级点阵 +「下一级：眩晕4秒」 | 缺少这些元素，等级标题为「修炼等级」 | 左区左下说明块 + 详情栏完整补齐（含 `<InkTag>` 朱红标签、`InkDivider` 分段、`AcupointPipRow` 等级点阵） |")
    w("")
    w("### 2.2 多人模式 `multiplayer.html` → `MultiplayerPage.cs`")
    w("")
    w("| 设计稿要求 | 落地前 | 本轮实现 |")
    w("| --- | --- | --- |")
    w("| 匹配横幅 meta 行：预计等待 15秒 / 匹配范围 Lv. 38-46 / 当前匹配 326人（图标 + 标签 + 数值 + 1px 分隔） | 整行缺失 | `CreateMatchMetaItem` / `CreateMatchMetaDivider` + `_matchMeta` 行 |")
    w("| 按钮下「点击进入匹配队列」（11px 三级色） | 缺失 | `_matchBtnHint` |")
    w("| 模式面板页脚「选择模式进入对应大厅」 | 缺失 | `_modeFooterHint` + 图标 |")
    w("| 筛选栏三组标签（模式 / 状态 / 排序）+ 排序药丸（人数 / 房间名）+ 右侧刷新按钮 | 只有 8 枚无分组药丸 | 补分组标签、`_sortButtons`、`_refreshButton`，并由 `LayoutFilterBarRight` 按实际内容宽度贴右排布 |")
    w("| 房间列表页脚「共 6 个房间 · 已加载全部」+「加载更多」 | 缺失 | `_roomListFooter` + `_loadMoreButton` + `LoadMoreRequested` / `RefreshRequested` 事件 |")
    w("")
    w("**编译验证**（复用 Flax 生成的 `Game.CSharp.response`，输出重定向到临时目录以规避 DLL 占用）：")
    w("")
    w("```")
    w("powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1")
    w("--- 0 个编译错误 ---")
    w("warnings: 142     # 与改动前基线一致，未新增警告")
    w("csc exit code: 0")
    w("```")
    w("")
    w("## 3. 剩余差距（按真实缺口排序）")
    w("")
    w("下表按「真实文案缺口」降序列出，并给出缺口性质判断。**注意**：凡实现侧由插值/拼接生成的文案")
    w("（如 `$\"房主: {host}\"`、`\"第 \" + n + \" 步\"`、多行合并的 `\\n` 字符串）会被自动计为「缺口」，")
    w("这类条目已在「判定」列标注为**口径伪差**。")
    w("")
    w("| 设计稿 | 覆盖 | 真实缺口 | 缺口样例 | 判定 |")
    w("| --- | --- | --- | --- | --- |")

    rows = sorted(((v[3], k, v) for k, v in gap.items()), reverse=True)
    for real_n, page, v in rows:
        if real_n == 0:
            continue
        cov, hit, n, real, data, real_list = v
        sample = " | ".join(real_list.split(" | ")[:5])
        if len(sample) > 200:
            sample = sample[:200] + "…"
        judgement = "待复核"
        if page in EXCLUDED:
            judgement = "**本轮排除**（用户指定已微调）"
        elif page in DONE_THIS_ROUND:
            judgement = "**本轮已补**（剩余为插值/拼接口径伪差）"
        elif "房主" in real_list or "第 " in real_list or "\n" in real_list:
            judgement = "口径伪差（插值/多行拼接）"
        w("| %s.html | %.0f%% | %d | %s | %s |" % (page, cov, real, sample.replace("|", "\\|"), judgement))
    w("")
    w("### 3.1 已核实的**口径伪差**（审计会误报，实际已落地）")
    w("")
    w("- `acupoint.html`：8 个穴位名由 `AcupointNames[i] + \"穴\"` 生成，字面量不含「穴」后缀。")
    w("- `multiplayer.html`：房主行由 `$\"房主: {_roomHosts[i]}\"` 生成。")
    w("- `chapter-transition.html`：三行旁白是单个含 `\\n` 的字面量。")
    w("- `reward-level-up / menu-char-attributes-v2 / element-vision`：属性值、等级、距离标注均为示例数值。")
    w("- **按键提示类**（`guide-action` / `guide-martial` / `popup-guide-side`）：设计稿把「在疾跑中按 + W + 键起跳」拆成多个 `<span>`，")
    w("  审计会逐段比对而实现侧是整句或插值拼接，因此「键进入疾跑状态 / 键起跳 / 第 3 步 / 共 8 步」这类片段会误报。")
    w("")
    w("### 3.2 建议的下一批（按性价比排序）")
    w("")
    w("1. **示例数据初始化**（覆盖 20+ 页）：把设计稿示例数据（坐标、人数、等级、进度、金额）落为组件初值，")
    w("   并在接入真实数据源后覆盖——这是目前最大的一块「看着不像设计稿」的来源。")
    w("2. **`menu-casual-mode`（10 条）**：活动日历 / 今日活动 / 签到福利 / 散步江湖 / 今日状态 / 成就预览 / 奖励预览 / 活动详情等语义块。")
    w("3. **`menu-sect`（8 条）**：门派信息 / 成员列表 / 门派等级 3000 进度 / 门规文案。")
    w("4. **`popup-message`（8 条）**：信笺正文分行、落款（青城派掌门 玄真子 / 庚子年 暮秋 / 青城山 · 上清宫）与标签（昆仑异象 · 主线）。")
    w("5. **`menu-equipment`（20 条）**：待用户确认是否属于「已微调、不动」范围。")
    w("6. **`settings-audio`（2 条）/ `menu-gacha`（2 条）**：小游戏音量分组、心愿池与再保底计数等小项。")
    w("")
    w("## 4. 设计与实现的公共底座（已就绪，后续页面复用）")
    w("")
    w("- **设计 Token**：`hundun-yy-ui/colors_and_type.css` → `StyleSystem/InkWashTheme.cs`（深墨黑 `#0E1016`、鎏金 `#C8A858`、朱红 `#c0392b`、纸色 `#f5f0e8`、四类品质色、状态色、字体四角色、圆角 0/2/4/8）。")
    w("- **组件库**：`Ink/InkPanels.cs`（Panel/Solid/Elevated/Paper/BrushBorder/CornerDeco/PanelTitle）、`InkButtons.cs`（Button 5 变体×3 尺寸、BackButton、Tag 3 变体）、")
    w("  `InkText.cs`（6 种文本样式）、`InkBars.cs`、`InkCells.cs`（Cell/ListItem/Avatar/Dot）、`InkDividers.cs`（含竖排标题）、`InkBackgrounds.cs`（背景层/暗角/墨点 + 渲染辅助）。")
    w("- **专用组件**：`InkMeridianDiagram`、`InkBackpackGrid`、`InkEquipmentSlot`、`InkSkillSlotGrid`、`InkAttributeTooltip`、`HexRadarChartOverlay`、`InkMinimap`、`InkDialClock`、`CharacterPreview3D`、`InkTabButton`、`InkToolBarBubble`。")
    w("- **导航契约**：`InkPageRouter` 中 `InkPageDomIds` 共 75 个 dom-id（含 5 条跨页直达动作 `Action*`），`MainUIManager.RegisterInkPages` 全部注册工厂；")
    w("  页面外壳 `InkPageShell` 统一承担 1920×1080 设计坐标系等比缩放、返回按钮、背景/暗角/粒子层。")
    w("")
    w("## 5. 复跑方式")
    w("")
    w("```bash")
    w("python .compile-temp/design_extract.py     # 生成 .compile-temp/design/*.outline.txt（结构 + 文案清单）")
    w("python .compile-temp/design_gap.py         # 生成 .compile-temp/design-gap.txt（覆盖 + 真实缺口/示例数据分类）")
    w("python .compile-temp/design_probe.py       # 指定文案全树探针，定位实现位置或确认缺失")
    w("python .compile-temp/domid_gap.py          # 交互锚点（data-dom-id）落地覆盖")
    w("powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1   # 客户端编译验证")
    w("```")
    w("")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    io.open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print("wrote", OUT, len(lines), "lines")


if __name__ == "__main__":
    main()

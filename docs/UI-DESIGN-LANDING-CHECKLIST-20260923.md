# 燕云融合 UI 设计方案落地清单与差距报告

> 设计稿：`hundun-yy-ui/`（56 个页面 HTML + `colors_and_type.css` + `partials/project-shell.html` + 19 张切图）
> 实现侧：`HundunWorld/Source/Game/UI/**`（Ink 组件库 + Ink 页面 + `InkPageRouter` 导航契约）
> 本报告由 `.compile-temp/design_extract.py` / `design_gap.py` / `design_probe.py` / `domid_gap.py` 生成，可复跑。

## 0. 审计口径与结论先行

**方法**（三层，全部基于磁盘上的真实代码，不依赖任何自述台账）：

1. **页面映射**：设计稿 56 个 HTML → `InkPageDomIds` 常量 → `MainUIManager.RegisterInkPages` 注册的工厂 → 工厂返回类型所在源文件。
2. **文案覆盖**：设计稿剥离 `<head>/<style>/<script>/注释` 后的可见文案，与该页实现文件中的字符串字面量逐条比对；
   命中统计区分「真实文案缺口」与「示例数据缺口」（纯数值/等级/人数等由运行时数据或插值拼接产生，不计为缺口）。
3. **交互锚点**：设计稿 `data-dom-id` 与实现侧字符串比对（`domid_gap.py`），用于识别整块交互结构缺失。

**结论**：`hundun-yy-ui` 并非「尚未开始落地」——它已在 2026-07-18～07-21 的提交中批量落地
（`2269294 网络同步0.1` 首次引入 `UI/Ink/Pages`，`090250b UI 调整-1` / `358eb38 UI 微调1.0.1` 达到 81 个 Ink 文件），
本轮审计确认 56 个设计页**全部已映射到已注册的 dom-id，且全部存在实现文件**（无 unmapped、无 noImpl）。
剩余差距集中在两类：**示例数据未替换为设计稿初值**，以及**少量组件级语义细节未落地**（本轮已修 2 页）。

> 注意：`docs/UI-DESIGN-LANDING.md` 与 `Tools/ui-fidelity-audit.mjs` 是此前会话留下的**未提交**文件，
> 其自述覆盖率口径与本报告不同（它统计「全局命中」而非「本页命中」），引用时建议以本报告数字为准。

## 1. 设计清单（56 页，按业务流程分组）

列含义：**锚点** = 设计稿 `data-dom-id` 数量（交互契约）；**覆盖** = 本页文案命中率；
**真实缺口** = 非示例数据的未落地文案条数；**示例数据** = 仅示例数值未替换的条数；
**状态** = `完成`（无真实缺口）／`补全`（本轮已补）／`待补`（有真实缺口）。

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |

### ① 创角与开场（7 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| cc-naming.html | 创角·命名 *(已微调，本轮排除)* | `cc-naming` | 2 | 100% (13/13) | 0 | 0 | 完成 | `Ink/Pages/CharacterCreation/CharacterNamingPage.cs` |
| cc-face-customize.html | 创角·捏脸 *(已微调，本轮排除)* | `cc-face-customize` | 1 | 100% (8/8) | 0 | 0 | 完成 | `Ink/Pages/CharacterCreation/CharacterFaceCustomizePage.cs` |
| loading-1.html | 加载·山水 | `loading-1` | 3 | 100% (7/7) | 0 | 0 | 完成 | `Ink/Pages/LoadingPages.cs` |
| loading-2.html | 加载·远峰 | `loading-2` | 5 | 100% (9/9) | 0 | 0 | 完成 | `Ink/Pages/LoadingPages.cs` |
| chapter-transition.html | 章节过场 | `chapter-transition` | 1 | 62% (5/8) | 3 | 0 | 待补 | `Ink/Pages/ChapterTransition/ChapterTransitionPage.cs` |
| ink-anim-ref-1.html | 水墨分镜·晕染 | `ink-anim-ref-1` | 0 | 100% (31/31) | 0 | 0 | 完成 | `Ink/Pages/InkAnim/InkAnimationRefPages.cs + Ink/Data/InkAnimationSpecs.cs` |
| ink-anim-ref-2.html | 水墨分镜·笔触 | `ink-anim-ref-2` | 0 | 100% (36/36) | 0 | 0 | 完成 | `Ink/Pages/InkAnim/InkAnimationRefPages.cs + Ink/Data/InkAnimationSpecs.cs` |

### ② 战斗 / 技能 / 引导（10 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| combat-hud.html | 战斗HUD | `combat-hud` | 9 | 100% (3/3) | 0 | 0 | 完成 | `Ink/Pages/CombatHudPage.cs` |
| combat-hud-v2.html | 战斗HUDv2 | `combat-hud-v2` | 1 | 100% (29/29) | 0 | 0 | 完成 | `Ink/Pages/Combat/CombatHudV2Page.cs` |
| guide-hud.html | 引导·HUD | `guide-hud` | 5 | 100% (4/4) | 0 | 0 | 完成 | `Ink/Pages/Guide/GuideHudPage.cs` |
| guide-action.html | 引导操作 | `guide-action` | 4 | 67% (6/9) | 3 | 0 | 待补 | `Ink/Pages/Guide/GuideActionPage.cs` |
| guide-martial.html | 引导奇术 | `guide-martial` | 4 | 64% (9/14) | 5 | 0 | 待补 | `Ink/Pages/Guide/GuideMartialPage.cs` |
| element-vision.html | 元素视野 | `nav-element-vision` | 3 | 50% (6/12) | 2 | 4 | 待补 | `Ink/Pages/ElementVision/ElementVisionPage.cs` |
| death-screen.html | 阵亡 | `death-screen` | 3 | 80% (4/5) | 1 | 0 | 待补 | `Ink/Pages/Combat/DeathScreenPage.cs` |
| qte.html | QTE·千钧 | `qte` | 3 | 75% (3/4) | 1 | 0 | 待补 | `Ink/Pages/Combat/QtePage.cs` |
| acupoint.html | 点穴·经络 | `acupoint` | 11 | 53% (9/17) | 8 | 0 | 补全 | `Ink/Pages/Combat/AcupointPage.cs + Ink/Components/InkMeridianDiagram.cs` |
| dialogue-confirm.html | 对话确认 | `dialogue-confirm` | 5 | 100% (4/4) | 0 | 0 | 完成 | `Ink/Pages/Social/DialogueConfirmPage.cs` |

### ③ 角色与养成（9 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| menu-char-attributes.html | 角色属性 *(已微调，本轮排除)* | `menu-char-attributes` | 9 | 6% (1/18) | 13 | 4 | 待补 | `Ink/Pages/MenuCharAttributesPage.cs` |
| menu-char-attributes-v2.html | 属性面板V2 *(已微调，本轮排除)* | `nav-character-v2` | 2 | 64% (28/44) | 1 | 15 | 待补 | `Ink/Pages/Character/MenuCharAttributesV2Page.cs` |
| menu-equipment.html | 装备 *(已微调，本轮排除)* | `nav-equipment` | 33 | 42% (20/48) | 20 | 8 | 待补 | `Ink/Pages/Character/MenuEquipmentPage.cs` |
| menu-appearance.html | 外观 *(已微调，本轮排除)* | `nav-appearance` | 58 | 95% (20/21) | 1 | 0 | 待补 | `Ink/Pages/Appearance/MenuAppearancePage.cs` |
| menu-battle-prep.html | 备战 | `nav-battle-prep` | 2 | 94% (52/55) | 1 | 2 | 待补 | `Ink/Pages/Combat/MenuBattlePrepPage.cs` |
| menu-sect.html | 门派 | `nav-sect` | 2 | 85% (68/80) | 8 | 4 | 待补 | `Ink/Pages/Sect/MenuSectPage.cs` |
| menu-personal-info.html | 个人信息 | `nav-personal-info` | 20 | 100% (25/25) | 0 | 0 | 完成 | `Ink/Pages/Personal/MenuPersonalInfoPage.cs` |
| menu-time.html | 时辰 | `nav-time` | 8 | 88% (21/24) | 2 | 1 | 待补 | `Ink/Pages/Time/MenuTimePage.cs` |
| menu-martial-record.html | 武林录 | `nav-martial-record` | 17 | 100% (41/41) | 0 | 0 | 完成 | `Ink/Pages/MartialRecord/MenuMartialRecordPage.cs` |

### ④ 任务与图鉴（2 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| menu-quests.html | 任务 | `nav-quests` | 16 | 98% (44/45) | 0 | 1 | 完成 | `Ink/Pages/MenuQuestsPage.cs` |
| menu-bestiary.html | 博物志 | `nav-bestiary` | 27 | 92% (24/26) | 1 | 1 | 待补 | `Ink/Pages/Bestiary/MenuBestiaryPage.cs` |

### ⑤ 生活 / 社交 / 商城（6 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| menu-livelihood.html | 营生 | `nav-livelihood` | 14 | 64% (28/44) | 1 | 15 | 待补 | `Ink/Pages/Livelihood/MenuLivelihoodPage.cs` |
| menu-team.html | 组队 | `nav-team` | 32 | 82% (28/34) | 5 | 1 | 待补 | `Ink/Pages/Team/MenuTeamPage.cs` |
| menu-mail.html | 邮箱 | `nav-mail` | 28 | 83% (38/46) | 4 | 4 | 待补 | `Ink/Pages/Mail/MenuMailPage.cs` |
| menu-shop.html | 商店 | `nav-shop` | 40 | 93% (28/30) | 1 | 1 | 待补 | `Ink/Pages/MenuShopPage.cs` |
| menu-casual-mode.html | 休闲模式 | `nav-casual-mode` | 25 | 74% (46/62) | 10 | 6 | 待补 | `Ink/Pages/Casual/MenuCasualModePage.cs` |
| multiplayer.html | 多人模式 | `nav-multiplayer` | 25 | 82% (36/44) | 6 | 2 | 补全 | `Ink/Pages/Multiplayer/MultiplayerPage.cs` |

### ⑥ 商业化（4 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| menu-gacha.html | 抽卡祈愿 | `nav-gacha` | 2 | 90% (34/38) | 2 | 2 | 待补 | `Ink/Pages/Gacha/MenuGachaPage.cs` |
| menu-activities.html | 活动 | `nav-activities` | 2 | 100% (45/45) | 0 | 0 | 完成 | `Ink/Pages/Activities/MenuActivitiesPage.cs` |
| menu-battle-pass.html | 通行证 | `nav-battle-pass` | 4 | 79% (83/105) | 4 | 18 | 待补 | `Ink/Pages/BattlePass/MenuBattlePassPage.cs` |
| shop-rare-items.html | 奇珍阁 | `nav-shop-rare` | 26 | 79% (42/53) | 4 | 7 | 待补 | `Ink/Pages/Shop/ShopRareItemsPage.cs` |

### ⑦ 弹窗层（8 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| popup-item-acquired.html | 物品获得 | `popup-item-acquired` | 6 | 100% (5/5) | 0 | 0 | 完成 | `Ink/Pages/PopupPages.cs` |
| popup-message.html | 留言 | `popup-message` | 5 | 33% (4/12) | 8 | 0 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-martial-arts.html | 武学 | `popup-martial-arts` | 4 | 92% (37/40) | 2 | 1 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-martial-detail.html | 奇术详情 | `popup-martial-detail` | 18 | 75% (27/36) | 5 | 4 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-skill-realization.html | 心法领悟 | `popup-skill-realization` | 6 | 90% (18/20) | 2 | 0 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-verification.html | 验讫 | `popup-verification` | 5 | 93% (13/14) | 1 | 0 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-guide-side.html | 引导侧弹窗 | `popup-guide-side` | 6 | 61% (14/23) | 6 | 3 | 待补 | `Ink/Pages/PopupPages.cs` |
| popup-bestiary-side.html | 博物侧弹窗 | `popup-bestiary-side` | 6 | 96% (26/27) | 1 | 0 | 待补 | `Ink/Pages/PopupPages.cs` |

### ⑧ 奖励层（7 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| reward-achievement.html | 成就解锁 | `reward-achievement` | 5 | 89% (8/9) | 0 | 1 | 完成 | `Ink/Pages/RewardPages.cs` |
| reward-teleport-unlock.html | 传送解锁 | `reward-teleport-unlock` | 3 | 100% (7/7) | 0 | 0 | 完成 | `Ink/Pages/RewardPages.cs` |
| reward-map-unlock.html | 地图解锁 | `reward-map-unlock` | 3 | 79% (11/14) | 0 | 3 | 完成 | `Ink/Pages/RewardPages.cs` |
| reward-level-up.html | 等级提升 | `reward-level-up` | 5 | 30% (6/20) | 1 | 13 | 待补 | `Ink/Pages/Reward/RewardLevelUpPage.cs` |
| reward-congratulations.html | 恭喜获得 | `reward-congratulations` | 2 | 91% (10/11) | 1 | 0 | 待补 | `Ink/Pages/RewardPages.cs` |
| reward-quest-complete.html | 任务完成 | `reward-quest-complete` | 2 | 81% (13/16) | 2 | 1 | 待补 | `Ink/Pages/RewardPages.cs` |
| mini-game-complete.html | 小游戏完成 | `mini-game-complete` | 8 | 100% (12/12) | 0 | 0 | 完成 | `Ink/Pages/MiniGameCompletePage.cs` |

### ⑨ 设置与工具（3 页）

| 设计稿 | 页面 | dom-id | 锚点 | 覆盖 | 真实缺口 | 示例数据 | 状态 | 实现文件 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| settings.html | 设置 | `nav-settings` | 2 | 98% (52/53) | 0 | 1 | 完成 | `Ink/Pages/SettingsPage.cs` |
| settings-audio.html | 音频设置 | `nav-settings-audio` | 3 | 95% (37/39) | 2 | 0 | 待补 | `Ink/Pages/Settings/SettingsAudioPage.cs` |
| photo-mode.html | 拍照模式 | `nav-photo-mode` | 15 | 89% (16/18) | 0 | 2 | 完成 | `Ink/Pages/PhotoMode/PhotoModePage.cs` |

合计 56 页。

## 2. 本轮实施（2 页，均已编译验证）

### 2.1 点穴 `acupoint.html` → `AcupointPage.cs` + `InkMeridianDiagram.cs`

设计稿左侧是「人体穴位图 + 穴位名标签 + 经脉说明」，右侧是**满高**详情栏。审计发现实现侧有 4 处结构性偏差，本轮全部对齐：

| 设计稿要求 | 落地前 | 本轮实现 |
| --- | --- | --- |
| 8 个穴位点各带 `.acupoint-label` 穴位名标签（18px 偏移、深墨底 + 金边、悬停/选中显隐） | 只有圆点，无标签 | `InkMeridianDiagram` 新增标签绘制 + 悬停探测（`AlwaysShowLabels` 可改常显） |
| 选中态：朱红 14px 核心 + 朱红光晕 + 2s 周期扩散环（scale 0.8→1.8） | 金色单点提示 | 朱红核心/光晕 + `Update` 驱动的脉动扩散环 |
| 穴位点坐标取设计稿 `top/left` 百分比（太阳在左 26%、风池在右 58%） | 太阳/风池左右颠倒 | 8 点坐标逐一对齐设计稿 |
| 图下「督脉 · 任脉」+「共标记 8 处穴位」，详情栏含「要穴」标签、「已选中」状态、「穴位等级 3 / 5」+ 5 级点阵 +「下一级：眩晕4秒」 | 缺少这些元素，等级标题为「修炼等级」 | 左区左下说明块 + 详情栏完整补齐（含 `<InkTag>` 朱红标签、`InkDivider` 分段、`AcupointPipRow` 等级点阵） |

### 2.2 多人模式 `multiplayer.html` → `MultiplayerPage.cs`

| 设计稿要求 | 落地前 | 本轮实现 |
| --- | --- | --- |
| 匹配横幅 meta 行：预计等待 15秒 / 匹配范围 Lv. 38-46 / 当前匹配 326人（图标 + 标签 + 数值 + 1px 分隔） | 整行缺失 | `CreateMatchMetaItem` / `CreateMatchMetaDivider` + `_matchMeta` 行 |
| 按钮下「点击进入匹配队列」（11px 三级色） | 缺失 | `_matchBtnHint` |
| 模式面板页脚「选择模式进入对应大厅」 | 缺失 | `_modeFooterHint` + 图标 |
| 筛选栏三组标签（模式 / 状态 / 排序）+ 排序药丸（人数 / 房间名）+ 右侧刷新按钮 | 只有 8 枚无分组药丸 | 补分组标签、`_sortButtons`、`_refreshButton`，并由 `LayoutFilterBarRight` 按实际内容宽度贴右排布 |
| 房间列表页脚「共 6 个房间 · 已加载全部」+「加载更多」 | 缺失 | `_roomListFooter` + `_loadMoreButton` + `LoadMoreRequested` / `RefreshRequested` 事件 |

**编译验证**（复用 Flax 生成的 `Game.CSharp.response`，输出重定向到临时目录以规避 DLL 占用）：

```
powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1
--- 0 个编译错误 ---
warnings: 142     # 与改动前基线一致，未新增警告
csc exit code: 0
```

## 3. 剩余差距（按真实缺口排序）

下表按「真实文案缺口」降序列出，并给出缺口性质判断。**注意**：凡实现侧由插值/拼接生成的文案
（如 `$"房主: {host}"`、`"第 " + n + " 步"`、多行合并的 `\n` 字符串）会被自动计为「缺口」，
这类条目已在「判定」列标注为**口径伪差**。

| 设计稿 | 覆盖 | 真实缺口 | 缺口样例 | 判定 |
| --- | --- | --- | --- | --- |
| menu-equipment.html | 42% | 20 | 个人信息 \| 博物志 \| 武林录 \| 无名侠 \| 逍遥派 | **本轮排除**（用户指定已微调） |
| menu-char-attributes.html | 6% | 13 | 个人信息 \| 博物志 \| 武林录 \| 无名侠 \| 逍遥派 | **本轮排除**（用户指定已微调） |
| menu-casual-mode.html | 74% | 10 | 燕雲十六聲 \| 不肝 · 休闲模式 \| 轻松游玩，奖励不落 \| 今日活动 \| 连签进度 | 待复核 |
| popup-message.html | 33% | 8 | 近日昆仑山中异象频生，墨麒麟出没，恐有大变。山中灵气紊乱，飞禽走兽皆不安其位，草木枯荣失序，此乃天地示警之兆。 \| 老夫已派弟子前往探查，望少侠亦能相助。江湖之中，能辨此异象者寥寥，少侠身负天命，或可窥见其中端倪。 \| 此事关系江湖安危，切不可等闲视之。若任其蔓延，恐殃及无辜苍生，届时悔之晚矣。 \| 若有发现，速来报知。青城掌门 顿首。 \| 青城派掌门 玄真子 | 待复核 |
| menu-sect.html | 85% | 8 | 混沌世界 \| 个人信息 \| 门派列表 \| 内门弟子 · 贡献 3200 \| 不得以门派武学欺凌弱小，违者逐出师门。 | 待复核 |
| acupoint.html | 53% | 8 | 百会穴 \| 太阳穴 \| 风池穴 \| 膻中穴 \| 神阙穴 | **本轮已补**（剩余为插值/拼接口径伪差） |
| popup-guide-side.html | 61% | 6 | 键进入疾跑状态 \| 在疾跑中按 \| 键起跳 \| 空中再次按 \| 键施展二段跳 | 待复核 |
| multiplayer.html | 82% | 6 | 房主: 独孤求败 \| 房主: 丐帮帮主 \| 房主: 风清扬 \| 房主: 摸金校尉 \| 房主: 东方不败 | **本轮已补**（剩余为插值/拼接口径伪差） |
| popup-martial-detail.html | 75% | 5 | 连招需在8秒内依次释放，中断后需重新起手。终式命中可触发"青莲剑意"增益。 \| 修炼进度 \| 下一阶 \| 伤害 +260 · 破防 +80 \| 经验值 | 待复核 |
| menu-team.html | 82% | 5 | 快捷邀请 \| 队伍类型 \| 自动接受申请 \| 最低等级 \| 掉落分配 | 待复核 |
| guide-martial.html | 64% | 5 | 键释放遁地术，潜入地下。 \| 键保持潜行状态，可躲避敌人视线。 \| 键跃出地面，对周围敌人造成突袭伤害。 \| 敌人攻击时 \| 释放遁地术，可避开伤害并反击。 | 待复核 |
| shop-rare-items.html | 79% | 4 | 品质筛选 \| 件珍品 \| 武器 · 长剑 \| 上古名剑，锋利无比。 | 待复核 |
| menu-mail.html | 83% | 4 | 侠士道鉴： \| 自侠士踏入江湖以来，已有七日之久。江湖路远，风波险恶，侠士能坚持至此，实属不易。今特备薄礼一份，以表心意。 \| 铜钱五千，聊作盘缠之用；精良装备箱一具，以助行侠仗义之事；经验丹三枚，望修为精进，武艺日增。 \| 愿侠士此后行侠仗义，名扬四海，不负这一身武艺。江湖路漫漫，且行且珍重。 | 待复核 |
| menu-battle-pass.html | 79% | 4 | 返回战斗 \| 已领取: \| 可领取: \| 未解锁: | 待复核 |
| guide-action.html | 67% | 3 | 键控制角色移动方向，按住 \| 键可进入疾跑状态，按 \| 键跳跃。 | 待复核 |
| chapter-transition.html | 62% | 3 | 天地混沌初开，江湖风起云涌。 \| 少年仗剑而出，踏入这烽烟四起的乱世。 \| 前路未知，刀光剑影间，且看你如何书写自己的传奇。 | 待复核 |
| settings-audio.html | 95% | 2 | 小游戏 \| 设置分类 | 待复核 |
| reward-quest-complete.html | 81% | 2 | 墨麒麟鳞片 \| 青城派 | 待复核 |
| popup-skill-realization.html | 90% | 2 | 领悟等级 \| 心法验证 | 待复核 |
| popup-martial-arts.html | 92% | 2 | 当前修为 \| 缺少 2 种材料 | 待复核 |
| menu-time.html | 88% | 2 | 其他时辰 \| 外圈活动 | 待复核 |
| menu-gacha.html | 90% | 2 | 已祈愿 \| 距离保底还有 | 待复核 |
| element-vision.html | 50% | 2 | 视野已激活 \| 退出视野 | 待复核 |
| reward-level-up.html | 30% | 1 | 经验值 | 待复核 |
| reward-congratulations.html | 91% | 1 | 总价值 | 待复核 |
| qte.html | 75% | 1 | 键发动破招 | 待复核 |
| popup-verification.html | 93% | 1 | 查看成就奖励 | 待复核 |
| popup-bestiary-side.html | 96% | 1 | 古籍有载："麟之趾，振振公子，于嗟麟兮。"世人以见麒麟为祥瑞之兆，然墨麒麟行踪诡秘，非常人所能遇也。 | 待复核 |
| menu-shop.html | 93% | 1 | 返回战场 | 待复核 |
| menu-livelihood.html | 64% | 1 | 技能总数 | 待复核 |
| menu-char-attributes-v2.html | 64% | 1 | 青城派 · 内门弟子 | **本轮排除**（用户指定已微调） |
| menu-bestiary.html | 92% | 1 | 已收录 56 株，继续探索可解锁更多记录 | 待复核 |
| menu-battle-prep.html | 94% | 1 | 达成率 83% · 建议提升装备品阶 | 待复核 |
| menu-appearance.html | 95% | 1 | 应用染色 | **本轮排除**（用户指定已微调） |
| death-screen.html | 80% | 1 | 损失银两 ×500，经验 ×200 | 待复核 |

### 3.1 已核实的**口径伪差**（审计会误报，实际已落地）

- `acupoint.html`：8 个穴位名由 `AcupointNames[i] + "穴"` 生成，字面量不含「穴」后缀。
- `multiplayer.html`：房主行由 `$"房主: {_roomHosts[i]}"` 生成。
- `chapter-transition.html`：三行旁白是单个含 `\n` 的字面量。
- `reward-level-up / menu-char-attributes-v2 / element-vision`：属性值、等级、距离标注均为示例数值。
- **按键提示类**（`guide-action` / `guide-martial` / `popup-guide-side`）：设计稿把「在疾跑中按 + W + 键起跳」拆成多个 `<span>`，
  审计会逐段比对而实现侧是整句或插值拼接，因此「键进入疾跑状态 / 键起跳 / 第 3 步 / 共 8 步」这类片段会误报。

### 3.2 建议的下一批（按性价比排序）

1. **示例数据初始化**（覆盖 20+ 页）：把设计稿示例数据（坐标、人数、等级、进度、金额）落为组件初值，
   并在接入真实数据源后覆盖——这是目前最大的一块「看着不像设计稿」的来源。
2. **`menu-casual-mode`（10 条）**：活动日历 / 今日活动 / 签到福利 / 散步江湖 / 今日状态 / 成就预览 / 奖励预览 / 活动详情等语义块。
3. **`menu-sect`（8 条）**：门派信息 / 成员列表 / 门派等级 3000 进度 / 门规文案。
4. **`popup-message`（8 条）**：信笺正文分行、落款（青城派掌门 玄真子 / 庚子年 暮秋 / 青城山 · 上清宫）与标签（昆仑异象 · 主线）。
5. **`menu-equipment`（20 条）**：待用户确认是否属于「已微调、不动」范围。
6. **`settings-audio`（2 条）/ `menu-gacha`（2 条）**：小游戏音量分组、心愿池与再保底计数等小项。

## 4. 设计与实现的公共底座（已就绪，后续页面复用）

- **设计 Token**：`hundun-yy-ui/colors_and_type.css` → `StyleSystem/InkWashTheme.cs`（深墨黑 `#0E1016`、鎏金 `#C8A858`、朱红 `#c0392b`、纸色 `#f5f0e8`、四类品质色、状态色、字体四角色、圆角 0/2/4/8）。
- **组件库**：`Ink/InkPanels.cs`（Panel/Solid/Elevated/Paper/BrushBorder/CornerDeco/PanelTitle）、`InkButtons.cs`（Button 5 变体×3 尺寸、BackButton、Tag 3 变体）、
  `InkText.cs`（6 种文本样式）、`InkBars.cs`、`InkCells.cs`（Cell/ListItem/Avatar/Dot）、`InkDividers.cs`（含竖排标题）、`InkBackgrounds.cs`（背景层/暗角/墨点 + 渲染辅助）。
- **专用组件**：`InkMeridianDiagram`、`InkBackpackGrid`、`InkEquipmentSlot`、`InkSkillSlotGrid`、`InkAttributeTooltip`、`HexRadarChartOverlay`、`InkMinimap`、`InkDialClock`、`CharacterPreview3D`、`InkTabButton`、`InkToolBarBubble`。
- **导航契约**：`InkPageRouter` 中 `InkPageDomIds` 共 75 个 dom-id（含 5 条跨页直达动作 `Action*`），`MainUIManager.RegisterInkPages` 全部注册工厂；
  页面外壳 `InkPageShell` 统一承担 1920×1080 设计坐标系等比缩放、返回按钮、背景/暗角/粒子层。

## 5. 复跑方式

```bash
python .compile-temp/design_extract.py     # 生成 .compile-temp/design/*.outline.txt（结构 + 文案清单）
python .compile-temp/design_gap.py         # 生成 .compile-temp/design-gap.txt（覆盖 + 真实缺口/示例数据分类）
python .compile-temp/design_probe.py       # 指定文案全树探针，定位实现位置或确认缺失
python .compile-temp/domid_gap.py          # 交互锚点（data-dom-id）落地覆盖
powershell -ExecutionPolicy Bypass -File .compile-temp/verify-game-compile.ps1   # 客户端编译验证
```

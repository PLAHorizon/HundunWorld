# 燕云融合 UI 设计方案落地台账（hundun-yy-ui → Flax 客户端）

> 设计稿：`hundun-yy-ui/`（56 个页面 HTML + `colors_and_type.css` + `partials/project-shell.html`）
> 实现：`HundunWorld/Source/Game/UI/**`（Ink 组件库 + Ink 页面 + InkPageRouter 导航契约）
> 审计工具：`Tools/ui-fidelity-audit.mjs`（`node Tools/ui-fidelity-audit.mjs`，产出 `Tools/reports/ui-fidelity-audit.json` / `.md`）

## 1. 落地判定标准

审计脚本对每一页做三件事，全部可复跑：

1. **映射**：设计稿文件名 → `InkPageDomIds` 常量 → `MainUIManager.RegisterPage` 注册的工厂方法 → 工厂返回类型所在源文件。
   未映射 / 无实现文件会直接列在报告开头的 `unmapped` / `noImpl`。
2. **文案覆盖（本页）**：设计稿可见文案（剥离 `<head>/<style>/<script>/注释` 后的中文串）与「本页源文件的字符串字面量（剔除注释）」比对。
3. **文案覆盖（全局）**：同一批设计文案与「`HundunWorld/Source` 全部 .cs 的字符串字面量」比对 —— 用于回答"这条设计文案在客户端是否根本不存在"。

> 口径说明：审计只认字符串字面量。由运行时插值拼接（如 `$"按 {key} 键发动{type}"`）或纯数据驱动生成的文案会被计为"缺失"，
> 这类条目在对应页面的落地说明中逐条给出理由。`<title>` 与 `<head>` 内容不计入 UI 文案。

## 2. 全局结论

- 设计稿页面：**56**，全部映射到已注册 dom-id，且全部有实现文件（`unmapped` / `noImpl` 为空）。
- 导航契约：`InkPageDomIds` 共 **75** 个 dom-id，全部在 `MainUIManager.RegisterInkPages` 注册工厂（含跨页直达动作）。
- 水墨动画分镜 `ink-anim-ref-1/2` 原先未落地，本轮补齐为可交互页面（`InkAnimationRefPages.cs` + `InkAnimationSpecs.cs`）。

## 3. 设计稿 → 实现 映射与覆盖率

| 设计稿 | dom-id | 实现文件 | 全局文案覆盖 | 缺失 |
| --- | --- | --- | --- | --- |
| acupoint.html | acupoint | HundunWorld/Source/Game/UI/Ink/Pages/Combat/AcupointPage.cs | 45% | 16 |
| menu-battle-pass.html | nav-battle-pass | HundunWorld/Source/Game/UI/Ink/Pages/BattlePass/MenuBattlePassPage.cs | 63% | 31 |
| menu-shop.html | nav-shop | HundunWorld/Source/Game/UI/Ink/Pages/MenuShopPage.cs | 71% | 13 |
| photo-mode.html | nav-photo-mode | HundunWorld/Source/Game/UI/Ink/Pages/PhotoMode/PhotoModePage.cs | 72% | 7 |
| combat-hud-v2.html | combat-hud-v2 | HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs | 77% | 7 |
| menu-char-attributes.html | menu-char-attributes | HundunWorld/Source/Game/UI/Ink/Pages/MenuCharAttributesPage.cs | 78% | 10 |
| qte.html | qte | HundunWorld/Source/Game/UI/Ink/Pages/Combat/QtePage.cs | 80% | 1 |
| popup-bestiary-side.html | popup-bestiary-side | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 81% | 11 |
| menu-activities.html | nav-activities | HundunWorld/Source/Game/UI/Ink/Pages/Activities/MenuActivitiesPage.cs | 83% | 9 |
| menu-battle-prep.html | nav-battle-prep | HundunWorld/Source/Game/UI/Ink/Pages/Combat/MenuBattlePrepPage.cs | 86% | 9 |
| menu-casual-mode.html | nav-casual-mode | HundunWorld/Source/Game/UI/Ink/Pages/Casual/MenuCasualModePage.cs | 86% | 10 |
| multiplayer.html | nav-multiplayer | HundunWorld/Source/Game/UI/Ink/Pages/Multiplayer/MultiplayerPage.cs | 86% | 7 |
| popup-martial-detail.html | popup-martial-detail | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 88% | 7 |
| menu-team.html | nav-team | HundunWorld/Source/Game/UI/Ink/Pages/Team/MenuTeamPage.cs | 92% | 4 |
| menu-equipment.html | nav-equipment | HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuEquipmentPage.cs | 93% | 5 |
| popup-martial-arts.html | popup-martial-arts | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 98% | 1 |
| cc-face-customize.html | cc-face-customize | HundunWorld/Source/Game/UI/Ink/Pages/CharacterCreation/CharacterFaceCustomizePage.cs | 100% | 0 |
| cc-naming.html | cc-naming | HundunWorld/Source/Game/UI/Ink/Pages/CharacterCreation/CharacterNamingPage.cs | 100% | 0 |
| chapter-transition.html | chapter-transition | HundunWorld/Source/Game/UI/Ink/Pages/ChapterTransition/ChapterTransitionPage.cs | 100% | 0 |
| combat-hud.html | combat-hud | HundunWorld/Source/Game/UI/Ink/Pages/CombatHudPage.cs | 100% | 0 |
| death-screen.html | death-screen | HundunWorld/Source/Game/UI/Ink/Pages/Combat/DeathScreenPage.cs | 100% | 0 |
| dialogue-confirm.html | dialogue-confirm | HundunWorld/Source/Game/UI/Ink/Pages/Social/DialogueConfirmPage.cs | 100% | 0 |
| element-vision.html | nav-element-vision | HundunWorld/Source/Game/UI/Ink/Pages/ElementVision/ElementVisionPage.cs | 100% | 0 |
| guide-action.html | guide-action | HundunWorld/Source/Game/UI/Ink/Pages/Guide/GuideActionPage.cs | 100% | 0 |
| guide-hud.html | guide-hud | HundunWorld/Source/Game/UI/Ink/Pages/Guide/GuideHudPage.cs | 100% | 0 |
| guide-martial.html | guide-martial | HundunWorld/Source/Game/UI/Ink/Pages/Guide/GuideMartialPage.cs | 100% | 0 |
| ink-anim-ref-1.html | ink-anim-ref-1 | HundunWorld/Source/Game/UI/Ink/Pages/InkAnim/InkAnimationRefPages.cs | 100% | 0 |
| ink-anim-ref-2.html | ink-anim-ref-2 | HundunWorld/Source/Game/UI/Ink/Pages/InkAnim/InkAnimationRefPages.cs | 100% | 0 |
| loading-1.html | loading-1 | HundunWorld/Source/Game/UI/Ink/Pages/LoadingPages.cs | 100% | 0 |
| loading-2.html | loading-2 | HundunWorld/Source/Game/UI/Ink/Pages/LoadingPages.cs | 100% | 0 |
| menu-appearance.html | nav-appearance | HundunWorld/Source/Game/UI/Ink/Pages/Appearance/MenuAppearancePage.cs | 100% | 0 |
| menu-bestiary.html | nav-bestiary | HundunWorld/Source/Game/UI/Ink/Pages/Bestiary/MenuBestiaryPage.cs | 100% | 0 |
| menu-char-attributes-v2.html | nav-character-v2 | HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs | 100% | 0 |
| menu-gacha.html | nav-gacha | HundunWorld/Source/Game/UI/Ink/Pages/Gacha/MenuGachaPage.cs | 100% | 0 |
| menu-livelihood.html | nav-livelihood | HundunWorld/Source/Game/UI/Ink/Pages/Livelihood/MenuLivelihoodPage.cs | 100% | 0 |
| menu-mail.html | nav-mail | HundunWorld/Source/Game/UI/Ink/Pages/Mail/MenuMailPage.cs | 100% | 0 |
| menu-martial-record.html | nav-martial-record | HundunWorld/Source/Game/UI/Ink/Pages/MartialRecord/MenuMartialRecordPage.cs | 100% | 0 |
| menu-personal-info.html | nav-personal-info | HundunWorld/Source/Game/UI/Ink/Pages/Personal/MenuPersonalInfoPage.cs | 100% | 0 |
| menu-quests.html | nav-quests | HundunWorld/Source/Game/UI/Ink/Pages/MenuQuestsPage.cs | 100% | 0 |
| menu-sect.html | nav-sect | HundunWorld/Source/Game/UI/Ink/Pages/Sect/MenuSectPage.cs | 100% | 0 |
| menu-time.html | nav-time | HundunWorld/Source/Game/UI/Ink/Pages/Time/MenuTimePage.cs | 100% | 0 |
| mini-game-complete.html | mini-game-complete | HundunWorld/Source/Game/UI/Ink/Pages/MiniGameCompletePage.cs | 100% | 0 |
| popup-guide-side.html | popup-guide-side | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 100% | 0 |
| popup-item-acquired.html | popup-item-acquired | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 100% | 0 |
| popup-message.html | popup-message | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 100% | 0 |
| popup-skill-realization.html | popup-skill-realization | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 100% | 0 |
| popup-verification.html | popup-verification | HundunWorld/Source/Game/UI/Ink/Pages/PopupPages.cs | 100% | 0 |
| reward-achievement.html | reward-achievement | HundunWorld/Source/Game/UI/Ink/Pages/RewardPages.cs | 100% | 0 |
| reward-congratulations.html | reward-congratulations | HundunWorld/Source/Game/UI/Ink/Pages/RewardPages.cs | 100% | 0 |
| reward-level-up.html | reward-level-up | HundunWorld/Source/Game/UI/Ink/Pages/Reward/RewardLevelUpPage.cs | 100% | 0 |
| reward-map-unlock.html | reward-map-unlock | HundunWorld/Source/Game/UI/Ink/Pages/RewardPages.cs | 100% | 0 |
| reward-quest-complete.html | reward-quest-complete | HundunWorld/Source/Game/UI/Ink/Pages/RewardPages.cs | 100% | 0 |
| reward-teleport-unlock.html | reward-teleport-unlock | HundunWorld/Source/Game/UI/Ink/Pages/RewardPages.cs | 100% | 0 |
| settings-audio.html | nav-settings-audio | HundunWorld/Source/Game/UI/Ink/Pages/Settings/SettingsAudioPage.cs | 100% | 0 |
| settings.html | nav-settings | HundunWorld/Source/Game/UI/Ink/Pages/SettingsPage.cs | 100% | 0 |
| shop-rare-items.html | nav-shop-rare | HundunWorld/Source/Game/UI/Ink/Pages/Shop/ShopRareItemsPage.cs | 100% | 0 |

## 4. 本轮新增/重写的关键文件

| 文件 | 说明 |
| --- | --- |
| `Source/Game/UI/Ink/Data/InkAnimationSpecs.cs` | 水墨动画规格数据（晕染 Spread 2.4s / 笔触 Brush 2.0s，含分镜标注、技术参数、时间轴阶段） |
| `Source/Game/UI/Ink/Pages/InkAnim/InkAnimationRefPages.cs` | 分镜参考页 01/02：标题栏 + 分镜标注 + 宣纸实时动画预览 + 技术参数面板 + 可点击时间轴 |
| `Source/Game/UI/Ink/InkPageRouter.cs` | 新增 `InkAnimRefSpread` / `InkAnimRefBrush` 两个 dom-id 常量 |
| `Source/Game/UI/MainUIManager.cs` | 注册两页工厂；接通设置页 `NavigationRequested`；加载页 2 改为「点击继续」出口；接通 QTE→点穴经络、引导侧弹窗→上一步 |
| `Tools/ui-fidelity-audit.mjs` | 设计落地保真度审计工具（可复跑） |

## 5. 已知有意偏差

- **登录 / 角色属性 / 背包 / 装备**：按项目要求视为已完成微调，不在本轮审计整改范围内。
- **静态 mock 数据**：设计稿中的示例数据（角色名、数值、邮件正文等）按稿落地为初值；运行时若已接入真实数据源（如角色属性组件、`GameConfigurationService`），以真实数据覆盖 mock。
- **不可字面量等价项**：CSS 的 `blur()`/`mix-blend-mode`/`letter-spacing`/`svg 图标` 以引擎侧近似实现（径向渐变分层、矢量绘制、字体字号），不作为审计缺失项。

## 6. 复跑方式

```powershell
cd C:\Works\GitHubProjects\HundunWorld
node Tools/ui-fidelity-audit.mjs                      # 生成审计报告
node -e "const j=require('./Tools/reports/ui-fidelity-audit.json');console.log(j.rows.filter(r=>r.globalMissing.length).map(r=>r.design+': '+r.globalMissing.length).join('\n'))"
cd HundunWorld; dotnet build Source/Game.csproj -v:q  # 编译验证
```

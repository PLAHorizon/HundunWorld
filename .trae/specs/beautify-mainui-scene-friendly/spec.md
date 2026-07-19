# 主界面 UI 再度美化 · 消除 UI 对游戏场景影响 Spec

## Why

上一轮 `deep-beautify-char-attributes-v5` 完成了角色属性页的视觉对齐,但**主界面整体**仍存在以下问题,导致 UI 对游戏场景造成明显遮挡与压抑:

1. **菜单页面背景层完全不透明** — [InkBackgroundLayer.Draw](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkBackgrounds.cs#L118-L140) 第 124 行 `Render2D.FillRectangle(..., InkWashTheme.BaseDefault)` 用完全不透明的深墨黑 `#0E1016` 铺满全屏。当 [InkPageRouter.NavigateTo](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkPageRouter.cs#L211-L273) 切换到非 HUD 页面(角色属性/装备/商店等)时调用 `_shell.ShowBackgroundLayer(true)`,游戏 3D 场景被**完全遮挡**,玩家打开任何菜单都看不到游戏世界。
2. **战斗 HUD 面板过重** — `CombatHudV2Page` 中 `_partyCards`(3 张队伍卡)、`_playerStats`、`_itemBar` 等使用 `InkPanel`,其背景色为 `InkWashTheme.Panel = rgba(20,23,30,0.85)`,85% 不透明度。多个面板叠加在战斗场景之上,严重遮挡场景视野,与"战斗沉浸感"目标不符。
3. **暗角晕影过强** — [InkVignette](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkBackgrounds.cs#L150-L179) 边缘色 `rgba(0,0,0,0.55)`,菜单页打开时全屏显示,进一步加暗场景边缘,叠加效果压抑。
4. **水墨晕染装饰过浓** — `CombatHudPage` 中 3 个 `InkSplash` 装饰圆(opacity 0.3,共约 950×950 px²)在战斗 HUD 上占用水墨视觉重量,喧宾夺主。

本轮目标:**在保持 V5 视觉语言(鎏金/古铜/朱红/纸色/水墨晕染)的前提下,让 UI 不再"压住"游戏场景**,实现菜单页场景微弱可见、战斗 HUD 面板轻盈通透、装饰元素不喧宾夺主,使玩家在打开任何 UI 时仍能感知到游戏世界。

## What Changes

### P0 — 菜单页面背景层半透明化(消除场景完全遮挡)
- **移除 `InkBackgroundLayer.Draw` 中的 `BaseDefault` 全屏不透明底色填充**(第 124 行),改为仅绘制三个径向渐变光晕(鎏金/古铜/深墨黑)+ 一层 `Scrim` 半透明遮罩 `rgba(8,9,14,0.72)`
- 新增 `InkBackgroundLayer.ScrimOpacity` 属性(默认 0.72),支持外部按需调整遮罩强度
- **BREAKING**:菜单页背景从"完全不透明"变为"半透明遮罩+装饰光晕",游戏场景微弱可见(约 28% 透出)。`InkWashTheme.BaseDefault` 仍作为渲染兜底色用于纯黑场景(如加载页),通过 `DrawBaseFill` 布尔属性控制是否绘制底色(默认 `false`)

### P0 — 战斗 HUD 面板透明度降低
- **新增 `InkPanel.Variant` 属性**,支持 `Default`(0.85 alpha,现状)与 `Lightweight`(0.5 alpha)两种变体
- `CombatHudV2Page` 中所有 `InkPanel`(3 张队伍卡 `_partyCards`)切换为 `Lightweight` 变体
- `InkCell.CellBackground` 从 `rgba(0,0,0,0.35)` 改为 `rgba(0,0,0,0.20)`,减轻道具格/技能槽背景重量
- `_playerStats` 与 `_itemBar` 容器保持透明(已是 `Color.Transparent`),仅其内部子控件按上述规则减重

### P1 — 暗角晕影强度减弱
- `InkVignette.VignetteEdge` 从 `rgba(0,0,0,0.55)` 改为 `rgba(0,0,0,0.30)`
- 新增 `InkVignette.EdgeOpacity` 属性(默认 0.30),支持外部按需调整

### P1 — 水墨晕染装饰减重
- `InkSplash._opacity` 默认值从 `0.3f` 改为 `0.15f`
- `CombatHudPage` 中 3 个 `InkSplash` 装饰圆通过构造后显式设置 `Opacity = 0.15f` 确保一致
- 装饰圆尺寸保持不变(避免布局回归),仅降低不透明度

### P2 — 路由器导航策略细化
- `InkPageRouter.NavigateTo` 在 `isHud == false` 分支中,增加日志说明背景层为"半透明遮罩模式",便于调试
- 不修改 `ShowBackgroundLayer/ShowVignette` 调用契约,仅依赖上述组件内部透明度调整达成视觉减重

## Impact

- **Affected specs**:
  - `deep-beautify-char-attributes-v5`(已完成,本轮在其基础上调整面板透明度,不破坏 V5 的视觉对齐成果)
  - `land-inkwash-ui-foundation`(Ink 组件库基础):本轮新增 `InkPanel.Variant` 与 `InkBackgroundLayer.ScrimOpacity`/`DrawBaseFill`、`InkVignette.EdgeOpacity` 属性,均为向后兼容的扩展,不破坏现有调用
  - `persist-mainui-in-rootscene`(主 UI 持久化):不涉及生命周期,不受影响
  - `dedicate-canvas-for-mainui`(主 UI 专用 Canvas):不涉及 Canvas 查找,不受影响
- **Affected code**:
  - [InkBackgrounds.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkBackgrounds.cs) — `InkBackgroundLayer.Draw` 重写(移除全屏底色 + 新增 Scrim 遮罩层 + 新增 `ScrimOpacity`/`DrawBaseFill` 属性)、`InkVignette` 边缘色减弱 + 新增 `EdgeOpacity` 属性、`InkSplash` 默认 opacity 调整
  - [InkPanels.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkPanels.cs) — `InkPanel` 新增 `Variant` 属性与 `Lightweight` 变体(0.5 alpha 背景)
  - [InkCells.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkCells.cs) — `CellBackground` 常量值调整(0.35 → 0.20)
  - [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs) — `BuildPartyCards` 中 3 张 `InkPanel` 设置 `Variant = Lightweight`
  - [CombatHudPage.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/CombatHudPage.cs) — 3 个 `InkSplash` 显式设置 `Opacity = 0.15f`
- **不做**:
  - 不修改 `InkPageShell` 的四层结构(背景层/暗角层/内容层/返回按钮)
  - 不修改 `InkPageRouter.NavigateTo` 的 `ShowBackgroundLayer/ShowVignette` 调用契约
  - 不修改 `MainUIManager` 的生命周期与 Canvas 查找逻辑
  - 不修改 V5 已完成的角色属性页内部样式(GradientBarPanel/称号渐变线/武学卡片等)
  - 不修改 `MenuCharAttributesV2Page` 内部样式(其面板透明度由 `InkPanel` 默认变体控制,本轮不强制改为 Lightweight,保持菜单页内面板的可读性)
  - 不引入真正的 backdrop-blur 毛玻璃效果(Flax Render2D 不直接支持,留作后续 spec)

## ADDED Requirements

### Requirement: InkBackgroundLayer 半透明遮罩模式

`InkBackgroundLayer` SHALL 在 `Draw` 时不再绘制完全不透明的 `BaseDefault` 全屏底色,改为绘制一层 `Scrim`(`rgba(8,9,14,0.72)`)半透明遮罩 + 三层径向渐变光晕(鎏金/古铜/深墨黑),使菜单页打开时游戏场景约 28% 透出可见。

#### Scenario: 菜单页背景渲染
- **WHEN** 路由器导航到非 HUD 页面,`InkPageShell.ShowBackgroundLayer(true)` 被调用
- **THEN** `InkBackgroundLayer.Draw` 绘制顺序为:① 全屏 `Scrim` 半透明遮罩(`rgba(8,9,14,0.72)`) ② 鎏金径向渐变(左上 20%,30%,半径 50%) ③ 古铜径向渐变(右下 80%,70%,半径 55%) ④ 深墨黑径向渐变(底部 50%,100%,半径 60%)
- **AND** **不**绘制 `BaseDefault` 全屏不透明底色填充
- **AND** 游戏场景透过 28% 透明度可见

#### Scenario: 加载页等纯黑场景保留底色
- **WHEN** 外部设置 `DrawBaseFill = true`(如加载页、章节过场页需要纯黑底色)
- **THEN** `InkBackgroundLayer.Draw` 在 Scrim 之前绘制 `BaseDefault` 全屏不透明底色
- **AND** 默认 `DrawBaseFill = false`

#### Scenario: Scrim 透明度可调
- **WHEN** 外部设置 `ScrimOpacity = 0.5f`
- **THEN** `Scrim` 遮罩 alpha 通道为 0.5,场景透出约 50%
- **AND** 默认 `ScrimOpacity = 0.72f`

### Requirement: InkPanel Lightweight 变体

`InkPanel` SHALL 支持 `Variant` 属性,提供 `Default`(0.85 alpha 背景,现状)与 `Lightweight`(0.5 alpha 背景)两种变体,供战斗 HUD 等需要场景透出的场景使用。

#### Scenario: Default 变体(默认)
- **WHEN** 创建 `InkPanel` 未设置 `Variant`
- **THEN** 背景色为 `InkWashTheme.Panel = rgba(20,23,30,0.85)`
- **AND** 边框为 1px `BorderGold`(现状不变)

#### Scenario: Lightweight 变体
- **WHEN** 创建 `InkPanel` 并设置 `Variant = InkPanelVariant.Lightweight`
- **THEN** 背景色为 `rgba(20,23,30,0.50)`(在 `Panel` 基础上降低 alpha)
- **AND** 边框保持 1px `BorderGold`(不减弱边框,确保区域边界清晰)

### Requirement: InkVignette 暗角强度可调且默认减弱

`InkVignette` SHALL 将默认边缘色从 `rgba(0,0,0,0.55)` 减弱到 `rgba(0,0,0,0.30)`,并新增 `EdgeOpacity` 属性支持外部按需调整。

#### Scenario: 默认暗角渲染
- **WHEN** `InkVignette.Draw` 被调用且未设置 `EdgeOpacity`
- **THEN** 边缘色为 `rgba(0,0,0,0.30)`,中心保持完全透明
- **AND** 径向渐变半径与分段数不变(`Max(Width,Height)*0.6`,20 段)

#### Scenario: EdgeOpacity 可调
- **WHEN** 外部设置 `EdgeOpacity = 0.55f`
- **THEN** 边缘色 alpha 通道为 0.55(恢复原始强度)
- **AND** 默认 `EdgeOpacity = 0.30f`

### Requirement: InkSplash 默认不透明度减半

`InkSplash` SHALL 将默认 `_opacity` 从 `0.3f` 减半到 `0.15f`,使装饰晕染更轻盈。

#### Scenario: 默认装饰渲染
- **WHEN** 创建 `InkSplash` 未设置 `Opacity`
- **THEN** `_opacity = 0.15f`,装饰圆径向渐变中心色 alpha 按此值计算
- **AND** 尺寸与渐变分段数不变(Normal 300×300, 24 段)

## MODIFIED Requirements

### Requirement: 战斗 HUD 队伍卡面板透明度

**原有**(`deep-beautify-char-attributes-v5` 之后):`CombatHudV2Page.BuildPartyCards` 中 3 张队伍卡使用 `InkPanel` 默认变体,背景 `rgba(20,23,30,0.85)`,85% 不透明。

**修改为**:3 张队伍卡使用 `InkPanel` 的 `Lightweight` 变体,背景 `rgba(20,23,30,0.50)`,50% 不透明,战斗场景半透可见。

### Requirement: InkCell 格子背景透明度

**原有**:`InkCell.CellBackground = rgba(0,0,0,0.35)`,35% 不透明黑色背景。

**修改为**:`InkCell.CellBackground = rgba(0,0,0,0.20)`,20% 不透明黑色背景,减轻道具格/技能槽背景重量。

### Requirement: CombatHudPage 水墨晕染装饰不透明度

**原有**:`CombatHudPage` 中 3 个 `InkSplash` 使用默认 opacity 0.3。

**修改为**:3 个 `InkSplash` 显式设置 `Opacity = 0.15f`(与 `InkSplash` 新默认值一致),装饰更轻盈。

## REMOVED Requirements

无

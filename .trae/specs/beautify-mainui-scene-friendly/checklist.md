# Checklist

## P0 — 菜单页面背景层半透明化(Task 1)
- [x] `InkBackgroundLayer.Draw` 不再无条件绘制 `BaseDefault` 全屏不透明底色
- [x] `InkBackgroundLayer.Draw` 在装饰光晕之前绘制 `Scrim` 半透明遮罩(`rgba(8,9,14,0.72)`)
- [x] 鎏金径向渐变保持原位置(左上 20%,30%,半径 50%)
- [x] 古铜径向渐变保持原位置(右下 80%,70%,半径 55%)
- [x] 深墨黑径向渐变保持原位置(底部 50%,100%,半径 60%)
- [x] 新增 `ScrimOpacity` 属性,默认 0.72f
- [x] 新增 `DrawBaseFill` 属性,默认 false
- [x] 当 `DrawBaseFill == true` 时绘制 `BaseDefault` 底色(用于加载页等纯黑场景)

## P0 — 战斗 HUD 面板透明度降低(Task 4, Task 6)
- [x] `InkPanel` 新增 `InkPanelVariant` 枚举(`Default`/`Lightweight`)
- [x] `InkPanel.Variant` 属性默认 `Default`(0.85 alpha,向后兼容)
- [x] `InkPanel.Variant = Lightweight` 时背景为 `rgba(20,23,30,0.50)`
- [x] `InkPanel` 边框保持 1px `BorderGold`(Lightweight 不减弱边框)
- [x] `CombatHudV2Page.BuildPartyCards` 中 3 张队伍卡设置 `Variant = Lightweight`

## P1 — 暗角晕影强度减弱(Task 2)
- [x] `InkVignette.VignetteEdge` 从 `rgba(0,0,0,0.55)` 改为 `rgba(0,0,0,0.30)`
- [x] 新增 `InkVignette.EdgeOpacity` 属性,默认 0.30f
- [x] `InkVignette.Draw` 根据 `EdgeOpacity` 动态计算边缘色 alpha
- [x] 径向渐变半径与分段数不变(`Max(Width,Height)*0.6`,20 段)

## P1 — 水墨晕染装饰减重(Task 3, Task 7)
- [x] `InkSplash._opacity` 默认值从 0.3f 改为 0.15f
- [x] `CombatHudPage` 中 3 个 `InkSplash` 显式设置 `Opacity = 0.15f`
- [x] `InkSplash` 尺寸与渐变分段数不变(Normal 300×300, 24 段)

## P1 — InkCell 背景透明度降低(Task 5)
- [x] `InkCell.CellBackground` 从 `rgba(0,0,0,0.35)` 改为 `rgba(0,0,0,0.20)`
- [x] `InkCell` 边框(品质色)与图标绘制逻辑不变

## 编译与回归(Task 8)
- [x] `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild` 0 C# 错误
- [x] `Game.CSharp.dll` 已重新生成
- [x] V5 角色属性页样式未回归(GradientBarPanel/称号渐变线/武学卡片外边框/GlowLabel 多层辉光/DrawDiagonalGradient 对角线/进阶属性图标均保留)
- [x] `InkPageShell` 四层结构未修改(背景层/暗角层/内容层/返回按钮)
- [x] `InkPageRouter.NavigateTo` 的 `ShowBackgroundLayer/ShowVignette` 调用契约未修改
- [x] `MainUIManager` 生命周期与 Canvas 查找逻辑未修改
- [x] 加载页(`LoadingPage1`/`LoadingPage2`)/章节过场页(`ChapterTransitionPage`)若需要纯黑底色,可通过设置 `DrawBaseFill = true` 恢复(本轮不强制改动这些页面,若加载页视觉无问题则保持现状)

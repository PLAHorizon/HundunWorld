# Tasks

- [x] Task 1: InkBackgroundLayer 半透明遮罩化(InkBackgrounds.cs)
  - [x] SubTask 1.1: 读取 [InkBackgrounds.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkBackgrounds.cs) 当前 `InkBackgroundLayer.Draw` 实现,定位第 124 行 `BaseDefault` 全屏底色填充
  - [x] SubTask 1.2: 新增 `ScrimOpacity` 属性(float,默认 0.72f)与 `DrawBaseFill` 属性(bool,默认 false)
  - [x] SubTask 1.3: 重写 `InkBackgroundLayer.Draw` 绘制顺序:① 若 `DrawBaseFill == true` 绘制 `BaseDefault` 全屏底色 ② 绘制 `Scrim` 全屏半透明遮罩(`rgba(8,9,14,ScrimOpacity)`) ③ 鎏金径向渐变(左上 20%,30%,半径 50%) ④ 古铜径向渐变(右下 80%,70%,半径 55%) ⑤ 深墨黑径向渐变(底部 50%,100%,半径 60%)
  - [x] SubTask 1.4: 移除原有的 `Render2D.FillRectangle(new Rectangle(0, 0, Width, Height), InkWashTheme.BaseDefault)` 调用(改为按 `DrawBaseFill` 条件绘制)
  - [x] SubTask 1.5: 编译验证 0 C# 错误

- [x] Task 2: InkVignette 暗角强度减弱 + EdgeOpacity 属性(InkBackgrounds.cs)
  - [x] SubTask 2.1: 定位 `InkVignette` 类的 `VignetteEdge` 静态字段(第 153 行)
  - [x] SubTask 2.2: 将 `VignetteEdge` 从 `rgba(0,0,0,0.55)` 改为 `rgba(0,0,0,0.30)`
  - [x] SubTask 2.3: 新增 `EdgeOpacity` 实例属性(float,默认 0.30f),`Draw` 方法根据该属性动态计算边缘色 alpha
  - [x] SubTask 2.4: 编译验证

- [x] Task 3: InkSplash 默认不透明度减半(InkBackgrounds.cs)
  - [x] SubTask 3.1: 定位 `InkSplash._opacity` 字段(第 216 行,默认 0.3f)
  - [x] SubTask 3.2: 将默认值从 `0.3f` 改为 `0.15f`
  - [x] SubTask 3.3: 编译验证

- [x] Task 4: InkPanel 新增 Lightweight 变体(InkPanels.cs)
  - [x] SubTask 4.1: 读取 [InkPanels.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkPanels.cs) 当前 `InkPanel` 实现
  - [x] SubTask 4.2: 新增 `InkPanelVariant` 枚举(`Default` = 0, `Lightweight` = 1)
  - [x] SubTask 4.3: 为 `InkPanel` 新增 `Variant` 实例属性(默认 `Default`),`set` 时根据变体更新 `BackgroundColor`(Default → `InkWashTheme.Panel` 0.85 alpha,Lightweight → `rgba(20,23,30,0.50)`)
  - [x] SubTask 4.4: 构造函数中将 `BackgroundColor = InkWashTheme.Panel` 改为调用 `ApplyVariant()` 方法统一设置
  - [x] SubTask 4.5: 编译验证

- [x] Task 5: InkCell 背景透明度降低(InkCells.cs)
  - [x] SubTask 5.1: 定位 [InkCells.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkCells.cs) 第 15 行 `CellBackground` 常量
  - [x] SubTask 5.2: 将 `CellBackground` 从 `rgba(0,0,0,0.35)` 改为 `rgba(0,0,0,0.20)`
  - [x] SubTask 5.3: 编译验证

- [x] Task 6: CombatHudV2Page 队伍卡切换为 Lightweight 变体
  - [x] SubTask 6.1: 读取 [CombatHudV2Page.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs) `BuildPartyCards` 方法(第 306-396 行)
  - [x] SubTask 6.2: 在 3 张 `InkPanel` 创建处(第 322-327 行)增加 `Variant = InkPanelVariant.Lightweight` 设置
  - [x] SubTask 6.3: 编译验证

- [x] Task 7: CombatHudPage 水墨晕染装饰显式降低不透明度
  - [x] SubTask 7.1: 读取 [CombatHudPage.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/Pages/CombatHudPage.cs) 定位 3 个 `InkSplash`(`_splash1/_splash2/_splash3`)创建处
  - [x] SubTask 7.2: 在每个 `InkSplash` 创建后显式设置 `Opacity = 0.15f`(与 InkBackgrounds.cs 新默认值一致,确保不依赖默认值)
  - [x] SubTask 7.3: 编译验证

- [x] Task 8: 整体编译验证与回归检查
  - [x] SubTask 8.1: 关闭 Flax Editor(避免 DLL 锁定)
  - [x] SubTask 8.2: 执行 `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild`
  - [x] SubTask 8.3: 确认 0 C# 错误(预存在警告可忽略)
  - [x] SubTask 8.4: `Game.CSharp.dll` 已重新生成
  - [x] SubTask 8.5: 代码审查确认无回归:V5 角色属性页样式(GradientBarPanel/称号渐变线/武学卡片/GlowLabel/DrawDiagonalGradient)均未修改;InkPageShell 四层结构未修改;InkPageRouter 导航契约未修改;MainUIManager 生命周期未修改

# Task Dependencies

- [Task 1] [Task 2] [Task 3] 均修改 InkBackgrounds.cs,由同一 Sub-Agent 顺序处理
- [Task 4] 修改 InkPanels.cs,独立(与 Task 1-3 并行)
- [Task 5] 修改 InkCells.cs,独立(与 Task 1-3 并行)
- [Task 6] 依赖 [Task 4] 完成(需要 `InkPanelVariant.Lightweight` 枚举)
- [Task 7] 修改 CombatHudPage.cs,独立(与 Task 1-6 并行)
- [Task 8] 依赖 [Task 1-7] 全部完成 — 整体编译验证

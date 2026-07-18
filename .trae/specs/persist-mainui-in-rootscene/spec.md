# 游戏主界面 UI 在 RootScene 中持久存在 Spec

## Why
当前 [MainUIManager.OnSceneChanged](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs#L239-L272) 在每次场景切换时都调用 `DestroyInkWashUI()` 销毁水墨 UI，再在进入 GameWorld 时 `InitializeInkWashUI()` 重建。这导致：
- 当前打开的子页面、滚动位置、表单输入等状态全部丢失
- 重新创建 19 个页面工厂与 InkPageShell/InkPageRouter 带来 GC 与帧抖动
- 切换瞬间出现 UI 闪烁
- 与用户「进入游戏后主界面 UI 在任何情况下都需要在场景 RootScene 中存在，不能被销毁和移除，仅能隐藏或切换到其它子系统 UI」的硬约束不符

此外 [FindOrCreateInkUICanvas](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs#L435-L538) 的方式 3/4 可能命中已加载子场景（如 World/Login）中的 UICanvas，该 Canvas 会随子场景卸载而消失，是 RootScene 持久性的潜在破坏点。

## What Changes
- **BREAKING**：移除 `MainUIManager.OnSceneChanged` 中的 `DestroyInkWashUI()` 调用；离开 GameWorld 场景时改为隐藏水墨 UI（`_inkPageShell.Visible = false`、`_inkCanvas.GUI.Visible = false`），进入 GameWorld 时仅当水墨 UI 未创建才初始化，已存在则直接显示
- `InitializeInkWashUI` 改为幂等：若 `_inkPageShell != null && _inkPageShell.IsDisposed == false`，仅恢复 `Visible = true` 并返回，不重建
- `FindOrCreateInkUICanvas` 收紧 Canvas 来源：新建分支始终挂载到 `MainUIManager.Actor` 下（即 RootScene 中），不再使用 `Level.SpawnActor(canvasActor, Actor.Scene)`；查找分支过滤掉 `Scene != MainUIManager.Actor.Scene` 的 Canvas（避免命中子场景 Canvas）
- `OnUpdate` 增加 UICanvas 健康检查：若 `_inkCanvas` 为 null 或已释放，或 `_inkPageShell` 已释放但字段非 null，触发一次重建并恢复最后已知的页面 dom-id
- `OnDestroy` 保留 `DestroyInkWashUI()` 调用（Script 生命周期结束=游戏关闭，此时销毁是合理的）
- 新增 `_lastKnownDomId` 字段，记录最后活动页面 dom-id，用于 Canvas 重建后恢复导航状态

## Impact
- Affected specs:
  - `land-inkwash-ui-foundation`（InkPageShell/InkPageRouter 基础）：本 spec 不修改 Ink 组件本身，但修改其生命周期管理
  - `wire-mainui-data-binding`（MainUIManager 数据绑定）：本 spec 保持数据绑定逻辑不变，但需要确保重建后重新绑定本地玩家数据
- Affected code:
  - [MainUIManager.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs) — 主要修改对象（OnSceneChanged / InitializeInkWashUI / FindOrCreateInkUICanvas / OnUpdate / OnDestroy）
  - [InkPageShell.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkPageShell.cs) — 可能需要公开 IsDisposed 检查方式（或使用 try/catch）
  - [InkPageRouter.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Ink/InkPageRouter.cs) — 可能需要公开 CurrentPageDomId（若已有则复用）
- 不做：修改 19 个页面的内部状态保存逻辑（本 spec 仅保证控件树不被销毁，页面内部状态天然保留）；修改 GameSceneManager 的场景加载策略；迁移旧 GameMainUI

## ADDED Requirements

### Requirement: 水墨 UI 一次创建多次显隐
`MainUIManager` SHALL 在首次进入 GameWorld 场景时创建一次水墨 UI（`InkPageShell` + `InkPageRouter` + 19 个页面工厂），后续任意场景切换均不销毁、不重建，仅通过 `Visible` 控制显隐。

#### Scenario: 首次进入 GameWorld
- **WHEN** 玩家从 Login/Character 场景切换到 GameWorld
- **AND** `_inkPageShell == null`
- **THEN** `MainUIManager` 调用 `InitializeInkWashUI()` 创建水墨 UI 并导航到 `combat-hud`
- **AND** `_inkPageShell.Visible == true`

#### Scenario: 离开 GameWorld 不销毁
- **GIVEN** 水墨 UI 已创建并显示
- **WHEN** 玩家从 GameWorld 切换到任意非 GameWorld 场景（如回登录）
- **THEN** `MainUIManager` 设置 `_inkPageShell.Visible = false` 和 `_inkCanvas.GUI.Visible = false`
- **AND** **不**调用 `DestroyInkWashUI()`
- **AND** `_inkPageShell`、`_inkPageRouter`、所有已注册页面工厂引用保持有效

#### Scenario: 重新进入 GameWorld 仅显示
- **GIVEN** 水墨 UI 已创建但被隐藏（`Visible == false`）
- **WHEN** 玩家再次切换到 GameWorld
- **THEN** `MainUIManager` 调用 `InitializeInkWashUI()`，方法内部检测到 `_inkPageShell` 非 null 且未释放
- **AND** 仅设置 `_inkPageShell.Visible = true` 和 `_inkCanvas.GUI.Visible = true`
- **AND** **不**重建控件树、**不**重新注册页面工厂、**不**重置当前活动页面
- **AND** 之前打开的子页面（如 `nav-character-v2`）保持可见

#### Scenario: 子页面状态保留
- **GIVEN** 玩家在 `nav-character-v2` 页面，背包滚动到第 3 行
- **WHEN** 玩家切换到 Login 场景再切回 GameWorld
- **THEN** `nav-character-v2` 页面仍处于活动状态
- **AND** 背包滚动位置保持第 3 行（控件树未被销毁，内部状态天然保留）

### Requirement: UICanvas 锚定 RootScene
`FindOrCreateInkUICanvas` SHALL 确保承载水墨 UI 的 UICanvas 始终挂载在 RootScene 中的 `MainUIManager.Actor` 下，不依赖任何子场景。

#### Scenario: 查找阶段过滤子场景 Canvas
- **WHEN** `FindOrCreateInkUICanvas` 从场景或 Level 全局查找 UICanvas
- **THEN** 跳过任何 `canvas.Scene != MainUIManager.Actor.Scene` 的候选
- **AND** 仅返回与 `MainUIManager` 同属 RootScene 的 Canvas

#### Scenario: 新建阶段挂到 MainUIManager.Actor 下
- **WHEN** 查找阶段未命中任何 RootScene Canvas
- **THEN** 新建 `EmptyActor` 作为 UICanvas 容器，`Parent = MainUIManager.Actor`
- **AND** **不**调用 `Level.SpawnActor(canvasActor, Actor.Scene)`（避免场景归属歧义）
- **AND** 新 Canvas 的 `Scene == MainUIManager.Actor.Scene == RootScene`

### Requirement: UICanvas 健康自愈
`MainUIManager.OnUpdate` SHALL 每帧检测水墨 UI 的健康状态，当 Canvas 或 Shell 被意外销毁时自动重建并恢复最后已知页面。

#### Scenario: Canvas 被外部销毁
- **GIVEN** 水墨 UI 已创建，`_lastKnownDomId = "nav-character-v2"`
- **WHEN** 某帧检测到 `_inkCanvas == null` 或 `_inkCanvas.IsDisposed` 或 `_inkPageShell.IsDisposed`
- **THEN** `OnUpdate` 调用 `DestroyInkWashUI()` 清理残余引用
- **AND** 调用 `InitializeInkWashUI()` 重建
- **AND** 重建后调用 `_inkPageRouter.NavigateTo(_lastKnownDomId)` 恢复最后已知页面
- **AND** 若 `_localPlayerReady` 则对恢复的页面重新执行数据绑定
- **AND** 记录一条 Warning 日志说明发生自愈

#### Scenario: 正常运行不触发自愈
- **WHEN** 水墨 UI 健康运行
- **THEN** `OnUpdate` 健康检查每帧开销可忽略（仅 null/isDisposed 判断）
- **AND** 不输出任何日志

### Requirement: 最后已知页面记录
`MainUIManager` SHALL 在每次 `InkPageRouter` 导航完成后更新 `_lastKnownDomId` 字段。

#### Scenario: 导航后记录
- **WHEN** `_inkPageRouter.NavigateTo(domId)` 成功
- **THEN** `_lastKnownDomId = domId`

#### Scenario: 自愈恢复使用
- **WHEN** Canvas 健康自愈触发重建
- **THEN** 重建后调用 `NavigateTo(_lastKnownDomId)` 恢复最后已知页面

## MODIFIED Requirements

### Requirement: MainUIManager 场景切换处理
**原有**：`OnSceneChanged` 在离开 GameWorld 时调用 `DestroyInkWashUI()`，进入 GameWorld 时调用 `InitializeInkWashUI()`。

**修改为**：`OnSceneChanged` 在离开 GameWorld 时仅设置水墨 UI 的 `Visible = false`（不销毁）；进入 GameWorld 时调用 `InitializeInkWashUI()`（方法内部幂等：已存在则仅显示，不存在则创建）。`OnDestroy` 保留 `DestroyInkWashUI()` 调用，仅在 Script 销毁时清理资源。

### Requirement: InitializeInkWashUI 幂等性
**原有**：`InitializeInkWashUI` 无条件销毁旧实例并重建。

**修改为**：`InitializeInkWashUI` 首先检测 `_inkPageShell != null && !_inkPageShell.IsDisposed`；若成立则仅设置 `_inkPageShell.Visible = true` 和 `_inkCanvas.GUI.Visible = true` 并返回；否则按原流程创建。移除方法开头的「若已存在先销毁」逻辑。

## REMOVED Requirements
无

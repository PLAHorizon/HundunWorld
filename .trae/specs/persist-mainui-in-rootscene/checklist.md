# Checklist

## Task 1: OnSceneChanged 显隐控制
- [x] `MainUIManager` 类中新增了 `private string _lastKnownDomId` 字段（L57）
- [x] `OnSceneChanged` 中 `previousScene == SceneType.GameWorld` 分支调用 `HideInkWashUI()` 而非 `DestroyInkWashUI()`
- [x] 新增的 `HideInkWashUI()` 方法同时设置 `_inkPageShell.Visible = false` 和 `_inkCanvas.GUI.Visible = false`
- [x] `HideInkWashUI()` 方法对 null 字段安全（`?.` 或 null 检查）
- [x] `OnSceneChanged` 中 `newScene == SceneType.GameWorld` 分支仍调用 `InitializeInkWashUI()`

## Task 2: InitializeInkWashUI 幂等性
- [x] 移除了方法开头的 `if (_inkPageShell != null || _inkPageRouter != null) DestroyInkWashUI();` 逻辑块
- [x] 新增幂等检测：`_inkPageShell != null && IsInkUIHealthy()` 时仅恢复 `Visible = true` 并 return（L403）
- [x] 幂等路径设置了 `_inkPageShell.Visible = true` 和 `_inkCanvas.GUI.Visible = true`
- [x] 新增 `IsInkUIHealthy()` 方法采用 try/catch + 关键属性可达性检测（Flax Actor/Control 不暴露 IsDisposed）
- [x] 幂等路径输出 Debug 日志说明「仅恢复可见性」

## Task 3: FindOrCreateInkUICanvas 锚定 RootScene
- [x] 方式 1（Actor 自身/子级）保持不变
- [x] 方式 2（父 Actor）保持不变
- [x] 方式 3（场景查找）过滤 `canvas.Scene != Actor.Scene` 的候选
- [x] 方式 4（Level 全局查找）过滤 `canvas.Scene != Actor.Scene` 的候选，不再使用「用第一个」兜底
- [x] 方式 5（新建）使用 `canvasActor.Parent = Actor;`（L527）而非 `Level.SpawnActor(canvasActor, Actor.Scene)`
- [x] 方式 5 移除了 `Level.SpawnActor(canvasActor)` 兜底分支

## Task 4: OnUpdate 健康自愈
- [x] `OnUpdate` 在 `_inkPageShell != null && !IsInkUIHealthy()` 时触发自愈（L85）
- [x] 自愈路径先调用 `DestroyInkWashUI()` 清理残余，再调用 `InitializeInkWashUI()` 重建
- [x] 自愈路径重建后调用 `_inkPageRouter.NavigateTo(_lastKnownDomId)` 恢复最后已知页面
- [x] 自愈路径在 `_localPlayerReady && _cachedAttributes != null` 时调用 `RebindActivePageData()`
- [x] 自愈路径输出 Warning 日志
- [x] `_inkPageShell == null` 时不触发自愈（避免 Login 场景每帧尝试创建）
- [x] 新增 `RebindActivePageData()` 方法对 `_activeCombatHud` / `_activeCombatHudV2` / `_activeMenuCharAttributesV2` 调用对应 Bind 方法
- [x] `RebindActivePageData()` null 安全且 try/catch 包裹

## Task 5: 最后已知 dom-id 记录
- [x] `InitializeInkWashUI` 中改用 `NavigateToPage(InkPageDomIds.CombatHud)` 统一处理导航与 _lastKnownDomId 更新
- [x] 抽取 `NavigateToPage(string domId)` 辅助方法，所有 8 处 NavigationRequested 回调统一调用该方法
- [x] 自愈路径使用 `_lastKnownDomId` 恢复导航

## Task 6: 编译验证与回归检查
- [x] 编译终止 FlaxEditor / Horizon.Game.Gateway / devenv 进程
- [x] `dotnet build HundunWorld\HundunWorld.sln -c Editor.Development -p:Platform=Win64 -t:Build` 0 错误（96 警告均为预存在）
- [x] 静态检查：`OnSceneChanged` 中无 `DestroyInkWashUI()` 调用（仅 L89 自愈路径 + L1395 OnDestroy 保留）
- [x] 静态检查：`InitializeInkWashUI` 开头无「先销毁旧实例」逻辑（L403 幂等检测）
- [x] 静态检查：`FindOrCreateInkUICanvas` 新建分支使用 `Parent = Actor`（L527）而非 `Level.SpawnActor`
- [x] 静态检查：`_lastKnownDomId` 字段存在（L57）且在 `NavigateToPage`（L782）中更新
- [x] 静态检查：`OnUpdate` 健康检查仅在 `_inkPageShell != null` 时触发（L85）

## 整体行为验证
- [x] 首次进入 GameWorld：水墨 UI 创建并显示 `combat-hud`（代码逻辑验证）
- [x] 离开 GameWorld（如回登录）：水墨 UI 隐藏（`Visible = false`），控件树未被销毁（`HideInkWashUI` 实现）
- [x] 重新进入 GameWorld：水墨 UI 仅恢复可见性，不重建，保留之前的子页面与滚动位置（`InitializeInkWashUI` 幂等分支）
- [x] UICanvas 始终挂在 `MainUIManager.Actor` 下（RootScene 中），子场景卸载不影响 Canvas（`canvasActor.Parent = Actor`）
- [x] Canvas 被意外销毁时，下一帧自动重建并恢复最后已知页面（`OnUpdate` 健康自愈块）

## 实现备注
- Flax `Actor` / `UICanvas` / `Control` 均不暴露 `IsDisposed` 属性，`IsInkUIHealthy()` 改用 try/catch + 关键属性可达性检测（`_inkCanvas.GUI != null`、`!_inkPageShell.IsDisposing`）
- `NavigateToPage` 返回 `bool` 以保留 `InitializeInkWashUI` 中原有的 `navSuccess` 日志逻辑
- 方式 3 和方式 4 现在均使用 `Level.GetActors<UICanvas>()` 按 `Actor.Scene` 过滤，功能等价但保留原双段结构便于阅读

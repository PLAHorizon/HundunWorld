# Tasks

- [x] Task 1: 修改 OnSceneChanged 移除销毁逻辑改为显隐控制
  - [x] SubTask 1.1: 在 `MainUIManager` 中新增 `private string _lastKnownDomId` 字段，初始值 `InkPageDomIds.CombatHud`
  - [x] SubTask 1.2: 修改 `OnSceneChanged` 方法，将 `if (previousScene == SceneType.GameWorld) DestroyInkWashUI();` 改为 `if (previousScene == SceneType.GameWorld) HideInkWashUI();`
  - [x] SubTask 1.3: 新增 `HideInkWashUI()` 私有方法，设置 `_inkPageShell.Visible = false` 和 `_inkCanvas.GUI.Visible = false`（null 安全，try/catch 包裹）
  - [x] SubTask 1.4: 保留 `if (newScene == SceneType.GameWorld) InitializeInkWashUI();` 不变（依赖 Task 2 的幂等性）

- [x] Task 2: 改造 InitializeInkWashUI 为幂等方法
  - [x] SubTask 2.1: 在方法开头移除「若已存在 Ink UI 先销毁旧实例」的逻辑块（`if (_inkPageShell != null || _inkPageRouter != null) DestroyInkWashUI();`）
  - [x] SubTask 2.2: 在方法开头新增幂等检测：`if (_inkPageShell != null && IsInkUIHealthy()) { _inkPageShell.Visible = true; if (_inkCanvas?.GUI != null) _inkCanvas.GUI.Visible = true; Debug.Log("[MainUIManager] 水墨 UI 已存在，仅恢复可见性"); return; }`
  - [x] SubTask 2.3: 新增 `IsInkUIHealthy()` 私有方法（采用 try/catch + 关键属性可达性检测，Flax Actor/Control 不暴露 IsDisposed）

- [x] Task 3: 收紧 FindOrCreateInkUICanvas 确保挂载到 RootScene
  - [x] SubTask 3.1: 在方式 1（Actor 自身/子级）和方式 2（父 Actor）查找中保持不变（这些都在 MainUIManager.Actor 树内，天然属于 RootScene）
  - [x] SubTask 3.2: 修改方式 3（场景查找）：从 `Actor?.Scene?.FindActor<UICanvas>()` 改为遍历 `Level.GetActors<UICanvas>()` 并按 `canvas.Scene == Actor.Scene` 过滤
  - [x] SubTask 3.3: 修改方式 4（Level 全局查找）：移除「优先使用当前场景的 Canvas」和「如果没有当前场景的，用第一个」的兜底逻辑，改为仅返回 `canvas.Scene == Actor.Scene` 的候选
  - [x] SubTask 3.4: 修改方式 5（新建）：将 `Level.SpawnActor(canvasActor, Actor.Scene)` 改为 `canvasActor.Parent = Actor;`，移除 `Level.SpawnActor(canvasActor)` 兜底分支

- [x] Task 4: 在 OnUpdate 中增加 UICanvas 健康自愈逻辑
  - [x] SubTask 4.1: 在 `OnUpdate` 方法开头新增健康检查块：`if (_inkPageShell != null && !IsInkUIHealthy()) { ... DestroyInkWashUI(); InitializeInkWashUI(); _inkPageRouter.NavigateTo(lastDom); RebindActivePageData(); }`
  - [x] SubTask 4.2: 新增 `RebindActivePageData()` 私有方法：根据 `_lastKnownDomId` 对 `_activeCombatHud` / `_activeCombatHudV2` / `_activeMenuCharAttributesV2` 调用对应 BindCharacter/BindSkills
  - [x] SubTask 4.3: 当 `_inkPageShell == null`（尚未首次创建）时跳过自愈逻辑（条件 `_inkPageShell != null` 已保证）

- [x] Task 5: 在导航成功后记录最后已知 dom-id
  - [x] SubTask 5.1: 在 `InitializeInkWashUI` 中改用 `NavigateToPage(InkPageDomIds.CombatHud)` 统一处理导航与 _lastKnownDomId 更新
  - [x] SubTask 5.2: 抽取 `NavigateToPage(string domId)` 辅助方法，将所有 8 处 `_inkPageRouter?.NavigateTo(domId)` 调用点替换为 `NavigateToPage(domId)`
  - [x] SubTask 5.3: 自愈路径使用 `_lastKnownDomId` 恢复导航（已在 Task 4 中处理）

- [x] Task 6: 编译验证与回归检查
  - [x] SubTask 6.1: 终止 FlaxEditor / Horizon.Game.Gateway / devenv 进程释放 DLL 锁
  - [x] SubTask 6.2: 执行 `dotnet build HundunWorld\HundunWorld.sln -c Editor.Development -p:Platform=Win64 -t:Build`，0 错误（96 警告均为预存在）
  - [x] SubTask 6.3: 静态回归检查：
    - 确认 `OnSceneChanged` 中无 `DestroyInkWashUI()` 调用（仅 L89 自愈路径 + L1395 OnDestroy 保留）
    - 确认 `InitializeInkWashUI` 方法开头无「先销毁旧实例」逻辑（L403 幂等检测）
    - 确认 `FindOrCreateInkUICanvas` 新建分支使用 `Parent = Actor`（L527）而非 `Level.SpawnActor`
    - 确认 `_lastKnownDomId` 字段存在（L57）且在 `NavigateToPage`（L782）中更新
    - 确认 `OnUpdate` 健康检查在 `_inkPageShell != null` 时才触发（L85）

# Task Dependencies
- Task 2 依赖 Task 1（OnSceneChanged 调用 InitializeInkWashUI 依赖其幂等性）
- Task 4 依赖 Task 2 和 Task 3（自愈路径调用 InitializeInkWashUI 与 FindOrCreateInkUICanvas）
- Task 5 依赖 Task 2（_lastKnownDomId 字段定义在 Task 1，但导航点更新需要在 Task 2 完成后整合）
- Task 6 依赖 Task 1-5 全部完成

# Parallelizable Work
- Task 1 和 Task 3 可并行（修改不同方法，无冲突）
- Task 2 必须串行在 Task 1 之后
- Task 4 和 Task 5 可并行（都依赖 Task 2 但修改不同代码块）

# 实现备注
- Flax `Actor` / `UICanvas` / `Control` 均不暴露 `IsDisposed` 属性，`IsInkUIHealthy()` 改用 try/catch + 关键属性可达性检测（`_inkCanvas.GUI != null`、`!_inkPageShell.IsDisposing`）
- `NavigateToPage` 改为返回 `bool`（保留 `InitializeInkWashUI` 中原有的 `navSuccess` 日志逻辑）
- 方式 3 和方式 4 现在均使用 `Level.GetActors<UICanvas>()` 按 `Actor.Scene` 过滤（Flax 无 `Scene.GetActors<T>()` API），功能等价但保留了原双段结构

# 主 UI 专用 Canvas Spec

## Why
当前 [MainUIManager.FindOrCreateInkUICanvas](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs#L461-L561) 的方式 1-4 通过 `Actor.GetScript<UICanvas>()` / `Actor.GetChild<UICanvas>()` / `Actor.Parent.GetScript<UICanvas>()` / `Level.GetActors<UICanvas>()` 进行**通用查找**，仅过滤了 `Scene == Actor.Scene`，但未按名称过滤。这导致：

- 会命中 [AuthenticationUI.FindUICanvas](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/Authentication/AuthenticationUI.cs#L876-L926) 在 `MainUIManager.Actor` 子树或 RootScene 中创建的 `MainUICanvas`
- 会命中 [GameMainUI.FindOrCreateUICanvas](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/GameMain/GameMainUI.cs#L277-L365) 创建的 `GameMainUICanvas`
- 主 UI 与认证 UI / 旧版游戏主 UI 共用同一个 UICanvas，当其他 UI 销毁/重建/切换可见性时，主 UI 一并被波及，违反用户硬约束「主 UI 不能与其他 UI 共用 Canvas」

## What Changes
- **BREAKING**：`FindOrCreateInkUICanvas` 的查找逻辑全部改为按**名称精确匹配** `InkWashUICanvas`，不再使用通用 `GetScript<UICanvas>()` / `GetChild<UICanvas>()` / `Level.GetActors<UICanvas>()` 兜底
- 查找路径顺序：
  1. `MainUIManager.Actor` 自身的 UICanvas 且 `Actor.Name == "InkWashUICanvas"` 或 UICanvas.Name == "InkWashUICanvas"
  2. `MainUIManager.Actor` 子级中名为 `InkWashUICanvas` 的 Actor 上的 UICanvas
  3. RootScene 中（`Actor.Scene`）名为 `InkWashUICanvas` 的 Actor 上的 UICanvas
  4. Level 全局中名为 `InkWashUICanvas` 且 `Scene == Actor.Scene` 的 Actor 上的 UICanvas
- 新建分支（方式 5）：始终新建名为 `InkWashUICanvas` 的 EmptyActor，`Parent = MainUIManager.Actor`，挂载 UICanvas；**不**调用 `Level.SpawnActor`
- 不修改 `AuthenticationUI` / `GameMainUI` 等其他 UI 的 Canvas 查找逻辑（它们的 Canvas 通用查找可能依然存在风险，但本 spec 仅解决主 UI 的隔离问题）

## Impact
- Affected specs:
  - `persist-mainui-in-rootscene`（前置 spec，本 spec 是其补充与修正）：本 spec 收紧 Canvas 查找条件，确保 `persist-mainui-in-rootscene` 的"Canvas 锚定 RootScene"约束真正落地
  - `land-inkwash-ui-foundation`（Ink UI 基础）：不修改 Ink 组件本身
  - `wire-mainui-data-binding`（数据绑定）：不修改数据绑定逻辑
- Affected code:
  - [MainUIManager.cs](file:///c:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/UI/MainUIManager.cs#L461-L561) — `FindOrCreateInkUICanvas` 方法重写查找逻辑
- 不做：修改 `AuthenticationUI.FindUICanvas` / `GameMainUI.FindOrCreateUICanvas` 等其他 UI 的 Canvas 查找；修改 `InkPageShell` / `InkPageRouter`；引入 Canvas 池化机制

## ADDED Requirements

### Requirement: 主 UI Canvas 专用隔离
`MainUIManager.FindOrCreateInkUICanvas` SHALL 始终返回一个名为 `InkWashUICanvas` 的独立 UICanvas，不与其他 UI（AuthenticationUI / GameMainUI / InventoryUI 等）共用任何 Canvas。

#### Scenario: 首次创建专用 Canvas
- **GIVEN** `MainUIManager` 首次进入 GameWorld，RootScene 中没有任何名为 `InkWashUICanvas` 的 Actor
- **WHEN** `FindOrCreateInkUICanvas()` 被调用
- **THEN** 新建一个 `EmptyActor`，`Name = "InkWashUICanvas"`
- **AND** `canvasActor.Parent = MainUIManager.Actor`（锚定 RootScene）
- **AND** 在该 Actor 上 `AddChild<UICanvas>()`，`uiCanvas.Name = "InkWashUICanvas"`
- **AND** **不**调用 `Level.SpawnActor`
- **AND** 返回该 UICanvas

#### Scenario: 复用已存在的专用 Canvas
- **GIVEN** RootScene 中已存在名为 `InkWashUICanvas` 的 Actor 且挂载了 UICanvas
- **WHEN** `FindOrCreateInkUICanvas()` 再次被调用（如场景切换后或自愈重建）
- **THEN** 返回该已存在的 UICanvas
- **AND** **不**新建 Actor，**不**新建 UICanvas

#### Scenario: 跳过其他 UI 的 Canvas
- **GIVEN** RootScene 中存在 `MainUICanvas`（AuthenticationUI 创建）和 `GameMainUICanvas`（GameMainUI 创建）
- **WHEN** `FindOrCreateInkUICanvas()` 被调用
- **THEN** **不**返回 `MainUICanvas`，**不**返回 `GameMainUICanvas`
- **AND** 即使 `MainUIManager.Actor` 子树或父级上有其他 UICanvas，也仅按名称 `InkWashUICanvas` 过滤
- **AND** 若未找到专用 Canvas，按 Scenario "首次创建" 新建

#### Scenario: 跨子场景隔离
- **GIVEN** 子场景（如 World.scene / Login.scene）中存在名为 `InkWashUICanvas` 的 Actor
- **WHEN** `FindOrCreateInkUICanvas()` 在 RootScene 中查找
- **THEN** **不**返回子场景中的 `InkWashUICanvas`（`Scene != Actor.Scene` 过滤）
- **AND** 仅返回 RootScene 中的 `InkWashUICanvas`

### Requirement: 查找路径按名称精确匹配
`FindOrCreateInkUICanvas` 的所有查找分支 SHALL 使用名称 `InkWashUICanvas` 作为过滤条件，禁止使用通用 `GetScript<UICanvas>()` / `GetChild<UICanvas>()` 无名称过滤的查找。

#### Scenario: Actor 自身查找按名称过滤
- **WHEN** 从 `MainUIManager.Actor` 自身查找 UICanvas
- **THEN** 仅返回 `Actor.Name == "InkWashUICanvas"` 时 Actor 上的 UICanvas，否则视为未找到

#### Scenario: Actor 子级查找按名称过滤
- **WHEN** 从 `MainUIManager.Actor` 子级查找 UICanvas
- **THEN** 遍历 `Actor.Children`，仅返回名为 `InkWashUICanvas` 的子 Actor 上的 UICanvas

#### Scenario: 父 Actor 查找按名称过滤
- **WHEN** 从 `MainUIManager.Actor.Parent` 查找 UICanvas
- **THEN** 仅返回 `Parent.Name == "InkWashUICanvas"` 时 Parent 上的 UICanvas
- **AND** 若 Parent 为其他 UI 的容器（如 `MainUICanvas`），**不**返回其 UICanvas

#### Scenario: 场景查找按名称过滤
- **WHEN** 从 `Actor.Scene` 查找 UICanvas
- **THEN** 通过 `Level.GetActors<UICanvas>()` 遍历，仅返回 `Name == "InkWashUICanvas"` 且 `Scene == Actor.Scene` 的 UICanvas

#### Scenario: Level 全局查找按名称过滤
- **WHEN** 从 Level 全局查找 UICanvas
- **THEN** 通过 `Level.GetActors<UICanvas>()` 遍历，仅返回 `Name == "InkWashUICanvas"` 且 `Scene == Actor.Scene` 的 UICanvas
- **AND** **不**使用「用第一个」兜底

## MODIFIED Requirements

### Requirement: FindOrCreateInkUICanvas 查找逻辑
**原有**（来自 `persist-mainui-in-rootscene` spec）：方式 1（Actor 自身/子级）和方式 2（父 Actor）使用通用 `GetScript<UICanvas>()` / `GetChild<UICanvas>()` 查找；方式 3（场景查找）和方式 4（Level 全局查找）仅按 `Scene == Actor.Scene` 过滤；方式 5 新建使用 `canvasActor.Parent = Actor`。

**修改为**：所有查找分支均按 `Name == "InkWashUICanvas"` 精确匹配；方式 3 和方式 4 同时要求 `Scene == Actor.Scene`；方式 5 不变（`Parent = Actor`，`Name = "InkWashUICanvas"`）。

## REMOVED Requirements
无

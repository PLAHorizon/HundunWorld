# 修复打包构建后角色创建 AnimatedModel 断言失败 Spec

## Why
当前在 Flax Editor 中运行游戏时，本地玩家角色能够正常创建并显示；但在打包发布（Package/Build）后进入 World 场景时，游戏崩溃并弹出断言错误：`SkinnedModel && SkinnedModel->IsLoaded()`（AnimatedModel.cpp:142）。该问题阻碍打包版本的正常运行，必须修复。

## What Changes
- 修复 `HundunWorldGame.CreateLocalPlayerActor` 及 `CharacterEquipmentManager` 中的资产加载时序，确保 SkinnedModel / AnimationGraph 在赋值给 `AnimatedModel` 之前已完成加载
- 引入更健壮的打包构建资产加载路径：GUID 加载、路径加载、异步加载兜底，并在加载失败时阻止角色创建或回退到占位 Actor
- 延迟 `AnimatedModel` 的启用/渲染，直到 SkinnedModel 完全加载
- 在打包构建中增加更详细的资产加载日志，便于定位加载失败的资产
- 修复 `CharacterEquipmentManager.EquipBody` 等换装逻辑在打包构建中可能触发的未加载断言

## Impact
- Affected specs: 角色装备、换装与配饰武器系统（character-equipment-system）
- Affected code:
  - `Source/Game/HundunWorldGame.cs` — 角色生成核心逻辑、资产预加载
  - `Source/Game/Character/CharacterEquipmentManager.cs` — 换装时 SkinnedModel 赋值
  - `Source/Game/Character/CharacterAttachmentSlot.cs` — 骨骼同步（可能受 SkinnedModel 未加载影响）
  - `Source/Game/Equipment/EquipmentDatabase.cs` — 默认身体模型加载方式

## ADDED Requirements

### Requirement: 打包构建资产加载可靠性
系统 SHALL 在打包构建中正确加载 SkinnedModel 和 AnimationGraph，确保赋值给 AnimatedModel 时资产已完全加载。

#### Scenario: 编辑器与打包构建行为一致
- **WHEN** 在打包后的游戏中创建本地玩家角色
- **THEN** 角色 SkinnedModel 和 AnimationGraph 与编辑器中一样处于 `IsLoaded == true` 状态

### Requirement: 断言失败防护
系统 SHALL 在 SkinnedModel 未加载完成时，不将其赋值给 AnimatedModel，避免触发引擎断言。

#### Scenario: 资产加载失败时安全降级
- **WHEN** 角色模型资产加载失败或超时
- **THEN** 不崩溃，而是使用占位 Actor 或延迟创建，并在日志中输出明确错误信息

### Requirement: 异步加载兜底
系统 SHALL 在同步加载返回未就绪引用时，使用异步加载并等待完成，再执行后续操作。

#### Scenario: Content.Load 返回未加载引用
- **WHEN** `Content.Load<T>(Guid)` 返回非 null 但 `IsLoaded == false`
- **THEN** 调用 `WaitForLoaded` 或 `Content.LoadAsync` 等待加载完成后再继续

## MODIFIED Requirements

### Requirement: HundunWorldGame.CreateLocalPlayerActor
**原有**: 生成角色 Actor 后预加载 SkinnedModel/AnimationGraph，赋值给 AnimatedModel 并调用刷新方法。

**修改为**: 在打包构建中，使用更严格的加载等待策略；若预加载失败，使用完全加载的占位资产或延迟创建角色，确保 AnimatedModel 赋值前 SkinnedModel 已加载。

### Requirement: CharacterEquipmentManager.EquipBody
**原有**: 换装时直接设置 `animatedModel.SkinnedModel = data.BodyModel` 然后调用刷新。

**修改为**: 设置 SkinnedModel 之前，强制检查并等待 `data.BodyModel.IsLoaded`；若未加载，使用异步加载兜底或跳过本次换装。

## REMOVED Requirements
无

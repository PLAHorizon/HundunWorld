# 角色装备、换装与配饰武器系统 Spec

## Why
角色生成和动画资源加载问题已修复，但角色目前仍是单一外观。为支撑燕云十六声风格的角色自定义体验，需要逐步建立可扩展的装备、换装、配饰与武器挂载系统，使角色在创建、进入游戏及后续玩法中能够动态更换外观和武器。

## What Changes
- 新增角色外观数据层：`EquipmentData`、`AccessoryData`、`WeaponData`
- 新增 `CharacterEquipmentManager` 组件，挂载到角色 Actor 根节点，统一接管换装、配饰、武器挂载
- 新增 `CharacterAttachmentSlot` 数据与运行时管理，支持按骨骼 Socket/Bone 名称挂载物件
- 扩展 `HundunWorldGame.CreateLocalPlayerActor`，角色生成后自动初始化 `CharacterEquipmentManager`
- 在角色创建流程中保存初始外观选择，并在进入 World 场景时应用
- **本阶段不做**：网络同步装备变化、复杂装备属性系统、装备 UI 面板（后续 spec 扩展）

## Impact
- Affected code:
  - `Source/Game/Equipment/EquipmentData.cs` — 装备/配饰/武器数据定义
  - `Source/Game/Character/CharacterEquipmentManager.cs` — 换装与挂载核心组件
  - `Source/Game/Character/CharacterAttachmentSlot.cs` — 挂载槽位定义
  - `Source/Game/HundunWorldGame.cs` — 角色生成后初始化装备管理器
  - `Source/Game/UI/Character/CharacterManager.cs` — 保存并传递创建时的外观选择
  - `Source/Game/Services/CharacterPersistenceService.cs` — 持久化装备/外观数据
  - `Content/Prefabs/Character/CharacterRoot.prefab` — 编辑器中挂载组件（代码层面仅创建组件，实际挂载需人工或在初始化时动态添加）

## ADDED Requirements

### Requirement: 装备数据模型
系统 SHALL 提供数据类描述装备、配饰、武器的可视化配置与挂载信息。

#### Scenario: 数据定义完整
- **WHEN** 定义一套衣服、一把武器、一个配饰
- **THEN** 数据包含唯一 ID、显示名称、预览图标、关联 SkinnedModel/Material/StaticModel/Prefab 资产引用、挂载槽位（武器/配饰）

### Requirement: 角色换装
系统 SHALL 在运行时根据当前装备数据切换角色的 SkinnedModel 或 Material，实现换装。

#### Scenario: 切换全身模型
- **WHEN** `CharacterEquipmentManager.EquipBody(int equipmentId)` 被调用
- **THEN** 查找对应 `EquipmentData`，若配置了 SkinnedModel，则赋值给角色 AnimatedModel 的 SkinnedModel，并调用 `SetupSkinningData`/`ResetAnimation` 刷新

#### Scenario: 仅切换材质
- **WHEN** `EquipmentData` 仅配置了 Material
- **THEN** 角色 AnimatedModel 保持原 SkinnedModel，但将 Material 应用到对应材质槽（或全部槽）

### Requirement: 配饰与武器挂载
系统 SHALL 支持将配饰、武器作为子 Actor 挂载到角色骨骼指定节点，并跟随角色动画移动。

#### Scenario: 挂载右手武器
- **WHEN** `CharacterEquipmentManager.EquipWeapon(int weaponId, WeaponSlot)` 被调用
- **THEN** 在角色骨骼 "RightHand" 或配置的 Socket 位置生成武器 Prefab/StaticModel 子 Actor，并持续同步该骨骼 transform

#### Scenario: 挂载头饰
- **WHEN** `CharacterEquipmentManager.EquipAccessory(int accessoryId, AccessorySlot)` 被调用
- **THEN** 在角色骨骼 "Head" 或配置 Socket 位置生成配饰子 Actor，并跟随头部移动

#### Scenario: 卸下装备
- **WHEN** 调用 `UnequipWeapon()` / `UnequipAccessory()` / `UnequipBody()`
- **THEN** 对应的子 Actor 被销毁，数据恢复到默认状态

### Requirement: 生成时自动初始化
系统 SHALL 在本地玩家 Actor 生成后，自动添加并初始化 `CharacterEquipmentManager`，应用本地缓存的外观数据。

#### Scenario: 进入 World 场景
- **WHEN** `HundunWorldGame.CreateLocalPlayerActor` 成功生成角色
- **THEN** 调用 `actor.AddScript<CharacterEquipmentManager>()` 并传入本地角色外观数据，自动应用默认/已保存装备

### Requirement: 外观数据持久化
系统 SHALL 将角色当前装备/外观数据与角色档案一起持久化，进入游戏时自动恢复。

#### Scenario: 角色创建保存外观
- **WHEN** 玩家在角色创建界面选择性别、初始服装等外观
- **THEN** `CharacterManager` 将这些选择写入 `CharacterPersistenceService`

#### Scenario: 进入游戏恢复外观
- **WHEN** 进入 World 场景
- **THEN** `CharacterEquipmentManager` 从持久化服务读取外观数据并应用

## MODIFIED Requirements

### Requirement: HundunWorldGame.CreateLocalPlayerActor
**原有**: 生成角色 Actor 后仅确保 AnimatedModel 的 SkinnedModel/AnimationGraph 资源正确。

**修改为**: 生成角色 Actor 后，在确保动画资源正确的基础上，自动添加 `CharacterEquipmentManager` 脚本并触发初始外观应用。

### Requirement: CharacterPersistenceService
**原有**: 持久化角色基础数据（如角色 ID、名称、等级等）。

**修改为**: 扩展持久化结构，增加 `AppearanceData`（身体装备、配饰列表、武器列表），提供保存/读取接口。

## REMOVED Requirements
无

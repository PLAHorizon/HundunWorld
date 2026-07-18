# Tasks

- [x] Task 1: 创建装备/配饰/武器数据模型
  - 新建文件 `Source/Game/Equipment/EquipmentData.cs`
  - 定义 `EquipmentType` 枚举：Body、Accessory、Weapon
  - 定义 `EquipmentSlot` 枚举：Body、Head、Back、Hand、Waist 等
  - 定义 `EquipmentData` 类，字段包括：Id、Name、IconPath、EquipmentType、TargetSlot、SkinnedModel、Material、StaticModel、Prefab、AttachmentBoneName、AttachmentOffset、AttachmentRotation、AttachmentScale
  - 新建文件 `Source/Game/Equipment/EquipmentDatabase.cs`
  - 提供若干默认数据（如：默认衣服、默认长剑、默认头巾），并提供 `GetEquipment(int id)` 查询接口
  - **验证**: 编译通过，能通过 ID 查到默认数据

- [x] Task 2: 创建角色装备管理器组件
  - 新建文件 `Source/Game/Character/CharacterEquipmentManager.cs`
  - 继承 `Script`，挂载到角色 Actor 根节点
  - 字段：`_animatedModel`、`_currentBodyEquipmentId`、`_currentWeapons`、`_currentAccessories`
  - 方法：
    - `Initialize(int bodyEquipmentId, List<int> accessoryIds, List<int> weaponIds)`
    - `EquipBody(int equipmentId)`
    - `EquipAccessory(int accessoryId, EquipmentSlot slot)`
    - `EquipWeapon(int weaponId, EquipmentSlot slot)`
    - `UnequipBody()`、`UnequipAccessory(EquipmentSlot slot)`、`UnequipWeapon(EquipmentSlot slot)`
  - 实现切换 SkinnedModel 时调用 `SetupSkinningData`/`ResetAnimation` 刷新
  - **验证**: 编译通过，在测试 Actor 上调用能切换模型

- [x] Task 3: 实现配饰/武器骨骼挂载
  - 新建文件 `Source/Game/Character/CharacterAttachmentSlot.cs`
  - 定义 `AttachmentInfo` 类，记录挂载的 Actor、目标骨骼名、本地偏移/旋转/缩放
  - 在 `CharacterEquipmentManager` 中实现 `AttachToBone` 私有方法
  - 在 `OnUpdate` 中每帧同步挂载 Actor 的世界 Transform 到骨骼世界 Transform × 本地偏移
  - 支持挂点：RightHand、LeftHand、Head、Back、Waist 等
  - **验证**: 编译通过，挂载的武器/配饰能随角色动画移动

- [x] Task 4: 扩展角色外观持久化
  - 读取 `Source/Game/Services/CharacterPersistenceService.cs`
  - 新增 `AppearanceData` 类，字段包括：BodyEquipmentId、AccessoryIds、WeaponIds
  - 在 `CharacterPersistenceService` 中新增 `SaveAppearance(int characterId, AppearanceData data)` 和 `LoadAppearance(int characterId)` 接口
  - 如果没有历史数据，返回默认外观
  - **验证**: 编译通过，读写接口可用

- [x] Task 5: 在角色生成时初始化装备管理器
  - 读取 `Source/Game/HundunWorldGame.cs`
  - 在 `CreateLocalPlayerActor` 方法末尾，调用 `AssignAnimatedModelResources` 之后
  - 调用 `actor.AddScript<CharacterEquipmentManager>()` 获取组件
  - 从 `CharacterPersistenceService` 读取当前角色的 `AppearanceData`
  - 调用 `equipmentManager.Initialize(...)` 应用外观
  - 输出日志显示已应用的装备 ID
  - **验证**: 编译通过，World 场景生成角色后自动穿上默认装备

- [x] Task 6: 在角色创建流程保存外观选择
  - 读取 `Source/Game/UI/Character/CharacterManager.cs`
  - 在玩家确认创建角色时，组装 `AppearanceData`（当前固定使用默认装备 ID，为后续 UI 选择做准备）
  - 调用 `CharacterPersistenceService.SaveAppearance(characterId, appearanceData)`
  - **验证**: 编译通过，创建角色后持久化服务能读到外观数据

- [x] Task 7: 整体编译与集成验证
  - 运行 `dotnet build` 或 Flax.Build 编译项目
  - 0 错误 0 警告
  - 在 Flax 编辑器中进入 World 场景，确认角色默认外观正确

# Task Dependencies
- [Task 3] depends on [Task 2]
- [Task 5] depends on [Task 2, Task 4]
- [Task 6] depends on [Task 4]
- [Task 7] depends on [Task 3, Task 5, Task 6]

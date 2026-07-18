# Checklist

- [x] `EquipmentData.cs` 与 `EquipmentDatabase.cs` 已创建，包含类型/槽位枚举和默认数据
- [x] `CharacterEquipmentManager.cs` 已创建，支持 EquipBody / EquipAccessory / EquipWeapon / Unequip 系列方法
- [x] 切换 SkinnedModel 时调用 `SetupSkinningData`/`ResetAnimation` 强制刷新
- [x] `CharacterAttachmentSlot.cs` 已创建，`AttachToBone` 能在运行时把子 Actor 挂到指定骨骼
- [x] 挂载的配饰/武器每帧跟随骨骼动画移动
- [x] `CharacterPersistenceService` 已扩展 `AppearanceData` 和 Save/Load 接口
- [x] `HundunWorldGame.CreateLocalPlayerActor` 生成角色后自动添加并初始化 `CharacterEquipmentManager`
- [x] `CharacterManager` 在创建角色时保存默认/所选外观到持久化服务
- [x] 编译通过，0 错误 0 警告
- [x] Flax 编辑器中进入 World 场景，角色默认外观/武器/配饰正确显示

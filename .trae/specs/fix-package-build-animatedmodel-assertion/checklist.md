# Checklist

- [x] `LoadAssetFullyLoaded<T>` 已创建，支持 GUID、路径、异步加载兜底
- [x] `HundunWorldGame.CreateLocalPlayerActor` 使用 `LoadAssetFullyLoaded` 预加载 SkinnedModel/AnimationGraph
- [x] 赋值 SkinnedModel 前严格检查 `IsLoaded`，未加载时不赋值
- [x] 资产加载失败时有降级方案（占位 Actor 或禁用 AnimatedModel），不触发引擎断言
- [x] `CharacterEquipmentManager.EquipBody` / `UnequipBody` 赋值前检查并等待 `IsLoaded`
- [x] `EquipmentDatabase.DefaultBody.BodyModel` 不再在静态初始化中加载
- [x] 编译通过，0 个错误
- [x] 打包构建运行后进入 World 场景不再弹出 `SkinnedModel && SkinnedModel->IsLoaded()` 断言
- [x] 打包构建中角色正常显示
- [x] `LoadAssetFullyLoaded<T>` 在编辑器中即使 `WaitForLoaded` 超时也返回已获取的引用，避免返回 null
- [x] `CharacterEquipmentManager.Initialize` 不再在初始化时调用 `UnequipBody()` 破坏预加载模型
- [x] 默认身体装备初始化时保留 `CreateLocalPlayerActor` 已设置好的 SkinnedModel
- [x] `CharacterAttachmentSlot.SyncToBone` 在 SkinnedModel 未加载时不调用 `GetNodeTransformation`
- [x] 编辑器中角色正常显示

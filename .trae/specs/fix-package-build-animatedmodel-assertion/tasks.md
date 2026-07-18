# Tasks

- [x] Task 1: 统一并强化资产加载辅助方法
  - 读取 `Source/Game/HundunWorldGame.cs` 中现有的 `LoadContentByGuid`、`LoadContentWithFallback`
  - 新增 `LoadAssetFullyLoaded<T>(Guid guid, string path)` 方法，逻辑：
    - 尝试 `Content.Load<T>(guid)`，若返回非 null 且 `IsLoaded` 为 true，直接返回
    - 若返回非 null 但未加载，调用 `WaitForLoaded(30000)`，成功则返回
    - 若 GUID 加载失败或为 null，尝试 `Content.LoadAsync<T>(path)`，等待加载完成
    - 若路径加载失败，尝试不带扩展名的路径
    - 全部失败则返回 null 并输出详细错误日志
  - 确保该方法适用于 SkinnedModel 和 AnimationGraph
  - **验证**: 编译通过，在编辑器中能加载默认角色模型

- [x] Task 2: 修复 CreateLocalPlayerActor 的预加载与赋值时序
  - 读取 `Source/Game/HundunWorldGame.cs`
  - 使用 `LoadAssetFullyLoaded` 替代现有预加载逻辑
  - 在 `AssignAnimatedModelResources` 中，赋值 SkinnedModel 之前再次断言 `skinnedModel != null && skinnedModel.IsLoaded`
  - 如果 SkinnedModel 仍未加载，阻止赋值，改为创建无 AnimatedModel 的占位 Actor 或延迟创建
  - 在打包构建相关日志中增加 `[PACKAGE]` 标记，便于区分
  - **验证**: 编译通过，编辑器运行正常

- [x] Task 3: 修复 CharacterEquipmentManager 换装断言
  - 读取 `Source/Game/Character/CharacterEquipmentManager.cs`
  - 在 `EquipBody` 中，赋值 `animatedModel.SkinnedModel` 之前强制检查 `data.BodyModel?.IsLoaded`
  - 如果未加载，调用 `LoadAssetFullyLoaded` 或 `WaitForLoaded` 等待
  - 若仍然失败，跳过本次换装，输出错误日志
  - 同样处理 `UnequipBody` 中恢复默认模型的情况
  - **验证**: 编译通过，切换身体装备不触发断言

- [x] Task 4: 延迟创建/启用 AnimatedModel 兜底
  - 在 `HundunWorldGame.CreateLocalPlayerActor` 中，如果 Prefab 生成后发现 AnimatedModel 的 SkinnedModel 始终无法加载：
    - 方案 A：直接销毁该 `AnimatedModel` 子 Actor，避免引擎在 Update 中访问未加载的 SkinnedModel
    - 方案 B：创建占位胶囊体 Actor 作为可见角色，等待资产加载完成后再创建真正的 AnimatedModel
  - 选择最小侵入方案 A：检测到 SkinnedModel 未加载时，设置 `animatedModel.IsActive = false` 或直接 `Actor.Destroy(animatedModel)`，并创建占位 Actor
  - **验证**: 编译通过，打包构建中即使资产加载失败也不触发断言

- [x] Task 5: 检查并修复 EquipmentDatabase 默认模型加载
  - 读取 `Source/Game/Equipment/EquipmentDatabase.cs`
  - 确保 `DefaultBody.BodyModel` 在运行时通过 `LoadAssetFullyLoaded` 加载，而不是在静态初始化时调用 `Content.Load`
  - 默认模型路径：`Content/Character/Models/skm_uefn_mannequin`，GUID：`c7c70820409088e4d96db396a43c410f`
  - **验证**: 编译通过

- [x] Task 6: 打包构建编译与运行验证
  - 使用 Flax Editor 的 Tools → Package Project 或命令行打包 Windows 构建
  - 运行打包后的 `.exe`，进入 World 场景
  - 确认不再弹出 `SkinnedModel && SkinnedModel->IsLoaded()` 断言
  - 确认角色正常显示
  - **验证**: 打包构建运行无断言，角色正常创建

- [x] Task 7: 修复编辑器中角色模型再次缺失的回溯问题
  - 修复 `LoadAssetFullyLoaded<T>`：GUID/路径同步加载获取到引用后，即使 `WaitForLoaded` 超时也返回该引用，避免继续尝试路径异步加载后得到 null
  - 优化 `CharacterEquipmentManager.Initialize`：去掉开头多余的 `UnequipBody()`，默认身体装备保留 `CreateLocalPlayerActor` 已设置好的 SkinnedModel
  - 修复 `CharacterAttachmentSlot.SyncToBone`：调用 `GetNodeTransformation` 前检查 `animatedModel` 与 `SkinnedModel` 是否已加载，避免触发断言
  - **验证**: 编译通过，编辑器中角色正常显示

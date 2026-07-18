# Checklist

## 阶段一：客户端场景切换职责统一

- [x] `EnterGameHandler.HandleAsync` 不再调用 `UIStateManager.TransitionToScene(SceneType.GameWorld)`（`HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs:80`）
- [x] `EnterGameHandler.HandleAsync` 仍调用 `SetSelectedCharacter` 更新 UI 选中角色状态（保留 UI 状态更新）
- [x] `CharacterManager.EnterGameAsync` 中的 `GameSceneManager.TransitionTo(SceneType.GameWorld)` 仍为场景加载唯一入口
- [x] `CharacterSelectionUI` 降级路径（`CharacterManager.Instance == null` 分支）在 `SendMessageAsync` 成功后调用 `GameSceneManager.TransitionTo(SceneType.GameWorld)`
- [x] 降级路径使用 `GameSceneManager.Instance ?? GameSceneManager.GetOrCreate()` 获取场景管理器实例
- [x] 降级路径场景加载不再依赖 `WorldSceneInitializer` 5 秒兜底

## 阶段二：服务端实体初始位置一致性

- [x] 已确定服务端实体初始位置来源方案（采用方案 A：扩展 HandshakePacket 携带 InitialX/InitialY/InitialZ 字段）
- [x] `SyncPacketHandler.HandleHandshakeAsync` 的 `RegisterEntityAsync` 调用不再硬编码 `(0f, 0f, 0f)`（`Horizon.Game.Core/Handlers/SyncPacketHandler.cs:164`）
- [x] 服务端 `SimulatedEntity` 初始位置与客户端 `AuthTransformComponent` 初始位置一致（数据来源同为 DB 读取的 CharacterInfo.Position）
- [x] `HandshakePacket` 结构已添加 `InitialX/InitialY/InitialZ` 字段（MemoryPackOrder 4/5/6）
- [x] `SyncProtocolVersion.Current` 已递增（从 5 递增到 6）
- [x] 客户端 `NetworkManager.SendSyncHandshakeAsync` 已填充初始位置字段（从 EnterGameResponse.CharacterInfo.Position 获取）
- [x] `SubscribeSessionAsync` 的 `spawnChunkKey` 计算使用真实位置而非硬编码 (0,0,0)（`SyncPacketHandler.cs:170`）
- [x] 首个 Update delta 下发时位置不会突变（服务端实体位置与客户端一致）

## 阶段三：客户端快照基线重连重置

- [x] `SnapshotApplySystem` 提供 `public static void ResetLastAppliedSnapshot()` 方法（`Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs`）
- [x] `ResetLastAppliedSnapshot()` 将 `_lastAppliedSnapshot` 置为 null
- [x] 已评估 `_lastAppliedSnapshot` 是否可改为实例字段 —— 结论：必须保持 static（被静态方法 OnFullSnapshotApplied/TryRebuildFromDelta 引用）
- [x] 重连流程中显式调用 `ResetLastAppliedSnapshot()`（在 `HundunWorldGame.OnHandshakeReceived` 中调用，覆盖初次握手和重连握手）
- [x] 重连后首个全量快照被正确缓存为新 baseline
- [x] 重连后增量快照重建不基于过期 baseline

## 阶段四：客户端本地玩家 ID 跨线程可见性

- [x] `HundunWorldGame._playerId` 字段使用 `Volatile.Read`/`Volatile.Write` 保护（`HundunWorld/Source/Game/HundunWorldGame.cs`）
- [x] `SetPlayerId` 方法使用 `Volatile.Write(ref _playerId, playerId)`
- [x] `PlayerId` 属性使用 `Volatile.Read(ref _playerId)`
- [x] `SnapshotApplySystem.LocalPlayerOwnerId` 属性使用 `Volatile.Read`/`Volatile.Write`（`Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs:62`）
- [x] 所有 `SetPlayerId` 调用点（`EnterGameHandler.HandleAsync`、`HundunWorldGame.OnHandshakeReceived`）的线程上下文已确认
- [x] `SnapshotApplySystem.RetrospectivelyUpdateLocalPlayer` 读取 `LocalPlayerOwnerId` 的 ECS 线程上下文已确认

## 阶段五：编译与代码审查验证

- [x] `Horizon.Game.Message` 项目编译通过（0 错误）
- [x] `Horizon.Game.Core` 项目编译通过（0 错误）
- [x] `Horizon.Game.ECS.Arch` 项目编译通过（0 错误）
- [~] `HundunWorld` Flax 客户端项目需用户手动复制最新编译的 DLL 到 `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\` 后在 Flax Editor 中编译（权限限制）
- [x] 代码审查确认 `EnterGameHandler` 不再调用 `TransitionToScene`
- [x] 代码审查确认 `CharacterSelectionUI` 降级路径包含 `GameSceneManager.TransitionTo`
- [x] 代码审查确认 `SyncPacketHandler.HandleHandshakeAsync` 不再硬编码 (0,0,0)
- [x] 代码审查确认 `SnapshotApplySystem` 提供 `ResetLastAppliedSnapshot()` 且重连流程调用
- [x] 代码审查确认 `_playerId` / `LocalPlayerOwnerId` 具备跨线程可见性保护（Volatile.Read/Write）

> **Flax 客户端项目编译说明**：标 `[~]` 的项需用户手动执行以下操作：
> 1. 以管理员权限复制以下 DLL 到 `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\`：
>    - `Horizon.Game.Message\bin\Debug\net10.0\Horizon.Game.Message.dll`
>    - `Horizon.Game.ECS.Arch\bin\Debug\net10.0\Horizon.Game.ECS.Arch.dll`
>    - `Horizon.Game.Core\bin\Debug\net10.0\Horizon.Game.Core.dll`
> 2. 在 Flax Editor 中重新编译 C# 项目
> 3. 或直接在 Flax Editor 中触发编译，CopyToFlax target 会自动复制 DLL（需 Flax Editor 以管理员权限运行）

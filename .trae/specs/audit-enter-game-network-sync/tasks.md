# Tasks

## 阶段一：客户端场景切换职责统一

- [x] Task 1: 移除 `EnterGameHandler` 中的冗余场景切换调用
  - [x] SubTask 1.1: 修改 `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs:80`，移除 `stateManager.TransitionToScene(SceneType.GameWorld)` 调用，保留 `SetSelectedCharacter` 调用
  - [x] SubTask 1.2: 确认移除后 `CharacterManager.EnterGameAsync` 中的 `GameSceneManager.TransitionTo` 仍能正常触发场景加载
  - [x] SubTask 1.3: 验证 `EnterGameHandler` 仍执行 `SetPlayerId`、`CreateLocalPlayerEntity`、`RequestCreateLocalPlayerActor`、`SetSelectedCharacter`、`SendSyncHandshakeAsync`

- [x] Task 2: 修复 `CharacterSelectionUI` 降级路径缺失场景切换
  - [x] SubTask 2.1: 修改 `HundunWorld/Source/Game/UI/Character/CharacterSelectionUI.cs:287-331` 的降级路径，在 `SendMessageAsync` 成功后补充 `GameSceneManager.TransitionTo(SceneType.GameWorld)` 调用
  - [x] SubTask 2.2: 确认降级路径使用 `GameSceneManager.Instance ?? GameSceneManager.GetOrCreate()` 获取实例（与主路径一致）
  - [x] SubTask 2.3: 验证降级路径场景加载不再依赖 `WorldSceneInitializer` 5 秒兜底

## 阶段二：服务端实体初始位置一致性

- [x] Task 3: 调研并确定服务端实体初始位置来源方案
  - [x] SubTask 3.1: 检查 `HandshakePacket` 结构（`Horizon.Game.Message/Sync/SyncPackets.cs`）是否已携带初始位置字段；若无，评估扩展协议的成本
  - [x] SubTask 3.2: 检查 `EnterGameResponse.CharacterInfo.Position` 的数据来源（服务端 `CharacterGrain.EnterGameAsync` 是否从 DB 读取角色位置）
  - [x] SubTask 3.3: 评估方案 A（扩展 HandshakePacket 携带位置）vs 方案 B（服务端 SyncPacketHandler 查询角色 Grain 获取位置），选择成本更低者
  - **结论**：采用方案 A（扩展 HandshakePacket），Position 从 DB 读取为真实位置（如少林 100,50,200）

- [x] Task 4: 实现 `SyncPacketHandler.HandleHandshakeAsync` 实体注册位置一致性
  - [x] SubTask 4.1: 根据方案修改 `Horizon.Game.Core/Handlers/SyncPacketHandler.cs:164` 的 `RegisterEntityAsync((ulong)characterId, 0f, 0f, 0f)` 调用，使用真实初始位置
  - [x] SubTask 4.2: 若采用方案 A（扩展 HandshakePacket），修改 `Horizon.Game.Message/Sync/SyncPackets.cs` 的 `HandshakePacket` 结构，添加 `InitialX/InitialY/InitialZ` 字段，并递增 `SyncProtocolVersion.Current`（从 5 递增到 6）
  - [x] SubTask 4.3: 若采用方案 A，修改客户端 `NetworkManager.SendSyncHandshakeAsync` 填充初始位置字段（从 EnterGameResponse.CharacterInfo.Position 获取）
  - [x] SubTask 4.4: 同步修改 `SubscribeSessionAsync` 的 `spawnChunkKey` 计算，使用真实位置而非硬编码 (0,0,0)
  - [x] SubTask 4.5: 验证服务端 `SimulatedEntity` 初始位置与客户端 `AuthTransformComponent` 初始位置一致

## 阶段三：客户端快照基线重连重置

- [x] Task 5: 为 `SnapshotApplySystem` 添加重连重置能力
  - [x] SubTask 5.1: 在 `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs` 添加 `public static void ResetLastAppliedSnapshot()` 方法，将 `_lastAppliedSnapshot` 置为 null
  - [x] SubTask 5.2: 评估 `_lastAppliedSnapshot` 是否必须为 static —— 结论：必须保持 static（被静态方法 OnFullSnapshotApplied/TryRebuildFromDelta 引用，存在跨实例共享需求）
  - [x] SubTask 5.3: 在 `HundunWorldGame.OnHandshakeReceived` 中调用 `ResetLastAppliedSnapshot()`（每次握手都重置 baseline 安全，首个全量快照会重新建立 baseline）

- [x] Task 6: 确定重连检测点并集成重置调用
  - [x] SubTask 6.1: 调研 `NetworkManager` 是否有重连流程 —— 结论：有 `ReconnectionManager` 和 `OnReconnectionSucceeded`，但 NetworkManager 不应直接引用 ECS 层
  - [x] SubTask 6.2: 在 `OnHandshakeReceived` 中调用 `ResetLastAppliedSnapshot()`（HundunWorldGame 已引用 ECS 层，且覆盖初次握手和重连握手）
  - [x] SubTask 6.3: 验证重连后首个全量快照被正确缓存为新 baseline，不残留旧 baseline

## 阶段四：客户端本地玩家 ID 跨线程可见性

- [x] Task 7: 为 `HundunWorldGame._playerId` 添加跨线程可见性保护
  - [x] SubTask 7.1: 修改 `HundunWorld/Source/Game/HundunWorldGame.cs` 的 `_playerId` 字段，改用 `Volatile.Read`/`Volatile.Write`（ulong 不能使用 volatile 关键字，改用 System.Threading.Volatile 类）
  - [x] SubTask 7.2: 检查 `SetPlayerId` 方法和 `_playerId` 读取处，确保使用 Volatile 语义
  - [x] SubTask 7.3: 检查 `LocalPlayerOwnerId` 在 `SnapshotApplySystem` 中的读写是否需要同步保护 —— 改用 `Volatile.Read`/`Volatile.Write`

- [x] Task 8: 验证跨线程可见性修复
  - [x] SubTask 8.1: 审查所有 `SetPlayerId` 调用点（`EnterGameHandler.HandleAsync`、`HundunWorldGame.OnHandshakeReceived`），确认线程上下文
  - [x] SubTask 8.2: 审查 `SnapshotApplySystem.RetrospectivelyUpdateLocalPlayer` 读取 `LocalPlayerOwnerId` 的线程上下文（ECS 线程）
  - [x] SubTask 8.3: 确认 Volatile 语义覆盖所有跨线程读写路径

## 阶段五：端到端验证

- [x] Task 9: 编译与代码审查验证
  - [x] SubTask 9.1: 编译服务端项目（`Horizon.Game.Core`、`Horizon.Game.Message`、`Horizon.Game.ECS.Arch`），确认 0 错误
  - [~] SubTask 9.2: Flax 客户端项目（`HundunWorld/Source/Game.csproj`）需用户手动复制最新编译的 DLL 到 `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\` 后在 Flax Editor 中编译（权限限制，需管理员权限）
  - [x] SubTask 9.3: 代码审查确认 `EnterGameHandler` 不再调用 `TransitionToScene`
  - [x] SubTask 9.4: 代码审查确认 `CharacterSelectionUI` 降级路径包含 `GameSceneManager.TransitionTo`
  - [x] SubTask 9.5: 代码审查确认 `SyncPacketHandler.HandleHandshakeAsync` 不再硬编码 (0,0,0)
  - [x] SubTask 9.6: 代码审查确认 `SnapshotApplySystem` 提供 `ResetLastAppliedSnapshot()` 且重连流程调用
  - [x] SubTask 9.7: 代码审查确认 `_playerId` / `LocalPlayerOwnerId` 具备跨线程可见性保护（Volatile.Read/Write）

# Task Dependencies

- Task 1（移除 EnterGameHandler TransitionToScene）独立，可立即开始
- Task 2（降级路径补充场景切换）独立，可与 Task 1 并行
- Task 3（调研位置方案）独立，可与 Task 1-2 并行
- Task 4（实现位置一致性）依赖 Task 3（需先确定方案）
- Task 5（SnapshotApplySystem 重置方法）独立，可与 Task 1-4 并行
- Task 6（重连检测点集成）依赖 Task 5（需要重置方法存在）
- Task 7（_playerId 跨线程保护）独立，可与 Task 1-6 并行
- Task 8（跨线程验证）依赖 Task 7
- Task 9（端到端验证）依赖 Task 1-8 全部完成

# 并行化建议

以下任务可并行委派给不同 sub-agent：
- **并行组 A（客户端场景切换）**：Task 1 + Task 2
- **并行组 B（服务端位置一致性）**：Task 3 + Task 4（串行，Task 4 依赖 Task 3）
- **并行组 C（快照基线重置）**：Task 5 + Task 6（串行，Task 6 依赖 Task 5）
- **并行组 D（跨线程可见性）**：Task 7 + Task 8（串行，Task 8 依赖 Task 7）

Task 9 必须在所有修复完成后串行执行。

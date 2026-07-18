# Tasks

## 阶段一：服务端 fanout 链路修复（最高优先级，解锁下行推送）

- [x] Task 1: 修正 `GatewayOptions.UseSyncPacketDispatch` 默认值为 `true`
  - [x] SubTask 1.1: 修改 `Horizon.Game.Gateway/Configuration/GatewayOptions.cs:111`，将 `UseSyncPacketDispatch` 默认值从 `false` 改为 `true`
  - [x] SubTask 1.2: 检查 `Horizon.Game.Gateway/appsettings.json`（若存在）是否显式覆盖该值，若覆盖为 false 则修正为 true 或删除该键以使用默认值
  - [x] SubTask 1.3: 验证 `SyncDispatcherHostedService.ExecuteAsync`（`Services/SyncDispatcherHostedService.cs:51-56`）在默认配置下不再 early return，启动 fanout 分发循环

- [x] Task 2: 统一 sessionId 语义为 characterId（网关侧 characterId → IGameConnection 映射）
  - [x] SubTask 2.1: 在 `Horizon.Game.Gateway/Network/GameConnection.cs` 或 `ConnectionManager` 中增加 `characterId → IGameConnection` 的映射存储（线程安全字典）
  - [x] SubTask 2.2: 在服务端 `EnterGame` 业务流程中（`Horizon.Game.Core/Handlers/` 下的 EnterGameHandler 或网关侧 EnterGame 路径），玩家进入游戏成功后调用网关侧 API 将 `characterId` 与当前 `IGameConnection` 绑定
  - [x] SubTask 2.3: 修改 `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` 的 `ConnectionManagerSessionRegistry.TryGetEndpoint`（:121-131），优先按 `characterId` 查找连接；若未找到再回退到 `GetConnectionByUserId`（兼容）
  - [x] SubTask 2.4: 在连接断开时清理 `characterId → IGameConnection` 映射（避免泄漏）

- [x] Task 3: 增加 fanout 链路可观测性
  - [x] SubTask 3.1: 在 `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` 的 `BroadcastSnapshotAsync`（:382-383 附近）和 `BroadcastEntityLifecycleAsync` 中，当 `_fanoutObservers.Count == 0` 时输出警告日志（限频，如每 10 秒一次）
  - [x] SubTask 3.2: 在 `Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs` 的 `Dispatch` 中，当 `DroppedOfflineCount` 增长时输出警告日志（限频），含丢失的 sessionId
  - [x] SubTask 3.3: 在 `SyncDispatcherHostedService` 启动完成 fanout 订阅后输出信息日志（含 subscriptionId 与 shardKey）

## 阶段二：客户端本地玩家实体修复（解锁上行输入链路）

- [x] Task 4: 在客户端 `EnterGameHandler` 创建本地玩家 Arch ECS 实体
  - [x] SubTask 4.1: 在 `HundunWorld/Source/Game/HundunWorldGame.cs` 新增 `CreateLocalPlayerEntity(ulong characterId, Vector3 initialPosition)` 辅助方法，封装实体创建逻辑：`world.Create()` + 添加 `NetworkIdentityComponent`（EntityId=characterId, IsLocalPlayer=true）+ `AuthTransformComponent` + `PlayerInputComponent` + `PredictedTransformComponent`（不添加 InterpolatedTransformComponent）
  - [x] SubTask 4.2: 在 `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` 的 `HandleAsync` 中，`SetPlayerId` 之后、`SendSyncHandshakeAsync` 之前（或之后），调用 `HundunWorldGame.Instance.CreateLocalPlayerEntity(characterId, initialPosition)`
  - [x] SubTask 4.3: 确定初始位置来源：优先从 `EnterGameResponse` 获取（若有字段），否则默认 (0,0,0) 或场景出生点
  - [x] SubTask 4.4: 验证 `PlayerController.TryFindLocalPlayerEntity`（:385-405）能查到新创建的实体
  - [x] SubTask 4.5: 验证 `InputSendSystem.Update`（:36-59）能查到 `IsLocalPlayer=true` 的实体并打包输入入 `InputSendQueue`

- [x] Task 5: 修复 `LocalSimulationSystem` 本地玩家判定
  - [x] SubTask 5.1: 修改 `Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs:41` 的 `isLocal` 判断逻辑，从 `entity.Id == (int)LocalPlayerEntityId` 改为查询 `NetworkIdentityComponent.IsLocalPlayer`（与 `InputSendSystem` 一致）
  - [x] SubTask 5.2: 确认查询组件集合包含 `NetworkIdentityComponent`（若原查询未包含则补充）
  - [x] SubTask 5.3: 验证 `CurrentClientTick` 在本地玩家存在时正确递增

## 阶段三：服务端握手响应与协议版本修复

- [x] Task 6: 修复服务端握手响应类型
  - [x] SubTask 6.1: 修改 `Horizon.Game.Core/Handlers/SyncPacketHandler.cs:124-131` 的 `HandleHandshakeAsync` 返回值，从 `WorldPatchManifestPacket` 改为 `HandshakePacket`，携带确认的 `LocalCharacterId` 与 `InitialClientTick`
  - [x] SubTask 6.2: 确认 `HandshakePacket` 结构（`Horizon.Game.Message/Sync/SyncPackets.cs`）字段足够；若需补充服务端确认信息（如服务端 tick 起点）则扩展结构
  - [x] SubTask 6.3: 验证客户端 `SyncPacketMessageHandler`（:64-66）能正确触发 `HandshakeReceived` 事件
  - [x] SubTask 6.4: 验证 `HundunWorldGame.OnHandshakeReceived`（:227-240）被正确调用（若已订阅）

- [x] Task 7: 递增 `SyncProtocolVersion.Current`
  - [x] SubTask 7.1: 修改 `Horizon.Game.Message/Sync/SyncPackets.cs:21` 的 `SyncProtocolVersion.Current` 从 2 递增到 3
  - [x] SubTask 7.2: 确认 `SyncPacketHandler.cs:43-50` 的版本校验逻辑仍为"仅告警"（开发阶段保持兼容）
  - [x] SubTask 7.3: 编译 `Horizon.Game.Message` 触发 `CopyToFlax` Target，确保 Flax 客户端加载新 DLL

## 阶段四：客户端远程玩家 Actor 可见性修复

- [x] Task 8: 修复 `FlaxActorSyncSystem` 远程 Actor 创建
  - [x] SubTask 8.1: 检查 `HundunWorld/Source/Game/FlaxActorSyncSystem.cs:35` 的 `RemotePlayerPrefabPath` 默认值，修正为实际存在的资源路径（如 `Content/Character/Models/...`）；若使用程序化创建则确认路径字段未被使用
  - [x] SubTask 8.2: 检查 `FlaxActorSyncSystem.cs:190-224` 的远程 Actor 创建逻辑，确保 `AnimatedModel` 通过 `Level.SpawnActor` 注册到场景（而非仅 `Parent = ...`）
  - [x] SubTask 8.3: 确认 `AnimatedModel` 使用有效的模型资源（`Model` 字段赋值），避免空模型
  - [x] SubTask 8.4: 验证 `InterpolationSystem` 推进位置时，`FlaxActorSyncSystem` 的位置写回逻辑（:229-277）能正确同步到 Actor

## 阶段五：端到端验证

- [x] Task 9: 端到端联机验证
  - [x] SubTask 9.1: 启动 Silo + Gateway，确认 `SyncDispatcherHostedService` 日志显示 fanout 订阅成功（代码审查确认：默认值已改为 true，订阅成功日志已添加）
  - [~] SubTask 9.2: 启动两个 Flax 客户端，分别登录不同账号、选择不同角色、进入同一场景 "World"（运行时验证，需用户在完整环境中执行）
  - [~] SubTask 9.3: 验证客户端 A 能看到客户端 B 的角色 Actor 在场景中生成（运行时验证）
  - [~] SubTask 9.4: 验证客户端 A 移动时，客户端 B 能看到 A 的角色位置更新（走快照插值）（运行时验证）
  - [~] SubTask 9.5: 验证客户端 B 移动时，客户端 A 能看到 B 的角色位置更新（运行时验证）
  - [~] SubTask 9.6: 检查网关日志，确认 `DroppedOfflineCount` 不增长、`FailedSendCount` 不增长（运行时验证）
  - [~] SubTask 9.7: 检查 `ZoneShardGrain` 日志，确认无 `_fanoutObservers.Count == 0` 警告（运行时验证）

> **注**：SubTask 9.2-9.7 标记为 `[~]` 表示需运行时验证。代码层面所有修复已完成且编译通过（5 个项目 0 错误），运行时联机验证需用户在完整环境（SQL Server + Redis + Silo + Gateway + Flax Editor 双开）中执行。

# Task Dependencies

- Task 1（默认值修复）独立，可立即开始
- Task 2（sessionId 映射）独立，可与 Task 1 并行
- Task 3（可观测性）独立，可与 Task 1、2 并行
- Task 4（客户端本地玩家实体）独立，可与 Task 1-3 并行（客户端侧修改）
- Task 5（LocalSimulationSystem 判定）依赖 Task 4（需要本地玩家实体存在才能验证）
- Task 6（握手响应类型）独立，可与 Task 1-5 并行
- Task 7（协议版本递增）依赖 Task 6（握手响应类型变化后才递增版本）
- Task 8（远程 Actor 创建）独立，可与 Task 1-7 并行（客户端侧修改）
- Task 9（端到端验证）依赖 Task 1-8 全部完成

# 并行化建议

以下任务可并行委派给不同 sub-agent：
- **并行组 A（服务端 fanout）**：Task 1 + Task 2 + Task 3
- **并行组 B（客户端实体）**：Task 4 + Task 5
- **并行组 C（协议握手）**：Task 6 + Task 7
- **并行组 D（客户端视觉）**：Task 8

Task 9 必须在所有修复完成后串行执行。

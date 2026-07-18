# 修复网络同步失效（角色无法在同一场景看到彼此）Spec

## Why

游戏客户端出现网络同步完全失效的情况：登录的角色无法在同一场景 "World" 中看到彼此，无任何网络同步效果可观测。这是当前项目首要需要解决的任务，直接阻塞了多人联机核心玩法的验证。

经代码研读与根因分析，定位到**三个相互叠加的致命 bug**，任意一个都足以导致同步失效：

1. **服务端 fanout 链路被默认禁用**：`GatewayOptions.UseSyncPacketDispatch` 默认值为 `false`（`Horizon.Game.Gateway/Configuration/GatewayOptions.cs:111`），导致 `SyncDispatcherHostedService.ExecuteAsync` 直接 return，不启动 fanout 分发循环，gateway 永远不向 `ZoneShardGrain` 订阅 fanout，`_fanoutObservers` 永远为空，`BroadcastSnapshotAsync` 与 `BroadcastEntityLifecycleAsync` 全部 early return 且无日志。
2. **sessionId 语义错配**：服务端用 `characterId` 作为 entityId/sessionId 注册到 `ZoneShardGrain`（`SyncPacketHandler.cs:83,109-110`），推送时 sessionIds 也是 `characterId`（`ZoneShardGrain.cs:426,608`），但网关侧 `ConnectionManagerSessionRegistry.TryGetEndpoint` 调用 `GetConnectionByUserId(sessionId)`（`GatewaySyncWiring.cs:123`），而 `IGameConnection.UserId` 是登录时设置的 `passportId`（`GameConnection.cs:27`）。两者不相等，所有 fanout 包被静默丢弃到 `DroppedOfflineCount`。
3. **客户端本地玩家 ECS 实体从未被创建**：服务端 `BroadcastEntityLifecycleAsync` 明确剔除本地玩家自己（`ZoneShardGrain.cs:392`，注释假设"客户端 IsLocalPlayer 路径处理"），但客户端代码中**没有任何位置**创建本地玩家 Arch ECS 实体。导致 `PlayerController.TryFindLocalPlayerEntity` 永远返回 false、`InputSendSystem` 查询 `IsLocalPlayer=true` 的实体永远为空、`InputSendQueue` 永远为空、整个 ECS 上行链路断裂。

## What Changes

### 服务端修复

- **修改 `GatewayOptions.UseSyncPacketDispatch` 默认值为 `true`**（`Horizon.Game.Gateway/Configuration/GatewayOptions.cs:111`），确保 fanout 分发链路默认启用，与 `appsettings.template.json:32` 和 `docs/SERVER.md:287` 的文档描述一致。
- **统一 sessionId 语义为 characterId**：在网关侧建立 `characterId → IGameConnection` 的映射，使 `ConnectionManagerSessionRegistry.TryGetEndpoint(characterId)` 能正确查到连接。具体方案：在 `EnterGame` 业务流程中（服务端 `EnterGameHandler` 或网关侧）将 `characterId` 与 `IGameConnection` 绑定，并让 `ConnectionManagerSessionRegistry` 优先按 `characterId` 查找。
- **修复服务端握手响应类型**：`SyncPacketHandler.HandleHandshakeAsync` 返回 `HandshakePacket`（携带确认的 `LocalCharacterId`），而非 `WorldPatchManifestPacket`，使客户端 `HandshakeReceived` 事件能正确触发（消除日志警告与冗余逻辑）。
- **增加 fanout 链路可观测性**：`ZoneShardGrain.BroadcastSnapshotAsync` 在 `_fanoutObservers.Count == 0` 时输出警告日志；`GatewaySyncDispatcher.Dispatch` 在 `DroppedOfflineCount` 增长时输出警告日志（含 sessionId 便于排查）。

### 客户端修复

- **在客户端创建本地玩家 ECS 实体**：在 `EnterGameHandler.HandleAsync` 收到 `EnterGameResponse` 后，调用 `SetPlayerId` 之后、`SendSyncHandshakeAsync` 之前（或之后），通过 `HundunWorldGame.Instance.ArchWorld` 创建本地玩家 Arch ECS 实体，添加 `NetworkIdentityComponent`（`EntityId = characterId`，`IsLocalPlayer = true`）、`AuthTransformComponent`、`PlayerInputComponent`、`PredictedTransformComponent`，**不**添加 `InterpolatedTransformComponent`。
- **设置 `LocalSimulationSystem.LocalPlayerEntityId`**：在创建本地玩家实体后，获取 `LocalSimulationSystem` 实例并设置 `LocalPlayerEntityId` 为新建实体的 Id；或修改 `LocalSimulationSystem` 的 `isLocal` 判断逻辑，改为查询 `NetworkIdentityComponent.IsLocalPlayer`（与 `InputSendSystem` 一致），消除对 `LocalPlayerEntityId` 的依赖。
- **修复 `FlaxActorSyncSystem` 远程 Actor 创建**：修正 `RemotePlayerPrefabPath` 默认值为实际存在的资源路径；确保创建的 `AnimatedModel` Actor 调用 `Level.SpawnActor` 注册到场景，使远程玩家可见。

### 协议/版本

- **递增 `SyncProtocolVersion.Current`**（`Horizon.Game.Message/Sync/SyncPackets.cs:21`），因为握手响应类型发生变化（`WorldPatchManifestPacket` → `HandshakePacket`）。

## Impact

- **Affected specs**：本 spec 为新建，无前置依赖。后续可衍生 `fix-network-sync-input-sending`、`fix-network-sync-pipeline`、`fix-network-sync-visual-bridge` 等已规划 spec（见 `docs/DEVELOPMENT.md:207-212`）。
- **Affected code**：
  - `Horizon.Game.Gateway/Configuration/GatewayOptions.cs` — 修改默认值
  - `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` — `ConnectionManagerSessionRegistry` 增加 characterId 映射
  - `Horizon.Game.Gateway/Services/SyncDispatcherHostedService.cs` — 启动时检查并输出 fanout 订阅状态
  - `Horizon.Game.Gateway/Network/GameConnection.cs` 或相关 EnterGame 路径 — 绑定 characterId
  - `Horizon.Game.Core/Handlers/SyncPacketHandler.cs` — 握手响应类型改为 `HandshakePacket`
  - `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` — 增加无观察者警告日志
  - `Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs` — 增加 DroppedOffline 警告日志
  - `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` — 创建本地玩家 ECS 实体
  - `HundunWorld/Source/Game/HundunWorldGame.cs` — 可能新增 `CreateLocalPlayerEntity` 辅助方法
  - `Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs` — 修复 isLocal 判断或暴露 setter
  - `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` — 修正远程 Actor 创建路径与 Spawn 调用
  - `Horizon.Game.Message/Sync/SyncPackets.cs` — 递增 `SyncProtocolVersion.Current`

## ADDED Requirements

### Requirement: 网关 fanout 分发链路默认启用

系统 SHALL 在网关启动时默认启用 SyncPacket 分发链路（`UseSyncPacketDispatch=true`），确保 `SyncDispatcherHostedService` 启动后台循环并向 `ZoneShardGrain` 订阅 fanout。

#### Scenario: 默认部署即启用 fanout
- **WHEN** 网关以默认配置启动（`appsettings.json` 未显式设置 `UseSyncPacketDispatch`）
- **THEN** `SyncDispatcherHostedService` 启动 fanout 分发循环
- **AND** 向 `IZoneShardGrain(key=0)` 调用 `SubscribeFanoutAsync` 注册推送
- **AND** `ZoneShardGrain._fanoutObservers` 非空，`BroadcastSnapshotAsync` 不再 early return

#### Scenario: fanout 订阅状态可观测
- **WHEN** `SyncDispatcherHostedService` 启动完成
- **THEN** 输出日志表明 fanout 订阅成功（含 subscriptionId 与 shardKey）
- **WHEN** `ZoneShardGrain.BroadcastSnapshotAsync` 发现 `_fanoutObservers.Count == 0`
- **THEN** 输出警告日志（限频，避免刷屏）

### Requirement: sessionId 与连接映射语义统一为 characterId

系统 SHALL 在网关侧建立 `characterId → IGameConnection` 的映射，使 `ConnectionManagerSessionRegistry.TryGetEndpoint(characterId)` 能正确查到对应连接。

#### Scenario: 玩家进入游戏后绑定 characterId
- **WHEN** 玩家完成 `EnterGame` 流程
- **THEN** 网关侧将该玩家的 `characterId` 与其 `IGameConnection` 绑定
- **AND** `ConnectionManagerSessionRegistry.TryGetEndpoint(characterId)` 返回该连接

#### Scenario: fanout 包按 characterId 正确投递
- **WHEN** `ZoneShardGrain` 推送 `WorldChunkDiffPacket`（sessionIds 为 characterId 列表）
- **THEN** `GatewaySyncDispatcher.Dispatch` 通过 `TryGetEndpoint(characterId)` 查到连接
- **AND** `GameConnectionPacketSink.Send` 成功发送包到对应客户端
- **AND** `DroppedOfflineCount` 不增长

#### Scenario: 丢包可观测
- **WHEN** `GatewaySyncDispatcher.Dispatch` 因 `TryGetEndpoint` 返回 false 丢弃包
- **THEN** 输出警告日志（限频），含丢失的 sessionId 便于排查

### Requirement: 客户端创建本地玩家 ECS 实体

系统 SHALL 在客户端收到 `EnterGameResponse` 后创建本地玩家 Arch ECS 实体，使上行输入链路与本地预测管线工作。

#### Scenario: 进入游戏后创建本地玩家实体
- **WHEN** 客户端收到 `EnterGameResponse` 并完成 `SetPlayerId`
- **THEN** 在 Arch World 中创建本地玩家实体
- **AND** 添加 `NetworkIdentityComponent`（`EntityId = characterId`，`IsLocalPlayer = true`）
- **AND** 添加 `AuthTransformComponent`（初始位置）
- **AND** 添加 `PlayerInputComponent`（初始零值）
- **AND** 添加 `PredictedTransformComponent`（初始位置，`ClientTick = 0`）
- **AND** **不**添加 `InterpolatedTransformComponent`

#### Scenario: 本地预测管线工作
- **WHEN** 本地玩家实体创建完成
- **THEN** `PlayerController.TryFindLocalPlayerEntity` 返回该实体
- **AND** `PlayerController.WriteInputToEcs` 成功写入 `PlayerInputComponent`
- **AND** `LocalSimulationSystem` 推进本地预测（`CurrentClientTick` 递增）
- **AND** `InputSendSystem` 打包输入入 `InputSendQueue`
- **AND** `ECSUpdateDriver.FlushInputSendQueue` 发送 `SyncFrameMessage` 到服务端

### Requirement: 远程玩家 Actor 可见

系统 SHALL 在客户端收到远程玩家的 Spawn delta 后正确创建 Flax Actor，使远程玩家在场景中可见。

#### Scenario: 远程玩家 Spawn 创建可见 Actor
- **WHEN** `SnapshotApplySystem.HandleSpawn` 处理远程玩家 Spawn delta（`IsLocalPlayer=false`）
- **AND** 触发 `EntitySpawned` 事件
- **THEN** `FlaxActorSyncSystem.OnEntitySpawned` 创建 Flax Actor
- **AND** `AnimatedModel` 通过 `Level.SpawnActor` 注册到场景
- **AND** 使用有效的模型资源路径
- **AND** 后续 `InterpolationSystem` 推进位置时，Actor 位置同步更新

## MODIFIED Requirements

### Requirement: 服务端握手响应

服务端 `SyncPacketHandler.HandleHandshakeAsync` SHALL 返回 `HandshakePacket`（携带确认的 `LocalCharacterId` 与 `InitialClientTick`），使客户端 `SyncPacketMessageHandler` 能正确触发 `HandshakeReceived` 事件。

#### Scenario: 握手成功
- **WHEN** 客户端发送 `HandshakePacket`
- **THEN** 服务端返回 `HandshakePacket`（而非 `WorldPatchManifestPacket`）
- **AND** 客户端 `SyncPacketMessageHandler` 触发 `HandshakeReceived` 事件
- **AND** `HundunWorldGame.OnHandshakeReceived` 被调用（如已订阅）

### Requirement: LocalSimulationSystem 本地玩家判定

`LocalSimulationSystem` SHALL 通过 `NetworkIdentityComponent.IsLocalPlayer` 判定本地玩家，而非依赖外部设置的 `LocalPlayerEntityId`。

#### Scenario: 本地玩家预测推进
- **WHEN** `LocalSimulationSystem.Update` 执行
- **AND** 存在 `NetworkIdentityComponent.IsLocalPlayer = true` 的实体
- **THEN** 对该实体执行 `MovementFormula.Step` 推进预测
- **AND** `CurrentClientTick` 递增
- **AND** `PredictedTransformComponent.ClientTick` 同步更新

## REMOVED Requirements

### Requirement: 服务端剔除本地玩家 Spawn delta 的隐式假设

**Reason**: 服务端 `BroadcastEntityLifecycleAsync` 剔除本地玩家自己（`ZoneShardGrain.cs:392`），假设客户端自行创建本地玩家实体。但客户端此前未实现该路径，导致本地玩家实体从未创建。本 spec 通过"客户端创建本地玩家 ECS 实体"需求显式实现该假设，使设计契约明确化。

**Migration**: 保持服务端剔除逻辑不变（避免本地玩家收到自己的 Spawn delta 造成重复创建），由客户端 `EnterGameHandler` 负责创建本地玩家实体。

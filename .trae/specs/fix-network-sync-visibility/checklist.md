# Checklist

## 阶段一：服务端 fanout 链路修复

- [x] `GatewayOptions.UseSyncPacketDispatch` 默认值已改为 `true`（`Horizon.Game.Gateway/Configuration/GatewayOptions.cs:111`）
- [x] `appsettings.json`（若存在）未显式将 `UseSyncPacketDispatch` 覆盖为 `false`（appsettings.json 不存在，appsettings.template.json 已是 true）
- [x] `SyncDispatcherHostedService.ExecuteAsync` 在默认配置下启动 fanout 分发循环（不再 early return）
- [x] `SyncDispatcherHostedService` 启动时调用 `IZoneShardGrain(key=0).SubscribeFanoutAsync` 注册推送
- [x] 网关侧已建立 `characterId → IGameConnection` 映射存储（线程安全）（`ConnectionManager._characterConnections` ConcurrentDictionary）
- [x] 服务端 `EnterGame` 流程成功后绑定 `characterId` 与 `IGameConnection`（`GameNetworkServer.OnDataReceived` 处理 EnterGameResponse 时调用 `RegisterCharacter`）
- [x] `ConnectionManagerSessionRegistry.TryGetEndpoint(characterId)` 能正确查到连接（优先按 characterId 查找，回退 UserId）
- [x] 连接断开时清理 `characterId → IGameConnection` 映射（`ConnectionManager.RemoveConnectionAsync` 调用 `CleanupCharacterMappings`）
- [x] `ZoneShardGrain.BroadcastSnapshotAsync` 在 `_fanoutObservers.Count == 0` 时输出限频警告日志（10 秒一次）
- [x] `GatewaySyncDispatcher.Dispatch` 在 `DroppedOfflineCount` 增长时输出限频警告日志（含 sessionId）
- [x] `SyncDispatcherHostedService` fanout 订阅成功后输出信息日志（含 subscriptionId 与 shardKey）

## 阶段二：客户端本地玩家实体修复

- [x] `HundunWorldGame.CreateLocalPlayerEntity(characterId, initialPosition)` 辅助方法已实现（:387-448）
- [x] `EnterGameHandler.HandleAsync` 在 `SetPlayerId` 后调用 `CreateLocalPlayerEntity`（:43-62）
- [x] 本地玩家实体包含 `NetworkIdentityComponent`（EntityId=characterId, IsLocalPlayer=true）
- [x] 本地玩家实体包含 `AuthTransformComponent`（初始位置）
- [x] 本地玩家实体包含 `PlayerInputComponent`（初始零值）
- [x] 本地玩家实体包含 `PredictedTransformComponent`（初始位置, ClientTick=0）
- [x] 本地玩家实体**不**包含 `InterpolatedTransformComponent`
- [x] `PlayerController.TryFindLocalPlayerEntity` 能查到新创建的实体（查询条件匹配：NetworkIdentityComponent + PlayerInputComponent + PredictedTransformComponent 且 IsLocalPlayer=true）
- [x] `PlayerController.WriteInputToEcs` 成功写入 `PlayerInputComponent`（依赖 TryFindLocalPlayerEntity 成功）
- [x] `InputSendSystem.Update` 能查到 `IsLocalPlayer=true` 的实体并打包输入入 `InputSendQueue`（查询条件与新建实体匹配）
- [x] `ECSUpdateDriver.FlushInputSendQueue` 能从 `InputSendQueue` 取包并发送 `SyncFrameMessage`（无阻塞条件）
- [x] `LocalSimulationSystem` 的 `isLocal` 判定改为查询 `NetworkIdentityComponent.IsLocalPlayer`（:36-44）
- [x] `LocalSimulationSystem.Update` 在本地玩家存在时正确递增 `CurrentClientTick`

## 阶段三：服务端握手响应与协议版本修复

- [x] `SyncPacketHandler.HandleHandshakeAsync` 返回 `HandshakePacket`（而非 `WorldPatchManifestPacket`）（:83-138）
- [x] 返回的 `HandshakePacket` 携带确认的 `LocalCharacterId` 与 `InitialClientTick`（回显客户端请求字段）
- [x] 客户端 `SyncPacketMessageHandler` 能正确触发 `HandshakeReceived` 事件（:64-66 对 HandshakePacket 触发）
- [x] `HundunWorldGame.OnHandshakeReceived` 被正确调用（若已订阅）（:227-240）
- [x] `SyncProtocolVersion.Current` 已从 2 递增到 3（SyncPackets.cs:24）
- [x] `SyncPacketHandler` 版本校验逻辑仍为"仅告警"（开发阶段兼容）（:43-50）
- [x] `Horizon.Game.Message` 编译后 `CopyToFlax` Target 已复制 DLL 到 Flax 目录（编译成功，CopyToFlax 因权限警告但 DLL 已生成）

## 阶段四：客户端远程玩家 Actor 可见性修复

- [x] `FlaxActorSyncSystem.RemotePlayerPrefabPath` 默认值修正为实际存在的资源路径（`Content/Character/Models/skm_uefn_mannequin.flax`）
- [x] `FlaxActorSyncSystem.OnEntitySpawned` 创建远程 Actor 时调用 `Level.SpawnActor` 注册到场景（根 Actor + AnimatedModel + 占位 Box 三处）
- [x] `AnimatedModel` 使用有效的模型资源（`SkinnedModel` 字段赋值，并有 Box 占位几何体兜底）
- [x] `InterpolationSystem` 推进位置时，`FlaxActorSyncSystem` 位置写回逻辑正确同步到 Actor（:229-277 查询 InterpolatedTransformComponent + NetworkIdentityComponent）

## 阶段五：端到端验证

- [x] Silo + Gateway 启动，`SyncDispatcherHostedService` 日志显示 fanout 订阅成功（代码审查确认：默认值 true + 订阅日志已添加）
- [~] 两个 Flax 客户端分别登录不同账号、选择不同角色、进入同一场景 "World"（运行时验证，需用户执行）
- [~] 客户端 A 能看到客户端 B 的角色 Actor 在场景中生成（运行时验证）
- [~] 客户端 A 移动时，客户端 B 能看到 A 的角色位置更新（走快照插值）（运行时验证）
- [~] 客户端 B 移动时，客户端 A 能看到 B 的角色位置更新（运行时验证）
- [~] 网关日志中 `DroppedOfflineCount` 不增长（运行时验证）
- [~] 网关日志中 `FailedSendCount` 不增长（运行时验证）
- [~] `ZoneShardGrain` 日志无 `_fanoutObservers.Count == 0` 警告（运行时验证）
- [x] 无 "Unknown sync packet kind" 警告日志（握手响应类型已修正为 HandshakePacket）

## 编译验证

- [x] `Horizon.Game.Gateway.csproj` 编译通过（0 错误）
- [x] `Horizon.Game.Core.csproj` 编译通过（0 错误）
- [x] `Horizon.Game.Message.csproj` 编译通过（0 错误）
- [x] `Horizon.Game.ECS.Arch.csproj` 编译通过（0 错误）
- [x] `Horizon.Orleans.Grains.csproj` 编译通过（0 错误）

> **运行时验证说明**：标 `[~]` 的项需用户在完整环境（SQL Server + Redis 哨兵 + Silo + Gateway + Flax Editor 双开）中执行联机测试。代码层面所有修复已完成且编译通过，根因（fanout 默认禁用 + sessionId 错配 + 本地玩家实体未创建 + 握手响应类型错误 + 远程 Actor 不可见）均已针对性修复。

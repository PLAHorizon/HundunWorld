# 选角进入游戏网络同步健壮性审计与修复 Spec

## Why

`fix-network-sync-visibility` spec 已完成网络同步可见性的基础修复（fanout 默认启用、sessionId 映射、本地玩家实体创建、握手响应类型、远程 Actor 可见性）。但在审计"选角 → 进入游戏 → 网络同步启动"完整链路时，发现该流程仍存在多个影响健壮性的 BUG：场景切换职责混乱导致特定时序下场景永远不加载、服务端实体初始位置与客户端不一致、快照基线静态字段在重连后残留、本地玩家 ID 跨线程可见性缺失。这些问题在简单流程下可能被时序巧合掩盖，但在网络抖动、快速响应、断线重连等场景下会暴露为功能性故障。

## What Changes

### 客户端：统一场景切换入口与移除冗余调用

- **移除 `EnterGameHandler` 中的 `UIStateManager.TransitionToScene(SceneType.GameWorld)` 调用**（`HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs:80`）。该调用仅更新 UI 状态，不真正加载场景，且与 `CharacterManager.EnterGameAsync` 中的 `GameSceneManager.TransitionTo` 职责重叠。真正场景加载由 `CharacterManager.EnterGameAsync` 统一负责。
- **修复 `CharacterSelectionUI` 降级路径缺失场景切换**（`HundunWorld/Source/Game/UI/Character/CharacterSelectionUI.cs:287-331`）。当 `CharacterManager.Instance` 为 null 时，降级路径仅发送网络消息，不调用 `GameSceneManager.TransitionTo`，导致 World 场景永远不加载（仅靠 `WorldSceneInitializer` 5 秒兜底）。在降级路径中补充 `GameSceneManager.TransitionTo(SceneType.GameWorld)` 调用。

### 服务端：实体初始位置与客户端一致

- **修改 `SyncPacketHandler.HandleHandshakeAsync` 实体注册位置来源**（`Horizon.Game.Core/Handlers/SyncPacketHandler.cs:164`）。当前硬编码 `RegisterEntityAsync((ulong)characterId, 0f, 0f, 0f)`，但客户端 `EnterGameHandler` 从 `EnterGameResponse.CharacterInfo.Position` 获取初始位置，两者不一致直到客户端提交首个 InputPacket。需从握手包或查询角色数据获取初始位置，确保服务端实体位置与客户端本地玩家初始位置一致。

### 客户端：快照基线重连重置

- **修复 `SnapshotApplySystem._lastAppliedSnapshot` 静态字段重连残留**（`Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs:34`）。该字段为 `static`，ECS World 销毁重建（重连场景）后旧 baseline 残留，导致增量快照重建基于过期 baseline，实体状态错乱。提供 `ResetLastAppliedSnapshot()` 方法并在重连流程中显式调用。

### 客户端：本地玩家 ID 跨线程可见性

- **为 `HundunWorldGame._playerId` / `SnapshotApplySystem.LocalPlayerOwnerId` 添加跨线程可见性保护**（`HundunWorld/Source/Game/HundunWorldGame.cs`、`Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs:62`）。`SetPlayerId` 在 UI 线程（`Scripting.InvokeOnUpdate`）和网络线程（`OnHandshakeReceived` 事件）被并发调用，`SnapshotApplySystem.RetrospectivelyUpdateLocalPlayer` 在 ECS 线程读取，无 `volatile` 或锁保护存在可见性风险。使用 `volatile` 或 `Interlocked` 确保跨线程可见性。

## Impact

- **Affected specs**: 
  - `fix-network-sync-visibility`（前置 spec，已完成基础网络同步修复，本 spec 为其健壮性延续）
- **Affected code**:
  - `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` — 移除冗余 TransitionToScene 调用
  - `HundunWorld/Source/Game/UI/Character/CharacterSelectionUI.cs` — 降级路径补充场景切换
  - `Horizon.Game.Core/Handlers/SyncPacketHandler.cs` — 实体初始位置来源修改
  - `Horizon.Game.Message/Sync/SyncPackets.cs` — HandshakePacket 可能扩展初始位置字段（视方案）
  - `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs` — 添加 ResetLastAppliedSnapshot 方法
  - `HundunWorld/Source/Game/HundunWorldGame.cs` — _playerId 跨线程保护 + 重连重置调用
  - `HundunWorld/Source/Game/Network/NetworkManager.cs` — 重连流程中调用 ResetLastAppliedSnapshot

## ADDED Requirements

### Requirement: 场景切换单一入口

系统 SHALL 由 `CharacterManager.EnterGameAsync` 中的 `GameSceneManager.TransitionTo` 作为场景加载的唯一入口，`EnterGameHandler` 不再调用 `UIStateManager.TransitionToScene` 触发场景切换（仅保留 `SetSelectedCharacter` 的 UI 状态更新）。

#### Scenario: 主路径场景切换
- **WHEN** 玩家通过 `CharacterManager.EnterGameAsync` 进入游戏
- **THEN** `GameSceneManager.TransitionTo(SceneType.GameWorld)` 被调用启动场景加载
- **AND** `EnterGameHandler` 收到响应后不再调用 `UIStateManager.TransitionToScene(SceneType.GameWorld)`
- **AND** `EnterGameHandler` 仍调用 `SetSelectedCharacter` 更新选中角色 UI 状态

#### Scenario: 降级路径场景切换
- **WHEN** `CharacterManager.Instance` 为 null，`CharacterSelectionUI` 走降级路径
- **THEN** 降级路径在发送 EnterGameRequest 后调用 `GameSceneManager.TransitionTo(SceneType.GameWorld)`
- **AND** 场景加载正常启动，不依赖 `WorldSceneInitializer` 5 秒兜底

### Requirement: 服务端实体初始位置与客户端一致

系统 SHALL 在 `SyncPacketHandler.HandleHandshakeAsync` 注册实体时使用与客户端一致的初始位置，避免服务端实体位置与客户端本地玩家位置在首个 InputPacket 提交前不一致。

#### Scenario: 实体注册位置一致
- **WHEN** 客户端发送 `HandshakePacket` 携带初始位置（或服务端从角色数据查询位置）
- **THEN** `ZoneShardGrain.RegisterEntityAsync` 使用该位置注册实体
- **AND** 服务端 `SimulatedEntity` 初始位置与客户端 `AuthTransformComponent` 初始位置一致
- **AND** 首个 Update delta 下发时位置不会突变

### Requirement: 快照基线重连重置

系统 SHALL 在客户端断线重连流程中显式重置 `SnapshotApplySystem._lastAppliedSnapshot`，避免旧 baseline 残留导致增量快照重建错乱。

#### Scenario: 重连后快照基线重置
- **WHEN** 客户端断线重连，重新建立网络连接并发起握手
- **THEN** `SnapshotApplySystem.ResetLastAppliedSnapshot()` 被调用
- **AND** `_lastAppliedSnapshot` 被置为 null
- **AND** 后续收到的首个全量快照被正确缓存为新 baseline

### Requirement: 本地玩家 ID 跨线程可见性

系统 SHALL 确保 `LocalPlayerOwnerId` 的跨线程读写可见性，使用 `volatile` 或 `Interlocked` 语义保证 UI 线程、网络线程、ECS 线程之间的可见性。

#### Scenario: 多线程设置与读取
- **WHEN** 网络线程 `OnHandshakeReceived` 或 UI 线程 `EnterGameHandler` 调用 `SetPlayerId`
- **THEN** ECS 线程 `SnapshotApplySystem.RetrospectivelyUpdateLocalPlayer` 能立即读到最新值
- **AND** 不存在因 CPU 缓存导致读到旧值（0）的风险

## MODIFIED Requirements

### Requirement: EnterGameHandler 响应处理

`EnterGameHandler.HandleAsync` 收到 `EnterGameResponse` 后 SHALL 仅负责：设置本地玩家 ID、创建本地玩家 ECS 实体、缓存 Actor 创建请求、更新 UI 选中角色状态、发送同步握手。不再调用 `UIStateManager.TransitionToScene`（场景加载由 `CharacterManager.EnterGameAsync` 提前完成）。

#### Scenario: 响应处理不再触发场景切换
- **WHEN** `EnterGameHandler.HandleAsync` 收到 `EnterGameResponse`（Success=true）
- **THEN** 执行 `SetPlayerId`、`CreateLocalPlayerEntity`、`RequestCreateLocalPlayerActor`、`SetSelectedCharacter`、`SendSyncHandshakeAsync`
- **AND** 不调用 `UIStateManager.TransitionToScene(SceneType.GameWorld)`

## REMOVED Requirements

### Requirement: EnterGameHandler 触发场景切换

**Reason**: `EnterGameHandler.cs:80` 调用 `stateManager.TransitionToScene(SceneType.GameWorld)` 仅更新 UI 状态机标记，不真正加载场景。真正场景加载由 `CharacterManager.EnterGameAsync` 中的 `GameSceneManager.TransitionTo` 负责。两者并存导致职责混乱，且在响应极快返回时序下可能造成场景永远不加载。

**Migration**: 移除 `EnterGameHandler` 中的 `TransitionToScene` 调用，保留 `SetSelectedCharacter` 的 UI 状态更新。场景切换完全由 `CharacterManager.EnterGameAsync` 负责。

# 角色移动旋转同步与断线 Despawn 修复 Spec

## Why

`audit-enter-game-network-sync` spec 完成后，选角进入游戏流程已正常工作（本地玩家实体创建成功、Actor 已生成）。但实测发现两个新 BUG：
1. **角色移动、旋转没有在客户端同步**：远程角色的位置和朝向不更新，本地玩家移动后其他客户端看不到变化。
2. **角色退出游戏后在其它客户端还存在**：客户端断开后，其他客户端的远程角色 Actor 未被销毁。

审计定位到两个根因：
- **移动同步根因**：`PlayerController.Camera` 引用可能为 null，导致 `input.LookYaw` 永远为 0，服务端 `entity.Yaw` 永不更新；同时 `ECSUpdateDriver.FlushInputSendQueue` 在 `IsSyncHandshakeComplete` 为 false 时静默丢弃所有 InputPacket，若握手响应未到达则移动永远不同步。
- **Despawn 根因**：`ZoneShardGrain.BroadcastEntityLifecycleAsync` 对 Despawn 使用 AOI 过滤（按实体最后位置 chunk 查订阅者），实体移动后 chunk 与 Spawn 时不一致，原订阅者收不到 Despawn；且 Despawn 缺少 Spawn 的 `includeNewSession` 等价兜底机制。

## What Changes

### 移动同步修复

- **为 `PlayerController.WriteInputToEcs` 添加 Camera 缺失诊断与降级**（`HundunWorld/Source/Game/PlayerController.cs:500-504`）。当 `Camera == null` 时，从 Actor 层级自动查找主相机（`Camera.MainCamera` 或 `Actor.GetScript<Camera>()`），若仍为 null 则输出警告日志并使用 `Actor.Orientation.Yaw` 作为 LookYaw 降级来源，避免 LookYaw 永远为 0。
- **为 `ECSUpdateDriver.FlushInputSendQueue` 添加握手未完成诊断日志**（`HundunWorld/Source/Game/ECSUpdateDriver.cs:88`）。当前握手未完成时静默 return，无任何日志。添加首次丢弃日志（限频），帮助定位握手是否完成。

### Despawn 修复

- **修改 `ZoneShardGrain.BroadcastEntityLifecycleAsync` 对 Despawn 的广播策略**（`Horizon.Orleans.Grains/World/ZoneShardGrain.cs:611-680`）。Despawn 不再走 AOI 过滤，改为广播给所有在线 session（`_aoi.GetAllSubscribers()` 或遍历所有已注册 session）。理由：实体生命周期事件（Spawn/Despawn）必须可靠送达所有曾收到 Spawn 的 session，否则会形成幽灵 Actor；AOI 过滤仅适用于 Update delta（位置高频更新）。
- **为 `ZoneShardGrain` 添加 `GetAllSubscribers` 能力**（若 `ZoneShardAoi` 无此方法）。Despawn 广播需要获取所有在线 session 列表。

### 诊断日志增强

- **为 `SnapshotApplySystem.HandleDespawn` 添加日志**（`Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs:560`）。当前 HandleDespawn 无任何日志输出，无法确认客户端是否收到 Despawn delta。添加 EntityId 级别的 Debug 日志。

## Impact

- **Affected specs**:
  - `audit-enter-game-network-sync`（前置 spec，已完成选角进入游戏流程修复）
  - `fix-network-sync-visibility`（基础网络同步可见性修复）
- **Affected code**:
  - `HundunWorld/Source/Game/PlayerController.cs` — Camera 缺失诊断与降级
  - `HundunWorld/Source/Game/ECSUpdateDriver.cs` — 握手未完成诊断日志
  - `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` — Despawn 广播策略修改
  - `Horizon.Game.Core/World/ZoneShardAoi.cs` — 可能添加 GetAllSubscribers 方法
  - `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs` — HandleDespawn 日志

## ADDED Requirements

### Requirement: PlayerController Camera 缺失降级

系统 SHALL 在 `PlayerController.WriteInputToEcs` 中当 `Camera` 引用为 null 时自动查找主相机，若仍为 null 则使用 `Actor.Orientation.Yaw` 作为 LookYaw 降级来源，确保 `input.LookYaw` 不会因 Camera 未配置而永远为 0。

#### Scenario: Camera 引用为 null 时自动查找主相机
- **WHEN** `PlayerController.Camera` 为 null
- **THEN** 自动通过 `Camera.MainCamera` 或 `Actor.GetScript<Camera>()` 查找主相机
- **AND** 若找到则使用该相机的 Yaw/Pitch 填充 `input.LookYaw`/`input.LookPitch`
- **AND** 若未找到则输出警告日志，并使用 `Actor.Orientation.Yaw * Mathf.DegreesToRadians` 作为 `input.LookYaw` 降级值

#### Scenario: Camera 引用正常时无影响
- **WHEN** `PlayerController.Camera` 非 null
- **THEN** 使用现有逻辑填充 `input.LookYaw`/`input.LookPitch`，行为不变

### Requirement: FlushInputSendQueue 握手未完成诊断

系统 SHALL 在 `ECSUpdateDriver.FlushInputSendQueue` 因 `IsSyncHandshakeComplete` 为 false 而丢弃 InputPacket 时输出首次诊断日志（限频），帮助定位握手是否完成。

#### Scenario: 握手未完成时输出诊断
- **WHEN** `FlushInputSendQueue` 因 `IsSyncHandshakeComplete == false` 而 return
- **THEN** 首次丢弃时输出警告日志 "InputPacket 被丢弃：同步握手未完成"
- **AND** 后续丢弃静默（避免日志刷屏），每 5 秒最多输出一次

### Requirement: Despawn 全量广播

系统 SHALL 在 `ZoneShardGrain.BroadcastEntityLifecycleAsync` 处理 Despawn 时广播给所有在线 session，不使用 AOI 过滤，确保所有曾收到该实体 Spawn 的客户端都能收到 Despawn。

#### Scenario: Despawn 广播给所有在线 session
- **WHEN** `UnregisterEntityAsync` 被调用，触发 `BroadcastEntityLifecycleAsync(Despawn)`
- **THEN** Despawn delta 广播给所有在线 session（`_aoi.GetAllSubscribers()`）
- **AND** 不使用实体最后位置 chunk 过滤订阅者
- **AND** 触发方 session（已断开的 entityId）被剔除
- **AND** 所有其他在线客户端收到 Despawn delta 并销毁对应 Actor

#### Scenario: Spawn 仍走 AOI 过滤（保持现有行为）
- **WHEN** `RegisterEntityAsync` 被调用，触发 `BroadcastEntityLifecycleAsync(Spawn)`
- **THEN** Spawn delta 仍按实体所在 chunk 过滤订阅者（保持现有行为）
- **AND** 新 session 仍收到全场已存在实体的 Spawn 补发（includeNewSession 机制不变）

## MODIFIED Requirements

### Requirement: ZoneShardGrain.BroadcastEntityLifecycleAsync 广播策略

`BroadcastEntityLifecycleAsync` 对 Spawn 和 Despawn 采用不同广播策略：
- **Spawn**：走 AOI 过滤（仅发给实体所在 chunk 的订阅者）+ includeNewSession 兜底（给新 session 补发全场 Spawn）。
- **Despawn**：全量广播（发给所有在线 session），不走 AOI 过滤，确保曾收到 Spawn 的客户端都能收到 Despawn。

#### Scenario: Despawn 不走 AOI 过滤
- **WHEN** `BroadcastEntityLifecycleAsync` 处理 Despawn
- **THEN** 不调用 `_aoi.GetSubscribers(triggerChunkKey)`
- **AND** 改为调用 `_aoi.GetAllSubscribers()` 获取所有在线 session
- **AND** 剔除触发方 session（entityId）

### Requirement: SnapshotApplySystem.HandleDespawn 日志

`HandleDespawn` 在销毁远程实体时输出 EntityId 级别的 Debug 日志，便于客户端定位是否收到 Despawn delta。

#### Scenario: Despawn 应用日志
- **WHEN** `HandleDespawn` 销毁远程实体
- **THEN** 输出日志 "SnapshotApply: Despawn 实体 EntityId={id}"
- **AND** 本地玩家保护分支输出 "SnapshotApply: Despawn 跳过本地玩家 EntityId={id}"

## REMOVED Requirements

### Requirement: Despawn 走 AOI 过滤

**Reason**: `BroadcastEntityLifecycleAsync` 原实现对 Spawn 和 Despawn 统一使用 `_aoi.GetSubscribers(triggerChunkKey)` 过滤订阅者。但实体移动后其最后位置 chunk 可能与 Spawn 时不同，导致曾收到 Spawn 的客户端因 AOI 窗口漂移而收不到 Despawn，形成幽灵 Actor。实体生命周期事件必须可靠送达，不应受 AOI 裁剪。

**Migration**: Despawn 改为全量广播（`GetAllSubscribers`），Spawn 保持 AOI 过滤 + includeNewSession 兜底。

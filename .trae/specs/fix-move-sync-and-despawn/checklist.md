# Checklist

## 阶段一：移动同步修复

- [x] `PlayerController.WriteInputToEcs` 在 `Camera == null` 时自动查找主相机（`FlaxEngine.Camera.MainCamera?.GetScript<ThirdPersonCamera>()`）
- [x] 若主相机仍为 null，使用 `Actor.Orientation.EulerAngles.Y * Mathf.DegreesToRadians` 作为 `input.LookYaw` 降级值
- [x] Camera 缺失时输出警告日志
- [x] `BuildAndSendInputPacket` 备用路径使用相同降级逻辑（复用 `TryGetLookYawPitch` 方法）
- [x] `ECSUpdateDriver.FlushInputSendQueue` 在 `IsSyncHandshakeComplete == false` 时输出首次诊断日志
- [x] 诊断日志限频（首次立即输出，后续每 5 秒最多一次）
- [x] 日志格式包含等待秒数

## 阶段二：Despawn 全量广播修复

- [x] `ZoneShardAoi` 提供 `GetAllSubscribers()` 方法（返回副本 `new HashSet<long>(_sessionToChunks.Keys)`）
- [x] `ZoneShardGrain.BroadcastEntityLifecycleAsync` 对 Despawn 使用全量广播（`GetAllSubscribers()`）
- [x] Despawn 不再走 AOI 过滤（不调用 `_aoi.GetSubscribers(triggerChunkKey)`）
- [x] Despawn 广播剔除触发方 session（entityId）
- [x] Spawn 保持现有 AOI 过滤 + includeNewSession 兜底逻辑不变
- [x] Despawn 全量广播分支有日志输出广播目标数量

## 阶段三：诊断日志增强

- [x] `SnapshotApplySystem.HandleDespawn` 本地玩家保护分支有日志输出（第 565 行，已存在）
- [x] `SnapshotApplySystem.HandleDespawn` 远程实体销毁分支有日志输出（第 571 行，新增）
- [x] 日志包含 EntityId 信息
- [x] 日志风格与现有 HandleSpawn/HandleUpdate 一致（`Console.WriteLine` + `[SnapshotApplySystem]` 前缀）

## 阶段四：编译与代码审查验证

- [x] `Horizon.Game.Core` 项目编译通过（0 错误）
- [x] `Horizon.Orleans.Grains` 项目编译通过（0 错误）
- [x] `Horizon.Game.ECS.Arch` 项目编译通过（0 错误）
- [x] 代码审查确认 `PlayerController.WriteInputToEcs` 有 Camera 缺失降级
- [x] 代码审查确认 `ECSUpdateDriver.FlushInputSendQueue` 有握手未完成诊断日志
- [x] 代码审查确认 `ZoneShardGrain.BroadcastEntityLifecycleAsync` Despawn 走全量广播
- [x] 代码审查确认 `SnapshotApplySystem.HandleDespawn` 有日志输出

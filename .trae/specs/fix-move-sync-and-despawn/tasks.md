# Tasks

## 阶段一：移动同步修复

- [x] Task 1: PlayerController Camera 缺失诊断与降级
  - [x] SubTask 1.1: 修改 `WriteInputToEcs` 方法（第 500-504 行附近），当 `Camera == null` 时自动查找主相机
  - [x] SubTask 1.2: 自动查找逻辑：优先 `FlaxEngine.Camera.MainCamera?.GetScript<ThirdPersonCamera>()`，若找到则缓存到 `Camera` 字段
  - [x] SubTask 1.3: 若仍为 null，输出警告日志，并使用 `Actor.Orientation.EulerAngles.Y * Mathf.DegreesToRadians` 填充 `input.LookYaw`
  - [x] SubTask 1.4: 同步修改 `BuildAndSendInputPacket` 备用路径，抽取 `TryGetLookYawPitch` 方法复用

- [x] Task 2: ECSUpdateDriver 握手未完成诊断日志
  - [x] SubTask 2.1: 修改 `FlushInputSendQueue` 方法，当 `IsSyncHandshakeComplete == false` 时输出诊断日志
  - [x] SubTask 2.2: 实现限频：首次立即输出，后续每 5 秒最多一次（使用 `Time.GameTime` 记录上次输出时间）
  - [x] SubTask 2.3: 日志格式："InputPacket 被丢弃：同步握手未完成（已等待 {seconds} 秒）"

## 阶段二：Despawn 全量广播修复

- [x] Task 3: ZoneShardAoi 添加 GetAllSubscribers 方法
  - [x] SubTask 3.1: 读取现有 AOI 结构（`_sessionToChunks` 映射）
  - [x] SubTask 3.2: 方法已存在，修改实现为返回副本 `new HashSet<long>(_sessionToChunks.Keys)` 并更新 XML 注释
  - [x] SubTask 3.3: 已有等价方法（原 GetAllSubscribers 返回视图，现改为返回副本）

- [x] Task 4: 修改 ZoneShardGrain.BroadcastEntityLifecycleAsync Despawn 广播策略
  - [x] SubTask 4.1: 修改 `BroadcastEntityLifecycleAsync` 方法（第 611-680 行）
  - [x] SubTask 4.2: 当 `kind == EntityDeltaKind.Despawn` 时，使用 `_aoi.GetAllSubscribers()` 全量广播
  - [x] SubTask 4.3: 剔除触发方 session：`allOnlineSessions.Where(s => (ulong)s != entityId).ToArray()`
  - [x] SubTask 4.4: 当 `kind == EntityDeltaKind.Spawn` 时，保持现有 AOI 过滤逻辑不变
  - [x] SubTask 4.5: 在 Despawn 全量广播分支添加日志，输出广播目标数量

## 阶段三：诊断日志增强

- [x] Task 5: SnapshotApplySystem.HandleDespawn 添加日志
  - [x] SubTask 5.1: 修改 `HandleDespawn` 方法（第 560 行附近）
  - [x] SubTask 5.2: 本地玩家保护分支日志已存在（第 565 行）
  - [x] SubTask 5.3: 远程实体销毁分支添加日志（第 571 行）
  - [x] SubTask 5.4: 使用 `Console.WriteLine` 与 `[SnapshotApplySystem]` 前缀，与现有日志风格一致

## 阶段四：端到端验证

- [x] Task 6: 编译与代码审查验证
  - [x] SubTask 6.1: 编译 `Horizon.Game.Core` 项目（0 错误）
  - [x] SubTask 6.2: 编译 `Horizon.Orleans.Grains` 项目（0 错误）
  - [x] SubTask 6.3: 编译 `Horizon.Game.ECS.Arch` 项目（0 错误）
  - [x] SubTask 6.4: 代码审查确认 `PlayerController.WriteInputToEcs` 有 Camera 缺失降级
  - [x] SubTask 6.5: 代码审查确认 `ECSUpdateDriver.FlushInputSendQueue` 有握手未完成诊断日志
  - [x] SubTask 6.6: 代码审查确认 `ZoneShardGrain.BroadcastEntityLifecycleAsync` Despawn 走全量广播
  - [x] SubTask 6.7: 代码审查确认 `SnapshotApplySystem.HandleDespawn` 有日志输出

# Task Dependencies

- Task 1（PlayerController Camera 降级）独立，可立即开始
- Task 2（ECSUpdateDriver 诊断日志）独立，可与 Task 1 并行
- Task 3（ZoneShardAoi GetAllSubscribers）独立，可与 Task 1-2 并行
- Task 4（ZoneShardGrain Despawn 全量广播）依赖 Task 3（需要 GetAllSubscribers 方法存在）
- Task 5（SnapshotApplySystem 日志）独立，可与 Task 1-4 并行
- Task 6（端到端验证）依赖 Task 1-5 全部完成

# 并行化建议

以下任务可并行委派给不同 sub-agent：
- **并行组 A（移动同步）**：Task 1 + Task 2（互相独立）
- **并行组 B（Despawn 修复）**：Task 3 + Task 4（串行，Task 4 依赖 Task 3）
- **并行组 C（诊断日志）**：Task 5（独立）

Task 6 必须在所有修复完成后串行执行。

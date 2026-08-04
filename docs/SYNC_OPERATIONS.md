# 同步管线运维监控手册

> 本文档记录 HundunWorld 远程角色移动平滑同步管线的运维监控指标与诊断日志排查指引。
> 对应 spec 4.4.1（平滑度指标可观测）、4.4.2（关键事件可追溯）。

---

## 1. ClientSyncMetrics 监控指标

`ClientSyncMetrics`（静态类，`HundunWorld/Source/Game/Network/ClientSyncMetrics.cs`）提供客户端网络同步可观测性指标。
线程安全（Interlocked 计数器 + volatile 浮点），供 UI/调试面板/运维监控读取。

### 1.1 新增指标（远程角色移动平滑性修复）

以下四个指标为本次新增，由 `ECSUpdateDriver.ForwardEcsMetrics` 每帧从 ECS 系统采集转发。

| 指标 | 类型 | 单位 | 采集源 | 含义 |
| --- | --- | --- | --- | --- |
| `@CurrentInterpolationDelayMs | float | ms | `SnapshotApplySystem.AdaptiveInterpolationDelaySeconds` × 1000 | 当前自适应插值延迟窗口。公式：`max(avg+2·jitter, rtt/2+rttJitter)` clamp[100ms, 400ms]。强网约 100ms，弱网最高 400ms。 |
| @StaleEntitiesCleaned | long | 个 | `SnapshotApplySystem.StaleEntitiesCleaned` 增量 | 累计因超时（90 秒未收到快照）被客户端兜底清理的远程实体数。正常为 0；持续增长说明服务端异常断线检测滞后或 fanout 链路断裂。 |
| @SmoothnessScore | float | 0..100 | `InterpolationSystem` 每帧采样 → 60 帧滑动窗口 | 远程角色移动平滑度评分。匀速移动（delta 稳定）→ 评分高（接近 100）；卡顿/跳跃（delta 波动大）→ 评分低。公式：`100 / (1 + stdDelta*5 + stdFrame*200)`。 |
| @CurrentStrategyCombo | string | — | `ECSUpdateDriver` 拼接 | 当前同步策略组合描述，格式：`Active|{Lerp 或 Lerp+DeadReckoning}|{NetworkQualityLevel}|{delayMs}ms|SmoothSample={Y/N}`。供运维查询对比不同网络环境下的方案表现。 |

### 1.2 查询方式

#### 通过 C# 代码查询

```csharp
using HundunWorld.Game.Network;

// 当前插值延迟（ms）
float delayMs = ClientSyncMetrics.CurrentInterpolationDelayMs;

// 累计清理的过期实体数
long staleCleaned = ClientSyncMetrics.StaleEntitiesCleaned;

// 平滑度评分（0..100，越大越平滑）
float smoothness = ClientSyncMetrics.SmoothnessScore;

// 当前策略组合（如 "Active|Lerp+DeadReckoning|Medium|180ms|SmoothSample=Y"）
string strategy = ClientSyncMetrics.CurrentStrategyCombo;
```

#### 通过调试面板查询

在 Flax Engine 调试面板中添加 UI 控件绑定上述静态属性，每帧刷新即可实时显示。
`ECSUpdateDriver.ForwardEcsMetrics` 每帧自动采集转发，无需额外调用。

### 1.3 告警阈值建议

| 指标 | 正常范围 | 告警阈值 | 告警含义与排查 |
| ---3.3 告警阈值建议

| 指标 | 正常范围 | 告警阈值 | 告警含义与排查 |
| --- | --- | --- | --- |
| @CurrentInterpolationDelayMs | 100..200 ms | > 350ms 持续 10 秒 | 网络质量差（RTT 高或抖动大），插值窗口逼近上限。排查：检查 `EstimatedRttMs` 与 `RttJitterMs`，确认是否网络分区或服务端 tick 卡顿。 |
| @StaleEntitiesCleaned | 0 | 每分钟增量 > 5 | 远程实体频繁超时清理。排查：服务端心跳是否正常（`FullSnapshotIntervalTicks=60`），fanout observer 是否重订阅失败，网关是否重连。 |
| @SmoothnessScore | 70..100 | < 50 持续 5 秒 | 远程角色移动卡顿/跳跃。排查：检查 `SnapshotIntervalMs` 与 `SnapshotJitterMs`（快照到达是否稳定），`CurrentInterpolationDelayMs` 是否过小导致缓冲抽干。 |
| @CurrentStrategyCombo | 含 "Strong" 或 "Medium" | 含 "Weak" 持续 30 秒 | 网络质量等级降到 Weak。排查：检查 `EstimatedRttMs` 是否 > 200ms，确认是否 4G/WiFi 切换或服务端负载高。 |

### 1.4 既有指标（参考）

| 指标 | 含义 |
| --- | --- |
| @EstimatedRttMs | 估计 RTT（ms），EWMA 平滑（α=0.125） |
| @RttJitterMs | RTT 抖动（ms） |
| @SnapshotIntervalMs | 快照间隔滑动平均（ms） |
| @SnapshotJitterMs | 快照间隔标准差（ms） |
| @PredictionErrorAvg | 预测误差滑动平均（米） |
| @CorrectionsApplied |(Applied | 累计修正次数 |
| @SnapshotOverflowCount | 累计单帧快照消费溢出次数 |
| @InputRetransmits | 累计冗余重传数 |

---

## 2. ISyncDiagnosticsSink 结构化诊断日志

`ISyncDiagnosticsSink`（`Horizon.Game.ECS.Arch/Diagnostics/ISyncDiagnosticsSink.cs`）定义 5 类同步管线关键事件。
游戏层实现 `SyncDiagnosticsSinkImpl`（`HundunWorld/Source/Game/Network/SyncDiagnosticsSinkImpl.cs`）将事件转发到 Flax `Debug` 结构化日志。
所有异常被吞掉（try-catch 包裹），避免日志失败影响同步主逻辑。

### 2.1 五类日志事件格式

#### 2.1.1 传送跳变（OnTeleportJump）

**触发条件**：远程角色目标位置与当前位置距离超过传送阈值（默认 50m），直接跳到目标位置。

**日志格式**：
```
[SyncDiag] TeleportJump Entity={entityId} Distance={distance:F2}m ServerTick={serverTick}
```

**日志级别**：`Debug.Log`（Info）

**排查指引**：
- 正常场景：复活、跨地图传送，Distance > 50m。
- 异常场景：Distance 在 50~100m 之间频繁触发，可能是远程玩家临时断网恢复后位置累积变化。检查 `EstimatedRttMs` 是否突增，确认是否网络分区。

#### 2.1.2 修正风暴触发（OnCorrectionStormTriggered）

**触发条件**：`ReconciliationSystem` 检测到窗口内修正次数超阈值（默认 5 次/2 秒），进入冷却期跳过修正。

**日志格式**：
```
[SyncDiag] CorrectionStorm Entity={entityId} Count={recentCount} Window={windowSeconds:F1}s
```

**日志级别**：`Debug.LogWarning`（Warning）

**排查指引**：
- 频繁触发说明客户端预测与服务端权威持续分叉。排查：检查 `EstimatedRttMs` 是否过高（> 200ms），`PredictionErrorAvg` 是否持续 > 0.5m，确认是否高延迟导致预测漂移。
- 可通过配置外化放宽 `SyncReconciliation:StormThreshold`（如高 RTT 玩家设为 10）减少误触发。

#### 2.1.3 过期修正跳过（OnStaleCorrectionSkipped）

**触发条件**：`ReconciliationSystem` 收到的 Correction 其 `LastProcessedClientTick` < 当前已 ACK 的最新 tick，说明该 Correction 已被后续 ACK 覆盖。

**日志格式**：
```
[SyncDiag] StaleCorrectionSkipped Entity={entityId} ProcessedTick={lastProcessedTick} AckedTick={lastAckedTick}
```

**日志级别**：`Debug.Log`（Info）

**排查指引**：
- 偶发正常（网络乱序）。频繁触发说明 ACK 与 Correction 网络乱序严重。排查：检查 `EstimatedRttMs` 与 `RttJitterMs`，确认是否网络抖动导致包乱序。

#### 2.1.4 自适应窗口调整（OnAdaptiveWindowAdjusted）

**触发条件**：`SnapshotApplySystem.Ad' 自适应插值延迟窗口发生显著变化（> 20ms）。

**日志格式**：
```
[SyncDiag] AdaptiveWindow {oldDelayMs}ms->{newDelayMs}ms RTT={rttMs}ms Jitter={jitterMs}ms
```

**日志级别**：`Debug.Log`（Info）

**排查指引**：
- 窗口增大说明网络变差（RTT 或抖动增加），窗口减小说明网络恢复。
- 若窗口频繁在 100ms↔400ms 之间跳变，说明网络质量不稳定。排查：检查 `EstimatedRttMs` 与 `RttJitterMs` 趋势，确认是否 4G/WiFi 切换或服务端负载波动。

#### 2.1.5 baseline 重传请求（OnBaselineResyncRequested）

**触发条件**：`SnapshotApplySystem` delta 解码时 baseline 不匹配（持有的 baseline tick 与 delta 期望的 `BaselineTick` 不一致）。

**日志格式**：
```
[SyncDiag] BaselineResync Expected={expectedBaselineTick} Received={receivedBaselineTick}
```

**日志级别**：`Debug.LogWarning`（Warning）

**排查指引**：
- 偶发正常（重连或 AOI 边界抖动导致 baseline 丢失）。频繁触发说明 baseline 频繁丢失。排查：
  - 检查服务端 `ZoneShardGrain` 是否正常下发全量快照（`FullSnapshotIntervalTicks=60`）。
  - 检查客户端 `SnapshotApplySystem.OnFullSnapshotApplied` 是否被正确调用。
  - 检查 `+AOI 边界是否频繁切换导致 baseline 失效。
- 服务端收到 `BaselineResyncRequestPacket` 后设置 `_forceFullSnapshotNextTick=true`，下一 tick 强制下发全量快照。

### 2.2 日志采集与过滤

- **采集**：日志通过 Flax `Debug.Log`/`Debug.LogWarning` 输出，写入 Flax 日志文件与控制台。
- **过滤**：可用日志级别过滤（Info/Warning）。`CorrectionStorm` 与 `BaselineResync` 为 Warning 级别，其余为 Info。
- **限频**：`AdaptiveWindow` 仅在窗口变化 > 20ms 时输出，避免刷屏。`BaselineResync` 队列限流 16，避免队列爆炸。
- **异常安全**：所有日志方法内部 try-catch 包裹，日志失败不影响同步主逻辑。

---

## 3. 配置外化参数

### 3.1 修正风暴检测参数（SyncReconciliationOptions）

配置节：`appsettings.json` → `SyncReconciliation`

| 参数 | 默认值 | 范围 | 说明 |
| --- | --- | --- | --- |
| StormThreshold | 5 | 1..100 | 修正风暴检测窗口内允许的最大修正次数。高 RTT 玩家可放宽到 10。 |
| StormWindowSeconds | 2.0 | 0.5..30 | 修正风暴检测窗口（秒）。 |
| StormCooldownSeconds | 1.0 | 0.1..10 | 修正风暴冷却时间（秒）。 |

**注入路径**：
1. `Program.cs` 注册 `services.Configure<SyncReconciliationOptions>(context.Configuration.GetSection("SyncReconciliation"))`。
2. `ECSUpdateDriver` 公开属性 `ReconciliationStormThreshold`/`ReconciliationStormWindowSeconds`/`ReconciliationStormCooldownSeconds` 接收配置。
3. `ECSUpdateDriver` 一次性注入逻辑将配置应用到 `ReconciliationSystem.StormThreshold`/`StormWindowSeconds`/`StormCooldownSeconds`。

**示例配置**（高 RTT 玩家放宽）：
```json
{
  "SyncReconciliation": {
    "StormThreshold": 10,
    "StormWindowSeconds": 3.0,
    "StormCooldownSeconds": 2.0
  }
}
```

---

## 4. 超大规模容量治理：带宽限流 / 规模档位事件（v8 重构新增）

> 同步链路归一重构（协议 v8）新增服务端带宽预算限流（spec 5.5.1.1）与客户端规模档位控制（spec 5.5.1.3）。
> 本节补充对应的运维观测方式与排查指引。

### 4.1 服务端带宽限流 / 恢复事件

**触发条件**：
- `OnBandwidthThrottled`：会话 1 秒滚动窗口平均带宽超预算（默认 100kbps 红线）→ 快照频率降档（20Hz→10Hz）；持续超预算 → 二次降频（10Hz→5Hz）。
- `OnBandwidthRecovered`：连续 `RecoverySeconds`（默认 3）秒低于预算 → 快照频率逐级回升（5Hz→10Hz→20Hz）。

**日志格式**（服务端 ILogger）：
```
[GatewaySyncDispatcher] 带宽超阈值限流：bandwidth={kbps}kbps > threshold={kbps}kbps，快照频率降为 {Hz}Hz
[GatewaySyncDispatcher] 带宽持续超阈值深度限流：bandwidth={kbps}kbps，快照频率降为 {Hz}Hz
[GatewaySyncDispatcher] 带宽恢复回升：连续 {Seconds} 秒低于阈值，快照频率回升为 {Hz}Hz
```

**排查指引**：
- 频繁触发限流说明单会话下行超 100kbps。排查：
  1. 包体构成：`GatewaySyncDispatcher.EstimatePacketSizeBytes` 估算是否虚高（压缩包已按 0.6× 折减）。
  2. 频率分布：`GetSessionSnapshotHz` 三档（20/10/5）是否正确随带宽调整。
  3. 兴趣分级：`InterestGradeOptions` 近/中/远档是否生效（远档实体 5Hz 低频裁剪）。
- 超大规模目标 ≤ 50kbps/会话（`UltraScaleBudgetKbps`）。未达标时输出压测报告（包体构成、频率分布）作为阻断证据。

### 4.2 客户端规模档位切换 / 降级事件

**触发条件**：
- `OnScaleTierChanged`：客户端同屏远程实体数跨档位阈值 20/100/1000/5000。
- `OnScaleDegrade`：同屏实体数超当前档位上限，按距离排序选取最远实体暂停插值推进（不消失、不销毁 Actor）。

**日志格式**（客户端 Flax Debug）：
```
[SyncDiag] ScaleTierChanged Count={entityCount} {from}->{to}
[SyncDiag] ScaleDegrade Entity={entityId} Dist={distanceMeters:F1}m Reason=ScaleOverLimit
```

**排查指引**：
- 档位频繁升降说明同屏实体数在阈值附近波动。排查 AOI 订阅是否抖动（`PlayerSubscriptionStateComponent` 订阅更新频率）。
- 降级实体不应消失：若远程角色消失，检查 `InterpolationSystem.SetDegradedEntities` 是否误将降级集合同步为"移除订阅"。

### 4.3 带宽 / 规模红线观测方式

| 观测项 | 采集方式 | 红线 |
| --- | --- | --- |
| 每会话带宽 | `GatewaySyncDispatcher.GetBandwidthSnapshot()`（sessionId → kbps） | ≤ 100kbps（超大规模目标 ≤ 50kbps） |
| 每会话快照频率 | `GatewaySyncDispatcher.GetSessionSnapshotHz(sessionId)` | 正常 20Hz / 限流 10Hz / 深度降级 5Hz |
| 同屏实体数 | `FlaxActorSyncSystem.GetRemoteActorCount()` / `SyncScaleController.CurrentTier` | 档位 20/100/1000/5000，超 5000 触发 OverLimit 降级 |
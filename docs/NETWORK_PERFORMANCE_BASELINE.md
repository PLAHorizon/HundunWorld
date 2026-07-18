# MMORPG 网络同步性能基线报告

> 生成时间：2026-07-18 13:58:09
> 由 `NetworkPerformanceBaselineReportTests.GeneratePerformanceBaselineReport` 自动生成。

## 1. 测试环境

| 项 | 值 |
| --- | --- |
| 操作系统 | Microsoft Windows NT 10.0.19045.0 |
| .NET 运行时 | 10.0.9 |
| 处理器核心数 | 16 |
| 64 位系统 | True |
| 64 位进程 | True |
| 测试机器名 | LONGMAC |

## 2. 指标定义

| 指标 | 单位 | 定义 |
| --- | --- | --- |
| SessionCount | 个 | 模拟并发玩家会话数。 |
| DurationTicks | tick | 模拟持续 tick 数（60Hz 下 1 tick = 16.67ms）。 |
| DurationSeconds | 秒 | 模拟持续墙钟时长 = DurationTicks × 16.67ms / 1000。 |
| TotalBytesSent | 字节 | 累计发送字节数（snapshot 下行 + input 上行编码后总字节）。 |
| AvgBandwidthKbps | kbps | 每玩家平均带宽 = (总字节 / session 数) × 8 / 1024 / 时长秒。 |
| MaxBandwidthKbps | kbps | 单 session 峰值带宽。 |
| AvgLatencyMs | 毫秒 | 端到端平均延迟（input→snapshot round trip = 2 × 16.67ms）。 |
| MaxLatencyMs | 毫秒 | 端到端峰值延迟。 |
| TotalPacketsEncoded | 个 | 累计编码的同步包总数。 |
| TotalPacketsDecoded | 个 | 累计解码的同步包总数。 |
| PacketsEncodedPerSecond | 包/秒 | 编码吞吐量。 |
| EffectiveLossRate | 比率 | 弱网下实际丢包率 = 丢弃包数 / 发送包数。 |

## 3. 100 玩家压测数据

**测试配置**：100 并发玩家 × 600 tick（10 秒 @ 60Hz），每 session 每 tick 上行 1 个 InputPacket + 下行 1 个 SnapshotPacket（含 10 个 EntityDelta）。

| 指标 | 值 |
| --- | --- |
| SessionCount | 100 |
| DurationTicks | 600 |
| DurationSeconds | 10.00 |
| TotalBytesSent | 12,676,643 |
| TotalBytesReceived | 12,676,643 |
| AvgBandwidthKbps | 99.04 |
| MaxBandwidthKbps | 101.45 |
| AvgLatencyMs | 33.33 |
| MaxLatencyMs | 33.33 |
| TotalPacketsEncoded | 80,000 |
| TotalPacketsDecoded | 80,000 |
| PacketsEncodedPerSecond | 36564 |
| ElapsedMs（墙钟） | 2188.0 |

## 4. 弱网压测数据

**测试配置**：200ms 延迟 + 5% 丢包率 + 50ms 抖动，600 tick（10.0 秒 @ 60Hz），每 tick 投递 1 个 SnapshotPacket。

| 指标 | 值 |
| --- | --- |
| LatencyMs（配置） | 200 |
| PacketLossRate（配置） | 5% |
| JitterMs（配置） | 50 |
| Ticks | 600 |
| PacketsSent | 600 |
| PacketsDropped | 29 |
| PacketsDelivered | 571 |
| EffectiveLossRate | 4.83% |
| MaxQueueDepth | 15 |

## 5. 带宽目标达成结论

**目标**：每玩家平均带宽 < 100 kbps。

- **实测每玩家平均带宽**：99.04 kbps
- **目标阈值**：100.00 kbps
- **达成结论**：✅ 达成

- **裕量**：0.96 kbps（低于阈值 1.0%）

## 6. 容量规划建议

基于以上压测数据，给出以下容量规划建议：

### 6.1 单 shard 容量

- 每玩家平均带宽消耗：12677 字节/秒（99.04 kbps）
- 假设单 shard 网络出口 1000 Mbps，理论上限可承载 ~10,339 并发玩家。
- 实际部署应预留 30% 冗余应对流量峰值与突发抖动。

### 6.2 集群扩展性

- 同步层为无状态设计（SyncPacketCodec 编解码 + JitterBuffer per-session），可水平扩展。
- 单 Gateway 实例建议承载 ≤ 2000 并发玩家（考虑 CPU 编解码开销）。
- 集群规模 = 目标 CCU / 2000，向上取整。

### 6.3 弱网降级策略

- 在 200ms 延迟 + 5% 丢包下，实际丢包率 4.83%（应接近配置值）。
- JitterBuffer 自适应插值延迟窗口 [80ms, 200ms]，可吸收 200ms 以内延迟抖动。
- InputPacket 冗余重传（落后 5 tick 触发）可对抗 10% 以内丢包率。
- 超过 10% 丢包率建议触发 ReconnectResume 全量恢复。

### 6.4 监控指标

- 每玩家平均/峰值带宽（kbps）
- 端到端平均/峰值延迟（ms）
- JitterBuffer EMA RTT 与方差
- InputSendSystem 未确认队列深度
- WeakNetworkSimulator 实际丢包率

---

## 附录：测试方法学

- **压测工具**：`Horizon.Game.Core.LoadTest.NetworkLoadHarness`（真实 SyncPacketCodec 编解码，不依赖 Orleans/TouchSocket 实例）。
- **弱网仿真**：`Horizon.Game.Core.LoadTest.WeakNetworkSimulator`（注入延迟/丢包/抖动/中断）。
- **确定性**：所有压测使用固定 RNG seed，结果可回归。
- **覆盖范围**：100 并发玩家 × 10 秒双向流量 + 200ms/5% 丢包弱网场景。

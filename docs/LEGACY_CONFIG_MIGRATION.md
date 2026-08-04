# 网络同步配置迁移指南（v8 重构）

> 同步链路归一重构（协议 v8）将旧脚本组件同步链路物理删除后，旧配置键不再产生新的配置来源。
> 本指南说明旧键→新键的迁移映射、迁移行为与运维重配指引。
> 对应 spec 4.5.2 / design.md 2.2.2.5，实现类：`Horizon.Game.ECS.Arch.Configuration.LegacyConfigMigration`。

---

## 1. 迁移背景

重构前客户端并存两套同步实现：
- **权威 ECS 链路**（保留归一）：`SnapshotApplySystem` / `InterpolationSystem` / `FlaxActorSyncSystem`。
- **旧脚本组件链路**（物理删除）：`NetworkSyncManager` / `NpcSyncManager` / `SkillSyncHandler` / `NetworkSyncIntegration` / `EntitySynchronizationManager`。

旧脚本组件的配置项随旧文件一并删除。配置加载层通过 `LegacyConfigMigration` 识别旧键并转换为新配置语义，输出迁移日志供运维核对。

---

## 2. 迁移映射表

| 旧键（旧脚本组件） | 新键（权威链路配置） | 迁移行为 |
| --- | --- | --- |
| `NetworkSyncManager.InterpolationDelay` | `SnapshotApplySystem.AdaptiveDelayMinSeconds/MaxSeconds` | 旧插值延迟（秒）→ 自适应窗口上下限语义组（以旧值为窗口中心，上下各 50% 带宽） |
| `NetworkSyncManager.PositionCorrectionThreshold` | `ReconciliationSystem.CorrectionThreshold` | 同值迁移（默认 0.5m） |
| `NetworkSyncManager.NetworkUpdateRate` | `BandwidthBudgetOptions.NormalSnapshotHz` | 客户端 20Hz 上行与 20Hz 下发对齐 |
| `NpcSyncManager.NearSyncInterval` | `InterestGradeOptions.NearSnapshotHz` | 旧间隔毫秒（50ms）→ 频率 Hz（20Hz） |
| `NpcSyncManager.MidSyncInterval` | `InterestGradeOptions.MidSnapshotHz` | 旧间隔毫秒（100ms）→ 频率 Hz（10Hz） |
| `NpcSyncManager.FarSyncInterval` | `InterestGradeOptions.FarSnapshotHz` | 旧间隔毫秒（200ms）→ 频率 Hz（5Hz） |

> 键名归一化：`LegacyConfigMigration.TryMigrateLegacyKey` 同时支持"裸键名"（如 `PositionCorrectionThreshold`）与"完整旧键名"（如 `NetworkSyncManager.PositionCorrectionThreshold`）。

---

## 3. 迁移行为

- 未知旧键：返回 `false`，不抛异常，配置加载层忽略。
- 已知旧键：返回 `true`，输出迁移日志：
  ```
  [LegacyConfigMigration] 旧配置键已迁移：{OldKey}={OldValue} → {NewKey}={NewValue}
  ```
- 迁移完成后，旧键随旧文件删除一并收敛（不再有新的配置来源）。

---

## 4. 运维重配指引

### 4.1 带宽预算（服务端）

新配置节：`BandwidthBudgetOptions`

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `BudgetKbps` | 100.0 | 带宽红线预算（kbps），任意会话平均下行红线 |
| `UltraScaleBudgetKbps` | 50.0 | 超大规模档位目标预算（kbps） |
| `NormalSnapshotHz` | 20 | 正常快照频率（Hz） |
| `ThrottledSnapshotHz` | 10 | 限流快照频率（Hz，第一级降频） |
| `DegradedSnapshotHz` | 5 | 深度降级快照频率（Hz，第二级降频） |
| `RecoverySeconds` | 3 | 带宽恢复判定秒数（连续低于预算后逐级回升） |
| `WindowSeconds` | 1.0 | 带宽统计滚动窗口（秒） |

### 4.2 兴趣分级（服务端）

新配置节：`InterestGradeOptions`

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `NearDistanceMeters` | 30 | 近档距离上限（米），近档高频全量 |
| `MidDistanceMeters` | 80 | 中档距离上限（米），中档降频+裁剪 |
| `NearSnapshotHz` | 20 | 近档下发频率（Hz） |
| `MidSnapshotHz` | 10 | 中档下发频率（Hz） |
| `FarSnapshotHz` | 5 | 远档下发频率（Hz） |
| `HysteresisMeters` | 5 | 分级切换滞回距离（米），防边界抖动 |

### 4.3 规模档位（客户端）

新配置节：`RemoteSyncThresholdOptions` 扩展字段

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `TierThresholds` | `{ 20, 100, 1000, 5000 }` | 客户端规模档位阈值（实体数），严格递增 |
| `UltraScaleEntityCap` | 5000 | 超规模实体数上限，超过触发 OverLimit 最远优先降级 |

---

## 5. 验证

迁移逻辑由 `Horizon.Game.Gateway.Tests/LegacyConfigMigrationTests.cs` 覆盖：
- `Migrate_PositionCorrectionThreshold_SameValue`
- `Migrate_NetworkUpdateRate_ToNormalSnapshotHz`
- `Migrate_InterpolationDelay_ToAdaptiveWindow`
- `Migrate_NpcNearSyncInterval_ToNearSnapshotHz`
- `Migrate_UnknownKey_ReturnsFalse_NoThrow`
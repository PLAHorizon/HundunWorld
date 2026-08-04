# 网络连接精简治理配置与运维手册

> 本手册记录连接精简治理（仅保留必要连接）的配置参数、清理来源枚举、治理统计口径与运维排查指引。
> 对应 spec `connection_cleanup/spec.md` 5.1~5.5，实现类：`Horizon.Game.Gateway.Configuration.ConnectionGovernanceOptions`、
> `Horizon.Game.Gateway.ConnectionCleanupSource`、`HundunWorld.Game.Network.ClientConnectionCoordinator`。

---

## 1. 治理目标

- **客户端**：任意客户端进程任一时刻仅持有一条必要游戏连接（登录/进游戏/重连三类建连请求经
  `ClientConnectionCoordinator` 互斥编排），从源头消除"幽灵连接 + 重复连接 + 闲置连接"。
- **服务端**：连接数三级约束（全局→每 IP→每用户）+ 重复连接检测，超限拒绝并返回明确提示。
- **可观测**：清理来源枚举化（`ConnectionCleanupSource`）+ 治理统计（幽灵/损坏/重复/未绑定四类）。

---

## 2. 服务端治理配置（ConnectionGovernanceOptions）

配置节：`appsettings.json` → `ConnectionGovernance`

| 参数 | 默认值 | 合法区间 | 说明 |
| --- | --- | --- | --- |
| `FirstPacketTimeoutSeconds` | 5 | (0, +∞) | 首包超时判定（连接建立后 N 秒未收到任何数据判定为幽灵连接）。与既有 `NetworkOptions.FirstPacketTimeoutSeconds` 对齐。 |
| `IdleTimeoutSeconds` | 30 | (0, +∞) | 空闲超时（收到数据后 N 秒无活动判定离线）。与既有 `NetworkOptions.IdleTimeoutSeconds` 对齐。 |
| `MaxConnections` | 10000 | (0, +∞) | 全局最大连接数。与既有 `GatewayOptions.MaxConnections` 对齐。 |
| `MaxConnectionsPerIp` | 4 | > 0 且 ≥ 每用户 | 每 IP 最大连接数（重复连接防护）。 |
| `MaxConnectionsPerUser` | 1 | > 0 且 ≤ 每 IP | 每用户最大连接数（一用户一连接）。 |
| `DespawnGracePeriodSeconds` | 15 | (0, +∞) | 清理宽限期（断开清理时角色 Despawn 延迟）。与 `PlayerDespawnScheduler` 对齐。 |

**兜底规则**：非法值（≤0、`MaxConnectionsPerIp < MaxConnectionsPerUser`）由
`ConnectionGovernanceOptionsValidator` 回退默认并输出 `[ConnectionGovernance] 配置非法回退` 诊断。

**示例配置**：
```json
{
  "ConnectionGovernance": {
    "FirstPacketTimeoutSeconds": 5,
    "IdleTimeoutSeconds": 30,
    "MaxConnections": 10000,
    "MaxConnectionsPerIp": 4,
    "MaxConnectionsPerUser": 1,
    "DespawnGracePeriodSeconds": 15
  }
}
```

---

## 3. 清理来源枚举（ConnectionCleanupSource）

| 枚举成员 | 触发语义 |
| --- | --- |
| `FirstPacketTimeout` | 首包超时（幽灵连接）：建连后 N 秒未收到任何数据 |
| `IdleTimeout` | 空闲超时（闲置/离线连接）：收到数据后 N 秒无活动 |
| `Corrupted` | 发送损坏（`MarkAsBroken`）：发送遇致命异常（如 writer 已 completed） |
| `ClosedEvent` | Closed 事件：TouchSocket / GameConnection Closed 事件 |
| `ConnectionLimit` | 全局连接数上限拒绝 |
| `PerIpLimit` | 每 IP 连接数上限拒绝 |
| `PerUserLimit` | 每用户连接数上限拒绝 |

**结构化清理日志**（spec 4.4.1a）：
```
[ConnectionCleanup] Id={connectionId} Source={enum} Characters={count} Result={Cleaned|AlreadyRemoved}
```

---

## 4. 治理统计口径（ConnectionManagerStatistics 扩展）

| 统计字段 | 含义 | 累加时机 |
| --- | --- | --- |
| `GhostConnectionCleanupCount` | 幽灵连接清理次数 | 首包超时（`FirstPacketTimeout`）清理完成 |
| `CorruptedConnectionCount` | 损坏连接次数 | `MarkAsBroken`/Closed 事件映射 Corrupted 清理 |
| `DuplicateConnectionRejectedCount` | 重复/超限连接拒绝次数 | 全局/IP/用户上限拒绝（含被拒新连接） |
| `UnboundConnectionCount` | 当前未绑定角色连接数 | 注册时（UserId null）递增；绑定角色/清理时递减 |

**运维查询**：`IConnectionManager.GetStatistics()` 返回上述字段；`ActiveConnections` 与 `_connections.Count` 一致。
治理统计为内存计数器，跨网关一致性由既有集群协调机制承载。

---

## 5. 客户端单连接编排（ClientConnectionCoordinator）

- 三类建连请求（登录/进游戏/重连）统一经 `IClientConnectionCoordinator.RequestConnectAsync(kind)` 互斥编排。
- 返回 `true` = 本请求实际执行 TCP 建连（唯一夺锁路径）；返回 `false` = 连接已在线被复用或另有路径在建连（不得重复建连）。
- `LastFirstPacketLatencyMs` 观测"TCP 建立 → 首包发出"时延；超过 1 秒输出诊断日志。
- 启动阶段预连接（`HundunWorldGame.InitializeNetworkManager`）已移除，建连只允许在业务意图触发点发起。

---

## 6. 运维排查指引

| 场景 | 现象 | 排查 |
| --- | --- | --- |
| 幽灵连接 | `FirstPacketTimeout` 清理日志 | 客户端是否发首包；`ClientConnectionCoordinator` 是否接管建连 |
| 重复连接 | `PerIpLimit`/`PerUserLimit` 拒绝日志 + `DuplicateConnectionRejectedCount` | 客户端是否多路径并发建连（应经协调器互斥） |
| 损坏连接 | `Corrupted` 清理日志 + "Writing is not allowed" 异常 | 客户端是否异常关闭；发送路径是否绕过 `_sendLock` |
| 未绑定闲置连接 | `UnboundConnectionCount` 偏高 | 连接建立后未完成认证/角色绑定 |
| 连接数超限 | `ConnectionLimit` 拒绝 + 客户端收到"服务器连接数已满"提示 | 集群整体连接数规划 |
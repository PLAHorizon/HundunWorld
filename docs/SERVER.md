# 服务端架构 · SERVER

> **最后更新**：2026-06-15 · 配套文档：[NETCODE](./NETCODE.md) · [NETWORK_PROTOCOL](./NETWORK_PROTOCOL.md) · [CLIENT](./CLIENT.md)

---

## 1. 服务端总览

服务端由 4 类宿主组成，共享 Orleans 集群 + 持久化层：

| 宿主 | 项目 | 角色 | 入口 |
|------|------|------|------|
| **Silo** | `Horizon.Orleans.Silo` | Orleans 主集群（承载所有 Grain） | `Program.cs:53` Main |
| **游戏网关** | `Horizon.Game.Gateway` | 客户端连接入口 + fanout 分发 | `Program.cs` |
| **Web API** | `Horizon.WebApi` | ASP.NET Core REST + IdentityServer | — |
| **IM 网关** | `Horizon.IM.Gateway` | 即时通讯网关 | — |

---

## 2. Horizon.Orleans.Silo（主集群）

### 2.1 入口

`Horizon.Orleans.Silo/Program.cs`：
- 命名空间：`Horizon.Orleans.Silo`（`:44`）
- `public static async Task<int> Main(string[] args)`（`:53`）

### 2.2 集群配置

| 项 | 值 | 来源 |
|----|-----|------|
| Clustering | **AdoNet（SQL Server）** | `ClusteringSiloOptions.SqlServer` |
| ClusterId | `dev` | `appsettings.json:47` |
| ServiceId | `BaseService` | `appsettings.json:48` |
| Silo 端点 | `192.168.1.78:11111` | `appsettings.json:49` |
| Gateway 端口 | `30000` | `appsettings.json:50` |
| Dashboard | `http://192.168.1.78:1199`（用户 `Horizon`） | `DashboardOptions` |

### 2.3 Silo 依赖（10 个项目）

```
Core.Abstract → Core → Entities → Game.Message → IM.Message
→ Orleans.Grains → Orleans.Interface → Share
→ Strategy.Storage.Redis → IoT.MQTT
```

额外 NuGet：EF Core Design、OpenTelemetry、CSRedis、Polly。

---

## 3. Horizon.Game.Gateway（游戏网关）★

### 3.1 监听端口

`appsettings.json:30-44` 的 `Network` 节：

| 端口 | 协议 | 用途 |
|------|------|------|
| **7789** | TCP | 主业务连接（实际使用） |
| 8889 | UDP | 预留 |
| 8890 | WebSocket | 预留/Web 客户端 |

监听 IP：`192.168.1.78`（内网，见 [TECH_DEBT.md](./TECH_DEBT.md)）

### 3.2 Orleans Client 接入

`Horizon.Game.Gateway/Program.cs:276` —— `.UseOrleansClient((context, client) => { ... })`：

```
L280  绑定 ClusteringSiloOptions → OrleansClusteringDbOptions
L283  绑定 Orleans 节 → OrleansOptions
L285-288  空连接串校验（抛 InvalidOperationException）
L291-294  client.UseAdoNetClustering(options => {
            options.ConnectionString = ...;
            options.Invariant = "Microsoft.Data.SqlClient";
          })
L295-299  .Configure<ClusterOptions> → ClusterId / ServiceId
L301-304  .Configure<ClientMessagingOptions> → ResponseTimeout = 30s
L305-309  .Configure<GatewayOptions> → PreferredGatewayIndex = 0
                                    + GatewayListRefreshPeriod = 15s
```

> ⚠️ 关键超时修复：`ResponseTimeout=30s` + `GatewayListRefreshPeriod=15s` 是为避免历史 30 分钟超时问题。`OrleansStartupConnectionRetryFilter` 提供启动重连容错。

### 3.3 DI 装配（核心 fanout 数据流装配点）

`Program.cs:244-260` —— 整个下行数据流的装配：

```
GatewayZoneShardFanoutSource   一个对象同时实现 IZoneShardFanoutObserver + IZoneShardFanoutSource
  ├─ IZoneShardFanoutObserver  绑定到 GatewayZoneShardFanoutSource（被 grain 推送）
  ├─ IZoneShardFanoutSource    绑定到 GatewayZoneShardFanoutSource（被 dispatcher drain）
  ├─ ISessionRegistry          = ConnectionManagerSessionRegistry
  ├─ IClientPacketSink         = GameConnectionPacketSink
  └─ AddHostedService<SyncDispatcherHostedService>()
```

### 3.4 fanout 数据流（三个适配器类）

`Services/GatewaySyncWiring.cs`：

#### `GatewayZoneShardFanoutSource`（`:26-104`）
桥接 grain 回调线程与 dispatcher 工作线程：
- 同时实现 `IZoneShardFanoutObserver`（被 ZoneShardGrain 推送）和 `IZoneShardFanoutSource`（被 dispatcher drain）
- 中间用 **`Channel.CreateBounded<FanoutEvent>(8192)` + `BoundedChannelFullMode.DropOldest`** 做无锁队列
- 反压时**丢旧保新**（游戏同步容忍丢帧，不容忍延迟）

#### `ConnectionManagerSessionRegistry`（`:111-132`）
把 `ISessionRegistry.TryGetEndpoint(sessionId)` 适配到 `IConnectionManager.GetConnectionByUserId()`。

#### `GameConnectionPacketSink`（`:141-188`）
发包最终环节：
1. `SyncPacketCodec.Encode` 编码 SyncPacket 为 6B 帧
2. 包装为 `SyncFrameMessage`
3. `HorizonMessageAdapter.PackMessage(..., compress: false)` 打包为 8B 外层线路帧
4. `conn.SendAsync(wireBytes)` 异步发送（fire-and-forget）

### 3.5 后台调度循环

`Services/SyncDispatcherHostedService.cs:24-128`：
- 灰度开关 `GatewayOptions.UseSyncPacketDispatch`（`appsettings.json:27 = true`）
- 启动时向 `IZoneShardGrain(key=0)` 调 `SubscribeFanoutAsync(subscriptionId, observer)` 注册推送
- 主循环不停调 `dispatcher.RunOnceAsync()`（内部 `TryDequeueAsync` 自阻塞，无需 sleep）
- 退出时 `UnsubscribeFanoutAsync` 清理

### 3.6 角色指纹（防多开）

`Services/CharacterFingerprintService.cs`（Redis 分布式锁实现）：
- Key：`character:fingerprint:{characterId}`，**TTL 5 分钟**
- 用 `RedisCache.AcquireLockAsync` 做分布式锁，3 次重试
- 维护 `connection:characters:{connectionId}` 集合，断线时批量清理
- 过期指纹可被新连接抢占（`:72-78`）

接口契约：`Horizon.Game.Core/Interfaces/ICharacterFingerprintService.cs:11-51`，方法 `TryAcquire` / `Release` / `Refresh` / `ReleaseByConnection` / `IsOnline`。

---

## 4. SyncPacketHandler（同步通道总入口）

`Horizon.Game.Core/Handlers/SyncPacketHandler.cs:15-322`，按 `packet.Kind` 分派：

### 4.1 HandshakePacket（握手）
- 调 `IPlayerSessionGrain.HandshakeAsync`
- 注册实体到 `IZoneShardGrain`
- 返回 `WorldPatchManifestPacket`

### 4.2 InputPacket（输入上行）
- 调 `IPlayerSessionGrain.ReceiveInputAsync`（校验/去重）
- 转发到 `IZoneShardGrain.SubmitInputAsync`（权威模拟）
- 返回 `InputAckPacket`

### 4.3 ReconnectResumePacket（重连）
返回 4 种恢复决策：
| 决策 | 含义 |
|------|------|
| `ResumeIncremental` | 增量恢复（最常见，会话还在） |
| `RequireLauncherPatch` | 需打补丁（版本过旧） |
| `ResendFullChunks` | 重发全部 chunk（会话已丢） |
| `ForceReLogin` | 强制重新登录（彻底失效） |

---

## 5. Orleans Grain 层 ★

### 5.1 Grain 契约位置

Grain 接口在独立的 **`Horizon.Orleans.Interface/`** 项目（不是 Grains 项目本身）：
- `ICharacterGrain.cs`、`World/IZoneShardGrain.cs`
- `IZoneShardFanoutObserver`（observer 接口，grain → gateway 推送）

### 5.2 ZoneShardGrain（空间权威模拟核心）

`Horizon.Orleans.Grains/World/ZoneShardGrain.cs:20-612`

**shard 身份** = Orleans grain 的 **long 主键**（`this.GetPrimaryKeyLong()`，见 `:59,101,188,...`）。当前业务侧硬编码使用 shard 0。

**Tick 循环**（`:48-66` `OnActivateAsync` 注册 1/60s 定时器）：
- 每 tick 遍历 `_simulatedEntities`
- 对每个实体的 `PendingInputs` 调 `MovementValidator.Validate`
- 产生 `EntityDelta`(Update) + 可能的 `CorrectionPacket`
- 打包成 `SnapshotPacket` 调 `BroadcastSnapshotAsync`（`:302-312`）

**SimulatedEntity 内部结构体**（`:582-611`）：位置 XYZ、Vz 速度、MaxSpeed、PendingInputs 队列、客户端上报终点、跳跃计数、施法状态、Hp。

**Fanout 推送**（`BroadcastSnapshotAsync :462-543`）：
- 每个 `EntityDelta` 序列化进 `WorldChunkDiffPacket.Payload`
- 通过 AOI `_aoi.GetSubscribers(mortonKey)` 查受众 sessionId
- 遍历所有 `_fanoutObservers` 调 `OnChunkDiffAsync`
- Correction 包走 `EventPacket` → `WorldChunkDiffPacket`（**可靠+高优先级通道**）

**AOI**：委托 `ZoneShardAoi`（基于 Morton 键，`MortonCodec.cs`）做空间索引。

**瞬态**：断线时 `PlayerSessionGrain` 显式调 `RemoveSessionAsync` 清理。

构造函数（`:36-46`）实例化 `MovementValidator`：
```csharp
new MovementValidator(new MovementValidator.Options {
    PositionEpsilon = MovementValidator.DefaultPositionEpsilon,  // 0.5f
    HardSpeedCap    = MovementValidator.DefaultHardSpeedCap,     // 20f
    MaxSpeed        = MovementFormula.DefaultMaxSpeed,           // 6f
    TickDtSeconds   = 1f / 60f,
});
```

### 5.3 PlayerSessionGrain（会话状态）

`Horizon.Orleans.Grains/World/PlayerSessionGrain.cs:25-113`

- **瞬态 grain**（不落盘），委托给 `PlayerSessionState`
- `HandshakeAsync` / `ReceiveInputAsync`（去重）
- 输入接受结果：`InputAcceptResult.Accepted / Duplicate / Invalid / TooOld`
- `BuildInputAckAsync` 推进 `LastProcessedClientTick`
- `ResumeAsync` 返回 4 种重连决策

### 5.4 CharacterGrain（持久化角色状态）

`Horizon.Orleans.Grains/CharacterGrain.cs:29`（共 **937 行**）

- `[PersistentState("character", "GameStore")] IPersistentState<CharacterState>`（`:44`）—— 走 Orleans 持久化
- `OnActivateAsync` 从数据库 fallback 加载（`:58-81`）
- 涵盖完整 RPG 玩法：创建/进入/移动/战斗/技能/装备/任务/社交/公会/师徒/结拜/五行铸造

> ⚠️ 这是**老的 RPC 风格角色管理**，与 ZoneShardGrain 的高频移动模拟是**并行的两套系统**。详见 [NETCODE.md §10](./NETCODE.md) 和 [TECH_DEBT.md](./TECH_DEBT.md)。

### 5.5 其他 Grain（100+）

Grains 项目极其庞大，覆盖：
- **花卉电商全链路**（Flower* 系列）
- **IM**（IMUser / IMGroup）
- **支付**（Payment / Alipay / Wechat Pay）
- **跨服**（CrossServer*）
- **副本**（DungeonGrain）
- **交易**（TradeGrain）
- **排行榜**（RankingGrain）
- **社交**（好友/邮件/聊天/组队/PK/决斗）

---

## 6. 持久化拓扑

### 6.1 存储矩阵

| 存储 | 用途 | 配置位置 |
|------|------|---------|
| **SQL Server** (`Orleans` 库) | Orleans clustering 表 + Grain 持久化 | `ClusteringSiloOptions.SqlServer` |
| **SQL Server** (`Game` 库) | CharacterGrain 业务数据（GameStore） | `DatabaseOptions.Game` |
| **SQL Server** (`Basic` 库) | 账户/基础数据 | `DatabaseOptions.Basic` |
| **Redis 哨兵集群** | 会话/分布式锁/缓存 | `DataBase.RedisMasters/Slaves/Sentinels` |
| **MongoDB** | 大 schema 变更领域（Grains 引用） | MongoDB.Driver |
| **LiteDB** | 客户端本地存储（GengDi 启动器） | `Horizon.Game.GengDi` |

### 6.2 Redis 哨兵配置（`appsettings.json`）

```
Masters:  127.0.0.1:9379 (×2)
Slaves:   127.0.0.1:9679 / 9779 / 9879
Sentinels: 127.0.0.1:6379 (×2)
密码: DB65F7F9C
```

### 6.3 数据库连接串

`DatabaseOptions`（`appsettings.json:143-164`）—— 5 个库共用同一 SQL Server 实例（`Data Source=.`）：
- `Basic` / `Game` / `Article` / `Support` / `Xingguang`
- 连接串统一：`Pooling=True;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True`

### 6.4 SQL 迁移脚本

`scripts/sql/`：
- `001_world_state.sql` —— 世界状态表
- `002_flower_ai_stored_procedures.sql` —— 花卉 AI 存储过程
- `003_fix_orleans_storage_pk_violation.sql` —— Orleans 存储 PK 冲突修复

---

## 7. 关键配置（appsettings.json 字段说明）

### 7.1 Gateway 节

| 字段 | 默认值 | 含义 |
|------|--------|------|
| `MaxConnections` | 10000 | 最大连接数 |
| `ConnectionTimeout` | 300 | 连接超时（秒） |
| `HeartbeatInterval` | 30 | 心跳间隔（秒） |
| `EnableCompression` | true | 启用压缩 |
| `BufferSize` | 8192 | 缓冲区大小 |
| `UseSyncPacketDispatch` | **true** | **fanout 分发灰度开关** |

### 7.2 Security 节

| 字段 | 默认值 |
|------|--------|
| `MaxLoginAttempts` | 5 |
| `LoginAttemptsWindowMinutes` | 15 |
| `SessionTimeoutHours` | 24 |
| `RequiredClientVersion` | `1.0.0` |
| `EnableRateLimiting` | true |
| `MaxRequestsPerMinute` | 60 |

### 7.3 Authentication 节

| 字段 | 默认值 |
|------|--------|
| `TokenExpirationMinutes` | 1440（24 小时） |
| `RefreshTokenExpirationHours` | 168（7 天） |
| `EnableMultipleDeviceLogin` | true |
| `MaxConcurrentSessions` | 3 |

### 7.4 CharacterManagement 节

| 字段 | 默认值 |
|------|--------|
| `MaxCharactersPerAccount` | 5 |
| `CharacterNameMinLength` | 2 |
| `CharacterNameMaxLength` | 12 |
| `EnableSensitiveWordFilter` | true |
| `CharacterDeletionCooldownHours` | 24 |

---

## 8. Observer 推送模式（grain → gateway）

ZoneShardGrain 不直接持有 Gateway 引用，而是通过 **Orleans Observer 模式**解耦：

```
Gateway 启动时:
  IZoneShardGrain(key=0).SubscribeFanoutAsync(subscriptionId, observer)
    ↓ observer 是 GatewayZoneShardFanoutSource（跨进程代理）

ZoneShardGrain.TickAsync 每 1/60s:
  BroadcastSnapshotAsync
    ↓ 遍历 _fanoutObservers
    ↓ 调 observer.OnChunkDiffAsync(packet)
    ↓ Orleans 运行时把调用转发到 Gateway 进程

Gateway 进程内:
  GatewayZoneShardFanoutSource.OnChunkDiffAsync
    ↓ Channel<FanoutEvent>(8192).Enqueue
    ↓ SyncDispatcherHostedService.RunOnceAsync drain
    ↓ GameConnectionPacketSink.Send
```

**优势**：grain 不需要知道有多少 Gateway、有多少客户端，只负责"产出快照 + 通知订阅者"。

---

## 9. 可观测性

| 能力 | 实现 | 端点/位置 |
|------|------|---------|
| Orleans Dashboard | 内置 | `http://192.168.1.78:1199`（用户 `Horizon`） |
| 链路追踪 | OpenTelemetry（Silo + Gateway） | appsettings 配置 |
| 指标采集 | Prometheus + Grafana | `monitoring/` 目录 |
| 告警 | Alertmanager | `monitoring/` 目录 |
| Grain 统计 | `EnableStatistics=true` + `EnablePerformanceCounters=true` | `Orleans` 节 |

---

## 10. 压测

`Horizon.PerformanceTests`（NBomber 6.1.0 + NBomber.Http 6.0.0）：
- 针对 Gateway / Grain 的负载测试
- 报告输出到 `bin/.../reports/`（⚠️ 27 份报告混入 git，详见 [TECH_DEBT.md](./TECH_DEBT.md)）

---

## 11. 相关文档

- [NETCODE.md](./NETCODE.md) — ZoneShardGrain 的 Tick 逻辑与 MovementValidator
- [NETWORK_PROTOCOL.md](./NETWORK_PROTOCOL.md) — SyncPacketHandler 的包分派
- [ARCHITECTURE.md](./ARCHITECTURE.md) — 服务端在整体架构中的位置
- [KEY_FILES_INDEX.md](./KEY_FILES_INDEX.md) — 服务端关键文件索引

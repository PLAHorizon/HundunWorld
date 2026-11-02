# DS 脚本化世界生成（怪物 / NPC / 特殊事件）设计方案

> 状态：**方案已确认（决策记录见 §10），进入落地阶段**。当前已落地范围以 §9 分期表为准。
> 日期：2026-10-03
> 范围：DS 权威模拟层新增"脚本化生成"能力，并预留**客户端授权角色通过指令在指定位置触发 DS 生成**的玩法通道，指令执行由 **Orleans Grain 权威裁决**。

---

## 1. 目标与非目标

### 目标
1. DS（`Horizon.DsHost`）能在运行时动态生成三类对象：**怪物**、**NPC**、**特殊事件**。
2. 支持"玩法"语义：有生命周期（存活时长 / 到期清除）、有可见性（AOI + 晚加入补发）、有交互（NPC 交互、事件播报）。
3. 预留客户端指令通道：已登录的**特定授权角色**可在游戏内输入指令 → 在指定位置触发 DS 生成。
4. 指令必须经 **Grain 权威授权确认**后方可执行——客户端与 DS 都无权自行判定。

### 非目标（本期不做）
- 不做怪物/NPC 的行为编辑器、AI 蓝图、技能表扩展（沿用现有 `MonsterAiCore`）。
- 不做 UE5 DS 灰度通道的对应能力（现有 DS 通道为唯一目标）。
- 不做面向玩家的付费/活动事件编排系统；`特殊事件` 只定义契约与最小实现骨架，内容后续填充。

---

## 2. 现状基线（已实测核实的锚点）

| 事实 | 证据位置 |
|---|---|
| DS 是独立控制台进程，自带 60Hz 权威模拟循环，**不是** Orleans Silo；Orleans 仅作可选只读客户端 | `Horizon.DsHost/Program.cs:91-134`、`DsHostRunner.cs:402 TickLoopAsync` |
| DS 权威世界用普通字典持有实体，**不走 ECS**（ECS/Arch 仅客户端预测/插值层） | `Horizon.Game.Core/Sim/Server/DsWorldSession.cs:383 _monstersById`、`:419 _spawnPoints` |
| 怪物体系已完整：刷怪点、AI FSM、重生、战斗、掉落 | `Horizon.Game.Sim/SpawnDirectorCore.cs`、`MonsterAiCore.cs`、`MonsterTemplateCatalog.cs`、`CombatCore.cs` |
| 实体 ID：`(spawnPointId << 32) | (slot+1)` | `SpawnDirectorCore.cs:183-189 ComputeEntityId` |
| 快照下行：`DsWorldSession.BuildSnapshot()` → `EntityDelta{Kind=Spawn/Update/Despawn}` → `DsSnapshotBatch` → 网关按 sessionId 定向投递 | `DsWorldSession.cs:1227`、`Horizon.Game.Message/Sync/SyncPackets.cs:375,407` |
| 网络实体类型已预留 `Npc=3 / Monster=4 / Projectile=5 / Item=6` | `Horizon.Game.Message/Network/GameplayMessages.cs:748 NetworkEntityType` |
| **网关 → DS 下行已有先例**：`SessionEnd = 7`，复用 DS 已建立的单向 TCP（尾部追加，旧端遇未知 kind 跳过 → 前向兼容） | `Horizon.Game.Core/Sim/Server/DsUplinkProtocol.cs:61-80`、`Horizon.Game.Gateway/Services/DsUplinkServer.cs:76-110` |
| 客户端→网关为 TouchSocket TCP + `HorizonMessagePacket`；处理器按 `Body.Type/ServiceType` 首匹配，程序集扫描自动注册 | `Gateway/Network/GameNetworkServer.cs`、`ClientPacketPipeline.cs`、`Gateway/Program.cs AddAllMessageHandlers` |
| 身份绑定：网关管线 Stage4 校验 `MessageHeader.AuthToken`（AES-256-CBC + HMAC-SHA256），Stage6 建立 `characterId ↔ connectionId` 权威映射；`SessionBindingValidator` 可回答"该角色是否绑定该连接" | `ClientPacketPipeline.cs` Stage3/4/6、`Gateway/Services/SessionBindingValidator.cs` |
| **`IGmCommandGrain` 已存在但：①方法全靠调用方自报 `gmId`，内部零鉴权；②全仓零调用方（死契约）** | `Horizon.Orleans.Interface/World/IGmCommandGrain.cs` |
| 服务端已有 `SystemCommand/PlayerManagement/ServerManagement` 消息 schema，但**无任何处理器认领**（死 schema） | `Horizon.Game.Message/Network/SystemAdminMessages.cs` |
| 现成可扩展的空位：`MemoryPackUnion` tag **238 起全空闲**；`MessageType` **1109 起空闲**；`DsUplinkMessageKind` **8 起空闲** | 实测 grep 结论 |

**结论：缺口是三块**——① DS 侧没有"运行时按指令生成"的入口（只有静态刷怪点）；② 没有 NPC/事件的实体化；③ 没有真正带鉴权的授权层。聊天链路（`ChatHandler` → `IChatGrain` → `PlayerPushService`）是现成的指令通道模板，但它的 `req.SenderId` 是客户端自报的，**不可照抄**。

---

## 3. 核心抽象

三类生成物统一为 **脚本化世界实体（Scripted World Entity）**，与静态刷怪点实体并列存在于同一张实体表中，共用快照、AOI、Despawn、补发链路。

```
ScriptedKind
├── Monster      → 复用 DsMonster + MonsterAiCore（可被击杀、掉落、仇恨）
├── Npc          → 新增 DsNpc（无战斗、可交互、通常常驻）
└── Event        → 新增 DsWorldEvent（两种形态，见 §3.2）
```

### 3.1 生命周期（三类共用）
- `TimeToLiveSeconds`：0 = 永久；>0 = 到期自动 Despawn。
- `OwnerCharacterId`：记录由哪个角色触发（审计与"谁可清除"判定用）。
- 显式 `Despawn(entityId)` 指令，按 `NetworkEntityType` 分支清除。
- **Despawn 必须全量广播**（不做 AOI 裁剪），沿用 `DsWorldSession` 现有的离线实体全广播机制，避免幽灵残留。
- 晚加入/重连者：脚本实体纳入会话级 `InitialSpawnSnapshots` 补发（与玩家/怪物同一不变量）。

### 3.2 特殊事件的三态（已确认：首批含区域触发器）
| 形态 | 是否建实体 | 通道 | 例 |
|---|---|---|---|
| **表现型** | 否 | 复用 `SyncEventKind.Vfx/Sfx` 定向广播到受众会话 | 区域光柱、天气切换、屏幕特效、全服公告 |
| **玩法型** | 是 | 建实体 + TTL + 全广播 Spawn/Despawn | 世界 BOSS、限时事件怪群、争夺点标记物 |
| **触发型** | 是（区域实体） | 新增 `DsTriggerZone`，按玩家权威坐标做进出沿检测后派发内联事件 | 踏入石阵召唤守卫、进入区域开启限时怪群、禁区告警 |

表现型不占实体槽、不进快照，成本极低；玩法型走完整实体生命周期；触发型见 §6.5（DS 当前**完全没有** trigger volume 概念，需新增）。

### 3.3 实体 ID 空间（零冲突方案）
不改 `ComputeEntityId`。脚本实体使用**负数 `spawnPointId` 域**：

```
scriptedEntityId = SpawnDirectorCore.ComputeEntityId(-scriptSerial, slot + 1)
// 高 32 位 = (uint)(-scriptSerial) ≥ 0x80000000，与所有正值静态刷怪点天然不相交
```

`scriptSerial` 由 `DsWorldSession` 内单调递增计数器分配（1,2,3…），每 DS 会话唯一。判定"是否脚本实体"：`(int)(entityId >> 32) < 0`。

---

## 4. 授权模型（Grain 权威）

### 4.1 唯一裁决点：改造复用 `IGmCommandGrain`（已确认）
不新建 Grain。在既有 **`IGmCommandGrain : IGrainWithIntegerKey`**（全局单例，Key=`0` 表示系统级 GM 权威域）上补齐**真正的鉴权**与脚本授权裁决能力，并把它**首次接入运行时**（当前全仓零调用方）。

现有 8 个方法的签名保持语义不变，但 `gmId` 的**来源**改为：网关从 `MessageHeader.AuthToken` 解出的 `CharacterId`（或 WebApi 的 `X-Admin-API-Key` 管理会话映射到的操作者 id），**不再接受客户端 body 自报**。Grain 内部对每个 `gmId` 查权限表并 `AssertPermission(flags)`，越权一律 `GmOperationResult{Success=false, ErrorMessage="denied"}` + 落审计。

接口版本需从 `[Version(1)]` 升到 `[Version(2)]`（新增方法）——**代价**：网关与 Silo **必须同批构建部署**，新旧混跑会 `TypeLoadException`；上线按既有流程先终止旧 Silo 进程。

```csharp
[GenerateSerializer]
public enum ScriptPermissionFlags : uint   // [Flags]
{
    None          = 0,
    SpawnMonster  = 1u << 0,
    SpawnNpc      = 1u << 1,
    FireEvent     = 1u << 2,   // 表现型事件（Vfx/Sfx/公告）
    SpawnEvent    = 1u << 3,   // 玩法型事件（建实体 + TTL）
    PlaceTrigger  = 1u << 4,   // 触发型事件（放置 DsTriggerZone）
    DespawnOwned  = 1u << 5,   // 清除自己生成的（/despawn mine）
    DespawnAny    = 1u << 6,   // 清除他人脚本生成物的（/despawn <id>）；**不**解锁静态刷怪点——静态点不在脚本 ID 域内，DS 一律拒绝按 ID 清除
    MultiEntity   = 1u << 7,   // 单次 count > 1
    BypassDist    = 1u << 8,   // 允许超距（**不**放松分片实体上限，也**不**涉及分片归属）
    GlobalCast    = 1u << 9,   // **保留位，无判定消费**（全服公告走 GM 运维面的 BroadcastAnnouncementAsync，由 GmOps 把关）
    Query         = 1u << 10,  // 查询授权与审计
    GmOps         = 1u << 11,  // GM 运维（封禁/补偿/邮件/公告/传送/角色查询），与世界编排位分列
    WorldDirector = 0x7FF,     // 世界编排 + 查询（不含 GmOps）
    Operator      = GmOps | Query,
    GameMaster    = Admin = WorldDirector | GmOps,
}
```

**落地口径与上方设计稿的差异，以 `ScriptAuthorityCore.RequiredFlagFor` 为唯一真值源**；
每位是否有真实消费方、以及"设计稿里写了但代码里没有"的位（`DevOnly` 因无写入路径已删除，见 §11）见 §11 的
[权限位与消费方对齐审计](#权限位与消费方对齐审计2026-10-04)。

`IGmCommandGrain` 新增方法（追加在契约末尾，`[Id]` 顺延）：
| 方法 | 用途 |
|---|---|
| `Task<GmScriptGrantSnapshot> GetScriptPermissionsAsync(long operatorId)` | 读授权位、配额余量、到期时间 |
| `Task<GmOperationResult> GrantScriptAsync(long operatorId, ScriptPermissionFlags flags, TimeSpan ttl, long callerId, string reason)` | 授权；内部校验 `caller` 自身位 ⊇ 目标位，且**永不转授** `DevOnly` |
| `Task<GmOperationResult> RevokeScriptAsync(long operatorId, long callerId, string reason)` | 回收 |
| `Task<GmScriptVerdict> TryConsumeScriptAsync(GmScriptTicket ticket)` | **裁决 + 扣预算 + 签发令牌**（核心，唯一真值源） |
| `Task<GmAuditEntry[]> QueryScriptAuditAsync(long? operatorId, int limit)` | 脚本操作审计查询（复用现成 `GmAuditEntry`） |

**`TryConsumeScriptAsync` 一次调用完成全部判定**，DS 与网关不再各自判断（避免多源真值）：
1. 权限位是否覆盖本操作；
2. 模板 `templateId` 是否在白名单目录内；
3. 冷却（默认 1 次 / 5s）与配额（默认 60 次 / 分钟）；
4. 目标点与角色当前权威坐标的距离（默认 ≤ 50m，除非持 `BypassDist`）；
5. 目标 shard：**不给"跨 shard"这个概念留口子**——令牌的 `TargetShardId` 直接取服务端读到的操作者所在 shard，客户端无从指定别人的片；DS 侧再拒一切不属于自己声明的片（配置错位的 tripwire，见下方"shard 归属口径修正"）；
6. 该 shard 脚本实体总量是否越上限（默认 200）；触发区域另有独立上限（默认 32）；
7. 通过后落审计条目，返回 `GmScriptVerdict{ Allowed, Reason, GrantToken, RemainingBudget, EntityIdHint }`。

### 4.2 授权结果如何到达 DS —— 签名令牌（关键设计）

**DS 的 60Hz tick 内绝不做跨进程 RPC。** 因此裁决结果以**自证式签名令牌**随下行帧带下来：

```
Grain 裁决通过
  → 产出 ScriptGrantToken {
        Nonce, CharacterId, ScriptedKind, TemplateId,
        TargetShardId, Position(Z-up), Count, TtlSeconds,
        IssuedAtUtc, ExpiresUtc(默认 5s), Signature
    }
  → Signature = HMAC-SHA256(token 规范字节, DsScriptSigningKey)
```

- 密钥来源：与 `Horizon.Core/Security/UserAuthTokenProvider` 同族的 AES-256-CBC + HMAC-SHA256 体系，新增配置项 `DsScriptSigningKey`，由网关与 DS **共享**（沿用现有 `DsControlToken` 的共享密钥下发方式）。
- **DS 只验签、只入队，不再判断"谁有权"**：验签失败/过期/`TargetShardId != 本 DS 持有 shard`/`Nonce` 重放 → 直接丢弃并计数告警。
- 令牌 5s 过期 + 单次消费（DS 侧维护已消费 Nonce 的有界 LRU），杜绝重放。

### 4.3 端到端链路

```
[客户端 Flax]
  玩家输入 "/spawn 墨麒麟 3 @12.5,4.0,-30"  (或走 §6.4 的结构化消息，不在客户端解析文本)
        │  GmScriptCommandMessage (tag 238)
        ▼
[网关 Horizon.Game.Gateway]
  WorldScriptCommandHandler（新，Core/Handlers）
    Stage4 已鉴权 → 身份只取 MessageHeader.AuthToken 解出的 CharacterId
                 + SessionBindingValidator 二次确认"角色↔本连接"
                 （绝不信任 body 自报的 CharacterId/SenderId）
    → GetGrain<IGmCommandGrain>(0).TryConsumeScriptAsync(ticket)   // ticket.OperatorId 来自上面的权威身份
    ├── Allowed=false → 回 GmScriptResultMessage(拒绝原因) 给发起者，链路终止
    └── Allowed=true  → 走 DS 通道下发：
             IDsScriptDispatcher.DispatchAsync(shardId, token)
               → DsUplinkServer 复用现有 DS 长连接，新 kind = 8 (ScriptedSpawn)
                     ▼
[DS Horizon.DsHost]
  DsUplinkClient 收到 kind=8 → 验签 → 入 _pendingScriptCommands
  下一个 tick 边界（TickSpawnDirector 之后、TickMonsters 之前）按到达序号顺序消费
    → DsWorldSession.SpawnScriptedMonster / SpawnScriptedNpc / FireScriptedEvent
    → 本 tick 的 BuildSnapshot 自动产生 EntityDelta{Kind=Spawn}
                     ▼
        原有下行链路不变 → 各客户端看到新实体
                     ▼
  跨层事实（若有）→ DsGameplayEvent → 网关 → Orleans（复用 OrleansGameplayWiring）
```

**降级**：`UseDS == false`（Orleans 灰度路径）时，同一 `TryConsumeScriptAsync` 通过后改调 `IZoneShardGrain`（或补齐 `IInstanceGrain.SpawnMonsterAsync` 的调用方，该方法目前存在但零调用方）。两条路径共用授权层，只差最后一步投递。

### 4.4 授权来源与入口（2026-10-04 按拓扑口径重做）

**入口分工是硬约束**：登录走 WebApi、IM 聊天走 IM Gateway、**其余全部走 GameGateway**；服务端固定为 DS + GameGateway + Orleans + Redis + SQL Server，不为便利性引入新进程或新管理面。`Horizon.WebAdmin` 不承载本游戏的运营面。

因此授权管理**不做成 HTTP 管理端点**，而是游戏内指令，经 GameGateway 进 Grain：

| 指令 | 语义 | 落点 |
|---|---|---|
| `/grant <角色ID> <权限位数值> [小时]` | 授予脚本权限 | `IGmCommandGrain.GrantScriptAsync`（**不生成令牌、不经 DS、不占生成配额**） |
| `/revoke [角色ID]` | 回收（省略即本人） | `RevokeScriptAsync` |
| `/audit [条数]` | 查审计 | `QueryScriptAuditAsync`，条目以文本回给操作者 |
| `/scriptperm` | 查自己的授权与配额 | `GetScriptPermissionsAsync` / 裁决快照 |

调用者身份与生成类操作同源：只取 `MessageHeader.AuthToken` 解出的 `CharacterId`，客户端在消息体里**没有可自报的身份字段**。授予能否成立由 Grain 判定（须持 `GmOps` 位，或位列引导配置）。

**持久化仍是双轨**（这一层与入口无关）：
- **Redis 热层**：Grain 的 Orleans 持久化（`GameStore` 提供程序），裁决读路径只碰它；
- **SQLServer 权威层**：`gm_script_grant` 表。写顺序固定"先表后热层"，表写失败就不刷热层，避免"热层已生效、重启后消失"的假象；Grain 激活时若热层为空则从表重建。
- 建表脚本 `scripts/sql/gm_script_grant.sql`（幂等、纯 ASCII 注释以避开 sqlcmd 对无 BOM UTF-8 中文的静默不执行）。**本次不动 EF Migrations**：模型快照正被另一路改动占用，加迁移会冲突。

**引导**：`ScriptedSpawn:BootstrapAdminIds`（Silo 配置）解决"第一个 GM 由谁授予"。每次经引导授予都以 Warning 落审计；正式授权迁入 DB 后应清空该列表。`DevOnly` 位任何路径不可转授。

---

## 5. 数据契约变更清单

| # | 位置 | 变更 |
|---|---|---|
| C1 | `Horizon.Game.Message/Enums/MessageType.cs` | `ScriptCommand = 1109`（客户端 → 服务端唯一入口）、`ScriptResult = 1110`（服务端 → 客户端）。曾另立 `ScriptQuery = 1111`，因无人使用已删除——查询走 `ScriptCommand + Op=Query`，一个入口一套校验 |
| C2 | `Horizon.Game.Message/Network/WorldScriptMessages.cs`（新） | `GmScriptCommandMessage` `[NetworkMessage(238, ServiceType.System)]`、`GmScriptResultMessage` `(239)` |
| C3 | `Horizon.Game.Message/MessageUnion.cs` | 手写 `<generated-union-block>` 内追加 tag **238/239**（生成器不产出此处；`UnionRegistrationParityTests` 会把关） |
| C4 | `Horizon.Game.Core/Sim/Server/DsUplinkProtocol.cs` | `DsUplinkMessageKind.ScriptedSpawn = 8`（尾部追加，前向兼容）+ `DsScriptedSpawnEnvelope { ScriptGrantToken Token; byte[] CommandBytes; }` |
| C5 | `Horizon.Orleans.Interface/World/IGmCommandGrain.cs`（**改造**） | `[Version(1)]`→`[Version(2)]`；追加 §4.1 五个脚本方法；现有 8 方法补 `AssertPermission` |
| C5b | `Horizon.Orleans.Interface/World/GmScriptTypes.cs`（新） | `ScriptPermissionFlags`/`GmScriptTicket`/`GmScriptVerdict`/`GmScriptGrantSnapshot`/`ScriptGrantToken`（`[GenerateSerializer]`+`[Id(n)]`；Grain 状态类另加 `[MemoryPackable][MemoryPackOrder]`） |
| C5c | `Horizon.Orleans.Grains/World/GmCommandGrain.cs`（**改造**） | 权限表 + 裁决 + 令牌签发 + 审计；激活时从 DB 重建 Redis |
| C5d | `Horizon.Entities`（新 `GmScriptGrant` + EF 迁移）· `Horizon.Strategy.Storage.Redis`（`gm:script:perm:*`）· `Horizon.WebApi/Controllers/GmScriptController.cs`（新）· `Horizon.WebAdmin` 授权页（新） | 授权持久化与管理面（P1 内交付，见 §4.4） |
| C6 | `Horizon.Game.Message/Sync/SyncPackets.cs` | `EntityDelta` 增可选 `Scripted` 组件（携带 `ScriptedKind`、`TtlRemaining`、`InteractRadius`），或复用 `Profile` 槽位承载（**倾向后者，零改快照宽度**） |
| C7 | `Horizon.Game.Sim/ScriptedEntityCatalog.cs`（新） | 怪物模板复用 `MonsterTemplateCatalog`；新增 `NpcTemplateCatalog`、`WorldEventTemplateCatalog`（对称 3 个 dev 模板） |
| C8 | 配置 | `ScriptedSpawn: { Enabled(false), MaxPerShard(200), MaxDistanceM(50), RatePerMinute(60), CooldownSec(5), TokenTtlSec(5), DevBootstrapCharacterIds([]) }`；`DsScriptSigningKey` |

命名/序列化须遵守：Orleans Grain 类型注册规范、`MemoryPackable`+`MemoryPackOrder` 双标注、`Gm*` 前缀不复用（避免与死契约 `IGmCommandGrain` 混淆）。

---

## 6. DS 侧实现细节

### 6.1 新增类型
- `DsNpc`（对称 `DsWorldSession.cs:206 DsMonster`）：`EntityId / TemplateId / Position(Z-up) / Yaw / InteractRadius / ExpiresAtTick / SpawnedToClients`。**不进 `_monstersById`**，新增 `_npcsById`，避免污染战斗与 AI 遍历（`:1074 TickMonsters`、`:1301/:1485/:1688` 三处怪物遍历保持零成本）。
- `DsWorldEvent`：`{ EventKind, Center, Radius, ExpiresAtTick, PayloadBytes }`；`_eventsById`。
- `ScriptCommandQueue`：`ConcurrentQueue` + 到达序号；tick 边界单线程消费，保证与 `TickSpawnDirector` 一致的确定性顺序（刷怪点按 `SpawnPointId` 排序的既有纪律延续）。

### 6.2 快照构建扩展
`BuildSnapshot()`（`:1227`）现有对玩家与怪物的两段遍历，追加 NPC 与玩法型事件两段，产出 `EntityDelta{Kind=Spawn, Identity.EntityType = Npc/Monster}`。**坐标换轴沿用既有 Z-up→Y-up 单点转换**（`:1268-1272`、`:1331-1334`：`Y = ecs.Z; Z = ecs.Y`）——这是本项目踩过多次的坑，NPC/事件必须走同一转换，不得各写一份。

### 6.3 到期与清除
- tick 内检查 `ExpiresAtTick` → 走与 `MarkMonsterDead`（`:1864`）同一条 Despawn 全广播路径。
- DS 重启后脚本实体**不恢复**（P1）；持久化到 `IWorldChunkCellGrain` 留到 P4 评估（世界状态需可回滚，脚本实体若常驻会造成重启后状态不一致，故默认带 TTL）。

### 6.4 客户端指令入口
- 复用现有聊天输入框（`ChatClient`），以 `/` 前缀识别为指令，客户端**只做拆分**成结构化 `GmScriptCommandMessage`，**语义解析与校验在服务端**。
- 首批指令集（映射到 `GmScriptTicket` 字段）：
  - `/spawn monster <templateName|id> [count] [x,y,z] [ttl]`
  - `/spawn npc <templateName|id> [x,y,z]`
  - `/event <eventName> [x,y,z] [radius]`（表现型 / 玩法型由**服务端目录**决定，所需权限位也随之不同：只放表现的 `FireEvent`、会建怪群的 `SpawnEvent`）
  - `/trigger <eventName> <x,y,z> <radius> [once|repeat] [ttl]`（放置触发区域）
  - `/despawn <entityId>`（按 ID 清除，**可能是他人的生成物** ⇒ 需 `DespawnAny`；静态刷怪点实体不在脚本 ID 域内，一律拒）/ `/despawn mine`（只清本人 ⇒ 需 `DespawnOwned`）
  - `/scriptperm`（查询自身授权位与配额余量）
- 缺省坐标 = 调用者当前权威坐标（由 Grain 侧从 `IZoneShardGrain`/DS 位置回写服务读取，**不取客户端上报**）。
- 回包：`GmScriptResultMessage` 直接复用客户端现成的 toast（注意本项目 `UIHelper.ShowToast` 的历史坑）。

### 6.5 触发区域 `DsTriggerZone`（新增子系统，DS 此前完全没有 trigger volume）
最小可用形态，避免膨胀成通用脚本引擎：

- **形状**：首版只做**球形**（`Center` Z-up + `Radius`）。盒形/多边形留待有需求再做（形状字段先预留 `ShapeKind`，避免后续改包宽度）。
- **数据结构**：`DsTriggerZone { EntityId, TemplateId, Center, Radius, Mode(Once|Repeat), CooldownSeconds, ExpiresAtTick, InsideSet(HashSet<long>), SpawnedToClients }`。
- **进出判定**：每 tick 对**该区域候选玩家**做 `distSq <= r²` 比较，与 `InsideSet` 求差集得"进入沿/离开沿"。
  - **不遍历全体玩家 × 全体区域**：复用现有 AOI 分桶（`ZoneShardAoi` + `WorldCoord`，16m/chunk）建"区域→覆盖 chunk 集合"倒排索引，玩家只在跨 chunk 时重取候选集。区域上限默认 32/shard，成本可忽略。
  - 比较用**平方距离**，且**只用服务端权威坐标**（`DsPlayer` 的已校验位置，不用客户端上报）。
- **触发效果**：进入沿 → 派发该区域绑定的内联动作（表现型广播给进入者；或玩法型生成怪群/NPC；或两者组合）。`Once` 触发后自身进入冷却/可到期清除；`Repeat` 按 `CooldownSeconds` 限频，避免玩家在边界反复横跳刷怪。
- **确定性**：区域遍历按 `EntityId` 升序、玩家候选按 `sessionKey` 升序，与 `TickSpawnDirector` 按 `SpawnPointId` 排序的既有纪律一致，保证可回放。
- **可视化**：区域本身**不画实体**（避免客户端需要渲染体积）。改为向授权操作者定向下发一次表现型 Vfx 环，客户端零改动即可看到落点；真正的区域调试可视化列入 P6 可选项。
- **权限**：需要 `PlaceTrigger` 位；触发出来的怪群**在放置时一次性裁决**（预算/上限/距离都在放置点校验），不在触发瞬间回头问 Grain——热路径零 RPC 的原则同样适用于区域触发。因此区域必须带 TTL（默认上限 10 分钟）且 `Repeat` 冷却不得为 0。

---

## 7. 安全与防护清单

| 风险 | 对策 |
|---|---|
| 客户端伪造他人角色发指令 | 身份只从 `MessageHeader.AuthToken` + `SessionBindingValidator` 取；body 内 `CharacterId` 仅作日志字段，丢弃不参与判定 |
| 无权限角色探测指令 | `TryConsumeAsync` 拒绝也落审计；对拒绝率异常的连接做告警（沿用现有 IP 连接频率限制机制） |
| 令牌重放 / 伪造 | HMAC 签名 + 5s TTL + Nonce 有界 LRU 去重 + shard 归属校验 |
| 实体泛滥打爆带宽/AOI | `MaxPerShard`（**硬顶，无任何权限位可越**）+ `MultiEntity` 权限位 + `count ≤ 10` 硬顶 + 配额/冷却 |
| 越权生成强怪破坏经济/平衡 | 模板白名单（只接受 catalog 内的 `templateId`，不接受客户端传数值） |
| 热路径 RPC 打爆 tick | 令牌自证，DS tick 内零跨进程调用 |
| 旧版 DS/网关混跑 | kind=8 尾部追加（未知 kind 跳过）；`ScriptedSpawn.Enabled` 默认 false；`GmScriptCommandMessage` 在无 DS 且未开启 Orleans 回退时直接拒绝 |
| 破坏移动反作弊/位置权威 | 目标点校验用**服务端权威坐标**（`DsPlayer` 已校验位置）；脚本实体不参与 `MovementValidator` 输入链 |
| 发布版误开后门 | `Enabled` 默认 false；`BootstrapAdminIds` 每次使用都以 Warning 落审计，且正式授权应迁入 DB 后清空该列表 |
| Orleans 接口升版导致新旧 Silo 混跑 `TypeLoadException` | `IGmCommandGrain` 升到 `[Version(2)]` 后，**Silo 与网关必须同批构建部署**，部署前先终止旧 Silo 进程（既有运维流程）；发布说明中列为强制项 |
| 触发区域被用来制造刷怪风暴 | 区域数上限（默认 32/shard）+ `Repeat` 冷却不得为 0 + 区域强制 TTL（≤10min）+ 放置时一次性扣预算，不在触发瞬间回头问 Grain |
| Redis/DB 授权层不可用 | Grain 读权限走 Redis，**读不到即判无权限（fail-closed）**，不回退到"默认允许"；管理面写路径 DB 失败则不刷 Redis，保证两层一致 |
| 授权与审计双轨不一致 | 写顺序固定"先 DB 后 Redis"，Grain 激活时以 DB 为权威重建 Redis；审计条目写入失败不阻断裁决，但要计入 `ScriptAuditWriteFailureCount` 告警 |
| **权限位挂名而无消费方**（授予了却不生效，或两档共用一位而档位形同虚设） | 操作→所需位的映射**集中在 `ScriptAuthorityCore.RequiredFlagFor` 一处**，逐位做了消费方审计（见 §11"权限位与消费方对齐审计"）；无消费方的位（`GlobalCast`）在枚举上注明保留原因，且**不在客户端授权回显里当成一条权限报给操作者**；目录解析不出形态时**不降档**，权限不因目录缺项而变松 |

---

## 8. 确定性与测试

- **黄金值可执行路径**（本项目测试规范）：扩展 `GameplayGoldenTrace` / `GoldenTraceReplay`，把脚本命令流（到达序号 + 验签后的确定性入参）纳入 trace，回放必须逐 tick 一致。
- 单测（`Horizon.Game.Gateway.Tests`）：
  1. 权限位矩阵（每操作 × 有/无对应位 → 允许/拒绝）；
  2. 冷却/配额耗尽、模板非白名单、超距、跨 shard、超 `MaxPerShard`；
  3. 令牌签名篡改/过期/重放；
  4. `scriptedEntityId` 与静态刷怪点 ID 全空间不相交（含 slot 边界）；
  5. Despawn 全广播（AOI 关闭/开启两态）+ 晚加入者补发含脚本实体 —— **已落为 `ScriptedSpawnVisibilityTests`（3 例）**：AOI 视野外的一批绝不进该玩家快照（而生成点仍在世界里，用以区分"被裁剪"与"没生成"）、晚进场会话通过 `InitialSpawnSnapshots` 拿到已存在的脚本实体、TTL 到期走 `DespawnBroadcast` 全广播（受众含从没看见过它的那位）。两个断言易错点：AOI 开启时下发在 `SnapshotGroups` 分组里而不是 `WorldSnapshot`；Despawn 只在到期的那一个 tick 出现一次，必须逐 tick 累积。；
  6. NPC 不进入战斗/怪物遍历（回归 `_monstersById` 无副作用）。
- 集成：双客户端 PIE 目视验证（生成可见、位置正确、到期消失、另一客户端同步）。**按既有协作约定，PIE 运行与截图由用户执行**，我交付代码 + 逐页观察清单 + 取证要点。

---

## 9. 分期落地

| 期 | 内容 | 交付判据 |
|---|---|---|
| **P0** | 契约与骨架：C1~C8 定义（含 `IGmCommandGrain` 升 `[Version(2)]` 与方法签名）、令牌签发/验签、权限判定纯逻辑状态机（可 mock）、配置项、`Enabled=false` 全链空跑 | 编译通过 + 权限矩阵/令牌单测绿；无行为变更风险 |
| **P1** | **授权面 + 脚本怪物端到端**：`GmScriptGrant` EF 表 + 幂等迁移脚本、Redis 权限层、`GmCommandGrain` 裁决与授予/回收、`GmScriptController` + WebAdmin 页、`WorldScriptCommandHandler`、kind=8 下行、`SpawnScriptedMonster`、快照广播、`/spawn monster` `/despawn` `/scriptperm` | 管理端授予→角色登录→指令出怪→双客户端可见可击杀→审计可查；未授权角色被拒且留痕 |
| **P2** | **NPC**：`DsNpc`、`NpcTemplateCatalog`、`_npcsById`、`InteractRadius` → `SyncEventKind.InteractStart/End`；`/spawn npc` | 客户端可见 NPC 并可交互；`_monstersById` 相关遍历零回归 |
| **P3** | **表现型 + 玩法型事件**：Vfx/Sfx 定向广播、公告、带 TTL 的事件实体（世界 BOSS/怪群）、`WorldEventTemplateCatalog`；`/event` | 事件触发后受众可见、到期干净清除（无幽灵残留） |
| **P4** | **触发区域 `DsTriggerZone`**：球形区域 + AOI 倒排候选 + 进出沿检测 + Once/Repeat 冷却 + 强制 TTL；`/trigger` | 玩家踏入按预期触发、边界横跳不刷风暴、到期干净清除 |
| **P5** | 收尾：Orleans 回退路径（`UseDS=false`）、黄金值 trace 纳入脚本命令流、脚本实体重启语义评估、客户端结构化指令面板与坐标拾取辅助 | 灰度两态行为一致；回放逐 tick 一致 |
| **P6** | 可选（未排期）：区域调试可视化、盒形/多边形区域、按运营账号体系细化 `callerId` | — |

每期独立可编译、可测、可回滚。P1 结束即满足目标核心诉求（授权角色在指定位置触发 DS 生成，且由 Grain 权威确认）。

---

## 10. 决策记录（2026-10-03 已确认）

| # | 议题 | 决策 | 对设计的影响 |
|---|---|---|---|
| D1 | 裁决结果如何送达 DS | **签名令牌自证** | §4.2 成立：`ScriptGrantToken` + HMAC + Nonce 去重 + 5s TTL；DS 热路径零 RPC |
| D2 | 特殊事件语义范围 | **三态：再加区域触发器** | 新增 §6.5 `DsTriggerZone` 子系统与 `PlaceTrigger` 权限位；排期独立为 P4 |
| D3 | 授权位来源 | **P1 即落 Redis/DB + WebApi** | §4.4 改为持久化主路径，配置引导降级为"仅 Debug 的本地兜底"；P1 范围显著变大（含 EF 迁移 + 管理面） |
| D4 | 旧 GM 死契约处置 | **改造复用 `IGmCommandGrain`** | §4.1 改为升 `[Version(2)]` 扩接口并补鉴权，不新建 Grain；连带新增"新旧 Silo 必须同批部署"的运维强制项（§7） |

### 仍按默认取定、如无异议即按此落地
- **NPC/事件实体重启不恢复**，一律带 TTL；持久化到 `IWorldChunkCellGrain` 留待 P5 评估（重启后状态一致性风险大于收益）。
- **触发区域首版只做球形**，且不做体积可视化（用一次表现型 Vfx 环代替），避免膨胀为通用脚本引擎。
- **脚本实体 ID 走负 `spawnPointId` 域**，不改 `ComputeEntityId`，与静态刷怪点天然不相交（§3.3）。
- `SystemAdminMessages` 那批无处理器的死 schema **本次不动**（既不复用也不删除），避免把无关的删除性改动混进本目标。

---

## 11. 落地进度（2026-10-04 更新）

### 2026-10-05 复验（隔天按当前工作树重跑，不引用昨天的结论）
- **发现并修回一处被并发吞掉的传输层成员**：另一路在 `DsUplinkMessageKind` 末尾追加
  `EntityDescriptor = 9` 时，把本功能的 `ScriptedSpawn = 8,` **声明行整行吃掉**（kind=8 的文档注释还在）。
  症状是 `error CS0117: "DsUplinkMessageKind"未包含"ScriptedSpawn"的定义`，三个调用点全红——
  整个后端连同测试装配编不过，看起来像"对方故意撤了这条通道"。判据：注释还在 + 新成员编号仍排在它之后
  ⇒ 是吞行事故，**补回那一行**（7/8/9 顺序与编号都不动），不要去改调用点也不要动对方的成员。
- 修完后 `Horizon.Game.Message / Core / Orleans.Grains / DsHost / Game.Gateway` 全部 **0 CS 错误**。
- 本目标范围合并 filter：**148 例通过 / 0 失败**（= 本目标 145 例 + 3 条方法名里含 `ScriptedSpawn` 的
  `WuxingMonsterDomainTests`，按类相加才是 145）；其中含真 DS 进程的 `DsHostScriptedLiveTests` 4 例。
- 真集群授权取证今天重跑：`GmCommandGrainScriptAuthorityLiveClusterTests` **2/2 通过**
  （`HORIZON_ORLEANS_REDIS=127.0.0.1:9379`）。
- 主工程全量：**3145 passed / 8 skipped / 1 failed**。唯一失败与本目标无关且已定位到具体两行：
  `UnionRegistrationParityTests` 报 `Expected: 240 Actual: 242` ⇒
  生成器看到 `[NetworkMessage]` 的 **241 `QuestInteractRequestMessage` / 242 `QuestInteractResultMessage`**
  没有对应的手写 `[MemoryPackUnion]` 注册（任务系统那路在途工作）。这道门禁正是为此而设，
  归对方补齐；本文件记录以免下一个人从"union 是不是被我改坏了"查起。
  （仓内既有规则同样写着**新增网络消息必须手写 `[MemoryPackUnion]` 登记**——只加 `[NetworkMessage]` 特性
  会让生成器认得、运行时不认得，消息**静默失败**。）
- 客户端侧编译自检今天跑不通，但**归因明确**：`scripts/scripted-spawn-preflight.ps1 -ClientCompile`
  报的全部是 `error CS0234: 'QuestClientService' 不存在`，落在
  `CombatHudTraditionalPage.cs` 与 `QuestLogPage.cs` 两个文件（条数随对方在途编辑浮动，故不写死）；
  **本功能的 `Social/WorldScriptClient.cs` 在同一次 csc 全量编译里 0 诊断** —— Roslyn 会列出所有文件的错误，
  别人文件红不会掩掉自己文件的错，所以这仍是一条有效的"客户端指令层当前可编译"证据。
  参照昨天的全绿记录：`576 response entries / 4 protocol reference(s) pointed at the repo builds`。
  顺手修了自己脚本的一处误导：`-ClientCompile` 原先用"正则命中数"当错误数（编译器每条诊断会打印两遍），
  报出的数字与下面列出的行对不上；现改为**去重后的诊断行**，并按文件先给归属计数——
  多路并发改同一个工程时，第一眼该看见的是"有没有我自己的文件"。


### 已完成并有测试覆盖（P0 + P1 服务端主体）
脚本化生成相关（本目标范围）**145 例已执行全绿**（按类单独运行为准：`ScriptedSpawnAuthorityTests` 71、`GmCommandGrainScriptAuthorityTests` 11、`WorldScriptCommandHandlerTests` 10、`ScriptedSpawnEndToEndTests` 7、`ScriptedSpawnConfigurationBindingTests` 7、`ScriptedSpawnDiagnosticsTests` 8、`WorldScriptRoutingTests` 7、`WorldScriptWireContractTests` 6、`ScriptedSpawnFrameTests` 4、`MessageUnionTypeMemberDispatchTests` 2、`DsHostScriptedLiveTests` 4、`ScriptedSpawnSocketIntegrationTests` 3、`GmCommandGrainScriptAuthorityLiveClusterTests` 2（真集群，需 `HORIZON_ORLEANS_REDIS`）、`ScriptedSpawnVisibilityTests` 3（含 Theory 展开）。
主测试工程回归 `Horizon.Game.Gateway.Tests` **3130 passed / 0 failed / 8 skipped**（2026-10-04 19:24 实测；
那 8 条跳过是既有的 `HORIZON_DSHOST_EXE` 门控重连用例）。本目标范围 145 例都在这个工程里。
两个**本目标之外**的工程各有一条情况，都按证据归了属，没有动它们的文件：
- `Horizon.IM.Gateway.Tests`：合并跑时 1 条失败，单独复跑 20/20 全绿 ⇒ 时序抖动（既有记录里同类现象：并行跑与单跑结果不同）。
- `Horizon.Game.GengDi.Tests`：`CurrentUserAvatarRefreshTests.User_Avatar_SyncSemantics_FromLocalFile`
  两次都是 `Assert.True() Failure`（该工程与本目标 0 文件交集，且 `git status` 显示这两个目录都不在工作树改动列表里）。
> 同日再跑时 `dotnet test Horizon.sln` 会在 `Horizon.PerformanceTests` 上**构建失败**
> （`ICharacterGrain` 不再含 `MoveAsync` / `AttackAsync`）。这是并发那一路正在改角色/战斗 Grain 契约留下的，
> 与本目标范围 0 关系：本目标的改动不涉及那两位方法，且 `git show HEAD` 里接口同样没有它们（他们的 `ICharacterGrain.cs` 在工作树里是 M 态）。
> 归因做法与既有记录一致：先分清归属再动手，**不去改别人的文件**。
> 一条操作性事实：`dotnet test` **只接受一个工程**，把四个测试工程写在同一条命令里会得到
> `MSB1008: 只能指定一个项目`（而不是部分执行）；要么逐个跑，要么跑 `.sln` 并容忍那个工程。
> 本目标范围 145 例按类逐个运行为全绿。
全量回归 2998 passed / 2 failed / 8 skipped（2026-10-04）：那 2 条是 `WuxingSkillTreeTests.LootTables_*`，属另一路并发工作（其新文件 `Horizon.Orleans.Silo/Startup/WuxingLootTables.cs`），该测试文件对本目标范围 0 引用；本目标 125 例单独运行为全绿。

| 能力 | 落点 |
|---|---|
| 消息与枚举契约 | `Horizon.Game.Message/Network/WorldScriptMessages.cs`（union 238/239、`MessageType` 1109/1110、`ScriptPermissionFlags` 含 `GmOps`/`PlaceTrigger`；引导位 `DevOnly` 因无写入路径已删除） |
| 令牌签发与自证 | `Horizon.Game.Message/Security/ScriptGrantTokenSigner.cs`（HMAC-SHA256、定长比较、过期/未来签发/缺密钥一律 fail-closed） |
| 纯逻辑裁决核 | `Horizon.Game.Core/Security/ScriptAuthorityCore.cs` + `ScriptedSpawnOptions`（七步判定；参数校验先于目录查询，避免"半径写错却报模板不存在"） |
| Grain 权威裁决 | `IGmCommandGrain` 升 `[Version(2)]` + 五个脚本方法；原有 7 个 GM 方法补 `AssertGmAsync`；授权管理走游戏内 `/grant` `/revoke` `/audit`（经 GameGateway，**无 HTTP 管理端点**）；Redis 热层（Orleans `GameStore` 持久化）+ SQLServer 权威层；`BootstrapAdminIds` 解决"第一个 GM 由谁授权" |
| 网关→DS 下行 | `DsUplinkMessageKind.ScriptedSpawn = 8` + `DsScriptedSpawnEnvelope` + `IDsScriptDispatcher`（由 `DsUplinkServer` 实现，await 真实写入结果） |
| 网关 handler | `WorldScriptCommandHandler`：身份**只取** `MessageHeader.AuthToken`，不信任 body 自报 |
| DS 执行 | `DsWorldSession`：脚本生成点落在**负 `SpawnPointId` 域**，复用 `SpawnDirectorCore` 槽位池 → AI/重生/快照/AOI/晚加入补发自动生效；TTL 到期经 `DespawnBroadcast` 全广播；`DespawnMine` 按归属清除；静态刷怪点实体拒绝被脚本清除 |
| P2 NPC | `NpcTemplateCatalog`（3 个开发模板）+ `DsMonster.ScriptedKind` + `ScriptedEntityTypeOf` → 下发 `NetworkEntityType.Npc`，NPC 无仇恨/不移动/不可重生；两处身份构造点（非 AOI 与 AOI 分组）与显示目录选择均按类别分流 |
| P3 玩法型事件 | `ScriptedKind.Event` 复用怪群通路 + 强制 TTL，到期经 `DespawnBroadcast` 全广播 |
| 实体 ID 回执 | DS 在刷怪调度之后按**实际生成的实体**发回执（`SyncEventKind.ScriptedSpawnResult` 尾部追加 + `ScriptedSpawnResultPayload` 二次序列化，与 Correction/InputAck 同一模式），受众只有操作者；`RequestId` 由 Grain 写入**被签名的令牌**，客户端改不动。受理回包与执行回执**故意分两条**：前者只代表"裁决通过且已投递"，后者才是"世界里真有了"。有测试锁定两者 ID 集合与快照完全一致、空结果也如实回执 |
| 网关链路 | `WorldScriptCommandHandlerTests`：身份与 shard 一律由服务端推导（`GmScriptCommandMessage` 里根本没有操作者字段可自报）、无令牌/他签令牌直接拒且不触碰 DS、Grain 拒时不下发、DS 不可用时如实回"未生效"而非谎报受理 |
| 客户端入口 | `WorldScriptClient.cs` + `WorldScriptMessageHandler.cs`（`/spawn` `/event` `/trigger` `/despawn` `/scriptperm`），`ChatClient.Send` 以 `/` 前缀分流 |

### 待办
1. **建表**：`scripts/sql/gm_script_grant.sql`（幂等，待授权后执行）。未建表时授予操作会失败而不是静默放行。本次**不动 EF Migrations**——模型快照正被另一路改动占用，加迁移会冲突。
2. **DLL 分发（提权，由你执行）**：Flax 编译客户端脚本引用的是引擎 `Flax_1.12\Binaries\Tools` 里那份协议程序集。实测（复用 Flax 自己的 csc response 文件跑同一次编译）：引用指向 Tools 那份旧副本时**报 10 个错误，全在 `WorldScriptClient.cs`**（`ScriptOp.Grant/Revoke/Audit`、`TargetOperatorId`、`RequestedFlags`、`TtlHours`、`AuditLimit` 不存在）；把这几条引用改指仓库 `bin\Debug\net10.0` 后**同一批源文件 0 错误**。
   结论有两条：客户端代码与当前协议**是对的**；唯一挡着 PIE 的就是"Tools 副本陈旧"这一步。
   现成脚本：`powershell -File scripts\copy-shared-dlls-to-flax-tools.ps1`（非提权会在碰任何文件之前直接退出；每份文件打印复制前后 SHA-256 前 16 位，与 `scripts\scripted-spawn-preflight.ps1` 同一算法可互相印证）。`scripted-spawn-preflight.ps1 -ClientCompile` 可以直接跑这次离线编译验证，不用先开编辑器。
   > 更正两条：① 我曾写"客户端 `Game.csproj` 现在 0 错误"，那是早于并发改动的结论；② 我随后据 `dotnet build Game.csproj` 报的 36 个错误断定"客户端被 `Plugins/NarrativePro` 挡住"——**那也是错的**：MSBuild 会把插件源码一起编进 `Game.csproj`，而 Flax 真正的脚本编译是插件各自成 DLL、`Game.CSharp.response` 只引用它们的产物。按 Flax 的通路实测，客户端唯一的阻塞就是上面那份旧 DLL。
3. **P1 收尾（已完成并纠正方向）**：授权管理面最初做成了 WebApi 端点 + WebAdmin 页面，经按拓扑复核后**全部撤销**（`git checkout` 还原 WebAdmin 两文件、删除控制器/服务/页面与菜单项），改为上表的游戏内指令。管理面不再存在第二道门。
4. **确定性黄金值（已落地）**：`ScriptedSpawnAuthorityTests.脚本生成轨迹黄金值` 直接驱动真实 `DsWorldSession`，把逐 tick 的实体 ID / 下发类型 / 昵称 / 坐标量化 / 全广播清除 / 实体 ID 回执串成 SHA-256 常量钉住，并先自查轨迹非空。**没有**往 `GameplayGoldenTrace` 里加脚本语法——那个文件自带一套手写 `MonsterAiCore`/`SpawnDirectorCore` 重放器而不驱动真实会话，加进去等于把 DS 生成规则抄第二份，两份迟早漂移。
5. **P3 表现型事件（已落地）**：`/event 天空异象` → DS 经既有 `EventPacket{SyncEvent{Kind=Vfx, Payload}}` 通道下发 `ScriptedEffectPayload`，客户端 `HundunWorldGame.TryShowScriptedEffect` 解码后用现成的 `DamageNumberSystem.ShowText` 在世界坐标显示文字并在聊天留一条系统行。**事件的形态（刷怪群 / 只放表现）由服务端 `WorldEventCatalog` 决定，不由指令参数决定**——否则客户端就能自行指定生成数量，白名单与预算形同虚设；数量与怪物模板同样取自目录（`Count` 被目录的 `SpawnCount` 覆盖，有测试锁定）。
   > 更正：我一度判定"方案 §3.2 复用 Vfx 的假设被证伪"，那是查错了对象——不存在的是 `VfxPacket` 这种**专用包类型**，而 `SyncEvent` 本就带 `Kind`+`Payload` 通用通道（Correction/InputAck 都这么挂）。原假设成立，无需新增协议包。
5. **P4 触发区域（已落地）**：`DsTriggerZone` + chunk 倒排索引（候选从 O(玩家×区域) 降到 O(玩家)）+ 平方距离球体判定 + 进出沿 + Once 触发即失效 / Repeat 冷却限频 + 强制有限 TTL + DS 侧区域数硬顶（默认 32）+ `/despawn mine` 连带清区域；召唤走与直接指令同一条 `CreateScriptedPoint` 通路。**区域本身不下发给客户端**（工程内无体积渲染，画它要新增协议包），故首批不做区域可视化。
5. **Silo 与网关必须同批部署**（`IGmCommandGrain` 已升 Version(2)，混跑会 `TypeLoadException`）。

### 部署强约束（新增）
- **`ScriptGrantTokenSigner.Version` 已升到 2**（`RequestId` 进入规范字节）。滚动发布中若出现"新 Grain + 旧 DS"，表现是 DS 侧 `signature-mismatch` **全量拒绝**（fail-closed，不会误执行），因此三进程必须同批构建部署。
- 客户端**编译**依赖 `Flax_1.12\Binaries\Tools\Horizon.Game.Message.dll`：Flax.Build 解析脚本程序集优先取那里，**不是** `HundunWorld\Output\Windows`。仓库内自动分发因 UAC 被拒（表现为 MSB3021 警告），所以在提权拷贝完成前，`Game.csproj` 会报新类型 CS0246/CS0117——这是环境分发问题，不是代码问题。

### 三进程共享的运行配置（2026-10-04 收敛为"一对环境变量"）
| 键 | 生效位置 | 说明 |
|---|---|---|
| `ScriptedSpawn__Enabled` | 网关 / Silo / DS 三进程 | 总开关，默认关闭（灰度安全）。网关与 Silo 由 .NET 配置系统识别 `节名__键名` 形式的环境变量；DS 宿主不加载 appsettings，由 `ScriptedSpawnDiagnostics.FromEnvironment` 显式读取同名变量 |
| `ScriptedSpawn__SigningKeyBase64` | 三进程 | 三处**必须同值**：Grain 用它签发、网关自检、DS 验签。不同值的表现是 DS 侧 `signature-mismatch` 全量拒绝（设计使然的 fail-closed） |
| `ScriptedSpawn:BootstrapAdminIds` | 仅 Silo 配置节 | 引导管理员角色 ID，只用于授予第一个 GM，每次使用都以 Warning 落审计 |

> 旧名 `HZN_SCRIPTED_SPAWN` / `HZN_SCRIPT_SIGNING_KEY_B64` 仍作为 DS 的**回退**保留，但两名同时存在时权威名赢；
> 权威名显式写 `0` 就是关闭，不会被旧名的 `1` 复活——开关必须只有一个真值源。
> 之所以统一走环境变量而非三份 appsettings：密钥不该进仓库，而"三处抄漏一处"是这条链路最常见的故障。

### 启动自检（把"少配一项"变成一行可读日志）
`Horizon.Game.Core/Security/ScriptedSpawnDiagnostics.cs` 在**每个进程启动时**打印一行 `[ScriptedSpawn]`，
内容为有效开关 + **密钥指纹**（`SHA-256(密钥)` 前 4 字节十六进制，密钥本体绝不入日志）+ 全部限额，并按状态定级：

| 状态 | 级别 | 文本要点 |
|---|---|---|
| 关闭 | Warning | "全部指令会被 Grain 以『脚本化生成功能未开启』拒绝"，并点名该设哪两个环境变量 |
| 开启但密钥缺失/非法 Base64 | Error | "本进程一律 fail-closed"（不是"跳过校验"） |
| 就绪 | Info | 要求核对 Grain/网关/DS 三行指纹完全一致 |

限额同样从同一份配置注入 DS：`MaxTriggerZonesPerShard → DsHostOptions.MaxScriptedTriggerZones`、
`MaxTriggerTtlSeconds → MaxScriptedTriggerZoneTtlSeconds`，避免"日志报一套、DS 执行另一套"。
三态与解析优先级由 `ScriptedSpawnDiagnosticsTests` 锁定（7 例，含"启动日志里不出现密钥本体"）。

### 位置基准补口（2026-10-04）
距离校验的比较基准必须是**服务端权威位置**，客户端只允许提供"目标点"。此前 Grain 的守卫写成
`position == null && !ticket.HasPosition`，于是存在一条窄缝：**位置存储取不到、而客户端自报了目标点时**，
裁决会继续走，基准被 `?? 0f` 折成 `(0,0,0)`。表现是"在脚下生成被判太远、在 origin 附近生成被放行"——
前者是误拒，后者是距离校验形同虚设。
现在这条规则收回裁决核（`ScriptEvalContext.OperatorPositionKnown` + `Evaluate` 第 2.5 步）：
- 生成类操作（`Op=Spawn`，含怪物/NPC/玩法事件/触发区域）**必须有权威位置**，否则以 `InvalidArgument` 拒；
- `Query`（`/scriptperm`）与 `Despawn`（按实体 ID 清除）不需要位置，不受影响；
- 拒绝发生在扣预算之前，冷却/配额/签发计数一律不动；
- Grain 侧删除了自己的重复判断，只如实报告"有没有位置"，避免"网关一套、Grain 一套"。
三条新单测锁住这些性质（`服务端无权威位置时拒绝生成_距离基准不得由客户端供给` 等）。

### 坐标口径（2026-10-04 补齐的一处轴序缺陷）
服务端全链路的坐标空间是 **ECS Z-up**（`ScriptGrantToken.X/Y/Z` → `SpawnPointConfig`、触发区域球心、
`DsWorldSession` 的距离与槽位计算），只在**下发快照给客户端时**换成 Flax Y-up（`Y ← ECS.Z`、`Z ← ECS.Y`）。
因此指令里用户手输的 `x,y,z`（客户端所见即 Flax Y-up）必须在**客户端边界**换轴后再上行：
`WorldScriptClient.TryApplyPosition` 现按 `cmd.Y = z_flax; cmd.Z = y_flax` 转换，与收快照时的 `(x, z, y)` 对称。
修前的表现不是报错而是"生成点跑偏"：前后与上下互换，且距离校验拿 Flax 口径去比 Z-up 基准，
把"我站的位置"算成几十米外而误拒 `TooFar`。缺省（不写坐标）路径本来就取服务端权威位置，不受影响。
测试侧锁定的仍是 Z-up 语义（黄金值轨迹里的坐标量化即此口径），故 DS/裁决核无需改动。

### 三段链路端到端测试（2026-10-04 新增）
分段测试之间曾留了一条缝：网关交出去的令牌是不是 DS 认的那一枚。现在
`ScriptedSpawnEndToEndTests` 把三段在同进程接起来跑（**真** handler + **真**裁决核签发 + **真** `DsWorldSession`，
只有 Orleans 与 socket 是替身），锁住：规范字节序与 `Version` 跨进程一致、shard 绑定生效、
模板名→ID 只在服务端解析（昵称/`NetworkEntityType` 来自目录）、负 ID 域、回执 ID 与快照 ID 一致、
白名单拦截时 DS 一次都没被碰。

其中一条**故意保留的不对称**也被测试钉住：网关的"已受理"建立在帧写出成功之上，
它无从知道 DS 验签后有没有把指令丢进拒绝计数——所以密钥不同源/DS 缺密钥时，网关仍回"成功"，
真相只在 DS 的 `LastScriptRejectReason`（`signature-mismatch` / `missing-signing-key`）与回执缺席里。
这不是谎报（受理 ≠ 生效的口径在设计里就是分两条的），但**排障必须看 DS 那一行**：
`DsHostRunner` 在 tick 前消费队列时会为每次拒绝打一行
`DsHostRunner：脚本指令被拒。Reason=… Operator=… Kind=…`，`ScriptedRejectedCount` 也进了诊断计数。

### shard 归属口径修正（多片 DS 会全量拒绝）
一个 DS 进程可向注册表声明承载多片（`ShardIds = 1,2,3`，多片共用同一个世界实例——玩家实体与快照本来就不按片隔离），
而 `DsWorldSession` 只有 `ShardId = ShardIds[0]`。原实现拿它和令牌的 `TargetShardId` 单值比较，
于是**声明多片的 DS 会把落在 2/3 的一切合法指令判成 `wrong-shard` 全量拒绝**。
现在改为按集合判定（`DsWorldSessionOptions.HostedShardIds`，`DsHostRunner` 用 `ShardIds` 注入；
留空则回退"只认自己那一片"，缺省仍是最小权限），与网关 `_registry.TryResolve(shardId)` 的口径一致，
同时仍然拒绝"投给不承载该片的 DS"（跨片重放）。

### 线上格式（kind=8）单独锁住
三段链路都在同一进程里传对象，只有"网关→DS"这一跳要把签好名的令牌经 MemoryPack 序列化过 TCP。
签名保护的是**字段值**，所以这一跳要证的不是"能编能解"，而是**序列化后 canonical 字段逐位一致**——
任何一个字段在 wire 上被丢掉/改序/精度受损，表现都是 DS 侧 `signature-mismatch` 全量拒绝，
而分段与端到端单测都看不见它们（不经过序列化）。`ScriptedSpawnFrameTests`（4 例）钉住：
19 个字段（含 `Signature` 字节、三个浮点、`TargetShardId`、`TriggerMode`、`RequestId`、两个时间戳）
往返完全相等且**用同一把密钥仍然验得过**；中途改一个坐标即 `signature-mismatch`；
脚本帧与注册帧粘包时按序解出；`Token=null` 的信封安静解出后被 DS 判 `null-token`（不在 tick 线程抛异常）。

### 审计改为持久层 + Grain 面首次有直接测试（2026-10-04）
审计原先只进 Grain 的进程内 `List`（字段自带注释"生产环境应持久化到数据库"）。这不是风格问题：
**Orleans 会按需去激活空闲 Grain**，进程内列表随之清零，于是 `/audit` 与 GM 事后核查会在无人察觉的情况下
返回空表——而"空表"最容易被误读成"没人做过"。现在：
- `GmScriptAuthorityState` 增加 `AuditLog`（`[MemoryPackOrder(1)] [Id(1)]`），与授权同存 Redis 热层，
  有界环形保留最近 **500** 条（`MaxDurableAuditEntries`），丢最旧；长期归档仍是 Silo 日志；
- `RecordAuditLogAsync` 改为写持久层（**被拒绝的 GM 动作也留痕**，这恰恰是最需要事后核查的一类）；
  旧字段 `_auditLogs` 与 `MaxAuditLogSize` 删除，不留两个真值源；
- `QueryScriptAuditAsync` 只回脚本面动作（`Script*` / `GrantScript` / `RevokeScript`），
  且不传 operatorId 时不再回落到"全部 GM 动作"——否则封禁、邮件会混进 `/audit` 的脚本文案里；
  `QueryAuditLogsAsync`（GM 全量面）保持原语义；
- 状态新增成员对既有快照前向兼容：老快照缺字段 → 反序列化成空列表，不会读失败。

新增 `GmCommandGrainScriptAuthorityTests`（11 例）第一次直接驱动这个 Grain（其余测试都把它换成替身）：
无 `GmOps` 的授予被拒且仍留痕、引导管理员授予同时落权威表与热层、**不能凭空造出自己没有的权限位**、
权威表写失败时不留"热层已生效"假象（抛异常且 `Operators` 仍空）、审计活得过去去激活、
脚本审计不混入无关 GM 动作、裁决消耗预算后第二次落冷却且令牌可用同密钥验签、
缺密钥一律拒且不偷偷扣预算、无权威位置只拦生成类而查询面照常、回收同时清两层并在表里标记撤销（不删行）。

脚本化生成相关 **100 例**全绿（实测按类：`ScriptedSpawnAuthorityTests` 60、`WorldScriptCommandHandlerTests` 10、`ScriptedSpawnDiagnosticsTests` 8、`ScriptedSpawnEndToEndTests` 7、`ScriptedSpawnFrameTests` 4、`GmCommandGrainScriptAuthorityTests` 11）。
全量回归 2998 passed / 2 failed / 8 skipped（2026-10-04）：那 2 条是 `WuxingSkillTreeTests.LootTables_*`，属另一路并发工作（其新文件 `Horizon.Orleans.Silo/Startup/WuxingLootTables.cs`），该测试文件对本目标范围 0 引用；本目标 125 例单独运行为全绿。

### 真进程 + 真 TCP 的开启路径验证（2026-10-04）
`DsHostScriptedLiveTests`（4 例）启一个**真实 DsHost 进程**，连一个只讲上行协议的测试网关，
从 DS 自己的 stdout 取证据。它锁的是以前没人碰过的三件事：

| 用例 | 断言的真实行为 |
|---|---|
| 权威环境变量开启 | 启动行 `[ScriptedSpawn] DS(验签)：Enabled=True 密钥指纹=<算出来的值>`；错密钥签的帧报 `Reason=signature-mismatch`，同密钥签的帧**不出现**任何拒绝行 |
| 默认关闭态 | `Enabled=False` + 正确密钥签的帧也报 `Reason=scripted-disabled`（灰度默认） |
| 旧变量名 | `HZN_SCRIPTED_SPAWN=1` + `HZN_SCRIPT_SIGNING_KEY_B64` 仍能把开关与密钥带起来 |
| 缺密钥 | 打印 `(missing)` 与 fail-closed 告警，进程**不退出**，帧一律 `missing-signing-key` |

"被拒的指令出现在日志里"等价于证明 **下行帧 → `ScriptedSpawnReceived` 事件 → 宿主队列 → tick 前消费 → 验签**
这条接线在真进程里是通的（漏接那一行 `uplink.ScriptedSpawnReceived += runner.NotifyScriptedSpawn`
以前没有任何测试能发现）；"验签通过之后"的执行语义由 `ScriptedSpawnEndToEndTests` 用同一个
`DsWorldSession` 钉住，两边合起来覆盖完整链路。

两个必须写下来的环境坑（都是实测）：
1. **DS 的中文日志是 UTF-8，而 .NET 读重定向子进程 stdout 默认跟随控制台代码页（本机 936/GBK）**，
   `DS(验签)` 会被读成 `DS(楠岀)` 让断言永远不中——测试里显式 `StandardOutputEncoding = Encoding.UTF8`；
2. 测试 DS **不能加入开发中的 Orleans 集群**：`bin/Verify/appsettings.json` 里带着
   `Orleans:ConnectionString`，实测那个进程会打出"已连接 Orleans 集群（ClusterId=dev）"并预热掉落表。
   命令行是第 3 层覆盖，传 `--orleans-silo=`（空值）即可让它走内置确定性掉落表、完全不碰集群。

本目标范围的用例计数以本节末尾的汇总表为唯一真值源（不在此重复数字，两处计数会漂）。全仓库回归现已整体 0 失败（此前 2 条 `WuxingSkillTreeTests.LootTables_*` 由并发工作那条线自行修复）。

### 网关路由层的守卫（2026-10-04）
`ClientPacketPipeline.ProcessMessageAsync` 是"遍历 handler，取**第一个** `CanHandle` 且 `ValidateMessage`
通过的处理器"——**首个匹配即胜出**，两处出错都不会报错、只会静默走错门：有第二个处理器也声明
`ScriptCommand`，或命中判定被包头里客户端可自报的字段带跑。`WorldScriptRoutingTests`（7 例）钉住：
只声明 `{ScriptCommand}` 且属 `System` 服务；正常包命中；**Header 冒充 ScriptCommand 而 Body 是别的类型 → 不命中**
（以 Body 自带的权威 Type 为准）；客户端把服务端下行 `ScriptResult` 原样发回来 → 不命中；
同一类型落在别的服务类别 → 不命中；只有 Body 缺失时才回退 Header 路由。

> 顺带记录一个不属于本目标的既有事实：`MessageType` 里 `Chat = 311` 与 `ChatMessage = 311` 是**同值别名**
> （两个名字一个数）。若将来有处理器分别声明这两个名字，就会正好踩上"首个匹配即胜出"的静默旁路。
> 本次未改动聊天面，只把这条写在这里备查。

### `MessageUnion.Type` 的成员隐藏陷阱（实测，路由能工作全靠它走的读法恰好正确）
`MessageUnion` 基类在 `MemoryPackOrder(255)` 上有一个 `Type`，而每个具体消息类**又各自声明一个同名 `Type`**
（隐藏 `new`，不是 `override`）。对象初始化器只写派生那一个，基类槽位永远是默认值。实测三种读法：

| 读法 | 拿到的值 |
|---|---|
| `cmd.Type`（具体类型） | 真实类型 ✅ |
| `((INetworkMessage)cmd).Type`（**路由 `CanHandle` 用的就是这条**） | 真实类型 ✅ |
| `((MessageUnion)cmd).Type`（基类静态类型） | `default`（`Unknown`）❌ |

所以网关"按 MessageType 认领 handler"是有效的，但**任何改成按基类变量读 `.Type` 的代码都会让全部脚本指令静默无人认领**；
反过来说，客户端消息处理器（`WorldScriptMessageHandler`）用 `message.Body.GetType()` 分流是安全的。
线格式的类型识别靠 union tag（238/239），与这个槽位无关——已实测：按抽象类型序列化/反序列化后
仍解回原具体类型，20 个字段（含 `TargetEntityId`、`TriggerMode`、`AuditLimit`、`Header.AuthToken`）逐位保真。
`WorldScriptWireContractTests` + `MessageUnionTypeMemberDispatchTests` 把上面这张表钉成断言（已执行通过）。

### 一处会让两个后端进程起不来的配置陷阱（实测并已修）
网关与 Silo 原本这样取配置：`Configuration.GetSection("ScriptedSpawn").Get<ScriptedSpawnOptions>()`，
而本文档让运营设 `ScriptedSpawn__Enabled=1`。实测：**绑定器把 "1" 当布尔转换失败时是抛异常，不是静默忽略**
（`InvalidOperationException: Failed to convert configuration value '1' at 'ScriptedSpawn:Enabled' to type System.Boolean`），
而这段代码在 `ConfigureServices` 里 —— 一个数字字面量足以让**网关与 Silo 双双启动失败**。
（顺带证实：`AddEnvironmentVariables` 会把 `ScriptedSpawn__Enabled` 正确呈现为 `ScriptedSpawn:Enabled`，
`BootstrapAdminIds__0/__1` 也能绑进 `long[]`，所以变量名方案本身是对的。）

现在三进程共用一个开关口径：新增 `ScriptedSpawnConfiguration.Bind(configuration, out rawEnabledValue)`
逐键手工绑定——开关认 `1/true/on/yes/enabled` 与 `0/false/off/no/disabled`，数值键看不懂就保留类内默认值，
坏元素直接跳过，**任何配置笔误都不会让进程起不来**；DS 侧的 `FromEnvironment` 也改为复用同一个
`TryParseSwitch`。网关与 Silo 的注册点都已切到 `Bind`，仓库里不再留裸 `Get<ScriptedSpawnOptions>()`。
`ScriptedSpawnConfigurationBindingTests`（7 例）钉住这些性质，其中一条专门记录"裸绑定器遇到 1 会抛"这一事实，
防止将来有人图省事换回去。

### Grain → 真实 socket → DsHost 宿主 → 真实世界（最后一处装配证据）
`ScriptedSpawnSocketIntegrationTests` 把以前各自分段的两头接起来跑：**真 `GmCommandGrain` 签发** → 真帧经
`DsUplinkCodec` 写到真 TCP → **真 `DsUplinkClient` 解析并触发事件** → `DsHostRunner.NotifyScriptedSpawn` 入队 →
`RunSingleTickAsync` 在 tick 前消费 → 真 `DsWorldSession` 出怪 → 回执带真实实体 ID。
装配那一行是照抄 `Horizon.DsHost/Program.cs` 的 `uplink.ScriptedSpawnReceived += runner.NotifyScriptedSpawn;`——
漏这一行不会有任何报错，只表现为"指令发出去、世界毫无动静"，所以它必须有人在测试里盯着。
另外三条负向：在途改一个字节 → `signature-mismatch` 且世界里什么都没有；`EnableScriptedSpawn=false` →
`scripted-disabled`（灰度回退态，即便令牌完全合法）。

> 写这条测试时踩到一个真实的时序事实：DS 快照默认 **每 3 tick 一份**（`SnapshotIntervalTicks=3`），
> 而实体 ID 回执在"生成那一 tick"就发出了。只看最后一个 tick 的事件会把一条正常链路误判成丢了回执——
> 断言必须跨 tick 收集事件。

### 真机取证：活 Silo + 真 Redis + 活网关控制面（2026-10-04 18:30 实测）
在**正在运行的**那套后端上（Silo PID 35584 起于 15:33、网关 PID 39688 起于 16:17、Redis 127.0.0.1:9379）
用环境门控用例 `GmCommandGrainScriptAuthorityLiveClusterTests` 直打真集群，逐字观测到：

```
GetScriptPermissionsAsync(合成角色) => null
TryConsumeScriptAsync            => Allowed=False Reason=Disabled Message=脚本化生成功能未开启。
GrantScriptAsync(非 GM 调用者)    => Success=False Error=调用者无 GmOps 权限。
QueryScriptAuditAsync(caller)     => 1 条：GrantScript      ← 被拒的授予也留痕，且能跨连接读回
TryConsumeScriptAsync(Query)      => Allowed=False Reason=Disabled Flags=0
```

四条结论：① Version(2) 的脚本契约在**活进程**里可调（不是只在测试宿主里成立）；
② 当前这套 Silo/网关是在**没设 `ScriptedSpawn__Enabled`** 的会话里启动的，所以功能处于关闭态——
要跑正向出怪必须按下面的步骤带变量重启（Silo 与网关必须同批）；③ 授权面 fail-closed 在生产代码路径上生效；
④ **审计进持久层**那处改动在真 Redis 上成立（换个客户端连接仍读得回来）。

另外用一个自启的 DS 冒烟实例（`--shard-ids=999`，不与任何玩家路由重叠；用完已停）连上活网关，日志自证：

```
[ScriptedSpawn] DS(验签)：Enabled=True 密钥指纹=23EBDB93 配额=60/分钟 冷却=5s 单次上限=10 ...
DsUplinkClient：已连接网关 127.0.0.1:19871（第 1 次）。
DsUplinkClient：注册成功。DsId=ds-smoke Shards=999 Port=29880
```

即：**权威环境变量的开启路径、启动自检、密钥指纹、与真实网关注册**四件事都在真进程里成立；
日志里 `脚本指令被拒` 计数为 0（没有静默失败）。

> 顺手一条工具事实：从 shell 里用 `cut -d=` 取 base64 会把结尾的 `=` 切掉，密钥变成 43 字符的非法值；
> `start-scripted-local.ps1` 对此**直接拒绝并说明原因**（而不是带着坏密钥把三个进程拉起来），这是期望行为。

### 权限位与消费方对齐审计（2026-10-04）
**问题类别**：一个 `[Flags]` 枚举里挂了名字、也回显给了操作者，但**没有任何判定读它**——
"授予了就以为生效"。逐个位核了一遍（`grep` 位名 → 只看是否出现在 `&`/`RequiredFlagFor` 右侧），
结果与修正：

| 位 | 谁要求它（唯一真值源 `ScriptAuthorityCore.RequiredFlagFor`） | 判定落点 | 测试 |
|---|---|---|---|
| `SpawnMonster` 0x1 | `/spawn monster` | Grain 裁决第 2 步 | `怪物生成只在持SpawnMonster位时通过` |
| `SpawnNpc` 0x2 | `/spawn npc` | 同上 | `生成NPC需要SpawnNpc位_怪物位不够` + `NPC目录未登记前一律拒绝…` |
| `FireEvent` 0x4 | **纯表现型事件**（目录 `WorldEventMode.Presentation`） | 第 2 步"形态由目录决定"分支 | `事件所需权限位由目录形态决定` |
| `SpawnEvent` 0x8 | 会往世界建实体怪群的事件（`MonsterGroup`）+ 目录解析不出时的默认档 | 第 2 步 | 同上 + `目录解析不出事件时不降档到FireEvent…` |
| `PlaceTrigger` 0x10 | `/trigger` | 第 2 步 | `触发区域需PlaceTrigger位且半径与TTL受限` |
| `DespawnOwned` 0x20 | `/despawn mine` | 第 2 步 | `清除按归属分档_按ID需要DespawnAny` |
| `DespawnAny` 0x40 | `/despawn <实体ID>` | 第 2 步；DS 侧再按"只承认脚本 ID 域"兜底 | 同上 |
| `MultiEntity` 0x80 | `count > 1` | 第 5 步 | `数量大于1需要MultiEntity位` |
| `BypassDist` 0x100 | 超距目标点（**不**再放松分片实体上限，见下方第 4 条） | 第 6 步距离校验 | `超距拒绝_持BypassDist可越` + `shard实体预算上限拒绝_持BypassDist也不例外` |
| `Query` 0x400 | `/scriptperm`、审计查询 | 第 2 步 | `缺位置只拦生成类操作_查询与按ID清除不受影响` |
| `GmOps` 0x800 | `/grant` `/revoke` `/audit`、封禁/补偿等运维面 | `GmCommandGrain.AssertGmAsync` | `GmCommandGrainScriptAuthorityTests` |
| `GlobalCast` 0x200 | **无消费方**（保留位） | 全服公告走 `BroadcastAnnouncementAsync`，由 `GmOps` 把关 | 不参与判定，故无法也不需要测试 |

四处真实缺陷（前三处是"看着有、其实没判"，第四处是"取名只管 A 却顺手关了 B 的闸"）与修法：

1. **`DespawnAny` 从未被要求过**：`Despawn` 与 `DespawnMine` 都映射到 `DespawnOwned`。   后果不是"权限变松"而是**档位形同虚设**——按 ID 清除别人摆的内容和只清自己的用同一位，
   授权表里的 0x40 是死的。现改为 `Despawn → DespawnAny`、`DespawnMine → DespawnOwned`，
   并且互换都必须被拒（新增 4 条 Theory 分支）。
2. **`FireEvent` 从未被要求过**：事件档一律要求 `SpawnEvent`，于是"放一段全屏特效"和
   "刷一波世界 BOSS 怪群"共用同一权限；只放表现的操作者被迫去要建实体的位。
   现由**服务端目录**决定形态：`Presentation → FireEvent`、`MonsterGroup → SpawnEvent`。
   刻意保留的口径：目录解析不出来时**不降档**（仍按 `SpawnEvent` 要求），权限不会因为目录缺项而变松，
   由第 5 步再报"模板不在白名单"——操作者看到的是他真正该修的东西。
3. **`GlobalCast` 被当成一条权限回显给操作者**：客户端 `/scriptperm` 的授权展开里印着"全服播报"，
   而全仓没有一处判定读它。已从 `WorldScriptClient.FlagText` 移除（枚举保留并注明保留原因，
   避免已写进授权表的编号被另作他用）；未识别的位仍回落到 `0x…` 十六进制，不谎报成能力。
4. **`BypassDist` 顺手关掉了分片实体上限**：预算检查写成"`(Flags & BypassDist) == 0` 才查"，
   于是一个取名只管**距离**的位可以越过的却是防洪闸（`MaxScriptedEntitiesPerShard`）。
   设计稿从未授权过这种越法，而触发区域上限（默认 32/shard）本来就是无条件的——两处口径不一致。
   现分片实体上限改为**硬顶**，任何位都不能越；测试
   `shard实体预算上限拒绝_持BypassDist也不例外` 两档都锁住。

顺手补的一处可用性：`InsufficientPermission` 原先回 `缺少权限位 64。`，运营拿到这个数字无法自查。
现由 `DescribeFlag` 回 `缺少权限位 DespawnAny(0x40, 清除他人生成物)。`，测试
`权限位不足的回执点名缺哪一位` 钉住它——**拒绝原因必须能指向下一步动作**。

一处**一直就是假绿**的用例也修了：`缺位置只拦生成类操作_查询与按ID清除不受影响` 里那条 despawn 用例持 `SpawnMonster`，
而清除档要求的是 Despawn*，于是它在第 2 步就被拒、根本没走到位置判定，
`DoesNotContain("权威位置")` 无论位置闸门存在与否都会通过——锁不住任何东西。
现持 `DespawnOwned|DespawnAny` 并直接断言 `Allowed`，才真正是"走到裁决末尾并放行"。

### 裁决码与回执链的对齐审计（同日顺手核的第二类）
同一套查法用到了 `ScriptRejectReason` 上（它是要上线的**整数**，不是内部枚举；底层类型 `byte`，
所以新增码别忘了 255 的上限）：逐个值找生产者，
只有 `WrongShard = 8` **没有任何生产者**——跨 shard 的判定在 DS 侧，以字符串 reason
（`wrong-shard:{n}`）落 DS 日志，从不作为 Grain 裁决码回给操作者。
挂着它等于在告诉读代码的人"分片不匹配会告诉操作者"，而实际上不会。

处理：删除该成员，但**不重排** 9~12（`Cooldown`/`QuotaExhausted`/`EntityBudgetExceeded`/`Internal`）——
重排会让一个陈旧的客户端把 12 读成 9，比读到未知码更难查。编号由
`WorldScriptWireContractTests.裁决码编号冻结且8号为已废弃空位` 钉住，客户端 `ReasonText` 的对应分支同时删除。
客户端现已按**去掉该成员后**的 `Horizon.Game.Message` 重新编译通过（preflight `-ClientCompile`，576 条响应项 / 4 个协议引用指向仓库构建）。

其余 10 个失败码都有生产者（`None = 0` 不是失败码，是成功态的默认值），逐条数过调用点：
`InsufficientPermission` 5 处、`InvalidArgument` 10 处、
`Internal` 7 处、其余各 1 处。DS 执行侧回执链也核到底：
`DsWorldSession` 发 `SyncEventKind.ScriptedSpawnResult` → `HundunWorldGame.cs:3159 TryApplyScriptedSpawnResult`
→ `WorldScriptClient.ApplySpawnResult`，"受理 ≠ 生效"这条口径在客户端有真实落点，不是文档里的空话。

### 本地开启到能跑 PIE 的最短路径
0. **只读自检**（不启停任何进程、不写任何目录）：
   `powershell -File scripts\scripted-spawn-preflight.ps1`
   它检查四件最容易错的事：本会话的开关与密钥、密钥长度与**指纹**（与三进程日志同一算法，
   已用 PowerShell / openssl / .NET 三方对同一把密钥得到 `D7A8A7DB` 钉成单测
   `密钥指纹与外部工具口径一致`）、监听端口上是否还坐着改动前启动的旧进程、以及
   `Horizon.Game.Message.dll` 在"构建输出 / `HundunWorld\Output\Windows` / `Flax_1.12\Binaries\Tools`"
   三处是否同一份构建。加 `-ProbeSql`（需 `sqlcmd` 在 PATH）才会去查授权表存在与否，默认不碰数据库。
1. 生成一把密钥：`openssl rand -base64 32`（或 PowerShell `[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))`）。
2. 在**三个**启动会话里各设同值：`ScriptedSpawn__Enabled=1`（`true/on/yes` 同样认）与 `ScriptedSpawn__SigningKeyBase64=<上一步输出>`。
3. 依次启动 Silo → 网关 → DS：`powershell -File scripts\start-scripted-local.ps1 -Key "<base64>" [-BootstrapAdminIds <角色ID>]`。它只做三件事——把同一对变量注入这三个子进程、按依赖顺序拉起、结束时把三行 `[ScriptedSpawn]` 摘要打出来比对指纹；端口已被占用时**直接拒绝而不是杀进程**，日志写进 `%TEMP%\hzn-scripted-launch\` 而不是仓库里；加 `-DryRun` 可以先看它打算做什么而不启动任何东西。（`IGmCommandGrain` 是 Version(2)，Silo 与网关必须同批，不可混跑。）
4. 执行 `scripts/sql/gm_script_grant.sql` 建表（未建表时授予操作会失败而不是静默放行）。
5. 第一次授权：把操作者角色 ID 临时列入 `ScriptedSpawn:BootstrapAdminIds`（Silo 配置）→ 游戏内
   `/grant <角色ID> 4095`（4095 = `GameMaster` = `WorldDirector`2047 | `GmOps`2048；只要编排能力给 2047）
   → 重启 Silo 清空该列表。此后权限只存 Redis 热层 + SQLServer 权威层，引导列表应长期为空。
   核查用 `/audit [条数]`，自查当前生效位用 `/scriptperm`。

### 未做（明确记录，不是遗漏）
- **`UseDS=false` 的 Orleans 回退通道不生成脚本实体**：脚本生成的执行体是 `DsWorldSession`（负 `SpawnPointId` 域复用
  `SpawnDirectorCore` 的槽位池/AI/重生/快照）。`ZoneShardGrain` 侧要等价能力，等于把这套规则抄第二份，
  且该文件正被另一路改动占用。当前表现是 DS 不可用时**如实回"世界服务暂不可用，指令未生效（配额已消耗）"**
  而不是谎报受理——与 §7 的 fail-closed 口径一致。灰度回退期间若要出怪，把该 shard 切回 DS 即可。
- **脚本生成物不持久化**（明确决定，不是遗漏）：`CreateScriptedPoint` 只改 DS 进程内的刷怪点表，
  不写 Redis/SQL。DS 重启即全部消失，只有授权记录（Redis 热层 + SQLServer）留存。
  理由：脚本生成是**运营动作**而非世界资产——把 GM 一次即兴生成落库，会让"重启后世界该长什么样"
  变成需要人工清理的存量，且触发区域这类有限寿命对象本就该随进程结束而消失。
  操作者视角的后果：重启后 `/spawn` 需要重发；要长期存在的怪群请配进静态刷怪点（`SpawnPointConfig`）。
  审计链不受影响但**分工明确**：Grain 审计记的是 `OperationId=Nonce` 与请求号（受理侧），
  实体 ID 只出现在 DS 的执行回执与日志里；两边靠 `RequestId`/`Nonce` 对齐，不要指望在一处看到全部。
- **区域可视化**：工程内无体积渲染，画它需新增协议包，故不做。
- **NPC 交互语义**：NPC 已按 `NetworkEntityType.Npc` 正确下发（可选中、有名牌与等级），但"按了能干什么"缺一个产品定义。


# 混沌世界 MMORPG 战斗 HUD 子系统后端接口契约文档

| 字段 | 值 |
|------|----|
| 文档编号 | 01-COMBAT-HUD-API |
| 子系统 | 战斗 HUD（Combat HUD） |
| 版本 | v1.0.0 |
| 技术栈 | .NET 10 C# + Orleans + SqlServer + Redis + EFCore + TouchSocket + MemoryPack |
| 文档状态 | 草案（Draft） |
| 最后更新 | 2026-07-19 |
| 适用范围 | 后端服务、网关、客户端通信层、前端战斗 HUD UI 模块 |

---

## 1. 子系统概述与领域边界

### 1.1 子系统职责

战斗 HUD 子系统（Combat HUD Subsystem）负责向前端战斗界面层实时推送与查询玩家在战斗过程中的全部"可视战斗状态"。该子系统是战斗逻辑层（`CombatGrain` / `SkillGrain` / `EffectGrain`）与前端 HUD 渲染层（`HUD.cs` / `Canvas.cs`）之间的"读取/订阅投影层"，不负责伤害结算、属性变更等真正的业务逻辑，仅承担以下职责：

- 聚合并缓存当前玩家的战斗 HUD 视图状态
- 将战斗逻辑层产生的事件（伤害、Buff、技能冷却、目标切换等）转译为前端可消费的推送消息
- 维护小队（Team / Party）维度的成员战斗状态聚合视图
- 提供低延迟（< 16ms，对应 60 FPS）的内存级读取路径
- 提供按需拉取（Pull）与订阅推送（Push）双通道访问能力

### 1.2 设计目标

| 指标 | 目标值 | 说明 |
|------|--------|------|
| 单次 HUD 状态读取 RTT（P99） | ≤ 4ms | 命中 Redis 缓存 |
| 推送消息到端延迟（P99） | ≤ 50ms | TouchSocket WebSocket 通道 |
| 单 Grain 内存占用（活跃状态） | ≤ 8KB | MemoryPack 紧凑序列化 |
| 单服同时订阅玩家数 | ≥ 5000 | 单 Silo 集群规模 |
| 伤害飘字丢失率 | ≤ 0.1% | 断线重连补帧 |
| Redis 缓存命中率 | ≥ 95% | 30s TTL + 主动刷新 |

### 1.3 数据范围（领域实体清单）

战斗 HUD 子系统所管理的实时数据，按 HUD 区块划分为以下八类：

1. **血量（Health）**：当前 HP、最大 HP、HP 变化速率（每秒回复/流失）
2. **内力（NeiLi / Energy）**：当前内力值、最大内力值、内力恢复速率
3. **怒气（Rage）**：当前怒气值、最大怒气值（用于怒气技能释放判定）
4. **技能冷却（Skill Cooldown）**：当前处于冷却中的技能列表、剩余冷却时间、冷却进度百分比
5. **Buff（增益状态）**：当前施加于自身的正面状态列表（持续时长、堆叠层数、来源）
6. **Debuff（负面状态）**：当前施加于自身的负面状态列表（同上）
7. **目标信息（Target Info）**：当前锁定目标（敌人/友方）的简要战斗状态（血条、距离、等级、阵营）
8. **伤害飘字（Damage Float）**：近 N 秒内产生的伤害/治疗数字流（含暴击、闪避、格挡标记）
9. **小队状态（Party Status）**：当前所在小队成员的血量/内力/怒气/在线状态摘要

> 说明：本子系统仅负责"展示数据"的聚合与推送，不持有伤害结算权威副本（Authoritative State），权威状态归属于 `CombatGrain`。HUD 子系统以只读投影（Read Projection）方式订阅战斗事件流并维护自身缓存。

### 1.4 领域边界（与其他子系统关系）

```text
                         ┌─────────────────────────────────────────┐
                         │            TouchSocket Gateway          │
                         │   (TCP/WebSocket, MessagePack binary)    │
                         └──────────┬───────────────────┬──────────┘
                                    │ 请求/响应           │ 推送
                                    ▼                    ▼
┌──────────────┐   事件订阅   ┌─────────────────────────────────┐   读取/缓存
│  CombatGrain │ ──────────▶ │     ICombatHudGrain (本子系统)   │ ◀─────────▶ Redis
│  (权威源)    │   事件流     │  - CombatHudState               │   Hash/SortedSet
└──────────────┘              │  - PartyCombatState             │
       ▲                      └────────────┬────────────────────┘
       │ 持久化                              │ 快照回写（按事件）
       │                                     ▼
┌──────┴───────┐   成员变更事件     ┌────────────────────────┐
│   SqlServer  │ ◀──────────────── │   IPartyCombatGrain    │
│ (EFCore)     │                    │   (小队维度聚合)        │
└──────────────┘                    └────────────────────────┘
```

**职责划分红线**：

- `CombatGrain`：战斗逻辑权威源，负责伤害结算、Buff 生效、技能消耗扣减
- `ICombatHudGrain`（本文档）：从 `CombatGrain` 订阅事件，聚合为 HUD 展示视图，对外提供查询/推送
- `IPartyCombatGrain`（本文档）：从 `TeamGrain` 订阅成员变更，聚合小队战斗状态视图
- 前端 HUD UI：**只**通过 TouchSocket 与 `ICombatHudGrain` 通信，**不**直接调用 `CombatGrain`

### 1.5 非目标（明确不做）

- 不负责属性计算公式（暴击率、伤害减免等由 `CombatGrain` 计算）
- 不负责战斗判定（命中、闪避由 `CombatGrain` 计算）
- 不负责持久化玩家最终属性快照（由 `CharacterEntity` 持久化）
- 不负责小队成员管理（加入/退出/踢人由 `TeamGrain` 处理）
- 不负责跨服战斗同步（跨服战斗走独立 `CrossServerTransferGrain`）

---

## 2. Orleans Grain 接口定义

### 2.1 命名空间约定

| 项目 | 命名空间 |
|------|---------|
| Grain 接口 | `Horizon.Orleans.Interface` |
| Grain 实现 | `Horizon.Orleans.Grains` |
| Grain 状态 | `Horizon.Orleans.States` |
| 持久化存储名 | `OrleansConst.GameStore`（SqlServer AdoNet） |
| 内存缓存存储名 | `OrleansConst.PubSubStore`（Redis 订阅） |

> 与项目现有 `CombatGrain` / `TeamGrain` / `SkillGrain` 保持命名空间一致。

### 2.2 ICombatHudGrain（玩家维度 HUD 投影）

`ICombatHudGrain` 是战斗 HUD 子系统的核心 Grain，每个在线玩家对应一个 Grain 实例。其职责是：聚合该玩家的全部战斗展示状态，并为前端提供"拉取 + 订阅"双通道访问。

**GrainKey 策略**：
- Key 类型：`long`（`playerId` / `characterId`）
- Key 来源：玩家登录后由网关分配的 `CharacterEntity.Id`
- 生命周期：玩家在线期间常驻；玩家下线 60s 后由 Orleans Deactivation 策略回收
- 部署：默认 `Default` silo，采用 `RandomPlacement`（与 CombatGrain 同 Key，但 Grain 类型不同，互不影响）

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network.CombatHud;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 战斗 HUD 投影 Grain 接口。
    /// GrainKey: playerId (long)
    /// </summary>
    public interface ICombatHudGrain : IGrainWithIntegerKey
    {
        /// <summary>获取当前玩家的完整战斗 HUD 状态（首次进入战斗、断线重连时全量拉取）</summary>
        Task<CombatHudStateDto> GetHudStateAsync();

        /// <summary>获取 HUD 状态中的指定分块（部分拉取，降低带宽）</summary>
        /// <param name="section">分块标识（Health/Energy/Rage/Buffs/Cooldowns/Target）</param>
        Task<CombatHudStateDto> GetHudSectionAsync(HudSection section);

        /// <summary>订阅伤害飘字推送流，订阅成功后通过 Observer 反向推送到客户端代理</summary>
        /// <param name="subscriber">订阅者句柄（通常为网关侧 ICombatHudObserver）</param>
        /// <param name="filter">过滤条件（如仅订阅暴击、仅订阅自身伤害）</param>
        /// <returns>订阅句柄 Guid，用于后续取消订阅</returns>
        Task<Guid> SubscribeDamageAsync(ICombatHudObserver subscriber, DamageFloatFilter filter);

        /// <summary>取消伤害飘字订阅</summary>
        Task UnsubscribeAsync(Guid subscriptionId);

        /// <summary>通知 HUD 投影层：战斗逻辑层产生了伤害事件（由 CombatGrain 调用）</summary>
        Task NotifyDamageAsync(DamageFloatDto damage);

        /// <summary>通知 HUD 投影层：Buff 列表发生变化（由 EffectGrain 调用）</summary>
        Task NotifyBuffUpdateAsync(BuffDto buff, BuffChangeType changeType);

        /// <summary>通知 HUD 投影层：技能冷却状态发生变化（由 SkillGrain 调用）</summary>
        Task NotifySkillCooldownAsync(SkillCooldownDto cooldown);

        /// <summary>通知 HUD 投影层：当前锁定目标发生变化（null 表示清空目标）</summary>
        Task NotifyTargetChangeAsync(TargetInfoDto? targetInfo);

        /// <summary>强制刷新缓存并重建 HUD 状态（玩家死亡复活、跨场景切换、GM 强制刷新）</summary>
        Task<CombatHudStateDto> RefreshAsync();

        /// <summary>心跳保活。前端每 5s 调用一次，续期 Redis TTL 并检测连接状态</summary>
        Task<DateTime> HeartbeatAsync();
    }

    /// <summary>HUD 状态分块标识</summary>
    public enum HudSection
    {
        All = 0, Health = 1, Energy = 2, Rage = 3,
        Buffs = 4, Debuffs = 5, Cooldowns = 6, Target = 7
    }

    /// <summary>战斗 HUD 观察者接口（网关侧实现，用于 Grain 反向推送）</summary>
    public interface ICombatHudObserver : IGrainObserver
    {
        Task OnDamageFloatAsync(DamageFloatDto damage);     // 伤害飘字推送
        Task OnStatePushAsync(CombatStatePush push);        // HUD 状态变更推送
        Task OnBuffUpdateAsync(BuffUpdatePush push);        // Buff 更新推送
        Task OnSkillCooldownAsync(SkillCooldownPush push); // 技能冷却推送
    }

    /// <summary>伤害飘字订阅过滤条件</summary>
    [MemoryPackable]
    public partial record DamageFloatFilter
    {
        [MemoryPackOrder(0)] public bool CriticalOnly { get; init; }            // 仅订阅暴击伤害
        [MemoryPackOrder(1)] public bool SelfDamageOnly { get; init; } = true; // 仅订阅自身造成的伤害
        [MemoryPackOrder(2)] public int MaxDurationSeconds { get; init; } = 3600; // 最大订阅时长（秒）
        [MemoryPackOrder(3)] public int MinDamageThreshold { get; init; }      // 伤害下限过滤
    }
}
```

### 2.3 IPartyCombatGrain（小队维度 HUD 投影）

`IPartyCombatGrain` 聚合一个小队所有成员的战斗 HUD 摘要，供小队 UI 面板渲染。一个玩家的小队 UI 只需要成员的"血条 / 内力条 / 怒气条 / 在线状态"四个字段，无需完整 HUD 状态。

**GrainKey 策略**：
- Key 类型：`long`（`teamId`）
- Key 来源：`TeamGrain` 创建小队时分配的 `TeamEntity.Id`
- 生命周期：小队存在期间常驻；解散后立即回收
- 部署：`Default` silo

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network.CombatHud;
using Orleans;

namespace Horizon.Orleans.Interface
{
    /// <summary>小队战斗状态聚合 Grain。GrainKey: teamId (long)</summary>
    public interface IPartyCombatGrain : IGrainWithIntegerKey
    {
        /// <summary>获取小队全部成员的战斗状态摘要</summary>
        Task<IReadOnlyList<PartyMemberDto>> GetPartyStatusAsync();

        /// <summary>订阅小队成员战斗状态变更推送（成员血量/内力/怒气变化超过阈值时触发，默认 5%）</summary>
        Task<Guid> SubscribePartyStatusAsync(ICombatHudObserver subscriber);

        /// <summary>通知小队聚合 Grain：某成员的战斗状态发生变更（由该成员所属的 ICombatHudGrain 调用）</summary>
        Task NotifyMemberUpdateAsync(long playerId, PartyMemberDto delta);

        /// <summary>通知小队聚合 Grain：成员加入/离开小队（由 TeamGrain 调用）</summary>
        Task NotifyMemberRosterAsync(long playerId, bool isJoin);

        /// <summary>获取小队中处于战斗状态的成员列表（用于战术 UI 高亮）</summary>
        Task<IReadOnlyList<long>> GetInCombatMembersAsync();
    }
}
```

### 2.4 GrainKey 总览

| Grain 类型 | Key 类型 | Key 取值 | 策略说明 |
|-----------|---------|---------|---------|
| `ICombatHudGrain` | `long` | `playerId` | 一玩家一实例；与 `CombatGrain` 同 Key 不同类型 |
| `IPartyCombatGrain` | `long` | `teamId` | 一小队一实例；与 `TeamGrain` 同 Key 不同类型 |
| `ICombatHudObserver`（Observer） | `Guid` | 订阅时生成 | 网关侧每条 WebSocket 连接一个 Observer |

### 2.5 Grain 间调用拓扑

```text
CombatGrain.ProcessAttackAsync()
   │
   ├──(计算完成)──▶ ICombatHudGrain[victim].NotifyDamageAsync(damage)
   │                    │
   │                    ├──(更新内存状态)
   │                    ├──(写 Redis Hash, 续期 TTL)
   │                    └──(推送到 ICombatHudObserver.OnDamageFloatAsync)
   │
   └──(若是小队成员)──▶ IPartyCombatGrain[teamId].NotifyMemberUpdateAsync(playerId, delta)
                            │
                            └──(广播到小队所有成员的 Observer)
```

---

## 3. Grain 状态与持久化

### 3.1 CombatHudGrainState 内存结构

Grain 内部维护一个轻量的内存状态对象，该状态**不直接落 SqlServer**，而是写 Redis Hash 作为热缓存。SqlServer 仅持久化"快照"与"日志"，详见第 6 节。

```csharp
using System;
using System.Collections.Generic;
using Horizon.Game.Message.Network.CombatHud;

namespace Horizon.Orleans.States
{
    /// <summary>战斗 HUD Grain 的内存状态（不直接持久化到 SqlServer，镜像写入 Redis Hash）</summary>
    [GenerateSerializer]
    public class CombatHudGrainState
    {
        [Id(0)]  public long PlayerId { get; set; }
        [Id(1)]  public int CurrentHealth { get; set; }       // 当前 HP
        [Id(2)]  public int MaxHealth { get; set; }            // 最大 HP
        [Id(3)]  public int CurrentNeiLi { get; set; }         // 当前内力
        [Id(4)]  public int MaxNeiLi { get; set; }             // 最大内力
        [Id(5)]  public int CurrentRage { get; set; }          // 当前怒气
        [Id(6)]  public int MaxRage { get; set; }               // 最大怒气
        [Id(7)]  public List<BuffDto> Buffs { get; set; } = new();
        [Id(8)]  public List<BuffDto> Debuffs { get; set; } = new();
        [Id(9)]  public List<SkillCooldownDto> Cooldowns { get; set; } = new();
        [Id(10)] public TargetInfoDto? Target { get; set; }    // 当前目标信息（无目标为 null）
        [Id(11)] public bool IsInCombat { get; set; }           // 是否处于战斗状态
        [Id(12)] public long TeamId { get; set; }              // 所在小队 ID（无小队为 0）
        [Id(13)] public DateTime LastHeartbeat { get; set; }   // 最近一次心跳时间
        [Id(14)] public DateTime LastStateChange { get; set; } // 最近一次状态变更时间
        [Id(15)] public List<Guid> SubscriptionHandles { get; set; } = new(); // 当前活跃的订阅句柄列表
    }
}
```

### 3.2 Redis Hash 结构（热缓存）

Grain 内部状态通过 Redis Hash 镜像到缓存层，键名为 `hundun:combat:{playerId}:hud`，TTL 30s。前端可绕过 Grain 直接读 Redis（仅读路径）以降低延迟。

**Hash 字段映射**：

| Hash Field | 类型 | 说明 | 与 Grain 字段对应 |
|-----------|------|------|------------------|
| `playerId` | string | 玩家 ID | `PlayerId` |
| `health` | int (string) | 当前 HP | `CurrentHealth` |
| `maxHealth` | int (string) | 最大 HP | `MaxHealth` |
| `neiLi` | int (string) | 当前内力 | `CurrentNeiLi` |
| `maxNeiLi` | int (string) | 最大内力 | `MaxNeiLi` |
| `rage` | int (string) | 当前怒气 | `CurrentRage` |
| `maxRage` | int (string) | 最大怒气 | `MaxRage` |
| `isInCombat` | "0"/"1" | 是否战斗中 | `IsInCombat` |
| `teamId` | int (string) | 小队 ID | `TeamId` |
| `lastUpdate` | long (unix ms) | 最近更新时间戳 | `LastStateChange` |
| `buffs` | MemoryPack binary | Buff 列表二进制 | `Buffs` |
| `debuffs` | MemoryPack binary | Debuff 列表二进制 | `Debuffs` |
| `cooldowns` | MemoryPack binary | 冷却列表二进制 | `Cooldowns` |
| `target` | MemoryPack binary | 目标信息二进制 | `Target` |

> 说明：标量字段用 string 存储，便于运维直接 `HGETALL` 调试；列表字段用 MemoryPack 二进制存储以降低内存占用与序列化开销。

### 3.3 TTL 与回写策略

| 数据项 | TTL | 回写时机 | 回写方式 |
|--------|-----|---------|---------|
| `hundun:combat:{playerId}:hud` | 30s | 每次 HUD 状态变更、每 5s 心跳 | 写穿（Write-Through） |
| `hundun:combat:{playerId}:cooldowns` | 60s | 技能释放、冷却完成 | Sorted Set 增量更新 |
| `hundun:combat:{playerId}:damage:recent` | 10s | 每次伤害事件 | List LPUSH + LTRIM（保留最近 50 条） |

**回写策略说明（Write-Through）**：

1. **写路径**：Grain 内存状态变更 → 同步写 Redis Hash（`HMSET` + `EXPIRE`） → 异步推送给前端 Observer
2. **读路径**：前端读请求优先命中 Redis Hash；Redis 未命中或 TTL < 5s 时，回源到 Grain 内存；Grain 激活时若内存为空，则从 `CombatGrain` 重建
3. **失败回退**：Redis 写失败不阻塞业务流程，仅记录告警日志（缓存不可用降级为直读 Grain）
4. **续期**：心跳调用 `HeartbeatAsync()` 时执行 `EXPIRE` 续期 TTL；连续 3 次心跳缺失则视为客户端断线，停止推送并启动 30s 倒计时回收

**冷启动重建流程**：

```text
1. ICombatHudGrain 激活（OnActivateAsync）
2. 检查 Redis Hash 是否存在且未过期
   ├── 存在且 TTL > 5s ──▶ 直接加载到内存
   └── 不存在或过期 ──▶ 调用 CombatGrain.GetCombatInfoAsync(playerId) 重建
3. 重建完成后写入 Redis Hash（含 30s TTL）
4. 标记 LastStateChange = DateTime.UtcNow
5. 进入正常服务状态
```

### 3.4 持久化存储配置

Grain 状态本身**不使用** Orleans 的 `IPersistentState`（即不通过 ADO.NET 持久化 Grain 状态本身），仅持久化"快照"与"日志"表（详见第 6 节）。配置文件 `Orleans.silo.json` 中的存储声明：

```json
{
  "Orleans": {
    "Storage": {
      "GameStore": {
        "Type": "AdoNet",
        "ConnectionString": "...SqlServer连接串...",
        "AdoInvariant": "System.Data.SqlClient"
      }
    }
  }
}
```

> Redis 客户端使用 `StackExchange.Redis`，由 `Horizon.Core.Cache`（`Cache.cs`）封装为 `ICache` 注入到 Grain 中。

---

## 4. TouchSocket 消息协议

### 4.1 协议总览

战斗 HUD 消息复用项目现有的 `MessageUnion` 联合体机制（位于 `Horizon.Game.Message/MessageUnion.cs`）。本子系统新增的消息类型编号从 `300` 开始（与已有的 0~225 段不冲突）。

**传输通道**：

| 通道 | 协议 | 用途 |
|------|------|------|
| 主请求/响应通道 | TouchSocket TCP（长连接） | 玩家主动查询、订阅请求 |
| 推送通道 | TouchSocket WebSocket（长连接） | 服务端主动推送战斗事件 |
| 降级通道 | TouchSocket UDP（可选） | 伤害飘字等容忍丢包的推送 |

### 4.2 消息类型枚举

新增 `CombatHudMessageType` 枚举（与项目 `MessageType` 体系独立，用于 HUD 内部路由）：

```csharp
using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 战斗 HUD 消息类型枚举。
    /// 编号范围 300~399，避免与 MessageUnion 已有 0~225 冲突。
    /// </summary>
    public enum CombatHudMessageType : ushort
    {
        HudStateRequest       = 300, // 请求：获取完整 HUD 状态
        HudStateResponse      = 301, // 响应：完整 HUD 状态
        HudSectionRequest     = 302, // 请求：获取 HUD 指定分块
        HudSectionResponse    = 303, // 响应：HUD 分块状态
        SubscribeDamageRequest  = 310, // 请求：订阅伤害飘字
        SubscribeDamageResponse = 311, // 响应：订阅结果（含订阅句柄）
        UnsubscribeRequest      = 312, // 请求：取消订阅
        UnsubscribeResponse     = 313, // 响应：取消订阅结果
        HeartbeatRequest       = 314, // 请求：心跳保活
        HeartbeatResponse      = 315, // 响应：心跳确认
        CombatStatePush        = 320, // 推送：HUD 状态变更（增量）
        DamageFloatPush        = 321, // 推送：伤害飘字
        BuffUpdatePush         = 322, // 推送：Buff 更新
        SkillCooldownPush      = 323, // 推送：技能冷却更新
        TargetChangePush       = 324, // 推送：目标变更
        PartyMemberPush        = 330, // 推送：小队成员状态变更
        CombatFlagPush         = 331, // 推送：战斗状态进入/退出
        DeathPush              = 332, // 推送：死亡通知
        ErrorResponse          = 399  // 错误响应
    }

    /// <summary>Buff 变更类型</summary>
    public enum BuffChangeType : byte
    {
        Added = 0, Updated = 1, Removed = 2, StackChanged = 3, Expired = 4, Dispersed = 5
    }

    /// <summary>伤害飘字类型</summary>
    public enum DamageFloatKind : byte
    {
        PhysicalDamage = 0, MagicDamage = 1, WuxingDamage = 2, CriticalDamage = 3,
        Heal = 4, Block = 5, Dodge = 6, Miss = 7, TrueDamage = 8
    }
}
```

### 4.3 请求消息体

#### 4.3.1 HudStateRequest / HudStateResponse

```csharp
using MemoryPack;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>请求获取完整战斗 HUD 状态</summary>
    [MemoryPackable]
    public partial record HudStateRequest
    {
        [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;    // 请求 ID
        [MemoryPackOrder(1)] public long PlayerId { get; init; }                       // 玩家 ID（网关注入）
        [MemoryPackOrder(2)] public bool ForceRefresh { get; init; }                    // 是否强制刷新缓存
        [MemoryPackOrder(3)] public ulong ClientVersion { get; init; }                 // 客户端当前版本号
    }

    /// <summary>完整 HUD 状态响应</summary>
    [MemoryPackable]
    public partial record HudStateResponse
    {
        [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
        [MemoryPackOrder(1)] public int Code { get; init; }                       // 响应码（0 表示成功）
        [MemoryPackOrder(2)] public string Message { get; init; } = string.Empty; // 错误消息
        [MemoryPackOrder(3)] public CombatHudStateDto State { get; init; } = default!;
        [MemoryPackOrder(4)] public ulong ServerVersion { get; init; }            // 服务端 HUD 版本号
        [MemoryPackOrder(5)] public bool IsDelta { get; init; }                   // 是否为增量响应
    }
}
```

#### 4.3.2 订阅与心跳消息

```csharp
[MemoryPackable]
public partial record SubscribeDamageRequest
{
    [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public DamageFloatFilter Filter { get; init; } = new();
}

[MemoryPackable]
public partial record SubscribeDamageResponse
{
    [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public int Code { get; init; }
    [MemoryPackOrder(2)] public string Message { get; init; } = string.Empty;
    [MemoryPackOrder(3)] public Guid SubscriptionId { get; init; }  // 订阅句柄
    [MemoryPackOrder(4)] public int ExpiresIn { get; init; }       // 订阅有效期（秒）
}

[MemoryPackable]
public partial record UnsubscribeRequest
{
    [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public Guid SubscriptionId { get; init; }
}

[MemoryPackable]
public partial record UnsubscribeResponse
{
    [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public int Code { get; init; }
    [MemoryPackOrder(2)] public bool Success { get; init; }
}

[MemoryPackable]
public partial record HeartbeatRequest
{
    [MemoryPackOrder(0)] public DateTime ClientTime { get; init; }
    [MemoryPackOrder(1)] public ulong ClientVersion { get; init; }
}

[MemoryPackable]
public partial record HeartbeatResponse
{
    [MemoryPackOrder(0)] public DateTime ServerTime { get; init; }
    [MemoryPackOrder(1)] public ulong ServerVersion { get; init; }
    [MemoryPackOrder(2)] public int PlayerId { get; init; }
}
```

### 4.4 推送消息体（服务端 → 客户端，单向）

#### 4.4.1 CombatStatePush（HUD 状态变更推送）

```csharp
using System.Collections.Generic;
using MemoryPack;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>
    /// HUD 状态变更推送。仅推送发生变化的字段，未变更字段为 null（引用类型）或默认值（值类型）。
    /// 客户端按字段是否为 null 决定是否更新本地视图。
    /// </summary>
    [MemoryPackable]
    public partial record CombatStatePush
    {
        [MemoryPackOrder(0)] public ulong Sequence { get; init; }       // 推送序号（单调递增，检测丢帧）
        [MemoryPackOrder(1)] public long Timestamp { get; init; }       // 推送时间戳（Unix 毫秒）
        [MemoryPackOrder(2)] public int? CurrentHealth { get; init; }    // 当前 HP（变更时填充）
        [MemoryPackOrder(3)] public int? MaxHealth { get; init; }        // 最大 HP
        [MemoryPackOrder(4)] public int? CurrentNeiLi { get; init; }     // 当前内力
        [MemoryPackOrder(5)] public int? MaxNeiLi { get; init; }          // 最大内力
        [MemoryPackOrder(6)] public int? CurrentRage { get; init; }      // 当前怒气
        [MemoryPackOrder(7)] public int? MaxRage { get; init; }           // 最大怒气
        [MemoryPackOrder(8)] public bool? IsInCombat { get; init; }      // 是否进入/退出战斗
        [MemoryPackOrder(9)] public string Reason { get; init; } = string.Empty; // 推送触发原因
    }
}
```

#### 4.4.2 DamageFloatPush / BuffUpdatePush / SkillCooldownPush

```csharp
[MemoryPackable]
public partial record DamageFloatPush
{
    [MemoryPackOrder(0)] public ulong Sequence { get; init; }
    [MemoryPackOrder(1)] public List<DamageFloatDto> Floats { get; init; } = new(); // 飘字事件列表
}

[MemoryPackable]
public partial record BuffUpdatePush
{
    [MemoryPackOrder(0)] public ulong Sequence { get; init; }
    [MemoryPackOrder(1)] public BuffDto Buff { get; init; } = default!;       // 变更的 Buff
    [MemoryPackOrder(2)] public BuffChangeType ChangeType { get; init; }      // 变更类型
    [MemoryPackOrder(3)] public bool IsDebuff { get; init; }                  // 是否为 Debuff
}

[MemoryPackable]
public partial record SkillCooldownPush
{
    [MemoryPackOrder(0)] public ulong Sequence { get; init; }
    [MemoryPackOrder(1)] public SkillCooldownDto Cooldown { get; init; } = default!;
    [MemoryPackOrder(2)] public bool IsReady { get; init; } // 是否冷却完成
}

[MemoryPackable]
public partial record TargetChangePush
{
    [MemoryPackOrder(0)] public ulong Sequence { get; init; }
    [MemoryPackOrder(1)] public TargetInfoDto? Target { get; init; } // null 表示清空目标
}

[MemoryPackable]
public partial record PartyMemberPush
{
    [MemoryPackOrder(0)] public ulong Sequence { get; init; }
    [MemoryPackOrder(1)] public List<PartyMemberDto> Members { get; init; } = new();
    [MemoryPackOrder(2)] public bool IsDelta { get; init; } // 是否为增量变更
}
```

### 4.5 消息封装格式

所有 TouchSocket 消息包统一封装格式（与项目 `PacketParser` 兼容）：

```text
+--------+--------+--------+----------+----------------------+
| Magic  | Version| MsgType| BodyLen  | Body (MemoryPack)    |
| 2 字节 | 1 字节 | 2 字节 | 4 字节   | N 字节               |
+--------+--------+--------+----------+----------------------+
| 0x48 0x44 | 0x01 | enum   | uint32   | binary               |
+--------+--------+--------+----------+----------------------+

说明：
- Magic = 0x48 0x44 (ASCII "HD")
- Version = 0x01（首版）
- MsgType = CombatHudMessageType 枚举值（小端序）
- BodyLen = Body 字节数（不含头部）
- Body = MemoryPack 序列化后的消息体二进制
```

### 4.6 错误响应与错误码

```csharp
[MemoryPackable]
public partial record HudErrorResponse
{
    [MemoryPackOrder(0)] public string RequestId { get; init; } = string.Empty;
    [MemoryPackOrder(1)] public int Code { get; init; }
    [MemoryPackOrder(2)] public string Message { get; init; } = string.Empty;
    [MemoryPackOrder(3)] public int RetryAfterMs { get; init; } // 建议重试时间（0 表示不可重试）
}
```

| Code | 含义 | 处理建议 |
|------|------|---------|
| 0 | 成功 | - |
| 1001 | 玩家未登录 | 跳转登录 |
| 1002 | Grain 未激活 | 等待 500ms 重试 |
| 1003 | Redis 不可用 | 降级直读 Grain |
| 1004 | 订阅句柄无效 | 重新订阅 |
| 1005 | 频率超限 | 客户端节流 |
| 2001 | 参数错误 | 校验入参 |
| 5000 | 内部异常 | 上报日志 |

---

## 5. MemoryPack 序列化 DTO

本节定义战斗 HUD 子系统对外暴露的全部 DTO。所有 DTO 满足以下约束：

1. 使用 `record` 语法（不可变，线程安全）
2. 标注 `[MemoryPackable]`，采用 `SerializeLayout.Explicit` 显式指定字段顺序
3. 字段顺序从 0 开始连续递增，**禁止跳号**（保证协议向前兼容）
4. 引用类型字段默认值不得为 null（用 `= string.Empty` / `= new()`）
5. 字段顺序 ≤ 250（保留 254/255 给 `MessageUnion` 基类）

### 5.1 CombatHudStateDto（HUD 完整状态）

```csharp
using System;
using System.Collections.Generic;
using MemoryPack;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>战斗 HUD 完整状态 DTO。用于首次拉取、断线重连、强制刷新等全量场景。</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record CombatHudStateDto
    {
        [MemoryPackOrder(0)]  public long PlayerId { get; init; }
        [MemoryPackOrder(1)]  public string PlayerName { get; init; } = string.Empty;
        [MemoryPackOrder(2)]  public int CurrentHealth { get; init; }       // 当前 HP
        [MemoryPackOrder(3)]  public int MaxHealth { get; init; }          // 最大 HP
        [MemoryPackOrder(4)]  public int CurrentNeiLi { get; init; }        // 当前内力
        [MemoryPackOrder(5)]  public int MaxNeiLi { get; init; }            // 最大内力
        [MemoryPackOrder(6)]  public int CurrentRage { get; init; }         // 当前怒气
        [MemoryPackOrder(7)]  public int MaxRage { get; init; }             // 最大怒气
        [MemoryPackOrder(8)]  public float HealthRegenPerSec { get; init; } // HP 回复速率（每秒）
        [MemoryPackOrder(9)]  public float NeiLiRegenPerSec { get; init; }  // 内力回复速率（每秒）
        [MemoryPackOrder(10)] public float RageGainPerSec { get; init; }    // 怒气增长速率（每秒，受击时累计）
        [MemoryPackOrder(11)] public List<BuffDto> Buffs { get; init; } = new();
        [MemoryPackOrder(12)] public List<BuffDto> Debuffs { get; init; } = new();
        [MemoryPackOrder(13)] public List<SkillCooldownDto> Cooldowns { get; init; } = new();
        [MemoryPackOrder(14)] public TargetInfoDto? Target { get; init; }   // 当前目标信息
        [MemoryPackOrder(15)] public bool IsInCombat { get; init; }          // 是否处于战斗状态
        [MemoryPackOrder(16)] public long TeamId { get; init; }              // 所在小队 ID
        [MemoryPackOrder(17)] public ulong ServerVersion { get; init; }     // 服务端版本号（单调递增）
        [MemoryPackOrder(18)] public long SnapshotTimestamp { get; init; }  // 快照时间戳（Unix 毫秒）
    }
}
```

### 5.2 DamageFloatDto（伤害飘字）

```csharp
using MemoryPack;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>伤害飘字 DTO。一条记录对应 HUD 上的一次飘字动画。</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record DamageFloatDto
    {
        [MemoryPackOrder(0)]  public ulong FloatId { get; init; }       // 飘字唯一 ID（客户端去重）
        [MemoryPackOrder(1)]  public long AttackerId { get; init; }     // 造成伤害的实体 ID
        [MemoryPackOrder(2)]  public long VictimId { get; init; }       // 承受伤害的实体 ID
        [MemoryPackOrder(3)]  public int Amount { get; init; }           // 伤害数值
        [MemoryPackOrder(4)]  public DamageFloatKind Kind { get; init; } // 飘字类型
        [MemoryPackOrder(5)]  public byte ElementType { get; init; }     // 五行元素（0=无，1~5=金木水火土）
        [MemoryPackOrder(6)]  public int RemainingHealth { get; init; } // 目标剩余血量
        [MemoryPackOrder(7)]  public int MaxHealth { get; init; }        // 目标最大血量
        [MemoryPackOrder(8)]  public float WorldX { get; init; }         // 飘字世界坐标 X
        [MemoryPackOrder(9)]  public float WorldY { get; init; }         // 飘字世界坐标 Y
        [MemoryPackOrder(10)] public float WorldZ { get; init; }         // 飘字世界坐标 Z
        [MemoryPackOrder(11)] public long Timestamp { get; init; }      // 产生时间（Unix 毫秒）
        [MemoryPackOrder(12)] public bool IsSelf { get; init; }          // 是否对自身（true=自身受伤）
        [MemoryPackOrder(13)] public int SkillId { get; init; }          // 技能 ID（普通攻击为 0）
    }
}
```

### 5.3 BuffDto（Buff / Debuff 通用）

```csharp
using MemoryPack;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>Buff / Debuff 通用 DTO。通过 IsDebuff 字段区分增益/减益。</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record BuffDto
    {
        [MemoryPackOrder(0)]  public ulong BuffInstanceId { get; init; }  // Buff 实例 ID（服务端生成）
        [MemoryPackOrder(1)]  public int BuffId { get; init; }           // Buff 配置 ID
        [MemoryPackOrder(2)]  public string Name { get; init; } = string.Empty;
        [MemoryPackOrder(3)]  public string IconPath { get; init; } = string.Empty;
        [MemoryPackOrder(4)]  public string Description { get; init; } = string.Empty; // 含数值插值后的最终文本
        [MemoryPackOrder(5)]  public bool IsDebuff { get; init; }         // 是否为 Debuff
        [MemoryPackOrder(6)]  public int TotalDurationMs { get; init; }  // 总持续时间（毫秒，0 表示永久）
        [MemoryPackOrder(7)]  public int RemainingDurationMs { get; init; } // 剩余持续时间（毫秒）
        [MemoryPackOrder(8)]  public int StackCount { get; init; }        // 当前堆叠层数
        [MemoryPackOrder(9)]  public int MaxStackCount { get; init; }     // 最大堆叠层数
        [MemoryPackOrder(10)] public long SourceId { get; init; }         // Buff 来源实体 ID（施法者）
        [MemoryPackOrder(11)] public byte SourceType { get; init; }       // 来源类型（0=玩家，1=怪物，2=物品，3=环境）
        [MemoryPackOrder(12)] public bool Dispellable { get; init; }      // 是否可手动驱散
        [MemoryPackOrder(13)] public int Priority { get; init; }         // 优先级（越大越靠左显示）
        [MemoryPackOrder(14)] public long AppliedAt { get; init; }        // 生效时间（Unix 毫秒）
        [MemoryPackOrder(15)] public long ExpiresAt { get; init; }        // 到期时间（永久 Buff 为 long.MaxValue）
    }
}
```

### 5.4 SkillCooldownDto（技能冷却）

```csharp
using MemoryPack;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>技能冷却 DTO</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record SkillCooldownDto
    {
        [MemoryPackOrder(0)]  public int SkillId { get; init; }           // 技能 ID
        [MemoryPackOrder(1)]  public byte SlotIndex { get; init; }        // 技能槽位（0~9，对应快捷栏）
        [MemoryPackOrder(2)]  public string SkillName { get; init; } = string.Empty;
        [MemoryPackOrder(3)]  public string IconPath { get; init; } = string.Empty;
        [MemoryPackOrder(4)]  public int TotalCooldownMs { get; init; }   // 冷却总时长（毫秒）
        [MemoryPackOrder(5)]  public int RemainingCooldownMs { get; init; } // 剩余冷却时长（毫秒）
        [MemoryPackOrder(6)]  public long CooldownStartedAt { get; init; } // 冷却开始时间（Unix 毫秒）
        [MemoryPackOrder(7)]  public long CooldownEndsAt { get; init; }   // 冷却结束时间（Unix 毫秒）
        [MemoryPackOrder(8)]  public float Progress { get; init; }        // 冷却进度百分比（0.0~1.0）
        [MemoryPackOrder(9)]  public bool IsGlobalCooldown { get; init; } // 是否为公共冷却（GCD）
        [MemoryPackOrder(10)] public byte CooldownReason { get; init; }   // 冷却原因（0=释放，1=中断，2=沉默，3=缴械）
        [MemoryPackOrder(11)] public int NeiLiCost { get; init; }         // 当前技能消耗（内力）
        [MemoryPackOrder(12)] public int RageCost { get; init; }          // 当前技能消耗（怒气）
        [MemoryPackOrder(13)] public bool CanCast { get; init; }          // 是否可释放（综合判定）
        [MemoryPackOrder(14)] public byte BlockReason { get; init; }      // 不可释放原因（CanCast=false 时填充）
    }
}
```

### 5.5 TargetInfoDto 与 PartyMemberDto

```csharp
using MemoryPack;

namespace Horizon.Game.Message.Network.CombatHud
{
    /// <summary>当前锁定目标的简要战斗信息</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record TargetInfoDto
    {
        [MemoryPackOrder(0)]  public long TargetId { get; init; }
        [MemoryPackOrder(1)]  public string Name { get; init; } = string.Empty;
        [MemoryPackOrder(2)]  public int Level { get; init; }              // 目标等级
        [MemoryPackOrder(3)]  public byte Faction { get; init; }          // 阵营（0=中立，1=友方，2=敌对，3=敌对玩家）
        [MemoryPackOrder(4)]  public int CurrentHealth { get; init; }
        [MemoryPackOrder(5)]  public int MaxHealth { get; init; }
        [MemoryPackOrder(6)]  public float HealthPercent { get; init; }   // HP 百分比（0.0~1.0）
        [MemoryPackOrder(7)]  public float Distance { get; init; }       // 与自身距离（米）
        [MemoryPackOrder(8)]  public bool IsInCombat { get; init; }       // 目标是否处于战斗状态
        [MemoryPackOrder(9)]  public string Title { get; init; } = string.Empty; // 头衔/称谓
        [MemoryPackOrder(10)] public string PortraitPath { get; init; } = string.Empty;
        [MemoryPackOrder(11)] public int DebuffCount { get; init; }       // 目标身上已有 Debuff 数量
        [MemoryPackOrder(12)] public byte EntityType { get; init; }       // 类型（0=玩家，1=普通怪，2=精英，3=Boss，4=NPC）
    }

    /// <summary>
    /// 小队成员战斗状态摘要。
    /// 用于小队 UI 面板的轻量展示，比完整 HUD DTO 节省约 80% 带宽。
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial record PartyMemberDto
    {
        [MemoryPackOrder(0)]  public long PlayerId { get; init; }
        [MemoryPackOrder(1)]  public string Name { get; init; } = string.Empty;
        [MemoryPackOrder(2)]  public byte ClassId { get; init; }          // 职业（1=剑客，2=医者，3=刺客，4=力士，5=法师）
        [MemoryPackOrder(3)]  public int Level { get; init; }
        [MemoryPackOrder(4)]  public int CurrentHealth { get; init; }
        [MemoryPackOrder(5)]  public int MaxHealth { get; init; }
        [MemoryPackOrder(6)]  public int CurrentNeiLi { get; init; }
        [MemoryPackOrder(7)]  public int MaxNeiLi { get; init; }
        [MemoryPackOrder(8)]  public int CurrentRage { get; init; }
        [MemoryPackOrder(9)]  public int MaxRage { get; init; }
        [MemoryPackOrder(10)] public bool IsOnline { get; init; }
        [MemoryPackOrder(11)] public bool IsInCombat { get; init; }
        [MemoryPackOrder(12)] public bool IsDead { get; init; }
        [MemoryPackOrder(13)] public string PortraitPath { get; init; } = string.Empty;
        [MemoryPackOrder(14)] public List<BuffDto> TopBuffs { get; init; } = new(); // 前 3 个最重要 Buff（头像角标）
        [MemoryPackOrder(15)] public float Distance { get; init; }         // 距自身距离（米）
    }
}
```

### 5.6 字段顺序约定与兼容性规则

| 规则编号 | 内容 |
|---------|------|
| DTO-COMPAT-01 | 已发布 DTO 的字段顺序禁止调整，新增字段必须追加在末尾 |
| DTO-COMPAT-02 | 已发布字段类型禁止变更（int→long 必须新增字段） |
| DTO-COMPAT-03 | 删除字段时保留占位序号，新字段不得复用已删除序号 |
| DTO-COMPAT-04 | 字段顺序上限 250（254/255 保留给基类） |
| DTO-COMPAT-05 | 可空字段使用 C# `?` 修饰符，MemoryPack 会序列化 null 标志位 |
| DTO-COMPAT-06 | 集合字段必须使用 `List<T>`，禁止使用 `IEnumerable<T>`（MemoryPack 需要具体类型） |
| DTO-COMPAT-07 | 枚举字段必须显式指定底层类型（`byte`/`ushort`），避免不同编译器默认值差异 |

---

## 6. EFCore 实体与 SqlServer 表结构

战斗 HUD 子系统在 SqlServer 中持久化两类数据：

1. **CombatSnapshot**：HUD 状态快照表，定期落盘用于冷启动恢复与跨服迁移
2. **DamageLog**：伤害日志表，用于战斗复盘、伤害统计、反作弊审计

实体类位于 `Horizon.Model.GameModel` 命名空间，与项目现有 `CharacterEntity` / `BagEntity` / `ItemEntity` 同目录。

### 6.1 CombatSnapshot 实体与表结构

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;

namespace Horizon.Model.GameModel
{
    /// <summary>战斗 HUD 状态快照实体。用于玩家下线后恢复战斗 HUD、跨服迁移时携带战斗状态。</summary>
    [Table("CombatSnapshot")]
    public class CombatSnapshotEntity : IBasicEntity, ISoftDeleted
    {
        [Key] [Column("Id")] public long Id { get; set; }

        [Column("PlayerId")] [Index("IX_CombatSnapshot_PlayerId")]
        public long PlayerId { get; set; }

        [Column("CurrentHealth")] public int CurrentHealth { get; set; }
        [Column("MaxHealth")] public int MaxHealth { get; set; }
        [Column("CurrentNeiLi")] public int CurrentNeiLi { get; set; }
        [Column("MaxNeiLi")] public int MaxNeiLi { get; set; }
        [Column("CurrentRage")] public int CurrentRage { get; set; }
        [Column("MaxRage")] public int MaxRage { get; set; }

        /// <summary>Buff 列表（MemoryPack 二进制，存为 varbinary）</summary>
        [Column("Buffs")] public byte[]? Buffs { get; set; }

        /// <summary>Debuff 列表（MemoryPack 二进制）</summary>
        [Column("Debuffs")] public byte[]? Debuffs { get; set; }

        /// <summary>技能冷却列表（MemoryPack 二进制）</summary>
        [Column("Cooldowns")] public byte[]? Cooldowns { get; set; }

        [Column("TargetId")] public long TargetId { get; set; }       // 当前目标 ID（0 表示无目标）
        [Column("IsInCombat")] public bool IsInCombat { get; set; }
        [Column("TeamId")] public long TeamId { get; set; }
        [Column("Version")] public ulong Version { get; set; }         // 快照版本号

        [Column("SnapshotAt")] [Index("IX_CombatSnapshot_SnapshotAt")]
        public DateTime SnapshotAt { get; set; }

        [Column("IsDeleted")] public bool IsDeleted { get; set; }
        [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
        [Column("UpdatedAt")] public DateTime UpdatedAt { get; set; }
    }
}
```

**SqlServer 表 DDL**：

```sql
CREATE TABLE CombatSnapshot (
    Id              BIGINT         NOT NULL PRIMARY KEY,
    PlayerId        BIGINT         NOT NULL,
    CurrentHealth   INT            NOT NULL DEFAULT 0,
    MaxHealth       INT            NOT NULL DEFAULT 0,
    CurrentNeiLi    INT            NOT NULL DEFAULT 0,
    MaxNeiLi        INT            NOT NULL DEFAULT 0,
    CurrentRage     INT            NOT NULL DEFAULT 0,
    MaxRage         INT            NOT NULL DEFAULT 0,
    Buffs           VARBINARY(MAX) NULL,
    Debuffs         VARBINARY(MAX) NULL,
    Cooldowns       VARBINARY(MAX) NULL,
    TargetId        BIGINT         NOT NULL DEFAULT 0,
    IsInCombat      BIT            NOT NULL DEFAULT 0,
    TeamId          BIGINT         NOT NULL DEFAULT 0,
    Version         BIGINT         NOT NULL DEFAULT 0,
    SnapshotAt      DATETIME2(3)   NOT NULL,
    IsDeleted       BIT            NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2(3)   NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX IX_CombatSnapshot_PlayerId   ON CombatSnapshot (PlayerId);
CREATE INDEX IX_CombatSnapshot_SnapshotAt ON CombatSnapshot (SnapshotAt);
CREATE INDEX IX_CombatSnapshot_PlayerId_SnapshotAt ON CombatSnapshot (PlayerId, SnapshotAt DESC);
```

**快照写入策略**：

| 触发时机 | 写入方式 | 说明 |
|---------|---------|------|
| 玩家下线 | 同步写 | 保证下次上线可恢复 |
| 战斗结束 | 异步写 | 5s 延迟，合并多次更新 |
| 跨服迁移 | 同步写 | 迁移前必须落盘 |
| 每 60s 定时 | 异步写 | 防止意外宕机丢失 |
| 死亡复活 | 同步写 | 死亡时刻血量必须持久化 |

### 6.2 DamageLog 实体与表结构

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 伤害日志实体。用于战斗复盘、伤害统计、反作弊审计。
    /// 单条记录约 80 字节，按单服 1000 玩家在线、人均每秒 5 次伤害估算，
    /// 单日数据量约 35GB，需按月分区。
    /// </summary>
    [Table("DamageLog")]
    public class DamageLogEntity : IBasicEntity, ISoftDeleted
    {
        [Key] [Column("Id")] public long Id { get; set; }

        [Column("CombatInstanceId")] [Index("IX_DamageLog_CombatInstanceId")]
        public ulong CombatInstanceId { get; set; } // 战斗实例 ID（同一场战斗内单调递增）

        [Column("AttackerId")] [Index("IX_DamageLog_AttackerId")]
        public long AttackerId { get; set; }

        [Column("VictimId")] [Index("IX_DamageLog_VictimId")]
        public long VictimId { get; set; }

        [Column("Amount")] public int Amount { get; set; }                          // 伤害数值
        [Column("Kind")] public byte Kind { get; set; }                             // 伤害类型（见 DamageFloatKind）
        [Column("ElementType")] public byte ElementType { get; set; }               // 五行元素
        [Column("SkillId")] public int SkillId { get; set; }                        // 技能 ID
        [Column("RemainingHealth")] public int RemainingHealth { get; set; }       // 受害者剩余血量

        [Column("OccurredAt")] [Index("IX_DamageLog_OccurredAt")]
        public DateTime OccurredAt { get; set; }                                    // 伤害发生时间

        [Column("MapId")] public int MapId { get; set; }                            // 所在地图 ID
        [Column("WorldX")] public float WorldX { get; set; }
        [Column("WorldY")] public float WorldY { get; set; }
        [Column("WorldZ")] public float WorldZ { get; set; }

        [Column("IsDeleted")] public bool IsDeleted { get; set; }
        [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    }
}
```

**SqlServer 表 DDL（按月分区）**：

```sql
CREATE TABLE DamageLog (
    Id                BIGINT       NOT NULL,
    CombatInstanceId  BIGINT       NOT NULL,
    AttackerId        BIGINT       NOT NULL,
    VictimId          BIGINT       NOT NULL,
    Amount            INT          NOT NULL,
    Kind              TINYINT      NOT NULL,
    ElementType       TINYINT      NOT NULL DEFAULT 0,
    SkillId           INT          NOT NULL DEFAULT 0,
    RemainingHealth   INT          NOT NULL,
    OccurredAt        DATETIME2(3) NOT NULL,
    MapId             INT          NOT NULL DEFAULT 0,
    WorldX            REAL         NOT NULL DEFAULT 0,
    WorldY            REAL         NOT NULL DEFAULT 0,
    WorldZ            REAL         NOT NULL DEFAULT 0,
    IsDeleted         BIT          NOT NULL DEFAULT 0,
    CreatedAt         DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_DamageLog PRIMARY KEY (Id, OccurredAt)
) ON PS_DamageLogByMonth (OccurredAt);

CREATE INDEX IX_DamageLog_CombatInstanceId ON DamageLog (CombatInstanceId);
CREATE INDEX IX_DamageLog_AttackerId      ON DamageLog (AttackerId, OccurredAt);
CREATE INDEX IX_DamageLog_VictimId        ON DamageLog (VictimId, OccurredAt);
CREATE INDEX IX_DamageLog_OccurredAt      ON DamageLog (OccurredAt);

-- 按月分区方案（伪代码，实际需先创建分区函数与方案）
-- CREATE PARTITION FUNCTION PF_DamageLogByMonth (DATETIME2)
--     AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', ...);
-- CREATE PARTITION SCHEME PS_DamageLogByMonth
--     AS PARTITION PF_DamageLogByMonth ALL TO ([PRIMARY]);
```

**日志写入策略**：

- 写入路径：`ICombatHudGrain.NotifyDamageAsync` → 异步队列（`MemoryQueue`） → 批量写入 SqlServer
- 批次大小：500 条/批，或 1s 超时触发
- 失败重试：最多 3 次，间隔 2s/4s/8s 指数退避
- 失败兜底：3 次失败后写入本地 `damage_log_failed.jsonl` 文件，由运维定时补录

### 6.3 GameEntityContext 注册

实体在 `GameEntityContext` 中注册（与项目现有实体一致）：

```csharp
// Horizon.Entities/GameEntityContext.cs（节选，仅展示新增注册）
public class GameEntityContext : DbContext
{
    public DbSet<CombatSnapshotEntity> CombatSnapshots { get; set; } = null!;
    public DbSet<DamageLogEntity> DamageLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CombatSnapshotEntity>(b =>
        {
            b.ToTable("CombatSnapshot");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.PlayerId);
            b.HasIndex(e => e.SnapshotAt);
            b.HasIndex(e => new { e.PlayerId, e.SnapshotAt });
            b.Property(e => e.Buffs).HasColumnType("VARBINARY(MAX)");
            b.Property(e => e.Debuffs).HasColumnType("VARBINARY(MAX)");
            b.Property(e => e.Cooldowns).HasColumnType("VARBINARY(MAX)");
        });

        modelBuilder.Entity<DamageLogEntity>(b =>
        {
            b.ToTable("DamageLog");
            b.HasKey(e => new { e.Id, e.OccurredAt });
            b.HasIndex(e => e.CombatInstanceId);
            b.HasIndex(e => new { e.AttackerId, e.OccurredAt });
            b.HasIndex(e => new { e.VictimId, e.OccurredAt });
        });
    }
}
```

---

## 7. Redis 缓存键命名规范

### 7.1 命名总则

| 规则 | 说明 |
|------|------|
| 全局前缀 | `hundun:`（与项目 `CacheConst` 一致） |
| 子系统段 | `combat:` |
| 实体段 | `{playerId}` 或 `{teamId}` |
| 用途段 | `hud` / `cooldowns` / `damage:recent` / `subscription` 等 |
| 分隔符 | 冒号 `:` |
| 大小写 | 全小写 |
| 占位符 | `{}` 包裹的变量名（实际使用时替换为具体值） |

### 7.2 战斗 HUD 子系统键清单

| 键模板 | 数据结构 | TTL | 用途 | 写入方 | 读取方 |
|--------|---------|-----|------|--------|--------|
| `hundun:combat:{playerId}:hud` | Hash | 30s | HUD 完整状态热缓存 | `ICombatHudGrain` | 前端查询、Grain 冷启动 |
| `hundun:combat:{playerId}:cooldowns` | Sorted Set | 60s | 技能冷却列表（Score=到期时间戳） | `ICombatHudGrain` | Grain 内存重建 |
| `hundun:combat:{playerId}:damage:recent` | List | 10s | 最近 50 条伤害飘字（断线重连补帧） | `ICombatHudGrain` | 前端重连时拉取 |
| `hundun:combat:{playerId}:subscription:{subId}` | Hash | 3600s | 订阅句柄元数据（filter、过期时间） | `ICombatHudGrain` | 网关 Observer |
| `hundun:combat:{playerId}:heartbeat` | String | 15s | 心跳时间戳（防重复激活） | 网关 | `ICombatHudGrain` |
| `hundun:combat:{playerId}:lock` | String (NX) | 5s | 重建锁（防并发重建雪崩） | `ICombatHudGrain` | `ICombatHudGrain` |
| `hundun:combat:team:{teamId}:members` | Set | 600s | 小队成员 ID 集合 | `IPartyCombatGrain` | `IPartyCombatGrain` |
| `hundun:combat:team:{teamId}:status` | Hash | 30s | 小队成员战斗状态摘要 | `IPartyCombatGrain` | 前端小队 UI |
| `hundun:combat:seq:{playerId}` | String (INCR) | 永久 | 推送序号生成器（单调递增） | `ICombatHudGrain` | - |
| `hundun:combat:online` | Set | 60s | 在线玩家 ID 集合（监控用） | 网关 | 运维监控 |

### 7.3 键示例

```text
hundun:combat:10086:hud                    # Hash, TTL 30s
hundun:combat:10086:cooldowns              # Sorted Set, TTL 60s
hundun:combat:10086:damage:recent          # List, TTL 10s
hundun:combat:10086:subscription:a3f2...   # Hash, TTL 3600s
hundun:combat:10086:heartbeat              # String, TTL 15s
hundun:combat:10086:lock                   # String NX, TTL 5s
hundun:combat:team:2048:members            # Set, TTL 600s
hundun:combat:team:2048:status             # Hash, TTL 30s
hundun:combat:seq:10086                    # String (INCR), 永久
hundun:combat:online                       # Set, TTL 60s
```

### 7.4 典型操作示例

#### 7.4.1 写入 HUD 状态（Write-Through）

```text
HMSET hundun:combat:10086:hud
  playerId 10086
  health 8500
  maxHealth 10000
  neiLi 780
  maxNeiLi 1000
  rage 45
  maxRage 100
  isInCombat 1
  teamId 2048
  lastUpdate 1721376000000
  buffs <MemoryPack binary>
  debuffs <MemoryPack binary>
  cooldowns <MemoryPack binary>
  target <MemoryPack binary>

EXPIRE hundun:combat:10086:hud 30
```

#### 7.4.2 写入技能冷却（Sorted Set）

```text
# Score = 冷却到期时间戳（毫秒）
ZADD hundun:combat:10086:cooldowns 1721376060000 "skillId:1001"
ZADD hundun:combat:10086:cooldowns 1721376080000 "skillId:1002"
ZADD hundun:combat:10086:cooldowns 1721376100000 "skillId:1003"
EXPIRE hundun:combat:10086:cooldowns 60

# 查询当前未完成冷却的技能
ZRANGEBYSCORE hundun:combat:10086:cooldowns <当前时间戳> +inf
```

#### 7.4.3 伤害飘字最近列表（List，断线重连补帧）

```text
LPUSH hundun:combat:10086:damage:recent <DamageFloatDto MemoryPack binary>
LTRIM hundun:combat:10086:damage:recent 0 49
EXPIRE hundun:combat:10086:damage:recent 10

# 断线重连时拉取
LRANGE hundun:combat:10086:damage:recent 0 -1
```

#### 7.4.4 推送序号生成

```text
INCR hundun:combat:seq:10086
# 返回值即为本次推送的 Sequence 字段
```

### 7.5 缓存清理与回收

| 场景 | 清理动作 |
|------|---------|
| 玩家正常下线 | `DEL hundun:combat:{playerId}:*`（Lua 脚本批量删除） |
| 玩家切换角色 | `DEL hundun:combat:{oldPlayerId}:*`，新角色按冷启动流程 |
| 小队解散 | `DEL hundun:combat:team:{teamId}:*` |
| 订阅超时 | 由 Redis TTL 自动过期，Observer 端检测到推送失败时主动 unsubscribe |
| 定时巡检 | 每 5 分钟扫描 `hundun:combat:online`，与心跳比对清理僵尸 key |

### 7.6 Lua 脚本：玩家下线批量清理

```lua
-- KEYS[1] = hundun:combat:{playerId}:*
-- 性能优化：避免大量 DEL 阻塞，使用 UNLINK 异步删除
local keys = redis.call('KEYS', KEYS[1])
if #keys > 0 then
  return redis.call('UNLINK', unpack(keys))
end
return 0
```

---

## 8. 前端调用时序图

### 8.1 时序图一：玩家进入战斗场景，首次拉取 HUD 状态

```text
步骤 1：前端检测到玩家进入战斗场景（OnEnterCombatScene 事件）
        前端 HUD Canvas 模块构造 HudStateRequest
            ├── RequestId = 新生成 UUID
            ├── PlayerId = 当前角色 ID
            ├── ForceRefresh = false
            └── ClientVersion = 本地缓存版本号（首次为 0）

步骤 2：前端通过 TouchSocket WebSocket 发送 HudStateRequest
        消息封装：Magic(0xHD) + Version(0x01) + MsgType(300) + Body
        Body = MemoryPack 序列化的 HudStateRequest

步骤 3：网关（Horizon.Game.Gateway）收到消息
        3.1 校验 WebSocket 会话已登录
        3.2 从会话上下文取 playerId，覆盖请求体中的 PlayerId（防伪造）
        3.3 调用 ICombatHudGrain[playerId].GetHudStateAsync()

步骤 4：ICombatHudGrain 处理 GetHudStateAsync()
        4.1 检查 Redis Hash hundun:combat:{playerId}:hud
            ├── 命中且 TTL > 5s ──▶ 反序列化为 CombatHudStateDto 返回
            └── 未命中或 TTL <= 5s ──▶ 进入重建流程：
                  4.1.1 获取重建锁 SET hundun:combat:{playerId}:lock NX EX 5
                        ├── 获取失败 ──▶ 等待 200ms 重试（最多 3 次）
                        └── 获取成功
                  4.1.2 调用 CombatGrain[playerId].GetCombatInfoAsync() 获取权威状态
                  4.1.3 调用 SkillGrain[playerId].GetAllCooldownsAsync() 获取冷却
                  4.1.4 调用 EffectGrain[playerId].GetActiveBuffsAsync() 获取 Buff
                  4.1.5 组装 CombatHudStateDto
                  4.1.6 写入 Redis Hash（HMSET + EXPIRE 30）
                  4.1.7 释放重建锁 DEL hundun:combat:{playerId}:lock
                  4.1.8 返回 CombatHudStateDto

步骤 5：Grain 返回 CombatHudStateDto 给网关

步骤 6：网关构造 HudStateResponse
            ├── RequestId = 步骤 1 的 RequestId
            ├── Code = 0
            ├── State = CombatHudStateDto
            ├── ServerVersion = Grain 返回的版本号
            └── IsDelta = false（首次拉取为全量）

步骤 7：网关通过 WebSocket 推送 HudStateResponse 给前端

步骤 8：前端收到响应
        8.1 校验 RequestId 一致
        8.2 更新本地 HUD 视图模型
            ├── 更新血条/内力条/怒气条
            ├── 渲染 Buff 列表
            ├── 渲染技能冷却进度
            └── 渲染目标信息（若有）
        8.3 保存 ServerVersion 到本地，供下次请求增量判定
        8.4 启动心跳定时器（每 5s 发送 HeartbeatRequest）
```

### 8.2 时序图二：战斗中伤害飘字实时推送

```text
步骤 1：玩家 A 攻击玩家 B（A、B 同小队）
        CombatGrain[A].ProcessAttackAsync(AttackMessage)
            1.1 计算伤害值（含暴击、闪避、格挡判定）
            1.2 更新 B 的 HP（CombatState 内部）
            1.3 写入 CombatState 持久化
            1.4 返回 DamageMessage

步骤 2：CombatGrain 触发 HUD 事件通知
        2.1 调用 ICombatHudGrain[B].NotifyDamageAsync(DamageFloatDto)
              ├── FloatId = INCR hundun:combat:seq:B
              ├── AttackerId = A, VictimId = B
              ├── Amount = 计算后的伤害值
              ├── Kind = CriticalDamage（若是暴击）
              ├── RemainingHealth = B 当前 HP
              ├── IsSelf = true（对 B 而言是自身受伤）
              └── Timestamp = 当前 Unix 毫秒

        2.2 调用 ICombatHudGrain[A].NotifyDamageAsync(DamageFloatDto)
              └── IsSelf = false（对 A 而言是造成伤害）

步骤 3：ICombatHudGrain[B] 处理 NotifyDamageAsync
        3.1 更新内存状态 CombatHudGrainState.CurrentHealth
        3.2 写 Redis Hash（更新 health 字段 + lastUpdate）
        3.3 LPUSH hundun:combat:B:damage:recent + LTRIM 0 49
        3.4 遍历 B 的订阅者列表 SubscriptionHandles
            对每个订阅者：
              3.4.1 检查订阅过滤条件（CriticalOnly / MinDamageThreshold）
              3.4.2 通过 ICombatHudObserver[B].OnDamageFloatAsync(damage) 推送
                    └── 网关收到回调，构造 DamageFloatPush 消息
                          └── 通过 WebSocket 推送给 B 的客户端

步骤 4：B 的客户端收到 DamageFloatPush
        4.1 校验 Sequence 单调递增（若发现跳号，请求补帧）
        4.2 在世界坐标 (WorldX, WorldY, WorldZ) 处生成飘字动画
        4.3 更新本地血条（根据 RemainingHealth / MaxHealth）

步骤 5：同时，ICombatHudGrain[B] 触发 CombatStatePush
        5.1 构造 CombatStatePush
              ├── Sequence = INCR hundun:combat:seq:B
              ├── CurrentHealth = 新 HP
              └── Reason = "damage_received"
        5.2 通过 ICombatHudObserver[B].OnStatePushAsync(push) 推送
              └── 网关推送 CombatStatePush 给 B 的客户端

步骤 6：小队状态联动
        6.1 ICombatHudGrain[B] 调用 IPartyCombatGrain[B.TeamId].NotifyMemberUpdateAsync(B, delta)
              ├── delta = 仅含 HP 变更的 PartyMemberDto
              └── 推送频率控制：HP 变化超过 5% 才触发，避免高频推送
        6.2 IPartyCombatGrain[B.TeamId] 遍历小队成员（含 A）
              对每个成员：
                调用 ICombatHudGrain[member].NotifyPartyMemberUpdateAsync(B, delta)
                  └── 通过 Observer 推送 PartyMemberPush 给该成员客户端

步骤 7：A 的客户端收到 PartyMemberPush
        7.1 更新小队 UI 中 B 的血条
        7.2 若 B 的 HP 触发低血量阈值（< 30%），播放警告音效
```

### 8.3 时序图三：技能冷却推送

```text
步骤 1：玩家 A 释放技能（SkillId = 1001，冷却 6s）
        SkillGrain[A].CastSkillAsync(1001)
            1.1 校验内力/怒气/冷却/距离
            1.2 扣减消耗
            1.3 启动冷却计时器
            1.4 返回施放结果

步骤 2：SkillGrain 通知 HUD 投影
        调用 ICombatHudGrain[A].NotifySkillCooldownAsync(SkillCooldownDto)
            ├── SkillId = 1001, SlotIndex = 0
            ├── TotalCooldownMs = 6000, RemainingCooldownMs = 6000
            ├── CooldownStartedAt = now, CooldownEndsAt = now + 6000
            ├── Progress = 0.0
            ├── IsGlobalCooldown = false
            └── CanCast = false（进入冷却）

步骤 3：ICombatHudGrain[A] 处理
        3.1 更新内存状态 Cooldowns 列表
        3.2 写 Redis Sorted Set
              ZADD hundun:combat:A:cooldowns <now+6000> "skillId:1001"
        3.3 构造 SkillCooldownPush
              ├── Sequence = INCR hundun:combat:seq:A
              ├── Cooldown = SkillCooldownDto
              └── IsReady = false
        3.4 通过 Observer 推送给 A 的客户端

步骤 4：A 的客户端收到 SkillCooldownPush
        4.1 在快捷栏槽位 0 上叠加冷却蒙版（圆形遮罩从 100% 收缩到 0%）
        4.2 显示剩余冷却时间数字（每秒更新一次，前端本地计时）

步骤 5：冷却到期（6s 后）
        5.1 SkillGrain 检测冷却完成
        5.2 调用 ICombatHudGrain[A].NotifySkillCooldownAsync(SkillCooldownDto)
              ├── RemainingCooldownMs = 0
              ├── Progress = 1.0
              └── CanCast = true
        5.3 ICombatHudGrain[A] 移除内存中的冷却项
              ZREMRANGEBYSCORE hundun:combat:A:cooldowns -inf <now>
        5.4 推送 SkillCooldownPush（IsReady = true）给客户端
        5.5 客户端移除冷却蒙版，技能图标恢复可点击状态
```

### 8.4 时序图四：断线重连补帧

```text
步骤 1：客户端检测到网络断开（WebSocket onclose 事件）
        1.1 启动重连定时器（指数退避：1s, 2s, 4s, 8s, 16s）
        1.2 本地保留最后收到的 Sequence 号

步骤 2：客户端重连成功
        2.1 重新建立 WebSocket 连接
        2.2 重新登录认证（TokenLoginRequest）
        2.3 触发 HUD 重建流程

步骤 3：客户端发送重连拉取请求
        3.1 发送 HudStateRequest（ForceRefresh = true）
              └── 强制刷新，绕过 Redis 缓存，直读 Grain
        3.2 同时附带最后收到的 Sequence 号

步骤 4：ICombatHudGrain 处理重连
        4.1 检测 ForceRefresh = true，跳过 Redis 缓存
        4.2 从 CombatGrain 重建完整状态
        4.3 返回 CombatHudStateDto（全量）

步骤 5：客户端补帧伤害飘字
        5.1 从 LRANGE hundun:combat:{playerId}:damage:recent 0 -1 拉取最近 50 条
            └── 该操作由网关代理执行（客户端不直连 Redis）
        5.2 客户端按 Sequence 去重，仅播放 Sequence > 本地最后值 的飘字
        5.3 补帧完成后，恢复正常订阅推送

步骤 6：客户端恢复订阅
        6.1 发送 SubscribeDamageRequest（Filter 与断线前一致）
        6.2 收到新的 SubscriptionId
        6.3 后续伤害飘字通过新订阅推送
```

---

## 附录 A：版本历史

| 版本 | 日期 | 变更说明 |
|------|------|---------|
| v1.0.0 | 2026-07-19 | 首版：定义 ICombatHudGrain / IPartyCombatGrain 接口、TouchSocket 消息协议、MemoryPack DTO、EFCore 实体、Redis 键命名规范、4 个时序图 |

## 附录 B：参考资料

- 项目内 `CombatGrain` 实现：`Horizon.Orleans.Grains/CombatGrain.cs`
- 项目内消息联合体：`Horizon.Game.Message/MessageUnion.cs`
- 项目内 UI 枚举：`Horizon.Game.Message/Enums/UIEnums.cs`
- 项目内存储常量：`Horizon.Core.Abstract/OrleansConst.cs`
- Orleans 官方文档：Grain Observer 模式
- MemoryPack 官方文档：SerializeLayout.Explicit
- TouchSocket 官方文档：TCP/WebSocket 服务端实现

## 附录 C：术语表

| 术语 | 英文 | 说明 |
|------|------|------|
| HUD | Head-Up Display | 平视显示器，游戏界面中浮层显示的战斗信息 |
| Grain | - | Orleans 中的虚拟 Actor 单元 |
| Observer | - | Orleans 中的观察者，用于 Grain 反向调用客户端 |
| TTL | Time To Live | 缓存过期时间 |
| GCD | Global Cooldown | 公共冷却时间 |
| AOE | Area of Effect | 范围效果 |
| DPS | Damage Per Second | 每秒伤害 |
| Write-Through | - | 写穿策略，写缓存同时写后端 |
| 序号 | Sequence | 单调递增的推送编号，用于丢帧检测 |
| 补帧 | Replay | 断线重连后补发遗漏的事件 |

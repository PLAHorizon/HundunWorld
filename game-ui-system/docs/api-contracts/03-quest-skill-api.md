# 混沌世界 MMORPG - 任务与技能子系统后端接口契约文档

| 项目 | 说明 |
| --- | --- |
| 文档编号 | 03-quest-skill-api |
| 所属系统 | 混沌世界 HundunWorld MMORPG |
| 技术栈 | .NET 10 C# + Orleans（Grain actor 模型）+ SqlServer（EFCore 持久化）+ Redis（缓存）+ TouchSocket（TCP/WebSocket 实时通信）+ MemoryPack（二进制序列化） |
| 文档版本 | v1.0 |
| 最后更新 | 2026-07-19 |
| 适用范围 | 前端 UI（quest-log、skill-panel 等页面）与后端 Grain 之间的接口契约、消息协议、持久化结构与缓存规范 |

---

## 1. 子系统概述与领域边界

### 1.1 子系统定位

任务与技能子系统是混沌世界 MMORPG 玩家成长循环的核心支撑模块，负责玩家在游戏世界中“做什么（任务）”与“怎么变强（技能）”两条主线的状态管理。子系统独立部署为多个 Orleans Grain，通过 TouchSocket 与前端进行实时二进制通信，状态最终落盘至 SqlServer，热数据缓存于 Redis。

### 1.2 任务子系统领域边界

- **任务日志（Quest Log）**：玩家当前持有与历史完成的任务集合，按类型分类展示。
  - 主线任务（MainQuest）：线性剧情，唯一进行中实例，不可放弃。
  - 支线任务（SideQuest）：可选剧情，可同时持有多个。
  - 日常任务（DailyQuest）：每日 0 点重置（UTC+8），单日上限 20 条。
  - 周常任务（WeeklyQuest）：每周一 4 点重置，单周上限 10 条。
  - 活动任务（EventQuest）：限时活动驱动，过期自动归档。
- **任务进度追踪（Quest Progress Tracking）**：每个任务包含若干目标，按目标索引独立计数，全部完成后任务状态迁移为“可提交”。
- **任务目标（Quest Objective）**：支持五种目标类型。
  - 击杀目标（Kill）：击杀指定 NpcId 累计 RequiredCount。
  - 采集目标（Collect）：采集指定 ItemId 累计 RequiredCount。
  - 对话目标（Talk）：与指定 NpcId 完成对话，0/1 计数。
  - 到达目标（Reach）：进入指定 ZoneId 区域触发。
  - 使用物品目标（UseItem）：对指定 ItemId 使用累计 RequiredCount。

任务子系统**不负责**：奖励发放的实际物品/经验写入（由 InventoryGrain、CharacterGrain 负责）、副本进度（由 DungeonGrain 负责）、剧情演出（由客户端 NarrativePro 模块负责）。任务完成时仅产出 `QuestCompleteResult` 事件，由上游 Grain 消费。

### 1.3 技能子系统领域边界

- **技能树（Skill Tree）**：以心法为根的多层有向无环图（DAG），每个节点对应一个可学习技能。
  - 心法（XinFa）：根节点，决定流派（如太阴、太阳、少阳），每个角色最多装备 1 个心法。
  - 奇术（QiShu）：被动增益节点，不占用快捷栏，学习即生效。
  - 招式（ZhaoShi）：主动技能节点，可装备至快捷栏释放。
- **技能装备（Skill Bar）**：玩家快捷栏，固定 10 个槽位（Slot 0-9），仅招式类技能可装备。
- **技能升级（Skill Upgrade）**：消耗技能点提升技能等级，最高 10 级，每级调整内力消耗、范围、冷却等属性。
- **技能释放（Skill Cast）**：由 CombatGrain 协同处理，本子系统仅维护冷却与已学习状态。

技能子系统**不负责**：伤害结算（CombatGrain）、目标选择与命中判定（CombatGrain）、心法切换的战斗中断（CombatGrain）。技能冷却查询与重置由本子系统独占。

### 1.4 跨子系统协作关系

| 协作方向 | 上游 | 下游 | 交互方式 |
| --- | --- | --- | --- |
| 任务完成 → 奖励发放 | QuestGrain | InventoryGrain / CharacterGrain | Grain 直接调用，幂等 |
| 击杀事件 → 任务进度 | CombatGrain | QuestGrain | Stream 事件订阅 |
| 采集事件 → 任务进度 | InventoryGrain | QuestGrain | Grain 直接调用 |
| 技能释放 → 冷却校验 | CombatGrain | SkillGrain | Grain 直接调用 |
| 心法切换 → 战斗中断 | SkillTreeGrain | CombatGrain | Stream 事件订阅 |
| 技能升级 → 属性重算 | SkillGrain | CharacterGrain | Grain 直接调用 |

### 1.5 术语表

| 术语 | 说明 |
| --- | --- |
| GrainKey | Orleans Grain 主键策略，本子系统统一使用 `IGrainWithGuidKey`，Guid 取玩家角色 ID |
| QuestId / SkillId | 任务 / 技能模板 ID，全局唯一，由策划配置表分配 |
| SkillPoint | 技能点，升级消耗资源，由角色升级与任务奖励产出 |
| ObjectiveIndex | 任务目标在目标列表中的零基索引，用于精确定位进度更新目标 |

---

## 2. Orleans Grain 接口定义

本节定义任务与技能子系统的三个核心 Grain 接口。所有接口均继承 `IGrainWithGuidKey`，GrainKey 为玩家角色 Guid。接口标注 `[Orleans.CodeGeneration.Version(1)]` 以支持灰度升级。

### 2.1 IQuestGrain 接口

`IQuestGrain` 负责单一玩家的任务全生命周期管理。GrainKey 策略：`Guid = playerId`，每个玩家对应一个独立 Grain 实例，激活后从 GameStore 加载 `QuestState`。

```csharp
using Orleans;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IQuestGrain : IGrainWithGuidKey
    {
        /// <summary>获取当前进行中的任务列表（主线/支线/日常/周常/活动混合）</summary>
        Task<List<QuestData>> GetActiveQuestsAsync();

        /// <summary>获取已完成的任务列表（用于任务日志历史标签页）</summary>
        Task<List<QuestData>> GetCompletedQuestsAsync();

        /// <summary>获取单个任务详情（含全部目标进度），不存在返回 null</summary>
        /// <param name="questId">任务模板 ID</param>
        Task<QuestData?> GetQuestAsync(int questId);

        /// <summary>接受任务</summary>
        /// <param name="questType">任务类型 (0=主线, 1=支线, 2=日常, 3=周常, 4=活动)</param>
        /// <param name="rewards">奖励字典 (key=奖励类型, value=数量)</param>
        Task<bool> AcceptQuestAsync(int questId, string questName, string description,
            int questType, int level, Dictionary<string, int> rewards);

        /// <summary>放弃任务（主线不可放弃）</summary>
        Task<bool> AbandonQuestAsync(int questId);

        /// <summary>完成任务并领取奖励</summary>
        Task<QuestCompleteResult> CompleteQuestAsync(int questId);

        /// <summary>查询单个任务的全部目标进度，任务不存在返回空列表</summary>
        Task<List<QuestObjectiveData>> GetProgressAsync(int questId);

        /// <summary>更新任务目标进度（由 CombatGrain / InventoryGrain 触发）</summary>
        /// <param name="objectiveIndex">目标零基索引</param>
        /// <param name="progressCount">进度增量（正整数）</param>
        Task<bool> UpdateQuestProgressAsync(int questId, int objectiveIndex, int progressCount);

        /// <summary>为任务追加目标（任务模板初始化时调用，玩家侧一般不直接使用）</summary>
        /// <param name="objectiveType">目标类型 (Kill/Collect/Talk/Reach/UseItem)</param>
        Task<bool> AddQuestObjectiveAsync(int questId, string objectiveType,
            string description, int requiredCount);
    }
}
```

**方法语义说明**：
- `GetActiveQuestsAsync` 与 `GetCompletedQuestsAsync` 直接读取 Grain 内存状态，不触发持久化，调用成本低，适合前端高频刷新。
- `AcceptQuestAsync` 校验顺序：questId 有效性 → 名称非空 → 未在 ActiveQuests → 未在 CompletedQuests → 未达 MaxActiveQuests（默认 20）。
- `CompleteQuestAsync` 仅当所有目标 IsCompleted=true 或目标列表为空时允许完成，完成后任务从 ActiveQuests 迁移至 CompletedQuests。
- `UpdateQuestProgressAsync` 自动封顶 `Math.Min(current + delta, required)`，全部目标完成后任务状态置为 `ReadyToSubmit=1`。

### 2.2 ISkillGrain 接口

`ISkillGrain` 负责玩家已学习技能的管理、装备槽位、升级与冷却查询。GrainKey 策略：`Guid = playerId`。

```csharp
using Orleans;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ISkillGrain : IGrainWithGuidKey
    {
        /// <summary>获取玩家所有已学习技能（含等级、冷却、装备槽位）</summary>
        Task<List<SkillInfo>> GetSkillsAsync();

        /// <summary>学习技能（消耗技能点，校验前置依赖）</summary>
        Task<bool> LearnSkillAsync(int skillId);

        /// <summary>升级技能（消耗技能点，校验等级上限）</summary>
        Task<bool> UpgradeSkillAsync(int skillId);

        /// <summary>装备技能到指定快捷栏槽位（须为招式类）</summary>
        /// <param name="slot">槽位索引 (0-9)</param>
        Task<bool> EquipSkillAsync(int skillId, int slot);

        /// <summary>卸下指定槽位的技能</summary>
        Task<bool> UnequipSkillAsync(int slot);

        /// <summary>获取当前快捷栏装备情况（槽位 -> 技能 ID，空槽位不包含键）</summary>
        Task<Dictionary<int, int>> GetSkillBarAsync();

        /// <summary>查询技能剩余冷却时间（秒，未学习或无冷却返回 0）</summary>
        Task<float> GetSkillCooldownAsync(int skillId);

        /// <summary>重置技能冷却（GM/道具使用）</summary>
        Task<bool> ResetSkillCooldownAsync(int skillId);

        /// <summary>重置所有已学习技能并返还技能点</summary>
        Task<bool> ResetAllSkillsAsync();

        /// <summary>设置技能前置依赖（运营配置用，含循环依赖检测）</summary>
        Task<bool> SetSkillDependencyAsync(int skillId, List<int> prerequisites);

        /// <summary>获取可用技能点</summary>
        Task<int> GetSkillPointsAsync();

        /// <summary>增加技能点（由角色升级/任务完成触发）</summary>
        Task<bool> AddSkillPointsAsync(int points);
    }
}
```

**方法语义说明**：
- `LearnSkillAsync` 校验顺序：未学习 → 技能点>0 → 前置技能全部已学习。学习后初始 Level=1、MaxLevel=10、Cooldown=3000ms、CastTime=500ms、NeiLiCost=10、Range=5.0。
- `UpgradeSkillAsync` 校验顺序：已学习 → Level<MaxLevel。升级后 NeiLiCost×1.1、Range+0.5。
- `EquipSkillAsync` 校验顺序：已学习 → SkillType=招式 → slot∈[0,9] → 该槽位是否被占用（占用则覆盖并推送 SkillEquippedPush）。
- `ResetAllSkillsAsync` 按 Level 之和返还技能点，清空 LearnedSkills 与 SkillCooldowns。

### 2.3 ISkillTreeGrain 接口

`ISkillTreeGrain` 负责技能树拓扑结构与节点解锁状态管理。GrainKey 策略：`Guid = playerId`，与 ISkillGrain 共享 playerId 但状态独立持久化。

```csharp
using Orleans;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ISkillTreeGrain : IGrainWithGuidKey
    {
        /// <summary>获取玩家可见的完整技能树（含解锁状态）</summary>
        /// <param name="xinFaId">心法 ID（0 表示当前装备心法）</param>
        Task<SkillTreeDto> GetTreeAsync(int xinFaId);

        /// <summary>解锁技能树节点（不等于学习，仅解锁可学习权限）</summary>
        Task<bool> UnlockNodeAsync(int nodeId);

        /// <summary>查询节点解锁状态</summary>
        Task<SkillNodeUnlockStatus> GetNodeStatusAsync(int nodeId);

        /// <summary>切换装备的心法（会触发心法切换事件）</summary>
        Task<bool> SwitchXinFaAsync(int xinFaId);

        /// <summary>获取当前装备的心法</summary>
        Task<int> GetActiveXinFaAsync();
    }
}
```

**节点解锁状态枚举**：

```csharp
public enum SkillNodeUnlockStatus
{
    Locked = 0,    // 未达成前置条件
    Unlocked = 1,  // 已解锁，可学习
    Learned = 2,   // 已学习
    Maxed = 3      // 已学习且已满级
}
```

### 2.4 GrainKey 策略汇总

| Grain 接口 | Key 类型 | Key 取值 | 激活策略 |
| --- | --- | --- | --- |
| IQuestGrain | Guid | playerId | 按需激活，空闲 5 分钟去激活 |
| ISkillGrain | Guid | playerId | 按需激活，空闲 5 分钟去激活 |
| ISkillTreeGrain | Guid | playerId | 按需激活，空闲 10 分钟去激活 |

去激活时由 Orleans 框架自动调用 `OnDeactivateAsync`，触发状态写回 SqlServer。Redis 缓存不清除，由 TTL 自然过期。

---

## 3. Grain 状态与持久化

### 3.1 持久化存储配置

本子系统使用 Orleans 的 `IPersistentState<T>` 进行状态持久化，存储名称为 `GameStore`，对应 SqlServer 表 `OrleansStorage`。配置示例（仅文档化）：

```json
{
  "Orleans": {
    "Storage": {
      "GameStore": {
        "Type": "AdoNet",
        "AdoNet": {
          "ConnectionString": "Server=...;Database=HundunGame;...",
          "Invariant": "System.Data.SqlClient"
        }
      }
    }
  }
}
```

### 3.2 QuestState 状态结构

`QuestState` 由 `QuestGrain` 持有，标注 `[PersistentState("quest", "GameStore")]`：

```csharp
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public partial class QuestState
{
    [MemoryPackOrder(0)] [Id(0)] public Dictionary<int, QuestData> ActiveQuests { get; set; } = new();     // 进行中的任务 (QuestId -> QuestData)
    [MemoryPackOrder(1)] [Id(1)] public Dictionary<int, QuestData> CompletedQuests { get; set; } = new(); // 已完成的任务 (QuestId -> QuestData)
    [MemoryPackOrder(2)] [Id(2)] public int MaxActiveQuests { get; set; } = 20;                           // 最大同时接受任务数（默认 20）
}
```

### 3.3 SkillState 状态结构

`SkillState` 由 `SkillGrain` 持有，标注 `[PersistentState("skill", "GameStore")]`：

```csharp
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public partial class SkillState
{
    [MemoryPackOrder(0)] [Id(0)] public Dictionary<int, SkillInfo> LearnedSkills { get; set; } = new();          // 已学习技能 (SkillId -> SkillInfo)
    [MemoryPackOrder(1)] [Id(1)] public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();         // 技能冷却记录 (SkillId -> 上次施放时间)
    [MemoryPackOrder(2)] [Id(2)] public int SkillPoints { get; set; } = 0;                                       // 可用技能点
    [MemoryPackOrder(3)] [Id(3)] public int TotalSkillPointsUsed { get; set; } = 0;                             // 已使用技能点总数
    [MemoryPackOrder(4)] [Id(4)] public Dictionary<int, List<int>> SkillDependencies { get; set; } = new();     // 技能前置依赖 (SkillId -> 前置 SkillId 列表)
    [MemoryPackOrder(5)] [Id(5)] public Dictionary<int, int> SkillBar { get; set; } = new();                    // 快捷栏装备 (Slot -> SkillId)
}
```

### 3.4 SkillTreeState 状态结构

`SkillTreeState` 由 `SkillTreeGrain` 持有，标注 `[PersistentState("skilltree", "GameStore")]`：

```csharp
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public partial class SkillTreeState
{
    [MemoryPackOrder(0)] [Id(0)] public HashSet<int> UnlockedNodes { get; set; } = new();                  // 已解锁节点集合 (NodeId)
    [MemoryPackOrder(1)] [Id(1)] public int ActiveXinFa { get; set; } = 0;                                 // 当前装备心法 SkillId
    [MemoryPackOrder(2)] [Id(2)] public Dictionary<int, DateTime> UnlockedXinFa { get; set; } = new();     // 心法解锁记录 (XinFaId -> 解锁时间)
}
```

### 3.5 Redis 缓存结构

Grain 激活时优先从 Redis 读取热缓存，未命中则从 SqlServer 加载并回写 Redis。所有写操作采用“写穿（Write-Through）”策略：先写 SqlServer，成功后异步更新 Redis。

| 缓存键 | Redis 数据结构 | TTL | 用途 | 回写时机 |
| --- | --- | --- | --- | --- |
| `hundun:quest:{playerId}:active` | Sorted Set | 30 分钟 | 进行中任务列表，Score=接取时间戳 | AcceptQuest / AbandonQuest / CompleteQuest |
| `hundun:quest:{playerId}:progress` | Hash | 30 分钟 | 任务目标进度，Field=`{questId}:{objIdx}` | UpdateQuestProgress |
| `hundun:quest:{playerId}:completed` | Set | 7 天 | 已完成任务 ID 集合（去重） | CompleteQuest |
| `hundun:skill:{playerId}` | Hash | 60 分钟 | 已学习技能序列化 blob，Field=SkillId | LearnSkill / UpgradeSkill / ResetAllSkills |
| `hundun:skill:{playerId}:bar` | List | 60 分钟 | 快捷栏 10 槽位 SkillId（0 表示空） | EquipSkill / UnequipSkill |
| `hundun:skill:{playerId}:cooldown` | Hash | 5 分钟 | 技能冷却到期时间戳，Field=SkillId | CastSkill / ResetSkillCooldown |
| `hundun:skilltree:{playerId}` | Hash | 120 分钟 | 技能树节点解锁状态，Field=NodeId | UnlockNode / SwitchXinFa |

### 3.6 回写策略细节

- **写穿优先**：所有状态变更方法在 `WriteStateAsync()` 成功后，通过 `IConnectionMultiplexer` 异步写入 Redis，写失败仅记录日志不回滚 SqlServer，依赖下次读取时重建缓存。
- **缓存击穿防护**：Grain 激活时使用 Redis `SET NX` 加分布式锁（`hundun:lock:quest:{playerId}`，TTL 5s）防止并发重建；冷启动回填时若 Redis 未命中且从 SqlServer 加载成功，立即异步回填 Redis 全部相关键。
- **去激活清理**：Grain 去激活时不主动清除 Redis，由 TTL 自然过期；SqlServer 状态已在前序 WriteStateAsync 中持久化。

### 3.7 并发与一致性

- 单个玩家的 QuestGrain / SkillGrain 由 Orleans 单实例激活，天然串行化，无需额外锁。
- 跨 Grain 调用（如任务完成触发奖励发放）采用幂等设计：`CompleteQuestAsync` 内部检查任务是否已在 CompletedQuests，重复调用返回相同结果。
- Redis 与 SqlServer 之间允许短时不一致（秒级），以 SqlServer 为最终一致来源。前端展示优先读 Redis，关键决策（如完成任务发奖）必须经 Grain 串行化确认。

---

## 4. TouchSocket 消息协议

本节定义任务与技能子系统在 TouchSocket（TCP/WebSocket）链路上的消息协议。所有消息体使用 MemoryPack 二进制序列化，外层包封采用 `MessageUnion` 基类的 `Type` 与 `ServiceType` 字段路由。

### 4.1 消息类型枚举

消息类型枚举扩展自 `MessageType`（位于 `Horizon.Game.Message.Enums`）：

```csharp
public enum MessageType
{
    // ===== 任务消息 1000-1099 =====
    QuestProgressPush = 1000,        // 任务进度推送（服务端 → 客户端）
    QuestCompletePush = 1001,        // 任务完成推送
    QuestAcceptRequest = 1002,       // 接受任务请求
    QuestAcceptResponse = 1003,      // 接受任务响应
    QuestAbandonRequest = 1004,      // 放弃任务请求
    QuestAbandonResponse = 1005,     // 放弃任务响应
    QuestListRequest = 1006,         // 任务列表查询
    QuestListResponse = 1007,        // 任务列表响应
    QuestDetailRequest = 1008,       // 任务详情查询
    QuestDetailResponse = 1009,      // 任务详情响应
    QuestCompleteRequest = 1010,     // 完成任务请求
    QuestCompleteResponse = 1011,    // 完成任务响应

    // ===== 技能消息 1100-1199 =====
    SkillLearnedPush = 1100,         // 技能学习推送
    SkillEquippedPush = 1101,        // 技能装备推送
    SkillUpgradedPush = 1102,        // 技能升级推送
    SkillLearnRequest = 1103,        // 学习技能请求
    SkillLearnResponse = 1104,       // 学习技能响应
    SkillUpgradeRequest = 1105,      // 升级技能请求
    SkillUpgradeResponse = 1106,     // 升级技能响应
    SkillEquipRequest = 1107,        // 装备技能请求
    SkillEquipResponse = 1108,       // 装备技能响应
    SkillUnequipRequest = 1109,      // 卸下技能请求
    SkillUnequipResponse = 1110,     // 卸下技能响应
    SkillBarQueryRequest = 1111,     // 快捷栏查询
    SkillBarQueryResponse = 1112,    // 快捷栏响应
    SkillCooldown = 1113,            // 冷却查询/推送
    SkillTreeQueryRequest = 1114,    // 技能树查询
    SkillTreeQueryResponse = 1115,   // 技能树响应
    SkillTreeUnlockRequest = 1116,   // 节点解锁请求
    SkillTreeUnlockResponse = 1117   // 节点解锁响应
}
```

### 4.2 请求/响应消息体

所有请求/响应消息均继承 `MessageUnion, INetworkMessage`，标注 `[MemoryPackable]` 与 `[GenerateSerializer]`。下列展示典型消息体，其余遵循相同模式。

**任务接受请求/响应**：

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class QuestAcceptRequest : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)]
    public ulong CharacterId { get; set; }

    [MemoryPackOrder(1)] [Id(1)]
    public int QuestId { get; set; }

    [MemoryPackOrder(2)] [Id(2)]
    public MessageType Type { get; set; } = MessageType.QuestAcceptRequest;
    [MemoryPackOrder(3)] [Id(3)]
    public ServiceType ServiceType { get; set; } = ServiceType.Game;
}

[MemoryPackable]
[GenerateSerializer]
public partial class QuestAcceptResponse : MessageUnion, INetworkMessage
{
    /// <summary>是否接受成功</summary>
    [MemoryPackOrder(0)] [Id(0)]
    public bool Success { get; set; }

    /// <summary>失败原因码（0=成功, 1=已存在, 2=已达上限, 3=前置未完成, 4=等级不足）</summary>
    [MemoryPackOrder(1)] [Id(1)]
    public int ReasonCode { get; set; }

    /// <summary>接受后的任务 DTO（成功时填充）</summary>
    [MemoryPackOrder(2)] [Id(2)]
    public QuestData? Quest { get; set; }

    [MemoryPackOrder(3)] [Id(3)]
    public MessageType Type { get; set; } = MessageType.QuestAcceptResponse;
    [MemoryPackOrder(4)] [Id(4)]
    public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

**任务完成请求/响应**（结构精简，字段语义同上）：

```csharp
public partial class QuestCompleteRequest : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public int QuestId { get; set; }
    [MemoryPackOrder(2)] [Id(2)] public MessageType Type { get; set; } = MessageType.QuestCompleteRequest;
    [MemoryPackOrder(3)] [Id(3)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}

public partial class QuestCompleteResponse : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public bool Success { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public string Message { get; set; } = "";
    [MemoryPackOrder(2)] [Id(2)] public int QuestId { get; set; }
    /// <summary>奖励清单 (key=奖励类型, value=数量)</summary>
    [MemoryPackOrder(3)] [Id(3)] public Dictionary<string, int> Rewards { get; set; } = new();
    [MemoryPackOrder(4)] [Id(4)] public MessageType Type { get; set; } = MessageType.QuestCompleteResponse;
    [MemoryPackOrder(5)] [Id(5)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

**技能学习与装备请求**：

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class SkillLearnRequest : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public int SkillId { get; set; }
    /// <summary>学习方式 (0=普通, 1=金币, 2=道具)</summary>
    [MemoryPackOrder(2)] [Id(2)] public int LearnMethod { get; set; }
    [MemoryPackOrder(3)] [Id(3)] public MessageType Type { get; set; } = MessageType.SkillLearnRequest;
    [MemoryPackOrder(4)] [Id(4)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}

[MemoryPackable]
[GenerateSerializer]
public partial class SkillEquipRequest : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public int SkillId { get; set; }
    /// <summary>目标槽位 (0-9)</summary>
    [MemoryPackOrder(2)] [Id(2)] public int Slot { get; set; }
    [MemoryPackOrder(3)] [Id(3)] public MessageType Type { get; set; } = MessageType.SkillEquipRequest;
    [MemoryPackOrder(4)] [Id(4)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

### 4.3 推送消息体

#### 4.3.1 QuestProgressPush

当 `UpdateQuestProgressAsync` 成功且目标进度变化时，由 QuestGrain 通过 Stream 推送至客户端。前端用于实时刷新任务追踪 UI。

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class QuestProgressPush : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public int QuestId { get; set; }
    /// <summary>任务名称（便于前端直接展示）</summary>
    [MemoryPackOrder(2)] [Id(2)] public string QuestName { get; set; } = "";
    /// <summary>变更的目标索引</summary>
    [MemoryPackOrder(3)] [Id(3)] public int ObjectiveIndex { get; set; }
    /// <summary>目标类型 (Kill/Collect/Talk/Reach/UseItem)</summary>
    [MemoryPackOrder(4)] [Id(4)] public string ObjectiveType { get; set; } = "";
    [MemoryPackOrder(5)] [Id(5)] public int CurrentCount { get; set; }
    [MemoryPackOrder(6)] [Id(6)] public int RequiredCount { get; set; }
    /// <summary>该目标是否已完成</summary>
    [MemoryPackOrder(7)] [Id(7)] public bool ObjectiveCompleted { get; set; }
    /// <summary>任务整体状态 (0=进行中, 1=可提交)</summary>
    [MemoryPackOrder(8)] [Id(8)] public int QuestStatus { get; set; }
    [MemoryPackOrder(9)] [Id(9)] public MessageType Type { get; set; } = MessageType.QuestProgressPush;
    [MemoryPackOrder(10)] [Id(10)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

#### 4.3.2 QuestCompletePush

当任务被服务端自动完成（如活动任务超时自动结算）或玩家手动领取奖励成功后推送。

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class QuestCompletePush : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    [MemoryPackOrder(1)] [Id(1)] public int QuestId { get; set; }
    [MemoryPackOrder(2)] [Id(2)] public string QuestName { get; set; } = "";
    /// <summary>完成方式 (0=玩家领取, 1=自动完成, 2=活动结算)</summary>
    [MemoryPackOrder(3)] [Id(3)] public int CompleteMethod { get; set; }
    /// <summary>奖励清单</summary>
    [MemoryPackOrder(4)] [Id(4)] public Dictionary<string, int> Rewards { get; set; } = new();
    /// <summary>完成时间戳（Unix 毫秒）</summary>
    [MemoryPackOrder(5)] [Id(5)] public long CompleteTimestamp { get; set; }
    [MemoryPackOrder(6)] [Id(6)] public MessageType Type { get; set; } = MessageType.QuestCompletePush;
    [MemoryPackOrder(7)] [Id(7)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

#### 4.3.3 SkillLearnedPush

技能学习成功后推送，触发前端技能面板刷新与新技能高亮动画。

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class SkillLearnedPush : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    /// <summary>新学习的技能完整 DTO</summary>
    [MemoryPackOrder(1)] [Id(1)] public SkillInfo LearnedSkill { get; set; } = new();
    /// <summary>剩余技能点</summary>
    [MemoryPackOrder(2)] [Id(2)] public int RemainingSkillPoints { get; set; }
    [MemoryPackOrder(3)] [Id(3)] public MessageType Type { get; set; } = MessageType.SkillLearnedPush;
    [MemoryPackOrder(4)] [Id(4)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

#### 4.3.4 SkillEquippedPush

技能装备或卸下后推送，前端同步刷新快捷栏 UI。

```csharp
[MemoryPackable]
[GenerateSerializer]
public partial class SkillEquippedPush : MessageUnion, INetworkMessage
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; set; }
    /// <summary>变更类型 (0=装备, 1=卸下, 2=交换)</summary>
    [MemoryPackOrder(1)] [Id(1)] public int ChangeType { get; set; }
    /// <summary>槽位索引 (0-9)</summary>
    [MemoryPackOrder(2)] [Id(2)] public int Slot { get; set; }
    /// <summary>新技能 ID（卸下时为 0）</summary>
    [MemoryPackOrder(3)] [Id(3)] public int NewSkillId { get; set; }
    /// <summary>旧技能 ID（被覆盖或卸下的技能）</summary>
    [MemoryPackOrder(4)] [Id(4)] public int OldSkillId { get; set; }
    [MemoryPackOrder(5)] [Id(5)] public MessageType Type { get; set; } = MessageType.SkillEquippedPush;
    [MemoryPackOrder(6)] [Id(6)] public ServiceType ServiceType { get; set; } = ServiceType.Game;
}
```

### 4.4 消息路由与分发

- TouchSocket 网关接收二进制帧后，按 `MessageUnion.Type` 字段路由至对应 `IMessageHandler`。
- 任务相关消息（1000-1099）路由至 `QuestHandler`，调用 `GrainFactory.GetGrain<IQuestGrain>(playerGuid)`。
- 技能相关消息（1100-1199）路由至 `SkillHandler`，调用 `GrainFactory.GetGrain<ISkillGrain>(playerGuid)`。
- 推送消息（`*Push`）由 Grain 通过 `IStreamProvider` 的 `QuestEvents` / `SkillEvents` 命名空间发布，网关订阅后按 CharacterId 转发至对应 TCP/WebSocket 连接。

### 4.5 错误码规范

| ReasonCode | 含义 | 触发场景 |
| --- | --- | --- |
| 0 | 成功 | 所有成功响应 |
| 1 | 已存在 | 接受任务时任务已在 ActiveQuests |
| 2 | 已达上限 | 进行中任务数 ≥ MaxActiveQuests |
| 3 | 前置未完成 | 主线任务前置未完成 |
| 4 | 等级不足 | 玩家等级 < 任务 Level 要求 |
| 5 | 未学习 | 操作未学习的技能 |
| 6 | 技能点不足 | LearnSkill / UpgradeSkill 时 SkillPoints≤0 |
| 7 | 等级上限 | UpgradeSkill 时 Level≥MaxLevel |
| 8 | 槽位无效 | slot 不在 [0,9] |
| 9 | 类型不符 | 装备非招式类技能至快捷栏 |
| 10 | 冷却中 | 释放技能时冷却未结束 |
| 99 | 未知错误 | 异常兜底 |

---

## 5. MemoryPack 序列化 DTO

本节定义任务与技能子系统对外暴露的传输 DTO。所有 DTO 采用 C# `record` 语法，标注 `[MemoryPackable]` 与 `[MemoryPackOrder(n)]` 显式声明字段顺序，确保二进制序列化的跨版本兼容性。同时标注 Orleans 的 `[GenerateSerializer]` 与 `[Id(n)]` 以兼容 Orleans 远程调用序列化。

> **约定**：本节 DTO 用于“跨进程/跨网络”传输，与第 3 节的 Grain 状态类（`QuestState` 等，使用 `partial class`）区分。Grain 内部状态可变，DTO 不可变（record 的 with 表达式生成新实例）。

### 5.1 QuestDto 任务传输对象

```csharp
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network.Dtos
{
    /// <summary>任务传输对象 - 用于网络传输与缓存序列化，不可变 record，字段顺序固定禁止调整</summary>
    [MemoryPackable]
    [GenerateSerializer]
    public record QuestDto
    {
        [MemoryPackOrder(0)] [Id(0)] public int QuestId { get; init; }
        [MemoryPackOrder(1)] [Id(1)] public string QuestName { get; init; } = "";
        [MemoryPackOrder(2)] [Id(2)] public string Description { get; init; } = "";
        [MemoryPackOrder(3)] [Id(3)] public int QuestType { get; init; }          // 0=主线,1=支线,2=日常,3=周常,4=活动
        [MemoryPackOrder(4)] [Id(4)] public int Level { get; init; }
        [MemoryPackOrder(5)] [Id(5)] public int Status { get; init; }              // 0=进行中,1=可提交,2=已完成,3=已放弃
        [MemoryPackOrder(6)] [Id(6)] public List<QuestObjectiveDto> Objectives { get; init; } = new();  // 顺序敏感，索引即 ObjectiveIndex
        [MemoryPackOrder(7)] [Id(7)] public Dictionary<string, int> Rewards { get; init; } = new();    // key=奖励类型如"gold"/"exp"/"item:1001"
        [MemoryPackOrder(8)] [Id(8)] public DateTime AcceptTime { get; init; }     // UTC
        [MemoryPackOrder(9)] [Id(9)] public DateTime? CompleteTime { get; init; }  // UTC，未完成为 null
        [MemoryPackOrder(10)] [Id(10)] public DateTime? ExpireTime { get; init; }  // UTC，仅活动任务有值
    }
}
```

### 5.2 QuestObjectiveDto 任务目标传输对象

```csharp
[MemoryPackable]
[GenerateSerializer]
public record QuestObjectiveDto
{
    [MemoryPackOrder(0)] [Id(0)] public string ObjectiveType { get; init; } = "";  // Kill/Collect/Talk/Reach/UseItem
    [MemoryPackOrder(1)] [Id(1)] public string Description { get; init; } = "";
    [MemoryPackOrder(2)] [Id(2)] public int TargetId { get; init; }                // NpcId/ItemId/ZoneId，按 ObjectiveType 解释
    [MemoryPackOrder(3)] [Id(3)] public int RequiredCount { get; init; }
    [MemoryPackOrder(4)] [Id(4)] public int CurrentCount { get; init; }
    [MemoryPackOrder(5)] [Id(5)] public bool IsCompleted { get; init; }
}
```

### 5.3 SkillDto 技能传输对象

```csharp
[MemoryPackable]
[GenerateSerializer]
public record SkillDto
{
    [MemoryPackOrder(0)] [Id(0)] public int SkillId { get; init; }
    [MemoryPackOrder(1)] [Id(1)] public string SkillName { get; init; } = "";
    [MemoryPackOrder(2)] [Id(2)] public string Description { get; init; } = "";
    [MemoryPackOrder(3)] [Id(3)] public string Icon { get; init; } = "";           // 技能图标资源路径
    [MemoryPackOrder(4)] [Id(4)] public int SkillCategory { get; init; }           // 0=心法,1=奇术,2=招式
    [MemoryPackOrder(5)] [Id(5)] public int Level { get; init; }
    [MemoryPackOrder(6)] [Id(6)] public int MaxLevel { get; init; }
    [MemoryPackOrder(7)] [Id(7)] public int NeiLiCost { get; init; }
    [MemoryPackOrder(8)] [Id(8)] public long Cooldown { get; init; }                // 毫秒
    [MemoryPackOrder(9)] [Id(9)] public long CastTime { get; init; }               // 毫秒
    [MemoryPackOrder(10)] [Id(10)] public float Range { get; init; }               // 米
    [MemoryPackOrder(11)] [Id(11)] public float RemainingCooldown { get; init; }   // 剩余冷却秒数，实时查询时填充
    [MemoryPackOrder(12)] [Id(12)] public int EquippedSlot { get; init; } = -1;    // 已装备快捷栏槽位，-1 表示未装备
}
```

### 5.4 SkillTreeNodeDto 技能树节点传输对象

```csharp
[MemoryPackable]
[GenerateSerializer]
public record SkillTreeNodeDto
{
    [MemoryPackOrder(0)] [Id(0)] public int NodeId { get; init; }                  // 节点 ID（技能树内唯一）
    [MemoryPackOrder(1)] [Id(1)] public int SkillId { get; init; }                 // 对应技能模板 ID
    [MemoryPackOrder(2)] [Id(2)] public string DisplayName { get; init; } = "";
    [MemoryPackOrder(3)] [Id(3)] public int Tier { get; init; }                    // 节点层级，心法=0 逐层递增
    [MemoryPackOrder(4)] [Id(4)] public int Category { get; init; }                // 0=心法,1=奇术,2=招式
    [MemoryPackOrder(5)] [Id(5)] public List<int> PrerequisiteNodeIds { get; init; } = new();  // 前置节点 ID 列表（DAG 入边）
    [MemoryPackOrder(6)] [Id(6)] public int UnlockStatus { get; init; }            // 0=Locked,1=Unlocked,2=Learned,3=Maxed
    [MemoryPackOrder(7)] [Id(7)] public int CurrentLevel { get; init; }            // 当前等级，未学习为 0
    [MemoryPackOrder(8)] [Id(8)] public int RequiredSkillPoints { get; init; }     // 学习消耗
    [MemoryPackOrder(9)] [Id(9)] public int RequiredPlayerLevel { get; init; }
    [MemoryPackOrder(10)] [Id(10)] public string Icon { get; init; } = "";
}
```

### 5.5 SkillBarSlotDto 快捷栏槽位传输对象

```csharp
[MemoryPackable]
[GenerateSerializer]
public record SkillBarSlotDto
{
    [MemoryPackOrder(0)] [Id(0)] public int Slot { get; init; }                    // 槽位索引 0-9
    [MemoryPackOrder(1)] [Id(1)] public int SkillId { get; init; }                 // 装备的技能 ID，0 表示空槽位
    [MemoryPackOrder(2)] [Id(2)] public SkillDto? SkillSnapshot { get; init; }     // 技能快照，前端展示用避免二次查询
    [MemoryPackOrder(3)] [Id(3)] public float RemainingCooldown { get; init; }     // 剩余冷却秒数
}

/// <summary>完整快捷栏 DTO（10 槽位整体传输）</summary>
[MemoryPackable]
[GenerateSerializer]
public record SkillBarDto
{
    [MemoryPackOrder(0)] [Id(0)] public ulong CharacterId { get; init; }
    [MemoryPackOrder(1)] [Id(1)] public List<SkillBarSlotDto> Slots { get; init; } = new();  // 固定 10 项，索引即 Slot
}
```

### 5.6 SkillTreeDto 技能树整体传输对象

```csharp
[MemoryPackable]
[GenerateSerializer]
public record SkillTreeDto
{
    [MemoryPackOrder(0)] [Id(0)] public int XinFaId { get; init; }                 // 心法 ID（该树根节点）
    [MemoryPackOrder(1)] [Id(1)] public string XinFaName { get; init; } = "";
    [MemoryPackOrder(2)] [Id(2)] public bool IsActive { get; init; }               // 是否为当前装备心法
    [MemoryPackOrder(3)] [Id(3)] public List<SkillTreeNodeDto> Nodes { get; init; } = new();  // 全部节点列表
    [MemoryPackOrder(4)] [Id(4)] public int AvailableSkillPoints { get; init; }    // 可用技能点
}
```

### 5.7 序列化兼容性约定

- **字段顺序不可变**：`[MemoryPackOrder(n)]` 一旦发布到生产，序号 `n` 不可回收或重排，新增字段追加至末尾并使用新的序号。
- **字段可空化兼容**：原有非空字段需变为可空时，保留原序号并将类型改为 `T?`，旧客户端反序列化时缺失值取 null。
- **弃用字段处理**：禁止物理删除已发布字段，改为内部忽略并注释 `// Deprecated since v1.1`，序号保留。
- **联合类型禁止**：DTO 字段类型禁止使用 `object` 或 `dynamic`，复杂结构须拆分为独立 DTO。
- **字典键约束**：`Dictionary<string, int>` 的 key 须为 ASCII 字符串，避免跨平台编码差异。

---

## 6. EFCore 实体与 SqlServer 表结构

本节定义任务与技能子系统的 EFCore 实体模型与 SqlServer 表结构。实体配置位于 `Horizon.Entities` 项目的 `GameEntityContext` 中，使用 Fluent API 配置关系与索引。

### 6.1 Quest 任务模板实体

```csharp
namespace Horizon.Model.Game
{
    /// <summary>任务模板实体（策划配置表，只读）</summary>
    public class Quest
    {
        public int QuestId { get; set; }
        public string QuestName { get; set; } = "";
        public string Description { get; set; } = "";
        public int QuestType { get; set; }          // 0=主线,1=支线,2=日常,3=周常,4=活动
        public int Level { get; set; }
        public int MinPlayerLevel { get; set; }
        public int PrerequisiteQuestId { get; set; }    // 前置任务 ID，0 表示无
        public bool CanAbandon { get; set; }
        public int DurationMinutes { get; set; }         // 有效时长，0 表示永久
        public string RewardsJson { get; set; } = "{}";  // 奖励 JSON 配置
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ICollection<QuestObjectiveTemplate> Objectives { get; set; } = new List<QuestObjectiveTemplate>();
    }
}
```

**表 `Quest` 字段**：

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| QuestId | int | PK, IDENTITY | 任务模板主键 |
| QuestName | nvarchar(64) | NOT NULL | 任务名称 |
| Description | nvarchar(512) | NOT NULL | 任务描述 |
| QuestType | tinyint | NOT NULL | 任务类型（0=主线,1=支线,2=日常,3=周常,4=活动） |
| Level | int | NOT NULL, default 1 | 任务等级 |
| MinPlayerLevel | int | NOT NULL, default 1 | 接取最低等级 |
| PrerequisiteQuestId | int | NOT NULL, default 0 | 前置任务 |
| CanAbandon | bit | NOT NULL, default 1 | 是否可放弃 |
| DurationMinutes | int | NOT NULL, default 0 | 有效时长（0=永久） |
| RewardsJson | nvarchar(max) | NOT NULL | 奖励配置 JSON |
| CreatedAt / UpdatedAt | datetime2 | NOT NULL | 创建/更新时间 |

**索引**：`IX_Quest_QuestType` (QuestType)、`IX_Quest_PrerequisiteQuestId` (PrerequisiteQuestId)、`UQ_Quest_QuestName` UNIQUE (QuestName)。

### 6.2 PlayerQuestProgress 玩家任务进度实体

```csharp
namespace Horizon.Model.Game
{
    /// <summary>玩家任务进度实体（运行时状态）</summary>
    public class PlayerQuestProgress
    {
        public long Id { get; set; }                       // 自增主键
        public Guid PlayerId { get; set; }                 // 玩家角色 Guid
        public int QuestId { get; set; }
        public int Status { get; set; }                    // 0=进行中,1=可提交,2=已完成,3=已放弃
        public string ObjectivesJson { get; set; } = "[]"; // 目标进度 JSON
        public DateTime AcceptTime { get; set; }
        public DateTime? CompleteTime { get; set; }
        public DateTime? ExpireTime { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Quest Quest { get; set; } = null!;
    }
}
```

**表 `PlayerQuestProgress` 字段与索引**：

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| Id | bigint | PK, IDENTITY | 自增主键 |
| PlayerId | uniqueidentifier | NOT NULL | 玩家 Guid |
| QuestId | int | NOT NULL, FK→Quest | 任务模板 |
| Status | tinyint | NOT NULL | 任务状态 |
| ObjectivesJson | nvarchar(max) | NOT NULL | 目标进度快照 JSON |
| AcceptTime | datetime2 | NOT NULL | 接取时间 |
| CompleteTime | datetime2 | NULL | 完成时间 |
| ExpireTime | datetime2 | NULL | 过期时间 |
| UpdatedAt | datetime2 | NOT NULL | 更新时间 |

**索引**：`IX_PlayerQuestProgress_PlayerId_Status` (PlayerId, Status)、`UQ_PlayerQuestProgress_PlayerId_QuestId` UNIQUE (PlayerId, QuestId)、`IX_PlayerQuestProgress_ExpireTime` (ExpireTime) WHERE ExpireTime IS NOT NULL。

### 6.3 Skill 技能模板实体

```csharp
namespace Horizon.Model.Game
{
    /// <summary>技能模板实体（策划配置表，只读）</summary>
    public class Skill
    {
        public int SkillId { get; set; }
        public string SkillName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public int SkillCategory { get; set; }       // 0=心法,1=奇术,2=招式
        public int MaxLevel { get; set; }
        public int BaseNeiLiCost { get; set; }
        public long BaseCooldown { get; set; }        // 毫秒
        public long BaseCastTime { get; set; }        // 毫秒
        public float BaseRange { get; set; }
        public float NeiLiCostGrowth { get; set; }    // 每级内力消耗增长率
        public float RangeGrowth { get; set; }        // 每级范围增长
        public string AttributesJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
```

**索引**：`IX_Skill_SkillCategory` (SkillCategory)、`UQ_Skill_SkillName` UNIQUE (SkillName)。

### 6.4 PlayerSkill 玩家技能实体

```csharp
namespace Horizon.Model.Game
{
    /// <summary>玩家已学习技能实体</summary>
    public class PlayerSkill
    {
        public long Id { get; set; }
        public Guid PlayerId { get; set; }
        public int SkillId { get; set; }
        public int Level { get; set; }                // 当前等级
        public int EquippedSlot { get; set; }         // 装备槽位，-1 表示未装备
        public DateTime LearnedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Skill Skill { get; set; } = null!;
    }
}
```

**表 `PlayerSkill` 字段与索引**：

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| Id | bigint | PK, IDENTITY | 自增主键 |
| PlayerId | uniqueidentifier | NOT NULL | 玩家 Guid |
| SkillId | int | NOT NULL, FK→Skill | 技能模板 |
| Level | int | NOT NULL, default 1 | 当前等级 |
| EquippedSlot | smallint | NOT NULL, default -1 | 装备槽位 |
| LearnedAt / UpdatedAt | datetime2 | NOT NULL | 学习/更新时间 |

**索引**：`UQ_PlayerSkill_PlayerId_SkillId` UNIQUE (PlayerId, SkillId)、`IX_PlayerSkill_PlayerId_EquippedSlot` (PlayerId, EquippedSlot) WHERE EquippedSlot >= 0。

### 6.5 SkillTree 与 SkillTreeNode 实体

```csharp
namespace Horizon.Model.Game
{
    /// <summary>技能树实体（每个心法对应一棵树）</summary>
    public class SkillTree
    {
        public int TreeId { get; set; }
        public int XinFaSkillId { get; set; }         // 根心法技能 ID
        public string TreeName { get; set; } = "";
        public string Description { get; set; } = "";
        public int MaxTier { get; set; }              // 最大层级
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ICollection<SkillTreeNode> Nodes { get; set; } = new List<SkillTreeNode>();
    }

    /// <summary>技能树节点实体</summary>
    public class SkillTreeNode
    {
        public int NodeId { get; set; }
        public int TreeId { get; set; }
        public int SkillId { get; set; }              // 关联技能模板
        public string DisplayName { get; set; } = "";
        public int Tier { get; set; }                 // 层级
        public int Category { get; set; }             // 0=心法,1=奇术,2=招式
        public string PrerequisiteNodeIdsJson { get; set; } = "[]";  // 前置节点 JSON
        public int RequiredSkillPoints { get; set; }
        public int RequiredPlayerLevel { get; set; }
        public string Icon { get; set; } = "";
        public int SortOrder { get; set; }
        public SkillTree Tree { get; set; } = null!;
        public Skill Skill { get; set; } = null!;
    }
}
```

**表 `SkillTreeNode` 索引**：`IX_SkillTreeNode_TreeId_Tier` (TreeId, Tier)、`IX_SkillTreeNode_SkillId` (SkillId)。外键 TreeId→SkillTree 采用 Cascade 删除，SkillId→Skill 采用 Restrict。

### 6.6 EFCore Fluent API 配置要点

实体配置位于 `GameEntityContext.OnModelCreating`，关键配置如下（仅列要点，非完整代码）：

- 所有实体 `ToTable` 映射至对应表名，`HasKey` 显式声明主键。
- 枚举字段（`QuestType`、`Status`）使用 `HasConversion<byte>()` 以 tinyint 存储。
- 字符串字段统一 `HasMaxLength` + `IsRequired`，避免 nvarchar(max) 滥用。
- 外键关系：`PlayerQuestProgress.QuestId → Quest` 使用 `Restrict` 防止误删模板；`SkillTreeNode.TreeId → SkillTree` 使用 `Cascade` 随树删除。
- 索引命名统一前缀 `IX_`（普通索引）/ `UQ_`（唯一索引），后接 `表名_字段名`。
- 过滤索引使用 `HasFilter`，如 `[ExpireTime] IS NOT NULL`、`[EquippedSlot] >= 0`。
- `EquippedSlot` 字段 `HasDefaultValue(-1)`，未装备时默认 -1。

### 6.7 实体关系图（文字描述）

```
Quest (1) ──< PlayerQuestProgress (N)     [QuestId, Restrict]
Quest (1) ──< QuestObjectiveTemplate (N)  [QuestId, Cascade]

Skill (1) ──< PlayerSkill (N)             [SkillId, Restrict]
Skill (1) ──< SkillTreeNode (N)           [SkillId, Restrict]

SkillTree (1) ──< SkillTreeNode (N)       [TreeId, Cascade]

PlayerId 为逻辑外键（不建立物理 FK），由应用层保证一致性，
对应 Player 实体位于 Horizon.Model.Basic。
```

---

## 7. Redis 缓存键命名规范

### 7.1 命名约定

- **前缀**：所有键以 `hundun:` 开头，标识混沌世界系统。
- **分隔符**：使用冒号 `:` 分隔层级，便于 Redis Cluster 的哈希标签与命令行工具阅读。
- **变量占位**：`{playerId}` 为玩家角色 Guid 的 32 位无连字符小写形式（如 `a1b2c3d4e5f6...`）。
- **类型标识**：使用 `active`/`progress`/`completed`/`bar`/`cooldown` 等语义化短词。
- **禁止**：键名包含空格、中文、特殊字符；键长度建议不超过 128 字符。

### 7.2 任务子系统缓存键

#### 7.2.1 `hundun:quest:{playerId}:active` - 进行中任务列表

- **数据结构**：Sorted Set
- **TTL**：30 分钟
- **Member**：任务的 MemoryPack 二进制 Base64 编码字符串（或 QuestId 字符串，按场景取舍）
- **Score**：任务接取时间的 Unix 时间戳（秒）
- **用途**：前端任务日志“进行中”标签页快速加载，避免每次走 Grain。

```
ZADD hundun:quest:a1b2c3d4:active 1752900000 <questBlob:1001>
ZRANGE hundun:quest:a1b2c3d4:active 0 -1 WITHSCORES
ZREM hundun:quest:a1b2c3d4:active <questBlob:1001>
```

#### 7.2.2 `hundun:quest:{playerId}:progress` - 任务目标进度

- **数据结构**：Hash
- **TTL**：30 分钟
- **Field**：`{questId}:{objectiveIndex}`，如 `1001:0`、`1001:1`
- **Value**：`{currentCount}/{requiredCount}` 字符串，如 `3/10`
- **用途**：细粒度进度查询，支持单目标增量更新而不需重写整个任务。

```
HSET hundun:quest:a1b2c3d4:progress 1001:0 3/10
HGETALL hundun:quest:a1b2c3d4:progress
HDEL hundun:quest:a1b2c3d4:progress 1001:0
```

#### 7.2.3 `hundun:quest:{playerId}:completed` - 已完成任务集合

- **数据结构**：Set
- **TTL**：7 天
- **Member**：QuestId 字符串
- **用途**：任务历史去重、前置任务完成校验。

```
SADD hundun:quest:a1b2c3d4:completed 1001 1002
SISMEMBER hundun:quest:a1b2c3d4:completed 1001
SMEMBERS hundun:quest:a1b2c3d4:completed
```

### 7.3 技能子系统缓存键

#### 7.3.1 `hundun:skill:{playerId}` - 已学习技能

- **数据结构**：Hash
- **TTL**：60 分钟
- **Field**：SkillId 字符串
- **Value**：SkillDto 的 MemoryPack 二进制 blob
- **用途**：技能面板列表加载、技能释放时校验已学习状态。

```
HSET hundun:skill:a1b2c3d4 2001 <skillBlob>
HGETALL hundun:skill:a1b2c3d4
HDEL hundun:skill:a1b2c3d4 2001
```

#### 7.3.2 `hundun:skill:{playerId}:bar` - 快捷栏装备

- **数据结构**：List
- **TTL**：60 分钟
- **元素**：固定 10 项，索引即槽位，值为 SkillId 字符串（0 表示空）
- **用途**：HUD 快捷栏渲染、技能释放时槽位查找。

```
RPUSH hundun:skill:a1b2c3d4:bar 2001 0 2002 0 0 0 2003 0 0 0
LRANGE hundun:skill:a1b2c3d4:bar 0 -1
LSET hundun:skill:a1b2c3d4:bar 2 2002
```

#### 7.3.3 `hundun:skill:{playerId}:cooldown` - 技能冷却

- **数据结构**：Hash
- **TTL**：5 分钟（与最长冷却时长对齐）
- **Field**：SkillId 字符串
- **Value**：冷却到期 Unix 时间戳（毫秒）字符串
- **用途**：技能释放前快速校验冷却，避免走 Grain；由 CastSkill 后台写。

```
HSET hundun:skill:a1b2c3d4:cooldown 2001 1752900030000
HGET hundun:skill:a1b2c3d4:cooldown 2001
HDEL hundun:skill:a1b2c3d4:cooldown 2001
```

#### 7.3.4 `hundun:skilltree:{playerId}` - 技能树解锁状态

- **数据结构**：Hash
- **TTL**：120 分钟
- **Field**：NodeId 字符串
- **Value**：UnlockStatus 整数字符串（0/1/2/3）
- **用途**：技能树面板渲染节点状态颜色。

```
HSET hundun:skilltree:a1b2c3d4 101 1 102 2 103 0
HGETALL hundun:skilltree:a1b2c3d4
```

### 7.4 辅助键与模板缓存

除上述玩家维度缓存键外，子系统还使用以下辅助键：分布式锁 `hundun:lock:quest:{playerId}` / `hundun:lock:skill:{playerId}`（String NX，TTL 5s，用于 Grain 激活时防并发重建）；只读模板缓存 `hundun:quest:template:{questId}` / `hundun:skill:template:{skillId}` / `hundun:skilltree:template:{treeId}`（String，TTL 24h，存储策划配置 JSON）。

### 7.5 失效与清理策略

- **主动失效**：玩家完成任务、学习技能、装备变更时，对应缓存键由 Grain 在 WriteStateAsync 成功后异步更新。
- **被动过期**：所有键设置 TTL，过期后由 Redis 自动回收，下次读取触发 Grain 重建；玩家角色删除时通过 Lua 脚本扫描 `hundun:*:{playerId}:*` 批量删除（低峰期执行）。
- **禁止**：使用 `KEYS hundun:*` 命令进行全量扫描，必须使用 `SCAN` 迭代器。

---

## 8. 前端调用时序图

本节以文字步骤描述典型业务场景的调用时序，覆盖 UI 事件 → TouchSocket 网关 → Grain → Redis/SqlServer → 响应回传的全链路。每步以“组件 + 动作”形式描述，`→` 表示数据流方向。

### 8.1 场景一：玩家接受任务

**触发**：玩家在 NPC 对话界面点击“接受任务”按钮。

```
步骤 1  [前端 UI] quest-log.html 点击“接受任务” → 构造 QuestAcceptRequest { CharacterId, QuestId } → MemoryPack 序列化 → TouchSocket 发送（MessageType=1002）
步骤 2  [TouchSocket 网关] 解析 MessageType → 路由至 QuestHandler → 反序列化请求
步骤 3  [QuestHandler] 获取 playerGuid → GrainFactory.GetGrain<IQuestGrain>(playerGuid) → 调用 AcceptQuestAsync(questId, name, desc, type, level, rewards)
步骤 4  [QuestGrain] 激活（若未激活）：读 Redis hundun:quest:{playerId}:active → 未命中则加锁从 SqlServer PlayerQuestProgress 加载 → 回填 Redis
步骤 5  [QuestGrain] 校验 questId 有效 → 未在 ActiveQuests → 未在 CompletedQuests → 未达上限 → 写入 ActiveQuests → WriteStateAsync() 持久化 SqlServer
步骤 6  [QuestGrain] 异步更新 Redis：ZADD active {ts} {questBlob}、HSET progress {questId}:0 0/10（按目标初始化）→ 返回 true
步骤 7  [QuestHandler] 构造 QuestAcceptResponse { Success=true, Quest } → MemoryPack 序列化 → 回送前端
步骤 8  [前端 UI] 收到响应 → 任务日志新增任务卡片 → 播放“接受任务成功”动画
```

### 8.2 场景二：击杀怪物触发任务进度更新（服务端推送）

**触发**：玩家在野外击杀任务目标怪物。

```
步骤 1  [CombatGrain] 击杀结算完成 → 发布 CombatPlayerKill 事件至 CombatEvents Stream
步骤 2  [QuestGrain] 订阅 CombatEvents 收到事件 → 匹配 ActiveQuests 中 Kill 类型目标且 TargetId=NpcId → 命中则调用 UpdateQuestProgressAsync(questId, objIdx, +1)
步骤 3  [QuestGrain] 校验目标未完成 → CurrentCount = Min(current+1, required) → 达标则 IsCompleted=true → 全部完成则 Status=ReadyToSubmit(1) → WriteStateAsync() 持久化
步骤 4  [QuestGrain] 异步更新 Redis：HSET progress {questId}:{objIdx} {new}/{required} → 发布 QuestProgressPush 至 QuestEvents Stream
步骤 5  [TouchSocket 网关] 订阅 QuestEvents → 按 CharacterId 查找在线连接 → 序列化 QuestProgressPush → 发送至 WebSocket
步骤 6  [前端 UI] 收到推送 → 更新任务追踪 HUD 进度文本（3/10 → 4/10）→ 目标完成播放音效 → 可提交则卡片高亮“可领取”
```

### 8.3 场景三：完成任务并领取奖励

**触发**：玩家在任务日志点击“领取奖励”按钮。

```
步骤 1  [前端 UI] 点击“领取奖励” → 构造 QuestCompleteRequest → MemoryPack 序列化 → TouchSocket 发送（MessageType=1010）
步骤 2  [TouchSocket 网关] 路由至 QuestHandler → 调用 questGrain.CompleteQuestAsync(questId)
步骤 3  [QuestGrain] 校验任务在 ActiveQuests → 校验全部目标 IsCompleted=true → Status=Completed(2) → 迁移至 CompletedQuests → WriteStateAsync() 持久化
步骤 4  [QuestGrain] 异步更新 Redis：ZREM active {questBlob}、SADD completed {questId}、HDEL progress {questId}:*（Lua 批量删）
步骤 5  [QuestGrain] 跨 Grain 发放奖励：inventoryGrain.AddItemAsync、characterGrain.AddExperienceAsync、AddCurrencyAsync（幂等）
步骤 6  [QuestGrain] 发布 QuestCompletePush 至 QuestEvents Stream → 携带 { CharacterId, QuestId, Rewards, CompleteMethod=0 }
步骤 7  [TouchSocket 网关] 转发 QuestCompletePush 至前端 → 同时返回 QuestCompleteResponse { Success, Rewards }
步骤 8  [前端 UI] 任务卡片迁移至“已完成”标签页 → 弹出奖励展示弹窗 → 触发“任务完成”全屏特效
```

### 8.4 场景四：学习技能并装备至快捷栏

**触发**：玩家在技能面板 skill-panel.html 点击技能树节点的“学习”按钮，随后拖拽至快捷栏槽位。

```
步骤 1  [前端 UI] 点击“学习” → 构造 SkillLearnRequest { CharacterId, SkillId, LearnMethod=0 } → 发送（MessageType=1103）
步骤 2  [TouchSocket 网关] 路由至 SkillHandler → 调用 skillGrain.LearnSkillAsync(skillId)
步骤 3  [SkillGrain] 校验未学习 → SkillPoints>0 → 前置技能已学习 → LearnedSkills[skillId]=new SkillInfo{Level=1,...} → SkillPoints-- → WriteStateAsync()
步骤 4  [SkillGrain] 异步更新 Redis：HSET hundun:skill:{playerId} {skillId} {skillBlob} → 发布 SkillLearnedPush { LearnedSkill, RemainingSkillPoints }
步骤 5  [TouchSocket 网关] 转发 SkillLearnedPush → 返回 SkillLearnResponse { Success=true }
步骤 6  [前端 UI] 节点状态 Unlocked→Learned → 技能点扣减动画 → 新技能图标点亮可拖拽
步骤 7  [前端 UI] 拖拽技能图标至快捷栏槽位 3 → 构造 SkillEquipRequest { SkillId, Slot=3 } → 发送（MessageType=1107）
步骤 8  [SkillGrain] 校验已学习 → SkillCategory=招式 → Slot∈[0,9] → 记录 oldSkillId → SkillBar[3]=skillId → WriteStateAsync()
步骤 9  [SkillGrain] 异步更新 Redis：LSET bar 3 {skillId} → 发布 SkillEquippedPush { Slot=3, NewSkillId, OldSkillId }
步骤 10 [TouchSocket 网关] 转送推送 + 返回 SkillEquipResponse → [前端 UI] 槽位 3 渲染新技能图标 → 若 OldSkillId≠0 原槽位置空
```

### 8.5 场景五：技能释放冷却查询（读缓存优先）

**触发**：玩家战斗中点击快捷栏技能。

```
步骤 1  [前端 UI] 点击槽位 3 技能 → 读本地缓存的剩余冷却 → 未结束则灰显不发请求 → 已结束则构造 SkillCastMessage 发送至 CombatHandler
步骤 2  [CombatGrain] 收到释放请求 → 调用 skillGrain.GetSkillCooldownAsync(skillId) 二次校验
步骤 3  [SkillGrain] 优先读 Redis cooldown 字段 → 命中且未到期返回剩余秒数（CombatGrain 拒绝）→ 未命中或已到期读内存 → 仍空返回 0（允许释放）
步骤 4  [CombatGrain] 校验通过执行伤害结算 → 调用 skillGrain 记录冷却（SkillCooldowns[skillId]=Now）→ 异步 HSET Redis cooldown {skillId} {expireTs}
步骤 5  [CombatGrain] 发布 CombatSkillCast 事件 → 网关转发 SkillCooldownUpdateMessage → [前端 UI] 更新本地冷却倒计时 → 槽位 3 显示冷却遮罩
```

### 8.6 异常与重试时序

- **TouchSocket 断线**：前端本地缓存继续展示，重连后主动发送 `QuestListRequest` / `SkillBarQueryRequest` 全量同步。
- **Grain 调用超时**：网关默认 3 秒超时返回 ReasonCode=99，前端展示“网络异常，请重试”，不自动重试写操作（避免重复接受任务）。
- **SqlServer 写入失败**：Grain 抛异常，网关返回错误，Redis 不更新保持旧值，前端状态不变由玩家手动重试。
- **Redis 写入失败**：仅记录日志，不影响主流程，下次读取由 Grain 重建缓存。

---

## 附录 A：版本变更记录

| 版本 | 日期 | 变更说明 |
| --- | --- | --- |
| v1.0 | 2026-07-19 | 初始版本，定义 IQuestGrain / ISkillGrain / ISkillTreeGrain 接口、TouchSocket 消息协议、MemoryPack DTO、EFCore 实体、Redis 缓存规范与时序图 |

## 附录 B：相关文件索引与命名空间

**核心文件路径**：

| 文件 | 路径 | 说明 |
| --- | --- | --- |
| QuestGrain / SkillGrain 实现 | `Horizon.Orleans.Grains/QuestGrain.cs` / `SkillGrain.cs` | 任务 / 技能 Grain 参考实现 |
| Grain 接口定义 | `Horizon.Orleans.Interface/IQuestDungeonGrains.cs` / `IGameSystemGrains.cs` | IQuestGrain / ISkillGrain 现有定义 |
| 状态/数据模型 | `Horizon.Game.Message/Network/GrainStateModels.cs` | QuestState / SkillState / QuestData 定义 |
| 技能消息 | `Horizon.Game.Message/Network/SkillMessages.cs` | SkillInfo / LearnSkillRequest 定义 |
| 枚举定义 | `Horizon.Game.Message/Network/GrainEnums.cs` | QuestProgressStatus 等枚举 |
| 实体上下文 | `Horizon.Entities/GameEntityContext.cs` | EFCore DbContext |
| 前端页面 | `game-ui-system/pages/quest-log.html` / `skill-panel.html` | 任务日志 / 技能面板 UI |

**命名空间约定**：Grain 接口 `Horizon.Orleans.Interface`、Grain 实现 `Horizon.Orleans.Grains`、消息/DTO `Horizon.Game.Message.Network`（原有）与 `Horizon.Game.Message.Network.Dtos`（新增 record DTO）、实体 `Horizon.Model.Game`、枚举 `Horizon.Game.Message.Enums`。前端 TypeScript 类型定义应与第 5 节 DTO 一一对应，由 `TypeDumper` 工具自动生成，避免手写偏差。

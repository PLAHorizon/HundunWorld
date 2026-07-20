# 混沌世界 MMORPG - 角色与背包子系统后端接口契约文档

> 文档编号：02-character-inventory-api
> 版本：v1.0.0
> 最后更新：2026-07-19
> 技术栈：.NET 10 C# + Orleans（Grain actor 模型）+ SqlServer（EFCore 持久化）+ Redis（缓存）+ TouchSocket（TCP/WebSocket 实时通信）+ MemoryPack（二进制序列化）

---

## 目录

- [1. 子系统概述与领域边界](#1-子系统概述与领域边界)
- [2. Orleans Grain 接口定义](#2-orleans-grain-接口定义)
- [3. Grain 状态与持久化](#3-grain-状态与持久化)
- [4. TouchSocket 消息协议](#4-touchsocket-消息协议)
- [5. MemoryPack 序列化 DTO](#5-memorypack-序列化-dto)
- [6. EFCore 实体与 SqlServer 表结构](#6-efcore-实体与-sqlserver-表结构)
- [7. Redis 缓存键命名规范](#7-redis-缓存键命名规范)
- [8. 前端调用时序图](#8-前端调用时序图)

---

## 1. 子系统概述与领域边界

### 1.1 子系统定位

角色与背包子系统是混沌世界 MMORPG 的核心基础子系统，负责管理玩家角色的属性成长、装备穿戴、物品存储与流转、装备强化以及制造（生活技能）等玩法逻辑。本子系统是战斗、社交、交易、任务等上层系统的数据与服务基础。

架构定位：

- 上游依赖：账号系统（提供 playerId）、世界系统（场景上下文）、配置系统（物品/装备/配方静态数据）。
- 下游服务：战斗系统（读取五维属性与装备词条）、交易系统（读写背包物品）、任务系统（校验与扣减任务道具）、社交系统（角色信息展示）。

### 1.2 角色五维属性

角色属性采用「五维」模型，五维属性共同决定角色的战斗能力与成长上限。

| 属性代号 | 中文名 | 说明 | 主要影响 |
|---------|--------|------|---------|
| `Constitution` | 体魄 | 身体强度与生命力根基 | 生命上限、物理防御、负重上限 |
| `InnerPower` | 内功 | 内力修为与气劲强弱 | 内力上限、内功攻击、内功防御 |
| `Agility` | 身法 | 敏捷与反应速度 | 闪避、命中、暴击、出手速度 |
| `RootBone` | 根骨 | 修炼资质与承受力 | 修为增长效率、抗性、韧性 |
| `Comprehension` | 悟性 | 领悟与学习天赋 | 技能学习速度、招式领悟概率、制造成功率 |

属性数值采用 `基础值 + 成长值 + 装备加成 + buff 加成` 的累加模型。基础值由角色创建时分配；成长值随等级提升自动增长；装备加成由穿戴装备词条提供；buff 加成由临时状态（丹药、功法、阵法）提供。每个五维属性的有效区间为 `1 ~ 9999`，超出区间需截断并记录告警日志。

### 1.3 装备槽系统

角色拥有 10 类装备槽位（共 11 个槽，戒指拆分为主指/副指），每个槽位仅允许装备对应类型的装备，且同一槽位同一时刻只能容纳一件装备。

| 槽位代号 | 中文名 | 允许装备类型 | 参与外观 |
|---------|--------|------------|---------|
| `Head` | 头部 | 冠、帽、兜、盔 | 是 |
| `Body` | 身体 | 袍、甲、衣 | 是 |
| `Hands` | 手部 | 护腕、手套 | 是 |
| `Feet` | 足部 | 靴、鞋 | 是 |
| `Waist` | 腰部 | 腰带、束带 | 是 |
| `Wrists` | 腕部 | 护腕、镯 | 否 |
| `Neck` | 颈部 | 项链、坠 | 否 |
| `FingerMain`/`FingerSub` | 指部 | 戒指（主指/副指） | 否 |
| `Weapon` | 武器 | 刀、剑、枪、棍、暗器等 | 是 |
| `OffHand` | 副手 | 盾、副武器、法器 | 是 |

装备槽与背包之间存在严格流转约束：穿戴（Equip）必须从背包取出；卸下（Unequip）必须放回背包（背包已满则失败）。

### 1.4 背包格子

每个角色拥有一个主背包，默认容量 120 格，可通过道具或 VIP 等级扩展至最多 240 格。特性：

- 格子按线性索引编号，从 0 开始；每格可放置一个物品堆叠，堆叠上限由物品静态配置决定（普通材料 999，装备 1）。
- 空格子以 `ItemId == 0` 或 `Count == 0` 表示；背包支持多页展示（前端逻辑），后端只感知线性索引。

扩展存储（仅作边界说明）：仓库（Warehouse，离线可访问，容量更大）、临时背包（TempBag，任务/活动物品，有过期时间）。

### 1.5 物品操作

背包物品支持以下原子操作，所有操作均通过 `IInventoryGrain` 完成，完成后通过 TouchSocket 推送 `InventoryUpdatePush` 通知前端：

| 操作 | 说明 | 关键校验 |
|------|------|---------|
| `Move` | 移动物品到指定格子 | 目标格为空或可堆叠；源格与目标格 itemId 一致才允许堆叠合并 |
| `Use` | 使用物品（消耗品、道具） | 物品可使用；使用条件（等级、场景）；使用冷却 |
| `Drop` | 丢弃物品 | 物品可丢弃（绑定物品禁止）；二次确认 |
| `Split` | 拆分堆叠 | 拆分数量 1 ~ 当前堆叠数 - 1；目标格必须为空 |
| `Sort` | 整理背包 | 按类型/Id 聚合堆叠，空格后移；整理为原子事务，期间锁定背包写操作 |

物品操作必须保证原子性：任一步骤失败则整体回滚。所有写操作通过 Grain 单线程模型天然串行化，避免并发冲突。

### 1.6 装备强化

装备强化是提升装备属性的主要途径。规则：

- 强化等级 `+0 ~ +15`，+10 及以上为「高阶强化」，失败有降级风险。
- 消耗：银两 + 强化材料（如「玄铁」「精金」），高阶强化额外消耗保底材料。
- 成功率随等级递减，+10 以上显著降低，可使用「幸运符」提升成功率。
- 效果：按装备基础属性的百分比叠加（每级 +5%~ +10%，由装备类型决定）。
- 失败处理：+1 ~ +9 仅消耗材料；+10 ~ +14 降 1 级；+15 为上限不可继续强化。

强化通过 `IEquipmentGrain.Enhance` 完成，结果通过 `EquipmentChangePush` 推送。强化涉及读取背包（扣材料）与装备实例（改强化等级），采用「EquipmentGrain 为主、InventoryGrain 为辅」的调用链。

### 1.7 制造系统

制造系统（生活技能）允许玩家使用材料制作装备、药品、材料等物品。规则：

- 制造配方（Recipe）定义产出物品、所需材料、所需工具、制造时长、成功率。
- 制造为异步过程：发起后进入制造队列，时长到达后产出物品并扣除材料。
- 制造队列：每角色最多同时 3 项（可扩展），队列项可取消（取消返还材料，已完成的不可取消）。
- 成功率受「悟性」属性与「幸运符」加成影响。
- 进度通过 `CraftingProgressPush` 实时推送，完成时推送最终结果。

制造流程涉及 `ICraftingGrain`（队列管理）与 `IInventoryGrain`（材料扣减与产物入包）的协作。

### 1.8 领域边界

- 属于本子系统：角色五维属性读写、装备槽管理、背包物品 CRUD、装备强化、制造队列。
- 不属于本子系统：战斗计算（战斗系统消费属性）、交易撮合（交易系统调用本子系统）、物品静态配置（配置系统）、外观渲染（前端）。
- 跨子系统约束：本子系统对外仅暴露 Grain 接口与 TouchSocket 消息，不直接暴露数据库访问。

---

## 2. Orleans Grain 接口定义

### 2.1 Grain 总览

本子系统包含 4 个核心 Grain，均以 `playerId`（长整型，全局唯一玩家标识）作为 GrainKey。

| Grain 接口 | 职责 | GrainKey | 激活策略 |
|-----------|------|---------|---------|
| `ICharacterGrain` | 角色属性与基础信息 | `playerId` | 按需激活，状态持久化 |
| `IInventoryGrain` | 背包物品管理 | `playerId` | 按需激活，持久化 + Redis 缓存 |
| `IEquipmentGrain` | 装备槽与装备实例 | `playerId` | 按需激活，状态持久化 |
| `ICraftingGrain` | 制造队列管理 | `playerId` | 按需激活，持久化 + 定时器 |

### 2.2 通用响应包装

所有 Grain 方法返回统一的 `GrainResult<T>` 结构，便于错误处理与日志追踪。

```csharp
[MemoryPackable]
public partial record GrainResult<T>(
    [property: MemoryPackOrder(0)] bool Success,
    [property: MemoryPackOrder(1)] int Code,        // 0 表示成功，非 0 为错误码
    [property: MemoryPackOrder(2)] string Message,  // 人类可读消息
    [property: MemoryPackOrder(3)] T? Data           // 业务数据，失败时为 null
);
```

### 2.3 ICharacterGrain

负责角色五维属性与基础信息管理。

```csharp
using Orleans;

namespace HundunWorld.Game.Grains;

public interface ICharacterGrain : IGrainWithStringKey
{
    Task<GrainResult<CharacterDto>> GetCharacter();                              // 获取角色完整信息
    Task<GrainResult<CharacterDto>> UpdateAttributes(UpdateAttributesRequest request);  // 更新五维属性
    Task<GrainResult<CharacterDto>> AddExperience(long exp);                     // 增加经验，自动升级
    Task<GrainResult<CharacterDto>> AllocatePoints(                             // 角色加点
        int constitution, int innerPower, int agility, int rootBone, int comprehension);
    Task<GrainResult<CharacterDto>> ResetAttributes();                          // 重置属性点（消耗道具）
}

[MemoryPackable]
public partial record UpdateAttributesRequest(
    [property: MemoryPackOrder(0)] int Constitution,
    [property: MemoryPackOrder(1)] int InnerPower,
    [property: MemoryPackOrder(2)] int Agility,
    [property: MemoryPackOrder(3)] int RootBone,
    [property: MemoryPackOrder(4)] int Comprehension,
    [property: MemoryPackOrder(5)] string Reason  // 变更原因，用于审计
);
```

### 2.4 IInventoryGrain

负责背包物品全部操作，是本子系统中调用最频繁的 Grain。

```csharp
using Orleans;

namespace HundunWorld.Game.Grains;

public interface IInventoryGrain : IGrainWithStringKey
{
    Task<GrainResult<InventorySnapshotDto>> GetInventory();                          // 完整背包快照
    Task<GrainResult<InventorySnapshotDto>> MoveItem(int fromIndex, int toIndex, int count);  // 移动/堆叠
    Task<GrainResult<UseItemResultDto>> UseItem(int slotIndex, int count, long targetId);     // 使用物品
    Task<GrainResult<InventorySnapshotDto>> DropItem(int slotIndex, int count);               // 丢弃
    Task<GrainResult<InventorySnapshotDto>> SplitItem(int fromIndex, int toIndex, int count); // 拆分
    Task<GrainResult<InventorySnapshotDto>> SortInventory();                                  // 整理
    Task<GrainResult<int>> QueryItemCount(int itemId);                                        // 查询物品数量（任务/交易校验）
    Task<GrainResult<InventorySnapshotDto>> AddItem(int itemId, int count, string source);    // 系统加物品
    Task<GrainResult<InventorySnapshotDto>> RemoveItem(int itemId, int count, string reason); // 系统扣物品（不足则整体失败）
}
```

### 2.5 IEquipmentGrain

负责装备槽管理与装备强化。

```csharp
using Orleans;

namespace HundunWorld.Game.Grains;

public interface IEquipmentGrain : IGrainWithStringKey
{
    Task<GrainResult<EquipmentSnapshotDto>> GetEquipment();                                    // 所有装备槽状态
    Task<GrainResult<EquipResultDto>> Equip(int slotIndex, EquipmentSlot? targetSlot);         // 穿戴（targetSlot 为 null 自动选择）
    Task<GrainResult<UnequipResultDto>> Unequip(EquipmentSlot slot);                           // 卸下（放回背包）
    Task<GrainResult<EnhanceResultDto>> Enhance(EquipmentSlot slot, bool useLuckyCharm, bool useProtection);  // 强化
    Task<GrainResult<EquipmentDto>> GetEquipmentDetail(EquipmentSlot slot);                    // 装备实例详情
    Task<GrainResult<EquipmentDto>> Repair(EquipmentSlot slot);                                // 修理（恢复耐久）
}

/// <summary>装备槽位枚举（10 类 11 个槽）</summary>
public enum EquipmentSlot
{
    Head = 0, Body = 1, Hands = 2, Feet = 3, Waist = 4,
    Wrists = 5, Neck = 6, FingerMain = 7, FingerSub = 8,
    Weapon = 9, OffHand = 10
}
```

### 2.6 ICraftingGrain

负责制造队列管理与进度推进。

```csharp
using Orleans;

namespace HundunWorld.Game.Grains;

public interface ICraftingGrain : IGrainWithStringKey
{
    Task<GrainResult<CraftingQueueDto>> GetProgress();                                         // 获取制造队列
    Task<GrainResult<CraftingQueueDto>> StartCrafting(int recipeId, int count, bool useLuckyCharm);  // 开始制造
    Task<GrainResult<CraftingQueueDto>> CancelCrafting(long queueItemId);                      // 取消（仅进行中）
    Task<GrainResult<ClaimCraftResultDto>> ClaimResult(long queueItemId);                      // 领取产物（转入背包）
    Task<GrainResult<List<CraftingRecipeDto>>> GetAvailableRecipes();                           // 可制造配方（按材料过滤）
}
```

### 2.7 GrainKey 策略

所有 Grain 采用 `IGrainWithStringKey`，Key 为 `playerId` 的字符串形式。采用字符串 Key 的原因：便于日志追踪与运维查询；与 Redis 缓存键命名规范保持一致；支持未来扩展复合 Key（如 `playerId:sessionId`）。

```csharp
var characterGrain = client.GetGrain<ICharacterGrain>(playerId.ToString());
var inventoryGrain = client.GetGrain<IInventoryGrain>(playerId.ToString());
var equipmentGrain = client.GetGrain<IEquipmentGrain>(playerId.ToString());
var craftingGrain  = client.GetGrain<ICraftingGrain>(playerId.ToString());
```

### 2.8 跨 Grain 调用约束

- `IEquipmentGrain` 与 `ICraftingGrain` 可调用 `IInventoryGrain`（扣材料、入产物），反向调用禁止。
- `IInventoryGrain` 不调用任何其他业务 Grain，保持纯粹的数据存储职责。
- `ICharacterGrain` 独立运行，不依赖其他业务 Grain。
- 所有跨 Grain 调用必须设置超时（默认 5 秒），超时后回滚本地状态。

---

## 3. Grain 状态与持久化

### 3.1 持久化策略

本子系统采用「Redis 缓存 + SqlServer 持久化」的双层存储模型。Grain 状态生命周期：

1. **激活时**：优先从 Redis 读取状态；未命中则从 SqlServer 加载并回填 Redis。
2. **运行时**：写操作先更新内存状态，异步写 Redis（立即），定时或事件触发回写 SqlServer。
3. **deactivate 时**：失活前将内存状态完整回写 SqlServer，并保留 Redis 缓存（带 TTL）。

Orleans 采用 `RedisStorage` 作为 Grain 存储提供程序：

```csharp
siloBuilder.AddRedisGrainStorage("CharacterStorage", options =>
{
    options.ConnectionString = redisConfig.ConnectionString;
    options.UseJson = false;             // 使用 MemoryPack 二进制
    options.Prefix = "hundun:grain:";    // Grain 状态键前缀
});
```

### 3.2 Redis Hash 结构

每个 Grain 状态在 Redis 中以 Hash 结构存储，便于部分字段更新。

**ICharacterGrain** — Key：`hundun:char:{playerId}`

| 字段 | 类型 | 说明 |
|----------|------|------|
| `player_id` | string | 玩家 Id |
| `name` | string | 角色名 |
| `level` | int | 等级 |
| `experience` | long | 当前经验 |
| `constitution` / `inner_power` / `agility` / `root_bone` / `comprehension` | int | 五维属性 |
| `free_points` | int | 可分配属性点 |
| `updated_at` | long | 最后更新时间戳（Unix 毫秒） |
| `version` | long | 状态版本号（乐观锁） |

**IInventoryGrain** — Key：`hundun:inv:{playerId}`（TTL 5 分钟）

| 字段 | 类型 | 说明 |
|----------|------|------|
| `capacity` | int | 背包容量 |
| `slots` | string | 格子数组（`InventorySlotDto[]` 的 MemoryPack 二进制经 Base64 编码） |
| `updated_at` | long | 最后更新时间戳 |
| `version` | long | 状态版本号 |

`slots` 字段为整体序列化，避免 Hash 字段过多导致的性能损耗。

**IEquipmentGrain** — Key：`hundun:equip:{playerId}`：`slots`（装备槽位数组，MemoryPack + Base64）、`updated_at`、`version`。

**ICraftingGrain** — Key：`hundun:craft:{playerId}`：`queue`（制造队列，MemoryPack + Base64）、`next_item_id`（队列项 Id 自增）、`updated_at`、`version`。

### 3.3 TTL 与回写策略

| 缓存键 | TTL | 回写触发条件 | 说明 |
|--------|-----|------------|------|
| `hundun:char:{playerId}` | 永久 | 每次 UpdateAttributes/升级 | 角色数据高频读，永久缓存 |
| `hundun:inv:{playerId}` | 5 分钟 | 每次物品操作 | 离线后短期保留，避免反复回源 |
| `hundun:equip:{playerId}` | 永久 | 每次 Equip/Enhance | 装备数据量小，永久缓存 |
| `hundun:craft:{playerId}` | 永久 | 每次队列变更 | 制造队列需长期保留 |

回写策略细节：

- **写穿透到 Redis**：每次 Grain 写操作完成后立即更新 Redis Hash 对应字段，保证缓存与内存一致。
- **写回到 SqlServer**：采用「定时 + 事件」双触发。定时：每 30 秒检查脏标记，有变更则回写；事件：关键操作（强化、制造完成、卸装备）立即回写；deactivate：失活前强制完整回写。

### 3.4 一致性保证

- **乐观锁**：每个状态携带 `version` 字段，每次更新自增。回写 SqlServer 时校验 version，冲突时重试（最多 3 次）。
- **操作日志**：所有写操作记录到 `operation_log` 表，用于审计与故障恢复。
- **回滚机制**：跨 Grain 调用失败时，主调 Grain 通过补偿事务回滚本地已生效的变更。

---

## 4. TouchSocket 消息协议

### 4.1 协议总览

TouchSocket 作为实时通信层，承载前端 UI 与后端 Grain 之间的双向消息。特性：

- 传输层：TCP 长连接（PC 客户端）+ WebSocket（Web/H5 客户端）。
- 序列化：MemoryPack 二进制（紧凑、高性能）。
- 消息模型：请求-响应（同步语义）+ 推送（异步通知）。
- 消息头：每条消息携带 `MessageType`（ushort，2 字节）+ `RequestId`（long，8 字节）+ `PlayerId`（long，8 字节）+ `Payload Length`（int，4 字节），后接 MemoryPack 序列化的消息体。

### 4.2 消息类型枚举

```csharp
namespace HundunWorld.Game.Message;

public enum CharacterInventoryMessageType : ushort
{
    // === 请求消息（0x0001 ~ 0x00FF）===
    GetCharacterReq       = 0x0001,   UpdateAttributesReq   = 0x0002,
    GetInventoryReq       = 0x0011,   MoveItemReq           = 0x0012,
    UseItemReq            = 0x0013,   DropItemReq           = 0x0014,
    SplitItemReq          = 0x0015,   SortInventoryReq      = 0x0016,
    GetEquipmentReq       = 0x0021,   EquipReq              = 0x0022,
    UnequipReq            = 0x0023,   EnhanceReq            = 0x0024,
    RepairReq             = 0x0025,
    GetCraftingReq        = 0x0031,   StartCraftingReq      = 0x0032,
    CancelCraftingReq     = 0x0033,   ClaimCraftResultReq   = 0x0034,
    GetAvailableRecipesReq= 0x0035,

    // === 响应消息（0x0100 ~ 0x01FF，与请求一一对应 +0x100）===
    GetCharacterResp      = 0x0101,   UpdateAttributesResp  = 0x0102,
    GetInventoryResp      = 0x0111,   MoveItemResp          = 0x0112,
    // ... 其余响应类推

    // === 推送消息（0x1000 ~ 0x10FF，服务端主动推送）===
    InventoryUpdatePush   = 0x1001,   // 背包变更推送
    EquipmentChangePush   = 0x1002,   // 装备变更推送
    CraftingProgressPush  = 0x1003,   // 制造进度推送
    AttributesChangePush  = 0x1004,   // 属性变更推送
    ItemObtainedPush      = 0x1005    // 获得物品提示推送
}
```

### 4.3 请求/响应消息体

请求消息体与第 2 节 Grain 方法参数一一对应，典型示例：

```csharp
[MemoryPackable]
public partial record MoveItemRequest(
    [property: MemoryPackOrder(0)] int FromIndex,
    [property: MemoryPackOrder(1)] int ToIndex,
    [property: MemoryPackOrder(2)] int Count);

[MemoryPackable]
public partial record EnhanceRequest(
    [property: MemoryPackOrder(0)] EquipmentSlot Slot,
    [property: MemoryPackOrder(1)] bool UseLuckyCharm,
    [property: MemoryPackOrder(2)] bool UseProtection);

[MemoryPackable]
public partial record StartCraftingRequest(
    [property: MemoryPackOrder(0)] int RecipeId,
    [property: MemoryPackOrder(1)] int Count,
    [property: MemoryPackOrder(2)] bool UseLuckyCharm);
```

响应消息体统一包装在 `GrainResult<T>` 中（`Success` / `Code` / `Message` / `Data`），与第 2.2 节结构一致，此处不再赘述。

### 4.4 推送消息体

推送消息由服务端在 Grain 状态变更后主动发送，前端无需请求即可接收。

**4.4.1 InventoryUpdatePush** — 背包发生任何变更时推送。为降低带宽，推送内容为「变更的格子集合」而非完整快照。

```csharp
[MemoryPackable]
public partial record InventoryUpdatePush(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] List<InventorySlotDto> ChangedSlots,  // 变更的格子
    [property: MemoryPackOrder(2)] int TotalCapacity,                    // 背包总容量
    [property: MemoryPackOrder(3)] long Timestamp);
```

**4.4.2 EquipmentChangePush** — 装备槽发生变更（穿戴、卸下、强化、修理）时推送。

```csharp
[MemoryPackable]
public partial record EquipmentChangePush(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] EquipmentSlot Slot,              // 变更的槽位
    [property: MemoryPackOrder(2)] EquipmentDto? Equipment,         // 变更后的装备（null 表示该槽已空）
    [property: MemoryPackOrder(3)] CharacterDto? CharacterSnapshot, // 属性快照（装备影响属性时携带）
    [property: MemoryPackOrder(4)] long Timestamp);
```

**4.4.3 CraftingProgressPush** — 制造进度变更时推送，前端据此更新进度条。推送频率：每秒 1 次进度更新，完成时立即推送最终结果。

```csharp
[MemoryPackable]
public partial record CraftingProgressPush(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] long QueueItemId,            // 队列项 Id
    [property: MemoryPackOrder(2)] int RecipeId,                // 配方 Id
    [property: MemoryPackOrder(3)] float Progress,              // 进度 0.0 ~ 1.0
    [property: MemoryPackOrder(4)] int RemainingSeconds,        // 剩余秒数
    [property: MemoryPackOrder(5)] CraftingItemStatus Status,   // 当前状态
    [property: MemoryPackOrder(6)] long Timestamp);

public enum CraftingItemStatus : byte
{
    Pending = 0, Crafting = 1, Success = 2, Failed = 3, Cancelled = 4, Claimed = 5
}
```

**4.4.4 AttributesChangePush** — 角色属性变更时推送（升级、加点、buff 变化），前端据此刷新 HUD。

```csharp
[MemoryPackable]
public partial record AttributesChangePush(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] CharacterDto Character,   // 最新角色快照
    [property: MemoryPackOrder(2)] string Reason,            // 变更原因
    [property: MemoryPackOrder(3)] long Timestamp);
```

### 4.5 错误码定义

| 错误码 | 含义 | 错误码 | 含义 |
|--------|------|--------|------|
| 0 | 成功 | 2003 | 背包已满 |
| 1001 | 参数无效 | 2004 | 强化材料不足 |
| 1002 | 格子索引越界 | 2005 | 强化已达上限（+15） |
| 1003 | 物品不存在 | 3001 | 配方不存在 |
| 1004 | 物品不可堆叠 | 3002 | 制造材料不足 |
| 1005 | 数量不足 | 3003 | 制造队列已满 |
| 1006 | 物品绑定（禁止丢弃） | 3004 | 队列项不可取消 |
| 1007 | 操作进行中（锁占用） | 9001 | 系统内部错误 |
| 2001 | 装备类型不匹配 | 9002 | 操作超时 |
| 2002 | 装备等级不足 | | |

---

## 5. MemoryPack 序列化 DTO

### 5.1 序列化约定

本子系统所有跨进程传输的数据结构均使用 MemoryPack 二进制序列化。约定：

- 所有 DTO 使用 `record` 类型，标注 `[MemoryPackable]` 和 `partial`。
- 字段顺序通过 `[property: MemoryPackOrder(n)]` 显式指定，从 0 开始连续递增。
- 字段顺序一经发布不可变更（仅可追加），以保证前后端二进制兼容性。
- 可空字段使用 `?` 标注；集合类型使用 `List<T>` 或数组；枚举显式指定底层类型（`int` 或 `byte`）。

### 5.2 CharacterDto

角色完整信息 DTO，用于 `GetCharacter` 返回与 `AttributesChangePush` 推送。

```csharp
using MemoryPack;

namespace HundunWorld.Game.Dtos;

[MemoryPackable]
public partial record CharacterDto(
    [property: MemoryPackOrder(0)] long PlayerId,            // 玩家 Id
    [property: MemoryPackOrder(1)] string Name,              // 角色名
    [property: MemoryPackOrder(2)] int Level,                // 等级
    [property: MemoryPackOrder(3)] long Experience,          // 当前经验
    [property: MemoryPackOrder(4)] long ExperienceToNext,    // 升级所需经验
    [property: MemoryPackOrder(5)] int Constitution,         // 体魄
    [property: MemoryPackOrder(6)] int InnerPower,           // 内功
    [property: MemoryPackOrder(7)] int Agility,              // 身法
    [property: MemoryPackOrder(8)] int RootBone,             // 根骨
    [property: MemoryPackOrder(9)] int Comprehension,        // 悟性
    [property: MemoryPackOrder(10)] int FreePoints,          // 可分配属性点
    [property: MemoryPackOrder(11)] long MaxHp,              // 生命上限（计算值）
    [property: MemoryPackOrder(12)] long MaxMp,              // 内力上限（计算值）
    [property: MemoryPackOrder(13)] int Attack,              // 攻击力（计算值）
    [property: MemoryPackOrder(14)] int Defense,             // 防御力（计算值）
    [property: MemoryPackOrder(15)] int HitRate,             // 命中（计算值）
    [property: MemoryPackOrder(16)] int DodgeRate,           // 闪避（计算值）
    [property: MemoryPackOrder(17)] int CritRate,            // 暴击率（计算值）
    [property: MemoryPackOrder(18)] int CarryCapacity,       // 负重上限
    [property: MemoryPackOrder(19)] int CurrentCarry,        // 当前负重
    [property: MemoryPackOrder(20)] long CreatedAt,          // 创建时间戳
    [property: MemoryPackOrder(21)] long LastLoginAt         // 最后登录时间戳
);
```

### 5.3 InventorySlotDto 与背包快照

背包格子 DTO 是背包数据的最小单元。

```csharp
[MemoryPackable]
public partial record InventorySlotDto(
    [property: MemoryPackOrder(0)] int Index,           // 格子索引（0 ~ capacity-1）
    [property: MemoryPackOrder(1)] int ItemId,          // 物品 Id（0 表示空格）
    [property: MemoryPackOrder(2)] int Count,           // 堆叠数量（0 表示空格）
    [property: MemoryPackOrder(3)] long InstanceId,     // 物品实例 Id（装备类唯一，材料类为 0）
    [property: MemoryPackOrder(4)] int Durability,      // 耐久度（仅装备有效，-1 表示无耐久概念）
    [property: MemoryPackOrder(5)] byte BindType,       // 绑定类型：0=不绑定 1=装备绑定 2=拾取绑定 3=账号绑定
    [property: MemoryPackOrder(6)] long ExpireAt,       // 过期时间戳（0 表示永不过期）
    [property: MemoryPackOrder(7)] int EnhanceLevel,    // 强化等级（仅装备，默认 0）
    [property: MemoryPackOrder(8)] string ExtraData     // 扩展数据（JSON，用于词条、宝石等，可空）
);

[MemoryPackable]
public partial record InventorySnapshotDto(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] int Capacity,                    // 背包容量
    [property: MemoryPackOrder(2)] List<InventorySlotDto> Slots,    // 全部格子（含空格）
    [property: MemoryPackOrder(3)] int UsedCount,                   // 已用格子数
    [property: MemoryPackOrder(4)] long Version,                    // 状态版本号
    [property: MemoryPackOrder(5)] long Timestamp                   // 快照时间戳
);
```

### 5.4 EquipmentDto 与装备相关 DTO

装备实例 DTO，包含装备的完整信息。

```csharp
[MemoryPackable]
public partial record EquipmentDto(
    [property: MemoryPackOrder(0)] long InstanceId,            // 装备实例唯一 Id
    [property: MemoryPackOrder(1)] int ItemId,                 // 装备基础 Id（关联静态配置）
    [property: MemoryPackOrder(2)] string Name,                // 装备名
    [property: MemoryPackOrder(3)] EquipmentSlot Slot,         // 当前所在槽位（背包中为 null）
    [property: MemoryPackOrder(4)] int ItemLevel,              // 物品等级
    [property: MemoryPackOrder(5)] int RequireLevel,           // 穿戴等级要求
    [property: MemoryPackOrder(6)] int EnhanceLevel,           // 强化等级 +0 ~ +15
    [property: MemoryPackOrder(7)] int Durability,             // 当前耐久
    [property: MemoryPackOrder(8)] int MaxDurability,          // 最大耐久
    [property: MemoryPackOrder(9)] int BaseAttack,             // 基础攻击
    [property: MemoryPackOrder(10)] int BaseDefense,           // 基础防御
    [property: MemoryPackOrder(11)] List<AttributeModifier> Modifiers,  // 词条列表
    [property: MemoryPackOrder(12)] List<int> SocketGems,      // 镶嵌宝石 Id 列表
    [property: MemoryPackOrder(13)] byte BindType,             // 绑定类型
    [property: MemoryPackOrder(14)] string ExtraData           // 扩展数据（JSON）
);

[MemoryPackable]
public partial record AttributeModifier(
    [property: MemoryPackOrder(0)] AttributeType Type,     // 词条类型
    [property: MemoryPackOrder(1)] int Value,              // 数值
    [property: MemoryPackOrder(2)] bool IsPercentage);     // 是否百分比

public enum AttributeType : byte
{
    Constitution = 0, InnerPower = 1, Agility = 2, RootBone = 3, Comprehension = 4,
    MaxHp = 5, MaxMp = 6, Attack = 7, Defense = 8,
    HitRate = 9, DodgeRate = 10, CritRate = 11, CritDamage = 12,
    FireResist = 13, IceResist = 14, PoisonResist = 15
}

[MemoryPackable]
public partial record EquipmentSnapshotDto(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] List<EquipmentSlotData> Slots,  // 11 个槽位数据
    [property: MemoryPackOrder(2)] long Version,
    [property: MemoryPackOrder(3)] long Timestamp);

[MemoryPackable]
public partial record EquipmentSlotData(
    [property: MemoryPackOrder(0)] EquipmentSlot Slot,
    [property: MemoryPackOrder(1)] EquipmentDto? Equipment);   // null 表示该槽为空
```

### 5.5 CraftingRecipeDto 与制造相关 DTO

制造配方 DTO 定义制造规则。

```csharp
[MemoryPackable]
public partial record CraftingRecipeDto(
    [property: MemoryPackOrder(0)] int RecipeId,                  // 配方 Id
    [property: MemoryPackOrder(1)] string Name,                   // 配方名
    [property: MemoryPackOrder(2)] string Description,            // 描述
    [property: MemoryPackOrder(3)] int OutputItemId,             // 产出物品 Id
    [property: MemoryPackOrder(4)] int OutputCount,              // 单次产出数量
    [property: MemoryPackOrder(5)] int RequireLevel,             // 制造等级要求
    [property: MemoryPackOrder(6)] int RequireComprehension,     // 所需悟性（影响成功率）
    [property: MemoryPackOrder(7)] int BaseDurationSeconds,      // 基础制造时长（秒）
    [property: MemoryPackOrder(8)] float BaseSuccessRate,        // 基础成功率 0.0 ~ 1.0
    [property: MemoryPackOrder(9)] List<CraftingMaterial> Materials,  // 所需材料
    [property: MemoryPackOrder(10)] int RequireToolId,           // 所需工具 Id（0 表示无需）
    [property: MemoryPackOrder(11)] int Category,                // 配方分类（锻造/炼药/制甲等）
    [property: MemoryPackOrder(12)] bool CanUseLuckyCharm);      // 是否允许使用幸运符

[MemoryPackable]
public partial record CraftingMaterial(
    [property: MemoryPackOrder(0)] int ItemId,        // 材料 Id
    [property: MemoryPackOrder(1)] int Count,         // 所需数量
    [property: MemoryPackOrder(2)] bool ConsumeOnFail); // 失败时是否消耗（默认 true）
```

### 5.6 CraftingQueueDto 与操作结果 DTO

制造队列 DTO 表示当前制造队列状态。

```csharp
[MemoryPackable]
public partial record CraftingQueueDto(
    [property: MemoryPackOrder(0)] long PlayerId,
    [property: MemoryPackOrder(1)] int MaxSlots,                      // 队列最大容量（默认 3）
    [property: MemoryPackOrder(2)] List<CraftingQueueItemDto> Items,  // 队列项列表
    [property: MemoryPackOrder(3)] long Version,
    [property: MemoryPackOrder(4)] long Timestamp);

[MemoryPackable]
public partial record CraftingQueueItemDto(
    [property: MemoryPackOrder(0)] long QueueItemId,            // 队列项唯一 Id
    [property: MemoryPackOrder(1)] int RecipeId,                // 配方 Id
    [property: MemoryPackOrder(2)] int OutputItemId,            // 产出物品 Id
    [property: MemoryPackOrder(3)] int OutputCount,             // 产出数量
    [property: MemoryPackOrder(4)] int TotalCount,              // 总制造数量
    [property: MemoryPackOrder(5)] int CompletedCount,          // 已完成数量
    [property: MemoryPackOrder(6)] float CurrentProgress,       // 当前项进度 0.0 ~ 1.0
    [property: MemoryPackOrder(7)] int RemainingSeconds,        // 剩余秒数
    [property: MemoryPackOrder(8)] CraftingItemStatus Status,   // 状态
    [property: MemoryPackOrder(9)] float SuccessRate,           // 实际成功率
    [property: MemoryPackOrder(10)] long StartTime,             // 开始时间戳
    [property: MemoryPackOrder(11)] long CompleteTime,          // 完成时间戳（未完成为 0）
    [property: MemoryPackOrder(12)] bool UseLuckyCharm);        // 是否使用了幸运符

// === 操作结果 DTO ===
[MemoryPackable]
public partial record UseItemResultDto(
    [property: MemoryPackOrder(0)] bool Success,
    [property: MemoryPackOrder(1)] int ConsumedCount,                    // 实际消耗数量
    [property: MemoryPackOrder(2)] List<EffectAppliedDto> Effects,       // 触发的效果
    [property: MemoryPackOrder(3)] InventorySnapshotDto? Inventory);

[MemoryPackable]
public partial record EffectAppliedDto(
    [property: MemoryPackOrder(0)] int EffectId,
    [property: MemoryPackOrder(1)] int Value,
    [property: MemoryPackOrder(2)] int DurationSeconds);

[MemoryPackable]
public partial record EquipResultDto(
    [property: MemoryPackOrder(0)] bool Success,
    [property: MemoryPackOrder(1)] EquipmentSlot EquippedSlot,           // 实际穿戴的槽位
    [property: MemoryPackOrder(2)] EquipmentDto? PreviousEquipment,      // 被替换下的装备（若有）
    [property: MemoryPackOrder(3)] CharacterDto? CharacterSnapshot,      // 属性变化后的快照
    [property: MemoryPackOrder(4)] InventorySnapshotDto? Inventory);

[MemoryPackable]
public partial record UnequipResultDto(
    [property: MemoryPackOrder(0)] bool Success,
    [property: MemoryPackOrder(1)] EquipmentSlot Slot,
    [property: MemoryPackOrder(2)] int TargetSlotIndex,                  // 放入背包的格子索引
    [property: MemoryPackOrder(3)] CharacterDto? CharacterSnapshot,
    [property: MemoryPackOrder(4)] InventorySnapshotDto? Inventory);

[MemoryPackable]
public partial record EnhanceResultDto(
    [property: MemoryPackOrder(0)] bool Success,                         // 是否强化成功
    [property: MemoryPackOrder(1)] int PreviousLevel,                    // 强化前等级
    [property: MemoryPackOrder(2)] int CurrentLevel,                     // 强化后等级
    [property: MemoryPackOrder(3)] bool Downgraded,                      // 是否降级
    [property: MemoryPackOrder(4)] int ConsumedSilver,                   // 消耗银两
    [property: MemoryPackOrder(5)] List<MaterialConsumedDto> ConsumedMaterials,
    [property: MemoryPackOrder(6)] EquipmentDto? Equipment,
    [property: MemoryPackOrder(7)] InventorySnapshotDto? Inventory);

[MemoryPackable]
public partial record MaterialConsumedDto(
    [property: MemoryPackOrder(0)] int ItemId,
    [property: MemoryPackOrder(1)] int Count);

[MemoryPackable]
public partial record ClaimCraftResultDto(
    [property: MemoryPackOrder(0)] bool Success,
    [property: MemoryPackOrder(1)] long QueueItemId,
    [property: MemoryPackOrder(2)] List<ObtainedItemDto> ObtainedItems,
    [property: MemoryPackOrder(3)] InventorySnapshotDto? Inventory);

[MemoryPackable]
public partial record ObtainedItemDto(
    [property: MemoryPackOrder(0)] int ItemId,
    [property: MemoryPackOrder(1)] int Count,
    [property: MemoryPackOrder(2)] long InstanceId);
```

---

## 6. EFCore 实体与 SqlServer 表结构

### 6.1 实体总览

本子系统使用 EFCore 进行 SqlServer 持久化，采用「Code-First」模式。实体属性用 PascalCase，数据库列名用 snake_case。

| 实体类 | 表名 | 说明 |
|--------|------|------|
| `Character` | `character` | 角色主表 |
| `InventoryItem` | `inventory_item` | 背包物品表 |
| `EquipmentInstance` | `equipment_instance` | 装备实例表 |
| `CraftingRecipe` | `crafting_recipe` | 制造配方表（静态配置） |
| `CraftingQueue` | `crafting_queue` | 制造队列表 |
| `OperationLog` | `operation_log` | 操作日志表 |

### 6.2 Character 实体

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HundunWorld.Game.Entities;

[Table("character")]
public class Character
{
    [Key][Column("player_id")] public long PlayerId { get; set; }
    [Required][MaxLength(32)][Column("name")] public string Name { get; set; } = string.Empty;
    [Column("level")] public int Level { get; set; } = 1;
    [Column("experience")] public long Experience { get; set; } = 0;

    // 五维属性
    [Column("constitution")] public int Constitution { get; set; } = 10;
    [Column("inner_power")] public int InnerPower { get; set; } = 10;
    [Column("agility")] public int Agility { get; set; } = 10;
    [Column("root_bone")] public int RootBone { get; set; } = 10;
    [Column("comprehension")] public int Comprehension { get; set; } = 10;
    [Column("free_points")] public int FreePoints { get; set; } = 0;

    [Column("carry_capacity")] public int CarryCapacity { get; set; } = 1000;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("last_login_at")] public DateTime? LastLoginAt { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Column("version")] public long Version { get; set; } = 0;   // 乐观锁版本号

    // 导航属性
    public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
    public virtual ICollection<EquipmentInstance> EquipmentInstances { get; set; } = new List<EquipmentInstance>();
    public virtual ICollection<CraftingQueue> CraftingQueues { get; set; } = new List<CraftingQueue>();
}
```

表 `character` 索引：主键 `player_id`（聚集）；唯一索引 `ux_character_name` on `name`（角色名全局唯一）；非聚集索引 `ix_character_last_login` on `last_login_at`（活跃玩家查询）。

### 6.3 InventoryItem 实体

```csharp
[Table("inventory_item")]
public class InventoryItem
{
    [Key][Column("id")] public long Id { get; set; }                 // 自增主键
    [Column("player_id")] public long PlayerId { get; set; }
    [Column("slot_index")] public int SlotIndex { get; set; }        // 格子索引
    [Column("item_id")] public int ItemId { get; set; }
    [Column("count")] public int Count { get; set; }
    [Column("instance_id")] public long InstanceId { get; set; }     // 装备实例 Id，材料为 0
    [Column("durability")] public int Durability { get; set; } = -1;
    [Column("bind_type")] public byte BindType { get; set; } = 0;
    [Column("expire_at")] public DateTime? ExpireAt { get; set; }    // null 表示永不过期
    [Column("enhance_level")] public int EnhanceLevel { get; set; } = 0;
    [Column("extra_data")] public string? ExtraData { get; set; }    // JSON
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PlayerId))]
    public virtual Character Character { get; set; } = null!;
}
```

表 `inventory_item` 索引：主键 `id`（聚集）；唯一索引 `ux_inventory_player_slot` on (`player_id`, `slot_index`)（同一玩家同一格子仅一条）；非聚集索引 `ix_inventory_player_item` on (`player_id`, `item_id`)（按物品查询数量）；非聚集索引 `ix_inventory_expire` on (`expire_at`)（过期物品清理）。

### 6.4 EquipmentInstance 实体

```csharp
[Table("equipment_instance")]
public class EquipmentInstance
{
    [Key][Column("instance_id")] public long InstanceId { get; set; }   // 雪花 Id，全局唯一
    [Column("player_id")] public long PlayerId { get; set; }
    [Column("item_id")] public int ItemId { get; set; }                 // 关联静态装备配置
    [Column("current_slot")] public int? CurrentSlot { get; set; }      // 当前所在槽位（null 表示在背包）
    [Column("inventory_slot_index")] public int? InventorySlotIndex { get; set; }  // 在背包中的格子索引
    [Column("enhance_level")] public int EnhanceLevel { get; set; } = 0;
    [Column("durability")] public int Durability { get; set; }
    [Column("max_durability")] public int MaxDurability { get; set; }
    [Column("modifiers")] public string Modifiers { get; set; } = "[]";     // 词条 JSON
    [Column("socket_gems")] public string SocketGems { get; set; } = "[]";  // 宝石 Id JSON
    [Column("bind_type")] public byte BindType { get; set; } = 0;
    [Column("extra_data")] public string? ExtraData { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PlayerId))]
    public virtual Character Character { get; set; } = null!;
}
```

表 `equipment_instance` 索引：主键 `instance_id`（聚集）；非聚集索引 `ix_equip_player` on (`player_id`)（查询玩家全部装备）；非聚集索引 `ix_equip_player_slot` on (`player_id`, `current_slot`)（查询某槽位装备，过滤 null）。

### 6.5 CraftingRecipe 实体

```csharp
[Table("crafting_recipe")]
public class CraftingRecipe
{
    [Key][Column("recipe_id")] public int RecipeId { get; set; }
    [Required][MaxLength(64)][Column("name")] public string Name { get; set; } = string.Empty;
    [Column("description")] public string Description { get; set; } = string.Empty;
    [Column("output_item_id")] public int OutputItemId { get; set; }
    [Column("output_count")] public int OutputCount { get; set; } = 1;
    [Column("require_level")] public int RequireLevel { get; set; } = 1;
    [Column("require_comprehension")] public int RequireComprehension { get; set; } = 0;
    [Column("base_duration_seconds")] public int BaseDurationSeconds { get; set; }
    [Column("base_success_rate")] public float BaseSuccessRate { get; set; } = 0.9f;
    [Column("materials")] public string Materials { get; set; } = "[]";     // 材料 JSON
    [Column("require_tool_id")] public int RequireToolId { get; set; } = 0;
    [Column("category")] public int Category { get; set; }
    [Column("can_use_lucky_charm")] public bool CanUseLuckyCharm { get; set; } = true;
    [Column("enabled")] public bool Enabled { get; set; } = true;
}
```

表 `crafting_recipe` 索引：主键 `recipe_id`（聚集）；非聚集索引 `ix_recipe_category` on (`category`, `enabled`)（按分类查询可用配方）。

### 6.6 CraftingQueue 实体

```csharp
[Table("crafting_queue")]
public class CraftingQueue
{
    [Key][Column("queue_item_id")] public long QueueItemId { get; set; }   // 雪花 Id
    [Column("player_id")] public long PlayerId { get; set; }
    [Column("recipe_id")] public int RecipeId { get; set; }
    [Column("output_item_id")] public int OutputItemId { get; set; }
    [Column("output_count")] public int OutputCount { get; set; }
    [Column("total_count")] public int TotalCount { get; set; }
    [Column("completed_count")] public int CompletedCount { get; set; } = 0;
    [Column("current_progress")] public float CurrentProgress { get; set; } = 0f;
    [Column("remaining_seconds")] public int RemainingSeconds { get; set; }
    [Column("status")] public byte Status { get; set; } = 0;               // 对应 CraftingItemStatus
    [Column("success_rate")] public float SuccessRate { get; set; }
    [Column("use_lucky_charm")] public bool UseLuckyCharm { get; set; }
    [Column("start_time")] public DateTime? StartTime { get; set; }
    [Column("complete_time")] public DateTime? CompleteTime { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PlayerId))] public virtual Character Character { get; set; } = null!;
    [ForeignKey(nameof(RecipeId))] public virtual CraftingRecipe Recipe { get; set; } = null!;
}
```

表 `crafting_queue` 索引：主键 `queue_item_id`（聚集）；非聚集索引 `ix_craft_player_status` on (`player_id`, `status`)（查询玩家进行中的队列）；非聚集索引 `ix_craft_complete_time` on (`complete_time`)（定时任务扫描已完成项）。

### 6.7 关系与约束

实体间关系：

- `Character` 1 : N `InventoryItem`（一个角色拥有多条背包物品记录）
- `Character` 1 : N `EquipmentInstance`（一个角色拥有多件装备实例）
- `Character` 1 : N `CraftingQueue`（一个角色拥有多个制造队列项）
- `CraftingRecipe` 1 : N `CraftingQueue`（一个配方可被多次制造）

数据库级约束：

- `inventory_item` 表对 (`player_id`, `slot_index`) 建立唯一约束，保证格子不重复占用。
- `equipment_instance` 表的 `current_slot` 与 `inventory_slot_index` 互斥（同一装备要么在槽位要么在背包），由应用层保证。
- 所有外键关系配置为 `ON DELETE CASCADE`（删除角色时级联清理相关数据，仅用于账号注销场景）。

### 6.8 EFCore DbContext 配置（节选）

```csharp
using Microsoft.EntityFrameworkCore;

namespace HundunWorld.Game.Entities;

public class GameDbContext : DbContext
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<EquipmentInstance> EquipmentInstances => Set<EquipmentInstance>();
    public DbSet<CraftingRecipe> CraftingRecipes => Set<CraftingRecipe>();
    public DbSet<CraftingQueue> CraftingQueues => Set<CraftingQueue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>(e =>
        {
            e.HasIndex(c => c.Name).IsUnique();
            e.HasIndex(c => c.LastLoginAt);
            e.Property(c => c.Version).IsRowVersion();   // 乐观锁
        });

        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.HasIndex(i => new { i.PlayerId, i.SlotIndex }).IsUnique();
            e.HasIndex(i => new { i.PlayerId, i.ItemId });
            e.HasIndex(i => i.ExpireAt);
            e.HasOne(i => i.Character).WithMany(c => c.InventoryItems)
             .HasForeignKey(i => i.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EquipmentInstance>(e =>
        {
            e.HasIndex(i => i.PlayerId);
            e.HasIndex(i => new { i.PlayerId, i.CurrentSlot });
            e.HasOne(i => i.Character).WithMany(c => c.EquipmentInstances)
             .HasForeignKey(i => i.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CraftingQueue>(e =>
        {
            e.HasIndex(q => new { q.PlayerId, q.Status });
            e.HasIndex(q => q.CompleteTime);
            e.HasOne(q => q.Character).WithMany(c => c.CraftingQueues)
             .HasForeignKey(q => q.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(q => q.Recipe).WithMany().HasForeignKey(q => q.RecipeId);
        });
    }
}
```

---

## 7. Redis 缓存键命名规范

### 7.1 命名约定

本子系统所有 Redis 键遵循统一命名规范，确保可读性、可运维性与避免冲突。

命名规则：`hundun:{子系统}:{playerId}[:{子资源}][:{标识}]`

- 前缀 `hundun`：项目标识，避免与其他项目共用 Redis 时冲突。
- 子系统段：`char`（角色）、`inv`（背包）、`equip`（装备）、`craft`（制造）。
- 分隔符：英文冒号 `:`；大小写：全小写；playerId 段为长整型字符串，不带前导零。

### 7.2 键列表

| 键模式 | 类型 | TTL | 说明 |
|--------|------|-----|------|
| `hundun:char:{playerId}` | Hash | 永久 | 角色完整状态（见 3.2） |
| `hundun:inv:{playerId}` | Hash | 5 分钟 | 背包状态（见 3.2） |
| `hundun:equip:{playerId}` | Hash | 永久 | 装备槽状态（见 3.2） |
| `hundun:craft:{playerId}` | Hash | 永久 | 制造队列状态（见 3.2） |
| `hundun:craft:{playerId}:queue` | List | 永久 | 制造队列项 Id 列表（按完成时间排序） |
| `hundun:inv:{playerId}:lock` | String | 10 秒 | 背包操作分布式锁（整理等长操作） |
| `hundun:char:{playerId}:online` | String | 60 秒 | 在线状态标记（心跳续期） |
| `hundun:cooldown:{playerId}:item:{itemId}` | String | 动态 | 物品使用冷却（TTL = 冷却时长） |
| `hundun:cooldown:{playerId}:enhance` | String | 5 秒 | 强化操作冷却（防刷） |

### 7.3 命名示例

```
# 角色 10086 的相关键
hundun:char:10086                       # 角色状态 Hash
hundun:inv:10086                        # 背包状态 Hash（5 分钟过期）
hundun:equip:10086                      # 装备槽状态 Hash
hundun:craft:10086                      # 制造队列状态 Hash
hundun:craft:10086:queue                # 制造队列项 Id 列表
hundun:inv:10086:lock                   # 背包操作锁
hundun:char:10086:online                # 在线标记
hundun:cooldown:10086:item:5012         # 物品 5012 使用冷却
hundun:cooldown:10086:enhance           # 强化冷却
```

### 7.4 过期与清理策略

- **TTL 管理**：背包键 5 分钟自动过期，玩家再次操作时回源 SqlServer 并重建缓存。
- **主动失效**：角色注销时主动删除 `online` 标记；背包整理完成时删除 `lock` 键。
- **冷却键**：由业务代码在首次使用/强化时设置，TTL 即为冷却时长，到期自动清理。
- **过期物品清理**：定时任务（每 5 分钟）扫描 `inventory_item` 表的 `expire_at` 字段，清理过期物品并推送 `InventoryUpdatePush`。

### 7.5 分布式锁使用规范

背包整理等长耗时操作需获取分布式锁，避免并发导致状态错乱。流程：

1. 尝试 `SET hundun:inv:{playerId}:lock {requestId} NX EX 10`。
2. 获取成功则执行操作；失败则返回错误码 1007「操作进行中，请稍后」。
3. 操作完成后通过 Lua 脚本校验 `requestId` 后删除锁，避免误删他人持有的锁。

```csharp
// 加锁示例（仅文档化）
var requestId = Guid.NewGuid().ToString("N");
var acquired = await redis.StringSetAsync(
    $"hundun:inv:{playerId}:lock", requestId,
    TimeSpan.FromSeconds(10), When.NotExists);

if (!acquired) return GrainResult<InventorySnapshotDto>.Fail(1007, "背包操作进行中，请稍后");

try { /* 执行整理逻辑 */ }
finally
{
    // Lua 脚本释放锁（校验 requestId，避免误删）
    var script = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
    await redis.ScriptEvaluateAsync(script,
        new RedisKey[] { $"hundun:inv:{playerId}:lock" },
        new RedisValue[] { requestId });
}
```

---

## 8. 前端调用时序图

本节以文字步骤描述典型业务场景的前后端交互时序，涵盖 UI 事件、TouchSocket 消息、Grain 调用、Redis/SqlServer 读写与响应推送的完整链路。

### 8.1 物品移动时序

场景：玩家在背包界面将 A 格的物品拖动到 B 格。

```
1. [前端 UI] 玩家拖拽 A 格物品到 B 格，UI 触发 MoveItemReq 事件
2. [前端] 构造 MoveItemRequest{FromIndex=A, ToIndex=B, Count=0}，通过 TouchSocket 发送（MessageType=0x0012）
3. [Gateway] 解析消息头获取 PlayerId，获取 IInventoryGrain 引用并调用 MoveItem(A, B, 0)
4. [IInventoryGrain] 检查背包操作锁（hundun:inv:{playerId}:lock），从内存读取背包状态，校验 A 格非空、B 格索引合法、堆叠规则匹配
5. [IInventoryGrain] 执行移动/合并逻辑，更新内存 slots 数组
6. [IInventoryGrain] 异步写 Redis（HSET slots/version）并标记脏数据等待定时回写 SqlServer
7. [IInventoryGrain] 返回 GrainResult<InventorySnapshotDto> 给 Gateway
8. [Gateway] 构造 MoveItemResp 消息，通过 TouchSocket 回送前端
9. [IInventoryGrain] 异步触发推送 InventoryUpdatePush{ChangedSlots=[A,B]}，由 Gateway 推送到该 PlayerId 的活跃连接
10. [前端] 收到响应与推送，更新本地背包 UI（A 格清空或减少，B 格填充或增加）
```

### 8.2 装备穿戴时序

场景：玩家在背包中右键点击一件装备进行穿戴。

```
1. [前端 UI] 玩家右键装备，UI 触发 EquipReq 事件
2. [前端] 构造 EquipRequest{SlotIndex=A, TargetSlot=null}，通过 TouchSocket 发送（MessageType=0x0022）
3. [Gateway] 解析消息，获取 IEquipmentGrain 引用，调用 Equip(A, null)
4. [IEquipmentGrain] 跨 Grain 调用 IInventoryGrain.GetInventory() 读取 A 格装备信息，校验类型与槽位匹配、等级达标、绑定规则，确定目标槽位
5. [IEquipmentGrain] 检查目标槽位是否有旧装备：有则先调用 IInventoryGrain.MoveItem 将旧装备放回背包，无则直接进入下一步
6. [IEquipmentGrain] 跨 Grain 调用 IInventoryGrain.RemoveItem(equipItemId, 1, "equip") 清空背包 A 格
7. [IEquipmentGrain] 更新装备槽内存状态，将装备实例写入对应槽位，重新计算角色属性（基础 + 装备加成）
8. [IEquipmentGrain] 异步写 Redis（hundun:equip slots、hundun:char 各属性），立即回写 SqlServer（关键操作）
9. [IEquipmentGrain] 返回 GrainResult<EquipResultDto>（含角色快照、背包快照）
10. [Gateway] 回送 EquipResp 给前端
11. [IEquipmentGrain] 异步触发推送：EquipmentChangePush{Slot=目标槽, Equipment=新装备, CharacterSnapshot} 与 InventoryUpdatePush{ChangedSlots=[A格]}
12. [前端] 收到响应与推送，更新背包 UI（A 格清空）、装备槽 UI、角色属性 HUD
```

### 8.3 装备强化时序

场景：玩家在装备强化界面强化武器。

```
1. [前端 UI] 玩家选择武器槽，勾选「使用幸运符」「使用保底材料」，点击「强化」
2. [前端] 发送 EnhanceRequest{Slot=Weapon, UseLuckyCharm=true, UseProtection=true}（MessageType=0x0024）
3. [Gateway] 获取 IEquipmentGrain 引用，调用 Enhance(Weapon, true, true)
4. [IEquipmentGrain] 检查强化冷却键 hundun:cooldown:{playerId}:enhance（若存在则拒绝），读取武器槽装备实例校验强化等级 < 15，读取强化配方确定所需材料与银两
5. [IEquipmentGrain] 跨 Grain 调用 IInventoryGrain.QueryItemCount 校验材料充足后调用 RemoveItem 扣除材料与幸运符（失败则整体回滚返回 2004），并扣除银两
6. [IEquipmentGrain] 设置强化冷却（SET ... EX 5），计算实际成功率（基础 + 幸运符 + 悟性加成）
7. [IEquipmentGrain] 执行随机判定：成功则等级 +1；失败且 +10 以上且未用保底则等级 -1；失败且用了保底或 +9 以下则等级不变
8. [IEquipmentGrain] 更新装备实例内存状态（enhance_level、durability），重新计算装备与角色属性
9. [IEquipmentGrain] 写 Redis（装备 + 角色），立即回写 SqlServer（关键操作）
10. [IEquipmentGrain] 返回 GrainResult<EnhanceResultDto>（含成败、前后等级、消耗），Gateway 回送 EnhanceResp
11. [IEquipmentGrain] 异步推送：EquipmentChangePush{Slot=Weapon, Equipment=强化后装备, CharacterSnapshot}、InventoryUpdatePush{ChangedSlots=[材料格变更]}、AttributesChangePush（属性变化时）
12. [前端] 播放强化动画，根据成败显示不同特效，刷新装备槽与属性 HUD
```

### 8.4 制造流程时序

场景：玩家在制造界面选择配方，制造 5 件「精铁剑」。

```
1. [前端 UI] 玩家选择配方，设置数量=5，点击「制造」
2. [前端] 发送 StartCraftingRequest{RecipeId=1001, Count=5, UseLuckyCharm=false}（MessageType=0x0032）
3. [Gateway] 获取 ICraftingGrain 引用，调用 StartCrafting(1001, 5, false)
4. [ICraftingGrain] 读取制造队列状态，校验队列未满（< 3 项）
5. [ICraftingGrain] 读取配方配置（CraftingRecipe），校验配方启用、等级达标
6. [ICraftingGrain] 跨 Grain 调用 IInventoryGrain.QueryItemCount 校验所有材料充足
7. [ICraftingGrain] 跨 Grain 调用 IInventoryGrain.RemoveItem 扣除全部材料
   （扣除失败则回滚，返回 3002）
8. [ICraftingGrain] 计算实际成功率（基础 + 悟性/100 + 幸运符加成）
9. [ICraftingGrain] 创建队列项 CraftingQueueItem，状态=Pending，总数量=5
10. [ICraftingGrain] 启动 Orleans Timer（每秒触发一次进度更新）
11. [ICraftingGrain] 写 Redis：HSET hundun:craft:{playerId} queue，RPUSH hundun:craft:{playerId}:queue <queueItemId>
12. [ICraftingGrain] 立即回写 SqlServer（制造启动需持久化，防宕机丢失）
13. [ICraftingGrain] 返回 GrainResult<CraftingQueueDto>（含新队列项）
14. [Gateway] 回送 StartCraftingResp
15. [ICraftingGrain] Timer 每秒触发：
    a. 更新进度（CurrentProgress += 1/BaseDurationSeconds）
    b. 计算剩余秒数
    c. 推送 CraftingProgressPush{Progress, RemainingSeconds, Status=Crafting}
16. [前端] 收到进度推送，更新进度条
17. [Timer] 当进度达到 1.0：
    a. 执行成功率判定（每件独立判定）
    b. 成功：CompletedCount++，产出物品暂存队列项（不入背包，待领取）
    c. 失败：CompletedCount++，无产出（材料已扣）
    d. 若 CompletedCount < TotalCount：开始下一件制造
    e. 若 CompletedCount == TotalCount：状态置为 Success/Failed，停止 Timer
18. [ICraftingGrain] 完成时推送 CraftingProgressPush{Progress=1.0, Status=Success}
19. [前端] 显示「制造完成，请领取」
20. [前端 UI] 玩家点击「领取」，发送 ClaimCraftResultReq{QueueItemId}
21. [ICraftingGrain] 校验状态为 Success，跨 Grain 调用 IInventoryGrain.AddItem 产出入包
22. [ICraftingGrain] 状态置为 Claimed，从队列移除
23. [ICraftingGrain] 返回 ClaimCraftResultDto，推送 InventoryUpdatePush
24. [前端] 刷新背包，显示获得物品提示
```

### 8.5 背包整理时序

场景：玩家点击背包界面的「整理」按钮。

```
1. [前端 UI] 玩家点击「整理」按钮，UI 显示加载状态
2. [前端] 发送 SortInventoryReq（MessageType=0x0016）
3. [Gateway] 获取 IInventoryGrain 引用，调用 SortInventory()
4. [IInventoryGrain] 尝试获取分布式锁 hundun:inv:{playerId}:lock（SET NX EX 10）
5. [IInventoryGrain] 若锁获取失败：返回错误码 1007「整理进行中」
6. [IInventoryGrain] 锁获取成功，读取完整 slots 数组
7. [IInventoryGrain] 执行整理算法：
   a. 过滤出非空格子
   b. 按 item_id 升序排序
   c. 合并可堆叠物品（同 itemId 且未达堆叠上限）
   d. 紧凑排列到前端索引（从 0 开始连续）
8. [IInventoryGrain] 生成新 slots 数组，与旧状态 diff 得到 ChangedSlots
9. [IInventoryGrain] 更新内存状态，写 Redis（slots + version）
10. [IInventoryGrain] 立即回写 SqlServer（整理为批量变更，需持久化）
11. [IInventoryGrain] 释放分布式锁（Lua 脚本校验 requestId 后 DEL）
12. [IInventoryGrain] 返回 GrainResult<InventorySnapshotDto>
13. [Gateway] 回送 SortInventoryResp
14. [IInventoryGrain] 推送 InventoryUpdatePush{ChangedSlots=全部变更格}
15. [前端] 整体刷新背包 UI，关闭加载状态
```

### 8.6 异常与超时处理

所有时序中均包含异常处理分支：

- **Grain 调用超时**（>5 秒）：Gateway 返回错误码 9002，前端提示「网络异常，请重试」并主动拉取一次 `GetInventoryReq` 同步状态；**跨 Grain 调用失败**时主调 Grain 通过补偿事务回滚已生效变更，返回对应错误码。
- **推送消息丢失**：前端维护本地状态版本号，收到推送时校验 version 连续性，发现版本跳跃（如收到 v5 但本地为 v3）时主动发起 `GetInventoryReq` 全量同步；**客户端断线重连**后必须先发送 `GetCharacterReq` + `GetInventoryReq` + `GetEquipmentReq` + `GetCraftingReq` 全量拉取最新状态，再处理后续推送。

---

## 附录：术语表

| 术语 | 说明 | 术语 | 说明 |
|------|------|------|------|
| Grain | Orleans 虚拟 Actor，单线程执行，状态隔离 | 制造队列 | 玩家进行中的制造任务列表，最多 3 项并行 |
| GrainKey | Grain 实例唯一标识，本子系统用 playerId 字符串 | 乐观锁 | 基于版本号的并发控制，冲突时重试 |
| TouchSocket | 实时通信框架，支持 TCP/WebSocket，承载请求-响应与推送 | 写穿透 | 写操作同步更新缓存，保证缓存与内存一致 |
| MemoryPack | 高性能二进制序列化库，零分配，用于消息体与状态序列化 | 写回 | 写操作异步更新数据库，由定时或事件触发 |
| 五维属性 | 体魄/内功/身法/根骨/悟性，角色成长五大维度 | 装备槽 | 角色可穿戴装备的位置，共 10 类 11 个槽位 |

---

## 修订历史

| 版本 | 日期 | 变更说明 |
|------|------|---------|
| v1.0.0 | 2026-07-19 | 初始版本，定义角色与背包子系统完整接口契约 |

---

*文档结束*

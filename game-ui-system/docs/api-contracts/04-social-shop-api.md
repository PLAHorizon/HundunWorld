# 混沌世界 MMORPG 社交与商城子系统后端接口契约文档

> 文档版本：v1.0
> 技术栈：.NET 10 C# + Orleans（Grain actor 模型）+ SqlServer（EFCore 持久化）+ Redis（缓存）+ TouchSocket（TCP/WebSocket 实时通信）+ MemoryPack（二进制序列化）
> 适用模块：门派、好友、邮件、师徒、排行榜、成就、商城、副本入口

**目录**：1. 子系统概述与领域边界 | 2. Orleans Grain 接口定义 | 3. Grain 状态与持久化 | 4. TouchSocket 消息协议 | 5. MemoryPack 序列化 DTO | 6. EFCore 实体与 SqlServer 表结构 | 7. Redis 缓存键命名规范 | 8. 前端调用时序图

---

## 1. 子系统概述与领域边界

### 1.1 子系统定位

社交与商城子系统是混沌世界 MMORPG 中连接玩家社交关系、经济流通与 PvE 入口的核心中间层。向上承接 UI 层（TouchSocket 消息与 HTTP 接口），向下调用角色 Grain、背包 Grain、战斗 Grain，横向与任务系统、经济系统、竞技场系统交互。本子系统遵循"Grain 即领域边界"原则，每个子模块由独立 Grain 承载，Grain 之间通过显式接口调用通信，不共享状态。

### 1.2 子模块领域边界

| 子模块 | 领域职责 | 主要 Grain | 关键交互方 |
| --- | --- | --- | --- |
| 门派 | 创建/加入/退出/管理（职位、贡献、申请审批） | `IGuildGrain` | 角色 Grain（贡献度）、邮件 Grain（通知） |
| 好友 | 添加/删除/分组/在线状态推送 | `IFriendGrain` | 会话 Grain（在线状态）、邮件 Grain |
| 邮件 | 收发、附件领取、批量操作、过期清理 | `IMailGrain` | 背包 Grain（附件发放）、经济系统 |
| 师徒 | 拜师/出师/贡献、师徒任务 | `IMentorGrain` | 角色 Grain（等级校验）、成就 Grain |
| 排行榜 | 战力/等级/门派/竞技场实时排名 | `ILeaderboardGrain` | 角色 Grain（分数上报）、竞技场 |
| 成就 | 分类、进度跟踪、奖励领取 | `IAchievementGrain` | 任务系统（事件触发）、邮件 Grain |
| 商城 | 购买、多货币、限购、促销活动 | `IShopGrain` | 经济系统（扣费）、背包 Grain |
| 副本入口 | 匹配、进入、奖励结算 | `IDungeonGrain` | 战斗 Grain（副本战斗）、匹配池 |

### 1.3 领域边界约束

- **门派**：玩家同一时间仅属一个门派；上限 200 人；职位 5 级（帮主/副帮主/长老/精英/成员）。
- **好友**：单向好友关系；上限 500；支持黑名单与分组（最多 20 组）。
- **邮件**：邮箱容量 100 封；系统邮件 30 天过期；附件领取后邮件保留 7 天。
- **师徒**：师傅等级 ≥ 60，徒弟等级 ≤ 40；师傅同时最多带 3 名徒弟；出师后不可再拜师。
- **排行榜**：四类独立榜单；每 10 秒刷新快照；Top 1000 进入 Sorted Set 缓存。
- **成就**：分类含战斗/探索/社交/经济/成长；进度持久化；奖励仅可领取一次。
- **商城**：支持金币/钻石/绑定点券三货币；限购按账号/角色维度计数；促销含限时折扣与礼包。
- **副本入口**：单人/组队/门派三种匹配模式；组队上限 5 人；匹配超时 120 秒回退单人。

### 1.4 跨子系统调用约束

社交/商城 Grain 向下调用：角色 Grain（`IGameGrain`：等级、战力、货币校验）、背包 Grain（`IBagGrain`：物品发放、道具消耗）、战斗 Grain（`ICombatGrain`：副本战斗、伤害结算）、任务 Grain（`IQuestGrain`：事件触发、任务进度）、经济系统（`IEconomyGrain`：扣费、退款、流水记录）。

所有跨 Grain 调用必须通过接口方法，禁止直接读写对方持久化状态。涉及货币与物品变更的操作必须采用两阶段确认（预扣 → 发放 → 确认/回滚）。

## 2. Orleans Grain 接口定义

> 命名空间统一为 `Horizon.Orleans.Interface`，所有接口继承 `IGrainWithStringKey`（Guid 转 Key 字符串，便于 Redis 缓存键复用）。返回类型为 `record`，错误码定义见附录 A。

### 2.1 IGuildGrain 门派接口

**GrainKey**：门派 Guid 字符串。门派创建时由系统生成 `Guid.NewGuid()`。

```csharp
public interface IGuildGrain : IGrainWithStringKey {
    Task<GuildResult> CreateGuildAsync(string guildName, ulong creatorId);   // 创建门派（≤32 字符）
    Task<bool> JoinGuildAsync(ulong playerId, string inviteCode);            // 加入门派（邀请场景）
    Task<bool> LeaveGuildAsync(ulong memberId);                              // 离开门派（帮主需先转让）
    Task<GuildMemberPage> GetMembersAsync(int offset, int limit);            // 获取成员列表（分页）
    Task<bool> KickMemberAsync(ulong operatorId, ulong targetId);            // 踢出成员（权限校验）
    Task<GuildDto> GetGuildInfoAsync();                                       // 获取门派信息
    Task<bool> AppointPositionAsync(ulong operatorId, ulong targetId, int position); // 任命职位
    Task<bool> AddContributionAsync(ulong memberId, int amount);             // 更新贡献度
}
public record GuildResult(bool Success, Guid GuildId, int ErrorCode, string Message);
public record GuildMemberPage(IReadOnlyList<GuildMemberDto> Members, int Total, int Offset, int Limit);
```

### 2.2 IFriendGrain 好友接口

**GrainKey**：玩家角色 Guid 字符串。Grain 内部维护该玩家的好友列表。

```csharp
public interface IFriendGrain : IGrainWithStringKey {
    Task<bool> AddFriendAsync(ulong targetPlayerId, string remark, int groupId);        // 添加好友（双向）
    Task<bool> RemoveFriendAsync(ulong targetPlayerId);                                  // 删除好友（双向）
    Task<List<FriendDto>> GetFriendsAsync(int groupId = -1);                             // 获取好友列表
    Task<Dictionary<ulong, bool>> GetOnlineStatusAsync(IReadOnlyList<ulong> friendIds);  // 批量在线状态
    Task<bool> MoveToGroupAsync(ulong friendId, int targetGroupId);                      // 移动至分组
    Task<bool> SetBlockAsync(ulong targetPlayerId, bool blocked);                        // 拉黑/取消
    Task OnPlayerOnlineStatusChangedAsync(ulong playerId, bool online);                  // 上下线回调
}
```

### 2.3 IMailGrain 邮件接口

**GrainKey**：收件人角色 Guid 字符串。每个玩家独立邮箱 Grain。

```csharp
public interface IMailGrain : IGrainWithStringKey {
    Task<MailSendResult> SendMailAsync(MailSendRequest request);                          // 发送邮件
    Task<MailPage> GetMailsAsync(int offset, int limit, MailFilter filter);               // 邮件列表（分页）
    Task<MailDto> ReadMailAsync(Guid mailId);                                             // 读取邮件（标记已读）
    Task<bool> DeleteMailAsync(Guid mailId);                                              // 删除邮件
    Task<int> DeleteMailsAsync(IReadOnlyList<Guid> mailIds);                              // 批量删除
    Task<ClaimResult> ClaimAttachmentAsync(Guid mailId);                                  // 领取附件
    Task<ClaimResult> ClaimAllAttachmentsAsync();                                         // 一键领取
    Task<int> GetUnreadCountAsync();                                                      // 未读数量
}
public record MailSendRequest(ulong SenderId, ulong ReceiverId, string Title, string Content,
    MailAttachment Attachment, MailType Type, int ExpireDays);
public record MailSendResult(bool Success, Guid MailId, string Message);
public record MailPage(IReadOnlyList<MailDto> Mails, int Total, int Offset, int Limit);
public record ClaimResult(bool Success, IReadOnlyList<string> ClaimedItems, string Message);
public record MailFilter(MailType? Type, bool? OnlyUnread, bool? OnlyWithAttachment);
```

### 2.4 IMentorGrain 师徒接口

**GrainKey**：师傅角色 Guid 字符串。以师傅维度组织师徒关系。

```csharp
public interface IMentorGrain : IGrainWithStringKey {
    Task<bool> ApprenticeAsync(ulong masterId, ulong apprenticeId);                       // 拜师
    Task<GraduateResult> GraduateAsync(ulong apprenticeId);                               // 出师（结算奖励）
    Task<bool> ContributeAsync(ulong apprenticeId, int contribution, MentorTaskType type); // 贡献度上报
    Task<List<MentorDto>> GetApprenticesAsync();                                          // 徒弟列表
    Task<MentorDto> GetMentorRelationAsync(ulong apprenticeId);                           // 反向查询
    Task<bool> BreakRelationAsync(ulong apprenticeId, int reasonCode);                    // 强制解除
}
public record GraduateResult(bool Success, int MasterReward, int ApprenticeReward, string Message);
```

### 2.5 ILeaderboardGrain 排行榜接口

**GrainKey**：榜单类型字符串（`"CombatPower"`/`"Level"`/`"Guild"`/`"Arena"`）。全局唯一榜单实例。

```csharp
public interface ILeaderboardGrain : IGrainWithStringKey {
    Task<List<LeaderboardEntryDto>> GetRankingAsync(int topN, int offset = 0);             // 获取排行榜
    Task<LeaderboardEntryDto> GetPlayerRankAsync(ulong playerId);                          // 玩家自身排名
    Task<bool> UpdateScoreAsync(ulong playerId, long score, bool isDelta = false);         // 更新分数
    Task<bool> BatchUpdateScoresAsync(IReadOnlyList<(ulong PlayerId, long Score)> updates); // 批量更新
    Task<bool> RemovePlayerAsync(ulong playerId);                                          // 移除玩家
}
```

### 2.6 IAchievementGrain 成就接口

**GrainKey**：玩家角色 Guid 字符串。每个玩家独立成就进度。

```csharp
public interface IAchievementGrain : IGrainWithStringKey {
    Task<List<AchievementDto>> GetAchievementsAsync(AchievementCategory? category = null); // 获取成就列表
    Task<bool> UnlockAsync(ulong playerId, int achievementId, int progress);               // 解锁/推进成就
    Task<ClaimResult> ClaimRewardAsync(int achievementId);                                 // 领取奖励
    Task<bool> BatchProgressAsync(IReadOnlyList<AchievementEvent> events);                 // 批量事件推进
}
public record AchievementEvent(int AchievementId, int ProgressDelta, ulong TriggerPlayerId);
```

### 2.7 IShopGrain 商城接口

**GrainKey**：商城实例标识字符串（`"GlobalShop"`/`"GuildShop:guid"`/`"ArenaShop"`）。

```csharp
public interface IShopGrain : IGrainWithStringKey {
    Task<List<ShopItemDto>> GetItemsAsync(ShopCategory? category = null);                  // 商品列表
    Task<ShopOrderDto> PurchaseAsync(ShopPurchaseRequest request);                         // 购买商品
    Task<List<ShopOrderDto>> GetHistoryAsync(ulong playerId, int offset, int limit);       // 购买历史
    Task<Dictionary<int, int>> GetRemainingLimitsAsync(ulong playerId, IReadOnlyList<int> itemIds); // 限购剩余
    Task<bool> RefreshPromotionAsync(PromotionConfig config);                              // 刷新促销
}
public record ShopPurchaseRequest(ulong PlayerId, int ItemId, int Quantity, CurrencyType Currency, string? PromoCode);
```

### 2.8 IDungeonGrain 副本入口接口

**GrainKey**：副本定义 ID 字符串（`"Dungeon:1001"`）。

```csharp
public interface IDungeonGrain : IGrainWithStringKey {
    Task<MatchResult> MatchmakingAsync(ulong playerId, DungeonMatchMode mode, int teamId = 0); // 加入匹配
    Task<bool> CancelMatchmakingAsync(ulong playerId);                                    // 取消匹配
    Task<DungeonDto> EnterDungeonAsync(ulong playerId, Guid matchId);                     // 进入副本
    Task<DungeonCompleteResult> CompleteDungeonAsync(Guid matchId, DungeonResult result);  // 完成副本
    Task<DungeonRecordDto> GetRecordAsync(ulong playerId);                                 // 获取记录
}
public record MatchResult(bool Success, Guid MatchId, string Message, int EstimatedWaitSeconds);
public record DungeonCompleteResult(bool Success, IReadOnlyList<string> Rewards, int Score, string Message);
```

### 2.9 GrainKey 策略汇总

| Grain 接口 | Key 类型 | Key 语义 | 实例数量级 |
| --- | --- | --- | --- |
| `IGuildGrain` | string（Guid） | 门派 ID | 千级 |
| `IFriendGrain` | string（Guid） | 玩家角色 ID | 百万级 |
| `IMailGrain` | string（Guid） | 玩家角色 ID | 百万级 |
| `IMentorGrain` | string（Guid） | 师傅角色 ID | 十万级 |
| `ILeaderboardGrain` | string | 榜单类型名 | 固定 4 个 |
| `IAchievementGrain` | string（Guid） | 玩家角色 ID | 百万级 |
| `IShopGrain` | string | 商城实例标识 | 十级 |
| `IDungeonGrain` | string | 副本定义 ID | 百级 |

> 项目历史代码中部分 Grain 同时存在 `IGrainWithGuidKey` 与 `IGrainWithStringKey` 用法。本契约统一采用 `IGrainWithStringKey`（Guid 转字符串），便于 Redis 缓存键直接复用且避免跨语言 Key 编码歧义。

## 3. Grain 状态与持久化

### 3.1 持久化总体策略

采用 **Orleans 持久化 + Redis 缓存 + SqlServer 归档** 三层存储：**Orleans PersistentState** 作为 Grain 权威工作内存（频繁读写的在线状态）；**SqlServer（EFCore）** 作为冷数据与审计归档（历史邮件、订单流水、副本记录等需复杂查询的数据）；**Redis** 作为热缓存与跨 Grain 共享视图（排行榜、商城商品、匹配队列）。

**回写策略**：**Write-through**（Grain 状态变更后立即 `WriteStateAsync()`，保证崩溃不丢数据）；**Cache-aside**（Grain 激活时优先从 Redis 读快照，缺失再从 SqlServer 加载并回填）；**Write-behind**（高频读低频写如排行榜分数，内存累加每 10 秒批量回写 SqlServer）。

### 3.2 各 Grain 状态结构

#### 3.2.1 GuildState（门派状态）

```csharp
[GenerateSerializer]
public class GuildState {
    [Id(0)] public bool IsCreated { get; set; }
    [Id(1)] public string GuildName { get; set; } = "";
    [Id(2)] public ulong LeaderId { get; set; }
    [Id(3)] public int Level { get; set; } = 1;
    [Id(4)] public int MaxMembers { get; set; } = 200;
    [Id(5)] public string Declaration { get; set; } = "";
    [Id(6)] public long CreateTime { get; set; }
    [Id(7)] public Dictionary<ulong, GuildMemberState> Members { get; set; } = new();
    [Id(8)] public Dictionary<Guid, GuildApplication> Applications { get; set; } = new();
    [Id(9)] public Dictionary<string, int> Resources { get; set; } = new();
}
[GenerateSerializer]
public class GuildMemberState {
    [Id(0)] public ulong MemberId { get; set; }
    [Id(1)] public int Position { get; set; }       // 0=帮主 1=副帮主 2=长老 3=精英 4=成员
    [Id(2)] public int Contribution { get; set; }
    [Id(3)] public long JoinTime { get; set; }
    [Id(4)] public long LastActiveTime { get; set; }
}
```

- 持久化存储名 `"guild"`，使用 `GameStore`（SqlServer AdoNet provider）。Redis 镜像 `hundun:guild:{guildId}` Hash，TTL 30 分钟。

#### 3.2.2 FriendState（好友状态）

```csharp
[GenerateSerializer]
public class FriendState {
    [Id(0)] public Dictionary<ulong, FriendInfo> Friends { get; set; } = new();
    [Id(1)] public Dictionary<Guid, FriendRequest> Requests { get; set; } = new();
    [Id(2)] public HashSet<ulong> BlockedPlayers { get; set; } = new();
    [Id(3)] public Dictionary<int, FriendGroup> Groups { get; set; } = new();
    [Id(4)] public int MaxFriends { get; set; } = 500;
}
[GenerateSerializer]
public class FriendInfo {
    [Id(0)] public ulong FriendId { get; set; }
    [Id(1)] public bool IsOnline { get; set; }
    [Id(2)] public int Intimacy { get; set; }
    [Id(3)] public int GroupId { get; set; }
    [Id(4)] public string Remark { get; set; } = "";
    [Id(5)] public long LastLoginTime { get; set; }
}
```

- 持久化存储名 `"friend"`。Redis 镜像 `hundun:friend:{playerId}` Hash，TTL 15 分钟。

#### 3.2.3 MailState（邮件状态）

邮件数据量大，采用 **Grain 存索引 + SqlServer 存正文** 混合策略：

```csharp
[GenerateSerializer]
public class MailState {
    [Id(0)] public Dictionary<Guid, MailIndex> MailIndexs { get; set; } = new();
    [Id(1)] public int UnreadCount { get; set; }
    [Id(2)] public long LastMailTime { get; set; }
}
[GenerateSerializer]
public class MailIndex {
    [Id(0)] public Guid MailId { get; set; }
    [Id(1)] public ulong SenderId { get; set; }
    [Id(2)] public string Title { get; set; } = "";
    [Id(3)] public MailType Type { get; set; }
    [Id(4)] public bool IsRead { get; set; }
    [Id(5)] public bool HasAttachment { get; set; }
    [Id(6)] public bool AttachmentClaimed { get; set; }
    [Id(7)] public long SendTime { get; set; }
    [Id(8)] public long ExpireTime { get; set; }
}
```

- 正文存储 SqlServer `Mails` 表。Redis 镜像 `hundun:mail:{playerId}:unread` List，TTL 5 分钟。

#### 3.2.4 LeaderboardState / ShopState

排行榜 Grain 不使用 PersistentState，直接以 Redis Sorted Set 为权威存储；ShopState 以 SqlServer 为权威、Redis 缓存商品列表：

```csharp
[GenerateSerializer] public class LeaderboardState {
    [Id(0)] public LeaderboardType Type { get; set; }
    [Id(1)] public long LastRefreshTime { get; set; }
    [Id(2)] public int TotalPlayers { get; set; }
    [Id(3)] public Dictionary<ulong, long> PendingUpdates { get; set; } = new(); // write-behind 缓冲
}

[GenerateSerializer] public class ShopState {
    [Id(0)] public Dictionary<int, ShopItemCache> Items { get; set; } = new();
    [Id(1)] public PromotionConfig? ActivePromotion { get; set; }
    [Id(2)] public long LastRefreshTime { get; set; }
}
```

### 3.3 持久化存储配置

```csharp
// Program.cs (Silo)
siloBuilder.AddAdoNetGrainStorage("GameStore", options => {
    options.ConnectionString = configuration.GetConnectionString("GameDb");
    options.UseSqlServer();
    options.GrainStorageSerializer = new JsonGrainStorageSerializer();
});
// Grain 构造函数声明存储
public GuildGrain(ILogger<GuildGrain> logger,
    [PersistentState("guild", "GameStore")] IPersistentState<GuildState> guildState)
{ _logger = logger; _guildState = guildState; }
```

### 3.4 Redis TTL 与回写策略汇总

| 数据 | Redis 结构 | TTL | 写入时机 | 回写 SqlServer 时机 |
| --- | --- | --- | --- | --- |
| 门派信息 | Hash | 30min | Grain 失活、WriteState 后 | Write-through |
| 好友列表 | Hash | 15min | Grain 失活 | Write-through |
| 未读邮件 ID | List | 5min | 邮件到达、读取后 | Write-through（索引） |
| 排行榜 | Sorted Set | 永久 | 分数更新立即 | 每 10 分钟快照 |
| 商城商品 | Hash | 1h | 运营修改、首次加载 | 运营后台直接写库 |
| 副本匹配队列 | Sorted Set | 会话级 | 玩家入队 | 不回写（临时数据） |

---

## 4. TouchSocket 消息协议

### 4.1 协议总览

在 TouchSocket 之上定义独立的 **请求/响应/推送** 消息体系。请求/响应复用 `MessageUnion`（MemoryPack Union），推送走独立通道。帧格式遵循 `NETWORK_PROTOCOL.md` 定长头 + 变长 payload：`[0..1] 1B Kind`（消息类型枚举）、`[1..2] 1B Compression`（压缩标记 0=none/1=lz4）、`[2..6] 4B PayloadLength`（i32 小端序）、`[6..] N B Payload`（MemoryPack 序列化字节）。

### 4.2 消息类型枚举

```csharp
namespace Horizon.Game.Message.Enums;

/// <summary>社交与商城消息类型（与 SyncPacketKind 区分，从 0x40 起编）</summary>
public enum SocialShopMessageKind : byte
{
    // 门派类
    GuildCreateRequest = 0x40, GuildCreateResponse = 0x41,
    GuildMemberOnlinePush = 0x42, GuildMemberOfflinePush = 0x43,
    // 好友类
    FriendAddRequest = 0x50, FriendAddResponse = 0x51,
    FriendStatusPush = 0x52, FriendRequestPush = 0x53,
    // 邮件类
    MailSendRequest = 0x60, MailSendResponse = 0x61,
    MailReceivedPush = 0x62, MailUnreadCountPush = 0x63,
    // 师徒类
    MentorApprenticeRequest = 0x70, MentorGraduatePush = 0x71,
    // 排行榜类
    LeaderboardQueryRequest = 0x80, LeaderboardQueryResponse = 0x81, LeaderboardUpdatePush = 0x82,
    // 成就类
    AchievementUnlockPush = 0x90, AchievementClaimRequest = 0x91,
    // 商城类
    ShopListRequest = 0xA0, ShopListResponse = 0xA1,
    ShopPurchaseRequest = 0xA2, ShopPurchaseResponse = 0xA3,
    ShopPurchasePush = 0xA4, ShopPromotionPush = 0xA5,
    // 副本类
    DungeonMatchRequest = 0xB0, DungeonMatchResponse = 0xB1, DungeonMatchPush = 0xB2,
    DungeonEnterRequest = 0xB3, DungeonEnterResponse = 0xB4, DungeonCompletePush = 0xB5,
}
```

### 4.3 关键推送消息体

#### 4.3.1 GuildMemberOnlinePush（门派成员上线推送）

```csharp
[MemoryPackable]
public partial record GuildMemberOnlinePush {
    [PropertyOrder(0)] public ulong GuildId { get; init; }
    [PropertyOrder(1)] public ulong MemberId { get; init; }
    [PropertyOrder(2)] public string MemberName { get; init; } = "";
    [PropertyOrder(3)] public int Position { get; init; }
    [PropertyOrder(4)] public long OnlineTime { get; init; }  // Unix ms
}
```

- S → C。触发：会话系统检测门派成员上线，通过 `IGuildGrain` 获取成员列表后向其他在线成员推送。路由：按 `GuildId` 找到所有在线成员会话 ID 逐个下发。

#### 4.3.2 MailReceivedPush（新邮件推送）

```csharp
[MemoryPackable]
public partial record MailReceivedPush {
    [PropertyOrder(0)] public Guid MailId { get; init; }
    [PropertyOrder(1)] public ulong SenderId { get; init; }
    [PropertyOrder(2)] public string SenderName { get; init; } = "";
    [PropertyOrder(3)] public string Title { get; init; } = "";
    [PropertyOrder(4)] public MailType Type { get; init; }
    [PropertyOrder(5)] public bool HasAttachment { get; init; }
    [PropertyOrder(6)] public long SendTime { get; init; }
    [PropertyOrder(7)] public int UnreadCount { get; init; }  // 推送后邮箱未读总数
}
```

- S → C。触发：`IMailGrain.SendMailAsync` 成功后向在线收件人推送。离线时仅写入 SqlServer，玩家上线时由 `MailGrain.OnActivateAsync` 主动拉取未读并补推。

#### 4.3.3 FriendStatusPush（好友状态变更推送）

```csharp
[MemoryPackable]
public partial record FriendStatusPush {
    [PropertyOrder(0)] public ulong FriendId { get; init; }
    [PropertyOrder(1)] public bool IsOnline { get; init; }
    [PropertyOrder(2)] public long Timestamp { get; init; }
    [PropertyOrder(3)] public string? CustomStatus { get; init; }  // 离开/忙碌/隐身
}
```

- S → C。触发：`IFriendGrain.OnPlayerOnlineStatusChangedAsync` 被会话系统调用后，向所有将该玩家列为好友的在线玩家推送。好友数 ≥ 50 时采用批量推送帧（合并多个至单个 payload）。

#### 4.3.4 ShopPurchasePush（购买结果异步推送）

```csharp
[MemoryPackable]
public partial record ShopPurchasePush {
    [PropertyOrder(0)] public Guid OrderId { get; init; }
    [PropertyOrder(1)] public int ItemId { get; init; }
    [PropertyOrder(2)] public int Quantity { get; init; }
    [PropertyOrder(3)] public PurchaseStatus Status { get; init; }  // Success/Failed/Refunded
    [PropertyOrder(4)] public string? FailReason { get; init; }
    [PropertyOrder(5)] public long Timestamp { get; init; }
}
```

- S → C。触发：商城购买涉及跨 Grain 异步扣费（钻石走计费中心），主流程立即返回订单 ID，扣费完成后推送最终结果。幂等：客户端按 `OrderId` 去重，重复推送以最后一次 `Status` 为准。

#### 4.3.5 DungeonMatchPush（副本匹配成功推送）

```csharp
[MemoryPackable]
public partial record DungeonMatchPush {
    [PropertyOrder(0)] public Guid MatchId { get; init; }
    [PropertyOrder(1)] public int DungeonId { get; init; }
    [PropertyOrder(2)] public DungeonMatchMode Mode { get; init; }
    [PropertyOrder(3)] public IReadOnlyList<MatchMemberInfo> Members { get; init; } = Array.Empty<MatchMemberInfo>();
    [PropertyOrder(4)] public int EstimatedEnterSeconds { get; init; }  // 确认进入倒计时
}
[MemoryPackable]
public partial record MatchMemberInfo {
    [PropertyOrder(0)] public ulong PlayerId { get; init; }
    [PropertyOrder(1)] public string Name { get; init; } = "";
    [PropertyOrder(2)] public int Level { get; init; }
    [PropertyOrder(3)] public int CombatPower { get; init; }
    [PropertyOrder(4)] public bool IsLeader { get; init; }
}
```

- S → C。触发：匹配成功后向所有队列成员推送。玩家需在 `EstimatedEnterSeconds` 内调用 `EnterDungeonAsync`，超时视为放弃。

### 4.4 请求/响应消息示例

请求/响应消息体同样使用 `[MemoryPackable]` record，字段顺序从 0 起。以门派创建与商城购买为例：

```csharp
[MemoryPackable] public partial record GuildCreateRequest {
    [PropertyOrder(0)] public ulong CreatorId { get; init; }
    [PropertyOrder(1)] public string GuildName { get; init; } = "";
    [PropertyOrder(2)] public string Declaration { get; init; } = "";
}
[MemoryPackable] public partial record GuildCreateResponse {
    [PropertyOrder(0)] public bool Success { get; init; }
    [PropertyOrder(1)] public Guid GuildId { get; init; }
    [PropertyOrder(2)] public int ErrorCode { get; init; }
    [PropertyOrder(3)] public string Message { get; init; } = "";
}
[MemoryPackable] public partial record ShopPurchaseResponse {
    [PropertyOrder(0)] public bool Accepted { get; init; }    // 是否受理（异步购买）
    [PropertyOrder(1)] public Guid OrderId { get; init; }
    [PropertyOrder(2)] public int ErrorCode { get; init; }
    [PropertyOrder(3)] public string Message { get; init; } = "";
}
```

### 4.5 路由处理与可靠传输

**路由处理**：网关 `SocialShopMessageHandler` 按 `Kind` 字段 switch 路由，反序列化 payload 后调用对应 Grain，返回结果序列化后下发。示例：

```csharp
public async Task<byte[]> HandleAsync(byte kind, ReadOnlyMemory<byte> payload) => kind switch
{
    (byte)SocialShopMessageKind.GuildCreateRequest  => await HandleGuildCreate(payload),
    (byte)SocialShopMessageKind.ShopPurchaseRequest => await HandleShopPurchase(payload),
    (byte)SocialShopMessageKind.DungeonMatchRequest => await HandleDungeonMatch(payload),
    _ => throw new UnknownMessageKindException(kind)
};
// HandleGuildCreate: 反序列化 → GetGrain<IGuildGrain> → CreateGuildAsync → 序列化响应
```

**可靠传输**：可靠推送（邮件、购买结果、匹配成功）走 TCP 可靠通道（`FixedHeaderPackageAdapter` 自带 ACK 重传）；不可靠推送（好友状态、门派成员上线）可走 UDP 或丢弃重传，由 `MessageReliability` 字段标记；所有推送携带 `Timestamp` 与业务唯一 ID，客户端按 ID 去重保证幂等。

---

## 5. MemoryPack 序列化 DTO

### 5.1 DTO 设计约定

所有跨进程传输 DTO 使用 `record` + `[MemoryPackable]` + `[PropertyOrder]`，命名空间 `Horizon.Share.Dtos.Games`。字段顺序从 0 起连续编号禁止跳号；时间统一 `long`（Unix 毫秒），角色 ID 用 `ulong`，业务实体 ID 用 `Guid`；集合类型优先 `IReadOnlyList<T>`。

### 5.2 门派相关 DTO

```csharp
/// <summary>门派信息</summary>
[MemoryPackable]
public partial record GuildDto {
    [PropertyOrder(0)] public Guid GuildId { get; init; }
    [PropertyOrder(1)] public string GuildName { get; init; } = "";
    [PropertyOrder(2)] public ulong LeaderId { get; init; }
    [PropertyOrder(3)] public string LeaderName { get; init; } = "";
    [PropertyOrder(4)] public int Level { get; init; }
    [PropertyOrder(5)] public int MemberCount { get; init; }
    [PropertyOrder(6)] public int MaxMembers { get; init; }
    [PropertyOrder(7)] public string Declaration { get; init; } = "";
    [PropertyOrder(8)] public long CreateTime { get; init; }
    [PropertyOrder(9)] public IReadOnlyDictionary<string, int> Resources { get; init; } = new Dictionary<string, int>();
}
/// <summary>门派成员</summary>
[MemoryPackable]
public partial record GuildMemberDto {
    [PropertyOrder(0)] public ulong MemberId { get; init; }
    [PropertyOrder(1)] public string MemberName { get; init; } = "";
    [PropertyOrder(2)] public int Position { get; init; }       // 0=帮主 1=副帮主 2=长老 3=精英 4=成员
    [PropertyOrder(3)] public string PositionName { get; init; } = "";
    [PropertyOrder(4)] public int Level { get; init; }
    [PropertyOrder(5)] public int CombatPower { get; init; }
    [PropertyOrder(6)] public int Contribution { get; init; }
    [PropertyOrder(7)] public bool IsOnline { get; init; }
    [PropertyOrder(8)] public long JoinTime { get; init; }
    [PropertyOrder(9)] public long LastActiveTime { get; init; }
}
```

### 5.3 好友相关 DTO

```csharp
[MemoryPackable]
public partial record FriendDto {
    [PropertyOrder(0)] public ulong FriendId { get; init; }
    [PropertyOrder(1)] public string FriendName { get; init; } = "";
    [PropertyOrder(2)] public int Level { get; init; }
    [PropertyOrder(3)] public int CombatPower { get; init; }
    [PropertyOrder(4)] public bool IsOnline { get; init; }
    [PropertyOrder(5)] public string? CustomStatus { get; init; }   // 离开/忙碌/隐身
    [PropertyOrder(6)] public int Intimacy { get; init; }
    [PropertyOrder(7)] public int GroupId { get; init; }
    [PropertyOrder(8)] public string GroupName { get; init; } = "";
    [PropertyOrder(9)] public string Remark { get; init; } = "";
    [PropertyOrder(10)] public long LastLoginTime { get; init; }
}
[MemoryPackable]
public partial record FriendGroupDto {
    [PropertyOrder(0)] public int GroupId { get; init; }
    [PropertyOrder(1)] public string GroupName { get; init; } = "";
    [PropertyOrder(2)] public int MemberCount { get; init; }
}
```

### 5.4 邮件相关 DTO

```csharp
[MemoryPackable]
public partial record MailDto {
    [PropertyOrder(0)] public Guid MailId { get; init; }
    [PropertyOrder(1)] public ulong SenderId { get; init; }
    [PropertyOrder(2)] public string SenderName { get; init; } = "";
    [PropertyOrder(3)] public string Title { get; init; } = "";
    [PropertyOrder(4)] public string Content { get; init; } = "";
    [PropertyOrder(5)] public MailType Type { get; init; }          // System/Player/Guild/Auction
    [PropertyOrder(6)] public bool IsRead { get; init; }
    [PropertyOrder(7)] public bool HasAttachment { get; init; }
    [PropertyOrder(8)] public bool AttachmentClaimed { get; init; }
    [PropertyOrder(9)] public MailAttachmentDto? Attachment { get; init; }
    [PropertyOrder(10)] public long SendTime { get; init; }
    [PropertyOrder(11)] public long ExpireTime { get; init; }
}
[MemoryPackable]
public partial record MailAttachmentDto {
    [PropertyOrder(0)] public IReadOnlyList<MailItemEntry> Items { get; init; } = Array.Empty<MailItemEntry>();
    [PropertyOrder(1)] public long Gold { get; init; }
    [PropertyOrder(2)] public long Diamond { get; init; }
    [PropertyOrder(3)] public long BoundPoint { get; init; }
    [PropertyOrder(4)] public int Exp { get; init; }
}
[MemoryPackable]
public partial record MailItemEntry {
    [PropertyOrder(0)] public int ItemId { get; init; }
    [PropertyOrder(1)] public int Quantity { get; init; }
    [PropertyOrder(2)] public bool IsBound { get; init; }
}
```

### 5.5 师徒与排行榜 DTO

```csharp
[MemoryPackable]
public partial record MentorDto {
    [PropertyOrder(0)] public ulong MasterId { get; init; }
    [PropertyOrder(1)] public string MasterName { get; init; } = "";
    [PropertyOrder(2)] public ulong ApprenticeId { get; init; }
    [PropertyOrder(3)] public string ApprenticeName { get; init; } = "";
    [PropertyOrder(4)] public int ApprenticeLevel { get; init; }
    [PropertyOrder(5)] public int Contribution { get; init; }       // 累计贡献
    [PropertyOrder(6)] public int TargetContribution { get; init; } // 出师所需
    [PropertyOrder(7)] public bool CanGraduate { get; init; }
    [PropertyOrder(8)] public long ApprenticeTime { get; init; }
}
[MemoryPackable]
public partial record LeaderboardEntryDto {
    [PropertyOrder(0)] public ulong PlayerId { get; init; }
    [PropertyOrder(1)] public string PlayerName { get; init; } = "";
    [PropertyOrder(2)] public int Rank { get; init; }
    [PropertyOrder(3)] public long Score { get; init; }
    [PropertyOrder(4)] public int Level { get; init; }
    [PropertyOrder(5)] public int CombatPower { get; init; }
    [PropertyOrder(6)] public ulong? GuildId { get; init; }
    [PropertyOrder(7)] public string? GuildName { get; init; }
    [PropertyOrder(8)] public long LastUpdateTime { get; init; }
}
```

### 5.6 成就相关 DTO

```csharp
[MemoryPackable]
public partial record AchievementDto {
    [PropertyOrder(0)] public int AchievementId { get; init; }
    [PropertyOrder(1)] public string Name { get; init; } = "";
    [PropertyOrder(2)] public string Description { get; init; } = "";
    [PropertyOrder(3)] public AchievementCategory Category { get; init; } // Combat/Explore/Social/Economy/Growth
    [PropertyOrder(4)] public int CurrentProgress { get; init; }
    [PropertyOrder(5)] public int TargetProgress { get; init; }
    [PropertyOrder(6)] public bool IsUnlocked { get; init; }
    [PropertyOrder(7)] public bool RewardClaimed { get; init; }
    [PropertyOrder(8)] public AchievementRewardDto? Reward { get; init; }
    [PropertyOrder(9)] public int Tier { get; init; }                  // 铜银金铂层级
    [PropertyOrder(10)] public long? UnlockTime { get; init; }
}
[MemoryPackable]
public partial record AchievementRewardDto {
    [PropertyOrder(0)] public IReadOnlyList<MailItemEntry> Items { get; init; } = Array.Empty<MailItemEntry>();
    [PropertyOrder(1)] public long Gold { get; init; }
    [PropertyOrder(2)] public long Diamond { get; init; }
    [PropertyOrder(3)] public int Exp { get; init; }
    [PropertyOrder(4)] public int TitleId { get; init; }   // 称号奖励
}
```

### 5.7 商城相关 DTO

```csharp
[MemoryPackable]
public partial record ShopItemDto {
    [PropertyOrder(0)] public int ItemId { get; init; }
    [PropertyOrder(1)] public string Name { get; init; } = "";
    [PropertyOrder(2)] public string Description { get; init; } = "";
    [PropertyOrder(3)] public ShopCategory Category { get; init; }
    [PropertyOrder(4)] public CurrencyType Currency { get; init; }     // Gold/Diamond/BoundPoint
    [PropertyOrder(5)] public long OriginalPrice { get; init; }
    [PropertyOrder(6)] public long CurrentPrice { get; init; }
    [PropertyOrder(7)] public int DiscountPercent { get; init; }        // 0-100，100 为无折扣
    [PropertyOrder(8)] public int LimitType { get; init; }              // 0=无限 1=日限 2=周限 3=月限 4=账号限
    [PropertyOrder(9)] public int LimitCount { get; init; }
    [PropertyOrder(10)] public int RemainingStock { get; init; }        // -1 表示无限
    [PropertyOrder(11)] public bool IsHot { get; init; }
    [PropertyOrder(12)] public bool IsNew { get; init; }
    [PropertyOrder(13)] public long? PromoStartTime { get; init; }
    [PropertyOrder(14)] public long? PromoEndTime { get; init; }
    [PropertyOrder(15)] public IReadOnlyList<ShopItemBundle> BundleItems { get; init; } = Array.Empty<ShopItemBundle>();
}
[MemoryPackable]
public partial record ShopItemBundle {
    [PropertyOrder(0)] public int ItemId { get; init; }
    [PropertyOrder(1)] public int Quantity { get; init; }
    [PropertyOrder(2)] public bool IsBound { get; init; }
}
[MemoryPackable]
public partial record ShopOrderDto {
    [PropertyOrder(0)] public Guid OrderId { get; init; }
    [PropertyOrder(1)] public ulong PlayerId { get; init; }
    [PropertyOrder(2)] public int ItemId { get; init; }
    [PropertyOrder(3)] public int Quantity { get; init; }
    [PropertyOrder(4)] public CurrencyType Currency { get; init; }
    [PropertyOrder(5)] public long UnitPrice { get; init; }
    [PropertyOrder(6)] public long TotalPrice { get; init; }
    [PropertyOrder(7)] public OrderStatus Status { get; init; }        // Pending/Success/Failed/Refunded
    [PropertyOrder(8)] public long CreateTime { get; init; }
    [PropertyOrder(9)] public long? CompleteTime { get; init; }
    [PropertyOrder(10)] public string? PromoCode { get; init; }
    [PropertyOrder(11)] public int ErrorCode { get; init; }
}
```

### 5.8 副本相关 DTO

```csharp
[MemoryPackable]
public partial record DungeonDto {
    [PropertyOrder(0)] public Guid MatchId { get; init; }
    [PropertyOrder(1)] public int DungeonId { get; init; }
    [PropertyOrder(2)] public string DungeonName { get; init; } = "";
    [PropertyOrder(3)] public DungeonMatchMode Mode { get; init; }     // Single/Team/Guild
    [PropertyOrder(4)] public int Difficulty { get; init; }
    [PropertyOrder(5)] public int MaxPlayers { get; init; }
    [PropertyOrder(6)] public IReadOnlyList<ulong> MemberIds { get; init; } = Array.Empty<ulong>();
    [PropertyOrder(7)] public long EnterTime { get; init; }
    [PropertyOrder(8)] public int TimeLimitSeconds { get; init; }
    [PropertyOrder(9)] public string SceneAddress { get; init; } = "";  // 战斗服务器地址
}
[MemoryPackable]
public partial record DungeonRecordDto {
    [PropertyOrder(0)] public ulong PlayerId { get; init; }
    [PropertyOrder(1)] public int TotalClearCount { get; init; }
    [PropertyOrder(2)] public int BestScore { get; init; }
    [PropertyOrder(3)] public long BestClearTime { get; init; }
    [PropertyOrder(4)] public int WeeklyClearCount { get; init; }
    [PropertyOrder(5)] public long LastClearTime { get; init; }
    [PropertyOrder(6)] public IReadOnlyList<DungeonHistoryEntry> RecentHistory { get; init; } = Array.Empty<DungeonHistoryEntry>();
}
[MemoryPackable]
public partial record DungeonHistoryEntry {
    [PropertyOrder(0)] public Guid MatchId { get; init; }
    [PropertyOrder(1)] public int DungeonId { get; init; }
    [PropertyOrder(2)] public int Score { get; init; }
    [PropertyOrder(3)] public long ClearTime { get; init; }
    [PropertyOrder(4)] public long Duration { get; init; }
    [PropertyOrder(5)] public bool IsWin { get; init; }
}
```

### 5.9 枚举定义汇总

```csharp
public enum MailType : byte { System = 0, Player = 1, Guild = 2, Auction = 3, Achievement = 4 }
public enum CurrencyType : byte { Gold = 0, Diamond = 1, BoundPoint = 2 }
public enum ShopCategory : byte { Equipment = 0, Consumable = 1, Cosmetic = 2, Bundle = 3, Mount = 4, Pet = 5 }
public enum OrderStatus : byte { Pending = 0, Success = 1, Failed = 2, Refunded = 3 }
public enum PurchaseStatus : byte { Success = 0, Failed = 1, Refunded = 2 }
public enum AchievementCategory : byte { Combat = 0, Explore = 1, Social = 2, Economy = 3, Growth = 4 }
public enum LeaderboardType : byte { CombatPower = 0, Level = 1, Guild = 2, Arena = 3 }
public enum DungeonMatchMode : byte { Single = 0, Team = 1, Guild = 2 }
public enum MentorTaskType : byte { Daily = 0, Weekly = 1, Special = 2 }
```

---

## 6. EFCore 实体与 SqlServer 表结构

### 6.1 实体设计约定

所有实体继承 `BaseGameModel`（提供 `Id`、`CreateTime`、`UpdateTime`、`IsDeleted`），命名空间 `Horizon.Model.GameModel`。主键统一 `Guid`，角色 ID 用 `ulong`（SqlServer 存 `decimal(20,0)`），索引命名 `IX_{表名}_{字段}`。时间字段 `DateTime`（UTC），与 DTO 的 Unix 毫秒在 Mapper 层转换，软删除 `IsDeleted` 统一过滤。

### 6.2 Guild / GuildMember 门派实体

```csharp
public class Guild : BaseGameModel {
    public Guid GuildId { get; set; }
    public string GuildName { get; set; } = "";
    public ulong LeaderId { get; set; }
    public string LeaderName { get; set; } = "";
    public int Level { get; set; } = 1;
    public int MaxMembers { get; set; } = 200;
    public string Declaration { get; set; } = "";
    public int TotalContribution { get; set; }
    public ICollection<GuildMember> Members { get; set; } = new List<GuildMember>();
}
public class GuildMember : BaseGameModel {
    public Guid GuildId { get; set; }
    public ulong MemberId { get; set; }
    public string MemberName { get; set; } = "";
    public int Position { get; set; }
    public int Contribution { get; set; }
    public long JoinTime { get; set; }
    public Guild Guild { get; set; } = null!;
}
```

- **表 `Guilds`**：主键 `Id`(Guid)，`GuildId`(UQ)，`GuildName`(nvarchar(32), 唯一索引)，`LeaderId`(decimal(20,0))，`Declaration`(nvarchar(500))。基类字段：`CreateTime`/`UpdateTime`/`IsDeleted`。
- **表 `GuildMembers`**：`GuildId`(FK→Guilds)，`MemberId`(decimal(20,0), UQ 一人一门派)。索引：`IX_GuildMembers_GuildId_Position`（按职位排序）。

### 6.3 Friend 好友实体

```csharp
public class Friend : BaseGameModel {
    public ulong PlayerId { get; set; }      // 持有方
    public ulong FriendId { get; set; }       // 被加方
    public string FriendName { get; set; } = "";
    public int GroupId { get; set; }
    public string Remark { get; set; } = "";
    public int Intimacy { get; set; }
    public long LastLoginTime { get; set; }
    public bool IsBlocked { get; set; }
}
```

- **表 `Friends`**：`PlayerId`/`FriendId`(decimal(20,0))，`FriendName`/`Remark`(nvarchar(64))。索引：`UQ_Friends_PlayerId_FriendId`（防重复）、`IX_Friends_PlayerId_GroupId`（按分组）。

### 6.4 Mail 邮件实体

```csharp
public class Mail : BaseGameModel {
    public Guid MailId { get; set; }
    public ulong SenderId { get; set; }
    public string SenderName { get; set; } = "";
    public ulong ReceiverId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public MailType Type { get; set; }
    public bool IsRead { get; set; }
    public bool HasAttachment { get; set; }
    public bool AttachmentClaimed { get; set; }
    public string? AttachmentJson { get; set; }  // 附件 JSON 序列化
    public long SendTime { get; set; }
    public long ExpireTime { get; set; }
}
```

- **表 `Mails`**：`MailId`(UQ)，`SenderId`/`ReceiverId`(decimal(20,0))，`Title`(nvarchar(128))，`Content`/`AttachmentJson`(nvarchar(max))，`Type`(tinyint)。索引：`IX_Mails_ReceiverId_SendTime`（时间倒序）、`IX_Mails_ReceiverId_IsRead`（未读）、`IX_Mails_ExpireTime`（过期清理）。

### 6.5 Mentorship 师徒关系实体

```csharp
public class Mentorship : BaseGameModel {
    public ulong MasterId { get; set; }
    public string MasterName { get; set; } = "";
    public ulong ApprenticeId { get; set; }
    public string ApprenticeName { get; set; } = "";
    public int ApprenticeLevel { get; set; }
    public int Contribution { get; set; }
    public int TargetContribution { get; set; }
    public bool IsGraduated { get; set; }
    public long ApprenticeTime { get; set; }
    public long? GraduateTime { get; set; }
    public int Status { get; set; }   // 0=进行中 1=已出师 2=已解除
}
```

- **表 `Mentorships`**：`MasterId`/`ApprenticeId`(decimal(20,0))，`ApprenticeId`(UQ 一人仅一师)。索引：`IX_Mentorships_MasterId_Status`（师傅查询在带徒弟）。

### 6.6 LeaderboardScore 排行榜分数实体

```csharp
public class LeaderboardScore : BaseGameModel {
    public LeaderboardType Type { get; set; }
    public ulong PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public long Score { get; set; }
    public int Level { get; set; }
    public int CombatPower { get; set; }
    public ulong? GuildId { get; set; }
    public long LastUpdateTime { get; set; }
}
```

- **表 `LeaderboardScores`**：`Type`(tinyint)，`PlayerId`(decimal(20,0))，`Score`(bigint)。索引：`UQ_LeaderboardScores_Type_PlayerId`（每榜每人一条）、`IX_LeaderboardScores_Type_Score`（按分数排序，兜底，主要走 Redis）。

### 6.7 Achievement / PlayerAchievement 成就实体

```csharp
public class Achievement : BaseGameModel {
    public int AchievementId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public AchievementCategory Category { get; set; }
    public int TargetProgress { get; set; }
    public int Tier { get; set; }
    public string? RewardJson { get; set; }     // 奖励配置 JSON
    public bool IsActive { get; set; }
}
public class PlayerAchievement : BaseGameModel {
    public ulong PlayerId { get; set; }
    public int AchievementId { get; set; }
    public int CurrentProgress { get; set; }
    public bool IsUnlocked { get; set; }
    public bool RewardClaimed { get; set; }
    public long? UnlockTime { get; set; }
    public Achievement Achievement { get; set; } = null!;
}
```

- **表 `Achievements`**（成就定义，运营维护）与 **`PlayerAchievements`**（玩家进度）。
- `PlayerAchievements` 索引：`UQ_PlayerAchievements_PlayerId_AchievementId`（每玩家每成就一条）、`IX_PlayerAchievements_PlayerId_IsUnlocked`（查询已解锁）。

### 6.8 ShopItem / ShopOrder 商城实体

```csharp
public class ShopItem : BaseGameModel {
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ShopCategory Category { get; set; }
    public CurrencyType Currency { get; set; }
    public long OriginalPrice { get; set; }
    public long CurrentPrice { get; set; }
    public int LimitType { get; set; }
    public int LimitCount { get; set; }
    public int RemainingStock { get; set; }
    public string? BundleItemsJson { get; set; }
    public bool IsActive { get; set; }
    public long? PromoStartTime { get; set; }
    public long? PromoEndTime { get; set; }
}
public class ShopOrder : BaseGameModel {
    public Guid OrderId { get; set; }
    public ulong PlayerId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public CurrencyType Currency { get; set; }
    public long UnitPrice { get; set; }
    public long TotalPrice { get; set; }
    public OrderStatus Status { get; set; }
    public long CreateTime { get; set; }
    public long? CompleteTime { get; set; }
    public string? PromoCode { get; set; }
    public int ErrorCode { get; set; }
}
```

- 索引：`ShopItems`：`UQ_ShopItems_ItemId`、`IX_ShopItems_Category_IsActive`；`ShopOrders`：`UQ_ShopOrders_OrderId`、`IX_ShopOrders_PlayerId_CreateTime`、`IX_ShopOrders_Status`。

### 6.9 DungeonRecord 副本记录实体

```csharp
public class DungeonRecord : BaseGameModel {
    public ulong PlayerId { get; set; }
    public int DungeonId { get; set; }
    public Guid MatchId { get; set; }
    public int Score { get; set; }
    public long ClearTime { get; set; }
    public long Duration { get; set; }
    public bool IsWin { get; set; }
    public DungeonMatchMode Mode { get; set; }
    public IReadOnlyList<ulong> MemberIds { get; set; } = Array.Empty<ulong>();
    public string? RewardsJson { get; set; }
}
```

- **表 `DungeonRecords`** 索引：`IX_DungeonRecords_PlayerId_ClearTime`（玩家副本历史）、`IX_DungeonRecords_DungeonId_Score`（副本高分榜）、`IX_DungeonRecords_PlayerId_DungeonId`（玩家某副本最佳记录）。

### 6.10 EFCore DbContext 注册

```csharp
public class GameEntityContext : DbContext {
    public DbSet<Guild> Guilds { get; set; } = null!;
    public DbSet<GuildMember> GuildMembers { get; set; } = null!;
    public DbSet<Friend> Friends { get; set; } = null!;
    public DbSet<Mail> Mails { get; set; } = null!;
    public DbSet<Mentorship> Mentorships { get; set; } = null!;
    public DbSet<LeaderboardScore> LeaderboardScores { get; set; } = null!;
    public DbSet<Achievement> Achievements { get; set; } = null!;
    public DbSet<PlayerAchievement> PlayerAchievements { get; set; } = null!;
    public DbSet<ShopItem> ShopItems { get; set; } = null!;
    public DbSet<ShopOrder> ShopOrders { get; set; } = null!;
    public DbSet<DungeonRecord> DungeonRecords { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder mb) {
        mb.Entity<GuildMember>(e => { e.HasIndex(m => new { m.GuildId, m.Position }); e.HasIndex(m => m.MemberId).IsUnique(); });
        mb.Entity<Friend>(e => { e.HasIndex(f => new { f.PlayerId, f.FriendId }).IsUnique(); e.HasIndex(f => new { f.PlayerId, f.GroupId }); });
        mb.Entity<Mail>(e => { e.HasIndex(m => new { m.ReceiverId, m.SendTime }); e.HasIndex(m => new { m.ReceiverId, m.IsRead }); e.HasIndex(m => m.ExpireTime); });
        // 其余索引见各实体说明
    }
}
```

---

## 7. Redis 缓存键命名规范

### 7.1 命名总则

所有键统一以 `hundun:` 开缀；使用 `:` 作层级分隔符（便于 Redis Cluster hashtag 与可视化分组）；全小写，单词间用连字符 `-`，变量占位用 `{varName}`。涉及同一玩家多键操作时，将玩家 ID 用 `{}` 包裹路由至同一分片，如 `hundun:player:{playerId}:mail`。

### 7.2 键命名清单

#### 7.2.1 门派缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:guild:{guildId}` | Hash | 30min | 门派基础信息与成员快照（字段：guildName/leaderId/level/memberCount/maxMembers/declaration/createTime） |
| `hundun:guild:{guildId}:members` | Set | 30min | 成员角色 ID 集合（快速判断在线） |
| `hundun:guild:name:{guildName}` | String | 30min | 门派名 → GuildId 映射（创建时唯一性校验） |
| `hundun:guild:player:{playerId}` | String | 30min | 玩家所属门派 ID（快速查询） |

#### 7.2.2 好友缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:friend:{playerId}` | Hash | 15min | 好友列表（field=friendId，value=FriendInfo JSON） |
| `hundun:friend:{playerId}:groups` | Hash | 15min | 分组信息 |
| `hundun:friend:{playerId}:blocked` | Set | 15min | 黑名单集合 |

#### 7.2.3 邮件缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:mail:{playerId}:unread` | List | 5min | 未读邮件 ID 列表（最新在前） |
| `hundun:mail:{playerId}:count` | String | 5min | 未读数量（独立计数，避免 LPUSH 后再 LLEN） |
| `hundun:mail:{playerId}:index` | Hash | 10min | 邮件索引（mailId → MailIndex JSON） |
| `hundun:mail:content:{mailId}` | String | 1h | 邮件正文缓存（首次读取后回填） |

**操作约定**：新邮件到达 `LPUSH unread + INCR count + 推送 MailUnreadCountPush`；读取邮件 `LREM unread + DECR count`。

#### 7.2.4 师徒与成就缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:mentor:{masterId}` | Hash | 30min | 师傅的徒弟列表 |
| `hundun:mentor:apprentice:{apprenticeId}` | String | 30min | 徒弟 → 师傅映射（反向查询） |
| `hundun:achievement:{playerId}` | Hash | 30min | 玩家成就进度（field=achievementId，value=进度 JSON） |
| `hundun:achievement:unlocked:{playerId}` | Set | 30min | 已解锁成就 ID 集合 |
| `hundun:achievement:defs` | Hash | 6h | 成就定义缓存（运营修改后刷新） |

#### 7.2.5 排行榜缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:leaderboard:{type}` | Sorted Set | 永久（快照定期刷） | 排行榜主存储（member=playerId, score=分数） |
| `hundun:leaderboard:{type}:snapshot` | String | 1h | Top 100 JSON 快照（降低 ZRANGE 压力） |
| `hundun:leaderboard:player:{playerId}:rank` | Hash | 5min | 玩家在各榜的排名与分数 |

**操作约定**：更新分数 `ZADD + 异步写 SqlServer`；查询 Top N 优先读 `snapshot`，miss 则 `ZREVRANGE`；自身排名 `ZREVRANK + ZSCORE`。

#### 7.2.6 商城缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:shop:items` | Hash | 1h | 商品列表（field=itemId，value=ShopItemDto JSON） |
| `hundun:shop:items:category:{category}` | Set | 1h | 按分类的商品 ID 集合 |
| `hundun:shop:promo:active` | String | 与促销同步 | 当前生效促销配置 JSON |
| `hundun:shop:limit:{playerId}:{itemId}` | String | 至下次重置 | 玩家限购已购次数（日/周/月） |
| `hundun:shop:stock:{itemId}` | String | 商品下架前 | 剩余库存（原子 DECR） |

**限购键 TTL 策略**：日限至当日 24:00 UTC；周限至下周一 00:00 UTC；月限至下月 1 日 00:00 UTC；账号限无 TTL。

#### 7.2.7 副本匹配缓存

| 键格式 | 类型 | TTL | 说明 |
| --- | --- | --- | --- |
| `hundun:dungeon:matchmaking` | Sorted Set | 会话级 | 全局匹配队列（score=入队时间戳，member=playerId） |
| `hundun:dungeon:matchmaking:{dungeonId}` | Sorted Set | 会话级 | 按副本的匹配队列 |
| `hundun:dungeon:match:{matchId}` | Hash | 10min | 匹配会话信息（成员、状态、超时） |
| `hundun:dungeon:team:{teamId}` | Set | 30min | 组队成员集合 |
| `hundun:dungeon:record:{playerId}` | Hash | 1h | 玩家副本记录缓存 |

### 7.3 缓存回源与一致性

**回源流程**：读请求优先查 Redis（HIT 直接返回）；MISS 则 Grain 激活从 PersistentState 读取（命中则回填 Redis 返回；为空则查 SqlServer，回填 PersistentState + Redis）。

**一致性保证**：
- 写操作：先更新 Grain PersistentState（Write-through），再更新 Redis，最后异步写 SqlServer 审计表。
- 缓存失效：Grain 主动删除 Redis 键后立即重写，避免脏读窗口。
- 热点 Key：排行榜等热点采用本地二级缓存（`IMemoryCache`，TTL 10 秒）降低 Redis 压力。

---

## 8. 前端调用时序图

### 8.1 门派创建时序

```
1. 玩家在 UI 点击"创建门派"，输入门派名称与宣言
2. 前端构造 GuildCreateRequest，通过 TouchSocket 发送（Kind=0x40）
3. 网关 SocialShopMessageHandler 接收，反序列化 payload
4. Handler 调用 IGrainFactory.GetGrain<IGuildGrain>(newGuid.ToString())
5. GuildGrain.CreateGuildAsync 被激活
   - OnActivateAsync 从 Redis（hundun:guild:{guildId}）读取状态，miss 则从 PersistentState 加载
   - 校验门派名是否重复（查询 hundun:guild:name:{guildName}）
   - 写入 GuildState（LeaderId=creatorId, Members 添加创建者）→ WriteStateAsync 持久化到 SqlServer
   - 回写 Redis（HSET hundun:guild:{guildId} ...）+ SET hundun:guild:name:{guildName} {guildId} EX 1800
6. GuildGrain 返回 GuildResult(true, guildId, 0, "")
7. Handler 序列化 GuildCreateResponse，通过 TouchSocket 下发（Kind=0x41）
8. 前端收到响应，跳转至门派主界面；异步向创建者推送门派成员上线通知（可选）
```

### 8.2 好友添加时序

```
1. 玩家 A 在 UI 输入玩家 B 名称，点击"添加好友"
2. 前端发送 FriendAddRequest（Kind=0x50，含 targetName）
3. Handler 根据 B 的名称查询角色 Grain 获取 B 的 playerId
4. Handler 获取 A 的 IFriendGrain（key=A.playerId）
5. A.FriendGrain.AddFriendAsync(B.playerId, remark, groupId)
   - 校验好友上限、黑名单、是否已存在 → 写入 FriendState.Friends → WriteStateAsync 持久化
   - HSET hundun:friend:{A.playerId} {B.playerId} {FriendInfo JSON}
6. A.FriendGrain 通过 Grain 调用 B.FriendGrain.OnFriendAddedAsync(A.playerId)（双向同步）
7. B.FriendGrain 写入反向好友关系，若 B 在线则推送 FriendStatusPush（Kind=0x52）
8. Handler 返回 FriendAddResponse 给 A；B 在线时收到 FriendStatusPush，UI 刷新好友列表
```

### 8.3 邮件发送与推送时序

```
1. 系统/玩家触发邮件发送（如任务奖励、拍卖结算）
2. 调用方获取 IMailGrain（key=receiverId）
3. MailGrain.SendMailAsync(request)
   - 生成 MailId → 写入 MailState.MailIndexs（仅索引）→ WriteStateAsync 持久化
   - 正文写入 SqlServer Mails 表（EFCore SaveChanges）
   - LPUSH hundun:mail:{receiverId}:unread {mailId} + INCR count + HSET index
4. MailGrain 返回 MailSendResult(true, mailId, "")
5. MailGrain 检查收件人在线状态（查询会话 Grain）
   - 在线：推送 MailReceivedPush（Kind=0x62），含 MailId、Title、UnreadCount
   - 离线：仅持久化，待收件人上线时 OnActivateAsync 批量补推
6. 收件人点击邮件 → ReadMailAsync → 读取正文（先查 Redis hundun:mail:content:{mailId}，miss 查 SqlServer 并回填）
```

### 8.4 商城购买时序（异步扣费）

```
1. 玩家在商城 UI 选择商品，点击购买
2. 前端发送 ShopPurchaseRequest（Kind=0xA2，含 itemId, quantity, currency）
3. Handler 获取 IShopGrain（key=GlobalShop）
4. ShopGrain.PurchaseAsync(request)
   - 从 Redis hundun:shop:items 读取 ShopItemDto（miss 则查 SqlServer 并回填）
   - 校验商品上架状态、促销时间、库存（DECR hundun:shop:stock:{itemId}）
   - 校验限购（GET hundun:shop:limit:{playerId}:{itemId}，未超限则 INCR）
   - 创建订单（OrderId=Guid.NewGuid），状态 Pending，写入 SqlServer ShopOrders
   - 调用经济系统 IEconomyGrain 预扣货币（两阶段）：钻石走计费中心异步返回，金币/绑定点券同步扣减
   - 返回 ShopOrderDto（Accepted=true, OrderId）
5. Handler 返回 ShopPurchaseResponse（Kind=0xA3）给前端，UI 显示"处理中"
6. 异步：经济系统扣费完成回调 ShopGrain.OnPaymentResultAsync(orderId, success)
   - 成功：调用背包 Grain 发放物品，更新订单 Status=Success
   - 失败：回滚限购计数（DECR）、恢复库存（INCR）、退款，订单 Status=Failed
7. ShopGrain 推送 ShopPurchasePush（Kind=0xA4，含 OrderId, Status）
8. 前端收到推送，UI 显示购买结果（成功/失败），刷新背包与货币
```

### 8.5 副本匹配与进入时序

```
1. 玩家在副本入口 UI 选择副本与模式，点击"匹配"
2. 前端发送 DungeonMatchRequest（Kind=0xB0）
3. Handler 获取 IDungeonGrain（key=Dungeon:{dungeonId}）
4. DungeonGrain.MatchmakingAsync(playerId, mode, teamId)
   - 校验玩家等级、入场次数（查 hundun:dungeon:record:{playerId}）
   - ZADD hundun:dungeon:matchmaking:{dungeonId} {nowTimestamp} {playerId}
   - 根据 mode 检查匹配池：Single 立即匹配；Team 等待 5 人凑齐或超时回退；Guild 等待同门派成员
   - 匹配成功：HSET hundun:dungeon:match:{matchId} 记录成员与超时
   - 返回 MatchResult(true, matchId, estimatedWait)
5. Handler 返回 DungeonMatchResponse（Kind=0xB1）
6. DungeonGrain 向所有匹配成员推送 DungeonMatchPush（Kind=0xB2），含 MatchId、成员列表、确认倒计时
7. 玩家点击"进入副本" → 前端发送 DungeonEnterRequest（Kind=0xB3）
8. DungeonGrain.EnterDungeonAsync(playerId, matchId)
   - 校验 MatchId 有效性、玩家在成员列表中、未超时
   - 分配战斗服务器实例，获取 SceneAddress → 调用战斗 Grain 初始化副本场景
   - 返回 DungeonDto（含 SceneAddress、成员、时间限制）
9. 前端收到响应，切换至战斗场景连接 SceneAddress
10. 副本战斗结束 → 战斗 Grain 调用 CompleteDungeonAsync(matchId, result)
    - 结算奖励（经验、物品、积分）→ 更新玩家副本记录 → 更新排行榜 → 推送 DungeonCompletePush（Kind=0xB5）
11. 前端收到完成推送，显示结算界面
```

### 8.6 排行榜查询与成就解锁时序

```
排行榜查询：
1. 玩家打开排行榜 UI，选择"战力榜"
2. 前端发送 LeaderboardQueryRequest（Kind=0x80，含 type=CombatPower, topN=100）
3. Handler 获取 ILeaderboardGrain（key=CombatPower）
4. LeaderboardGrain.GetRankingAsync(topN=100, offset=0)
   - 优先读本地缓存（IMemoryCache，TTL 10s）→ miss 读 Redis snapshot → 仍 miss 则 ZREVRANGE
   - 批量查询玩家名称、门派信息 → 组装 List<LeaderboardEntryDto>，回填本地缓存与 snapshot
5. 返回给前端，UI 渲染排行榜列表
6. 玩家查询自身排名：ZREVRANK + ZSCORE → 返回 LeaderboardEntryDto（含 rank, score）

成就解锁与领取：
1. 战斗系统检测到玩家击杀 Boss 达成条件
2. 战斗 Grain 调用 IAchievementGrain.UnlockAsync(playerId, achievementId, progress)
3. AchievementGrain 激活
   - 从 Redis hundun:achievement:{playerId} 读取进度 → 更新 CurrentProgress
   - 若 CurrentProgress >= TargetProgress 且未解锁：标记 IsUnlocked=true → WriteStateAsync + 更新 Redis
4. 若新解锁，推送 AchievementUnlockPush（Kind=0x90）给在线玩家
5. 玩家点击领取 → 前端发送 AchievementClaimRequest（Kind=0x91）
6. ClaimRewardAsync：校验 IsUnlocked=true 且 RewardClaimed=false → 背包 Grain 发放物品 + 经济系统发货币 → 标记 RewardClaimed=true
7. 返回 ClaimResult(true, claimedItems) → 前端刷新成就 UI 与背包
```

### 8.8 师徒出师时序

```
1. 徒弟完成师徒任务，贡献度达标
2. 任务系统调用 IMentorGrain.ContributeAsync(apprenticeId, contribution, taskType)
3. MentorGrain 更新贡献度，检查是否达到出师条件
4. 若达标，推送 MentorGraduatePush（Kind=0x71）给师傅与徒弟，提示可出师
5. 徒弟在 UI 点击"出师" → 前端调用 MentorGrain.GraduateAsync(apprenticeId)
6. GraduateAsync 处理：
   - 校验贡献度达标、师徒关系有效
   - 结算奖励：师傅获得师傅奖励（经验、称号、贡献度），徒弟获得出师奖励
   - 通过邮件 Grain 发放奖励邮件（带附件）
   - 更新 Mentorship 表 Status=1, IsGraduated=true, GraduateTime=now
   - 更新成就 Grain（出师成就解锁）→ 删除 Redis hundun:mentor:apprentice:{apprenticeId}
7. 返回 GraduateResult(true, masterReward, apprenticeReward)
8. 前端显示出师结算界面，师傅与徒弟各收到奖励邮件
```

### 8.9 玩家上线联动与异常重试时序

```
玩家上线联动：
1. 玩家登录，SessionGrain.OnPlayerLoginAsync(playerId)
2. 会话 Grain 通知各社交子系统：
   2.1 IFriendGrain.OnPlayerOnlineStatusChangedAsync(playerId, true)
       - 更新自身在线状态，遍历好友列表向每个在线好友推送 FriendStatusPush
   2.2 IMailGrain.OnPlayerLoginAsync(playerId)
       - 检查未读邮件推送 MailUnreadCountPush，离线邮件批量补推 MailReceivedPush
   2.3 IGuildGrain（通过 hundun:guild:player:{playerId} 查询门派）
       - 向门派其他在线成员推送 GuildMemberOnlinePush
   2.4 IAchievementGrain 检查登录相关成就；ILeaderboardGrain 不主动通知（被动查询）
3. 前端收到多个推送，UI 同步刷新好友/邮件/门派/成就状态

商城扣费超时异常重试：
1. ShopGrain.PurchaseAsync 调用 IEconomyGrain.DeductAsync 超时（>5s）
2. ShopGrain 标记订单 Status=Pending（保留），不回滚库存与限购
3. 启动定时任务（每 30s 查询一次订单状态）
4. 经济系统恢复后回调 OnPaymentResultAsync(orderId, result)
   - 成功 → 走发放流程，推送 ShopPurchasePush(Success)
   - 失败 → 回滚库存与限购，订单 Status=Failed，推送 ShopPurchasePush(Failed)
5. 10 分钟后仍无回调，ShopGrain 主动查询经济系统订单状态，确认失败后回滚
6. 客户端按 OrderId 幂等处理推送，避免重复发放
```

---

## 附录 A：错误码定义

| 错误码 | 含义 | 错误码 | 含义 |
| --- | --- | --- | --- |
| 0 | 成功 | 4003 | 邮件已过期 |
| 1001 | 参数无效 | 5001 | 师傅名额已满 |
| 1002 | 权限不足 | 5002 | 等级不符 |
| 2001 | 门派名已存在 | 6001 | 排行榜不存在 |
| 2002 | 门派成员已满 | 7001 | 成就未解锁 |
| 2003 | 已在门派中 | 7002 | 奖励已领取 |
| 3001 | 好友列表已满 | 8001 | 商品已下架 |
| 3002 | 好友已存在 | 8002 | 库存不足 |
| 3003 | 目标在黑名单 | 8003 | 限购已达上限 |
| 4001 | 邮箱已满 | 8004 | 货币不足 |
| 4002 | 附件已领取 | 8005 | 促销已结束 |
| 9001 | 副本未开放 | 9002 | 匹配超时 |
| 9003 | 副本进入超时 | | |

## 附录 B：版本演进约定

- **MemoryPack DTO**：字段只能追加末尾，禁止改已有顺序与类型；旧客户端忽略尾部字段。**消息枚举**：新增类型追加分组下一可用值，禁止复用废弃值。
- **Grain 接口**：新增方法须有默认实现或版本化接口（`IGuildGrainV2`）保证旧 Silo 兼容。
- **SqlServer 表**：新增列允许 NULL 或带默认值，禁止删列（用软删除标记）；索引变更低峰期执行。**Redis 键**：新键用新命名，旧键设过渡 TTL 自动失效，不主动 DEL 避免影响在线 Grain。

---

> 文档结束。本契约为社交与商城子系统的权威接口定义，所有后端实现与前端对接以此为准。

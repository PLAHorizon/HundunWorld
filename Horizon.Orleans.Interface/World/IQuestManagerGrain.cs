using System;
using System.Collections.Generic;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 任务玩法管理 Grain（全局单例，主键固定 <see cref="QuestManagerKeys.Singleton"/>）。
/// <para>
/// 它管三件事：① 模板目录的内存缓存与版本号（"配置是否变了"的唯一对账基准）；
/// ② 实时发布 / 关闭（不停服改玩法）；③ 进行中实例的持有者登记，供"强制取消"定向下发。
/// </para>
/// <para>
/// <b>为什么上下线状态读库而不是只留在内存</b>：Grain 去激活会静默清零进程内字段，
/// 只放字段的后果是"运营关掉的任务在一次重启后自己活了"。所以 publish_state 落在
/// <c>Game_HunduShijie_QuestTemplate</c>，内存里只有缓存。
/// </para>
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IQuestManagerGrain : IGrainWithIntegerKey
{
    /// <summary>取模板目录快照（默认走缓存，缓存过期才回库）。</summary>
    /// <param name="forceRefresh">true = 立即回库，绕过缓存。</param>
    Task<QuestCatalogSnapshot> GetCatalogAsync(bool forceRefresh = false);

    /// <summary>当前目录版本号：任一任务的内容或上下线变化都会让它自增。</summary>
    Task<int> GetCatalogVersionAsync();

    /// <summary>实时发布一条任务（写库 + 失效缓存 + 版本自增）。</summary>
    Task<QuestOpResult> PublishAsync(int questId);

    /// <summary>
    /// 实时关闭一条任务。
    /// </summary>
    /// <param name="questId">任务模板 id。</param>
    /// <param name="forceAbandonActive">true = 同时强制放弃所有进行中的实例；默认 false，只停止新的接取，
    /// 已完成的努力仍可提交（热更不该抹掉玩家进度）。</param>
    Task<QuestOpResult> UnpublishAsync(int questId, bool forceAbandonActive = false);

    /// <summary>
    /// 内容热更新（预留接口）：把一份任务草稿写回模板表，服务端侧校验通过才落库。
    /// <para>与 <see cref="PublishAsync"/> 一样，改动只影响<b>之后</b>新接的实例；
    /// 已接实例继续用它接取时的目标与奖励快照，避免"接到一半奖励被换了"。</para>
    /// </summary>
    /// <param name="draftJson">任务草稿 JSON（与任务编辑器 / CLI 同一份格式）。</param>
    Task<QuestOpResult> UpsertTemplateAsync(string draftJson);

    /// <summary>定向取消某个角色身上的某个任务（运营兜底：卡死的任务、错误发放的前置）。</summary>
    Task<QuestOpResult> CancelCharacterQuestAsync(Guid characterId, int questId, string reason);

    /// <summary>
    /// 某发布者（NPC / 物品 / 物件）当前能向这个角色提供的任务列表。
    /// 已按发布状态、等级门槛、前置任务、是否已接/已交过滤——权威在这里，客户端只显示。
    /// </summary>
    /// <param name="giverKind">发布者类别（1NPC 3物品 4物件）。</param>
    /// <param name="giverTemplateId">发布者模板 id。</param>
    /// <param name="characterLevel">
    /// 角色等级。<b>必须由网关从角色 Grain 取到再传进来</b>，不能直接用客户端报的数，
    /// 否则等级门槛形同虚设。
    /// </param>
    /// <param name="completedQuestIds">该角色已完成的任务 id（网关从 QuestGrain 读）。</param>
    /// <param name="activeQuestIds">该角色进行中的任务 id（网关从 QuestGrain 读）。</param>
    Task<List<QuestOfferSummary>> GetOfferedQuestsAsync(int giverKind, int giverTemplateId, int characterLevel,
        int[] completedQuestIds, int[] activeQuestIds);

    /// <summary>
    /// 取走并清空某角色的"待撤销"任务队列。
    /// <para>强制关闭走的是记账而不是跨 Grain 直呼：<see cref="UnpublishAsync"/> 只把持有者写进队列，
    /// 由网关在轮询/刷新时取走并逐个执行放弃。这样任务 Grain 与管理 Grain 之间不会出现互相等待的调用环。</para>
    /// </summary>
    Task<int[]> TakeRevokedQuestsAsync(Guid characterId);

    /// <summary>角色接取成功后登记持有者（供 <see cref="UnpublishAsync"/> 的强制取消定位到人）。</summary>
    Task NotifyAcceptedAsync(int questId, Guid characterId);

    /// <summary>任务提交或放弃后注销持有者。</summary>
    Task NotifyFinishedAsync(int questId, Guid characterId);

    /// <summary>某个任务当前是否有在线持有者（调试与审计用）。</summary>
    Task<int> CountHoldersAsync(int questId);
}

/// <summary>任务管理 Grain 的主键约定。</summary>
public static class QuestManagerKeys
{
    /// <summary>全局单例主键。</summary>
    public const long Singleton = 1;
}

/// <summary>一次任务管理操作的结果。</summary>
[GenerateSerializer]
public sealed class QuestOpResult
{
    /// <summary>是否成功。</summary>
    [Id(0)] public bool Success { get; set; }
    /// <summary>面向运营/日志的说明。</summary>
    [Id(1)] public string Message { get; set; } = string.Empty;
    /// <summary>操作后的目录版本号；失败时为 0。</summary>
    [Id(2)] public int CatalogVersion { get; set; }
    /// <summary>被强制取消的实例数（仅 <see cref="IQuestManagerGrain.UnpublishAsync"/> 用）。</summary>
    [Id(3)] public int AbandonedCount { get; set; }

    /// <summary>构造失败结果。</summary>
    public static QuestOpResult Fail(string message) => new QuestOpResult { Success = false, Message = message };

    /// <summary>构造成功结果。</summary>
    public static QuestOpResult Ok(string message, int version = 0, int abandoned = 0)
        => new QuestOpResult { Success = true, Message = message, CatalogVersion = version, AbandonedCount = abandoned };
}

/// <summary>模板目录快照。</summary>
[GenerateSerializer]
public sealed class QuestCatalogSnapshot
{
    /// <summary>目录版本号。</summary>
    [Id(0)] public int CatalogVersion { get; set; }
    /// <summary>快照生成时间（UTC）。</summary>
    [Id(1)] public DateTime BuiltAtUtc { get; set; }
    /// <summary>目录条目（含下线任务，便于审计；<see cref="QuestCatalogEntry.PublishState"/> 区分状态）。</summary>
    [Id(2)] public QuestCatalogEntry[] Entries { get; set; } = Array.Empty<QuestCatalogEntry>();
}

/// <summary>目录里的一条任务模板（运行时可见字段，刻意不含内部字段）。</summary>
[GenerateSerializer]
public sealed class QuestCatalogEntry
{
    /// <summary>任务模板 id。</summary>
    [Id(0)] public int QuestId { get; set; }
    /// <summary>任务名。</summary>
    [Id(1)] public string QuestName { get; set; } = string.Empty;
    /// <summary>0主线 1支线 2日常 3周常。</summary>
    [Id(2)] public int QuestType { get; set; }
    /// <summary>接取等级门槛。</summary>
    [Id(3)] public int LevelRequire { get; set; }
    /// <summary>0下线 1已发布。</summary>
    [Id(4)] public int PublishState { get; set; }
    /// <summary>发布者类别（1NPC 3物品 4物件）。</summary>
    [Id(5)] public int GiverKind { get; set; }
    /// <summary>发布者模板 id。</summary>
    [Id(6)] public int GiverTemplateId { get; set; }

    /// <summary>交付者类别（与 <see cref="GiverKind"/> 同一套编号；0 = 沿用发布者）。</summary>
    [Id(14)] public int TurnInKind { get; set; }

    /// <summary>交付者模板 id（0 = 沿用发布者；与发布者一起为 0 时表示不限）。</summary>
    [Id(15)] public int TurnInTemplateId { get; set; }
    /// <summary>内容版本号。</summary>
    [Id(7)] public int ContentVersion { get; set; }
    /// <summary>前置任务 id 列表。</summary>
    [Id(8)] public int[] PrerequisiteQuestIds { get; set; } = Array.Empty<int>();
    /// <summary>任务描述（客户端画可接列表用，权威文本仍以库里这份为准）。</summary>
    [Id(9)] public string Description { get; set; } = string.Empty;
    /// <summary>可重复次数：0=一次性，N=最多交 N 次，决定交过之后还提不提供。</summary>
    [Id(10)] public int RepeatLimit { get; set; }
    /// <summary>模板 <c>objectives</c> 列原文：由网关解码成线格式，Grain 侧不复制一套解码规则。</summary>
    [Id(11)] public string ObjectivesJson { get; set; } = string.Empty;
    /// <summary>模板 <c>rewards</c> 列原文（同上）。</summary>
    [Id(12)] public string RewardsJson { get; set; } = string.Empty;
    /// <summary>发布者显示名（服务端按目录解析：NPC 名 / 物品名；解析不到时留空，不编一个假名）。</summary>
    [Id(13)] public string GiverName { get; set; } = string.Empty;
}

/// <summary>NPC/物品"能提供"的任务摘要（给客户端画可接列表用）。</summary>
[GenerateSerializer]
public sealed class QuestOfferSummary
{
    /// <summary>任务模板 id。</summary>
    [Id(0)] public int QuestId { get; set; }
    /// <summary>任务名。</summary>
    [Id(1)] public string QuestName { get; set; } = string.Empty;
    /// <summary>任务描述。</summary>
    [Id(2)] public string Description { get; set; } = string.Empty;
    /// <summary>0主线 1支线 2日常 3周常。</summary>
    [Id(3)] public int QuestType { get; set; }
    /// <summary>内容版本号。</summary>
    [Id(4)] public int ContentVersion { get; set; }
    /// <summary>是否已被该角色接过（用于画"已接/可交"角标）。</summary>
    [Id(5)] public bool AlreadyAccepted { get; set; }
    /// <summary>是否已完成过且不可重复（画灰）。</summary>
    [Id(6)] public bool Finished { get; set; }
    /// <summary>接取等级门槛（0=不限）；列表里就带上，玩家能看懂这条为什么灰着。</summary>
    [Id(7)] public int LevelRequire { get; set; }
    /// <summary>模板 <c>objectives</c> 列原文，由网关解码成线格式（解码规则只留一份）。</summary>
    [Id(8)] public string ObjectivesJson { get; set; } = string.Empty;
    /// <summary>模板 <c>rewards</c> 列原文。</summary>
    [Id(9)] public string RewardsJson { get; set; } = string.Empty;
    /// <summary>发布者类别与模板 id（0/0 = 未配置发布者）。</summary>
    [Id(10)] public int GiverKind { get; set; }
    [Id(11)] public int GiverTemplateId { get; set; }
    /// <summary>发布者显示名，由服务端解析（客户端不再需要知道模板 id 就能画"这是谁给的"）。</summary>
    [Id(12)] public string GiverName { get; set; } = string.Empty;
}

/// <summary>任务草稿的原始字段（<see cref="IQuestManagerGrain.UpsertTemplateAsync"/> 的入参形态由 JSON 承载，这里只放结果）。</summary>
[GenerateSerializer]
public sealed class QuestValidationReport
{
    /// <summary>错误级条目。</summary>
    [Id(0)] public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>警告级条目。</summary>
    [Id(1)] public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>持有者登记表的持久化状态。</summary>
[GenerateSerializer]
public sealed class QuestManagerState
{
    /// <summary>任务 id → 持有该任务的角色集合。强制取消要靠它定位到人，必须落盘。</summary>
    [Id(0)] public Dictionary<int, List<Guid>> Holders { get; set; } = new Dictionary<int, List<Guid>>();
    /// <summary>已观察到的目录版本号。</summary>
    [Id(1)] public int CatalogVersion { get; set; }
    /// <summary>热更新审计流水尾部若干条（不落专用表时的最小可追溯手段）。</summary>
    [Id(2)] public List<string> Audit { get; set; } = new List<string>();
    /// <summary>
    /// 待撤销队列：角色 → 被强制关闭且需要放弃的任务 id。
    /// 必须落盘——Grain 去激活会静默清零进程内字段，只在内存里记就等于"关单漏人"。
    /// </summary>
    [Id(3)] public Dictionary<Guid, List<int>> RevokedQueues { get; set; } = new Dictionary<Guid, List<int>>();
}

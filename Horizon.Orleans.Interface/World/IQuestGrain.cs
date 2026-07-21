using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 任务 Grain 契约（P2.5）。<br/>
/// Grain Primary Key = questInstanceId（由 QuestManagerGrain 在接取时分配）。<br/>
/// 负责：任务状态机（接取/进行中/完成/奖励）、目标追踪、条件判断。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IQuestGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 初始化任务实例（接取时调用）。
    /// </summary>
    Task InitializeAsync(QuestInitData data);

    /// <summary>
    /// 更新任务目标进度（杀怪/采集/交互等）。
    /// </summary>
    /// <param name="objectiveType">目标类型。</param>
    /// <param name="targetId">目标 ID（怪物模板/物品 ID/NPC ID）。</param>
    /// <param name="count">本次完成数量。</param>
    /// <returns>更新后的任务状态。</returns>
    Task<QuestState> UpdateObjectiveAsync(QuestObjectiveType objectiveType, int targetId, int count = 1);

    /// <summary>
    /// 提交任务（所有目标完成后调用）。
    /// </summary>
    /// <returns>提交结果（含奖励信息）。</returns>
    Task<QuestSubmitResult> SubmitAsync();

    /// <summary>
    /// 放弃任务。
    /// </summary>
    Task AbandonAsync();

    /// <summary>
    /// 获取当前任务状态。
    /// </summary>
    Task<QuestState> GetStateAsync();
}

/// <summary>
/// 任务初始化数据。
/// </summary>
[GenerateSerializer]
public sealed class QuestInitData
{
    [Id(0)] public int QuestTemplateId { get; set; }
    [Id(1)] public string QuestName { get; set; } = string.Empty;
    [Id(2)] public long CharacterId { get; set; }
    [Id(3)] public QuestObjectiveData[] Objectives { get; set; } = Array.Empty<QuestObjectiveData>();
    [Id(4)] public QuestRewardData Reward { get; set; } = new();
    [Id(5)] public int RequiredLevel { get; set; }
    [Id(6)] public int[] PrerequisiteQuestIds { get; set; } = Array.Empty<int>();
}

/// <summary>
/// 任务目标定义。
/// </summary>
[GenerateSerializer]
public sealed class QuestObjectiveData
{
    [Id(0)] public QuestObjectiveType Type { get; set; }
    [Id(1)] public int TargetId { get; set; }
    [Id(2)] public int RequiredCount { get; set; }
    [Id(3)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 任务奖励。
/// </summary>
[GenerateSerializer]
public sealed class QuestRewardData
{
    [Id(0)] public long ExpReward { get; set; }
    [Id(1)] public long GoldReward { get; set; }
    [Id(2)] public int[] ItemRewardIds { get; set; } = Array.Empty<int>();
    [Id(3)] public int[] ItemRewardCounts { get; set; } = Array.Empty<int>();
}

/// <summary>
/// 任务目标类型。
/// </summary>
[GenerateSerializer]
public enum QuestObjectiveType : byte
{
    /// <summary>击杀怪物。</summary>
    KillMonster = 0,
    /// <summary>采集物品。</summary>
    CollectItem = 1,
    /// <summary>与 NPC 对话。</summary>
    TalkToNpc = 2,
    /// <summary>到达指定地点。</summary>
    ReachLocation = 3,
    /// <summary>使用道具。</summary>
    UseItem = 4,
    /// <summary>完成副本。</summary>
    CompleteInstance = 5,
}

/// <summary>
/// 任务状态。
/// </summary>
[GenerateSerializer]
public sealed class QuestState
{
    [Id(0)] public long QuestInstanceId { get; set; }
    [Id(1)] public int QuestTemplateId { get; set; }
    [Id(2)] public string QuestName { get; set; } = string.Empty;
    [Id(3)] public QuestPhase Phase { get; set; }
    [Id(4)] public QuestObjectiveProgress[] Objectives { get; set; } = Array.Empty<QuestObjectiveProgress>();
    [Id(5)] public DateTime AcceptTime { get; set; }
    [Id(6)] public bool AreAllObjectivesComplete { get; set; }
}

/// <summary>
/// 任务目标进度。
/// </summary>
[GenerateSerializer]
public sealed class QuestObjectiveProgress
{
    [Id(0)] public QuestObjectiveType Type { get; set; }
    [Id(1)] public int TargetId { get; set; }
    [Id(2)] public int CurrentCount { get; set; }
    [Id(3)] public int RequiredCount { get; set; }
    [Id(4)] public bool IsComplete { get; set; }
    [Id(5)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 任务阶段。
/// </summary>
[GenerateSerializer]
public enum QuestPhase : byte
{
    /// <summary>已接取/进行中。</summary>
    Active = 0,
    /// <summary>目标全部完成（待提交）。</summary>
    ReadyToSubmit = 1,
    /// <summary>已提交/完成。</summary>
    Completed = 2,
    /// <summary>已放弃。</summary>
    Abandoned = 3,
    /// <summary>失败（超时/条件不满足）。</summary>
    Failed = 4,
}

/// <summary>
/// 任务提交结果。
/// </summary>
[GenerateSerializer]
public sealed class QuestSubmitResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(2)] public QuestRewardData Reward { get; set; } = new();
}

using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 任务系统Grain接口 - 负责任务接取、进度更新、完成、放弃
    /// </summary>
    public interface IQuestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 接受任务
        /// </summary>
        /// <param name="questId">任务模板ID</param>
        /// <param name="questName">任务名称</param>
        /// <param name="description">任务描述</param>
        /// <param name="questType">任务类型 (0=主线, 1=支线, 2=日常, 3=周常)</param>
        /// <param name="level">任务等级要求</param>
        /// <param name="rewards">任务奖励</param>
        /// <returns>是否接受成功</returns>
        Task<bool> AcceptQuestAsync(int questId, string questName, string description, int questType, int level, Dictionary<string, int> rewards);

        /// <summary>
        /// 更新任务目标进度
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <param name="objectiveIndex">目标索引</param>
        /// <param name="progressCount">进度增量</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateQuestProgressAsync(int questId, int objectiveIndex, int progressCount);

        /// <summary>
        /// 完成任务（领取奖励）
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <returns>任务完成结果</returns>
        Task<QuestCompleteResult> CompleteQuestAsync(int questId);

        /// <summary>
        /// 放弃任务
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <returns>是否放弃成功</returns>
        Task<bool> AbandonQuestAsync(int questId);

        /// <summary>
        /// 获取所有进行中的任务
        /// </summary>
        /// <returns>进行中的任务列表</returns>
        Task<List<QuestData>> GetActiveQuestsAsync();

        /// <summary>
        /// 获取已完成的任务列表
        /// </summary>
        /// <returns>已完成的任务列表</returns>
        Task<List<QuestData>> GetCompletedQuestsAsync();

        /// <summary>
        /// 获取单个任务详情
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <returns>任务详情</returns>
        Task<QuestData> GetQuestAsync(int questId);

        /// <summary>
        /// 添加任务目标
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <param name="objectiveType">目标类型</param>
        /// <param name="description">目标描述</param>
        /// <param name="requiredCount">需要完成数量</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddQuestObjectiveAsync(int questId, string objectiveType, string description, int requiredCount);
    }

    /// <summary>
    /// 副本系统Grain接口 - 负责副本创建、进入、通关、奖励发放
    /// </summary>
    public interface IDungeonGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 创建副本实例
        /// </summary>
        /// <param name="dungeonTemplateId">副本模板ID</param>
        /// <param name="dungeonName">副本名称</param>
        /// <param name="difficulty">难度 (0=普通, 1=困难, 2=英雄, 3=地狱)</param>
        /// <param name="maxPlayers">最大玩家数</param>
        /// <param name="timeLimitMinutes">时间限制（分钟）</param>
        /// <returns>是否创建成功</returns>
        Task<bool> CreateDungeonAsync(int dungeonTemplateId, string dungeonName, int difficulty, int maxPlayers, int timeLimitMinutes);

        /// <summary>
        /// 玩家进入副本
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>是否进入成功</returns>
        Task<bool> EnterDungeonAsync(Guid playerId);

        /// <summary>
        /// 玩家离开副本
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>是否离开成功</returns>
        Task<bool> LeaveDungeonAsync(Guid playerId);

        /// <summary>
        /// 击败Boss/完成阶段
        /// </summary>
        /// <param name="bossId">Boss/阶段ID</param>
        /// <returns>是否击败成功</returns>
        Task<bool> DefeatBossAsync(int bossId);

        /// <summary>
        /// 完成副本
        /// </summary>
        /// <returns>副本完成结果</returns>
        Task<DungeonCompleteResult> CompleteDungeonAsync();

        /// <summary>
        /// 获取副本信息
        /// </summary>
        /// <returns>副本信息</returns>
        Task<DungeonData> GetDungeonInfoAsync();

        /// <summary>
        /// 检查副本是否超时
        /// </summary>
        /// <returns>是否超时</returns>
        Task<bool> IsTimedOutAsync();

        /// <summary>
        /// 获取副本内玩家列表
        /// </summary>
        /// <returns>玩家列表</returns>
        Task<List<Guid>> GetPlayersAsync();

        /// <summary>
        /// 添加Boss/阶段到副本
        /// </summary>
        /// <param name="bossId">Boss/阶段ID</param>
        /// <param name="bossName">Boss名称</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddBossAsync(int bossId, string bossName);
    }

    #region 任务系统数据模型

    /// <summary>
    /// 任务数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int QuestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string QuestName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        /// <summary>
        /// 任务类型 (0=主线, 1=支线, 2=日常, 3=周常)
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int QuestType { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Level { get; set; }

        /// <summary>
        /// 任务状态 (0=进行中, 1=可提交, 2=已完成, 3=已放弃)
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public List<QuestObjectiveData> Objectives { get; set; } = new();

        [MemoryPackOrder(7)]
        [Id(7)]
        public Dictionary<string, int> Rewards { get; set; } = new();

        [MemoryPackOrder(8)]
        [Id(8)]
        public DateTime AcceptTime { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime? CompleteTime { get; set; }
    }

    /// <summary>
    /// 任务目标数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestObjectiveData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public string ObjectiveType { get; set; } = "";

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int RequiredCount { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentCount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// 任务完成结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestCompleteResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int QuestId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public Dictionary<string, int> Rewards { get; set; } = new();
    }

    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum QuestProgressStatus
    {
        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 0,

        /// <summary>
        /// 可提交（所有目标已完成）
        /// </summary>
        ReadyToSubmit = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 已放弃
        /// </summary>
        Abandoned = 3
    }

    #endregion

    #region 副本系统数据模型

    /// <summary>
    /// 副本数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int DungeonTemplateId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string DungeonName { get; set; } = "";

        /// <summary>
        /// 难度 (0=普通, 1=困难, 2=英雄, 3=地狱)
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Difficulty { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int CurrentPlayers { get; set; }

        /// <summary>
        /// 副本状态 (0=等待中, 1=进行中, 2=已完成, 3=失败)
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int Status { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int TimeLimitMinutes { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public DateTime? StartTime { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsCreated { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public List<DungeonBossData> Bosses { get; set; } = new();

        [MemoryPackOrder(10)]
        [Id(10)]
        public int DefeatedBossCount { get; set; }
    }

    /// <summary>
    /// 副本Boss数据
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonBossData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int BossId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string BossName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public bool IsDefeated { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime? DefeatTime { get; set; }
    }

    /// <summary>
    /// 副本完成结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class DungeonCompleteResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int DungeonTemplateId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Difficulty { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int TotalBosses { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int DefeatedBosses { get; set; }

        /// <summary>
        /// 通关时间（秒）
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public double ClearTimeSeconds { get; set; }
    }

    /// <summary>
    /// 副本状态枚举
    /// </summary>
    public enum DungeonStatus
    {
        /// <summary>
        /// 等待中
        /// </summary>
        Waiting = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 2,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 3
    }

    /// <summary>
    /// 副本难度枚举
    /// </summary>
    public enum DungeonDifficulty
    {
        /// <summary>
        /// 普通
        /// </summary>
        Normal = 0,

        /// <summary>
        /// 困难
        /// </summary>
        Hard = 1,

        /// <summary>
        /// 英雄
        /// </summary>
        Heroic = 2,

        /// <summary>
        /// 地狱
        /// </summary>
        Hell = 3
    }

    #endregion
}

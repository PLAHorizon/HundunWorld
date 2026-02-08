using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 任务系统Grain接口 - 负责任务接取、进度更新、完成、放弃
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
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
        Task<QuestData?> GetQuestAsync(int questId);

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
    [global::Orleans.CodeGeneration.Version(1)]
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
}

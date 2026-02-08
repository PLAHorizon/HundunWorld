using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 成就系统Grain接口 - 负责成就追踪与解锁管理
    /// Key格式: 玩家ID (Guid)
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IAchievementGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 注册成就
        /// </summary>
        /// <param name="achievementId">成就ID</param>
        /// <param name="name">成就名称</param>
        /// <param name="description">成就描述</param>
        /// <param name="category">成就分类</param>
        /// <param name="points">成就点数</param>
        /// <param name="targetProgress">目标进度</param>
        /// <param name="rewards">奖励</param>
        /// <returns>是否成功</returns>
        Task<bool> RegisterAchievementAsync(int achievementId, string name, string description, int category, int points,
            int targetProgress, Dictionary<string, int>? rewards = null);

        /// <summary>
        /// 更新成就进度
        /// </summary>
        /// <param name="achievementId">成就ID</param>
        /// <param name="progressIncrement">进度增量</param>
        /// <returns>解锁结果（null表示未解锁）</returns>
        Task<AchievementUnlockResult?> UpdateProgressAsync(int achievementId, int progressIncrement);

        /// <summary>
        /// 获取所有成就
        /// </summary>
        /// <returns>成就列表</returns>
        Task<List<AchievementData>> GetAllAchievementsAsync();

        /// <summary>
        /// 获取已解锁成就
        /// </summary>
        /// <returns>已解锁成就列表</returns>
        Task<List<AchievementData>> GetUnlockedAchievementsAsync();

        /// <summary>
        /// 获取特定分类的成就
        /// </summary>
        /// <param name="category">成就分类</param>
        /// <returns>成就列表</returns>
        Task<List<AchievementData>> GetAchievementsByCategoryAsync(int category);

        /// <summary>
        /// 获取成就详情
        /// </summary>
        /// <param name="achievementId">成就ID</param>
        /// <returns>成就数据</returns>
        Task<AchievementData?> GetAchievementAsync(int achievementId);

        /// <summary>
        /// 获取总成就点数
        /// </summary>
        /// <returns>总点数</returns>
        Task<int> GetTotalPointsAsync();

        /// <summary>
        /// 获取已解锁成就数量
        /// </summary>
        /// <returns>已解锁数量</returns>
        Task<int> GetUnlockedCountAsync();
    }
}

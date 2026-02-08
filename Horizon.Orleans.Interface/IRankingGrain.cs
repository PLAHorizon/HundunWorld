using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 排行榜系统Grain接口 - 负责排行榜管理
    /// Key格式: RankingType (int)
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IRankingGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 初始化排行榜
        /// </summary>
        /// <param name="rankingType">排行榜类型</param>
        /// <param name="rankingName">排行榜名称</param>
        /// <param name="maxEntries">最大排名数</param>
        /// <returns>是否成功</returns>
        Task<bool> InitializeAsync(int rankingType, string rankingName, int maxEntries = 100);

        /// <summary>
        /// 更新玩家分数
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <param name="playerName">玩家名称</param>
        /// <param name="score">分数</param>
        /// <returns>更新后的排名（-1表示未进入排行榜）</returns>
        Task<int> UpdateScoreAsync(Guid playerId, string playerName, long score);

        /// <summary>
        /// 获取排行榜前N名
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <returns>排行榜条目列表</returns>
        Task<List<RankingEntry>> GetTopEntriesAsync(int count);

        /// <summary>
        /// 获取玩家排名
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>排名（-1表示未上榜）</returns>
        Task<int> GetPlayerRankAsync(Guid playerId);

        /// <summary>
        /// 获取玩家排名条目
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>排名条目（null表示未上榜）</returns>
        Task<RankingEntry?> GetPlayerEntryAsync(Guid playerId);

        /// <summary>
        /// 获取排行榜信息
        /// </summary>
        /// <returns>排行榜信息</returns>
        Task<RankingInfo> GetRankingInfoAsync();

        /// <summary>
        /// 移除玩家排名
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <returns>是否成功</returns>
        Task<bool> RemovePlayerAsync(Guid playerId);

        /// <summary>
        /// 重置排行榜
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> ResetRankingAsync();
    }
}

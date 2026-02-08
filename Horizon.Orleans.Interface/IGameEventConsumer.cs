using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏事件消费者Grain接口 — 订阅Orleans Stream异步处理游戏事件
    /// 解耦战斗结果通知、日志统计等非关键路径
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameEventConsumerGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 初始化事件订阅（激活时自动调用）
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 获取已处理的事件统计
        /// </summary>
        /// <returns>事件处理统计信息</returns>
        Task<EventProcessingStats> GetProcessingStatsAsync();

        /// <summary>
        /// 获取最近处理的事件列表
        /// </summary>
        /// <param name="count">获取数量，默认20</param>
        /// <returns>最近处理的事件摘要列表</returns>
        Task<List<ProcessedEventSummary>> GetRecentEventsAsync(int count = 20);

        /// <summary>
        /// 重置统计计数器
        /// </summary>
        Task ResetStatsAsync();
    }
}

using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    /// <summary>
    /// 事件处理统计信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EventProcessingStats
    {
        /// <summary>总处理事件数</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long TotalEventsProcessed { get; set; }

        /// <summary>各事件类型处理计数</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, long> EventTypeCounters { get; set; } = new();

        /// <summary>处理失败计数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long FailedEvents { get; set; }

        /// <summary>最后处理事件时间（UTC Ticks）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long LastProcessedTimestamp { get; set; }

        /// <summary>订阅的命名空间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Namespace { get; set; } = string.Empty;

        /// <summary>统计起始时间（UTC Ticks）</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long StatsStartTimestamp { get; set; }
    }

    /// <summary>
    /// 已处理事件摘要
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ProcessedEventSummary
    {
        /// <summary>事件ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EventId { get; set; } = string.Empty;

        /// <summary>事件类型</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public GameEventType EventType { get; set; }

        /// <summary>触发角色ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong CharacterId { get; set; }

        /// <summary>处理时间（UTC Ticks）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ProcessedTimestamp { get; set; }

        /// <summary>是否处理成功</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public bool Success { get; set; }

        /// <summary>事件描述</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Description { get; set; } = string.Empty;
    }
}

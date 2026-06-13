using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 重连结果信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ReconnectResult
    {
        /// <summary>
        /// 重连是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 尝试次数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int AttemptNumber { get; set; }

        /// <summary>
        /// 使用的重连策略
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ReconnectStrategy Strategy { get; set; }

        /// <summary>
        /// 重连原因
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ReconnectReason Reason { get; set; }

        /// <summary>
        /// 重连持续时间（毫秒）
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public double Duration { get; set; }

        /// <summary>
        /// 重连过程中发生的异常（如果有的话）
        /// </summary>
        [MemoryPackIgnore]
        [Id(5)]
        public Exception Exception { get; set; }

        /// <summary>
        /// 结果时间戳
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(6)]
        public DateTime Timestamp { get; set; }
    }
}
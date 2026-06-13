using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 重连尝试信息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ReconnectAttempt
    {
        /// <summary>
        /// 尝试次数
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AttemptNumber { get; set; }

        /// <summary>
        /// 最大尝试次数
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int MaxAttempts { get; set; }

        /// <summary>
        /// 当前使用的重连策略
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
        /// 尝试时间戳
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime Timestamp { get; set; }
    }
}
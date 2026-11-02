using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    #region 角色属性加点

    /// <summary>
    /// 角色自由点分配（服务端权威）。字段序与客户端
    /// <c>CharacterAttributesComponent.AttributeAllocation</c> 一致：力量/敏捷/体质/智力 + 金木水火土。
    /// 纯数据载体（不含计算成员，避免 MemoryPack 序列化歧义），求和由消费方负责。
    /// <para>
    /// 第 4 项原为"内力"，2026-09-28 改叫"智力"。这里只改属性名、
    /// <see cref="MemoryPackOrderAttribute"/> 保持 3 不变 ⇒ 线上字节流兼容，
    /// 但**两端必须同版本部署**（旧服务端反序列化后该位仍是内力点数，语义已不同）。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [Serializable]
    public partial class AttributeAllocationData
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Strength { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Agility { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Health { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Intelligence { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int Metal { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int Wood { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int Water { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Fire { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public int Earth { get; set; }
    }

    /// <summary>
    /// 属性加点请求：一次提交**期望终态**（九项全量），而非增量。
    /// <para>
    /// 客户端"预览 + 确认"交互天然是一次性提交多点的批量操作，用终态可让请求幂等、
    /// 且服务端只需做一次总和校验；全零即"重置加点"，无需单独的 reset 消息。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(235, Enums.ServiceType.Game)]
    public partial class AttributeAllocateRequest : MessageUnion, INetworkMessage
    {
        /// <summary>角色ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>期望的分配终态（各项 ≥ 0，总和 ≤ 点池）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public AttributeAllocationData Allocation { get; set; } = new();

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.AttributeAllocate;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 属性加点响应：无论成功失败都回服务端当前权威分配与点池，
    /// 客户端据此收敛（失败即回滚预览），不需要再发一次查询。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(236, Enums.ServiceType.Game)]
    public partial class AttributeAllocateResponse : MessageUnion, INetworkMessage
    {
        /// <summary>是否已按期望终态提交</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>结果说明（失败原因）</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>角色ID</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong CharacterId { get; set; }

        /// <summary>服务端当前权威分配（失败时为未变更的原状）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public AttributeAllocationData Allocation { get; set; } = new();

        /// <summary>点池上限（等级 × 每级点数）</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int TotalPoints { get; set; }

        /// <summary>剩余可分配点数</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int AvailablePoints { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.AttributeAllocate;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}

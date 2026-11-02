using System;
using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 商人货架上的一行（服务端算好的价格与库存，客户端只画）。
    /// <para><b>价格一律服务端算</b>：客户端即使改了显示价，也改不了成交价——
    /// 买卖回执里的金额由服务端在成交时重新计算，与本 DTO 无关（它只是展示）。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ShopItemDto
    {
        /// <summary>窗口里的格子（数据决定顺序，客户端不重排）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int Slot { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int ItemId { get; set; }

        /// <summary>买入价（玩家付出）。0 = 这件不卖。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long BuyPrice { get; set; }

        /// <summary>收购价（玩家卖得）。0 = 商人不收这件。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long SellPrice { get; set; }

        /// <summary>剩余件数；<see cref="Unlimited"/> 为真时无意义（约定为 −1）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Remaining { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public bool Unlimited { get; set; }
    }

    /// <summary>
    /// 打开商人窗口。<b>只带运行时实体 id</b>——NPC 身份由服务端从 DS 实体注册表解出并核对邻近，
    /// 与任务交互同一条纪律（客户端声明"我和 NPC 3 说过话"不算数）。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(250, ServiceType.Trade)]
    public partial class ShopOpenRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopOpenRequestMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong EntityId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }

    /// <summary>商人窗口回执：货物清单 + 当前金币。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(251, ServiceType.Trade)]
    public partial class ShopOpenResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopOpenResultMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Ok { get; set; }

        /// <summary>失败短码（<c>npc_not_found</c> / <c>too_far</c> / <c>not_a_vendor</c> …）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>服务端认定的 NPC 编号（失败时为 0）。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int NpcId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long Gold { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public ShopItemDto[] Items { get; set; } = Array.Empty<ShopItemDto>();

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }

    /// <summary>购买请求（按格子）。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(252, ServiceType.Trade)]
    public partial class ShopBuyRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopBuyRequestMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong EntityId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Slot { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }

    /// <summary>购买回执。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(253, ServiceType.Trade)]
    public partial class ShopBuyResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopBuyResultMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Ok { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Reason { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Slot { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        /// <summary>实际成交金额（服务端算的）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long TotalCost { get; set; }

        /// <summary>成交后的余额（失败时为当前余额）。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long GoldAfter { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }

    /// <summary>卖出请求。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(254, ServiceType.Trade)]
    public partial class ShopSellRequestMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopSellRequestMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong EntityId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int ItemId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }

    /// <summary>卖出回执。</summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(255, ServiceType.Trade)]
    public partial class ShopSellResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 路由权威是**基类**的 <c>Type</c>/<c>ServiceType</c>：网关按 Body 的这两项匹配 handler。
        /// 类里另有同名的历史字段（<c>Type</c>/<c>Service</c>），它们不参与接口映射——
        /// 少了这段构造，包头 ServiceType 恒为 0，商店请求会被"无 handler 匹配消息"直接丢弃。
        /// </summary>
        public ShopSellResultMessage()
        {
            base.Type = MessageType.Shop;
            base.ServiceType = ServiceType.Trade;
        }
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Ok { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Reason { get; set; } = string.Empty;

        [MemoryPackOrder(2)]
        [Id(2)]
        public int ItemId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Count { get; set; }

        /// <summary>单价（服务端算的）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long UnitPrice { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public long GoldAfter { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.Shop;

        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType Service { get; set; } = ServiceType.Trade;
    }
}

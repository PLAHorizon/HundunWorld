using Horizon.IM.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.IM.Message.Network
{
    /// <summary>
    /// IM消息头
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    public partial class IMMessageHeader
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 消息识别码
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string MessageId { get; set; } = "";

        /// <summary>
        /// 消息序列号
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long SequenceId { get; set; }

        /// <summary>
        /// 时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Timestamp { get; set; }

        /// <summary>
        /// IM消息类型码
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public IMMessageType MessageType { get; set; }

        /// <summary>
        /// IM服务类型
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public IMServiceType ServiceType { get; set; }

        /// <summary>
        /// 消息哈希值
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Hash { get; set; } = "";

        /// <summary>
        /// 消息标志位
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public uint Flags { get; set; }

        /// <summary>
        /// 扩展数据
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public Dictionary<string, string> ExtensionData { get; set; } = new();

        /// <summary>
        /// 用户鉴权令牌
        /// 包含用户登录时间、机器ID与PassportId的加密数据，仅网关层或服务端层可解析验证
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public string AuthToken { get; set; } = "";

        /// <summary>
        /// 客户端机器唯一标识符（由客户端通过 MachineIdentifier.GetMachineGuid() 获取，用于令牌机器ID验证）
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public string MachineId { get; set; } = "";

        /// <summary>
        /// 是否需要响应
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public bool RequireResponse { get; set; }

        /// <summary>
        /// 是否是响应消息
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool IsResponse { get; set; }

        /// <summary>
        /// 响应的消息ID
        /// </summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public string ResponseToMessageId { get; set; } = "";

        /// <summary>
        /// 消息优先级
        /// </summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public byte Priority { get; set; }

        /// <summary>
        /// 消息版本
        /// </summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public byte Version { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public IMMessageHeader()
        {
            MessageId = Guid.NewGuid().ToString("N");
            Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Version = 1;
            Priority = 0;
        }
    }
}

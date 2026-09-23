using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 消息头
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    public partial class MessageHeader
    {
        /// <summary>
        /// 游戏ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public uint GameId { get; set; }

        /// <summary>
        /// 分区ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public uint ZoneId { get; set; }

        /// <summary>
        /// 服务器ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public uint ServerId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong UserId { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 消息识别码
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string MessageId { get; set; } = "";

        /// <summary>
        /// 消息序列号
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long SequenceId { get; set; }

        /// <summary>
        /// 时间戳（毫秒）
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 消息类型码
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType MessageType { get; set; }

        /// <summary>
        /// 服务类型（MessageType的别名）
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public ServiceType ServiceType { get; set; }

        /// <summary>
        /// 消息哈希值
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public string Hash { get; set; } = "";

        /// <summary>
        /// 消息标志位
        /// </summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public uint Flags { get; set; }

        /// <summary>
        /// 扩展数据。
        /// </summary>
        /// <remarks>
        /// 修复：原为 <c>Dictionary&lt;string, object&gt;</c>——<c>object</c> 无 MemoryPack union 且无 Orleans
        /// 序列化器，任意非基本类型值都会在序列化时抛异常（潜在崩溃）。改为 <c>Dictionary&lt;string, string&gt;</c>，
        /// 与 IM 侧 IMMessageHeader.ExtensionData 一致。
        /// 注意：因值为 <c>string</c>，写入方须自行做字符串转换（数值按 InvariantCulture 往返），不要再存 object。
        /// 现有使用方：<c>HundunWorld/Source/Game/Network/Handlers/EnhancedMessageProcessor.cs</c>
        /// 的 <c>PerformanceMonitoringInterceptor</c>（写入 "StartTime" 毫秒时间戳）。
        /// </remarks>
        [MemoryPackOrder(17)]
        [Id(17)]
        public Dictionary<string, string> ExtensionData { get; set; } = new();

        /// <summary>
        /// 用户鉴权令牌
        /// 包含用户登录时间、机器ID与PassportId的加密数据，角色进入游戏后还含角色Id，仅网关层或服务端层可解析验证
        /// </summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public string AuthToken { get; set; } = "";

        /// <summary>
        /// 客户端机器唯一标识符（由客户端通过 MachineIdentifier.GetMachineGuid() 获取，用于令牌机器ID验证）
        /// </summary>
        [MemoryPackOrder(19)]
        [Id(19)]
        public string MachineId { get; set; } = "";

        /// <summary>
        /// 幂等键：客户端为每次<b>非幂等</b>操作（如创建角色）生成的唯一标识，
        /// 请求重发（在途断线自动重试）时保持不变，服务端按键去重保证同一次操作只执行一次。
        /// 空字符串表示该请求不参与幂等去重（旧客户端/查询类请求）。
        /// </summary>
        [MemoryPackOrder(20)]
        [Id(20)]
        public string IdempotencyKey { get; set; } = "";

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
        public MessageHeader()
        {
            MessageId = Guid.NewGuid().ToString("N");
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Version = 1;
            Priority = 0;
        }
    }
}
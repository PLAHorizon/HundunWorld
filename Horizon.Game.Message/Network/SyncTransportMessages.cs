using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Network;

/// <summary>
/// 实时同步传输消息：在现有 HorizonMessagePacket 主通道内承载 SyncPacketCodec 生成的二进制帧。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial class SyncFrameMessage : MessageUnion, INetworkMessage
{
    /// <summary>SyncPacketCodec.Encode 输出的完整帧（含 6 字节同步帧头）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public byte[] Frame { get; set; } = [];

    /// <summary>同步包种类，冗余记录便于网关 fast-path 诊断。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public byte PacketKind { get; set; }

    /// <summary>同步协议版本。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public int ProtocolVersion { get; set; }

    /// <summary>服务类型。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public ServiceType ServiceType { get; set; } = ServiceType.Game;

    /// <summary>消息类型。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public MessageType Type { get; set; } = MessageType.SyncPacket;
}
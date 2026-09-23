using System;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Sync;
// 安全审查 2026-08：改用本项目带解压上限防护的 LZ4Pickler 副本（K4os 库版无长度上限，存在解压炸弹风险）。
using LZ4Pickler = Horizon.Game.Message.Utilities.LZ4Pickler;
using MemoryPack;

namespace Horizon.Game.Message.Network;

/// <summary>
/// 握手线路帧的纯逻辑编解码（无 UE / TouchSocket 依赖，可单测）。
/// <para>
/// 承载链路：<see cref="SyncPacketCodec.Encode"/> 产出 6 字节同步帧 → 包装为
/// <see cref="SyncFrameMessage"/> → 装入 <see cref="HorizonMessagePacket"/>（MessageUnion tag 169）
/// → 序列化后加 8 字节线路头（<see cref="WireHeaderLength"/>）。与服务端
/// <c>HorizonMessageAdapter.WrapPacket</c> 逐字节同构，供 UE5 客户端
/// <c>GameGatewaySessionClient</c> 与测试（Gateway.Tests 直接引用本库）共用同一份实现，
/// 避免双端打包/解析漂移。
/// </para>
/// </summary>
public static class SyncHandshakeWire
{
    /// <summary>线路头长度（字节）：4 载荷长度 LE + 1 消息类型 + 1 压缩标志 + 2 校验和。</summary>
    public const int WireHeaderLength = 8;

    /// <summary>超过该字节数的 <see cref="HorizonMessagePacket"/> 载荷启用 LZ4 压缩（与服务端 <c>HorizonMessageAdapter</c> 阈值一致）。</summary>
    public const int CompressionThreshold = 256;

    /// <summary>
    /// 把 <see cref="SyncPacket"/>（<see cref="HandshakePacket"/> 或 <see cref="HandshakeRejectPacket"/>）
    /// 打包为完整线路帧（8 字节头 + MemoryPack 载荷）。
    /// </summary>
    /// <param name="packet">同步包（含协议版本 / FormulaVersion / LocalCharacterId）。</param>
    /// <param name="authToken">网关鉴权令牌（登录链路返回的 ImAuthToken；为空时与服务端 auth-exempt 语义对齐）。</param>
    /// <param name="machineId">机器唯一标识符（与 ImAuthToken 绑定；为空时服务端按配置校验）。</param>
    /// <param name="requireResponse">
    /// 是否期待服务器响应。握手（默认 true）期待回显；会话期上行（Input / BaselineResync 等）为
    /// fire-and-forget 语义（false）——服务端对 Input 不返回同步响应（InputAck 走独立下行通道），
    /// 且 <c>MessageHandlerBase.HandleAsync</c> 仅在处理器返回非空包时才下发响应，因此该位不影响
    /// 服务端行为，仅用于线路头的语义标注。
    /// </param>
    public static byte[] Pack(SyncPacket packet, string? authToken = null, string? machineId = null, bool requireResponse = true)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // 1. 6 字节同步帧（SyncPacketCodec，与快照/输入共用同一通道格式）。
        SyncPacketCodec.Encode(packet, out var frame, out var frameLength);
        byte[] payload;
        try
        {
            payload = new byte[frameLength];
            Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }

        var syncFrame = new SyncFrameMessage
        {
            Frame = payload,
            PacketKind = (byte)packet.Kind,
            ProtocolVersion = packet.ProtocolVersion,
        };

        // 2. HorizonMessagePacket（header 字段与 GengDi GameGatewayClient 对齐）。
        var messagePacket = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
                IsResponse = false,
                RequireResponse = requireResponse,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameId = 1,
                ZoneId = 1,
                ServerId = 1,
                AuthToken = authToken ?? string.Empty,
                MachineId = machineId ?? string.Empty,
            },
            ServiceType = ServiceType.Game,
            Body = syncFrame,
        };

        // 3. 8 字节线路头（与服务端 HorizonMessageAdapter.WrapPacket 逐字节同构，见 <see cref="PackPacket"/>）。
        // 握手包很小，不压缩（compress: false）。
        return PackPacket(messagePacket, compress: false);
    }

    /// <summary>
    /// 把已构造的 <see cref="HorizonMessagePacket"/> 打包为完整线路帧（8 字节头 + 载荷）。
    /// <para>
    /// 与服务端 <see cref="HorizonGame.Core.HorizonMessageAdapter.PackPacket"/> 的 <c>WrapPacket</c>
    /// 逐字节同构：载荷长度 LE + 消息类型字节（> byte 上限时按低位截断）+ 压缩标志 +
    /// <see cref="HorizonProtocol.CalculateChecksum"/> 和校验。所有客户端（GengDi
    /// <c>GameGatewayClient</c> / UE5 <c>GameGatewaySessionClient</c>）复用同一份实现，
    /// 消除多实现线路层漂移。
    /// </para>
    /// </summary>
    /// <param name="packet">待打包的完整消息包（<see cref="HorizonMessagePacket.Header"/> 非空）。</param>
    /// <param name="compress">超过 <see cref="CompressionThreshold"/>（256B）时启用 LZ4 压缩；
    /// 仅在压缩后更小时采用（与服务端一致）。默认 true。</param>
    public static byte[] PackPacket(HorizonMessagePacket packet, bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Header is null)
        {
            throw new ArgumentException("消息包头不能为空。", nameof(packet));
        }

        var messageData = MemoryPackSerializer.Serialize(packet);
        return Wrap(messageData, packet.Header.MessageType, compress);
    }

    /// <summary>8 字节线路头封装（唯一实现，与 <c>HorizonMessageAdapter.WrapPacket</c> 逐字节一致）。</summary>
    private static byte[] Wrap(byte[] messageData, MessageType messageType, bool compress)
    {
        byte[] finalData = messageData;
        var isCompressed = false;

        if (compress && messageData.Length > CompressionThreshold)
        {
            var compressed = LZ4Pickler.Pickle(messageData);
            if (compressed.Length < messageData.Length)
            {
                finalData = compressed;
                isCompressed = true;
            }
        }

        var wire = new byte[WireHeaderLength + finalData.Length];
        BitConverter.GetBytes(finalData.Length).CopyTo(wire, 0);
        // MessageType 可 > byte 上限（如 SyncPacket = 1392），运行时按低位截断（与服务端 span[4] = (byte)messageType 一致）。
        wire[4] = unchecked((byte)messageType);
        wire[5] = isCompressed ? (byte)1 : (byte)0;
        BitConverter.GetBytes(HorizonProtocol.CalculateChecksum(finalData)).CopyTo(wire, 6);
        Array.Copy(finalData, 0, wire, WireHeaderLength, finalData.Length);
        return wire;
    }

    /// <summary>
    /// 从完整线路帧解析响应为 <see cref="SyncPacket"/>（<see cref="HandshakePacket"/> 或
    /// <see cref="HandshakeRejectPacket"/>）。校验：长度下限、载荷长度边界、校验和、压缩标志；
    /// 畸形帧抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static SyncPacket Unpack(ReadOnlySpan<byte> wire)
    {
        if (wire.Length < WireHeaderLength)
        {
            throw new InvalidOperationException(
                $"握手响应帧过短：{wire.Length} 字节，至少需要 {WireHeaderLength} 字节线路头。");
        }

        var bodyLength = BitConverter.ToInt32(wire.Slice(0, 4));
        // 用减法而非加法判界，避免 WireHeaderLength + bodyLength 对 int.MaxValue 溢出回绕后绕过边界检查。
        if (bodyLength <= 0 || bodyLength > wire.Length - WireHeaderLength)
        {
            throw new InvalidOperationException($"握手响应帧载荷长度非法：{bodyLength}（帧总长 {wire.Length}）。");
        }

        var isCompressed = wire[5] != 0;
        var expectedChecksum = BitConverter.ToUInt16(wire.Slice(6, 2));
        var body = wire.Slice(WireHeaderLength, bodyLength);

        var actualChecksum = HorizonProtocol.CalculateChecksum(body);
        if (actualChecksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"握手响应帧校验和不匹配：期望 {expectedChecksum}，实际 {actualChecksum}。");
        }

        byte[]? finalBody;
        try
        {
            finalBody = isCompressed ? LZ4Pickler.Unpickle(body) : body.ToArray();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"握手响应帧解压失败：{ex.Message}", ex);
        }

        if (finalBody is null)
        {
            throw new InvalidOperationException(
                $"握手响应帧解压失败（畸形输入或超过 {LZ4Pickler.MaxDecompressedLength} 字节上限），帧载荷 {body.Length} 字节。");
        }

        var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalBody)
                     ?? throw new InvalidOperationException("握手响应帧反序列化失败（HorizonMessagePacket 为 null）。");

        if (packet.Body is not SyncFrameMessage syncFrame)
        {
            throw new InvalidOperationException(
                $"握手响应帧消息体类型异常：{packet.Body?.GetType().Name ?? "null"}，期望 SyncFrameMessage。");
        }

        try
        {
            return SyncPacketCodec.Decode(syncFrame.Frame);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"握手响应帧内层同步帧解析失败：{ex.Message}", ex);
        }
    }
}

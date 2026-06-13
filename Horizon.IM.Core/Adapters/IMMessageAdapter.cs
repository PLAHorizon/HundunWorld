using System.Buffers;
using System.Threading;

using Horizon.IM.Core.Utilities;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using MemoryPack;

using TouchSocket.Core;

namespace Horizon.IM.Core.Adapters;

public class IMMessageAdapter : CustomFixedHeaderDataHandlingAdapter<IMMessageInfo>
{
    private static long _sequenceSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public override int HeaderLength => IMProtocol.HeaderLength;

    protected override IMMessageInfo GetInstance() => new();

    public IMMessagePacket CreatePacket<T>(
        T message,
        ulong userId = 0,
        bool isResponse = false,
        string? responseToMessageId = null) where T : IMMessageUnion, IIMNetworkMessage
    {
        return CreatePacket((IMMessageUnion)message, userId, isResponse, responseToMessageId);
    }

    public IMMessagePacket CreatePacket(
        IMMessageUnion message,
        ulong userId = 0,
        bool isResponse = false,
        string? responseToMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (messageType, serviceType) = ResolveMessageMetadata(message);

        var header = new IMMessageHeader
        {
            UserId = userId,
            MessageType = messageType,
            ServiceType = serviceType,
            IsResponse = isResponse,
            ResponseToMessageId = responseToMessageId ?? string.Empty,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceId = Interlocked.Increment(ref _sequenceSeed)
        };

        var packet = new IMMessagePacket
        {
            Header = header,
            ServiceType = serviceType,
            Body = message
        };

        packet.RawData = MemoryPackSerializer.Serialize(packet);
        packet.Length = packet.RawData.Length;
        return packet;
    }

    public byte[] PackMessage<T>(
        T message,
        ulong userId = 0,
        bool isResponse = false,
        string? responseToMessageId = null,
        bool compress = true) where T : IMMessageUnion, IIMNetworkMessage
    {
        var packet = CreatePacket(message, userId, isResponse, responseToMessageId);
        return PackPacket(packet, compress);
    }

    public byte[] PackPacket(IMMessagePacket packet, bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var packetBytes = MemoryPackSerializer.Serialize(packet);
        byte[] payload = packetBytes;
        var isCompressed = false;

        if (compress && packetBytes.Length > 256)
        {
            var compressed = LZ4Pickler.Pickle(packetBytes);
            if (compressed.Length < packetBytes.Length)
            {
                payload = compressed;
                isCompressed = true;
            }
        }

        var frame = new byte[HeaderLength + payload.Length];
        var span = frame.AsSpan();

        BitConverter.TryWriteBytes(span[..4], payload.Length);
        span[4] = (byte)packet.Header.MessageType;
        span[5] = (byte)(isCompressed ? 1 : 0);
        BitConverter.TryWriteBytes(span.Slice(6, 2), IMProtocol.CalculateChecksum(payload));
        payload.AsSpan().CopyTo(span[HeaderLength..]);

        return frame;
    }

    private static (IMMessageType MessageType, IMServiceType ServiceType) ResolveMessageMetadata(IMMessageUnion message)
    {
        var messageType = message.Type;
        var serviceType = message.ServiceType;

        var runtimeType = message.GetType();
        if (runtimeType == typeof(IMMessageUnion))
        {
            return (messageType, serviceType);
        }

        var runtimeMessageType = runtimeType.GetProperty(nameof(IMMessageUnion.Type))?.GetValue(message);
        if (runtimeMessageType is IMMessageType typedMessageType)
        {
            messageType = typedMessageType;
        }

        var runtimeServiceType = runtimeType.GetProperty(nameof(IMMessageUnion.ServiceType))?.GetValue(message);
        if (runtimeServiceType is IMServiceType typedServiceType)
        {
            serviceType = typedServiceType;
        }

        return (messageType, serviceType);
    }
}

public static class IMProtocol
{
    public const int HeaderLength = 8;

    public static ushort CalculateChecksum(ReadOnlySpan<byte> data)
    {
        uint checksum = 0;
        foreach (var b in data)
        {
            checksum += b;
        }

        return (ushort)(checksum & 0xFFFF);
    }
}

public partial class IMMessageInfo : IFixedHeaderRequestInfo
{
    private bool _isCompressed;
    private ushort _expectedChecksum;

    public int BodyLength { get; set; }

    public byte[]? Body { get; set; }

    public IMMessagePacket? Packet { get; set; }

    public int MaxLength => 1024 * 1024;

    public IMMessageInfo()
    {
    }

    public IMMessageInfo(IMMessagePacket packet)
    {
        Packet = packet;
        if (packet.RawData != null)
        {
            Body = packet.RawData;
            BodyLength = packet.RawData.Length;
        }
    }

    public bool TryBuild(ReadOnlySequence<byte> buffer, int length, out IRequestInfo requestInfo)
    {
        requestInfo = default!;

        try
        {
            if (buffer.Length < IMProtocol.HeaderLength)
            {
                return false;
            }

            var reader = new SequenceReader<byte>(buffer);
            if (!reader.TryReadLittleEndian(out int payloadLength))
            {
                return false;
            }

            if (buffer.Length < IMProtocol.HeaderLength + payloadLength)
            {
                return false;
            }

            if (!reader.TryRead(out _))
            {
                return false;
            }

            if (!reader.TryRead(out byte compressedFlag))
            {
                return false;
            }

            _isCompressed = compressedFlag != 0;
            if (!reader.TryReadLittleEndian(out short checksum))
            {
                return false;
            }

            _expectedChecksum = unchecked((ushort)checksum);
            var payload = buffer.Slice(IMProtocol.HeaderLength, payloadLength).ToArray();
            if (!TryDeserialize(payload, out var packet))
            {
                return false;
            }

            requestInfo = new IMMessageInfo(packet)
            {
                Body = payload,
                BodyLength = payloadLength
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryBuild(ReadOnlySequence<byte> buffer, out IRequestInfo requestInfo)
    {
        return TryBuild(buffer, (int)buffer.Length, out requestInfo);
    }

    public bool OnParsingHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < IMProtocol.HeaderLength)
        {
            return false;
        }

        try
        {
            var bodyLength = BitConverter.ToInt32(header);
            if (bodyLength <= 0 || bodyLength > MaxLength)
            {
                return false;
            }

            BodyLength = bodyLength;
            _isCompressed = header[5] != 0;
            _expectedChecksum = BitConverter.ToUInt16(header.Slice(6, 2));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool OnParsingBody(ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            return false;
        }

        try
        {
            if (IMProtocol.CalculateChecksum(body) != _expectedChecksum)
            {
                return false;
            }

            Body = body.ToArray();
            if (!TryDeserialize(body, out var packet))
            {
                return false;
            }

            Packet = packet;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
    {
        if (Packet == null)
        {
            return;
        }

        var body = MemoryPackSerializer.Serialize(Packet);
        byteBlock.Write(BitConverter.GetBytes(body.Length));
        byteBlock.Write(body);
    }

    private bool TryDeserialize(ReadOnlySpan<byte> payload, out IMMessagePacket packet)
    {
        packet = default!;
        var finalPayload = _isCompressed ? LZ4Pickler.Unpickle(payload) : payload.ToArray();
        if (finalPayload == null)
        {
            return false;
        }

        packet = MemoryPackSerializer.Deserialize<IMMessagePacket>(finalPayload)!;
        if (packet == null)
        {
            return false;
        }

        packet.RawData = payload.ToArray();
        packet.Length = packet.RawData.Length;
        return true;
    }
}
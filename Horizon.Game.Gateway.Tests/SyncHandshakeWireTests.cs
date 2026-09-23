using Horizon.Game.Core;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.0.6 — 握手线路帧共享纯逻辑（<see cref="SyncHandshakeWire"/>)的正式单元测试。
/// <para>
/// 覆盖：8 字节线路头格式、成功/拒绝包往返、与服务端 <see cref="HorizonMessageAdapter.PackPacket"/>
/// 的逐字节兼容对拍、畸形帧（过短/非法长度/校验和不匹配/错误消息体/损坏内层同步帧）的拒绝路径、
/// 会话期 Input 上行 fire-and-forget、共享 PackPacket（GengDi 复用路径）、以及边界矩阵
/// （空/缺省凭据 6 组合、2 万条增量超大合法帧、压缩标志置位（wire[5]=1）大载荷响应）。
/// UE5 客户端 <c>GameGatewaySessionClient</c> 与测试共用同一份实现，避免双端打包/解析漂移。
/// </para>
/// </summary>
public class SyncHandshakeWireTests
{
    private static HandshakePacket CreateHandshake() => new()
    {
        LocalCharacterId = 42,
        InitialClientTick = 100,
        InitialX = 1.5f,
        InitialY = 2.5f,
        InitialZ = 3.5f,
        ProtocolVersion = SyncProtocolVersion.Current,
        FormulaVersion = 3,
    };

    [Fact]
    public void Pack_ProducesValidWireHeader()
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake(), "auth-token", "machine-1");

        Assert.True(wire.Length >= SyncHandshakeWire.WireHeaderLength, "线路帧至少应含 8 字节头");
        var bodyLength = BitConverter.ToInt32(wire, 0);
        Assert.Equal(wire.Length - SyncHandshakeWire.WireHeaderLength, bodyLength);
        // MessageType.SyncPacket = 1392 > byte 上限，线路头按低位截断（与服务端一致）。
        Assert.Equal(unchecked((byte)MessageType.SyncPacket), wire[4]);
        Assert.Equal(0, wire[5]); // 握手包不压缩
        var expectedChecksum = HorizonProtocol.CalculateChecksum(
            wire.AsSpan(SyncHandshakeWire.WireHeaderLength, bodyLength));
        Assert.Equal(expectedChecksum, BitConverter.ToUInt16(wire, 6));
    }

    [Fact]
    public void Pack_Then_Unpack_RoundTripsHandshake()
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake(), "auth-token", "machine-1");

        var decoded = Assert.IsType<HandshakePacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(42UL, decoded.LocalCharacterId);
        Assert.Equal(100L, decoded.InitialClientTick);
        Assert.Equal(1.5f, decoded.InitialX);
        Assert.Equal(2.5f, decoded.InitialY);
        Assert.Equal(3.5f, decoded.InitialZ);
        Assert.Equal(SyncProtocolVersion.Current, decoded.ProtocolVersion);
        Assert.Equal(3, decoded.FormulaVersion);
    }

    [Fact]
    public void Pack_Then_Unpack_RoundTripsHandshakeReject()
    {
        var reject = new HandshakeRejectPacket
        {
            ServerVersion = SyncProtocolVersion.Current,
            MinimumVersion = SyncProtocolVersion.MinimumSupported,
            Reason = "公式版本不匹配",
        };

        var wire = SyncHandshakeWire.Pack(reject);

        var decoded = Assert.IsType<HandshakeRejectPacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(SyncProtocolVersion.Current, decoded.ServerVersion);
        Assert.Equal(SyncProtocolVersion.MinimumSupported, decoded.MinimumVersion);
        Assert.Equal("公式版本不匹配", decoded.Reason);
    }

    /// <summary>
    /// 跨实现对拍：服务端打包路径（<see cref="HorizonMessageAdapter.PackPacket"/> 产生的线路帧）
    /// 必须能被客户端解析路径（<see cref="SyncHandshakeWire.Unpack"/>）完整还原——这是客户端
    /// 接收服务端握手响应时的真实数据流。
    /// </summary>
    [Fact]
    public void Unpack_ParsesServerWrappedResponse()
    {
        var handshake = CreateHandshake();
        SyncPacketCodec.Encode(handshake, out var frame, out var frameLength);
        try
        {
            var syncFrame = new SyncFrameMessage
            {
                Frame = frame.AsSpan(0, frameLength).ToArray(),
                PacketKind = (byte)handshake.Kind,
                ProtocolVersion = handshake.ProtocolVersion,
            };
            var packet = new HorizonMessagePacket
            {
                Header = new MessageHeader
                {
                    MessageType = MessageType.SyncPacket,
                    ServiceType = ServiceType.Game,
                },
                ServiceType = ServiceType.Game,
                Body = syncFrame,
            };

            var wire = new HorizonMessageAdapter().PackPacket(packet, compress: false);

            var decoded = Assert.IsType<HandshakePacket>(SyncHandshakeWire.Unpack(wire));
            Assert.Equal(42UL, decoded.LocalCharacterId);
            Assert.Equal(3, decoded.FormulaVersion);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    [Fact]
    public void Unpack_TooShortWire_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SyncHandshakeWire.Unpack(new byte[4]));
        Assert.Contains("过短", ex.Message);
    }

    [Fact]
    public void Unpack_InvalidBodyLength_Throws()
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake());
        // 声明载荷长度超出实际帧长 → 边界非法。
        BitConverter.GetBytes(int.MaxValue).CopyTo(wire, 0);

        var ex = Assert.Throws<InvalidOperationException>(() => SyncHandshakeWire.Unpack(wire));
        Assert.Contains("非法", ex.Message);
    }

    [Fact]
    public void Unpack_ChecksumMismatch_Throws()
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake());
        wire[SyncHandshakeWire.WireHeaderLength] ^= 0xFF; // 篡改载荷首个字节

        var ex = Assert.Throws<InvalidOperationException>(() => SyncHandshakeWire.Unpack(wire));
        Assert.Contains("校验和", ex.Message);
    }

    [Fact]
    public void Unpack_NonSyncFrameBody_Throws()
    {
        var packet = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
            },
            ServiceType = ServiceType.Game,
            Body = new LoginRequest(),
        };

        var wire = new HorizonMessageAdapter().PackPacket(packet, compress: false);

        var ex = Assert.Throws<InvalidOperationException>(() => SyncHandshakeWire.Unpack(wire));
        Assert.Contains("消息体类型", ex.Message);
    }

    [Fact]
    public void Unpack_GarbageSyncFrame_Throws()
    {
        var syncFrame = new SyncFrameMessage { Frame = new byte[] { 0xFF } }; // 非法内层同步帧
        var packet = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
            },
            ServiceType = ServiceType.Game,
            Body = syncFrame,
        };

        var wire = new HorizonMessageAdapter().PackPacket(packet, compress: false);

        var ex = Assert.Throws<InvalidOperationException>(() => SyncHandshakeWire.Unpack(wire));
        Assert.Contains("同步帧解析失败", ex.Message);
    }

    // ── 会话期上行（GameGatewaySessionClient.SendInputAsync 同款打包路径） ──────────

    /// <summary>输入包上行采用 fire-and-forget 语义：RequireResponse 位必须为 false。</summary>
    [Fact]
    public void Pack_InputPacket_RequireResponseFalse()
    {
        var input = new InputPacket
        {
            ClientTick = 100,
            InputBits = 0x01,
            LookYaw = 1.0f,
            MoveX = 0.5f,
            MoveY = -0.5f,
            CharacterId = 42,
            PredictedEndX = 1.5f,
            PredictedEndY = 2.5f,
            PredictedEndZ = 3.5f,
            MaxSpeed = 6f,
        };

        var wire = SyncHandshakeWire.Pack(input, "auth", "machine", requireResponse: false);

        // 还原线路头的 MessageHeader 校验 RequireResponse 位。
        var bodyLength = BitConverter.ToInt32(wire, 0);
        var message = MemoryPack.MemoryPackSerializer.Deserialize<HorizonMessagePacket>(
            wire.AsSpan(SyncHandshakeWire.WireHeaderLength, bodyLength));
        Assert.False(message!.Header.RequireResponse);
    }

    /// <summary>缺省 requireResponse 保持 true：握手语义不被会话期参数改变（既有调用零变化）。</summary>
    [Fact]
    public void Pack_RequireResponse_DefaultIsTrue()
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake());

        var bodyLength = BitConverter.ToInt32(wire, 0);
        var message = MemoryPack.MemoryPackSerializer.Deserialize<HorizonMessagePacket>(
            wire.AsSpan(SyncHandshakeWire.WireHeaderLength, bodyLength));
        Assert.True(message!.Header.RequireResponse);
    }

    /// <summary>InputPacket 上行打包 → 解析往返：会话闭环上行路径字段零丢失（同帧格式复用）。</summary>
    [Fact]
    public void Pack_InputPacket_RoundTrips()
    {
        var input = new InputPacket
        {
            ClientTick = 100,
            InputBits = 0x03,
            LookYaw = 1.0f,
            LookPitch = -0.2f,
            MoveX = 0.5f,
            MoveY = -0.5f,
            CharacterId = 42,
            PredictedEndX = 1.5f,
            PredictedEndY = 2.5f,
            PredictedEndZ = 3.5f,
            MaxSpeed = 6f,
        };

        var wire = SyncHandshakeWire.Pack(input, "auth-token", "machine-1", requireResponse: false);

        var decoded = Assert.IsType<InputPacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(100L, decoded.ClientTick);
        Assert.Equal(0x03u, decoded.InputBits);
        Assert.Equal(1.0f, decoded.LookYaw);
        Assert.Equal(-0.2f, decoded.LookPitch);
        Assert.Equal(0.5f, decoded.MoveX);
        Assert.Equal(-0.5f, decoded.MoveY);
        Assert.Equal(42UL, decoded.CharacterId);
        Assert.Equal(1.5f, decoded.PredictedEndX);
        Assert.Equal(2.5f, decoded.PredictedEndY);
        Assert.Equal(3.5f, decoded.PredictedEndZ);
        Assert.Equal(6f, decoded.MaxSpeed);
        Assert.Equal(SyncProtocolVersion.Current, decoded.ProtocolVersion);
    }

    // ── 通用线路帧 PackPacket（GengDi GameGatewayClient 复用的共享打包路径） ──────────

    private static HorizonMessagePacket CreateTestPacket(string? bigPayload = null)
    {
        return new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
                IsResponse = false,
                RequireResponse = true,
                GameId = 1,
                ZoneId = 1,
                ServerId = 1,
                AuthToken = "auth-token",
                MachineId = "machine-1",
            },
            ServiceType = ServiceType.Game,
            Body = new ErrorMessage
            {
                ErrorCode = 500,
                Message = bigPayload ?? "ok",
                Details = bigPayload ?? string.Empty,
                Timestamp = 123456789,
                RelatedMessageId = "test-1",
                ShouldRetry = false,
            },
        };
    }

    /// <summary>解析通用线路帧为 HorizonMessagePacket（校验和 + 解压；GengDi 适配器同款逻辑）。</summary>
    private static HorizonMessagePacket ParseWire(byte[] wire)
    {
        var bodyLength = BitConverter.ToInt32(wire, 0);
        Assert.InRange(bodyLength, 1, wire.Length - SyncHandshakeWire.WireHeaderLength);

        var isCompressed = wire[5] != 0;
        var expectedChecksum = BitConverter.ToUInt16(wire, 6);
        var body = wire.AsSpan(SyncHandshakeWire.WireHeaderLength, bodyLength);
        Assert.Equal(expectedChecksum, HorizonProtocol.CalculateChecksum(body));

        // 与生产解析路径（SyncHandshakeWire.Unpack → 本地带防护 LZ4Pickler）保持同源；
        // K4os 库版 Unpickle 是带版本头的自有格式，与本地 [长度前缀+LZ4块] 格式不兼容。
        var finalBody = isCompressed
            ? Horizon.Game.Message.Utilities.LZ4Pickler.Unpickle(body)
            : body.ToArray();
        if (finalBody is null)
            throw new InvalidOperationException("线路帧解压失败（畸形输入或超限）");
        return MemoryPack.MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalBody)
               ?? throw new InvalidOperationException("线路帧反序列化失败");
    }

    [Fact]
    public void PackPacket_SmallPacket_NoCompression_AndRoundTrips()
    {
        var wire = SyncHandshakeWire.PackPacket(CreateTestPacket(), compress: true);

        Assert.Equal(0, wire[5]); // 小包（<256B）不压缩
        var decoded = ParseWire(wire);
        var body = Assert.IsType<ErrorMessage>(decoded.Body);
        Assert.Equal(500, body.ErrorCode);
        Assert.Equal("ok", body.Message);
        Assert.Equal("auth-token", decoded.Header.AuthToken);
    }

    [Fact]
    public void PackPacket_LargePacket_Compresses_AndRoundTrips()
    {
        var wire = SyncHandshakeWire.PackPacket(CreateTestPacket(new string('x', 4000)), compress: true);

        Assert.Equal(1, wire[5]); // 大包（>256B）且压缩后更小 → 走压缩路径
        var decoded = ParseWire(wire);
        var body = Assert.IsType<ErrorMessage>(decoded.Body);
        Assert.Equal(new string('x', 4000), body.Message);
        Assert.Equal(new string('x', 4000), body.Details);
    }

    [Fact]
    public void PackPacket_CompressFalse_KeepsUncompressed()
    {
        var wire = SyncHandshakeWire.PackPacket(CreateTestPacket(new string('x', 4000)), compress: false);

        Assert.Equal(0, wire[5]);
        var decoded = ParseWire(wire);
        Assert.Equal(new string('x', 4000), Assert.IsType<ErrorMessage>(decoded.Body).Message);
    }

    /// <summary>
    /// 跨端可解析性：GengDi 客户端经共享 PackPacket 发出的线路帧（压缩/未压缩两路）必须能被服务端
    /// <see cref="HorizonMessageAdapter.UnpackMessage"/> 完整解析并还原 Body（校验和 + 解压 + 反序列化）。
    /// 这是 GengDi 改用共享实现后与服务端通信零行为变化的依据——服务端接收侧不校验 SequenceId，
    /// 客户端帧无需与服务端 PackPacket 逐字节一致（服务端 PackPacket 还会注入 RawData/SequenceId，
    /// 其 CRC32 在 Horizon.Game.Core，Message 层无法引用）。
    /// </summary>
    [Fact]
    public void PackPacket_ServerUnpackMessage_ParsesAndRoundTrips()
    {
        // 未压缩小包
        var wire = SyncHandshakeWire.PackPacket(CreateTestPacket());
        var parsed = new HorizonMessageAdapter().UnpackMessage(wire);
        var body = Assert.IsType<ErrorMessage>(parsed.Body);
        Assert.Equal(500, body.ErrorCode);
        Assert.Equal("ok", body.Message);

        // 压缩大包
        var wireCompressed = SyncHandshakeWire.PackPacket(CreateTestPacket(new string('x', 4000)));
        Assert.Equal(1, wireCompressed[5]);
        var parsedCompressed = new HorizonMessageAdapter().UnpackMessage(wireCompressed);
        Assert.Equal(new string('x', 4000), Assert.IsType<ErrorMessage>(parsedCompressed.Body).Message);
    }

    /// <summary>
    /// 客户端零行为变化：GengDi CreatePacket 预置 RawData（SequenceId 留默认），共享 PackPacket
    /// 原样序列化——与 GengDi 旧私有 PackPacket 输出逐字节一致（旧实现同样不注入 SequenceId）。
    /// </summary>
    [Fact]
    public void PackPacket_PreservesPreSetRawData_NoSequenceIdInjection()
    {
        var body = new ErrorMessage { ErrorCode = 500, Message = "ok" };
        var packet = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
                RequireResponse = true,
            },
            ServiceType = ServiceType.Game,
            Body = body,
            // GengDi CreatePacket 同款预置：RawData 已序列化，SequenceId 留默认。
            RawData = MemoryPack.MemoryPackSerializer.Serialize(body),
        };

        var wire = SyncHandshakeWire.PackPacket(packet);
        var parsed = new HorizonMessageAdapter().UnpackMessage(wire);

        // 共享实现不注入 SequenceId：解析结果仍为默认 0，与旧客户端帧一致。
        Assert.Equal(0u, parsed.Header.SequenceId);
        Assert.Equal("ok", Assert.IsType<ErrorMessage>(parsed.Body).Message);
    }

    // ── 边界矩阵：空/缺省凭据、超大合法帧、压缩标志置位响应 ──────────────────────

    /// <summary>
    /// 空/缺省 authToken 与 machineId 的 6 种组合：打包不抛异常、线路头校验和合法、
    /// 握手往返字段零丢失，且 Header 凭据字段归一为空串（null → 空串，服务端 auth-exempt 语义）。
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("auth-token", "")]
    [InlineData("", "machine-1")]
    [InlineData(null, "machine-1")]
    [InlineData("auth-token", null)]
    public void Pack_EmptyOrNullCredentials_ValidWireAndRoundTrips(string? authToken, string? machineId)
    {
        var wire = SyncHandshakeWire.Pack(CreateHandshake(), authToken, machineId);

        // 线路头合法：载荷长度/校验和自洽（Unpack 校验和通过即证明）。
        var decoded = Assert.IsType<HandshakePacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(42UL, decoded.LocalCharacterId);
        Assert.Equal(3, decoded.FormulaVersion);

        // Header 凭据字段：null 归一为空串，不残留 null。
        var bodyLength = BitConverter.ToInt32(wire, 0);
        var message = MemoryPack.MemoryPackSerializer.Deserialize<HorizonMessagePacket>(
            wire.AsSpan(SyncHandshakeWire.WireHeaderLength, bodyLength));
        Assert.Equal(authToken ?? string.Empty, message!.Header.AuthToken);
        Assert.Equal(machineId ?? string.Empty, message.Header.MachineId);
    }

    /// <summary>
    /// 超大合法帧：2 万条实体增量的大快照（伪随机 EntityId 抑制内层压缩）经打包/解包往返
    /// 零丢失——验证 Unpack 对大帧无截断、无长度回绕、LZ4 解压边界正确。
    /// </summary>
    [Fact]
    public void Pack_Then_Unpack_LargeSnapshotFrame_RoundTrips()
    {
        const int deltaCount = 20000;
        var deltas = new EntityDelta[deltaCount];
        uint seed = 0x12345678;
        for (var i = 0; i < deltaCount; i++)
        {
            // xorshift：伪随机 EntityId 抑制 LZ4 压缩，保证内层帧确实为大尺寸。
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            deltas[i] = new EntityDelta { EntityId = seed, Kind = EntityDeltaKind.Update };
        }

        var snapshot = new SnapshotPacket { ServerTick = 42, Deltas = deltas };
        var wire = SyncHandshakeWire.Pack(snapshot, requireResponse: true);

        // 帧确实足够大（>64KB），超出任何小缓冲/截断缺陷的检测面。
        Assert.True(wire.Length > 64 * 1024, $"帧应超过 64KB（实际 {wire.Length} 字节）");

        var decoded = Assert.IsType<SnapshotPacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(42L, decoded.ServerTick);
        Assert.Equal(deltaCount, decoded.Deltas.Length);
        Assert.Equal(deltas[0].EntityId, decoded.Deltas[0].EntityId);
        Assert.Equal(deltas[deltaCount - 1].EntityId, decoded.Deltas[deltaCount - 1].EntityId);
    }

    /// <summary>
    /// 压缩标志置位（wire[5]=1）响应：服务端对 >256B 且可压缩的载荷走 LZ4 外层压缩，
    /// 客户端 Unpack 必须能解压并还原——此处用 200KB 可压缩载荷（全 0x41）保证压缩必然生效。
    /// 内层为 <see cref="WorldChunkDiffPacket"/>（SyncPacketCodec 不压缩该类型，内层保持原始大载荷）。
    /// </summary>
    [Fact]
    public void Unpack_CompressedFlagSetResponse_RoundTripsLargeDiff()
    {
        const int payloadSize = 200 * 1024;
        var payload = new byte[payloadSize];
        Array.Fill(payload, (byte)0x41); // 高度可压缩

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 0x1234567890ABCDEFUL,
            DiffSeqStart = 1,
            DiffSeqEnd = 1,
            Payload = payload,
            PayloadType = WorldChunkDiffPayloadType.EntityDelta,
        };

        SyncPacketCodec.Encode(diff, out var frame, out var frameLength);
        byte[] frameCopy;
        try
        {
            frameCopy = frame.AsSpan(0, frameLength).ToArray();
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }

        var syncFrame = new SyncFrameMessage
        {
            Frame = frameCopy,
            PacketKind = (byte)diff.Kind,
            ProtocolVersion = diff.ProtocolVersion,
        };
        var packet = new HorizonMessagePacket
        {
            Header = new MessageHeader
            {
                MessageType = MessageType.SyncPacket,
                ServiceType = ServiceType.Game,
            },
            ServiceType = ServiceType.Game,
            Body = syncFrame,
        };

        var wire = SyncHandshakeWire.PackPacket(packet, compress: true);

        // 压缩标志必须置位：200KB 全 0x41 载荷经 LZ4 必然收缩。
        Assert.Equal(1, wire[5]);

        var decoded = Assert.IsType<WorldChunkDiffPacket>(SyncHandshakeWire.Unpack(wire));
        Assert.Equal(0x1234567890ABCDEFUL, decoded.ChunkMortonKey);
        Assert.Equal(payloadSize, decoded.Payload.Length);
        Assert.Equal(WorldChunkDiffPayloadType.EntityDelta, decoded.PayloadType);
    }
}

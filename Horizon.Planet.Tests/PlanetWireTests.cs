using System;
using System.Buffers.Binary;
using Horizon.Planet;
using Horizon.Planet.Dem;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 线格式判据。重点是三件事：
/// 1) 字节布局由一张独立的偏移表复核——读写同时写错同一格时 round-trip 仍然绿，只有偏移表能抓住；
/// 2) 每一个截断长度都必须被拒（不是只测短 1 字节）；
/// 3) 每条拒绝分支都要有同夹具的正向对照，否则"红"可能只是夹具根本喂不进去。
/// </summary>
public class PlanetWireTests
{
    private static void Field(long expected, long actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static void Field(int expected, int actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static void Field(ushort expected, ushort actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static void Field(short expected, short actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static void Field(byte expected, byte actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static void Field(uint expected, uint actual, string name) =>
        Assert.True(expected == actual, $"{name}：期望 {expected}，实得 {actual}");

    private static PlanetHandshake SampleHandshake(ushort protocol = PlanetHandshake.ProtocolVersion) => new()
    {
        WireProtocol = protocol,
        PlanetRadiusCm = PlanetConstants.PlanetRadiusCm,
        RegionLevel = PlanetConstants.DefaultRegionLevel,
        TerrainSeed = unchecked((long)0x39bb6eb45a785101UL),
        TerrainVersion = 0xDEADBEEF,
        BasefieldVersion = 2026,
        BasefieldHashHi = unchecked((long)0x1234_5678_9ABC_DEF0UL),
    };

    private static PlayerStatePacket SampleState() => new()
    {
        Tick = -1234567890123L,
        RegionId = new RegionKey(3, 14, 12345, 6789).ToId(),
        LocalXCm = -450_000,
        LocalYCm = 90_001,
        LocalZCm = 12_345,
        VelocityXCms = 350,
        VelocityYCms = -12,
        VelocityZCms = 0,
        YawQuantized = 1234,
        PitchQuantized = 65535,
        Flags = 0x8001,
    };

    private static RegionTransferRequest SampleRequest() => new()
    {
        FromRegionId = new RegionKey(0, 14, 1, 2).ToId(),
        ToRegionId = new RegionKey(1, 14, 3, 4).ToId(),
        GlobalCm = new EcefCentimeters(-6_371_000_001L, 123_456_789L, -1L),
        Tick = 99,
    };

    private static RegionTransferAck SampleAck() => new()
    {
        RegionId = new RegionKey(5, 14, 16383, 0).ToId(),
        LocalXCm = 5,
        LocalYCm = -6,
        LocalZCm = 7,
        OriginCm = new EcefCentimeters(1, -2, 3),
        Frame = QuaternionQuantized.From(0.0, -0.5, 0.5, Math.Sqrt(0.5)),
    };

    // ---------- 布局表 ----------

    [Fact]
    public void Handshake_Layout_MatchesIndependentOffsetTable()
    {
        var buffer = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(buffer, SampleHandshake(), out int written));
        Assert.Equal(PlanetHandshake.Size, written);

        Field(PlanetHandshake.ProtocolVersion, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0)), "wireProtocol");
        Field(PlanetConstants.PlanetRadiusCm, BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(2)), "planetRadiusCm");
        Field((byte)PlanetConstants.DefaultRegionLevel, buffer[10], "regionLevel");
        Field(unchecked((long)0x39bb6eb45a785101UL), BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(11)), "terrainSeed");
        Field(0xDEADBEEF, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(19)), "terrainVersion");
        Field(2026u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(23)), "basefieldVersion");
        Field(unchecked((long)0x1234_5678_9ABC_DEF0UL), BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(27)), "basefieldHashHi");
        Assert.Equal(35, PlanetHandshake.Size);
    }

    [Fact]
    public void PlayerState_Layout_MatchesIndependentOffsetTable()
    {
        var buffer = new byte[PlayerStatePacket.Size];
        Assert.True(PlayerStatePacket.TryWrite(buffer, SampleState(), out int written));
        Assert.Equal(PlayerStatePacket.Size, written);

        Field(-1234567890123L, BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0)), "tick");
        Field(new RegionKey(3, 14, 12345, 6789).ToId(), BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(8)), "regionId");
        Field(-450_000, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16)), "localX");
        Field(90_001, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20)), "localY");
        Field(12_345, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24)), "localZ");
        Field(350, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28)), "velX");
        Field(-12, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32)), "velY");
        Field(0, BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(36)), "velZ");
        Field((ushort)1234, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(40)), "yaw");
        Field((ushort)65535, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(42)), "pitch");
        Field((ushort)0x8001, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(44)), "flags");
        Assert.Equal(46, PlayerStatePacket.Size);
    }

    [Fact]
    public void TransferMessages_Layout_MatchesIndependentOffsetTable()
    {
        var request = new byte[RegionTransferRequest.Size];
        Assert.True(RegionTransferRequest.TryWrite(request, SampleRequest(), out int writtenR));
        Assert.Equal(RegionTransferRequest.Size, writtenR);
        Field(new RegionKey(0, 14, 1, 2).ToId(), BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(0)), "fromRegion");
        Field(new RegionKey(1, 14, 3, 4).ToId(), BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(8)), "toRegion");
        Field(-6_371_000_001L, BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(16)), "globalX");
        Field(123_456_789L, BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(24)), "globalY");
        Field(-1L, BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(32)), "globalZ");
        Field(99L, BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(40)), "tick");
        Assert.Equal(48, RegionTransferRequest.Size);

        var ack = new byte[RegionTransferAck.Size];
        Assert.True(RegionTransferAck.TryWrite(ack, SampleAck(), out int writtenA));
        Assert.Equal(RegionTransferAck.Size, writtenA);
        Field(new RegionKey(5, 14, 16383, 0).ToId(), BinaryPrimitives.ReadInt64LittleEndian(ack.AsSpan(0)), "regionId");
        Field(5, BinaryPrimitives.ReadInt32LittleEndian(ack.AsSpan(8)), "localX");
        Field(-6, BinaryPrimitives.ReadInt32LittleEndian(ack.AsSpan(12)), "localY");
        Field(-2L, BinaryPrimitives.ReadInt64LittleEndian(ack.AsSpan(28)), "originY");
        Field((short)Math.Round(-0.5 * QuaternionQuantized.Scale, MidpointRounding.AwayFromZero),
              BinaryPrimitives.ReadInt16LittleEndian(ack.AsSpan(46)), "quatY");
        Assert.Equal(52, RegionTransferAck.Size);
    }

    // ---------- round-trip ----------

    [Fact]
    public void RoundTrips_PreserveEveryField()
    {
        var buffer = new byte[PlanetHandshake.Size];
        PlanetHandshake.TryWrite(buffer, SampleHandshake(), out _);
        Assert.True(PlanetHandshake.TryRead(buffer, out var handshake, out var hs, out int consumed));
        Assert.Equal(PlanetWireStatus.Ok, hs);
        Assert.Equal(PlanetHandshake.Size, consumed);
        Assert.Equal(SampleHandshake(), handshake);

        buffer = new byte[PlayerStatePacket.Size];
        PlayerStatePacket.TryWrite(buffer, SampleState(), out _);
        Assert.True(PlayerStatePacket.TryRead(buffer, out var state, out var ss, out consumed));
        Assert.Equal(PlanetWireStatus.Ok, ss);
        Assert.Equal(SampleState(), state);

        buffer = new byte[RegionTransferRequest.Size];
        RegionTransferRequest.TryWrite(buffer, SampleRequest(), out _);
        Assert.True(RegionTransferRequest.TryRead(buffer, out var request, out var rs, out consumed));
        Assert.Equal(PlanetWireStatus.Ok, rs);
        Assert.Equal(SampleRequest(), request);

        buffer = new byte[RegionTransferAck.Size];
        RegionTransferAck.TryWrite(buffer, SampleAck(), out _);
        Assert.True(RegionTransferAck.TryRead(buffer, out var ack, out var ackStatus, out consumed));
        Assert.Equal(PlanetWireStatus.Ok, ackStatus);
        Assert.Equal(SampleAck(), ack);
    }

    [Fact]
    public void RegionSurvives_IdAndWire_EndToEnd()
    {
        // RegionKey → id → 线上 i64 → 解回 → 还原 key：四段都要对得上
        var key = new RegionKey(2, PlanetConstants.DefaultRegionLevel, 9876, 5432);
        var packet = SampleState();
        packet.RegionId = key.ToId();
        var buffer = new byte[PlayerStatePacket.Size];
        PlayerStatePacket.TryWrite(buffer, packet, out _);
        Assert.True(PlayerStatePacket.TryRead(buffer, out var decoded, out _, out _));
        Assert.Equal(key, RegionKey.FromId(decoded.RegionId));
    }

    // ---------- 截断：每个长度都要红 ----------

    [Fact]
    public void EveryTruncation_IsRejected()
    {
        AssertTruncation(PlanetHandshake.Size,
            (b, m) => PlanetHandshake.TryWrite(b, m, out _),
            (b, out m, out s, out c) => PlanetHandshake.TryRead(b, out m, out s, out c),
            SampleHandshake());
        AssertTruncation(PlayerStatePacket.Size,
            (b, m) => PlayerStatePacket.TryWrite(b, m, out _),
            (b, out m, out s, out c) => PlayerStatePacket.TryRead(b, out m, out s, out c),
            SampleState());
        AssertTruncation(RegionTransferRequest.Size,
            (b, m) => RegionTransferRequest.TryWrite(b, m, out _),
            (b, out m, out s, out c) => RegionTransferRequest.TryRead(b, out m, out s, out c),
            SampleRequest());
        AssertTruncation(RegionTransferAck.Size,
            (b, m) => RegionTransferAck.TryWrite(b, m, out _),
            (b, out m, out s, out c) => RegionTransferAck.TryRead(b, out m, out s, out c),
            SampleAck());
    }

    private static void AssertTruncation<T>(int size, WriteMessage<T> write, ReadMessage<T> read, T message)
        where T : struct
    {
        var full = new byte[size];
        Assert.True(write(full, message));

        for (int len = 0; len < size; len++)
        {
            var truncated = new byte[len];
            full.AsSpan(0, len).CopyTo(truncated);
            Assert.False(read(truncated, out _, out PlanetWireStatus status, out int consumed),
                         $"长度 {len}/{size} 竟然解成功了");
            Assert.Equal(PlanetWireStatus.Truncated, status);
            Assert.Equal(0, consumed);
        }
    }

    private delegate bool WriteMessage<T>(Span<byte> buffer, T message) where T : struct;
    private delegate bool ReadMessage<T>(ReadOnlySpan<byte> buffer, out T message,
                                         out PlanetWireStatus status, out int consumed) where T : struct;

    [Fact]
    public void TrailingBytes_AreRejected_ButFieldsStillDecoded()
    {
        var buffer = new byte[PlayerStatePacket.Size + 1];
        PlayerStatePacket.TryWrite(buffer, SampleState(), out _);
        Assert.False(PlayerStatePacket.TryRead(buffer, out var state, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.TrailingBytes, status);
        Assert.Equal(PlayerStatePacket.Size, consumed);
        Assert.Equal(SampleState(), state);           // 拒绝也要回对账字段
    }

    [Fact]
    public void UnsupportedProtocol_IsRejected_ButFieldsStillDecoded()
    {
        var buffer = new byte[PlanetHandshake.Size];
        PlanetHandshake.TryWrite(buffer, SampleHandshake(99), out _);
        Assert.False(PlanetHandshake.TryRead(buffer, out var message, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.UnsupportedProtocol, status);
        Assert.Equal(PlanetHandshake.Size, consumed);
        Field(PlanetConstants.PlanetRadiusCm, message.PlanetRadiusCm, "planetRadiusCm");
        Field((ushort)99, message.WireProtocol, "wireProtocol");

        // 正向对照：同一夹具只换协议号就该通过
        PlanetHandshake.TryWrite(buffer, SampleHandshake(), out _);
        Assert.True(PlanetHandshake.TryRead(buffer, out _, out var ok, out _));
        Assert.Equal(PlanetWireStatus.Ok, ok);
    }

    [Fact]
    public void LocalCoordinate_OverRegionSpan_IsRejected()
    {
        var packet = SampleState();
        packet.LocalXCm = PlayerStatePacket.MaxAbsLocalCm + 1;
        var buffer = new byte[PlayerStatePacket.Size];
        PlayerStatePacket.TryWrite(buffer, packet, out _);
        Assert.False(PlayerStatePacket.TryRead(buffer, out var decoded, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.InvalidRegion, status);
        Assert.Equal(PlayerStatePacket.Size, consumed);
        Field(packet.LocalXCm, decoded.LocalXCm, "localX");     // 报错也要把原值带回给日志

        // 正向对照：同夹具取到边界值就该通过
        packet.LocalXCm = PlayerStatePacket.MaxAbsLocalCm;
        PlayerStatePacket.TryWrite(buffer, packet, out _);
        Assert.True(PlayerStatePacket.TryRead(buffer, out _, out var ok, out _));
        Assert.Equal(PlanetWireStatus.Ok, ok);
    }

    // ---------- 量化误差上界 ----------

    [Fact]
    public void LocalQuantisation_ErrorIsAtMostHalfCentimetre()
    {
        var rng = new Random(2026);
        for (int i = 0; i < 5000; i++)
        {
            double value = (rng.NextDouble() * 2 - 1) * 4_500_000.0;
            int q = PlayerStatePacket.QuantizeLocalCm(value, out double error);
            Assert.True(Math.Abs(error) <= 0.5 + 1e-9, $"{value} → {q}，误差 {error}");
            Assert.True(Math.Abs(q - value) <= 0.5 + 1e-9);
        }
        Assert.Equal(PlayerStatePacket.MaxAbsLocalCm, PlayerStatePacket.QuantizeLocalCm(1e30, out _));
        Assert.Equal(-PlayerStatePacket.MaxAbsLocalCm, PlayerStatePacket.QuantizeLocalCm(-1e30, out _));
    }

    [Fact]
    public void AngleQuantisation_Monotonic_Wraps_AndBoundedError()
    {
        const double step = 1.0 / 65536.0;
        ushort previous = 0;
        for (int i = 1; i < 400; i++)           // 上界不含 1.0：那一圈按定义回绕成 0，误差判据不适用
        {
            double turn = i / 400.0;
            ushort q = PlayerStatePacket.QuantizeAngle01(turn);
            Assert.True(Math.Abs(PlayerStatePacket.DecodeAngle01(q) - turn) <= step, $"turn={turn} 量化误差越界");
            Assert.True(q >= previous, $"turn={turn} 处量化值倒退：{q} < {previous}");
            previous = q;
        }
        Assert.Equal((ushort)0, PlayerStatePacket.QuantizeAngle01(1.0));      // 一圈回绕
        Assert.Equal((ushort)0, PlayerStatePacket.QuantizeAngle01(0.0));
        Assert.Equal((ushort)32768, PlayerStatePacket.QuantizeAngle01(0.5));
        Assert.Equal((ushort)32768, PlayerStatePacket.QuantizeAngle01(-0.5)); // 负半圈同值
    }

    [Fact]
    public void QuaternionQuantisation_ErrorIsBounded()
    {
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            double x = rng.NextDouble() * 2 - 1, y = rng.NextDouble() * 2 - 1,
                   z = rng.NextDouble() * 2 - 1, w = rng.NextDouble() * 2 - 1;
            var q = QuaternionQuantized.From(x, y, z, w);
            Assert.True(Math.Abs(q.DecodeX - x) <= 0.5 / QuaternionQuantized.Scale + 1e-12);
            Assert.True(Math.Abs(q.DecodeY - y) <= 0.5 / QuaternionQuantized.Scale + 1e-12);
            Assert.True(Math.Abs(q.DecodeZ - z) <= 0.5 / QuaternionQuantized.Scale + 1e-12);
            Assert.True(Math.Abs(q.DecodeW - w) <= 0.5 / QuaternionQuantized.Scale + 1e-12);
        }

        var clamp = QuaternionQuantized.From(2.0, -2.0, 0.25, 0.25);
        Assert.Equal(short.MaxValue, clamp.X);
        Assert.Equal(short.MinValue, clamp.Y);
    }

    // ---------- 口径不一致的可读回执 ----------

    [Fact]
    public void DescribeMismatch_NamesTheFirstDifferentField()
    {
        var expected = SampleHandshake();
        Assert.Equal("", PlanetHandshake.DescribeMismatch(expected, expected));

        var other = expected; other.PlanetRadiusCm += 1;
        Assert.Contains("radius", PlanetHandshake.DescribeMismatch(expected, other));

        other = expected; other.TerrainVersion += 1;
        Assert.Contains("terrainVersion", PlanetHandshake.DescribeMismatch(expected, other));

        other = expected; other.BasefieldHashHi ^= 0xFF;
        Assert.Contains("basefieldHash", PlanetHandshake.DescribeMismatch(expected, other));

        other = expected; other.RegionLevel = 12;
        Assert.Contains("regionLevel", PlanetHandshake.DescribeMismatch(expected, other));

        other = expected; other.WireProtocol = 7;
        Assert.Contains("wire", PlanetHandshake.DescribeMismatch(expected, other));
    }

    [Fact]
    public void Handshake_CarriesTerrainIdentityFromTheRealProfile()
    {
        // 握手里的地形口径必须直接来自 TerrainProfile / manifest，不能另算一套
        var grid0 = new short?[4, 8];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 8; c++)
                grid0[r, c] = 100;
        using var dem = TempDem.Create(45.0, (0, grid0));
        var manifest = dem.Load();
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, 15.0 / 3600.0);
        uint version = profile.ComputeTerrainVersion(manifest.ContentSeed, manifest.Format);

        var handshake = new PlanetHandshake
        {
            WireProtocol = PlanetHandshake.ProtocolVersion,
            PlanetRadiusCm = PlanetConstants.PlanetRadiusCm,
            RegionLevel = PlanetConstants.DefaultRegionLevel,
            TerrainSeed = manifest.ContentSeed,
            TerrainVersion = version,
            BasefieldVersion = (uint)manifest.Format,
            BasefieldHashHi = manifest.ContentSeed,
        };
        var buffer = new byte[PlanetHandshake.Size];
        PlanetHandshake.TryWrite(buffer, handshake, out _);
        Assert.True(PlanetHandshake.TryRead(buffer, out var read, out _, out _));
        Field(version, read.TerrainVersion, "terrainVersion");
        Field(manifest.ContentSeed, read.TerrainSeed, "terrainSeed");
        Field((byte)PlanetConstants.DefaultRegionLevel, read.RegionLevel, "regionLevel");

        // 换基场身份必须换出不同 terrainVersion：证明版本真的吃到了基场，而不是只吃 profile
        using var grid = new DemGrid(manifest);
        var sameIdentity = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
        var otherIdentity = new HybridHeightSource(grid, profile, manifest.ContentSeed ^ 0x5, manifest.Format);
        Assert.Equal(version, sameIdentity.TerrainVersion);
        Assert.NotEqual(version, otherIdentity.TerrainVersion);
    }

    /// <summary>把局部三轴映到指定全局指向的右手基。</summary>
    private static QuaternionQuantized Basis(PlanetVector3 east, PlanetVector3 up, PlanetVector3 south) =>
        QuaternionQuantized.FromBasis(east, up, south);

    private static double Deviation(QuaternionQuantized q, PlanetVector3 localAxis, PlanetVector3 expected)
    {
        PlanetVector3 got = q.Rotate(localAxis);
        return Math.Sqrt((got.X - expected.X) * (got.X - expected.X)
                       + (got.Y - expected.Y) * (got.Y - expected.Y)
                       + (got.Z - expected.Z) * (got.Z - expected.Z));
    }

    [Fact]
    public void FromBasis_RoundTripsEveryAxisImage()
    {
        // 恒等基：East=X Up=Y South=Z（Cross(X,Y)=Z，右手）
        var identity = Basis(PlanetVector3.AxisX, PlanetVector3.AxisY, PlanetVector3.AxisZ);
        Assert.True(Deviation(identity, PlanetVector3.AxisX, PlanetVector3.AxisX) < 1e-4);
        Assert.True(Deviation(identity, PlanetVector3.AxisY, PlanetVector3.AxisY) < 1e-4);
        Assert.True(Deviation(identity, PlanetVector3.AxisZ, PlanetVector3.AxisZ) < 1e-4);

        // 90° 循环基：X→Y、Y→Z、Z→X（Cross(Y,Z)=X，右手）
        var cycle = Basis(PlanetVector3.AxisY, PlanetVector3.AxisZ, PlanetVector3.AxisX);
        Assert.True(Deviation(cycle, PlanetVector3.AxisX, PlanetVector3.AxisY) < 1e-4);
        Assert.True(Deviation(cycle, PlanetVector3.AxisY, PlanetVector3.AxisZ) < 1e-4);
        Assert.True(Deviation(cycle, PlanetVector3.AxisZ, PlanetVector3.AxisX) < 1e-4);

        // 绕 Y 转 180°：East=−X、Up=Y、South=−Z（Cross(−X,Y)=−Z，右手）——这一档 w 恰好为 0
        var halfTurn = Basis(-PlanetVector3.AxisX, PlanetVector3.AxisY, -PlanetVector3.AxisZ);
        Assert.True(Math.Abs(halfTurn.DecodeW) < 1e-4, "180° 姿态的 w 该是 0，夹具不对");
        Assert.True(Deviation(halfTurn, PlanetVector3.AxisX, -PlanetVector3.AxisX) < 1e-4,
                    "w=0 时旋转变换被归零：闭式漏了 |vec|² 那一项");
        Assert.True(Deviation(halfTurn, PlanetVector3.AxisY, PlanetVector3.AxisY) < 1e-4);
        Assert.True(Deviation(halfTurn, PlanetVector3.AxisZ, -PlanetVector3.AxisZ) < 1e-4);
    }

    [Fact]
    public void FromBasis_MatchesRealRegionFramesWithinQuantisationBound()
    {
        double step = 1.0 / QuaternionQuantized.Scale;
        double worst = 0;
        foreach (var key in new[]
        {
            new RegionKey(0, 14, 8192, 8192), new RegionKey(1, 14, 0, 16383),
            new RegionKey(2, 10, 512, 512), new RegionKey(3, 15, 32767, 32767),
            new RegionKey(4, 0, 0, 0), new RegionKey(5, 12, 4095, 4095),
        })
        {
            var frame = RegionFrame.Build(key);
            var q = Basis(frame.East, frame.Up, frame.South);
            worst = Math.Max(worst, Deviation(q, PlanetVector3.AxisX, frame.East));
            worst = Math.Max(worst, Deviation(q, PlanetVector3.AxisY, frame.Up));
            worst = Math.Max(worst, Deviation(q, PlanetVector3.AxisZ, frame.South));

            // 三个像之间仍要正交（量化后允许微小偏离，但不许漂成另一个基）
            PlanetVector3 e = q.Rotate(PlanetVector3.AxisX), u = q.Rotate(PlanetVector3.AxisY);
            Assert.True(Math.Abs(PlanetVector3.Dot(e, u)) < 1e-3, $"{key} 的东·上在量化后不再正交");
        }

        // 上界从量化步长推出来，不是挑一个刚好过得去的数：四个分量各 ≤ 半格
        Assert.True(worst < 8 * step, $"姿态量化后基向量最大偏差 {worst:E3}，超过 8×Q15 步长 {8 * step:E3}");
    }

    [Fact]
    public void FromBasis_RejectsNonRightHandedOrNonUnitBasis()
    {
        var east = PlanetVector3.AxisX;
        var up = PlanetVector3.AxisY;
        Assert.Throws<ArgumentException>(() => QuaternionQuantized.FromBasis(east, up, up));        // 退化：两轴重合
        Assert.Throws<ArgumentException>(() => QuaternionQuantized.FromBasis(east, -up, east));     // 既不正交也不同向第三轴
        Assert.Throws<ArgumentException>(() => QuaternionQuantized.FromBasis(east * 2, up, PlanetVector3.AxisZ));  // 非单位
        // 左手系（Cross(东,上) = −南）也要拒
        Assert.Throws<ArgumentException>(() => QuaternionQuantized.FromBasis(east, up, -PlanetVector3.AxisZ));

        // 正向对照：真区域的基就是合法输入
        var frame = RegionFrame.Build(new RegionKey(0, 14, 8192, 8192));
        var ok = QuaternionQuantized.FromBasis(frame.East, frame.Up, frame.South);
        Assert.True(Deviation(ok, PlanetVector3.AxisY, frame.Up) < 1e-4);
    }
}

using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// v17 翻越协议契约：包类型/MemoryPack union 注册、编解码往返、拒绝原因数值与
/// <see cref="TraversalRejectReason"/> 对齐（包层刻意不引用 Sim.Contracts，靠本组测试钉住）。
/// </summary>
public class TraversalProtocolContractTests
{
    [Fact]
    public void PacketKinds_AreDistinct_AndBeyondPreviousTails()
    {
        Assert.Equal(19, (int)SyncPacketKind.TraversalRequest);
        Assert.Equal(20, (int)SyncPacketKind.TraversalResult);
        Assert.NotEqual(SyncPacketKind.TraversalRequest, SyncPacketKind.TraversalResult);
    }

    [Fact]
    public void Codec_RoundTripsTraversalRequest()
    {
        var packet = new TraversalRequestPacket
        {
            CharacterId = 12345UL,
            ClientTick = 67890L,
            StartX = 1.25f,
            StartY = -2.5f,
            StartZ = 0.75f,
            EndX = 2.0f,
            EndY = -2.0f,
            EndZ = 1.5f,
            Duration = 0.65f,
            ArcHeight = 0.12f,
            EndYaw = 0.5f,
            CurveVersion = TraversalMath.Version,
        };

        SyncPacketCodec.Encode(packet, out var frame, out var length);
        try
        {
            var decoded = SyncPacketCodec.Decode(frame.AsSpan(0, length));
            var round = Assert.IsType<TraversalRequestPacket>(decoded);
            Assert.Equal(packet.CharacterId, round.CharacterId);
            Assert.Equal(packet.ClientTick, round.ClientTick);
            Assert.Equal(packet.StartX, round.StartX);
            Assert.Equal(packet.StartY, round.StartY);
            Assert.Equal(packet.StartZ, round.StartZ);
            Assert.Equal(packet.EndX, round.EndX);
            Assert.Equal(packet.EndY, round.EndY);
            Assert.Equal(packet.EndZ, round.EndZ);
            Assert.Equal(packet.Duration, round.Duration);
            Assert.Equal(packet.ArcHeight, round.ArcHeight);
            Assert.Equal(packet.EndYaw, round.EndYaw);
            Assert.Equal(packet.CurveVersion, round.CurveVersion);
            Assert.Equal(SyncPacketKind.TraversalRequest, round.Kind);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    [Fact]
    public void Codec_RoundTripsTraversalResult()
    {
        var packet = new TraversalResultPacket
        {
            CharacterId = 7UL,
            ClientTick = 99L,
            Accepted = false,
            Status = (byte)TraversalRejectReason.StartTooFar,
            EndX = 3f,
            EndY = 4f,
            EndZ = 5f,
            ServerTick = 4321L,
        };

        SyncPacketCodec.Encode(packet, out var frame, out var length);
        try
        {
            var decoded = SyncPacketCodec.Decode(frame.AsSpan(0, length));
            var round = Assert.IsType<TraversalResultPacket>(decoded);
            Assert.Equal(packet.CharacterId, round.CharacterId);
            Assert.Equal(packet.ClientTick, round.ClientTick);
            Assert.False(round.Accepted);
            Assert.Equal(packet.Status, round.Status);
            Assert.Equal(packet.EndX, round.EndX);
            Assert.Equal(packet.EndY, round.EndY);
            Assert.Equal(packet.EndZ, round.EndZ);
            Assert.Equal(packet.ServerTick, round.ServerTick);
            Assert.Equal(SyncPacketKind.TraversalResult, round.Kind);
        }
        finally
        {
            SyncPacketCodec.ReturnFrame(frame);
        }
    }

    /// <summary>
    /// 包层用 <c>byte Status</c> 而不引用 Sim.Contracts——这份词表一旦漂移，
    /// 客户端会把"服务端拒绝"读成未知值而静默不回正，所以逐值钉住。
    /// </summary>
    [Fact]
    public void ResultStatusVocabulary_MatchesRejectReason()
    {
        Assert.Equal(0, (int)TraversalRejectReason.None);
        Assert.Equal(1, (int)TraversalRejectReason.InvalidDuration);
        Assert.Equal(2, (int)TraversalRejectReason.HeightOutOfRange);
        Assert.Equal(3, (int)TraversalRejectReason.HorizontalOutOfRange);
        Assert.Equal(4, (int)TraversalRejectReason.ArcOutOfRange);
        Assert.Equal(5, (int)TraversalRejectReason.StartTooFar);
        Assert.Equal(6, (int)TraversalRejectReason.InvalidNumbers);
        Assert.Equal(7, (int)TraversalRejectReason.AlreadyActive);
        Assert.Equal(8, (int)TraversalRejectReason.Cooldown);
        Assert.Equal(9, (int)TraversalRejectReason.AuthorityUnavailable);

        // Accepted=true 必须对应 Status=0（服务端两条字段同源构造）。
        var accepted = new TraversalResultPacket { Accepted = true, Status = (byte)TraversalRejectReason.None };
        Assert.True(accepted.Accepted);
        Assert.Equal(0, accepted.Status);
    }

    [Fact]
    public void ProtocolVersion_IsAtLeast17()
    {
        // v17 引入翻越包；降到 16 会让新客户端发旧标签，老服务端按未知包丢弃。
        Assert.True(SyncProtocolVersion.Current >= 17);
        Assert.True(SyncProtocolVersion.MinimumSupported <= 5);
    }
}

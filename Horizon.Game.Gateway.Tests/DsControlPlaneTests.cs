using System;
using System.Buffers;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Gateway.Services;
using Horizon.Game.Message.Sync;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M2.1/M2.3 —— DS 上行控制面与下行注入测试（验收门 H2-a）。
/// <para>
/// 这些测试锁定的是**接线**而非算法：在本次改动之前
/// <see cref="DSRegistry.RegisterOrUpdate"/> / <c>Heartbeat</c> / <c>Unregister</c>
/// 全仓没有任何调用方（缺口 G3），<see cref="DSRegistry"/> 只是一张永远为空的表。
/// 因此这里断言的重点是"控制消息确实改变了路由表"与"DS 快照确实进了分发管线"。
/// </para>
/// </summary>
public class DsControlPlaneTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSink : IDsDownlinkSink
    {
        public List<FanoutEvent> Events { get; } = new();
        public bool Accept { get; set; } = true;
        public bool TryEnqueue(FanoutEvent evt)
        {
            if (!Accept) return false;
            Events.Add(evt);
            return true;
        }
    }

    private static (DsControlPlane plane, DSRegistry registry, FakeSink sink, DsDownlinkIngress ingress) Build(
        bool enabled = true, string token = "", DateTimeOffset? now = null)
    {
        var t = now ?? T0;
        var registry = new DSRegistry(staleAfter: TimeSpan.FromSeconds(90), clock: () => t);
        var sink = new FakeSink();
        var ingress = new DsDownlinkIngress(sink);
        var plane = new DsControlPlane(registry, ingress, isEnabled: () => enabled, expectedToken: token);
        return (plane, registry, sink, ingress);
    }

    private static DsRegisterRequest Register(string dsId = "ds-0", long[]? shards = null, string token = "")
        => new DsRegisterRequest
        {
            DsId = dsId,
            ShardIds = shards ?? new long[] { 0 },
            Host = "10.0.0.1",
            Port = 19000,
            Region = "local",
            Token = token,
        };

    // ---------- 注册 ----------

    [Fact]
    public void Register_ValidRequest_PopulatesRoutingTable()
    {
        var (plane, registry, _, _) = Build();

        var result = plane.HandleRegister(Register("ds-a", new long[] { 0, 1 }));

        Assert.True(result.Ok);
        Assert.Equal(1, registry.CountAlive());
        Assert.True(registry.TryResolve(0, out var ep));
        Assert.Equal("ds-a", ep.DsId);
        Assert.Equal("10.0.0.1", ep.Host);
        Assert.Equal(19000, ep.Port);
        Assert.True(registry.TryResolve(1, out _));
        Assert.False(registry.TryResolve(2, out _));
    }

    [Fact]
    public void Register_IsIdempotentUpsert()
    {
        var (plane, registry, _, _) = Build();

        plane.HandleRegister(Register("ds-a", new long[] { 0 }));
        plane.HandleRegister(Register("ds-a", new long[] { 0, 5 }));

        Assert.Equal(1, registry.CountAlive());
        Assert.True(registry.TryResolve(5, out _));
        Assert.Equal(2, plane.RegisterCount);
    }

    [Fact]
    public void Register_BadToken_IsRejectedAndNotRegistered()
    {
        var (plane, registry, _, _) = Build(token: "expected");

        var result = plane.HandleRegister(Register(token: "wrong"));

        Assert.False(result.Ok);
        Assert.Equal("bad_token", result.Reason);
        Assert.Equal(0, registry.CountAlive());
        Assert.Equal(1, plane.TokenRejectedCount);
    }

    [Fact]
    public void Register_CorrectToken_IsAccepted()
    {
        var (plane, registry, _, _) = Build(token: "expected");
        var result = plane.HandleRegister(Register(token: "expected"));
        Assert.True(result.Ok);
        Assert.Equal(1, registry.CountAlive());
    }

    [Fact]
    public void Register_WhenChannelDisabled_IsRejected()
    {
        var (plane, registry, _, _) = Build(enabled: false);
        var result = plane.HandleRegister(Register());

        Assert.False(result.Ok);
        Assert.Equal("ds_channel_disabled", result.Reason);
        Assert.Equal(0, registry.CountAlive());
        Assert.Equal(1, plane.DisabledRejectedCount);
    }

    [Theory]
    [InlineData("", "10.0.0.1", 19000, "missing_dsid")]
    [InlineData("ds-a", "", 19000, "missing_host")]
    [InlineData("ds-a", "10.0.0.1", 0, "bad_port")]
    [InlineData("ds-a", "10.0.0.1", 70000, "bad_port")]
    public void Register_InvalidFields_AreRejected(string dsId, string host, int port, string expectedReason)
    {
        var (plane, registry, _, _) = Build();
        var req = Register(dsId);
        req.Host = host;
        req.Port = port;

        var result = plane.HandleRegister(req);

        Assert.False(result.Ok);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(0, registry.CountAlive());
    }

    [Fact]
    public void Register_NullRequest_IsRejected()
    {
        var (plane, _, _, _) = Build();
        var result = plane.HandleRegister(null);
        Assert.False(result.Ok);
        Assert.Equal("null_request", result.Reason);
    }

    [Fact]
    public void Register_NullShardArray_DoesNotThrow()
    {
        var (plane, registry, _, _) = Build();
        var req = Register();
        req.ShardIds = null!;

        var result = plane.HandleRegister(req);

        Assert.True(result.Ok);
        Assert.Equal(1, registry.CountAlive());
    }

    // ---------- 心跳 ----------

    [Fact]
    public void Heartbeat_RefreshesStaleEntry()
    {
        var now = T0;
        var registry = new DSRegistry(staleAfter: TimeSpan.FromSeconds(90), clock: () => now);
        var sink = new FakeSink();
        var plane = new DsControlPlane(registry, new DsDownlinkIngress(sink), () => true);

        plane.HandleRegister(Register("ds-a"));

        // 推进到 80s：仍存活但接近过期边界。
        now = T0.AddSeconds(80);
        Assert.Equal(1, registry.CountAlive());

        // 心跳续约后，再过 80s 依然存活（证明续约确实生效，而不是"恰好没过期"）。
        plane.HandleHeartbeat(new DsIdentityRequest { DsId = "ds-a" });
        now = T0.AddSeconds(160);

        Assert.Equal(1, registry.CountAlive());
        Assert.True(plane.HeartbeatCount >= 1);
    }

    [Fact]
    public void Heartbeat_WithoutRenewal_EventuallyExpires()
    {
        var now = T0;
        var registry = new DSRegistry(staleAfter: TimeSpan.FromSeconds(90), clock: () => now);
        var plane = new DsControlPlane(registry, new DsDownlinkIngress(new FakeSink()), () => true);

        plane.HandleRegister(Register("ds-a"));
        now = T0.AddSeconds(91);

        Assert.Equal(0, registry.CountAlive());
        Assert.False(registry.TryResolve(0, out _));
    }

    [Fact]
    public void Heartbeat_UnknownDs_IsAcceptedButCounted()
    {
        var (plane, registry, _, _) = Build();

        var result = plane.HandleHeartbeat(new DsIdentityRequest { DsId = "ghost" });

        // 语义：不报错（否则 DS 会误以为 ID 非法而放弃），但计数以便运维发现异常注册。
        Assert.True(result.Ok);
        Assert.Equal(0, registry.CountAlive());
        Assert.Equal(1, plane.UnknownDsHeartbeatCount);
    }

    [Fact]
    public void Heartbeat_BadToken_IsRejected()
    {
        var (plane, _, _, _) = Build(token: "expected");
        var result = plane.HandleHeartbeat(new DsIdentityRequest { DsId = "ds-a", Token = "nope" });
        Assert.False(result.Ok);
        Assert.Equal("bad_token", result.Reason);
    }

    [Fact]
    public void Heartbeat_WhenDisabled_IsRejected()
    {
        var (plane, _, _, _) = Build(enabled: false);
        var result = plane.HandleHeartbeat(new DsIdentityRequest { DsId = "ds-a" });
        Assert.False(result.Ok);
        Assert.Equal("ds_channel_disabled", result.Reason);
    }

    // ---------- 摘除 ----------

    [Fact]
    public void Unregister_RemovesFromRoutingTable()
    {
        var (plane, registry, _, _) = Build();
        plane.HandleRegister(Register("ds-a"));
        Assert.Equal(1, registry.CountAlive());

        var result = plane.HandleUnregister(new DsIdentityRequest { DsId = "ds-a" });

        Assert.True(result.Ok);
        Assert.Equal(0, registry.CountAlive());
        Assert.False(registry.TryResolve(0, out _));
    }

    [Fact]
    public void Unregister_WorksEvenWhenChannelDisabled()
    {
        // 关键语义：灰度开关关闭时必须允许 DS 自摘，否则残留在路由表里的条目
        // 会造成最长 90s 的路由黑洞。
        var enabled = true;
        var registry = new DSRegistry(clock: () => T0);
        var plane = new DsControlPlane(registry, new DsDownlinkIngress(new FakeSink()), () => enabled);

        plane.HandleRegister(Register("ds-a"));
        enabled = false;

        var result = plane.HandleUnregister(new DsIdentityRequest { DsId = "ds-a" });

        Assert.True(result.Ok);
        Assert.Equal(0, registry.CountAlive());
    }

    // ---------- 快照批次 ----------

    [Fact]
    public void SnapshotBatch_WhenEnabled_IsIngested()
    {
        var (plane, _, sink, _) = Build();

        var batch = new DsSnapshotBatch
        {
            Items = new[] { MakeItem(new long[] { 1001 }, MakeSnapshotPacket(7)) },
        };

        plane.HandleSnapshotBatch(batch);

        Assert.Single(sink.Events);
        Assert.Contains(1001L, sink.Events[0].TargetSessionIds);
        Assert.Equal(SyncPacketKind.Snapshot, sink.Events[0].Packet.Kind);
    }

    [Fact]
    public void SnapshotBatch_WhenDisabled_IsDropped()
    {
        var (plane, _, sink, _) = Build(enabled: false);

        var batch = new DsSnapshotBatch
        {
            Items = new[] { MakeItem(new long[] { 1001 }, MakeSnapshotPacket(7)) },
        };

        var result = plane.HandleSnapshotBatch(batch);

        Assert.False(result.Ok);
        Assert.False(result.ExpectsAck); // 单向消息：即使失败也不应答
        Assert.Empty(sink.Events);
    }

    // ---------- 下行注入 ----------

    [Fact]
    public void Ingress_DecodesFrameAndTargetsSessions()
    {
        var sink = new FakeSink();
        var ingress = new DsDownlinkIngress(sink);

        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                MakeItem(new long[] { 1, 2 }, MakeSnapshotPacket(11)),
                MakeItem(new long[] { 3 }, MakeSnapshotPacket(12)),
            },
        };

        var accepted = ingress.Ingest(batch);

        Assert.Equal(2, accepted);
        Assert.Equal(2, sink.Events.Count);
        Assert.Equal(2, sink.Events[0].TargetSessionIds.Count);
        Assert.Equal(2L, ingress.AcceptedCount);
    }

    [Fact]
    public void Ingress_CorruptFrame_DropsOnlyThatItem()
    {
        var sink = new FakeSink();
        var ingress = new DsDownlinkIngress(sink);

        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                new DsSnapshotItem { SessionIds = new long[] { 1 }, Frame = new byte[] { 0xFF } }, // 短于帧头
                MakeItem(new long[] { 2 }, MakeSnapshotPacket(21)),
            },
        };

        var accepted = ingress.Ingest(batch);

        // 关键语义：一帧坏数据不能让整批玩家的状态更新全部丢失。
        Assert.Equal(1, accepted);
        Assert.Single(sink.Events);
        Assert.Equal(1, ingress.DecodeFailureCount);
    }

    [Fact]
    public void Ingress_EmptyAudience_IsSkipped()
    {
        var sink = new FakeSink();
        var ingress = new DsDownlinkIngress(sink);

        var batch = new DsSnapshotBatch
        {
            Items = new[] { MakeItem(Array.Empty<long>(), MakeSnapshotPacket(31)) },
        };

        Assert.Equal(0, ingress.Ingest(batch));
        Assert.Empty(sink.Events);
        Assert.Equal(1, ingress.SkippedEmptyAudienceCount);
    }

    [Fact]
    public void Ingress_Backpressure_IsCountedNotThrown()
    {
        var sink = new FakeSink { Accept = false };
        var ingress = new DsDownlinkIngress(sink);

        var batch = new DsSnapshotBatch
        {
            Items = new[] { MakeItem(new long[] { 1 }, MakeSnapshotPacket(41)) },
        };

        Assert.Equal(0, ingress.Ingest(batch));
        Assert.Equal(1, ingress.DroppedByBackpressureCount);
        Assert.Equal(0, ingress.AcceptedCount);
    }

    [Fact]
    public void Ingress_NullBatch_IsNoOp()
    {
        var ingress = new DsDownlinkIngress(new FakeSink());
        Assert.Equal(0, ingress.Ingest(null));
        Assert.Equal(0, ingress.Ingest(new DsSnapshotBatch { Items = Array.Empty<DsSnapshotItem>() }));
    }

    [Fact]
    public void Ingress_DropCallback_ReceivesReason()
    {
        var reasons = new List<string>();
        var ingress = new DsDownlinkIngress(new FakeSink { Accept = false }, r => reasons.Add(r));

        ingress.Ingest(new DsSnapshotBatch
        {
            Items = new[] { MakeItem(new long[] { 1 }, MakeSnapshotPacket(51)) },
        });

        Assert.Contains("backpressure", reasons);
    }

    // ---------- 测试辅助 ----------

    private static DsSnapshotItem MakeItem(long[] sessions, SyncPacket packet)
        => new DsSnapshotItem { SessionIds = sessions, Frame = EncodeToExactArray(packet) };

    private static SyncPacket MakeSnapshotPacket(long serverTick)
        => new SnapshotPacket
        {
            ProtocolVersion = SyncProtocolVersion.Current,
            ServerTick = serverTick,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };

    private static byte[] EncodeToExactArray(SyncPacket packet)
    {
        SyncPacketCodec.Encode(packet, out var rented, out var length);
        try
        {
            var exact = new byte[length];
            Array.Copy(rented, exact, length);
            return exact;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

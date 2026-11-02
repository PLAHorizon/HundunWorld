using System;
using System.Buffers.Binary;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M2 —— DS 上行协议（控制面 + 定向下行帧）单元测试。
/// <para>
/// 覆盖目标：长度前缀分帧在 TCP 字节流下的**粘包/拆包**、超长帧拒绝（DoS 防护）、
/// 编解码往返一致性、以及「未知 kind 前向兼容不抛异常」。
/// </para>
/// <para>
/// 这些是 DS↔网关闭环（验收门 H2）里最容易出线上事故的一层：DS 侧（UnrealSharp）
/// 与网关侧共用同一份实现，任何分帧语义错误会表现为"偶发丢包/半包解析"，极难排查。
/// 因此把分帧逻辑做成纯函数并在此穷举边界。
/// </para>
/// </summary>
public class DsUplinkProtocolTests
{
    // ---------- 分帧：完整帧 ----------

    [Fact]
    public void TryReadFrame_SingleCompleteFrame_ReturnsPayload()
    {
        var reader = new DsFrameReader();
        var frame = DsUplinkCodec.FramePayload(new byte[] { 1, 2, 3, 4 });

        reader.Append(frame);

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload.ToArray());

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
        Assert.False(reader.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_EmptyPayload_IsValidFrame()
    {
        var reader = new DsFrameReader();
        reader.Append(DsUplinkCodec.FramePayload(ReadOnlySpan<byte>.Empty));

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(0, payload.Length);

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    // ---------- 分帧：拆包（半帧到达）----------

    [Fact]
    public void TryReadFrame_SplitAcrossAppends_OnlyCompletesWhenWhole()
    {
        var reader = new DsFrameReader();
        var frame = DsUplinkCodec.FramePayload(new byte[] { 10, 20, 30 });

        // 先到一半（含长度前缀的一部分）
        reader.Append(frame.AsSpan(0, 3));
        Assert.False(reader.TryReadFrame(out _));

        // 再到长度前缀剩余部分——仍不完整（负载未到齐）
        reader.Append(frame.AsSpan(3, 3));
        Assert.False(reader.TryReadFrame(out _));

        // 最后到达尾部
        reader.Append(frame.AsSpan(6));
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(new byte[] { 10, 20, 30 }, payload.ToArray());

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    [Fact]
    public void TryReadFrame_ByteByByteArrival_EventuallySucceeds()
    {
        var reader = new DsFrameReader(initialCapacity: 4);
        var frame = DsUplinkCodec.FramePayload(new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 });

        for (var i = 0; i < frame.Length - 1; i++)
        {
            reader.Append(frame.AsSpan(i, 1));
            Assert.False(reader.TryReadFrame(out _));
        }

        reader.Append(frame.AsSpan(frame.Length - 1, 1));
        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(8, payload.Length);

        reader.ConsumeFrame();
        Assert.Equal(0, reader.BufferedBytes);
    }

    // ---------- 分帧：粘包（一帧半 / 多帧）----------

    [Fact]
    public void TryReadFrame_StickyPackets_YieldsFramesInOrder()
    {
        var reader = new DsFrameReader();
        var a = DsUplinkCodec.FramePayload(new byte[] { 1 });
        var b = DsUplinkCodec.FramePayload(new byte[] { 2, 2 });
        var c = DsUplinkCodec.FramePayload(new byte[] { 3, 3, 3 });

        var combined = new byte[a.Length + b.Length + c.Length];
        a.CopyTo(combined, 0);
        b.CopyTo(combined, a.Length);
        c.CopyTo(combined, a.Length + b.Length);
        reader.Append(combined);

        Assert.True(reader.TryReadFrame(out var p1));
        Assert.Equal(new byte[] { 1 }, p1.ToArray());
        reader.ConsumeFrame();

        Assert.True(reader.TryReadFrame(out var p2));
        Assert.Equal(new byte[] { 2, 2 }, p2.ToArray());
        reader.ConsumeFrame();

        Assert.True(reader.TryReadFrame(out var p3));
        Assert.Equal(new byte[] { 3, 3, 3 }, p3.ToArray());
        reader.ConsumeFrame();

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(0, reader.BufferedBytes);
    }

    [Fact]
    public void TryReadFrame_FramePlusPartialNext_LeavesRemainderBuffered()
    {
        var reader = new DsFrameReader();
        var a = DsUplinkCodec.FramePayload(new byte[] { 9, 9 });
        var b = DsUplinkCodec.FramePayload(new byte[] { 8, 8, 8, 8 });

        var combined = new byte[a.Length + 5];
        a.CopyTo(combined, 0);
        b.AsSpan(0, 5).CopyTo(combined.AsSpan(a.Length));
        reader.Append(combined);

        Assert.True(reader.TryReadFrame(out var p1));
        Assert.Equal(new byte[] { 9, 9 }, p1.ToArray());
        reader.ConsumeFrame();

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(5, reader.BufferedBytes);

        // 剩余部分补齐后应能成帧。
        reader.Append(b.AsSpan(5));
        Assert.True(reader.TryReadFrame(out var p2));
        Assert.Equal(new byte[] { 8, 8, 8, 8 }, p2.ToArray());
    }

    // ---------- 分帧：超长帧拒绝（DoS 防护）----------

    [Fact]
    public void TryReadFrame_OversizedDeclaredLength_IsRejectedAndCounted()
    {
        var reader = new DsFrameReader(maxPayloadBytes: 1024);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 1024 * 1024); // 声明 1MiB，超过上限
        reader.Append(header);

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(1, reader.OversizedFrameCount);
        Assert.Equal(0, reader.BufferedBytes); // 非法帧后缓冲被清空
    }

    [Fact]
    public void TryReadFrame_NegativeDeclaredLength_IsRejectedAndCounted()
    {
        var reader = new DsFrameReader();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, -1);
        reader.Append(header);

        Assert.False(reader.TryReadFrame(out _));
        Assert.Equal(1, reader.OversizedFrameCount);
    }

    [Fact]
    public void FramePayload_ExceedingMax_Throws()
    {
        var oversized = new byte[DsUplinkCodec.MaxPayloadBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => DsUplinkCodec.FramePayload(oversized));
    }

    // ---------- 编解码：往返 ----------

    [Fact]
    public void Register_RoundTrip_PreservesAllFields()
    {
        var request = new DsRegisterRequest
        {
            DsId = "ds-shard-0",
            ShardIds = new long[] { 0, 3, 7 },
            Host = "10.0.0.12",
            Port = 19870,
            Region = "华东",
            Token = "s3cr3t",
        };

        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.Register, request);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        reader.ConsumeFrame();

        Assert.True(DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.Register, kind);
        Assert.NotNull(decoded);
        Assert.Equal("ds-shard-0", decoded!.DsId);
        Assert.Equal(new long[] { 0, 3, 7 }, decoded.ShardIds);
        Assert.Equal("10.0.0.12", decoded.Host);
        Assert.Equal(19870, decoded.Port);
        Assert.Equal("华东", decoded.Region);
        Assert.Equal("s3cr3t", decoded.Token);
    }

    [Fact]
    public void SnapshotBatch_RoundTrip_PreservesSessionTargetingAndFrames()
    {
        var batch = new DsSnapshotBatch
        {
            Items = new[]
            {
                new DsSnapshotItem { SessionIds = new long[] { 1001 }, Frame = new byte[] { 0xAA, 0xBB } },
                new DsSnapshotItem { SessionIds = new long[] { 1002, 1003 }, Frame = new byte[] { 0xCC } },
            },
        };

        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.SnapshotBatch, batch);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));
        reader.ConsumeFrame();

        Assert.True(DsUplinkCodec.TryDecode<DsSnapshotBatch>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.SnapshotBatch, kind);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded!.Items.Length);
        Assert.Equal(new long[] { 1002, 1003 }, decoded.Items[1].SessionIds);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Items[0].Frame);
    }

    [Fact]
    public void ControlAck_RoundTrip_PreservesFailureReason()
    {
        var ack = new DsControlAck { Ok = false, Reason = "no_ds_route" };
        var frame = DsUplinkCodec.EncodeFrame(DsUplinkMessageKind.ControlAck, ack);

        var reader = new DsFrameReader();
        reader.Append(frame);
        Assert.True(reader.TryReadFrame(out var payload));

        Assert.True(DsUplinkCodec.TryDecode<DsControlAck>(payload, out _, out var decoded));
        Assert.False(decoded!.Ok);
        Assert.Equal("no_ds_route", decoded.Reason);
    }

    // ---------- 前向兼容与健壮性 ----------

    [Fact]
    public void TryDecode_UnknownKind_DoesNotThrow()
    {
        var payload = new byte[] { 250, 1, 2, 3 }; // kind=250 未定义
        var ok = DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out var kind, out var body);

        Assert.False(ok);
        Assert.Null(body);
        Assert.Equal((DsUplinkMessageKind)250, kind);
    }

    [Fact]
    public void TryDecode_TruncatedBody_DoesNotThrow()
    {
        var payload = new byte[] { (byte)DsUplinkMessageKind.Register, 0xFF, 0x01 };
        var ok = DsUplinkCodec.TryDecode<DsRegisterRequest>(payload, out _, out var body);

        Assert.False(ok);
        Assert.Null(body);
    }

    [Fact]
    public void TryDecode_EmptyPayload_DoesNotThrow()
    {
        Assert.False(DsUplinkCodec.TryDecode<DsRegisterRequest>(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void TryDecode_KindOnlyPayload_IsRejected()
    {
        var payload = new byte[] { (byte)DsUplinkMessageKind.Heartbeat };
        Assert.False(DsUplinkCodec.TryDecode<DsIdentityRequest>(payload, out var kind, out _));
        Assert.Equal(DsUplinkMessageKind.Heartbeat, kind);
    }

    [Fact]
    public void Reader_GrowsPastInitialCapacity_WithoutLosingData()
    {
        var reader = new DsFrameReader(initialCapacity: 8, maxPayloadBytes: 4096);
        var big = new byte[2048];
        for (var i = 0; i < big.Length; i++) big[i] = (byte)(i % 251);

        reader.Append(DsUplinkCodec.FramePayload(big));

        Assert.True(reader.TryReadFrame(out var payload));
        Assert.Equal(2048, payload.Length);
        Assert.Equal(big, payload.ToArray());
    }

    // ---------- 玩法事件跨过线上契约：逐槽位往返 ----------

    [Fact]
    public void GameplayEvent_过线往返_每一格都回到自己那一格()
    {
        // 金币接进钱包那一轮的真实教训：内核侧新加了 Money，DTO 与两个转换函数没跟上，
        // 于是生产链路上每条击杀金币事件跨过这道转换后金额归零，被消费端
        // 「金额 ≤0 就算处理过」那一档安静吞掉——而直接喂内核事件的单测全绿。
        // 所以这里从内核事件出发、走真序列化、再回到内核事件，逐格比对：
        // 每格填互不相同的哨兵值，漏传（变 0）与串位（变成邻格的数）都当场可辨。
        var core = new DsGameplayEvent
        {
            Kind = DsGameplayEventKind.KillMoney,
            ShardId = 11,
            ServerTick = 22,
            ActorCharacterId = 33,
            MonsterEntityId = 44,
            MonsterTemplateId = 55,
            ItemId = 66,
            ItemCount = 77,
            QuestId = 88,
            Money = 99,
        };
        var batch = new DsGameplayEventBatch
        {
            Events = new[] { DsGameplayEventDto.FromCoreEvent(in core) },
        };

        var payload = DsUplinkCodec.EncodePayload(DsUplinkMessageKind.GameplayEvent, batch);
        Assert.True(DsUplinkCodec.TryDecode<DsGameplayEventBatch>(payload, out var kind, out var decoded));
        Assert.Equal(DsUplinkMessageKind.GameplayEvent, kind);
        var back = Assert.Single(decoded!.Events).ToCoreEvent();

        Assert.Equal(core.Kind, back.Kind);
        Assert.Equal(core.ShardId, back.ShardId);
        Assert.Equal(core.ServerTick, back.ServerTick);
        Assert.Equal(core.ActorCharacterId, back.ActorCharacterId);
        Assert.Equal(core.MonsterEntityId, back.MonsterEntityId);
        Assert.Equal(core.MonsterTemplateId, back.MonsterTemplateId);
        Assert.Equal(core.ItemId, back.ItemId);
        Assert.Equal(core.ItemCount, back.ItemCount);
        Assert.Equal(core.QuestId, back.QuestId);
        // 单独把这一格打印出来：这条判据哪天红了，要一眼看出是哪一格串了位。
        Assert.Equal(core.Money, back.Money);
    }

    [Fact]
    public void 内核玩法事件新增一格时线上DTO必须一起长()
    {
        // 上一条测"已有的格都到了"，这条挡"将来新加的格被忘"：两份清单按名字对照，
        // 内核加了字段而 DTO 没跟上 ⇒ 当场红，而不是等某个金额在跨过线上时静默归零。
        var coreFields = typeof(DsGameplayEvent)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.Name)
            .ToHashSet();
        var dtoFields = typeof(DsGameplayEventDto)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var missing = coreFields.Where(n => !dtoFields.Contains(n)).OrderBy(n => n).ToArray();
        var extra = dtoFields.Where(n => !coreFields.Contains(n)).OrderBy(n => n).ToArray();
        Assert.True(missing.Length == 0,
                    "内核事件里有 " + missing.Length + " 格线上 DTO 没有（跨过这道转换会被静默丢掉）：" +
                    string.Join(",", missing));
        Assert.True(extra.Length == 0,
                    "线上 DTO 里有 " + extra.Length + " 格内核事件没有（多半是改名而不是新增）：" +
                    string.Join(",", extra));
    }

    [Fact]
    public void 尸体拾取三帧的编解码往返且种类号只是尾部追加()
    {
        // 种类号是**线上契约**：插入到中间会让新旧两端把请求当回执。所以钉死数值，不钉名字。
        Assert.Equal(10, (int)Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootRequest);
        Assert.Equal(11, (int)Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootForward);
        Assert.Equal(12, (int)Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootResult);

        var request = new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
        {
            SessionId = 2001, CorpseEntityId = 0xDEADBEEFCAFEUL, SlotIndex = 3, DistanceMeters = 4.25f, RequestId = 77,
        };
        var reqFrame = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodeFrame(
            Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootRequest, request);
        var reqPayload = reqFrame.AsSpan(4);
        Assert.True(Horizon.Game.Core.Sim.Server.DsUplinkCodec.TryDecode<Horizon.Game.Core.Sim.Server.DsCorpseLootRequest>(
            reqPayload, out var reqKind, out var reqBody));
        Assert.Equal(Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootRequest, reqKind);
        Assert.Equal(2001, reqBody!.SessionId);
        Assert.Equal(0xDEADBEEFCAFEUL, reqBody.CorpseEntityId);
        Assert.Equal(3, reqBody.SlotIndex);
        Assert.Equal(4.25f, reqBody.DistanceMeters);
        Assert.Equal(77, reqBody.RequestId);

        var forward = new Horizon.Game.Core.Sim.Server.DsCorpseLootForward
        {
            SessionId = 2002, CorpseEntityId = 42UL, SlotIndex = 0, RequestId = 78, ServerTick = 123456,
        };
        var fwdPayload = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodePayload(
            Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootForward, forward);
        Assert.True(Horizon.Game.Core.Sim.Server.DsUplinkCodec.TryDecode<Horizon.Game.Core.Sim.Server.DsCorpseLootForward>(
            fwdPayload, out var fwdKind, out var fwdBody));
        Assert.Equal(Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootForward, fwdKind);
        Assert.Equal(123456, fwdBody!.ServerTick);
        Assert.Equal(78, fwdBody.RequestId);

        var result = new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
        {
            RequestId = 78, Status = 2, ItemId = 555, Count = 2, ConditionKind = 1, QuestId = 90007, Money = 0,
        };
        var resultPayload = Horizon.Game.Core.Sim.Server.DsUplinkCodec.EncodePayload(
            Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootResult, result);
        Assert.True(Horizon.Game.Core.Sim.Server.DsUplinkCodec.TryDecode<Horizon.Game.Core.Sim.Server.DsCorpseLootResult>(
            resultPayload, out var resKind, out var resBody));
        Assert.Equal(Horizon.Game.Core.Sim.Server.DsUplinkMessageKind.CorpseLootResult, resKind);
        Assert.Equal(2, resBody!.Status);
        Assert.Equal(555, resBody.ItemId);
        Assert.Equal(2, resBody.Count);
        Assert.Equal(90007, resBody.QuestId);
    }
}

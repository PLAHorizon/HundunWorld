using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task D.7.2：InputPacket 冗余重传与去重单元测试。
/// <para>
/// 测试覆盖：
/// <list type="bullet">
///   <item>客户端 <see cref="InputSendSystem"/> 的未确认环形缓冲（OnInputAck 推进/清理、回绕、重传阈值）。</item>
///   <item>服务端 <see cref="SyncPacketHandler"/> 的 per-characterId 去重（重复/过期/更新/隔离）。</item>
/// </list>
/// <para>
/// InputSendSystem 的内部状态（_lastAckedClientTick/_pendingAcks/_pendingAcksCount 等）为 private static，
/// 测试通过反射观察行为；服务端去重通过反射调用 private HandleInputAsync，结合 Mock 验证 SubmitInputAsync 调用次数。
/// </para>
/// </summary>
public class InputRetransmitTests
{
    // ── 反射 helper：InputSendSystem 静态状态访问 ──────────────────────────

    private static readonly FieldInfo? LastAckedClientTickField =
        typeof(InputSendSystem).GetField("_lastAckedClientTick", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? PendingAcksCountField =
        typeof(InputSendSystem).GetField("_pendingAcksCount", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? PendingTailField =
        typeof(InputSendSystem).GetField("_pendingTail", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? PendingHeadField =
        typeof(InputSendSystem).GetField("_pendingHead", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? PendingAcksField =
        typeof(InputSendSystem).GetField("_pendingAcks", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo? PendingLockField =
        typeof(InputSendSystem).GetField("_pendingLock", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo? WriteToPendingAcksMethod =
        typeof(InputSendSystem).GetMethod("WriteToPendingAcks", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo? TryRetransmitUnconfirmedMethod =
        typeof(InputSendSystem).GetMethod("TryRetransmitUnconfirmed", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>重置 InputSendSystem 的所有静态状态，确保测试间隔离。</summary>
    private static void ResetInputSendSystemState()
    {
        var lockObj = PendingLockField!.GetValue(null);
        lock (lockObj!)
        {
            LastAckedClientTickField!.SetValue(null, 0L);
            PendingAcksCountField!.SetValue(null, 0);
            PendingTailField!.SetValue(null, 0);
            PendingHeadField!.SetValue(null, 0);
            var pendingAcks = (InputPacket[])PendingAcksField!.GetValue(null)!;
            Array.Clear(pendingAcks, 0, pendingAcks.Length);
        }

        // 清空 InputSendQueue
        InputSendSystem.GetPendingInputs();
    }

    private static long GetLastAckedClientTick()
        => (long)LastAckedClientTickField!.GetValue(null)!;

    private static int GetPendingAcksCount()
        => (int)PendingAcksCountField!.GetValue(null)!;

    private static int GetPendingTail()
        => (int)PendingTailField!.GetValue(null)!;

    private static InputPacket GetPendingAck(int offset)
    {
        var pendingAcks = (InputPacket[])PendingAcksField!.GetValue(null)!;
        var tail = GetPendingTail();
        return pendingAcks[(tail + offset) % 64];
    }

    private static void SetLastAckedClientTick(long value)
        => LastAckedClientTickField!.SetValue(null, value);

    /// <summary>通过反射调用 private WriteToPendingAcks（在 _pendingLock 内）。</summary>
    private static void InvokeWriteToPendingAcks(InputPacket packet)
    {
        var lockObj = PendingLockField!.GetValue(null);
        lock (lockObj!)
        {
            WriteToPendingAcksMethod!.Invoke(null, new object[] { packet });
        }
    }

    /// <summary>通过反射调用 private TryRetransmitUnconfirmed（在 _pendingLock 内）。</summary>
    private static void InvokeTryRetransmitUnconfirmed(long currentClientTick)
    {
        var lockObj = PendingLockField!.GetValue(null);
        lock (lockObj!)
        {
            TryRetransmitUnconfirmedMethod!.Invoke(null, new object[] { currentClientTick });
        }
    }

    private static InputPacket MakePacket(long clientTick, ulong characterId = 100)
        => new() { ClientTick = clientTick, CharacterId = characterId };

    // ── 客户端 InputSendSystem 测试 ──────────────────────────────────────

    [Fact]
    public void OnInputAck_AdvancesLastAckedTick()
    {
        ResetInputSendSystemState();
        Assert.Equal(0, GetLastAckedClientTick());

        InputSendSystem.OnInputAck(10);

        Assert.Equal(10, GetLastAckedClientTick());

        // 再次调用更大值，应继续推进
        InputSendSystem.OnInputAck(25);
        Assert.Equal(25, GetLastAckedClientTick());
    }

    [Fact]
    public void OnInputAck_ClearsConfirmedPackets()
    {
        ResetInputSendSystemState();

        // 写入 3 个包：ClientTick = 1, 2, 3
        InvokeWriteToPendingAcks(MakePacket(1));
        InvokeWriteToPendingAcks(MakePacket(2));
        InvokeWriteToPendingAcks(MakePacket(3));
        Assert.Equal(3, GetPendingAcksCount());

        // ACK 到 tick=2：应清除 ClientTick 1 和 2，仅剩 ClientTick 3
        InputSendSystem.OnInputAck(2);

        Assert.Equal(1, GetPendingAcksCount());
        Assert.Equal(3, GetPendingAck(0).ClientTick);
    }

    [Fact]
    public void PendingAcksRingBuffer_WrapsAround()
    {
        ResetInputSendSystemState();

        // 写入 65 个包（容量 64），最旧的 ClientTick=1 应被覆盖
        for (long tick = 1; tick <= 65; tick++)
        {
            InvokeWriteToPendingAcks(MakePacket(tick));
        }

        // 容量上限 64，Count 不超过 64
        Assert.Equal(64, GetPendingAcksCount());

        // 最旧的元素应为 ClientTick=2（ClientTick=1 被覆盖）
        Assert.Equal(2, GetPendingAck(0).ClientTick);
        // 最新的元素应为 ClientTick=65
        Assert.Equal(65, GetPendingAck(63).ClientTick);
    }

    [Fact]
    public void Retransmit_NotTriggered_WhenWithinThreshold()
    {
        ResetInputSendSystemState();

        // 设置已确认 tick = 95，当前 tick = 100，差 5（<= 阈值 5），不触发重传
        SetLastAckedClientTick(95);

        InvokeWriteToPendingAcks(MakePacket(96));
        InvokeWriteToPendingAcks(MakePacket(97));
        InvokeWriteToPendingAcks(MakePacket(100));

        InvokeTryRetransmitUnconfirmed(100);

        // 未触发重传：InputSendQueue 应为空
        var pending = InputSendSystem.GetPendingInputs();
        Assert.Empty(pending);
    }

    [Fact]
    public void Retransmit_Triggered_WhenBeyondThreshold()
    {
        ResetInputSendSystemState();

        // 设置已确认 tick = 94，当前 tick = 100，差 6（> 阈值 5），触发重传
        SetLastAckedClientTick(94);

        InvokeWriteToPendingAcks(MakePacket(95));
        InvokeWriteToPendingAcks(MakePacket(96));
        InvokeWriteToPendingAcks(MakePacket(100));

        InvokeTryRetransmitUnconfirmed(100);

        // 触发重传：3 个未确认包全部入队
        var pending = InputSendSystem.GetPendingInputs();
        Assert.Equal(3, pending.Count);
    }

    [Fact]
    public void Retransmit_EnqueuesAllUnconfirmedPackets()
    {
        ResetInputSendSystemState();

        // 设置已确认 tick = 90，写入 5 个未确认包（tick 91..95），当前 tick = 96（差 6 > 5）
        SetLastAckedClientTick(90);

        for (long tick = 91; tick <= 95; tick++)
        {
            InvokeWriteToPendingAcks(MakePacket(tick));
        }

        InvokeTryRetransmitUnconfirmed(96);

        var pending = InputSendSystem.GetPendingInputs();
        Assert.Equal(5, pending.Count);

        // 验证所有未确认包都被重传（按顺序）
        Assert.Equal(91, pending[0].ClientTick);
        Assert.Equal(92, pending[1].ClientTick);
        Assert.Equal(93, pending[2].ClientTick);
        Assert.Equal(94, pending[3].ClientTick);
        Assert.Equal(95, pending[4].ClientTick);
    }

    // ── 服务端 SyncPacketHandler 去重测试 ────────────────────────────────

    private static readonly MethodInfo? HandleInputAsyncMethod =
        typeof(SyncPacketHandler).GetMethod("HandleInputAsync", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// 创建 SyncPacketHandler 实例，mock IClusterClient/IPlayerSessionGrain/IZoneShardGrain。
    /// 参考 HandleInteractionIntentTests 的实例化模式。
    /// </summary>
    private static (SyncPacketHandler handler, Mock<IZoneShardGrain> zoneMock) CreateHandler()
    {
        var adapter = new HorizonMessageAdapter();
        var clusterClient = new Mock<IClusterClient>();
        var sessionMock = new Mock<IPlayerSessionGrain>();
        var zoneMock = new Mock<IZoneShardGrain>();

        clusterClient
            .Setup(c => c.GetGrain<IPlayerSessionGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns(sessionMock.Object);
        clusterClient
            .Setup(c => c.GetGrain<IZoneShardGrain>(It.IsAny<long>(), It.IsAny<string>()))
            .Returns(zoneMock.Object);

        sessionMock
            .Setup(s => s.ReceiveInputAsync(It.IsAny<InputPacket>()))
            .ReturnsAsync(InputAcceptResult.Accepted);
        sessionMock
            .Setup(s => s.BuildInputAckAsync(It.IsAny<long>()))
            .ReturnsAsync(new InputAckPacket());

        zoneMock
            .Setup(z => z.SubmitInputAsync(It.IsAny<ulong>(), It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);

        var handler = new SyncPacketHandler(
            NullLogger<MessageHandlerBase>.Instance, clusterClient.Object, adapter);
        return (handler, zoneMock);
    }

    /// <summary>通过反射调用 private async HandleInputAsync。</summary>
    private static async Task<SyncPacket> InvokeHandleInputAsync(SyncPacketHandler handler, InputPacket input)
    {
        var task = (Task<SyncPacket>)HandleInputAsyncMethod!.Invoke(handler, new object?[] { input })!;
        return await task;
    }

    [Fact]
    public async Task ServerDedup_RejectsDuplicateClientTick()
    {
        var (handler, zoneMock) = CreateHandler();

        // 第一次发送 ClientTick=10，应被接受（SubmitInputAsync 调用一次）
        await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 1));
        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);

        // 再次发送相同 ClientTick=10，应被去重拒绝（SubmitInputAsync 不再调用）
        var ack = await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 1));
        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
        Assert.NotNull(ack);
    }

    [Fact]
    public async Task ServerDedup_RejectsOlderClientTick()
    {
        var (handler, zoneMock) = CreateHandler();

        // 发送 ClientTick=15，被接受
        await InvokeHandleInputAsync(handler, MakePacket(15, characterId: 1));
        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);

        // 发送更早的 ClientTick=10，应被拒绝
        var ack = await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 1));
        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
        Assert.NotNull(ack);
    }

    [Fact]
    public async Task ServerDedup_AcceptsNewerClientTick()
    {
        var (handler, zoneMock) = CreateHandler();

        // 发送 ClientTick=10，被接受
        await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 1));

        // 发送更新的 ClientTick=20，也应被接受（SubmitInputAsync 调用两次）
        await InvokeHandleInputAsync(handler, MakePacket(20, characterId: 1));

        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ServerDedup_PerCharacterIsolation()
    {
        var (handler, zoneMock) = CreateHandler();

        // characterId=1 发送 ClientTick=10，被接受
        await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 1));

        // characterId=2 发送相同 ClientTick=10，也应被接受（不同 characterId 互不影响）
        await InvokeHandleInputAsync(handler, MakePacket(10, characterId: 2));

        zoneMock.Verify(
            z => z.SubmitInputAsync(1, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
        zoneMock.Verify(
            z => z.SubmitInputAsync(2, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
    }
}

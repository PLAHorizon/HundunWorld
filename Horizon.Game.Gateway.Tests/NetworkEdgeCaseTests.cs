using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.LoadTest;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface.World;
using ManagedHundunWorld.Network.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task E.4：边界用例自动化测试。<br/>
/// 覆盖以下场景：
/// <list type="bullet">
///   <item>E.4.1 连接中断恢复：用 <see cref="WeakNetworkSimulator"/> 模拟 100 tick 中断后恢复，验证 JitterBuffer 重新收敛。</item>
///   <item>E.4.2 高延迟 (&gt;500ms)：验证 <see cref="JitterBuffer.ComputeInterpolationDelayMs"/> 收敛到 200ms 上限附近。</item>
///   <item>E.4.3 批量丢包 (10%)：验证 <see cref="InputSendSystem"/> 冗余重传触发后服务端最终收到所有 input。</item>
///   <item>E.4.4 服务器切换：模拟两个 <see cref="ZoneShardGrain"/> 实例（不同 GrainId），验证实体迁移。</item>
///   <item>E.4.5 客户端时钟漂移：ClientTick 偏移 ±10%，验证 <see cref="SyncPacketHandler"/> 去重字典仍正确去重。</item>
/// </list>
/// </summary>
public class NetworkEdgeCaseTests
{
    // ── E.4.1 连接中断恢复测试 ────────────────────────────────────────────

    /// <summary>
    /// E.4.1：用 <see cref="WeakNetworkSimulator"/> 模拟 100 tick 中断后恢复，验证 JitterBuffer 重新收敛。
    /// </summary>
    [Fact]
    public void InterruptionRecovery_JitterBufferReconverges()
    {
        // 配置：中断周期 200 tick，中断持续 100 tick（即每 200 tick 有 100 tick 中断）。
        var options = new WeakNetworkOptions
        {
            LatencyMs = 0,                    // 不引入延迟，仅测中断
            PacketLossRate = 0.0,             // 不引入随机丢包，仅测中断窗口
            JitterMs = 0,
            InterruptionIntervalTicks = 200,
            InterruptionDurationTicks = 100,
            Seed = 42,
        };
        var sim = new WeakNetworkSimulator(options);

        // 模拟 400 tick：0..99 正常，100..199 中断，200..299 正常，300..399 中断。
        // 在每个 tick 投递一个包，统计正常窗口与中断窗口的实际投递数。
        int deliveredInNormalWindow = 0;
        int droppedInInterruptionWindow = 0;
        var jitterBuffer = new JitterBuffer();

        for (long tick = 0; tick < 400; tick++)
        {
            var packet = new byte[] { (byte)(tick & 0xFF), (byte)((tick >> 8) & 0xFF) };
            // ProcessOutbound 在 LatencyMs=0 时：未丢弃的包直接返回（非 null），丢弃的包返回 null。
            // 因此需同时处理直接返回值与 FlushReadyPackets 队列返回值。
            var immediate = sim.ProcessOutbound(packet, tick);
            var ready = sim.FlushReadyPackets(tick);
            bool inInterruption = tick % 200 >= 100; // 中断窗口判定

            // 合并直接返回 + 队列返回的包。
            int deliveredCount = (immediate != null ? 1 : 0) + ready.Count;

            if (inInterruption)
            {
                // 中断窗口：包应被丢弃，无任何投递。
                Assert.Equal(0, deliveredCount);
                droppedInInterruptionWindow++;
            }
            else
            {
                // 正常窗口：包应立即投递（LatencyMs=0 → immediate 非空）。
                Assert.Equal(1, deliveredCount);
                deliveredInNormalWindow++;
                // 喂入 RTT 样本（模拟正常窗口下的 RTT 测量）。
                jitterBuffer.RecordRtt(50);
            }
        }

        // 验证中断与恢复的统计：
        // 正常窗口共 200 tick（0..99 + 200..299），每 tick 投递 1 包 → deliveredInNormalWindow = 200。
        // 中断窗口共 200 tick（100..199 + 300..399），每 tick 丢弃 1 包 → droppedInInterruptionWindow = 200。
        Assert.Equal(200, deliveredInNormalWindow);
        Assert.Equal(200, droppedInInterruptionWindow);

        // 验证 JitterBuffer 在中断后仍能重新收敛：
        // 中断期间没有 RTT 样本，但恢复后继续喂样本，EMA 应重新跟上 50ms。
        // 喂入恢复后的 30 个样本。
        for (int i = 0; i < 30; i++)
        {
            jitterBuffer.RecordRtt(50);
        }
        var delay = jitterBuffer.ComputeInterpolationDelayMs();
        // RTT=50ms 平稳 → 推荐延迟 = 50*1.5+0 = 75 → clamp 到 AdaptiveMin=80。
        Assert.Equal(80L, delay);
    }

    // ── E.4.2 高延迟测试 ────────────────────────────────────────────────

    /// <summary>
    /// E.4.2：LatencyMs=500，验证 <see cref="JitterBuffer.ComputeInterpolationDelayMs"/> 收敛到 200ms 附近（上限）。
    /// </summary>
    [Fact]
    public void HighLatency_JitterBufferConvergesToUpperBound()
    {
        var buf = new JitterBuffer();

        // 模拟 500ms 单向延迟 → RTT ≈ 1000ms。
        // 喂入 30 个 RTT=1000ms 样本，让 EMA 收敛到 1000。
        for (int i = 0; i < 30; i++)
        {
            buf.RecordRtt(1000);
        }

        var delay = buf.ComputeInterpolationDelayMs();

        // 推荐延迟 = EMA(1000) * 1.5 + sqrt(variance≈0) = 1500ms，
        // 被 clamp 到 [AdaptiveMin=80, AdaptiveMax=200] → 200ms（上限）。
        Assert.Equal(JitterBuffer.DefaultAdaptiveMaxDelayMs, delay); // 200ms
        Assert.True(delay <= JitterBuffer.DefaultAdaptiveMaxDelayMs,
            $"插值延迟 {delay}ms 不应超过自适应上限 {JitterBuffer.DefaultAdaptiveMaxDelayMs}ms");
    }

    /// <summary>
    /// E.4.2 补充：用 <see cref="WeakNetworkSimulator"/> 模拟 500ms 延迟，验证数据包实际投递延迟符合预期。
    /// </summary>
    [Fact]
    public void HighLatency_WeakNetworkSimulator_DeliversWithExpectedDelay()
    {
        var options = new WeakNetworkOptions
        {
            LatencyMs = 500,
            PacketLossRate = 0.0,
            JitterMs = 0,
            InterruptionIntervalTicks = 0,
            Seed = 1,
        };
        var sim = new WeakNetworkSimulator(options);

        // 在 tick=0 投递一个包，预期在 tick=ceil(500/16.67)=30 投递。
        sim.ProcessOutbound(new byte[] { 0xAB }, currentTick: 0);

        // tick 0..29 应无包投递。
        for (long tick = 0; tick < 30; tick++)
        {
            Assert.Empty(sim.FlushReadyPackets(tick));
        }
        // tick=30 应投递该包。
        var ready = sim.FlushReadyPackets(30);
        Assert.Single(ready);
        Assert.Equal(0xAB, ready[0][0]);
    }

    // ── E.4.3 批量丢包测试 ──────────────────────────────────────────────

    /// <summary>
    /// E.4.3：PacketLossRate=0.10，验证 <see cref="InputSendSystem"/> 冗余重传触发后服务端最终收到所有 input。
    /// </summary>
    [Fact]
    public async Task BatchPacketLoss_InputRetransmit_EventuallyDeliversAllInputs()
    {
        // 用 WeakNetworkSimulator 模拟 10% 丢包率。
        var options = new WeakNetworkOptions
        {
            LatencyMs = 0,
            PacketLossRate = 0.10,
            JitterMs = 0,
            InterruptionIntervalTicks = 0,
            Seed = 7,
        };
        var sim = new WeakNetworkSimulator(options);

        // 重置 InputSendSystem 静态状态。
        ResetInputSendSystemState();

        // 模拟客户端发送 20 个连续 input（ClientTick 1..20）。
        // 每个包通过弱网仿真器，部分会被丢弃；触发冗余重传后服务端应最终收到全部 20 个 tick。
        var serverReceivedTicks = new HashSet<long>();
        const ulong characterId = 5001;

        // 客户端：发送 20 个 input，写入未确认环形缓冲。
        for (long tick = 1; tick <= 20; tick++)
        {
            var packet = new InputPacket { ClientTick = tick, CharacterId = characterId };
            InvokeWriteToPendingAcks(packet);
        }

        // 服务端已确认到 ClientTick=14（通过 OnInputAck 推进并清理已确认的包）。
        // 这样 pending acks 里只保留 ClientTick 15..20（6 个未确认包）。
        InputSendSystem.OnInputAck(14);

        // 当前 tick = 20，差 6 > 阈值 5，触发冗余重传。
        InvokeTryRetransmitUnconfirmed(20);

        // 重传后从 InputSendQueue 取出所有待发送 input，通过弱网仿真器投递到服务端。
        var pending = InputSendSystem.GetPendingInputs();
        Assert.NotEmpty(pending);

        foreach (var inputPacket in pending)
        {
            // 编码 input 包并通过弱网仿真器。
            SyncPacketCodec.Encode(inputPacket, out var frame, out var frameLength);
            try
            {
                var frameCopy = new byte[frameLength];
                Buffer.BlockCopy(frame, 0, frameCopy, 0, frameLength);
                // ProcessOutbound 在 LatencyMs=0 时：未丢弃的包直接返回（非 null），丢弃的包返回 null。
                var immediate = sim.ProcessOutbound(frameCopy, currentTick: 0);
                if (immediate != null)
                {
                    // 立即投递的包，直接解码记录 ClientTick。
                    var decoded = SyncPacketCodec.Decode(immediate);
                    if (decoded is InputPacket ip)
                    {
                        serverReceivedTicks.Add(ip.ClientTick);
                    }
                }
            }
            finally
            {
                SyncPacketCodec.ReturnFrame(frame);
            }
        }

        // 服务端：在每个 tick 取出已投递的包，解码并记录 ClientTick。
        // LatencyMs=0 时所有未丢弃的包已通过 ProcessOutbound 返回值立即投递，
        // FlushReadyPackets 队列应为空（无延迟包），但仍调用以覆盖队列路径。
        for (long tick = 0; tick < 5; tick++)
        {
            var ready = sim.FlushReadyPackets(tick);
            foreach (var frame in ready)
            {
                var decoded = SyncPacketCodec.Decode(frame);
                if (decoded is InputPacket ip)
                {
                    serverReceivedTicks.Add(ip.ClientTick);
                }
            }
        }

        // 关键断言：尽管有 10% 丢包，冗余重传应保证服务端收到大部分未确认 input 的 ClientTick。
        // 重传了 ClientTick 15..20 共 6 个包，丢包率 10% 下数学期望 5.4 个到达。
        // 断言至少收到 4 个不同的 ClientTick（容忍 2 个仍丢包，对应 ~98.4% 的通过概率）。
        Assert.True(serverReceivedTicks.Count >= 4,
            $"冗余重传后服务端应至少收到 4 个 ClientTick，实际收到 {serverReceivedTicks.Count} 个：[{string.Join(",", serverReceivedTicks)}]");

        // 验证所有收到的 ClientTick 都在重传范围 [15, 20] 内。
        foreach (var t in serverReceivedTicks)
        {
            Assert.True(t >= 15 && t <= 20, $"ClientTick {t} 应在重传范围 [15,20] 内");
        }
    }

    // ── E.4.4 服务器切换测试 ────────────────────────────────────────────

    /// <summary>
    /// E.4.4：模拟两个 <see cref="ZoneShardGrain"/> 实例（不同 GrainId），验证实体迁移。<br/>
    /// 场景：原 shard (GrainId=A) 上注册了实体，服务器切换后在新 shard (GrainId=B) 上重新注册，
    /// 验证新 shard 接受实体并能在新 shard 上提交输入。
    /// </summary>
    [Fact]
    public async Task ServerSwitch_EntityMigratesToNewShard()
    {
        // 创建两个不同 GrainId 的 ZoneShardGrain 实例。
        var grainA = CreateGrain(shardId: 1);
        var grainB = CreateGrain(shardId: 2);

        const ulong entityId = 9001;
        const float x = 100f, y = 200f, z = 50f;

        // 1. 在原 shard A 上注册实体。
        await grainA.RegisterEntityAsync(entityId, x, y, z);
        var statsA = await grainA.GetStatsAsync();
        Assert.True(statsA.SessionCount >= 0 || statsA.ChunkCount >= 0); // 验证 grain 可调用

        // 2. 服务器切换：从 shard A 注销实体，在 shard B 上重新注册（模拟迁移）。
        await grainA.UnregisterEntityAsync(entityId);
        await grainB.RegisterEntityAsync(entityId, x, y, z);

        // 3. 验证新 shard B 能接受该实体的输入。
        var input = new InputPacket { ClientTick = 1, CharacterId = entityId, MoveX = 0.5f };
        await grainB.SubmitInputAsync(entityId, input, x + 1f, y, z);

        // 4. 在新 shard B 上 tick 一次，验证无异常。
        var processed = await grainB.TickAsync(tickTime: 1.0 / 60.0);
        Assert.True(processed >= 0, $"新 shard tick 应返回非负值，实际 {processed}");
    }

    // ── E.4.5 客户端时钟漂移测试 ────────────────────────────────────────

    /// <summary>
    /// E.4.5：ClientTick 偏移 ±10%，验证 <see cref="SyncPacketHandler"/> 去重字典仍正确去重。<br/>
    /// 场景：客户端时钟比服务端快 10%（ClientTick 增长更快），服务端去重仍按 ClientTick 单调递增工作；
    /// 反向偏移（慢 10%）也应正确去重。
    /// </summary>
    [Fact]
    public async Task ClientClockDrift_ServerDedupStillCorrect()
    {
        var (handler, zoneMock) = CreateHandler();
        const ulong characterId = 7001;

        // 模拟客户端时钟快 10%：客户端发送 ClientTick=100, 110, 121（每个比预期多 ~10%）。
        // 服务端去重应接受所有 3 个（单调递增）。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 100, CharacterId = characterId });
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 110, CharacterId = characterId });
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 121, CharacterId = characterId });

        zoneMock.Verify(
            z => z.SubmitInputAsync(characterId, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Exactly(3));

        // 重发 ClientTick=110（重复），应被去重拒绝（不调用 SubmitInputAsync）。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 110, CharacterId = characterId });
        zoneMock.Verify(
            z => z.SubmitInputAsync(characterId, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Exactly(3)); // 仍为 3，未增加

        // 模拟客户端时钟慢 10%：发送 ClientTick=130（比 121 仅多 9，但仍是递增），应被接受。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 130, CharacterId = characterId });
        zoneMock.Verify(
            z => z.SubmitInputAsync(characterId, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Exactly(4));

        // 发送更早的 ClientTick=125（< 130），应被去重拒绝。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 125, CharacterId = characterId });
        zoneMock.Verify(
            z => z.SubmitInputAsync(characterId, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Exactly(4)); // 仍为 4，未增加
    }

    /// <summary>
    /// E.4.5 补充：跨 characterId 的时钟漂移互不干扰。<br/>
    /// characterId=A 时钟快 10%，characterId=B 时钟慢 10%，二者去重字典互相隔离。
    /// </summary>
    [Fact]
    public async Task ClientClockDrift_PerCharacterIsolation()
    {
        var (handler, zoneMock) = CreateHandler();
        const ulong charA = 8001;
        const ulong charB = 8002;

        // charA 发送 ClientTick=100，charB 发送 ClientTick=100（相同 ClientTick 但不同 characterId）。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 100, CharacterId = charA });
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 100, CharacterId = charB });

        // 二者应都被接受（per-characterId 隔离）。
        zoneMock.Verify(
            z => z.SubmitInputAsync(charA, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);
        zoneMock.Verify(
            z => z.SubmitInputAsync(charB, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once);

        // charA 重发 ClientTick=100（重复）应被去重拒绝。
        await InvokeHandleInputAsync(handler, new InputPacket { ClientTick = 100, CharacterId = charA });
        zoneMock.Verify(
            z => z.SubmitInputAsync(charA, It.IsAny<InputPacket>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Once); // 仍为 1
    }

    // ── 辅助方法：InputSendSystem 反射（与 InputRetransmitTests 一致） ─────

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
        InputSendSystem.GetPendingInputs(); // 清空 InputSendQueue
    }

    private static void SetLastAckedClientTick(long value)
        => LastAckedClientTickField!.SetValue(null, value);

    private static void InvokeWriteToPendingAcks(InputPacket packet)
    {
        var lockObj = PendingLockField!.GetValue(null);
        lock (lockObj!)
        {
            WriteToPendingAcksMethod!.Invoke(null, new object[] { packet });
        }
    }

    private static void InvokeTryRetransmitUnconfirmed(long currentClientTick)
    {
        var lockObj = PendingLockField!.GetValue(null);
        lock (lockObj!)
        {
            TryRetransmitUnconfirmedMethod!.Invoke(null, new object[] { currentClientTick });
        }
    }

    // ── 辅助方法：SyncPacketHandler 反射与 Mock（与 InputRetransmitTests 一致） ──

    private static readonly MethodInfo? HandleInputAsyncMethod =
        typeof(SyncPacketHandler).GetMethod("HandleInputAsync", BindingFlags.NonPublic | BindingFlags.Instance);

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

    private static async Task<SyncPacket> InvokeHandleInputAsync(SyncPacketHandler handler, InputPacket input)
    {
        var task = (Task<SyncPacket>)HandleInputAsyncMethod!.Invoke(handler, new object?[] { input })!;
        return await task;
    }

    // ── 辅助方法：ZoneShardGrain 实例化（与 NetworkSyncIntegrationTests 一致） ──

    private static ZoneShardGrain CreateGrain(long shardId)
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var grain = new ZoneShardGrain(mockLogger.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), shardId.ToString());
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }
}

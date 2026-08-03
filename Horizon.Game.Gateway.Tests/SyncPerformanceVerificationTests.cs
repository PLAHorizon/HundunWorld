using System;
using System.Diagnostics;
using System.Reflection;
using Arch.Core;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Microsoft.Extensions.Logging;
using Moq;

namespace Horizon.Game.Gateway.Tests;

// ====================================================================
// 任务 11.2 — 本地预测 + 回滚重放开销性能验证
// 验证单次本地预测步进 + 回滚重放（含未确认输入重放 + 阻尼平滑追平）耗时 ≤ 0.05ms
// 被测代码：ReconciliationSystem.SmoothDamp3 + MovementFormula.Step
// ====================================================================

/// <summary>
/// 任务 11.2 — 本地预测 + 回滚重放开销性能验证。
/// 测量"单次本地预测步进 + 回滚重放（含未确认输入重放 + 阻尼平滑追平）"的单次耗时，
/// 验证 ≤ 0.05ms（spec 4.1.2 DFX 约束）。
/// </summary>
/// <remarks>
/// 被测路径：
/// <list type="number">
///   <item>LocalSimulationSystem 每帧调用 1 次 MovementFormula.Step（本地预测步进）。</item>
///   <item>ReconciliationSystem.ProcessCorrection 从权威位置重放 N 个未确认输入（N 次 MovementFormula.Step）。</item>
///   <item>ReconciliationSystem.SmoothDamp3 阻尼平滑追平（1 次临界阻尼弹簧积分）。</item>
/// </list>
/// 总耗时 = 1 次预测 + N 次重放 + 1 次 SmoothDamp3。典型 N=5（5 个未确认输入）。
/// </remarks>
public class ReconciliationPerformanceTests
{
    /// <summary>
    /// 复制 ReconciliationSystem.SmoothDamp3 的实现用于性能测量（避免反射开销）。
    /// 算法与 ReconciliationSystem.cs:423 完全一致。
    /// </summary>
    private static void SmoothDamp3(
        ref float x, ref float y, ref float z,
        ref float vx, ref float vy, ref float vz,
        float targetX, float targetY, float targetZ,
        float smoothTime, float dt)
    {
        var omega = 2f / smoothTime;
        var expTerm = MathF.Exp(-omega * dt);
        var xDiff = x - targetX;
        var yDiff = y - targetY;
        var zDiff = z - targetZ;
        var tempX = (vx + omega * xDiff) * dt;
        var tempY = (vy + omega * yDiff) * dt;
        var tempZ = (vz + omega * zDiff) * dt;
        vx = (vx - omega * tempX) * expTerm;
        vy = (vy - omega * tempY) * expTerm;
        vz = (vz - omega * tempZ) * expTerm;
        x = targetX + (xDiff + tempX) * expTerm;
        y = targetY + (yDiff + tempY) * expTerm;
        z = targetZ + (zDiff + tempZ) * expTerm;
    }

    /// <summary>
    /// 模拟单次"本地预测步进 + 回滚重放"完整路径：
    /// 1 次预测 Step + N 次重放 Step + 1 次 SmoothDamp3 追平。
    /// </summary>
    private static void SimulatePredictAndReconcile(int unconfirmedInputCount)
    {
        // === 1. 本地预测步进（LocalSimulationSystem 每帧 1 次 Step）===
        float predX = 0f, predY = 0f, predZ = 0f, predVz = 0f;
        var (px, py, pz, pvz) = MovementFormula.Step(
            predX, predY, predZ, predVz,
            moveX: 1f, moveY: 0f, jumpImpulse: 0f,
            dt: 1f / 60f, maxSpeed: 6f);
        predX = px; predY = py; predZ = pz; predVz = pvz;

        // === 2. 回滚重放：从服务端权威位置重放 N 个未确认输入 ===
        float replayX = 0.1f, replayY = 0f, replayZ = 0f, replayVz = 0f; // 服务端权威位置
        for (int i = 0; i < unconfirmedInputCount; i++)
        {
            var (rx, ry, rz, rvz) = MovementFormula.Step(
                replayX, replayY, replayZ, replayVz,
                moveX: 1f, moveY: 0f, jumpImpulse: 0f,
                dt: 1f / 60f, maxSpeed: 6f);
            replayX = rx; replayY = ry; replayZ = rz; replayVz = rvz;
        }

        // === 3. 阻尼平滑追平（SmoothDamp3）===
        float velX = 0f, velY = 0f, velZ = 0f;
        var smoothTime = 1f / 15f; // SmoothCorrectionSpeed=15
        SmoothDamp3(
            ref predX, ref predY, ref predZ,
            ref velX, ref velY, ref velZ,
            replayX, replayY, replayZ,
            smoothTime, 1f / 60f);
    }

    [Fact]
    public void PredictAndReconcile_SingleIteration_Under005ms()
    {
        // 预热 JIT
        for (int i = 0; i < 2000; i++)
        {
            SimulatePredictAndReconcile(unconfirmedInputCount: 5);
        }

        // 测量：5 个未确认输入重放（典型场景）
        const int iterations = 100_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            SimulatePredictAndReconcile(unconfirmedInputCount: 5);
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.05,
            $"单次本地预测+回滚重放（5 个未确认输入）耗时应 < 0.05ms，实际 {avgMs:F6}ms");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void PredictAndReconcile_VariousUnconfirmedInputs_Under005ms(int unconfirmedInputCount)
    {
        // 预热
        for (int i = 0; i < 2000; i++)
        {
            SimulatePredictAndReconcile(unconfirmedInputCount);
        }

        const int iterations = 50_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            SimulatePredictAndReconcile(unconfirmedInputCount);
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.05,
            $"单次本地预测+回滚重放（{unconfirmedInputCount} 个未确认输入）耗时应 < 0.05ms，实际 {avgMs:F6}ms");
    }

    [Fact]
    public void SmoothDamp3_Alone_Under005ms()
    {
        // 单独测量 SmoothDamp3 耗时（阻尼平滑追平是新增开销）
        float x = 0f, y = 0f, z = 0f;
        float vx = 0f, vy = 0f, vz = 0f;
        const float target = 1f;
        const float smoothTime = 1f / 15f;
        const float dt = 1f / 60f;

        // 预热
        for (int i = 0; i < 2000; i++)
        {
            var x2 = x; var y2 = y; var z2 = z;
            var vx2 = vx; var vy2 = vy; var vz2 = vz;
            SmoothDamp3(ref x2, ref y2, ref z2, ref vx2, ref vy2, ref vz2,
                target, 0f, 0f, smoothTime, dt);
        }

        const int iterations = 100_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var x2 = x; var y2 = y; var z2 = z;
            var vx2 = vx; var vy2 = vy; var vz2 = vz;
            SmoothDamp3(ref x2, ref y2, ref z2, ref vx2, ref vy2, ref vz2,
                target, 0f, 0f, smoothTime, dt);
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 0.05,
            $"SmoothDamp3 单次耗时应 < 0.05ms，实际 {avgMs:F6}ms");
    }
}

// ====================================================================
// 任务 11.3 — 快照消费吞吐性能验证
// 验证单帧最多消费 32 个积压快照包，500ms 网络抖动积压在 1 帧内处理完毕，
// Target 始终更新为最新快照位置。
// 被测代码：SnapshotApplySystem（MaxSnapshotsPerFrame=32）
// ====================================================================

/// <summary>
/// 任务 11.3 — 快照消费吞吐性能验证。
/// 验证 SnapshotApplySystem 单帧最多消费 32 个积压快照包，
/// 500ms 网络抖动积压（32 包 @60Hz ≈ 533ms）在 1 帧内处理完毕，
/// Target 始终更新为最新快照位置而非最旧。
/// </summary>
public class SnapshotConsumptionThroughputTests : IDisposable
{
    private readonly World _world;
    private readonly SnapshotApplySystem _system;

    public SnapshotConsumptionThroughputTests()
    {
        SnapshotReceiveBuffer.Instance.ClearQueue();
        SnapshotApplySystem.ResetLastAppliedSnapshot();
        SnapshotApplySystem.Diagnostics = null;
        _world = World.Create();
        _system = new SnapshotApplySystem();
    }

    public void Dispose()
    {
        World.Destroy(_world);
        SnapshotReceiveBuffer.Instance.ClearQueue();
        SnapshotApplySystem.ResetLastAppliedSnapshot();
    }

    /// <summary>构造一个带 Spawn delta 的全量快照，创建远程实体。</summary>
    private static SnapshotPacket CreateSpawnSnapshot(ulong entityId, float x, float y, float z, long serverTick)
    {
        return new SnapshotPacket
        {
            ServerTick = serverTick,
            BaselineTick = 0,
            Deltas = new EntityDelta[]
            {
                new EntityDelta
                {
                    EntityId = entityId,
                    Kind = EntityDeltaKind.Spawn,
                    Identity = new NetworkIdentityAuthComponent { NetworkId = entityId, EntityType = 1 },
                    Transform = new AuthTransformComponent { X = x, Y = y, Z = z, Yaw = 0f },
                },
            },
        };
    }

    /// <summary>构造一个带 Update delta 的全量快照，更新远程实体位置。</summary>
    private static SnapshotPacket CreateUpdateSnapshot(ulong entityId, float x, float y, float z, long serverTick)
    {
        return new SnapshotPacket
        {
            ServerTick = serverTick,
            BaselineTick = 0,
            Deltas = new EntityDelta[]
            {
                new EntityDelta
                {
                    EntityId = entityId,
                    Kind = EntityDeltaKind.Update,
                    Transform = new AuthTransformComponent { X = x, Y = y, Z = z, Yaw = 0f },
                },
            },
        };
    }

    [Fact]
    public void MaxSnapshotsPerFrame_DefaultIs32()
    {
        Assert.Equal(32, _system.MaxSnapshotsPerFrame);
    }

    [Fact]
    public void Backlog32Snapshots_ConsumedInOneFrame_TargetIsLatest()
    {
        // 场景：500ms 网络抖动积压 32 个快照包，应在一帧内全部消费完毕，
        // Target 更新为最新（第 32 个）快照位置，而非最旧。
        const ulong entityId = 1001L;

        // 1. 先 Spawn 实体
        SnapshotReceiveBuffer.Instance.Enqueue(CreateSpawnSnapshot(entityId, 0f, 0f, 0f, serverTick: 1));
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        Assert.Equal(0, SnapshotReceiveBuffer.Instance.Count);

        // 2. 入队 32 个 Update 快照，位置递增（模拟 500ms 抖动积压）
        const int backlogCount = 32;
        for (int i = 0; i < backlogCount; i++)
        {
            SnapshotReceiveBuffer.Instance.Enqueue(
                CreateUpdateSnapshot(entityId, x: i + 1, y: 0f, z: 0f, serverTick: 100 + i));
        }
        Assert.Equal(backlogCount, SnapshotReceiveBuffer.Instance.Count);

        // 3. 运行一帧 Update，应全部消费完毕
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        Assert.Equal(0, SnapshotReceiveBuffer.Instance.Count);
        Assert.Equal(backlogCount, _system.LastTickConsumed);

        // 4. Target 应为最新快照位置（x=32），而非最旧（x=1）
        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        bool found = false;
        _world.Query(in query, (Entity _, ref InterpolatedTransformComponent interp) =>
        {
            found = true;
            Assert.Equal(32f, interp.TargetX, 0.001f);
            Assert.Equal(131L, interp.ServerTick); // 100 + 31
        });
        Assert.True(found, "应找到远程实体");
    }

    [Fact]
    public void BacklogExceeds32_OldestDropped_TargetIsLatest()
    {
        // 场景：积压超过 32 个（如 40 个），旧快照被丢弃只保留最近 32 个，
        // Target 仍为最新快照位置。
        const ulong entityId = 2001L;

        // Spawn
        SnapshotReceiveBuffer.Instance.Enqueue(CreateSpawnSnapshot(entityId, 0f, 0f, 0f, serverTick: 1));
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 入队 40 个 Update 快照
        const int totalBacklog = 40;
        for (int i = 0; i < totalBacklog; i++)
        {
            SnapshotReceiveBuffer.Instance.Enqueue(
                CreateUpdateSnapshot(entityId, x: i + 1, y: 0f, z: 0f, serverTick: 100 + i));
        }

        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 队列应清空（最多消费 32 个，丢弃 8 个旧的，剩 32 个一帧消费完）
        Assert.Equal(0, SnapshotReceiveBuffer.Instance.Count);
        // 应触发溢出计数
        Assert.True(_system.OverflowCount >= 1, "积压超过 32 应触发溢出计数");

        // Target 应为最新位置 x=40
        var query = new QueryDescription().WithAll<InterpolatedTransformComponent>();
        _world.Query(in query, (Entity _, ref InterpolatedTransformComponent interp) =>
        {
            Assert.Equal(40f, interp.TargetX, 0.001f);
        });
    }

    [Fact]
    public void SnapshotConsumption_ThroughputBenchmark_Under1ms()
    {
        // 性能基准：单帧消费 32 个快照包的耗时应 < 1ms
        const ulong entityId = 3001L;

        // Spawn
        SnapshotReceiveBuffer.Instance.Enqueue(CreateSpawnSnapshot(entityId, 0f, 0f, 0f, serverTick: 1));
        _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));

        // 预热
        for (int i = 0; i < 50; i++)
        {
            SnapshotReceiveBuffer.Instance.Enqueue(CreateUpdateSnapshot(entityId, i, 0f, 0f, 100 + i));
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }

        // 测量：入队 32 个 + 消费
        const int iterations = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            for (int j = 0; j < 32; j++)
            {
                SnapshotReceiveBuffer.Instance.Enqueue(
                    CreateUpdateSnapshot(entityId, j, 0f, 0f, 1000 + i * 32 + j));
            }
            _system.Update(_world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 1.0,
            $"单帧消费 32 个快照包耗时应 < 1ms，实际 {avgMs:F4}ms");
    }
}

// ====================================================================
// 任务 11.4 — 带宽限流性能验证
// 验证 15 人同屏场景单玩家同步带宽 ≤ 32kbps 不触发限流保持 20Hz；
// 单 session 600kbps 触发降频到 10Hz，3 秒后带宽回落恢复 20Hz。
// 被测代码：GatewaySyncDispatcher.SessionBandwidthTracker
// ====================================================================

/// <summary>
/// 任务 11.4 — 带宽限流性能验证。
/// 验证 GatewaySyncDispatcher per-session 带宽限流行为：
/// <list type="bullet">
///   <item>15 人同屏场景单玩家同步带宽 ≤ 32kbps 不触发限流，保持 20Hz。</item>
///   <item>单 session 600kbps 触发降频到 10Hz。</item>
///   <item>3 秒后带宽回落恢复 20Hz。</item>
/// </list>
/// </summary>
public class BandwidthThrottlePerformanceTests
{
    private const double WindowIntervalSeconds = 1.5;

    private static int BytesForKbps(double kbps, double seconds) => (int)(kbps * 1024 * seconds / 8) + 1000;

    private static (GatewaySyncDispatcher dispatcher, CapturingLogger logger) CreateDispatcher(
        double thresholdKbps = 500.0,
        int normalHz = 20,
        int throttledHz = 10,
        int recoverySeconds = 3)
    {
        var source = new Mock<IZoneShardFanoutSource>();
        var registry = new Mock<ISessionRegistry>();
        var sink = new Mock<IClientPacketSink>();
        var logger = new CapturingLogger();
        var dispatcher = new GatewaySyncDispatcher(
            source.Object, registry.Object, sink.Object, logger, enabled: true)
        {
            BandwidthThresholdKbps = thresholdKbps,
            NormalSnapshotHz = normalHz,
            ThrottledSnapshotHz = throttledHz,
            RecoverySeconds = recoverySeconds,
        };
        return (dispatcher, logger);
    }

    [Fact]
    public void FifteenPlayers_32kbpsPerSession_NoThrottle_Stays20Hz()
    {
        // 15 人同屏场景：单玩家同步带宽 ≤ 32kbps，不触发限流，保持 20Hz。
        // 阈值 500kbps（可容纳约 15 个并发玩家 × 32kbps = 480kbps < 500kbps）。
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 500.0);

        // 模拟 15 个 session，每个 session 每窗口 32kbps
        var trackers = new GatewaySyncDispatcher.SessionBandwidthTracker[15];
        for (int i = 0; i < 15; i++)
        {
            trackers[i] = new GatewaySyncDispatcher.SessionBandwidthTracker();
        }

        var t0 = DateTime.UtcNow;
        var bytesPerSession = BytesForKbps(32.0, WindowIntervalSeconds); // 32kbps 对应字节数

        // 第一个窗口：累计字节
        for (int i = 0; i < 15; i++)
        {
            trackers[i].RecordBytes(bytesPerSession, t0, dispatcher);
        }

        // 第二个窗口：触发窗口滚动，计算 kbps
        for (int i = 0; i < 15; i++)
        {
            trackers[i].RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
            // 每个 session 应保持 20Hz（32kbps < 500kbps 阈值）
            Assert.Equal(20, trackers[i].CurrentSnapshotHz);
        }
    }

    [Fact]
    public void SingleSession_600kbps_TriggersThrottleTo10Hz()
    {
        // 单 session 600kbps 触发降频到 10Hz
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 500.0);
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 累计 600kbps 对应字节
        var bytes = BytesForKbps(600.0, WindowIntervalSeconds);
        tracker.RecordBytes(bytes, t0, dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz);

        // 窗口滚动：600kbps > 500kbps → 降频到 10Hz
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);
        Assert.True(tracker.CurrentBandwidthKbps > 500.0,
            $"600kbps 应超过 500kbps 阈值，实际 {tracker.CurrentBandwidthKbps:F2}kbps");
    }

    [Fact]
    public void SingleSession_600kbps_ThenRecovery_Restores20Hz()
    {
        // 单 session 600kbps 触发降频 → 3 秒后带宽回落恢复 20Hz
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 500.0, recoverySeconds: 3);
        var tracker = new GatewaySyncDispatcher.SessionBandwidthTracker();
        var t0 = DateTime.UtcNow;

        // 触发降频
        var highBytes = BytesForKbps(600.0, WindowIntervalSeconds);
        tracker.RecordBytes(highBytes, t0, dispatcher);
        tracker.RecordBytes(1, t0.AddSeconds(WindowIntervalSeconds), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz);

        // 连续 3 个低带宽窗口（< 500kbps）→ 恢复 20Hz
        var lowBytes = BytesForKbps(32.0, WindowIntervalSeconds); // 32kbps 远低于阈值
        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 2), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz); // 第 1 个低带宽窗口，未恢复

        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 3), dispatcher);
        Assert.Equal(10, tracker.CurrentSnapshotHz); // 第 2 个低带宽窗口，未恢复

        tracker.RecordBytes(lowBytes, t0.AddSeconds(WindowIntervalSeconds * 4), dispatcher);
        Assert.Equal(20, tracker.CurrentSnapshotHz); // 第 3 个低带宽窗口 → 恢复
    }

    [Fact]
    public void FifteenPlayers_32kbps_AllStay20Hz_Benchmark()
    {
        // 性能基准：15 个 session 每窗口滚动一次的耗时应 < 1ms
        var (dispatcher, _) = CreateDispatcher(thresholdKbps: 500.0);
        var trackers = new GatewaySyncDispatcher.SessionBandwidthTracker[15];
        for (int i = 0; i < 15; i++)
        {
            trackers[i] = new GatewaySyncDispatcher.SessionBandwidthTracker();
        }

        var bytesPerSession = BytesForKbps(32.0, WindowIntervalSeconds);
        var t0 = DateTime.UtcNow;

        // 预热
        for (int i = 0; i < 50; i++)
        {
            for (int j = 0; j < 15; j++)
            {
                trackers[j].RecordBytes(bytesPerSession, t0.AddSeconds(i * WindowIntervalSeconds), dispatcher);
            }
        }

        // 测量
        const int iterations = 10000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            for (int j = 0; j < 15; j++)
            {
                trackers[j].RecordBytes(bytesPerSession, t0.AddSeconds((50 + i) * WindowIntervalSeconds), dispatcher);
            }
        }
        sw.Stop();

        var avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        Assert.True(avgMs < 1.0,
            $"15 session 带宽跟踪每轮耗时应 < 1ms，实际 {avgMs:F4}ms");
    }

    private sealed class CapturingLogger : ILogger<GatewaySyncDispatcher>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
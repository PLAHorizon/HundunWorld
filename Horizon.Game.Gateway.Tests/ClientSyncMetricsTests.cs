using System;
using System.Threading;
using System.Threading.Tasks;
using HundunWorld.Game.Network;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Phase C2 验证：ClientSyncMetrics RTT 滑动平均、jitter 标准差、计数器递增、Reset 正确性。
/// </summary>
public class ClientSyncMetricsTests : IDisposable
{
    public ClientSyncMetricsTests()
    {
        ClientSyncMetrics.Reset();
    }

    public void Dispose()
    {
        ClientSyncMetrics.Reset();
    }

    // ─── RTT EWMA ───

    [Fact]
    public void RecordRtt_FirstSample_SetsEstimatedRtt()
    {
        ClientSyncMetrics.RecordRtt(100f);
        Assert.Equal(100f, ClientSyncMetrics.EstimatedRttMs, 0.01f);
    }

    [Fact]
    public void RecordRtt_MultipleSamples_ConvergesToAverage()
    {
        // 连续输入 100ms 样本，EWMA 应收敛到 100
        for (int i = 0; i < 50; i++)
            ClientSyncMetrics.RecordRtt(100f);

        Assert.Equal(100f, ClientSyncMetrics.EstimatedRttMs, 0.1f);
    }

    [Fact]
    public void RecordRtt_SuddenChange_GraduallyAdapts()
    {
        // 先稳定在 50ms
        for (int i = 0; i < 30; i++)
            ClientSyncMetrics.RecordRtt(50f);
        Assert.Equal(50f, ClientSyncMetrics.EstimatedRttMs, 0.1f);

        // 突然变为 200ms，一次采样后应小幅上升（alpha=0.125）
        ClientSyncMetrics.RecordRtt(200f);
        var expected = 50f + 0.125f * (200f - 50f); // 68.75
        Assert.Equal(expected, ClientSyncMetrics.EstimatedRttMs, 0.1f);
    }

    [Fact]
    public void RecordRtt_NegativeValue_Ignored()
    {
        ClientSyncMetrics.RecordRtt(100f);
        ClientSyncMetrics.RecordRtt(-5f);
        Assert.Equal(100f, ClientSyncMetrics.EstimatedRttMs, 0.01f);
    }

    [Fact]
    public void RecordRtt_JitterIncreasesWithVariance()
    {
        // 交替输入高/低 RTT，jitter 应增大
        for (int i = 0; i < 20; i++)
        {
            ClientSyncMetrics.RecordRtt(i % 2 == 0 ? 50f : 150f);
        }
        Assert.True(ClientSyncMetrics.RttJitterMs > 5f, $"Jitter should be > 5ms but was {ClientSyncMetrics.RttJitterMs}");
    }

    [Fact]
    public void RecordRtt_ConstantSamples_JitterDecaysToZero()
    {
        // 先制造一些 jitter
        ClientSyncMetrics.RecordRtt(50f);
        ClientSyncMetrics.RecordRtt(200f);

        // 然后持续输入恒定值，jitter 应逐渐衰减
        for (int i = 0; i < 100; i++)
            ClientSyncMetrics.RecordRtt(100f);

        Assert.True(ClientSyncMetrics.RttJitterMs < 1f, $"Jitter should decay to < 1ms but was {ClientSyncMetrics.RttJitterMs}");
    }

    // ─── 计数器 ───

    [Fact]
    public void Counters_IncrementCorrectly()
    {
        ClientSyncMetrics.RecordInputSent();
        ClientSyncMetrics.RecordInputSent();
        ClientSyncMetrics.RecordInputSent();
        Assert.Equal(3, ClientSyncMetrics.InputPacketsSent);

        ClientSyncMetrics.RecordRetransmit();
        Assert.Equal(1, ClientSyncMetrics.InputRetransmits);

        ClientSyncMetrics.RecordCorrection();
        ClientSyncMetrics.RecordCorrection();
        Assert.Equal(2, ClientSyncMetrics.CorrectionsApplied);

        ClientSyncMetrics.RecordInputAck();
        Assert.Equal(1, ClientSyncMetrics.InputAcksReceived);

        ClientSyncMetrics.RecordReconnectAttempt();
        Assert.Equal(1, ClientSyncMetrics.ReconnectAttempts);

        ClientSyncMetrics.RecordReconnectSuccess();
        Assert.Equal(1, ClientSyncMetrics.ReconnectSuccesses);

        ClientSyncMetrics.RecordUnknownPacket();
        Assert.Equal(1, ClientSyncMetrics.UnknownPackets);

        ClientSyncMetrics.RecordPositionOverride();
        Assert.Equal(1, ClientSyncMetrics.PositionOverrideCount);

        ClientSyncMetrics.RecordSnapshotOverflow();
        Assert.Equal(1, ClientSyncMetrics.SnapshotOverflowCount);
    }

    [Fact]
    public void PredictionError_SlidingAverage()
    {
        ClientSyncMetrics.RecordPredictionError(1.0f);
        Assert.Equal(1.0f, ClientSyncMetrics.PredictionErrorAvg, 0.01f);

        // 第二次采样：old + 0.1 * (new - old) = 1.0 + 0.1 * (0.5 - 1.0) = 0.95
        ClientSyncMetrics.RecordPredictionError(0.5f);
        Assert.Equal(0.95f, ClientSyncMetrics.PredictionErrorAvg, 0.01f);
    }

    // ─── 快照间隔统计 ───

    [Fact]
    public void RecordSnapshotReceived_IncrementsCounter()
    {
        ClientSyncMetrics.RecordSnapshotReceived();
        ClientSyncMetrics.RecordSnapshotReceived();
        Assert.Equal(2, ClientSyncMetrics.SnapshotsReceived);
    }

    [Fact]
    public void RecordSnapshotReceived_IntervalIsComputed()
    {
        ClientSyncMetrics.RecordSnapshotReceived();
        Thread.Sleep(50); // 等待 ~50ms
        ClientSyncMetrics.RecordSnapshotReceived();

        // 间隔应大致为 50ms（允许误差）
        Assert.True(ClientSyncMetrics.SnapshotIntervalMs > 20f,
            $"Interval should be > 20ms but was {ClientSyncMetrics.SnapshotIntervalMs}");
        Assert.True(ClientSyncMetrics.SnapshotIntervalMs < 200f,
            $"Interval should be < 200ms but was {ClientSyncMetrics.SnapshotIntervalMs}");
    }

    // ─── Reset ───

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        ClientSyncMetrics.RecordRtt(100f);
        ClientSyncMetrics.RecordInputSent();
        ClientSyncMetrics.RecordCorrection();
        ClientSyncMetrics.RecordSnapshotReceived();

        ClientSyncMetrics.Reset();

        Assert.Equal(0f, ClientSyncMetrics.EstimatedRttMs);
        Assert.Equal(0f, ClientSyncMetrics.RttJitterMs);
        Assert.Equal(0, ClientSyncMetrics.InputPacketsSent);
        Assert.Equal(0, ClientSyncMetrics.CorrectionsApplied);
        Assert.Equal(0, ClientSyncMetrics.SnapshotsReceived);
        Assert.Equal(0f, ClientSyncMetrics.SnapshotIntervalMs);
    }

    // ─── 线程安全 ───

    [Fact]
    public async Task ConcurrentIncrements_AreThreadSafe()
    {
        const int iterations = 10_000;
        var tasks = new Task[4];
        for (int t = 0; t < 4; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                    ClientSyncMetrics.RecordInputSent();
            });
        }
        await Task.WhenAll(tasks);
        Assert.Equal(4 * iterations, ClientSyncMetrics.InputPacketsSent);
    }
}

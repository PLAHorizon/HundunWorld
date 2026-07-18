using System;
using Horizon.Game.Message.Sync;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// Task B.7.4：CharacterSyncConfig 裁剪策略单元测试。
/// <para>
/// 验证 <see cref="CharacterSyncConfig"/> 声明的同步频率与间隔数值，以及频率与间隔的一致性，
/// 并估算仅位置同步时单玩家带宽是否满足 &lt;100kbps 的预算目标。
/// </para>
/// </summary>
public class CharacterSyncConfigPolicyTests
{
    // ── 频率（Hz）断言 ──────────────────────────────────────────────────

    [Fact]
    public void PositionSnapshotHz_Is20()
    {
        Assert.Equal(20, CharacterSyncConfig.PositionSnapshotHz);
    }

    [Fact]
    public void MovementStateHeartbeatHz_Is10()
    {
        Assert.Equal(10, CharacterSyncConfig.MovementStateHeartbeatHz);
    }

    [Fact]
    public void AttributeHeartbeatHz_Is1()
    {
        Assert.Equal(1, CharacterSyncConfig.AttributeHeartbeatHz);
    }

    // ── 间隔（TimeSpan）断言 ───────────────────────────────────────────

    [Fact]
    public void PositionSnapshotInterval_Is50Ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(50), CharacterSyncConfig.PositionSnapshotInterval);
    }

    [Fact]
    public void MovementStateHeartbeatInterval_Is100Ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), CharacterSyncConfig.MovementStateHeartbeatInterval);
    }

    [Fact]
    public void AttributeHeartbeatInterval_Is1Second()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), CharacterSyncConfig.AttributeHeartbeatInterval);
    }

    // ── 频率与间隔一致性 ───────────────────────────────────────────────

    [Fact]
    public void FrequencyConsistency_IntervalMatchesHz()
    {
        // Hz * Interval == 1s（即 Hz 与 Interval 互为倒数）
        // 验证：Interval == 1s / Hz
        Assert.Equal(CharacterSyncConfig.PositionSnapshotInterval,
            TimeSpan.FromSeconds(1.0 / CharacterSyncConfig.PositionSnapshotHz));

        Assert.Equal(CharacterSyncConfig.MovementStateHeartbeatInterval,
            TimeSpan.FromSeconds(1.0 / CharacterSyncConfig.MovementStateHeartbeatHz));

        Assert.Equal(CharacterSyncConfig.AttributeHeartbeatInterval,
            TimeSpan.FromSeconds(1.0 / CharacterSyncConfig.AttributeHeartbeatHz));

        // 验证：Hz == 1s / Interval（以 ticks 精度计算，避免浮点误差）
        Assert.Equal(CharacterSyncConfig.PositionSnapshotHz,
            (int)(TimeSpan.TicksPerSecond / CharacterSyncConfig.PositionSnapshotInterval.Ticks));
        Assert.Equal(CharacterSyncConfig.MovementStateHeartbeatHz,
            (int)(TimeSpan.TicksPerSecond / CharacterSyncConfig.MovementStateHeartbeatInterval.Ticks));
        Assert.Equal(CharacterSyncConfig.AttributeHeartbeatHz,
            (int)(TimeSpan.TicksPerSecond / CharacterSyncConfig.AttributeHeartbeatInterval.Ticks));
    }

    // ── 带宽预算估算 ───────────────────────────────────────────────────

    [Fact]
    public void BandwidthBudget_PositionOnly_Under100kbps()
    {
        // 估算仅位置同步时单玩家带宽：
        // SnapshotPacket 含 1 个 EntityDelta（位置 Transform：X/Y/Z/Vx/Vy/Vz/Yaw/Pitch = 8 floats = 32 bytes
        // + EntityId 8 bytes + EntityDeltaKind 1 byte + ServerTick 8 bytes + BaselineTick 8 bytes
        // + SyncPacket 基类 Kind 1 byte + ProtocolVersion 4 bytes + MemoryPack 数组头开销）
        // 约 60 bytes/包（保守估算含序列化开销）。
        const int estimatedBytesPerSnapshot = 60;
        var bytesPerSecond = estimatedBytesPerSnapshot * CharacterSyncConfig.PositionSnapshotHz;
        var bitsPerSecond = bytesPerSecond * 8L;
        var kbps = bitsPerSecond / 1024.0;

        // 20Hz * 60 bytes = 1200 bytes/s = 9600 bits/s ≈ 9.375 kbps，远低于 100kbps
        Assert.True(kbps < 100.0,
            $"仅位置同步带宽估算 {kbps:F3} kbps 应低于 100kbps。" +
            $"（{estimatedBytesPerSnapshot} bytes/packet * {CharacterSyncConfig.PositionSnapshotHz}Hz = {bytesPerSecond} bytes/s）");
    }
}

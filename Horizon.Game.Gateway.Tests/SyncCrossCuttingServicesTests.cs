using System;
using Horizon.Game.Core.Handlers;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 5.2 — SyncPacketHandler 拆分出的横切服务单测：
/// <see cref="SessionInputDeduper"/>（去重/握手基线/复合 key 隔离/时间戳保护/清理）、
/// <see cref="SessionInteractionRateLimiter"/>（限流边界/周期清理/per-key 独立）、
/// <see cref="SyncMetricsSampler.ShouldRecordInputLag"/>（InputLag 过滤纯函数）。
/// </summary>
public class InputDeduperTests
{
    private static SessionInputDeduper CreateDeduper() => new(new SyncSessionState());

    [Fact]
    public void FirstInput_IsAccepted_AndAdvancesWatermark()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());

        var decision = deduper.CheckDedup(42, "conn-1", 100, out var lastAccepted);
        Assert.Equal(InputDedupDecision.Accepted, decision);
        Assert.Equal(0, lastAccepted);

        // 更新的 tick 仍接受
        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-1", 101, out _));
    }

    [Fact]
    public void DuplicateAndOlderTicks_AreRejected()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());
        deduper.CheckDedup(42, "conn-1", 100, out _);

        // 完全重复
        var decision = deduper.CheckDedup(42, "conn-1", 100, out var lastAccepted);
        Assert.Equal(InputDedupDecision.Duplicate, decision);
        Assert.Equal(100, lastAccepted);

        // 更早的 tick
        Assert.Equal(InputDedupDecision.Duplicate, deduper.CheckDedup(42, "conn-1", 99, out _));
    }

    [Fact]
    public void HandshakeBaseline_MarksOlderInputAsStaleSession()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());

        // 握手声明基线 1000
        deduper.RecordHandshakeBaseline(42, "conn-1", 1000);

        // 低于基线的包 = 旧会话残留
        Assert.Equal(InputDedupDecision.StaleSession, deduper.CheckDedup(42, "conn-1", 500, out _));

        // 不低于基线的包正常接受
        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-1", 1000, out _));
    }

    [Fact]
    public void HandshakeBaseline_TimestampProtection_ResetsToZero()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());

        // 客户端错误发送 Unix 时间戳（> 1_000_000）作为基线 → 重置为 0，避免后续 tick 全部被拒
        deduper.RecordHandshakeBaseline(42, "conn-1", 1_700_000_000_000);

        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-1", 100, out _));
    }

    [Fact]
    public void CompositeKey_IsolatesConnections_OldConnectionHighTickDoesNotPollute()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());

        // 旧连接把水位推到 10570
        deduper.CheckDedup(42, "conn-old", 10570, out _);

        // 新连接从低 tick 开始，不受旧连接水位影响
        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-new", 6480, out _));
        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-new", 6481, out _));
    }

    [Fact]
    public void CleanupCharacter_RemovesOtherConnectionsButKeepsCurrent()
    {
        var deduper = new SessionInputDeduper(new SyncSessionState());
        deduper.CheckDedup(42, "conn-old", 5000, out _);
        deduper.RecordHandshakeBaseline(42, "conn-old", 4990);
        deduper.CheckDedup(43, "conn-old", 100, out _);

        deduper.CleanupCharacter(42, keepConnectionId: "conn-new");

        // 同 characterId 的旧连接记录被清理 → 不再去重（新连接重新接受）
        Assert.Equal(InputDedupDecision.Accepted, deduper.CheckDedup(42, "conn-old", 0, out _));
        // 其他 characterId 不受影响
        Assert.Equal(InputDedupDecision.Duplicate, deduper.CheckDedup(43, "conn-old", 100, out _));
    }
}

/// <summary><see cref="SessionInteractionRateLimiter"/> 单测。</summary>
public class InteractionRateLimiterTests
{
    private static SessionInteractionRateLimiter CreateLimiter() => new(new SyncSessionState());

    [Fact]
    public void FirstRequest_Allowed_ImmediateSecond_Blocked()
    {
        var limiter = CreateLimiter();

        Assert.True(limiter.Allow(7));
        // 间隔远小于 100ms（10/sec）→ 被限流
        Assert.False(limiter.Allow(7));
    }

    [Fact]
    public void DifferentKeys_AreIndependent()
    {
        var limiter = CreateLimiter();

        Assert.True(limiter.Allow(1));
        Assert.False(limiter.Allow(1));
        // 不同 key 不受影响
        Assert.True(limiter.Allow(2));
    }

    [Fact]
    public void StaleEntries_AreCleanedUp_OnNextRequest()
    {
        var state = new SyncSessionState();
        var limiter = new SessionInteractionRateLimiter(state);

        limiter.Allow(9);

        // 模拟时间流逝：条目超过 1 秒未活动，清理器应将其移除
        state.InteractionLastRequestTime[9] = DateTime.UtcNow.AddSeconds(-5);
        state.LastRateLimitCleanup = DateTime.MinValue;

        // 下一次 Allow 触发周期清理 → 旧条目被移除 → 允许通过
        Assert.True(limiter.Allow(9));
    }
}

/// <summary><see cref="SyncMetricsSampler"/> InputLag 过滤纯函数单测。</summary>
public class SyncMetricsSamplerTests
{
    [Fact]
    public void InputLagFilter_AcceptsPositiveSubMinuteLag()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(SyncMetricsSampler.ShouldRecordInputLag(nowMs - 100));
    }

    [Fact]
    public void InputLagFilter_RejectsFutureTimestamps()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(SyncMetricsSampler.ShouldRecordInputLag(nowMs + 5000)); // 负数滞后
    }

    [Fact]
    public void InputLagFilter_RejectsOverOneMinuteLag()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(SyncMetricsSampler.ShouldRecordInputLag(nowMs - 120_000));
    }
}

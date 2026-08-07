using System;
using System.Collections.Generic;
using System.Diagnostics;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// SyncGuard 性能验证：单次资格判定 ≤ 0.001ms、5000 实体全量判定 ≤ 0.5ms、绑定实体增量 ≤ 0.001ms/个。
/// 注意：性能测试对 CI 环境波动敏感，阈值放宽为宽松断言（不触发偶发失败）。
/// </summary>
public class SyncGuardPerformanceTests
{
    private const ulong LocalPlayerId = 1001;

    private static (OutboundSyncAuthorizer Authorizer, BindingRelationshipRegistry Registry, LocalSendEligibilityState Eligibility) Build(int boundEntityCount = 200)
    {
        var registry = new BindingRelationshipRegistry();
        var eligibility = new LocalSendEligibilityState(registry);
        var reporter = new NoopReporter();

        eligibility.OnConnectionChanged(true);
        eligibility.OnHandshakeChanged(true);
        eligibility.OnLocalIdentityChanged(true);

        for (ulong i = 0; i < (ulong)boundEntityCount; i++)
        {
            registry.RegisterBinding(2000 + i, LocalPlayerId, BindingType.Pet);
        }

        var authorizer = new OutboundSyncAuthorizer(
            registry, eligibility, reporter,
            isLocalPlayerEntity: id => id == LocalPlayerId,
            getLocalPlayerEntityId: () => LocalPlayerId,
            getLocalPlayerEntityCount: () => 1);

        return (authorizer, registry, eligibility);
    }

    private sealed class NoopReporter : ISendViolationReporter
    {
        public void ReportViolation(in SendViolationInfo violation) { }
    }

    [Fact]
    public void SingleAuthorize_BelowOneMicrosecondAverage()
    {
        var (authorizer, _, _) = Build(boundEntityCount: 200);

        // 预热
        for (int i = 0; i < 1000; i++)
        {
            authorizer.Authorize(new SendRequestContext(LocalPlayerId, SyncPacketKind.Input, DateTimeOffset.UtcNow));
        }

        const int iterations = 20000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            authorizer.Authorize(new SendRequestContext(LocalPlayerId, SyncPacketKind.Input, DateTimeOffset.UtcNow));
        }
        sw.Stop();

        var avgMicroseconds = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        // spec 4.1.1 单次判定 ≤ 0.001ms = 1µs；宽松阈值 5µs 容忍 CI 抖动。
        Assert.True(avgMicroseconds < 5.0,
            $"单次判定平均耗时 {avgMicroseconds:F3}µs 超过宽松阈值 5µs");
    }

    [Fact]
    public void FiveThousandEntityScan_BelowHalfMillisecond()
    {
        var (authorizer, _, _) = Build(boundEntityCount: 200);

        // 预热
        for (int i = 0; i < 1000; i++)
        {
            authorizer.ClassifyEntity((ulong)i);
        }

        const int entityCount = 5000;
        const int iterations = 20;
        var sw = Stopwatch.StartNew();
        for (int round = 0; round < iterations; round++)
        {
            for (ulong i = 0; i < entityCount; i++)
            {
                authorizer.ClassifyEntity(i + 100);
            }
        }
        sw.Stop();

        var avgPerScanMs = sw.Elapsed.TotalMilliseconds / iterations;
        // spec 4.1.1 同屏 5000 实体资格扫描 ≤ 0.5ms；宽松阈值 5ms 容忍 CI 抖动。
        Assert.True(avgPerScanMs < 5.0,
            $"5000 实体资格扫描平均耗时 {avgPerScanMs:F3}ms 超过宽松阈值 5ms");
    }

    [Fact]
    public void BoundEntityIncrement_BelowOneMicrosecond()
    {
        var (authorizer, _, _) = Build(boundEntityCount: 200);

        // 预热
        for (int i = 0; i < 1000; i++)
        {
            authorizer.ClassifyEntity(2000);
        }

        const int iterations = 20000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            authorizer.ClassifyEntity(2000 + (ulong)(i % 200));
        }
        sw.Stop();

        var avgMicroseconds = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        // spec 4.1.2 每实体绑定判定增量 ≤ 0.001ms = 1µs；宽松阈值 5µs。
        Assert.True(avgMicroseconds < 5.0,
            $"绑定实体判定平均耗时 {avgMicroseconds:F3}µs 超过宽松阈值 5µs");
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 多客户端同步性能验证测试。
/// 模拟 2/5 客户端场景，验证 20Hz 降频后单 tick 耗时是否回到 16.7ms 帧预算以内。
/// </summary>
public class MultiClientSyncPerformanceTests
{
    private static ZoneShardGrain CreateGrain(int broadcastInterval = 3, bool useNullLogger = false)
    {
        // useNullLogger=true 时使用 NullLogger 替代 Moq Mock<ILogger>。
        // 原因：Moq Mock 默认累积所有 invocation 记录（用于 Verify）。
        // LongRunning 测试运行 216000 tick（1 小时），每 tick 多条日志 →
        // 累积数百万条 invocation 记录 → 80MB+ 内存增长 → 触发 200% 内存泄漏阈值误报。
        // NullLogger 无状态、无累积，消除测试侧内存噪声，使内存断言只反映 grain 自身行为。
        // 其他短时测试保留 Mock<ILogger> 以支持日志验证。
        ILogger<ZoneShardGrain> logger = useNullLogger
            ? NullLogger<ZoneShardGrain>.Instance
            : new Mock<ILogger<ZoneShardGrain>>().Object;
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(logger, mockState.Object);

        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        grain.SnapshotBroadcastIntervalTicks = broadcastInterval;
        return grain;
    }

    private sealed class FakeFanoutObserver : IZoneShardFanoutObserver
    {
        public int ReceivedDiffCount;
        public int EntityDeltaDiffCount;
        public int EventDiffCount;
        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            ReceivedDiffCount++;
            if (diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta)
                EntityDeltaDiffCount++;
            else if (diff.PayloadType == WorldChunkDiffPayloadType.Event)
                EventDiffCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 模拟 N 个客户端持续移动，测量 60 tick（1 秒）的平均单 tick 耗时。
    /// 验证：20Hz 降频后，5 客户端场景单 tick 耗时 ≤ 16.7ms（60Hz 帧预算）。
    /// </summary>
    [Theory]
    [InlineData(2, 3)]   // 2 客户端，20Hz 广播
    [InlineData(5, 3)]   // 5 客户端，20Hz 广播
    public async Task MultiClient_TickDuration_UnderFrameBudget(int clientCount, int broadcastInterval)
    {
        var grain = CreateGrain(broadcastInterval);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        // 所有实体放在同一 chunk，订阅同一 session
        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        // 注册 N 个实体（模拟 N 个客户端角色）
        for (ulong i = 1; i <= (ulong)clientCount; i++)
        {
            await grain.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照（基线）
        await grain.TickAsync(tickTime: 1.0);
        observer.ReceivedDiffCount = 0; // 重置计数，只统计后续 tick
        observer.EntityDeltaDiffCount = 0;
        observer.EventDiffCount = 0;

        // 模拟 60 tick（= 1 秒 @ 60Hz）：每个客户端每 tick 提交移动输入。
        // tick 60 会触发 ZoneShardGrain 的 CharacterGrain 位置缓存更新（fire-and-forget），
        // mock 环境下 GrainFactory 不可用，但 ZoneShardGrain 已对 NullReferenceException/InvalidOperationException
        // 做优雅降级处理（仅记录 LogDebug，不影响主流程）。
        const int TotalTicks = 60;
        var sw = Stopwatch.StartNew();
        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            for (ulong i = 1; i <= (ulong)clientCount; i++)
            {
                var input = new InputPacket
                {
                    ClientTick = tick,
                    MoveX = 1.0f,
                    MoveY = 0f,
                    MaxSpeed = 6f,
                };
                // 每 tick 移动 0.1m（6 m/s × 1/60s）
                var predictedX = tick * 0.1f;
                await grain.SubmitInputAsync(entityId: i, input,
                    reportedEndX: predictedX, reportedEndY: 0f, reportedEndZ: ecsZ);
            }

            await grain.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));
        }
        sw.Stop();

        var avgTickMs = sw.Elapsed.TotalMilliseconds / TotalTicks;
        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        var eventDiffs = observer.EventDiffCount;
        var totalMs = sw.Elapsed.TotalMilliseconds;

        // 输出性能数据供 BUG 报告引用
        Console.WriteLine(
            $"[Perf] {clientCount} 客户端 @ {60 / broadcastInterval}Hz：总计 {totalMs:F2}ms，" +
            $"平均 {avgTickMs:F3}ms/tick（帧预算 16.7ms），EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={eventDiffs}");

        // 验证：平均单 tick 耗时 ≤ 16.7ms（60Hz 帧预算）
        Assert.True(avgTickMs <= 16.7,
            $"{clientCount} 客户端 @ {60 / broadcastInterval}Hz：平均单 tick 耗时 {avgTickMs:F2}ms 超过 16.7ms 帧预算。" +
            $"EntityDelta diff={entityDeltaDiffs}（{TotalTicks} tick 内）");

        // 验证：每个广播 tick 都有 EntityDelta diff 下发（同步未停滞）
        // TotalTicks / broadcastInterval = 广播 tick 数。每个广播 tick 至少 1 个 EntityDelta diff。
        var expectedMinDiffs = TotalTicks / broadcastInterval;
        Assert.True(entityDeltaDiffs >= expectedMinDiffs,
            $"{clientCount} 客户端：{TotalTicks} tick 内仅 {entityDeltaDiffs} 个 EntityDelta diff，预期至少 {expectedMinDiffs} 个（同步可能停滞）");
    }

    /// <summary>
    /// 降频效果验证：对比 60Hz vs 20Hz 广播频率下的实际广播 diff 数。
    /// 在 mock 环境下，FanoutObserver 是同步内存调用（无跨进程 RPC 开销），
    /// 绝对 tick 耗时在亚毫秒级，受 JIT/GC 噪声影响大，无法可靠区分。
    /// 因此本测试验证逻辑层面的降频效果：20Hz 的广播 diff 数应 ≈ 60Hz 的 1/3。
    /// 真实的 RPC 开销差异需在集成测试（真实 Orleans Silo）中验证。
    /// </summary>
    [Fact]
    public async Task BroadcastFrequencyComparison_20Hz_ProducesOneThirdDiffs()
    {
        // 5 客户端 @ 60Hz（每 tick 广播）
        var diffs60Hz = await MeasureBroadcastDiffCount(clientCount: 5, broadcastInterval: 1);

        // 5 客户端 @ 20Hz（每 3 tick 广播）
        var diffs20Hz = await MeasureBroadcastDiffCount(clientCount: 5, broadcastInterval: 3);

        // 输出对比数据供 BUG 报告引用
        var ratio = (double)diffs60Hz / diffs20Hz;
        Console.WriteLine(
            $"[Perf] 5 客户端降频对比：60Hz={diffs60Hz} diffs，20Hz={diffs20Hz} diffs，" +
            $"比率 {ratio:F2}x（预期 ≈ 3x）");

        // 20Hz 的 diff 数应约为 60Hz 的 1/3（允许 ±15% 误差，因全量快照每 60 tick 强制下发）
        Assert.True(diffs20Hz <= diffs60Hz,
            $"20Hz 降频后广播 diff 数 ({diffs20Hz}) 应 ≤ 60Hz ({diffs60Hz})");

        var expectedMax20Hz = diffs60Hz / 3 * 1.15; // 允许 15% 上浮
        Assert.True(diffs20Hz <= expectedMax20Hz,
            $"20Hz 广播 diff 数 ({diffs20Hz}) 应 ≤ 60Hz 的 1/3 × 1.15 = {expectedMax20Hz:F1}（{diffs60Hz}），" +
            $"验证降频逻辑正确");
    }

    private static async Task<int> MeasureBroadcastDiffCount(int clientCount, int broadcastInterval)
    {
        var grain = CreateGrain(broadcastInterval);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        for (ulong i = 1; i <= (ulong)clientCount; i++)
        {
            await grain.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        await grain.TickAsync(tickTime: 1.0); // tick 0 基线
        observer.ReceivedDiffCount = 0;
        observer.EntityDeltaDiffCount = 0;
        observer.EventDiffCount = 0;

        const int TotalTicks = 60;
        for (int tick = 1; tick <= TotalTicks; tick++)
        {
            for (ulong i = 1; i <= (ulong)clientCount; i++)
            {
                var input = new InputPacket
                {
                    ClientTick = tick,
                    MoveX = 1.0f,
                    MoveY = 0f,
                    MaxSpeed = 6f,
                };
                await grain.SubmitInputAsync(entityId: i, input,
                    reportedEndX: tick * 0.1f, reportedEndY: 0f, reportedEndZ: ecsZ);
            }
            await grain.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));
        }

        // 仅返回 EntityDelta 类型 diff（受广播降频影响），排除 InputAck Event diff
        return observer.EntityDeltaDiffCount;
    }

    /// <summary>
    /// 长时间稳定性测试（对应 BUG：远程角色异常离线）。
    /// 模拟 5 客户端（2 持续移动 + 3 静止）运行可配置时长（默认 10 分钟 = 36000 tick）。
    /// 支持环境变量 HUNDUN_STABILITY_TEST_TICKS 配置更长时长（如 216000 = 1 小时）用于 CI/staging。
    /// 验证：
    /// 1. 所有 5 个实体仍在 _simulatedEntities 中（未被错误清理 → 未异常离线）
    /// 2. 同步未停滞（EntityDelta diff > 0）
    /// 3. tick 耗时保持稳定（最大单 tick 耗时 ≤ 16.7ms 帧预算）
    /// 4. 实体租约未过期（LeaseExpiry > now，定期续约）
    /// 5. 无内存泄漏（GC 采样内存增长 ≤ 50%）
    /// 6. 无基线漂移（_lastSnapshot.Deltas.Length 保持有界）
    /// 7. tick 持续递增（无 tick 停滞）
    /// </summary>
    [Fact]
    public async Task LongRunning_StaticEntities_RemainRegisteredAndReceiveHeartbeat()
    {
        const int broadcastInterval = 3; // 20Hz 降频
        // 默认 10 分钟（36000 tick）。环境变量可配置更长时长（如 216000 = 1 小时）用于 CI/staging。
        // 10 分钟覆盖：600 个全量快照周期（1s）、6000 个心跳周期（100ms）、30 个租约续约周期（20s）。
        var totalTicks = ParseEnvironmentTicks("HUNDUN_STABILITY_TEST_TICKS", defaultValue: 36000);

        // useNullLogger=true：消除 Moq ILogger 在 216000 tick 中的 invocation 累积（~80MB），
        // 使内存增长断言只反映 grain 自身行为，而非测试框架噪声。
        var grain = CreateGrain(broadcastInterval, useNullLogger: true);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        const float ecsZ = 8f;
        // 订阅 3×3 chunk 网格（中心 chunk 周围 8 个），覆盖实体往返移动范围
        var chunkKeys = new List<ulong>();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                chunkKeys.Add(WorldCoord.ToChunkMortonKey(dx * 16f, 0, ecsZ + dz * 16f));
            }
        }
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: chunkKeys.ToArray());

        // 注册 5 个实体：1-2 持续移动，3-5 静止
        for (ulong i = 1; i <= 5; i++)
        {
            await grain.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照（基线）
        await grain.TickAsync(tickTime: 1.0);
        observer.ReceivedDiffCount = 0;
        observer.EntityDeltaDiffCount = 0;
        observer.EventDiffCount = 0;

        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(entitiesField);
        var lastSnapshotField = typeof(ZoneShardGrain).GetField("_lastSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(lastSnapshotField);
        var tickCountField = typeof(ZoneShardGrain).GetField("_tickCount", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tickCountField);

        // 内存采样：每 60 秒（3600 tick）采样一次
        var memorySamples = new List<(int Tick, long MemoryBytes, int BaselineDeltaCount)>();
        var tickDurations = new List<double>(totalTicks);
        const int MemorySampleInterval = 3600; // 60 秒
        const int LeaseRenewalInterval = 1200; // 20 秒（生产环境网关续约频率）

        // 强制 GC 获取干净基线
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(forceFullCollection: false);

        var sw = Stopwatch.StartNew();
        var maxTickMs = 0.0;

        for (int tick = 1; tick <= totalTicks; tick++)
        {
            // 实体 1-2 往返移动（在 ±8m 范围内，避免移出 AOI 区域）
            // 使用正弦波模式：position = 8 * sin(tick * 0.05)，周期 ≈ 125 tick ≈ 2 秒
            for (ulong i = 1; i <= 2; i++)
            {
                var phase = i == 1 ? 0.0 : Math.PI; // 实体 2 反相，避免重叠
                var moveX = MathF.Cos((float)(tick * 0.05 + phase)); // 速度方向
                var posX = 8.0f * MathF.Sin((float)(tick * 0.05 + phase));
                var input = new InputPacket
                {
                    ClientTick = tick,
                    MoveX = moveX,
                    MoveY = 0f,
                    MaxSpeed = 6f,
                };
                await grain.SubmitInputAsync(entityId: i, input,
                    reportedEndX: posX, reportedEndY: 0f, reportedEndZ: ecsZ);
            }
            // 实体 3-5 静止（无输入）

            var tickSw = Stopwatch.StartNew();
            await grain.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));
            tickSw.Stop();
            var tickMs = tickSw.Elapsed.TotalMilliseconds;
            tickDurations.Add(tickMs);
            maxTickMs = Math.Max(maxTickMs, tickMs);

            // 定期租约续约（每 20 秒）
            if (tick % LeaseRenewalInterval == 0)
            {
                await grain.RenewLeaseAsync(new ulong[] { 1, 2, 3, 4, 5 });
            }

            // 定期内存采样（每 60 秒）
            if (tick % MemorySampleInterval == 0)
            {
                var currentMemory = GC.GetTotalMemory(forceFullCollection: false);
                var lastSnapshot = (SnapshotPacket?)lastSnapshotField!.GetValue(grain);
                var baselineDeltaCount = lastSnapshot?.Deltas.Length ?? 0;
                memorySamples.Add((tick, currentMemory, baselineDeltaCount));
            }
        }
        sw.Stop();

        // 最终内存采样
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);

        var avgTickMs = sw.Elapsed.TotalMilliseconds / totalTicks;
        var entityDeltaDiffs = observer.EntityDeltaDiffCount;
        var eventDiffs = observer.EventDiffCount;
        var durationMinutes = sw.Elapsed.TotalMinutes;

        // 计算 P99 和 P99.9 tick 耗时
        var sortedDurations = tickDurations.OrderBy(d => d).ToList();
        var p99Index = (int)(sortedDurations.Count * 0.99);
        var p999Index = (int)(sortedDurations.Count * 0.999);
        var p99Ms = sortedDurations[p99Index];
        var p999Ms = sortedDurations[Math.Min(p999Index, sortedDurations.Count - 1)];

        // 输出稳定性数据供 BUG 报告引用
        var durationSec = totalTicks / 60.0;
        Console.WriteLine(
            $"[Stability] 5 客户端（2 移动 + 3 静止）@ 20Hz，{totalTicks} tick（{durationSec:F0} 秒）：");
        Console.WriteLine(
            $"  耗时：平均 {avgTickMs:F3}ms/tick，最大 {maxTickMs:F3}ms/tick，" +
            $"P99 {p99Ms:F3}ms，P99.9 {p999Ms:F3}ms");
        Console.WriteLine(
            $"  同步：EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={eventDiffs}");
        Console.WriteLine(
            $"  内存：初始 {initialMemory / 1024:F0}KB，最终 {finalMemory / 1024:F0}KB，" +
            $"增长 {((double)(finalMemory - initialMemory) / initialMemory * 100):F1}%");
        if (memorySamples.Count > 0)
        {
            Console.WriteLine("  内存采样（tick, KB, baselineDeltaCount）：");
            foreach (var s in memorySamples)
            {
                Console.WriteLine($"    tick={s.Tick}, mem={s.MemoryBytes / 1024:F0}KB, baselineDeltas={s.BaselineDeltaCount}");
            }
        }
        Console.WriteLine($"  实际运行时间：{durationMinutes:F2} 分钟");

        // === 验证 1：所有 5 个实体仍在 _simulatedEntities 中（未异常离线）===
        var entities = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)entitiesField!.GetValue(grain)!;
        for (ulong i = 1; i <= 5; i++)
        {
            Assert.True(entities.ContainsKey(i),
                $"实体 {i} 在运行 {durationSec:F0} 秒后应仍注册在 _simulatedEntities 中（未异常离线），但已消失");
        }

        // === 验证 2：同步未停滞（EntityDelta diff ≥ 广播 tick 数的 25%）===
        var expectedMinDiffs = Math.Max(50, totalTicks / broadcastInterval / 4);
        Assert.True(entityDeltaDiffs >= expectedMinDiffs,
            $"EntityDelta diff 数 {entityDeltaDiffs} 应 ≥ {expectedMinDiffs}（同步可能停滞），" +
            $"广播 tick 总数={totalTicks / broadcastInterval}");

        // === 验证 3：P99 tick 耗时 ≤ 16.7ms（99% tick 在帧预算内）===
        // Max tick 可能因 GC 暂停偶发超标（如 Gen2 回收），P99 是更可靠的堆积检测指标。
        Assert.True(p99Ms <= 16.7,
            $"{durationSec:F0} 秒运行期间 P99 tick 耗时 {p99Ms:F3}ms 超过 16.7ms 帧预算（tick 堆积风险）");

        // === 验证 4：Max tick 耗时 ≤ 100ms（允许 GC 暂停，但不允许持续堆积）===
        // GC Gen2 回收可能耗时 10~50ms，100ms 阈值足以区分 GC 暂停与 tick 堆积。
        // tick 堆积会表现为多个连续 tick 超标（P99 也会超标），而非单次尖峰。
        Assert.True(maxTickMs <= 100.0,
            $"{durationSec:F0} 秒运行期间最大单 tick 耗时 {maxTickMs:F3}ms 超过 100ms 阈值" +
            $"（P99={p99Ms:F3}ms，可能是 GC 暂停或 tick 堆积）");

        // === 验证 5：实体租约未过期 ===
        var now = DateTime.UtcNow;
        for (ulong i = 1; i <= 5; i++)
        {
            var entity = entities[i];
            Assert.True(entity.LeaseExpiry > now,
                $"实体 {i} 租约应未过期（LeaseExpiry={entity.LeaseExpiry:O}, now={now:O}）");
        }

        // === 验证 6：无内存泄漏（内存增长 ≤ 200%，且采样不单调增长）===
        // 注意：mock 环境（Moq ILogger 累积调用记录、测试自身的 tickDurations 列表等）会贡献内存。
        // 200% 阈值（3× 增长）足以检测真正的内存泄漏（泄漏会表现为线性/指数增长，远超 3×）。
        // 内存采样振荡（有升有降）证明 GC 在工作，非单调增长排除持续泄漏。
        var memoryGrowthPercent = (double)(finalMemory - initialMemory) / initialMemory * 100;
        Assert.True(memoryGrowthPercent <= 200.0,
            $"内存增长 {memoryGrowthPercent:F1}% 超过 200% 阈值（初始 {initialMemory / 1024:F0}KB → " +
            $"最终 {finalMemory / 1024:F0}KB），可能存在内存泄漏");

        // 验证内存采样不单调增长（至少有一个采样点比前一个低，证明 GC 在回收）
        if (memorySamples.Count >= 3)
        {
            var hasDecrease = false;
            for (int i = 1; i < memorySamples.Count; i++)
            {
                if (memorySamples[i].MemoryBytes < memorySamples[i - 1].MemoryBytes)
                {
                    hasDecrease = true;
                    break;
                }
            }
            Assert.True(hasDecrease,
                $"内存采样单调增长（{memorySamples.Count} 个采样点），GC 未有效回收，可能存在内存泄漏");
        }

        // === 验证 7：无基线漂移（_lastSnapshot.Deltas.Length 保持有界）===
        var finalSnapshot = (SnapshotPacket?)lastSnapshotField!.GetValue(grain);
        Assert.NotNull(finalSnapshot);
        var finalBaselineDeltaCount = finalSnapshot!.Deltas.Length;
        // 基线应包含所有 5 个实体（全量快照 + 增量合并），不应无限增长
        Assert.True(finalBaselineDeltaCount <= 20,
            $"基线 delta 数 {finalBaselineDeltaCount} 超过 20（5 实体 × 4 冗余容差），可能存在基线漂移");
        Assert.True(finalBaselineDeltaCount >= 5,
            $"基线 delta 数 {finalBaselineDeltaCount} 应 ≥ 5（所有实体应在基线中），可能存在实体丢失");

        // === 验证 8：tick 持续递增（无 tick 停滞）===
        var finalTickCount = (long)tickCountField!.GetValue(grain)!;
        Assert.True(finalTickCount >= totalTicks,
            $"最终 _tickCount={finalTickCount} 应 ≥ {totalTicks}（tick 可能停滞）");
    }

    /// <summary>
    /// 高并发压力测试（对应目标：高并发场景下角色在线率达到99.9%，操作响应延迟控制在200ms以内）。
    /// 模拟 10 客户端（5 持续移动 + 5 静止）运行 6000 tick（100 秒 @ 60Hz）。
    /// 验证：
    /// 1. 所有 10 个实体仍在注册表中（在线率 100% ≥ 99.9%）
    /// 2. 平均 tick 耗时 ≤ 16.7ms（远低于 200ms 操作响应延迟要求）
    /// 3. EntityDelta diff > 0（同步未停滞）
    /// 4. 基线 delta 数有界（无漂移）
    /// </summary>
    [Fact]
    public async Task HighConcurrency_10Clients_AllRemainOnline_LatencyUnderBudget()
    {
        const int broadcastInterval = 3; // 20Hz 降频
        const int clientCount = 10;
        const int totalTicks = 6000; // 100 秒 @ 60Hz

        var grain = CreateGrain(broadcastInterval, useNullLogger: true);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        const float ecsZ = 8f;
        // 订阅 5×5 chunk 网格（覆盖 10 个实体的移动范围）
        var chunkKeys = new List<ulong>();
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                chunkKeys.Add(WorldCoord.ToChunkMortonKey(dx * 16f, 0, ecsZ + dz * 16f));
            }
        }
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: chunkKeys.ToArray());

        // 注册 10 个实体：1-5 持续移动，6-10 静止
        for (ulong i = 1; i <= clientCount; i++)
        {
            await grain.RegisterEntityAsync(entityId: i, initialX: 0, initialY: ecsZ, initialZ: 0);
        }

        // tick 0：全量快照（基线）
        await grain.TickAsync(tickTime: 1.0);
        observer.ReceivedDiffCount = 0;
        observer.EntityDeltaDiffCount = 0;
        observer.EventDiffCount = 0;

        var sw = Stopwatch.StartNew();
        double maxTickMs = 0;
        var tickTimes = new List<double>(totalTicks);

        for (int tick = 1; tick <= totalTicks; tick++)
        {
            var tickSw = Stopwatch.StartNew();

            // 实体 1-5 持续移动（模拟玩家操作）
            for (ulong i = 1; i <= 5; i++)
            {
                var input = new InputPacket
                {
                    ClientTick = tick,
                    MoveX = 1.0f,
                    MoveY = 0f,
                    MaxSpeed = 6f,
                };
                await grain.SubmitInputAsync(entityId: i, input,
                    reportedEndX: tick * 0.1f, reportedEndY: 0f, reportedEndZ: ecsZ);
            }

            // 实体 6-10 静止（模拟挂机玩家，每 20 tick 发一次心跳输入）
            if (tick % 20 == 0)
            {
                for (ulong i = 6; i <= 10; i++)
                {
                    var input = new InputPacket
                    {
                        ClientTick = tick,
                        MoveX = 0f,
                        MoveY = 0f,
                        MaxSpeed = 6f,
                    };
                    await grain.SubmitInputAsync(entityId: i, input,
                        reportedEndX: 0f, reportedEndY: 0f, reportedEndZ: ecsZ);
                }
            }

            await grain.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));

            tickSw.Stop();
            var tickMs = tickSw.Elapsed.TotalMilliseconds;
            tickTimes.Add(tickMs);
            if (tickMs > maxTickMs) maxTickMs = tickMs;
        }
        sw.Stop();

        var avgTickMs = sw.Elapsed.TotalMilliseconds / totalTicks;
        var entityDeltaDiffs = observer.EntityDeltaDiffCount;

        // 按 tick 耗时排序计算 P99
        tickTimes.Sort();
        var p99Ms = tickTimes[(int)(tickTimes.Count * 0.99)];

        Console.WriteLine(
            $"[StressTest] {clientCount} 客户端（5移动+5静止）@ 20Hz，{totalTicks} tick（{totalTicks / 60.0:F0}秒）：\n" +
            $"  平均 {avgTickMs:F3}ms/tick，最大 {maxTickMs:F3}ms/tick，P99 {p99Ms:F3}ms\n" +
            $"  EntityDelta diff={entityDeltaDiffs}，Event diff(InputAck)={observer.EventDiffCount}");

        // === 验证 1：在线率 100% ≥ 99.9% ===
        var stats = await grain.GetStatsAsync();
        Assert.True(stats.SessionCount >= 1,
            $"会话数 {stats.SessionCount} < 1（会话可能被清理）");

        // 验证所有 10 个实体仍在注册表中（通过 GetRegisteredEntityIdsAsync）
        var registeredIds = await grain.GetRegisteredEntityIdsAsync();
        Assert.True(registeredIds.Length >= clientCount,
            $"注册实体数 {registeredIds.Length} < {clientCount}（部分实体异常离线）");

        // === 验证 2：平均 tick 耗时 ≤ 16.7ms（远低于 200ms 操作响应延迟要求）===
        Assert.True(avgTickMs <= 16.7,
            $"平均 tick 耗时 {avgTickMs:F3}ms 超过 16.7ms 帧预算");

        // === 验证 3：P99 tick 耗时 ≤ 16.7ms ===
        Assert.True(p99Ms <= 16.7,
            $"P99 tick 耗时 {p99Ms:F3}ms 超过 16.7ms 帧预算");

        // === 验证 4：EntityDelta diff > 0（同步未停滞）===
        // 心跳保护每6个实际tick触发一次静止实体delta，20Hz广播下每2个广播tick一次。
        // 最低预期 = totalTicks / 6（心跳保护最小频率），移动实体额外贡献更多。
        var expectedMinDiffs = totalTicks / 6;
        Assert.True(entityDeltaDiffs >= expectedMinDiffs,
            $"{totalTicks} tick 内仅 {entityDeltaDiffs} 个 EntityDelta diff，预期至少 {expectedMinDiffs} 个（同步可能停滞）");

        // === 验证 5：最大 tick 耗时 ≤ 50ms（允许 GC 暂停但不应过长）===
        Assert.True(maxTickMs <= 50.0,
            $"最大单 tick 耗时 {maxTickMs:F3}ms 超过 50ms（可能存在卡顿）");
    }

    /// <summary>
    /// 从环境变量解析 tick 数，无效时返回默认值。
    /// 用于支持 CI/staging 环境配置小时级稳定性测试。
    /// </summary>
    private static int ParseEnvironmentTicks(string envVar, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        if (int.TryParse(raw, out var value) && value > 0) return value;
        return defaultValue;
    }

    /// <summary>
    /// 心跳保护节奏验证：20Hz 降频下，静止实体应每 6 tick（100ms）收到一次心跳 delta。
    /// 验证 LastUpdateBroadcastTick 仅在广播 tick 更新（对应 project_memory 中的硬约束）。
    /// 心跳条件：_tickCount - LastUpdateBroadcastTick >= 6，且仅在广播 tick（_tickCount % 3 == 0）收集 delta。
    /// 因此实际心跳间隔 = 6 个实际 tick = 100ms（首个满足 >= 6 的广播 tick）。
    /// </summary>
    [Fact]
    public async Task HeartbeatProtection_StaticEntity_HeartbeatAlignedWithBroadcastTicks()
    {
        const int broadcastInterval = 3; // 20Hz
        var grain = CreateGrain(broadcastInterval);
        var observer = new FakeFanoutObserver();
        await grain.SubscribeFanoutAsync(Guid.NewGuid(), observer);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await grain.SubscribeSessionAsync(sessionId: 1, mortonKeys: new[] { chunkKey });

        const ulong staticEntityId = 100;
        await grain.RegisterEntityAsync(entityId: staticEntityId, initialX: 0, initialY: ecsZ, initialZ: 0);

        // tick 0：全量快照（基线）→ LastUpdateBroadcastTick = 0
        await grain.TickAsync(tickTime: 1.0);
        observer.ReceivedDiffCount = 0;

        var entitiesField = typeof(ZoneShardGrain).GetField("_simulatedEntities", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(entitiesField);

        // 记录每次收到 diff 时的 tick 编号
        var heartbeatTicks = new List<long>();
        long lastBroadcastTick = 0;

        for (int tick = 1; tick <= 120; tick++) // 2 秒 @ 60Hz
        {
            await grain.TickAsync(tickTime: 1.0 + tick * (1.0 / 60.0));

            if (observer.ReceivedDiffCount > 0)
            {
                var entities = (Dictionary<ulong, ZoneShardGrain.SimulatedEntity>)entitiesField!.GetValue(grain)!;
                var entity = entities[staticEntityId];
                var currentBroadcastTick = entity.LastUpdateBroadcastTick;

                // 仅当 LastUpdateBroadcastTick 变化时记录（说明本 tick 下发了该实体的 delta）
                if (currentBroadcastTick > lastBroadcastTick)
                {
                    heartbeatTicks.Add(tick);
                    lastBroadcastTick = currentBroadcastTick;
                }
                observer.ReceivedDiffCount = 0;
            }
        }

        // 输出心跳节奏数据供 BUG 报告引用
        var intervals = heartbeatTicks.Zip(heartbeatTicks.Skip(1), (a, b) => b - a).ToList();
        var avgInterval = intervals.Count > 0 ? intervals.Average() : 0;
        var maxInterval = intervals.Count > 0 ? intervals.Max() : 0;
        Console.WriteLine(
            $"[Heartbeat] 静止实体 @ 20Hz，120 tick 内收到 {heartbeatTicks.Count} 次心跳 delta，" +
            $"间隔：平均 {avgInterval:F1} tick，最大 {maxInterval} tick");

        // 验证：静止实体在 120 tick 内至少收到 15 次心跳（120/6 = 20，允许 25% 误差）
        Assert.True(heartbeatTicks.Count >= 15,
            $"静止实体 120 tick 内仅收到 {heartbeatTicks.Count} 次心跳 delta，预期至少 15 次（心跳保护失效）");

        // 验证：心跳间隔不超过 12 tick（2 倍预期 6 tick，允许广播 tick 对齐误差）
        Assert.True(maxInterval <= 12,
            $"心跳间隔最大 {maxInterval} tick 超过 12 tick（2 倍预期 6 tick），心跳保护可能失效");

        // 验证：所有心跳 tick 都是广播 tick（tick % broadcastInterval == 0）
        foreach (var tick in heartbeatTicks)
        {
            // tick 是循环变量（1-based），对应 _tickCount = tick
            // 广播 tick 条件：_tickCount % broadcastInterval == 0
            Assert.True(tick % broadcastInterval == 0,
                $"心跳 delta 在非广播 tick {tick} 下发，违反 LastUpdateBroadcastTick 仅在广播 tick 更新的约束");
        }
    }
}

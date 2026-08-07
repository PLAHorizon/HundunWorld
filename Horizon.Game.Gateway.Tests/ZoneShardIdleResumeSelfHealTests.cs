using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Interface.World;
using MemoryPack;
using Orleans.TestingHost;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// P-F6/P-F9 运行时验证：复现用户报告的两类同步失效场景（真实 Orleans Silo 端到端）。
/// <list type="number">
///   <item>静止一段时间后恢复移动：权威位置应继续跟随输入，无 Correction 回弹；</item>
///   <item>实体被清理后输入恢复：P-F9 自愈重注册，实体以客户端上报位置重建并下发 Spawn 全量快照。</item>
/// </list>
/// </summary>
public class ZoneShardIdleResumeSelfHealTests : IAsyncLifetime
{
    private TestCluster? _cluster;
    private readonly ITestOutputHelper _output;

    public ZoneShardIdleResumeSelfHealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ZoneShardTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ZoneShardTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    /// <summary>解析 EntityDelta payload 的 observer，保留全部实体 delta 供断言。</summary>
    private sealed class DeltaCapturingObserver : IZoneShardFanoutObserver
    {
        public ConcurrentQueue<EntityDelta> Deltas { get; } = new();
        public ConcurrentQueue<SyncEvent> Events { get; } = new();

        public Task OnChunkDiffAsync(WorldChunkDiffPacket diff, IReadOnlyCollection<long> sessionIds)
        {
            try
            {
                if (diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta && diff.Payload is { Length: > 0 })
                {
                    var deltas = MemoryPackSerializer.Deserialize<EntityDelta[]>(diff.Payload);
                    if (deltas != null)
                        foreach (var d in deltas) Deltas.Enqueue(d);
                }
                else if (diff.PayloadType == WorldChunkDiffPayloadType.Event && diff.Payload is { Length: > 0 })
                {
                    var evt = MemoryPackSerializer.Deserialize<EventPacket>(diff.Payload);
                    if (evt?.Events != null)
                        foreach (var e in evt.Events) Events.Enqueue(e);
                }
            }
            catch { /* 解析失败忽略，断言会暴露缺失 */ }
            return Task.CompletedTask;
        }

        /// <summary>取指定实体最新一条 delta。</summary>
        public EntityDelta? LatestDeltaFor(ulong entityId)
        {
            EntityDelta? latest = null;
            foreach (var d in Deltas)
                if (d.EntityId == entityId) latest = d;
            return latest;
        }

        public int CorrectionEventCount => Events.Count(e => e.Kind == SyncEventKind.Correction);

        /// <summary>P-F10 同源诊断：输出每个校正包的原因/偏差/tick，定位误判来源。</summary>
        public string DescribeCorrections()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in Events)
            {
                if (e.Kind != SyncEventKind.Correction) continue;
                try
                {
                    var c = MemoryPackSerializer.Deserialize<Horizon.Game.Core.Sim.CorrectionPacket>(e.Payload);
                    sb.Append($"[Reason={c!.Reason}, Drift={c.DriftMeters:F2}m, LastProcTick={c.LastProcessedClientTick}, ServerTick={c.ServerTick}] ");
                }
                catch { sb.Append("[解析失败] "); }
            }
            return sb.Length == 0 ? "无" : sb.ToString();
        }
    }

    private async Task<(IZoneShardGrain Grain, DeltaCapturingObserver Observer, Guid SubId)> SetupAsync(long grainKey, long sessionId)
    {
        var zoneShard = _cluster!.GrainFactory.GetGrain<IZoneShardGrain>(grainKey);
        var observer = new DeltaCapturingObserver();
        var observerRef = _cluster.Client.CreateObjectReference<IZoneShardFanoutObserver>(observer);
        var subscriptionId = Guid.NewGuid();
        await zoneShard.SubscribeFanoutAsync(subscriptionId, observerRef);

        const float ecsZ = 8f;
        var chunkKey = WorldCoord.ToChunkMortonKey(0, 0, ecsZ);
        await zoneShard.SubscribeSessionAsync(sessionId, new[] { chunkKey });
        return (zoneShard, observer, subscriptionId);
    }

    /// <summary>构造沿 +X 移动一帧的输入（0.1m/tick @ MaxSpeed=6，与服务端回放一致）。</summary>
    private static InputPacket MoveInput(long clientTick, float fromX, float ecsZ) => new()
    {
        ClientTick = clientTick,
        MoveX = 1.0f,
        MoveY = 0f,
        MaxSpeed = 6f,
        CharacterId = 0,
        PredictedEndX = fromX + 0.1f,
        PredictedEndY = 0f,
        PredictedEndZ = ecsZ,
    };

    /// <summary>
    /// 场景 1：移动 → 静止 120 tick（2 秒）→ 恢复移动。
    /// 断言：恢复移动后权威位置继续跟随（不回弹/不停摆），全程零 Correction。
    /// </summary>
    [Fact]
    public async Task IdleThenResume_AuthoritativeTracks_NoCorrection()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(grainKey: 901, sessionId: 1);

        await zoneShard.RegisterEntityAsync(1, 0, ecsZ, 0);
        await zoneShard.TickAsync(1.0); // 全量基线

        // ── 阶段 1：移动 30 tick（0 → 3.0m）──
        long clientTick = 0;
        float x = 0f;
        for (int t = 0; t < 30; t++)
        {
            clientTick++;
            var input = MoveInput(clientTick, x, ecsZ);
            await zoneShard.SubmitInputAsync(1, input, input.PredictedEndX, input.PredictedEndY, input.PredictedEndZ);
            x = input.PredictedEndX;
            await zoneShard.TickAsync(1.0 + clientTick * (1.0 / 60.0));
        }

        var afterMove = observer.LatestDeltaFor(1);
        Assert.True(afterMove.HasValue, "移动阶段应收到实体 1 的 delta");
        Assert.True(afterMove!.Value.Transform.HasValue, "移动阶段 delta 应携带 Transform");
        Assert.True(Math.Abs(afterMove.Value.Transform!.Value.X - 3.0f) < 0.35f,
            $"移动阶段权威位置应≈3.0，实际 {afterMove.Value.Transform.Value.X:F2}");

        // ── 阶段 2：静止 120 tick（2 秒，无输入）──
        for (int t = 0; t < 120; t++)
        {
            clientTick++; // 客户端 tick 照常推进（与真实客户端一致），但不发送输入
            await zoneShard.TickAsync(1.0 + clientTick * (1.0 / 60.0));
        }

        // ── 阶段 3：恢复移动 30 tick（3.0 → 6.0m，ClientTick 存在大间隙，复现真实场景）──
        for (int t = 0; t < 30; t++)
        {
            clientTick++;
            var input = MoveInput(clientTick, x, ecsZ);
            await zoneShard.SubmitInputAsync(1, input, input.PredictedEndX, input.PredictedEndY, input.PredictedEndZ);
            x = input.PredictedEndX;
            await zoneShard.TickAsync(1.0 + clientTick * (1.0 / 60.0));
        }

        var afterResume = observer.LatestDeltaFor(1);
        Assert.True(afterResume.HasValue, "恢复移动后应收到实体 1 的 delta");
        Assert.True(afterResume!.Value.Transform.HasValue, "恢复移动后 delta 应携带 Transform");
        var finalX = afterResume.Value.Transform!.Value.X;

        _output.WriteLine($"[IdleResume] 移动后={afterMove.Value.Transform!.Value.X:F2}, 恢复移动后={finalX:F2}, Corrections={observer.CorrectionEventCount}");

        // 权威位置应继续跟随到 ≈6.0m（允许间隙吸附的收敛误差）——若停摆/回弹则远小于该值
        Assert.True(Math.Abs(finalX - 6.0f) < 0.5f,
            $"静止后恢复移动，权威位置应跟随到≈6.0m，实际 {finalX:F2}（回弹/停摆）");
        // 全程不应产生 Correction（回弹的直接来源）
        Assert.True(observer.CorrectionEventCount == 0,
            $"静止后恢复移动不应产生校正，实际 {observer.CorrectionEventCount} 个：{observer.DescribeCorrections()}");

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }

    /// <summary>
    /// 场景 2（P-F9）：实体被注销（模拟孤儿清理/租约过期）后客户端恢复输入。
    /// 断言：实体自愈重注册，后续快照包含该实体且位置≈客户端上报位置，Kind 为 Spawn（P-F6 全量自愈）。
    /// </summary>
    [Fact]
    public async Task EntityLost_ThenInput_SelfHealReregisters()
    {
        const float ecsZ = 8f;
        var (zoneShard, observer, subId) = await SetupAsync(grainKey: 902, sessionId: 1);

        await zoneShard.RegisterEntityAsync(1, 0, ecsZ, 0);
        await zoneShard.TickAsync(1.0);

        // 移动 10 tick 到 1.0m
        long clientTick = 0;
        float x = 0f;
        for (int t = 0; t < 10; t++)
        {
            clientTick++;
            var input = MoveInput(clientTick, x, ecsZ);
            await zoneShard.SubmitInputAsync(1, input, input.PredictedEndX, input.PredictedEndY, input.PredictedEndZ);
            x = input.PredictedEndX;
            await zoneShard.TickAsync(1.0 + clientTick * (1.0 / 60.0));
        }

        // 模拟实体被孤儿清理/注销（连接仍存活）
        await zoneShard.UnregisterEntityAsync(1);
        await zoneShard.TickAsync(1.0 + (++clientTick) * (1.0 / 60.0));

        // 客户端恢复输入：当前位置 5.0m（静止期间客户端已移动到新位置，直接上报 PredictedEnd）
        const float clientCurrentX = 5.0f;
        clientTick++;
        var resumeInput = new InputPacket
        {
            ClientTick = clientTick,
            MoveX = 1.0f,
            MoveY = 0f,
            MaxSpeed = 6f,
            PredictedEndX = clientCurrentX,
            PredictedEndY = 0f,
            PredictedEndZ = ecsZ,
        };
        await zoneShard.SubmitInputAsync(1, resumeInput, clientCurrentX, 0f, ecsZ);
        await zoneShard.TickAsync(1.0 + clientTick * (1.0 / 60.0));
        await zoneShard.TickAsync(1.0 + (++clientTick) * (1.0 / 60.0));

        // 断言：实体自愈重注册——快照重新包含实体 1，位置≈客户端上报位置
        var healedNullable = observer.LatestDeltaFor(1);
        Assert.True(healedNullable.HasValue, "P-F9 自愈后应重新收到实体 1 的 delta");
        var healed = healedNullable.Value;
        Assert.True(healed.Transform.HasValue, "自愈 delta 应携带 Transform");
        var healedX = healed.Transform!.Value.X;

        _output.WriteLine($"[SelfHeal] 自愈后位置={healedX:F2}（客户端上报 {clientCurrentX:F2}），Kind={healed.Kind}");

        Assert.True(Math.Abs(healedX - clientCurrentX) < 0.6f,
            $"P-F9 自愈后权威位置应≈客户端上报 {clientCurrentX:F2}，实际 {healedX:F2}");

        // P-F6：重注册触发强制全量快照，该实体的最新 delta Kind 应为 Spawn（客户端据此自愈视野）
        Assert.True(healed.Kind == EntityDeltaKind.Spawn,
            $"P-F9 重注册后应通过全量快照下发 Spawn Kind，实际 {healed.Kind}");

        await zoneShard.UnsubscribeFanoutAsync(subId);
    }
}

using System;
using System.Reflection;
using System.Threading.Tasks;
using Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Grains.World;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务 10.4 — baseline 重传请求端到端集成测试。
/// 验证完整链路：客户端缺失 baseline 收到 delta → 发送 BaselineResyncRequestPacket →
/// 服务端 _forceFullSnapshotNextTick=true → 下一 tick 下发全量快照 → 客户端恢复同步。
/// 被测代码：SnapshotApplySystem.cs:516（EnqueueResyncRequest）+ ZoneShardGrain.cs:1670（RequestBaselineResyncAsync）。
/// </summary>
public class BaselineResyncE2ETests : IDisposable
{
    /// <summary>每测试独立的接收队列：注入每个系统实例，消除并行测试对静态单例的串扰（阶段 6.4）。</summary>
    private readonly SnapshotReceiveBuffer _buffer = new();

    public BaselineResyncE2ETests()
    {
        SnapshotApplySystem.Diagnostics = null;
    }

    public void Dispose()
    {
        _buffer.ClearQueue();
    }

    // ─── 客户端侧：缺失 baseline 收到 delta → 入队重传请求 ───

    [Fact]
    public void Client_MissingBaseline_ReceivesDelta_EnqueuesResyncRequest()
    {
        // 客户端无 baseline（_lastAppliedSnapshot = null），收到 delta（BaselineTick=100）
        var system = CreateSystem();

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 期望客户端持有 baseline tick=100，但客户端无 baseline
            Deltas = Array.Empty<EntityDelta>(),
        };
        _buffer.Enqueue(delta);

        // 运行 SnapshotApplySystem.Update 消费快照
        var world = World.Create();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        // 应入队 BaselineResyncRequestPacket
        var request = system.TakePendingResyncRequest();
        Assert.NotNull(request);
        Assert.Equal(100L, request!.ExpectedBaselineTick);
        Assert.Equal(0L, request.ClientLastAppliedTick); // 客户端无 baseline → 0
    }

    [Fact]
    public void Client_MismatchedBaseline_ReceivesDelta_EnqueuesResyncRequestWithLastAppliedTick()
    {
        // 客户端持有 baseline tick=50，收到 delta 期望 baseline tick=100
        var system = CreateSystem();
        var existingBaseline = new SnapshotPacket
        {
            ServerTick = 50,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };
        system.OnFullSnapshotApplied(existingBaseline);

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 期望 100，客户端持有 50 → 不匹配
            Deltas = Array.Empty<EntityDelta>(),
        };
        _buffer.Enqueue(delta);

        var world = World.Create();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = system.TakePendingResyncRequest();
        Assert.NotNull(request);
        Assert.Equal(100L, request!.ExpectedBaselineTick);
        Assert.Equal(50L, request.ClientLastAppliedTick); // 客户端持有 50
    }

    [Fact]
    public void Client_MatchedBaseline_ReceivesDelta_NoResyncRequest()
    {
        // 客户端持有 baseline tick=100，收到 delta 期望 baseline tick=100 → 匹配，不请求重传
        var system = CreateSystem();
        var existingBaseline = new SnapshotPacket
        {
            ServerTick = 100,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };
        system.OnFullSnapshotApplied(existingBaseline);

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 匹配
            Deltas = Array.Empty<EntityDelta>(),
        };
        _buffer.Enqueue(delta);

        var world = World.Create();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = system.TakePendingResyncRequest();
        Assert.Null(request); // 匹配 → 无重传请求
    }

    // ─── 服务端侧：接收重传请求 → _forceFullSnapshotNextTick=true ───

    [Fact]
    public async Task Server_ReceivesResyncRequest_SetsForceFullSnapshotNextTick()
    {
        var grain = CreateGrain();
        const ulong entityId = 12345L;
        const long expectedTick = 100L;
        const long clientLastTick = 50L;

        // 初始 _forceFullSnapshotNextTick 应为 false
        Assert.False(GetForceFullSnapshotNextTick(grain));

        await grain.RequestBaselineResyncAsync(entityId, expectedTick, clientLastTick);

        // 收到请求后应置 true
        Assert.True(GetForceFullSnapshotNextTick(grain),
            "收到 baseline 重传请求后应设置 _forceFullSnapshotNextTick=true");
    }

    // ─── 端到端：客户端请求 → 服务端接收 → 强制全量 → 客户端恢复 ───

    [Fact]
    public async Task E2E_ClientResyncRequest_ServerForceFull_ClientRecovers()
    {
        // === 1. 客户端缺失 baseline，收到 delta → 入队重传请求 ===
        // 同一测试内多个系统实例共享同一 buffer（模拟同一客户端连接的状态连续性）。
        var clientSystem = CreateSystem();
        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100,
            Deltas = Array.Empty<EntityDelta>(),
        };
        _buffer.Enqueue(delta);

        var world = World.Create();
        try
        {
            clientSystem.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = clientSystem.TakePendingResyncRequest();
        Assert.NotNull(request);
        Assert.Equal(100L, request!.ExpectedBaselineTick);

        // === 2. 客户端请求上送服务端 → 服务端 _forceFullSnapshotNextTick=true ===
        var grain = CreateGrain();
        const ulong entityId = 12345L;
        await grain.RequestBaselineResyncAsync(entityId, request.ExpectedBaselineTick, request.ClientLastAppliedTick);
        Assert.True(GetForceFullSnapshotNextTick(grain));

        // === 3. 服务端下一 tick 强制下发全量快照（BaselineTick=0）===
        // 模拟服务端下发全量快照
        var fullSnapshot = new SnapshotPacket
        {
            ServerTick = 201,
            BaselineTick = 0, // 全量
            Deltas = Array.Empty<EntityDelta>(),
        };

        // === 4. 客户端收到全量快照 → 恢复同步（同一客户端系统实例应用全量，保持 baseline 状态）===
        _buffer.Enqueue(fullSnapshot);
        var world2 = World.Create();
        try
        {
            clientSystem.Update(world2, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world2);
        }

        // 客户端应已应用全量快照（_lastAppliedSnapshot 更新）
        // 此时再收到匹配的 delta 不应触发重传
        var followUpDelta = new SnapshotPacket
        {
            ServerTick = 202,
            BaselineTick = 201, // 匹配刚应用的全量快照
            Deltas = Array.Empty<EntityDelta>(),
        };
        _buffer.Enqueue(followUpDelta);

        var world3 = World.Create();
        try
        {
            clientSystem.Update(world3, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world3);
        }

        var noMoreRequest = clientSystem.TakePendingResyncRequest();
        Assert.Null(noMoreRequest); // 恢复同步，不再请求重传
    }

    // ─── 重传请求队列限流 16 ───

    [Fact]
    public void Client_ResyncRequestQueue_LimitedTo16()
    {
        // 队列上限 16，超过不再入队（避免队列爆炸）
        var system = CreateSystem();

        // 入队 20 个不匹配的 delta
        for (int i = 0; i < 20; i++)
        {
            var delta = new SnapshotPacket
            {
                ServerTick = 200 + i,
                BaselineTick = 100 + i, // 都不匹配
                Deltas = Array.Empty<EntityDelta>(),
            };
            _buffer.Enqueue(delta);

            var world = World.Create();
            try
            {
                system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        // 应最多取出 16 个请求
        int count = 0;
        while (system.TakePendingResyncRequest() != null)
        {
            count++;
        }
        Assert.True(count <= 16,
            $"重传请求队列应限流 16，实际入队 {count} 个");
        Assert.True(count > 0, "应至少入队一些请求");
    }

    // ─── 辅助方法 ───

    /// <summary>创建注入本测试独立 buffer 的系统实例（消除共享静态串扰）。</summary>
    private SnapshotApplySystem CreateSystem() => new(_buffer);

    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object);

        // 注入 mock IGrainContext 使 GetPrimaryKeyLong() 不抛异常
        GrainTestHarness.AttachGrainContext(grain);

        return grain;
    }

    private static bool GetForceFullSnapshotNextTick(ZoneShardGrain grain)
    {
        var field = typeof(ZoneShardGrain).GetField("_forceFullSnapshotNextTick", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)field!.GetValue(grain)!;
    }
}
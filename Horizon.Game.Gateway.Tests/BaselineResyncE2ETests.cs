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
    public BaselineResyncE2ETests()
    {
        // 清理静态状态
        SnapshotReceiveBuffer.Instance.ClearQueue();
        SnapshotApplySystem.ResetLastAppliedSnapshot();
        SnapshotApplySystem.Diagnostics = null;
    }

    public void Dispose()
    {
        SnapshotReceiveBuffer.Instance.ClearQueue();
        SnapshotApplySystem.ResetLastAppliedSnapshot();
    }

    // ─── 客户端侧：缺失 baseline 收到 delta → 入队重传请求 ───

    [Fact]
    public void Client_MissingBaseline_ReceivesDelta_EnqueuesResyncRequest()
    {
        // 客户端无 baseline（_lastAppliedSnapshot = null），收到 delta（BaselineTick=100）
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 期望客户端持有 baseline tick=100，但客户端无 baseline
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotReceiveBuffer.Instance.Enqueue(delta);

        // 运行 SnapshotApplySystem.Update 消费快照
        var world = World.Create();
        var system = new SnapshotApplySystem();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        // 应入队 BaselineResyncRequestPacket
        var request = SnapshotApplySystem.TakePendingResyncRequest();
        Assert.NotNull(request);
        Assert.Equal(100L, request!.ExpectedBaselineTick);
        Assert.Equal(0L, request.ClientLastAppliedTick); // 客户端无 baseline → 0
    }

    [Fact]
    public void Client_MismatchedBaseline_ReceivesDelta_EnqueuesResyncRequestWithLastAppliedTick()
    {
        // 客户端持有 baseline tick=50，收到 delta 期望 baseline tick=100
        var existingBaseline = new SnapshotPacket
        {
            ServerTick = 50,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotApplySystem.OnFullSnapshotApplied(existingBaseline);

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 期望 100，客户端持有 50 → 不匹配
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotReceiveBuffer.Instance.Enqueue(delta);

        var world = World.Create();
        var system = new SnapshotApplySystem();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = SnapshotApplySystem.TakePendingResyncRequest();
        Assert.NotNull(request);
        Assert.Equal(100L, request!.ExpectedBaselineTick);
        Assert.Equal(50L, request.ClientLastAppliedTick); // 客户端持有 50
    }

    [Fact]
    public void Client_MatchedBaseline_ReceivesDelta_NoResyncRequest()
    {
        // 客户端持有 baseline tick=100，收到 delta 期望 baseline tick=100 → 匹配，不请求重传
        var existingBaseline = new SnapshotPacket
        {
            ServerTick = 100,
            BaselineTick = 0,
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotApplySystem.OnFullSnapshotApplied(existingBaseline);

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100, // 匹配
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotReceiveBuffer.Instance.Enqueue(delta);

        var world = World.Create();
        var system = new SnapshotApplySystem();
        try
        {
            system.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = SnapshotApplySystem.TakePendingResyncRequest();
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
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        var delta = new SnapshotPacket
        {
            ServerTick = 200,
            BaselineTick = 100,
            Deltas = Array.Empty<EntityDelta>(),
        };
        SnapshotReceiveBuffer.Instance.Enqueue(delta);

        var world = World.Create();
        var clientSystem = new SnapshotApplySystem();
        try
        {
            clientSystem.Update(world, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world);
        }

        var request = SnapshotApplySystem.TakePendingResyncRequest();
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

        // === 4. 客户端收到全量快照 → 恢复同步 ===
        SnapshotReceiveBuffer.Instance.Enqueue(fullSnapshot);
        var world2 = World.Create();
        var clientSystem2 = new SnapshotApplySystem();
        try
        {
            clientSystem2.Update(world2, TimeSpan.FromSeconds(1.0 / 60.0));
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
        SnapshotReceiveBuffer.Instance.Enqueue(followUpDelta);

        var world3 = World.Create();
        var clientSystem3 = new SnapshotApplySystem();
        try
        {
            clientSystem3.Update(world3, TimeSpan.FromSeconds(1.0 / 60.0));
        }
        finally
        {
            World.Destroy(world3);
        }

        var noMoreRequest = SnapshotApplySystem.TakePendingResyncRequest();
        Assert.Null(noMoreRequest); // 恢复同步，不再请求重传
    }

    // ─── 重传请求队列限流 16 ───

    [Fact]
    public void Client_ResyncRequestQueue_LimitedTo16()
    {
        // 队列上限 16，超过不再入队（避免队列爆炸）
        SnapshotApplySystem.ResetLastAppliedSnapshot();

        // 入队 20 个不匹配的 delta
        for (int i = 0; i < 20; i++)
        {
            var delta = new SnapshotPacket
            {
                ServerTick = 200 + i,
                BaselineTick = 100 + i, // 都不匹配
                Deltas = Array.Empty<EntityDelta>(),
            };
            SnapshotReceiveBuffer.Instance.Enqueue(delta);

            var world = World.Create();
            var system = new SnapshotApplySystem();
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
        while (SnapshotApplySystem.TakePendingResyncRequest() != null)
        {
            count++;
        }
        Assert.True(count <= 16,
            $"重传请求队列应限流 16，实际入队 {count} 个");
        Assert.True(count > 0, "应至少入队一些请求");
    }

    // ─── 辅助方法 ───

    private static ZoneShardGrain CreateGrain()
    {
        var mockLogger = new Mock<ILogger<ZoneShardGrain>>();
        var mockState = new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(mockLogger.Object, mockState.Object);

        // 注入 mock IGrainContext 使 GetPrimaryKeyLong() 不抛异常
        var grainId = GrainId.Create(GrainType.Create("ZoneShard"), "1");
        var mockContext = new Mock<IGrainContext>();
        mockContext.SetupGet(c => c.GrainId).Returns(grainId);

        var contextField = typeof(Grain).GetField("<GrainContext>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        contextField?.SetValue(grain, mockContext.Object);

        return grain;
    }

    private static bool GetForceFullSnapshotNextTick(ZoneShardGrain grain)
    {
        var field = typeof(ZoneShardGrain).GetField("_forceFullSnapshotNextTick", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)field!.GetValue(grain)!;
    }
}
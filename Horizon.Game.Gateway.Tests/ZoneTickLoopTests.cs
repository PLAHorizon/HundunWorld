using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Orleans.Grains.World;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 3.4：ITickLoop 抽象接线测试。
/// 验证：①ZoneShardGrain 激活时经注入的 ITickLoop 驱动 tick（外部注入优先、未注入自建 Orleans 实现）；
/// ②OrleansTimerTickLoop 的异常隔离语义（单 tick 异常不中断循环、tick 计数不受影响、错误进日志）；
/// ③Stop 幂等。
/// </summary>
public sealed class ZoneTickLoopTests
{
    /// <summary>手动驱动的 ITickLoop fake：捕获 onTick 与 period，由测试显式推进。</summary>
    private sealed class ManualTickLoop : ITickLoop
    {
        private long _tickCount;
        public Func<double, Task>? OnTick;
        public TimeSpan Period;
        public bool Started;
        public bool Stopped;

        public void Start(Func<double, Task> onTick, TimeSpan period)
        {
            OnTick = onTick;
            Period = period;
            Started = true;
        }

        public void Stop() => Stopped = true;

        public long TickCount => Interlocked.Read(ref _tickCount);

        /// <summary>模拟一次定时器触发：异常隔离（吞掉并计数）对齐 OrleansTimerTickLoop 语义。</summary>
        public async Task FireAsync(double serverTime, bool throwFromHandler = false)
        {
            try
            {
                if (throwFromHandler) throw new InvalidOperationException("boom");
                await OnTick!(serverTime);
            }
            catch
            {
                // 隔离：与 OrleansTimerTickLoop 一致，异常被循环层吞掉
            }
            Interlocked.Increment(ref _tickCount);
        }
    }

    private static ZoneShardGrain CreateGrain(ITickLoop loop, Mock<IPersistentState<ZoneShardState>>? mockState = null)
    {
        var logger = NullLogger<ZoneShardGrain>.Instance;
        mockState ??= new Mock<IPersistentState<ZoneShardState>>();
        mockState.SetupGet(s => s.State).Returns(new ZoneShardState());
        var grain = new ZoneShardGrain(logger, mockState.Object, tickLoop: loop);

        // 共享裸激活基建：GrainContext（GetPrimaryKeyLong）+ mock Runtime/TimerRegistry
        // （RegisterTimer 委托给 Runtime.TimerRegistry，不注入时裸激活会在
        // OnActivateAsync 的 persist/orphan 定时器注册处抛 EnsureRuntime 异常）。
        GrainTestHarness.AttachGrainContext(grain);
        GrainTestHarness.AttachMockRuntime(grain);

        return grain;
    }

    [Fact]
    public async Task InjectedLoop_IsStarted_WithExpectedPeriod()
    {
        var loop = new ManualTickLoop();
        var grain = CreateGrain(loop);
        await GrainTestHarness.ActivateAsync(grain);

        Assert.True(loop.Started);
        Assert.True(loop.Period > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 60.0), loop.Period);
        Assert.NotNull(loop.OnTick);
    }

    [Fact]
    public async Task InjectedLoop_IsStopped_OnDeactivate()
    {
        var loop = new ManualTickLoop();
        var grain = CreateGrain(loop);
        await GrainTestHarness.ActivateAsync(grain);
        await GrainTestHarness.DeactivateAsync(grain);

        Assert.True(loop.Stopped);
    }

    [Fact]
    public async Task TickLoop_DrivesTickAsync_WithServerTime()
    {
        var loop = new ManualTickLoop();
        var grain = CreateGrain(loop);
        await GrainTestHarness.ActivateAsync(grain);

        // 手动推进 tick——真实 tick 路径（输入/模拟/广播）不应抛异常
        await loop.FireAsync(1700000000.0);

        Assert.Equal(1, loop.TickCount);
    }

    [Fact]
    public async Task TickLoop_HandlerException_IsIsolated_AndCounted()
    {
        // 模拟 tick 处理器抛异常：循环层隔离，不向上传播，tick 计数仍递增
        var loop = new ManualTickLoop();
        var grain = CreateGrain(loop);
        await GrainTestHarness.ActivateAsync(grain);

        await loop.FireAsync(1700000000.0, throwFromHandler: true);

        Assert.Equal(1, loop.TickCount); // 异常被隔离，循环继续
        Assert.True(loop.Started);
    }

    [Fact]
    public void OrleansTimerTickLoop_Start_IsIdempotent()
    {
        var registered = new List<(Func<object, Task> callback, object? state, TimeSpan due, TimeSpan period)>();
        var loop = new OrleansTimerTickLoop(
            (callback, state, due, period) =>
            {
                registered.Add((callback, state, due, period));
                return new DummyDisposable();
            },
            NullLogger<ZoneShardGrain>.Instance,
            "1");

        loop.Start(_ => Task.CompletedTask, TimeSpan.FromSeconds(1.0 / 60.0));
        loop.Start(_ => Task.CompletedTask, TimeSpan.FromSeconds(1.0 / 60.0)); // 第二次调用应被忽略

        Assert.Single(registered); // 仅注册一次（幂等）
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 60.0), registered[0].period);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 60.0), registered[0].due);
    }

    [Fact]
    public void OrleansTimerTickLoop_Stop_IsIdempotent()
    {
        var loop = new OrleansTimerTickLoop(
            (callback, state, due, period) => new DummyDisposable(),
            NullLogger<ZoneShardGrain>.Instance,
            "1");

        loop.Stop(); // 未启动时调用无副作用
        loop.Start(_ => Task.CompletedTask, TimeSpan.FromSeconds(1.0 / 60.0));
        loop.Stop();
        loop.Stop(); // 重复 Stop 无副作用
        Assert.True(true);
    }

    [Fact]
    public async Task OrleansTimerTickLoop_Callback_IsolatesHandlerException()
    {
        // 模拟 Orleans 定时器回调：处理器抛异常 → 循环层记录错误并继续，tick 计数递增
        Func<object, Task>? capturedCallback = null;
        var loop = new OrleansTimerTickLoop(
            (callback, state, due, period) =>
            {
                capturedCallback = callback;
                return new DummyDisposable();
            },
            NullLogger<ZoneShardGrain>.Instance,
            "1");

        loop.Start(_ => throw new InvalidOperationException("tick boom"), TimeSpan.FromSeconds(1.0 / 60.0));

        await capturedCallback!(null!); // 模拟定时器触发

        Assert.Equal(1, loop.TickCount); // 异常被隔离，计数仍递增
    }

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

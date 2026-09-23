using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// 固定步长 tick 循环抽象（阶段 3.4）。
/// <para>
/// 把「定时器驱动 → tick 处理器」的接线从 Grain 中解耦：Grain 只保留业务
/// <c>TickAsync</c>，循环的启动/停止/异常隔离/次数统计收敛到单一实现
/// （生产 <see cref="OrleansTimerTickLoop"/>，测试可注入手动驱动的 fake）。
/// </para>
/// <para>
/// <c>onTick</c> 接收当前 Unix 秒时间戳（与 Orleans 定时器回调的原时间源一致），
/// 由实现负责计算并传入——测试 fake 可注入受控时间而不依赖真实时钟。
/// </para>
/// </summary>
public interface ITickLoop
{
    /// <summary>启动循环：每 <paramref name="period"/> 调用一次 <paramref name="onTick"/>；幂等（已启动直接返回）。</summary>
    void Start(Func<double, Task> onTick, TimeSpan period);

    /// <summary>停止循环并释放定时器（幂等；未启动调用无副作用）。</summary>
    void Stop();

    /// <summary>当前已推进的 tick 数（诊断/可观测性）。</summary>
    long TickCount { get; }
}

/// <summary>
/// Orleans 定时器驱动的 <see cref="ITickLoop"/> 默认实现（阶段 3.4）。
/// </summary>
/// <remarks>
/// <c>Grain.RegisterTimer</c> 为 protected，外部类无法直接调用，故通过委托注入
/// <c>(callback, state, dueTime, period) → IDisposable</c>（由 Grain 在激活时传入
/// <c>RegisterTimer</c> 引用）。异常隔离在循环层完成：单次 tick 处理器异常记录错误日志
/// 并继续下一 tick（对齐原 Grain 内 try/catch 语义），tick 计数不受影响。
/// </remarks>
public sealed class OrleansTimerTickLoop : ITickLoop
{
    private readonly Func<Func<object, Task>, object?, TimeSpan, TimeSpan, IDisposable> _registerTimer;
    private readonly ILogger _logger;
    private readonly string _grainIdentity;
    private IDisposable? _timer;
    private long _tickCount;

    public OrleansTimerTickLoop(
        Func<Func<object, Task>, object?, TimeSpan, TimeSpan, IDisposable> registerTimer,
        ILogger logger,
        string grainIdentity)
    {
        _registerTimer = registerTimer ?? throw new ArgumentNullException(nameof(registerTimer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainIdentity = grainIdentity;
    }

    public void Start(Func<double, Task> onTick, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        if (_timer != null) return; // 幂等

        _timer = _registerTimer(
            async _ =>
            {
                try
                {
                    await onTick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ZoneShard {ShardId}: 定时器 TickAsync 异常。", _grainIdentity);
                }
                Interlocked.Increment(ref _tickCount);
            },
            null,
            period,
            period);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public long TickCount => Interlocked.Read(ref _tickCount);
}

using System;
using System.Collections.Generic;

namespace HundunWorld.Game.RemoteVisibility;

/// <summary>
/// 重连流程异常隔离包装：对重连事件处理器统一 try-catch 隔离与空引用防护，
/// 确保"重连成功"结论一经确立不被事件处理器异常推翻（spec 5.6.1 规则 4、5.6.3 异常 4）。
/// </summary>
public sealed class ReconnectFlowGuard
{
    /// <summary>被隔离的事件处理器异常计数（观测）。</summary>
    public long GuardedExceptionCount { get; private set; }

    /// <summary>注册的安全包装事件处理器。</summary>
    private readonly List<(string Stage, Action Handler)> _guardedHandlers = new();

    /// <summary>
    /// 注册一个安全包装的事件处理器（按注册顺序执行）。
    /// </summary>
    /// <param name="stage">事件阶段标识（用于可审计日志，如 "OnReconnectionSucceeded"）。</param>
    /// <param name="handler">事件处理器。</param>
    public void RegisterGuardedHandler(string stage, Action handler)
    {
        _guardedHandlers.Add((stage, handler ?? throw new ArgumentNullException(nameof(handler))));
    }

    /// <summary>
    /// 以安全包装方式触发全部已注册处理器：单个处理器异常被隔离并记录阶段日志，不向外传播。
    /// </summary>
    public void RaiseSafely()
    {
        foreach (var (stage, handler) in _guardedHandlers)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                GuardedExceptionCount++;
                // 异常记录含阶段与上下文的可审计日志（spec 4.4.6 重连异常可定位）后吞并。
                System.Diagnostics.Debug.WriteLine($"[ReconnectFlowGuard] 事件处理器异常被隔离（不影响重连成功结论）: Stage={stage}, Ex={ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 对 <see cref="HundunWorld.Game.Network.NetworkManager"/> 的访问做空引用防护的调用辅助。
    /// </summary>
    public static void SafeInvoke(string stage, Action? action)
    {
        if (action == null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReconnectFlowGuard] 空引用防护调用异常被隔离: Stage={stage}, Ex={ex.Message}");
        }
    }
}
using System;
using System.Collections.Generic;
using HundunWorld.Game.RemoteVisibility;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 重连流程异常隔离（ReconnectFlowGuard）单元测试：覆盖 spec 5.6.1 规则 4、5.6.3 异常 4。
/// </summary>
public class ReconnectFlowGuardTests
{
    [Fact]
    public void HandlerThrowsNre_IsIsolated_Counted()
    {
        var guard = new ReconnectFlowGuard();
        guard.RegisterGuardedHandler("StageA", () => throw new NullReferenceException("_client 为空"));

        guard.RaiseSafely();

        Assert.Equal(1, guard.GuardedExceptionCount);
    }

    [Fact]
    public void MultipleHandlers_OneThrows_OthersStillRun()
    {
        var guard = new ReconnectFlowGuard();
        var executed = new List<string>();

        guard.RegisterGuardedHandler("StageA", () => throw new NullReferenceException());
        guard.RegisterGuardedHandler("StageB", () => executed.Add("B"));
        guard.RegisterGuardedHandler("StageC", () => executed.Add("C"));

        guard.RaiseSafely();

        Assert.Equal(1, guard.GuardedExceptionCount);
        Assert.Equal(new[] { "B", "C" }, executed);
    }

    [Fact]
    public void NullClientAccess_SafeInvoke_DoesNotThrow()
    {
        // 模拟 _client == null 时的事件链访问（空引用防护）。
        ReconnectFlowGuard.SafeInvoke("OnReconnectionStateChanged", () =>
        {
            var client = (object?)null;
            _ = client != null && client.ToString() == "x";
        });

        Assert.True(true); // 未抛出即通过
    }

    [Fact]
    public void EmptyHandlerList_RaiseSafely_Noop()
    {
        var guard = new ReconnectFlowGuard();
        guard.RaiseSafely();
        Assert.Equal(0, guard.GuardedExceptionCount);
    }

    [Fact]
    public void RegisterNullHandler_ThrowsArgumentNull()
    {
        var guard = new ReconnectFlowGuard();
        Assert.Throws<ArgumentNullException>(() => guard.RegisterGuardedHandler("Stage", null!));
    }
}
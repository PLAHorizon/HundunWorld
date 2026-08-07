using Horizon.Game.GengDi.Core.Services.Call;
using Horizon.IM.Message.Enums;

namespace Horizon.Game.GengDi.Tests.Social;

/// <summary>
/// 通话状态机单元测试：覆盖发起、接听、拒绝、取消、挂断、乱序信令过滤等核心流转。
/// </summary>
public class CallStateMachineTests
{
    [Fact]
    public void StartOutgoing_FromIdle_TransitionsToOutgoingRinging()
    {
        var machine = new CallStateMachine();

        var accepted = machine.TryStartOutgoing("call-1");

        Assert.True(accepted);
        Assert.Equal(CallState.OutgoingRinging, machine.State);
        Assert.Equal("call-1", machine.CallId);
        Assert.True(machine.IsOutgoing);
    }

    [Fact]
    public void StartOutgoing_WhenBusy_IsRejected()
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");

        Assert.False(machine.TryStartOutgoing("call-2"));
        Assert.False(machine.TryReceiveOffer("call-3"));
        Assert.Equal("call-1", machine.CallId);
    }

    [Fact]
    public void OutgoingFlow_AcceptThenMediaReady_ReachesInCall()
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");

        Assert.True(machine.TryRemoteAccept());
        Assert.Equal(CallState.Connecting, machine.State);

        Assert.True(machine.TryEnterInCall());
        Assert.Equal(CallState.InCall, machine.State);
    }

    [Fact]
    public void IncomingFlow_Accept_ReachesConnecting()
    {
        var machine = new CallStateMachine();
        machine.TryReceiveOffer("call-1");
        Assert.Equal(CallState.IncomingRinging, machine.State);
        Assert.False(machine.IsOutgoing);

        Assert.True(machine.TryAccept());
        Assert.Equal(CallState.Connecting, machine.State);
        Assert.True(machine.TryEnterInCall());
    }

    [Fact]
    public void Accept_IsInvalidFromOutgoingRinging()
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");

        Assert.False(machine.TryAccept());
        Assert.Equal(CallState.OutgoingRinging, machine.State);
    }

    [Theory]
    [InlineData(IMCallSignalType.Accept, true)]
    [InlineData(IMCallSignalType.Reject, true)]
    [InlineData(IMCallSignalType.Busy, true)]
    [InlineData(IMCallSignalType.Cancel, true)]
    [InlineData(IMCallSignalType.Hangup, false)]
    [InlineData(IMCallSignalType.KeepAlive, false)]
    public void ShouldHandleSignal_DuringOutgoingRinging(IMCallSignalType signal, bool expected)
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");

        Assert.Equal(expected, machine.ShouldHandleSignal(signal));
    }

    [Theory]
    [InlineData(IMCallSignalType.Cancel, true)]
    [InlineData(IMCallSignalType.Timeout, true)]
    [InlineData(IMCallSignalType.Accept, false)]
    [InlineData(IMCallSignalType.MediaReady, false)]
    public void ShouldHandleSignal_DuringIncomingRinging(IMCallSignalType signal, bool expected)
    {
        var machine = new CallStateMachine();
        machine.TryReceiveOffer("call-1");

        Assert.Equal(expected, machine.ShouldHandleSignal(signal));
    }

    [Theory]
    [InlineData(IMCallSignalType.Hangup, true)]
    [InlineData(IMCallSignalType.MediaState, true)]
    [InlineData(IMCallSignalType.KeepAlive, true)]
    [InlineData(IMCallSignalType.Accept, false)]
    public void ShouldHandleSignal_DuringInCall(IMCallSignalType signal, bool expected)
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");
        machine.TryRemoteAccept();
        machine.TryEnterInCall();

        Assert.Equal(expected, machine.ShouldHandleSignal(signal));
    }

    [Fact]
    public void EnterInCall_WithoutConnecting_IsRejected()
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");

        Assert.False(machine.TryEnterInCall());
        Assert.Equal(CallState.OutgoingRinging, machine.State);
    }

    [Fact]
    public void BeginEnding_FromInCall_AllowsResetToIdle()
    {
        var machine = new CallStateMachine();
        machine.TryStartOutgoing("call-1");
        machine.TryRemoteAccept();
        machine.TryEnterInCall();

        Assert.True(machine.TryBeginEnding());
        Assert.Equal(CallState.Ending, machine.State);
        // 结束中阶段重复终结信令幂等受理
        Assert.True(machine.ShouldHandleSignal(IMCallSignalType.Hangup));
        Assert.False(machine.TryBeginEnding());

        machine.Reset();
        Assert.Equal(CallState.Idle, machine.State);
        Assert.Equal(string.Empty, machine.CallId);
    }

    [Fact]
    public void BeginEnding_FromIdle_IsRejected()
    {
        var machine = new CallStateMachine();

        Assert.False(machine.TryBeginEnding());
        Assert.Equal(CallState.Idle, machine.State);
    }

    [Fact]
    public void Reset_AllowsNewCallSession()
    {
        var machine = new CallStateMachine();
        machine.TryReceiveOffer("call-1");
        machine.Reset();

        Assert.True(machine.TryStartOutgoing("call-2"));
        Assert.True(machine.IsOutgoing);
        Assert.Equal("call-2", machine.CallId);
    }

    [Fact]
    public void ReceiveOffer_WithEmptyCallId_IsRejected()
    {
        var machine = new CallStateMachine();

        Assert.False(machine.TryReceiveOffer(""));
        Assert.False(machine.TryStartOutgoing(null));
        Assert.Equal(CallState.Idle, machine.State);
    }
}

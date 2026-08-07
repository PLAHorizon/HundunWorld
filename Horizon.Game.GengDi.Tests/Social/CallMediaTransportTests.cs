using System.Security.Cryptography;

using Horizon.Game.GengDi.Core.Services.Call;

namespace Horizon.Game.GengDi.Tests.Social;

/// <summary>
/// 通话媒体传输层测试：验证音频帧、视频分片重组与会话ID过滤（通过本机回环 UDP）。
/// </summary>
public class CallMediaTransportTests
{
    [Fact]
    public async Task AudioChunk_RoundTripsBetweenPeers()
    {
        using var caller = new CallMediaTransport("call-audio-1");
        using var callee = new CallMediaTransport("call-audio-1");

        caller.SetRemoteEndpoint(callee.LocalEndpoint);
        callee.SetRemoteEndpoint(caller.LocalEndpoint);

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        callee.AudioChunkReceived += pcm => received.TrySetResult(pcm);

        var payload = new byte[640];
        RandomNumberGenerator.Fill(payload);
        caller.SendAudio(payload);

        var completed = await Task.WhenAny(received.Task, Task.Delay(5000));
        Assert.True(completed == received.Task, "未在超时时间内收到远端音频包");
        Assert.Equal(payload, await received.Task);
        Assert.True(callee.LastPacketReceivedMs > 0);
    }

    [Fact]
    public async Task LargeVideoFrame_IsFragmentedAndReassembled()
    {
        using var caller = new CallMediaTransport("call-video-1");
        using var callee = new CallMediaTransport("call-video-1");

        caller.SetRemoteEndpoint(callee.LocalEndpoint);
        callee.SetRemoteEndpoint(caller.LocalEndpoint);

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        callee.VideoFrameReceived += frame => received.TrySetResult(frame);

        // 构造约 8KB 的"JPEG"帧（超出单个 UDP 分片容量，强制走分片/重组路径）
        var jpeg = new byte[8 * 1024];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        RandomNumberGenerator.Fill(jpeg.AsSpan(2));

        Assert.True(caller.SendVideoFrame(jpeg));

        var completed = await Task.WhenAny(received.Task, Task.Delay(5000));
        Assert.True(completed == received.Task, "未在超时时间内收到重组后的视频帧");
        Assert.Equal(jpeg, await received.Task);
    }

    [Fact]
    public async Task PacketsFromDifferentCall_AreIgnored()
    {
        using var caller = new CallMediaTransport("call-x");
        using var callee = new CallMediaTransport("call-y"); // 会话ID不一致

        callee.SetRemoteEndpoint(caller.LocalEndpoint);
        // caller 不设置远端端点之外的过滤：向 callee 直接发包
        caller.SetRemoteEndpoint(callee.LocalEndpoint);

        var audioReceived = false;
        callee.AudioChunkReceived += _ => audioReceived = true;

        caller.SendAudio(new byte[320]);

        await Task.Delay(1000);
        Assert.False(audioReceived, "不同会话ID的媒体包不应被受理");
        Assert.Equal(0, callee.LastPacketReceivedMs);
    }

    [Fact]
    public void SendVideoFrame_WithoutRemoteEndpoint_ReturnsFalse()
    {
        using var transport = new CallMediaTransport("call-no-remote");

        Assert.False(transport.HasRemoteEndpoint);
        Assert.False(transport.SendVideoFrame(new byte[] { 0xFF, 0xD8, 0x00 }));
    }

    [Fact]
    public void ResolveLocalMediaAddress_NeverReturnsNull()
    {
        var address = CallMediaTransport.ResolveLocalMediaAddress();

        Assert.NotNull(address);
        Assert.True(address.ToString().Length > 0);
    }

    [Fact]
    public void CallIdHash_IsStableAndDistinct()
    {
        var hashA1 = CallMediaTransport.ComputeCallIdHash("call-1");
        var hashA2 = CallMediaTransport.ComputeCallIdHash("call-1");
        var hashB = CallMediaTransport.ComputeCallIdHash("call-2");

        Assert.Equal(hashA1, hashA2);
        Assert.NotEqual(hashA1, hashB);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 5.1.2 — ConnectionManager 四个广播入口（全量/谓词/用户组/属性）收敛为
/// <see cref="ConnectionManager"/> 的单一 <c>BroadcastCoreAsync</c> 后，行为与统计口径
/// 保持不变的合并后测试：在线过滤、并发发送、统计计数（按候选连接数）、空集安全。
/// </summary>
public class ConnectionManagerBroadcastTests
{
    private static readonly byte[] Payload = new byte[] { 1, 2, 3, 4 };

    private static ConnectionManager CreateManager()
        => new(
            NullLogger<ConnectionManager>.Instance,
            new StaticOptionsMonitor<GatewayOptions>(new GatewayOptions()));

    private static Mock<IGameConnection> CreateConnection(
        string id, long? userId, bool connected, Dictionary<string, object>? properties = null)
    {
        var connection = new Mock<IGameConnection>();
        connection.SetupGet(c => c.ConnectionId).Returns(id);
        connection.SetupGet(c => c.UserId).Returns(userId);
        connection.SetupGet(c => c.IsConnected).Returns(connected);
        connection.SetupGet(c => c.Properties).Returns(properties ?? new Dictionary<string, object>());
        connection.Setup(c => c.SendAsync(It.IsAny<byte[]>())).Returns(Task.CompletedTask);
        return connection;
    }

    private static async Task<ConnectionManager> CreateManagerWithConnections(
        params Mock<IGameConnection>[] connections)
    {
        var manager = CreateManager();
        foreach (var connection in connections)
        {
            Assert.True(await manager.AddConnectionAsync(connection.Object));
        }
        return manager;
    }

    [Fact]
    public async Task BroadcastAsync_SendsToAllOnlineConnections_SkipsOffline()
    {
        var online1 = CreateConnection("c-1", userId: 10, connected: true);
        var online2 = CreateConnection("c-2", userId: 20, connected: true);
        var offline = CreateConnection("c-3", userId: 30, connected: false);
        var manager = await CreateManagerWithConnections(online1, online2, offline);

        await manager.BroadcastAsync(Payload);

        online1.Verify(c => c.SendAsync(It.Is<byte[]>(b => b.Length == Payload.Length)), Times.Once);
        online2.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Once);
        offline.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Never);

        // 统计按候选连接数（含离线，与原实现一致）
        var stats = manager.GetNetworkStatistics();
        Assert.Equal(3, stats.MessagesSent);
        Assert.Equal(Payload.Length * 3, stats.BytesSent);
    }

    [Fact]
    public async Task BroadcastAsync_WithPredicate_FiltersConnections()
    {
        var connA = CreateConnection("c-a", userId: 10, connected: true);
        var connB = CreateConnection("c-b", userId: 20, connected: true);
        var manager = await CreateManagerWithConnections(connA, connB);

        await manager.BroadcastAsync(Payload, c => c.UserId == 20);

        connA.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Never);
        connB.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Once);

        var stats = manager.GetNetworkStatistics();
        Assert.Equal(1, stats.MessagesSent);
        Assert.Equal(Payload.Length, stats.BytesSent);
    }

    [Fact]
    public async Task BroadcastToUserGroupAsync_SendsOnlyToMatchingUsers()
    {
        var connA = CreateConnection("c-a", userId: 100, connected: true);
        var connB = CreateConnection("c-b", userId: 200, connected: true);
        var connC = CreateConnection("c-c", userId: 300, connected: true);
        var manager = await CreateManagerWithConnections(connA, connB, connC);

        await manager.BroadcastToUserGroupAsync(Payload, new long[] { 100, 300 });

        connA.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Once);
        connB.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Never);
        connC.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Once);

        var stats = manager.GetNetworkStatistics();
        Assert.Equal(2, stats.MessagesSent);
        Assert.Equal(Payload.Length * 2, stats.BytesSent);
    }

    [Fact]
    public async Task BroadcastByPropertyAsync_FiltersByConnectionProperty()
    {
        var connA = CreateConnection("c-a", userId: 10, connected: true,
            properties: new Dictionary<string, object> { ["channel"] = "global" });
        var connB = CreateConnection("c-b", userId: 20, connected: true,
            properties: new Dictionary<string, object> { ["channel"] = "guild" });
        var manager = await CreateManagerWithConnections(connA, connB);

        await manager.BroadcastByPropertyAsync(Payload, props => props.TryGetValue("channel", out var v) && Equals(v, "global"));

        connA.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Once);
        connB.Verify(c => c.SendAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task Broadcast_WithNoConnections_IsSafe()
    {
        var manager = CreateManager();

        await manager.BroadcastAsync(Payload);
        await manager.BroadcastAsync(Payload, _ => true);
        await manager.BroadcastToUserGroupAsync(Payload, new long[] { 1 });
        await manager.BroadcastByPropertyAsync(Payload, _ => true);

        Assert.Equal(0, manager.GetNetworkStatistics().MessagesSent);
    }

    private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public StaticOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}

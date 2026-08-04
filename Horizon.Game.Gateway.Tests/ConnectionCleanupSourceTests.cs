using System;
using Horizon.Game.Gateway.Configuration;
using Horizon.Game.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 5.2 — 清理来源枚举化与统计一致性测试（spec 4.4.1 / 6.3）。
/// </summary>
public class ConnectionCleanupSourceTests
{
    private static ConnectionManager CreateManager()
    {
        var governanceMonitor = new Mock<IOptionsMonitor<ConnectionGovernanceOptions>>();
        governanceMonitor.Setup(m => m.CurrentValue).Returns(new ConnectionGovernanceOptions());
        var gatewayMonitor = new Mock<IOptionsMonitor<GatewayOptions>>();
        gatewayMonitor.Setup(m => m.CurrentValue).Returns(new GatewayOptions { MaxConnections = 10000 });
        return new ConnectionManager(
            NullLogger<ConnectionManager>.Instance,
            gatewayMonitor.Object,
            governanceMonitor.Object);
    }

    [Fact]
    public void Enum_HasExpectedMembers()
    {
        Assert.Equal("FirstPacketTimeout", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.FirstPacketTimeout));
        Assert.Equal("IdleTimeout", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.IdleTimeout));
        Assert.Equal("Corrupted", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.Corrupted));
        Assert.Equal("ClosedEvent", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.ClosedEvent));
        Assert.Equal("ConnectionLimit", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.ConnectionLimit));
        Assert.Equal("PerIpLimit", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.PerIpLimit));
        Assert.Equal("PerUserLimit", Enum.GetName(typeof(ConnectionCleanupSource), ConnectionCleanupSource.PerUserLimit));
    }

    [Fact]
    public void RecordCleanup_FirstPacketTimeout_IncrementsGhostCount()
    {
        var manager = CreateManager();
        manager.RecordCleanup(ConnectionCleanupSource.FirstPacketTimeout);
        manager.RecordCleanup(ConnectionCleanupSource.FirstPacketTimeout);
        Assert.Equal(2, manager.GetStatistics().GhostConnectionCleanupCount);
    }

    [Fact]
    public void RecordCleanup_Corrupted_IncrementsCorruptedCount()
    {
        var manager = CreateManager();
        manager.RecordCleanup(ConnectionCleanupSource.Corrupted);
        Assert.Equal(1, manager.GetStatistics().CorruptedConnectionCount);
    }

    [Fact]
    public void RecordCleanup_RejectSources_IncrementDuplicateRejectedCount()
    {
        var manager = CreateManager();
        manager.RecordCleanup(ConnectionCleanupSource.ConnectionLimit);
        manager.RecordCleanup(ConnectionCleanupSource.PerIpLimit);
        manager.RecordCleanup(ConnectionCleanupSource.PerUserLimit);
        Assert.Equal(3, manager.GetStatistics().DuplicateConnectionRejectedCount);
    }

    [Fact]
    public void RecordCleanup_IdleTimeout_DoesNotIncrementGhostOrCorrupted()
    {
        var manager = CreateManager();
        manager.RecordCleanup(ConnectionCleanupSource.IdleTimeout);
        manager.RecordCleanup(ConnectionCleanupSource.ClosedEvent);
        Assert.Equal(0, manager.GetStatistics().GhostConnectionCleanupCount);
        Assert.Equal(0, manager.GetStatistics().CorruptedConnectionCount);
        Assert.Equal(0, manager.GetStatistics().DuplicateConnectionRejectedCount);
    }

    [Fact]
    public void Statistics_Fields_AreExposed()
    {
        var manager = CreateManager();
        var stats = manager.GetStatistics();
        // 新字段默认 0，可读。
        Assert.Equal(0, stats.GhostConnectionCleanupCount);
        Assert.Equal(0, stats.CorruptedConnectionCount);
        Assert.Equal(0, stats.DuplicateConnectionRejectedCount);
        Assert.Equal(0, stats.UnboundConnectionCount);
    }
}
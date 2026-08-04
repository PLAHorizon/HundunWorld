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
/// 任务组 5.1 — 服务端连接数约束与重复连接检测单元测试（spec 4.3.1 / 4.4.3）。
/// </summary>
public class ConnectionGovernanceTests
{
    private sealed class FakeConnection : IGameConnection
    {
        public string ConnectionId { get; set; } = "";
        public long? UserId { get; set; }
        public string RemoteAddress { get; set; } = "127.0.0.1";
        public DateTime ConnectedTime { get; set; } = DateTime.UtcNow;
        public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;
        public bool IsConnected { get; set; } = true;
        public bool IsAuthenticated { get; set; }
        public string AuthToken { get; set; } = "";
        public Dictionary<string, object> Properties { get; } = new();
        public event EventHandler<ConnectionClosedEventArgs>? Closed;

        public void SetProperty(string key, object value) => Properties[key] = value;
        public object? GetProperty(string key) => Properties.TryGetValue(key, out var v) ? v : null;
        public bool RemoveProperty(string key) => Properties.Remove(key);
        public Task SendAsync(byte[] data) => Task.CompletedTask;
        public Task CloseAsync(string reason = "") => Task.CompletedTask;
    }

    private static ConnectionManager CreateManager(ConnectionGovernanceOptions options)
    {
        var governanceMonitor = new Mock<IOptionsMonitor<ConnectionGovernanceOptions>>();
        governanceMonitor.Setup(m => m.CurrentValue).Returns(options);
        var gatewayMonitor = new Mock<IOptionsMonitor<GatewayOptions>>();
        gatewayMonitor.Setup(m => m.CurrentValue).Returns(new GatewayOptions { MaxConnections = 10000 });
        return new ConnectionManager(
            NullLogger<ConnectionManager>.Instance,
            gatewayMonitor.Object,
            governanceMonitor.Object);
    }

    // ── 每 IP 上限 ──────────────────────────────────────────────────────

    [Fact]
    public async Task PerIpLimit_5thConnection_Rejected()
    {
        var manager = CreateManager(new ConnectionGovernanceOptions { MaxConnectionsPerIp = 4 });

        for (int i = 0; i < 4; i++)
        {
            var conn = new FakeConnection { ConnectionId = $"ip-{i}", RemoteAddress = "192.168.1.10" };
            var reason = await manager.TryAcquireConnectionSlotAsync(conn, conn.RemoteAddress, null);
            Assert.Null(reason); // 前 4 条接受
            Assert.True(await manager.AddConnectionAsync(conn));
        }

        // 第 5 条同 IP 连接被拒。
        var fifth = new FakeConnection { ConnectionId = "ip-5", RemoteAddress = "192.168.1.10" };
        var reject = await manager.TryAcquireConnectionSlotAsync(fifth, fifth.RemoteAddress, null);
        Assert.NotNull(reject);
        Assert.Contains("每IP", reject!);
        Assert.Equal(4, manager.GetActiveConnectionCountByIp("192.168.1.10"));
    }

    // ── 每用户上限 ─────────────────────────────────────────────────────

    [Fact]
    public async Task PerUserLimit_2ndConnectionForSameUser_Rejected()
    {
        var manager = CreateManager(new ConnectionGovernanceOptions { MaxConnectionsPerUser = 1 });

        var conn1 = new FakeConnection { ConnectionId = "user-1", RemoteAddress = "192.168.1.20" };
        Assert.Null(await manager.TryAcquireConnectionSlotAsync(conn1, conn1.RemoteAddress, 100L));
        Assert.True(await manager.AddConnectionAsync(conn1));
        // 角色进入游戏后建立"用户会话→连接"映射（与既有 RegisterCharacter 语义一致）。
        manager.RegisterCharacter(100L, conn1);

        // 同用户第 2 条连接被拒（保留旧连接）。
        var conn2 = new FakeConnection { ConnectionId = "user-2", RemoteAddress = "192.168.1.21" };
        var reject = await manager.TryAcquireConnectionSlotAsync(conn2, conn2.RemoteAddress, 100L);
        Assert.NotNull(reject);
        Assert.Contains("每用户", reject!);

        // 既有业务连接不受影响。
        Assert.NotNull(manager.GetConnectionByUserId(100L));
    }

    // ── 全局上限 ───────────────────────────────────────────────────────

    [Fact]
    public async Task GlobalLimit_Exceeded_Rejected()
    {
        var manager = CreateManager(new ConnectionGovernanceOptions { MaxConnections = 2 });

        var conn1 = new FakeConnection { ConnectionId = "g-1", RemoteAddress = "10.0.0.1" };
        Assert.Null(await manager.TryAcquireConnectionSlotAsync(conn1, conn1.RemoteAddress, null));
        Assert.True(await manager.AddConnectionAsync(conn1));

        var conn2 = new FakeConnection { ConnectionId = "g-2", RemoteAddress = "10.0.0.2" };
        Assert.Null(await manager.TryAcquireConnectionSlotAsync(conn2, conn2.RemoteAddress, null));
        Assert.True(await manager.AddConnectionAsync(conn2));

        var conn3 = new FakeConnection { ConnectionId = "g-3", RemoteAddress = "10.0.0.3" };
        var reject = await manager.TryAcquireConnectionSlotAsync(conn3, conn3.RemoteAddress, null);
        Assert.NotNull(reject);
    }

    // ── 拒绝后既有连接不受影响 + 统计 ──────────────────────────────────

    [Fact]
    public async Task Rejection_IncrementsDuplicateRejectedCount()
    {
        var manager = CreateManager(new ConnectionGovernanceOptions { MaxConnectionsPerIp = 1 });

        var conn1 = new FakeConnection { ConnectionId = "r-1", RemoteAddress = "192.168.1.30" };
        Assert.True(await manager.AddConnectionAsync(conn1));

        // 同 IP 第 2 条连接被拒。
        var conn2 = new FakeConnection { ConnectionId = "r-2", RemoteAddress = "192.168.1.30" };
        var reject = await manager.TryAcquireConnectionSlotAsync(conn2, conn2.RemoteAddress, 2L);
        Assert.NotNull(reject);

        var stats = manager.GetStatistics();
        Assert.True(stats.DuplicateConnectionRejectedCount >= 1);
        Assert.Equal(1, stats.ActiveConnections);
    }

    // ── 治理配置校验器 ────────────────────────────────────────────────

    [Fact]
    public void Validator_InvalidPerIpLessThanPerUser_FallsBackToDefaults()
    {
        var result = ConnectionGovernanceOptionsValidator.Validate(
            new ConnectionGovernanceOptions { MaxConnectionsPerIp = 2, MaxConnectionsPerUser = 3 });
        Assert.Equal(4, result.MaxConnectionsPerIp);
        Assert.Equal(1, result.MaxConnectionsPerUser);
    }

    [Fact]
    public void Validator_NullOptions_ReturnsDefaults()
    {
        var result = ConnectionGovernanceOptionsValidator.Validate(null);
        Assert.Equal(5, result.FirstPacketTimeoutSeconds);
        Assert.Equal(30, result.IdleTimeoutSeconds);
        Assert.Equal(10000, result.MaxConnections);
        Assert.Equal(4, result.MaxConnectionsPerIp);
        Assert.Equal(1, result.MaxConnectionsPerUser);
        Assert.Equal(15, result.DespawnGracePeriodSeconds);
    }

    [Fact]
    public void Validator_NonPositiveValues_FallsBackToDefaults()
    {
        var result = ConnectionGovernanceOptionsValidator.Validate(
            new ConnectionGovernanceOptions
            {
                FirstPacketTimeoutSeconds = 0,
                IdleTimeoutSeconds = -5,
                MaxConnections = 0,
                DespawnGracePeriodSeconds = 0,
            });
        Assert.Equal(5, result.FirstPacketTimeoutSeconds);
        Assert.Equal(30, result.IdleTimeoutSeconds);
        Assert.Equal(10000, result.MaxConnections);
        Assert.Equal(15, result.DespawnGracePeriodSeconds);
    }
}
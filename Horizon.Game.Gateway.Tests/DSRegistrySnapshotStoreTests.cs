using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Gateway.Services;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 2.5+ — DSRegistry 快照存储序列化契约测试。
/// <para>
/// 覆盖 <see cref="DSRegistrySnapshotStore.Serialize"/> / <see cref="DSRegistrySnapshotStore.Deserialize"/>
/// 的往返一致性（不含 Redis：静态纯函数可直接单测）。损坏输入 → null（不抛异常）。
/// </para>
/// </summary>
public class DSRegistrySnapshotStoreTests
{
    private static List<DsEndpointInfo> SampleEntries()
    {
        var now = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        return new List<DsEndpointInfo>
        {
            new("ds-1", new long[] { 0, 1 }, "127.0.0.1", 5001, now.AddSeconds(-10)),
            new("ds-2", new long[] { 2 }, "10.0.0.7", 7777, now.AddSeconds(-5)),
        };
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_PreservesAllFields()
    {
        var entries = SampleEntries();
        var json = DSRegistrySnapshotStore.Serialize(entries);

        var restored = DSRegistrySnapshotStore.Deserialize(json);
        Assert.NotNull(restored);
        Assert.Equal(2, restored.Count);

        var e1 = restored.Single(e => e.DsId == "ds-1");
        Assert.Equal(new long[] { 0, 1 }, e1.ShardIds);
        Assert.Equal("127.0.0.1", e1.Host);
        Assert.Equal(5001, e1.Port);
        Assert.Equal(entries[0].LastHeartbeatUtc, e1.LastHeartbeatUtc); // 时间戳精确往返（快照恢复依赖它判定存活）

        var e2 = restored.Single(e => e.DsId == "ds-2");
        Assert.Equal(new long[] { 2 }, e2.ShardIds);
        Assert.Equal("10.0.0.7", e2.Host);
        Assert.Equal(7777, e2.Port);
        Assert.Equal(entries[1].LastHeartbeatUtc, e2.LastHeartbeatUtc);
    }

    [Fact]
    public void Deserialize_EmptyList_RoundTrips()
    {
        var json = DSRegistrySnapshotStore.Serialize(new List<DsEndpointInfo>());
        var restored = DSRegistrySnapshotStore.Deserialize(json);
        Assert.NotNull(restored);
        Assert.Empty(restored);
    }

    [Fact]
    public void Deserialize_CorruptJson_ReturnsNull()
    {
        Assert.Null(DSRegistrySnapshotStore.Deserialize("{not valid json"));
        Assert.Null(DSRegistrySnapshotStore.Deserialize(""));
        Assert.Null(DSRegistrySnapshotStore.Deserialize("   "));
    }

    [Fact]
    public void Deserialize_UnknownFields_Tolerated()
    {
        // 向前兼容：新增字段的旧快照仍可反序列化（多余字段被忽略）。
        var json = "[{\"DsId\":\"ds-1\",\"ShardIds\":[0],\"Host\":\"127.0.0.1\",\"Port\":5001," +
                   "\"LastHeartbeatUtc\":\"2026-08-11T00:00:00+00:00\",\"FutureField\":42}]";
        var restored = DSRegistrySnapshotStore.Deserialize(json);
        Assert.NotNull(restored);
        var e = Assert.Single(restored);
        Assert.Equal("ds-1", e.DsId);
        Assert.Equal(new long[] { 0 }, e.ShardIds);
    }

    [Fact]
    public void RestoreSnapshot_NullEntries_Throws()
    {
        var registry = new DSRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.RestoreSnapshot(null!));
    }
}

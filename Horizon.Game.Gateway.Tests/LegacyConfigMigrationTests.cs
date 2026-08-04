using System;
using Horizon.Game.ECS.Arch.Configuration;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 5.2 — 旧配置键迁移单元测试（spec 4.5.2 / design.md 2.2.2.5）。
/// </summary>
public class LegacyConfigMigrationTests
{
    [Fact]
    public void Migrate_PositionCorrectionThreshold_SameValue()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "PositionCorrectionThreshold", 0.5f, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("ReconciliationSystem.CorrectionThreshold", newKey);
        Assert.Equal(0.5f, migratedValue);
    }

    [Fact]
    public void Migrate_PositionCorrectionThreshold_Double_ConvertsToFloat()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "PositionCorrectionThreshold", 0.75d, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("ReconciliationSystem.CorrectionThreshold", newKey);
        Assert.Equal(0.75f, migratedValue);
    }

    [Fact]
    public void Migrate_NetworkUpdateRate_ToNormalSnapshotHz()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "NetworkUpdateRate", 20, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("BandwidthBudgetOptions.NormalSnapshotHz", newKey);
        Assert.Equal(20, migratedValue);
    }

    [Fact]
    public void Migrate_InterpolationDelay_ToAdaptiveWindow()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "NetworkSyncManager.InterpolationDelay", 0.1f, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.StartsWith("SnapshotApplySystem.AdaptiveDelay", newKey);
        var (min, max) = ((float, float))migratedValue!;
        Assert.True(min > 0f);
        Assert.True(max > min);
    }

    [Fact]
    public void Migrate_NpcNearSyncInterval_ToNearSnapshotHz()
    {
        // 旧键为"间隔毫秒"（50ms）→ 新键为"频率 Hz"（20Hz）。
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "NpcSyncManager.NearSyncInterval", 50, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("InterestGradeOptions.NearSnapshotHz", newKey);
        Assert.Equal(20, migratedValue);
    }

    [Fact]
    public void Migrate_NpcMidSyncInterval_ToMidSnapshotHz()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "NpcSyncManager.MidSyncInterval", 100, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("InterestGradeOptions.MidSnapshotHz", newKey);
        Assert.Equal(10, migratedValue);
    }

    [Fact]
    public void Migrate_NpcFarSyncInterval_ToFarSnapshotHz()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "NpcSyncManager.FarSyncInterval", 200, out var newKey, out var migratedValue);

        Assert.True(ok);
        Assert.Equal("InterestGradeOptions.FarSnapshotHz", newKey);
        Assert.Equal(5, migratedValue);
    }

    [Fact]
    public void Migrate_UnknownKey_ReturnsFalse_NoThrow()
    {
        var ok = LegacyConfigMigration.TryMigrateLegacyKey(
            "Unknown.Config.Key", 123, out var newKey, out var migratedValue);

        Assert.False(ok);
        Assert.Equal(string.Empty, newKey);
        Assert.Equal(123, migratedValue);
    }

    [Fact]
    public void Migrate_NullOrEmptyKey_ReturnsFalse()
    {
        Assert.False(LegacyConfigMigration.TryMigrateLegacyKey(null, 1, out _, out _));
        Assert.False(LegacyConfigMigration.TryMigrateLegacyKey(string.Empty, 1, out _, out _));
    }
}
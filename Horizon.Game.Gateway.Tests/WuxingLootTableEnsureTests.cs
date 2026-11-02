using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Orleans.Interface.World;
using Horizon.Orleans.Silo.Startup;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 五行域掉落表装配的"缺则补填"口径。
/// <para>
/// <b>为什么需要自愈</b>：<c>LootTableGrain</c> 把配置放在普通字段里（没有 <c>IPersistentState</c>），
/// 启动装配的那一份只活在一次激活中；Orleans 回收空闲激活后配置就没了，表现是
/// "刷怪什么都不掉"，而 Silo 的装配日志与 DS 的预热计数两端都各自正常，中间没有任何一行报错。
/// 2026-10-04 在真集群上逐个读回 1..5 域，五张表全空，正是这个机制。
/// </para>
/// <para>
/// 因此周期装配必须守住一条线：**已有数据的域绝不覆盖**。覆盖会把"库里的真值"换成
/// "本周期读到的那份"，一旦两边分叉（比如有人在库里调过掉落率），自愈反而在抹掉生产改动，
/// 而且抹得悄无声息。
/// </para>
/// </summary>
public class WuxingLootTableEnsureTests
{
    private static LootTableConfig Table(int domain, int entries) => new()
    {
        TableId = domain,
        Name = $"五行域{domain}产出",
        Entries = Enumerable.Range(0, entries)
            .Select(i => new LootEntry
            {
                ItemId = 800000 + domain * 10000 + 101 + i,
                ItemName = $"b{domain}_{i}",
                DropRate = 0.1f,
                MinCount = 1,
                MaxCount = 1,
                Rarity = 1,
            })
            .ToArray(),
    };

    /// <summary>已有数据的域：一条都不许再写。</summary>
    [Fact]
    public async Task AbsentOnly_NeverOverwritesADomainThatAlreadyHasEntries()
    {
        var grains = new Dictionary<int, Mock<ILootTableGrain>>();
        grains[1] = GrainWithConfig(Table(1, entries: 15));      // 已有
        grains[2] = GrainWithoutConfig();                          // 空（激活回收过）
        grains[3] = GrainWithoutConfig();

        var tables = new Dictionary<int, LootTableConfig>
        {
            [1] = Table(1, 12), [2] = Table(2, 15), [3] = Table(3, 15),
        };

        var result = await WuxingLootTableEnsure.EnsureAsync(
            tables, d => grains[d].Object, onlyIfAbsent: true);

        Assert.Equal(new[] { 2, 3 }, result.Seeded.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 1 }, result.Skipped.ToArray());
        Assert.Empty(result.Failed);

        // 真正的护栏：有数据那一域根本没被 Initialize 碰过
        grains[1].Verify(g => g.InitializeAsync(It.IsAny<LootTableConfig>()), Times.Never);
        grains[2].Verify(g => g.InitializeAsync(tables[2]), Times.Once);
    }

    /// <summary>空 Grain 也算"缺"——GetConfigAsync 回 null 或回一张零条目的表都不许跳过。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task AbsentOnly_TreatsNullAndEmptyTableAsMissing(int? entryCount)
    {
        var grain = new Mock<ILootTableGrain>();
        if (entryCount is null)
        {
            grain.Setup(g => g.GetConfigAsync()).ReturnsAsync((LootTableConfig)null!);
        }
        else
        {
            grain.Setup(g => g.GetConfigAsync()).ReturnsAsync(new LootTableConfig
            {
                TableId = 4,
                Name = "空表",
                Entries = Array.Empty<LootEntry>(),
            });
        }
        grain.Setup(g => g.InitializeAsync(It.IsAny<LootTableConfig>())).Returns(Task.CompletedTask);

        var tables = new Dictionary<int, LootTableConfig> { [4] = Table(4, 15) };
        var result = await WuxingLootTableEnsure.EnsureAsync(tables, _ => grain.Object, onlyIfAbsent: true);

        Assert.Equal(new[] { 4 }, result.Seeded.ToArray());
        Assert.Empty(result.Skipped);
    }

    /// <summary>启动那一次是无条件写：那时集群里本来就该按最新配置重建。</summary>
    [Fact]
    public async Task FirstAssembly_WritesEveryDomain_RegardlessOfExistingData()
    {
        var g1 = GrainWithConfig(Table(1, 15));
        var g2 = GrainWithoutConfig();
        var tables = new Dictionary<int, LootTableConfig> { [1] = Table(1, 12), [2] = Table(2, 15) };

        var result = await WuxingLootTableEnsure.EnsureAsync(
            tables, d => (d == 1 ? g1 : g2).Object, onlyIfAbsent: false);

        Assert.Equal(new[] { 1, 2 }, result.Seeded.OrderBy(x => x).ToArray());
        Assert.Empty(result.Skipped);
        g1.Verify(g => g.InitializeAsync(tables[1]), Times.Once);
    }

    /// <summary>单张表失败不许带走其余的：少一域产出比整个装配半途抛出更容易看清。</summary>
    [Fact]
    public async Task OneFailingDomain_DoesNotStopTheOthers_AndIsReportedPerDomain()
    {
        var broken = new Mock<ILootTableGrain>();
        broken.Setup(g => g.GetConfigAsync()).ThrowsAsync(new InvalidOperationException("grain unavailable"));
        var healthy = GrainWithoutConfig();
        healthy.Setup(g => g.InitializeAsync(It.IsAny<LootTableConfig>())).Returns(Task.CompletedTask);

        var tables = new Dictionary<int, LootTableConfig> { [1] = Table(1, 15), [2] = Table(2, 15) };
        var result = await WuxingLootTableEnsure.EnsureAsync(
            tables, d => (d == 1 ? broken : healthy).Object, onlyIfAbsent: true);

        Assert.Equal(new[] { 2 }, result.Seeded.ToArray());
        Assert.Equal(new[] { 1 }, result.Failed.ToArray());
        Assert.True(result.Errors.ContainsKey(1), "失败域要带上异常，日志才说得出原因");
        Assert.IsType<InvalidOperationException>(result.Errors[1]);
    }

    /// <summary>取消要如实抛出，不能被"单张表失败"的兜底吞成一条失败记录。</summary>
    [Fact]
    public async Task Cancellation_Propagates_InsteadOfBecomeAAFailedDomain()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var grain = GrainWithoutConfig();
        var tables = new Dictionary<int, LootTableConfig> { [1] = Table(1, 15) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WuxingLootTableEnsure.EnsureAsync(tables, _ => grain.Object, onlyIfAbsent: true, cts.Token));
        grain.Verify(g => g.InitializeAsync(It.IsAny<LootTableConfig>()), Times.Never);
    }

    /// <summary>间隔不能短到每几秒重读一次物品表——那是给库加无谓负载。</summary>
    [Fact]
    public void HealInterval_IsNotSoShortThatItPoundsTheItemTable()
    {
        Assert.True(WuxingLootTableSeedService.HealInterval >= TimeSpan.FromMinutes(1),
            $"自愈间隔 {WuxingLootTableSeedService.HealInterval} 太短：每轮都要重读整张物品模板表");
    }

    private static Mock<ILootTableGrain> GrainWithConfig(LootTableConfig config)
    {
        var grain = new Mock<ILootTableGrain>();
        grain.Setup(g => g.GetConfigAsync()).ReturnsAsync(config);
        grain.Setup(g => g.InitializeAsync(It.IsAny<LootTableConfig>())).Returns(Task.CompletedTask);
        return grain;
    }

    private static Mock<ILootTableGrain> GrainWithoutConfig()
    {
        var grain = new Mock<ILootTableGrain>();
        grain.Setup(g => g.InitializeAsync(It.IsAny<LootTableConfig>())).Returns(Task.CompletedTask);
        return grain;
    }
}

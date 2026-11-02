using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// T8 回归：<c>UnpublishAsync(questId, forceAbandonActive:true)</c> 曾经持着不可重入的
/// <c>_writeGate</c> 再去等同一把锁 → **单例管理 Grain 永久卡死**，之后每一次任务管理调用都排队到超时，
/// 且现场没有任何日志解释"运营点了下线之后整个任务系统没反应"。
/// <para>
/// 锁外记账已经修好，并由 <c>QuestGrainLockDisciplineTests</c> 以**源码结构**守住（挪回锁内即判红）。
/// 但结构性守不住行为：它只保证"这一处不重入"，不保证"这条路径真的能在有限时间内走完并把持有者记进队列"。
/// 本文件补的就是后者——在真集群上把这条路径走一遍，并给每一步套死时限。
/// </para>
/// <para>
/// <b>为什么只能走真集群</b>：进程内直接 <c>new QuestManagerGrain(...)</c>（含用 Moq 代理
/// <c>IPersistentState</c>）会让 vstest 的 <c>testhost</c> 静默秒退，一行测试结果都不出，还会留下孤儿进程锁住
/// <c>Horizon.*.dll</c>（见 <c>docs/QUEST_SYSTEM_DESIGN.md</c> §7.2）。所以这里复用
/// <see cref="OrleansGrainLiveIntegrationTests"/> 的客户端宿主。
/// </para>
/// <para>
/// <b>环境门控</b>：未设置 <c>HORIZON_ORLEANS_REDIS</c> 时直接返回（视为跳过），不污染 CI。
/// 运行前提：Redis + 一个已更新到"撤销队列"这一版的 Silo 在跑（旧 Silo 没有这套方法，跑出来是反序列化错而不是超时，
/// 那同样说明没生效，但别把它误读成"死锁复现"）。示例：
/// <code>
/// $env:HORIZON_ORLEANS_REDIS = '127.0.0.1:9379,password=***'
/// dotnet test --filter "FullyQualifiedName~QuestManagerRevocationLiveTests"
/// </code>
/// 宿主在跑时构建本工程可能报 MSB3027 文件锁，那是锁不是代码错。
/// </para>
/// </summary>
public class QuestManagerRevocationLiveTests
{
    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(RedisConnectionString);

    private static string ClusterId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
    private static string ServiceId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";

    /// <summary>
    /// 探针任务 id。默认 <b>20001</b>——它是 <c>014</c> 种子里那条**刻意保持下线**的示范任务：
    /// <c>UnpublishAsync</c> 把 <c>publish_state</c> 写成 0，而它本来就是 0，所以对库是**净零改动**，
    /// 不会像拿 90001–90005（玩家真在接的五行入门）那样跑一次测试就把线上可接任务关掉。
    /// 需要换一条时用 <c>HORIZON_QUEST_PROBE_ID</c>，但必须挑一条当前下线的。
    /// </summary>
    private static int ProbeQuestId
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("HORIZON_QUEST_PROBE_ID");
            return int.TryParse(raw, out var v) && v > 0 ? v : 20001;
        }
    }

    /// <summary>单次 Grain 调用的时限。死锁的表现就是"永远不返回"，所以时限本身就是断言。</summary>
    private const int CallTimeoutSeconds = 5;

    private static async Task<(IHost Host, IClusterClient Client)> ConnectAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = ClusterId;
                o.ServiceId = ServiceId;
            });
        });

        var host = builder.Build();
        await host.StartAsync();
        return (host, host.Services.GetRequiredService<IClusterClient>());
    }

    /// <summary>
    /// 在时限内等一次 Grain 调用。超时不是失败重试的对象：调用不返回本身就是本测试要抓的缺陷，
    /// 所以这里把"没在 N 秒内返回"直接写成断言消息，并让 Grain 那边继续挂着（进程退出时随之消失）。
    /// </summary>
    private static async Task<T> WithDeadlineAsync<T>(string what, Task<T> call)
    {
        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(CallTimeoutSeconds)));
        Assert.True(ReferenceEquals(finished, call),
            $"{what} 未在 {CallTimeoutSeconds} 秒内返回——这正是 _writeGate 自锁的表现：单例 Grain 被自己等死，" +
            "之后所有任务管理调用都会排队到超时（T8）。");
        return await call;
    }

    [Fact]
    public async Task Live_UnpublishForce_有持有者时按时返回并把它写进撤销队列()
    {
        if (!LiveEnabled) return;

        var questId = ProbeQuestId;
        var characterId = Guid.NewGuid();

        var (host, client) = await ConnectAsync();
        try
        {
            var manager = client.GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);

            // 撤销账只按**持有者**记人：没有持有者时 RecordRevocationsAsync 直接早退（返回 0），
            // 于是整条自锁路径一步都不走——测试会变成空转。所以必须先把这个探针角色登记成持有者。
            await manager.NotifyAcceptedAsync(questId, characterId);
            Assert.Equal(1, await WithDeadlineAsync("CountHoldersAsync", manager.CountHoldersAsync(questId)));

            var result = await WithDeadlineAsync("UnpublishAsync(force)", manager.UnpublishAsync(questId, forceAbandonActive: true));

            Assert.True(result.Success, "强制关闭应成功，实际：" + result.Message);
            Assert.Equal(1, result.AbandonedCount);

            // 队列里必须是这个持有者——"abandoned=1 但没人排队"等于关闭时漏掉了玩家。
            var revoked = await WithDeadlineAsync("TakeRevokedQuestsAsync", manager.TakeRevokedQuestsAsync(characterId));
            Assert.Equal(new[] { questId }, revoked);

            // 取走即清空（网关每轮刷新都会调它，重复投递会让玩家被放弃 N 次）。
            var revokedAgain = await WithDeadlineAsync("TakeRevokedQuestsAsync(第二次)", manager.TakeRevokedQuestsAsync(characterId));
            Assert.Empty(revokedAgain);
        }
        finally
        {
            // 持有者表是 Grain 持久状态，不清掉就留下一个"幽灵持有者"：
            // 之后任何一次 unpublish(force) 都会给这个不存在的角色记一笔撤销账。
            try
            {
                var manager = client.GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);
                await manager.NotifyFinishedAsync(questId, characterId);
            }
            catch { /* 收尾失败不该掩盖主断言的结果 */ }
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_强制关闭之后管理Grain必须还能继续应答()
    {
        if (!LiveEnabled) return;

        var questId = ProbeQuestId;
        var characterId = Guid.NewGuid();

        var (host, client) = await ConnectAsync();
        try
        {
            var manager = client.GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);
            await manager.NotifyAcceptedAsync(questId, characterId);

            await WithDeadlineAsync("UnpublishAsync(force)", manager.UnpublishAsync(questId, forceAbandonActive: true));

            // 卡死的现场表现不在这一次调用上，而在**它之后**：锁被自己占住，后续每一条都要等，
            // 于是"下线一条任务"演变成"整个任务管理面失去响应"。这里连打三种后续调用做哨兵。
            var versionBefore = await WithDeadlineAsync("GetCatalogVersionAsync", manager.GetCatalogVersionAsync());
            Assert.True(versionBefore > 0, "目录版本号应为正数，实际 " + versionBefore + "（Silo 可能是没带任务系统的旧 DLL）");

            await WithDeadlineAsync("UnpublishAsync(不强制)", manager.UnpublishAsync(questId, forceAbandonActive: false));

            var catalog = await WithDeadlineAsync("GetCatalogAsync", manager.GetCatalogAsync(forceRefresh: true));
            Assert.Contains(catalog.Entries, e => e.QuestId == questId);

            // 强制那次记的账必须**还在**，而且**只有一条**：紧随其后的"不强制"下线不得重复记账
            // （`RecordRevocationsAsync` 里有 `queue.Contains` 去重；玩家被放弃两次是错的）。
            var revoked = await WithDeadlineAsync("TakeRevokedQuestsAsync", manager.TakeRevokedQuestsAsync(characterId));
            Assert.Equal(new[] { questId }, revoked);

            // 取走即清空。
            Assert.Empty(await WithDeadlineAsync("TakeRevokedQuestsAsync(第二次)", manager.TakeRevokedQuestsAsync(characterId)));
        }
        finally
        {
            try
            {
                var manager = client.GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);
                await manager.NotifyFinishedAsync(questId, characterId);
            }
            catch { /* 同上 */ }
            await host.StopAsync();
            host.Dispose();
        }
    }
}

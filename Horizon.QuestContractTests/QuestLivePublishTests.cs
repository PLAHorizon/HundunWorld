using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Sim.Quests;
using Horizon.Orleans.Interface.World;
using Horizon.QuestCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// "不停服实时发布 / 实时关闭"这条承诺的**运行态**证明（TECH_DEBT 里没有对应项，因为它是目标本身）。
/// <para>
/// 之前只证到两半：CLI 会改 <c>publish_state</c>（作者闭环测试），以及 Grain 的目录缓存带 TTL 会重查（读代码）。
/// 两半之间的缝——"运营改完库，正在跑的 Grain 到底多久看得见"——一直没被执行过。
/// 而这条缝恰恰是"实时"的全部内容：看不见就是**要重启**，那接口预留就等于没做。
/// </para>
/// <para>
/// 双重门控：<c>HORIZON_QUEST_DB</c>（要写真库）+ <c>HORIZON_ORLEANS_REDIS</c>（要打真集群）。
/// 缺任何一个直接返回（视为跳过）。跑法：
/// <code>
/// $env:HORIZON_QUEST_DB = '1'
/// $env:HORIZON_ORLEANS_REDIS = '127.0.0.1:9379'
/// dotnet test Horizon.QuestContractTests --filter "FullyQualifiedName~QuestLivePublishTests"
/// </code>
/// </para>
/// <para>
/// 探针走的是**刻意保持下线的演示位**（与 <c>20001</c> 同一条思路：新建行自建自清），
/// <c>finally</c> 里无条件把发布态打回下线并按 id 物理删除，绝不在库里留下可接的任务。
/// </para>
/// </summary>
public class QuestLivePublishTests
{
    private const int ProbeGiverTemplateId = 3;      // NpcTemplateCatalog 里登记过的"游方商队"

    private static string? Connection => ConnectionConfig.Resolve(out _);

    private static string? Redis => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(Connection) && !string.IsNullOrWhiteSpace(Redis);

    private static string ClusterId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
    private static string ServiceId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";

    [Fact]
    public async Task 库里改发布态正在跑的Grain不必重启就该看得见()
    {
        if (!LiveEnabled) return;

        // 探针 id 由时间戳派生：并行会话也在跑作者闭环测试，写死同一个号会互相踩。
        var probeId = 950000 + (int)(DateTime.UtcNow.Ticks % 5000);

        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(Redis!);
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
        try
        {
            var manager = host.Services.GetRequiredService<IClusterClient>()
                                .GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);

            using (var store = new QuestStore(Connection!))
            {
                var created = store.Upsert(NewDraft(probeId));
                Assert.True(created.Ok, "探针任务入库失败：" + created.Message);
            }

            // 基线：库里刚写的这条是下线态 → 可接列表里**不该**有它。
            Assert.DoesNotContain(probeId, await OffersAsync(manager));

            // ① 实时发布：只在库里改 publish_state（等价于 CLI `publish` / 编辑器"发布"按钮走的那条路），
            //    不碰 Grain 的写接口，然后强制刷一次目录。
            using (var store = new QuestStore(Connection!))
            {
                var published = store.SetPublishState(probeId, (int)QuestPublishState.Online);
                Assert.True(published.Ok, published.Message);
            }
            Assert.Contains(probeId, await OffersAsync(manager, forceRefresh: true));

            // ② 实时关闭：同样只改库。默认只停新接取，所以列表里必须干净地消失。
            using (var store = new QuestStore(Connection!))
            {
                var unpublished = store.SetPublishState(probeId, (int)QuestPublishState.Offline);
                Assert.True(unpublished.Ok, unpublished.Message);
            }
            Assert.DoesNotContain(probeId, await OffersAsync(manager, forceRefresh: true));

            // ③ 版本指纹必须跟着动：客户端的脏标记全靠它，不动就等于"运营改了玩法玩家看不见"。
            var offlineVersion = await manager.GetCatalogVersionAsync();
            using (var store = new QuestStore(Connection!))
            {
                store.SetPublishState(probeId, (int)QuestPublishState.Online);
            }
            await manager.GetCatalogAsync(forceRefresh: true);
            var onlineVersion = await manager.GetCatalogVersionAsync();
            Assert.NotEqual(offlineVersion, onlineVersion);
        }
        finally
        {
            // 无条件收尾：先把发布态打回下线（哪怕中途断言失败，也绝不留一条"在线的探针"在目录里），
            // 再按 id 物理删除。
            try
            {
                using var store = new QuestStore(Connection!);
                store.SetPublishState(probeId, (int)QuestPublishState.Offline);
            }
            catch { /* 收尾失败不该掩盖主断言 */ }
            PurgeProbe(probeId);
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// §7 那张表里"服务端权威接口"那一行的**唯一一次真实执行**：
    /// 走 <see cref="IQuestManagerGrain"/> 做内容热更 + 发布 + 关闭，而不是走 CLI 改库。
    /// <para>
    /// 为什么单独一条：目标要求"预留实时发布/关闭/取消的接口"，而接口写了不等于能用——
    /// <c>UpsertTemplateAsync</c> 要过同一份校验器、<c>PublishAsync</c> 要按**发布之后**的样子校验
    /// （早先它拿当前 publish_state=0 的行去跑发布校验，于是每一条发布都被 Q050 自相矛盾地拒掉）。
    /// 那两个语义只有真打一次 Grain 才算被测到。
    /// </para>
    /// <para>
    /// 新任务一律由数据库发号（<c>Id</c> 是 IDENTITY 列，Grain 会忽略草稿里的 id），
    /// 所以这里先用探针行占好 id，再走 Grain 更新它——避免"创建了却找不回那条"的收尾困境。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 走Grain入口的热更与上下线也立刻可见()
    {
        if (!LiveEnabled) return;

        var probeId = 960000 + (int)(DateTime.UtcNow.Ticks % 5000);

        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(Redis!);
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
        try
        {
            var manager = host.Services.GetRequiredService<IClusterClient>()
                                .GetGrain<IQuestManagerGrain>(QuestManagerKeys.Singleton);

            using (var store = new QuestStore(Connection!))
            {
                Assert.True(store.Upsert(NewDraft(probeId)).Ok, "探针任务入库失败");
            }
            Assert.DoesNotContain(probeId, await OffersAsync(manager));

            // ① 热更内容（走 Grain：服务端自己解析草稿 + 同一份校验器）：改门槛与名字。
            var hot = NewDraft(probeId);
            hot.QuestName = "实时发布探针·热更";
            hot.LevelRequire = 25;
            var updated = await manager.UpsertTemplateAsync(hot.ToJson());
            Assert.True(updated.Success, "Grain 侧热更被拒：" + updated.Message);

            // 库里真的换了：版本号自增 + 名字改过来了。
            using (var store = new QuestStore(Connection!))
            {
                var row = store.FindQuest(probeId);
                Assert.NotNull(row);
                Assert.Equal("实时发布探针·热更", row!.QuestName);
                Assert.Equal(25, row.LevelRequire);
                Assert.Equal(2, row.ContentVersion);
            }

            // ② 25 级门槛：60 级的探针角色看得见，10 级的看不见——门槛在**服务端**判，不在客户端。
            await manager.PublishAsync(probeId);
            Assert.Contains(probeId, await OffersAsync(manager, level: 60));
            Assert.DoesNotContain(probeId, await OffersAsync(manager, level: 10));

            // ③ Grain 侧关闭：立刻从可接列表消失。
            var closed = await manager.UnpublishAsync(probeId);
            Assert.True(closed.Success, closed.Message);
            Assert.DoesNotContain(probeId, await OffersAsync(manager, level: 60));
        }
        finally
        {
            try
            {
                using var store = new QuestStore(Connection!);
                store.SetPublishState(probeId, (int)QuestPublishState.Offline);
            }
            catch { /* 收尾失败不该掩盖主断言 */ }
            PurgeProbe(probeId);
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static async Task<List<int>> OffersAsync(IQuestManagerGrain manager, bool forceRefresh = false, int level = 60)
    {
        if (forceRefresh) await manager.GetCatalogAsync(forceRefresh: true);
        var offers = await manager.GetOfferedQuestsAsync(
            (int)QuestEntityKind.Npc, ProbeGiverTemplateId, level, Array.Empty<int>(), Array.Empty<int>());
        return offers.Select(offer => offer.QuestId).ToList();
    }

    private static QuestDraft NewDraft(int questId)
    {
        var draft = new QuestDraft
        {
            QuestId = questId,
            QuestName = "实时发布探针",
            Description = "验证\"改库即生效\"，跑完即删。",
            QuestType = (int)QuestTypeCode.Side,
            LevelRequire = 1,
            PublishState = (int)QuestPublishState.Offline,
            IsValid = true,
            Giver = new QuestEntityRefDraft { Kind = "npc", TemplateId = ProbeGiverTemplateId },
        };
        draft.Objectives.Add(new QuestObjectiveDraft
        {
            Kind = "kill_monster", TemplateId = 4, Count = 1, Description = "猎杀 1 只银爪狸",
        });
        draft.Rewards.Exp = 10;
        return draft;
    }

    private static void PurgeProbe(int questId)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(Connection);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Game_HunduShijie_QuestTemplate WHERE quest_id = @id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = questId;
        command.Parameters.Add(parameter);
        connection.Open();
        command.ExecuteNonQuery();
    }
}

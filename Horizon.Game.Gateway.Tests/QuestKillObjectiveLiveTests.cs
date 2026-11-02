using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 真集群：一条"按五行域限定的击杀目标"从接取到交付的完整走法。
/// <para>
/// 治的是两件一直存在的事：
/// ① <c>objectives</c> 列建了但没人解析，于是接了任务就能直接交——§14.2"通过任务获得初级技能书"
///    实际上是个空转按钮；
/// ② 默认匹配器只认"这是个击杀目标"，任意击杀都推进（"杀 5 只狼 + 杀 3 只熊"会串）。
/// </para>
/// <para>
/// 环境门控同 <see cref="OrleansGrainLiveIntegrationTests"/>：未设 <c>HORIZON_ORLEANS_REDIS</c> 直接返回。
/// 用合成角色键（9_900_000_0xx），不碰真人存档。
/// </para>
/// </summary>
[Collection("Wuxing live cluster")]
public class QuestKillObjectiveLiveTests
{
    private const ulong TestCharacterId = 9_900_000_031UL;
    // 每次运行换一个 questId：任务状态是持久的，一次性任务交付第二次就再也接不上
    // （AbandonIfPresentAsync 对"已完成"的任务不能撤，所以只能靠新 id 保证可重复运行）。
    private static readonly int QuestId = 900100 + (int)(DateTime.UtcNow.Ticks % 500);
    private const string ObjectiveType = "kill_domain:3";   // 水域
    private const int Required = 5;

    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static string ClusterId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
    private static string ServiceId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";

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

    [Fact]
    public async Task Live_DomainKillObjective_RefusesEarlySubmit_IgnoresOtherDomainsAndCompletesOnTarget()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeyMapper.ToInventoryKey(TestCharacterId);
            var quests = client.GetGrain<IQuestGrain>(key);
            var progressWriter = new OrleansQuestProgressWriter(client);

            // 目标生物的域号从目录实测取，别把"墨麒麟=水域"抄成字面量（内容一改这里就假失败）
            var targetTemplate = MonsterTemplateCatalog.RegisteredTemplates()
                .First(t => t.WuxingDomain == 3).TemplateId;
            var offDomainTemplate = MonsterTemplateCatalog.RegisteredTemplates()
                .First(t => t.WuxingDomain != 3).TemplateId;

            // 上一轮可能留着一个已完成/已交付的同 id 任务，先把状态摆正（共享存档要自证前置）
            await AbandonIfPresentAsync(quests);

            var accepted = await quests.AcceptQuestAsync(
                QuestId, "水域异兽", "猎杀 5 只水域异兽，取回它们身上的东西", questType: 1, level: 1,
                rewards: new System.Collections.Generic.Dictionary<string, int> { ["item:830301"] = 1 });
            Assert.True(accepted, "接任务应成功");
            Assert.True(await quests.AddQuestObjectiveAsync(QuestId, ObjectiveType, "猎杀 5 只水域异兽", Required),
                "按模板口径加击杀目标应成功");

            var quest = await quests.GetQuestAsync(QuestId);
            Assert.NotNull(quest);
            var objective = Assert.Single(quest!.Objectives);
            Assert.Equal(ObjectiveType, objective.ObjectiveType);
            Assert.Equal(Required, objective.RequiredCount);
            Assert.Equal(0, objective.CurrentCount);

            // ① 一条目标都没推进时就要能挡住交付。这条断言以前是恒过的——因为根本没有目标，
            //    "接了就能交"那时看起来像 bug，其实只是没人解析 objectives 列。
            var tooEarly = await quests.CompleteQuestAsync(QuestId);
            Assert.False(tooEarly.Success, $"目标未完成时不得交付：{tooEarly.Message}");

            // ② 别域的击杀一次都不算数（旧匹配器会把它们全算进来，于是"杀错地方"也能交任务）
            for (var i = 0; i < Required * 2; i++)
            {
                await progressWriter.AdvanceKillObjectiveAsync(TestCharacterId, offDomainTemplate, 1);
            }

            quest = await quests.GetQuestAsync(QuestId);
            objective = Assert.Single(quest!.Objectives);
            Assert.Equal(0, objective.CurrentCount);
            Assert.False(objective.IsCompleted);
            var stillLocked = await quests.CompleteQuestAsync(QuestId);
            Assert.False(stillLocked.Success, "别域击杀不许把任务凑齐");

            // ③ 目标域的击杀正常推进，够了就转"可提交"
            var advanced = await progressWriter.AdvanceKillObjectiveAsync(TestCharacterId, targetTemplate, Required);
            Assert.Equal(1, advanced);

            quest = await quests.GetQuestAsync(QuestId);
            objective = Assert.Single(quest!.Objectives);
            Assert.Equal(Required, objective.CurrentCount);
            Assert.True(objective.IsCompleted);

            var done = await quests.CompleteQuestAsync(QuestId);
            Assert.True(done.Success, done.Message);

            // 交付出来的奖励仍是服务端那份，不是调用方自带的
            Assert.NotNull(done.Rewards);
            Assert.Equal(1, done.Rewards["item:830301"]);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>把同 id 任务清掉：任务状态是持久的，第二次运行不该被上一轮的"已完成"卡住。</summary>
    private static async Task AbandonIfPresentAsync(IQuestGrain quests)
    {
        var existing = await quests.GetQuestAsync(QuestId);
        if (existing is null) return;
        if (existing.Status == (int)QuestProgressStatus.Completed) return;   // 已完成的不占用
        try { await quests.AbandonQuestAsync(QuestId); } catch { /* 尽力而为 */ }
    }
}

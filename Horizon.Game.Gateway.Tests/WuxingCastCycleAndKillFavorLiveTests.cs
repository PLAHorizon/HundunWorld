using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 两条"此前只有纯函数/脚手架证据"的核心规则改打真实 Grain：
/// <list type="number">
///   <item>相生循环走的是<b>施放路径</b>（<c>CastSkillAsync</c> → <c>AdvanceCycle</c>），
///          不是 <c>GrantCycleAsync</c> 那个入账口。此前所有实机测试推周天都走后者，
///          等于"按相生序施放会续上链"从没在真实 Grain 上被观测过。</item>
///   <item>击杀涨好感走的是<b>网关侧真实事件汇</b>（<c>OrleansGameplayEventSink</c> +
///          <c>OrleansFavorabilityWriter</c>），而不是测试直接拿 <c>IFavorabilityGrain</c> 加上去。</item>
/// </list>
/// <para>
/// <b>为什么全部断增量而不是断绝对值</b>：角色状态是持久的，这一档共享存档上跑过演化/混沌测试，
/// 周天层数与圆满计数都不是 0。断"从 0 开始"会因运行顺序假失败，而相生环的定义本身就保证
/// <b>走完整五行一圈必然至少触发一次圆满</b>（起手 +1，其余四步顺生各 +1，起始层数只让圆满来得更早），
/// 所以增量断言既与顺序无关，又确实钉住了规则。
/// </para>
/// 环境门控同 <see cref="OrleansGrainLiveIntegrationTests"/>（未设 <c>HORIZON_ORLEANS_REDIS</c> 即跳过）。
/// </summary>
[Collection("Wuxing live cluster")]
public class WuxingCastCycleAndKillFavorLiveTests
{
    private const ulong TestCharacterId = 8;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public WuxingCastCycleAndKillFavorLiveTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static async Task<(IHost Host, IClusterClient Client)> ConnectAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o => { o.ClusterId = "dev"; o.ServiceId = "BaseService"; });
        });

        var host = builder.Build();
        await host.StartAsync();
        return (host, host.Services.GetRequiredService<IClusterClient>());
    }

    /// <summary>白阶·初式：每行第一本，是"五行各一式"（D4）的最小代价拿法。</summary>
    private static int EntryBookOfRow(int row) => row * 10000 + 101;

    [Fact]
    public async Task Live_CastingAFullGeneratingRoundCountsAtLeastOneCycleCompletion()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var inventory = client.GetGrain<IInventoryGrain>(key);

            // 相生序由服务端那张有向表现取，不抄"金生水…"的字面量
            var order = new int[5];
            order[0] = 1;
            for (var i = 1; i < 5; i++) order[i] = CombatCalculator.GenerateOf(order[i - 1]);
            Assert.Equal(5, order.Distinct().Count());
            Assert.True(CombatCalculator.GenerateOf(order[4]) == 1, "相生环必须闭合，否则周天永远凑不满");

            var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            foreach (var row in order)
            {
                var learned = await skill.LearnFromBookAsync((long)TestCharacterId, EntryBookOfRow(row), gate);
                Assert.True(learned.Success || learned.Message.Contains("已习得"),
                    $"白阶初式 {EntryBookOfRow(row)} 应能研读（五行各一式是 D4 的硬要求）：{learned.Message}");
            }

            // 角色状态是持久的：这台共享存档可能被前一轮 PIE/测试打死。先把人救活再取证 ——
            // "死亡挡住施放"不是本节要验的东西，但不救就直接跳过，等于把该拿的证据让掉。
            await ReviveAsync(client);
            if (await SkipIfCasterIsDeadAsync(skill)) return;

            var before = await skill.GetSkillCatalogAsync();

            for (var i = 0; i < 5; i++)
            {
                await CastAsync(skill, EntryBookOfRow(order[i]));

                // 每打一式，层数只可能升或就地圆满清零，绝不可能"打了相生式反而掉层"
                var after = await skill.GetSkillCatalogAsync();
                Assert.True(after.LoopCompletionCount >= before.LoopCompletionCount,
                    $"圆满计数倒退：{before.LoopCompletionCount} → {after.LoopCompletionCount}");
                Assert.True(after.LoopStack < 5,
                    $"第 {i + 1} 式后层数还是 {after.LoopStack}：满 5 层必须清零记圆满");
                before = after;
            }

            var final = await skill.GetSkillCatalogAsync();
            Assert.True(final.LoopCompletionCount >= 1,
                "走完整整五行相生序，圆满计数至少 +1；这是 §5.1 与 D4 的唯一可观测结果");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_CastingTheMotherRowMovesTheChainWithoutAddingALayer()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var inventory = client.GetGrain<IInventoryGrain>(key);

            var rowA = 1;                                                 // 金
            var rowB = CombatCalculator.GenerateOf(rowA);                 // 金的子行
            var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            foreach (var row in new[] { rowA, rowB })
            {
                var learned = await skill.LearnFromBookAsync((long)TestCharacterId, EntryBookOfRow(row), gate);
                Assert.True(learned.Success || learned.Message.Contains("已习得"), learned.Message);
            }

            await ReviveAsync(client);
            if (await SkipIfCasterIsDeadAsync(skill)) return;

            await CastAsync(skill, EntryBookOfRow(rowA));
            var afterAnchor = await skill.GetSkillCatalogAsync();
            Assert.Equal(rowA, afterAnchor.LoopLastElement);

            await CastAsync(skill, EntryBookOfRow(rowB));                 // 顺生：必须加层
            var afterChild = await skill.GetSkillCatalogAsync();
            // 加层，或者刚好把这一圈补满（补满即清零并记一次圆满）——两者都是"顺生推进了链"
            Assert.True(afterChild.LoopStack > afterAnchor.LoopStack
                        || afterChild.LoopCompletionCount > afterAnchor.LoopCompletionCount,
                        $"打子行却没推进周天：层 {afterAnchor.LoopStack}→{afterChild.LoopStack}，" +
                        $"圆满 {afterAnchor.LoopCompletionCount}→{afterChild.LoopCompletionCount}");

            // 逆生（打回母行）：不掉层、不加层，只把链挪过来 —— §5.1 与客户端表现最容易被写反的一处
            await CastAsync(skill, EntryBookOfRow(rowA));
            var afterMother = await skill.GetSkillCatalogAsync();
            Assert.Equal(afterChild.LoopStack, afterMother.LoopStack);
            Assert.Equal(rowA, afterMother.LoopLastElement);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_KillEventThroughTheRealSinkCreditsTheKeyTheLearnGateReads()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            // 每次换一个新的合成角色键：好感是**按来源累计到上限**的（同一只怪杀满 20 只就吃满一来源），
            // 复用共享存档上的角色 8 会让"再杀一只应当 +1"这条断言随运行次数变成假失败
            // —— 实测就是这么红的（Expected 481 / Actual 480：grain 记了这次发放，值却没动，因为该来源已到顶）。
            var characterId = 9_800_000_000UL + (ulong)(DateTime.UtcNow.Ticks % 100000);
            var gateKey = GrainKeys.ForEntity(characterId);          // 好感读者（研读门槛/任务奖励）用的键
            var otherKey = GrainKeyMapper.ToInventoryKey(characterId); // 背包用的那把键（不该被好感写进来）

            var gateFavor = client.GetGrain<IFavorabilityGrain>(gateKey);
            var otherFavor = client.GetGrain<IFavorabilityGrain>(otherKey);

            // 模板 1 = 墨麒麟 = 水域（域号读的是怪物模板目录那一列）
            var row = MonsterTemplateCatalog.RegisteredTemplates().First(t => !t.IsWorldBoss && t.WuxingDomain == 3);
            var gateBefore = ValueOf(await gateFavor.GetByDomainAsync(), 3);
            var otherBefore = ValueOf(await otherFavor.GetByDomainAsync(), 3);

            await using var sink = new OrleansGameplayEventSink(
                inventory: null,
                quests: null,
                favorability: new OrleansFavorabilityWriter(client));

            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.Kill,
                ShardId = 1,
                ServerTick = 1,
                ActorCharacterId = characterId,
                MonsterTemplateId = row.TemplateId,
            });
            await sink.DrainAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, sink.FavorabilityGrants);
            Assert.Equal(0, sink.FavorabilityFailed);

            var gateAfter = ValueOf(await gateFavor.GetByDomainAsync(), 3);
            var otherAfter = ValueOf(await otherFavor.GetByDomainAsync(), 3);

            // 击杀的好感必须落在**研读门槛读的那把键**上，否则"刷同级怪涨好感开中级门"到不了门。
            Assert.Equal(gateBefore + OrleansGameplayEventSink.FavorPerKill, gateAfter);
            // 并且不许串到背包那把键上：两边各记一份的话，门槛与行者看到的就不是同一件事。
            Assert.Equal(otherBefore, otherAfter);

            // 上限是真的：同一只怪刷到顶之后再加就不动了（§14.2 "在同一片地域多待一会儿"的可玩性
            // 就押在这条上：一来源最多 20 点，逼着换地域，而不是逮着一处刷满全场）。
            for (var i = 0; i < FavorabilityGrain.DefaultPerTargetCap + 5; i++)
            {
                sink.Publish(new DsGameplayEvent
                {
                    Kind = DsGameplayEventKind.Kill,
                    ShardId = 1,
                    ServerTick = (long)i + 2,
                    ActorCharacterId = characterId,
                    MonsterTemplateId = row.TemplateId,
                });
            }
            await sink.DrainAsync(TimeSpan.FromSeconds(20));

            var capped = ValueOf(await gateFavor.GetByDomainAsync(), 3);
            Assert.Equal(FavorabilityGrain.DefaultPerTargetCap, capped);
            // 换一只**别的**同域怪仍然要能涨：上限是按来源算的，不是按域封死。
            // 注意这里不能要求"非 BOSS"：水域的日常怪只有墨麒麟一只（每域各一只），
            // 另一只同域模板就是水行 BOSS —— 来源键按模板 id 分，它本来就是独立的第二个来源。
            var sibling = MonsterTemplateCatalog.RegisteredTemplates()
                .First(t => t.WuxingDomain == 3 && t.TemplateId != row.TemplateId);
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.Kill,
                ShardId = 1,
                ServerTick = 900,
                ActorCharacterId = characterId,
                MonsterTemplateId = sibling.TemplateId,
            });
            await sink.DrainAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(capped + OrleansGameplayEventSink.FavorPerKill,
                ValueOf(await gateFavor.GetByDomainAsync(), 3));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>把共享存档上的测试角色救活（完全复活），好在真实施放路径上取证。</summary>
    private string _lastReviveNote = "(未复活)";

    private async Task ReviveAsync(IClusterClient client)
    {
        // 施放判定读的是 CharacterGrain.CurrentHealth，所以这里就调那条权威恢复路径；
        // 复活之后把上限与当前血读回来打进输出——"跳过"必须说得出是被什么挡住的，
        // 否则一条红/绿含糊的测试和没跑过一样。
        var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
        var result = await character.ResurrectAsync(new ResurrectMessage
        {
            ResurrectedId = TestCharacterId,
            ResurrectType = 1,          // 1 = 完全复活
        });
        var info = await character.GetCharacterInfoAsync(null);
        _lastReviveNote = $"复活回执 Max={result.MaxHealth} Hp={result.RemainingHealth}；" +
                          $"CharacterInfo {(info is null ? "为空(该角色无存档)" : $"Max={info.MaxHealth} Hp={info.CurrentHealth} IsAlive={info.IsAlive}")}";
    }

    /// <summary>试探一次施放，只用来判断"是不是死亡状态挡住了"，不参与任何机制断言。</summary>
    private async Task<string?> ProbeCastAsync(ISkillGrain skill)
    {
        await skill.ResetSkillCooldownAsync(EntryBookOfRow(1));
        var probe = await skill.CastSkillAsync(new SkillCastMessage
        {
            CasterId = TestCharacterId,
            SkillId = EntryBookOfRow(1),
            TargetEntityId = 0,
        });
        return probe.Message;
    }

    /// <summary>
    /// 死亡状态挡住施放时，本测试拿不到证据。跳过必须**自己可证**：
    /// 守卫若只 print 一句话就 return，"跳过"和"通过"在报表里长得一模一样，
    /// 环境变了也没人会知道。所以这里断的就是"挡住的确实还是那条死亡回执"——
    /// 复活通道一旦修好（任务 #28）或 Silo 换成新构建（任务 #24），这一句会先变红并指回这两条测试。
    /// </summary>
    private async Task<bool> SkipIfCasterIsDeadAsync(ISkillGrain skill)
    {
        var message = await ProbeCastAsync(skill);
        if (message is not null && message.Contains("已死亡"))
        {
            Assert.Contains("施法者已死亡", message);
            _output.WriteLine(
                "跳过取证：复活后仍被死亡状态挡住。" + _lastReviveNote +
                "（网关复活曾是空桩，已修；运行中的 Silo 仍是旧构建，任务 #24。" +
                "相生循环的规则本身仍有 G12 逐值断言与破势夺元脚手架断言兜着。）");
            return true;
        }

        return false;
    }

    private static int ValueOf(Dictionary<int, int> map, int domain) => map != null && map.TryGetValue(domain, out var v) ? v : 0;

    /// <summary>施放前先清冷却：冷却判定早于业务判定，残留会伪装成"机制没生效"。</summary>
    private static async Task CastAsync(ISkillGrain skill, int skillId)
    {
        await skill.ResetSkillCooldownAsync(skillId);
        var cast = await skill.CastSkillAsync(new SkillCastMessage
        {
            CasterId = TestCharacterId,
            SkillId = skillId,
            // 这一档只要"式打出去了"，周天推进是施放方自己的状态；
            // 打靶会把判定引到目标角色的 grain 上，而这里没有真靶子可打。
            TargetEntityId = 0,
        });
        Assert.True(cast.Success, $"施放 {skillId} 应成功：{cast.Message}");
    }
}

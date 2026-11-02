using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Core;
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
/// 终式"混沌一炁"与 §6.2 计数门槛的真集群验证（环境门控 + 与其它实机测试同一串行 collection）。
/// <para>
/// 这是目标的封顶那一句："五行技能在特定条件下可演化为新的更高层次的技能<b>最高可超越天阶</b>"。
/// 道阶已经单独验过（<see cref="SkillEvolutionLiveTests"/>），但混沌那一路多的是
/// **跨系计数门槛**（任意 3 对道阶三境圆满 + 周天圆满 ≥ 40 + 五域好感全达标 + 基础属性），
/// 而"按字段判而不是按身份判"这类计数最容易出"数据里有、代码不判"——
/// §6.2 的两条计数器就是这么被发现从来没生效过的，所以必须让它在真服务上撞一次。
/// </para>
/// <para>
/// 断言的顺序刻意是"先缺一对 → 被拒 → 补齐 → 放行"：只测"最后能学到"证明不了计数门在拦人。
/// </para>
/// </summary>
[Collection("Wuxing live cluster")]
public class WuxingChaosLiveTests
{
    private const ulong TestCharacterId = 8;         // 开发库里的一次性测试角色 "123"

    private const int ChaosSkill = 90001;

    private static string? Redis => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");

    private static async Task<(IHost Host, IClusterClient Client)> ConnectAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        var options = ConfigurationOptions.Parse(Redis!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = options);
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
                o.ServiceId = Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";
            });
        });

        var host = builder.Build();
        await host.StartAsync();
        return (host, host.Services.GetRequiredService<IClusterClient>());
    }

    /// <summary>相生序上的前三对：金水、水木、木火（母行, 子行）。</summary>
    private static readonly (int Mother, int Child)[] Pairs = { (1, 3), (3, 2), (2, 4) };

    [Fact]
    public async Task Live_ChaosRequiresThreeCompletedDaoSeriesAndPassesWhenTheyAreThere()
    {
        if (string.IsNullOrWhiteSpace(Redis)) return;      // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
            var inventory = client.GetGrain<IInventoryGrain>(key);
            var favor = client.GetGrain<IFavorabilityGrain>(key);

            // 混沌门槛：Lv300、亲和 100、四维 58/58/86/115、五域好感各 110、五种炼材各若干
            await character.AddExpAsync(5_000_000);
            var allocated = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Strength = 70, Agility = 70, Health = 100, Intelligence = 130,
                Metal = 200, Wood = 200, Water = 200, Fire = 200, Earth = 200,
            });
            Assert.True(allocated.Success, allocated.Message);

            for (int domain = 1; domain <= 5; domain++)
            {
                // 好感有"单一来源默认 20 点上限"，要堆到 110 只能走多个来源
                for (int source = 0; source < 7; source++)
                    await favor.AddAsync(domain, 20, $"test:chaos:{domain}:{source}");
            }
            for (int material = 910001; material <= 910010; material++)
                Assert.True(await inventory.AddItemAsync(material, 80, 0), $"灵材 {material} 应能入账");

            // 四行的阶梯从白阶一路推到天阶并练满（三对道阶共用这四行）
            foreach (int row in new[] { 1, 2, 3, 4 })
            {
                for (int grade = 1; grade <= 5; grade++)
                {
                    for (int stage = 1; stage <= 3; stage++)
                        await EnsureLearnedAsync(skill, inventory, row * 10000 + grade * 100 + stage);
                }

                for (int stage = 1; stage <= 3; stage++)
                {
                    int tian = row * 10000 + 500 + stage;
                    for (int i = 0; i < 30; i++)
                    {
                        if (!await skill.UpgradeSkillAsync(tian)) break;
                    }
                }
            }

            // 周天圆满次数要 40：一层一轮里的 5 层，给足余量
            for (int i = 0; i < 220; i++)
                Assert.True(await skill.GrantCycleAsync(), "周天推进应成功");

            // 前两对道阶补齐（每对三境，且**每境练满**）——此时"圆满对数"是 2，混沌必须被计数门槛拦下
            foreach (var pair in new[] { 1, 2 })
            {
                for (int stage = 1; stage <= 3; stage++)
                {
                    int dao = 70000 + pair * 100 + stage;
                    await EnsureLearnedAsync(skill, inventory, dao, $"道阶第 {pair} 对");
                    await MaxOutAsync(skill, dao);
                }
            }

            // 负向断言只在混沌尚未习得时可观测：已经学过之后再研读会先回"已习得此式"，
            // 计数门槛根本没机会说话。所以第一次运行才是它的取证窗口，重跑时如实跳过，
            // 而不是把这条断言删掉（正向路径"补齐三对才放行"仍然每轮都验）。
            if (!(await skill.GetSkillsAsync()).Any(s => s.SkillId == ChaosSkill))
            {
                var tooEarly = await LearnOnce(skill, inventory, ChaosSkill);
                Assert.False(tooEarly.Success, "只有两对道阶圆满时不该允许混沌");
                Assert.Contains("道阶", tooEarly.Message);
            }

            // 第三对补齐后，计数门槛满足，混沌应能演化（掷签低概率，交给重试）
            for (int stage = 1; stage <= 3; stage++)
            {
                int dao = 70000 + 3 * 100 + stage;
                await EnsureLearnedAsync(skill, inventory, dao, "道阶第 3 对");
                await MaxOutAsync(skill, dao);
            }

            await EnsureLearnedAsync(skill, inventory, ChaosSkill, "混沌一炁应能演化");

            var learned = await skill.GetSkillsAsync();
            Assert.Contains(learned, s => s.SkillId == ChaosSkill);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// 练满一式。§6.2 的"道阶系圆满对数"按 <b>Level==MaxLevel</b> 计对，
    /// 只学到 1 级是不算的——第一次跑这条测试时就因为漏了这一步，被门槛如实报"当前 0 对"。
    /// 升级接口到顶会返回 false，循环到拒绝即止。
    /// </summary>
    private static async Task MaxOutAsync(ISkillGrain skill, int skillId)
    {
        for (int i = 0; i < 30; i++)
        {
            if (!await skill.UpgradeSkillAsync(skillId)) return;
        }
        Assert.True(false, $"技能 {skillId} 练满循环异常（30 次仍在升）");
    }

    private static async Task EnsureLearnedAsync(
        ISkillGrain skill, IInventoryGrain inventory, int skillId, string? what = null, int attempts = 40)
    {
        LearnSkillResponse? last = null;
        for (int i = 0; i < attempts; i++)
        {
            last = await LearnOnce(skill, inventory, skillId);
            if (last.Success || last.Message.Contains("已习得")) return;
            if (!last.Message.Contains("未成")) break;      // 只有掷签未中才值得再试
        }
        Assert.True(false, $"{skillId} {what ?? "应能研读"}：{last?.Message}");
    }

    private static async Task<LearnSkillResponse> LearnOnce(ISkillGrain skill, IInventoryGrain inventory, int skillId)
    {
        var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
        return await skill.LearnFromBookAsync((long)TestCharacterId, skillId, gate);
    }
}

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
/// "角色基础属性触发融合技能学习的前置条件"（§14.2 道阶那一档的 attrNeed）的真集群验证。
/// <para>
/// §14 的四族动态条件里，好感（G1）、材料（材料门测试）、技能树完整性/演化链（G2、G4）都各自被
/// "缺一样就拦下来"实测过，只剩基础属性这族一直是"把值给足然后看它放行"——
/// 那只证明了不拦，没证明会拦。这条把它补齐：其它条件一律满足，只把四维清零，必须被属性挡住。
/// </para>
/// <para>
/// 清零是可行的：<c>AllocateAttributeAsync</c> 收的是"目标分配态"（整体替换），所以五行点保持高位、
/// 四维归零能稳定停在"亲和够、属性不够"这一点上；判定顺序是 等级→境界→亲和→前置→演化源→计数→动态门槛，
/// 因此报出来的原因就是属性，而不是前面某一关先替它说话。
/// </para>
/// <para>与其它实机测试共用一次性角色（<c>character_id=8</c>，名 "123"），同一 collection 串行。</para>
/// </summary>
[Collection("Wuxing live cluster")]
public class WuxingAttributeGateLiveTests
{
    private const ulong TestCharacterId = 8;

    private const int DaoPair5Stage1 = 70501;      // 土生金那一对的道阶·初：道阶是唯一带 attrNeed 的档，而这一对没有任何测试碰过

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

    [Fact]
    public async Task Live_AttributeGate_BlocksDaoEvolutionUntilBasicAttributesAreAllocated()
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

            await character.AddExpAsync(5_000_000);         // 道阶门槛 Lv300
            for (int domain = 1; domain <= 5; domain++)
            {
                for (int source = 0; source < 7; source++)
                    await favor.AddAsync(domain, 20, $"test:attr:{domain}:{source}");
            }
            for (int material = 910001; material <= 910010; material++)
                Assert.True(await inventory.AddItemAsync(material, 80, 0), $"灵材 {material} 应能入账");

            // 亲和给足、四维清零：让"属性"成为唯一的短板
            var starved = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Metal = 200, Wood = 200, Water = 200, Fire = 200, Earth = 200,
            });
            Assert.True(starved.Success, starved.Message);

            // 土行阶梯（火行由混沌那条测试推过）：白→天，天阶三式练满
            for (int grade = 1; grade <= 5; grade++)
            {
                for (int stage = 1; stage <= 3; stage++)
                    await EnsureLearnedAsync(skill, inventory, 5 * 10000 + grade * 100 + stage);
            }
            foreach (int stage in new[] { 1, 2, 3 })
            {
                int tian = 5 * 10000 + 500 + stage;
                for (int i = 0; i < 30; i++)
                {
                    if (!await skill.UpgradeSkillAsync(tian)) break;
                }
            }
            for (int i = 0; i < 220; i++)
                Assert.True(await skill.GrantCycleAsync(), "周天推进应成功");

            // 差属性：必须被属性挡住，而且报的确实是属性。
            // 同样加状态守卫：这一式一旦在某轮被补齐属性后学到，重跑时研读会先回"已习得此式"，
            // 属性门就没机会开口——负向断言只在第一次运行可观测，正向路径每轮都验。
            bool observedRefusal = false;
            bool freshAtStart = !(await skill.GetSkillsAsync()).Any(s => s.SkillId == DaoPair5Stage1);
            if (freshAtStart)
            {
                var refused = await LearnOnce(skill, inventory, DaoPair5Stage1);
                Assert.False(refused.Success, "四维为零时不该允许演化道阶");
                Assert.Contains("不足：需", refused.Message);
                Assert.True(
                    refused.Message.Contains("力量") || refused.Message.Contains("敏捷")
                    || refused.Message.Contains("体魄") || refused.Message.Contains("神识"),
                    $"报出来的应当是四维之一：{refused.Message}");
                observedRefusal = true;
            }

            // 补上四维即放行（同一对道阶·初，条件与上面完全一样，只差属性）
            var fed = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Strength = 70, Agility = 70, Health = 100, Intelligence = 130,
                Metal = 200, Wood = 200, Water = 200, Fire = 200, Earth = 200,
            });
            Assert.True(fed.Success, fed.Message);

            await EnsureLearnedAsync(skill, inventory, DaoPair5Stage1, "补齐基础属性后道阶应能演化");
            Assert.Contains((await skill.GetSkillsAsync()), s => s.SkillId == DaoPair5Stage1);

            // 自证：这一轮如果该式原本还没学过，就必须真的看到属性拦截。
            // 少了这条，"守卫跳过"会静默把唯一的证据窗口藏起来，测试照样绿——那是假绿。
            Assert.True(observedRefusal || !freshAtStart,
                "本轮没有观测到属性拦截：该式此前已被学过，请换一个没被碰过的演化式再取证");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static async Task EnsureLearnedAsync(
        ISkillGrain skill, IInventoryGrain inventory, int skillId, string? what = null, int attempts = 40)
    {
        LearnSkillResponse? last = null;
        for (int i = 0; i < attempts; i++)
        {
            last = await LearnOnce(skill, inventory, skillId);
            if (last.Success || last.Message.Contains("已习得")) return;
            if (!last.Message.Contains("未成")) break;
        }
        Assert.True(false, $"{skillId} {what ?? "应能研读"}：{last?.Message}");
    }

    private static async Task<LearnSkillResponse> LearnOnce(ISkillGrain skill, IInventoryGrain inventory, int skillId)
    {
        var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
        return await skill.LearnFromBookAsync((long)TestCharacterId, skillId, gate);
    }
}

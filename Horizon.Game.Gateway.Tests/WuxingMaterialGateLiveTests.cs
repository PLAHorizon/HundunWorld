using System;
using System.Collections.Generic;
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
/// "技能所需特殊材料触发高级技能学习条件"（§14.2 的地阶那一档）的真集群验证。
/// <para>
/// 前面几条实机测试都是把料给足再往上推，所以只证明了"有料能过"，没证明"缺料会拦"。
/// 缺料这半才是这条门槛的全部意义：它必须让材料变成真消耗品，否则"高级技能靠特殊材料"
/// 只是技能树页上的一行说明文字。
/// </para>
/// <para>
/// 顺带钉住一件容易写坏的事：<b>判定失败一粒料都不该扣</b>。判定在 SkillGrain、扣除在 InventoryGrain，
/// 中间没有第三人能动包；但只要顺序写反（先扣再判），一次被拒的研读就会白吃材料。
/// </para>
/// <para>与其它实机测试共用一次性角色（<c>character_id=8</c>，名 "123"），故同一 collection 串行。</para>
/// </summary>
[Collection("Wuxing live cluster")]
public class WuxingMaterialGateLiveTests
{
    private const ulong TestCharacterId = 8;

    private const int EarthRow = 5;                 // 土行：没有任何一条测试爬过它的阶梯，前置链最干净
    private const int DiFirst = EarthRow * 10000 + 401;      // 土·地·初：§14.2 里"材料触发高级技能"的那一式
    private const int DiTierOneMaterial = 910005;            // 土行粗炼灵材（地阶门槛要求的就是它）

    private static readonly int[] AllMaterials = Enumerable.Range(910001, 10).ToArray();

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
    public async Task Live_MaterialGate_BlocksAdvancedTierWithoutSpecialMaterialsAndConsumesNothingOnRefusal()
    {
        if (string.IsNullOrWhiteSpace(Redis)) return;      // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
            var inventory = client.GetGrain<IInventoryGrain>(key);

            // 等级/亲和备好，让"缺料"成为唯一的拦截原因
            await character.AddExpAsync(5_000_000);
            var allocated = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Strength = 70, Agility = 70, Health = 100, Intelligence = 130,
                Metal = 200, Wood = 200, Water = 200, Fire = 200, Earth = 200,
            });
            Assert.True(allocated.Success, allocated.Message);

            // 前置链：土行白·三式 + 黄·三式 + 玄·三式（玄要该域好感，好感此前已堆够）
            for (int grade = 1; grade <= 3; grade++)
            {
                for (int stage = 1; stage <= 3; stage++)
                    await EnsureLearnedAsync(skill, inventory, EarthRow * 10000 + grade * 100 + stage);
            }

            // 把灵材扣空：扣到 0 而不是"少给一点"，否则残留数量可能刚好够某一式
            var gateBeforeDrain = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            foreach (int material in AllMaterials)
            {
                int have = gateBeforeDrain.ItemCounts.TryGetValue(material, out var count) ? count : 0;
                if (have > 0)
                    Assert.True(await inventory.ConsumeByTemplateAsync(TestCharacterId, material, have),
                        $"扣除灵材 {material} ×{have} 应成功");
            }

            var emptyGate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            Assert.Equal(0, emptyGate.ItemCounts.TryGetValue(DiTierOneMaterial, out var still) ? still : 0);

            // 缺料必须被拒，且拒的是材料。
            // 状态守卫：角色状态是持久的，上一轮已经补齐并学到这一式之后再研读会先回"已习得此式"，
            // 材料门根本没机会说话——所以第一次运行才是这条负向断言的取证窗口，重跑时如实跳过。
            if (!(await skill.GetSkillsAsync()).Any(s => s.SkillId == DiFirst))
            {
                var refused = await LearnOnce(skill, inventory, DiFirst);
                Assert.False(refused.Success, "缺特殊材料时地阶不该学得");
                Assert.Contains("材料", refused.Message);

                // 判定失败不许白吃材料：一门都没添，扣完仍是空
                var afterRefusal = await inventory.BuildLearnGateContextAsync(TestCharacterId);
                Assert.Equal(0, afterRefusal.ItemCounts.TryGetValue(DiTierOneMaterial, out var leftover) ? leftover : 0);
            }

            // 补料后放行（地·初只要求粗炼那一味；给足到该式需求之上）
            Assert.True(await inventory.AddItemAsync(DiTierOneMaterial, 20, 0));
            await EnsureLearnedAsync(skill, inventory, DiFirst, "补齐特殊材料后地阶应能习得");

            var learned = await skill.GetSkillsAsync();
            Assert.Contains(learned, s => s.SkillId == DiFirst);
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

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
/// 相克侧的真集群验证：破势夺元 + 镇岳护体行压过"防御最高行"自动口径（§5.2 / §5.3）。
/// <para>
/// 断言故意用一组**可判别**的元素，而不是"随便克一次看看成不成功"：
/// 火(4) 克 金(1)，但不克 水(3)。所以让护体行=水、把"防御最高行"当成金传进去，
/// 如果服务端读的是自动行就会破成功，读的是镇岳指定行就会破失败——
/// 只有后一种才是设计意图（玩家指定了盾，就不该被自动口径顶掉）。
/// </para>
/// <para>
/// 层的消耗同理：夺元只有一层可夺，第一次破势必须成功、第二次必须失败；
/// 若两次都成功，说明破势没真的扣层（"循环往复"就变成了一句口号）。
/// </para>
/// <para>与另外两条实机测试共用一次性角色，故并进同一 collection 串行。</para>
/// </summary>
[Collection("Wuxing live cluster")]
public class WuxingCounterLiveTests
{
    private const ulong TestCharacterId = 8;         // 开发库里的 "123" 号测试角色

    private const int Metal = 1, Water = 3, Fire = 4, Earth = 5;

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
    public async Task Live_BreakCycle_UsesTheChosenGuardRowAndConsumesOneLayer()
    {
        if (string.IsNullOrWhiteSpace(Redis)) return;      // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
            var inventory = client.GetGrain<IInventoryGrain>(key);

            // 镇岳只能指定"练过的那一行"，所以先把水行白·初拿到手（等级/亲和按需要一并备好）
            await character.AddExpAsync(500_000);
            await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Metal = 60, Wood = 60, Water = 60, Fire = 60, Earth = 60,
            });
            await EnsureLearnedAsync(skill, inventory, Water * 10000 + 101);

            var guard = await skill.SetGuardElementAsync(Water);
            // 冷却里的拒绝也要拿到"生效后的那一行"：结果字段回的是生效值，不是本次调用是否改了它
            Assert.Equal(Water, guard.GuardElement);

            // 基线必须自己定：角色状态是持久的，上一轮（演化测试推了 66 次周天）会留下残层。
            // 先把层夺空——护体行是水，用土克必然成立，一路夺到返回 false 就是 0 层。
            while (await skill.BreakCycleAsync(Earth, Water)) { }
            Assert.Equal(0, (await skill.GetSkillCatalogAsync()).LoopStack);

            // 只堆一层，好让"夺到"与"夺空"各对应一次调用
            Assert.True(await skill.GrantCycleAsync(), "周天推进应成功");
            Assert.Equal(1, (await skill.GetSkillCatalogAsync()).LoopStack);

            // 火克金、不克水：把自动口径的"防御最高行"当作金传进来
            var wrongElement = await skill.BreakCycleAsync(Fire, Metal);
            Assert.False(wrongElement, "火不克水：护体行是镇岳指定的水，破势不该成立");
            Assert.Equal(1, (await skill.GetSkillCatalogAsync()).LoopStack);   // 判负也不该白扣层

            var rightElement = await skill.BreakCycleAsync(Earth, Metal);
            Assert.True(rightElement, "土克水：破势应打在镇岳指定的护体行上");

            // 只有一层可夺：第二次必须落空，否则等于破势不扣层
            var second = await skill.BreakCycleAsync(Earth, Metal);
            Assert.False(second, "层已被夺走：同一层不该被连续夺两次");
            Assert.Equal(0, (await skill.GetSkillCatalogAsync()).LoopStack);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static async Task EnsureLearnedAsync(ISkillGrain skill, IInventoryGrain inventory, int skillId, int attempts = 12)
    {
        LearnSkillResponse? last = null;
        for (int i = 0; i < attempts; i++)
        {
            var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            last = await skill.LearnFromBookAsync((long)TestCharacterId, skillId, gate);
            if (last.Success || last.Message.Contains("已习得")) return;
            if (!last.Message.Contains("未成")) break;      // 只有掷签未中才值得重试
        }
        Assert.True(false, $"{skillId} 应能研读：{last?.Message}");
    }
}

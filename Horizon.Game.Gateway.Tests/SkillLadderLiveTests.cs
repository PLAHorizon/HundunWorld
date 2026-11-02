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
/// 五行阶梯的真集群验证（环境门控，同 <see cref="OrleansGrainLiveIntegrationTests"/>：
/// 未设 <c>HORIZON_ORLEANS_REDIS</c> 直接返回）。
/// <para>
/// 这里要证明的不是"判定函数算得对"（那是 <c>WuxingSkillTreeTests</c> 的纯函数层），
/// 而是<b>门槛在服务端真的拦人、真的放人</b>：等级、境界、前置式、区域好感四条轴
/// 依次在真实 Grain + 真实库上走一遍。§14 的动态条件如果只有单测证据，
/// 就等于"设计上很严，实机上没人验过"。
/// </para>
/// <para>
/// 用的是开发库里名为 <c>123</c> 的一次性测试角色（character_id=8），不动真人存档。
/// </para>
/// </summary>
[Collection("Wuxing live cluster")]        // 与演化实机测试共用一次性角色，必须串行
public class SkillLadderLiveTests
{
    private const ulong TestCharacterId = 8;         // 开发库里的 "123" 号测试角色

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
    public async Task Live_WuxingLadder_FavorGateIsTheRealBlockerForMidTierBooks()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString)) return;   // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var inventory = client.GetGrain<IInventoryGrain>(key);
            var favor = client.GetGrain<IFavorabilityGrain>(key);

            // 1) 等级轴：升到玄阶起等级（76）以上。这条以前根本做不到——全仓没有等级写入方。
            var leveled = await character.AddExpAsync(1_000_000);
            Assert.True(leveled.Success, leveled.Message);
            Assert.True(leveled.Level >= 76, $"应至少到玄阶起等级，实际 {leveled.Level}");

            var info = await character.GetCharacterInfoAsync(null);
            Assert.True(info.Level >= 76, "门槛读的是 CharacterInfo，状态里的等级必须同步上去");

            // 2) 亲和轴：五行各给一档，免得某一行的"领悟门槛"把测试卡在门槛之外。
            //    这条断言顺带钉住一个事实：等级带来的属性点是按级派生的（5 点/级），
            //    所以升等后必须有可分配的余量，否则亲和门槛会永远过不去。
            var allocated = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Metal = 80, Wood = 80, Water = 80, Fire = 80, Earth = 80,
            });
            Assert.True(allocated.Success, $"五行亲和分配应成功：{allocated.Message}");

            // 3) 挑一个"还没被喂过好感"的域：好感是按域累计且没有清零接口，
            //    写死域号会让第二次运行把"被好感挡下"断成反向。
            var byDomainNow = await favor.GetByDomainAsync();
            int domain = new[] { 4, 5, 2, 1 }.FirstOrDefault(d =>
                !byDomainNow.TryGetValue(d, out var have) || have < 20);

            int target = domain == 0 ? 4 : domain;      // 五个域都被喂过时，仍要把"开门"那半走完
            int baseOf = 800000 + target * 10000;       // 该域秘籍物品号段（只用于失败文案）

            // 4) 前置式：该域白·三式 + 黄·三式按顺序研读。玄·初 的 pre_skill_id 指向黄·极，
            //    所以这一步同时也在验"前置未习得"这条轴。研读会掷签，失败就重新装配判定输入再试。
            foreach (var skillId in new[] { 1, 2, 3 }.Select(stage => target * 10000 + 100 + stage)
                         .Concat(new[] { 1, 2, 3 }.Select(stage => target * 10000 + 200 + stage)))
            {
                var learned = await LearnWithRetries(skill, inventory, skillId);
                Assert.True(learned.Success, $"前置式 {skillId} 应能学到：{learned.Message}");
            }

            // 5) 好感轴：玄阶真正的门。先证明"这个域零好感时被拒，且拒的是好感"。
            int xuanFirst = target * 10000 + 301;
            if (domain != 0)
            {
                var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
                var blocked = await skill.LearnFromBookAsync((long)TestCharacterId, xuanFirst, gate);
                Assert.False(blocked.Success, $"{target} 域零好感时玄·初不该学得（号段 {baseOf}）");
                Assert.Contains("好感", blocked.Message);
            }

            // 6) 给该域攒够好感（玄·初要 20），门就该开。
            await favor.AddAsync(target, 20, $"test:ladder:{DateTime.UtcNow.Ticks}");
            var gateAfter = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            Assert.True(gateAfter.FavorByDomain.TryGetValue(target, out var favorNow) && favorNow >= 20,
                "装配口应把真实好感读进判定输入");

            var granted = await LearnWithRetries(skill, inventory, xuanFirst);
            Assert.True(granted.Success, $"补够好感后玄·初应能学到：{granted.Message}");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// 研读会按 <c>learn_success_rate</c> 掷签（设计如此：一卷书可能读不成）。
    /// 测试要断言的是"门能不能开"，不是签运，所以给它有界次重试；每次重试前重新装配判定输入，
    /// 免得用一份过期的好感/材料快照去判。
    /// </summary>
    private static async Task<LearnSkillResponse> LearnWithRetries(
        ISkillGrain skill, IInventoryGrain inventory, int skillId, int attempts = 8)
    {
        LearnSkillResponse? last = null;
        for (int i = 0; i < attempts; i++)
        {
            var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            last = await skill.LearnFromBookAsync((long)TestCharacterId, skillId, gate);
            // 角色状态是持久的：上一轮已经学过这一式时回"已习得此式"，对验证而言等价于成功
            if (!last.Success && last.Message.Contains("已习得")) last.Success = true;
            if (last.Success) return last;

            // 只剩"掷签未中"（参悟未成）可以继续重试；其它拒绝原因是门槛问题，重试没有意义，直接原样回给断言
            if (!last.Message.Contains("未成")) return last;
        }
        return last!;
    }
}

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
/// 两条五行实机测试共用同一个一次性角色（character_id=8），必须串行：
/// xUnit 默认并行跑不同 collection，两个测试同时改同一份角色状态时，
/// 冷却/习得顺序会互相插队，表现就是"断言读到的消息与这条测试自己的顺序不符"。
/// </summary>
[CollectionDefinition("Wuxing live cluster", DisableParallelization = true)]
public class WuxingLiveClusterCollection { }

/// <summary>
/// "五行相生演化 = 换代（母/子源技封存）"的真集群验证（环境门控，同 <see cref="SkillLadderLiveTests"/>）。
/// <para>
/// 这是整套方案的核心卖点，此前只有纯函数与客户端语义层的证据：D2 定的"演化后源技不可施放、
/// 散功自动解封"是**派生态**（由"有没有已学演化式把它列进 consume"算出来），
/// 派生态最容易出的错是"某一处判定读的是旧的持久标记"。只在真 Grain 上跑一遍才看得出
/// 学道阶 → 封两式 → 散功 → 两式回来 这条链是否真的闭合。
/// </para>
/// <para>
/// 用的是开发库里的一次性测试角色（<c>character_id=8</c>，名 "123"）。
/// 门槛所需的好感/材料/属性一律"给足"而不是"照表算"：这条测试要证的是演化机制本身，
/// 数值节奏由 <c>WuxingSkillTreeTests</c> 的纯函数层钉住。
/// </para>
/// </summary>
[Collection("Wuxing live cluster")]
public class SkillEvolutionLiveTests
{
    private const ulong TestCharacterId = 8;

    // 道阶 pair 1 = 金生水：母行金(1) 的子行水(3)。consume 是两行的天阶三式。
    private static readonly int[] MotherRow = { 10501, 10502, 10503 };     // 金·天·初/中/极
    private static readonly int[] ChildRow = { 30501, 30502, 30503 };      // 水·天·初/中/极
    private const int DaoSkill = 70101;                                    // 道阶·初（金水演化式）
    private const int DaoMaxStage = 70103;                                 // 同系三境，测试只用初式

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
    public async Task Live_EvolveToDaoSealsBothSourceRowsAndUnlearnRestoresThem()
    {
        if (string.IsNullOrWhiteSpace(Redis)) return;      // 环境门控

        var (host, client) = await ConnectAsync();
        try
        {
            var key = GrainKeys.ForEntity(TestCharacterId);
            var character = client.GetGrain<ICharacterGrain>((long)TestCharacterId);
            var skill = client.GetGrain<ISkillGrain>(key);
            var inventory = client.GetGrain<IInventoryGrain>(key);
            var favor = client.GetGrain<IFavorabilityGrain>(key);

            // 1) 顶到阶梯之上：道阶门槛 Lv300，属性点池按 5/级 派生，够分配亲和与四维。
            var leveled = await character.AddExpAsync(5_000_000);
            Assert.True(leveled.Success, leveled.Message);
            Assert.Equal(300, leveled.Level);              // 曲线顶=300（§2.1 道阶起等级）

            var allocated = await character.AllocateAttributeAsync(TestCharacterId, new AttributeAllocationData
            {
                Strength = 60, Agility = 60, Health = 90, Intelligence = 120,
                Metal = 200, Wood = 200, Water = 200, Fire = 200, Earth = 200,
            });
            Assert.True(allocated.Success, allocated.Message);

            // 2) 两行的好感与各阶材料给足（玄/地/天/道 的 favor 与灵材需求都在下面这些数里）
            //    好感必须分多个来源累加：FavorabilityGrain 对"单一目标"有默认 20 点上限
            //    （防"逮着一个 NPC 刷满一域"），一个 key 递 120 只会记进 20。
            foreach (int domain in new[] { 1, 3 })
            {
                for (int npc = 0; npc < 8; npc++)
                {
                    await favor.AddAsync(domain, 20, $"test:evolve:{domain}:{npc}");
                }
                Assert.True(await inventory.AddItemAsync(910000 + domain, 60, 0));      // 粗炼灵材 T1
                Assert.True(await inventory.AddItemAsync(910005 + domain, 60, 0));      // 提炼灵材 T2
            }

            // 3) 两行各自从白阶一路研读上天阶（前置链按 阶×境 顺序走）
            foreach (int row in new[] { 1, 3 })
            {
                foreach (int grade in new[] { 1, 2, 3, 4, 5 })
                {
                    foreach (int stage in new[] { 1, 2, 3 })
                    {
                        int skillId = row * 10000 + grade * 100 + stage;
                        await EnsureLearnedAsync(skill, inventory, skillId);
                    }
                }
            }

            // 4) 演化源必须"练满"（§6.2 的圆满 = Level==MaxLevel），升级接口无代价，循环到上限
            foreach (int sourceId in MotherRow.Concat(ChildRow))
            {
                for (int i = 0; i < 30; i++)
                {
                    if (!await skill.UpgradeSkillAsync(sourceId)) break;
                }
            }

            // 5) 周天圆满次数：§6.2 的计数门槛。一轮 = 五行各一层（CycleMaxStack=5），
            //    道阶·初要 12 次圆满 → 至少 12×5 次推进，再留一轮余量防边界。
            for (int i = 0; i < 66; i++)
            {
                Assert.True(await skill.GrantCycleAsync(), "周天推进应成功");
            }

            // 6.前置) 把与本片源式重叠的相邻两对先散掉：封印是"已学演化式 consume 的并集"，
            // 而相生环上每一式都被两对共用（10501 同属 pair1 与 pair5，30501 同属 pair1 与 pair2）。
            // 只要邻对还学着，"散功即解封"就观察不到——那是结构，不是 bug。
            // 所以这条测试自己把前置摆正，而不是赌别的测试有没有跑过、按什么顺序跑。
            foreach (int neighbor in new[] { 70201, 70501 })
                await skill.UnlearnAsync((long)TestCharacterId, neighbor);

            // 6) 学道阶：consume 齐、练满、计数够，门就该开
            await EnsureLearnedAsync(skill, inventory, DaoSkill, "应能由金水相生演化");

            // 7) 换代：只有"被已学演化式融合的那些式"不可施放。
            //    封存集合按**规则**算，不按快照写死：它就是"已学演化式的 consume 并集"这一派生态，
            //    而同一个角色上还有别的实机测试会学到别的演化式（全量跑时顺序不固定）——
            //    写死"某式一定没被封"会在对方跑过之后假失败，写死"整行都该封"则是过度封锁。
            var sealedNow = await SealedSetAsync(skill);
            Assert.Contains(10501, sealedNow);
            await EnsureCastRefusal(skill, 10501, "散功");
            if (sealedNow.Contains(30501)) await EnsureCastRefusal(skill, 30501, "散功");
            foreach (int probe in new[] { 10502, 30502 })
                if (!sealedNow.Contains(probe)) await EnsureCastAllowed(skill, probe);

            // 8) 散功即解封：删掉演化式，源技自动回到可用（封存是派生态，不是持久标记）
            Assert.True(await skill.UnlearnAsync((long)TestCharacterId, DaoSkill), "散功道阶应成功");
            Assert.DoesNotContain(10501, await SealedSetAsync(skill));
            await EnsureCastAllowed(skill, 10501);

            // 9) 散功不该把玩家困死：条件仍在（源式都练满、周天次数只增不减），应能重新演化
            await EnsureLearnedAsync(skill, inventory, DaoSkill, "散功后应能重新演化");
            await EnsureCastRefusal(skill, 10501, "散功");
            await EnsureCastRefusal(skill, 30501, "散功");

        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// 施放一次，断言"被某句话挡下"。先清冷却：冷却是残留状态，不清的话它会比封存判定先返回，
    /// 测试就会把"上一轮的冷却"误读成"这一轮没封存"。
    /// </summary>
    private static async Task EnsureCastRefusal(ISkillGrain skill, int skillId, string expectedInMessage)
    {
        await skill.ResetSkillCooldownAsync(skillId);
        var cast = await skill.CastSkillAsync(new SkillCastMessage
        {
            CasterId = TestCharacterId,
            SkillId = skillId,
            TargetEntityId = 1,
        });
        Assert.False(cast.Success, $"技能 {skillId} 不该施放成功");
        Assert.Contains(expectedInMessage, cast.Message);
    }

    /// <summary>
    /// 施放一次，断言"没被封存牵连"。这里允许因冷却/目标范围等别的原因被拒——
    /// 这条测试的口径只有"封存与否"，把别的原因算成失败就是越界断言。
    /// </summary>
    private static async Task EnsureCastAllowed(ISkillGrain skill, int skillId)
    {
        await skill.ResetSkillCooldownAsync(skillId);
        var cast = await skill.CastSkillAsync(new SkillCastMessage
        {
            CasterId = TestCharacterId,
            SkillId = skillId,
            TargetEntityId = 1,
        });
        Assert.DoesNotContain("散功", cast.Message);
    }

    /// <summary>
    /// 把某一式读到"已习得"为止。两种情况都算达成：这次研读成功，或者上一轮运行已经学过
    /// （重复研读会回"已习得此式"，对验证而言等价——否则这条测试每跑一次就自断一次，
    /// 因为角色状态是持久的）。只有掷签未中才重试，其它拒绝原因原样抛给断言。
    /// </summary>
    private static async Task EnsureLearnedAsync(
        ISkillGrain skill, IInventoryGrain inventory, int skillId, string? what = null, int attempts = 10)
    {
        LearnSkillResponse? last = null;
        for (int i = 0; i < attempts; i++)
        {
            var gate = await inventory.BuildLearnGateContextAsync(TestCharacterId);
            last = await skill.LearnFromBookAsync((long)TestCharacterId, skillId, gate);
            if (last.Success || last.Message.Contains("已习得")) return;
            // 掷签未中（"参悟未成…此卷已尽"）可以再来一次：真正消耗书卷是在背包咽喉里做的，
            // 这条测试直接问技能 grain，所以重试不花书。其它拒绝原因都是门槛问题，重试无意义。
            if (!last.Message.Contains("未成")) break;
        }
        Assert.True(false, $"{skillId} {what ?? "应能研读"}：{last?.Message}");
    }

    /// <summary>
    /// 服务端那套"派生封存集合"的测试侧对应实现：已学演化式的 consume 并集。
    /// 刻意从目录下行里读模板的 learn_cost 自己算，而不是抄一串常量——
    /// 抄常量就是把机制的第二份定义写进测试，机制改了测试还会绿。
    /// </summary>
    private static async Task<HashSet<int>> SealedSetAsync(ISkillGrain skill)
    {
        var catalog = await skill.GetSkillCatalogAsync();
        var templates = new Dictionary<int, string>();
        foreach (var t in catalog.Templates)
            if (t != null) templates[t.SkillId] = t.LearnCostJson;

        var sealedSet = new HashSet<int>();
        foreach (var learned in catalog.Learned)
        {
            if (learned == null || !templates.TryGetValue(learned.SkillId, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!doc.RootElement.TryGetProperty("consume", out var consume)) continue;
                if (consume.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var item in consume.EnumerateArray())
                    if (item.ValueKind == System.Text.Json.JsonValueKind.Number && item.TryGetInt32(out var src))
                        sealedSet.Add(src);
            }
            catch (System.Text.Json.JsonException) { /* 脏learn_cost 由服务端负责拒绝，这里只忽略 */ }
        }
        return sealedSet;
    }

}

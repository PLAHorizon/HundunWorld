using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.Sim.Skills;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 91 式效果数据的**键契约**：数据里出现的每个键都必须在 <see cref="WuxingSkillEffects"/> 里登记角色，
/// 且登记的键都不能是已经没人用的孤儿。
/// <para>
/// 这条测试不实现任何效果，它做的是更基础的一件事：**把"数据说了但引擎没做"从静默变成记账**。
/// 普查结果（2026-10-06，按单词边界在四个服务端工程与客户端工程里逐键匹配读者）：
/// 69 个键里引擎真的结算的只有 <c>pen</c> 一个，门槛类 4 个、表现类 5 个，其余 59 个无人结算。
/// 也就是说技能详情与 <c>skill_effects</c> 里写的定身/眩晕/DOT/治疗/护盾/反射……目前不会在游戏里发生。
/// </para>
/// <para>
/// 为什么值得钉：这类落差的症状不是报错，而是"照着说明玩、发现效果没出现"。
/// 有了这张表，将来实现一条效果就把那一行从 <see cref="EffectRole.Unresolved"/> 挪到
/// <see cref="EffectRole.Applied"/>，账目始终对得上；反过来，往种子里加一个新键而忘了登记，这里会直接红。
/// </para>
/// </summary>
public class WuxingSkillEffectContractTests
{
    private const string SeedFile = @"scripts/sql/011_seed_wuxing_skill_tree.sql";

    /// <summary>
    /// 取每式那行效果 JSON。判据是通用键 <c>rangeType</c>（91 行全都有），
    /// 所以只会在 <c>skill_effects</c> 这一段命中，不会误抓奖励/门槛那种 JSON。
    /// </summary>
    private static List<string> ReadEffectBlobs()
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), SeedFile));
        return Regex.Matches(text, @"\{[^{}]*""rangeType""[^{}]*\}")
            .Select(m => m.Value)
            .ToList();
    }

    [Fact]
    public void SeedActuallyCarriesTheExpectedRowShape()
    {
        var blobs = ReadEffectBlobs();
        // 少于 91 行说明种子被改过或正则锚错了：那这条契约测试就退化成空跑（本仓反复踩过"闸门锚错处"）
        Assert.True(blobs.Count >= 91,
            $"只从 011 里解出 {blobs.Count} 行效果 JSON（期望 ≥91）：锚点或种子形状变了，这条测试正在空跑");
    }

    [Fact]
    public void EveryKeyInSeedDataIsRegistered()
    {
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var blob in ReadEffectBlobs())
            foreach (var key in WuxingSkillEffects.UnknownKeysIn(blob))
                unknown.Add(key);

        Assert.True(unknown.Count == 0,
            $"这些效果键出现在数据里但没有登记角色：{string.Join(", ", unknown)}。" +
            "去 WuxingSkillEffects.Roles 里归类（Applied/Gate/Presentation/Unresolved），" +
            "登记成 Unresolved 就是公开挂账——不登记等于让引擎静默忽略一条玩家能看到的说明");
    }

    [Fact]
    public void NoRegisteredKeyIsAnOrphan()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var blob in ReadEffectBlobs())
            foreach (var key in WuxingSkillEffects.KeysIn(blob))
                used.Add(key);

        var orphans = WuxingSkillEffects.KnownKeys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(orphans.Count == 0,
            $"登记表里有这些键在种子数据中一次都没出现：{string.Join(", ", orphans)}。" +
            "要么数据改名了（那两边的名字得对上），要么登记多余——留着只会让下一个人以为有这条效果");
    }

    [Fact]
    public void UnresolvedListIsHonestAboutWhatTheEngineDoesNotDo()
    {
        // 这三类最容易被当成"已经存在"：控制 / 持续伤害 / 护盾。
        // 现在它们没人结算，所以必须挂在 Unresolved 上；将来谁实现了却没挪，这条会红（防止账目谎报）。
        foreach (var key in new[] { "stunSec", "rootSec", "slowSec", "dotRatio", "shieldRatio", "dispelSec" })
        {
            Assert.Equal(EffectRole.Unresolved, WuxingSkillEffects.RoleOf(key));
        }

        // Applied 集合要**逐名钉死**，不能只断"非空"：否则实现一条效果却忘了登记，
        // 或者反过来把一个只是表现的东西自称"已结算"，这条都不会响。
        Assert.Equal(new[] { "healPct", "hpCost", "pen" }, WuxingSkillEffects.AppliedKeys.ToArray());
        Assert.All(WuxingSkillEffects.AppliedKeys, k => Assert.Equal(EffectRole.Applied, WuxingSkillEffects.RoleOf(k)));

        // 键总数钉成"数据里出现过的去重键数"：字典初始化器里同名键是**静默覆盖**（我已踩两次），
        // 覆盖会让条目数少 1，而"全登记/无孤儿"两条都照样绿 —— 只有这个总数能抓住它。
        var distinctDataKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var blob in ReadEffectBlobs())
            foreach (var key in WuxingSkillEffects.KeysIn(blob))
                distinctDataKeys.Add(key);
        Assert.Equal(distinctDataKeys.Count, WuxingSkillEffects.KnownKeys.Count);
        Assert.True(distinctDataKeys.Count >= 69,
            $"数据里的去重效果键只剩 {distinctDataKeys.Count} 个（普查时是 69）：数据或解析锚点变了");
    }

    /// <summary>
    /// 挂在 Applied 上的每个键都要有服务端代码撑着（防账本与实现走岔）。
    /// 名字锚在**当前实现**的方法与字面量上：改名/搬走时这条会红，逼着人来同步账本，
    /// 而不是让 "Applied" 变成一个没人维护的自我声明。
    /// </summary>
    [Fact]
    public void AppliedKeysAreBackedByActualServerCode()
    {
        var grain = File.ReadAllText(Path.Combine(FindRepoRoot(), @"Horizon.Orleans.Grains\SkillGrain.cs"));
        Assert.Contains("ApplySkillHealthFlowAsync", grain);
        Assert.Contains("\"healPct\"", grain);
        Assert.Contains("\"hpCost\"", grain);
        Assert.Contains("RequestHpChangeAsync", grain);
    }

    [Fact]
    public void NotAppliedKeysReportTheWholeGapForOneRow()
    {
        // 拿一条真有控制与持续伤害的行（10103 万剑归宗类：pen + 形状键），看未结算清单是否完整报出
        var json = "{\"pen\":0.15,\"rangeType\":\"Sector\",\"aoeRadius\":6,\"maxTargets\":3,\"dotRatio\":0.2,\"stunSec\":1}";

        var gaps = WuxingSkillEffects.NotAppliedKeysIn(json);

        Assert.Contains("dotRatio", gaps);
        Assert.Contains("stunSec", gaps);
        Assert.DoesNotContain("pen", gaps);              // 唯一真结算的那条
        Assert.DoesNotContain("rangeType", gaps);         // 表现类不算"未结算的伤害数值"
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}

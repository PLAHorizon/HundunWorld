using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;
using Xunit;
using Xunit.Abstractions;
using MonsterTextsExport = Horizon.MonsterCli.MonsterTextsExport;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 怪物编辑器 → 内容快照 → 运行期内核 这条链的可执行断言。
/// <para>
/// 三条"必须存在"的理由：
/// ① 让分曲线（灰名断供、随玩家等级反比例衰减）是**数值承诺**，只靠肉眼读代码会漂；
///    参照实现按定义**另写一份暴力版**，与内核互比才算测过（新旧同错抓不出来）。
/// ② 内容快照的装配/校验必须被真跑一次：文件读到了不等于内容装上了，
///    而"没装上"的表现是所有怪都退回 Lv1/100 血。
/// ③ AI v2 与 v1 的边界必须被钉住：能力位全关时 v2 要逐字复现 v1 的位移与状态序列，
///    否则"接入数据表顺带改了手感"这件事永远没人发现。
/// </para>
/// </summary>
[Collection("monster-content")]
public class MonsterEditorKernelTests
{
    private readonly ITestOutputHelper _log;

    public MonsterEditorKernelTests(ITestOutputHelper log) => _log = log;

    private static string RepoRoot()
    {
        // 测试宿主的工作目录是二进制目录，向上找 .git 所在处。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Fixture(string name) => Path.Combine(RepoRoot(), "HundunWorld", "Data", "Monsters", name);

    // ---------------- ① 让分曲线 ----------------

    /// <summary>
    /// 独立参照：完全按逆向规格的文字定义重算一遍（不用内核的任何函数、不做共享的钳位助手）。
    /// </summary>
    private static double ReferenceConcession(int mobLevel, int playerLevel)
    {
        if (playerLevel < 1) playerLevel = 1;
        // 灰名等级（境界分段表按逆向文档 §2.1 的设计口径，抄一份字面数字而不是调用内核）
        int[] starts = { 1, 1, 31, 76, 131, 201, 300 };
        int[] grayGap = { 5, 5, 6, 7, 9, 10, 12 };
        int[] zdTable = { 5, 6, 8, 11, 15, 20, 24 };
        var stage = 0;
        for (var i = 0; i < starts.Length; i++) if (playerLevel >= starts[i]) stage = i;
        var gray = playerLevel <= 5 ? 0 : Math.Max(0, playerLevel - grayGap[stage]);
        if (mobLevel <= gray) return 0.0;
        if (mobLevel > playerLevel)
        {
            var over = Math.Min(mobLevel - playerLevel, 4);
            return 1.0 + 0.05 * over;
        }
        var gap = playerLevel - mobLevel;
        var factor = 1.0 - (double)gap / zdTable[stage];
        return factor < 0.0 ? 0.0 : factor;
    }

    [Fact]
    public void 让分曲线与独立参照在全部等级组合上一致()
    {
        var mismatches = new List<string>();
        var compared = 0;
        for (var playerLevel = 1; playerLevel <= 300; playerLevel += 1)
        {
            for (var delta = -20; delta <= 15; delta++)
            {
                var mobLevel = playerLevel + delta;
                if (mobLevel < 1 || mobLevel > 300) continue;
                compared++;
                var kernel = MonsterDifficultyCore.ConcessionFactor(mobLevel, playerLevel);
                var reference = ReferenceConcession(mobLevel, playerLevel);
                if (Math.Abs(kernel - reference) > 1e-9)
                    mismatches.Add($"我{playerLevel} 怪{mobLevel}: 内核{kernel} vs 参照{reference}");
            }
        }
        _log.WriteLine($"比对了 {compared} 组等级组合");
        Assert.True(compared > 8000, $"参照覆盖面过小（{compared}）——解析器/循环什么都没跑到");
        Assert.Empty(mismatches);
    }

    [Fact]
    public void 灰名边界是硬断供而不是渐近趋零()
    {
        // WoW 的真实形状：灰名那一档直接 0，边界处有可见落差。
        // 这条钉的是"断供发生在灰名边界"，不是"曲线很陡"——后者用 >0 之类的弱判据会放过反向实现。
        var playerLevel = 20;
        var gray = MonsterDifficultyCore.GrayLevel(playerLevel);
        Assert.Equal(MonsterDifficulty.Gray, MonsterDifficultyCore.Of(gray, playerLevel));
        Assert.Equal(0.0, MonsterDifficultyCore.ConcessionFactor(gray, playerLevel));
        Assert.Equal(MonsterDifficulty.Green, MonsterDifficultyCore.Of(gray + 1, playerLevel));
        Assert.True(MonsterDifficultyCore.ConcessionFactor(gray + 1, playerLevel) > 0.0);
    }

    [Fact]
    public void 越级奖励封顶在四点差且上限一点二倍()
    {
        Assert.Equal(1.2, MonsterDifficultyCore.HighGapBonus(64, 60), 9);
        Assert.Equal(1.2, MonsterDifficultyCore.HighGapBonus(200, 60), 9);
        Assert.Equal(1.05, MonsterDifficultyCore.HighGapBonus(61, 60), 9);
        Assert.Equal(1.0, MonsterDifficultyCore.HighGapBonus(60, 60), 9);
    }

    [Fact]
    public void 难度色与经验断供是同一个判据()
    {
        // 全工程只许有一份：如果两处分别实现，这里就会出现"名字黄了却零经验"的组合。
        for (var playerLevel = 1; playerLevel <= 300; playerLevel += 7)
        {
            for (var delta = -25; delta <= 12; delta++)
            {
                var mobLevel = playerLevel + delta;
                if (mobLevel < 1) continue;
                var difficulty = MonsterDifficultyCore.Of(mobLevel, playerLevel);
                var factor = MonsterDifficultyCore.ConcessionFactor(mobLevel, playerLevel);
                Assert.Equal(difficulty == MonsterDifficulty.Gray, factor == 0.0);
            }
        }
    }

    [Fact]
    public void 境界表与角色等级曲线的分段序列同源()
    {
        // MonsterDifficultyCore 抄了 ProgressionCurve 的起始等级；两份清单一旦分叉，
        // "灰名窗口按境界走"就会在某个境界上静默错位。
        var expected = new[] { 1, 1, 31, 76, 131, 201, 300 };
        Assert.Equal(expected, MonsterDifficultyCore.StageStartLevels);
        Assert.Equal(300, MonsterDifficultyCore.MaxPlayerLevel);
        foreach (var stage in expected)
        {
            Assert.Equal(System.Array.IndexOf(expected, stage) >= 0 ? stage : 1, stage);
        }
    }

    [Fact]
    public void 经验定基用怪物等级且精英按档位放大()
    {
        var cfg = MonsterXpConfig.Default;
        // 同级普通怪：45 + 5×20 = 145，让分 1.0
        Assert.Equal(145L, MonsterXpCore.Compute(cfg, 20, 20, MonsterRank.Normal));
        // 精英 ×2
        Assert.Equal(290L, MonsterXpCore.Compute(cfg, 20, 20, MonsterRank.Elite));
        // 高 6 级只按高 4 级算：基准由**怪等级**定（45+5×26=175），×1.2 → 210
        Assert.Equal(210L, MonsterXpCore.Compute(cfg, 26, 20, MonsterRank.Normal));
        // 反证：若误用玩家等级定基，这里会是 (45+5×20)×1.2 = 174
        Assert.NotEqual(174L, MonsterXpCore.Compute(cfg, 26, 20, MonsterRank.Normal));
        // 定基口径：怪等级从 20 升到 21，基准随之上升（若误用玩家等级定基，这里不会变）
        Assert.NotEqual(MonsterXpCore.Compute(cfg, 20, 20, MonsterRank.Normal), MonsterXpCore.Compute(cfg, 21, 20, MonsterRank.Normal));
    }

    [Fact]
    public void 参与者按贡献时刻优先且截断到五人()
    {
        var participants = new[]
        {
            new MonsterXpParticipant { CharacterId = 3, Level = 20, LastContributionTick = 100 },
            new MonsterXpParticipant { CharacterId = 1, Level = 20, LastContributionTick = 300 },
            new MonsterXpParticipant { CharacterId = 2, Level = 20, LastContributionTick = 200 },
            new MonsterXpParticipant { CharacterId = 5, Level = 20, LastContributionTick = 200 },
            new MonsterXpParticipant { CharacterId = 4, Level = 20, LastContributionTick = 50 },
            new MonsterXpParticipant { CharacterId = 6, Level = 20, LastContributionTick = 10 },
        };
        MonsterXpCore.SortParticipantsByPriority(participants);
        Assert.Equal(1UL, participants[0].CharacterId);   // 最新
        Assert.Equal(2UL, participants[1].CharacterId);   // 同刻时 id 小的在前
        Assert.Equal(5UL, participants[2].CharacterId);

        var xp = new long[MonsterXpCore.MaxParticipants];
        var granted = MonsterXpCore.ComputeForParticipants(MonsterXpConfig.Default, 20, MonsterRank.Normal, participants, xp);
        Assert.Equal(5, granted);                          // 第 6 人被截断
        Assert.All(xp.Take(5), v => Assert.Equal(145L, v));
    }

    [Fact]
    public void 灰名参与者拿不到经验且不报错()
    {
        var participants = new[]
        {
            new MonsterXpParticipant { CharacterId = 1, Level = 300, LastContributionTick = 10 },
        };
        var xp = new long[MonsterXpCore.MaxParticipants];
        var granted = MonsterXpCore.ComputeForParticipants(MonsterXpConfig.Default, 8, MonsterRank.Normal, participants, xp);
        Assert.Equal(1, granted);
        Assert.Equal(0L, xp[0]);   // 300 级玩家打 Lv8：灰名断供
    }

    /// <summary>
    /// 独立参照：把 AzerothCore `KillRewarder` 分组那一段**另写一遍**
    /// （rate 的字面值、等级和当分母、有人灰名则减半、减半后 +1），
    /// 定基口径按决策 D3 换成怪物等级，其余形状逐字照抄。内核的组队分配与它对拍。
    /// </summary>
    private static long[] ReferenceGroupShares(int mobLevel, int[] memberLevels, double rankScale)
    {
        var count = Math.Min(memberLevels.Length, MonsterXpCore.MaxParticipants);
        double rate = count <= 2 ? 1.0 : count == 3 ? 1.166 : count == 4 ? 1.3 : 1.4;
        var poolLevel = 0;
        var maxLevel = 0;
        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            var lvl = memberLevels[i];
            sum += lvl;
            if (lvl > maxLevel) maxLevel = lvl;
            if (ReferenceConcession(mobLevel, lvl) > 0.0 && lvl > poolLevel) poolLevel = lvl;
        }
        var shares = new long[count];
        if (poolLevel == 0) return shares;                       // 全队灰名：这只怪根本不存在经验池
        var pool = (long)Math.Round((45 + 5 * mobLevel) * ReferenceConcession(mobLevel, poolLevel) * rankScale,
                                    MidpointRounding.AwayFromZero);
        var full = maxLevel == poolLevel;
        for (var i = 0; i < count; i++)
        {
            var lvl = memberLevels[i];
            if (lvl > poolLevel) continue;                        // 这个人已经灰名
            var factor = rate * lvl / sum;
            shares[i] = full ? (long)(pool * factor) : (long)(pool * factor / 2.0) + 1;
        }
        return shares;
    }

    private static MonsterXpParticipant[] GroupOf(params int[] levels)
    {
        var arr = new MonsterXpParticipant[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            arr[i] = new MonsterXpParticipant
            {
                CharacterId = (ulong)(i + 1), Level = levels[i],
                LastContributionTick = 1000 - i,      // 故意让"等级序"与"贡献序"不同，排序后仍能对上回执
            };
        }
        return arr;
    }

    [Fact]
    public void 五人组队打一只怪拿的是同一份池子而不是五份全额()
    {
        // 这是"组队刷怪"最核心的经济判据：AC 里 5 人队的总和对单人只多 40%（rate 1.4），
        // 而每人只拿到 pool×1.4×自己等级/等级和。以前本作是"每人一份全额"＝五倍，
        // 玩家的观感是"组队打怪经验白涨五份"，而低级号在队里毫无成本。
        var cfg = MonsterXpConfig.Default;                     // 45 + 5×怪等级
        var participants = GroupOf(20, 20, 20, 20, 20);
        var xp = new long[MonsterXpCore.MaxParticipants];
        var granted = MonsterXpCore.ComputeGroupShares(cfg, 20, MonsterRank.Normal, participants, xp, out var receipt);
        Assert.Equal(5, granted);

        var pool = MonsterXpCore.Compute(cfg, 20, 20, MonsterRank.Normal);
        Assert.Equal(145L, pool);                               // 手算对照：45+5×20，同级让分 1.0
        Assert.All(xp.Take(5), v => Assert.Equal(40L, v));       // 145 × 1.4 × 20/100 = 40.6 → 截断 40
        Assert.Equal(200L, xp.Take(5).Sum());
        Assert.True(xp.Take(5).Sum() < 5 * pool,
            $"五人总共拿了 {xp.Take(5).Sum()}，全额是 {5 * pool}——说明又在按「每人一份全额」发");
        Assert.Equal(1.4, receipt.Rate, 3);
        Assert.Equal(20, receipt.PoolLevel);
        Assert.False(receipt.Halved);
    }

    [Fact]
    public void 队里有人已经灰名则其余人减半而那个人拿零()
    {
        // AC 的 `_isFullXP`：只要队里有一个成员对这只怪已经灰名，其余人拿到的**再减半**；
        // 灰名那个人自己拿 0。玩家观感："队里带了个低级号，我们全组经验打折"——
        // 这条以前完全不存在，带小号没有任何代价，也没有"低级号拿不到"的表现。
        var cfg = MonsterXpConfig.Default;
        var participants = GroupOf(20, 250);                    // 250 级玩家打 Lv20 怪＝灰名
        var xp = new long[MonsterXpCore.MaxParticipants];
        var granted = MonsterXpCore.ComputeGroupShares(cfg, 20, MonsterRank.Normal, participants, xp, out var receipt);
        Assert.Equal(2, granted);

        var reference = ReferenceGroupShares(20, new[] { 20, 250 }, 1.0);
        Assert.Equal(reference[0], xp[0]);
        Assert.Equal(reference[1], xp[1]);
        Assert.True(receipt.Halved, "有人灰名却没减半——AC 那条 _isFullXP 判据没落地");
        Assert.Equal(1, receipt.GrayExcluded);
        Assert.Equal(20, receipt.PoolLevel);                    // 池子按"最高的那个没灰名的人"定
        Assert.True(xp[0] > 0, "没灰名那个人应该还有份（减半≠归零）");
    }

    [Fact]
    public void 单人击杀在组队公式里退化出与全额完全相同的数()
    {
        // 这条是**回归保护**：加了组队分配之后，单人的数字必须一个都不动
        // （端到端用例与既有让分用例钉的就是单人值）。rate(1)=1、分母=自己等级 → 因子恒为 1。
        var cfg = MonsterXpConfig.Default;
        for (var playerLevel = 1; playerLevel <= 60; playerLevel++)
        {
            for (var delta = -12; delta <= 8; delta++)
            {
                var mobLevel = playerLevel + delta;
                if (mobLevel < 1 || mobLevel > 300) continue;
                var solo = MonsterXpCore.Compute(cfg, mobLevel, playerLevel, MonsterRank.Normal);
                var xp = new long[MonsterXpCore.MaxParticipants];
                var granted = MonsterXpCore.ComputeGroupShares(cfg, mobLevel, MonsterRank.Normal,
                    GroupOf(playerLevel), xp, out _);
                Assert.Equal(1, granted);
                Assert.Equal(solo, xp[0]);
            }
        }
    }

    [Fact]
    public void 组队分配与独立参照在各种人数与等级组合上一致()
    {
        // 参照是另写的（上面那个函数），不是"用内核再算一遍"——新旧同错抓不出来。
        var cfg = MonsterXpConfig.Default;
        var mismatches = new List<string>();
        var compared = 0;
        var levelSets = new[]
        {
            new[] { 20 },
            new[] { 20, 20 },
            new[] { 10, 20 },
            new[] { 18, 20, 25 },
            new[] { 20, 30, 40, 50 },
            new[] { 20, 20, 20, 20, 20 },
            new[] { 5, 20, 35, 50, 60 },
            new[] { 1, 2, 3, 4, 5, 6, 7 },           // 超过上限：必须只取前 5（排序由内核负责，参照按同一份长度算）
            new[] { 250, 250 },
            new[] { 300, 8 },
        };
        foreach (var mobLevel in new[] { 8, 20, 26, 45, 60, 120 })
        {
            foreach (var levels in levelSets)
            {
                var participants = GroupOf(levels);
                MonsterXpCore.SortParticipantsByPriority(participants);          // 与宿主同一个前置纪律
                var counted = Math.Min(levels.Length, MonsterXpCore.MaxParticipants);
                var xp = new long[MonsterXpCore.MaxParticipants];
                var granted = MonsterXpCore.ComputeGroupShares(cfg, mobLevel, MonsterRank.Normal, participants, xp, out _);
                Assert.Equal(counted, granted);
                var sortedLevels = new long[counted];
                for (var i = 0; i < counted; i++) sortedLevels[i] = participants[i].Level;
                var reference = ReferenceGroupShares(mobLevel, Array.ConvertAll(sortedLevels, v => (int)v), 1.0);
                for (var i = 0; i < counted; i++)
                {
                    compared++;
                    if (xp[i] != reference[i])
                        mismatches.Add($"怪{mobLevel} 队[{string.Join(",", sortedLevels)}] 第{i}人: 内核{xp[i]} vs 参照{reference[i]}");
                }
            }
        }
        _log.WriteLine($"组队分配比对了 {compared} 个人次");
        Assert.True(compared > 60, $"覆盖面过小（{compared} 人次）——这条参照对拍等于没跑");
        Assert.Empty(mismatches);
    }

    [Fact]
    public void 全队都灰名时经验池根本不存在而回执说实话()
    {
        var cfg = MonsterXpConfig.Default;
        var participants = GroupOf(300, 280);
        var xp = new long[MonsterXpCore.MaxParticipants];
        var granted = MonsterXpCore.ComputeGroupShares(cfg, 8, MonsterRank.Normal, participants, xp, out var receipt);
        Assert.Equal(2, granted);
        Assert.Equal(0L, xp[0]);
        Assert.Equal(0L, xp[1]);
        Assert.Equal(0, receipt.PoolLevel);                     // 没有"没灰名的队员" ⇒ 没有池子
        Assert.Equal(2, receipt.GrayExcluded);
        Assert.Equal(0L, receipt.TotalGranted);
    }

    [Fact]
    public void DS里三人打死的怪只发一份池子而下线那个人不进分母()
    {
        // 上面几条测的是内核算得对不对；这条测**接线**：真两个以上玩家在同一只怪上贡献过伤害时，
        // DS 发的 KillXp 事件必须是"共享一份池子"，而不是过去那样"每人一份全额"。
        // 玩家侧看得见的数字：三人同队打一只 95 经验的怪，总共拿 108（含 1.166 的人数加成），
        // 而不是 285。
        const int templateId = 12461;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 9, false, 0f);   // 被动怪：不涉及索敌，只看谁摸过
        config.LevelBase = 10;
        config.LevelMin = 10;
        config.LevelMax = 10;
        // 血厚到"丙下线之前不会先被打死"（甲乙每人每拍 ~100，两拍才 400），
        // 又不至于让用例跑上百拍；经验只看等级，不受这里影响。
        config.Stats = new MonsterStatsConfig { HpBase = 1200, HpPerLevel = 0, AtkBase = 0, AtkPerLevel = 0 };
        config.Xp = MonsterXpConfig.Default;                            // 45 + 5×怪等级 = 95
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 3;              // 三只同模板怪：三人共享 / 下线者不进分母 / 跑远者不进分母
            point.SlotSpacing = 1f;          // 出生点 -0.5 / +0.5，三个人都够得着
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;
            point.MoveSpeed = 0f;
            // 血量走刷怪点这一格（`SpawnPointConfig.MaxHp`，默认 100 会被写进战斗属性）：
            // 默认值下三只怪两拍就倒，第三个人根本来不及进贡献账本，用例就变成在测时序。
            point.MaxHp = 3000;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 100,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 三个人都站在怪的 −X 侧：默认朝向 +X ⇒ 面向怪，攻击才不会被朝向扇区判负
            Assert.True(world.AddPlayer(7101, 7101, -2.0f, 0f, 0f, out var r1, "甲", 10), r1);
            Assert.True(world.AddPlayer(7102, 7102, -2.2f, 0f, 0f, out var r2, "乙", 10), r2);
            Assert.True(world.AddPlayer(7103, 7103, -2.4f, 0f, 0f, out var r3, "丙", 10), r3);

            var mobOne = SpawnDirectorCore.ComputeEntityId(1, 0);
            var mobTwo = SpawnDirectorCore.ComputeEntityId(1, 1);
            void Swing(long sessionId, ulong target)
            {
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = (ulong)sessionId, TargetId = target,
                    ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
            }

            var kills = new List<(ulong Mob, ulong Who, long Amount)>();
            var outcomeLog = new List<string>();
            var firstDead = false;
            for (var i = 0; i < 200 && !firstDead; i++)
            {
                // 三个人**每拍都打**：贡献账本要在怪倒地之前把三个人都记进去，
                // 轮流打会因为暴击/伤害浮动让最后那个人来不及摸到，用例就变成在测时序而不是测分配。
                var o1 = world.SubmitCombatAction(7101, new Horizon.Game.Message.Sync.CombatActionPacket
                { AttackerId = 7101, TargetId = mobOne, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack });
                var o2 = world.SubmitCombatAction(7102, new Horizon.Game.Message.Sync.CombatActionPacket
                { AttackerId = 7102, TargetId = mobOne, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack });
                var o3 = world.SubmitCombatAction(7103, new Horizon.Game.Message.Sync.CombatActionPacket
                { AttackerId = 7103, TargetId = mobOne, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack });
                outcomeLog.Add($"t{i} 甲{o1.Outcome}/{o1.Damage} 乙{o2.Outcome}/{o2.Damage} 丙{o3.Outcome}/{o3.Damage}");
                var r = world.Tick();
                foreach (var e in r.GameplayEvents)
                {
                    if (e.Kind != DsGameplayEventKind.KillXp) continue;
                    kills.Add((e.MonsterEntityId, e.ActorCharacterId, e.ItemCount));
                    firstDead = true;
                }
            }
            _log.WriteLine(string.Join(" | ", outcomeLog));
            Assert.True(firstDead, "200 拍里三个人轮流打，第一只怪还没死——伤害通路没接到 DS 世界");

            var firstShares = kills.Where(k => k.Mob == mobOne).ToList();
            Assert.Equal(3, firstShares.Count);            // 三个人摸过 ⇒ 三个人都有事件
            Assert.All(firstShares, k => Assert.Equal(36L, k.Amount));   // 95 × 1.166 × 10/30 = 36.97 → 36
            var firstTotal = firstShares.Sum(k => k.Amount);
            Assert.Equal(108L, firstTotal);
            var soloPool = MonsterXpCore.Compute(MonsterXpConfig.Default, 10, 10, MonsterRank.Normal);
            Assert.Equal(95L, soloPool);
            Assert.True(firstTotal > soloPool, $"三人总共 {firstTotal} 比单人 {soloPool} 还少——人数加成没落地");
            Assert.True(firstTotal < 3 * soloPool,
                $"三人总共 {firstTotal}，三份全额是 {3 * soloPool}——说明还在按「每人一份全额」发");
            Assert.Equal(1.166, world.KillXpLastGroupRate, 3);
            Assert.Equal(10, world.KillXpLastPoolLevel);         // 池子按"最高的那个没灰名的人"（这里三人同级 10）

            // 第二只：丙先一起摸几下、然后**下线**，再由甲乙打死 ⇒ 分母里不该再有丙
            var second = new List<(ulong Who, long Amount)>();
            for (var i = 0; i < 200; i++)
            {
                var alive = world.TryGetMonster(mobTwo, out var cur) && cur != null && cur.Ai.Hp > 0;
                if (alive)
                {
                    Swing(7101, mobTwo);
                    Swing(7102, mobTwo);
                    if (i < 2) Swing(7103, mobTwo);               // 丙只在前两拍进贡献账本
                    if (i == 2) Assert.True(world.RemovePlayer(7103), "下线失败");
                }
                var r = world.Tick();
                foreach (var e in r.GameplayEvents)
                {
                    if (e.Kind == DsGameplayEventKind.KillXp && e.MonsterEntityId == mobTwo)
                        second.Add((e.ActorCharacterId, e.ItemCount));
                }
                if (second.Count > 0) break;
            }
            Assert.True(second.Count > 0, "第二只怪没打死或没发经验——这一路的跳过判据根本没跑到");
            Assert.Equal(2, second.Count);                                   // 只剩甲乙在场
            Assert.DoesNotContain(7103UL, second.Select(s => s.Who));
            Assert.All(second, s => Assert.Equal(47L, s.Amount));            // 95 × 1.0 × 10/20 = 47.5 → 47
            Assert.Equal(1.0, world.KillXpLastGroupRate, 3);                 // 两个人 → 没有人数加成
            Assert.True(world.KillXpSkippedOffline >= 1,
                "丙已经下线却没被跳过——分母里还带着一个不在场的人，队友的经验会被稀释");

            // 第三只：丁摸过之后**跑远了**（下线用"重加到远处"模拟离开奖励距离）
            // ⇒ 在场判据要按距离跳过一个跑掉的人，而不是让他继续稀释活着的人那一份。
            var mobThree = SpawnDirectorCore.ComputeEntityId(1, 2);
            // 丁站得比甲更靠前一点：第三只怪落在 +1 米（三个槽位沿 X 排开），
            // 站 -2.2 就超出普攻距离 3 米，一刀都摸不到 ⇒ 根本不会进贡献账本，那条"跑远"判据也就没东西可跳。
            Assert.True(world.AddPlayer(7104, 7104, -1.5f, 0f, 0f, out var r4, "丁", 10), r4);
            for (var i = 0; i < 8; i++)      // 只打到一半血：下面那拍要"人已经跑远而怪还活着"
            {
                Swing(7101, mobThree);
                Swing(7104, mobThree);
                world.Tick();
            }
            world.RemovePlayer(7104);
            Assert.True(world.AddPlayer(7104, 7104, 200f, 0f, 0f, out var r5, "丁", 10), r5);   // 同一个角色，人已经在 200 米外
            var third = new List<(ulong Who, long Amount)>();
            for (var i = 0; i < 200; i++)
            {
                if (world.TryGetMonster(mobThree, out var cur) && cur != null && cur.Ai.Hp > 0) Swing(7101, mobThree);
                var r = world.Tick();
                foreach (var e in r.GameplayEvents)
                {
                    if (e.Kind == DsGameplayEventKind.KillXp && e.MonsterEntityId == mobThree)
                        third.Add((e.ActorCharacterId, e.ItemCount));
                }
                if (third.Count > 0) break;
            }
            Assert.True(third.Count > 0, "第三只怪没打死——跑远那一档根本没跑到");
            Assert.Equal(1, third.Count);                                   // 只有甲在场
            Assert.Equal(95L, third[0].Amount);                             // 一个人的分母 ⇒ 拿整份池子
            Assert.True(world.KillXpSkippedOutOfRange >= 1,
                "丁已经站在 200 米外却还进了经验分母——队友那一份会被他稀释");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    // ---------------- ② 威胁表与滞回 ----------------

    [Fact]
    public void 威胁表需要超出三成才换目标()
    {
        var table = new MonsterThreatEntry[MonsterThreatCore.MaxEntries];
        var count = 0;
        MonsterThreatCore.AddThreat(table, ref count, 1, 100, 0);
        MonsterThreatCore.AddThreat(table, ref count, 2, 120, 0);
        // 2 只有 120，未过 100×1.3=130 → 仍咬 1
        Assert.Equal(1UL, MonsterThreatCore.SelectCurrent(table, count, 1));
        MonsterThreatCore.AddThreat(table, ref count, 2, 15, 0);   // 135 > 130
        Assert.Equal(2UL, MonsterThreatCore.SelectCurrent(table, count, 1));
        // 2 已是第一，且再没人超过它 → 目标保持 2
        Assert.Equal(2UL, MonsterThreatCore.SelectCurrent(table, count, 2));
    }

    [Fact]
    public void 嘲讽抬到最高加一并可被正常仇恨抢回()
    {
        var table = new MonsterThreatEntry[MonsterThreatCore.MaxEntries];
        var count = 0;
        MonsterThreatCore.AddThreat(table, ref count, 1, 500, 0);
        MonsterThreatCore.AddThreat(table, ref count, 2, 10, 0);
        MonsterThreatCore.Taunt(table, ref count, 2, 0);
        Assert.Equal(2UL, MonsterThreatCore.Highest(table, count));
        Assert.Equal(501L, table[System.Array.FindIndex(table, 0, count, e => e.EntityId == 2UL)].Threat);
        // 1 再打 149（累计 649）：< 501×1.3=651.3 → 滞回仍咬住嘲讽目标
        MonsterThreatCore.AddThreat(table, ref count, 1, 149, 0);
        Assert.Equal(2UL, MonsterThreatCore.SelectCurrent(table, count, 2));
        // 补到 652：> 651.3 → 普通 OT 生效（嘲讽只争取窗口，不是永久锁）
        MonsterThreatCore.AddThreat(table, ref count, 1, 3, 0);
        Assert.Equal(1UL, MonsterThreatCore.SelectCurrent(table, count, 2));
    }

    [Fact]
    public void 治疗按半计威胁且表按实体序稳定()
    {
        Assert.Equal(50L, MonsterThreatCore.ThreatFromHealing(100));
        Assert.Equal(100L, MonsterThreatCore.ThreatFromDamage(100));
        var table = new MonsterThreatEntry[MonsterThreatCore.MaxEntries];
        var count = 0;
        foreach (var id in new ulong[] { 9, 1, 5, 3 }) MonsterThreatCore.AddThreat(table, ref count, id, 10, 0);
        for (var i = 1; i < count; i++) Assert.True(table[i - 1].EntityId < table[i].EntityId, "威胁表必须保持升序（内核的确定性前提）");
    }

    // ---------------- ③ 寻路 ----------------

    [Fact]
    public void 网格寻路会绕开阻挡并给出可达终点()
    {
        const int w = 12;
        const int h = 5;
        var costs = new byte[w * h];
        for (var i = 0; i < costs.Length; i++) costs[i] = 10;
        // 在 x=6 处立一堵墙，只留最上一格可过
        for (var y = 1; y < h; y++) costs[y * w + 6] = 0;

        var grid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = costs };
        var scratch = new MonsterNavScratch();
        scratch.EnsureCapacity(w * h);
        var cells = new int[MonsterPathCore.MaxPathCells];
        var result = MonsterPathCore.FindPath(grid, scratch, 1f, 2f, 10f, 2f, cells, out var length);
        Assert.True(result is MonsterPathResult.Found or MonsterPathResult.Partial, $"寻路结果 {result}");
        Assert.True(length >= 2, $"路径过短（{length}）——绕墙根本没走");
        // 路径里不得出现阻挡格
        for (var i = 0; i < length; i++) Assert.True(grid.IsWalkable(cells[i]), $"路径第 {i} 格落在阻挡上");
    }

    [Fact]
    public void 完全隔断时给不可达而不是空路径冒充直达()
    {
        const int w = 8;
        const int h = 8;
        var costs = new byte[w * h];
        for (var i = 0; i < costs.Length; i++) costs[i] = 10;
        for (var y = 0; y < h; y++) costs[y * w + 4] = 0;   // 整列封死
        var grid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = costs };
        var scratch = new MonsterNavScratch();
        scratch.EnsureCapacity(w * h);
        var cells = new int[MonsterPathCore.MaxPathCells];
        var result = MonsterPathCore.FindPath(grid, scratch, 1f, 1f, 7f, 1f, cells, out var length);
        // 目标被完全隔断 → 给"能走到的最远处"，而不是空路径冒充直达。
        // 这条判据的意义：怪会走到墙边站着，而不是原地发呆（后者在玩家眼里 = AI 挂了）。
        Assert.Equal(MonsterPathResult.Partial, result);
        Assert.True(length > 0, "部分路径长度为 0：怪会原地不动");
        Assert.True(result != MonsterPathResult.Direct, "不可达被判成直达");
        for (var i = 0; i < length; i++) Assert.True(grid.IsWalkable(cells[i]), $"部分路径第 {i} 格落在阻挡上");
    }

    [Fact]
    public void 同图同起终点的路径每次都相同()
    {
        const int w = 20;
        const int h = 20;
        var costs = new byte[w * h];
        // 起点与终点必须可走：内核遇到"起点被堵"返回 Direct + 零长度（退化成直线），
        // 那是正确行为而不是失败——把它当"不可复现"来断言就会误红。
        for (var i = 0; i < costs.Length; i++)
            costs[i] = (byte)(i != 0 && i != costs.Length - 1 && i % 17 == 0 ? 0 : 10);
        var grid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = costs };
        int[] Run()
        {
            var scratch = new MonsterNavScratch();
            scratch.EnsureCapacity(w * h);
            var cells = new int[MonsterPathCore.MaxPathCells];
            MonsterPathCore.FindPath(grid, scratch, 0f, 0f, 19f, 19f, cells, out var length);
            return cells.Take(length).ToArray();
        }
        var a = Run();
        var b = Run();
        Assert.True(a.Length > 0, "路径为空——确定性无从谈起");
        Assert.Equal(a, b);
    }

    // ---------------- ④ 内容快照装配 ----------------

    [Fact]
    public void 开发快照装配出的内容与代码目录同源()
    {
        var receipt = MonsterContentLoader.LoadFile(Fixture("dev_content.json"));
        Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join("; ", receipt.Problems));
        Assert.Equal(11, receipt.Templates);
        Assert.Equal(6, receipt.SpawnPoints);
        Assert.Empty(receipt.Problems);

        // 逐只核对身份三要素与域：这是"从代码目录迁到数据表"唯一的等价证据
        foreach (var row in MonsterTemplateCatalog.RegisteredTemplates())
        {
            Assert.True(MonsterContent.TryGetConfig(row.TemplateId, out var config) && config != null,
                $"模板 {row.TemplateId} 未装配");
            Assert.Equal(row.Name, config!.Name);
            Assert.Equal(row.Level, config.LevelBase);
            Assert.Equal(row.WuxingDomain, config.WuxingDomain);
            Assert.Equal(row.IsWorldBoss ? MonsterRank.WorldBoss : MonsterRank.Normal, config.Rank);
        }
        MonsterContent.ResetToHardcoded();
    }

    [Fact]
    public void 快照装配对空文件与坏路径都大声失败()
    {
        var missing = MonsterContentLoader.LoadFile(Path.Combine(RepoRoot(), "HundunWorld", "Data", "Monsters", "does_not_exist.json"));
        Assert.False(missing.Ok);
        Assert.NotEmpty(missing.Problems);

        var emptyPath = MonsterContentLoader.LoadFile(string.Empty);
        Assert.False(emptyPath.Ok);

        // 空模板的快照必须拒装：静默装成空内容 = 所有怪退回 Lv1/100 血
        var temp = Path.Combine(Path.GetTempPath(), "monster_empty_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temp, "{\"contentRevision\":1,\"templates\":[]}");
        try
        {
            var receipt = MonsterContentLoader.LoadFile(temp);
            Assert.False(receipt.Ok);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void 智能档位快照展开出预期能力位()
    {
        var receipt = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        Assert.True(MonsterContent.TryGetConfig(1, out var config) && config != null);
        var flags = config!.Behavior.Flags;
        Assert.True(flags.UseThreatTable);
        Assert.True(flags.UseSkills);
        Assert.True(flags.UseNavigation);
        Assert.True(flags.UseAssist);
        Assert.False(flags.UseTactics);      // 档位 2 不含战术
        Assert.Single(config.Skills);
        Assert.Equal(10101, config.Skills[0].SkillId);
        Assert.Equal(3000, config.Skills[0].ChanceBasisPoints);
        MonsterContent.ResetToHardcoded();
    }

    [Fact]
    public void 刷怪点引用未登记模板时整点被跳过并显形()
    {
        var temp = Path.Combine(Path.GetTempPath(), "monster_orphan_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(temp, """
{"contentRevision":1,"templates":[
  {"monsterId":7,"name":"孤儿对照","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "levelBase":10,"levelMin":10,"levelMax":10,"stats":{"hpBase":100,"hpPerLevel":0,"atkBase":10,"atkPerLevel":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"behavior":{"aiProfile":0}}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":7,"maxAlive":2,"slotSpacing":8},
  {"spawnPointId":2,"zoneId":0,"shardId":0,"x":60,"y":0,"z":0,"monsterTemplateId":999,"maxAlive":2,"slotSpacing":8}
]}
""");
        try
        {
            var receipt = MonsterContentLoader.LoadFile(temp);
            Assert.True(receipt.Ok);
            Assert.Equal(1, receipt.SpawnPoints);              // 坏点位被剔除
            Assert.Contains(receipt.Problems, p => p.Contains("999"));
        }
        finally
        {
            File.Delete(temp);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 导航网格装载器对坏文件大声报错()
    {
        var grid = MonsterNavGridLoader.Load(Fixture("dev_nav.grid"), out var error);
        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.True(grid.IsValid);
        Assert.Equal(32, grid.Width);

        var bad = Path.Combine(Path.GetTempPath(), "nav_bad_" + Guid.NewGuid().ToString("N") + ".grid");
        File.WriteAllText(bad, "MONSTERNAV 4 4 1.0 0 0\n10 10 10");   // 少一行
        try
        {
            var empty = MonsterNavGridLoader.Load(bad, out var badError);
            Assert.False(string.IsNullOrEmpty(badError));
            Assert.False(empty.IsValid);
        }
        finally
        {
            File.Delete(bad);
        }
    }

    // ---------------- ⑤ AI v2 的边界 ----------------

    [Fact]
    public void 能力位全关时不进入v2()
    {
        var bundle = LoadBundle("dev_content.json");
        Assert.True(MonsterContent.TryGetConfig(2, out var config) && config != null);
        Assert.True(MonsterAiRuntime.AllCapabilitiesOff(in config!.Behavior.Flags),
            "dev_content 的 ai_profile 应为 0（与代码目录等价），否则这条闸门就失去意义");
        Assert.Equal(bundle.TemplateCount, 11);
    }

    [Fact]
    public void 施法子状态在同种子下逐Tick可复现()
    {
        var mounts = new[]
        {
            new MonsterSkillMount { SkillId = 10101, ChanceBasisPoints = 5000, CastMs = 0, CooldownMs = 60000, MaxRange = 30f, Slot = MonsterSkillSlot.Combat, Interruptible = false },
            new MonsterSkillMount { SkillId = 10102, ChanceBasisPoints = 5000, CastMs = 0, CooldownMs = 60000, MaxRange = 30f, Slot = MonsterSkillSlot.Combat, Interruptible = false },
        };

        List<int> Run(int seedOffset)
        {
            var chosen = new List<int>();
            var self = default(MonsterCastingSelf);
            var ready = new long[mounts.Length];
            for (var tick = 0L; tick < 600L; tick++)
            {
                var decision = MonsterCastingCore.Advance(ref self, mounts, ready,
                    monsterEntityId: 5000UL + (ulong)seedOffset, threatTargetId: 7UL,
                    threatTargetX: 1f, threatTargetY: 1f, threatTargetZ: 0f,
                    distanceToThreat: 3f, hpPercent: 100, inCombat: true,
                    interruptSignal: false, serverTick: tick * 60 + seedOffset, ticksPerSecond: 60, dt: 1f / 60f);
                if (decision.FinishesCast) chosen.Add(decision.SkillId);
            }
            return chosen;
        }

        List<int> RunWithChance(int chanceBps)
        {
            var mounts2 = new[]
            {
                new MonsterSkillMount { SkillId = 10101, ChanceBasisPoints = chanceBps, CastMs = 0, CooldownMs = 60000, MaxRange = 30f, Slot = MonsterSkillSlot.Combat, Interruptible = false },
            };
            var chosen = new List<int>();
            var self = default(MonsterCastingSelf);
            var ready = new long[mounts2.Length];
            for (var tick = 0L; tick < 600L; tick++)
            {
                var decision = MonsterCastingCore.Advance(ref self, mounts2, ready,
                    monsterEntityId: 5000UL, threatTargetId: 7UL,
                    threatTargetX: 1f, threatTargetY: 1f, threatTargetZ: 0f,
                    distanceToThreat: 3f, hpPercent: 100, inCombat: true,
                    interruptSignal: false, serverTick: tick * 60, ticksPerSecond: 60, dt: 1f / 60f);
                if (decision.FinishesCast) chosen.Add(decision.SkillId);
            }
            return chosen;
        }

        var first = Run(0);
        var second = Run(0);
        Assert.NotEmpty(first);                       // 50% 概率跑 600 tick 还不放 = 施法链是死的
        Assert.Equal(first, second);                  // 同种子同序列（可复现）

        // 概率必须真的被咨询：两端对照比"换种子看序列变不变"更硬——
        // 换种子只证明随机源不是常数，证不了 chance 这一路通。
        var zeroChance = new MonsterSkillMount { SkillId = 10101, ChanceBasisPoints = 0, CastMs = 0, CooldownMs = 60000, MaxRange = 30f, Slot = MonsterSkillSlot.Combat };
        var probeReady = new long[1];
        Assert.False(MonsterCastingCore.IsCandidate(in zeroChance, 0, probeReady, 0L, 3f, 100, true),
            "chanceBps=0 的挂载竟然进了候选：IsCandidate 没有把概率当门槛");
        var never = RunWithChance(0);                 // chanceBps = 0 → 一次都不放
        Assert.Empty(never);
        var always = RunWithChance(MonsterCastingCore.ChanceBasisPointsMax);
        Assert.NotEmpty(always);
    }

    [Fact]
    public void 库内新增的怪让白名单与掉落一起跟上()
    {
        // 这条断言就是"配了有人读"的判据本体：
        // 未装配时 100001 不存在；装配后它必须同时被**白名单、目录列表、掉落表号、刷怪点**看见。
        Assert.False(MonsterTemplateCatalog.TryGetRegistered(100001, out _), "未装配时就该查不到（前提）");
        Assert.Equal(0, MonsterTemplateCatalog.LootTableIdOf(100001));
        Assert.Equal(0, MonsterTemplateCatalog.DeriveLootTableId(100001));

        var receipt = MonsterContentLoader.LoadFile(Fixture("published_extra_content.json"));
        Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join(";", receipt.Problems));
        try
        {
            Assert.True(MonsterTemplateCatalog.TryGetRegistered(100001, out var info));
            Assert.Equal("赤鳞蜃", info.Name);
            Assert.Equal(34, info.Level);
            Assert.Equal(0, info.WuxingDomain);          // 未定域：好感照旧不给任何域记账
            Assert.False(info.IsWorldBoss);

            // 掉落表号来自**绑定**而不是推导：这只怪未定域，推导只会给 0，
            // 于是"5"这个数只能是配置被读到了的证据。
            Assert.Equal(5, MonsterTemplateCatalog.LootTableIdOf(100001));

            // 但"纯推导"那一份必须**不受绑定影响**：落库脚本要拿它问"这只怪的域本来该是哪张表"，
            // 若它也先看绑定，写完一次后第二次问到的就是自己上次写进去的那个数——问一次把自己钉死。
            Assert.Equal(0, MonsterTemplateCatalog.DeriveLootTableId(100001));

            // 存量怪仍按推导（未配绑定时不许被新路径打断）
            Assert.Equal(3, MonsterTemplateCatalog.LootTableIdOf(1));    // 墨麒麟 域 3
            Assert.Equal(104, MonsterTemplateCatalog.LootTableIdOf(9));  // 赤炎鹏 = 100 + 火域 4
            Assert.Equal(3, MonsterTemplateCatalog.DeriveLootTableId(1));
            Assert.Equal(104, MonsterTemplateCatalog.DeriveLootTableId(9));
            // 无域的世界 BOSS（混沌遗蜕）落的是 100 那张总表，不是 100+0 之外的别的号
            Assert.Equal(MonsterTemplateCatalog.WorldBossTableBase, MonsterTemplateCatalog.DeriveLootTableId(11));

            // 目录列表：新增号必须在里面（任务选择器与内容对账读它）
            Assert.Contains(MonsterTemplateCatalog.RegisteredTemplates(), r => r.TemplateId == 100001);
            // 且不得因合并而出现重复条目（装配内容与代码目录有 11 个交集）
            Assert.Equal(MonsterTemplateCatalog.RegisteredTemplates().Length,
                         MonsterTemplateCatalog.RegisteredTemplates().Select(r => r.TemplateId).Distinct().Count());

            // 刷怪点也来自快照：dev 的 6 个 + 新增的 1 个
            var spawns = MonsterContent.SpawnPoints();
            Assert.Equal(7, spawns.Count);
            var added = spawns.FirstOrDefault(sp => sp.SpawnPointId == 100001);
            Assert.Equal(100001, added.MonsterTemplateId);
            Assert.True(MonsterContent.TryGetConfig(100001, out var addedConfig) && addedConfig != null);
            Assert.Equal(36, added.ResolveLevel(addedConfig!));   // 等级覆盖优先于模板基准

            // 能力位真的展开：档位 2 + 战术强制关
            Assert.True(MonsterContent.TryGetConfig(100001, out var config) && config != null);
            var flags = config!.Behavior.Flags;
            Assert.True(flags.UseThreatTable && flags.UseSkills && flags.UseNavigation && flags.UseAssist);
            Assert.False(flags.UseTactics);
            Assert.Equal(180f, config.Behavior.AggroConeDegrees);
            Assert.Equal(2, config.Skills.Length);
            // 数组顺序必须等于快照里的 SlotSeq 顺序（内核按下标遍历并用下标做掷骰盐值）
            Assert.Equal(10101, config.Skills[0].SkillId);
            Assert.Equal(10102, config.Skills[1].SkillId);
            Assert.Equal(MonsterSkillSlot.EmergencyLowHp, config.Skills[1].Slot);
            Assert.True(config.Skills[1].AoeRadius > 0f);
            Assert.False(config.Skills[1].Interruptible);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }

        // 退回后必须与改动前逐字一致：这条防的是"接了库就把存量怪改了"
        Assert.False(MonsterTemplateCatalog.TryGetRegistered(100001, out _));
        Assert.Equal(3, MonsterTemplateCatalog.LootTableIdOf(1));
        Assert.Equal(11, MonsterTemplateCatalog.RegisteredTemplates().Length);
    }

    [Fact]
    public void 掉落表号两条入口必须同数而首领档判据只有一处()
    {
        // 落库脚本（Tools/author-mob-loot.py）不再自己算表号，改问 CLI 的 lootTableIdDerived；
        // CLI 那一头喂的是**库里的草稿**（域 + 档位），目录那一头喂的是装配内容或代码目录。
        // 两条入口若算出不同的数，同一个怪在"内容掉落"与"绑定撤空后退回 provider"两条通路上
        // 就会指向两张表——而两边各自都跑得通、没人报错。所以钉的是"同数"，不是某个具体值。
        foreach (var templateId in new[] { 1, 5, 9, 11 })
        {
            var row = MonsterTemplateCatalog.Get(templateId);
            Assert.Equal(MonsterTemplateCatalog.DeriveLootTableId(templateId),
                         MonsterTemplateCatalog.DeriveLootTableId(row.WuxingDomain, row.IsWorldBoss));
        }

        // 档位口径：副本首领（3）与世界 BOSS（4）同走 100+域那张表。
        // 这一条是脚本第一版踩过的坑——它把"首领"写成 rank>=4，于是 3 档的怪会被写成普通域号。
        Assert.False(MonsterTemplateCatalog.IsBossTableRank(2));
        Assert.True(MonsterTemplateCatalog.IsBossTableRank(3));
        Assert.True(MonsterTemplateCatalog.IsBossTableRank(4));

        // 号段本体：普通怪=域号、首领=100+域、无域首领=总表 100、未定域的普通怪=0（不掉）。
        Assert.Equal(3, MonsterTemplateCatalog.DeriveLootTableId(3, false));
        Assert.Equal(103, MonsterTemplateCatalog.DeriveLootTableId(3, true));
        Assert.Equal(MonsterTemplateCatalog.WorldBossTableBase, MonsterTemplateCatalog.DeriveLootTableId(0, true));
        Assert.Equal(0, MonsterTemplateCatalog.DeriveLootTableId(0, false));
        // 越界的域不许被当成"某个域"：兜底成 0 才只表现为不掉，猜成 6 就指向一张不存在的表
        Assert.Equal(0, MonsterTemplateCatalog.DeriveLootTableId(6, false));
        Assert.Equal(MonsterTemplateCatalog.WorldBossTableBase, MonsterTemplateCatalog.DeriveLootTableId(6, true));
    }

    [Fact]
    public void 编辑器配的外观随怪物Spawn下发到客户端包()
    {
        // 这条断言是"外观"这一项目标的判据本体：编辑器里配的 model_key/体型/姿态/档位
        // 必须真的出现在 DS 下发的 Identity 里。缺任何一环，客户端都只能把所有怪画成占位人形，
        // 而"编译通过 + 库里存着配置"看起来完全正常。
        var receipt = MonsterContentLoader.LoadFile(Fixture("published_extra_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 100001;
            point.MonsterTemplateId = 100001;
            point.MaxAlive = 1;
            point.X = 500f;
            point.Y = 0f;
            point.Z = 0f;
            point.AggroRadius = 8f;      // 玩家放得很远，怪保持 Idle，断言不受追击扰动

            var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(1001, 1001, 9000f, 0f, 0f, out var reason), reason);

            Horizon.Game.Core.Sim.Server.DsTickResult? snapshot = null;
            // DsTickResult 是引用类型：判空用 snapshot != null，不写 .Value
            for (var i = 0; i < 30 && snapshot == null; i++)
            {
                var r = world.Tick();
                if (r.SnapshotDue && r.WorldSnapshot != null &&
                    r.WorldSnapshot.Deltas.Any(d => d.Identity.HasValue &&
                                                   d.Identity.Value.EntityType == Horizon.Game.Core.Sim.Server.DsWorldSession.EntityTypeMonster))
                {
                    snapshot = r;
                }
            }
            Assert.NotNull(snapshot);

            var monsterDelta = snapshot!.WorldSnapshot!.Deltas
                .First(d => d.Identity.HasValue && d.Identity.Value.EntityType == Horizon.Game.Core.Sim.Server.DsWorldSession.EntityTypeMonster);
            var identity = monsterDelta.Identity!.Value;

            Assert.Equal(100001, identity.TemplateId);
            Assert.Equal("chi_lin_shen", identity.ModelKey);
            Assert.Equal(1.2f, identity.ModelScale, 3);      // 编辑器里的体型缩放
            Assert.Equal(2, identity.StandState);            // 卧姿
            Assert.Equal(1, identity.Rank);                  // 精英
            Assert.Equal("赤鳞蜃", identity.Nickname);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 未装配内容时外观字段退回代码目录且不越界()
    {
        // 没装配时也必须给出**自洽**的一组值：缩放不能是 0（会把怪压成一个点），
        // 姿态必须是 -1（= 未下发），因为 0 是合法姿态、不能当哨兵。
        var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 3,
        });
        var point = SpawnPointConfig.Default;
        point.SpawnPointId = 1;
        point.MonsterTemplateId = 1;    // 代码目录里的墨麒麟
        point.MaxAlive = 1;
        point.X = 300f;
        point.AggroRadius = 8f;
        world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
        Assert.True(world.AddPlayer(2001, 2001, 9000f, 0f, 0f, out var reason), reason);

        Horizon.Game.Core.Sim.Server.DsTickResult? snapshot = null;
            // DsTickResult 是引用类型：判空用 snapshot != null，不写 .Value
        for (var i = 0; i < 30 && snapshot == null; i++)
        {
            var r = world.Tick();
            if (r.SnapshotDue && r.WorldSnapshot != null &&
                r.WorldSnapshot.Deltas.Any(d => d.Identity.HasValue &&
                                                d.Identity.Value.EntityType == Horizon.Game.Core.Sim.Server.DsWorldSession.EntityTypeMonster))
            {
                snapshot = r;
            }
        }
        Assert.NotNull(snapshot);

        var identity = snapshot!.WorldSnapshot!.Deltas
            .First(d => d.Identity.HasValue && d.Identity.Value.EntityType == Horizon.Game.Core.Sim.Server.DsWorldSession.EntityTypeMonster)
            .Identity!.Value;

        Assert.Equal(1, identity.TemplateId);
        Assert.Equal("mo_qilin", identity.ModelKey);
        Assert.True(identity.ModelScale > 0f, $"缩放被下发成 {identity.ModelScale}：客户端会把怪压成一个点");
        Assert.Equal(-1, identity.StandState);
        Assert.Equal(0, identity.Rank);
    }

    [Fact]
    public void 晚进场的人能从Spawn补发里拿到正在读的那根条()
    {
        // 起手是瞬时事件，只发给**当时**看得见这只怪的人；补发通路只有 Spawn。
        // 于是没有这一层时，新玩家走进一片正在施法的怪中间，看到的是一根根空条，
        // 而服务端、日志、编辑器全都"正常"——这条断言钉的就是 Spawn 里那份吟唱状态。
        var receipt = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            var world = MakeCastingWorld(out _, false, 3f, 0f);

            // 反向对照要用**真的不会施法**的那只怪：把玩家放到 9000 米外，怪全程 Idle。
            // （以前这里想"趁它还没起手"取第一份 Spawn——实测那次起手就在第一个快照之前发生，
            // 于是那条判据在正确实现上也会红：期望值必须量出来，不能从"应该还没开始"推。）
            var idleWorld = MakeCastingWorld(out _, false, 9000f, 0f);
            var idleIdentity = WaitMonsterIdentity(idleWorld, 300);
            Assert.True(idleIdentity.CastTotalSeconds <= 0f,
                        "怪没在读条，Spawn 却带了吟唱状态：客户端会画出一根不存在的条子");

            var seen = RunUntilCastEvents(world, 2400, 1);
            var start = seen.FirstOrDefault(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart);
            // SyncEvent 是值类型：`is not null` 永远为真，判"有没有起手"要看 Kind 本身。
            Assert.Equal(Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart, start.Event.Kind);
            Assert.Equal(1.2f, start.Event.FloatValue, 2);

            // 现在这只怪正在读条（1.2 秒＝72 tick，窗口足够）：新人进场必须立刻看到半程的条子。
            Assert.True(world.AddPlayer(2002, 2002, 3f, 2f, 0f, out var reason), reason);
            var lateIdentity = WaitMonsterIdentity(world, 300);

            Assert.Equal(1.2f, lateIdentity.CastTotalSeconds, 2);
            Assert.True(lateIdentity.CastElapsedSeconds > 0f,
                        $"补发时说\"在读条\"却零进度：{lateIdentity.CastElapsedSeconds}——客户端会把条子从头重放");
            Assert.True(lateIdentity.CastElapsedSeconds < lateIdentity.CastTotalSeconds, "进度越过了总时长");
            Assert.Equal(10101, lateIdentity.CastSkillId);
            Assert.Equal(8, lateIdentity.CastSchoolMask);          // 与起手事件 payload 同一系别（水）
            Assert.Equal(1, lateIdentity.CastInterruptible);       // 与起手事件同一描边判据
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>取最近一份带怪物 Identity 的 Spawn delta。</summary>
    private static Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent WaitMonsterIdentity(
        DsWorldSession world, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var r = world.Tick();
            if (r.WorldSnapshot == null) continue;
            foreach (var d in r.WorldSnapshot.Deltas)
            {
                if (d.Identity.HasValue &&
                    d.Identity.Value.EntityType == DsWorldSession.EntityTypeMonster)
                {
                    return d.Identity.Value;
                }
            }
        }
        throw new Xunit.Sdk.XunitException("跑了 " + maxTicks + " tick 都没拿到怪物的 Spawn Identity（补发通路根本没走通）");
    }

    [Fact]
    public void 协议v16只追加字段且能往返()
    {
        // 追加字段的判据不是"能编译"，而是**旧字段编号不动 + 新字段真的能过序列化**。
        // MemoryPack 的成员顺序错位不会报错，只会把值放错地方——所以逐字段回读。
        Assert.Equal(16, Horizon.Game.Message.Sync.SyncProtocolVersion.Current);
        Assert.Equal(5, Horizon.Game.Message.Sync.SyncProtocolVersion.MinimumSupported);

        var original = new Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent
        {
            NetworkId = 123456789UL,
            EntityType = 4,
            OwnerId = 0UL,
            Nickname = "赤鳞蜃",
            Title = "称号",
            TemplateId = 100001,
            ModelKey = "chi_lin_shen",
            ModelScale = 1.2f,
            StandState = 2,
            Rank = 1,
            CastSkillId = 800007,
            CastTotalSeconds = 3.5f,
            CastElapsedSeconds = 1.25f,
            CastSchoolMask = 4,
            CastInterruptible = 1,
        };

        var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
        var back = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.Components.NetworkIdentityAuthComponent>(bytes);

        Assert.Equal(original.NetworkId, back.NetworkId);
        Assert.Equal(original.EntityType, back.EntityType);
        Assert.Equal(original.Nickname, back.Nickname);
        Assert.Equal(original.Title, back.Title);
        Assert.Equal(original.TemplateId, back.TemplateId);
        Assert.Equal(original.ModelKey, back.ModelKey);
        Assert.Equal(original.ModelScale, back.ModelScale, 5);
        Assert.Equal(original.StandState, back.StandState);
        Assert.Equal(original.Rank, back.Rank);
        Assert.Equal(original.CastSkillId, back.CastSkillId);
        Assert.Equal(original.CastTotalSeconds, back.CastTotalSeconds, 5);
        Assert.Equal(original.CastElapsedSeconds, back.CastElapsedSeconds, 5);
        Assert.Equal(original.CastSchoolMask, back.CastSchoolMask);
        Assert.Equal(original.CastInterruptible, back.CastInterruptible);

        // v16：动作包那两格也要真的过序列化往返——否则"玩家技能带眩晕"会在编解码处静默丢成 0。
        var action = new Horizon.Game.Message.Sync.CombatActionPacket
        {
            AttackerId = 42UL,
            TargetId = 9001UL,
            ActionKind = Horizon.Game.Message.Sync.CombatActionKind.SkillCast,
            SkillId = 700001,
            ClientTick = 123,
            AttackerYaw = 0.75f,
            StatusId = 1,
            StatusDurationSeconds = 2.5f,
        };
        var actionBack = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.CombatActionPacket>(
            MemoryPack.MemoryPackSerializer.Serialize(action));
        Assert.Equal(action.AttackerId, actionBack.AttackerId);
        Assert.Equal(action.TargetId, actionBack.TargetId);
        Assert.Equal(action.SkillId, actionBack.SkillId);
        Assert.Equal(action.ClientTick, actionBack.ClientTick);
        Assert.Equal(action.AttackerYaw, actionBack.AttackerYaw, 5);
        Assert.Equal(1, actionBack.StatusId);
        Assert.Equal(2.5f, actionBack.StatusDurationSeconds, 5);
    }

    /// <summary>读快照并装配（测试结束由用例自己 ResetToHardcoded）。</summary>
    private static MonsterContentBundle LoadBundle(string fixtureName)
    {
        var receipt = MonsterContentLoader.LoadFile(Fixture(fixtureName));
        Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join(";", receipt.Problems));
        var snapshot = MonsterContentSnapshot.FromJson(File.ReadAllText(Fixture(fixtureName)))!;
        var bundle = snapshot.ToBundle(out _);
        return bundle;
    }

    /// <summary>本 tick 事件帧里挑出怪物施法条事件（起手或收条）。</summary>
    private static IEnumerable<(long[] Audience, Horizon.Game.Message.Sync.SyncEvent Event)> HarvestCastEvents(
        DsTickResult result)
    {
        if (result.Events == null) yield break;
        foreach (var (audience, packet) in result.Events)
        {
            if (packet is not Horizon.Game.Message.Sync.EventPacket ev || ev.Events == null) continue;
            foreach (var e in ev.Events)
            {
                if (e.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart ||
                    e.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStop)
                {
                    yield return (audience, e);
                }
            }
        }
    }

    /// <summary>跑若干 tick 收施法条事件；命中够数就早停。</summary>
    private static List<(long[] Audience, Horizon.Game.Message.Sync.SyncEvent Event)> RunUntilCastEvents(
        DsWorldSession world, int maxTicks, int wantStarts)
    {
        var found = new List<(long[] Audience, Horizon.Game.Message.Sync.SyncEvent Event)>();
        for (var i = 0; i < maxTicks; i++)
        {
            foreach (var hit in HarvestCastEvents(world.Tick())) found.Add(hit);
            if (found.Count(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart) >= wantStarts) break;
        }
        return found;
    }

    /// <summary>装配 smoke 内容并造一只"玩家就在旁边"的怪（模板 1：ai_profile=2 会吟唱 1.2 秒水系技能）。</summary>
    private static DsWorldSession MakeCastingWorld(out long sessionId, bool aoiFilter, float playerX, float playerY)
    {
        var point = SpawnPointConfig.Default;
        point.SpawnPointId = 1;
        point.MonsterTemplateId = 1;
        point.MaxAlive = 1;
        point.X = 0f;
        point.Y = 0f;
        point.Z = 0f;
        point.AggroRadius = 10f;
        point.MoveSpeed = 4f;

        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 3,
            EnableAoiFilter = aoiFilter,
        });
        world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });

        sessionId = 2001;
        Assert.True(world.AddPlayer(sessionId, 2001, playerX, playerY, 0f, out var reason), reason);
        return world;
    }

    [Fact]
    public void 主动怪不必挨打就会接敌并发出施法条起手与收条()
    {
        // 这条断言同时钉两件事：
        // ① v3 补的"主动怪按半径+朝向锥接敌"——在那之前，开了威胁表的主动怪**必须玩家先打它**
        //    才会进战斗，玩家看到的是一地站着不动的怪，而"是否挨过打"这件事他根本看不见。
        // ② 起手/收条都真的发到了线上——只配了 castMs 而没人发事件，客户端那根条子永远不出现。
        var receipt = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            var world = MakeCastingWorld(out var sessionId, false, 3f, 0f);
            var found = RunUntilCastEvents(world, 2400, 2);

            var starts = found.Where(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart).ToList();
            Assert.NotEmpty(starts);
            var stopCount = found.Count(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStop);
            Assert.True(stopCount >= 1, $"发了 {starts.Count} 次起手却一次收条都没有：条子会永远挂在头顶谎报还在读条");

            var start = starts[0];
            Assert.Contains(sessionId, start.Audience);
            Assert.Equal(10101, start.Event.IntValue);                        // 编辑器里挂的技能 id
            Assert.Equal(1.2f, start.Event.FloatValue, 2);                    // castMs 1200 → 1.2 秒
            Assert.Equal(Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart, start.Event.Kind);

            var payload = MemoryPack.MemoryPackSerializer
                .Deserialize<Horizon.Game.Message.Sync.MonsterCastPayload>(start.Event.Payload!);
            Assert.Equal(8, payload.SchoolMask);                              // 水（位号==域号，见 MonsterSchoolBits）
            Assert.True(payload.Interruptible);
            Assert.False(payload.AllowMovement);                              // 站桩读条
            Assert.Equal(3, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(payload.SchoolMask));

            // 收条必须带得上原因：客户端表现要分"放完了"与"被打断了"。
            var stop = found.First(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStop);
            Assert.True(stop.Event.IntValue == Horizon.Game.Message.Sync.MonsterCastEndReason.Completed ||
                        stop.Event.IntValue == Horizon.Game.Message.Sync.MonsterCastEndReason.Interrupted ||
                        stop.Event.IntValue == Horizon.Game.Message.Sync.MonsterCastEndReason.Cancelled,
                        $"收条原因 {stop.Event.IntValue} 不在 MonsterCastEndReason 的取值里");

            // 诊断计数必须与线上事件数一致：不一致就说明"发事件"与"记账"分家了。
            Assert.Equal(starts.Count, world.MonsterCastStartEvents);
            Assert.Equal(stopCount, world.MonsterCastStopEvents);
            Assert.Equal(0, world.MonsterCastMountLookupMisses);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 施法条只发给看得见这只怪的人()
    {
        // AOI 开着时（生产形态），条子是**按可见集合**发的：
        // 发给看不见它的人=白烧带宽，不发给看得见它的人=有人头顶缺一条。两个方向都要断言。
        var receipt = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            // 近处一个会话（3 米，一定在同一 chunk 订阅里）+ 远处一个会话（5000 米，一定在视野外）。
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 1;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 10f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                EnableAoiFilter = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(2001, 2001, 3f, 0f, 0f, out var nearReason), nearReason);
            Assert.True(world.AddPlayer(9009, 9009, 5000f, 0f, 0f, out var farReason), farReason);

            var found = RunUntilCastEvents(world, 2400, 1);
            var starts = found.Where(x => x.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart).ToList();
            Assert.NotEmpty(starts);                  // 开着 AOI 也必须发得出去（订阅没建起来就是全场静默）
            Assert.Contains(2001L, starts[0].Audience);
            Assert.DoesNotContain(9009L, starts[0].Audience);
            _log.WriteLine($"AOI 施法条受众：{string.Join(",", starts[0].Audience)}；起手 {starts.Count} 次");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 走v1的怪一条施法条事件都不发()
    {
        // 反向对照：装配**没有开技能能力位**的内容（ai_profile=0），跑同样长的时间必须 0 条事件。
        // 少了这条对照，上面两条的"NotEmpty"可能被一个"逢 tick 就发事件"的实现同样满足。
        var receipt = MonsterContentLoader.LoadFile(Fixture("dev_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            var world = MakeCastingWorld(out _, false, 3f, 0f);
            var found = RunUntilCastEvents(world, 1200, 1);
            Assert.Empty(found);
            Assert.Equal(0, world.MonsterCastStartEvents);
            Assert.Equal(0, world.MonsterCastStopEvents);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 施法载荷与系别位表能往返且位号与域号同源()
    {
        // MemoryPack 的槽位错位不报错，只会把值放错地方，所以逐字段回读。
        var original = new Horizon.Game.Message.Sync.MonsterCastPayload
        {
            SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Fire,
            Interruptible = false,
            AllowMovement = true,
            TargetCount = 3,
            AoeRadius = 4.5f,
        };
        var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
        var back = MemoryPack.MemoryPackSerializer.Deserialize<Horizon.Game.Message.Sync.MonsterCastPayload>(bytes);

        Assert.Equal(original.SchoolMask, back.SchoolMask);
        Assert.Equal(original.Interruptible, back.Interruptible);
        Assert.Equal(original.AllowMovement, back.AllowMovement);
        Assert.Equal(original.TargetCount, back.TargetCount);
        Assert.Equal(original.AoeRadius, back.AoeRadius, 3);

        // 位号==域号（1金 2木 3水 4火 5土）：这条是全工程"怪属于哪个域"的既有口径，
        // 错一位的后果是"火系技能画成水的颜色"，没有任何一层会报错。
        Assert.Equal(1, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Metal));
        Assert.Equal(2, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Wood));
        Assert.Equal(3, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Water));
        Assert.Equal(4, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Fire));
        Assert.Equal(5, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Earth));

        // 物理位与 0 都不该被硬塞成一个五行颜色。
        Assert.Equal(0, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(Horizon.Game.Message.Sync.MonsterSchoolBits.Physical));
        Assert.Equal(0, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(0));

        // 多系技能取最低位（稳定规则，不是"最后写入的那个"）。
        Assert.Equal(2, Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(
            Horizon.Game.Message.Sync.MonsterSchoolBits.Wood | Horizon.Game.Message.Sync.MonsterSchoolBits.Fire));

        // 校验器的上限必须与位表同源：CLI 那份是独立写的数（不能引用 Message 侧常量），
        // 所以这里逐值对账——上限写小了会拒掉合法配置，写大了会让越界位静默进库、
        // 客户端最终画成"未定系"的中性色。
        Assert.Equal(Horizon.MonsterCli.MonsterValidator.MaxSchoolMask, Horizon.Game.Message.Sync.MonsterSchoolBits.AllMask);

        var badDraft = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100003, Name = "越界系别" };
        badDraft.Behavior.LeashRadius = 30;
        badDraft.Behavior.AggroRadius = 10;
        badDraft.Skills.Add(new Horizon.MonsterCli.MonsterSkillDraft
        {
            SkillId = 10101,
            MaxRange = 8,
            SchoolMask = Horizon.MonsterCli.MonsterValidator.MaxSchoolMask + 1,
        });
        var bad = Horizon.MonsterCli.MonsterValidator.Validate(badDraft, null);
        Assert.False(bad.Ok);
        Assert.Contains(bad.ErrorPaths, p => p.EndsWith("schoolMask", StringComparison.Ordinal));

        // 正向同夹具对照：合法位（水=8）必须过，否则这条判据只是"永远红"。
        badDraft.Skills[0].SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Water;
        Assert.True(Horizon.MonsterCli.MonsterValidator.Validate(badDraft, null).Ok,
            "水系（位 8）被拒了：上限或位序与 MonsterSchoolBits 不一致");
    }

    [Fact]
    public void 校验器说清站桩档那几格是死配置()
    {
        // 站桩（移动方式=3）在运行期不产生位移（主人 `MonsterPatrolCore.FollowProducesMovement`），
        // 所以 leashRadius / wanderRadius / patrolRouteId 对它都是"改了什么都不发生"的那一格。
        // 判据必须有**正反两半**：只测"站桩会念"抓不出"见谁都在念"，只测"步行不念"又抓不出"根本不念"。
        static Horizon.MonsterCli.MonsterValidationResult Run(
            int followType, double leash, double wander, int routeId)
        {
            var d = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100011, Name = "站桩校验" };
            d.Behavior.FollowType = followType;
            d.Behavior.LeashRadius = leash;
            d.Behavior.WanderRadius = wander;
            d.Behavior.PatrolRouteId = routeId;
            d.Behavior.AggroRadius = 10;
            d.Behavior.MeleeSwingSeconds = 1.5f;
            return Horizon.MonsterCli.MonsterValidator.Validate(d, null);
        }

        var staticWithDeadConfig = Run(3, 30, 6, 7);
        Assert.True(staticWithDeadConfig.Warnings.Any(w => w.Contains("leashRadius")),
                    "站桩 + 脱战半径/游荡半径没念：策划改完这两格什么都不会发生而没有任何提示");
        Assert.True(staticWithDeadConfig.Warnings.Any(w => w.Contains("巡逻线")),
                    "站桩 + 巡逻线没念：那条线对它根本不生效");

        // 反面对照一：同样的三格挂在步行档上必须**不念**（否则这条 Warn 成了常驻噪音，
        // 而噪音会把人训练成忽略闸门——见"闸门只该挡有害的那一类"）。
        var walkSameNumbers = Run(0, 30, 6, 7);
        Assert.True(!walkSameNumbers.Warnings.Any(w => w.Contains("leashRadius") || w.Contains("巡逻线")),
                    "步行档也被念了：这条判据看的不是移动方式，而是数字本身");

        // 反面对照二：站桩但那几格都是"没配"（0）时必须不念——
        // 站桩本身不是错误，只有"配了却不生效"才是。
        var staticWithoutThoseFields = Run(3, 0, 0, 0);
        Assert.True(!staticWithoutThoseFields.Warnings.Any(w => w.Contains("死配置") || w.Contains("不生效")),
                    "什么都没配的站桩怪被念了：这条 Warn 在无事可报时也在响");
    }

    [Fact]
    public void 校验器说清检测位今天没有任何人读()
    {
        // 这一格的设计口径是 bit0 视觉 / bit1 听觉 / bit2 隐蔽探测，但**今天两条 AI 通路都不读它**
        // （`MonsterBehaviorConfig.CanDetectStealth` 全仓零调用方）。
        // 于是"我给它开了真视"与"根本没配"在玩家那边是同一个游戏——必须当场念出来，
        // 而不是等人去线上试出来。判据带正反两半：缺省那份不许念（噪音会把人训练成忽略闸门）。
        static Horizon.MonsterCli.MonsterValidationResult Run(int detectMask)
        {
            var d = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100012, Name = "检测位校验" };
            d.Behavior.AggroRadius = 10;
            d.Behavior.LeashRadius = 30;
            d.Behavior.MeleeSwingSeconds = 1.5f;
            d.Behavior.DetectMask = detectMask;
            return Horizon.MonsterCli.MonsterValidator.Validate(d, null);
        }

        Assert.True(MonsterBehaviorConfig.Default.DetectMask == 1,
                    "缺省那份不是 1：这条判据比的就不是「有没有偏离缺省」了");

        // 正半：开了位（5 = 视觉＋隐蔽探测）→ 念。
        var stealthOn = Run(5);
        Assert.True(stealthOn.Warnings.Any(w => w.Contains("detectMask")),
                    "开了检测位却没念「今天没人读」：策划改完什么都不发生而没有任何提示");
        // 反半一：缺省值不念（这一格本来就这么发的，见 020 的种子脚本）。
        var atDefault = Run(MonsterBehaviorConfig.Default.DetectMask);
        Assert.True(!atDefault.Warnings.Any(w => w.Contains("detectMask")),
                    "缺省检测位也被念了：这条 Warn 会在全部种子怪上长鸣");
        // 反半二：**关掉**（0）也念——0 同样偏离缺省，而"关掉听觉"这种改法最容易被人当成有效操作。
        var allOff = Run(0);
        Assert.True(allOff.Warnings.Any(w => w.Contains("detectMask")),
                    "把检测位改成 0 不念：那也是一次「改了不生效」的编辑");
        // 越界那一条照旧是 Error（不是 Warn），也别让新 Warn 把它的归因盖掉。
        var outOfRange = Run(1 << 20);
        Assert.True(outOfRange.Errors.Any(e => e.Contains("detectMask")),
                    "越界检测位不再是 Error：范围闸门被这条新 Warn 顶掉了");
    }

    [Fact]
    public void 校验器对状态号改为引用检查而起手动作仍然只念不拦()
    {
        // 同一族里现在只剩一格是"合法数据、零消费者"：`start_emote`（客户端起手动画表还没落地）。
        // `status_id` / `status_duration_seconds` 从切片 59 起**有消费者**（怪物施法命中玩家 → DS 施加状态、
        // 封移动、写快照状态位），所以它那两格从"没人读的 Warn"改成**引用检查**：
        // 号不认识 ⇒ Error；号认识但没时长 ⇒ Warn（缺一格掷不出效果）。
        // `aggro_say_id` 2026-10-28 起也有了消费者（文本旁挂源文件 → 内容包 → DS 发号 → 客户端气泡）。
        // 一条会说谎的警告会把真问题埋成背景噪声，所以这三处各自换了口径而不是"继续念"。
        static Horizon.MonsterCli.MonsterValidationResult Run(
            int aggroSayId, int statusId, double statusSeconds, int startEmote)
        {
            var d = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100013, Name = "悬空格校验" };
            d.AggroSayId = aggroSayId;
            d.Behavior.AggroRadius = 10;
            d.Behavior.LeashRadius = 30;
            d.Behavior.MeleeSwingSeconds = 1.5f;
            d.Skills.Add(new Horizon.MonsterCli.MonsterSkillDraft
            {
                SkillId = 9001,
                ChanceBps = 10000,
                MaxRange = 8,
                TargetCount = 1,
                DamageScaleBps = 10000,
                StatusId = statusId,
                StatusDurationSeconds = statusSeconds,
                StartEmote = startEmote,
            });
            return Horizon.MonsterCli.MonsterValidator.Validate(d, null);
        }

        bool Says(Horizon.MonsterCli.MonsterValidationResult r, string key)
            => r.Warnings.Any(w => w.Contains(key, StringComparison.Ordinal))
            || r.Errors.Any(e => e.Contains(key, StringComparison.Ordinal));

        // 缺省那份（都不配）：三条都不许念，否则 Warn 会在全部草稿上长鸣。
        var clean = Run(0, 0, 0, 0);
        Assert.True(!Says(clean, "aggroSayId") && !Says(clean, "statusId") && !Says(clean, "startEmote"),
                    "什么都没配也被念：这些判据看的不是「有没有效果」，而是数字本身");

        // 喊话那格配了**不许**念——它已有通路，每填必警会把"真悬空"训成背景噪声。
        Assert.True(!Says(Run(70001, 0, 0, 0), "aggroSayId"),
                    "配了索敌喊话仍在警告：这条通路已经接好，Warn 变成了会说谎的噪声");

        // 状态号：合法号 + 有时长 ⇒ **一次都不许念**（这一格已经有消费者了）。
        // 这条负断言就是"那条旧 Warn 不许复活"的闸门。
        Assert.True(!Says(Run(0, 1, 3.5, 0), "statusId"),
                    "配了合法的眩晕号仍在警告：状态层已经接上消费者，这条 Warn 现在会说谎");

        // 号不认识 ⇒ 必须是 **Error**（运行期会被拒收，玩家只看到"这怪从不控人"）。
        var badKind = Run(0, 12345, 3.5, 0);
        Assert.True(badKind.Errors.Any(e => e.Contains("statusId", StringComparison.Ordinal)),
                    "词表外的状态号没被当成错误：它能一路存进库并在运行期被静默拒收");
        Assert.False(badKind.Ok);

        // 号认识但没时长 ⇒ Warn（id 与时长是一对，缺一格掷不出效果）。
        Assert.True(Run(0, 1, 0, 0).Warnings.Any(w => w.Contains("statusId", StringComparison.Ordinal)),
                    "配了状态号却没给时长也不念：这一条运行期会被拒收，而策划以为它能晕人");

        Assert.True(Says(Run(0, 0, 0, 7), "startEmote"), "配了起手动作不念：那只怪不会做这个动作而没人知道");

        // 反向还要证明"不是整个校验器坏了才不念"：同一份配了喊话的草稿，另一格悬空照样要响。
        var sayPlusEmote = Run(70001, 0, 0, 7);
        Assert.True(!Says(sayPlusEmote, "aggroSayId") && Says(sayPlusEmote, "startEmote"),
                    "配了喊话后整个校验器一起哑掉：这条负断言就只是在读空转");
    }

    [Fact]
    public void 面板写一句台词能原样读回而重复号不会长出第二行()
    {
        // 文本住在旁挂源文件里（库里没有文本表，口径见 D15），所以"面板写 → 读侧回读"是
        // 一次真的跨函数往返：只断言 UpsertText 返回 ok 等于没验——写出的形状读不回来的话，
        // 表现是"策划在面板里改了台词而游戏里念的是旧的那句"，而没人会怀疑格式。
        var path = Path.Combine(Path.GetTempPath(), "hundun_texts_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // ① 文件还不存在：那是"还没人配文本"的如实状态，不是错误。
            var missing = MonsterTextsExport.LoadSource(path);
            Assert.True(missing.Ok && missing.FileMissing && missing.Rows.Count == 0);

            // ② 第一次写：写完必须能被**同一个读侧**原样读回。
            var first = MonsterTextsExport.UpsertText(path, 6, 1, "锐金出鞘，来者自断。", 0);
            Assert.True(first.Ok, "首次写文本失败：" + string.Join("；", first.Problems));
            Assert.Equal("upsert", first.Action);
            Assert.Equal(1, first.RowsAfter);
            var r1 = MonsterTextsExport.LoadSource(path);
            Assert.True(r1.Ok && !r1.FileMissing);
            var row = Assert.Single(r1.Rows);
            Assert.Equal(6, row.MonsterId);
            Assert.Equal(1, row.SayId);
            Assert.Equal("锐金出鞘，来者自断。", row.Text);
            Assert.Equal(0, row.Emote);

            // ③ 同一个号再写一次：**换掉**而不是追写。追写的表现是读侧只留第一条，
            // 于是"我明明改了台词"与"没改"在源文件里差一行而游戏里一模一样。
            var again = MonsterTextsExport.UpsertText(path, 6, 1, "换了第二句", 7);
            Assert.True(again.Ok);
            Assert.Equal("upsert", again.Action);
            Assert.Equal(1, again.RowsAfter);
            var row2 = Assert.Single(MonsterTextsExport.LoadSource(path).Rows);
            Assert.Equal("换了第二句", row2.Text);
            Assert.Equal(7, row2.Emote);

            // ④ 另一号：行数才 +1（"改一行不该动别行"这件事只有数得出来才看得见）。
            Assert.Equal(2, MonsterTextsExport.UpsertText(path, 6, 2, "另一句", 0).RowsAfter);

            // ⑤ 清空 = 撤销这一号，别的一行不动。
            var deleted = MonsterTextsExport.UpsertText(path, 6, 2, "   ", 0);
            Assert.Equal("delete", deleted.Action);
            Assert.Equal(1, deleted.RowsAfter);
            Assert.All(MonsterTextsExport.LoadSource(path).Rows, r => Assert.NotEqual(2, r.SayId));

            // ⑥ 清空一个本来就空的格子：如实说 noop，并且**不创建文件**——
            // "成功"回执与"其实什么都没发生"必须长得不一样，否则面板会把没落盘的编辑报成落盘了。
            var noopPath = Path.Combine(Path.GetTempPath(), "hundun_texts_noop_" + Guid.NewGuid().ToString("N") + ".json");
            var noop = MonsterTextsExport.UpsertText(noopPath, 6, 1, "", 0);
            Assert.True(noop.Ok, "清空一个不存在的号不该算失败：" + string.Join("；", noop.Problems));
            Assert.Equal("noop", noop.Action);
            Assert.False(File.Exists(noopPath), "noop 也把文件建出来了：源目录会长出一堆空壳");

            // ⑦ 非法编号/号：拒绝写，而不是写进去让读侧下次跳过它。
            Assert.False(MonsterTextsExport.UpsertText(path, 0, 1, "没有编号的台词").Ok);
            Assert.False(MonsterTextsExport.UpsertText(path, 6, 0, "号是 0 的台词").Ok,
                         "0 是「没配」的哨兵：让它占一行会让引用检查永远查不出悬空");

            // ⑨ 台词组（AC creature_text 的 group）：运行期按组挑一句，作者侧必须写得进组。
            //    三条：组内序号按写入顺序、只改文案不许把组清掉、显式 0 才是移出组。
            var g1 = MonsterTextsExport.UpsertText(path, 6, 11, "第一句", 0, group: 7);
            Assert.True(g1.Ok, "写组失败：" + string.Join("；", g1.Problems));
            Assert.Equal(7, g1.Group);          // 回执要念出落成了哪一组，不能只给 ok
            Assert.True(MonsterTextsExport.UpsertText(path, 6, 12, "第二句", 0, group: 7).Ok);
            var groupRows = MonsterTextsExport.LoadSource(path).Rows
                .Where(r => r.MonsterId == 6 && r.Group == 7).OrderBy(r => r.Seq).ToList();
            Assert.Equal(2, groupRows.Count);
            Assert.Equal(new[] { 1, 2 }, groupRows.Select(r => r.Seq).ToArray());

            // 只改文案（不给 group）：组与序号都不许动——只改一个词就换位置会让"组里第几句"变成写入历史
            Assert.True(MonsterTextsExport.UpsertText(path, 6, 11, "第一句（改）", 0).Ok);
            var row11 = MonsterTextsExport.LoadSource(path).Rows.Single(r => r.SayId == 11);
            Assert.Equal(7, row11.Group);
            Assert.Equal(1, row11.Seq);

            // 显式 group: 0 = 移出组（这时序号也归零：不成组的行没有组内位置）
            Assert.Equal(0, MonsterTextsExport.UpsertText(path, 6, 12, "第二句", 0, group: 0).Group);
            var row12 = MonsterTextsExport.LoadSource(path).Rows.Single(r => r.SayId == 12);
            Assert.Equal(0, row12.Group);
            Assert.Equal(0, row12.Seq);

            // 负数组号：拒绝写（0 = 不成组，负数没有含义）
            Assert.False(MonsterTextsExport.UpsertText(path, 6, 13, "负组", 0, group: -1).Ok);

            // ⑧ 空转防护：上面那些 Ok 断言全都只在"写这件事真的发生过"时才有意义。
            Assert.True(File.Exists(path), "整条用例一个字节都没落盘：所有断言都在读同一份初始状态");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 文本源读不出的时候写入拒绝覆盖而坏行被逐条点名()
    {
        // 这一条防的是最贵的那种失败：**一次面板保存把整个源文件换成一行**。
        // 覆盖写在这里是数据丢失，不是"方便"，所以判据是"文件字节没变"而不是"返回了 false"。
        var path = Path.Combine(Path.GetTempPath(), "hundun_texts_bad_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ 这不是 JSON", new System.Text.UTF8Encoding(false));
            var bytes = File.ReadAllBytes(path);

            var loaded = MonsterTextsExport.LoadSource(path);
            Assert.False(loaded.Ok, "坏文件被读成了 ok：那下面的拒绝分支永远不会走到");

            var refused = MonsterTextsExport.UpsertText(path, 6, 1, "我来了", 0);
            Assert.False(refused.Ok);
            Assert.True(refused.Problems.Any(p => p.Contains("拒绝覆盖", StringComparison.Ordinal)),
                        "拒绝写却没说为什么：面板只会显示一句失败");
            Assert.True(File.ReadAllBytes(path).SequenceEqual(bytes),
                        "读不出的文件被改写：里面本来可能有读得好的行（这里是整份内容）");

            // 坏行（文本号 0）在读侧是被跳过的，重写会把那一行一起带走——
            // 这件事必须出现在回执里，否则用户以为"我只动了一格"而另一格悄悄没了。
            File.WriteAllText(path,
                "{\"texts\":[{\"monsterId\":6,\"sayId\":0,\"text\":\"哨兵号占的一行\"}," +
                "{\"monsterId\":11,\"sayId\":1,\"text\":\"这句是好的\"}]}",
                new System.Text.UTF8Encoding(false));
            var mixed = MonsterTextsExport.UpsertText(path, 6, 2, "新的一句", 0);
            Assert.True(mixed.Ok, "混合文件里有一条坏行不该整体拒绝：" + string.Join("；", mixed.Problems));
            Assert.True(mixed.Problems.Count >= 1,
                        "重写带走了被跳过的行而回执没提：下一次没人知道那句哨兵号没了（Action=" + mixed.Action + "）");
            var after = MonsterTextsExport.LoadSource(path);
            Assert.Equal(2, after.Rows.Count);
            Assert.All(after.Rows, r => Assert.NotEqual(0, r.SayId));
            Assert.Contains(after.Rows, r => r.MonsterId == 11 && r.Text == "这句是好的");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 文本源那串相对路径在面板的工作目录下也会定回仓库里那一份()
    {
        // 复现的形状（不是假想的）：面板起的 CLI 把 WorkingDirectory 设成可执行文件所在处
        // （MonsterCliBridge.Run 里那句 `WorkingDirectory = Path.GetDirectoryName(exe)`），
        // 而默认位置写的是相对仓库根的一串 ⇒ 读会报"源文件不存在"、写会在 bin 底下长出一棵假树。
        // 手敲 CLI 时当前目录就是仓库根，所以这条只在面板里坏——上一轮那条探针每次都显式传了
        // --texts，因此它恰好没覆盖到缺省那一支（= 只测了成功路径）。
        //
        // 这条用例跑在测试自己的 bin 里（Horizon.Game.Gateway.Tests/bin/...），
        // 也就是**同样不在仓库根**，所以它天然复现面板那个处境，不需要伪造工作目录。
        var resolved = Horizon.MonsterCli.MonsterTextsExport.ResolveSourcePath(null);
        var resolvedWindows = resolved.Replace('/', '\\');

        Assert.True(Path.IsPathRooted(resolved), "解析结果还不是绝对路径：" + resolved);
        Assert.False(resolvedWindows.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase),
                     "解析结果还在 bin 底下（那就是面板读写到的假树）：" + resolved);
        var norm = resolved.Replace('\\', '/');
        Assert.True(norm.EndsWith("HundunWorld/Data/Monsters/monster_texts.json", StringComparison.OrdinalIgnoreCase),
                    "解析到的不是仓库里那一串：" + resolved);
        Assert.True(File.Exists(resolved),
                    "解析出来的那份不在盘上（真源文件在仓库里，说明锚点找错了）：" + resolved);

        // 客户端表那一串共用同一条算法（两处各写一遍就会在"该往哪写"上分家）。
        var tableWindows = Horizon.MonsterCli.MonsterTextsExport.ResolveClientTablePath(null).Replace('/', '\\');
        var tableForward = tableWindows.Replace('\\', '/');
        Assert.True(tableForward.EndsWith("HundunWorld/Data/Monsters/client_texts.json", StringComparison.OrdinalIgnoreCase),
                    "客户端表解析到的不是仓库里那一串：" + tableWindows);
        Assert.False(tableWindows.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase),
                     "客户端表也会被算进 bin 底下：" + tableWindows);

        // 给了绝对路径就照用：面板/脚本显式指定临时文件时，解析不许自作主张改道。
        var temp = Path.Combine(Path.GetTempPath(), "hzn_text_resolve_" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Equal(temp, Horizon.MonsterCli.MonsterTextsExport.ResolveSourcePath(temp));
        try
        {
            // 显式给一串**相对**路径时按标准 CLI 语义走（相对当前目录），不许被换成仓库里那一份：
            // 人在别处拿着一份局部文本源试跑，"我指的那份"必须就是被读的那份。
            var inCwd = Path.Combine(Directory.GetCurrentDirectory(), "hzn_text_resolve_rel_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(inCwd, "{\"texts\":[]}");
            try
            {
                var viaCwd = Horizon.MonsterCli.MonsterTextsExport.ResolveSourcePath(Path.GetFileName(inCwd));
                Assert.Equal(inCwd, viaCwd);
            }
            finally
            {
                if (File.Exists(inCwd)) File.Delete(inCwd);
            }

            // 缺省那一串**不**走这条：它相对的是"仓库根"这个约定，而调用方的工作目录未必是仓库根
            // （面板就是），所以它必须由上面那段往上走的锚点搜索来定。
            // 前提要自己站得住：这条用例之所以能复现面板的处境，是因为测试进程的工作目录在 bin 底下。
            // 哪天跑法变了（工作目录不再是 bin），前提就不成立，得当场红而不是静默变成一条空判据。
            Assert.True(Directory.GetCurrentDirectory().Replace('/', '\\')
                            .Contains("\\bin\\", StringComparison.OrdinalIgnoreCase),
                        "这条用例的前提没了：它复现的是「工作目录在 bin 底下」那个处境（面板就是这样）。" +
                        "现在工作目录是 " + Directory.GetCurrentDirectory() + " ⇒ 判据退化成空转，请改用地面构造目录来测");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        // 读侧要能把解析结果说出去：回执里那句路径如果是相对的，人就分不清是哪一份。
        var receipt = Horizon.MonsterCli.MonsterTextsExport.LoadSource(resolved);
        Assert.Equal(resolved, receipt.Path);
        Assert.True(receipt.Rows.Count >= 1,
                    "仓库里那份文本源读不出任何行（" + string.Join("；", receipt.Problems) + "）：判据在空转");
    }

    /// <summary>
    /// 路径解析的两条顺序（先当前目录 / 先往上找仓库）在<b>注入起点</b>之后才分得开。
    /// <para>反证 R2 第一版跑出绿，量出来的不是"判据太松"而是<b>几何问题</b>：
    /// 测试进程的 <c>CurrentDirectory</c> 与 <c>AppContext.BaseDirectory</c> 是同一个目录，
    /// 于是"当前目录那份"与"往上走第一站那份"是同一个路径，换顺序改不了任何读数
    /// ——那条变异在这一几何下是<b>等价变异体</b>，再怎么写断言都抓不住。</para>
    /// <para>正解是把两个起点抽成参数（<c>ResolveFrom</c>），在这里各给一棵自己的临时树：
    /// "在哪读写"这件事的顺序从此可以被离线断言。<b>判据抓不住时要先问是不是几何让两条路同形</b>。</para>
    /// </summary>
    [Fact]
    public void 路径解析的两条顺序在注入起点之后各自有牙()
    {
        var root = Path.Combine(Path.GetTempPath(), "hzn_text_order_" + Guid.NewGuid().ToString("N"));
        var cwdRoot = Path.Combine(root, "cwd");
        var baseRoot = Path.Combine(root, "base", "x64", "net10.0");
        const string relative = "monster_texts.json";
        try
        {
            Directory.CreateDirectory(cwdRoot);
            Directory.CreateDirectory(baseRoot);
            var cwdCopy = Path.Combine(cwdRoot, relative);
            var baseCopy = Path.Combine(baseRoot, relative);
            File.WriteAllText(cwdCopy,
                "{\"texts\":[{\"monsterId\":111,\"sayId\":1,\"text\":\"当前目录那一份\"}]}");
            File.WriteAllText(baseCopy,
                "{\"texts\":[{\"monsterId\":222,\"sayId\":1,\"text\":\"往上走找到的那一份\"}]}");

            // ① 两边都有 ⇒ 显式给的相对串按标准 CLI 语义归**当前目录**（人指哪份读哪份）。
            //    把顺序反过来就会拿到 222 那一份——这正是 R2 要能红的原因。
            var both = Horizon.MonsterCli.MonsterTextsExport.ResolveFrom(null, relative, cwdRoot, baseRoot);
            Assert.Equal(cwdCopy, both);
            Assert.Equal(111, Assert.Single(Horizon.MonsterCli.MonsterTextsExport.LoadSource(both).Rows).MonsterId);

            // ② 显式给的相对串**不许**被改道：哪怕当前目录里没有、而"往上走"会撞见一份同名的，
            //    也得是人指的那一条（没有就是没有，让人看见"那份不存在"，而不是悄悄读另一份）。
            //    这条必须带**目录段**：反证 R3 第一版（"显式串不许改道"那句删掉）跑出绿，
            //    原因是我给它造的是一串裸文件名 —— 裸文件名的"父目录"就是当前目录本身，
            //    永远存在，于是"先按当前目录算"与"当前目录里有才算"两种写法给出同一个答案，
            //    又是一次等价变异体（与 R2 同一族：判据要造出**能分开两个分支**的几何）。
            var subRelative = "sub/monster_texts.json";
            Directory.CreateDirectory(Path.Combine(baseRoot, "sub"));
            var baseSub = Path.Combine(baseRoot, "sub", "monster_texts.json");
            File.WriteAllText(baseSub,
                "{\"texts\":[{\"monsterId\":222,\"sayId\":1,\"text\":\"往上走会撞见的那一份\"}]}");
            Assert.False(Directory.Exists(Path.Combine(cwdRoot, "sub")), "夹具自己得先成立：当前目录里不能有那个子目录");

            var explicitChosen = Horizon.MonsterCli.MonsterTextsExport.ResolveFrom(
                null, subRelative, cwdRoot, baseRoot);
            Assert.Equal(Path.Combine(cwdRoot, "sub", "monster_texts.json"), explicitChosen);
            Assert.False(File.Exists(explicitChosen), "显式相对串被换成仓库那份了：那正是 R3 要拦的改道");

            // ②b **缺省**那一串才享受往上找：当前目录里没有这个目录结构，而 base 那侧有 ⇒ 定到 base。
            //     这就是面板的实际处境（工作目录是 exe 所在处，仓库根在它的上方）。
            Directory.CreateDirectory(Path.Combine(baseRoot, "HundunWorld", "Data", "Monsters"));
            var rescue = Horizon.MonsterCli.MonsterTextsExport.ResolveFrom(
                "HundunWorld/Data/Monsters/monster_texts.json", null, cwdRoot, baseRoot);
            Assert.Equal(Path.Combine(baseRoot, "HundunWorld", "Data", "Monsters", "monster_texts.json"), rescue);

            // ③ 缺省串两处都没有 ⇒ 返回按当前目录算的绝对路径（新建文件落在调用方看得见的地方，
            //    而不是悄悄落到某个往上走碰巧命中的目录底下）。
            var nowhere = Horizon.MonsterCli.MonsterTextsExport.ResolveFrom(
                "brand_new_dir/brand_new_texts.json", null, cwdRoot, baseRoot);
            Assert.Equal(Path.Combine(cwdRoot, "brand_new_dir", "brand_new_texts.json"), nowhere);
            Assert.False(File.Exists(nowhere), "两处都没有时不许凭空认为哪一份存在");

            // ④ 绝对路径照用：解析不许改道（脚本/面板显式指定临时文件那条路）。
            Assert.Equal(cwdCopy, Horizon.MonsterCli.MonsterTextsExport.ResolveFrom(null, cwdCopy, baseRoot, cwdRoot));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void 导航烘焙装配出的文件能被服务端加载器原样读回()
    {
        // 写侧（插件采样 → CLI → MonsterNavBakeCore）与读侧（DsHost 的 MonsterNavGridLoader）
        // 是同一个文件格式的两端，却分处两个进程。这里用**真读侧**回读**真写侧**的产物：
        // 只比对拼出来的字符串等于没验——漂掉一个空格时两边看起来都正常。
        const int width = 4;
        const int height = 3;
        const float cell = 2.5f;
        const float originX = -10f;
        const float originY = 20f;

        // 手工点算期望值（不从实现的输出反推）：
        //   行0：1 1 1 1      → 4 格可走
        //   行1：1 # 2 1      → 阻塞 1 格（下标 5）、难行 1 格（下标 6）→ 3 格可走
        //   行2：0 1 1 1      → 阻塞 1 格（下标 8）→ 3 格可走
        //   合计 10 格可走（含 1 格难行）、2 格阻塞
        var mask = "1111\n1#21\n0111";
        var built = MonsterNavBakeCore.FromMask(width, height, cell, originX, originY, mask);

        Assert.Equal(width * height, built.TotalCells);
        Assert.Equal(10, built.WalkableCells);
        Assert.Empty(built.Problems);

        // 头部标记必须与读侧常量逐字相同（两处各写一个字符串就是两份事实源）。
        Assert.Equal(Horizon.Game.Core.Sim.Server.MonsterNavGridLoader.HeaderToken,
                     MonsterNavBakeCore.MonsterNavGridLoaderHeader);

        var path = Path.Combine(Path.GetTempPath(), "hundun_bake_nav_" + Guid.NewGuid().ToString("N") + ".grid");
        try
        {
            File.WriteAllText(path, built.Text, new System.Text.UTF8Encoding(false));
            var grid = Horizon.Game.Core.Sim.Server.MonsterNavGridLoader.Load(path, out var loadError);

            Assert.True(string.IsNullOrEmpty(loadError), "读侧加载失败：" + loadError);
            Assert.True(grid.IsValid, "读回来的网格不自洽（Width/Height/CellCost 长度对不上）");
            Assert.Equal(width, grid.Width);
            Assert.Equal(height, grid.Height);
            Assert.Equal(cell, grid.CellSize, 3);
            Assert.Equal(originX, grid.OriginX, 3);
            Assert.Equal(originY, grid.OriginY, 3);

            Assert.True(grid.IsWalkable(0));
            Assert.False(grid.IsWalkable(5), "行1第2格是 '#'，必须读成阻塞");
            Assert.True(grid.CostOf(6) == MonsterNavBakeCore.HardCost,
                        "难行格必须保留代价 " + MonsterNavBakeCore.HardCost + "，不能退化成普通格（实得 " + grid.CostOf(6) + "）");
            Assert.False(grid.IsWalkable(8), "行2第1格是 '0'，必须读成阻塞");

            // 空转防护：判据必须真的数过非阻塞格，否则上面几条 IsWalkable 断言会一起空转。
            var opened = 0;
            for (var i = 0; i < grid.CellCost.Length; i++) if (grid.IsWalkable(i)) opened++;
            Assert.Equal(10, opened);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 导航烘焙对坏输入大声拒绝而不是写出一张半吊子图()
    {
        // 这四类输入的共同点是"看起来能跑"：长度差一格、字符拼错、整张全阻塞、尺寸单位填错。
        // 写下去的后果都是怪忽然"看不见路"，而现场只有一份静默生成的文件。
        var shortMask = MonsterNavBakeCore.FromMask(4, 3, 2f, 0f, 0f, "11111111111");
        Assert.Equal(0, shortMask.TotalCells);
        Assert.Contains(shortMask.Problems, p => p.Contains("掩码长度不符"));

        var badChar = MonsterNavBakeCore.FromMask(2, 2, 2f, 0f, 0f, "11X1");
        Assert.Contains(badChar.Problems, p => p.Contains("掩码第 3 个字符"));

        var allBlocked = MonsterNavBakeCore.FromMask(3, 3, 2f, 0f, 0f, new string('0', 9));
        Assert.Equal(0, allBlocked.WalkableCells);
        Assert.Contains(allBlocked.Problems, p => p == MonsterNavBakeCore.AllBlockedProblem);

        var allOpen = MonsterNavBakeCore.FromMask(3, 3, 2f, 0f, 0f, new string('1', 9));
        Assert.Equal(9, allOpen.WalkableCells);
        Assert.Contains(allOpen.Problems, p => p.Contains("全可走"));

        // 非法尺寸与格长：必须在装配阶段就拦下，别等到加载器报"头部数值非法"。
        Assert.Contains(MonsterNavBakeCore.FromMask(0, 3, 2f, 0f, 0f, "111").Problems, p => p.Contains("格数非法"));
        Assert.Contains(MonsterNavBakeCore.FromMask(4, 3, 0f, 0f, 0f, "111111111111").Problems, p => p.Contains("格长非法"));
        Assert.Contains(MonsterNavBakeCore.FromMask(MonsterNavBakeCore.MaxSide + 1, 2, 2f, 0f, 0f, "1").Problems,
                        p => p.Contains("超过单边上限"));
    }

    [Fact]
    public void 技能挑选方式决定打到谁()
    {
        // 内核这一层以前只有"打首要目标"是真的，其余四种挑法（随机/最近/血最低/AOE）
        // 全停在枚举定义里——所以每种挑法都要各自断言"打到的是谁"，而不是只断言"没抛异常"。
        var caster = new MonsterSkillMount
        {
            SkillId = 20201,
            Slot = MonsterSkillSlot.Combat,
            ChanceBasisPoints = 10000,
            MaxRange = 20f,
            TargetSelect = MonsterTargetSelect.AoeAroundThreat,
            TargetCount = 3,
            AoeRadius = 6f,
            SchoolMask = 8,
            Interruptible = true,
            AllowMovement = true,
        };
        // 候选按 EntityId 升序（内核的确定性前置契约）。10/11/12 挤在一起，20 在远处。
        var candidates = new[]
        {
            new MonsterCastingCore.MonsterCastCandidate(10UL, 0f, 0f, 100, true, true),
            new MonsterCastingCore.MonsterCastCandidate(11UL, 2f, 0f, 40, true, true),
            new MonsterCastingCore.MonsterCastCandidate(12UL, 4f, 0f, 80, true, false),
            new MonsterCastingCore.MonsterCastCandidate(20UL, 100f, 0f, 50, true, true),
        };
        var output = new ulong[8];

        Assert.True(MonsterCastingCore.SelectTargets(caster, 10UL, 0f, 0f, candidates, 99UL, 600L, output, out var count, out var requested));
        // 圆心 = 首要目标(10) 的位置；半径 6 米圈进 10/11/12，20 在 100 米外。
        Assert.Equal(3, count);
        Assert.Equal(new[] { 10UL, 11UL, 12UL }, output.Take(3).ToArray());
        Assert.Equal(3, requested);

        // 上限截断要"看得见"：想要 8 个而圈内只有 3 个时，requested 与 count 的差就是截断证据。
        var greedy = caster;
        greedy.TargetCount = 8;
        Assert.True(MonsterCastingCore.SelectTargets(greedy, 10UL, 0f, 0f, candidates, 99UL, 600L, output, out count, out requested));
        Assert.Equal(3, count);
        Assert.Equal(8, requested);

        // 首要目标已经不在（跑丢了/死了）：AOE 必须退化成"打一个人"，而不是放一个空技能。
        Assert.True(MonsterCastingCore.SelectTargets(caster, 777UL, 0f, 0f, candidates, 99UL, 600L, output, out count, out requested));
        Assert.Equal(1, count);
        Assert.Equal(10UL, output[0]);        // 施法者在 (0,0)：最近的是 10

        // 死亡目标不能顶包（Engageable=false 的一律不选）。
        var wounded = new[]
        {
            new MonsterCastingCore.MonsterCastCandidate(10UL, 0f, 0f, 100, false, true),
            new MonsterCastingCore.MonsterCastCandidate(11UL, 2f, 0f, 60, true, true),
        };
        Assert.True(MonsterCastingCore.SelectTargets(caster, 10UL, 0f, 0f, wounded, 99UL, 600L, output, out count, out requested));
        Assert.Equal(1, count);
        Assert.Equal(11UL, output[0]);

        // 其余四种挑法各自验一次。
        var single = caster;
        single.TargetSelect = MonsterTargetSelect.CurrentThreat;
        single.TargetCount = 1;
        Assert.True(MonsterCastingCore.SelectTargets(single, 12UL, 0f, 0f, candidates, 99UL, 600L, output, out count, out requested));
        Assert.Equal(12UL, output[0]);

        single.TargetSelect = MonsterTargetSelect.Nearest;
        Assert.True(MonsterCastingCore.SelectTargets(single, 12UL, 3.5f, 0f, candidates, 99UL, 600L, output, out count, out requested));
        Assert.Equal(12UL, output[0]);        // 施法者站在 3.5：0.5 米外的 12 才是最近的（不是序号最小的那个）

        single.TargetSelect = MonsterTargetSelect.LowestHp;
        Assert.True(MonsterCastingCore.SelectTargets(single, 10UL, 0f, 0f, candidates, 99UL, 600L, output, out count, out requested));
        Assert.Equal(11UL, output[0]);        // 40 血最低（补刀怪先杀残的）

        // 同血量取较小 ID（补刀怪不能因为数组顺序而换人）。
        var tie = new[]
        {
            new MonsterCastingCore.MonsterCastCandidate(30UL, 0f, 0f, 50, true, true),
            new MonsterCastingCore.MonsterCastCandidate(31UL, 0f, 0f, 50, true, true),
        };
        Assert.True(MonsterCastingCore.SelectTargets(single, 0UL, 0f, 0f, tie, 99UL, 600L, output, out count, out requested));
        Assert.Equal(30UL, output[0]);

        // 随机只认威胁表内的成员（12 不在表里），且同一个 tick 重放必须一模一样。
        single.TargetSelect = MonsterTargetSelect.RandomInThreat;
        var pickedPerTick = new Dictionary<long, ulong>();
        for (var rep = 0; rep < 40; rep++)
        {
            var tick = 600L + rep;
            Assert.True(MonsterCastingCore.SelectTargets(single, 10UL, 0f, 0f, candidates, 99UL, tick, output, out count, out requested));
            Assert.Equal(1, count);
            Assert.True(output[0] == 10UL || output[0] == 11UL || output[0] == 20UL,
                        "随机挑到了威胁表外的人：" + output[0]);
            if (!pickedPerTick.TryGetValue(tick, out var seen))
            {
                pickedPerTick[tick] = output[0];
                continue;
            }
            Assert.Equal(seen, output[0]);    // 同 tick 第二次调用必须给同一个人
        }
        // 空转防护：40 次里如果一个都没换过，说明掷骰根本没参与挑选（那就是"永远是首要目标"）。
        Assert.True(pickedPerTick.Values.Distinct().Count() > 1,
                    "40 个 tick 挑到的是同一个人：随机这一档没在起作用，断言等于没测");

        // 输出缓冲容量为 0 属于调用方错误：必须返回 false 而不是越界。
        Assert.False(MonsterCastingCore.SelectTargets(caster, 10UL, 0f, 0f, candidates, 99UL, 600L, new ulong[0], out count, out requested));
    }

    [Fact]
    public void AOE技能在权威侧真的打到范围内的多人()
    {
        // 上一条证的是内核会挑人；这条证的才是玩家看得见的东西：**同一次技能伤害落到多个人头上**。
        // 这一层以前没人接（宿主只打 cast.TargetEntityId 一个），所以"内核挑完人、宿主丢掉"这种事
        // 只能靠这条真跑一遍来兜住。
        var templateId = 777;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "演武木桩",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "yan_wu",
            Stats = new MonsterStatsConfig { HpBase = 100000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(45, 5, new[] { 1.0, 2.0, 2.5, 4.0, 4.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 12f;
        behavior.LeashRadius = 60f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 2f;
        behavior.IdleToPatrolSeconds = 9999f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20201,
                Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = 10000,
                CastMs = 300,
                CooldownMs = 900,
                MaxRange = 30f,
                TargetSelect = MonsterTargetSelect.AoeAroundThreat,
                TargetCount = 3,
                AoeRadius = 6f,
                SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
                Interruptible = true,
                AllowMovement = true,
                DamageScale = 1f,
            },
        };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 777;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 12f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 1f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });

            // 两个挤在一起（相距 2 米，必定同圈内）+ 一个在 80 米外（必定不在圈内）。
            Assert.True(world.AddPlayer(4001, 4001, 4f, 0f, 0f, out var r1), r1);
            Assert.True(world.AddPlayer(4002, 4002, 6f, 0f, 0f, out var r2), r2);
            Assert.True(world.AddPlayer(4003, 4003, 80f, 0f, 0f, out var r3), r3);

            var multiHitTicks = 0;
            var farPlayerHit = false;
            for (var i = 0; i < 3600; i++)
            {
                var result = world.Tick();
                var spellTargets = new List<ulong>();
                foreach (var (_, packet) in result.Events)
                {
                    if (packet is not Horizon.Game.Message.Sync.DamagePacket dmg) continue;
                    if (dmg.DamageType != 2) continue;            // 2 = 技能/系别伤害（普攻是 1）
                    spellTargets.Add(dmg.TargetId);
                }
                if (spellTargets.Count >= 2) multiHitTicks++;
                if (multiHitTicks > 0 && world.MonsterCastTargetsSelected >= 2) break;
            }

            Assert.True(world.MonsterCastTargetsSelected >= 2,
                        "技能从来没打到过第二个人：AOE 的挑选结果又在上层被丢掉了");
            Assert.True(multiHitTicks > 0, "没有任何一个 tick 同时给多人发出技能伤害包");
            Assert.Equal(0, world.MonsterCastNoTarget);
            _log.WriteLine($"AOE：选出 {world.MonsterCastTargetsSelected} 人次、截断 {world.MonsterCastTargetShortfalls} 次、" +
                           $"多人命中 tick {multiHitTicks} 个、空技能 {world.MonsterCastNoTarget} 次");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 刷怪点装配同时给出等级_血量与刷新秒()
    {
        // 这三样以前分家：等级只在宿主那份点配置里被覆盖，会话内部按模板基准算；
        // 刷新秒的模板公式算完没人用，所有怪都固定 15 秒重生（世界 BOSS 也是）。
        var templateId = 778;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "覆盖等级测试",
            Rank = MonsterRank.RareElite,
            LevelBase = 10,
            LevelMin = 1,
            LevelMax = 300,
            RespawnBaseSeconds = 60f,
            RespawnPerLevelSeconds = 2f,
            Stats = new MonsterStatsConfig { HpBase = 100, HpPerLevel = 10, AtkBase = 5, AtkPerLevel = 1 },
            Xp = new MonsterXpConfig(45, 5, new[] { 1.0, 2.0, 2.5, 4.0, 4.0 }),
        };
        bundle.ByTemplateId[templateId] = config;
        // 刷怪点把等级覆盖到 25，并显式给出"不覆盖刷新"（0 = 走公式）。
        bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
            778, 1, 0L, 0f, 0f, 0f, 0f, templateId, 25, 1, 8f, 0f, 0));
        MonsterContent.Install(bundle);
        try
        {
            var receipt = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1L);
            Assert.Single(receipt.Points);
            Assert.Empty(receipt.Problems);
            var point = receipt.Points[0];

            // 等级 25（覆盖值）→ 血量 = 100 + 10×25 = 350
            Assert.Equal(350, point.MaxHp);

            // 刷新秒 = (60 + 2×25) × 稀有精英倍率 3 = 330 秒（以前这里是默认 15 秒）
            Assert.True(Math.Abs(point.RespawnDelaySeconds - 330f) < 0.5f,
                        "刷新秒没走模板公式（实得 " + point.RespawnDelaySeconds + "）");

            // 显式覆盖仍然优先于公式
            bundle.SpawnPoints.Clear();
            bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
                778, 1, 0L, 0f, 0f, 0f, 0f, templateId, 25, 1, 8f, 7f, 0));
            MonsterContent.Install(bundle);
            var overridden = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1L).Points[0];
            Assert.True(Math.Abs(overridden.RespawnDelaySeconds - 7f) < 0.001f);

            // 模板没装配内容时必须显形，而不是安静地退回默认值
            bundle.ByTemplateId.Clear();
            MonsterContent.Install(bundle);
            var broken = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1L);
            Assert.NotEmpty(broken.Problems);
            Assert.Contains(broken.Problems, p => p.Contains("没有装配内容"));
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 刷怪点分片号真的把点挡在本会话之外()
    {
        // 这一格在面板上开出来之前，`SpawnPointConfig.ShardId` 全仓**只有写、没有比较**：
        // 策划把一条线只配在二区，游戏里照刷——库里有值、世界没变化，正是"配置格没人读"。
        // 判据 = 装配出口按会话分片挡点：0 = 任意分片，非 0 必须一致；同夹具里那三个点
        // 既要有被挡的，也要有留下来的（只断言"少了几个"会在"全被挡"时同样绿）。
        const int templateId = 779;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateId] = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "分片门测试",
            Rank = MonsterRank.Normal,
            LevelBase = 5,
            LevelMin = 5,
            LevelMax = 5,
            Stats = new MonsterStatsConfig { HpBase = 100, HpPerLevel = 10, AtkBase = 5, AtkPerLevel = 1 },
            Xp = new MonsterXpConfig(45, 5, new[] { 1.0 }),
        };
        // 1 号点 = 任意分片；2 号点 = 一片；3 号点 = 二片（同一模板，位置错开以免实体 id 撞）
        bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
            1, 0, 0L, 0f, 0f, 0f, 0f, templateId, null, 1, 8f, 0f, 0));
        bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
            2, 0, 1L, 20f, 0f, 0f, 0f, templateId, null, 1, 8f, 0f, 0));
        bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
            3, 0, 2L, 40f, 0f, 0f, 0f, templateId, null, 1, 8f, 0f, 0));
        MonsterContent.Install(bundle);
        try
        {
            var one = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(1L);
            Assert.Equal(1, one.SkippedByShard);
            Assert.Equal(2, one.Points.Count);
            // 正例半：任意分片那个点必须在——否则"挡对了"和"整组都没装配"分不开
            Assert.Contains(one.Points, p => p.SpawnPointId == 1);
            Assert.Contains(one.Points, p => p.SpawnPointId == 2);
            Assert.DoesNotContain(one.Points, p => p.SpawnPointId == 3);

            var two = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(2L);
            Assert.Contains(two.Points, p => p.SpawnPointId == 3);
            Assert.DoesNotContain(two.Points, p => p.SpawnPointId == 2);

            // 没给会话分片（<0）：无从判断本会话是哪一片 ⇒ 全部装配，各点保留自己登记的 shard
            var none = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1L);
            Assert.Equal(0, none.SkippedByShard);
            Assert.Equal(3, none.Points.Count);
            Assert.Equal(0L, none.Points.Single(p => p.SpawnPointId == 1).ShardId);
            Assert.Equal(2L, none.Points.Single(p => p.SpawnPointId == 3).ShardId);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 文本源路径的两种写法都要认而空值不许当缺省()
    {
        // 实测事故：`--texts=<路径>` 被旧的"只认空格写法"解析器**静默忽略**，落回默认源文件，
        // 于是两条测试台词被写进了真正的 `monster_texts.json`（回执里 path 还是"对"的）。
        // 所以这条判据两半都要：两种写法等价 + 给了却没带值必须拒绝。
        Assert.True(MonsterTextsExport.TryGetTextsOption(new[] { "set-text", "--texts=C:/x.json" }, out var equalForm, out var p1));
        Assert.Equal("C:/x.json", equalForm);
        Assert.Null(p1);

        Assert.True(MonsterTextsExport.TryGetTextsOption(new[] { "set-text", "--texts", "C:/y.json" }, out var spaceForm, out var p2));
        Assert.Equal("C:/y.json", spaceForm);
        Assert.Null(p2);

        Assert.True(MonsterTextsExport.TryGetTextsOption(new[] { "set-text" }, out var none, out var p3));
        Assert.Null(none);
        Assert.Null(p3);

        Assert.False(MonsterTextsExport.TryGetTextsOption(new[] { "set-text", "--texts=" }, out _, out var emptyProblem));
        Assert.NotNull(emptyProblem);
        Assert.False(MonsterTextsExport.TryGetTextsOption(new[] { "set-text", "--texts" }, out _, out var danglingProblem));
        Assert.NotNull(danglingProblem);
        Assert.False(MonsterTextsExport.TryGetTextsOption(new[] { "set-text", "--texts", "--monster=6" }, out _, out var flagProblem));
        Assert.NotNull(flagProblem);
    }

    [Fact]
    public void 同一组的台词每轮进战换着念()
    {
        // AC `creature_text` 的 group 口径：一个事件组里可以写多句，一次触发随机挑一句。
        // 本作此前只落了"一组一条"⇒ `Group`/`Seq` 两列运行期零读：哪怕文件里写了三句，
        // 游戏里也永远只念行为表指向的那一句（玩家看到的是"这只怪台词特别单调"）。
        const int templateId = 100201;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.Texts[templateId] = new List<MonsterTextSnapshot>
        {
            new MonsterTextSnapshot { SayId = 9001, Group = 7, Seq = 1, Text = "受死吧！" },
            new MonsterTextSnapshot { SayId = 9002, Group = 7, Seq = 2, Text = "又一个送死的" },
            new MonsterTextSnapshot { SayId = 9003, Group = 7, Seq = 3, Text = "血肉之躯……" },
            new MonsterTextSnapshot { SayId = 9101, Group = 0, Seq = 1, Text = "单句" },
        };
        MonsterContent.Install(bundle);
        try
        {
            var seen = new HashSet<int>();
            for (ulong salt = 0; salt < 12; salt++) seen.Add(MonsterContent.ResolveSayId(templateId, 9001, salt));
            Assert.True(seen.Count >= 2, "同一组三句台词只挑出一句：group 仍然没被读");
            // 挑出来的必须是**组内真实存在**的那几号（乱造号会让客户端查不到文案）
            Assert.All(seen, id => Assert.Contains(id, new[] { 9001, 9002, 9003 }));

            // 组 0 = 不成组：永远就是它自己（所有旧内容零变化）
            Assert.Equal(9101, MonsterContent.ResolveSayId(templateId, 9101, 7));
            // 指到不存在的号 → 0（调用方据此计"指针落空"，不能和"没配"并成一格）
            Assert.Equal(0, MonsterContent.ResolveSayId(templateId, 9999, 7));
            // 没配 → 0
            Assert.Equal(0, MonsterContent.ResolveSayId(templateId, 0, 7));
            // 稳定：同一个盐永远是同一号（金轨迹要能逐位复现）
            Assert.Equal(MonsterContent.ResolveSayId(templateId, 9001, 5),
                         MonsterContent.ResolveSayId(templateId, 9001, 5));
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void AOE圆心目标在吟唱中消失后退化成打最近的人()
    {
        // 内核里"圆心目标不见了就退化成打最近一人"早就写好，但这条在**宿主接线**上从没真跑过。
        // 玩家看得见的两种坏表现都出在这里：技能整发凭空消失（该掉的血没掉），
        // 或者朝一个已经离场的人砸出伤害包（客户端收到一个不存在的 target）。
        const int templateId = 12601;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "塌落点检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "ta_luo_dian",
            Stats = new MonsterStatsConfig { HpBase = 100000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(45, 5, new[] { 1.0, 2.0, 2.5, 4.0, 4.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 12f;
        behavior.LeashRadius = 60f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 2f;
        behavior.IdleToPatrolSeconds = 9999f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20907,
                Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = 10000,
                CastMs = 1200,               // 72 tick 的吟唱，够在起手同一 tick 把圆心目标抽走
                CooldownMs = 300,
                MaxRange = 30f,
                TargetSelect = MonsterTargetSelect.AoeAroundThreat,
                TargetCount = 3,
                AoeRadius = 6f,              // 两个玩家相距 2 米，必在同一圈内
                SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
                Interruptible = true,
                AllowMovement = true,
                DamageScale = 1f,
            },
        };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 779;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 12f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 1f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });

            // A 是当前威胁目标（4 米）、B 是"最近的人"（6 米）、C 在 80 米外（必定不在 AOE 圈内）。
            Assert.True(world.AddPlayer(5001, 5001, 4f, 0f, 0f, out var r1), r1);
            Assert.True(world.AddPlayer(5002, 5002, 6f, 0f, 0f, out var r2), r2);
            Assert.True(world.AddPlayer(5003, 5003, 80f, 0f, 0f, out var r3), r3);

            var entityByCharacter = new Dictionary<ulong, ulong>();
            var vanishedAId = 0UL;
            var removedAtTick = -1;
            var hitsAfterRemoval = 0;
            var hitsOnVanishedTarget = 0;
            var hitsOnFarPlayer = 0;
            var sawStart = false;

            void CollectIds(DsTickResult r)
            {
                if (r.WorldSnapshot == null) return;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (!d.Identity.HasValue || d.Identity.Value.EntityType != DsWorldSession.EntityTypePlayer) continue;
                    entityByCharacter[d.Identity.Value.OwnerId] = d.Identity.Value.NetworkId;
                }
            }

            // 先认人再开始测：Spawn 的 Identity 带 OwnerId=角色 id，用它把角色 id 对到 DS 实体 id。
            // 不先把这张表填满，后面的"抽走圆心目标"就无从判断砸到了谁身上。
            for (var i = 0; i < 60; i++)
            {
                CollectIds(world.Tick());
                if (entityByCharacter.ContainsKey(5001UL) && entityByCharacter.ContainsKey(5002UL)
                    && entityByCharacter.ContainsKey(5003UL)) break;
            }
            Assert.True(entityByCharacter.Count == 3,
                        $"三个玩家只认到 {entityByCharacter.Count} 个实体 id：认人通路没走通");

            for (var i = 0; i < 1200; i++)
            {
                var result = world.Tick();
                CollectIds(result);

                var spellTargets = new List<ulong>();
                if (result.Events != null)
                {
                    foreach (var (_, packet) in result.Events)
                    {
                        if (packet is Horizon.Game.Message.Sync.EventPacket ev && ev.Events != null)
                        {
                            foreach (var e in ev.Events)
                            {
                                if (e.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart && !sawStart)
                                {
                                    sawStart = true;
                                    // 起手这一 tick 就把圆心目标抽走：模拟"怪开始念咒的同时那个人退线了"。
                                    vanishedAId = entityByCharacter[5001UL];
                                    Assert.True(world.RemovePlayer(5001), "RemovePlayer 失败");
                                    removedAtTick = i;
                                }
                            }
                        }
                        if (packet is Horizon.Game.Message.Sync.DamagePacket dmg && dmg.DamageType == 2)
                        {
                            spellTargets.Add(dmg.TargetId);
                        }
                    }
                }

                if (removedAtTick < 0) continue;
                // 只认"起手后 72 tick 内"的落点：那一次才是被抽走圆心的**那次在途吟唱**，
                // 更晚的伤害可能来自重新索敌后的下一次施法。
                if (i - removedAtTick > 80) continue;
                foreach (var t in spellTargets)
                {
                    hitsAfterRemoval++;
                    if (t == vanishedAId) hitsOnVanishedTarget++;
                    if (entityByCharacter.TryGetValue(5003UL, out var farC) && t == farC) hitsOnFarPlayer++;
                }
            }

            Assert.True(sawStart, "跑了 1200 tick 一次起手都没有：这条通路根本没进施法");
            Assert.True(removedAtTick >= 0, "没走到抽目标的分支");
            Assert.True(hitsAfterRemoval >= 1,
                        "圆心目标在吟唱中消失后一发伤害都没有：在途技能被整发丢掉了（退化成打最近一人没接上）");
            Assert.Equal(0, hitsOnVanishedTarget);
            Assert.Equal(0, hitsOnFarPlayer);
            Assert.Equal(0, world.MonsterCastNoTarget);
            _log.WriteLine($"AOE 塌落点：抽走后 {hitsAfterRemoval} 次落点、砸到已离场者 {hitsOnVanishedTarget} 次、" +
                           $"砸到圈外 {hitsOnFarPlayer} 次、选空 {world.MonsterCastNoTarget} 次");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 飞行档穿过难走格而地面档绕开它()
    {
        // `follow_type` 的"直线"与"飞行"以前在移动上完全同形（两者都不查图），
        // 编辑器那个下拉于是只是个装饰。现在飞行档也走图，但难走格（陡坡/灌木，代价 20）对它折回基准代价。
        // 判据形态：同一张图、同一起终点，两种走法各自在自己的代价模型下最优，且**两条路径必须真的不同**。
        const int w = 24;
        const int h = 12;
        var costs = new byte[w * h];
        for (var i = 0; i < costs.Length; i++) costs[i] = MonsterNavGrid.BaseCellCost;
        // 一片 12 列宽的灌木（可走，但地面单位绕着走便宜）
        for (var y = 0; y <= 4; y++)
        {
            for (var x = 6; x <= 17; x++) costs[y * w + x] = MonsterNavGrid.HardCellCost;
        }
        // 直线上戳一格墙：逼 A* 真的去算路（视线通畅时内核直接判"可见即可直冲"，代价模型根本用不上）
        costs[2 * w + 5] = MonsterNavGrid.BlockedCellCost;

        var grid = new MonsterNavGrid
        {
            Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = costs,
        };
        var cellsGround = new int[MonsterPathCore.MaxPathCells];
        var cellsFly = new int[MonsterPathCore.MaxPathCells];
        var scratchA = new MonsterNavScratch();
        var scratchB = new MonsterNavScratch();
        scratchA.EnsureCapacity(w * h);
        scratchB.EnsureCapacity(w * h);

        var resGround = MonsterPathCore.FindPath(grid, scratchA, 1f, 2f, 19f, 2f, cellsGround, out var lenGround);
        var resFly = MonsterPathCore.FindPath(grid, scratchB, 1f, 2f, 19f, 2f, cellsFly, out var lenFly,
            0, ignoreHardPenalty: true);
        Assert.True(resGround is MonsterPathResult.Found or MonsterPathResult.Direct, $"地面档结果 {resGround}");
        Assert.True(resFly is MonsterPathResult.Found or MonsterPathResult.Direct, $"飞行档结果 {resFly}");
        Assert.True(lenGround >= 1 && lenFly >= 1, "两档都得给出路径（空路径等于没算）");

        var hardGround = CountHard(grid, cellsGround, lenGround);
        var hardFly = CountHard(grid, cellsFly, lenFly);
        Assert.True(hardFly > hardGround,
            $"飞行档没穿过难走格（fly={hardFly} ground={hardGround}）——把代价折回基准的开关没生效");
        Assert.Equal(0, hardGround);   // 地面档这次真的绕开了整片灌木
        Assert.True(lenFly <= lenGround,
            $"飞行档反而多走了路（fly={lenFly} ground={lenGround}）：它没有理由绕");

        // 反向对照（防空转）：如果两档算出来是同一条路径，上面那些断言全都无意义。
        var same = lenFly == lenGround;
        if (same)
        {
            for (var i = 0; i < lenFly && same; i++) same = cellsFly[i] == cellsGround[i];
        }
        Assert.False(same, "两档算出逐格相同的路径：代价模型对结果毫无影响，这条用例是在空转");

        // 墙对谁都是墙：飞行不穿墙（0 代价格对两档都不可走）。
        var wallCell = 2 * w + 5;
        for (var i = 0; i < lenFly; i++) Assert.True(cellsFly[i] != wallCell, "飞行档穿过了阻挡格");
        for (var i = 0; i < lenGround; i++) Assert.True(cellsGround[i] != wallCell, "地面档穿过了阻挡格");
        _log.WriteLine($"飞行/地面：fly 长度 {lenFly} 穿过难走格 {hardFly}；ground 长度 {lenGround} 穿过 {hardGround}");
    }

    /// <summary>数路径里落在"难走格"上的格数。</summary>
    private static int CountHard(in MonsterNavGrid grid, int[] cells, int length)
    {
        var hard = 0;
        for (var i = 0; i < length; i++)
        {
            if (grid.CostOf(cells[i]) == MonsterNavGrid.HardCellCost) hard++;
        }
        return hard;
    }

    [Fact]
    public void 飞行档查图而直线档穿墙而过()
    {
        // 这条测的是"移动方式真的传进了走图资格"，不是"代价模型认不认"（那是上一条）。
        // 判别形态必须选**两档行为必然不同**的几何：一堵只有顶部有缺口的墙。
        //   飞行档：查图 ⇒ 必须绕到缺口才能过去 ⇒ 途中 Y 明显升高；
        //   直线档：完全不查图 ⇒ 沿 y=2 一路穿墙推过去 ⇒ Y 不动。
        // （先前我用"飞行 vs 地面"当判别，证伪时它照样绿：两档都不会穿墙，测不到这条开关。
        //  判据要挑"开关拨回去就一定红"的那种形态，不是挑"看起来相关"的那种。）
        const int w = 24;
        const int h = 12;
        var costs = new byte[w * h];
        for (var i = 0; i < costs.Length; i++) costs[i] = MonsterNavGrid.BaseCellCost;
        // x=5 一堵墙，只留最上面两行可过
        for (var y = 0; y <= 9; y++) costs[y * w + 5] = MonsterNavGrid.BlockedCellCost;

        var grid = new MonsterNavGrid
        {
            Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = costs,
        };
        var scratchFly = new MonsterNavScratch();
        var scratchStraight = new MonsterNavScratch();
        scratchFly.EnsureCapacity(w * h);
        scratchStraight.EnsureCapacity(w * h);

        var fly = ChaseWithFollow(MonsterFollowType.Fly, grid, scratchFly);
        var straight = ChaseWithFollow(MonsterFollowType.Straight, grid, scratchStraight);

        // 起点 y=2：飞行档为了找缺口必须往北抬到 y≈10，直线档一路平推。
        Assert.True(fly.MaxY > 6f,
                    $"飞行档没绕到缺口（途中的最高 y={fly.MaxY:F2}）：它已经不查图了");
        Assert.True(straight.MaxY < 3f,
                    $"直线档也去绕缺口了（途中的最高 y={straight.MaxY:F2}）：它本该完全不查图");
        // 两档都在动，否则"Y 没抬起来"只是因为站着不动。
        Assert.True(fly.EndX > 6f && straight.EndX > 6f,
                    $"有一档几乎没推进（fly x={fly.EndX:F2}，straight x={straight.EndX:F2}）：用例前提不成立");
    }

    /// <summary>用给定移动方式让怪追同一个目标（起点 y=2、目标 (19,2)）跑 150 tick，返回最终 X 与途中最高 Y。</summary>
    private static (float EndX, float MaxY) ChaseWithFollow(MonsterFollowType follow, in MonsterNavGrid grid, MonsterNavScratch scratch)
    {
        var templateId = 12701;
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "绕行检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
        };
        var behavior = MonsterBehaviorConfig.Default;
        // 开"走图"能力位：不然两条通路都会退化成直线，测不出移动方式的作用。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, 1, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 60f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 200f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.FollowType = follow;
        config.Behavior = behavior;

        var self = new MonsterAiSelf
        {
            X = 1f, Y = 2f, Z = 0f, Yaw = 0f, SpawnX = 1f, SpawnY = 2f, SpawnZ = 0f,
            // **已进战**：这条测的是"移动方式传进走图资格"（飞行绕缺口 / 直线穿墙），
            // 不是"能不能发现人"。视线门（切片 75：墙后不起手）会把隔墙的目标挡在**索敌**这一步；
            // 从"没仇恨"起跑的话怪物根本不会开始追，测出来的 MaxY 与 followType 无关。
            // 进战的写法是把目标写进**威胁表**（威胁表开着时索敌读的是表，不是 ThreatTargetId）。
            // AC 的视线门同样只管"能不能打"，不管"已经在打的要不要继续"。
            State = MonsterAiState.Idle, Hp = 1000, ThreatTargetId = 88UL,
        };
        var state = new MonsterRuntimeState();
        MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, 88UL, 1L, 0L);
        var targets = new[]
        {
            new MonsterAiTarget
            {
                EntityId = 88UL, X = 19f, Y = 2f, Z = 0f, IsAlive = true, IsAttackable = true,
            },
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 60f, LeashRadius = 200f, WanderRadius = 0f, IdleToPatrolSeconds = 0f,
            AttackCooldownSeconds = 1f,
        };
        var inputs = new MonsterRuntimeInputs(config, 10, grid, scratch, false, 100L, 60, null);

        var maxY = self.Y;
        for (var tick = 0; tick < 150; tick++)
        {
            var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
            var d = result.Decision;
            self = new MonsterAiSelf
            {
                X = self.X + d.DeltaX,
                Y = self.Y + d.DeltaY,
                Z = self.Z,
                Yaw = d.NextYaw,
                SpawnX = self.SpawnX,
                SpawnY = self.SpawnY,
                SpawnZ = self.SpawnZ,
                State = d.NextState,
                Hp = self.Hp,
                ThreatTargetId = d.ThreatTargetId,
                PatrolDirection = self.PatrolDirection,
            };
            if (self.Y > maxY) maxY = self.Y;
        }
        return (self.X, maxY);
    }

    [Fact]
    public void 应援台词从组三挑且与死亡组互不串味()
    {
        // 组 3 = 应援（被同族叫起来时念）。它与组 2（临终）共用同一个挑选器
        // `MonsterContent.ResolveSayIdInGroup`——两处各写一遍的话，早晚在"只有一句/序号相等"上分家。
        const int templateId = 100203;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.Texts[templateId] = new List<MonsterTextSnapshot>
        {
            new MonsterTextSnapshot { SayId = 11, Group = 0, Seq = 0, Text = "进战" },
            new MonsterTextSnapshot { SayId = 21, Group = MonsterContent.DeathSayGroup, Seq = 1, Text = "临终" },
            new MonsterTextSnapshot { SayId = 31, Group = MonsterContent.AssistSayGroup, Seq = 1, Text = "来援一" },
            new MonsterTextSnapshot { SayId = 32, Group = MonsterContent.AssistSayGroup, Seq = 2, Text = "来援二" },
        };
        MonsterContent.Install(bundle);
        try
        {
            Assert.Equal(31, MonsterContent.ResolveAssistSayId(templateId, 0));    // Seq 最小者
            Assert.Equal(32, MonsterContent.ResolveAssistSayId(templateId, 1));
            Assert.Equal(21, MonsterContent.ResolveDeathSayId(templateId, 0));     // 两组互不串味
            Assert.Equal(31, MonsterContent.ResolveSayIdInGroup(templateId, MonsterContent.AssistSayGroup, 0));
            Assert.Equal(0, MonsterContent.ResolveSayIdInGroup(templateId, 9, 0)); // 没有那一组
            Assert.Equal(0, MonsterContent.ResolveAssistSayId(999998, 0));         // 没这只怪
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 临终台词只从死亡组里挑而没配就返回零()
    {
        // 死亡喊话是"同一种数据、不同的一拍"：它不靠行为表指针，而是靠 `MonsterContent.DeathSayGroup` 那一组。
        // 判据三半：① 只从那一组挑（进战那句在第 0 组，绝不能被挑成临终台词）；
        // ② 组内按 (Seq, SayId) 稳定序（盐 0 取 Seq 最小那条）；③ 没有那一组 → 0（调用方据此"什么都不发"）。
        const int templateId = 100202;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.Texts[templateId] = new List<MonsterTextSnapshot>
        {
            new MonsterTextSnapshot { SayId = 11, Group = 0, Seq = 0, Text = "进战那句" },
            new MonsterTextSnapshot { SayId = 21, Group = MonsterContent.DeathSayGroup, Seq = 2, Text = "不甘" },
            new MonsterTextSnapshot { SayId = 22, Group = MonsterContent.DeathSayGroup, Seq = 1, Text = "休想" },
        };
        // 只有进战台词、没有死亡组的那只
        bundle.Texts[templateId + 1] = new List<MonsterTextSnapshot>
        {
            new MonsterTextSnapshot { SayId = 11, Group = 0, Seq = 0, Text = "只有进战" },
        };
        MonsterContent.Install(bundle);
        try
        {
            var seen = new HashSet<int>();
            for (ulong salt = 0; salt < 8; salt++) seen.Add(MonsterContent.ResolveDeathSayId(templateId, salt));
            Assert.True(seen.SetEquals(new[] { 21, 22 }), "临终台词挑出了死亡组以外的号：" + string.Join(",", seen));
            Assert.Equal(22, MonsterContent.ResolveDeathSayId(templateId, 0));   // Seq=1 排在前面
            Assert.Equal(0, MonsterContent.ResolveDeathSayId(templateId + 1, 0)); // 没配死亡组
            Assert.Equal(0, MonsterContent.ResolveDeathSayId(999999, 0));         // 没这只怪
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 隔着墙的玩家不会把主动怪拉进战斗而同一份夹具去掉墙就会()
    {
        // AC 把 `IsWithinLOSInMap` 写在索敌条件里：怪不会隔墙起手。以前我们只有"距离 + 朝向锥 + 检测位"，
        // 玩家站在墙后 18 米照样被拉——而这一条今天**零行为变化**（生产世界还没有 MONSTERNAV 图），
        // 装了编辑器烘的图才看得出差别；所以夹具里"没图"那半必须也是起手（证明不是把索敌整个关掉）。
        const int w = 24, h = 8;
        byte[] Costs(bool wall)
        {
            var costs = new byte[w * h];
            for (var i = 0; i < costs.Length; i++) costs[i] = (byte)MonsterNavGrid.BaseCellCost;
            if (wall)
            {
                // 一道竖墙：x=10 的每一格都不可走（0 = 墙）。怪在 x=1，人在 x=19，直线正好穿过去。
                for (var y = 0; y < h; y++) costs[y * w + 10] = 0;
            }
            return costs;
        }

        var wallGrid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(true) };
        var openGrid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(false) };
        // 主人那一层先各断一条：墙后不通、空地通、没图返回 false（调用方必须自己先问 IsValid）
        Assert.False(MonsterPathCore.HasLineOfSight(in wallGrid, 1f, 2f, 19f, 2f));
        Assert.True(MonsterPathCore.HasLineOfSight(in openGrid, 1f, 2f, 19f, 2f));
        var emptyGrid = new MonsterNavGrid();
        Assert.False(MonsterPathCore.HasLineOfSight(in emptyGrid, 1f, 2f, 19f, 2f));

        static MonsterAiState Run(MonsterNavGrid grid, out ulong threat)
        {
            var config = new MonsterRuntimeConfig { TemplateId = 66, Name = "隔墙索敌", Rank = MonsterRank.Normal, LevelBase = 10, LevelMin = 10, LevelMax = 10 };
            var behavior = MonsterBehaviorConfig.Default;
            behavior.IsAggressive = true;
            behavior.AggroRadius = 60f;
            behavior.AggroConeDegrees = 360f;
            behavior.LeashRadius = 200f;
            behavior.WanderRadius = 0f;
            behavior.IdleToPatrolSeconds = 0f;
            config.Behavior = behavior;

            var self = new MonsterAiSelf
            {
                X = 1f, Y = 2f, Z = 0f, Yaw = 0f, SpawnX = 1f, SpawnY = 2f, SpawnZ = 0f,
                State = MonsterAiState.Idle, Hp = 1000,
            };
            var state = new MonsterRuntimeState();
            var targets = new[]
            {
                new MonsterAiTarget { EntityId = 88UL, X = 19f, Y = 2f, Z = 0f, IsAlive = true, IsAttackable = true },
            };
            var legacy = new MonsterAiConfig
            {
                MaxHp = 1000, AttackPower = 10, AttackRange = 2f, MoveSpeed = 4f,
                AggroRadius = 60f, LeashRadius = 200f, WanderRadius = 0f, IdleToPatrolSeconds = 0f,
                AttackCooldownSeconds = 1f,
            };
            var inputs = new MonsterRuntimeInputs(config, 10, grid, new MonsterNavScratch(), false, 100L, 60, null);
            var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
            threat = result.Decision.ThreatTargetId;
            return result.Decision.NextState;
        }

        // 墙后：不起手。判据写"没有进战斗"而不是"恰好 Idle"——IdleToPatrolSeconds=0 时
        // 不进战的怪下一步就是 Patrol（踱步），那正是应有的表现，钉 Idle 只会钉住一个无关的状态机细节。
        var walled = Run(wallGrid, out var walledThreat);
        Assert.True(walled != MonsterAiState.Chase && walled != MonsterAiState.Attack, "墙后还是进了战斗：" + walled);
        Assert.Equal(0UL, walledThreat);

        // 空地：同一具夹具、同一份数值，只把墙拿掉 → 必须起手（否则"挡住了"分不清是视线还是索敌坏了）
        var open = Run(openGrid, out var openThreat);
        Assert.NotEqual(MonsterAiState.Idle, open);
        Assert.Equal(88UL, openThreat);

        // 没装图（生产现状）：照旧起手——这条是"零行为变化"的正例
        var noGrid = Run(default, out var noGridThreat);
        Assert.NotEqual(MonsterAiState.Idle, noGrid);
        Assert.Equal(88UL, noGridThreat);
    }

    [Fact]
    public void 追不到路超过五秒就脱战而同一份夹具放开墙就照追()
    {
        // AC 的 `EVADE_REASON_NO_PATH`：`CanNotReachTarget` 计时器超过 `NpcEvadeIfTargetIsUnreachable`（默认 5 秒）
        // 就脱战。没有这一条时，玩家躲到怪绕不过去的墙后，那只怪会永远贴着墙站着——lure 不走、也不脱战，
        // 在玩家眼里就是"这只怪坏了"（而且是可被无限利用的免费沙包）。
        const int w = 24, h = 8;
        byte[] Costs(bool wall)
        {
            var costs = new byte[w * h];
            for (var i = 0; i < costs.Length; i++) costs[i] = (byte)MonsterNavGrid.BaseCellCost;
            if (wall)
            {
                for (var y = 0; y < h; y++) costs[y * w + 10] = 0;   // 整列是墙：绕不过去
            }
            return costs;
        }

        (MonsterAiState State, bool EvadedNoPath, ulong Threat, float EndX, int NoPathTicks, string Flags) Run(MonsterNavGrid grid, float seconds)
        {
            var config = new MonsterRuntimeConfig
            {
                TemplateId = 67, Name = "够不到检验兽", Rank = MonsterRank.Normal,
                LevelBase = 10, LevelMin = 10, LevelMax = 10,
            };
            var behavior = MonsterBehaviorConfig.Default;
            behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, 1, null, null);
            behavior.IsAggressive = true;
            behavior.AggroRadius = 60f;
            behavior.AggroConeDegrees = 360f;
            behavior.LeashRadius = 200f;
            behavior.WalkSpeed = 2f;
            behavior.MoveSpeed = 4f;
            behavior.WanderRadius = 0f;
            behavior.IdleToPatrolSeconds = 0f;
            behavior.FollowType = MonsterFollowType.GroundWalk;
            config.Behavior = behavior;

            var self = new MonsterAiSelf
            {
                X = 1f, Y = 2f, Z = 0f, Yaw = 0f, SpawnX = 1f, SpawnY = 2f, SpawnZ = 0f,
                State = MonsterAiState.Idle, Hp = 1000, ThreatTargetId = 88UL,
            };
            var state = new MonsterRuntimeState();
            MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, 88UL, 1L, 0L);
            var targets = new[]
            {
                new MonsterAiTarget { EntityId = 88UL, X = 19f, Y = 2f, Z = 0f, IsAlive = true, IsAttackable = true },
            };
            var legacy = new MonsterAiConfig
            {
                MaxHp = 1000, AttackPower = 10, AttackRange = 2f, MoveSpeed = 4f,
                AggroRadius = 60f, LeashRadius = 200f, WanderRadius = 0f, IdleToPatrolSeconds = 0f,
                AttackCooldownSeconds = 1f,
            };
            var scratch = new MonsterNavScratch();
            scratch.EnsureCapacity(w * h);      // 与"飞行档查图"那条同形：容量不给够时 A* 的读数不可信
            MonsterAiState stateNow = self.State;
            var flagsText = behavior.Flags.Describe();
            var evaded = false;
            ulong threat = self.ThreatTargetId;
            var ticks = (int)(seconds * 60);
            for (var tick = 0; tick < ticks; tick++)
            {
                // **ServerTick 必须逐拍推进**：够不到计时按服务端拍数差算，喂常量 tick 会让它永远算出 0 秒
                // （第一版夹具就是这样——怪在墙后站了 6 秒，NoPathTicks 一直是 0，判据看着像没生效）。
                var inputs = new MonsterRuntimeInputs(config, 10, grid, scratch, false, 100L + tick, 60, null);
                var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
                var d = result.Decision;
                if (d.EvadedByNoPath) evaded = true;
                stateNow = d.NextState;
                threat = d.ThreatTargetId;
                if (evaded) break;
                self = new MonsterAiSelf
                {
                    X = self.X + d.DeltaX, Y = self.Y + d.DeltaY, Z = self.Z, Yaw = d.NextYaw,
                    SpawnX = self.SpawnX, SpawnY = self.SpawnY, SpawnZ = self.SpawnZ,
                    State = d.NextState, Hp = self.Hp, ThreatTargetId = d.ThreatTargetId,
                    PatrolDirection = self.PatrolDirection,
                };
            }
            return (stateNow, evaded, threat, self.X, state.NoPathTicks, flagsText);
        }

        var wallGrid = new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(true) };
        // 先量"阈值之前不许踢"：4.5 秒还在追（AC 的窗口是 5 秒，早踢会把"正在绕路的怪"也赶回家）
        var early = Run(wallGrid, MonsterAiGate.NoPathEvadeSeconds - 0.5f);
        Assert.False(early.EvadedNoPath, $"不到 {MonsterAiGate.NoPathEvadeSeconds} 秒就脱战了：绕远路的怪会被误踢（x={early.EndX:0.##}）");

        var walled = Run(wallGrid, MonsterAiGate.NoPathEvadeSeconds + 1f);
        Assert.True(walled.EvadedNoPath, $"绕不过去的墙后追了 6 秒也没脱战（NoPathTicks={walled.NoPathTicks} x={walled.EndX:0.##} state={walled.State} 档位={walled.Flags}）：这只怪会永远贴着墙站着");
        Assert.Equal(MonsterAiState.Evade, walled.State);
        Assert.Equal(0UL, walled.Threat);

        // 同一具夹具、同一份数值，只把墙拿掉 ⇒ 照追（否则"脱战了"分不清是判据生效还是追击坏了）
        var open = Run(new MonsterNavGrid { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(false) }, MonsterAiGate.NoPathEvadeSeconds + 1f);
        Assert.False(open.EvadedNoPath, "空地也判成够不到：这条判据会把正常追击也踢成脱战");
        Assert.True(open.EndX > 6f, $"空地那只没推进（x={open.EndX:0.##}）：夹具前提没了");

        // 没装图：直线档无从判断"够不到"，不许脱战（与"没图就直线走"同口径）
        var noGrid = Run(default, MonsterAiGate.NoPathEvadeSeconds + 1f);
        Assert.False(noGrid.EvadedNoPath, "没装图也判够不到：直线档会被误踢");
    }

    [Fact]
    public void 站桩怪在没有路线时也不会左右漂移()
    {
        // 这条测的是 v4 修的那个绕开：站桩判据以前只写在位移出口 `MoveToward` 里，
        // 而"没有巡逻路线就按 WanderRadius 踱步"这一支直接写 DeltaX，把它整个绕开了。
        // 玩家侧表现很具体：炮台/绑柱子那类配成站桩的怪，在出生点左右蹭，而且改 follow_type 毫无反应。
        var drifted = MeasureMonsterDrift(true);
        Assert.True(Math.Abs(drifted) < 0.001f,
            $"配成站桩（follow_type=3）的怪在没有路线时漂移了 {drifted:F3} 米");

        // 对照：同一份配置只把站桩换成普通地面走，必须**真的走起来**。
        // 缺这条，上面那句"没漂移"可能只是因为这只怪从来没进过巡逻态（判据在空转）。
        var walked = MeasureMonsterDrift(false);
        Assert.True(Math.Abs(walked) > 0.5f,
            $"对照组（follow_type=0）只走了 {walked:F3} 米：说明这个用例没把踱步分支跑起来，上面那条断言是在空转");
    }

    /// <summary>造一只"玩家很远、没有路线"的怪跑 240 tick，返回它相对出生点在 X 上的位移。</summary>
    private static float MeasureMonsterDrift(bool staticFollow)
    {
        var templateId = 12321;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "站桩检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "zhan_zhuang",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // Skilled 档位：能力位不能"全关"，全关会退回 v1 通路，绕开本次要测的分支。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, null, null);
        behavior.IsAggressive = false;
        behavior.AggroRadius = 1f;
        behavior.LeashRadius = 30f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 20f;
        behavior.IdleToPatrolSeconds = 0.05f;
        behavior.FollowType = staticFollow ? MonsterFollowType.Static : MonsterFollowType.GroundWalk;
        config.Behavior = behavior;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 901;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f;
            point.Y = 0f;
            point.Z = 0f;
            point.AggroRadius = 1f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家放 9000 米外：只为了让会话有受众、快照发得出来，不参与索敌。
            Assert.True(world.AddPlayer(5001, 5001, 9000f, 0f, 0f, out var addReason), addReason);

            var monsterId = 0UL;
            var lastX = float.NaN;
            for (var i = 0; i < 240; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (d.Identity.HasValue &&
                        d.Identity.Value.EntityType == DsWorldSession.EntityTypeMonster)
                    {
                        monsterId = d.Identity.Value.NetworkId;   // 只有 Spawn 带 Identity，用它认这只怪
                    }
                    if (monsterId != 0UL && d.EntityId == monsterId && d.Transform.HasValue)
                    {
                        lastX = d.Transform.Value.X;
                    }
                }
            }

            Assert.True(monsterId != 0UL, "240 tick 内没有怪物的 Spawn——点位没装配起来，测不到移动");
            Assert.False(float.IsNaN(lastX), "从没读到这只怪的 Transform：移动判据无处落脚");
            return lastX;
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 同族求援把旁边那只没有主动性的怪也拉进战斗()
    {
        // WoW 观测：拉一只常带一片同族。这条测的正是 `assist_radius` + `species` + `flag_use_assist`
        // 第一次真的被读——以前档位面板会打印"求援 开"，运行时却永远是一只一只单打。
        var same = RunAssistDisplacement(1, 1, 20f);
        Assert.True(same.MovedA > 1f,
            $"对照组坏了：主动怪 A 自己都没进战斗（只动了 {same.MovedA:F2} 米），B 动不动都说明不了问题");
        // 先问"求援到底发生没发生"（内核记的账），再问"它动没动"：
        // 只看位移会把"求援压根没触发"与"触发了但那只怪被别的东西拦住不动"混成一件事。
        Assert.True(same.AssistSeeds > 0, "内核一次都没因求援写入威胁——这条通路根本没接起来");
        Assert.True(same.MovedB > 1f,
            $"求援写进了威胁表（{same.AssistSeeds} 次）但那只被动怪离出生点只动了 {same.MovedB:F2} 米");

        // 共享 leash 计时器（AC `Creature.cpp:231`）：并组发生在求援接手那一拍，之后打 A 必须同拍刷新 B。
        Assert.True(same.GroupA != 0UL && same.GroupB == same.GroupA,
            $"求援成立后两只没并进同一组（A={same.GroupA} B={same.GroupB}）——"
            + "共享计时器没有落点，'拉一只、同伴一起回家'仍然不成立");
        Assert.True(same.Linked >= 1, $"一次并组都没记（{same.Linked}）：宿主那条 LinkLeashTimerGroup 没被走到");
        Assert.True(same.BSharedTick,
            "打了 A 之后 B 的宽限窗起点没有同拍刷新——两只虽然同组，刷新只作用在自己身上。" + same.HitDiag);
        Assert.True(same.SharedRefreshes >= 1,
            $"共享刷新次数为 {same.SharedRefreshes}：要么没组、要么刷新循环把同组那只漏了");

        // 三条对照缺一条都不算测到：
        var otherSpecies = RunAssistDisplacement(1, 2, 20f);
        Assert.True(otherSpecies.AssistSeeds == 0 && Math.Abs(otherSpecies.MovedB) < 0.01f,
            $"不同种也赶来帮忙（种子 {otherSpecies.AssistSeeds} 次、动了 {otherSpecies.MovedB:F2} 米）——同族判定没在吃 species");
        // 没有求援关系就不许有组：否则"共享"会退化成"全图所有怪共用一个计时器"。
        Assert.True(otherSpecies.GroupB == 0UL && otherSpecies.Linked == 0 && !otherSpecies.BSharedTick,
            $"没有求援也并了组（GroupB={otherSpecies.GroupB}、并组 {otherSpecies.Linked} 次、B 被刷新={otherSpecies.BSharedTick}）");

        // 物种 0 = 没配。**绝不能**把 0==0 当成同族：那会让全图没配物种的怪互相拉成片。
        var unsetSpecies = RunAssistDisplacement(0, 0, 20f);
        Assert.True(unsetSpecies.AssistSeeds == 0 && Math.Abs(unsetSpecies.MovedB) < 0.01f,
            $"物种没配（都是 0）也互相求援（种子 {unsetSpecies.AssistSeeds} 次、动了 {unsetSpecies.MovedB:F2} 米）——这比「没有求援」糟得多");

        var noRadius = RunAssistDisplacement(1, 1, 0f);
        Assert.True(noRadius.AssistSeeds == 0 && Math.Abs(noRadius.MovedB) < 0.01f,
            $"求援半径配 0 却还是来帮忙（种子 {noRadius.AssistSeeds} 次、动了 {noRadius.MovedB:F2} 米）——半径判据没生效");
    }

    /// <summary>
    /// 造"A 主动 + B 被动"两只怪与一个玩家，跑 240 tick，返回两者各自离开出生点的距离、求援记账次数，
    /// 以及**共享 leash 计时器**的三个读数（组号 / 并组次数 / 打 A 之后 B 是否同一拍被刷新）。
    /// </summary>
    private static (float MovedA, float MovedB, int AssistSeeds,
                    ulong GroupA, ulong GroupB, int Linked, bool BSharedTick, int SharedRefreshes, string HitDiag)
        RunAssistDisplacement(int speciesA, int speciesB, float assistRadiusB)
    {
        const int templateA = 12401;
        const int templateB = 12402;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateA] = BuildAssistTemplate(templateA, speciesA, true, 20f);
        bundle.ByTemplateId[templateB] = BuildAssistTemplate(templateB, speciesB, false, assistRadiusB);
        MonsterContent.Install(bundle);
        try
        {
            var pointA = SpawnPointConfig.Default;
            pointA.SpawnPointId = 1;
            pointA.MonsterTemplateId = templateA;
            pointA.MaxAlive = 1;
            pointA.X = 0f; pointA.Y = 0f; pointA.Z = 0f;
            pointA.AggroRadius = 20f;
            pointA.MoveSpeed = 4f;

            var pointB = SpawnPointConfig.Default;
            pointB.SpawnPointId = 2;
            pointB.MonsterTemplateId = templateB;
            pointB.MaxAlive = 1;
            pointB.X = 8f; pointB.Y = 0f; pointB.Z = 0f;
            pointB.AggroRadius = 0f;      // B 自己不索敌：它动只能是因为听见了同族的求援
            pointB.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { pointA, pointB });
            Assert.True(world.AddPlayer(6001, 6001, 4f, 0f, 0f, out var reason), reason);

            var monsterA = 0UL;
            var monsterB = 0UL;
            var spawnXA = 0f;
            var spawnXB = 8f;
            var lastXA = 0f;
            var lastX = 8f;
            for (var i = 0; i < 240; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateB)
                    {
                        monsterB = d.Identity.Value.NetworkId;
                        if (d.Transform.HasValue) spawnXB = d.Transform.Value.X;
                    }
                    else if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateA)
                    {
                        monsterA = d.Identity.Value.NetworkId;
                        if (d.Transform.HasValue) spawnXA = d.Transform.Value.X;
                    }
                    if (monsterB != 0UL && d.EntityId == monsterB && d.Transform.HasValue) lastX = d.Transform.Value.X;
                    if (monsterA != 0UL && d.EntityId == monsterA && d.Transform.HasValue) lastXA = d.Transform.Value.X;
                }
            }

            Assert.True(monsterB != 0UL && monsterA != 0UL,
                $"两只怪没都 Spawn（A={monsterA} B={monsterB}）——点位没装配起来");
            Assert.True(world.MonsterCount == 2, $"场上应该有 2 只怪，实得 {world.MonsterCount}");

            // 共享 leash 计时器（AC `Creature.cpp:231`）：打 A ⇒ B 的宽限窗起点也在**同一拍**被刷新。
            // 这里先取组号（并组是求援接手那一拍做的），再打一下 A（玩家就贴在它身上），看 B 有没有跟着动。
            var groupA = world.TryGetMonster(monsterA, out var ma) && ma != null ? ma.Ai.LeashGroupId : 0UL;
            var groupB = world.TryGetMonster(monsterB, out var mb) && mb != null ? mb.Ai.LeashGroupId : 0UL;
            var linked = world.MonsterLeashGroupsLinked;

            var hitTick = world.ServerTick;
            var hit = world.SubmitCombatAction(6001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                // A 追到玩家 -X 侧（现场读数 1.5 vs 4.0），所以刀口要朝 -X：yaw=π。
                // 传 0（+X）会被判 OutOfArc——那正是"这一刀根本没落到"和"刷新没生效"最容易混的一种。
                AttackerId = 6001UL, TargetId = monsterA, SkillId = 0, ClientTick = 1, AttackerYaw = MathF.PI,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            var bShared = world.TryGetMonster(monsterB, out var mbAfter) && mbAfter != null
                          && mbAfter.Ai.LastLeashExtensionTick == hitTick;
            var diag = $"hit={hit.Outcome}/dmg={hit.Damage} A={(world.TryGetMonster(monsterA, out var mA2) && mA2 != null ? mA2.Ai.X : float.NaN):F1} "
                       + $"B={(world.TryGetMonster(monsterB, out var mB2) && mB2 != null ? mB2.Ai.X : float.NaN):F1} "
                       + $"playerDead={(world.TryGetMonster(monsterA, out var mA3) && mA3 != null && mA3.Ai.Hp <= 0)}";
            return (Math.Abs(lastXA - spawnXA), Math.Abs(lastX - spawnXB), world.MonsterAssistSeeds,
                    groupA, groupB, linked, bShared, world.MonsterLeashSharedRefreshes, diag);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>求援用例的模板：求援位一律开着（对照靠的是 species/半径，不是开关本身）。</summary>
    private static MonsterRuntimeConfig BuildAssistTemplate(int templateId, int species, bool aggressive, float assistRadius)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "求援检验兽" + templateId,
            Rank = MonsterRank.Normal,
            Species = species,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "qiu_yuan",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, 1, null);
        behavior.IsAggressive = aggressive;
        behavior.AggroRadius = aggressive ? 20f : 0f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 60f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;     // 不许踱步：位移只能来自战斗
        behavior.IdleToPatrolSeconds = 0.05f;
        behavior.AssistRadius = assistRadius;
        config.Behavior = behavior;
        return config;
    }

    /// <summary>
    /// 密簇求援场景：同族多到清单装不下，而**唯一在打架的那只恰好实体 id 最大**。
    /// <para>实体 id = 刷怪点&lt;&lt;32 | 槽位+1，所以"按 id 取前 64 条"等于"保留刷怪点号小的那批"，
    /// 而一个点内的槽位又沿 X 轴排开——id 序与距离序是两套顺序。把它们摆到会互相打脸的位置，
    /// 才问得出"清单到底是按什么切的"：按 id 切会把最近的开战同族挤出去（被动怪永远不来帮忙），
    /// 按距离切才保得住它。</para>
    /// </summary>
    private static (int Seeds, int Truncated, float MovedSelf, float MovedFighter, int Monsters) RunDenseAssistScene()
    {
        const int templateFiller = 12411;   // 占名额的同族：被动、不索敌、自己也不求援
        const int templateFighter = 12412;  // 正在打玩家的那只：主动
        const int templateSelf = 12413;     // 被测的被动怪：全场唯一开了求援半径的
        const int fillerCount = DsWorldSession.MaxAssistAllies + 1;
        const int species = 7;
        const float assistRadius = 60f;

        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateFiller] = BuildAssistTemplate(templateFiller, species, false, 0f);
        bundle.ByTemplateId[templateFighter] = BuildAssistTemplate(templateFighter, species, true, 0f);
        bundle.ByTemplateId[templateSelf] = BuildAssistTemplate(templateSelf, species, false, assistRadius);
        MonsterContent.Install(bundle);
        try
        {
            // 铺位全在 Y=Z=0 这条线上（求援判据只看 X/Y 平面）：
            //   被动怪在原点 → 开战的同族出生离它 5 米（**最近**）→ 65 只占位同族铺在 18.8..57.2 米
            //   （整群都在半径 60 内，否则筛完就不满 64 条，截断压根不会发生）。
            //   刷怪点号 1=占位群 / 2=开战的 / 3=被动怪，所以"开战的"在 id 序里排在占位群之后。
            //   玩家放在 14 米：开战那只必须**走过来**才够得着，对照组"它进战斗了"才有大位移可看；
            //   同时它停在 11 米上下，仍然比最近那只占位怪（18.8 米）更挨着被动怪。
            var fillerPoint = SpawnPointConfig.Default;
            fillerPoint.SpawnPointId = 1;
            fillerPoint.MonsterTemplateId = templateFiller;
            fillerPoint.MaxAlive = fillerCount;
            fillerPoint.SlotSpacing = 0.6f;
            fillerPoint.X = 38f; fillerPoint.Y = 0f; fillerPoint.Z = 0f;
            fillerPoint.AggroRadius = 0f;
            fillerPoint.MoveSpeed = 4f;

            var fighterPoint = SpawnPointConfig.Default;
            fighterPoint.SpawnPointId = 2;
            fighterPoint.MonsterTemplateId = templateFighter;
            fighterPoint.MaxAlive = 1;
            fighterPoint.X = 5f; fighterPoint.Y = 0f; fighterPoint.Z = 0f;
            fighterPoint.AggroRadius = 20f;
            fighterPoint.MoveSpeed = 4f;

            var selfPoint = SpawnPointConfig.Default;
            selfPoint.SpawnPointId = 3;
            selfPoint.MonsterTemplateId = templateSelf;
            selfPoint.MaxAlive = 1;
            selfPoint.X = 0f; selfPoint.Y = 0f; selfPoint.Z = 0f;
            selfPoint.AggroRadius = 0f;     // 它动只能是因为听见了同族的求援
            selfPoint.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { fillerPoint, fighterPoint, selfPoint });
            Assert.True(world.AddPlayer(7001, 7001, 14f, 0f, 0f, out var reason), reason);

            var monsterSelf = 0UL;
            var monsterFighter = 0UL;
            var spawnXS = 0f;
            var spawnXF = 5f;
            var lastXS = 0f;
            var lastXF = 5f;
            for (var i = 0; i < 240; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateSelf)
                    {
                        monsterSelf = d.Identity.Value.NetworkId;
                        if (d.Transform.HasValue) spawnXS = d.Transform.Value.X;
                    }
                    else if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateFighter)
                    {
                        monsterFighter = d.Identity.Value.NetworkId;
                        if (d.Transform.HasValue) spawnXF = d.Transform.Value.X;
                    }
                    if (monsterSelf != 0UL && d.EntityId == monsterSelf && d.Transform.HasValue) lastXS = d.Transform.Value.X;
                    if (monsterFighter != 0UL && d.EntityId == monsterFighter && d.Transform.HasValue) lastXF = d.Transform.Value.X;
                }
            }

            Assert.True(monsterSelf != 0UL && monsterFighter != 0UL,
                $"被动怪或开战的怪没 Spawn（self={monsterSelf} fighter={monsterFighter}）——点位没装配起来");
            return (world.MonsterAssistSeeds, world.MonsterAssistAlliesTruncated,
                    lastXS - spawnXS, lastXF - spawnXF, world.MonsterCount);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 求援清单挤满时保住最近那只同族而不是id最小的那批()
    {
        var scene = RunDenseAssistScene();
        _log.WriteLine($"密簇求援实测：场上 {scene.Monsters} 只、截断 {scene.Truncated} 次、种子 {scene.Seeds} 次、" +
                       $"被动怪位移 {scene.MovedSelf:F2} 米、开战怪位移 {scene.MovedFighter:F2} 米");

        // 先证明"挤满"这件事真的发生了：清单一次都没截断时，下面两条断言都在空转
        // （用例会因为"根本没截断"而通过，而不是因为"截断保住了最近的"）。
        Assert.True(scene.Truncated > 0,
            $"求援清单一次都没被截断（场上 {scene.Monsters} 只，上限 {DsWorldSession.MaxAssistAllies} 条）" +
            "——夹具没造出密簇，这条用例等于没跑");
        // 对照：那只主动怪必须真的接了敌，否则"没人来帮忙"是它自己没打起来，不是清单切错了。
        Assert.True(scene.MovedFighter > 1f,
            $"对照组坏了：主动怪自己都没动起来（{scene.MovedFighter:F2} 米），被动怪动不动说明不了问题");

        Assert.True(scene.Seeds > 0,
            $"清单截断了 {scene.Truncated} 次，而唯一在打架的那只同族一次都没被选中——" +
            "截断按实体 id 切（id 高 32 位是刷怪点号），把 id 最大的那条先挤掉了；" +
            "应按半径筛完再按距离截，让截断不改变「谁赢」");
        // 位移用有符号值：它要朝玩家（+X）跑，原地打转或往回走都不算被叫起来。
        Assert.True(scene.MovedSelf > 1f,
            $"求援写进威胁表 {scene.Seeds} 次，被动怪却只朝 +X 走了 {scene.MovedSelf:F2} 米");
    }

    /// <summary>
    /// 内核级：一只挂着回位免疫窗的被动怪，让同族一直喊着"我在打玩家"，从窗内跑到窗外。
    /// <para>返回三个分箱的种子次数（免疫窗内 / 出窗但仍在 AC 那两秒延迟里 / 延迟之后）与最后一拍的威胁目标。</para>
    /// </summary>
    private static (int SeedsInside, int SeedsInDelay, int SeedsAfter, ulong FinalThreat) RunAssistAcrossImmunityWindow()
    {
        const int species = 5;
        const long immunityUntilTick = 120;
        var config = BuildAssistTemplate(12421, species, false, 20f);
        var state = new MonsterRuntimeState { EvadeImmuneUntilTick = immunityUntilTick };
        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = 10f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
        };
        // 盟友：5 米外（在 20 米半径内）、正在打 91 号玩家。
        var allies = new[] { new MonsterAiAlly(5001UL, 5f, 0f, species, true, 91UL) };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 1, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 0f, LeashRadius = 60f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };

        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000,
        };
        var seedsInside = 0;
        var seedsInDelay = 0;
        var seedsAfter = 0;
        // AC 那两秒的应援延迟换算成 tick（60tick/秒 ⇒ 120 拍）。**窗口长度必须引用机制常量本身**：
        // 写死一个"40 拍"的窗口去测一条 120 拍之后才生效的机制，红了会被读成"参数没生效"。
        var delayTicks = (int)(MonsterAiRuntime.AssistDelaySeconds * 60);
        for (var tick = 100; tick < immunityUntilTick + delayTicks + 10; tick++)
        {
            var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, tick, 60, null, allies);
            var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
            if (result.SeededByAssist)
            {
                if (tick < immunityUntilTick) seedsInside++;
                else if (tick < immunityUntilTick + delayTicks) seedsInDelay++;
                else seedsAfter++;
            }
            self = new MonsterAiSelf
            {
                X = self.X + result.Decision.DeltaX,
                Y = self.Y + result.Decision.DeltaY,
                Z = self.Z,
                Yaw = result.Decision.NextYaw,
                SpawnX = self.SpawnX,
                SpawnY = self.SpawnY,
                SpawnZ = self.SpawnZ,
                State = result.Decision.NextState,
                Hp = self.Hp,
                ThreatTargetId = result.Decision.ThreatTargetId,
                PatrolDirection = self.PatrolDirection,
            };
        }
        return (seedsInside, seedsInDelay, seedsAfter, self.ThreatTargetId);
    }

    [Fact]
    public void 回位免疫窗里同族求援叫不动它而窗过之后还要等AC那两秒才咬住()
    {
        var scene = RunAssistAcrossImmunityWindow();
        _log.WriteLine($"免疫窗求援实测：窗内种子 {scene.SeedsInside} 次、" +
                       $"AC 延迟窗内 {scene.SeedsInDelay} 次、延迟之后 {scene.SeedsAfter} 次、末拍威胁 {scene.FinalThreat}");

        // 窗外的正例必须先成立：如果求援整条通路是坏的，"窗内 0 次"就是空转出来的绿灯。
        Assert.True(scene.SeedsAfter >= 1 && scene.FinalThreat == 91UL,
            $"免疫窗过了却没有来帮忙（窗外种子 {scene.SeedsAfter} 次、威胁目标 {scene.FinalThreat}）" +
            "——求援本身没接起来，下面那两条「叫不动」就无从谈起");

        // 口径（WoW 观感）：刚 evade 完回位的怪在免疫窗里**不**被同族叫走，否则玩家看到的是
        // "它跑回去一半又掉头追我"，与"脱战就回位"相反。
        Assert.True(scene.SeedsInside == 0,
            $"免疫窗里被同族叫起来 {scene.SeedsInside} 次——窗只挡住了索敌，没挡住求援这条例外通路");

        // 机制层单独一条：AC 的 2000 毫秒应援延迟（`CreatureFamilyAssistanceDelay`）把"出窗"与"接手"
        // 分开。少了这一条，上面那条"窗外 ≥1 次"会同样绿——但它绿的是"窗一过立刻接手"这个**没做**的口径。
        // 先自证这一族量尺不是空箱：延迟为 0 时中间那一箱**按构造**必然为空，
        // 那时"SeedsInDelay == 0"就不是判据而是同义反复（这种空箱以前骗过一次绿灯）。
        Assert.True(MonsterAiRuntime.AssistDelaySeconds > 0f,
                    $"AC 的应援延迟现在是 {MonsterAiRuntime.AssistDelaySeconds} 秒："
                    + "下面那条分箱判据退化成空箱，别再把它读成「延迟被验过」");
        Assert.True(scene.SeedsInDelay == 0,
            $"出窗后 {MonsterAiRuntime.AssistDelaySeconds} 秒内就接手 {scene.SeedsInDelay} 次"
            + "——AC 那两秒被吞掉了，玩家看到的是一拉整队同时启动");
    }

    /// <summary>
    /// 一只闲着的怪从 <c>startTick</c> 起听见同族求援，跑满"延迟 + 余量"，返回**第一次真的接手**那一拍与末拍威胁。
    /// <para><c>allyXAfterArm</c> 用来问 AC 那两秒到期时**要不要复查距离**：
    /// AC 的 `AssistDelayEvent` 里钉的是当时那份 GUID 清单，执行时只重跑 `CanAssistTo` 那一串门
    /// （活着 / 还闲着 / 不在 evade / 认得这个敌人），**不再比一次半径**——
    /// 因为首领在这两秒里把人拖开是常态，那一队照样要追。把盟友挪到半径外就是把这条口径钉住。</para>
    /// </summary>
    private static (int FirstSeedTick, ulong FinalThreat) RunAssistDelayScene(float allyXAfterArm, int startTick = 100)
    {
        const int species = 5;
        const ulong enemy = 91UL;
        const float assistRadius = 20f;
        var config = BuildAssistTemplate(12431, species, false, assistRadius);
        var state = new MonsterRuntimeState();
        var targets = new[]
        {
            new MonsterAiTarget { EntityId = enemy, X = 10f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 1, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 0f, LeashRadius = 200f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };
        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000,
        };

        var delayTicks = (int)(MonsterAiRuntime.AssistDelaySeconds * 60);
        var firstSeed = -1;
        for (var tick = startTick; tick < startTick + delayTicks + 30; tick++)
        {
            // 第一拍盟友贴在 5 米（在半径内 ⇒ 该记账），之后按调用方给的距离走。
            var allyX = tick == startTick ? 5f : allyXAfterArm;
            var allies = new[] { new MonsterAiAlly(5001UL, allyX, 0f, species, true, enemy) };
            var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, tick, 60, null, allies);
            var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
            if (result.SeededByAssist && firstSeed < 0) firstSeed = tick;
            self = new MonsterAiSelf
            {
                X = self.X + result.Decision.DeltaX,
                Y = self.Y + result.Decision.DeltaY,
                Z = self.Z,
                Yaw = result.Decision.NextYaw,
                SpawnX = self.SpawnX,
                SpawnY = self.SpawnY,
                SpawnZ = self.SpawnZ,
                State = result.Decision.NextState,
                Hp = self.Hp,
                ThreatTargetId = result.Decision.ThreatTargetId,
                PatrolDirection = self.PatrolDirection,
            };
        }
        return (firstSeed, self.ThreatTargetId);
    }

    [Fact]
    public void 求援按AC那样先听见后接手而那两秒到期时不再复查盟友距离()
    {
        var delayTicks = (int)(MonsterAiRuntime.AssistDelaySeconds * 60);

        // 正例：盟友一直贴在半径内 ⇒ 接手那一拍必须**正好**是"听见 + 两秒"，不是"大概两秒后"。
        var near = RunAssistDelayScene(5f);
        Assert.Equal(100 + delayTicks, near.FirstSeedTick);
        Assert.Equal(91UL, near.FinalThreat);

        // AC 那一半：出半径照样接手（事件只重跑 `CanAssistTo`，不再比半径）。
        // 盟友挪到 40 米 > 半径 20 米 ⇒ 若这里也红了，说明实现把"到期"写成了"到期且此刻还在半径内"，
        // 表现就是"首领拖出两步，队友索性不来了"。
        var far = RunAssistDelayScene(40f);
        Assert.Equal(100 + delayTicks, far.FirstSeedTick);
        Assert.Equal(91UL, far.FinalThreat);

        // 反证自己的尺子：把延迟读成 0 拍的话，上面两条会同时红在同一格上（第一次就接手）。
        Assert.True(delayTicks > 0, $"延迟换算成 {delayTicks} 拍 ⇒ 这条用例的秒表坏了");
    }

    /// <summary>
    /// 跑一拍"Idle 中的怪看见一个站在 <c>targetX</c> 米处的目标"，v2 那一条通路；
    /// 两个半径（内容行为表那一格 / 宿主那份 legacy）各自由调用方填——不一致就出在这里。
    /// <para><c>targetX</c> 是判据的一部分而不是摆设：半径填 0 时"5 米外看不见"是**减法自己就挡住了**，
    /// 那条用例量不到"≤0 是否非法"这一格的主人；只有**贴身**（0 米）那个几何会真的走到那条例外上
    /// （0 米 ≤ 0² 成立）。所以两种几何各跑一遍，缺一种就是留一条空转的绿灯。</para>
    /// </summary>
    private static (MonsterAiState NextState, ulong Threat) RunSightTickV2(
        float behaviorRadius, float legacyRadius, float targetX = 5f,
        int creatureLevel = 0, int targetLevel = 0)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = 12455,
            Name = "半径检验兽",
            Rank = MonsterRank.Normal,
            Species = 3,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "ban_jing_ce",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Master, null, null, null, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = behaviorRadius;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 200f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.MeleeSwingSeconds = 1f;
        config.Behavior = behavior;

        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000, ThreatTargetId = 0UL,
        };
        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = targetX, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true,
                                  Level = targetLevel },
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2.5f, MoveSpeed = 4f,
            AggroRadius = legacyRadius, LeashRadius = 200f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
            // 索敌那条判据在 v2 里读的也是这一格（装配层的投影出口），所以夹具两边同值，
            // 免得"半径算对了"其实是被另一格的 0 蒙过去的。
            Level = creatureLevel,
        };
        var inputs = new MonsterRuntimeInputs(config, creatureLevel > 0 ? creatureLevel : 10, default, null, false, 100L, 60, null);
        var result = MonsterAiRuntime.Advance(self, new MonsterRuntimeState(), targets, 1f / 60f, legacy, inputs);
        return (result.Decision.NextState, result.Decision.ThreatTargetId);
    }

    /// <summary>同样几何，走 v1 那条通路（半径只有一份，填 0 与填正数各跑一次）。</summary>
    private static (MonsterAiState NextState, ulong Threat) RunSightTickV1(
        float radius, float targetX = 5f, int creatureLevel = 0, int targetLevel = 0)
    {
        var cfg = MonsterAiConfig.Default;
        cfg.AggroRadius = radius;
        cfg.Level = creatureLevel;
        // 这两格要与 v2 那份夹具逐字同值，否则"状态相同"比的是空转→巡逻的时机而不是半径。
        cfg.IdleToPatrolSeconds = 0f;
        cfg.WanderRadius = 0f;
        cfg.LeashRadius = 200f;
        cfg.MoveSpeed = 4f;
        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000, ThreatTargetId = 0UL,
        };
        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = targetX, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true,
                                  Level = targetLevel },
        };
        var d = MonsterAiCore.Decide(self, targets, 1f / 60f, cfg, 100L);
        return (d.NextState, d.ThreatTargetId);
    }

    [Fact]
    public void 索敌半径的两条通路只剩一种口径而零与负数是非法值()
    {
        // 口径（2026-10-27 拍板）：这一格的 0 与负数都是**非法值**——既不是"把这一格关掉"
        // （那是 is_aggressive 的活），也不是"没配、交给兜底距离"。
        // 缺陷的形状是"两条通路各猜一种"：v1 见 0 就不索敌，v2 见 0 退回宿主那份（缺省 10 米），
        // 于是同一份内容包一个聋一个看得见，而两边的用例各自都能绿。
        // 作者侧（校验器）与装载侧（快照回执）各自把非法值说破，判据在下面两条。
        var v2Zero = RunSightTickV2(behaviorRadius: 0f, legacyRadius: 10f);
        Assert.Equal(0UL, v2Zero.Threat);
        Assert.NotEqual(MonsterAiState.Chase, v2Zero.NextState);

        var v1Zero = RunSightTickV1(0f);
        Assert.Equal(v2Zero.Threat, v1Zero.Threat);          // 两路同数：这一条才是"口径统一"本身
        Assert.Equal(v2Zero.NextState, v1Zero.NextState);

        // 正向对照（两侧都要）：填正数时同一几何必须照常索敌，
        // 否则上面那两条"没索敌"是空转出来的绿灯（拒绝断言的对照要同夹具）。
        var v2Ten = RunSightTickV2(behaviorRadius: 10f, legacyRadius: 10f);
        Assert.Equal(91UL, v2Ten.Threat);
        Assert.Equal(MonsterAiState.Chase, v2Ten.NextState);

        var v1Ten = RunSightTickV1(10f);
        Assert.Equal(v2Ten.Threat, v1Ten.Threat);
        Assert.Equal(v2Ten.NextState, v1Ten.NextState);

        // 贴身那一档才是"这一格的主人"的判据。上面那两条在反证里被抓出来是**空转的**：
        // 半径 0 时"5 米外看不见"是距离比较自己就挡住了（0 米 ≤ 0² 才是唯一走得通的那条），
        // 所以把主人改成"永远合法"，5 米那两条照样全绿。
        // 目标站在怪自己身上（近战里最常见的几何）才真的走到那条 ≤0 的例外上。
        var v2Hug = RunSightTickV2(behaviorRadius: 0f, legacyRadius: 10f, targetX: 0f);
        Assert.Equal(0UL, v2Hug.Threat);

        var v1Hug = RunSightTickV1(0f, targetX: 0f);
        Assert.Equal(v2Hug.Threat, v1Hug.Threat);

        // 同一几何的正向对照：半径合法时贴身必须索敌（否则上面那两条又是空转）。
        var v2HugTen = RunSightTickV2(behaviorRadius: 10f, legacyRadius: 10f, targetX: 0f);
        Assert.Equal(91UL, v2HugTen.Threat);
        var v1HugTen = RunSightTickV1(10f, targetX: 0f);
        Assert.Equal(v2HugTen.Threat, v1HugTen.Threat);
    }

    /// <summary>
    /// AC 的"等级差改视野"（<c>Creature::GetAggroRange</c>，<c>Creature.cpp:3224-3266</c>）落到本作的两条 AI 通路。
    /// <para>这一格以前完全不存在：同一只怪对 20 级小号和对 60 级大号是同一个可见半径，
    /// 而 WoW 的形状是"你比它高它就更早看见你"（每级约 1 码，负方向截到 25 级，上下限 45/5 码）。</para>
    /// <para>判据只许有一个主人（<c>MonsterAiGate.EffectiveAggroRadius</c>），且必须<b>逐候选</b>问：
    /// 把等级差算成"这只怪的一个半径"就会让同一轮里两个不同等级的人共用一个半径——
    /// 那正是 AC 的函数签名（带 target 参数）在反对的形状。</para>
    /// </summary>
    [Fact]
    public void 索敌半径按目标的等级算而两条通路对同一个候选得到同一个半径()
    {
        const float MetersPerYard = MonsterAiGate.AggroMetersPerLevel;
        Assert.Equal(0.9144f, MetersPerYard, 4);                       // 单位换算自己钉一次，别让它漂成"1 码=1 米"
        Assert.Equal(45f * 0.9144f, MonsterAiGate.MaxAggroRadiusMeters, 3);
        Assert.Equal(5f * 0.9144f, MonsterAiGate.MinAggroRadiusMeters, 3);

        // ① 内核那张表：每一档都按 AC 的式子手算一遍（不是拿实现再算一次当期望）。
        Assert.Equal(10f, MonsterAiGate.EffectiveAggroRadius(10f, 12, 12), 4);            // 同级 ⇒ 不调
        Assert.Equal(10f + 10 * MetersPerYard,
                     MonsterAiGate.EffectiveAggroRadius(10f, 12, 22), 4);                  // 大号：+10 码
        Assert.Equal(10f + 25 * MetersPerYard,
                     MonsterAiGate.EffectiveAggroRadius(10f, 12, 37), 4);                  // 25 级差 ⇒ 截在这
        Assert.Equal(10f + 25 * MetersPerYard,
                     MonsterAiGate.EffectiveAggroRadius(10f, 12, 40), 4);                  // 28 级差 ⇒ 仍截在 25（这条才量到那道截断）
        Assert.Equal(MonsterAiGate.MinAggroRadiusMeters,
                     MonsterAiGate.EffectiveAggroRadius(10f, 22, 12), 4);                  // 怪比人高 ⇒ 收窄到 5 码下限
        Assert.Equal(MonsterAiGate.MaxAggroRadiusMeters,
                     MonsterAiGate.EffectiveAggroRadius(40f, 1, 30), 4);                   // 40+22.86 会越过 45 码 ⇒ 截上限

        // ② 等级未知（任一侧 0）整条不调：这是"接进来对旧内容零变化"的那半判据，
        //    没有它，金轨迹与全部旧夹具会因为对面写了个 0 而把视野缩到 5 码。
        Assert.Equal(10f, MonsterAiGate.EffectiveAggroRadius(10f, 0, 30), 4);
        Assert.Equal(10f, MonsterAiGate.EffectiveAggroRadius(10f, 30, 0), 4);

        // ③ 非法半径不许被等级差救活（切片 23 定的口径在这一格上仍然成立）。
        Assert.Equal(0f, MonsterAiGate.EffectiveAggroRadius(0f, 12, 60), 4);
        Assert.Equal(-1f, MonsterAiGate.EffectiveAggroRadius(-1f, 12, 60), 4);

        // ④ 效果层：同一几何（目标站在 12 米外）只在"目标比怪高 3 级"时才看得见，
        //    且 v1 与 v2 四个角都同数——半径这一格从此没有第二种口径。
        var nearBlindV2 = RunSightTickV2(10f, 10f, targetX: 12f, creatureLevel: 12, targetLevel: 12);
        Assert.Equal(0UL, nearBlindV2.Threat);                          // 同级 ⇒ 10 米视野，12 米外看不见
        Assert.Equal(0UL, RunSightTickV1(10f, 12f, 12, 12).Threat);

        var nearSeenV2 = RunSightTickV2(10f, 10f, targetX: 12f, creatureLevel: 12, targetLevel: 15);
        Assert.Equal(91UL, nearSeenV2.Threat);                          // +3 级 ⇒ 10+2.7432=12.7432 米，看得见
        var nearSeenV1 = RunSightTickV1(10f, 12f, 12, 15);
        Assert.Equal(nearSeenV2.Threat, nearSeenV1.Threat);
        Assert.Equal(nearSeenV2.NextState, nearSeenV1.NextState);

        // ⑤ 反手那一半：怪比人高 ⇒ 视野收窄，12 米外的大号照样看不见（这条挡住"只加不减"的实现）。
        var shrunkV2 = RunSightTickV2(10f, 10f, targetX: 8f, creatureLevel: 20, targetLevel: 12);
        Assert.Equal(0UL, shrunkV2.Threat);                             // 10−7.3152=2.6848 米 < 8 米
        Assert.Equal(shrunkV2.Threat, RunSightTickV1(10f, 8f, 20, 12).Threat);
        // 同几何的正向对照：等级差关掉（0）时 8 米必须看得见，否则上面那条红只能归因给"根本没索敌"。
        Assert.Equal(91UL, RunSightTickV2(10f, 10f, targetX: 8f, creatureLevel: 0, targetLevel: 0).Threat);
        Assert.Equal(91UL, RunSightTickV1(10f, 8f).Threat);
    }

    /// <summary>
    /// 同一件事在**真 DS 世界**里再验一遍：装配层那两处递等级（投影 <c>cfg.Level</c> 与候选 <c>MonsterAiTarget.Level</c>）
    /// 只要漏一处，内核那张表再对也不会发生在游戏里——而源码针只能证明"写了那句"，证明不了"值到了"。
    /// <para>档位刻意选 <c>Dumb</c>（五位全关 ⇒ 走 v1，也就是生产全部种子怪那条通路）：
    /// 切片 21/22/23/25 每一轮都栽在"判据只长在 v2 上"。</para>
    /// </summary>
    [Fact]
    public void 世界里的怪对高等级玩家更早起手而等级相同看不见他()
    {
        const int TemplateId = 12470;
        const uint PlayerEntityId = 7171;
        var mobId = SpawnDirectorCore.ComputeEntityId(1, 0);

        static DsWorldSession BuildWorld(int playerLevel, int templateId, uint playerEntityId)
        {
            var bundle = new MonsterContentBundle { ContentRevision = 1 };
            var config = new MonsterRuntimeConfig
            {
                TemplateId = templateId,
                Name = "等级差检验兽",
                Rank = MonsterRank.Normal,
                Species = 1,
                LevelBase = 12, LevelMin = 12, LevelMax = 12,        // 怪固定 12 级，等级差只看人那一侧
                ModelKey = "deng_jing_ce",
                Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 0, AtkPerLevel = 0 },
                Xp = MonsterXpConfig.Default,
            };
            var behavior = MonsterBehaviorConfig.Default;
            behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Dumb, null, null, null, null, null);
            behavior.IsAggressive = true;
            behavior.AggroRadius = 10f;
            behavior.AggroConeDegrees = 360f;
            behavior.LeashRadius = 200f;
            behavior.WanderRadius = 0f;
            behavior.IdleToPatrolSeconds = 0f;
            behavior.WalkSpeed = 2f;
            behavior.MoveSpeed = 4f;
            config.Behavior = behavior;
            bundle.ByTemplateId[templateId] = config;
            MonsterContent.Install(bundle);

            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;              // 点位不覆盖：半径由内容那一格说了算
            point.MoveSpeed = 0f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 0,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 站在怪的 −X 侧 12 米：默认朝向 +X ⇒ 面向怪（这条夹具坑见记忆），
            // 而 12 米**恰好落在**"同级看不见 / 高 4 级看得见"的缝上（10 米 vs 10+4×0.9144=13.66 米）。
            Assert.True(world.AddPlayer(playerEntityId, playerEntityId, -12f, 0f, 0f, out var reason, "检验员", playerLevel), reason);
            return world;
        }

        // ① 同级（12 vs 12）：视野 10 米，12 米外的人看不见 ⇒ 不起手、也不朝他走。
        //    （状态是 Patrol 而不是 Idle：行为表里 idle_to_patrol=0 就是"立刻进巡逻"，
        //      而 wander_radius=0 让它留在原地——所以判据取"威胁为 0 + 没离开出生点"，不取状态名。）
        var same = BuildWorld(12, TemplateId, PlayerEntityId);
        for (var i = 0; i < 60; i++) same.Tick();
        Assert.True(same.TryGetMonster(mobId, out var sameMob));
        Assert.Equal(0UL, sameMob.Ai.ThreatTargetId);
        Assert.True(MathF.Abs(sameMob.Ai.X - sameMob.Ai.SpawnX) < 0.05f,
                    $"同级那一只离开了出生点 {(sameMob.Ai.X - sameMob.Ai.SpawnX):F} 米：看不见人却会走＝这条判据在测别的东西");

        // ② 只改玩家等级（16 级 ⇒ 等级差 −4 ⇒ 视野 10+3.66=13.66 米）：同一几何必须起手并追上来。
        var high = BuildWorld(16, TemplateId, PlayerEntityId);
        for (var i = 0; i < 60; i++) high.Tick();
        Assert.True(high.TryGetMonster(mobId, out var highMob));
        Assert.Equal((ulong)PlayerEntityId, highMob.Ai.ThreatTargetId);
        // 玩家在 −X 侧 ⇒ 追击是"X 变小"。只断"位置变了"不够：巡逻漂移也能满足，必须断朝哪一侧走。
        Assert.True(highMob.Ai.X < sameMob.Ai.X - 0.5f,
                    $"高等级那一只没朝玩家走（它 x={highMob.Ai.X:F}，同级那一只 x={sameMob.Ai.X:F}）：" +
                    "威胁有了却没位移＝追击那一段没接上，这条判据就不算效果层");
    }

    /// <summary>战术用例的一拍输入（残血逃跑 + 远程保持距离共用一份夹具，只差这几格）。</summary>
    private readonly record struct TacticsCase(
        int FleeHpPercent, bool Tactics, float RangedMin, float RangedMax, int Hp, float TargetX);

    /// <summary>
    /// 跑一拍战斗中的怪：目标永远摆在 <c>TargetX</c>（Y 同一条线），血量按 <c>Hp/1000</c> 折算百分比。
    /// <para>档位取 Master（五位全开），战术位由 <c>Tactics</c> 单项覆盖来关——
    /// 覆盖而不是换档位：换档位会连带关掉威胁表/技能位，红了分不清归因。</para>
    /// </summary>
    private static (MonsterAiState NextState, ulong Threat, bool WantsAttack, float DeltaX) RunTacticsTick(in TacticsCase c)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = 12431,
            Name = "战术检验兽",
            Rank = MonsterRank.Normal,
            Species = 2,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "zhan_zhuang_ce",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Master, null, null, null, null, c.Tactics ? 1 : 0);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 30f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 200f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MeleeSwingSeconds = 1f;
        behavior.FleeHpPercent = c.FleeHpPercent;
        behavior.RangedMinDistance = c.RangedMin;
        behavior.RangedMaxDistance = c.RangedMax;
        config.Behavior = behavior;

        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Chase, Hp = c.Hp, ThreatTargetId = 0UL,
        };
        var state = new MonsterRuntimeState();
        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = c.TargetX, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2.5f, MoveSpeed = 4f,
            AggroRadius = 30f, LeashRadius = 200f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };
        var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, 100L, 60, null);
        var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
        return (result.Decision.NextState, result.Decision.ThreatTargetId,
                result.Decision.WantsAttack, result.Decision.DeltaX);
    }

    [Fact]
    public void 残血逃跑只在战术位开着且血量真的过线时才发生()
    {
        // WoW 观测：会逃的怪一到那条血线就转身往回跑，而不是"边掉血边站桩打你"。
        // 判据是 `hpPercent <= FleeHpPercent`（闭区间，等于线就算）——所以三档必须各自钉住。
        var atLine = RunTacticsTick(new TacticsCase(FleeHpPercent: 20, Tactics: true, 0f, 0f, Hp: 200, TargetX: 1f));
        Assert.Equal(MonsterAiState.Evade, atLine.NextState);
        Assert.Equal(0UL, atLine.Threat);
        Assert.False(atLine.WantsAttack, "已经决定逃的怪这一拍还在挥刀——逃跑判据排在了攻击判定之后");

        // 正向对照一：只差 1% 血必须照常打（否则这条判据其实是"只要开战术就逃"）
        var aboveLine = RunTacticsTick(new TacticsCase(20, true, 0f, 0f, 210, 1f));
        Assert.NotEqual(MonsterAiState.Evade, aboveLine.NextState);
        Assert.True(aboveLine.WantsAttack && aboveLine.Threat == 91UL,
            $"没过血线却没打起来（状态 {aboveLine.NextState}、威胁 {aboveLine.Threat}）——对照组坏了，上面那条红说明不了问题");

        // 正向对照二：同一份配法关掉战术位，必须**不逃**（这条位是开关，不是装饰）
        var tacticsOff = RunTacticsTick(new TacticsCase(20, false, 0f, 0f, 200, 1f));
        Assert.NotEqual(MonsterAiState.Evade, tacticsOff.NextState);

        // 正向对照三：FleeHpPercent=0 = 没配 = 永不逃（0 与"没填"必须同义）
        var noFlee = RunTacticsTick(new TacticsCase(0, true, 0f, 0f, 1, 1f));
        Assert.NotEqual(MonsterAiState.Evade, noFlee.NextState);
    }

    [Fact]
    public void 远程怪贴脸时往后退而近战怪只会往前凑()
    {
        // WoW 观测：猎人型怪被你贴脸会拉开距离（风筝），而近战怪只会继续扑上来。
        // 判据是 `UseTactics && RangedMaxDistance>0 && 距离 < RangedMinDistance` → 往反方向走。
        var kiting = RunTacticsTick(new TacticsCase(0, true, RangedMin: 8f, RangedMax: 20f, Hp: 1000, TargetX: 3f));
        Assert.True(kiting.DeltaX < 0f,
            $"贴脸的远程怪没有往后退（本 tick 位移 {kiting.DeltaX:F4}，正号是朝玩家走）——保持距离那段没起作用");
        Assert.NotEqual(MonsterAiState.Attack, kiting.NextState);

        // 对照一：同一份几何，把 RangedMax 留 0（= 这只怪不是远程怪）必须**往前凑**。
        // 缺这条，上面那句"往后退"可能只是因为这只怪根本不动。
        var closing = RunTacticsTick(new TacticsCase(0, true, 8f, 0f, 1000, 3f));
        Assert.True(closing.DeltaX > 0f,
            $"近战怪在同样距离下没有扑上来（位移 {closing.DeltaX:F4}）——这个夹具里两档根本没差别，上面那条红不能归因给保持距离");

        // 对照二：距离已经落在 [min, max] 区间里，就不该再退（退过头会把怪拉出射程外的空地）
        var inBand = RunTacticsTick(new TacticsCase(0, true, 8f, 20f, 1000, 12f));
        Assert.True(inBand.DeltaX > 0f || Math.Abs(inBand.DeltaX) < 0.0001f,
            $"距离已经在保持区间里却还往后退（位移 {inBand.DeltaX:F4}）——判据没比对上下界");
    }

    /// <summary>造一只开了威胁表的怪的运行态：先被 91 嘲讽钉住，随后 92 打出一坨更高仇恨。</summary>
    private static MonsterRuntimeState BuildTauntedState(long tauntAtTick, float tauntSeconds)
    {
        var state = new MonsterRuntimeState();
        MonsterAiRuntime.NoteTaunt(state, 91UL, tauntAtTick, 60, tauntSeconds);
        // 嘲讽之后 DPS 又补了一刀（这是这条判据存在的全部理由：只抬"最高+1"的余量，别人一下平A 就盖过去了）
        MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, 92UL, 100000L, tauntAtTick + 10);
        return state;
    }

    private static (ulong Threat, ulong TauntTarget) AdvanceTauntCase(MonsterRuntimeState state, long tick, bool taunterPresent)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = 12441,
            Name = "嘲讽检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "chao_feng",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // Combatant 档位：只开威胁表。战术/技能/寻路都不掺进来，红了只归因给嘲讽这一条。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Combatant, null, null, null, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 30f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 200f;
        behavior.MoveSpeed = 4f;
        behavior.WalkSpeed = 2f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MeleeSwingSeconds = 1f;
        config.Behavior = behavior;

        var targetList = taunterPresent
            ? new[]
            {
                new MonsterAiTarget { EntityId = 91UL, X = 3f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
                new MonsterAiTarget { EntityId = 92UL, X = 4f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
            }
            : new[]
            {
                new MonsterAiTarget { EntityId = 92UL, X = 4f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
            };
        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Chase, Hp = 1000, ThreatTargetId = 92UL,
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2.5f, MoveSpeed = 4f,
            AggroRadius = 30f, LeashRadius = 200f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };
        var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, tick, 60, null);
        var result = MonsterAiRuntime.Advance(self, state, targetList, 1f / 60f, legacy, inputs);
        return (result.Decision.ThreatTargetId, state.TauntTargetId);
    }

    [Fact]
    public void 嘲讽窗口内钉住施法者而窗口一过或被钉的人不在场就立刻撤锁()
    {
        // 3 秒 @60tick = 180 tick：tauntAt=100 ⇒ 截止 280。
        var state = BuildTauntedState(100, 3f);
        Assert.True(state.TauntUntilTick == 280 && state.TauntTargetId == 91UL,
            $"NoteTaunt 落点不对（截止 {state.TauntUntilTick}、对象 {state.TauntTargetId}）——夹具与秒数换算没对上");

        // ① 窗口内：92 的仇恨高出五个数量级，也必须钉在 91 上。
        var inside = AdvanceTauntCase(state, 120, true);
        Assert.Equal(91UL, inside.Threat);

        // ② 窗口一过：交回威胁表（此时 92 才是最高）。
        var after = AdvanceTauntCase(BuildTauntedState(100, 3f), 281, true);
        Assert.Equal(92UL, after.Threat);
        Assert.Equal(0UL, after.TauntTarget);   // 撤锁要真的撤，不能只靠"下次不再进这个分支"

        // ③ 被钉的人不在场（死亡/离开视野）：**当拍就撤**，否则怪会朝查无此人狂奔。
        //    这条必须用"还在窗口内"的时刻测，否则红了分不清是过期还是不在场。
        var vanished = AdvanceTauntCase(BuildTauntedState(100, 3f), 120, false);
        Assert.Equal(92UL, vanished.Threat);
        Assert.Equal(0UL, vanished.TauntTarget);
    }

    [Fact]
    public void 玩家嘲讽的两条受理通路与三条拉不动的情形各自计数()
    {
        // 这一段测的是**宿主**：内核那条锁上一轮已经单独钉住了，这里要问的是
        // "客户端的一帧动作包真能走到那条锁吗"，以及"拉不动的原因是不是各回各的账"。
        // 拒绝分支只测最容易碰到的那一条 = 其余分支静默回同一个 Invalid，玩家与排查者都看不出区别。
        const int templateId = 12442;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, true, 0f);
        config.Behavior.AggroRadius = 12f;    // 坦克站 20 米：怪自己索不到他，只有嘲讽能把目标换成他
        config.Behavior.LeashRadius = 200f;   // 追到 20 米不算超距（否则红了分不清是嘲讽失效还是 evade）
        // 移动速度钉 0：怪站在原地，谁都别因为"走开了所以打不到/够不着"而把判据读成另一件事。
        config.Behavior.MoveSpeed = 0f;
        config.Behavior.WalkSpeed = 0f;
        // 怪的攻击力钉 0：这条用例要看的是"目标钉在谁身上"，不是"谁先死"。
        // 不钉的话贴身那个 DPS 会在几百拍后被磨死，他一死就离开目标集合，①c 那句会红在错误的原因上。
        config.Stats.AtkBase = 0;
        config.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateId] = config;

        // 第二条受理通路用的怪：**能力位全关**（= 生产数据里 11 只种子怪的 ai_profile=0）。
        // 它让 `TryDecideMonsterV2` 直接返回 null ⇒ 这只怪永远没有 `Runtime`，
        // 也就是"嘲讽"必须能在 v1 通路上也拉得动，否则绝大多数怪会静默地拉不动。
        const int templateV1 = 12443;
        var v1Config = BuildAssistTemplate(templateV1, 4, false, 0f);
        v1Config.Behavior.Flags = MonsterAiFlags.None;
        v1Config.Behavior.MoveSpeed = 0f;
        v1Config.Behavior.WalkSpeed = 0f;
        bundle.ByTemplateId[templateV1] = v1Config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 12f;
            point.MoveSpeed = 0f;

            var v1Point = SpawnPointConfig.Default;
            v1Point.SpawnPointId = 2;
            v1Point.MonsterTemplateId = templateV1;
            v1Point.MaxAlive = 1;
            v1Point.X = 40f; v1Point.Y = 0f; v1Point.Z = 0f;
            v1Point.AggroRadius = 0f;      // 它自己不索敌：目标变化只能来自嘲讽
            v1Point.LeashRadius = 200f;
            v1Point.MoveSpeed = 0f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point, v1Point });
            Assert.True(world.AddPlayer(8001, 8001, 20f, 0f, 0f, out var rTank), rTank);   // 坦克（远，超出普攻距离）
            Assert.True(world.AddPlayer(8002, 8002, -2f, 0f, 0f, out var rDps), rDps);      // DPS（贴身 2 米，且** facing 朝 +X 正好对着怪**）

            var monsterId = 0UL;
            var monsterIdV1 = 0UL;
            void DpsSwing()
            {
                world.SubmitCombatAction(8002, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 8002, TargetId = monsterId,
                    ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
            }

            for (var i = 0; i < 120; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (!d.Identity.HasValue) continue;
                    if (monsterId == 0UL && d.Identity.Value.TemplateId == templateId) monsterId = d.Identity.Value.NetworkId;
                    if (monsterIdV1 == 0UL && d.Identity.Value.TemplateId == templateV1) monsterIdV1 = d.Identity.Value.NetworkId;
                }
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn——点位没装配起来");
            Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null, "TryGetMonster 取不到刚 Spawn 的怪");
            world.Tick();
            Assert.Equal(8002UL, mob.Ai.ThreatTargetId);   // 开局盯的是贴身那个：没有这条，"换成坦克"就无从谈起

            // ① 受理：坦克在 20 米外喊一嗓子，怪必须把目标钉在坦克身上。
            //    20 米 > 普攻距离 3 米，所以这一条同时钉住"嘲讽是远程施法而不是贴身踢脚"。
            var accepted = world.SubmitCombatAction(8001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 8001, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Hit, accepted.Outcome);
            Assert.Equal(1, world.MonsterTauntAcceptedCount);
            world.Tick();
            Assert.Equal(8001UL, mob.Ai.ThreatTargetId);
            Assert.Equal(8001UL, mob.Runtime!.TauntTargetId);

            // ①b 这一条才是 v9 的重点：**光靠"威胁抬到最高+1"不算拉走**。
            //    DPS 就在 2 米外按着节拍继续砍，每一刀的威胁都远大于那 1 点余量；
            //    没有锁的话怪会被立刻带回去——玩家看到的就是"坦克拉了个寂寞"。
            //    数据面那道 60 tick 边界限流对所有战斗动作生效，所以两刀之间隔 65 tick。
            //    "砍到了没有"用**开砍前的血量**当参照，而不是拿模板里的 HpBase 推——
            //    上一版就是凭 `Hp < 1000` 判"落刀了"，而这只怪的实际血量根本不是 1000，空转判据反被读成通过。
            var hpBeforeSwings = mob.Ai.Hp;
            for (var i = 0; i < 130; i++)
            {
                world.Tick();
                if (i % 65 == 30) DpsSwing();
            }
            Assert.True(mob.Ai.Hp < hpBeforeSwings,
                $"DPS 在 2 米外砍了这么久，怪的血量还是 {mob.Ai.Hp}（开砍前 {hpBeforeSwings}）" +
                "——那一刀根本没落过，上面那句「仍钉住」是在空转");
            Assert.Equal(8001UL, mob.Ai.ThreatTargetId);   // 窗口（180 tick）还没过，必须仍钉在坦克身上

            // ①c 窗口一过必须交回伤害竞争（锁是限时的，不是永久粘住）。
            for (var i = 0; i < 140; i++)
            {
                world.Tick();
                if (i % 65 == 30) DpsSwing();
            }
            Assert.Equal(8002UL, mob.Ai.ThreatTargetId);
            Assert.Equal(0UL, mob.Runtime!.TauntTargetId);  // 撤锁要真的撤，不能只靠"下次不再进这个分支"

            // ② 够不着：100 米外的人喊，拒且计数（这一条不改动怪，可以接着往下测）。
            Assert.True(world.AddPlayer(8003, 8003, 100f, 0f, 0f, out var rFar), rFar);
            var tooFar = world.SubmitCombatAction(8003, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 8003, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.OutOfRange, tooFar.Outcome);
            Assert.Equal(1, world.MonsterTauntRejectedOutOfRangeCount);
            Assert.Equal(1, world.MonsterTauntAcceptedCount);   // 被拒的那次绝不能同时记成受理

            // ③ 回位免疫窗里拉不住：当场拒，而不是"答应下来、窗过再生效"。
            mob.Runtime!.EvadeImmuneUntilTick = world.ServerTick + 120;
            var immune = world.SubmitCombatAction(8001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 8001, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Invalid, immune.Outcome);
            Assert.Equal(1, world.MonsterTauntRejectedImmuneCount);

            // ④ 尸体不能被拉（坦克在怪倒地下了一手时该看见这条，而不是什么都没发生）。
            Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick), "打死的怪没进预期状态");
            var onCorpse = world.SubmitCombatAction(8001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 8001, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Invalid, onCorpse.Outcome);
            Assert.Equal(1, world.MonsterTauntRejectedCorpseCount);

            // ⑤ 第二条**受理**通路：能力位全关（v1）的怪没有威胁表也没有窗口，只钉它的单槽目标。
            //     这条必须单独钉——生产数据里种子怪全是这种（`ai_profile=0`）；
            //     只给开了威胁表的怪开口，"嘲讽"就对绝大多数怪静默失效，而那正是本轮反复在清的那一类形状。
            Assert.True(monsterIdV1 != 0UL, "v1 那只怪没 Spawn——第二个点位没装配起来");
            Assert.True(world.TryGetMonster(monsterIdV1, out var mobV1) && mobV1 != null);
            Assert.Null(mobV1.Runtime);   // 自证它真的走 v1（否则这条测的是上面那条通路）
            var v1Taunt = world.SubmitCombatAction(8001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 8001, TargetId = monsterIdV1,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Hit, v1Taunt.Outcome);
            Assert.Equal(1, world.MonsterTauntV1RetargetCount);
            Assert.Equal(1, world.MonsterTauntAcceptedCount);   // v1 那一次不该记到 v2 的受理账上
            world.Tick();
            Assert.Equal(8001UL, mobV1.Ai.ThreatTargetId);
            // 机制那一半：v1 的锁必须真的带窗口装进它自己的状态里（只钉目标不给截止时间，
            // "那人不在场就撤锁"这条判据根本没有输入可判，而锁会变成永久粘住）。
            Assert.Equal(8001UL, mobV1.Ai.TauntTargetId);
            Assert.True(mobV1.Ai.TauntUntilTick > world.ServerTick,
                        $"v1 的嘲讽窗口没装上：截止 {mobV1.Ai.TauntUntilTick}，当前 tick {world.ServerTick}");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 打在"正往家跑"的怪身上整手被拒，而**刚复活的**与**刚到家的**照旧挨打。
    /// <para>AzerothCore 把"打不到"挂在<b>受击方</b>那侧：<code>Creature::IsEvadingAttacks()</code>
    /// （<code>Creature.h:139</code>）被五处门共用——<code>Unit::CanAttack:10496</code>（攻击指令本身）、
    /// <code>RollMeleeOutcomeAgainst:2846</code>（已经在空中的那一刀）、<code>DealSpellDamage:1480</code> 与
    /// <code>DealMeleeDamage:1867</code>（伤害落地那一步）、<code>SpellHitResult:3529/3602</code>
    /// （回 <code>SPELL_MISS_EVADE</code>，屏幕上那行"闪避"就是它）。
    /// 而"刚复活 5 秒不许主动起手"挂在<b>攻击方</b>那侧（<code>Creature.cpp:2606</code>）——
    /// <b>两条宽度不同，必须分开钉</b>：合成一条会同时造出"刚冒出来的怪打不到"
    /// 与"跑回家了还一路挨打"两处与 AC 相反的表现。</para>
    /// </summary>
    [Fact]
    public void 打在正回位的怪身上整手被拒而宽限窗与免疫窗照旧挨打()
    {
        const int templateV2 = 12460;
        const int templateV1 = 12461;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };

        var c2 = BuildAssistTemplate(templateV2, 3, true, 0f);
        c2.Behavior.AggroRadius = 12f;
        c2.Behavior.LeashRadius = 3f;    // 半径只有 3 米：把出生锚点挪远，它当场就是"离家很远的回位中"
        c2.Behavior.MoveSpeed = 0f;      // 怪与人都不动 ⇒ 拒伤只能归因给那条门，不能是"走开了/够不着"
        c2.Behavior.WalkSpeed = 0f;
        c2.Stats.AtkBase = 0;
        c2.Stats.AtkPerLevel = 0;        // 怪不还手：这条用例不许有人中途倒下并离开目标集合
        bundle.ByTemplateId[templateV2] = c2;

        var c1 = BuildAssistTemplate(templateV1, 3, true, 0f);
        c1.Behavior.Flags = MonsterAiFlags.None;   // 生产数据里 11 只种子怪就是这个形态：宿主根本不进 v2
        c1.Behavior.AggroRadius = 12f;
        c1.Behavior.LeashRadius = 3f;
        c1.Behavior.MoveSpeed = 0f;
        c1.Behavior.WalkSpeed = 0f;
        c1.Stats.AtkBase = 0;
        c1.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateV1] = c1;
        MonsterContent.Install(bundle);
        try
        {
            SpawnPointConfig MakePoint(int id, int templateId, float x)
            {
                var p = SpawnPointConfig.Default;
                p.SpawnPointId = id;
                p.MonsterTemplateId = templateId;
                p.MaxAlive = 1;
                p.X = x; p.Y = 0f; p.Z = 0f;
                p.AggroRadius = 12f;
                p.LeashRadius = 3f;
                p.MoveSpeed = 0f;
                return p;
            }

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { MakePoint(1, templateV2, 0f), MakePoint(2, templateV1, 40f) });
            // 两个攻击者都站在怪的 -X 侧 2 米：玩家默认朝 +X，所以这个位置是**面对**怪的（上一轮就踩过
            // "站 +X 侧等于背对、攻击全被朝向扇区判负"那个坑，红了会归因错地方）。
            Assert.True(world.AddPlayer(9001, 9001, -2f, 0f, 0f, out var r1), r1);
            Assert.True(world.AddPlayer(9002, 9002, 38f, 0f, 0f, out var r2), r2);

            ulong idV2 = 0UL, idV1 = 0UL;
            for (var i = 0; i < 180; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (!d.Identity.HasValue) continue;
                    if (idV2 == 0UL && d.Identity.Value.TemplateId == templateV2) idV2 = d.Identity.Value.NetworkId;
                    if (idV1 == 0UL && d.Identity.Value.TemplateId == templateV1) idV1 = d.Identity.Value.NetworkId;
                }
            }
            Assert.True(idV2 != 0UL && idV1 != 0UL, "两只怪没 Spawn——点位没装配起来");
            Assert.True(world.TryGetMonster(idV2, out var mob) && mob != null, "TryGetMonster 取不到 v2 那只");
            Assert.True(world.TryGetMonster(idV1, out var mobV1) && mobV1 != null, "TryGetMonster 取不到 v1 那只");
            Assert.NotNull(mob.Runtime);   // 自证它真走 v2
            Assert.Null(mobV1.Runtime);    // 自证它真走 v1（否则"两条通路都挡"实际只测了一条）

            // 第一个参数是**会话号**（宿主按会话反查发起实体，不采信包里的 AttackerId），别当实体 ID 传。
            CombatResolution Swing(ulong targetId, long sessionId) => world.SubmitCombatAction(
                sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = (ulong)sessionId,
                    TargetId = targetId,
                    ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });

            // ① 先证明这一通路**本来打得动**：没有这条对照，"血没掉"与"这一刀从来没落到过"长得一模一样。
            Assert.True(mob.Ai.State == MonsterAiState.Attack || mob.Ai.State == MonsterAiState.Chase,
                        $"主动怪贴脸却没接敌（State={mob.Ai.State}）：后面所有'拒/不拒'都是在空转上读数");
            var hpBaseline = mob.Ai.Hp;
            var opener = Swing(idV2, 9001);
            Assert.Equal(CombatOutcome.Hit, opener.Outcome);
            Assert.True(mob.Ai.Hp < hpBaseline,
                        $"贴身一刀没落（血量仍是 {mob.Ai.Hp}，参照 {hpBaseline}）——这只怪从头到尾没挨到过");
            Assert.Equal(0, world.MonsterDamageRejectedEvadingCount);

            // ② 挪走出生锚点 ⇒ 从下一拍起它就是"正在回位"，而它和玩家仍在原地（2 米，普攻距离内）。
            //    两格的窗口起点都要先清成 0："刚刚那一刀"在新口径下会开风筝宽限窗（AC 的 leash 计时器），
            //    窗内**故意**不查回家距离——那是玩家能把它拉走的那 11 秒。这条用例要测的是"不在窗里"，
            //    所以显式把它写成 0（= 从没挨过打）；"伤害会把这格写成当前 tick"由 `挨打的那一拍…起点` 那条守。
            var s2 = mob.Ai; s2.SpawnX = 60f; s2.LastLeashExtensionTick = 0; mob.Ai = s2;
            var s1 = mobV1.Ai; s1.SpawnX = 100f; s1.LastLeashExtensionTick = 0; mobV1.Ai = s1;
            world.Tick();
            Assert.True(MonsterAiGate.ReturningHome(mob.Ai.State),
                        $"v2 那只没进回位态（State={mob.Ai.State}）：拒伤的前提没成立");
            Assert.True(MonsterAiGate.ReturningHome(mobV1.Ai.State),
                        $"v1 那只没进回位态（State={mobV1.Ai.State}）：作用域没补到生产多数");

            var hp2Before = mob.Ai.Hp;
            var hp1Before = mobV1.Ai.Hp;
            Assert.Equal(CombatOutcome.Evading, Swing(idV2, 9001).Outcome);
            Assert.Equal(CombatOutcome.Evading, Swing(idV1, 9002).Outcome);
            Assert.Equal(2, world.MonsterDamageRejectedEvadingCount);   // 机制层：两条通路各拒一次
            Assert.Equal(hp2Before, mob.Ai.Hp);                          // 效果层：血量一格没掉（v2）
            Assert.Equal(hp1Before, mobV1.Ai.Hp);                        // 效果层：血量一格没掉（v1）

            // ③ 服务端把这件事**告诉客户端**（否则玩家只会看到"按了技能而什么都没有"）。
            //    周期快照那一路必须带位，而且不许顺手把它标成尸体：回位中的怪血条与名字都还在。
            var carried = StateOfMonster(world, idV2);
            Assert.True(carried.HasValue, "回位中的怪在周期快照里没有状态分量");
            const uint evading = Horizon.Game.Message.Sync.Components.EntityStateBits.Evading;
            const uint dead = Horizon.Game.Message.Sync.Components.EntityStateBits.Dead;
            Assert.True((carried!.Value.StateBits & evading) != 0u,
                        $"快照没带 Evading 位：StateBits=0x{carried.Value.StateBits:X2}");
            Assert.True((carried.Value.StateBits & dead) == 0u, "回位被写成了死亡：尸体会收起名牌，而它不该");

            // ④ 两条"不挡打"的宽度：复活宽限（AC 挂在攻击方）与到家后的免疫窗（只管不接新敌）。
            //    同一次锚点复原 ⇒ 只有那一格窗口在变，红了只能归因给它。
            var back = mob.Ai; back.SpawnX = 0f; back.State = MonsterAiState.Idle; mob.Ai = back;
            world.Tick();
            Assert.True(!MonsterAiGate.ReturningHome(mob.Ai.State),
                        $"锚点复原后仍判成回位（State={mob.Ai.State}）：那三条'照旧挨打'的对照全都失效");

            var grace = mob.Ai; grace.NoInitiateUntilTick = world.ServerTick + 600; mob.Ai = grace;
            Assert.True(grace.NoInitiateUntilTick > world.ServerTick);
            var hpGraceBefore = mob.Ai.Hp;
            Assert.Equal(CombatOutcome.Hit, Swing(idV2, 9001).Outcome);
            Assert.True(mob.Ai.Hp < hpGraceBefore,
                        $"宽限窗里这一手被拒了（血 {hpGraceBefore} → {mob.Ai.Hp}）：AC 的 5 秒只管怪不许主动起手，不管挨打");
            Assert.Equal(2, world.MonsterDamageRejectedEvadingCount);   // 不许把"照旧挨打"也记进拒伤账

            var immune = mob.Ai; immune.EvadeImmuneUntilTick = world.ServerTick + 600; mob.Ai = immune;
            var hpImmuneBefore = mob.Ai.Hp;
            Assert.Equal(CombatOutcome.Hit, Swing(idV2, 9001).Outcome);
            Assert.True(mob.Ai.Hp < hpImmuneBefore,
                        $"免疫窗里这一手被拒了（血 {hpImmuneBefore} → {mob.Ai.Hp}）：那一窗只管不接新敌");
            Assert.Equal(2, world.MonsterDamageRejectedEvadingCount);

            // ⑤ 回位这一位会跟着状态一起收掉：到家之后必须恢复"打得动"（否则怪成了无敌炮台）。
            var clear = mob.Ai; clear.EvadeImmuneUntilTick = 0L; mob.Ai = clear;
            world.Tick();
            var afterHome = StateOfMonster(world, idV2);
            Assert.True(afterHome.HasValue && (afterHome.Value.StateBits & evading) == 0u,
                        $"它已经不往家跑了，快照里还挂着 Evading 位：StateBits=0x{afterHome?.StateBits ?? 0u:X2}");
            var hpAfterHome = mob.Ai.Hp;
            Assert.Equal(CombatOutcome.Hit, Swing(idV2, 9001).Outcome);
            Assert.True(mob.Ai.Hp < hpAfterHome, "位收掉了而血还在原地——拒伤的那道门根本没跟着状态走");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>取这只怪下一次周期快照里的状态分量（没有就返回 null，让调用方自己判"根本没下发"）。</summary>
    private static Horizon.Game.Message.Sync.Components.EntityStateAuthComponent? StateOfMonster(
        DsWorldSession world, ulong monsterId)
    {
        for (var i = 0; i < 12; i++)
        {
            var r = world.Tick();
            if (r.WorldSnapshot == null) continue;
            foreach (var d in r.WorldSnapshot.Deltas)
            {
                if (d.EntityId == monsterId && d.State.HasValue) return d.State.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// 站桩档（<c>follow_type=3</c>）走 v1 通路时的真表现。
    /// <para>切片 18 让"回位中打不到"生效之后，这一条多了一层要钉的东西：
    /// 走不回去的怪如果被判成"正在回位"，就永远出不去那个状态 ⇒ 变成一只<b>永远打不到</b>的怪。
    /// 所以这里同时钉四件事：站桩的不产生位移、站桩的不进回位、因此它仍然挨得到打，
    /// 而同一份夹具里的步行档照常回位、照常"打不到"（对照组证明这不是夹具空转）。</para>
    /// </summary>
    [Fact]
    public void 站桩档的默认怪不位移也不进回位而步行档同几何会回位并打不到()
    {
        const int templateStatic = 12470;
        const int templateWalk = 12471;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };

        var stat = BuildAssistTemplate(templateStatic, 3, true, 0f);
        stat.Behavior.Flags = MonsterAiFlags.None;            // 默认档：生产里全部种子怪的样子
        stat.Behavior.FollowType = MonsterFollowType.Static;
        stat.Behavior.AggroRadius = 45f;                      // 40 米外那个人要能被索到（Chase 才是"会动"的状态）
        stat.Behavior.LeashRadius = 3f;
        stat.Behavior.MoveSpeed = 3f;                         // 故意给非 0：不动必须是因为站桩，不是因为速度是 0
        stat.Behavior.WalkSpeed = 3f;
        stat.Stats.AtkBase = 0;
        stat.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateStatic] = stat;

        var walk = BuildAssistTemplate(templateWalk, 3, true, 0f);
        walk.Behavior.Flags = MonsterAiFlags.None;
        walk.Behavior.FollowType = MonsterFollowType.GroundWalk;   // 只差这一格
        walk.Behavior.AggroRadius = 45f;
        walk.Behavior.LeashRadius = 3f;
        walk.Behavior.MoveSpeed = 3f;
        walk.Behavior.WalkSpeed = 3f;
        walk.Stats.AtkBase = 0;
        walk.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateWalk] = walk;
        MonsterContent.Install(bundle);
        try
        {
            SpawnPointConfig MakePoint(int id, int templateId, float x)
            {
                var p = SpawnPointConfig.Default;
                p.SpawnPointId = id;
                p.MonsterTemplateId = templateId;
                p.MaxAlive = 1;
                p.X = x; p.Y = 0f; p.Z = 0f;
                p.AggroRadius = 45f;
                p.LeashRadius = 3f;
                p.MoveSpeed = 3f;
                return p;
            }

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { MakePoint(1, templateStatic, 0f), MakePoint(2, templateWalk, 20f) });

            // 两个"开局就在场"的人：远端那个专门给站桩那只锁（它追不到，因此一直停在 Chase 这一支
            // ——那正是"会写位移"的分支，才有东西可对照），近端那个给步行那只锁（2 米内 ⇒ Attack，
            // 不动、也不会因为追出去而先把自己脱战回家）。
            // 踩过的坑：两只都放 40 米外时，步行那只真的追出去了，追到超出脱战半径 → 自己回位、
            // 到家清威胁并开免疫窗 ⇒ 到读数那一步它 threat=0/state=Idle，对照组根本没在打谁。
            Assert.True(world.AddPlayer(9101, 9101, 40f, 0f, 0f, out var rFar), rFar);
            Assert.True(world.AddPlayer(9102, 9102, 22f, 0f, 0f, out var rNearB), rNearB);

            ulong idStatic = 0UL, idWalk = 0UL;
            for (var i = 0; i < 120; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (!d.Identity.HasValue) continue;
                    if (idStatic == 0UL && d.Identity.Value.TemplateId == templateStatic) idStatic = d.Identity.Value.NetworkId;
                    if (idWalk == 0UL && d.Identity.Value.TemplateId == templateWalk) idWalk = d.Identity.Value.NetworkId;
                }
            }
            Assert.True(idStatic != 0UL && idWalk != 0UL, "两只怪没 Spawn——点位没装配起来");
            Assert.True(world.TryGetMonster(idStatic, out var mobStatic) && mobStatic != null);
            Assert.True(world.TryGetMonster(idWalk, out var mobWalk) && mobWalk != null);
            Assert.Null(mobStatic.Runtime);   // 自证走的是 v1（FollowType 这个输入以前根本不存在）
            Assert.Null(mobWalk.Runtime);

            // 锁定成立：站桩那只必须在"会动"的那一支里（Chase），否则"没位移"根本没有对照物。
            // 不钉"锁的是哪一个人"：v1 锁的是射程外最近的那个（9102 在 22 米、9101 在 40 米），
            // 而这一条要的只是"它在追、因此本该产生位移"。
            Assert.Equal(MonsterAiState.Chase, mobStatic.Ai.State);
            Assert.NotEqual(0UL, mobStatic.Ai.ThreatTargetId);
            Assert.True(mobWalk.Ai.State == MonsterAiState.Attack || mobWalk.Ai.State == MonsterAiState.Chase,
                        $"步行那只没接敌（State={mobWalk.Ai.State} threat={mobWalk.Ai.ThreatTargetId}）：对照组不在战斗里，" +
                        "下面\"它会回位而站桩的不会\"就没有起点");
            Assert.True(!MonsterAiGate.ReturningHome(mobWalk.Ai.State),
                        $"对照组在挪锚点之前就已经在回位（State={mobWalk.Ai.State}）：夹具自己把自己脱战了");

            // 把两只的出生锚点都挪到 60/80 米外，而脱战半径只有 3 米。
            var s1 = mobStatic.Ai; s1.SpawnX = 60f; mobStatic.Ai = s1;
            var s2 = mobWalk.Ai; s2.SpawnX = 100f; mobWalk.Ai = s2;

            var xStaticBefore = mobStatic.Ai.X;
            var xWalkBefore = mobWalk.Ai.X;
            for (var i = 0; i < 60; i++) world.Tick();

            // ① 判据层：站桩的不进回位（走不回去的怪进了回位就是死锁，还会被切片 18 冻成"打不到"）
            Assert.True(MonsterAiGate.ReturningHome(mobWalk.Ai.State),
                        $"步行档挪了锚点却没进回位（State={mobWalk.Ai.State} threat={mobWalk.Ai.ThreatTargetId} " +
                        $"x={mobWalk.Ai.X} spawnX={mobWalk.Ai.SpawnX}；对照站桩那只 State={mobStatic.Ai.State} " +
                        $"threat={mobStatic.Ai.ThreatTargetId}）：对照组不成立，下面的读数全无意义");
            Assert.True(!MonsterAiGate.ReturningHome(mobStatic.Ai.State),
                        $"站桩档被判成了回位（State={mobStatic.Ai.State}）：它走不回去，这会变成永远打不到的怪");

            // ② 位移层：同一段时间里步行档真的走了，站桩档一格没动
            Assert.True(Math.Abs(mobWalk.Ai.X - xWalkBefore) > 0.5f,
                        $"对照组没移动（{xWalkBefore} → {mobWalk.Ai.X}）：这套夹具本来就动不了，" +
                        "\"站桩不动\"是在空转");
            Assert.True(Math.Abs(mobStatic.Ai.X - xStaticBefore) < 1e-4f,
                        $"站桩档在 60 tick 里漂了 {mobStatic.Ai.X - xStaticBefore} 米：那一格对 v1 还是装饰");

            // ③ 挨得到打：站桩的没在回位 ⇒ 切片 18 那道门不该拦它；步行档正在回位 ⇒ 该拦。
            //    9102 就是 B 贴身那个（它一直在场，B 往家走了 3 米后仍在它 3 米普攻距离内）；
            //    A 这边新放一个 9103 贴身——A 的锁定仍在远端那个身上，加个人不会把它换走。
            Assert.True(world.AddPlayer(9103, 9103, -1.5f, 0f, 0f, out var rNearA), rNearA);
            var hpStaticBefore = mobStatic.Ai.Hp;
            var hitStatic = world.SubmitCombatAction(9103, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9103UL, TargetId = idStatic,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.Equal(CombatOutcome.Hit, hitStatic.Outcome);
            Assert.True(mobStatic.Ai.Hp < hpStaticBefore,
                        $"站桩怪被拒了或没掉血（{hpStaticBefore} → {mobStatic.Ai.Hp}）：它没在回位，那道门不该拦它");

            var hpWalkBefore = mobWalk.Ai.Hp;
            var hitWalk = world.SubmitCombatAction(9102, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9102UL, TargetId = idWalk,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.Equal(CombatOutcome.Evading, hitWalk.Outcome);
            Assert.Equal(hpWalkBefore, mobWalk.Ai.Hp);
            Assert.Equal(1, world.MonsterDamageRejectedEvadingCount);   // 只拒正在回位那一只
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 巡逻点那格"停多久"真的能逐点覆盖路线默认，而 <c>-1</c> 与 <c>0</c> 是两回事。
    /// <para>
    /// <b>为什么要单独写这一条</b>：这一格的**数据面早就通了**——库列 <c>pause_seconds</c>
    /// （<c>scripts/sql/019_monster_editor_tables.sql:219</c>）→ 实体 → 草稿键 <c>pauseSeconds</c> →
    /// 快照（<c>MonsterContentSnapshot.cs:439</c>）→ 内核字段（<c>MonsterPatrolPoint.PauseSeconds</c>），
    /// 覆盖那一句在 <c>MonsterBehaviorCore.cs:516</c>。但全仓**没有任何用例问过它**：
    /// 于是"给瞭望点配 7 秒"与"根本没读这一格"在测试眼里是同一条路（记忆
    /// [[green-selftest-can-mean-the-line-never-ran]] 那一族——编译过不等于跑过）。
    /// </para>
    /// <para>
    /// <b>dt 取 0.25 而不是 1/60</b>：0.25 在二进制里是精确的，累计 k 次恰好是 0.25k 秒，
    /// 期望拍数能**精确算出来**（<c>Waiting = PausedSeconds &lt; pause</c> ⇒ 拍数 = ceil(pause/dt) - 1），
    /// 不用"±1 容差"去糊浮点累计漂移（那是把判据换成关系断言的信号，见
    /// [[fixture-numbers-must-be-measured-not-derived]]）。
    /// </para>
    /// <para>AC 侧的实测形状（<c>Tools/_rehearsal/ac_patrol_census.txt</c>）：97.8% 的点不停、
    /// 非零 delay 的中位是 7 秒 ⇒ "逐点能配停多久、且默认就是不停"是内容要的形状。</para>
    /// </summary>
    [Fact]
    public void 巡逻每点的停留秒真的逐点覆盖而没填与填零是两回事()
    {
        const float dt = 0.25f;      // 二进制精确：累计 k 拍 = 0.25k 秒，无浮点漂移
        const float speed = 40f;     // 每拍走 10 米：段长 10 米 ⇒ 一拍到下一个点，停留数不被赶路稀释

        // 小宿主：不停留就朝目的地走一步，停留就不动。返回**每次到点的停留拍数**（按到点顺序）。
        static List<int> Dwells(MonsterPatrolPoint[] points, float routePause, int maxTicks)
        {
            var route = new MonsterPatrolRoute
            {
                RouteId = 1,
                Mode = MonsterPatrolMode.Loop,
                PauseAtPointSeconds = routePause,
                UseNavMesh = false,
                Points = points,
            };
            var self = new MonsterPatrolSelf { PointIndex = 0, Direction = 1, Arrived = false };
            var x = points[0].X;
            var y = points[0].Y;
            var dwells = new List<int>();
            var current = -1;
            for (var tick = 0; tick < maxTicks; tick++)
            {
                var step = MonsterPatrolCore.Advance(ref self, route, x, y, dt);
                if (step.Waiting)
                {
                    current = Math.Max(0, current) + 1;
                    continue;           // 停留 = 不移动
                }
                // 一次 SwitchedPoint == 一段停留结束。**零秒也要记一段**（填 0 = 明确不停），
                // 所以这里不能只在"数过等待拍"时才记账——那样 pause=0 那一档会一段都数不到，
                // 而"数不到"与"没走到这个点"在这份读数里长得一样。
                if (step.SwitchedPoint)
                {
                    dwells.Add(Math.Max(0, current));
                    current = -1;
                }
                var dx = step.TargetX - x;
                var dy = step.TargetY - y;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= 0.0001f) continue;
                var ratio = MathF.Min(1f, speed * dt / dist);
                x += dx * ratio;
                y += dy * ratio;
            }
            if (current >= 0) dwells.Add(current);
            return dwells;
        }

        // ① 点级填 5 秒而路线默认 2 秒 ⇒ 必须按 5 秒停。
        //    理想值 ceil(5/0.25)-1 = 19：判据先自证"这一格真的赢过路线那一份"。
        var five = new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f, 5f),
            new MonsterPatrolPoint(10f, 0f, 0f, 5f),
        };
        var dwellFive = Dwells(five, 2f, 400);
        Assert.True(dwellFive.Count >= 2, "两次到点都没被测到（停留拍数列表 " + dwellFive.Count + " 项）：这条判据在空转");
        Assert.Equal(19, dwellFive[0]);
        Assert.Equal(19, dwellFive[1]);

        // ② 点级留空（构造默认 -1 = 没填）⇒ 落到路线那一份，ceil(2/0.25)-1 = 7。
        //    与①的读数**不同**，这两半放一起才排除"两个数其实谁都没被读"。
        var inherit = new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f),
            new MonsterPatrolPoint(10f, 0f, 0f),
        };
        Assert.Equal(7, Dwells(inherit, 2f, 400)[0]);

        // ③ 点级填 0 = **明确不停**（不是"用路线默认"）：少了这一档，"我要它一路走别停"就配不出来，
        //    而 AC 里 97.8% 的点恰恰是这一档。
        var noStop = new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f, 0f),
            new MonsterPatrolPoint(10f, 0f, 0f, 0f),
        };
        Assert.Equal(0, Dwells(noStop, 2f, 400)[0]);

        // ④ 同一条线上两个点各配不同秒数 ⇒ 停留**逐点不同**（ceil(1/0.25)-1 = 3）。
        //    整条路线只读一个统一秒数的实现会在这里露出来：它会给 [19,19] 或 [3,3]。
        var mixed = new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f, 5f),
            new MonsterPatrolPoint(10f, 0f, 0f, 1f),
        };
        var dwellMixed = Dwells(mixed, 2f, 400);
        Assert.True(dwellMixed.Count >= 4,
                    "四段停留没被测全（只测到 " + dwellMixed.Count + " 段）：这条判据数不到逐点差异");
        Assert.Equal(19, dwellMixed[0]);
        Assert.Equal(3, dwellMixed[1]);
        Assert.Equal(19, dwellMixed[2]);   // 第二轮回来还是 5 秒 ⇒ 停留是**按点**记的，不是按轮次衰减
        Assert.Equal(3, dwellMixed[3]);

        // ⑤ 防空转：上面三档若被某个实现统一读成同一个数，①②④ 会一起红——
        //    所以这里再把"路线级那一格确实也在被读"钉一次：路线 1 秒 + 点级没填 ⇒ 3 拍。
        Assert.Equal(3, Dwells(inherit, 1f, 400)[0]);
    }

    /// <summary>
    /// 同一条巡逻线在 v1 与 v2 上给出<b>逐格相同</b>的走法。
    /// <para>路线以前只有 v2 读得到（<c>MonsterContent.Route(...)</c> 只出现在那一段，v1 的
    /// <c>Decide</c> 连 route 入参都没有）⇒ 默认档（<c>ai_profile=0</c>，生产里全部种子怪）的怪
    /// 画了线也只在出生点周围按 <c>wander_radius</c> 游荡。这一条钉的不是"v1 现在会走线"，
    /// 而是<b>两条通路用的是同一份走线判据</b>：两份实现迟早会在"到点停多久、往返在哪一段回头"上分家，
    /// 而那种分家策划在编辑器里看不见（同一份内容，换一档 AI 就走出另一条轨迹）。</para>
    /// </summary>
    [Fact]
    public void 同一条巡逻线在v1与v2上给出逐格相同的走法()
    {
        const float dt = 1f / 60f;
        var route = new MonsterPatrolRoute
        {
            RouteId = 7,
            Mode = MonsterPatrolMode.PingPong,
            PauseAtPointSeconds = 0.5f,
            UseNavMesh = false,     // v1 没有图可查；这条对账只在**都不开走图**的配置上成立
            Points = new[]
            {
                new MonsterPatrolPoint(0f, 0f, 0f, 0f),
                new MonsterPatrolPoint(10f, 0f, 0f, 0f),
                new MonsterPatrolPoint(10f, 10f, 0f, 0f),
            },
        };

        static List<(float X, float Y)> WalkV1(MonsterPatrolRoute? line, int ticks)
        {
            var cfg = MonsterAiConfig.Default;
            var self = new MonsterAiSelf
            {
                State = MonsterAiState.Patrol, X = 0f, Y = 0f, Z = 0f,
                SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f, Hp = 100,
                StateElapsedSeconds = 10f, PatrolDirection = 1f,
            };
            var none = Array.Empty<MonsterAiTarget>();
            var path = new List<(float, float)>();
            for (var i = 0; i < ticks; i++)
            {
                var d = MonsterAiCore.Decide(self, none, dt, cfg, 1000L + i, 60, line);
                self.X += d.DeltaX;
                self.Y += d.DeltaY;
                self.State = d.NextState;
                self.Patrol = d.Patrol;        // 宿主就是这一句的语义（无条件写回）
                self.StateElapsedSeconds += dt;
                path.Add((self.X, self.Y));
            }
            return path;
        }

        static List<(float X, float Y)> WalkV2(MonsterPatrolRoute? line, int ticks)
        {
            // 至少开一条能力位：能力位全关时 v2 逐字复现 v1（那是另一条用例钉的事），
            // 而这条要看的正是 v2 那条读线的分支。威胁表开着但没有目标，等于什么都没改。
            var content = BuildAssistTemplate(12480, 3, false, 0f);
            content.Behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 0, 0, 0, 0);
            content.Behavior.MoveSpeed = MonsterAiConfig.Default.MoveSpeed;   // 与 v1 那份同一个速度
            content.Behavior.LeashRadius = 200f;
            content.Behavior.WanderRadius = MonsterAiConfig.Default.WanderRadius;
            var legacy = MonsterAiConfig.Default;
            legacy.MoveSpeed = content.Behavior.MoveSpeed;
            var state = new MonsterRuntimeState();
            var self = new MonsterAiSelf
            {
                State = MonsterAiState.Patrol, X = 0f, Y = 0f, Z = 0f,
                SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f, Hp = 100,
                StateElapsedSeconds = 10f, PatrolDirection = 1f,
            };
            var none = Array.Empty<MonsterAiTarget>();
            var path = new List<(float, float)>();
            for (var i = 0; i < ticks; i++)
            {
                var inputs = new MonsterRuntimeInputs(
                    content, 10, new MonsterNavGrid(), null, false, 1000L + i, 60, line, null);
                var result = MonsterAiRuntime.Advance(self, state, none, dt, legacy, inputs);
                var d = result.Decision;
                self.X += d.DeltaX;
                self.Y += d.DeltaY;
                self.State = d.NextState;
                self.StateElapsedSeconds += dt;
                path.Add((self.X, self.Y));
            }
            return path;
        }

        const int ticks = 600;   // 10 秒：走完 0→(10,0)→(10,10) 再回头一截足够
        var v1 = WalkV1(route, ticks);
        var v2 = WalkV2(route, ticks);
        Assert.Equal(v2.Count, v1.Count);
        for (var i = 0; i < v1.Count; i++)
        {
            Assert.True(MathF.Abs(v1[i].X - v2[i].X) < 1e-4f && MathF.Abs(v1[i].Y - v2[i].Y) < 1e-4f,
                        $"第 {i} tick 两条通路分家：v1=({v1[i].X},{v1[i].Y}) v2=({v2[i].X},{v2[i].Y})");
        }

        // 防空转：这条线必须**真的被走过**（Y 方向只有线上第二、三点才有）。
        // 判的是"整段轨迹里的最大 Y/X"而不是"最后一 tick 在哪"——PingPong 走满 10 秒时
        // 怪已经掉头走回第一点了，拿终点当证据会把一条正确的实现读成"没走线"（第一版就是这么错的）。
        var maxY = 0f;
        var maxX = 0f;
        foreach (var p in v1)
        {
            if (p.Y > maxY) maxY = p.Y;
            if (p.X > maxX) maxX = p.X;
        }
        Assert.True(maxY > 8f && maxX > 9f,
                    $"走完 {ticks} tick，轨迹最远只到 X={maxX:0.##} / Y={maxY:0.##}：这条线根本没被读，" +
                    "上面那句\"逐格相同\"是在比两份空轨迹");

        // 反面对照：不带线时 v1 退回游荡（这份夹具的游荡半径来自**刷怪点**那一份 = 6 米，
        // 而 Y 恒为 0 —— 只有这条线才会产生 Y 方向的位移）。
        var noLine = WalkV1(null, ticks);
        var noLineMaxY = 0f;
        foreach (var p in noLine) if (p.Y > noLineMaxY) noLineMaxY = p.Y;
        Assert.True(noLineMaxY < 1e-4f,
                    $"没给线却走出了 Y={noLineMaxY:0.###}：那条\"游荡\"分支被改了，这条对照已经抓不到区别");
    }

    /// <summary>宽限/嘲讽/世界BOSS 这三条判据共用的最小战斗夹具（都只动一格，红了能归因）。</summary>
    private static (MonsterAiState NextState, ulong Threat, bool WantsAttack, bool AssistSeeded, ulong AssistCallerId) AdvanceCombatCase(
        MonsterRank rank, long noInitiateUntilTick, bool tauntArmed, float selfX, long tick,
        bool preFillThreat = false, bool passiveWithAssist = false,
        MonsterAiState? forceState = null, int ticks = 1, long leashExtension = 0L)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = 12451,
            Name = "宽限检验兽",
            Rank = rank,
            Species = 6,                 // 求援那条对照需要"同族"：物种 0 一律不许当同族
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "kuan_xian",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = passiveWithAssist
            ? MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, null, null)
            : MonsterAiFlags.Resolve(MonsterAiProfile.Combatant, null, null, null, null, null);
        behavior.IsAggressive = !passiveWithAssist;
        behavior.AggroRadius = 30f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 10f;        // 刻意小于夹具里的 50 米：让"超距"这件事必然成立
        behavior.MoveSpeed = 4f;
        behavior.WalkSpeed = 2f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MeleeSwingSeconds = 1f;
        behavior.AssistRadius = passiveWithAssist ? 20f : 0f;
        config.Behavior = behavior;

        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = selfX + 2f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
        };
        var state = new MonsterRuntimeState { NoInitiateUntilTick = noInitiateUntilTick };
        if (tauntArmed)
        {
            MonsterAiRuntime.NoteTaunt(state, 91UL, tick, 60, 3f);
        }
        if (preFillThreat)
        {
            // "这个人已经打过我"——AC 那条例外（!IsEngagedBy(victim)）在本作的形状：
            // 伤害写进威胁表的那一路不受宽限影响。
            MonsterThreatCore.AddThreat(state.ThreatTable, ref state.ThreatCount, 91UL, 50L, tick);
        }
        var allies = passiveWithAssist
            ? new[] { new Horizon.Game.Sim.Monsters.MonsterAiAlly(5001UL, selfX + 3f, 0f, 6, true, 91UL) }
            : null;
        var self = new MonsterAiSelf
        {
            X = selfX, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = forceState ?? (passiveWithAssist ? MonsterAiState.Idle : MonsterAiState.Chase), Hp = 1000,
            LastLeashExtensionTick = leashExtension,
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2.5f, MoveSpeed = 4f,
            AggroRadius = 30f, LeashRadius = 10f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };
        var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, tick, 60, null, allies);
        var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
        // AssistSeeded 是**机制层**的读数（求援分支这一 tick 到底进没进）；Threat 是效果层。
        // 只断效果层会被别处的那道门兜住——本轮反证就抓出过一次"测试红不了是因为另有判据在挡"。
        var lastState = result.Decision.NextState;
        var lastThreat = result.Decision.ThreatTargetId;
        var lastWantsAttack = result.Decision.WantsAttack;
        var anyAssistSeed = result.SeededByAssist;
        var assistCaller = result.AssistCallerId;
        // 第一拍也要把位移与状态写回 self，否则循环的第二拍是"从同一位置再走一遍"，
        // 跑 N 拍量到的就不是连续轨迹而是同一拍重复 N 次。
        self = new MonsterAiSelf
        {
            X = self.X + result.Decision.DeltaX,
            Y = self.Y + result.Decision.DeltaY,
            Z = self.Z,
            Yaw = result.Decision.NextYaw,
            SpawnX = self.SpawnX,
            SpawnY = self.SpawnY,
            SpawnZ = self.SpawnZ,
            State = forceState ?? result.Decision.NextState,
            Hp = self.Hp,
            ThreatTargetId = result.Decision.ThreatTargetId,
            PatrolDirection = self.PatrolDirection,
            LastLeashExtensionTick = self.LastLeashExtensionTick,
        };
        // AC 的应援延迟（<see cref="MonsterAiRuntime.AssistDelaySeconds"/>）把"听见"与"接手"分成两拍，
        // 所以问"这一族门开不开求援"必须跑满那一拍（调用方把 ticks 传成 `delayTicks + 余量`）。
        // 一拍就下结论的用例只会红在"参数没生效"上——那是尺子太短，不是产品坏了。
        for (var step = 1; step < ticks; step++)
        {
            var at = tick + step;
            var stepInputs = new MonsterRuntimeInputs(config, 10, default, null, false, at, 60, null, allies);
            result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, stepInputs);
            self = new MonsterAiSelf
            {
                X = self.X + result.Decision.DeltaX,
                Y = self.Y + result.Decision.DeltaY,
                Z = self.Z,
                Yaw = result.Decision.NextYaw,
                SpawnX = self.SpawnX,
                SpawnY = self.SpawnY,
                SpawnZ = self.SpawnZ,
                State = forceState ?? result.Decision.NextState,
                Hp = self.Hp,
                ThreatTargetId = result.Decision.ThreatTargetId,
                PatrolDirection = self.PatrolDirection,
            };
            lastState = result.Decision.NextState;
            lastThreat = result.Decision.ThreatTargetId;
            lastWantsAttack = result.Decision.WantsAttack;
            anyAssistSeed |= result.SeededByAssist;
            if (result.AssistCallerId != 0UL) assistCaller = result.AssistCallerId;
        }
        return (lastState, lastThreat, lastWantsAttack, anyAssistSeed, assistCaller);
    }

    [Fact]
    public void 复活的怪五秒内不主动索敌也不被同族叫起而先挨的那一手照样还手()
    {
        // 逆向依据：AzerothCore Creature.cpp:2606 —— 复活后 5 秒内"不能起手攻击"，
        // 除非对方已经打过我（IsEngagedBy）。玩家观感：贴着刷新点站不会被刚冒出来的怪秒咬。
        const long graceUntil = 200;
        var inside = AdvanceCombatCase(MonsterRank.Normal, graceUntil, false, 5f, 150);
        Assert.Equal(0UL, inside.Threat);
        Assert.False(inside.WantsAttack, "宽限期里怪还在挥刀——这条判据没作用在索敌上");

        // 正例（对照）：同一个夹具只把 tick 推到宽限之后，必须咬人
        var after = AdvanceCombatCase(MonsterRank.Normal, graceUntil, false, 5f, 201);
        Assert.Equal(91UL, after.Threat);

        // AC 那个例外：伤害已经写进威胁表 ⇒ 不受宽限影响（"你先打我，我就还手"）
        var retaliate = AdvanceCombatCase(MonsterRank.Normal, graceUntil, false, 5f, 150, preFillThreat: true);
        Assert.Equal(91UL, retaliate.Threat);

        // 第二条发起通路：被同族叫起来也算"主动"，宽限期内同样不许。
        // 两条都要跑满"AC 那两秒 + 余量"：一拍就断言会把"延迟还没到期"读成"这一族门挡住了"。
        // 窗口是**算过才这么长**的：calledInside 从 150 跑到 275，
        // 若宽限那道门失效，它最早在 150 听见、270 接手 ⇒ 落在窗口里，红得起来；
        // 门正常时最早只能 200 听见、320 接手 ⇒ 落在窗口外，0 次才是这条判据的正解。
        var delayTicks = (int)(MonsterAiRuntime.AssistDelaySeconds * 60);
        var calledInside = AdvanceCombatCase(MonsterRank.Normal, graceUntil, false, 5f, 150,
                                             passiveWithAssist: true, ticks: delayTicks + 6);
        Assert.Equal(0UL, calledInside.Threat);
        var calledAfter = AdvanceCombatCase(MonsterRank.Normal, graceUntil, false, 5f, 201,
                                            passiveWithAssist: true, ticks: delayTicks + 6);
        Assert.Equal(91UL, calledAfter.Threat);
    }

    [Fact]
    public void 回位整段路上的怪不被同族求援叫起而Idle时会被叫起()
    {
        // AzerothCore 把回位免疫做成**状态**：`CreatureAI::EnterEvadeMode`（AI/CreatureAI.cpp:205-233）
        // 挂 UNIT_STATE_EVADE 后 MoveTargetedHome，`SmartAI::MovementInform`（:653-657）到达点位才清；
        // 整段路里不吃仇恨（Unit.cpp:1092、:14765）。本作以前只有"到家后 EvadeImmunitySeconds 秒"那一半，
        // 于是玩家看得见的是：怪正往家跑，旁边同族打起来了，它半路就掉头参战。
        // AC 那两秒的应援延迟装在"听见"与"接手"之间 ⇒ 问这一族门必须跑满它（窗口 = 延迟 + 余量）。
        var delayTicks = (int)(MonsterAiRuntime.AssistDelaySeconds * 60);
        var idle = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 120,
                                     passiveWithAssist: true, ticks: delayTicks + 6);
        Assert.Equal(91UL, idle.Threat);   // 对照：同族在打 91，Idle 状态下必须被叫起来
        Assert.True(idle.AssistSeeded, "对照档里求援分支根本没进入：这条判据测的不是求援");

        var evading = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 120,
                                        passiveWithAssist: true, forceState: MonsterAiState.Evade,
                                        ticks: delayTicks + 6);
        Assert.Equal(0UL, evading.Threat);   // 回家路上叫不动
        // 机制层也必须关：只求"结果里没有目标"会被后面那道清零兜住（本轮反证 V2 就是这么绿的）。
        Assert.False(evading.AssistSeeded, "回位路上求援分支照样在写仇恨，只是结果被另一道门擦掉了");
    }

    [Fact]
    public void 嘲讽窗口内不因超出脱战半径而世界BOSS根本不做这条距离检查()
    {
        // 依据同一处代码的 :2630：那段"距出生点"检查写在 `!isWorldBoss() && (… || HasTauntAura())` 里。
        // 玩家侧事实：① 坦克拉开的怪不该跑一半自己回去；② 世界 BOSS 不会因为被拉远就脱战满血。
        // 夹具把 leash 半径配成 10 米而怪站在 50 米外 ⇒ "超距"必然成立，两档差别只能由这两条豁免产生。
        var plain = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 120);
        Assert.Equal(MonsterAiState.Evade, plain.NextState);   // 对照组：没有豁免时必须脱战

        var taunted = AdvanceCombatCase(MonsterRank.Normal, 0L, true, 50f, 120);
        Assert.NotEqual(MonsterAiState.Evade, taunted.NextState);
        Assert.Equal(91UL, taunted.Threat);

        var worldBoss = AdvanceCombatCase(MonsterRank.WorldBoss, 0L, false, 50f, 120);
        Assert.NotEqual(MonsterAiState.Evade, worldBoss.NextState);

        // 反面对照：首领（rank=3）**不是**世界 BOSS，AC 那条豁免只给世界 BOSS，所以它照样脱战
        var raidBoss = AdvanceCombatCase(MonsterRank.Boss, 0L, false, 50f, 120);
        Assert.Equal(MonsterAiState.Evade, raidBoss.NextState);
    }

    [Fact]
    public void 风筝宽限窗里被拉远的怪不脱战而窗一过期同一位置立刻脱战()
    {
        // AC `Creature::CanCreatureAttack`：`GetLastLeashExtensionTime() + GetLeashTimer() > now` 为真
        // 就整段跳过"距回家点"的检查（注释写着 allow kiting away from spawnpoint）。
        // 夹具与上面嘲讽那条同一份：怪在 50 米外、leash 10 ⇒ 超距必然成立，差别只能由宽限窗产生。
        var plain = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 120);
        Assert.Equal(MonsterAiState.Evade, plain.NextState);   // 从没挨过打 ⇒ 照旧脱战（反照）

        // 120 拍挨的打；等级 10 ⇒ 窗 11 秒 = 660 拍 ⇒ 120..779 免检距离
        var kiting = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 120, leashExtension: 120L);
        Assert.NotEqual(MonsterAiState.Evade, kiting.NextState);
        Assert.Equal(91UL, kiting.Threat);

        // 窗过期那一拍（120 + 660 = 780）⇒ 同一位置、同一目标，立刻回家
        var expired = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 50f, 780, leashExtension: 120L);
        Assert.Equal(MonsterAiState.Evade, expired.NextState);
    }

    [Fact]
    public void 求援接手那一拍回执带着是谁叫的而普通索敌没有这个号()
    {
        // 共享 leash 计时器（AC `Creature.cpp:231` 把首领的指针交给盟友）要靠这个号把两只并进一组；
        // 没有它，宿主只知道"它被人叫了"，没法决定并给谁。
        var woken = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 5f, 120,
                                      passiveWithAssist: true, ticks: 200);
        Assert.True(woken.AssistSeeded, "这条夹具本来该被同族叫起来");
        Assert.Equal(5001UL, woken.AssistCallerId);      // 夹具里唯一那只盟友的实体 id

        // 反照：同一具夹具去掉盟友 ⇒ 不是求援接手，也不许凭空冒出一个号
        var alone = AdvanceCombatCase(MonsterRank.Normal, 0L, false, 5f, 120, ticks: 200);
        Assert.False(alone.AssistSeeded);
        Assert.Equal(0UL, alone.AssistCallerId);
    }

    [Fact]
    public void 求援重听按AC三秒节流而不是每拍都扫盟友清单()
    {
        // AC `CreatureFamilyAssistancePeriod = 3000`（`WorldConfig.cpp:354`；`Creature.cpp:816` 的
        // `m_assistanceTimer` 到点才 `CallAssistance`）。没有这一格时本作每拍都听：
        // "隔壁那只慢慢晃进半径"会被**当拍**叫起，而 AC 最长等三秒。
        const int templateId = 12477;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 6, false, 20f);   // 被动 + 求援半径 20
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var targets = new[]
            {
                new MonsterAiTarget { EntityId = 91UL, X = 3f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
            };
            var state = new MonsterRuntimeState();
            var self = new MonsterAiSelf
            {
                X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
                State = MonsterAiState.Idle, Hp = 1000,
            };
            var legacy = new MonsterAiConfig
            {
                MaxHp = 1000, AttackPower = 10, AttackRange = 2.5f, MoveSpeed = 4f,
                AggroRadius = 30f, LeashRadius = 100f, WanderRadius = 0f,
                IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
            };
            var allies = new[] { new Horizon.Game.Sim.Monsters.MonsterAiAlly(5001UL, 1f, 0f, 6, true, 91UL) };

            var seeded = false;
            var seedTick = 0L;
            for (var t = 100; t <= 600 && !seeded; t++)
            {
                // 100 那一拍盟友不在（听空 ⇒ 节流起点 = 100 + 3 秒 = 280），此后盟友一直贴着
                var present = t == 100 ? null : allies;
                var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, t, 60, null, present);
                var r = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
                if (r.SeededByAssist) { seeded = true; seedTick = t; }
            }

            Assert.True(seeded, "盟友一直贴着却始终没接手——节流把求援整个关掉了");
            // 280 听见 ⇒ 2 秒后（120 拍）接手 = 400。若没有节流：101 听见 ⇒ 221 接手。
            Assert.Equal(400L, seedTick);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 刷怪点复活的怪装上五秒宽限而首次生成的不装()
    {
        // 宽限期有**两个写方**：① 刷怪点重建实体（宿主），② 同一实体从 Dead 翻回 Idle（内核）。
        // ②的断言在 `复活的怪五秒内…` 那条夹具里（直接看 NoteRespawn 的落点），这条测①，
        // 并顺带钉住"首次生成不装"——装了的话服务器一开、全图的怪都先聋 5 秒。
        const int templateId = 12452;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, true, 0f);
        config.Behavior.AggroRadius = 20f;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 20f;
            point.LeashRadius = 200f;
            point.RespawnDelaySeconds = 2f;    // 让重生在 120 tick 内发生，用例不必等默认的 15 秒
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(9001, 9001, 3f, 0f, 0f, out var reason), reason);

            var monsterId = 0UL;
            for (var i = 0; i < 30 && monsterId == 0UL; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateId) monsterId = d.Identity.Value.NetworkId;
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn");
            Assert.True(world.TryGetMonster(monsterId, out var first) && first != null);
            world.Tick();
            // 首次生成：不装宽限，玩家贴着脸就必须被咬（这条同时也是"没有宽限时它本来会咬"的正向对照）
            Assert.False(first.SpawnIsRespawn, "首次生成的怪被标成了复活——宽限期会盖住整张图的开局索敌");
            Assert.Equal(9001UL, first.Ai.ThreatTargetId);
            Assert.True(first.Runtime!.NoInitiateUntilTick == 0,
                $"首次生成的怪也装了宽限期（截止 {first.Runtime!.NoInitiateUntilTick} @tick {world.ServerTick}）");

            Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick), "标记死亡失败");

            // 等重生：尸体窗口被压到重生前一 tick，所以这里等到"同一个 id 又是一只活怪"
            var deadline = world.ServerTick + 400;
            DsMonster reborn = null;
            while (world.ServerTick < deadline)
            {
                world.Tick();
                if (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.Ai.Hp > 0
                    && cur.CorpseExpireAtTick == 0)
                {
                    reborn = cur;
                    break;
                }
            }
            Assert.True(reborn != null, "等了 400 tick 也没等到这只怪重生——刷怪点或尸体窗口没接起来");
            Assert.True(reborn.SpawnIsRespawn, "复活的那只没被标成复活 ⇒ 宿主这一路的宽限期根本没装（两个写方漏了一个）");

            world.Tick();   // 让 Runtime 在这一拍被建出来（装的时机就在这里）
            var armedAt = world.ServerTick;
            var graceLeft = reborn.Runtime!.NoInitiateUntilTick - armedAt;
            Assert.True(graceLeft > 0, $"复活的怪没装宽限期（截止 {reborn.Runtime.NoInitiateUntilTick} @tick {armedAt}）");
            Assert.True(graceLeft <= 5 * 60,
                $"宽限期比 5 秒还长（剩余 {graceLeft} tick）——装载时机或秒数换算有问题");
            Assert.Equal(0UL, reborn.Ai.ThreatTargetId);   // 玩家还站在 3 米外，但这一拍它不许咬

            // 过了宽限必须真的开始咬（否则"装宽限"等于"这只怪永久聋了"）
            for (var i = 0; i < 360; i++) world.Tick();
            Assert.Equal(9001UL, reborn.Ai.ThreatTargetId);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 能力位全关的种子怪也装五秒宽限而窗口内挨的那一手照样还手()
    {
        // 上面那条宽限用例走的是 v2（开了能力位）。**生产数据里全部种子怪都是 ai_profile=0**，
        // 也就是宿主根本不建 Runtime 的那一路——判据如果只长在 v2，玩家实际玩到的怪一条都没享受过。
        // 这条把同一件事在 v1 通路上钉一遍：机制（字段装上了）与效果（窗内不咬、窗过咬、挨过手立刻还手）。
        const int templateV1 = 12461;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateV1, 3, true, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;   // 能力位全关 ⇒ TryDecideMonsterV2 直接 return null
        config.Behavior.AggroRadius = 20f;
        bundle.ByTemplateId[templateV1] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateV1;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 20f;
            point.LeashRadius = 200f;
            point.RespawnDelaySeconds = 2f;
            point.MoveSpeed = 0f;    // 站桩：位移不参与判据，红了只能是"起手与否"

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家站在目标的 **-X 侧**：默认朝向是 +X，站在 +3 米等于背对怪，
            // 那一刀永远不落，后面的"还手"断言就成空转（这条夹具坑踩过一次）。
            Assert.True(world.AddPlayer(9101, 9101, -2f, 0f, 0f, out var reason), reason);

            var monsterId = 0UL;
            for (var i = 0; i < 30 && monsterId == 0UL; i++)
            {
                var r = world.Tick();
                if (r.WorldSnapshot == null) continue;
                foreach (var d in r.WorldSnapshot.Deltas)
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateV1) monsterId = d.Identity.Value.NetworkId;
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn");
            Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null);
            world.Tick();
            Assert.Null(mob.Runtime);   // 自证这条测的确实是 v1 通路（否则测的是上面那条）

            // 首次生成不装宽限，玩家贴脸就必须被咬。
            Assert.Equal(0L, mob.Ai.NoInitiateUntilTick);
            Assert.Equal(9101UL, mob.Ai.ThreatTargetId);

            // 打死 → 等重生。复活那一次必须由宿主把宽限装到**自身状态**上（v1 没有 Runtime 可装）。
            Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick), "标记死亡失败");
            DsMonster? reborn = null;
            var deadline = world.ServerTick + 400;
            while (world.ServerTick < deadline)
            {
                world.Tick();
                if (world.TryGetMonster(monsterId, out var cur) && cur != null
                    && cur.Ai.Hp > 0 && cur.Ai.State != MonsterAiState.Dead && cur.CorpseExpireAtTick == 0)
                {
                    reborn = cur;
                    break;
                }
            }
            Assert.True(reborn != null, "等了 400 tick 也没等到这只怪重生");
            Assert.True(reborn.SpawnIsRespawn, "复活的那只没被标成复活 ⇒ 宿主这一路根本没装宽限（两个写方漏了一个）");
            var graceLeft = reborn.Ai.NoInitiateUntilTick - world.ServerTick;
            Assert.True(graceLeft > 0,
                        $"v1 的怪复活后没装宽限期（截止 {reborn.Ai.NoInitiateUntilTick} @tick {world.ServerTick}）");
            Assert.True(graceLeft <= 5 * 60, $"宽限期比 5 秒还长（剩余 {graceLeft} tick）——换算有问题");

            // 效果那一半：窗内不许主动起手，可人还站在 3 米外。
            world.Tick();
            Assert.Equal(0UL, reborn.Ai.ThreatTargetId);

            // AC 那条判据带的例外：窗内先挨一手，攻击者立刻被钉住（不许变成"打了它它也不还手"）。
            var hpBefore = reborn.Ai.Hp;
            world.SubmitCombatAction(9101, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9101, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            world.Tick();
            Assert.True(reborn.Ai.Hp < hpBefore,
                        $"那一刀没落（血量仍是 {reborn.Ai.Hp}，开砍前 {hpBefore}）——下面的还手断言会空转");
            Assert.Equal(9101UL, reborn.Ai.ThreatTargetId);

            // 窗口一过，没挨过手的那只也得真的开始咬（否则"装宽限"等于"永久聋"）。
            var other = 0UL;
            for (var i = 0; i < 360; i++)
            {
                world.Tick();
                if (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.Ai.ThreatTargetId != 0UL) { other = cur.Ai.ThreatTargetId; break; }
            }
            Assert.Equal(9101UL, other);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 能力位全关的怪跑回家会回满血而免疫窗里嘲讽拉不走()
    {
        // v2 的 Evade 早就"到家刷满血 + 开免疫窗"，v1 的 Leash 两件事都没做。
        // 生产数据里的种子怪全走 v1 ⇒ 玩家看得见的是：风筝耗残的怪站在家里不回血，
        // 而且坦克在它刚回家的这几秒照样能把它拽走（宿主那条免疫拒绝键在 Runtime 上，v1 没有 Runtime）。
        const int templateV1 = 12462;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateV1, 3, true, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;
        config.Behavior.AggroRadius = 0f;         // 不自己索敌：目标变化只来自嘲讽，判据干净
        config.Behavior.LeashRadius = 6f;
        config.Behavior.EvadeImmunitySeconds = 7f;   // 一个和内容缺省(3 秒)不同的数：窗长能证明这两格真到了 v1
        config.Behavior.MoveSpeed = 4f;
        config.Behavior.WalkSpeed = 4f;
        config.Stats.AtkBase = 0;                 // 怪不还手：这条测的是回位，不是战斗
        config.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateV1] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateV1;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;
            point.LeashRadius = 6f;
            point.MoveSpeed = 4f;
            // 血量上限只能有一个权威：点位这一格在真管线上是装配期由 `MonsterSpawnPointBuilder`
            // 从模板属性算出来盖进来的（内容 HpBase=1000、等级 10、每级 0 ⇒ 1000）。
            // 这里把它写成 100 就是"夹具自己造了一个生产上不存在的双权威现场"，
            // 于是"回位刷满血"刷到 1000 而战斗层以为上限 100——玩家看得见的是血条 1000/100。
            point.MaxHp = 1000;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(9201, 9201, 2f, 0f, 0f, out var r1), r1);

            var monsterId = 0UL;
            for (var i = 0; i < 30 && monsterId == 0UL; i++)
            {
                var res = world.Tick();
                if (res.WorldSnapshot == null) continue;
                foreach (var d in res.WorldSnapshot.Deltas)
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateV1) monsterId = d.Identity.Value.NetworkId;
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn");
            Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null);
            world.Tick();
            Assert.Null(mob.Runtime);   // 自证走的是 v1 那一路

            // 把它摆成"被拉出脱战半径、只剩 30 血"的回位途中：位移与状态转移交给内核自己跑。
            var staged = mob.Ai;
            staged.State = MonsterAiState.Leash;
            staged.X = 20f;
            staged.Hp = 30;
            staged.EvadeImmuneUntilTick = 0L;
            mob.Ai = staged;

            // 第一件事先测"**路上**"：秒数窗还没开（它刚起步），AC 的状态判据必须已经生效。
            // 少了这一档，"整段回家路都不被拉回战斗"就只是"到家后那 7 秒"，
            // 而玩家看得见的是"坦克喊一嗓子，怪掉半步头又跟走了"。
            var onTheWay = world.SubmitCombatAction(9201, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9201, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Invalid, onTheWay.Outcome);
            Assert.Equal(1, world.MonsterTauntRejectedImmuneCount);
            Assert.Equal(0UL, mob.Ai.TauntTargetId);   // 拒就是拒，不许"先答应下来、窗过再生效"

            var healedAt = 0L;
            for (var i = 0; i < 600; i++)
            {
                world.Tick();
                if (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.Ai.State == MonsterAiState.Idle
                    && cur.Ai.Hp >= 1000)
                {
                    healedAt = world.ServerTick;
                    break;
                }
            }
            Assert.True(healedAt > 0, "跑回家 600 tick 后血量仍不是满的（或状态没回到 Idle）");
            Assert.True(world.TryGetMonster(monsterId, out var home) && home != null);
            Assert.Equal(1000, home.Ai.Hp);
            var healedHp = home.Ai.Hp;
            Assert.True(home.Ai.EvadeImmuneUntilTick > healedAt,
                        $"回位后没装免疫窗（截止 {home.Ai.EvadeImmuneUntilTick}，到家 tick {healedAt}）");
            // 装配那一半：行为表配的 7 秒必须真的作用到 v1 的窗长上（缺省那一份是 3 秒，
            // 只测"有没有窗"的话，内容那格被丢掉照样看不出来）。
            Assert.Equal(7 * 60, home.Ai.EvadeImmuneUntilTick - healedAt);

            // 效果那一半：窗内嘲讽必须**当场拒**（答应下来再让怪走回去，坦克会以为拉到了）。
            var inWindow = home.Ai.EvadeImmuneUntilTick - 1;
            Assert.True(inWindow > world.ServerTick, "到家后窗已经过完了，这条拒不到东西——把位移或秒数改掉是让用例变空转");
            var rejected = world.SubmitCombatAction(9201, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9201, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Invalid, rejected.Outcome);
            // 路上那一次已经计过 1，这里是**到家后窗内**的第二次：两次都得拒，来源不同（状态 / 秒数窗）。
            Assert.Equal(2, world.MonsterTauntRejectedImmuneCount);

            // 窗过之后同一次嘲讽就该受理（否则"开窗"变成"永久免疫"）。
            for (var i = 0; i < 600; i++) world.Tick();
            var accepted = world.SubmitCombatAction(9201, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9201, TargetId = monsterId,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
            });
            Assert.Equal(CombatOutcome.Hit, accepted.Outcome);
            Assert.Equal(1, world.MonsterTauntV1RetargetCount);
            // **两个权威必须同数**：回位刷的那个"满血"就是战斗层/快照用的那个上限。
            // 这一致性靠"装配期 builder 把内容算好的血量盖进点位那一份"维持；
            // 谁让两条通路各算一遍血量，这里当场红成"血条 1000/100"（脚本生成点那条路以前就是这样，
            // 而脚本生成点也一样：那两个消费者都走同一个投影，所以点位那份不再决定血量）。
            var probe = world.SubmitCombatAction(9201, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 9201, TargetId = monsterId,
            });
            Assert.Equal(healedHp, probe.TargetMaxHp);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 行为表那三格（近战挥击间隔／待机转巡逻／索敌角度）到不到得了<b>默认档的 v1 怪</b>的现场。
    /// <para>每档只换那一格，其余几何、朝向、玩家位置逐字相同——否则红出来归不清是谁让它红的。</para>
    /// <para><paramref name="playerDx"/> 是玩家相对怪的 X 偏移（判据只看 X/Y 平面）：
    /// 200 = 半径外（只测转巡逻时机）；-5 = 身后 5 米（半径内，只测扇形）；2 = 身前射程内（只测冷却）。</para>
    /// </summary>
    private static (bool PatrolReached, bool Acquired, float MaxCooldown) RunV1BehaviorCell(
        int templateId, float meleeSwingSeconds, float idleToPatrolSeconds, float aggroConeDegrees, float playerDx,
        bool aggressive = true)
    {
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, true, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;      // 默认档：这条必须走 v1（下面还有 Runtime==null 的自证）
        config.Behavior.IsAggressive = aggressive;        // 内容表"主动攻击"那一格（被动怪不许先咬人）
        config.Behavior.AggroRadius = 10f;
        config.Behavior.LeashRadius = 60f;
        config.Behavior.WanderRadius = 0f;
        config.Behavior.MoveSpeed = 4f;
        config.Behavior.MeleeSwingSeconds = meleeSwingSeconds;
        config.Behavior.IdleToPatrolSeconds = idleToPatrolSeconds;
        config.Behavior.AggroConeDegrees = aggroConeDegrees;
        config.Stats.AtkBase = 1;
        config.Stats.AtkPerLevel = 0;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;
            // 点位这几格按生产管线的样子写（装配期由 builder 从模板属性盖进来）：
            // 血量/攻击的上限有两个消费者（生成时的初始血、战斗层的上限），夹具不许把它们写成两套。
            point.MaxHp = 1000;
            point.AttackPower = 1;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });

            // 玩家先进场：快照的受众按"看得见"算，没有玩家就没有 delta 可收集（不是怪没生成）。
            // 怪的坐标随后**钉到原点**（连同出生点，几何整体平移），所以玩家那一格就是相对偏移。
            Assert.True(world.AddPlayer(9201, 9201, playerDx, 0f, 0f, out var addReason), addReason);

            var monsterId = 0UL;
            for (var i = 0; i < 30 && monsterId == 0UL; i++)
            {
                var res = world.Tick();
                if (res.WorldSnapshot == null) continue;
                foreach (var d in res.WorldSnapshot.Deltas)
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateId) monsterId = d.Identity.Value.NetworkId;
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn");
            Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null);
            Assert.Null(mob.Runtime);     // 自证这一路真的走的是 v1，不是"v2 恰好也在测"

            var staged = mob.Ai;
            staged.X = 0f; staged.SpawnX = 0f;
            staged.Y = 0f; staged.SpawnY = 0f;
            staged.Yaw = 0f;              // 朝向 +X：身后那位玩家就是"扇形外"，身前那位是"扇形内"
            mob.Ai = staged;

            var patrolReached = false;
            var acquired = false;
            var maxCooldown = 0f;
            for (var i = 0; i < 300; i++)
            {
                world.Tick();
                if (!world.TryGetMonster(monsterId, out var cur) || cur == null) continue;
                var ai = cur.Ai;
                if (ai.State == MonsterAiState.Patrol) patrolReached = true;
                if (ai.ThreatTargetId != 0UL || ai.State == MonsterAiState.Chase || ai.State == MonsterAiState.Attack)
                    acquired = true;
                if (ai.AttackCooldownRemaining > maxCooldown) maxCooldown = ai.AttackCooldownRemaining;
            }
            return (patrolReached, acquired, maxCooldown);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 行为表的挥击转巡逻扇形与被动档都到了默认档的怪()
    {
        // 这三格以前只在 v2 那边被读（v1 的 cfg 由 tick 通路零散盖 Rank/免疫秒/移动方式三格），
        // 于是同一张行为表换一档 AI 就是两套数：策划把"挥击间隔"从 1.5 秒改成 3 秒，
        // 开了威胁表的怪变慢、生产里默认档的那批一动不动。现在两条通路吃同一个投影。
        const int templateId = 12471;

        // ① 待机转巡逻：玩家在 200 米外（半径外），所以"转没转巡逻"只能归因给那一格。
        var fastIdle = RunV1BehaviorCell(templateId, 1.5f, 0.05f, 360f, 200f);
        var slowIdle = RunV1BehaviorCell(templateId, 1.5f, 20f, 360f, 200f);
        Assert.True(fastIdle.PatrolReached, "夹具前提：0.05 秒那档必须在 300 tick 窗口内转巡逻，否则这条测不到任何东西");
        Assert.False(slowIdle.PatrolReached, "待机转巡逻秒没到 v1：行为表改成 20 秒，默认档的怪还是 5 秒内就出去踱步");
        Assert.False(slowIdle.Acquired, "夹具坏了：200 米外的玩家不该被索敌");

        // ② 索敌角度：玩家在**身后** 5 米（半径 10 之内），60° 扇形不该看见、360° 必须看见。
        var behindNarrow = RunV1BehaviorCell(templateId, 1.5f, 50f, 60f, -5f);
        var behindWide = RunV1BehaviorCell(templateId, 1.5f, 50f, 360f, -5f);
        Assert.False(behindNarrow.Acquired, "扇形外还是接了敌：那一格仍然只有 v2 读得到（背后走来的人照样被咬）");
        Assert.True(behindWide.Acquired, "夹具前提：同样几何只把角度改成全向必须能接敌，否则上面那条假绿");
        // 贴在脚下（距离≈0）时方向没有定义，按"看得见"处理：让玩家成功贴到怪脚上却让它无视自己，
        // 是更坏的那种"看不见"。这一档也挡住主人那条 early-return 被改成 return false。
        var atFeet = RunV1BehaviorCell(templateId, 1.5f, 50f, 60f, 0f);
        Assert.True(atFeet.Acquired, "玩家贴在怪脚下反而被扇形挡掉：主人那条「贴脚算看得见」没生效");

        // ③ 近战挥击间隔：玩家在身前 2 米（攻击射程 2.5 之内），第一刀之后冷却那一格必须真到 v1。
        var slowSwing = RunV1BehaviorCell(templateId, 6f, 50f, 360f, 2f);
        var baseSwing = RunV1BehaviorCell(templateId, 1.5f, 50f, 360f, 2f);
        Assert.True(slowSwing.Acquired && baseSwing.Acquired, "夹具前提：身前 2 米必须打得着，否则冷却读数是在空转");
        Assert.Equal(6.0, slowSwing.MaxCooldown, 1);
        Assert.Equal(1.5, baseSwing.MaxCooldown, 1);

        // ④ 主动/被动那一格：同一个"身前 2 米"的几何，主动档接敌、被动档不接敌。
        //    （被动档"先挨一手就还手"那半边住在内核用例 ③ 里——那是宽度问题，不是这一格到没到。）
        var aggressiveCell = RunV1BehaviorCell(templateId, 1.5f, 50f, 360f, 2f, aggressive: true);
        var passiveCell = RunV1BehaviorCell(templateId, 1.5f, 50f, 360f, 2f, aggressive: false);
        Assert.True(aggressiveCell.Acquired, "夹具前提：主动档在同一几何下必须接敌");
        Assert.False(passiveCell.Acquired, "被动怪那一格没到默认档：is_aggressive=0 的怪照样先咬人");
    }

    /// <summary>脚本生成一只怪，返回它**生成那一刻**的血量（点位那份与内容公式那份不一致时，这里就是分诊台）。</summary>
    private static int SpawnScriptedMonsterHp(int templateId)
    {
        var key = Enumerable.Repeat((byte)0x5A, 32).ToArray();
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 1,
            EnableScriptedSpawn = true, ScriptedSigningKey = key,
        });
        Assert.True(world.AddPlayer(9201, 9201, 0f, 0f, 0f, out var addReason), addReason);

        var token = new Horizon.Game.Message.Network.ScriptGrantToken
        {
            Nonce = 777,
            OperatorCharacterId = 7001,
            Op = Horizon.Game.Message.Network.ScriptOp.Spawn,
            Kind = Horizon.Game.Message.Network.ScriptedKind.Monster,
            TemplateId = templateId,
            TargetShardId = 0,
            X = 5f, Y = 0f, Z = 0f,
            Count = 1,
            TtlSeconds = 0,
        };
        Horizon.Game.Message.Security.ScriptGrantTokenSigner.Sign(
            token, key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 30);
        Assert.Null(world.EnqueueScriptedCommand(token));

        var monsterId = 0UL;
        for (var i = 0; i < 10 && monsterId == 0UL; i++)
        {
            var res = world.Tick();
            if (res.WorldSnapshot == null) continue;
            foreach (var d in res.WorldSnapshot.Deltas)
            {
                if (d.Kind != Horizon.Game.Message.Sync.EntityDeltaKind.Spawn) continue;
                if ((int)(d.EntityId >> 32) >= 0) continue;          // 只看脚本域（负数刷怪点）
                if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateId) monsterId = d.EntityId;
            }
        }
        Assert.True(monsterId != 0UL, "脚本生成的怪没出现在快照里");
        Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null);
        // 生成那一刻的血量就是点位那份的 MaxHp：这一格同时喂战斗层的上限，
        // 所以"点位那份"必须等于内容公式算出来的那份，否则回位刷血会与血条打架。
        return mob.Ai.Hp;
    }

    [Fact]
    public void 脚本生成的怪血量按内容公式而不是点位缺省()
    {
        // 静态刷怪点走 MonsterSpawnPointBuilder（那里已经把模板属性盖进点位），
        // 而脚本生成点**不走装配** ⇒ 以前这里的形状是"两套血量权威"：AI 投影按内容把血刷满（1000），
        // 战斗层/快照读点位那份（100），玩家看得见的是血条 1000/100。
        const int templateId = 12472;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, false, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;
        config.Behavior.AggroRadius = 0f;
        config.Stats.HpBase = 1000;
        config.Stats.HpPerLevel = 0;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        var withContent = SpawnScriptedMonsterHp(templateId);
        MonsterContent.ResetToHardcoded();
        // 对照：同一个模板号没装配内容时，脚本点仍拿点位缺省那份（旧调用点逐字不变）。
        var withoutContent = SpawnScriptedMonsterHp(templateId);

        Assert.Equal(1000, withContent);
        Assert.Equal(SpawnPointConfig.Default.MaxHp, withoutContent);
    }

    /// <summary>
    /// 让默认档的怪在没有目标的情况下巡逻 1 秒，返回它这 1 秒走了多少米。
    /// <para>两档夹具只差 <c>walk_speed</c> 那一格（另一档把它写成 0＝没配），
    /// 所以读数差只能归因给"这一格到不到得了 v1"。</para>
    /// </summary>
    private static float PatrolMetersInOneSecond(int templateId, float walkSpeed)
    {
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, false, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;         // 默认档：这条必须走 v1
        config.Behavior.AggroRadius = 0f;                    // 不索敌：位移只能来自巡逻
        config.Behavior.WanderRadius = 20f;                  // 1 秒最多 4 米，不会撞到游荡边界
        config.Behavior.IdleToPatrolSeconds = 0.05f;
        config.Behavior.MoveSpeed = 4f;
        config.Behavior.WalkSpeed = walkSpeed;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;
            point.MaxHp = 1000;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家放 200 米外：只是为了给快照一个受众，不参与任何索敌。
            Assert.True(world.AddPlayer(9201, 9201, 200f, 0f, 0f, out var addReason), addReason);

            var monsterId = 0UL;
            for (var i = 0; i < 30 && monsterId == 0UL; i++)
            {
                var res = world.Tick();
                if (res.WorldSnapshot == null) continue;
                foreach (var d in res.WorldSnapshot.Deltas)
                    if (d.Identity.HasValue && d.Identity.Value.TemplateId == templateId) monsterId = d.Identity.Value.NetworkId;
            }
            Assert.True(monsterId != 0UL, "怪没 Spawn");
            Assert.True(world.TryGetMonster(monsterId, out var mob) && mob != null);
            Assert.Null(mob.Runtime);

            // 先进到 Patrol（待机转巡逻 0.05 秒），再从"整数个 tick"开始量位移。
            for (var i = 0; i < 10; i++) world.Tick();
            Assert.True(world.TryGetMonster(monsterId, out var patrolling) && patrolling != null);
            var ready = patrolling.Ai;
            Assert.Equal(MonsterAiState.Patrol, ready.State);
            ready.X = 0f; ready.SpawnX = 0f; ready.Y = 0f; ready.SpawnY = 0f;
            ready.PatrolDirection = 1f;
            patrolling.Ai = ready;

            var moved = 0f;
            for (var i = 0; i < 60; i++)
            {
                var before = patrolling.Ai.X;
                world.Tick();
                if (!world.TryGetMonster(monsterId, out var cur) || cur == null) continue;
                moved += System.Math.Abs(cur.Ai.X - before);
                patrolling = cur;
            }
            return moved;
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 巡逻速度那一格到了默认档的怪()
    {
        // v1 的无路线游荡以前直接拿 move_speed（那是"追你"的速度），v2 拿 walk_speed
        // ⇒ 种子内容 walk=2.5 / move=4 时，默认档的怪溜达比冲刺快，而编辑器那一格对它不生效。
        Assert.Equal(2.5, (double)PatrolMetersInOneSecond(12481, 2.5f), 2);
        // 对照：把那一格拿掉（0＝没配）就退回追击速度——旧内容与金轨迹靠的就是这半条。
        Assert.Equal(4.0, (double)PatrolMetersInOneSecond(12481, 0f), 2);
    }

    [Fact]
    public void 等级区间按闭区间掷而退化区间一次都不掷()
    {
        // AzerothCore `Creature::SelectLevel`（Creature.cpp:1478-1480）的三条：
        // min/max 归一化、min==max 直接用、否则 urand(min,max)（**含两端**）。
        static int Roll(int lo, int hi, int key, long tick)
            => MonsterLevelCore.Roll(lo, hi, 1, (ulong)(key * 1000 + 7), tick);

        // ① 端点都出得来，且一格都不越界（400 个键；键不同⇒结果确定，所以这条不会今天红明天绿）。
        var seen = new HashSet<int>();
        foreach (var k in Enumerable.Range(0, 400))
        {
            var lvl = Roll(6, 8, k, 500L);
            Assert.True(lvl >= 6 && lvl <= 8, $"第 {k} 个键掷出 {lvl}：越出闭区间 [6,8]");
            seen.Add(lvl);
        }
        Assert.Equal(3, seen.Count);                       // 6/7/8 都得出现，含两端
        Assert.Contains(6, seen);
        Assert.Contains(8, seen);

        // ② 写反的区间按区间用（AC 的 std::min/std::max 那一手）：同一批键给出**逐个相同**的结果。
        foreach (var k in Enumerable.Range(0, 50))
            Assert.Equal(Roll(6, 8, k, 500L), Roll(8, 6, k, 500L));

        // ③ 退化区间不掷（这就是今天全部种子内容的形状：level_base=level_min=level_max）。
        foreach (var k in Enumerable.Range(0, 50))
            Assert.Equal(7, Roll(7, 7, k, 500L));

        // ④ 同一键同一 tick ⇒ 同一个等级（确定性是回放与金轨迹的前提）。
        Assert.Equal(Roll(6, 9, 42, 500L), Roll(6, 9, 42, 500L));
        // ⑤ 都没配 ⇒ 退回基准；基准也没 ⇒ 收成 1（0 级会把让分曲线与灰名断供推进未定义区）。
        Assert.Equal(5, MonsterLevelCore.Roll(0, 0, 5, 1UL, 500L));
        Assert.Equal(1, MonsterLevelCore.Roll(0, 0, 0, 1UL, 500L));
    }

    [Fact]
    public void 模板等级区间真的落到生成出来的那只怪身上()
    {
        // 这一格以前配了没人读：区间被快照抄进运行期配置之后，等级解析只用 LevelBase。
        // 现在两个消费者（生成时的初始血、战斗层的血量上限）都按**这只怪自己的等级**从同一个投影算，
        // 所以断言的不是"某个数"，而是"**这两笔账永远相等且跟着等级走**"。
        const int templateId = 12491;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, false, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;
        config.Behavior.AggroRadius = 0f;
        config.LevelBase = 6;
        config.LevelMin = 6;
        config.LevelMax = 9;
        // 血量随等级走（每级 +20），这样"等级变了而血没变"当场可测。
        config.Stats.HpBase = 100;
        config.Stats.HpPerLevel = 20;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 4;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;
            point.MaxHp = 123;                  // 点位那份**不再**是血量权威：留个可分辨的数

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
                PlayerAttackPower = 10f, PlayerAttackRange = 50f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(9201, 9201, -5f, 0f, 0f, out var addReason), addReason);

            var ids = new List<ulong>();
            for (var s = 0; s < 4; s++) ids.Add(SpawnDirectorCore.ComputeEntityId(1, s));
            for (var i = 0; i < 40; i++) world.Tick();

            var levels = new HashSet<int>();
            foreach (var id in ids)
            {
                Assert.True(world.TryGetMonster(id, out var mob) && mob != null, $"槽位没生成：{id}");
                var level = mob!.EffectiveLevel;
                Assert.True(level is >= 6 and <= 9, $"等级 {level} 越出配的区间 [6,9]");
                // 初始血 = 按这只怪自己的等级算的那份（不是点位那份 123，也不是基准等级那份）。
                var expected = MonsterStatsCore.Compute(config.Stats, level, config.Rank,
                    config.Stats.HpOverrideBps, config.Stats.AtkOverrideBps).MaxHp;
                Assert.Equal(expected, mob.Ai.Hp);
                Assert.NotEqual(123, mob.Ai.Hp);
                // 战斗层的上限与它同数（两个消费者一个权威）。
                var probe = world.SubmitCombatAction(9201, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 9201, TargetId = id,
                });
                Assert.Equal(expected, probe.TargetMaxHp);
                levels.Add(level);
            }
            // 区间真的在掷：四只槽位 + 若干轮 tick 之后不该恒等于同一个数。
            Assert.True(levels.Count >= 2, $"四只怪的等级全一样（{string.Join(",", levels)}）：区间没在掷");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 退化等级区间与接入前逐字相同()
    {
        // 020 的种子脚本把 level_base=level_min=level_max 三格写成同一个数（第 59 行），
        // 所以"接上区间掷"这一改动对今天的内容必须是**零变化**——这条就是那条承诺的护栏。
        const int templateId = 12492;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = BuildAssistTemplate(templateId, 3, false, 0f);
        config.Behavior.Flags = MonsterAiFlags.None;
        config.Behavior.AggroRadius = 0f;
        config.LevelBase = 8;
        config.LevelMin = 8;
        config.LevelMax = 8;
        config.Stats.HpBase = 100;
        config.Stats.HpPerLevel = 20;
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 3;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 0f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(9201, 9201, -5f, 0f, 0f, out var addReason), addReason);
            for (var i = 0; i < 40; i++) world.Tick();

            var atLevel8 = MonsterStatsCore.Compute(config.Stats, 8, config.Rank,
                config.Stats.HpOverrideBps, config.Stats.AtkOverrideBps).MaxHp;
            for (var s = 0; s < 3; s++)
            {
                var id = SpawnDirectorCore.ComputeEntityId(1, s);
                Assert.True(world.TryGetMonster(id, out var mob) && mob != null);
                Assert.Equal(8, mob!.EffectiveLevel);
                Assert.Equal(atLevel8, mob.Ai.Hp);
            }
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 施法内核按时机过滤候选挂载()
    {
        // 先在最里层确认"时机过滤"本身对不对：
        // 上一条宿主用例红的时候，得能分清是内核没放行还是宿主没把这条路走到。
        var mounts = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20901, Slot = MonsterSkillSlot.NonCombat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 1200, CooldownMs = 600, MaxRange = 30f, AllowMovement = true,
            },
            new MonsterSkillMount
            {
                SkillId = 20902, Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 1200, CooldownMs = 600, MaxRange = 30f, AllowMovement = false,
            },
        };

        var idleReady = new long[mounts.Length];
        var idleSelf = new MonsterCastingSelf { ActiveMountIndex = -1 };
        var idleCast = MonsterCastingCore.Advance(
            ref idleSelf, mounts, idleReady, 7UL, 0UL, 0f, 0f, 0f, 0f, 100,
            false, false, 100L, 60, 1f / 60f);
        Assert.True(idleCast.BeginsCast, "非战斗态一次起手都没有：slot_type=1 在内核就没被放行");
        Assert.Equal(20901, idleCast.SkillId);

        var fightReady = new long[mounts.Length];
        var fightSelf = new MonsterCastingSelf { ActiveMountIndex = -1 };
        var fightCast = MonsterCastingCore.Advance(
            ref fightSelf, mounts, fightReady, 7UL, 9UL, 3f, 0f, 0f, 3f, 100,
            true, false, 300L, 60, 1f / 60f);
        Assert.True(fightCast.BeginsCast, "战斗态没起手：战斗档挂了技能却进不了候选");
        Assert.Equal(20902, fightCast.SkillId);

        // 站桩施法必须把本 tick 位移吃掉（AllowMovement=false 那条），这是玩家看得见的"读条时不动"。
        Assert.True(fightCast.ShouldHalt, "战斗挂载 AllowMovement=false 却没要求站住：读条时怪会边走边施法");
        Assert.False(idleCast.ShouldHalt, "非战斗挂载 AllowMovement=true 却被要求站住");
    }

    [Fact]
    public void 运行态在非战斗状态里会推进施法子状态机()
    {
        // 中间层探针：施法内核自己认非战斗位（上一条已证），宿主只是照着 result 发事件。
        // 这一层负责回答"组合起来到底生没生效"——以前从没跑过，只是编译过。
        var templateId = 12502;
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "运行态探针兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 0, 1, 0, 0, 0);
        behavior.IsAggressive = false;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.LeashRadius = 30f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20901, Slot = MonsterSkillSlot.NonCombat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 1200, CooldownMs = 600, MaxRange = 30f, AllowMovement = true,
            },
        };

        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, Yaw = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000,
        };
        var state = new MonsterRuntimeState();
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 1f, LeashRadius = 30f, WanderRadius = 0f, IdleToPatrolSeconds = 0f,
            AttackCooldownSeconds = 1f,
        };
        var targets = Array.Empty<MonsterAiTarget>();
        var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, 100L, 60, null);

        var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
        Assert.True(result.Cast.BeginsCast,
            "Idle 态跑满一 tick 没起手：运行态那条「补非战斗施法」的通路其实没接起来");
        Assert.Equal(20901, result.Cast.SkillId);
    }

    [Fact]
    public void 非战斗技能位在巡逻路上真的会施放而战斗位不会()
    {
        // slot_type=1（非战斗）以前是"编辑器里选得到、进游戏什么都不发生"：
        // 施法推进那处把 inCombat 写死成 true，这一档永远进不了候选。
        // 判别形态必须是"同一只怪、同一个技能、只改挂载时机"——否则红与绿分不清是槽位还是技能本身没接。
        var nonCombat = RunOutOfCombatCast(MonsterSkillSlot.NonCombat);
        Assert.True(nonCombat.starts >= 1,
            $"非战斗挂载（slot_type=1）在外面跑了 600 tick 一次起手都没有：这一档还是进不了候选");
        Assert.Equal(20901, nonCombat.skillId);
        Assert.True(nonCombat.stops >= 1,
            $"发了 {nonCombat.starts} 次起手却一次收条都没有：条子会永远挂在头顶谎报还在读条");

        // 同一份配置只把挂载时机换成"战斗"：全程没有目标，就不该有任何起手。
        var combatOnly = RunOutOfCombatCast(MonsterSkillSlot.Combat);
        Assert.Equal(0, combatOnly.starts);
        Assert.Equal(0, combatOnly.stops);
    }

    /// <summary>玩家站在旁边看着但不打它（被动怪全程不进战斗），跑 600 tick 数施法条的起手/收条。</summary>
    private static (int starts, int stops, int skillId) RunOutOfCombatCast(MonsterSkillSlot slot)
    {
        const int templateId = 12501;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "路上施法检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "lu_shang_shi_fa",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // 只开技能位：求援/威胁表都别掺进来，保证红与绿只归因到 slot_type。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 0, 1, 0, 0, 0);
        behavior.IsAggressive = false;      // 被动：没有玩家打它就永远不进战斗
        behavior.AggroRadius = 1f;
        behavior.LeashRadius = 30f;
        behavior.WalkSpeed = 2f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;         // 不许踱步：位移与"有没有施法"两件事分开判
        behavior.IdleToPatrolSeconds = 0.05f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20901,
                Slot = slot,
                ChanceBasisPoints = 10000,
                CastMs = 1200,              // 有长度才有条子（瞬发本来就不发事件）
                CooldownMs = 600,
                MaxRange = 30f,
                SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
                Interruptible = true,
                AllowMovement = true,       // 走路时给自己上 buff：不该被站桩判据吃掉
                DamageScale = 1f,
            },
        };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家就站在旁边（5 米）：施法条是按可见集合发的，看不见的人手里没有事件可对账。
            // 这只怪是被动怪（IsAggressive=false 且没人打过它），所以"在旁边看"不等于"进战斗"。
            Assert.True(world.AddPlayer(7001, 7001, 5f, 0f, 0f, out var reason), reason);

            var starts = 0;
            var stops = 0;
            var skillId = 0;
            for (var i = 0; i < 600; i++)
            {
                foreach (var hit in HarvestCastEvents(world.Tick()))
                {
                    if (hit.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart)
                    {
                        starts++;
                        skillId = hit.Event.IntValue;
                    }
                    else if (hit.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStop)
                    {
                        stops++;
                    }
                }
            }

            Assert.True(world.MonsterCount == 1, $"场上应该有 1 只怪，实得 {world.MonsterCount}");
            return (starts, stops, skillId);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 掉落掷骰逐件独立且同输入可复现()
    {
        // 绑定语义（都是玩家看得见的东西）：
        //   总闸 rollChanceBps 决定这张表要不要触发，触发之后**每件各掷一次**（WoW 观测：一件不影响另一件）
        //   条件件交给业务层判；金币按区间取；同输入必须同结果（金轨迹可复现的前提）。
        var unconditional = new MonsterLootBindingSnapshot(
            3, 10000, 1, 1, 0, 0, 0, 0,
            new[]
            {
                new MonsterDropSnapshot(50001, 10000, 1, 1),
                new MonsterDropSnapshot(50001, 10000, 1, 1),   // 同物品第二条：必须独立掷，不能共用一次骰子
                new MonsterDropSnapshot(50002, 0, 1, 1),        // 0% 的东西永远不掉
            });
        var halfGate = new MonsterLootBindingSnapshot(
            4, 5000, 1, 1, 0, 0, 10, 20,
            new[] { new MonsterDropSnapshot(50003, 10000, 2, 5) });
        var conditional = new MonsterLootBindingSnapshot(
            5, 10000, 1, 1, 1, 90001, 0, 0,
            new[] { new MonsterDropSnapshot(50004, 10000, 1, 1) });
        var unsupported = new MonsterLootBindingSnapshot(
            6, 10000, 1, 1, 2, 0, 0, 0,
            new[] { new MonsterDropSnapshot(50005, 10000, 1, 1) });
        var bindings = new[] { unconditional, halfGate, conditional, unsupported };

        var first = MonsterLootCore.Roll(bindings, 7007UL, 4242, 600L);
        var replay = MonsterLootCore.Roll(bindings, 7007UL, 4242, 600L);

        // 无条件件：两条同物品必都掉（各 100%），50002 永不掉。
        Assert.Equal(2, first.Drops.Count(d => d.ItemId == 50001));
        Assert.DoesNotContain(first.Drops, d => d.ItemId == 50002);
        Assert.DoesNotContain(first.Drops, d => d.ItemId == 50004);   // 条件件走另一条出口
        Assert.DoesNotContain(first.Drops, d => d.ItemId == 50005);   // 判不了的条件一律不给
        Assert.Single(first.ConditionalDrops);
        Assert.Equal(50004, first.ConditionalDrops[0].ItemId);
        Assert.Equal(90001, first.ConditionalDrops[0].QuestId);
        Assert.Equal(1, first.EntriesDeferredUnknownCondition);

        // 金币跟着**总闸**走：这张表配的是 50% 闸，所以单个输入的期望只能从实际门结果推，
        // 不能从参数区间 [10,20] 直接断言"一定在区间里"——门失手时整表（含钱）都不给，那是正确行为。
        Assert.Equal(4, first.BindingsRolled);
        if (first.BindingsMissedGate == 1)
        {
            Assert.Equal(0, first.Money);
            Assert.DoesNotContain(first.Drops, d => d.ItemId == 50003);
        }
        else
        {
            Assert.Equal(0, first.BindingsMissedGate);
            Assert.True(first.Money >= 10 && first.Money <= 20, "门触发时金币越界：" + first.Money);
        }

        // 可复现：同 (击杀者, 模板, tick) 重放必须逐件一致
        Assert.Equal(first.Drops.Count, replay.Drops.Count);
        for (var i = 0; i < first.Drops.Count; i++)
        {
            Assert.Equal(first.Drops[i].ItemId, replay.Drops[i].ItemId);
            Assert.Equal(first.Drops[i].Count, replay.Drops[i].Count);
        }

        // 不同击杀者应当给出**不同的**数量组合（否则掷骰根本没吃输入）：
        // 用数量区间 [2,5] 的那件做判别，跑 400 个击杀者看是否出现过至少两种数量。
        var sawTwo = new HashSet<int>();
        var moneySeen = new HashSet<int>();
        for (var k = 1UL; k < 400UL; k++)
        {
            var r = MonsterLootCore.Roll(bindings, k, 4242, 600L);
            foreach (var d in r.Drops)
            {
                if (d.ItemId == 50003) sawTwo.Add(d.Count);
            }
            moneySeen.Add(r.Money);
        }
        Assert.True(sawTwo.Count >= 2, "数量区间从来没变过：RollCount 没在吃随机输入");

        // 金币的两条出口都要在这 399 次里出现过，且出现值全在区间内或恰为 0：
        // 只断言"在区间里"会把"闸其实从没失手（= 总闸形同虚设）"判成通过。
        Assert.True(moneySeen.Contains(0), "50% 总闸在 399 次里一次都没失手：闸没在吃输入");
        var inRange = moneySeen.Where(v => v >= 10 && v <= 20).ToList();
        Assert.True(inRange.Count >= 2, "金币区间没被遍历，只见过：" + string.Join(",", inRange));
        Assert.True(moneySeen.All(v => v == 0 || (v >= 10 && v <= 20)),
                    "金币落在区间外：" + string.Join(",", moneySeen.OrderBy(v => v)));

        // 「逐件独立」的**判别夹具**：上面那两条同物品都是 100%，就算两件共用同一颗骰子也看不出差别，
        // 所以那部分断言其实钉不住盐值里的条目序号。真正能判别的形态是"同表两条各 50%"：
        // 盐里丢条目序号 ⇒ 两次掷出同一个数 ⇒ 只会出现「一起掉」或「一起不掉」，永远见不到恰好掉一条。
        var twinTable = new[]
        {
            new MonsterLootBindingSnapshot(
                7, 10000, 1, 1, 0, 0, 0, 0,
                new[]
                {
                    new MonsterDropSnapshot(60001, 5000, 1, 1),
                    new MonsterDropSnapshot(60002, 5000, 1, 1),
                })
        };
        var both = 0;
        var onlyFirst = 0;
        var onlySecond = 0;
        var neither = 0;
        for (var k = 1UL; k <= 400UL; k++)
        {
            var r = MonsterLootCore.Roll(twinTable, k, 4242, 9000L);
            var a = r.Drops.Any(d => d.ItemId == 60001);
            var b = r.Drops.Any(d => d.ItemId == 60002);
            if (a && b) both++;
            else if (a) onlyFirst++;
            else if (b) onlySecond++;
            else neither++;
        }
        Assert.True(onlyFirst > 0 && onlySecond > 0,
            $"从来没出现过「只掉一条」：两条在共用骰子（both={both} onlyFirst={onlyFirst} onlySecond={onlySecond} neither={neither}）");
        Assert.True(both > 0 && neither > 0,
            $"四条组合缺一（盐或闸没在吃输入）：both={both} onlyFirst={onlyFirst} onlySecond={onlySecond} neither={neither}");

        // 期望件数与**独立暴力参照**（另写一份蒙特卡洛）对上：
        // 只有"公式与实现互比"才会写出两份同错的式子，所以这里按定义数一遍。
        // 注意参照要数**全部**无条件件（两条必掉 1+1，加门 50% × 均量 3.5），
        // 只数区间件会拿局部实测去比全局声明，两边永远差一截。
        const int trials = 40000;
        double hits = 0.0;
        for (var t = 0; t < trials; t++)
        {
            var r = MonsterLootCore.Roll(bindings, (ulong)(t + 1) * 7919UL, 4242, t * 13L);
            foreach (var d in r.Drops) hits += d.Count;
        }
        var measured = hits / trials;
        var declared = MonsterLootCore.ExpectedItemsPerKill(bindings);
        Assert.True(Math.Abs(measured - declared) < 0.08,
                    $"期望件数与实测不符：声明 {declared:0.###} vs 实测 {measured:0.###}");
    }

    /// <summary>造一张"必触发、无条件件、每件 1 个"的掉落表；每条给独立 seq，数组序即组内序。</summary>
    private static MonsterLootBindingSnapshot LootTableOf(params (int ItemId, int ChanceBps, int GroupId)[] rows)
    {
        var entries = new MonsterDropSnapshot[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            entries[i] = new MonsterDropSnapshot(rows[i].ItemId, rows[i].ChanceBps, 1, 1, i + 1, rows[i].GroupId);
        }
        return new MonsterLootBindingSnapshot(9, 10000, 1, 1, 0, 0, 0, 0, entries);
    }

    /// <summary>跑 N 次击杀，返回每件物品的中签次数（每次击杀换一个击杀者，骰子才会换）。</summary>
    private static Dictionary<int, int> RollMany(MonsterLootBindingSnapshot binding, int trials, out int maxItemsInOneRoll, out int rollsWithNothing)
    {
        var counts = new Dictionary<int, int>();
        maxItemsInOneRoll = 0;
        rollsWithNothing = 0;
        var bindings = new[] { binding };
        for (var t = 0; t < trials; t++)
        {
            var r = MonsterLootCore.Roll(bindings, (ulong)(t + 1) * 7919UL + 11UL, 4242, t * 13L);
            if (r.Drops.Count == 0) rollsWithNothing++;
            if (r.Drops.Count > maxItemsInOneRoll) maxItemsInOneRoll = r.Drops.Count;
            foreach (var d in r.Drops)
            {
                counts.TryGetValue(d.ItemId, out var c);
                counts[d.ItemId] = c + 1;
            }
        }
        return counts;
    }

    /// <summary>造一张带逐件条件的掉落表（-1 = 这件继承绑定）。</summary>
    private static MonsterLootBindingSnapshot MixedConditionTable(int bindingConditionKind, int bindingQuestId,
        params (int ItemId, int ChanceBps, int GroupId, int ConditionKind, int QuestId)[] rows)
    {
        var entries = new MonsterDropSnapshot[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            entries[i] = new MonsterDropSnapshot(rows[i].ItemId, rows[i].ChanceBps, 1, 1, i + 1,
                rows[i].GroupId, rows[i].ConditionKind, rows[i].QuestId);
        }
        return new MonsterLootBindingSnapshot(9, 10000, 1, 1, bindingConditionKind, bindingQuestId, 0, 0, entries);
    }

    [Fact]
    public void 一张表里普通货与任务件混挂时各自走自己的出口()
    {
        // 魔兽把 QuestRequired 挂在掉落模板的**每一行**上（AzerothCore LootMgr.h:127-149），
        // 而本作以前只有绑定级：一张表要么全判任务、要么全不判，"卖肉的刀必掉而围裙只对做了任务的人掉"配不出来。
        var table = MixedConditionTable(
            0, 0,
            (83001, 10000, 0, -1, 0),      // 继承绑定 = 无条件
            (83002, 10000, 0, 1, 90001),   // 这件自己声明"需持有任务 90001"
            (83003, 10000, 0, 2, 0));      // 组队条件：内核判不了 → 走"不给"那一档并计数
        var bindings = new[] { table };

        var r = MonsterLootCore.Roll(bindings, 4242UL, 8383, 600L);
        Assert.Single(r.Drops);
        Assert.Equal(83001, r.Drops[0].ItemId);
        Assert.Single(r.ConditionalDrops);
        Assert.Equal(83002, r.ConditionalDrops[0].ItemId);
        Assert.Equal(90001, r.ConditionalDrops[0].QuestId);   // 任务 id 必须跟着这一件走，而不是绑定的那一份
        Assert.Equal(1, r.EntriesDeferredUnknownCondition);
        Assert.Equal(r.EntriesRolled, r.Accounted);

        // 反方向也要能覆盖：绑定判任务，而某一件显式声明"无条件"——它就真的无条件出账。
        var loosened = MixedConditionTable(
            1, 90002,
            (83004, 10000, 0, -1, 0),   // 继承 = 需持有任务 90002
            (83005, 10000, 0, 0, 0));   // 显式改成无条件
        var r2 = MonsterLootCore.Roll(new[] { loosened }, 4242UL, 8383, 600L);
        Assert.Single(r2.Drops);
        Assert.Equal(83005, r2.Drops[0].ItemId);
        Assert.Single(r2.ConditionalDrops);
        Assert.Equal(83004, r2.ConditionalDrops[0].ItemId);
        Assert.Equal(90002, r2.ConditionalDrops[0].QuestId);   // 条目没带任务 id 时退回绑定那一份

        // 判别夹具：若继承哨兵被当成 0（无条件），第二段的 83004 会掉进 Drops 而不是 ConditionalDrops；
        // 若条目级条件被忽略，第一段的 83002 会掉进 Drops。两个方向各盯一条腿。
    }

    [Fact]
    public void 同组里混一件任务物品时胜者按自己的条件出账()
    {
        // 组是"抽一件"，条件是"给不给"——两格独立才有"保底随机一件，其中一件是任务物品"这种配法。
        var table = MixedConditionTable(
            0, 0,
            (84001, 0, 3, 1, 90003),   // 组内等概率档，且是任务件
            (84002, 0, 3, -1, 0));     // 组内等概率档，普通货
        var bindings = new[] { table };

        var questWins = 0;
        var normalWins = 0;
        for (var t = 0; t < 600; t++)
        {
            var r = MonsterLootCore.Roll(bindings, (ulong)(t + 1) * 7919UL + 3UL, 8484, t * 13L);
            Assert.Equal(1, r.GroupsPicked);                                  // 整组必出一件
            Assert.Equal(1, r.Drops.Count + r.ConditionalDrops.Count);         // 且只出一件
            if (r.ConditionalDrops.Count == 1)
            {
                Assert.Equal(84001, r.ConditionalDrops[0].ItemId);
                Assert.Equal(90003, r.ConditionalDrops[0].QuestId);
                questWins++;
            }
            else
            {
                Assert.Equal(84002, r.Drops[0].ItemId);
                normalWins++;
            }
            Assert.Equal(r.EntriesRolled, r.Accounted);
        }
        Assert.True(questWins > 0 && normalWins > 0, $"条件没有跟着胜者走：任务件出 {questWins} 次、普通件出 {normalWins} 次");
        Assert.True(Math.Abs(questWins - normalWins) <= 60, $"两档不均分：任务件 {questWins} vs 普通件 {normalWins}");
    }

    [Fact]
    public void 条目没写条件时旧数据的出账口径逐字不变()
    {
        // 这条钉的是**默认值**：DTO/草稿/实体的 ConditionKind 缺省是 -1（继承绑定），不是 0（无条件）。
        // 写成 0 的表现：所有旧快照文件里根本没有 conditionKind 这个键，反序列化落初值 0，
        // 于是每张"任务掉落表"里的物品都变成人人可拿——库里、编辑器、文件三处都看不出区别。
        var dto = new MonsterLootBindingSnapshotDto
        {
            MonsterTemplateId = 8585, LootTableId = 1, RollChanceBps = 10000, MinCount = 1, MaxCount = 1,
            ConditionKind = 1, QuestId = 90005,
        };
        dto.Entries.Add(new MonsterDropSnapshotDto { ItemId = 85001, ChanceBps = 10000, Seq = 1 });   // 什么都不填

        Assert.Equal(-1, dto.Entries[0].ConditionKind);   // 缺省必须是继承哨兵
        var snapshot = dto.ToSnapshot();
        Assert.Equal(MonsterLootCore.ConditionInherit, snapshot.Entries[0].ConditionKind);
        var r = MonsterLootCore.Roll(new[] { snapshot }, 9UL, 8585, 600L);
        Assert.Empty(r.Drops);
        Assert.Single(r.ConditionalDrops);
        Assert.Equal(90005, r.ConditionalDrops[0].QuestId);

        // 期望件数同样不能把条件件算进去（否则"试算掉落"报的数是服务端永远发不出的量）。
        Assert.Equal(0.0, MonsterLootCore.EntryExpectedItems(snapshot, 0), 9);
        var mixed = MixedConditionTable(0, 0,
            (85002, 10000, 0, -1, 0),
            (85003, 10000, 0, 1, 90006));
        var total = MonsterLootCore.ExpectedItemsPerKill(new[] { mixed });
        Assert.True(Math.Abs(total - 1.0) < 1e-9, $"条件件被计进了期望：{total}");

        // 判别夹具：条件件与无条件件在同一张表里，期望必须恰好是后者的量。
        var allConditional = MixedConditionTable(0, 0, (85004, 10000, 0, 1, 90007));
        Assert.Equal(0.0, MonsterLootCore.ExpectedItemsPerKill(new[] { allConditional }), 9);
    }

    [Fact]
    public void 经验两个倍率经导出文件装配到运行时之后真的作用在给出的经验上()
    {
        // 这条走玩家真正经过的链：库 → export 的 JSON → DsHost 装载 → MonsterContent → 宿主算经验。
        // 中间任何一跳把倍率丢掉都不会报错：表现是"编辑器里配了三倍，进游戏还是一倍"，
        // 而库里、编辑器、快照三处的数字全都对得上。
        var dir = Path.Combine(Path.GetTempPath(), "monster_xpmod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "xp_content.json");
        try
        {
            var snapshot = new MonsterContentSnapshot { ContentRevision = 13 };
            snapshot.Templates.Add(new MonsterTemplateSnapshot
            {
                MonsterId = 8787, Name = "倍率检验兽", Rank = 0, LevelBase = 20,
                Xp = new MonsterXpSnapshot { XpBase = 45, XpPerLevel = 5, XpModBps = 25000, HealthModBps = 5000 },
            });
            File.WriteAllText(path, snapshot.ToJson());

            var json = File.ReadAllText(path);
            Assert.True(json.Contains("25000") && json.Contains("healthModBps"), "导出的 JSON 没带这两个倍率");

            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe());
            Assert.True(MonsterContent.TryGetConfig(8787, out var config) && config != null, "装载后查不到这只怪");

            var loaded = MonsterXpCore.Decompose(config!.Xp, 20, 20, MonsterRank.Normal);
            Assert.Equal(2.5, loaded.XpMod, 9);
            Assert.Equal(0.5, loaded.HealthMod, 9);

            // 与独立参照对拍（两段截断的顺序照抄），并确认净效果既不是 ×2.5 也不是 ×0.5。
            var reference = ReferenceAcpStyleXp(45, 5, 20, 20, MonsterRank.Normal, 25000, 5000);
            Assert.Equal(reference, loaded.Final);
            var plain = MonsterXpCore.Decompose(
                new MonsterXpConfig(45, 5, MonsterXpConfig.DefaultRankScale()), 20, 20, MonsterRank.Normal);
            Assert.True(loaded.Final != plain.Final, "倍率算下来与不配一样——那这两格根本没在吃输入（这里应当不同）");
            Assert.True(loaded.Final > plain.Final && loaded.Final < plain.Final * 3,
                        $"净倍率应当是 ×1.25 量级：{plain.Final} → {loaded.Final}");

            // 另一半：这两格存在之前写好的快照文件（没有这两个键）装载后必须"全不开"。
            // 缺省写成 0 的话，所有旧内容一进游戏就变成零经验，而文件本身没有任何异常。
            var legacy = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
            Assert.True(legacy.Ok, legacy.Describe());
            foreach (var templateId in MonsterContent.RegisteredIds())
            {
                if (!MonsterContent.TryGetConfig(templateId, out var old) || old == null) continue;
                Assert.True(old.Xp.XpModBps == 10000 && old.Xp.HealthModBps == 10000,
                            $"旧快照里的模板 {templateId} 装出了非缺省倍率：{old.Xp.XpModBps}/{old.Xp.HealthModBps}");
            }
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
            try { Directory.Delete(dir, true); } catch (Exception) { /* 临时目录清不掉不影响判据 */ }
        }
    }

    /// <summary>按魔兽原文手工算一遍（独立参照：先在 xpMod 之后取整一次，再乘 ModHealth 并**截断**）。</summary>
    private static long ReferenceAcpStyleXp(int xpBase, int xpPerLevel, int mobLevel, int playerLevel, MonsterRank rank,
                                            int xpModBps, int healthModBps)
    {
        // 参照自己算让分：三段表从这里"另写一遍"，不复用内核的私有函数。
        var concession = MonsterDifficultyCore.ConcessionFactor(mobLevel, playerLevel, rank);
        double[] scale = { 1.0, 2.0, 2.5, 4.0, 4.0 };
        var baseXp = (double)xpBase + (double)xpPerLevel * Math.Max(1, mobLevel);
        var mod = xpModBps < 0 ? 1.0 : xpModBps / 10000.0;
        var first = (long)Math.Round(baseXp * concession * scale[(int)rank] * mod, MidpointRounding.AwayFromZero);
        // AC：`if (ModHealth <= 0.75f && >= 0.0f) _xp = uint32(_xp * ModHealth);`  —— 第二段是截断。
        if (healthModBps >= 0 && healthModBps <= 7500) return (long)(first * (healthModBps / 10000.0));
        return first;
    }

    [Fact]
    public void 逐模板经验倍率与血薄削减按魔兽的两段截断生效()
    {
        var scale = MonsterXpConfig.DefaultRankScale();

        // ① ModExperience 在同一次取整之前乘进去：×3 就是把整条曲线放大三倍（不是加三倍）。
        var plain = new MonsterXpConfig(45, 5, (double[])scale.Clone());
        var tripled = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 30000);
        for (var mob = 10; mob <= 30; mob += 7)
        {
            var a = MonsterXpCore.Compute(plain, mob, mob, MonsterRank.Normal);
            var b = MonsterXpCore.Compute(tripled, mob, mob, MonsterRank.Normal);
            Assert.True(Math.Abs(b - a * 3) <= 1, $"×3 倍率没作用：{a} → {b}（允许 ±1 的取整差）");
        }

        // ② "血薄削减"作用在**已经取整**的那个数上，而且是截断：拿一个奇数当一级结果，
        //    四舍五入与截断就会分岔——这正是两段顺序可被观测的地方。
        var halved = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 5000);
        var oddSeen = 0;
        for (var mob = 1; mob <= 60; mob++)
        {
            var firstStage = MonsterXpCore.Compute(plain, mob, mob, MonsterRank.Normal);
            var withHealth = MonsterXpCore.Compute(halved, mob, mob, MonsterRank.Normal);
            Assert.Equal((long)(firstStage * 0.5), withHealth);     // 截断，不是 round
            if (firstStage % 2 == 1) oddSeen++;
        }
        Assert.True(oddSeen > 0, "这条夹具里从来没有出现过程序数的一档：那截断与四舍五入无法区分，判据在空转");

        // ③ 魔兽那条只有 0..7500 生效：9000 与不填一样，15000 也不会放大。
        var noop9000 = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 9000);
        var boost = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 15000);
        for (var mob = 5; mob <= 40; mob += 5)
        {
            Assert.Equal(MonsterXpCore.Compute(plain, mob, mob, MonsterRank.Normal),
                         MonsterXpCore.Compute(noop9000, mob, mob, MonsterRank.Normal));
            Assert.Equal(MonsterXpCore.Compute(plain, mob, mob, MonsterRank.Normal),
                         MonsterXpCore.Compute(boost, mob, mob, MonsterRank.Normal));
        }
        // 填 0 是真的归零（不是"没配"）。
        Assert.Equal(0, MonsterXpCore.Compute(new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 0), 20, 20, MonsterRank.Normal));
        // 负数是坏值：按没配处理，绝不让经验归零。
        Assert.Equal(MonsterXpCore.Compute(plain, 20, 20, MonsterRank.Normal),
                     MonsterXpCore.Compute(new MonsterXpConfig(45, 5, (double[])scale.Clone(), -1, -1), 20, 20, MonsterRank.Normal));

        // ④ 独立参照对拍：三段等级 × 四种倍率组合，逐个等值。
        var combos = new (int XpMod, int HealthMod)[] { (10000, 10000), (25000, 10000), (10000, 3000), (15000, 7500), (10000, 7501), (4000, 6000) };
        foreach (var (xpMod, healthMod) in combos)
        {
            var cfg = new MonsterXpConfig(45, 5, (double[])scale.Clone(), xpMod, healthMod);
            foreach (var rank in new[] { MonsterRank.Normal, MonsterRank.Elite, MonsterRank.Boss })
            {
                for (var player = 12; player <= 60; player += 6)
                {
                    for (var delta = -10; delta <= 6; delta += 4)
                    {
                        var mob = Math.Max(1, player + delta);
                        var kernel = MonsterXpCore.Compute(cfg, mob, player, rank);
                        var reference = ReferenceAcpStyleXp(45, 5, mob, player, rank, xpMod, healthMod);
                        Assert.True(kernel == reference,
                                    $"内核 {kernel} vs 参照 {reference}（xpMod={xpMod} healthMod={healthMod} rank={rank} 怪{mob} 人{player}）");
                    }
                }
            }
        }

        // ⑤ 缺省值必须与"这两格存在之前"逐字相同（既有金轨迹与全部种子怪都靠这条兜着）。
        foreach (var rank in new[] { MonsterRank.Normal, MonsterRank.RareElite, MonsterRank.Elite, MonsterRank.Boss, MonsterRank.WorldBoss })
        {
            for (var mob = 1; mob <= 62; mob += 3)
            {
                Assert.Equal(MonsterXpCore.Compute(plain, mob, mob + 2, rank),
                             MonsterXpCore.Compute(new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 10000), mob, mob + 2, rank));
            }
        }

        // ⑥ 分解里两个因子看得见（编辑器"为什么是这个数"要能解释），且 1.0 有两种含义时看原始基点。
        var bd = MonsterXpCore.Decompose(new MonsterXpConfig(45, 5, (double[])scale.Clone(), 10000, 9000), 20, 20, MonsterRank.Normal);
        Assert.Equal(1.0, bd.XpMod, 9);
        Assert.Equal(1.0, bd.HealthMod, 9);   // 9000 落在那段"不生效"里——想知道是哪种，请读配置本身
    }

    [Fact]
    public void 组队池子同样吃这两个倍率而分池规则不变()
    {
        // 倍率作用在**每个人的算法**里（AC 就是在 _InitXP 里乘），所以池子、比例、灰名断供、
        // 有人灰名减半这几条必须原样成立——只放大，不改形状。
        var scale = MonsterXpConfig.DefaultRankScale();
        var plain = new MonsterXpConfig(45, 5, (double[])scale.Clone());
        var modified = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 20000, 5000);   // ×2 再截半 = 净 ×1（但中间截两次）

        var participants = new[]
        {
            new MonsterXpParticipant { CharacterId = 11, Level = 20, LastContributionTick = 100 },
            new MonsterXpParticipant { CharacterId = 12, Level = 20, LastContributionTick = 100 },
            new MonsterXpParticipant { CharacterId = 13, Level = 20, LastContributionTick = 100 },
        };
        var bufA = new long[MonsterXpCore.MaxParticipants];
        var bufB = new long[MonsterXpCore.MaxParticipants];
        MonsterXpCore.ComputeGroupShares(plain, 20, MonsterRank.Normal, participants, bufA, out var receiptA);
        MonsterXpCore.ComputeGroupShares(modified, 20, MonsterRank.Normal, participants, bufB, out var receiptB);

        Assert.Equal(receiptA.Count, receiptB.Count);
        Assert.Equal(receiptA.Rate, receiptB.Rate, 9);          // 人数加成与倍率无关
        Assert.Equal(receiptA.GrayExcluded, receiptB.GrayExcluded);
        // 倍率 20000 与 5000 合成净 ×1，但中间取整两次 ⇒ 允许 1 点之差，绝不允许差一半或差三倍。
        for (var i = 0; i < receiptA.Count; i++)
        {
            Assert.True(Math.Abs(bufA[i] - bufB[i]) <= 2, $"第 {i} 人：{bufA[i]} vs {bufB[i]}（两段截断不该改出这个量级）");
        }

        // 真放大三倍时，总额必须跟着走（比例不变）。
        var triple = new MonsterXpConfig(45, 5, (double[])scale.Clone(), 30000);
        var bufC = new long[MonsterXpCore.MaxParticipants];
        MonsterXpCore.ComputeGroupShares(triple, 20, MonsterRank.Normal, participants, bufC, out var receiptC);
        Assert.Equal(3.0, receiptC.TotalGranted / (double)Math.Max(1, receiptA.TotalGranted), 0.05);
        Assert.Equal(receiptA.PoolLevel, receiptC.PoolLevel);   // 定基等级不受倍率影响
    }

    /// <summary>造一张带表级件数区间的绑定（组内条目用 (件,概率,组,条件,任务)）。</summary>
    private static MonsterLootBindingSnapshot TableWithRounds(int minCount, int maxCount,
        params (int ItemId, int ChanceBps, int GroupId, int ConditionKind, int QuestId)[] rows)
    {
        var entries = new MonsterDropSnapshot[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            entries[i] = new MonsterDropSnapshot(rows[i].ItemId, rows[i].ChanceBps, 1, 1, i + 1,
                rows[i].GroupId, rows[i].ConditionKind, rows[i].QuestId);
        }
        return new MonsterLootBindingSnapshot(9, 10000, minCount, maxCount, 0, 0, 0, 0, entries);
    }

    [Fact]
    public void 表级件数区间让互斥组重掷而不管逐件独立掷()
    {
        // 绑定的 min_count/max_count（"这张表抽几件"）以前从编辑器一路到快照都没人读——
        // 现在它接的是 AC 的组重掷（LootGroup::Process 的 nonRefIterationsLeft）。
        // 关键的另一半：它**不许**改变组 0 的逐件独立掷，否则 D4 那条口径就被换了。
        var binding = TableWithRounds(1, 3,
            (88001, 5000, 1, -1, 0),
            (80002, 5000, 1, -1, 0),
            (88003, 10000, 0, -1, 0));      // 组外那件：100% 必掉，一次击杀里只可能出一次
        var bindings = new[] { binding };

        var perKill = new Dictionary<int, int>();
        var groupedSeen = new Dictionary<int, int>();
        var roundsRolled = 0;
        var groupsSeen = 0;
        var identityOk = true;
        var instanceOk = true;
        for (var t = 0; t < 4000; t++)
        {
            var r = MonsterLootCore.Roll(bindings, (ulong)(t + 1) * 7919UL + 13UL, 8888, t * 13L);
            roundsRolled += r.GroupsRolled;
            groupsSeen += 1;
            // 条目去重的那条恒等式与"实例 = 中签条目 + 重掷多给的件"必须同时成立。
            identityOk &= r.EntriesRolled == r.Accounted;
            instanceOk &= r.Drops.Count == r.EntriesGranted + r.GroupRepeatWins;
            var grouped = r.Drops.Count(d => d.ItemId == 88001 || d.ItemId == 80002);
            perKill[grouped] = perKill.TryGetValue(grouped, out var c) ? c + 1 : 1;
            foreach (var d in r.Drops)
            {
                if (d.ItemId != 88001 && d.ItemId != 80002) continue;
                groupedSeen[d.ItemId] = groupedSeen.TryGetValue(d.ItemId, out var g) ? g + 1 : 1;
            }
            // 组外那件：无论表级区间配成几件，都只有一份——重掷只作用于组。
            Assert.Equal(1, r.Drops.Count(d => d.ItemId == 88003));
            // 同一条目被重掷再抽中时，它掉的是"多件"，但条目去重仍只算一次
            Assert.True(grouped <= 3, $"一轮表最多 3 件，实测 {grouped}");
        }
        Assert.True(identityOk, "条目对账恒等式在重掷形态下不成立");
        Assert.True(instanceOk, "件数 ≠ 中签条目 + 重掷多给的件：多给的那部分没被单独数出来");
        Assert.True(perKill.ContainsKey(1) && perKill.ContainsKey(2) && perKill.ContainsKey(3),
                    "表级件数区间没有真的变化件数：" + string.Join(",", perKill.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.True(groupedSeen[88001] > 0 && groupedSeen[80002] > 0, "重掷里两档从不轮流：说明每轮用的是同一颗骰子");
        // ExtraGroupRoundsRolled = GroupsRolled - 组数
        Assert.True(roundsRolled > groupsSeen, $"表级区间配了 1..3 而组轮数只有 {roundsRolled}：重掷没发生");

        // 判别夹具：把区间退回 1..1，件数必须恒等于 1（说明"多件"真来自这两格，不是别处漏了限额）。
        var single = TableWithRounds(1, 1,
            (88001, 5000, 1, -1, 0), (80002, 5000, 1, -1, 0), (88003, 10000, 0, -1, 0));
        for (var t = 0; t < 400; t++)
        {
            var r = MonsterLootCore.Roll(new[] { single }, (ulong)(t + 1) * 7919UL + 13UL, 8888, t * 13L);
            Assert.Equal(1, r.Drops.Count(d => d.ItemId == 88001 || d.ItemId == 80002));
            Assert.Equal(0, r.ExtraGroupRoundsRolled);
            Assert.Equal(0, r.GroupRepeatWins);
        }
    }

    [Fact]
    public void 抽中任务件的组不再继续重掷()
    {
        // AC：<c>if (nonRefIterationsLeft > 1 && !item->needs_quest) Process(...)</c>——
        // 任务物品是"给做了任务的人一份"，不是"给三份"。这条停止规则必须可观测。
        var binding = TableWithRounds(3, 3,
            (89001, 5000, 1, 1, 90001),    // 任务件
            (89002, 5000, 1, -1, 0));      // 普通件
        var bindings = new[] { binding };

        var stopSeen = 0;
        var questFirstThenStopped = 0;
        var allThreeNormal = 0;
        var identityOk = true;
        for (var t = 0; t < 3000; t++)
        {
            var r = MonsterLootCore.Roll(bindings, (ulong)(t + 1) * 6151UL + 7UL, 8989, t * 7L);
            identityOk &= r.EntriesRolled == r.Accounted;
            var questWins = r.ConditionalDrops.Count;
            var normalWins = r.Drops.Count(d => d.ItemId == 89002);
            if (r.GroupQuestItemStopsMultiplying > 0)
            {
                stopSeen++;
                // 停止只可能发生在"这一轮抽中任务件"之后：它之前可以有普通件，之后一件都不许有。
                Assert.Equal(1, questWins);
                if (normalWins == 0) questFirstThenStopped++;
            }
            else if (normalWins == 3) allThreeNormal++;
            Assert.True(questWins + normalWins <= 3, $"重掷没被任务件止住：{questWins}+{normalWins}");
        }
        Assert.True(identityOk, "任务件停止那一路把对账走坏了");
        Assert.True(stopSeen > 0, "任务件一次都没止住重掷：那条判据是死的");
        Assert.True(questFirstThenStopped > 0, "从没出现\"第一轮就抽中任务件\"这一档，停止规则没被真正观察到");
        Assert.True(allThreeNormal > 0, "普通件连中三轮从没出现：轮次本身没在跑");
        // 三轮里止住的比例 = 任务件在第 1 轮或第 2 轮中签 = 0.5 + 0.25 = 0.75（第 3 轮中签也已无轮可止）
        Assert.True(Math.Abs(stopSeen / 3000.0 - 0.75) < 0.03,
                    $"止住重掷的比例 {stopSeen / 3000.0:0.###}，应为 0.75");
    }

    [Fact]
    public void 重掷轮数与任务件停止都算进期望件数()
    {
        // 试算与运行时必须同数：表级区间(1..3)和"任务件停止"两条都会改期望，
        // 少算任何一条，编辑器就会报一个服务端永远达不到的数字。
        var plain = TableWithRounds(1, 3, (81001, 5000, 1, -1, 0), (81002, 5000, 1, -1, 0));
        var declaredPlain = MonsterLootCore.ExpectedItemsPerKill(new[] { plain });
        Assert.True(Math.Abs(declaredPlain - 2.0) < 1e-9,
                    $"1..3 轮、每轮必出一件的表，期望应是 E[轮数]=2，实得 {declaredPlain:0.####}");

        // 任务件那份：3 轮固定、单轮 50% 抽中任务件 ⇒ 有效轮数期望 = 1 + 0.5 + 0.25 = 1.75。
        // 而"每次击杀期望件数"这个对外口径**故意只算无条件件**（条件件要业务层判），
        // 所以声明值是 0.875（普通件那一半），不是 1.75。两个数各测各的，都要钉住。
        var withQuest = TableWithRounds(3, 3, (81003, 5000, 1, 1, 90001), (81004, 5000, 1, -1, 0));
        var declaredUnconditional = MonsterLootCore.ExpectedItemsPerKill(new[] { withQuest });
        Assert.True(Math.Abs(declaredUnconditional - 0.875) < 1e-9,
                    $"无条件件期望应只含普通件那半（0.875），实得 {declaredUnconditional:0.####}");
        Assert.Equal(0.0, MonsterLootCore.EntryExpectedItems(withQuest, 0), 9);   // 任务件不进期望
        Assert.True(Math.Abs(MonsterLootCore.EntryExpectedItems(withQuest, 1) - 0.875) < 1e-9,
                    $"普通件期望 {MonsterLootCore.EntryExpectedItems(withQuest, 1):0.####}，应为 0.875");
        // 有效轮数因子单独可问（校验器/预览都靠它，而不是各自猜一个倍数）
        Assert.True(Math.Abs(MonsterLootCore.ExpectedRoundsForGroup(withQuest, MonsterLootCore.GroupStopChanceBps(withQuest, 1)) - 1.75) < 1e-9,
                    "轮数因子没把任务件停止算进去");

        // 独立参照：另写一份过程模拟（不用内核的骰子），跑 20 万次取"含任务件的总件数"。
        const int simTrials = 200000;
        var rng = new Random(20261026);
        long simItems = 0;
        for (var t = 0; t < simTrials; t++)
        {
            for (var r = 0; r < 3; r++)
            {
                simItems++;
                if (rng.NextDouble() < 0.5) break;    // 抽中任务件 ⇒ 停止重掷
            }
        }
        var simulated = simItems / (double)simTrials;
        Assert.True(Math.Abs(simulated - 1.75) < 0.03,
                    $"独立模拟给出的总件数 {simulated:0.###} 与解析值 1.75 不符（参照自己算错了）");

        // 真实掷骰的总件数（含条件件出口）要与解析值对上——这才是"运行时到底给几件"。
        const int n = 30000;
        long rolled = 0;
        for (var t = 0; t < n; t++)
        {
            var r = MonsterLootCore.Roll(new[] { withQuest }, (ulong)(t + 1) * 7919UL + 3UL, 8181, t * 17L);
            rolled += r.Drops.Count + r.ConditionalDrops.Count;
        }
        var measuredTotal = rolled / (double)n;
        Assert.True(Math.Abs(measuredTotal - 1.75) < 0.05,
                    $"掷骰实测总件数 {measuredTotal:0.###} vs 解析 1.75（模拟值 {simulated:0.###}）");

        // 判别夹具之一：组 0 的表**不该**被轮数放大（否则这条期望就成了"一切都乘轮数"）。
        var ungrouped = TableWithRounds(1, 3, (81005, 10000, 0, -1, 0));
        Assert.True(Math.Abs(MonsterLootCore.ExpectedItemsPerKill(new[] { ungrouped }) - 1.0) < 1e-9,
                    "组外的必掉件被表级轮数放大了：那会改掉 D4 的逐件独立口径");

        // 判别夹具之二：把区间退回 1..1，期望必须回到 1.0（说明放大真的来自那两格）。
        var oneRound = TableWithRounds(1, 1, (81006, 5000, 1, -1, 0), (81007, 5000, 1, -1, 0));
        Assert.True(Math.Abs(MonsterLootCore.ExpectedItemsPerKill(new[] { oneRound }) - 1.0) < 1e-9,
                    "min=max=1 的表期望不是 1 件：轮数因子被无端乘上了");

        // 与真实掷骰对拍（无条件那份表，声明值 2.0）
        long got = 0;
        for (var t = 0; t < n; t++)
        {
            var r = MonsterLootCore.Roll(new[] { plain }, (ulong)(t + 1) * 7919UL + 5UL, 8181, t * 19L);
            got += r.Drops.Count;
        }
        Assert.True(Math.Abs(got / (double)n - declaredPlain) < 0.05,
                    $"声明 {declaredPlain:0.###} vs 实测 {got / (double)n:0.###}");
    }

    [Fact]
    public void 同组互斥整组最多出一件而各件按填写概率分摊()
    {
        // 魔兽的 creature_loot_template 用 GroupId 把若干行捆成互斥桶（AzerothCore LootGroup::Roll，
        // LootMgr.cpp:1292-1327）：一次掷骰、累减法、只有把余额减成负数的那个人中签。
        // 旧模型只有"逐件独立"，于是"从这三件里出至多一件"根本写不出来——
        // 三条 20%/30%/10% 独立掷会一次掉出两三件，期望也变成 60% 而不是"至多一件"。
        const int trials = 40000;
        var table = LootTableOf(
            (70001, 2000, 1),
            (70002, 3000, 1),
            (70003, 1000, 1));

        var counts = RollMany(table, trials, out var maxItems, out var emptyRolls);

        Assert.True(maxItems <= 1, $"同组出了 {maxItems} 件——互斥没生效，这就是一堆独立掷");
        Assert.True(emptyRolls > 0, "组一次都没空手：显式档加起来 60% 却必出，说明余额被当成了必中");

        // 分摊比例必须是填写的概率本身（不是"归一化后抽一件"）：20/30/10 剩 40% 不掉。
        var expected = new Dictionary<int, int> { { 70001, 2000 }, { 70002, 3000 }, { 70003, 1000 } };
        foreach (var kv in expected)
        {
            var share = counts.TryGetValue(kv.Key, out var got) ? got : 0;
            var measuredBps = share * 10000.0 / trials;
            Assert.True(Math.Abs(measuredBps - kv.Value) < 200.0,
                $"物品 {kv.Key} 的中签率实测 {measuredBps:0.#}bps vs 填写 {kv.Value}bps");
        }

        // 判别夹具：归一化实现（把 60% 当成 100% 来抽）会在同一份数据上给出 33%/50%/17%，
        // 上面三条断言都会红；而"组内再各掷一次"会让 maxItems==2 直接红。
        // 空手率也要量：40% 的余额必须真的不掉，否则说明多算了一次必中。
        var emptyBps = emptyRolls * 10000.0 / trials;
        Assert.True(Math.Abs(emptyBps - 4000.0) < 200.0, $"组空手率实测 {emptyBps:0.#}bps，应为余额 4000bps");
    }

    [Fact]
    public void 组内概率填0不是永不掉而是平分显式档没吃到的余额()
    {
        // 同一个 0 在组外是"这件永不掉"，在组内是"平分剩下的"（AC 把组内成员拆成
        // ExplicitlyChanced / EqualChanced 两档，LootMgr.cpp:1283-1289）。
        // 这条差异必须可观测，否则策划按组外口径理解，会把"保底随机一件"配成"大概率什么都不掉"。
        const int trials = 40000;
        var table = LootTableOf(
            (71001, 2500, 2),      // 显式档 25%
            (71002, 0, 2),         // 等概率档 A：拿 (100-25)/2 = 37.5%
            (71003, 0, 2));        // 等概率档 B：同上

        var counts = RollMany(table, trials, out var maxItems, out var emptyRolls);

        Assert.True(maxItems <= 1, "等概率档和显式档一起中了：两档不是「至多一件」的关系");
        Assert.Equal(0, emptyRolls);   // 有等概率档兜底 ⇒ 组永远出一件（"必出 1 件"就是靠这个写的）

        var frontWins2 = counts.TryGetValue(71001, out var v1) ? v1 : 0;
        Assert.True(Math.Abs(frontWins2 * 10000.0 / trials - 2500.0) < 200.0,
                    $"显式档实测 {frontWins2 * 10000.0 / trials:0.#}bps，应为 2500");
        // 两条等概率档各拿余额的一半，且彼此应当接近（同一档内平分是"等概率"三个字的含义）
        var a = (counts.TryGetValue(71002, out var v2) ? v2 : 0) * 10000.0 / trials;
        var b = (counts.TryGetValue(71003, out var v3) ? v3 : 0) * 10000.0 / trials;
        Assert.True(Math.Abs(a - 3750.0) < 200.0, $"等概率档 A 实测 {a:0.#}bps，应为余额的一半 3750");
        Assert.True(Math.Abs(a - b) < 200.0, $"两档不等概率：A {a:0.#} vs B {b:0.#}");
    }

    [Fact]
    public void 组里放一件满概率会把排在它后面的成员整个挡死()
    {
        // AC 的口径：组内遇到 chance>=100% 的成员立刻返回它，后面的成员连减法都轮不到
        // （LootMgr.cpp:1309 的 `if (chance >= 100.0f) return item;`）。
        // 这条行为没人复现的话，"我给它配了 100% 却没影响别人"就成了查不出来的谜。
        var table = LootTableOf(
            (72001, 1000, 3),
            (72002, 10000, 3),   // 满概率：轮到他就直接中奖
            (72003, 5000, 3));   // 排在其后 ⇒ 永远轮不到

        var counts = RollMany(table, 3000, out var maxItems, out var emptyRolls);

        Assert.Equal(1, maxItems);                     // 每次都恰好出一件（组里必有一档中签）
        Assert.Equal(0, emptyRolls);
        Assert.False(counts.ContainsKey(72003), "被满概率档挡在身后的成员竟然掉过东西");
        // 前排 10% 仍然轮得到自己，满概率档吃剩下 90%（"100% 就独占整组"是另一种常见误写）
        var frontWins = counts.TryGetValue(72001, out var f) ? f : 0;
        Assert.True(Math.Abs(frontWins - 300.0) < 60.0, $"前排 10% 实测 {frontWins} 次 / 3000，应约 300");

        // 计数器侧同样要看得见"挡住了几个人"：否则配置错了也没回执。
        // 关系断言：只有满概率档中签的那一次才会挡死后排，所以 shadowed 总数 == 满概率档的中签次数。
        var shadowedTotal = 0;
        var identityHeld = true;
        for (var t = 0; t < 3000; t++)
        {
            var receipt = MonsterLootCore.Roll(new[] { table }, (ulong)(t + 1) * 7919UL + 11UL, 4242, t * 13L);
            shadowedTotal += receipt.GroupShadowedByGuaranteed;
            identityHeld &= receipt.EntriesRolled == receipt.Accounted;
            Assert.Equal(1, receipt.GroupsRolled);
            Assert.Equal(1, receipt.GroupsPicked);
            Assert.Equal(3, receipt.GroupEntriesRolled);
        }
        var middleWins = counts.TryGetValue(72002, out var m) ? m : 0;
        Assert.Equal(middleWins, shadowedTotal);
        Assert.True(identityHeld, "对账恒等式在这组输入里不成立");
    }

    [Fact]
    public void 组内总概率超出一百时后排成员只拿前面吃剩的预算()
    {
        // 累计减法的直接后果：60% + 60% 不是"各 60% 可能一起掉"，也不是"各 50% 归一化"，
        // 而是第一个拿 60%、第二个拿剩下的 40%。魔兽把这当成配错并打日志（LootMgr.cpp:1497-1509），
        // 但行为照做——所以内核必须与它一致，编辑器则要提示而不是假装没事。
        const int trials = 40000;
        var table = LootTableOf(
            (73001, 6000, 4),
            (73002, 6000, 4));

        var counts = RollMany(table, trials, out var maxItems, out var emptyRolls);

        Assert.True(maxItems <= 1, "组里出了两件：互斥失效");
        Assert.Equal(0, emptyRolls);   // 两档合计 120% ⇒ 必有一档中签
        var firstBps = (counts.TryGetValue(73001, out var v1) ? v1 : 0) * 10000.0 / trials;
        var secondBps = (counts.TryGetValue(73002, out var v2) ? v2 : 0) * 10000.0 / trials;
        Assert.True(Math.Abs(firstBps - 6000.0) < 200.0, $"前排实测 {firstBps:0.#}bps，应为 6000");
        Assert.True(Math.Abs(secondBps - 4000.0) < 200.0, $"后排实测 {secondBps:0.#}bps，应为吃剩的 4000（归一化实现会给 5000）");

        // 声明侧（期望公式）必须给出同一组数字，否则预览与运行时两套口径。
        var declared = MonsterLootCore.EntryWinChanceBps(table, 0);
        var declaredSecond = MonsterLootCore.EntryWinChanceBps(table, 1);
        Assert.True(Math.Abs(declared - 6000.0) < 1.0, $"期望式前排 {declared:0.#} vs 6000");
        Assert.True(Math.Abs(declaredSecond - 4000.0) < 1.0, $"期望式后排 {declaredSecond:0.#} vs 4000");
    }

    [Fact]
    public void 组的期望件数不会把互斥件加成好几件()
    {
        // 这是"编辑器试算掉落"与"服务端实际掉落"最容易分叉的一处：
        // 逐件概率相加对组 0 是对的，对互斥组会系统性偏高（下面这张表实际每次只出 1 件）。
        // 旧实现命令行自己抄了一遍相加的式子，加组之后那行数字就会自报一个服务端达不到的值。
        var table = LootTableOf(
            (74001, 10000, 5), (74002, 10000, 5), (74003, 10000, 5),
            (74004, 10000, 5), (74005, 10000, 5));

        var bindings = new[] { table };
        var declared = MonsterLootCore.ExpectedItemsPerKill(bindings);
        Assert.True(Math.Abs(declared - 1.0) < 0.0001,
                    $"五件同组满概率的期望件数是 {declared:0.###}，应为 1.0（互斥组每次最多一件）");

        // 逐件相加=5 的那份旧算式必须在这里红：用它做参照，断言的不是数值而是"两者不同"。
        double naive = 0.0;
        for (var e = 0; e < table.Entries.Length; e++)
        {
            var raw = table.Entries[e].ChanceBps;
            naive += Math.Min(10000, Math.Max(0, raw)) / 10000.0;
        }
        Assert.True(Math.Abs(naive - 5.0) < 0.0001, $"参照的逐件相加算式本身不对：{naive}");
        Assert.True(declared < naive / 2.0, "组期望没有折算互斥，仍然在逐件相加");

        // 期望必须与实测对上（独立暴力参照：数一遍真实掉落）。
        const int trials = 20000;
        var counts = RollMany(table, trials, out var maxItems, out var emptyRolls);
        Assert.Equal(1, maxItems);
        Assert.Equal(0, emptyRolls);
        var measured = counts.Values.Sum() / (double)trials;
        Assert.True(Math.Abs(measured - declared) < 0.02, $"声明 {declared:0.###} vs 实测 {measured:0.###}");
    }

    [Fact]
    public void 负数组号按独立掷处理而不会被丢成一个谁也不掉的孤儿()
    {
        // GroupId 是库里的 int，手滑写 -1 是可能的。若按"-1 也是一个组"处理，
        // 它会和别的 -1 互相排斥；若当非法值丢弃，这件就永远不掉。
        // 口径：负数 = 0 = 逐件独立掷，且必须用到与"写成 0"完全相同的那颗骰子。
        var negative = LootTableOf((75001, 5000, -1), (75002, 5000, 0));
        var zeroed = LootTableOf((75001, 5000, 0), (75002, 5000, 0));

        for (var k = 1UL; k <= 200UL; k++)
        {
            var a = MonsterLootCore.Roll(new[] { negative }, k, 4242, 6100L);
            var b = MonsterLootCore.Roll(new[] { zeroed }, k, 4242, 6100L);
            Assert.Equal(a.Drops.Count, b.Drops.Count);
            Assert.Equal(0, a.GroupsRolled);   // 没有任何一条被划进组通路
        }

        // 判别夹具：两条各 50% 的独立掷必须出现过"只掉一条"，否则它们其实在同一条组里互斥。
        var counts = RollMany(negative, 20000, out var maxItems, out _);
        Assert.True(maxItems == 2, $"两条 50% 的独立掷在 2 万次里从没一起掉过（最多 {maxItems} 件）——那是互斥，不是独立");
        Assert.True(counts.TryGetValue(75001, out var x) && x > 0);
        Assert.True(counts.TryGetValue(75002, out var y) && y > 0);
    }

    [Fact]
    public void 掉落每条出口都有计数且对账恒等式在全部形态下成立()
    {
        // Accounted == EntriesRolled 是"掉了但没发"与"什么都没掉"能否区分开的唯一凭据。
        // 少一条出口（比如命中了却物品 id 非法）就会让账目对不平，而日志上一切正常。
        var mixed = LootTableOf(
            (0, 10000, 0),        // 命中但 id 非法 → EntriesSkippedNoItem
            (76002, 0, 0),        // 独立掷落空 → EntriesMissedChance
            (76003, 10000, 7),    // 组内胜者 → Drops
            (76004, 10000, 7));   // 组内被挡的落败者 → GroupEntriesLost

        var r = MonsterLootCore.Roll(new[] { mixed }, 424242UL, 4242, 600L);

        Assert.Equal(r.EntriesRolled, r.Accounted);
        Assert.Equal(4, r.EntriesRolled);
        Assert.Equal(1, r.EntriesSkippedNoItem);
        Assert.Equal(1, r.EntriesMissedChance);
        Assert.Equal(1, r.GroupsRolled);
        Assert.Equal(2, r.GroupEntriesRolled);
        Assert.Equal(1, r.GroupsPicked);
        Assert.Equal(1, r.GroupEntriesLost);
        Assert.Single(r.Drops);
        Assert.Equal(76003, r.Drops[0].ItemId);   // 组内按数组序累减，第一条先把余额减成负数

        // 组空手那条出口也要有人数：一张全是"小概率显式档"的组必然有落空的时候。
        var sparse = LootTableOf((77001, 100, 8));
        var emptySeen = 0;
        var identityHeld = true;
        for (var t = 0; t < 400; t++)
        {
            var s = MonsterLootCore.Roll(new[] { sparse }, (ulong)(t + 1), 4242, t * 7L);
            if (s.GroupsRolledEmpty > 0) emptySeen++;
            identityHeld &= s.EntriesRolled == s.Accounted;
        }
        Assert.True(emptySeen > 0, "组落空这条出口一次都没走过：要么概率被当成必中，要么计数器没接上");
        Assert.True(identityHeld, "对账恒等式在某些输入下不成立");

        // 条件件与判不了的条件也要各自有账（这两条走的是 ConditionalDrops / Deferred）。
        var conditional = new MonsterLootBindingSnapshot(9, 10000, 1, 1, 1, 90001, 0, 0,
            new[] { new MonsterDropSnapshot(78001, 10000, 1, 1, 1, 9) });   // 组 9 里唯一的成员
        var c = MonsterLootCore.Roll(new[] { conditional }, 5UL, 4242, 6100L);
        Assert.Single(c.ConditionalDrops);
        Assert.Equal(c.EntriesRolled, c.Accounted);
    }

    [Fact]
    public void 掉落试算的期望走内核而不是逐件相加()
    {
        // 命令行 preview-drop 以前自己抄了一遍"总闸 × 每件概率 × 平均数量，逐件相加"。
        // 那对 group_id=0 成立，对互斥组会偏高：五件各 100% 同组实际每次只出 1 件，抄件会报 5 件。
        // 表现是编辑器"试算掉落"给一个服务端永远达不到的数字，而库里、编辑器、快照三处的概率全显示得对。
        List<MonsterLootBindingSnapshotDto> GroupedTable()
        {
            var dto = new MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = 4242,
                LootTableId = 9,
                RollChanceBps = 0,      // 未配置 = 必触发（与内核 GateChanceBps 同口径）
                MinCount = 1,
                MaxCount = 1,
            };
            for (var i = 0; i < 5; i++)
            {
                dto.Entries.Add(new MonsterDropSnapshotDto
                {
                    ItemId = 79000 + i, ChanceBps = 10000, CountMin = 1, CountMax = 1, Seq = i + 1, GroupId = 6,
                });
            }
            return new List<MonsterLootBindingSnapshotDto> { dto };
        }

        var grouped = Horizon.MonsterCli.MonsterDropPreview.Summarize(GroupedTable());
        Assert.True(Math.Abs(grouped.ExpectedItemsPerKill - 1.0) < 1e-9,
                    $"互斥组的试算期望是 {grouped.ExpectedItemsPerKill:0.###}，应为 1.0（逐件相加的旧抄件会给 5.0）");
        Assert.False(grouped.CrossCheckFailed,
            $"逐条求和 {grouped.EntrySum:0.####} 与整表 {grouped.ExpectedItemsPerKill:0.####} 不等：两条入口分叉了");

        // 每件的中签概率必须体现累计减法：只有排在最前的那件拿满，其余被短路挡死。
        var entries = grouped.Bindings[0].Entries;
        Assert.Equal(5, entries.Count);
        Assert.True(Math.Abs(entries[0].WinChanceBps - 10000.0) < 1e-6, $"首位 {entries[0].WinChanceBps:0.#}，应拿满");
        for (var i = 1; i < entries.Count; i++)
        {
            Assert.True(entries[i].WinChanceBps < 1e-9, $"第 {i + 1} 件的中签概率不是 0：{entries[i].WinChanceBps}");
        }
        Assert.True(grouped.PerItemChanceBps.TryGetValue(79001, out var shadowedChance) && shadowedChance < 1e-9,
                    "被挡死的件在 perItem 里仍然有份额");

        // 判别夹具的另一半：独立掷（组 0）的表**不该**被折算成 1 件——
        // 否则这条断言会把"把一切都压成互斥"这种错法也判成通过。
        var independent = GroupedTable();
        foreach (var e in independent[0].Entries) e.GroupId = 0;
        var free = Horizon.MonsterCli.MonsterDropPreview.Summarize(independent);
        Assert.True(Math.Abs(free.ExpectedItemsPerKill - 5.0) < 1e-9,
                    $"逐件独立的表被算成 {free.ExpectedItemsPerKill:0.###} 件，应为 5.0");

        // 试算的数字必须是真能掷出来的：拿同一份数据做蒙特卡洛，实测均值要落在声明值附近。
        var snapshots = new List<MonsterLootBindingSnapshot> { GroupedTable()[0].ToSnapshot() };
        const int trials = 20000;
        double items = 0.0;
        for (var t = 0; t < trials; t++)
        {
            var r = MonsterLootCore.Roll(snapshots, (ulong)(t + 1) * 7919UL + 5UL, 4242, t * 13L);
            items += r.Drops.Count;
        }
        var measured = items / trials;
        Assert.True(Math.Abs(measured - grouped.ExpectedItemsPerKill) < 0.02,
                    $"声明 {grouped.ExpectedItemsPerKill:0.###} vs 实测 {measured:0.###}");
    }

    [Fact]
    public void 掷骰顺序由数据里的seq决定而不是查询回来的行序()
    {
        // 库里 `seq` 列的注释写的就是「掷骰顺序」，而内核把"条目下标"当作掷骰盐值的一部分。
        // 以前这一列在快照边界被整列丢掉，于是"哪件物品配哪颗骰子"实际由 SQL 返回行序决定：
        // 加一行、改索引、换执行计划都会让整表掉率静默漂移，而编辑器、库、快照三处的数字全都没变。
        var dir = Path.Combine(Path.GetTempPath(), "monster_seq_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shuffled = Path.Combine(dir, "shuffled.json");
        var ordered = Path.Combine(dir, "ordered.json");
        var tieZero = Path.Combine(dir, "tie_zero.json");
        try
        {
            File.WriteAllText(shuffled, BuildSeqSnapshot(reverseRows: true, seqValue: -1).ToJson());
            File.WriteAllText(ordered, BuildSeqSnapshot(reverseRows: false, seqValue: -1).ToJson());
            // 全部 seq=0（策划没显式排序时的常态）：靠物品 id 兜底，结果必须仍与行序无关。
            File.WriteAllText(tieZero, BuildSeqSnapshot(reverseRows: true, seqValue: 0).ToJson());

            var receipt = MonsterContentLoader.LoadFile(shuffled);
            Assert.True(receipt.Ok, receipt.Describe());

            var entries = MonsterContent.LootBindings(4321)[0].Entries;
            Assert.Equal(3, entries.Length);
            Assert.True(entries[0].Seq <= entries[1].Seq && entries[1].Seq <= entries[2].Seq,
                        "装配后没有按 seq 升序（实得 " + string.Join(",", entries.Select(e => e.Seq)) + "）");
            Assert.Equal(70001, entries[0].ItemId);   // seq=1 的那件才该拿到第一颗骰子
            Assert.Equal(70003, entries[2].ItemId);
            var before = SampleLoot(4321);

            Assert.True(MonsterContentLoader.LoadFile(ordered).Ok);
            var after = SampleLoot(4321);
            Assert.Equal(string.Join(";", before), string.Join(";", after));   // 行序相反必须掷出同一堆掉落

            Assert.True(MonsterContentLoader.LoadFile(tieZero).Ok);
            var tie = SampleLoot(4321);
            Assert.Equal(string.Join(";", before), string.Join(";", tie));     // seq 全等时按 itemId，也不许随行序变
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
            try { Directory.Delete(dir, true); } catch (Exception) { /* 临时目录清不掉不影响判据 */ }
        }
    }

    [Fact]
    public void 组号经导出文件装配到运行时之后仍然真的互斥()
    {
        // 上面的组语义用例都是直接造快照喂内核；这条走**玩家真正会经过的那条链**：
        // 库 → export-content 的 JSON → DsHost 装载 → MonsterContent.LootBindings → Roll。
        // 链条上任一层把 group_id 丢掉都不会报错：表现是"库里配了互斥组，进游戏人人全套掉"，
        // 而编辑器、库、快照三处的数字全都对得上。
        var dir = Path.Combine(Path.GetTempPath(), "monster_group_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "group_content.json");
        try
        {
            var snapshot = new MonsterContentSnapshot { ContentRevision = 9 };
            snapshot.Templates.Add(new MonsterTemplateSnapshot
            {
                MonsterId = 4399, Name = "组检验兽", Rank = 1, LevelBase = 10,
            });
            var binding = new MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = 4399, LootTableId = 1, RollChanceBps = 10000, MinCount = 1, MaxCount = 1,
            };
            // 组 1：三件都把概率填 0 ⇒ 魔兽的"必出其中一件、均匀抽"写法（平分显式档剩下的整份预算）。
            foreach (var itemId in new[] { 81001, 81002, 81003 })
            {
                binding.Entries.Add(new MonsterDropSnapshotDto { ItemId = itemId, ChanceBps = 0, Seq = itemId - 81000, GroupId = 1 });
            }
            // 组 2：三件都填 100% ⇒ 累计减法的短路档，只有排在最前的 82001  ever 中奖，后两件一次也轮不到。
            // （这既是 AC 的字面行为，也是校验器"连减法都参与不了"那条警告针对的配法。）
            foreach (var itemId in new[] { 82001, 82002, 82003 })
            {
                binding.Entries.Add(new MonsterDropSnapshotDto { ItemId = itemId, ChanceBps = 10000, Seq = itemId - 81000, GroupId = 2 });
            }
            // 组外一件 100%：证明加组没有把未分组的逐件独立掷一起压成"最多一件"。
            binding.Entries.Add(new MonsterDropSnapshotDto { ItemId = 81004, ChanceBps = 10000, Seq = 99, GroupId = 0 });
            snapshot.LootBindings.Add(binding);
            File.WriteAllText(path, snapshot.ToJson());

            // JSON 文本里必须真的写着 groupId：键名漂一个字母，STJ 会"解析成功而值全是初值"。
            var json = File.ReadAllText(path);
            Assert.Equal(6, Regex.Matches(json, "\"groupId\": *[12]").Count);

            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe());

            var entries = MonsterContent.LootBindings(4399)[0].Entries;
            Assert.Equal(7, entries.Length);
            // 装配按 (seq, itemId) 升序：81001/81002/81003(seq 1..3) → 81004(seq 99) → 82001..82003(seq 1001..1003)。
            Assert.Equal(81001, entries[0].ItemId);
            Assert.Equal(1, entries[0].GroupId);
            Assert.Equal(1, entries[2].GroupId);
            Assert.Equal(0, entries[3].GroupId);    // 组外那件夹在两组中间：组号不参与排序，下标才是盐值
            Assert.Equal(2, entries[4].GroupId);
            Assert.Equal(2, entries[6].GroupId);

            // 端到端行为：每次击杀恰好 1 件组 1 + 1 件组 2 + 1 件组外；组 1 三件轮流向后出，组 2 只出第一条。
            var seen = new Dictionary<int, int>();
            var perKillCounts = new HashSet<int>();
            for (var k = 1UL; k <= 600UL; k++)
            {
                var r = MonsterLootCore.Roll(MonsterContent.LootBindings(4399), k, 4399, 500L);
                perKillCounts.Add(r.Drops.Count);
                Assert.Equal(1, r.Drops.Count(d => d.ItemId == 81004));   // 组外那件照常必掉
                Assert.Equal(1, r.Drops.Count(d => d.ItemId == 81001 || d.ItemId == 81002 || d.ItemId == 81003));
                Assert.Equal(1, r.Drops.Count(d => d.ItemId == 82001));
                Assert.Equal(0, r.Drops.Count(d => d.ItemId == 82002 || d.ItemId == 82003));
                foreach (var d in r.Drops)
                {
                    seen.TryGetValue(d.ItemId, out var c);
                    seen[d.ItemId] = c + 1;
                }
            }
            Assert.Equal(new[] { 3 }, perKillCounts.ToArray());   // 每次都是"组1 一件 + 组2 一件 + 组外一件"
            Assert.Equal(5, seen.Count);   // 81001/81002/81003/81004/82001 五个；82002、82003 从未出现
            foreach (var itemId in new[] { 81001, 81002, 81003 })
            {
                Assert.True(seen.TryGetValue(itemId, out var hits) && hits > 0,
                            $"组内物品 {itemId} 一次都没出过：装配把组号读成了别的值，或等概率档没接到余额");
                // 均匀抽 ⇒ 各拿三分之一（600 次里 ±40 已经是很宽的容差）
                Assert.True(Math.Abs(seen[itemId] - 200) <= 40, $"等概率档 {itemId} 出现 {seen[itemId]} 次，偏离 200 太远");
            }
            Assert.False(seen.ContainsKey(82002), "被满概率件挡死的组内成员竟然掉过东西");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
            try { Directory.Delete(dir, true); } catch (Exception) { /* 临时目录清不掉不影响判据 */ }
        }
    }

    [Fact]
    public void 逐件条件经导出文件装配到运行时之后仍然只对做了任务的人发()
    {
        // 与组号那条同一族：条件若在任何一跳被丢掉，表现都是"任务物品人人可拿"或"配了却永不发"，
        // 而编辑器、库、快照三处的数字全都对得上。这里走 export 的 JSON → LoadFile → Roll 整条链。
        var dir = Path.Combine(Path.GetTempPath(), "monster_cond_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cond_content.json");
        try
        {
            var snapshot = new MonsterContentSnapshot { ContentRevision = 11 };
            snapshot.Templates.Add(new MonsterTemplateSnapshot { MonsterId = 8686, Name = "条件检验兽", Rank = 1, LevelBase = 10 });
            var binding = new MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = 8686, LootTableId = 1, RollChanceBps = 10000, MinCount = 1, MaxCount = 1,
                ConditionKind = 0, QuestId = 0,
            };
            binding.Entries.Add(new MonsterDropSnapshotDto { ItemId = 86001, ChanceBps = 10000, Seq = 1 });                       // 缺省=继承=无条件
            binding.Entries.Add(new MonsterDropSnapshotDto { ItemId = 86002, ChanceBps = 10000, Seq = 2, ConditionKind = 1, QuestId = 90009 });
            snapshot.LootBindings.Add(binding);
            File.WriteAllText(path, snapshot.ToJson());

            var json = File.ReadAllText(path);
            Assert.True(json.Contains("90009"), "导出的 JSON 里没有任务 id：装配链某一跳把它丢了");

            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe());
            var entries = MonsterContent.LootBindings(8686)[0].Entries;
            Assert.Equal(MonsterLootCore.ConditionInherit, entries[0].ConditionKind);   // 第一件仍是"继承"，不是被改写成 0
            Assert.Equal(1, entries[1].ConditionKind);
            Assert.Equal(90009, entries[1].QuestId);

            var r = MonsterLootCore.Roll(MonsterContent.LootBindings(8686), 4242UL, 8686, 600L);
            Assert.Single(r.Drops);
            Assert.Equal(86001, r.Drops[0].ItemId);
            Assert.Single(r.ConditionalDrops);
            Assert.Equal(86002, r.ConditionalDrops[0].ItemId);
            Assert.Equal(90009, r.ConditionalDrops[0].QuestId);
            Assert.Equal(r.EntriesRolled, r.Accounted);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
            try { Directory.Delete(dir, true); } catch (Exception) { /* 临时目录清不掉不影响判据 */ }
        }
    }

    /// <summary>造一份只有"条目行序/seq"不同的内容快照（其余完全相同），用于上面的行序无关性判据。</summary>
    private static MonsterContentSnapshot BuildSeqSnapshot(bool reverseRows, int seqValue)
    {
        var snapshot = new MonsterContentSnapshot { ContentRevision = 7 };
        snapshot.Templates.Add(new MonsterTemplateSnapshot
        {
            MonsterId = 4321, Name = "顺序检验兽", Rank = 1, LevelBase = 10,
        });
        var entries = new List<MonsterDropSnapshotDto>();
        for (var i = 0; i < 3; i++)
        {
            entries.Add(new MonsterDropSnapshotDto
            {
                ItemId = 70001 + i,
                ChanceBps = 5000,
                CountMin = 1,
                CountMax = 3,
                // seqValue<0 表示"按行号给顺序"（1/2/3）；>=0 表示全部填同一个值（策划没排序的常态）。
                Seq = seqValue < 0 ? i + 1 : seqValue,
            });
        }
        if (reverseRows) entries.Reverse();
        snapshot.LootBindings.Add(new MonsterLootBindingSnapshotDto
        {
            MonsterTemplateId = 4321,
            LootTableId = 1,
            RollChanceBps = 10000,
            Entries = entries,
        });
        return snapshot;
    }

    /// <summary>把当前安装的掉落内容按 120 个击杀者各掷一遍，压成可逐字比对的文本。</summary>
    private static List<string> SampleLoot(int templateId)
    {
        var lines = new List<string>();
        for (var k = 1UL; k <= 120UL; k++)
        {
            var r = MonsterLootCore.Roll(MonsterContent.LootBindings(templateId), k, templateId, 500L);
            lines.Add(string.Join(",", r.Drops.Select(d => d.ItemId + "x" + d.Count)) + "|" + r.Money);
        }
        return lines;
    }

    [Fact]
    public void 击杀掉落优先用这只怪自己的绑定而不是兜底假表()
    {
        // 开发期那张"模板×1000+1"的假表把真实掉落完全遮住了：
        // 编辑器里配的物品 50001 与金币一个都不会出现，而日志里没有任何异常。
        var receipt = MonsterContentLoader.LoadFile(Fixture("smoke_v2_content.json"));
        Assert.True(receipt.Ok, receipt.Describe());
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 1;        // smoke 内容里给这只怪配了绑定：表 3、物品 50001、金币 10..40
            point.MaxAlive = 1;
            point.MaxHp = 30;                   // 一击就死，便于看掉落闭环
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 20f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 100f,
                PlayerAttackRange = 50f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(2001, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();                                       // 生成怪

            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            // 这只怪的血量**由内容模板决定**（点位那份只是装配期盖出来的副本，战斗层读的是按这只怪
            // 生效等级算的那一份，见 BuildMonsterStats）。这条用例测的是掉落闭环，不是数值，
            // 所以直接把血打到"一击就死"那一档，而不是回头去改点位 MaxHp 假装那是权威。
            Assert.True(world.TryGetMonster(monsterId, out var victim) && victim != null);
            var wounded = victim.Ai;
            wounded.Hp = 30;
            victim.Ai = wounded;

            var resolution = world.SubmitCombatAction(2001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                AttackerYaw = 0f,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.True(resolution.TargetDied, "这一击没打死怪，测试夹具的血量假设变了");

            var after = world.Tick();                           // 死亡事件随下一个 tick 出栈
            var loot = new List<(int ItemId, int Count)>();
            foreach (var evt in after.GameplayEvents)
            {
                if (evt.Kind != DsGameplayEventKind.Loot) continue;
                loot.Add((evt.ItemId, evt.ItemCount));
            }

            Assert.True(world.MonsterLootBindingsRolled >= 1, "没走内容绑定：掉落仍在用兜底假表");
            Assert.DoesNotContain(loot, d => d.ItemId == 1001 || d.ItemId == 1002);   // 假表的物品 id 由模板派生
            foreach (var d in loot) Assert.Equal(50001, d.ItemId);                     // 只可能是配的那件
            Assert.True(world.MonsterLootMoneyRolled >= 10,
                        "绑定里配了 10..40 金币却没算出钱：金币那一段没接上");
            _log.WriteLine($"掉落闭环：{loot.Count} 件、金币 {world.MonsterLootMoneyRolled}、" +
                           $"绑定 {world.MonsterLootBindingsRolled} 张、条件件 {world.MonsterLootConditionalRolled}");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 条件件在击杀时按自己的任务号出账而不是混进普通掉落()
    {
        // 生产内容里 1 号墨麒麟刚挂了一件 `conditionKind=1 / questId=20001`（§12.13）。
        // 内核那一半有用例（`ConditionalDrops` 分桶），但 **DS 出口这一段没人钉过**：
        // 少写 `QuestId = cd.QuestId`、或把它并进 Loot 事件，表现都是"接了任务也拿不到"，
        // 而库、编辑器、快照三处看不出任何差别。
        const int templateId = 778;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "条件件木桩",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "yan_wu",
            Stats = new MonsterStatsConfig { HpBase = 100000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(45, 5, new[] { 1.0, 2.0, 2.5, 4.0, 4.0 }),
        };
        config.Behavior = MonsterBehaviorConfig.Default;
        bundle.ByTemplateId[templateId] = config;
        bundle.LootBindings[templateId] = new List<MonsterLootBindingSnapshot>
        {
            new MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = templateId,
                LootTableId = 1,
                RollChanceBps = 10000,
                Entries = new List<MonsterDropSnapshotDto>
                {
                    // 两件都是 100%（省掉"这次没掷中"的随机噪声），差别只在条件那一格。
                    new MonsterDropSnapshotDto { ItemId = 60001, ChanceBps = 10000, CountMin = 1, CountMax = 1, Seq = 1, ConditionKind = 0 },
                    new MonsterDropSnapshotDto { ItemId = 60002, ChanceBps = 10000, CountMin = 1, CountMax = 1, Seq = 2, ConditionKind = 1, QuestId = 20001 },
                },
            }.ToSnapshot(),
        };
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = templateId;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.MaxHp = 30;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 20f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 100f,
                PlayerAttackRange = 50f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(2001, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();

            var monsterId = SpawnDirectorCore.ComputeEntityId(templateId, 0);
            Assert.True(world.TryGetMonster(monsterId, out var victim) && victim != null);
            var wounded = victim.Ai;
            wounded.Hp = 30;
            victim.Ai = wounded;

            var resolution = world.SubmitCombatAction(2001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                AttackerYaw = 0f,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.True(resolution.TargetDied, "这一击没打死怪：夹具的血量假设变了");

            var after = world.Tick();    // 死亡事件随下一个 tick 出栈
            var loot = after.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            var conditional = after.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.LootConditional).ToList();

            // 正向对照：无条件那件走普通掉落。少了它，"条件件没混进来"可能只是整份掉落都没出。
            Assert.Contains(loot, e => e.ItemId == 60001);
            Assert.DoesNotContain(loot, e => e.ItemId == 60002);
            var one = Assert.Single(conditional);
            Assert.Equal(60002, one.ItemId);
            Assert.Equal(1, one.ItemCount);
            Assert.Equal(20001, one.QuestId);      // 少了这一格：业务层拿到的是"任务 0"，永远不发放
            Assert.Equal(2001UL, one.ActorCharacterId);
            Assert.True(world.MonsterLootConditionalRolled >= 1, "计数没动：条件件根本没掷");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>造一份"只有金币区间不同、其余逐字相同"的内容，给金币事件用例的两个半段共用。</summary>
    private static MonsterContentBundle BuildGoldLootContent(int templateId, int moneyMin, int moneyMax)
    {
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "衔金兽" + templateId,
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "xian_jin",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 0, AtkPerLevel = 0 },
            Xp = MonsterXpConfig.Default,
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.IsAggressive = true;
        behavior.AggroRadius = 20f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 60f;
        behavior.WanderRadius = 0f;
        config.Behavior = behavior;
        bundle.ByTemplateId[templateId] = config;
        bundle.LootBindings[templateId] = new List<MonsterLootBindingSnapshot>
        {
            new MonsterLootBindingSnapshot(1, 10000, 1, 1, MonsterLootCore.ConditionNone, 0, moneyMin, moneyMax,
                new[] { new MonsterDropSnapshot(50001, 10000, 1, 1, 1) }),
        };
        return bundle;
    }

    /// <summary>装内容、生成一只怪、一击打死，回传死亡随下一个 tick 出栈的全部事件与金币计票。</summary>
    private static (List<DsGameplayEvent> Events, long MoneyRolled) KillOnceWithLootGold(
        int templateId, int moneyMin, int moneyMax)
    {
        MonsterContent.Install(BuildGoldLootContent(templateId, moneyMin, moneyMax));
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.MaxHp = 30;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 20f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 100f,
                PlayerAttackRange = 50f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(2001, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();

            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            Assert.True(world.TryGetMonster(monsterId, out var victim) && victim != null);
            // 血量权威在内容公式那一份（点位只是装配期盖出来的副本），所以和掉落闭环那条一样
            // 直接把血打到"一击就死"那一档，而不是回头改点位假装那是权威。
            var wounded = victim.Ai;
            wounded.Hp = 30;
            victim.Ai = wounded;

            var resolution = world.SubmitCombatAction(2001, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                AttackerYaw = 0f,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.True(resolution.TargetDied, "这一击没打死怪：夹具的血量假设变了");

            var after = world.Tick();
            return (new List<DsGameplayEvent>(after.GameplayEvents), world.MonsterLootMoneyRolled);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 击杀掷出的金币带金额发一条事件而没配金币时一条都不发()
    {
        // 金币这一路以前断在 DS 里：内核掷得出数额、DS 也只累计了计票，玩家口袋一个铜板都没多。
        // 这一条测**DS 侧那一跳**：死亡时发出的那一条事件带上金额，且数额就是掷出的那一份
        // （不是两处各算一遍）。下半段是正向对照——没配金币不许多发一条 0 元事件，
        // 否则消费端要为一个永远不存在的钱留分支；两半的物品数还必须是同一件，
        // 证明这个开关只动金币、没把掉落一起带走。
        const int templateId = 13577;

        var withGold = KillOnceWithLootGold(templateId, 30, 30);
        var gold = withGold.Events.FindAll(e => e.Kind == DsGameplayEventKind.KillMoney);

        var one = Assert.Single(gold);
        Assert.Equal(30L, one.Money);
        Assert.Equal(30L, withGold.MoneyRolled);
        Assert.Equal(2001UL, one.ActorCharacterId);
        Assert.Equal(templateId, one.MonsterTemplateId);
        Assert.Single(withGold.Events.FindAll(e => e.Kind == DsGameplayEventKind.Loot));

        var noGold = KillOnceWithLootGold(templateId, 0, 0);
        Assert.Empty(noGold.Events.FindAll(e => e.Kind == DsGameplayEventKind.KillMoney));
        Assert.Equal(0L, noGold.MoneyRolled);
        Assert.Single(noGold.Events.FindAll(e => e.Kind == DsGameplayEventKind.Loot));
    }

    // ---------------- 巡逻线覆盖（route_override_id 的消费者） ----------------

    private static MonsterPatrolRoute MakePatrolLine(int routeId, float stepX, float stepY) => new MonsterPatrolRoute
    {
        RouteId = routeId,
        Mode = MonsterPatrolMode.Loop,
        PauseAtPointSeconds = 0f,
        UseNavMesh = false,          // 本用例不装导航图：位移完全由"走哪条线"决定
        Points = new[]
        {
            new MonsterPatrolPoint(2f + stepX * 10f, 2f + stepY * 10f, 0f, 0f),
            new MonsterPatrolPoint(2f + stepX * 20f, 2f + stepY * 20f, 0f, 0f),
        },
    };

    /// <summary>
    /// 同一只怪、同两条线，只把刷怪点的 <c>route_override_id</c> 换成 0 或 32，跑 400 tick 看它走到哪。
    /// 两条线一条沿 +X、一条沿 +Y，所以"覆盖有没有被采用"在坐标上是可直接分辨的。
    /// </summary>
    private static (float X, float Y) RunPatrolWithRouteOverride(
        int routeOverrideId, bool withSpawnSnapshot = true, bool forceV1 = false, bool withLine = true)
    {
        const int templateId = 12601;
        const int behaviorRouteId = 31;
        const int overrideRouteId = 32;

        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "覆盖路线检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "lu_gai",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 0, 1, 0, 0, 0);
        // forceV1：能力位全关 ⇒ 宿主根本不进 v2（生产里 11 只种子怪就是这个形态），
        // 这一格是"巡逻线接进 v1"那一条用例的开关；其余用例逐字保持原来的 v2 通路。
        if (forceV1) behavior.Flags = MonsterAiFlags.None;
        behavior.IsAggressive = false;
        behavior.AggroRadius = 1f;
        behavior.LeashRadius = 80f;
        behavior.WalkSpeed = 4f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;         // 不许踱步：位移只能来自巡逻线
        behavior.IdleToPatrolSeconds = 0.05f;
        behavior.PatrolRouteId = withLine ? behaviorRouteId : 0;
        config.Behavior = behavior;
        bundle.ByTemplateId[templateId] = config;

        bundle.Routes[behaviorRouteId] = MakePatrolLine(behaviorRouteId, 1f, 0f);   // 沿 +X
        bundle.Routes[overrideRouteId] = MakePatrolLine(overrideRouteId, 0f, 1f);   // 沿 +Y
        if (withSpawnSnapshot)
        {
            bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
                1, 0, 0, 0f, 0f, 0f, 0f, templateId, null, 1, 6f, 0f, routeOverrideId));
        }
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            for (var i = 0; i < 400; i++) world.Tick();

            Assert.True(world.TryGetMonster(SpawnDirectorCore.ComputeEntityId(1, 0), out var monster) && monster != null,
                        "400 tick 后这只怪不在世界里，位移无从谈起");
            return (monster!.Ai.X, monster.Ai.Y);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 刷怪点覆盖的巡逻线真的取代了行为表那条()
    {
        // 库里那一列的注释写着"0=用行为表"，语义早就承诺了；而在这轮之前会话只读行为表那一份，
        // 于是"给这个刷怪点换一条线"在游戏里表现为什么都没发生。
        // 正反两半都要有：覆盖生效（换成 +Y）与不覆盖时保持原样（+X）——
        // 只测一半的话，"覆盖被忽略"和"两条线本来就一样"都长得像绿灯。
        var noOverride = RunPatrolWithRouteOverride(0);
        Assert.True(noOverride.X - noOverride.Y > 8f,
                    $"没覆盖时本该沿行为表的 +X 线走，实得 ({noOverride.X:0.#}, {noOverride.Y:0.#})");

        var overridden = RunPatrolWithRouteOverride(32);
        Assert.True(overridden.Y - overridden.X > 8f,
                    $"刷怪点覆盖成 32（+Y 那条）却没走它：实得 ({overridden.X:0.#}, {overridden.Y:0.#})——覆盖还是没人读");
    }

    [Fact]
    public void 内容里没有刷怪点快照时不丢行为表那条巡逻线()
    {
        // 消费方读的是"生成时定下的 monster.PatrolRouteId"，而它靠内容里的刷怪点快照才能算出来。
        // 内容包只带模板没带点位（脚本生成、或点位 id 对不上）是真实存在的形态——
        // 这时必须退回行为表那条线，而不是因为"没查到覆盖"变成 0=没有路线、原地站住。
        // 这条与上一条是**两条不同的通路**：上一条测覆盖被采用，这一条测覆盖查不到时的兜底。
        var pos = RunPatrolWithRouteOverride(0, withSpawnSnapshot: false);
        Assert.True(pos.X - pos.Y > 8f,
                    $"内容里没有点位快照时本该仍走行为表的 +X 线，实得 ({pos.X:0.#}, {pos.Y:0.#})");
    }

    [Fact]
    public void 默认档的怪也沿画好的巡逻线走()
    {
        // 巡逻线以前只在 v2 那一段被解析（v1 的 Decide 连 route 入参都没有）
        // ⇒ 给一只默认档（ai_profile=0，生产里全部种子怪）的怪画线，游戏里表现为原地不动。
        // 这份夹具把游荡半径钉成 0，所以"没线"与"有线"在坐标上是可直接分辨的——
        // 少了下面那个对照，"它走到了 (12,2)"也可能只是"它自己在动"。
        var withLine = RunPatrolWithRouteOverride(0, forceV1: true);
        Assert.True(withLine.X - withLine.Y > 8f,
                    $"默认档没沿线上走：实得 ({withLine.X:0.#}, {withLine.Y:0.#})——那条线还是只有 v2 读得到");

        var noLine = RunPatrolWithRouteOverride(0, forceV1: true, withLine: false);
        // 没线时 v1 退回"绕出生点游荡"，而它用的那份游荡半径来自**刷怪点**（SpawnPointConfig 的 6 米），
        // 不是行为表里钉成 0 的那一份——那是另一条已记的作用域洞（设计文档 §11.2 第 14 条），
        // 本条只认这个可分辨的差别：游荡走不到线上那个 12 米开外的点。
        Assert.True(noLine.X - noLine.Y < 8f,
                    $"没给线却走到了 ({noLine.X:0.#}, {noLine.Y:0.#})：这条对照已经分不清\"读了线\"与\"自己在游荡\"");
    }

    [Fact]
    public void 装配期念出巡逻线的三种断链()
    {
        // 三种"配了指针而指针那头没人"：线不合格被丢、行为表指向不存在的线、刷怪点覆盖指向不存在的线。
        // 这三条在运行期都只表现为"怪不沿线上走"，与"这一格根本没配"在屏幕上长得一样 ⇒ 必须装配期就说。
        static MonsterContentSnapshot MakeSnapshot(int linePoints, int spawnOverride)
        {
            var snap = new MonsterContentSnapshot { ContentRevision = 1 };
            var tpl = new MonsterTemplateSnapshot
            {
                MonsterId = 12481, Name = "断链兽",
                LevelBase = 10, LevelMin = 10, LevelMax = 10,
            };
            tpl.Behavior.PatrolRouteId = 7;
            snap.Templates.Add(tpl);

            var line = new MonsterRouteSnapshot { RouteId = 7 };
            for (var i = 0; i < linePoints; i++) line.Points.Add(new MonsterPointSnapshot { X = 2f + i, Y = 2f, Z = 0f });
            snap.Routes.Add(line);

            snap.SpawnPoints.Add(new MonsterSpawnSnapshotDto
            {
                SpawnPointId = 1, MonsterTemplateId = 12481, MaxAlive = 1,
                RouteOverrideId = spawnOverride,
            });
            return snap;
        }

        var broken = MakeSnapshot(linePoints: 1, spawnOverride: 9);
        broken.ToBundle(out var bad);
        Assert.True(bad.Any(p => p.Contains("巡逻线 7 被丢弃")),
                    "只画了一个点的线被静默丢掉：策划看到的仍然是\"我配了线而怪不走\"");
        Assert.True(bad.Any(p => p.Contains("行为表指向巡逻线 7")),
                    "行为表指向一条已被丢弃/不存在的线而没人说");
        Assert.True(bad.Any(p => p.Contains("路线覆盖指向 9")),
                    "刷怪点覆盖指向这份内容包里根本没有的线而没人说");

        // 正面对照：线画够两个点、覆盖就指这条线 ⇒ 三条都不该念。
        // 少了这一半，"永远在响的闸门"和"会念的闸门"在测试结果里是一样的。
        var good = MakeSnapshot(linePoints: 2, spawnOverride: 7);
        good.ToBundle(out var goodProblems);
        Assert.True(!goodProblems.Any(p => p.Contains("被丢弃") || p.Contains("指向巡逻线") || p.Contains("路线覆盖指向")),
                    "合法内容也被念了：这三条判据看的是引用关系还是别的东西？" +
                    " 实得：" + string.Join(" | ", goodProblems));
    }

    [Fact]
    public void 路线上关掉寻路时这一段按直线连而开着才绕开阻挡()
    {
        // use_nav_mesh 是路线表上的一列（默认 1）。以前装配、编辑器、库里都有它，内核从不查，
        // 于是"策划把这条线的寻路关掉，怪照样绕"——这一列现在是判据了。
        // 几何：一堵只能从最上方两格翻过去的墙横在巡逻线的两点之间。
        //   开寻路 → 必须先把 Y 抬到缺口（途中最高 y 远大于 3）；
        //   关寻路 → 直连两点，Y 一直贴着 3，X 直接穿过那堵墙。
        var withNav = RunPatrolAcrossWall(true);
        var withoutNav = RunPatrolAcrossWall(false);
        Assert.True(withNav.MaxY >= 10f,
                    $"路线开了寻路却没绕到缺口（途中最高 y={withNav.MaxY:0.##}）——这一列还是没人读");
        Assert.True(withoutNav.MaxY <= 5f,
                    $"路线关了寻路却仍在绕（途中最高 y={withoutNav.MaxY:0.##}）——这条线的开关被忽略了");
        Assert.True(withoutNav.MaxX > 9f,
                    $"关寻路时该直穿墙到达第二个点（x=11），实得最远 x={withoutNav.MaxX:0.##}");
        Assert.True(withNav.MaxX > 9f,
                    $"开寻路也得最终走到第二个点，实得最远 x={withNav.MaxX:0.##}——绕过去了却没到达");
        // 两条必须真的走成两副样子（否则上面的判据是同一条通路自证）
        Assert.True(withNav.MaxY - withoutNav.MaxY > 5f, "开/关寻路的轨迹没有区别，判据无从分辨");
    }

    /// <summary>同一只怪、同一条巡逻线，只换路线上的 use_nav_mesh 一格，跑 900 tick 看它怎么走。</summary>
    private static (float MaxY, float MaxX) RunPatrolAcrossWall(bool routeUsesNavMesh)
    {
        const int templateId = 12602;
        const int routeId = 41;

        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "绕墙检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "lu_qiang",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // 能力位开着导航：这只怪**会**走图，问题只在这条线让不让它走。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, 0, 1, 0, 0);
        behavior.IsAggressive = false;
        behavior.AggroRadius = 1f;
        behavior.LeashRadius = 80f;
        behavior.WalkSpeed = 4f;
        behavior.MoveSpeed = 4f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0.05f;
        behavior.FollowType = MonsterFollowType.GroundWalk;
        behavior.PatrolRouteId = routeId;
        config.Behavior = behavior;
        bundle.ByTemplateId[templateId] = config;

        var route = MakePatrolLine(routeId, 1f, 0f);
        route.Points = new[]
        {
            new MonsterPatrolPoint(3f, 3f, 0f, 0f),
            new MonsterPatrolPoint(11f, 3f, 0f, 0f),
        };
        route.UseNavMesh = routeUsesNavMesh;
        bundle.Routes[routeId] = route;
        bundle.SpawnPoints.Add(new MonsterSpawnSnapshot(
            1, 0, 0, 3f, 3f, 0f, 0f, templateId, null, 1, 6f, 0f, 0));
        MonsterContent.Install(bundle);
        try
        {
            const int w = 14;
            const int h = 14;
            var costs = new byte[w * h];
            for (var i = 0; i < costs.Length; i++) costs[i] = MonsterNavGrid.BaseCellCost;
            // x=6 一堵墙，只留最上面两格（y=12、13）可过
            for (var y = 0; y <= 11; y++) costs[y * w + 6] = MonsterNavGrid.BlockedCellCost;
            var grid = new MonsterNavGrid
            {
                Width = w,
                Height = h,
                CellSize = 1f,
                OriginX = 0f,
                OriginY = 0f,
                CellCost = costs,
            };

            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 3f; point.Y = 3f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            world.ConfigureNavigation(grid);
            Assert.True(world.HasNavigation, "导航网格没装上，绕墙与否根本无从分辨");

            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            // 取**全程最大值**而不是末尾位置：巡逻是 Loop，900 tick 落在往返的哪一段取决于速度，
            // 拿瞬时位置当期望值等于从参数推期望（会红在正确实现上）。
            var maxY = float.MinValue;
            var maxX = float.MinValue;
            for (var i = 0; i < 900; i++)
            {
                world.Tick();
                if (world.TryGetMonster(monsterId, out var m) && m != null)
                {
                    if (m.Ai.Y > maxY) maxY = m.Ai.Y;
                    if (m.Ai.X > maxX) maxX = m.Ai.X;
                }
            }
            Assert.True(maxY > float.MinValue && maxX > float.MinValue, "900 tick 里一次都没读到这只怪");
            return (maxY, maxX);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    // ---------------- 端到端：从数据面打进 DS 世界，掉落/经验/尸体都出现在真下行字节里 ----------------

    private const string KillChainFixture = """
{"contentRevision":1,"templates":[
  {"monsterId":12800,"name":"链证怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":2.0,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":1,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":30.0,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":0,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":12800,"maxAlive":1,"slotSpacing":6}
],"lootBindings":[
  {"monsterTemplateId":12800,"lootTableId":9001,"rollChanceBps":0,"minCount":1,"maxCount":1,
   "conditionKind":0,"questId":0,"moneyMin":0,"moneyMax":0,
   "entries":[{"itemId":555,"chanceBps":10000,"countMin":2,"countMax":2,"seq":1}]}
]}
""";

    private readonly record struct KillChainObservation(
        int DamagePackets, bool KillSeen, long XpForKiller, int LootEvents, int LootItemCount,
        bool CorpseCarriedDeadBit, bool SawCorpseFade);

    /// <summary>
    /// 按**真实进程会走的顺序**跑一遍击杀：字节包 → <c>DsDataPlaneHost</c>（含上行分发）→
    /// <c>DsWorldSession</c> 结算 → <c>DsDownlinkEncoder</c> 编码 → 再解回来看客户端会收到什么。
    /// 这条用例存在的理由：前面每一轮各自钉住了自己那一段（内核 / 会话 / 编码 / 上行），
    /// 而目标要的是"玩家在屏幕上看到的那份状态真的被他的动作改变了"——那只有一条端到端的链能证明。
    /// </summary>
    private static KillChainObservation RunKillChain(int playerLevel)
    {
        var path = Path.Combine(Path.GetTempPath(), "monster_chain_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, KillChainFixture);
        try
        {
            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join("; ", receipt.Problems));

            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12800;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.SlotSpacing = 6f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 30f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                PlayerAttackPower = 60f,     // 两下打死 100 血
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });

            var plane = new DsDataPlaneHost(world, listenPort: 0);
            plane.Apply(new Horizon.Game.Message.Sync.HandshakePacket
            {
                LocalCharacterId = 3001UL,
                InitialClientTick = 1,
                ProtocolVersion = Horizon.Game.Message.Sync.SyncProtocolVersion.Current,
                Nickname = "链证人",
                Level = playerLevel,
                Title = string.Empty,
                InitialX = -2f, InitialY = 0f, InitialZ = 0f,
            });
            Assert.Equal(1, world.PlayerCount);

            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            const uint dead = Horizon.Game.Message.Sync.Components.EntityStateBits.Dead;

            var damagePackets = 0;
            var killSeen = false;
            var lootEvents = 0;
            var lootItemCount = 0;
            long xpForKiller = -1;
            var corpseCarriedDeadBit = false;
            var sawCorpseFade = false;

            for (var i = 0; i < 400; i++)
            {
                // 每 61 tick 打一下：必须越过数据面那道 60 tick 的边界限流（不是绕过，是按它的规则发）
                if (i % 61 == 0 && world.MonsterCount > 0)
                {
                    var action = new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = 3001UL,
                        TargetId = monsterId,
                        SkillId = 0,
                        ClientTick = i,
                        AttackerYaw = 0f,
                        ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                    };
                    // 走编解码一圈：证明"客户端能发出去的那串字节"就是这个世界的入口，不是测试的后门
                    Horizon.Game.Message.Sync.SyncPacketCodec.Encode(action, out var frame, out var frameLength);
                    var slice = new byte[frameLength];
                    Array.Copy(frame, slice, frameLength);
                    plane.Apply((Horizon.Game.Message.Sync.SyncPacket)
                        Horizon.Game.Message.Sync.SyncPacketCodec.Decode(slice));
                }

                var result = world.Tick();
                var batch = DsDownlinkEncoder.Encode(result);
                foreach (var item in batch.Items)
                {
                    var packet = Horizon.Game.Message.Sync.SyncPacketCodec.Decode(item.Frame);
                    if (packet is Horizon.Game.Message.Sync.DamagePacket dmg && dmg.TargetId == monsterId) damagePackets++;
                    if (packet is Horizon.Game.Message.Sync.SnapshotPacket snap)
                    {
                        foreach (var delta in snap.Deltas)
                        {
                            if (delta.EntityId != monsterId) continue;
                            // 两条要分开判：Despawn 的 delta **本来就带的 State**，
                            // 用"有 State"当入口会把消失那一格整个跳过（第一版就是这么漏的）。
                            if (delta.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Despawn)
                            {
                                sawCorpseFade = true;
                                continue;
                            }
                            if (delta.State is null) continue;
                            if ((delta.State!.Value.StateBits & dead) != 0) corpseCarriedDeadBit = true;
                        }
                    }
                }

                foreach (var gameplay in result.GameplayEvents)
                {
                    if (gameplay.MonsterEntityId != monsterId && gameplay.ActorCharacterId != 3001UL) continue;
                    switch (gameplay.Kind)
                    {
                        case DsGameplayEventKind.Kill:
                            killSeen = true;
                            break;
                        case DsGameplayEventKind.Loot:
                            lootEvents++;
                            lootItemCount += gameplay.ItemCount;
                            break;
                        case DsGameplayEventKind.KillXp when gameplay.ActorCharacterId == 3001UL:
                            xpForKiller = gameplay.ItemCount;
                            break;
                    }
                }
            }

            Assert.True(world.MonsterCorpseCount == 0, "400 tick 后尸体还挂着：收尸没发生");
            return new KillChainObservation(damagePackets, killSeen, xpForKiller, lootEvents,
                lootItemCount, corpseCarriedDeadBit, sawCorpseFade);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 端到端_从数据面打进怪之后掉落经验与尸体都出现在下行字节里()
    {
        var seen = RunKillChain(playerLevel: 12);

        Assert.True(seen.DamagePackets >= 2,
            $"下行字节里没看到两下伤害包（实得 {seen.DamagePackets}）：客户端的血条没有数据来源");
        Assert.True(seen.KillSeen, "击杀玩法事件没发：任务进度与好感这一路整个断");
        Assert.Equal(1, seen.LootEvents);
        Assert.Equal(2, seen.LootItemCount);   // 夹具配的是 100% 掉 2 个，逐件独立掷的期望值就是 2
        Assert.True(seen.XpForKiller > 0, $"击杀经验事件没值（实得 {seen.XpForKiller}）：等级差衰减整条链在实机里不发生");
        Assert.True(seen.CorpseCarriedDeadBit, "尸体阶段的快照 delta 没带死亡位：客户端分不清残血和尸体");
        Assert.True(seen.SawCorpseFade, "没在下行里看到这只怪的 Despawn：尸体会永久留在屏幕上");
    }

    [Fact]
    public void 端到端_同一条链上高等级号拿到的经验更少并且差太多就断供()
    {
        // 目标原话里那条"随玩家等级变化反比例衰减"，端到端判据只能是这个形状：
        // 同一只怪（12 级）、同一个击杀路径，只换玩家等级。
        // 数值是**量出来的**（不是从公式推的）：12 级号 105、16 级号 35、20 级号没有经验事件。
        // 只测内核曲线的话，"内核算对了但会话没把等级传进账本"这一格永远看不见。
        var sameLevel = RunKillChain(playerLevel: 12);
        var higher = RunKillChain(playerLevel: 16);
        var tooHigh = RunKillChain(playerLevel: 20);

        Assert.True(sameLevel.XpForKiller > 0, $"12 级号那一边连经验都没发（实得 {sameLevel.XpForKiller}）");
        Assert.True(higher.XpForKiller > 0, $"16 级号这一边没拿到经验（实得 {higher.XpForKiller}）：等级差没传进账本");
        Assert.True(higher.XpForKiller < sameLevel.XpForKiller,
            $"16 级号拿到 {higher.XpForKiller}，12 级号拿到 {sameLevel.XpForKiller}：衰减在端到端这一层没生效");
        Assert.Equal(-1, tooHigh.XpForKiller);   // 灰名断供：算出 0 就**不发事件**（发一条 0 经验只会多一个分支）
        Assert.True(tooHigh.KillSeen && tooHigh.LootEvents == 1 && tooHigh.LootItemCount == 2,
            "断供只该砍掉经验这一项：击杀事件与掉落必须照常（等级高≠这只怪不该死）");
        Assert.True(sameLevel.DamagePackets >= 2 && higher.DamagePackets >= 2 && tooHigh.DamagePackets >= 2,
            "三档都得真的打死过，否则这条比较是在比三个空结果");
    }

    // ---------------- 打断读条（玩家踢断；内核那条分支以前没有任何调用方能触发） ----------------

    private enum InterruptScenario { Kickable, NeverKicked, Untouchable }

    private static (int Starts, int Stops, int StopReason, bool PlayerHurt, int Accepted, int RejectedNotInterruptible, int RejectedNoCast, int RejectedOutOfRange)
        RunInterruptScenario(InterruptScenario scenario)
    {
        const int templateId = 12900;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "可打断检验兽",
            Rank = MonsterRank.Normal,
            LevelBase = 12,
            LevelMin = 12,
            LevelMax = 12,
            ModelKey = "lu_duan",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 30, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, null, null, null, null, null);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 25f;
        behavior.LeashRadius = 80f;
        // 移速钉成 0：这只怪**永远走不到**近身射程，所以玩家身上出现的任何伤害都只能来自那条读条。
        // 没有这一条，"打断后玩家没掉血"就分不清是踢断了、还是它根本没打到。
        behavior.WalkSpeed = 0f;
        behavior.MoveSpeed = 0f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0.05f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 21001,
                Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = 10000,
                CastMs = 3000,                  // 3 秒读条：留出足够的踢人窗口
                CooldownMs = 200,
                MaxRange = 30f,
                SchoolMask = Horizon.Game.Message.Sync.MonsterSchoolBits.Water,
                Interruptible = scenario != InterruptScenario.Untouchable,
                AllowMovement = true,
                DamageScale = 3f,
            },
        };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 25f;
            point.LeashRadius = 80f;
            point.MoveSpeed = 0f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 3,
                // 踢的距离沿用玩家的攻击射程（本作没有单独的"脚踢射程"配置项，玩家的普攻本来就是远程的）。
                // "玩家不会挨近战那一下"的隔离**不靠这个数**，靠的是上面把怪的移速钉成 0 而它的近战射程是 2.5 米。
                PlayerAttackRange = 20f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家站在 14 米：在索敌半径与施法射程内，但在近身射程外（怪又走不动）
            Assert.True(world.AddPlayer(8001, 8001, 14f, 0f, 0f, out var reason), reason);

            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            var starts = 0;
            var stops = 0;
            var stopReason = -1;
            var kicked = false;

            for (var i = 0; i < 700; i++)
            {
                var result = world.Tick();
                foreach (var hit in HarvestCastEvents(result))
                {
                    if (hit.Event.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterCastStart) starts++;
                    else
                    {
                        stops++;
                        stopReason = (int)hit.Event.IntValue;
                    }
                }

                // 第一次看到起手就踢（Untouchable 那一路也要走到这里，才能证明它是被**拒**而不是被无视）
                if (!kicked && starts >= 1 && scenario != InterruptScenario.NeverKicked)
                {
                    kicked = true;
                    world.SubmitCombatAction(8001L, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = 8001UL,
                        TargetId = monsterId,
                        SkillId = 0,
                        ClientTick = 1,
                        AttackerYaw = 0f,
                        ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Interrupt,
                    });
                }

                if (stops >= 1) break;
            }

            Assert.True(world.TryGetPlayer(8001L, out var player) && player != null);
            return (starts, stops, stopReason,
                PlayerHurt: player!.Hp < player.MaxHp,
                Accepted: world.MonsterInterruptAcceptedCount,
                RejectedNotInterruptible: world.MonsterInterruptRejectedNotInterruptibleCount,
                RejectedNoCast: world.MonsterInterruptRejectedNoCastCount,
                RejectedOutOfRange: world.MonsterInterruptRejectedOutOfRangeCount);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 玩家能踢断可打断的读条而踢不动的必须当场被拒并计数()
    {
        // 内核里那条 interruptSignal 分支从写下起就没有调用方（宿主把信号钉成 false），
        // 于是"打断"在实机上等于不存在：怪的条子只能等它自己放完。
        // 三档一起跑，缺任何一个对照这条判据都会空转：
        //   可打断 + 踢   → 必须收一条"被打断"的条子，且玩家一点血都没掉
        //   可打断 + 不踢 → 必须放完并打中人（证明上一条里的"没掉血"是踢断的功劳，不是它本来就不疼）
        //   不可打断 + 踢 → 必须被**拒**并计数，且照样放完打中人（证明拒绝不是把动作悄悄吞掉）
        var kicked = RunInterruptScenario(InterruptScenario.Kickable);
        Assert.True(kicked.Starts >= 1, "整条链没有一次起手，后面都无从判起");
        Assert.True(kicked.Accepted == 1,
            $"踢不动：受理 {kicked.Accepted}／拒-不可打断 {kicked.RejectedNotInterruptible}／" +
            $"拒-没在读条 {kicked.RejectedNoCast}／拒-够不着 {kicked.RejectedOutOfRange}");
        Assert.Equal(0, kicked.RejectedOutOfRange);
        Assert.Equal(0, kicked.RejectedNotInterruptible);
        Assert.Equal(1, kicked.Stops);
        Assert.Equal(Horizon.Game.Message.Sync.MonsterCastEndReason.Interrupted, kicked.StopReason);
        Assert.False(kicked.PlayerHurt, "读条被踢断了玩家却还是挨了这一下：结算没跟着条子一起取消");

        var control = RunInterruptScenario(InterruptScenario.NeverKicked);
        Assert.Equal(0, control.Accepted);
        Assert.True(control.Stops >= 1, "不踢的那一路居然没放完");
        Assert.Equal(Horizon.Game.Message.Sync.MonsterCastEndReason.Completed, control.StopReason);
        Assert.True(control.PlayerHurt, "对照那一路都没打中人：那「踢断后没掉血」根本不是在比同一件事");

        var untouchable = RunInterruptScenario(InterruptScenario.Untouchable);
        Assert.Equal(0, untouchable.Accepted);
        Assert.Equal(1, untouchable.RejectedNotInterruptible);
        Assert.Equal(0, untouchable.RejectedNoCast);   // 它确实在读条，拒的原因是"不可打断"而不是"没在读条"
        Assert.Equal(Horizon.Game.Message.Sync.MonsterCastEndReason.Completed, untouchable.StopReason);
        Assert.True(untouchable.PlayerHurt, "不可打断的那条被踢之后居然停了：这条闸门把配置勾掉的东西又放行了");
    }

    // ---------------- 尸体窗口（corpse_seconds 的第一批消费者） ----------------

    /// <summary>
    /// 造一份只含"一只会挨打的怪"的内容快照：尸体秒数与刷新秒数由用例决定，
    /// 这样"窗口比刷新短/长/为零"三种边界都能在同一套断言里量出来。
    /// </summary>
    private static string CorpseFixture(double corpseSeconds, double respawnBaseSeconds = 5.0,
                                        double lootWindowSeconds = 60.0, double pickupDistanceMeters = 5.5)
    {
        var path = Path.Combine(Path.GetTempPath(), "monster_corpse_" + Guid.NewGuid().ToString("N") + ".json");
        var json = CorpseFixtureTemplate
            .Replace("__CORPSE__", corpseSeconds.ToString(CultureInfo.InvariantCulture))
            .Replace("__LOOT__", lootWindowSeconds.ToString(CultureInfo.InvariantCulture))
            .Replace("__DIST__", pickupDistanceMeters.ToString(CultureInfo.InvariantCulture))
            .Replace("__RESPAWN__", respawnBaseSeconds.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(path, json);
        return path;
    }

    private const string CorpseFixtureTemplate = """
{"contentRevision":1,"templates":[
  {"monsterId":12700,"name":"尸体测试怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":__CORPSE__,
   "lootWindowSeconds":__LOOT__,"pickupDistanceMeters":__DIST__,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":10,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":__RESPAWN__,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":0,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":12700,"maxAlive":1,"slotSpacing":6}
]}
""";

    /// <summary>尸体用例的世界：一个玩家站在怪前 2 米（够得着、且怪不会主动扑上来）。</summary>
    private static DsWorldSession MakeCorpseWorld(out long sessionId, bool aoiFilter = false, float respawnSeconds = 5f)
    {
        var point = SpawnPointConfig.Default;
        point.SpawnPointId = 1;
        point.MonsterTemplateId = 12700;     // 刻意不用 100：那是走代码目录的用例（DsCombatLoopTests）的编号空间，
                                             // 内容注册表是**进程级静态**，撞号会让两个并行测试类互相看到对方的配置。
        point.MaxAlive = 1;
        point.X = 0f;
        point.Y = 0f;
        point.Z = 0f;
        point.SlotSpacing = 6f;
        point.AggroRadius = 1f;      // 被动：只有挨打才还手，死亡时序才只由测试掌握
        point.LeashRadius = 20f;
        point.MoveSpeed = 4f;
        // 刷新延迟取**点位**这一份：内容包的 respawnBaseSeconds 只投影进 AI 配置
        // （BuildAiConfigFromContent 是局部副本），排重生槽位用的是刷怪点自己的值。
        // 这里不显式写就会静默用 SpawnPointConfig.Default 的 15 秒，用例里的时间窗全是错的。
        point.RespawnDelaySeconds = respawnSeconds;

        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 3,
            EnableAoiFilter = aoiFilter,
            PlayerAttackPower = 60f,   // 两下打死 100 血，不用跑到几百 tick
        });
        world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
        sessionId = 2001;
        Assert.True(world.AddPlayer(sessionId, 2001, -2f, 0f, 0f, out var reason), reason);
        return world;
    }

    /// <summary>真刀真枪地打死它（走 HandleMonsterDeath，而不是测试专用的 MarkMonsterDead 入口）。</summary>
    private static void KillMonsterByCombat(DsWorldSession world, long sessionId, ulong monsterId)
    {
        for (var i = 0; i < 60; i++)
        {
            Assert.True(world.TryGetMonster(monsterId, out var m) && m != null, "第 " + i + " 下之前怪就不在世界里了");
            if (m!.Ai.Hp <= 0) return;
            world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001UL,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                AttackerYaw = 0f,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            world.Tick();
        }
        Assert.True(false, "打了 60 下还没死：这套尸体断言的死亡时序不成立");
    }

    [Fact]
    public void 挨打的那一拍给怪物记下风筝宽限窗起点而死亡会把它清零()
    {
        // 宽限窗的**唯一写方**是伤害落点（AC 在伤害结算里 UpdateLeashExtensionTime）。
        // 内核两条用例只证明"窗开着时内核会免检"，证明不了"这条窗真的会被打开"——
        // 这两层必须各断一条，否则宿主那一行删掉之后内核照样全绿。
        var path = CorpseFixture(30.0, 5.0);      // 尸体 30 秒：死亡后那只还留在世界里，才读得到"清零"这一格
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var world = MakeCorpseWorld(out var sessionId, respawnSeconds: 5f);
            world.Tick();   // 生成
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            Assert.True(world.TryGetMonster(monsterId, out var before) && before != null, "怪没生成");
            Assert.Equal(0L, before!.Ai.LastLeashExtensionTick);   // 没挨打 ⇒ "从未"，不是"tick 0 挨过"

            world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001UL,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                AttackerYaw = 0f,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            var hitTick = world.ServerTick;
            Assert.True(world.TryGetMonster(monsterId, out var hit) && hit != null);
            Assert.Equal(hitTick, hit!.Ai.LastLeashExtensionTick);
            // 机制层：这一刀确实把"宽限窗被打开"记了一次（0 时"没挨打"与"没接"同形）。
            Assert.Equal(1, world.MonsterLeashExtensions);

            KillMonsterByCombat(world, sessionId, monsterId);
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null);
            Assert.True(dead!.Ai.Hp <= 0, "怪没死——下面那条'死亡清零'会空转");
            Assert.Equal(0L, dead.Ai.LastLeashExtensionTick);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>取该实体在**下一条快照**里的那份 delta（共享路径与 AOI 分组路径都覆盖）。</summary>
    private static Horizon.Game.Message.Sync.EntityDelta NextMonsterDelta(
        DsWorldSession world, ulong monsterId, int maxTicks = 30)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var r = world.Tick();
            if (r.WorldSnapshot != null)
            {
                foreach (var d in r.WorldSnapshot.Deltas)
                    if (d.EntityId == monsterId && d.State.HasValue) return d;
            }
            if (r.SnapshotGroups != null)
            {
                foreach (var g in r.SnapshotGroups)
                    foreach (var d in g.Snapshot.Deltas)
                        if (d.EntityId == monsterId && d.State.HasValue) return d;
            }
        }
        throw new Xunit.Sdk.XunitException("跑了 " + maxTicks + " tick，两条下发路径里都没有这个实体的状态 delta");
    }

    [Fact]
    public void 真打死的怪会作为尸体留在世界里并在到期后才消失()
    {
        // 接入本功能前：怪一死就 Despawn（HP 从 100 直接跳到"从没存在过"），
        // 玩家看到的是"倒地动画都没有就人间蒸发"，而库里 corpse_seconds=60 一直是死的。
        var path = CorpseFixture(2.0);          // 尸体 2 秒（120 tick），刷新 5 秒（300 tick）——窗口比刷新短
        try
        {
            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join("; ", receipt.Problems));

            var world = MakeCorpseWorld(out var sessionId);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            KillMonsterByCombat(world, sessionId, monsterId);

            const uint dead = Horizon.Game.Message.Sync.Components.EntityStateBits.Dead;
            Assert.Equal(1, world.MonsterCount);
            Assert.Equal(1, world.MonsterCorpseCount);
            Assert.Equal(1, world.MonsterCorpsesLingering);

            var delta = NextMonsterDelta(world, monsterId);
            Assert.NotEqual(Horizon.Game.Message.Sync.EntityDeltaKind.Despawn, delta.Kind);
            Assert.Equal(0, delta.State!.Value.Health);
            Assert.True((delta.State!.Value.StateBits & dead) != 0,
                        "尸体的 delta 没带死亡位：客户端分不清残血和尸体，名牌照挂、动画照播");

            // 尸体打不到（以前根本没有这个状态：实体已不在世界里，返回的是 Invalid）
            var res = world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
            {
                AttackerId = 2001UL,
                TargetId = monsterId,
                SkillId = 0,
                ClientTick = 1,
                ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
            });
            Assert.Equal(Horizon.Game.Sim.CombatOutcome.TargetDead, res.Outcome);

            // 窗口内不得消失（死亡那一 tick 之后再过 110 tick，仍在 120 之内）
            for (var i = 0; i < 110; i++) world.Tick();
            Assert.Equal(1, world.MonsterCorpseCount);
            Assert.Equal(1, world.MonsterCount);

            // 到期：必须像玩家离线那样**全广播** Despawn，否则不在同一 chunk 的观察者留着永久尸体
            var sawFade = false;
            for (var i = 0; i < 20; i++)
            {
                var r = world.Tick();
                if (r.DespawnBroadcast == null) continue;
                if (r.DespawnBroadcast.Deltas.Any(d => d.EntityId == monsterId
                                                      && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Despawn))
                {
                    sawFade = true;
                    break;
                }
            }
            Assert.True(sawFade, "过了 corpse_seconds 仍没广播 Despawn：尸体会永久留在屏幕上");
            Assert.Equal(0, world.MonsterCorpseCount);
            Assert.Equal(0, world.MonsterCount);

            // 重生（同一实体 ID）必须不带上一世的死亡位——这是复用 ID 最容易漏的一格
            for (var i = 0; i < 400 && world.MonsterCount == 0; i++) world.Tick();
            Assert.Equal(1, world.MonsterCount);
            var revived = NextMonsterDelta(world, monsterId);
            Assert.Equal(0u, revived.State!.Value.StateBits & dead);
            Assert.True(revived.State!.Value.Health > 0, "重生还挂着 0 血：这一世根本没法打");
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 权威拾取列表的六种结果分得开而取走才改位图()
    {
        // 2026-10-28 拍板：拾取列表归 Orleans。这一条测的是 Grain 将要持有的那个状态容器的判据，
        // 六种结果必须彼此可分——压成 bool 的话"窗口关了"会被报成"你已经拿过了"，玩家会去找不存在的上一个人。
        var roll = new MonsterLootRollResult();
        roll.Drops.Add(new MonsterLootDrop(itemId: 2001, count: 2, conditionKind: 0, questId: 0, bindingIndex: 0, entryIndex: 0));
        roll.ConditionalDrops.Add(new MonsterLootDrop(2002, 1, 1, 90007, 0, 1));
        roll.Money = 37;

        var state = MonsterCorpseLootState.FromRoll(roll, expireTick: 1200, pickupDistanceMeters: 5.5f);
        Assert.Equal(3, state.Slots.Count);                 // 普通件 + 条件件 + 金币
        Assert.True(state.Slots[2].IsMoney);
        Assert.Equal(37, state.Slots[2].Money);

        // 取走：改位图、拿到槽内容（这对名单为空 = 不限制，谁都取得到）
        Assert.Equal(LootTakeResult.Taken, state.TryTake(0, 700, 3f, 77UL, out var first));
        Assert.Equal(2001, first!.ItemId);
        Assert.Equal(2, first.Count);
        Assert.True(MonsterLootPickupCore.IsTaken(state.TakenMask, 0));

        // 重复取：AlreadyTaken，位图不变
        var maskAfterFirst = state.TakenMask;
        Assert.Equal(LootTakeResult.AlreadyTaken, state.TryTake(0, 700, 3f, 77UL, out _));
        Assert.Equal(maskAfterFirst, state.TakenMask);

        // 太远：TooFar（窗口还开着），且**不改**位图
        Assert.Equal(LootTakeResult.TooFar, state.TryTake(1, 700, 5.6f, 77UL, out _));
        Assert.Equal(maskAfterFirst, state.TakenMask);
        // 条件槽：没这个任务的人取不了，且**不消耗**（2026-10-30 收口：needs_quest 判在槽一级，不再只判入包）
        Assert.Equal(LootTakeResult.ConditionNotMet, state.TryTake(1, 700, 5.5f, 77UL, out _));
        Assert.Equal(maskAfterFirst, state.TakenMask);
        Assert.Equal(LootTakeResult.Taken,
            state.TryTake(1, 700, 5.5f, 77UL, out var second, heldQuestIds: new[] { 90007 }));   // 正好 5.5 米算够得着
        Assert.Equal(90007, second!.QuestId);

        // 越界：不是"空"，也不是"已取"
        Assert.Equal(LootTakeResult.InvalidSlot, state.TryTake(-1, 700, 1f, 77UL, out _));
        Assert.Equal(LootTakeResult.InvalidSlot, state.TryTake(state.Slots.Count, 700, 1f, 77UL, out _));

        // 到期那一拍：即便那个槽没被取过、人站在脚边，也必须报 Expired（过期优先于其它判断）
        Assert.Equal(LootTakeResult.Expired, state.TryTake(2, 1200, 0f, 77UL, out _));
        // 窗口配 0（取不了）
        state.ExpireTick = 0;
        Assert.Equal(LootTakeResult.Expired, state.TryTake(2, 1, 0f, 77UL, out _));

        // 空尸体：Empty（与"越界"分开）
        var empty = MonsterCorpseLootState.FromRoll(new MonsterLootRollResult(), 1200, 5.5f);
        Assert.Equal(LootTakeResult.Empty, empty.TryTake(0, 1, 1f, 77UL, out _));
        Assert.True(empty.IsEmpty);

        // 剩余计数：取走两个后还剩金币那一个
        state.ExpireTick = 1200;
        Assert.Equal(1, state.Remaining);
        Assert.False(state.IsEmpty);

        // 从只读输入复制：外部列表事后改动不能影响已建好的状态
        var slots = new List<MonsterCorpseLootState.Slot> { new() { ItemId = 3001, Count = 1 } };
        var copied = MonsterCorpseLootState.FromSlots(slots, 0UL, 1200, 5.5f);
        slots[0].ItemId = 9999;
        slots.Add(new MonsterCorpseLootState.Slot { ItemId = 4004, Count = 1 });
        Assert.Equal(1, copied.Slots.Count);
        Assert.Equal(3001, copied.Slots[0].ItemId);
    }

    [Fact]
    public void 同一具尸体的同一个槽只可能被取走一次()
    {
        // 权威侧原语（2026-10-28 拍板：列表归 Orleans）：一槽一取，重复取不改状态。
        // 这条是"两个人同时拾取同一槽只允许一个成功"的离线形态——Grain 那层只负责持锁，判据在这里。
        ulong taken = 0;
        Assert.Equal(MonsterLootPickupCore.PickupOutcome.Taken, MonsterLootPickupCore.TryTake(ref taken, 0));
        Assert.True(MonsterLootPickupCore.IsTaken(taken, 0));

        var before = taken;
        Assert.Equal(MonsterLootPickupCore.PickupOutcome.AlreadyTaken, MonsterLootPickupCore.TryTake(ref taken, 0));
        Assert.Equal(before, taken);   // 重复取**不许**改位图（否则对账会多记一次）

        // 越界：既不是"已取"也不是"取到"，位图也不动（静默当空尸体是最坏的一种）
        Assert.Equal(MonsterLootPickupCore.PickupOutcome.InvalidSlot, MonsterLootPickupCore.TryTake(ref taken, -1));
        Assert.Equal(MonsterLootPickupCore.PickupOutcome.InvalidSlot,
            MonsterLootPickupCore.TryTake(ref taken, MonsterLootPickupCore.MaxSlots));
        Assert.Equal(before, taken);

        // 最高槽可用；计数按 totalSlots 钳到上限，不越界读位图
        Assert.Equal(MonsterLootPickupCore.PickupOutcome.Taken,
            MonsterLootPickupCore.TryTake(ref taken, MonsterLootPickupCore.MaxSlots - 1));
        Assert.Equal(MonsterLootPickupCore.MaxSlots - 2,
            MonsterLootPickupCore.RemainingCount(taken, MonsterLootPickupCore.MaxSlots));
        Assert.Equal(MonsterLootPickupCore.MaxSlots - 2, MonsterLootPickupCore.RemainingCount(taken, 999));
        Assert.False(MonsterLootPickupCore.IsEmpty(taken, MonsterLootPickupCore.MaxSlots));
        Assert.True(MonsterLootPickupCore.IsEmpty(taken, 1));      // 只取了 0 号槽 ⇒ "总共只有 0 号槽"这具尸体算取空
        Assert.False(MonsterLootPickupCore.IsEmpty(taken, 2));     // 1 号槽没取过
        Assert.True(MonsterLootPickupCore.IsEmpty(0UL, 0));        // 空尸体算取空
    }

    [Fact]
    public void 开了尸体拾取后尸体会带可交互位留在注册表而未开时照旧当场摘除()
    {
        // 这是客户端 G 键能选到尸体的**前置**：死亡时如果照旧发 removed=true，
        // 客户端注册表里根本没有它，后面做什么 UI 都点不到。
        // 两个世界同夹具、只差灰度开关——这样"留表"与"摘除"的差别只能归因给那个开关。
        static (bool Removed, bool CorpseInteractable, bool Seen) Run(bool defer)
        {
            var path = CorpseFixture(30.0, respawnBaseSeconds: 120.0, lootWindowSeconds: 30.0);
            try
            {
                Assert.True(MonsterContentLoader.LoadFile(path).Ok);
                var point = SpawnPointConfig.Default;
                point.SpawnPointId = 1;
                point.MonsterTemplateId = 12700;
                point.MaxAlive = 1;
                point.X = 0f; point.Y = 0f; point.Z = 0f;
                point.AggroRadius = 1f;
                point.LeashRadius = 20f;
                point.MoveSpeed = 4f;
                point.RespawnDelaySeconds = 120f;
                var world = new DsWorldSession(new DsWorldSessionOptions
                {
                    ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                    DeferCorpseLootToAuthority = defer,
                });
                world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
                const long sessionId = 2001;
                Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
                world.Tick();
                var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
                // 描述符每拍出会话一次就被清空，所以必须在循环里累计——死亡那条正好搭下一班车。
                var seen = false;
                var removed = false;
                var corpseInteractable = false;
                for (var i = 0; i < 60; i++)
                {
                    if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                    world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                        AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                    });
                    foreach (var d in world.Tick().EntityDescriptors)
                    {
                        if (d.EntityId != monsterId) continue;
                        seen = true;
                        removed = d.Removed;
                        corpseInteractable = d.CorpseInteractable;
                    }
                }
                return (removed, corpseInteractable, seen);
            }
            finally
            {
                File.Delete(path);
                MonsterContent.ResetToHardcoded();
            }
        }

        var deferred = Run(defer: true);
        Assert.True(deferred.Seen, "开灰度后没看到这只怪的描述符");
        Assert.False(deferred.Removed, "开了 corpse loot 却在死亡时把尸体从注册表摘除了：客户端永远点不到它");
        Assert.True(deferred.CorpseInteractable, "描述符没带 CorpseInteractable 位");

        var legacy = Run(defer: false);
        Assert.True(legacy.Seen, "未开灰度时没看到描述符");
        Assert.True(legacy.Removed, "未开灰度时行为变了：死亡仍应照旧当场摘除");
        Assert.False(legacy.CorpseInteractable);
    }

    [Fact]
    public void 尸体可拾取位要进快照而不是只进网关描述符()
    {
        // 客户端只看得见**快照**，看不见网关那份实体描述符：只把 CorpseInteractable 写进描述符，
        // 玩家永远点不到尸体（"尸体还在但点不了"）。所以这一位必须由快照状态位带下去，
        // 并且在**拾取窗口关掉**那一刻就收（尸体可能还在地上 30 秒，但它已经取不了了）。
        static (bool HasBitAtDeath, bool HasBitAfterWindow, bool DeadAtDeath, bool PresentAfterWindow) Run(bool aoi, bool defer)
        {
            // 夹具必须有掉落：**没东西的尸体本就不该带可拾取位**（空列表不登记）。
            // 所以拿拾取链那份夹具改两处（窗口 60→5 秒、模板号换一个），而不是新写一份掉落形状。
            var path = Path.Combine(Path.GetTempPath(), "monster_bit_" + Guid.NewGuid().ToString("N") + ".json");
            var json = CorpseLootPickupFixture
                .Replace("\"lootWindowSeconds\":60.0", "\"lootWindowSeconds\":5.0")
                .Replace("12900", "13500");
            File.WriteAllText(path, json);
            try
            {
                Assert.True(MonsterContentLoader.LoadFile(path).Ok);
                var point = SpawnPointConfig.Default;
                point.SpawnPointId = 1;
                point.MonsterTemplateId = 13500;
                point.MaxAlive = 1;
                point.X = 0f; point.Y = 0f; point.Z = 0f;
                point.AggroRadius = 1f;
                point.LeashRadius = 20f;
                point.MoveSpeed = 4f;
                point.RespawnDelaySeconds = 120f;
                var world = new DsWorldSession(new DsWorldSessionOptions
                {
                    ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 1, PlayerAttackPower = 60f,
                    DeferCorpseLootToAuthority = defer, EnableAoiFilter = aoi,
                });
                world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
                const long sessionId = 2001;
                Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
                world.Tick();
                var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
                for (var i = 0; i < 60; i++)
                {
                    if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                    world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                        AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                    });
                    world.Tick();
                }
                Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");

                var atDeath = NextMonsterDelta(world, monsterId);
                Assert.True(atDeath.State.HasValue, "死亡那一拍没有带 State 的 delta");
                var bitsAtDeath = atDeath.State!.Value.StateBits;

                // 窗口 5 秒 = 300 拍：跑过去之后尸体还在（30 秒），但可拾取位必须已经收掉。
                while (world.ServerTick < 420) world.Tick();
                var present = world.TryGetMonster(monsterId, out var lingering) && lingering != null;
                var afterWindow = present ? NextMonsterDelta(world, monsterId) : default;
                var bitsAfter = afterWindow.State?.StateBits ?? 0u;
                return ((bitsAtDeath & Horizon.Game.Message.Sync.Components.EntityStateBits.CorpseInteractable) != 0,
                        (bitsAfter & Horizon.Game.Message.Sync.Components.EntityStateBits.CorpseInteractable) != 0,
                        (bitsAtDeath & Horizon.Game.Message.Sync.Components.EntityStateBits.Dead) != 0,
                        present);
            }
            finally
            {
                File.Delete(path);
                MonsterContent.ResetToHardcoded();
            }
        }

        foreach (var aoi in new[] { false, true })
        {
            var on = Run(aoi, defer: true);
            Assert.True(on.DeadAtDeath, $"EnableAoiFilter={aoi}：死亡位都没带，后面的可拾取位无从谈起");
            Assert.True(on.HasBitAtDeath, $"EnableAoiFilter={aoi}：快照里没有可拾取位 ⇒ 客户端点不到尸体");
            Assert.True(on.PresentAfterWindow, $"EnableAoiFilter={aoi}：5 秒后尸体就没了（夹具前提不成立，窗口那条测不到）");
            Assert.False(on.HasBitAfterWindow, $"EnableAoiFilter={aoi}：拾取窗口关了可拾取位还亮着 ⇒ 玩家对着取不了的尸体按 G");

            var legacy = Run(aoi, defer: false);
            Assert.True(legacy.DeadAtDeath);
            Assert.False(legacy.HasBitAtDeath, $"EnableAoiFilter={aoi}：未开灰度（击杀即发）却带可拾取位");
        }
    }

    [Fact]
    public void 尸体拾取窗口短于尸体停留时尸体还在但已经取不了()
    {
        // 2026-10-28 拍板：窗口 60 秒、距离 5.5 米、可逐怪配；权威在 Orleans，DS 这一层负责门槛。
        // 这个夹具刻意让 **窗口(5s) < 尸体(30s) < 刷新(120s)**，才能造出"尸体还在、但取不了"那一段——
        // 三者相等时"窗口过期"与"尸体消失"同形，测不出窗口自己有没有生效。
        var path = CorpseFixture(30.0, respawnBaseSeconds: 120.0, lootWindowSeconds: 5.0);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var world = MakeCorpseWorld(out var sessionId, respawnSeconds: 120f);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, sessionId, monsterId);
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null);

            // 两格都由同一处死亡拍算出来，差正好是"尸体 30 秒 − 窗口 5 秒"
            Assert.Equal(25 * 60L, dead!.CorpseExpireAtTick - dead.CorpseLootExpireAtTick);
            Assert.Equal(5.5f, dead.PickupDistanceMeters);

            // 窗口内：站得近能取、站远 0.1 米就 TooFar（判据与客户端提示同源）
            Assert.True(world.CanLootCorpse(monsterId, 5.5f, out var near));
            Assert.Equal(MonsterCorpseCore.PickupDenial.None, near);
            Assert.False(world.CanLootCorpse(monsterId, 5.6f, out var far));
            Assert.Equal(MonsterCorpseCore.PickupDenial.TooFar, far);

            // 推到窗口之后：尸体**仍在世界里**（还没到收尸 tick），但拾取必须已过期
            while (world.ServerTick < dead.CorpseLootExpireAtTick) world.Tick();
            Assert.True(world.TryGetMonster(monsterId, out var stillThere) && stillThere != null,
                "窗口过期那一刻尸体就已经没了——这个夹具要点出的正是'尸体还在但取不了'");
            Assert.True(stillThere!.CorpseExpireAtTick > world.ServerTick);
            Assert.False(world.CanLootCorpse(monsterId, 1f, out var expired));
            Assert.Equal(MonsterCorpseCore.PickupDenial.Expired, expired);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 网关处理器把登记转发回执串成一条链()
    {
        // 2026-10-28 拍板：权威在 Orleans。这一条钉网关那段映射：帧 → 存储 → 回执，
        // 且回执必须带原请求号（配不上对，DS 那边就只能超时丢弃，玩家点了没反应）。
        var store = new InMemoryCorpseLootStore();
        var handler = new Horizon.Game.Core.Sim.Server.CorpseLootUplinkHandler(store);

        var registration = new Horizon.Game.Core.Sim.Server.DsCorpseLootRegister
        {
            CorpseEntityId = 7UL, ExpireTick = 1200, PickupDistanceMeters = 5.5f,
            Slots = new[]
            {
                new Horizon.Game.Core.Sim.Server.CorpseLootSlotWire { ItemId = 555, Count = 2 },
                new Horizon.Game.Core.Sim.Server.CorpseLootSlotWire { ItemId = 0, Count = 1, Money = 10 },
            },
        };
        Assert.True(await handler.HandleRegisterAsync(registration));
        Assert.Equal(1, store.RegisterCount);
        Assert.Equal(1, handler.RegisteredCount);

        // 空登记不该被当成"成功登记了一具空尸体"（那会让玩家看到一具点不开的尸体）
        Assert.False(await handler.HandleRegisterAsync(new Horizon.Game.Core.Sim.Server.DsCorpseLootRegister { CorpseEntityId = 8UL }));

        var forward = new Horizon.Game.Core.Sim.Server.DsCorpseLootForward
        {
            RequestId = 9001, CorpseEntityId = 7UL, SlotIndex = 0, ServerTick = 700,
        };
        var taken = await handler.HandleForwardAsync(forward);
        Assert.Equal(9001, taken.RequestId);
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, taken.Status);
        Assert.Equal(555, taken.ItemId);
        Assert.Equal(2, taken.Count);

        // 重复请求同一槽：AlreadyTaken，且回执仍带原请求号
        var again = await handler.HandleForwardAsync(forward);
        Assert.Equal(9001, again.RequestId);
        Assert.Equal((byte)CorpseLootTakeStatus.AlreadyTaken, again.Status);

        // 金币槽
        var money = await handler.HandleForwardAsync(new Horizon.Game.Core.Sim.Server.DsCorpseLootForward
        {
            RequestId = 9002, CorpseEntityId = 7UL, SlotIndex = 1, ServerTick = 700,
        });
        Assert.Equal((byte)CorpseLootTakeStatus.Taken, money.Status);
        Assert.Equal(10, money.Money);

        // 作废之后：UnknownCorpse（而不是"空了"）
        await handler.HandleGoneAsync(new Horizon.Game.Core.Sim.Server.DsCorpseLootGone { CorpseEntityId = 7UL });
        Assert.Equal(0, store.Count);
        var afterGone = await handler.HandleForwardAsync(forward);
        Assert.Equal((byte)CorpseLootTakeStatus.UnknownCorpse, afterGone.Status);
    }

    [Fact]
    public async Task 拾取链从客户端入口到入包与结果包在进程内走通一遍()
    {
        // 各段此前都是分开测的（DS 入口 / 网关处理器 / Grain 状态机 / 下发包）。
        // 这条把它们**按真实顺序串起来**跑一遍——接缝处的错误（参数意义、会话号、槽序）分开测都看不出来。
        var store = new InMemoryCorpseLootStore();
        var gateway = new Horizon.Game.Core.Sim.Server.CorpseLootUplinkHandler(store);
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_e2e_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                world.Tick();
            }
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");

            // ① DS 登记帧 → 网关处理器 → 权威存储
            var regs = world.DrainCorpseLootRegistrations();
            Assert.Single(regs);
            Assert.True(await gateway.HandleRegisterAsync(regs[0]));
            Assert.Equal(1, store.Count);

            // ② 客户端入口（点槽 0）→ 转发帧 → 网关处理器问权威 → 回执 → DS 结账
            var status = world.HandleCorpseLootTakeFromClient(sessionId, monsterId, 0, out var denial);
            Assert.Equal(CorpseLootTakeStatus.Taken, status);
            Assert.Null(denial);
            var forwards = world.DrainCorpseLootForwards();
            Assert.Single(forwards);
            var reply = await gateway.HandleForwardAsync(forwards[0]);
            Assert.Equal((byte)CorpseLootTakeStatus.Taken, reply.Status);
            Assert.Equal(555, reply.ItemId);

            Assert.True(world.HandleCorpseLootResult(reply));
            var tickResult = world.Tick();
            // ③ 入包事件（业务层据此进背包）
            var grants = tickResult.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            Assert.Single(grants);
            Assert.Equal(555, grants[0].ItemId);
            Assert.Equal(2, grants[0].ItemCount);
            Assert.Equal(2001UL, grants[0].ActorCharacterId);
            // ④ 结果包下行给发起会话（客户端据此弹"获得 X"）
            var resultPackets = tickResult.Events
                .Select(e => e.Packet)
                .OfType<Horizon.Game.Message.Sync.CorpseLootTakeResultPacket>()
                .ToList();
            Assert.Single(resultPackets);
            Assert.Equal(monsterId, resultPackets[0].CorpseEntityId);
            Assert.Equal(0, resultPackets[0].SlotIndex);
            Assert.Equal((byte)CorpseLootTakeStatus.Taken, resultPackets[0].Status);
            Assert.Equal(555, resultPackets[0].ItemId);

            // ⑤ 「取下一件」（槽号 -1）：权威侧按槽序给下一个（金币槽），**回执带实际槽号**
            Assert.Equal(CorpseLootTakeStatus.Taken, world.HandleCorpseLootTakeFromClient(sessionId, monsterId, -1, out _));
            var nextForward = world.DrainCorpseLootForwards();
            Assert.Single(nextForward);
            Assert.Equal(-1, nextForward[0].SlotIndex);
            var nextReply = await gateway.HandleForwardAsync(nextForward[0]);
            Assert.Equal((byte)CorpseLootTakeStatus.Taken, nextReply.Status);
            Assert.Equal(1, nextReply.SlotIndex);          // 金币在 1 号槽
            Assert.Equal(10, nextReply.Money);
            Assert.True(world.HandleCorpseLootResult(nextReply));
            var moneyTick = world.Tick();
            Assert.Single(moneyTick.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.KillMoney));
            var moneyPacket = moneyTick.Events.Select(e => e.Packet)
                .OfType<Horizon.Game.Message.Sync.CorpseLootTakeResultPacket>().Single();
            Assert.Equal(1, moneyPacket.SlotIndex);        // 客户端靠这个数知道取到了哪一格
            Assert.Equal(10, moneyPacket.Money);

            // ⑥ 再取下一件：已经取空 ⇒ AlreadyTaken。**玩法不结账**（不发 Loot/KillMoney），
            //    但必须回一条结果包：否则"取空了"这件事只有权威侧知道，玩家屏幕上什么都没发生（步骤 17）。
            Assert.Equal(CorpseLootTakeStatus.Taken, world.HandleCorpseLootTakeFromClient(sessionId, monsterId, -1, out _));
            var drainedForward = world.DrainCorpseLootForwards();
            Assert.Single(drainedForward);
            var drainedReply = await gateway.HandleForwardAsync(drainedForward[0]);
            Assert.Equal((byte)CorpseLootTakeStatus.AlreadyTaken, drainedReply.Status);
            Assert.False(world.HandleCorpseLootResult(drainedReply));
            var quiet = world.Tick();
            Assert.Empty(quiet.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot));
            Assert.Empty(quiet.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.KillMoney));
            var rejectedPacket = quiet.Events.Select(e => e.Packet)
                .OfType<Horizon.Game.Message.Sync.CorpseLootTakeResultPacket>().Single();
            Assert.Equal((byte)CorpseLootTakeStatus.AlreadyTaken, rejectedPacket.Status);
            Assert.Equal(0, rejectedPacket.ItemId);   // 拒绝包不带物品
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 客户端拾取请求在DS侧量距离并把转发与当场拒分开排队()
    {
        // 客户端那一帧只有"哪具尸体、第几个槽"：距离必须由 DS 按权威坐标量（网关不知道玩家位置）。
        // 这一条钉两件事：① 受理时排进转发队列（带会话号）；② 拒绝时排进拒绝队列且**带会话号**——
        // 网关要凭那个号找到连接，丢了它就等于玩家点了没反应。
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_cli_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                world.Tick();
            }
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");
            world.DrainCorpseLootRegistrations();   // 登记帧与本次断言无关，先排空

            var status = world.HandleCorpseLootTakeFromClient(sessionId, monsterId, 0, out var denial);
            Assert.Equal(CorpseLootTakeStatus.Taken, status);   // = 已受理，等权威回执
            Assert.Null(denial);
            var forwards = world.DrainCorpseLootForwards();
            Assert.Single(forwards);
            Assert.Equal(sessionId, forwards[0].SessionId);
            Assert.Equal(monsterId, forwards[0].CorpseEntityId);
            Assert.True(forwards[0].ServerTick > 0);
            Assert.True(Math.Abs(forwards[0].ServerTick - world.ServerTick) <= 1);

            // 尸体到期后再点：当场拒，且拒绝回执带会话号（网关靠它找连接）
            while (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.CorpseExpireAtTick != 0)
                world.Tick();
            var late = world.HandleCorpseLootTakeFromClient(sessionId, monsterId, 0, out var lateDenial);
            Assert.NotEqual(CorpseLootTakeStatus.Taken, late);
            Assert.NotNull(lateDenial);
            Assert.Equal(sessionId, lateDenial!.SessionId);
            // 拒绝不再排上行队列：它由会话直接排成该会话的下行结果包（客户端要立刻看到理由）。
            Assert.True(world.CorpseLootResultPacketsQueued >= 1);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 权威裁决走请求转发回执三段而本地只挡距离与窗口()
    {
        // 2026-10-29 拍板：列表权威在 Orleans。DS 这一侧只做两件事：
        // ① 本地两道门（玩家活着 + 窗口/距离）不过就当场拒；② 过了就转发、等回执，**只有 Taken 才发入包事件**。
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_up_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,   // 没有本地存储，纯走上行三段
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            var lootAtKill = 0;
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                lootAtKill += world.Tick().GameplayEvents.Count(e => e.Kind == DsGameplayEventKind.Loot);
            }
            lootAtKill += world.Tick().GameplayEvents.Count(e => e.Kind == DsGameplayEventKind.Loot);
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");
            Assert.Equal(0, lootAtKill);

            // 登记：一帧，槽序 = 普通件 → 金币
            var regs = world.DrainCorpseLootRegistrations();
            Assert.Single(regs);
            Assert.Equal(monsterId, regs[0].CorpseEntityId);
            Assert.Equal(2, regs[0].Slots.Length);
            Assert.Equal(555, regs[0].Slots[0].ItemId);
            Assert.Equal(10, regs[0].Slots[1].Money);
            Assert.Equal(5.5f, regs[0].PickupDistanceMeters);

            // 本地门：站 100 米 ⇒ 当场拒（不转发），理由与 CanLootCorpse 同源
            var far = new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
            {
                SessionId = sessionId, CorpseEntityId = monsterId, SlotIndex = 0, DistanceMeters = 100f, RequestId = 9101,
            };
            Assert.False(world.HandleCorpseLootRequest(far, out var farForward, out var farDenial));
            Assert.Null(farForward);
            Assert.Equal((byte)CorpseLootTakeStatus.TooFar, farDenial!.Status);

            // 合法请求 ⇒ 转发，且带上 RequestId 与 DS 自己的 tick
            var ok = new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
            {
                SessionId = sessionId, CorpseEntityId = monsterId, SlotIndex = 0, DistanceMeters = 1f, RequestId = 9102,
            };
            Assert.True(world.HandleCorpseLootRequest(ok, out var forward, out var okDenial));
            Assert.Null(okDenial);
            Assert.Equal(9102, forward!.RequestId);
            Assert.Equal(monsterId, forward.CorpseEntityId);
            Assert.Equal(world.ServerTick, forward.ServerTick);

            // 回执 Taken ⇒ 发一条入包事件，记在发起人头上；同一 RequestId 再到达一次不算数
            var taken = new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
            {
                RequestId = 9102, Status = (byte)CorpseLootTakeStatus.Taken,
                ItemId = 555, Count = 2, ConditionKind = 0, QuestId = 0, Money = 0,
            };
            Assert.True(world.HandleCorpseLootResult(taken));
            var grants = world.Tick().GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            Assert.Single(grants);
            Assert.Equal(555, grants[0].ItemId);
            Assert.Equal(2, grants[0].ItemCount);
            Assert.Equal(2001UL, grants[0].ActorCharacterId);

            Assert.False(world.HandleCorpseLootResult(taken));
            Assert.Equal(1, world.CorpseLootResultUnmatched);

            // 尸体到期 ⇒ 发一帧作废，登记账目清干净
            while (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.CorpseExpireAtTick != 0)
                world.Tick();
            var gone = world.DrainCorpseLootGone();
            Assert.Single(gone);
            Assert.Equal(monsterId, gone[0].CorpseEntityId);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 条件件在尸体拾取路径上仍按条件出账且槽序固定()
    {
        // 尸体拾取把掉落从"击杀即发"改成了"取走才发"，于是条件件要走两条新路径：
        // ① 击杀时它必须**进登记**（否则玩家在尸体上永远看不到它，而库/快照里都配着）；
        // ② 取走时它必须还原成 `LootConditional`（否则任务检查被绕过，人人可拿）。
        // 槽序是持久化契约（普通件 → 条件件 → 金币）：今天排错，旧存档的槽号会整体平移。
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_cond_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ConditionalCorpseLootFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            var lootAtKill = 0;
            var conditionalAtKill = 0;
            var moneyAtKill = 0;
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                var ev = world.Tick().GameplayEvents;
                lootAtKill += ev.Count(e => e.Kind == DsGameplayEventKind.Loot);
                conditionalAtKill += ev.Count(e => e.Kind == DsGameplayEventKind.LootConditional);
                moneyAtKill += ev.Count(e => e.Kind == DsGameplayEventKind.KillMoney);
            }
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");
            // 三条下行路径必须**一起**被关掉：只关普通件时，条件件会在击杀瞬间直接入包（任务检查被绕开）。
            Assert.Equal(0, lootAtKill);
            Assert.Equal(0, conditionalAtKill);
            Assert.Equal(0, moneyAtKill);

            // ① 登记里三槽按序：普通件 → 条件件（条件/任务号必须跟着走） → 金币
            var regs = world.DrainCorpseLootRegistrations();
            var reg = Assert.Single(regs);
            Assert.Equal(3, reg.Slots.Length);
            Assert.Equal(555, reg.Slots[0].ItemId);
            Assert.Equal(0, reg.Slots[0].ConditionKind);
            Assert.Equal(6622, reg.Slots[1].ItemId);
            Assert.Equal(1, reg.Slots[1].ConditionKind);
            Assert.Equal(90007, reg.Slots[1].QuestId);
            Assert.Equal(10, reg.Slots[2].Money);

            // ② 取走条件槽 ⇒ 回执必须还原成条件件（QuestId 跟着走），且不产生普通掉落事件
            var askCond = new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
            {
                SessionId = sessionId, CorpseEntityId = monsterId, SlotIndex = 1, DistanceMeters = 1f, RequestId = 9201,
            };
            Assert.True(world.HandleCorpseLootRequest(askCond, out var condForward, out var condDenial));
            Assert.Null(condDenial);
            Assert.Equal(9201, condForward!.RequestId);
            var takenCond = new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
            {
                RequestId = 9201, Status = (byte)CorpseLootTakeStatus.Taken,
                ItemId = 6622, Count = 1, ConditionKind = 1, QuestId = 90007, Money = 0,
            };
            Assert.True(world.HandleCorpseLootResult(takenCond));
            var condTick = world.Tick().GameplayEvents;
            var condGrants = condTick.Where(e => e.Kind == DsGameplayEventKind.LootConditional).ToList();
            var one = Assert.Single(condGrants);
            Assert.Equal(6622, one.ItemId);
            Assert.Equal(90007, one.QuestId);              // 少了这一格：业务层按"任务 0"判，永远不发放
            Assert.Equal(2001UL, one.ActorCharacterId);
            Assert.Empty(condTick.Where(e => e.Kind == DsGameplayEventKind.Loot));

            // ③ 反面对照：取走普通槽仍然是普通掉落（证明上一条的"是条件件"不是恒真）
            var askNormal = new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
            {
                SessionId = sessionId, CorpseEntityId = monsterId, SlotIndex = 0, DistanceMeters = 1f, RequestId = 9202,
            };
            Assert.True(world.HandleCorpseLootRequest(askNormal, out var normalForward, out var normalDenial));
            Assert.Null(normalDenial);
            Assert.Equal(9202, normalForward!.RequestId);
            Assert.True(world.HandleCorpseLootResult(new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
            {
                RequestId = 9202, Status = (byte)CorpseLootTakeStatus.Taken,
                ItemId = 555, Count = 2, ConditionKind = 0, QuestId = 0, Money = 0,
            }));
            var normalTick = world.Tick().GameplayEvents;
            var normalGrants = normalTick.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            Assert.Single(normalGrants);
            Assert.Equal(555, normalGrants[0].ItemId);
            Assert.Empty(normalTick.Where(e => e.Kind == DsGameplayEventKind.LootConditional));
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 欠任务的条件槽被拒时计数单独念出来()
    {
        // 权威侧因 needs_quest 拒绝时：不发入包事件、**仍回一条结果包**（拒绝也要回执），
        // 并且计数进"拾取欠任务"这一格——混进"权威拒"会让"任务件为什么没人拿到"查不出来。
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_conddeny_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ConditionalCorpseLootFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                world.Tick();
            }
            world.DrainCorpseLootRegistrations();

            Assert.True(world.HandleCorpseLootRequest(new Horizon.Game.Core.Sim.Server.DsCorpseLootRequest
            {
                SessionId = sessionId, CorpseEntityId = monsterId, SlotIndex = 1, DistanceMeters = 1f, RequestId = 9301,
            }, out _, out _));

            var packetsBefore = world.CorpseLootResultPacketsQueued;
            Assert.False(world.HandleCorpseLootResult(new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
            {
                RequestId = 9301, Status = (byte)CorpseLootTakeStatus.ConditionNotMet,
                ItemId = 0, Count = 0, ConditionKind = 1, QuestId = 90007, Money = 0,
            }));
            Assert.Equal(1, world.CorpseLootRejectedCondition);
            Assert.Equal(1, world.CorpseLootResultsRejected);
            Assert.Equal(packetsBefore + 1, world.CorpseLootResultPacketsQueued);
            var after = world.Tick().GameplayEvents;
            Assert.Empty(after.Where(e => e.Kind == DsGameplayEventKind.Loot));
            Assert.Empty(after.Where(e => e.Kind == DsGameplayEventKind.LootConditional));
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 条件槽对没任务的人拒绝且不消耗()
    {
        // AC 的 `needs_quest`：不持有任务的玩家连"拿走"都不该成功，更不能把槽消耗掉——
        // 否则同队的任务持有者晚到一步就永远拿不到（东西被一个不该拿的人"点没了"）。
        static Horizon.Game.Sim.Monsters.MonsterCorpseLootState NewState()
        {
            var s = new Horizon.Game.Sim.Monsters.MonsterCorpseLootState { ExpireTick = 1200, PickupDistanceMeters = 5.5f };
            s.Slots.Add(new Horizon.Game.Sim.Monsters.MonsterCorpseLootState.Slot { ItemId = 555, Count = 1 });
            s.Slots.Add(new Horizon.Game.Sim.Monsters.MonsterCorpseLootState.Slot
            {
                ItemId = 6622, Count = 1, ConditionKind = MonsterLootCore.ConditionQuestHolder, QuestId = 90007,
            });
            s.Slots.Add(new Horizon.Game.Sim.Monsters.MonsterCorpseLootState.Slot { ItemId = 0, Count = 1, Money = 10 });
            return s;
        }

        // ① 显式槽：没任务 ⇒ ConditionNotMet；拿到任务后同一个槽仍可取走（证明没被消耗）
        var state = NewState();
        Assert.Equal(LootTakeResult.ConditionNotMet, state.TryTake(1, 700, 1f, 2001UL, out var denied, heldQuestIds: null));
        Assert.Null(denied);
        Assert.Equal(LootTakeResult.Taken,
            state.TryTake(1, 700, 1f, 2001UL, out var takenCond, heldQuestIds: new[] { 90007 }));
        Assert.Equal(6622, takenCond!.ItemId);

        // ② 一键取：没任务时跳条件槽、照常拿别的（不许一件条件件把整具尸体锁死）；
        //    取到只剩条件槽时如实说"条件不满足"，不能说成"已经被取走了"。
        var state2 = NewState();
        Assert.Equal(LootTakeResult.Taken, state2.TryTakeFirst(700, 1f, 2001UL, out _, out var firstIdx, heldQuestIds: null));
        Assert.Equal(0, firstIdx);
        Assert.Equal(LootTakeResult.Taken, state2.TryTakeFirst(700, 1f, 2001UL, out _, out var moneyIdx, heldQuestIds: null));
        Assert.Equal(2, moneyIdx);
        Assert.Equal(LootTakeResult.ConditionNotMet, state2.TryTakeFirst(700, 1f, 2001UL, out _, out _, heldQuestIds: null));
        Assert.Equal(LootTakeResult.Taken,
            state2.TryTakeFirst(700, 1f, 2001UL, out var held, out var condIdx, heldQuestIds: new[] { 90007 }));
        Assert.Equal(1, condIdx);
        Assert.Equal(6622, held!.ItemId);
    }

    [Fact]
    public void 接入权威存储后击杀不再直接入包而取走才发事件()
    {
        // 2026-10-29 拍板（D23：改为尸体拾取）：击杀只掷骰、把结果登记给权威侧；取走才发入包事件。
        // 这条把"旧路径有没有真的关掉"也钉住——只测"取走会发事件"是不够的：
        // 两条路径同时开着，玩家会拿到双份（这一族以前踩过"改了一半、两边都发"）。
        var store = new InMemoryCorpseLootStore();
        var path = Path.Combine(Path.GetTempPath(), "monster_corpseloot_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;   // 尸体 30s < 刷新 120s：让"尸体还在"这一段足够长
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
            }, null, store);
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);

            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            var lootAtKill = 0;
            var moneyAtKill = 0;
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    // 玩家站在怪的 -X 侧、朝 +X（yaw=0）——与 MakeCorpseWorld 里那一族的站位一致。
                    // 传 MathF.PI 会整段 OutOfArc：怪一直不死，于是这条用例会"没有掉落"地绿掉一大片。
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                var r = world.Tick();
                foreach (var e in r.GameplayEvents)
                {
                    if (e.Kind == DsGameplayEventKind.Loot) lootAtKill++;
                    if (e.Kind == DsGameplayEventKind.KillMoney) moneyAtKill++;
                }
            }
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0,
                "怪没被打死——后面所有'没有掉落/没有登记'都会是在空转上读数");
            // 死亡那一拍的事件要再取一次（击杀在 Submit 里结算，事件搭下一班车）
            var afterDeath = world.Tick();
            foreach (var e in afterDeath.GameplayEvents)
            {
                if (e.Kind == DsGameplayEventKind.Loot) lootAtKill++;
                if (e.Kind == DsGameplayEventKind.KillMoney) moneyAtKill++;
            }

            Assert.Equal(0, lootAtKill);   // 旧路径必须真的关掉
            Assert.Equal(0, moneyAtKill);
            Assert.Equal(1, store.RegisterCount);
            Assert.Equal(1, world.MonsterCorpseLootRegistered);

            // 取走物品槽：发一条 Loot 事件，且记在取走的人头上
            Assert.Equal(CorpseLootTakeStatus.Taken, world.TryLootCorpse(monsterId, sessionId, 0, 1f));
            var takenTick = world.Tick();
            var lootEvents = takenTick.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            Assert.Single(lootEvents);
            Assert.Equal(555, lootEvents[0].ItemId);
            Assert.Equal(2, lootEvents[0].ItemCount);
            Assert.Equal(2001UL, lootEvents[0].ActorCharacterId);

            // 重复取同一槽：AlreadyTaken（不能再发一次）
            Assert.Equal(CorpseLootTakeStatus.AlreadyTaken, world.TryLootCorpse(monsterId, sessionId, 0, 1f));
            Assert.Empty(world.Tick().GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot));

            // 金币槽：取走才发 KillMoney
            Assert.Equal(CorpseLootTakeStatus.Taken, world.TryLootCorpse(monsterId, sessionId, 1, 1f));
            var moneyEvents = world.Tick().GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.KillMoney).ToList();
            Assert.Single(moneyEvents);
            Assert.Equal(10, moneyEvents[0].Money);

            // 站远了取不了（DS 那道门与权威侧同源）
            Assert.Equal(CorpseLootTakeStatus.TooFar, world.TryLootCorpse(monsterId, sessionId, 0, 100f));

            // 尸体到期：登记被释放，之后再取一律 UnknownCorpse
            while (world.TryGetMonster(monsterId, out var cur) && cur != null && cur.CorpseExpireAtTick != 0)
                world.Tick();
            Assert.Equal(0, store.Count);
            Assert.Equal(1, store.RemoveCount);
            // DS 那道门对"实体已不在世界"统一报 Expired（与 CanLootCorpse 同源），玩家看到的理由才不会分叉；
            // "权威侧没有登记"是**内部**语义（比如权威侧重启过），单独在存储上断一条。
            Assert.Equal(CorpseLootTakeStatus.Expired, world.TryLootCorpse(monsterId, sessionId, 0, 1f));
            var afterRelease = store.TakeAsync(monsterId, 2001UL, 0, world.ServerTick, 1f).GetAwaiter().GetResult();
            Assert.Equal(CorpseLootTakeStatus.UnknownCorpse, afterRelease.Status);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 用一份"有掉落"的内容包起一个世界（提示与上限两条用例共用）。
    /// 缺省把拾取窗口设得比尸体短（1.5 秒 vs 5 秒）：那是唯一能区分"在窗口关的那一刻提示"与"在收尸时提示"的形状。
    /// 另一种形状（两者相等）也要测——缺省配置就是这么配的，而它走的正是"同一拍"那条分支。
    /// </summary>
    private static string ShortLootWindowFixture(int templateId, double corpseSeconds = 5.0, double lootWindowSeconds = 1.5)
        => """
{"contentRevision":1,"templates":[
  {"monsterId":__T__,"name":"短窗怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":__C__,
   "lootWindowSeconds":__L__,"pickupDistanceMeters":5.5,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":1,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":120.0,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":0,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":__T__,"maxAlive":1,"slotSpacing":6}
],"lootBindings":[
  {"monsterTemplateId":__T__,"lootTableId":9101,"rollChanceBps":0,"minCount":1,"maxCount":1,
   "conditionKind":0,"questId":0,"moneyMin":0,"moneyMax":0,
   "entries":[{"itemId":555,"chanceBps":10000,"countMin":1,"countMax":1,"seq":1}]}
]}
""".Replace("__T__", templateId.ToString(CultureInfo.InvariantCulture))
   .Replace("__C__", corpseSeconds.ToString("0.0", CultureInfo.InvariantCulture))
   .Replace("__L__", lootWindowSeconds.ToString("0.0", CultureInfo.InvariantCulture));

    [Fact]
    public void 尸体只让击杀参与者取且别人的请求连转发都不发()
    {
        // 2026-10-29 拍板：击杀参与者才能取（AC DIDNT_KILL）。判据要在**三处**同时成立：
        // DS 本地门（当场拒）、登记帧里的名单（权威侧拿得到）、权威侧判据（换个人直接问 Grain 也取不到）。
        // 这里断前两处，第三处由 CorpseLootGrainTests 断——只断一处的话，删掉另一处的名单照样全绿。
        var store = new InMemoryCorpseLootStore();
        var path = Path.Combine(Path.GetTempPath(), "monster_perm_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ShortLootWindowFixture(13100));
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 13100;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            }, null, store);
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long killerSession = 2001;
            const long watcherSession = 2002;
            Assert.True(world.AddPlayer(killerSession, 2001, -2f, 0f, 0f, out var r1), r1);
            // 旁观者站在尸体另一侧 4 米：够得着（< 5.5），但 > 索敌半径 1 米，不会把怪的死亡时序搅乱。
            Assert.True(world.AddPlayer(watcherSession, 2002, 4f, 0f, 0f, out var r2), r2);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, killerSession, monsterId);
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");

            // 登记帧必须带上参与者：权威侧只看得到这一份（它拿不到 DS 的账本）。
            var regs = world.DrainCorpseLootRegistrations();
            Assert.Single(regs);
            Assert.Equal(new[] { 2001UL }, regs[0].Contributors);

            // 旁观者：当场拒（NotYours），不排转发、不发入包事件
            Assert.Equal(CorpseLootTakeStatus.NotYours, world.TryLootCorpse(monsterId, watcherSession, 0, 1f));
            Assert.Empty(world.DrainCorpseLootForwards());
            Assert.Equal(1, world.CorpseLootRejectedNotYours);
            var quiet = world.Tick();
            Assert.Empty(quiet.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot));

            // 击杀者：同一具尸体、同一格，取得到
            Assert.Equal(CorpseLootTakeStatus.Taken, world.TryLootCorpse(monsterId, killerSession, 0, 1f));
            var grants = world.Tick().GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot).ToList();
            Assert.Single(grants);
            Assert.Equal(2001UL, grants[0].ActorCharacterId);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 一具尸体最多掉18件普通加32件任务件而超出的部分有计数()
    {
        // 2026-10-29 拍板：一具尸体 18 件 + 32 任务件（AC Loot::MAX_NR_LOOT_ITEMS / MAX_NR_QUEST_ITEMS）。
        // 上限按**桶**分而不是合成一个总数：任务件在客户端是另一个页签，合成总数会让 32 永远用不满。
        var normal = new MonsterDropSnapshot[25];
        for (var i = 0; i < normal.Length; i++)
            normal[i] = new MonsterDropSnapshot(1000 + i, MonsterLootCore.BasisPointsMax, 1, 1, seq: i);
        var normalBinding = new MonsterLootBindingSnapshot(1, 0, 1, 1, MonsterLootCore.ConditionNone, 0, 0, 0, normal);
        var rolledNormal = MonsterLootCore.Roll(new[] { normalBinding }, killerCharacterId: 7UL, monsterTemplateId: 880, serverTick: 100);

        Assert.Equal(MonsterLootCore.MaxNormalLootItems, rolledNormal.Drops.Count);
        Assert.Equal(25 - MonsterLootCore.MaxNormalLootItems, rolledNormal.ItemsDroppedOverCapacity);
        // 每条出口都进了账：少一条出口，"丢掉了"与"掷骰漏了"就同形
        Assert.Equal(rolledNormal.EntriesRolled, rolledNormal.Accounted);
        Assert.Equal(0, rolledNormal.ConditionalDrops.Count);   // 普通桶满不影响任务桶

        var quest = new MonsterDropSnapshot[40];
        for (var i = 0; i < quest.Length; i++)
            quest[i] = new MonsterDropSnapshot(2000 + i, MonsterLootCore.BasisPointsMax, 1, 1, seq: i, questId: 90001);
        var questBinding = new MonsterLootBindingSnapshot(1, 0, 1, 1, MonsterLootCore.ConditionQuestHolder, 90001, 0, 0, quest);
        var rolledQuest = MonsterLootCore.Roll(new[] { questBinding }, killerCharacterId: 7UL, monsterTemplateId: 880, serverTick: 100);

        Assert.Equal(MonsterLootCore.MaxQuestLootItems, rolledQuest.ConditionalDrops.Count);
        Assert.Equal(40 - MonsterLootCore.MaxQuestLootItems, rolledQuest.ItemsDroppedOverCapacity);
        Assert.Equal(rolledQuest.EntriesRolled, rolledQuest.Accounted);
        Assert.Empty(rolledQuest.Drops);

        // 两个上限加金币槽必须装得进槽位位图（18+32+1=51 ≤ 64）；装不进的话超出的槽永远取不到，
        // 而没有一条断言，它表现为"尸体上有件东西点了没反应"。
        Assert.True(MonsterLootCore.MaxNormalLootItems + MonsterLootCore.MaxQuestLootItems + 1
                    <= MonsterLootPickupCore.MaxSlots);
        Assert.Equal(18, MonsterLootCore.MaxNormalLootItems);
        Assert.Equal(32, MonsterLootCore.MaxQuestLootItems);
    }

    [Fact]
    public void 期望件数被18件上限截断而原始期望仍留给交叉核对()
    {
        // 期望值也是"预览给策划看的数"，运行期 EmitEntry 满了就丢，所以它必须同步截断；
        // 否则编辑器报"每次 25 件"、服务端只给 18 件，而这正是"预览自己编了个数"。
        // 交叉核对（逐条求和 vs 整表）拿的必须是**未截断**那一份，不然超限配法会被误报成"两个入口分叉"。
        var entries = new MonsterDropSnapshot[25];
        for (var i = 0; i < entries.Length; i++)
            entries[i] = new MonsterDropSnapshot(1000 + i, MonsterLootCore.BasisPointsMax, 1, 1, seq: i);
        var binding = new MonsterLootBindingSnapshot(1, 0, 1, 1, MonsterLootCore.ConditionNone, 0, 0, 0, entries);

        Assert.Equal(25.0, MonsterLootCore.ExpectedNormalItemsUncapped(new[] { binding }), 9);
        Assert.Equal(MonsterLootCore.MaxNormalLootItems, MonsterLootCore.ExpectedItemsPerKill(new[] { binding }), 9);

        // 小表不能被截断压低：截断只在超限时发生
        var small = new MonsterLootBindingSnapshot(1, 0, 1, 1, MonsterLootCore.ConditionNone, 0, 0, 0,
            new[] { new MonsterDropSnapshot(2001, 5000, 1, 1, seq: 0) });
        Assert.Equal(0.5, MonsterLootCore.ExpectedNormalItemsUncapped(new[] { small }), 9);
        Assert.Equal(0.5, MonsterLootCore.ExpectedItemsPerKill(new[] { small }), 9);
    }

    [Fact]
    public void 窗口到期还没拿完只给一条提示而拿完了就不打扰()
    {
        // 2026-10-29 拍板：窗口到期而东西没拿完，给一条提示（每人一条，同一具尸体不重复发）。
        var path = Path.Combine(Path.GetTempPath(), "monster_expire_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ShortLootWindowFixture(13200));
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 13200;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, sessionId, monsterId);

            // 一件都不取：一路 tick 到窗口关（1.5 秒 = 90 拍）。尸体 5 秒，所以提示必须**早于**收尸。
            var notices = new List<Horizon.Game.Message.Sync.SyncEvent>();
            var corpseGone = false;
            for (var i = 0; i < 400 && !corpseGone; i++)
            {
                var tick = world.Tick();
                foreach (var e in tick.Events.Select(x => x.Packet).OfType<Horizon.Game.Message.Sync.EventPacket>())
                    notices.AddRange(e.Events.Where(s => s.Kind == Horizon.Game.Message.Sync.SyncEventKind.CorpseLootExpired));
                corpseGone = !world.TryGetMonster(monsterId, out var m) || m == null || m.CorpseExpireAtTick == 0;
            }

            Assert.Single(notices);
            Assert.Equal(monsterId, notices[0].SourceEntityId);
            Assert.Equal(2001UL, notices[0].TargetEntityId);
            Assert.Equal(1, notices[0].IntValue);            // 还剩一件没拿走
            Assert.Equal(1, world.CorpseLootExpiredNotices);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 拾取窗口与尸体同拍到期时提示照样发得出来()
    {
        // 缺省配置就是"窗口 60 秒 = 尸体 60 秒"⇒ 两个期限落在**同一拍**。
        // 这一条单独存在，是因为"窗口比尸体短"那条用例对"提示写在摘除之前"是**等价变异体**：
        // 窗口先关的那些拍里尸体还没到期，把提示挪到 else 分支照样发得出来（实测反证绿了才发现）。
        // 只有同拍到期的形状，才能把"提示必须先于摘除"钉死。
        var path = Path.Combine(Path.GetTempPath(), "monster_expire_same_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ShortLootWindowFixture(13400, corpseSeconds: 1.5, lootWindowSeconds: 1.5));
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 13400;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, sessionId, monsterId);

            var notices = new List<Horizon.Game.Message.Sync.SyncEvent>();
            var noticeTickHadNoCorpse = false;
            for (var i = 0; i < 400; i++)
            {
                var tick = world.Tick();
                var found = tick.Events.Select(x => x.Packet)
                    .OfType<Horizon.Game.Message.Sync.EventPacket>()
                    .SelectMany(p => p.Events)
                    .Where(s => s.Kind == Horizon.Game.Message.Sync.SyncEventKind.CorpseLootExpired)
                    .ToList();
                notices.AddRange(found);
                if (found.Count > 0)
                {
                    // 这一拍收尸也发生了（同拍）：提示必须已经在同一份 tick 结果里，而不是下一拍。
                    noticeTickHadNoCorpse = !world.TryGetMonster(monsterId, out var m) || m == null;
                    break;
                }
            }

            Assert.Single(notices);
            Assert.Equal(1, notices[0].IntValue);
            Assert.True(noticeTickHadNoCorpse, "提示那一拍尸体还在世界里 ⇒ 收尸还没发生，这条用例没有测到同拍那条分支");
            Assert.Equal(1, world.CorpseLootExpiredNotices);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 东西拿完了窗口到期不必再提示()
    {
        // 反面：同一份内容、同一具尸体，取空之后窗口到期**不**打扰。
        // 少这一条的话，"每具尸体都发提示"也会让上面那条全绿——而玩家会为一件已经到手的东西收到道歉。
        var store = new InMemoryCorpseLootStore();
        var path = Path.Combine(Path.GetTempPath(), "monster_expire_done_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ShortLootWindowFixture(13300));
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 13300;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            }, null, store);
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, 2001, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, sessionId, monsterId);

            // 前提：这一格**真的**取到了（否则下面"不发提示"是在拿空尸体自证）
            Assert.Equal(CorpseLootTakeStatus.Taken, world.TryLootCorpse(monsterId, sessionId, 0, 1f));
            Assert.True(world.TryGetMonster(monsterId, out var afterTake) && afterTake != null);
            Assert.Equal(1, afterTake!.CorpseLootSlotsTaken);

            var notices = 0;
            for (var i = 0; i < 400; i++)
            {
                var tick = world.Tick();
                notices += tick.Events.Select(x => x.Packet)
                    .OfType<Horizon.Game.Message.Sync.EventPacket>()
                    .Sum(p => p.Events.Count(s => s.Kind == Horizon.Game.Message.Sync.SyncEventKind.CorpseLootExpired));
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.CorpseExpireAtTick == 0) break;
            }

            Assert.Equal(0, notices);
            Assert.Equal(0, world.CorpseLootExpiredNotices);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 真库坐标烘出来的导航图让巡逻怪绕开挡路带()
    {
        // 素材全是**真东西**：`game_content.json` 是生产库 Game 的导出（6 个刷怪点 X=20..320、两条 13 点巡逻环），
        // `nav_game_1m.nav` 是按这些坐标生成掩码再走 CLI `bake-nav` 烘出来的 321×74 网格，
        // 中间横着一条挡路带 x∈[208,210]. 4 号怪出生在 (200,0)，它巡逻环的第一点在 (226.9,0)——连线正好穿过那条带。
        // 所以"位置采样有没有进带"是这条用例的判据，而不是"计数看起来变了"。
        //
        // 素材缺了怎么重生成：
        //   DB_GAME_CONNECTION_STRING=…  dotnet run --project Horizon.MonsterCli -- export-content --out Tools/_rehearsal/game_content.json
        //   python Tools/gen-nav-mask-from-content.py
        //   … bake-nav --out Tools/_rehearsal/nav_game_1m.nav --width 321 --height 74 --cell 1 --origin 10 -36.701 --mask - < Tools/_rehearsal/nav_mask_game.txt
        //
        // 临时副本只改三格（v2 + 导航位 + 路线声明走图）：生产库里这三格仍然是关的（真场景 NavMesh 还没烘），
        // 这条用例不据此回写库。
        var root = RepoRoot();
        var contentPath = Path.Combine(root, "Tools", "_rehearsal", "game_content.json");
        var navPath = Path.Combine(root, "Tools", "_rehearsal", "nav_game_1m.nav");
        Assert.True(File.Exists(contentPath), "缺素材：" + contentPath);
        Assert.True(File.Exists(navPath), "缺烘出来的网格：" + navPath);

        var grid = MonsterNavGridLoader.Load(navPath, out var navError);
        Assert.True(grid.IsValid, "网格读不回来：" + navError);

        // 诊断（失败时一起念）：起点格可走吗、到环上两点各能不能找到路。
        var probeScratch = new MonsterNavScratch();
        probeScratch.EnsureCapacity(grid.Width * grid.Height);
        var probeCells = new int[grid.Width * grid.Height];
        var startCell = grid.CellIndexOf(200f, 0f);
        var goalCell = grid.CellIndexOf(226.897f, 0f);
        var toFirst = MonsterPathCore.FindPath(grid, probeScratch, 200f, 0f, 226.897f, 0f, probeCells, out var nFirst);
        var toFirstWide = MonsterPathCore.FindPath(grid, probeScratch, 200f, 0f, 226.897f, 0f, probeCells, out var nFirstWide,
            maxExploreCells: grid.Width * grid.Height);
        var toSeventh = MonsterPathCore.FindPath(grid, probeScratch, 200f, 0f, 173.884f, 6.437f, probeCells, out var nSeventh);
        var probe = $"startCell={startCell}(walkable={grid.IsWalkable(startCell)}) goalCell={goalCell}(walkable={grid.IsWalkable(goalCell)}) " +
                    $"到首点={toFirst}({nFirst}格) 全预算={toFirstWide}({nFirstWide}格) 到七号点={toSeventh}({nSeventh}格) 格子总数={grid.Width * grid.Height}";
        // 横截面：在 x∈[195,225] 这一段上，y=-20/-16/0/16/20 各一行"可走位图"——
        // 挡路带到底是不是我以为的那三列，一眼就能看出来（转置/错位会在这里露馅）。
        foreach (var probeY in new[] { -20f, -16f, 0f, 16f, 20f })
        {
            var rowCell = grid.CellIndexOf(195f, probeY) / grid.Width;
            var bits = new System.Text.StringBuilder();
            for (var x = 195; x <= 225; x++)
            {
                var cell = rowCell * grid.Width + (x - 10);   // 原点 x=10、格长 1 米
                bits.Append(grid.IsWalkable(cell) ? '1' : '0');
            }
            probe += $" | y={probeY} {bits}";
        }

        var temp = Path.Combine(Path.GetTempPath(), "nav_rehearsal_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(contentPath))!;
            var patched = 0;
            foreach (var tpl in doc["templates"]!.AsArray())
            {
                if ((int)tpl!["monsterId"]! != 4) continue;
                var behavior = tpl["behavior"]!.AsObject();
                behavior["aiProfile"] = 2;          // v2：v1 只会停在墙前，不会绕
                behavior["flagUseNavMesh"] = 1;
                behavior["flagUseThreat"] = 1;
                patched++;
            }
            foreach (var route in doc["routes"]!.AsArray())
            {
                if ((int)route!["routeId"]! != 4) continue;
                route["useNavMesh"] = 1;            // 路线自己那一列：走这条线时准不准走图
                patched++;
            }
            Assert.Equal(2, patched);
            File.WriteAllText(temp, doc.ToJsonString());
            Assert.True(MonsterContentLoader.LoadFile(temp).Ok);

            const float bandMinX = 207.5f, bandMaxX = 210.5f, bandAbsY = 15.5f;
            static bool InBand(float x, float y) => x > bandMinX && x < bandMaxX && MathF.Abs(y) < bandAbsY;
            // 走图那只按**格子**判：它擦着墙的边角走是允许的（格子只保证"中心不在阻塞格"），
            // 真正的不变量是"采样落进的格子必须可走"。用几何带判会把它贴着边角的合法路径误判成穿墙
            // （实测：y=15.3 刚好在阻塞带边缘 15.299 之外）。
            bool InBlockedCell(float x, float y)
            {
                var cell = grid.CellIndexOf(x, y);
                return cell >= 0 && !grid.IsWalkable(cell);
            }

            (int InBand, int PointsReached, float MaxDx, string Trace) Run(bool withGrid)
            {
                var point = SpawnPointConfig.Default;
                point.SpawnPointId = 1;
                point.MonsterTemplateId = 4;
                point.MaxAlive = 1;
                point.X = 200f; point.Y = 0f; point.Z = 0f;
                point.SlotSpacing = 6f;
                point.AggroRadius = 1f;      // 被动：没有玩家，全程只在巡逻
                point.LeashRadius = 60f;
                point.MoveSpeed = 4f;
                point.RespawnDelaySeconds = 120f;
                var world = new DsWorldSession(new DsWorldSessionOptions
                {
                    ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
                });
                world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
                if (withGrid) world.ConfigureNavigation(grid);
                world.Tick();
                var id = SpawnDirectorCore.ComputeEntityId(1, 0);
                var inBand = 0;
                var maxDx = 0f;
                var patrolSamples = 0;
                var trace = new System.Text.StringBuilder();
                for (var i = 0; i < 1800; i++)   // 30 秒：够走完一整段并回到第二个点
                {
                    world.Tick();
                    if (!world.TryGetMonster(id, out var m) || m == null) continue;
                    if (withGrid ? InBlockedCell(m.Ai.X, m.Ai.Y) : InBand(m.Ai.X, m.Ai.Y))
                    {
                        inBand++;
                        if (inBand <= 3)
                            trace.Append($"[进带@{i}:({m.Ai.X:0.##},{m.Ai.Y:0.##}){m.Ai.State}]");
                    }
                    if (m.Ai.State == MonsterAiState.Patrol) patrolSamples++;
                    var dx = MathF.Abs(m.Ai.X - 200f);
                    if (dx > maxDx) maxDx = dx;
                    if (i % 300 == 0)
                        trace.Append($"t{i}:({m.Ai.X:0.#},{m.Ai.Y:0.#}){m.Ai.State} ");
                }
                return (inBand, world.MonsterPatrolPointsReached, maxDx,
                        $"{trace} patrolSamples={patrolSamples}");
            }

            var navRun = Run(withGrid: true);
            Assert.True(navRun.MaxDx > 10f, $"装了图反而没动（最大位移 {navRun.MaxDx:0.##} 米）：夹具前提没了；{probe}；{navRun.Trace}");
            Assert.True(navRun.PointsReached >= 1,
                $"装了图也没走到第一个巡逻点（到达 {navRun.PointsReached} 个）：绕不过去就不是寻路；{probe}；{navRun.Trace}");
            Assert.True(navRun.InBand == 0,
                $"有 {navRun.InBand} 个采样落在挡路带 x∈[{bandMinX},{bandMaxX}] 里（到达 {navRun.PointsReached} 个巡逻点，" +
                $"最大位移 {navRun.MaxDx:0.##}）；{probe}；{navRun.Trace}");   // 关键：一个采样都不许落在挡路带里
            // 对照：同一份内容、同一张图**不装**⇒ 直线穿过挡路带（这正是装图要解决的那个形状）。
            // 少了这一半，"没进带"可能只是因为这只怪压根没往那个方向走。
            var straightRun = Run(withGrid: false);
            Assert.True(straightRun.InBand > 0,
                "不装图也从不进挡路带：挡路带不在它的路线上，这条用例什么也没证明");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 权威侧拒绝同一槽的第二个人时也要回一条结果包()
    {
        // 同一具尸体的同一个槽，两个人几乎同时点：第一下被权威侧裁定 Taken，第二下是 AlreadyTaken。
        // 第二下的理由**只有权威侧知道**（DS 本地门放行了它），不转给客户端就表现成"点了没反应"——
        // 玩家会以为没点中，继续反复点。所以拒绝也要回一条同状态的结果包（入包事件仍然不发）。
        var store = new InMemoryCorpseLootStore();
        var gateway = new Horizon.Game.Core.Sim.Server.CorpseLootUplinkHandler(store);
        var path = Path.Combine(Path.GetTempPath(), "monster_reject_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            }, null, store);
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long first = 2001, second = 2002;
            Assert.True(world.AddPlayer(first, 2001, -2f, 0f, 0f, out var r1), r1);
            Assert.True(world.AddPlayer(second, 2002, -1.5f, 1.0f, 0f, out var r2), r2);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            // 两个人都要进贡献账本：一人一刀（100 血、每刀 60 ⇒ 第二刀致命）。
            // 两人都站在 -X 一侧朝 +X（yaw=0）：换成 +X 侧就整段 OutOfArc，怪永远不死，
            // 这条用例会在"没有尸体"上空转（实测踩过）。
            // 两个人都要进贡献账本：轮流砍到死（这一档每刀约 15 血——别按 60 点攻击力推"两刀就死"，
            // 实测两刀后还剩 70 血；写死刀数会让用例在"没有尸体"上空转）。两人各站 -X 侧朝 +X。
            var killed = false;
            var hits = new List<string>();
            for (var round = 0; round < 12 && !killed; round++)
            {
                var session = round % 2 == 0 ? first : second;
                var outcome = world.SubmitCombatAction(session, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = (ulong)session, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                world.Tick();
                if (world.TryGetMonster(monsterId, out var m) && m != null)
                {
                    hits.Add($"{session}:hp={m.Ai.Hp:0.#}");
                    if (m.Ai.Hp <= 0) killed = true;
                }
            }
            Assert.True(killed, "砍了 12 刀还没死：后面的'同一槽第二个人'无从谈起；" + string.Join(" | ", hits));
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead!.CorpseLootContributors is { Length: 2 },
                "两个攻击者必须都在击杀参与者名单里（否则第二个人会先被本地权限门拒掉，测不到权威侧拒绝）；实际=" +
                (dead?.CorpseLootContributors == null ? "null" : string.Join(",", dead.CorpseLootContributors)));

            var regs = world.DrainCorpseLootRegistrations();
            Assert.Single(regs);
            Assert.True(await gateway.HandleRegisterAsync(regs[0]));

            // 第一个人：受理 → 权威裁定 Taken
            Assert.Equal(CorpseLootTakeStatus.Taken, world.HandleCorpseLootTakeFromClient(first, monsterId, 0, out _));
            var firstForward = world.DrainCorpseLootForwards();
            Assert.Single(firstForward);
            var takenReply = await gateway.HandleForwardAsync(firstForward[0]);
            Assert.Equal((byte)CorpseLootTakeStatus.Taken, takenReply.Status);
            Assert.True(world.HandleCorpseLootResult(takenReply));
            world.Tick();

            // 第二个人：本地门放行 → 权威侧 AlreadyTaken → **必须**有一条结果包回到他那里
            Assert.Equal(CorpseLootTakeStatus.Taken,
                world.HandleCorpseLootTakeFromClient(second, monsterId, 0, out _));   // Taken = "已受理"
            var secondForward = world.DrainCorpseLootForwards();
            Assert.Single(secondForward);
            var rejectedReply = await gateway.HandleForwardAsync(secondForward[0]);
            Assert.Equal((byte)CorpseLootTakeStatus.AlreadyTaken, rejectedReply.Status);
            Assert.False(world.HandleCorpseLootResult(rejectedReply), "没取到就不该结账");
            Assert.Equal(1, world.CorpseLootResultsRejected);

            var tick = world.Tick();
            var packets = tick.Events.Select(e => e.Packet)
                .OfType<Horizon.Game.Message.Sync.CorpseLootTakeResultPacket>().ToList();
            Assert.Single(packets);
            Assert.Equal((byte)CorpseLootTakeStatus.AlreadyTaken, packets[0].Status);
            Assert.Equal(monsterId, packets[0].CorpseEntityId);
            Assert.Equal(0, packets[0].SlotIndex);
            Assert.Empty(tick.GameplayEvents.Where(e => e.Kind == DsGameplayEventKind.Loot));
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 内容要求走图而宿主没装网格时摘要格会数出来()
    {
        // `flagUseNavMesh` 是内容侧开关，网格是宿主侧装配：两者对不上时内核安静退回直线，
        // 表现是"编辑器里勾了走图、怪照样穿墙"且没有任何一行日志说过原因。这一格就是那句话说出口的地方。
        var path = Path.Combine(Path.GetTempPath(), "nav_gauge_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, NavGaugeFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            (int WithoutGrid, int WithGrid) Run()
            {
                var point = SpawnPointConfig.Default;
                point.SpawnPointId = 1;
                point.MonsterTemplateId = 13700;
                point.MaxAlive = 1;
                point.X = 0f; point.Y = 0f; point.Z = 0f;
                point.AggroRadius = 1f;
                point.LeashRadius = 20f;
                point.MoveSpeed = 4f;
                point.RespawnDelaySeconds = 120f;
                var world = new DsWorldSession(new DsWorldSessionOptions
                {
                    ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3,
                });
                world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
                world.Tick();
                var without = world.MonstersWantingNavWithoutGrid;

                var costs = new byte[4];
                for (var i = 0; i < costs.Length; i++) costs[i] = (byte)MonsterNavGrid.BaseCellCost;
                world.ConfigureNavigation(new MonsterNavGrid
                {
                    Width = 2, Height = 2, CellSize = 4f, OriginX = -4f, OriginY = -4f, CellCost = costs,
                });
                return (without, world.MonstersWantingNavWithoutGrid);
            }

            var (without, with) = Run();
            Assert.Equal(1, without);   // 内容要走图、宿主没图 ⇒ 说出来
            Assert.Equal(0, with);      // 装上图之后这一格必须归零（它是仪表，不是累计）
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    private const string NavGaugeFixture = """
{"contentRevision":1,"templates":[
  {"monsterId":13700,"name":"走图怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":0.0,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":1,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":120.0,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":2,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":13700,"maxAlive":1,"slotSpacing":6}
]}
""";

    [Fact]
    public void 缺省网格只认真烘焙产物不认玩具图()
    {
        // `dev_nav.grid` 是 32×32 全可走的玩具图：自动探到它会让"没烘真图"看起来像"装好了"。
        var dir = Path.Combine(Path.GetTempPath(), "nav_default_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "dev_nav.grid"), "MONSTERNAV 1 1 4 0 0\n10\n");
            Assert.Equal(string.Empty, MonsterNavGridLoader.ResolveDefaultPath(new[] { dir }));

            var real = Path.Combine(dir, MonsterNavGridLoader.DefaultFileName);
            File.WriteAllText(real, "MONSTERNAV 1 1 4 0 0\n10\n");
            Assert.Equal(real, MonsterNavGridLoader.ResolveDefaultPath(new[] { dir }));
            Assert.Equal("monster_nav.grid", MonsterNavGridLoader.DefaultFileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void 客户端拾取包经数据面到下行结果包走完整一趟()
    {
        // 之前这条链的两端各自验过：会话入口（`HandleCorpseLootTakeFromClient`）与下行编码（`DsDownlinkEncoder`）。
        // 中间那段——**客户端包真的进得了数据面、结果真的编得回下行字节**——没人串起来跑过。
        // 接缝上任何一处错（包种、会话号、编码器漏掉这种包）都只会在玩家那边表现成"按了 G 没反应"。
        var store = new InMemoryCorpseLootStore();
        var path = Path.Combine(Path.GetTempPath(), "loot_plane_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, CorpseLootPickupFixture);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 1;
            point.MonsterTemplateId = 12900;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 1f;
            point.LeashRadius = 20f;
            point.MoveSpeed = 4f;
            point.RespawnDelaySeconds = 120f;
            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 3, PlayerAttackPower = 60f,
                DeferCorpseLootToAuthority = true,
            }, null, store);
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            const long sessionId = 2001;
            Assert.True(world.AddPlayer(sessionId, sessionId, -2f, 0f, 0f, out var reason), reason);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            for (var i = 0; i < 60; i++)
            {
                if (!world.TryGetMonster(monsterId, out var m) || m == null || m.Ai.Hp <= 0) break;
                world.SubmitCombatAction(sessionId, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 2001UL, TargetId = monsterId, SkillId = 0, ClientTick = 1,
                    AttackerYaw = 0f, ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                world.Tick();
            }
            Assert.True(world.TryGetMonster(monsterId, out var dead) && dead != null && dead.Ai.Hp <= 0, "怪没被打死");
            // 前置：死亡那一刻内容包必须还在（`ComputeCorpseExpiries` 靠它算尸体/窗口）。
            // 全量并行时这一条可能红——那是"进程级静态内容被别的类换掉了"，别让它伪装成"拾取门拒绝"。
            Assert.True(dead!.CorpseExpireAtTick > 0,
                $"死亡时没拿到尸体窗口：corpseExpire={dead.CorpseExpireAtTick} lootExpire={dead.CorpseLootExpireAtTick} " +
                $"registered={dead.CorpseLootRegistered}（内容包在死亡那一刻不在了？）");
            var regs = world.DrainCorpseLootRegistrations();
            Assert.Single(regs);
            var registration = regs[0];
            store.RegisterAsync(new Horizon.Game.Core.Sim.Server.CorpseLootRegistration
            {
                CorpseEntityId = registration.CorpseEntityId,
                ExpireTick = registration.ExpireTick,
                PickupDistanceMeters = registration.PickupDistanceMeters,
            }).GetAwaiter().GetResult();

            // ① 客户端包（先编码再解回，走的是真线上形态）→ 数据面
            var plane = new DsDataPlaneHost(world, listenPort: 0);
            var request = new Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket
            {
                Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest,
                CharacterId = 2001UL,
                CorpseEntityId = monsterId,
                SlotIndex = 0,
            };
            Horizon.Game.Message.Sync.SyncPacketCodec.Encode(request, out var frameBytes, out var frameLength);
            var decoded = Horizon.Game.Message.Sync.SyncPacketCodec.Decode(
                frameBytes.AsSpan(0, frameLength));
            Assert.IsType<Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket>(decoded);
            plane.Apply(decoded);
            Assert.Equal(1, plane.CorpseLootRequestsSeen);
            if (plane.CorpseLootRejected != 0)
            {
                // 拒了就把**理由**念出来（窗口/距离/尸体还在不在），别只给一个 1：
                // 全量并行时"内容被换掉"会伪装成"拾取门拒绝"，两者的修法完全不同。
                world.CanLootCorpse(monsterId, 2f, out var why);
                world.TryGetMonster(monsterId, out var corpseNow);
                Assert.Fail($"数据面拒了请求：gate={why} corpseExpire={corpseNow?.CorpseExpireAtTick} " +
                            $"lootExpire={corpseNow?.CorpseLootExpireAtTick} registered={corpseNow?.CorpseLootRegistered}");
            }

            // ② 数据面把请求交给会话 → 排一条转发（这一段是"距离在 DS 侧量"之后的那一步）
            var forwards = world.DrainCorpseLootForwards();
            Assert.Single(forwards);
            Assert.Equal(sessionId, forwards[0].SessionId);
            Assert.Equal(monsterId, forwards[0].CorpseEntityId);
            Assert.Equal(2001UL, forwards[0].TakerCharacterId);

            // ③ 权威侧裁完 → 会话结账（测试里直接用请求号回一条 Taken）
            Assert.True(world.HandleCorpseLootResult(new Horizon.Game.Core.Sim.Server.DsCorpseLootResult
            {
                RequestId = forwards[0].RequestId,
                SessionId = sessionId,
                SlotIndex = 0,
                Status = (byte)Horizon.Game.Core.Sim.Server.CorpseLootTakeStatus.Taken,
                ItemId = 555, Count = 2,
            }));

            // ④ 下行字节：真的编得出来、解回来是那一条结果包
            var batch = DsDownlinkEncoder.Encode(world.Tick());
            Horizon.Game.Message.Sync.CorpseLootTakeResultPacket? resultPacket = null;
            foreach (var item in batch.Items)
            {
                if (Horizon.Game.Message.Sync.SyncPacketCodec.Decode(item.Frame)
                    is Horizon.Game.Message.Sync.CorpseLootTakeResultPacket pkt)
                {
                    resultPacket = pkt;
                    break;
                }
            }
            Assert.NotNull(resultPacket);
            Assert.Equal(monsterId, resultPacket!.CorpseEntityId);
            Assert.Equal(0, resultPacket.SlotIndex);
            Assert.Equal((byte)Horizon.Game.Core.Sim.Server.CorpseLootTakeStatus.Taken, resultPacket.Status);
            Assert.Equal(555, resultPacket.ItemId);
            Assert.Equal(2, resultPacket.Count);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    private const string CorpseLootPickupFixture = """
{"contentRevision":1,"templates":[
  {"monsterId":12900,"name":"拾取链怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":30.0,
   "lootWindowSeconds":60.0,"pickupDistanceMeters":5.5,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":1,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":120.0,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":0,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":12900,"maxAlive":1,"slotSpacing":6}
],"lootBindings":[
  {"monsterTemplateId":12900,"lootTableId":9101,"rollChanceBps":0,"minCount":1,"maxCount":1,
   "conditionKind":0,"questId":0,"moneyMin":10,"moneyMax":10,
   "entries":[{"itemId":555,"chanceBps":10000,"countMin":2,"countMax":2,"seq":1}]}
]}
""";

    /// <summary>
    /// 与 <see cref="CorpseLootPickupFixture"/> 只差**一件条件件**（6622 / 任务 90007）：
    /// 用来钉"条件件在尸体拾取路径上既不丢、也不退化成普通件"。
    /// </summary>
    private const string ConditionalCorpseLootFixture = """
{"contentRevision":1,"templates":[
  {"monsterId":12900,"name":"拾取链怪","rank":0,"species":1,"wuxingDomain":1,"modelKey":"k","modelScale":1.0,
   "standState":0,"levelBase":12,"levelMin":12,"levelMax":12,"aggroSayId":0,"corpseSeconds":30.0,
   "lootWindowSeconds":60.0,"pickupDistanceMeters":5.5,
   "stats":{"hpBase":100,"hpPerLevel":0,"atkBase":1,"atkPerLevel":0,"defBase":0,"defPerLevel":0,"hpOverrideBps":0,"atkOverrideBps":0},
   "xp":{"xpBase":45,"xpPerLevel":5},"respawnBaseSeconds":120.0,"respawnPerLevelSeconds":0.0,
   "behavior":{"aiProfile":0,"isAggressive":0,"aggroRadius":1,"leashRadius":20,"moveSpeed":4},
   "skills":[]}
],"spawnPoints":[
  {"spawnPointId":1,"zoneId":0,"shardId":0,"x":0,"y":0,"z":0,"monsterTemplateId":12900,"maxAlive":1,"slotSpacing":6}
],"lootBindings":[
  {"monsterTemplateId":12900,"lootTableId":9101,"rollChanceBps":0,"minCount":1,"maxCount":1,
   "conditionKind":0,"questId":0,"moneyMin":10,"moneyMax":10,
   "entries":[{"itemId":555,"chanceBps":10000,"countMin":2,"countMax":2,"seq":1},
              {"itemId":6622,"chanceBps":10000,"countMin":1,"countMax":1,"seq":2,"conditionKind":1,"questId":90007}]}
]}
""";

    [Fact]
    public void 尸体拾取的三种结果可区分而到期那一拍已经取不了()
    {
        // 2026-10-28 拍板：权威在 Orleans、窗口与距离可配，缺省取 AC 的 60 秒 / 5.5 米。
        // 这一步只判"这一下能不能取"：三种结果必须分得开——压成 bool 的话
        // "过期了"与"站远了"在回执里同形，玩家看到的是同一句"拾取失败"。
        const long expiry = 1200;
        Assert.Equal(MonsterCorpseCore.PickupDenial.None,
            MonsterCorpseCore.CanPickUp(nowTick: 700, expiryTick: expiry, distanceMeters: 3f, pickupDistanceMeters: 5.5f));
        // 边界：正好站在 5.5 米上算够得着（AC 的 IsWithinDistInMap 含边界）
        Assert.Equal(MonsterCorpseCore.PickupDenial.None,
            MonsterCorpseCore.CanPickUp(700, expiry, 5.5f, 5.5f));
        Assert.Equal(MonsterCorpseCore.PickupDenial.TooFar,
            MonsterCorpseCore.CanPickUp(700, expiry, 5.51f, 5.5f));
        // 边界：到期那一拍就已经取不了（收尸与拾取不能竞争同一 tick）
        Assert.Equal(MonsterCorpseCore.PickupDenial.None,
            MonsterCorpseCore.CanPickUp(1199, expiry, 1f, 5.5f));
        Assert.Equal(MonsterCorpseCore.PickupDenial.Expired,
            MonsterCorpseCore.CanPickUp(1200, expiry, 1f, 5.5f));
        // 没有窗口（expiry<=0 = 当场消失）⇒ 一律过期
        Assert.Equal(MonsterCorpseCore.PickupDenial.Expired,
            MonsterCorpseCore.CanPickUp(10, 0, 1f, 5.5f));

        // 缺省值必须与 AC 字面量一致（跨层常量：内容列缺省、编辑器提示、这层默认都取这三个数）
        Assert.Equal(60f, MonsterCorpseCore.DefaultLootWindowSeconds);
        Assert.Equal(5.5f, MonsterCorpseCore.DefaultPickupDistanceMeters);
        // 传 0 距离阈值时退回缺省，而不是"谁都够不着"
        Assert.Equal(MonsterCorpseCore.PickupDenial.None,
            MonsterCorpseCore.CanPickUp(700, expiry, 5f, 0f));
    }

    [Fact]
    public void 两条下发路径给尸体的死亡位是同一规格()
    {
        // 怪物的状态由两个写方各写一份（共享全量路径 + AOI 分组/晚加入路径）。
        // 只测一条时，另一条会"静默留 0"——开着 AOI 的玩家看不到尸体，关着才看得到，
        // 这种只在一种配置下成立的正确性等于没有正确性。
        var path = CorpseFixture(30.0);         // 窗口比刷新长：整个用例期间都不会自己消失
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);
            const uint dead = Horizon.Game.Message.Sync.Components.EntityStateBits.Dead;

            foreach (var aoi in new[] { false, true })
            {
                var world = MakeCorpseWorld(out var sessionId, aoiFilter: aoi);
                world.Tick();
                var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
                KillMonsterByCombat(world, sessionId, monsterId);

                var delta = NextMonsterDelta(world, monsterId);
                Assert.True((delta.State!.Value.StateBits & dead) != 0,
                            $"EnableAoiFilter={aoi} 这条路径没给死亡位");
                Assert.Equal(0, delta.State!.Value.Health);
            }
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 尸体比刷新活得久时在重生前一tick收尸且绝不与同ID新怪撞在一起()
    {
        // 库里种子给的是 corpse=60 / respawn=15，所以"尸体活得比重生久"是**默认形状**，不是边角料。
        // 撞车的后果很具体：Despawn 走全广播、Spawn 走快照，同一个 tick 里两者先后由网关决定；
        // 客户端若先建 Actor 再被 Despawn 删掉，这个 ID 就再也不会有第二次 Spawn（全局标志已置真）
        // ——屏幕上是一块永久空位。所以这一条要断" never 同时"，不是断"看起来对"。
        var path = CorpseFixture(30.0, 2.0);      // 尸体 30 秒，刷新 2 秒（120 tick）→ 窗口被截到 119
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);

            var world = MakeCorpseWorld(out var sessionId, respawnSeconds: 2f);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);
            KillMonsterByCombat(world, sessionId, monsterId);

            const uint dead = Horizon.Game.Message.Sync.Components.EntityStateBits.Dead;

            var sawDespawnTick = -1L;
            var sawSpawnAfterFade = false;
            var collide = false;
            var sameTickDespawnAndSpawn = false;

            for (var i = 0; i < 200; i++)
            {
                var r = world.Tick();

                // 同一 tick 里"这块地既在收尸又已站上新怪"= 撞车（世界里同时有两只同 ID 的东西）
                if (world.MonsterCount > 1) collide = true;

                var despawnHere = r.DespawnBroadcast != null
                    && r.DespawnBroadcast.Deltas.Any(d => d.EntityId == monsterId
                                                          && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Despawn);
                if (despawnHere) sawDespawnTick = world.ServerTick;

                var spawnHere = ContainsMonsterSpawn(r, monsterId);
                if (despawnHere && spawnHere) sameTickDespawnAndSpawn = true;
                if (!despawnHere && spawnHere && sawDespawnTick > 0) sawSpawnAfterFade = true;

                if (sawSpawnAfterFade) break;
            }

            Assert.True(sawDespawnTick > 0, "200 tick 内没收到尸体的 Despawn");
            Assert.False(collide, "同一实体 ID 在世界里同时占了两份");
            Assert.False(sameTickDespawnAndSpawn, "收尸与重生落在同一个 tick：客户端可能先 Spawn 再被 Despawn 抹掉");
            Assert.True(sawSpawnAfterFade, "收尸之后没等到同 ID 的新怪 Spawn");

            // 新怪必须是活的、不带死亡位的
            var fresh = NextMonsterDelta(world, monsterId);
            Assert.Equal(0u, fresh.State!.Value.StateBits & dead);
            Assert.True(fresh.State!.Value.Health > 0);
            Assert.Equal(0, world.MonsterCorpseCount);
            Assert.Equal(1, world.MonsterCount);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>本 tick 的下发里是否有该实体的 Spawn delta（共享路径、AOI 分组路径、晚加入补发都算）。</summary>
    private static bool ContainsMonsterSpawn(DsTickResult r, ulong monsterId)
    {
        if (r.WorldSnapshot != null && r.WorldSnapshot.Deltas.Any(d => d.EntityId == monsterId
                                                                      && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn))
            return true;
        if (r.SnapshotGroups != null && r.SnapshotGroups.Any(g => g.Snapshot.Deltas.Any(d => d.EntityId == monsterId
                && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn)))
            return true;
        if (r.InitialSpawnSnapshots != null)
        {
            foreach (var initial in r.InitialSpawnSnapshots)
                if (initial.Snapshot.Deltas.Any(d => d.EntityId == monsterId
                    && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Spawn))
                    return true;
        }
        return false;
    }

    [Fact]
    public void 显式摘除不给尸体窗口()
    {
        // 操作者说「把它移走」之后还留一具尸体在地上，等于把自己的指令打了折扣；
        // 脚本实体的 TTL 到期同理。判据是"配置再大也不躺"。
        var path = CorpseFixture(60.0);
        try
        {
            Assert.True(MonsterContentLoader.LoadFile(path).Ok);

            var world = MakeCorpseWorld(out _);
            world.Tick();
            var monsterId = SpawnDirectorCore.ComputeEntityId(1, 0);

            Assert.True(world.MarkMonsterDead(monsterId, world.ServerTick, allowCorpseLinger: false));
            Assert.Equal(0, world.MonsterCorpseCount);
            Assert.Equal(0, world.MonsterCount);
            Assert.Equal(0, world.MonsterCorpsesLingering);

            var r = world.Tick();
            Assert.NotNull(r.DespawnBroadcast);
            Assert.Contains(r.DespawnBroadcast!.Deltas,
                d => d.EntityId == monsterId && d.Kind == Horizon.Game.Message.Sync.EntityDeltaKind.Despawn);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 尸体窗口不会跨过重生那一tick()
    {
        // 量出来的四格，不是从参数推的：窗口短于刷新 / 长于刷新 / 为零 / 没排定重生。
        Assert.Equal(1120L, MonsterCorpseCore.PlanExpiryTick(1000, 60, 2f, 1300));   // 2 秒 = 120 tick
        Assert.Equal(1299L, MonsterCorpseCore.PlanExpiryTick(1000, 60, 60f, 1300));  // 被重生前一 tick 截住
        Assert.Equal(1000L, MonsterCorpseCore.PlanExpiryTick(1000, 60, 0f, 1300));   // 零 = 当场消失（旧行为）
        Assert.Equal(4600L, MonsterCorpseCore.PlanExpiryTick(1000, 60, 60f, 0));     // 没排定重生 → 不截
        Assert.Equal(1060L, MonsterCorpseCore.PlanExpiryTick(1000, 0, 1f, 0));       // tps 非法退回 60

        // 反向对照：截断真的在起作用（同一个窗口，刷新早一步就没有截断）
        Assert.True(MonsterCorpseCore.PlanExpiryTick(1000, 60, 60f, 1300) < 1300,
                    "尸体窗口越过了重生 tick：同一实体 ID 会在同一个 tick 里既 Despawn 又 Spawn");
    }

    [Fact]
    public void 模板与行为表每一格都真的落到运行时配置()
    {
        // 这条是"声明了却没人写"的总闸：装配期把每一格都填成**互不相同**的数字，
        // 逐格问"值到了吗"。以前 ToConfig 里有一行 cfg.CorpseSeconds = 60f（既不来自库也没人读），
        // 单靠"文件读到了、快照装上了"永远发现不了——键名对账测的是另一回事。
        var path = Path.Combine(Path.GetTempPath(), "monster_fields_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
{"contentRevision":1,"templates":[
  {"monsterId":7,"name":"逐格对账怪","rank":1,"species":5,"wuxingDomain":2,"modelKey":"mk7","prefabGuid":"G-7","modelScale":1.75,
   "standState":2,"levelBase":31,"levelMin":29,"levelMax":33,"aggroSayId":8123,"corpseSeconds":17.5,
   "stats":{"hpBase":211,"hpPerLevel":13,"atkBase":47,"atkPerLevel":7,"defBase":23,"defPerLevel":11,"hpOverrideBps":1500,"atkOverrideBps":2500},
   "xp":{"xpBase":88,"xpPerLevel":9},
   "respawnBaseSeconds":21.5,"respawnPerLevelSeconds":0.25,
   "behavior":{"aiProfile":3,"isAggressive":0,"aggroRadius":19.5,"aggroConeDegrees":120.0,"leashRadius":57.0,
     "evadeImmunitySeconds":4.5,"assistRadius":13.0,"detectMask":5,"wanderRadius":7.5,"idleToPatrolSeconds":2.5,
     "walkSpeed":3.25,"moveSpeed":5.5,"followType":1,"meleeSwingSeconds":2.25,
     "rangedMinDistance":8.5,"rangedMaxDistance":24.0,"fleeHpPercent":15,"patrolRouteId":0},
   "skills":[]}
]}
""");
        try
        {
            var receipt = MonsterContentLoader.LoadFile(path);
            Assert.True(receipt.Ok, receipt.Describe() + " | " + string.Join("; ", receipt.Problems));
            Assert.True(MonsterContent.TryGetConfig(7, out var config) && config != null, "模板没装上");

            Assert.Equal("逐格对账怪", config!.Name);
            Assert.Equal((MonsterRank)1, config.Rank);
            Assert.Equal(5, config.Species);
            Assert.Equal(2, config.WuxingDomain);
            Assert.Equal("mk7", config.ModelKey);
            Assert.Equal("G-7", config.PrefabGuid);
            Assert.Equal(1.75f, config.ModelScale, 3);
            Assert.Equal(2, config.StandState);
            Assert.Equal(31, config.LevelBase);
            Assert.Equal(29, config.LevelMin);
            Assert.Equal(33, config.LevelMax);
            Assert.Equal(8123, config.AggroSayId);
            Assert.Equal(17.5f, config.CorpseSeconds, 3);           // 曾经被硬写成 60 的那一格
            Assert.Equal(21.5f, config.RespawnBaseSeconds, 3);
            Assert.Equal(0.25f, config.RespawnPerLevelSeconds, 3);

            Assert.Equal(211, config.Stats.HpBase);
            Assert.Equal(13, config.Stats.HpPerLevel);
            Assert.Equal(47, config.Stats.AtkBase);
            Assert.Equal(7, config.Stats.AtkPerLevel);
            Assert.Equal(23, config.Stats.DefBase);
            Assert.Equal(11, config.Stats.DefPerLevel);
            Assert.Equal(1500, config.Stats.HpOverrideBps);
            Assert.Equal(2500, config.Stats.AtkOverrideBps);

            Assert.Equal(88, config.Xp.XpBase);
            Assert.Equal(9, config.Xp.XpPerLevel);

            var b = config.Behavior;
            Assert.False(b.IsAggressive);
            Assert.Equal(19.5f, b.AggroRadius, 3);
            Assert.Equal(120f, b.AggroConeDegrees, 3);
            Assert.Equal(57f, b.LeashRadius, 3);
            Assert.Equal(4.5f, b.EvadeImmunitySeconds, 3);
            Assert.Equal(13f, b.AssistRadius, 3);
            Assert.Equal(5, b.DetectMask);
            Assert.Equal(7.5f, b.WanderRadius, 3);
            Assert.Equal(2.5f, b.IdleToPatrolSeconds, 3);
            Assert.Equal(3.25f, b.WalkSpeed, 3);
            Assert.Equal(5.5f, b.MoveSpeed, 3);
            Assert.Equal(Horizon.Game.Sim.Monsters.MonsterFollowType.Straight, b.FollowType);
            Assert.Equal(2.25f, b.MeleeSwingSeconds, 3);
            Assert.Equal(8.5f, b.RangedMinDistance, 3);
            Assert.Equal(24f, b.RangedMaxDistance, 3);
            Assert.Equal(15, b.FleeHpPercent);

            // 档位 3 = Master：五个能力位必须全开（"档位展开成了什么"的最后一环）
            Assert.True(b.Flags.UseThreatTable && b.Flags.UseSkills && b.Flags.UseNavigation
                        && b.Flags.UseAssist && b.Flags.UseTactics, b.Flags.Describe());
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    // ==================== 期 0：控制状态内核（切片 58） ====================

    /// <summary>
    /// 造一只「会追也会读条」的怪，从第 <paramref name="applyTick"/> 拍挂上 <paramref name="kind"/>
    /// （挂 <paramref name="durationTicks"/> 拍），跑满 <paramref name="ticks"/> 拍。
    /// <para>为什么量「位移 + 起手/收条」这一对：AC 的三条闸口差别就在**腿与嘴**
    /// （移动判据 <c>IsImmobilizedState = ROOT|STUNNED</c>，施法判据 <c>CheckCasterAuras</c>）。
    /// 只测「这一拍 Δ 是不是 0」会把「它本来就该站住」（走到点了、超出 leash、站桩施法）
    /// 一起算成「被拦住」——所以挂载刻意 <c>AllowMovement = true</c>、leash 放到 200 米。</para>
    /// </summary>
    private static (float Moved, int Starts, int Finishes, int BlockedMoveTicks, int BlockedCastTicks,
                    int ExpiredTotal, int InterruptSignals, int StartsWhileBlocked, int FinishesWhileBlocked,
                    float MovedWhileMoveBlocked)
        RunStatusScene(MonsterStatusKind kind, int applyTick, int durationTicks, int ticks,
                       int magnitudeBps = 0)
    {
        var config = new MonsterRuntimeConfig
        {
            TemplateId = 12501,
            Name = "控制检验兽",
            Rank = MonsterRank.Normal,
            Species = 9,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "kong_zhi",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // 威胁表与技能位都开着：期 0 走的是 v2 那一条通路。
        // 「v1 的怪身上装不了状态」这一条是**已知边界**（状态表住在 MonsterRuntimeState 里），
        // 要扩到 v1 得先定容器归谁，记在方案文档 §11.4，不在这里偷偷做。
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 1, 0, 0, 0);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 60f;             // 40 米外那个人必须看得见，否则整条用例没人追也没人读条
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 200f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MoveSpeed = 4f;
        behavior.WalkSpeed = 2f;
        behavior.MeleeSwingSeconds = 1f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20951, Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 600, CooldownMs = 200, MaxRange = 60f,
                AllowMovement = true,
            },
        };

        var targets = new[]
        {
            new MonsterAiTarget { EntityId = 7701UL, X = 40f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
        };
        var legacy = new MonsterAiConfig
        {
            MaxHp = 1000, AttackPower = 10, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 60f, LeashRadius = 200f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f,
        };
        var self = new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 1000,
        };
        var state = new MonsterRuntimeState();

        var moved = 0f;
        var starts = 0;
        var finishes = 0;
        var blockedMove = 0;
        var blockedCast = 0;
        var expiredTotal = 0;
        var interrupts = 0;
        var startsWhileBlocked = 0;
        var finishesWhileBlocked = 0;
        var movedWhileMoveBlocked = 0f;
        for (var tick = 0; tick < ticks; tick++)
        {
            if (tick == applyTick && kind != MonsterStatusKind.None)
            {
                // 与期 1 的宿主同一个入口：挂状态走 ApplyStatus（它会收走在读的吟唱并留下一条打断信号）
                state.ApplyStatus(kind, applyTick + durationTicks, tick, magnitudeBps);
            }

            var inputs = new MonsterRuntimeInputs(config, 10, default, null, false, tick, 60, null);
            var result = MonsterAiRuntime.Advance(self, state, targets, 1f / 60f, legacy, inputs);
            var step = MathF.Abs(result.Decision.DeltaX) + MathF.Abs(result.Decision.DeltaY);
            moved += step;
            if (result.Cast.BeginsCast) starts++;
            if (result.Cast.FinishesCast) finishes++;
            if (result.Cast.InterruptedCast) interrupts++;
            if (result.MovementBlockedByStatus)
            {
                blockedMove++;
                // 这一格才是"腿被拦住"的真判据：被拦的那些拍里位移必须**恰为 0**，
                // 而不是"总位移比基线小一半"——后者把"窗口外的正常追击"也算进来了
                // （第一版就是这么误判的：120 拍被拦住、180 拍正常追，比值 0.6 而不是 0.5）。
                movedWhileMoveBlocked += step;
            }
            if (result.CastingBlockedByStatus)
            {
                blockedCast++;
                if (result.Cast.BeginsCast) startsWhileBlocked++;
                if (result.Cast.FinishesCast) finishesWhileBlocked++;
            }
            expiredTotal += result.StatusesExpired;

            self = new MonsterAiSelf
            {
                X = self.X + result.Decision.DeltaX,
                Y = self.Y + result.Decision.DeltaY,
                Z = self.Z,
                Yaw = result.Decision.NextYaw,
                SpawnX = self.SpawnX,
                SpawnY = self.SpawnY,
                SpawnZ = self.SpawnZ,
                State = result.Decision.NextState,
                Hp = self.Hp,
                ThreatTargetId = result.Decision.ThreatTargetId,
                PatrolDirection = self.PatrolDirection,
            };
        }
        return (moved, starts, finishes, blockedMove, blockedCast, expiredTotal, interrupts,
                startsWhileBlocked, finishesWhileBlocked, movedWhileMoveBlocked);
    }

    /// <summary>跑 300 拍（5 秒 @60）：足够含住「60 拍起手、挂 120 拍、再跑 120 拍恢复」整段。</summary>
    private const int StatusSceneTicks = 300;

    [Fact]
    public void 控制状态的基线自己会走也会读条而没挂状态时逐字不变()
    {
        var baseline = RunStatusScene(MonsterStatusKind.None, 60, 120, StatusSceneTicks);
        Assert.True(baseline.Moved > 1f,
            $"基线里这只怪 300 拍只走了 {baseline.Moved:0.###} 米：追击通路没跑起来，"
            + "后面所有「位移被拦住」都无从谈起");
        Assert.True(baseline.Starts >= 1, "基线里一次都没起手：这一族判据全在测空集");
        Assert.Equal(0, baseline.BlockedMoveTicks);
        Assert.Equal(0, baseline.BlockedCastTicks);
        Assert.Equal(0, baseline.ExpiredTotal);
        Assert.Equal(0, baseline.InterruptSignals);

        // 「配了 0」与「什么都没配」必须同形（金轨迹护栏，与切片 19/22/25/36 同一族）。
        // 这里跑的是**同一份夹具的第二次**，差别只有 Apply 那一步被 None 拒收：
        // 如果 None 没被拒收，两支会给出不同读数，而"0 也算一条锁"这种病就露出来了。
        var sameKind = RunStatusScene(MonsterStatusKind.None, 60, 120, StatusSceneTicks);
        Assert.Equal(baseline.Moved, sameKind.Moved);
        Assert.Equal(baseline.Starts, sameKind.Starts);
    }

    [Fact]
    public void 眩晕既不让它走也不让它读条而定身只拦腿沉默只拦嘴()
    {
        // 这三条是 AC 的核心差别（方案文档 §2.2）：**两条独立闸口**，不能合成一位。
        var baseline = RunStatusScene(MonsterStatusKind.None, 60, 120, StatusSceneTicks);

        var stun = RunStatusScene(MonsterStatusKind.Stun, 60, 120, StatusSceneTicks);
        Assert.Equal(120, stun.BlockedMoveTicks);
        Assert.Equal(120, stun.BlockedCastTicks);
        Assert.Equal(0, stun.StartsWhileBlocked);
        Assert.Equal(0, stun.FinishesWhileBlocked);
        Assert.Equal(0f, stun.MovedWhileMoveBlocked);
        Assert.True(stun.Moved > 1f,
            "眩晕解开之后也没走了：闸口把这只怪**永久**钉住了，而不是只钉住那 120 拍");

        var root = RunStatusScene(MonsterStatusKind.Root, 60, 120, StatusSceneTicks);
        Assert.Equal(120, root.BlockedMoveTicks);
        Assert.Equal(0f, root.MovedWhileMoveBlocked);
        Assert.Equal(0, root.BlockedCastTicks);
        Assert.True(root.Starts >= 1,
            "定身中的怪一次都没起手：AC 的 IsImmobilizedState 只管腿，"
            + "把嘴一起封掉就是「定身 = 小号眩晕」");
        // 同一条窗口、同一条腿：定身与眩晕在**位移**上必须给出逐拍相同的读数。
        // 这一条是"两条闸口"的形状证据——合成一位就是一条，分开两位就是这一对。
        Assert.Equal(stun.Moved, root.Moved, 3);

        var silence = RunStatusScene(MonsterStatusKind.Silence, 60, 120, StatusSceneTicks);
        Assert.Equal(120, silence.BlockedCastTicks);
        Assert.Equal(0, silence.StartsWhileBlocked);
        Assert.Equal(0, silence.BlockedMoveTicks);
        Assert.Equal(0f, silence.MovedWhileMoveBlocked);
        Assert.Equal(baseline.Moved, silence.Moved, 3);   // 沉默对腿一点影响都没有（同夹具、同拍数、确定性）
    }

    [Fact]
    public void 眩晕与沉默在施加那一刻打断在读的条而到期那一拍自己恢复()
    {
        // AC 的「施加即打断」是字面的：`Unit::SetStunned` 里直接 `CastStop()`（Unit.cpp:18435）。
        // 沉默那一半本作是**刻意退化**（没有 PreventionType 那一格，见 MonsterStatusCore.InterruptsCastOnApply
        // 的注释），所以两档都要求"至少发出一条打断信号"，并把退化写在这里而不是藏进实现。
        var stun = RunStatusScene(MonsterStatusKind.Stun, 60, 120, StatusSceneTicks);
        Assert.True(stun.InterruptSignals >= 1,
            "眩晕施加时没有发出打断信号：客户端的读条会僵在原地（吟唱被悄悄清掉而没人广播）");
        var silence = RunStatusScene(MonsterStatusKind.Silence, 60, 120, StatusSceneTicks);
        Assert.True(silence.InterruptSignals >= 1, "沉默施加时没发打断信号：与上面同一格退化");

        // 减速这一档今天**不进任何闸口**（没有幅度来源，见 MonsterStatusKind.Slow 的注释）。
        // 这条断言钉的是「没做」这件事本身——哪天有人随手加一句 BlocksMovement(Slow)，它会红。
        var slow = RunStatusScene(MonsterStatusKind.Slow, 60, 120, StatusSceneTicks);
        Assert.Equal(0, slow.BlockedMoveTicks);
        Assert.Equal(0, slow.BlockedCastTicks);
        Assert.Equal(0, slow.InterruptSignals);
        Assert.True(slow.Moved > 1f && slow.Starts >= 1,
            "减速这一档把怪拦住了：它是**改速度**，不是禁行动（禁行动是定身/眩晕）");
        // 但强度没填（0）时连速度也不许改——"没填"与"填了 0"同义，都不生效。
        var noStatus = RunStatusScene(MonsterStatusKind.None, 60, 120, StatusSceneTicks);
        Assert.Equal(noStatus.Moved, slow.Moved, 4);

        // 到期单点回收：挂 60 拍、跑满，必须恰好回收 1 条，而且之后恢复行动。
        var shortStun = RunStatusScene(MonsterStatusKind.Stun, 60, 60, StatusSceneTicks);
        Assert.Equal(1, shortStun.ExpiredTotal);
        Assert.Equal(60, shortStun.BlockedMoveTicks);
        Assert.True(shortStun.Moved > 1f,
            $"到期之后还是不动（位移 {shortStun.Moved:0.##}）：回收只删了账没放开闸口");
    }

    [Fact]
    public void 状态表同档只刷新一条而填满之后再来同档条目数不变()
    {
        // 纯函数层：施加语义的全部非平凡分支。AC 对应
        // `Aura::TryRefreshStackOrCreate`（刷新或创建，Spell.cpp:3080）。
        var entries = new MonsterStatusEntry[MonsterStatusCore.MaxEntries];
        var count = 0;

        Assert.False(MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.None, 100L, 10L),
            "None 被收进表里了：0 是「没配」，不许变成一条永久的锁");
        Assert.False(MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Stun, 5L, 10L),
            "施加时已经过期的一条被收下了：那是「挂了但永远不会解开」的形状");
        Assert.Equal(0, count);

        Assert.True(MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Stun, 100L, 10L));
        // 同档再来一条更早到期的：只许往后推（AC 的 refresh 也不惩罚"先被晕再一次短晕"）。
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Stun, 60L, 20L);
        Assert.Equal(1, count);
        Assert.Equal(100L, entries[0].ExpireAtTick);
        Assert.True(MonsterStatusCore.Has(entries, count, MonsterStatusKind.Stun));

        // 填满四档（= 可挂档位数，见下面那条同数断言）之后再来同档 ⇒ 条目数不变、只刷新。
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Root, 200L, 20L);
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Silence, 300L, 20L);
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Slow, 350L, 20L);
        Assert.Equal(MonsterStatusCore.MaxEntries, count);
        Assert.True(MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Stun, 400L, 20L));
        Assert.Equal(MonsterStatusCore.MaxEntries, count);

        // 「表刚好装得下所有档」这件事必须有约束：加一档而忘了同步 MaxEntries，
        // 上面那条"填满"就会变成"溢出"，而溢出这一支今天没有人验过。
        Assert.Equal(System.Enum.GetValues(typeof(MonsterStatusKind)).Length - 1, MonsterStatusCore.MaxEntries);

        // 到期回收必须把尾部清干净：留着旧副本会让「这一档还在不在」取决于读到哪一格。
        var removed = MonsterStatusCore.Expire(entries, ref count, 401L);
        Assert.Equal(MonsterStatusCore.MaxEntries, removed);
        Assert.Equal(0, count);
        for (var i = 0; i < entries.Length; i++)
            Assert.Equal(MonsterStatusKind.None, entries[i].Kind);
        Assert.Equal(0, MonsterStatusCore.Expire(entries, ref count, 500L));   // 空表再回收不许报错也不许有读数
    }

    [Fact]
    public void 控制状态的词表与五行技能那一族同名而编号各归各()
    {
        // 两份词表迟早分家（本项目已经为「同一档两个名字」付过学费），所以名字集合要钉住；
        // 但**编号不许钉**：五行那一族的 StatusKind 是隐式顺序，动它的顺序会改 91 行种子数据，
        // 本枚举是显式编号的（0=None/1=Stun/2=Silence/3=Root/4=Slow），两侧只共用**词**。
        var wuxing = new HashSet<string>(
            System.Enum.GetNames(typeof(Horizon.Game.Sim.Skills.WuxingStatusEffects.StatusKind)),
            System.StringComparer.Ordinal);
        // 第一轮跑这条用例时它红了：`Silence` 在玩家技能那一族里**根本没有这一档**
        // （那边只有 ArmorDown/Vulnerability/DamageTakenDown/Stun/Root/Slow/Dot）。
        // 这不是"名字写错"，而是一条真实的**单边**：沉默今天只能由怪物侧施加，
        // 玩家技能配不出沉默。把它写成显式清单而不是放过——将来谁给玩家技能加了沉默这一档，
        // 这条会红，提醒他把清单收回去；而谁悄悄多造一个只有怪物有的新词，也会在这里被拦住。
        var monsterOnly = new HashSet<string>(System.StringComparer.Ordinal) { nameof(MonsterStatusKind.Silence) };
        var oneSided = new List<string>();
        foreach (var name in System.Enum.GetNames(typeof(MonsterStatusKind)))
        {
            if (name == nameof(MonsterStatusKind.None)) continue;   // 「没配」这一档那边没有，也不该有
            if (wuxing.Contains(name)) continue;
            oneSided.Add(name);
        }
        Assert.Equal(string.Join(",", monsterOnly.OrderBy(x => x)), string.Join(",", oneSided.OrderBy(x => x)));
        foreach (var name in monsterOnly)
            Assert.DoesNotContain(name, wuxing);   // 防空转：这一档必须真的不在那边，否则上面那条是恒等式

        // 默认值那一档必须是"什么都不拦"：结构体默认 0 撞上一条真锁是本工程反复踩过的那一族。
        Assert.Equal(MonsterStatusKind.None, default);
        Assert.False(MonsterStatusCore.BlocksMovement(default));
        Assert.False(MonsterStatusCore.BlocksCasting(default));
        Assert.False(MonsterStatusCore.InterruptsCastOnApply(default));
        Assert.False(MonsterStatusCore.IsUsable(default));
    }
    // ==================== 期 1a：状态经真 DS 落到玩家身上并封住移动输入（切片 59） ====================

    /// <summary>
    /// 造一只"会放带状态的技能"的怪 + 一个一直在按"往前走"的玩家，跑 <paramref name="ticks"/> 拍。
    /// <para>为什么在 DS 这一层测：期 0 只证明内核拿到状态会拦住腿与嘴，而
    /// <b>"怪物挂载上那两格 status_id / status_duration_seconds 到底有没有走到玩家的输入闸口"</b>
    /// 中间还隔着 施法完成 → 选目标 → 命中结算 → 施加 → 每拍回收 → 快照写位 六跳。</para>
    /// </summary>
    private static (int Applied, int Expired, int Rejected, long BlockedWhileStunned, long BlockedAfterExpiry,
                    bool StunnedBitSeen, float XWhileStunned, float XAfterExpiry, int StunActiveTicks,
                    int TauntRejectedByStatus, int AttackRejectedByStatus, string TauntOutcome,
                    bool SilencedBitSeen, bool RootedBitSeen)
        RunPlayerStunScene(int statusId, float statusSeconds, int ticks = 420)
    {
        const int templateId = 12511;
        const ulong playerId = 88001UL;
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "眩晕检验兽",
            Rank = MonsterRank.Normal,
            Species = 11,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "xuan_yun",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 10, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        behavior.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 1, 0, 0, 0);
        behavior.IsAggressive = true;
        behavior.AggroRadius = 30f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 80f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MoveSpeed = 4f;
        behavior.MeleeSwingSeconds = 1f;
        config.Behavior = behavior;
        config.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20961, Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 300, CooldownMs = 60000, MaxRange = 30f, AllowMovement = true,
                StatusId = statusId, StatusDurationSeconds = statusSeconds,
            },
        };
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 7001;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 30f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 1,          // 每拍都要快照：这一条要从快照里读玩家的状态位
                PlayerDefaultMaxHp = 100000,        // 玩家不许先被打死：这一条测的是控制，不是复活
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            Assert.True(world.AddPlayer(88001, 88001, 6f, 0f, 0f, out var why, "受控证人", 10), why);

            var lastX = 6f;
            var applied = 0;
            var expired = 0;
            var rejected = 0;
            var stunnedBitSeen = false;
            var silencedBitSeen = false;
            var rootedBitSeen = false;
            var blockedWhileStunned = 0L;
            var blockedAfterExpiry = 0L;
            var xWhileStunned = float.NaN;
            var xAfterExpiry = float.NaN;
            var stunActiveTicks = 0;
            var stunSeen = false;
            var expirySeen = false;
            var actionsTried = false;
            var tauntRejectedByStatus = 0;
            var attackRejectedByStatus = 0;
            var tauntOutcome = "(没试过)";
            var monsterId = Horizon.Game.Sim.SpawnDirectorCore.ComputeEntityId(7001, 0);

            for (var t = 0; t < ticks; t++)
            {
                // 每一拍都按"往前走"，且预测终点只声明"这一拍正常该走的距离"：
                // 被控时宿主会把这一份换成零位移、预测终点钉在当前位（否则反作弊会当成瞬移）。
                world.SubmitInput(88001, new Horizon.Game.Message.Sync.InputPacket
                {
                    CharacterId = playerId,
                    ClientTick = t + 1,
                    InputBits = 0u,
                    MoveX = 1f,
                    MoveY = 0f,
                    PredictedEndX = lastX + 4f / 60f,
                    PredictedEndY = 0f,
                    PredictedEndZ = 0f,
                    MaxSpeed = 4f,
                });

                var r = world.Tick();
                applied = world.PlayerStatusAppliedCount;
                expired = world.PlayerStatusExpiredCount;
                rejected = world.PlayerStatusRejectedCount;

                if (r.WorldSnapshot != null)
                {
                    foreach (var d in r.WorldSnapshot.Deltas)
                    {
                        if (d.EntityId != playerId) continue;
                        if (d.Transform.HasValue) lastX = d.Transform.Value.X;
                        if (d.State.HasValue && (d.State.Value.StateBits & Horizon.Game.Message.Sync.Components.EntityStateBits.Stunned) != 0)
                            stunnedBitSeen = true;
                        if (d.State.HasValue && (d.State.Value.StateBits & Horizon.Game.Message.Sync.Components.EntityStateBits.Silenced) != 0)
                            silencedBitSeen = true;
                        if (d.State.HasValue && (d.State.Value.StateBits & Horizon.Game.Message.Sync.Components.EntityStateBits.Rooted) != 0)
                            rootedBitSeen = true;
                    }
                }

                if (applied >= 1 && !stunSeen)
                {
                    stunSeen = true;
                    xWhileStunned = lastX;
                }
                if (stunSeen && !expirySeen) stunActiveTicks++;
                if (stunSeen && expired == 0) blockedWhileStunned = world.InputBlockedByStatus;
                if (expired >= 1 && !expirySeen)
                {
                    expirySeen = true;
                    xAfterExpiry = lastX;
                }
                if (expirySeen) blockedAfterExpiry = world.InputBlockedByStatus;

                // 受控期间的"技能类动作"必须被拒，而普通近战照打（AC 两条不同闸口）。
                // 只在这里试一次：Taunt 先试（应被状态拒），NormalAttack 紧接着试（不许因状态被拒）——
                // 两次读的都是**同一格计数器的增量**，所以"是谁让它红的"不靠猜。
                if (stunSeen && !expirySeen && !actionsTried)
                {
                    actionsTried = true;
                    var beforeTaunt = world.CombatActionRejectedByStatus;
                    var taunt = world.SubmitCombatAction(88001, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = playerId,
                        TargetId = monsterId,
                        ActionKind = Horizon.Game.Message.Sync.CombatActionKind.Taunt,
                    });
                    tauntOutcome = taunt.Outcome.ToString();
                    var afterTaunt = world.CombatActionRejectedByStatus;
                    world.SubmitCombatAction(88001, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = playerId,
                        TargetId = monsterId,
                        ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                    });
                    tauntRejectedByStatus = (int)(afterTaunt - beforeTaunt);
                    attackRejectedByStatus = (int)(world.CombatActionRejectedByStatus - afterTaunt);
                }
            }
            return (applied, expired, rejected, blockedWhileStunned, blockedAfterExpiry,
                    stunnedBitSeen, xWhileStunned, xAfterExpiry, stunActiveTicks,
                    tauntRejectedByStatus, attackRejectedByStatus, tauntOutcome,
                    silencedBitSeen, rootedBitSeen);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 怪物技能打出的眩晕在真DS里封住玩家移动输入而到期之后自己放开()
    {
        // 1 秒眩晕 @60tick = 60 拍。窗口取 420 拍（7 秒）：施法 300ms + 命中 + 眩晕 1 秒 + 恢复观察，
        // 窗口必须显著长于机制本身，否则红了会读成"参数没生效"。
        var scene = RunPlayerStunScene(statusId: 1, statusSeconds: 1f);
        _log.WriteLine($"玩家眩晕实测：施加 {scene.Applied} 次、到期 {scene.Expired} 条、被拒 {scene.Rejected} 次、" +
                       $"受控期间被拦输入 {scene.BlockedWhileStunned} 次、到期后累计 {scene.BlockedAfterExpiry} 次、" +
                       $"快照见到眩晕位={scene.StunnedBitSeen}、受控拍数 {scene.StunActiveTicks}");

        Assert.Equal(1, scene.Applied);
        Assert.Equal(0, scene.Rejected);          // 号认识、时长合法 ⇒ 一次都不许进"被拒"
        Assert.True(scene.StunnedBitSeen,
            "快照里一次都没出现眩晕位：客户端拿不到「被晕住了」，头顶表现与操作受限提示都无从谈起");
        Assert.True(scene.StunActiveTicks >= 50,
            $"受控只持续了 {scene.StunActiveTicks} 拍（1 秒应为 60 拍）：到期回收或施加时机不对");
        Assert.True(scene.BlockedWhileStunned >= 30,
            $"受控期间只拦下 {scene.BlockedWhileStunned} 次输入：玩家被晕着还能往前走");
        Assert.Equal(1, scene.Expired);

        // 到期之后不许再拦：这一条把「拦住」归因到状态本身，而不是「这个玩家一直走不动」。
        // 取 30 拍余量（0.5 秒）让"到期那一拍还在拦"这类差一错误也能被区分出来。
        var afterExpiryWindow = scene.BlockedAfterExpiry - scene.BlockedWhileStunned;
        Assert.True(afterExpiryWindow <= 2,
            $"到期之后又多拦了 {afterExpiryWindow} 次输入：回收与闸口没接在同一份表上");

        // 技能类动作：眩晕期间**拒**（AC 的 CheckCasterAuras），普通近战**不拒**（AttackerStateUpdate 只查 PACIFIED）。
        // 两条都读同一格计数器的增量 ⇒ 归因不靠猜。
        Assert.Equal(1, scene.TauntRejectedByStatus);
        Assert.Equal("Invalid", scene.TauntOutcome);
        Assert.Equal(0, scene.AttackRejectedByStatus);
    }

    [Fact]
    public void 挂载不填状态号时玩家的移动一次都不被拦()
    {
        // 负对照：同一夹具、同一条"一直往前走"的输入，只把 status_id 填成 0。
        // 少了这一半，上面那条「被拦 ≥30 次」可能只是「这个场景里输入本来就过不去」。
        var scene = RunPlayerStunScene(statusId: 0, statusSeconds: 0f);
        Assert.Equal(0, scene.Applied);
        Assert.Equal(0, scene.Rejected);          // 0 = 没配，不是"配错" ⇒ 不计数
        Assert.Equal(0L, scene.BlockedWhileStunned);
        Assert.Equal(0L, scene.BlockedAfterExpiry);
        Assert.False(scene.StunnedBitSeen);
        // 没被控时技能类动作也不许无端被拒：这一半防的是"闸门恒真"（那种实现会让上面那条永远绿）。
        Assert.Equal(0, scene.TauntRejectedByStatus);
        Assert.Equal(0, scene.AttackRejectedByStatus);
    }

    [Fact]
    public void 状态号不认识的挂载会被拒绝并计数而不是静默当成没配()
    {
        // 号段外的值：今天 `MonsterStatusKind` 只到 4。这条钉的是"解析不许宽松"——
        // 宽松解析会把「配了 7 而 7 不存在」变成「没配」，玩家只觉得这怪从不控人。
        var scene = RunPlayerStunScene(statusId: 7, statusSeconds: 1f);
        Assert.Equal(0, scene.Applied);
        Assert.True(scene.Rejected >= 1,
            "不认识的号一次都没被拒：配错了号在运行期完全看不出来");
    }
    // ==================== 期 1b：玩家技能把状态挂到怪物身上（切片 60，协议 v16） ====================

    /// <summary>
    /// 造一只"会追人"的怪 + 一个按 61 拍一刀的玩家；每刀都带 <paramref name="statusId"/>（挂 1 秒）。
    /// 返回：施加上报数、被拒数、无运行态数，以及"第一刀之后怪物位移"的两个读数（受控窗口内 / 恢复之后）。
    /// <para>为什么用位移当效果层：怪的 `MovementBlockedByStatus` 是内核回执、宿主没转发；
    /// 而"这怪有没有停在原地"才是玩家看得见的那一半。两者由 `MonsterStatusAppliedCount` 串起来——
    /// 若它涨了而位置照旧变，就是闸口没接上；若它没涨，则连施加都没到。</para>
    /// </summary>
    private static (int Applied, int Rejected, int Unsupported, float MovedDuringStun, float MovedAfterExpiry)
        RunMonsterStunScene(int statusId, bool giveAiCapabilities, int ticks = 420, int magnitudeBps = 0)
    {
        const int templateId = 12521;
        const ulong playerId = 88011UL;
        var config = new MonsterRuntimeConfig
        {
            TemplateId = templateId,
            Name = "怪物受控检验兽",
            Rank = MonsterRank.Normal,
            Species = 12,
            LevelBase = 10,
            LevelMin = 10,
            LevelMax = 10,
            ModelKey = "mon_ctrl",
            Stats = new MonsterStatsConfig { HpBase = 100000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var behavior = MonsterBehaviorConfig.Default;
        // giveAiCapabilities=false ⇒ 能力位全关 ⇒ 宿主不会给它建运行态（v1），状态无处可挂。
        behavior.Flags = giveAiCapabilities
            ? MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 0, 0, 0, 0)
            : MonsterAiFlags.None;
        behavior.IsAggressive = true;
        behavior.AggroRadius = 40f;
        behavior.AggroConeDegrees = 360f;
        behavior.LeashRadius = 120f;
        behavior.WanderRadius = 0f;
        behavior.IdleToPatrolSeconds = 0f;
        behavior.MoveSpeed = 4f;
        behavior.MeleeSwingSeconds = 1f;
        config.Behavior = behavior;
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateId] = config;
        MonsterContent.Install(bundle);
        try
        {
            var point = SpawnPointConfig.Default;
            point.SpawnPointId = 7101;
            point.MonsterTemplateId = templateId;
            point.MaxAlive = 1;
            point.X = 0f; point.Y = 0f; point.Z = 0f;
            point.AggroRadius = 40f;
            point.MoveSpeed = 4f;

            var world = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 1,
                PlayerDefaultMaxHp = 1000000,
                PlayerAttackPower = 5000,        // 必须真的打出伤害：状态挂在"命中且结算成功"那一步之后（零伤不挂）
                PlayerAttackRange = 60f,
            });
            world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
            // 玩家站在怪的 **−X 侧**：默认朝向 +X，站 +X 等于背对目标，一刀都不会落
            // （这一条在本仓的夹具里反复踩过，别退回）。
            // 距离取 60 米：怪在整段用例里都还在**赶路**，于是"停住/恢复"才有位移可看——
            // 贴着打时它一到位就进入 Attack 状态（本来就不动），那种读数证明不了闸口。
            Assert.True(world.AddPlayer(88011, 88011, -60f, 0f, 0f, out var why, "控怪证人", 10), why);

            var monsterId = Horizon.Game.Sim.SpawnDirectorCore.ComputeEntityId(7101, 0);
            var lastMonsterX = 0f;
            var movedDuringStun = 0f;
            var movedAfterExpiry = 0f;
            var stunEndsAtTick = -1L;
            var hitTick = -1L;
            var applied = 0;
            var rejected = 0;
            var unsupported = 0;
            var prevX = 0f;

            for (var t = 0; t < ticks; t++)
            {
                if (t % 61 == 0)
                {
                    // 状态只随**前两刀**带：每刀都带等于每秒重新晕一次，那只怪整场都在受控，
                    // "到期之后会不会动"就永远测不到（第一版就是这么红在 0.07 米上的）。
                    var withStatus = t <= 61;
                    world.SubmitCombatAction(88011, new Horizon.Game.Message.Sync.CombatActionPacket
                    {
                        AttackerId = playerId,
                        TargetId = monsterId,
                        ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                        StatusId = withStatus ? statusId : 0,
                        StatusDurationSeconds = withStatus && statusId > 0 ? 1f : 0f,
                        StatusMagnitudeBps = withStatus ? magnitudeBps : 0,
                    });
                }

                var r = world.Tick();
                applied = world.MonsterStatusAppliedCount;
                rejected = world.MonsterStatusRejectedCount;
                unsupported = world.MonsterStatusUnsupportedCount;

                if (r.WorldSnapshot != null)
                {
                    foreach (var d in r.WorldSnapshot.Deltas)
                    {
                        if (d.EntityId != monsterId || !d.Transform.HasValue) continue;
                        var x = d.Transform.Value.X;
                        if (hitTick < 0 && applied > 0)
                        {
                            hitTick = t;
                            stunEndsAtTick = t + 60;      // 动作包挂 1 秒 = 60 拍
                            prevX = x;
                            lastMonsterX = x;
                            continue;
                        }
                        if (hitTick >= 0)
                        {
                            var step = MathF.Abs(x - prevX);
                            if (t <= stunEndsAtTick) movedDuringStun += step;
                            else if (t <= stunEndsAtTick + 60) movedAfterExpiry += step;
                            prevX = x;
                            lastMonsterX = x;
                        }
                    }
                }
            }
            Assert.True(hitTick >= 0 || applied == 0, $"施加计数 {applied} 涨了却没定位到命中的那一拍");
            return (applied, rejected, unsupported, movedDuringStun, movedAfterExpiry);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 玩家技能把眩晕挂到怪物身上而那只怪真的停住并在到期之后恢复()
    {
        var scene = RunMonsterStunScene(statusId: 1, giveAiCapabilities: true);
        _log.WriteLine($"怪物受控实测：施加 {scene.Applied} 次、被拒 {scene.Rejected} 次、无运行态 {scene.Unsupported} 次、" +
                       $"受控窗口位移 {scene.MovedDuringStun:0.###} 米、恢复后位移 {scene.MovedAfterExpiry:0.###} 米");

        Assert.True(scene.Applied >= 1,
            $"动作包带的眩晕一次都没挂到怪身上（施加 {scene.Applied}、被拒 {scene.Rejected}）：v16 那两格没接到宿主");
        Assert.Equal(0, scene.Unsupported);
        // 受控窗口内它必须基本不动（允许 1 米以内的追赶残量：命中那一拍它可能已经迈出半步）。
        Assert.True(scene.MovedDuringStun < 1f,
            $"眩晕期间那只怪还走了 {scene.MovedDuringStun:0.##} 米：内核两条闸口对这个受击来源没生效");
        // 恢复之后必须重新会动，否则"停住"只是被永久钉死。
        Assert.True(scene.MovedAfterExpiry > 1f,
            $"到期之后它仍然不动（{scene.MovedAfterExpiry:0.##} 米）：回收只删了账没放开闸口");

        // 边界：能力位全关的怪没有运行态 ⇒ 状态无处可挂。这一格必须**计数**而不是假装成功——
        // 生产那 11 只种子怪今天全是 v1，不把这条边界念出来，"配了控制而怪照旧追人"就会被当成配错。
        var v1 = RunMonsterStunScene(statusId: 1, giveAiCapabilities: false);
        Assert.Equal(0, v1.Applied);
        Assert.True(v1.Unsupported >= 1,
            "v1 的怪装不了状态却一次都没记进「无运行态」：这条边界在运行期完全看不见");
    }
    [Fact]
    public void 减速按强度缩放位移而强度没填时连速度也不改()
    {
        // 纯函数层三条规则：最强那条赢（不相乘）、上限 9000、0 = 没填。
        var entries = new MonsterStatusEntry[MonsterStatusCore.MaxEntries];
        var count = 0;
        Assert.Equal(1f, MonsterStatusCore.SpeedScale(entries, count));
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Slow, 100L, 10L, 0);
        Assert.Equal(1f, MonsterStatusCore.SpeedScale(entries, count), 4);
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Slow, 200L, 10L, 5000);
        Assert.Equal(0.5f, MonsterStatusCore.SpeedScale(entries, count), 4);
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Slow, 200L, 10L, 7000);
        Assert.Equal(0.3f, MonsterStatusCore.SpeedScale(entries, count), 4);
        MonsterStatusCore.Apply(entries, ref count, MonsterStatusKind.Slow, 200L, 10L, 50000);
        Assert.Equal(0.1f, MonsterStatusCore.SpeedScale(entries, count), 4);

        // 效果层：同一份追击夹具、整段都挂减速，只改强度 ⇒ 总位移按比例缩小。
        // 窗口覆盖全场（applyTick=0、时长=总拍数），否则"120 拍减速 ÷ 300 拍"的比值天然是 0.8，
        // 那种期望会把正确实现读成错的。
        var baseline = RunStatusScene(MonsterStatusKind.None, 0, StatusSceneTicks, StatusSceneTicks);
        var half = RunStatusScene(MonsterStatusKind.Slow, 0, StatusSceneTicks, StatusSceneTicks, magnitudeBps: 5000);
        Assert.Equal(0, half.BlockedMoveTicks);
        Assert.True(baseline.Moved > 1f, "基线没走动：这条判据在空集上");
        var ratio = half.Moved / baseline.Moved;
        Assert.True(ratio > 0.45f && ratio < 0.55f,
            $"减速 50% 之后总位移比 {ratio:0.###}（期望 ≈0.5）：强度那一格没进位移");
    }
    [Fact]
    public void 校验器对状态强度三条口径各有正反对照()
    {
        const int slow = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Slow;
        const int stun = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Stun;

        static Horizon.MonsterCli.MonsterValidationResult Run(int statusId, double seconds, int magnitude)
        {
            var d = new Horizon.MonsterCli.MonsterDraft { MonsterId = 100014, Name = "强度校验" };
            d.Behavior.AggroRadius = 10;
            d.Behavior.LeashRadius = 30;
            d.Behavior.MeleeSwingSeconds = 1.5f;
            d.Skills.Add(new Horizon.MonsterCli.MonsterSkillDraft
            {
                SkillId = 9001, ChanceBps = 10000, MaxRange = 8, TargetCount = 1, DamageScaleBps = 10000,
                StatusId = statusId, StatusDurationSeconds = seconds, StatusMagnitudeBps = magnitude,
            });
            return Horizon.MonsterCli.MonsterValidator.Validate(d, null);
        }

        bool Warns(Horizon.MonsterCli.MonsterValidationResult r)
            => r.Warnings.Any(w => w.Contains("statusMagnitudeBps", StringComparison.Ordinal));

        // 减速 + 强度 0 ⇒ 必须念：运行期按「没填」处理，这条技能不会减速任何人。
        Assert.True(Warns(Run(slow, 3.0, 0)), "减速没给强度却不念：它等于没配");
        // 减速 + 5000 ⇒ 一次都不许念（合法形态）。
        Assert.False(Warns(Run(slow, 3.0, 5000)));
        // 越界 ⇒ Error（10000 等于定身，两档会分不清）。
        var bad = Run(slow, 3.0, 9500);
        Assert.True(bad.Errors.Any(e => e.Contains("statusMagnitudeBps", StringComparison.Ordinal)),
                    "越界强度没被拒：9000 以上会把减速做成定身");
        Assert.False(bad.Ok);
        // 眩晕 + 强度 ⇒ 念：这一格只对减速生效，配了不会报错也不会有效果。
        Assert.True(Warns(Run(stun, 3.0, 5000)), "眩晕配强度却不念：配了没效果是这一族最贵的坏形状");
    }
    [Fact]
    public void 减速让怪物在受控期间走得更慢而到期之后恢复原速()
    {
        // 效果层放在**怪物**身上：它的移动由内核驱动，位移是确定可读的。
        // （玩家侧那条"移动输入被按比例缩小"目前只能读机制回执 `InputSlowedByStatus`——
        //  本仓的离线夹具里玩家的位置由 SimWorld 按输入推进，而测试直接构造的 InputPacket
        //   不会真的让玩家走起来；这属于夹具能力缺口，如实登记在文档里，不假装测过。）
        const int slow = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Slow;
        var half = RunMonsterStunScene(slow, giveAiCapabilities: true, magnitudeBps: 5000);
        _log.WriteLine($"怪物减速实测：施加 {half.Applied} 次、受控期位移 {half.MovedDuringStun:0.###} m、" +
                       $"到期后位移 {half.MovedAfterExpiry:0.###} m");

        Assert.True(half.Applied >= 1, "减速一次都没挂到怪身上：这条判据在空集上");
        Assert.Equal(0, half.Unsupported);
        Assert.True(half.MovedAfterExpiry > 1f,
            $"到期之后它没动（{half.MovedAfterExpiry:0.##} m）：基线为空，比值说明不了问题");
        // 同一段窗口（60 拍）里：减速 50% ⇒ 位移应当≈恢复后的一半。
        var ratio = half.MovedDuringStun / half.MovedAfterExpiry;
        Assert.True(ratio > 0.4f && ratio < 0.62f,
            $"减速期走 {half.MovedDuringStun:0.###} m、恢复后 {half.MovedAfterExpiry:0.###} m"
            + $"（比值 {ratio:0.###}，期望 ≈0.5）：强度那一格没进位移");

        // 负对照：强度 0（= 没填）⇒ 位移与"没有减速"同量级（比值≈1）。
        var none = RunMonsterStunScene(slow, giveAiCapabilities: true, magnitudeBps: 0);
        Assert.True(none.Applied >= 1);
        var flat = none.MovedDuringStun / none.MovedAfterExpiry;
        Assert.True(flat > 0.85f && flat < 1.15f,
            $"强度 0 也改了速度（比值 {flat:0.###}）：0 必须是「没填」");
    }

    [Fact]
    public void 三档状态各自点亮自己的位而眩晕连带定身()
    {
        // 位与档是**多对一**的关系，写错一个就会让客户端干错事：
        // 眩晕 ⇒ Stunned + Rooted（AC 的 SetStunned 内部 SetRooted），定身 ⇒ 只 Rooted，沉默 ⇒ 只 Silenced。
        // 三档各跑一遍同一份夹具（同一条怪、同一个玩家、同一段窗口），差别只有挂上去的那一档。
        const int stun = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Stun;
        const int silence = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Silence;
        const int root = (int)Horizon.Game.Sim.Monsters.MonsterStatusKind.Root;

        var s = RunPlayerStunScene(stun, 1f);
        Assert.True(s.StunnedBitSeen && s.RootedBitSeen, "眩晕没有同时点亮 Stunned+Rooted");
        Assert.False(s.SilencedBitSeen);

        var q = RunPlayerStunScene(silence, 1f);
        Assert.True(q.SilencedBitSeen, "沉默没有点亮 Silenced：客户端不知道玩家放不出技能");
        Assert.False(q.StunnedBitSeen);
        Assert.False(q.RootedBitSeen);

        var r = RunPlayerStunScene(root, 1f);
        Assert.True(r.RootedBitSeen, "定身没有点亮 Rooted");
        Assert.False(r.StunnedBitSeen);
        Assert.False(r.SilencedBitSeen);
    }
    [Fact]
    public void 玩家输入真的能把他推动()
    {
        // 这条是**玩家侧效果层用例的前提闸门**：切片 63 曾想用"玩家每次输入的位移"量减速，
        // 结果两个窗口都是 0 米——当时误读成"减速没生效"，其实是那套夹具根本没把玩家驱动起来。
        // 这里用最小世界（无怪、无内容）单独证明"按着前进就真的会往前走"，
        // 以后谁给玩家侧写位移类判据，先看这一条；它红了说明前提没了，别去怪被测特性。
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 1, PlayerDefaultMaxHp = 1000000,
        });
        Assert.True(world.AddPlayer(91001, 91001, 0f, 0f, 0f, out var why, "探针", 10), why);
        var lastX = 0f;
        var trail = new List<string>();
        for (var t = 0; t < 120; t++)
        {
            world.SubmitInput(91001, new Horizon.Game.Message.Sync.InputPacket
            {
                CharacterId = 91001UL, ClientTick = t + 1, InputBits = 0u,
                MoveX = 1f, MoveY = 0f,
                PredictedEndX = lastX + 4f / 60f, PredictedEndY = 0f, PredictedEndZ = 0f, MaxSpeed = 4f,
            });
            var r = world.Tick();
            if (r.WorldSnapshot != null)
            {
                foreach (var d in r.WorldSnapshot.Deltas)
                {
                    if (d.EntityId == 91001UL && d.Transform.HasValue) lastX = d.Transform.Value.X;
                }
            }
            if (t % 20 == 0) trail.Add($"t{t}:{lastX:0.###}");
        }
        _log.WriteLine("玩家推动探针： " + string.Join(" ", trail) + $" 终={lastX:0.###} 丢={world.DuplicateInputDroppedCount}");
        // 120 拍 = 2 秒 @4 m/s ≈ 7.9 米（首帧按 SimWorld 的"首输入置零"设计不动）。
        // 门槛取 6 米：既证明"真的在按速度走"，又不把首帧语义钉死。
        Assert.True(lastX > 6f, $"120 拍按着前进，玩家只走到 x={lastX:0.###}（trail={string.Join(" ", trail)}）");

        // 第二个世界：**场上有一只被动怪**（离 50 米、不会索敌）。
        // 这一半回答的是"刚才那种动不了，是因为'场上有怪'，还是因为'在打架'"——
        // 如果这里也不动，那就是"有怪就动不了"的产品级问题，必须继续查；如果照常走，问题就落在战斗态那一侧。
        const int probeTemplate = 12551;
        var cfg = new MonsterRuntimeConfig
        {
            TemplateId = probeTemplate, Name = "探针兽", Rank = MonsterRank.Normal, Species = 15,
            LevelBase = 10, LevelMin = 10, LevelMax = 10, ModelKey = "probe_mob",
            Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var beh = MonsterBehaviorConfig.Default;
        beh.Flags = MonsterAiFlags.None;
        beh.IsAggressive = false;
        beh.AggroRadius = 5f;
        beh.LeashRadius = 30f;
        beh.WanderRadius = 0f;
        cfg.Behavior = beh;
        var bundle2 = new MonsterContentBundle { ContentRevision = 1 };
        bundle2.ByTemplateId[probeTemplate] = cfg;
        MonsterContent.Install(bundle2);
        try
        {
            var pt = SpawnPointConfig.Default;
            pt.SpawnPointId = 7401;
            pt.MonsterTemplateId = probeTemplate;
            pt.MaxAlive = 1;
            pt.X = 50f; pt.Y = 50f; pt.Z = 0f;
            pt.AggroRadius = 5f;
            var world2 = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 1, PlayerDefaultMaxHp = 1000000,
            });
            world2.ConfigureSpawnPoints(new List<SpawnPointConfig> { pt });
            Assert.True(world2.AddPlayer(91002, 91002, 0f, 0f, 0f, out var why2, "探针2", 10), why2);
            var lastX2 = 0f;
            for (var t = 0; t < 120; t++)
            {
                world2.SubmitInput(91002, new Horizon.Game.Message.Sync.InputPacket
                {
                    CharacterId = 91002UL, ClientTick = t + 1, InputBits = 0u,
                    MoveX = 1f, MoveY = 0f,
                    PredictedEndX = lastX2 + 4f / 60f, PredictedEndY = 0f, PredictedEndZ = 0f, MaxSpeed = 4f,
                });
                var r2 = world2.Tick();
                if (r2.WorldSnapshot != null)
                {
                    foreach (var d in r2.WorldSnapshot.Deltas)
                    {
                        if (d.EntityId == 91002UL && d.Transform.HasValue) lastX2 = d.Transform.Value.X;
                    }
                }
            }
            _log.WriteLine($"有被动怪在场时：终 x={lastX2:0.###}");
            Assert.True(lastX2 > 6f, $"场上有被动怪时玩家只走到 x={lastX2:0.###}：问题不在战斗态，而是'有怪就动不了'");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 减速让玩家每拍走得更少而到期之后回到原速()
    {
        // 玩家侧减速的效果层。**沿 X 轴走、读 Transform.X**：快照把 ECS 的 y（深度）映射到
        // `Transform.Z`，按同名轴读 MoveY 的位移会恒为 0——切片 63/67 那两次"走不动"就是这个读数错，
        // 不是产品缺陷（探针里 XYZ 三个分量一起打印才看得出来）。
        const int templateId = 12571;
        var cfg = new MonsterRuntimeConfig
        {
            TemplateId = templateId, Name = "玩家减速检验兽", Rank = MonsterRank.Normal, Species = 17,
            LevelBase = 10, LevelMin = 10, LevelMax = 10, ModelKey = "slow_player",
            Stats = new MonsterStatsConfig { HpBase = 100000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
            Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
        };
        var beh = MonsterBehaviorConfig.Default;
        beh.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 1, 0, 0, 0);
        beh.IsAggressive = true;
        beh.AggroRadius = 40f;
        beh.AggroConeDegrees = 360f;
        beh.LeashRadius = 120f;
        beh.WanderRadius = 0f;
        beh.IdleToPatrolSeconds = 0f;
        beh.MoveSpeed = 4f;
        cfg.Behavior = beh;
        cfg.Skills = new[]
        {
            new MonsterSkillMount
            {
                SkillId = 20991, Slot = MonsterSkillSlot.Combat,
                ChanceBasisPoints = MonsterCastingCore.ChanceBasisPointsMax,
                CastMs = 300, CooldownMs = 60000, MaxRange = 30f, AllowMovement = true,
                StatusId = (int)MonsterStatusKind.Slow, StatusDurationSeconds = 2f,
                StatusMagnitudeBps = 5000,
            },
        };
        var bundle = new MonsterContentBundle { ContentRevision = 1 };
        bundle.ByTemplateId[templateId] = cfg;
        MonsterContent.Install(bundle);
        try
        {
            var pt = SpawnPointConfig.Default;
            pt.SpawnPointId = 7601;
            pt.MonsterTemplateId = templateId;
            pt.MaxAlive = 1;
            pt.X = 0f; pt.Y = 0f; pt.Z = 0f;
            pt.AggroRadius = 40f;
            var w = new DsWorldSession(new DsWorldSessionOptions
            {
                ShardId = 0, TicksPerSecond = 60, SnapshotIntervalTicks = 1, PlayerDefaultMaxHp = 1000000,
            });
            w.ConfigureSpawnPoints(new List<SpawnPointConfig> { pt });
            Assert.True(w.AddPlayer(91011, 91011, 6f, 0f, 0f, out var why, "减速证人", 10), why);

            var x = 6f;
            var before = (n: 0, sum: 0f);
            var during = (n: 0, sum: 0f);
            var after = (n: 0, sum: 0f);
            var slowSeen = false;
            var expirySeen = false;
            for (var t = 0; t < 480; t++)
            {
                w.SubmitInput(91011, new Horizon.Game.Message.Sync.InputPacket
                {
                    CharacterId = 91011UL, ClientTick = t + 1, InputBits = 0u,
                    MoveX = 1f, MoveY = 0f,
                    PredictedEndX = x + 4f / 60f, PredictedEndY = 0f, PredictedEndZ = 0f, MaxSpeed = 4f,
                });
                var r = w.Tick();
                var xNew = x;
                if (r.WorldSnapshot != null)
                {
                    foreach (var d in r.WorldSnapshot.Deltas)
                    {
                        if (d.EntityId == 91011UL && d.Transform.HasValue) xNew = d.Transform.Value.X;
                    }
                }
                var step = MathF.Abs(xNew - x);
                x = xNew;

                if (w.PlayerStatusAppliedCount == 0) { before.n++; before.sum += step; }
                else if (w.PlayerStatusExpiredCount == 0) { slowSeen = true; during.n++; during.sum += step; }
                else { expirySeen = true; after.n++; after.sum += step; }
            }
            Assert.True(slowSeen && expirySeen, "减速窗口或到期窗口没出现：三段判据在空集上");

            var perBefore = before.sum / Math.Max(1, before.n);
            var perDuring = during.sum / Math.Max(1, during.n);
            var perAfter = after.sum / Math.Max(1, after.n);
            _log.WriteLine($"玩家减速三段：前 {perBefore:0.#####} m/拍（{before.n} 拍）、中 {perDuring:0.#####}（{during.n}）、" +
                           $"后 {perAfter:0.#####}（{after.n}）、被缩放输入 {w.InputSlowedByStatus} 次");
            Assert.True(perBefore > 0.02f, $"减速前每拍只走 {perBefore:0.#####} m：前提没了，别去怪减速");
            Assert.True(w.InputSlowedByStatus > 0, "输入一次都没被缩放：减速对玩家移动没有任何作用");
            var ratioDuring = perDuring / perBefore;
            Assert.True(ratioDuring > 0.4f && ratioDuring < 0.62f,
                $"减速中每拍 {perDuring:0.#####} vs 减速前 {perBefore:0.#####}（比值 {ratioDuring:0.###}，期望≈0.5）");
            if (perAfter > 0f)
            {
                var ratioAfter = perAfter / perBefore;
                Assert.True(ratioAfter > 0.85f && ratioAfter < 1.15f,
                    $"到期后每拍 {perAfter:0.#####} vs 减速前 {perBefore:0.#####}（比值 {ratioAfter:0.###}，期望≈1）");
            }
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 藏着的人只有带隐蔽探测那一位的怪才看得见()
    {
        // D32a：`detect_mask` 是本作独立口径，只接"能不能索敌"这一道闸（AC 的连续值公式明写不抄）。
        // 四条对照：v1/v2 × 看得见/看不见，只差 `DetectMask` 那一位——否则"没选中"分不清是判据管的还是别的东西。
        var hidden = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = 5f, Y = 0f, IsAlive = true, IsAttackable = true, IsHidden = true },
        };
        var visible = new[]
        {
            new MonsterAiTarget { EntityId = 91UL, X = 5f, Y = 0f, IsAlive = true, IsAttackable = true, IsHidden = false },
        };

        static MonsterAiConfig V1(int detectMask) => new MonsterAiConfig
        {
            MaxHp = 100, AttackPower = 1, AttackRange = 2f, MoveSpeed = 4f,
            AggroRadius = 20f, AggroConeDegrees = 360f, LeashRadius = 60f, WanderRadius = 0f,
            IdleToPatrolSeconds = 0f, AttackCooldownSeconds = 1f, DetectMask = detectMask,
        };

        static MonsterAiSelf Idle() => new MonsterAiSelf
        {
            X = 0f, Y = 0f, Z = 0f, SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
            State = MonsterAiState.Idle, Hp = 100,
        };

        // v1：默认只看视觉（0x1）⇒ 看不见藏着的人；带上 bit2（0x5）⇒ 看得见。
        var v1Blind = MonsterAiCore.Decide(Idle(), hidden, 1f / 60f, V1(0x1), 100L, 60);
        Assert.Equal(0UL, v1Blind.ThreatTargetId);
        var v1Sharp = MonsterAiCore.Decide(Idle(), hidden, 1f / 60f, V1(0x5), 100L, 60);
        Assert.Equal(91UL, v1Sharp.ThreatTargetId);
        // 没藏着的人：两种检测位都看得见（证明"没选中"确实是藏那一位造成的，而不是整个索敌坏了）。
        Assert.Equal(91UL, MonsterAiCore.Decide(Idle(), visible, 1f / 60f, V1(0x1), 100L, 60).ThreatTargetId);
        Assert.Equal(91UL, MonsterAiCore.Decide(Idle(), visible, 1f / 60f, V1(0x5), 100L, 60).ThreatTargetId);

        // v2：同四条，走 Advance（内容位 + 空盟友清单）。
        static (ulong Threat, bool Seeded) V2(int detectMask, MonsterAiTarget[] targets)
        {
            var cfg = new MonsterRuntimeConfig
            {
                TemplateId = 12581, Name = "检测检验兽", Rank = MonsterRank.Normal, Species = 18,
                LevelBase = 10, LevelMin = 10, LevelMax = 10, ModelKey = "detect",
                Stats = new MonsterStatsConfig { HpBase = 100, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
                Xp = new MonsterXpConfig(10, 1, new[] { 1.0 }),
            };
            var beh = MonsterBehaviorConfig.Default;
            beh.Flags = MonsterAiFlags.Resolve(MonsterAiProfile.Skilled, 1, 0, 0, 0, 0);
            beh.IsAggressive = true;
            beh.AggroRadius = 20f;
            beh.AggroConeDegrees = 360f;
            beh.LeashRadius = 60f;
            beh.WanderRadius = 0f;
            beh.IdleToPatrolSeconds = 0f;
            beh.DetectMask = detectMask;
            cfg.Behavior = beh;
            var state = new MonsterRuntimeState();
            var inputs = new MonsterRuntimeInputs(cfg, 10, default, null, false, 100L, 60, null);
            var r = MonsterAiRuntime.Advance(Idle(), state, targets, 1f / 60f, V1(detectMask), inputs);
            return (r.Decision.ThreatTargetId, r.SeededByAssist);
        }

        Assert.Equal(0UL, V2(0x1, hidden).Threat);
        Assert.Equal(91UL, V2(0x5, hidden).Threat);
        Assert.Equal(91UL, V2(0x1, visible).Threat);
        Assert.Equal(91UL, V2(0x5, visible).Threat);
    }
}

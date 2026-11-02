using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Npcs;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 死亡贡献经验随玩家等级衰减（原话："随玩家等级变化反比例衰减"）。
/// <para>
/// 既有的用例钉的是"大号杀小号 = 灰名断供"和"低等级观察者有经验"两端；
/// 这里钉中间的**形状**：同一只怪、玩家等级逐级升高时经验不回升，并且到灰名边界归零。
/// 只要有人把让分曲线的方向写反（或接错表），这条就会红；两端用例发现不了"中途反弹"。
/// </para>
/// <para>
/// 期望值不写死等级表：灰名边界从 <see cref="MonsterDifficultyCore.GrayLevel"/> 现算，
/// 表换了这条仍然按同一份数据源判对错（记忆 [期望值翻转开关要自己查数据源]）。
/// </para>
/// </summary>
public class NpcKillXpDecayTests
{
    [Fact]
    public void HostileNpc_XpNeverRisesAsPlayerLevelGrows_AndCutsOffAtGray()
    {
        var cfg = MonsterXpConfig.Default;
        const int target = 30;

        var firstGray = 0;
        for (var p = target; p <= MonsterDifficultyCore.MaxPlayerLevel; p++)
        {
            if (MonsterDifficultyCore.GrayLevel(p) < target) continue;
            firstGray = p;
            break;
        }

        Assert.True(firstGray > target, "找不到灰名边界（夹具前提不成立）");

        long first = -1, prev = long.MaxValue;
        var points = 0;
        for (var p = target; p < firstGray; p++)
        {
            var v = NpcKillCore.Evaluate(NpcKillSource.HostileNpc, cfg, target, p);
            Assert.NotEqual(NpcKillXpReject.GrayConcession, v.Reason);
            Assert.True(v.Xp > 0, $"玩家 {p} 级打 {target} 级敌对 NPC 经验为 0（{v.Describe()}）");
            Assert.True(v.Xp <= prev, $"玩家 {p} 级经验反而涨了：{prev} → {v.Xp}");
            if (first < 0) first = v.Xp;
            prev = v.Xp;
            points++;
        }

        // 防"夹具退化成两个点"：至少要有三个可比较的等级，且末点确实低于首点。
        Assert.True(points >= 3, "可比较的等级太少：" + points);
        Assert.True(prev < first, $"从 {target} 级到灰名边界前经验没有下降：{first} → {prev}");

        // 灰名边界：同一条判据断供，理由必须说得出来（不是"没配"）。
        var gray = NpcKillCore.Evaluate(NpcKillSource.HostileNpc, cfg, target, firstGray);
        Assert.Equal(0, gray.Xp);
        Assert.Equal(NpcKillXpReject.GrayConcession, gray.Reason);
    }

    [Fact]
    public void PvpKill_UsesTheSameDecayCurve()
    {
        var cfg = MonsterXpConfig.Default;
        const int target = 30;

        // PvP 与敌对 NPC 走同一条数值链：同等级观察者在两个来源下必须同数，
        // 只允许"来源给不给经验"这一层不同（这里两者都给）。
        var pvp = NpcKillCore.Evaluate(NpcKillSource.PvpPlayer, cfg, target, target);
        var npc = NpcKillCore.Evaluate(NpcKillSource.HostileNpc, cfg, target, target);
        Assert.Equal(npc.Xp, pvp.Xp);
        Assert.True(pvp.Xp > 0);

        // 高等级观察者打同一个 30 级目标：经验更低（不是更高、也不是同样多）。
        var grayLevel = MonsterDifficultyCore.GrayLevel(target + 5);
        if (grayLevel < target)
        {
            var high = NpcKillCore.Evaluate(NpcKillSource.PvpPlayer, cfg, target, target + 5);
            Assert.True(high.Xp < pvp.Xp, $"PvP 高等级观察者经验没有下降：{pvp.Xp} → {high.Xp}");
        }
    }
}

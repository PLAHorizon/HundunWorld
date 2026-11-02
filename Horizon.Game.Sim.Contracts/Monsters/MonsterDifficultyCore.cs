using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 怪物相对玩家的"让分难度"（concession）—— 魔兽世界逆向得到的六档。
/// <para>
/// <b>为什么全工程只许有这一个枚举</b>：在 WoW 里"名字是什么颜色"与"杀它有没有经验"是
/// 同一个判据的两个投影（灰名 = 零经验）。分成两处写就会出现名字已经灰了却还给经验，
/// 或者反之；这类不一致在日志里没有任何痕迹，只能靠"唯一事实源"消除。
/// </para>
/// </summary>
public enum MonsterDifficulty : byte
{
    /// <summary>灰：等级差过大，击杀**零经验**。</summary>
    Gray = 0,

    /// <summary>绿：明显低于玩家，经验按线性衰减。</summary>
    Green = 1,

    /// <summary>黄：同级（±2），标准经验。</summary>
    Yellow = 2,

    /// <summary>橙：高 3~4 级。</summary>
    Orange = 3,

    /// <summary>红：高 5~9 级。</summary>
    Red = 4,

    /// <summary>骷髅：高 10 级以上，或首领级怪物（不显示具体等级）。</summary>
    Skull = 5,
}

/// <summary>
/// 怪物难度档（rank）。决定血条样式、名牌是否显示等级、经验倍率与刷新节奏。
/// <para>与 <see cref="MonsterDifficulty"/> 的区别：rank 是怪**自身**的绝对档位，
/// <see cref="MonsterDifficulty"/> 是怪相对**某个玩家**的让分结果。</para>
/// </summary>
public enum MonsterRank : byte
{
    /// <summary>普通。</summary>
    Normal = 0,

    /// <summary>精英（黄名 + 龙框，WoW 观测为经验 ×2）。</summary>
    Elite = 1,

    /// <summary>稀有精英（紫名，独立附加掉落表）。</summary>
    RareElite = 2,

    /// <summary>副本首领。</summary>
    Boss = 3,

    /// <summary>世界 BOSS（全服通告，尸体长驻）。</summary>
    WorldBoss = 4,
}

/// <summary>
/// 怪物难度/让分的确定性纯函数（逆向规格 §2.1、§2.2）。
/// <para>
/// <b>落点纪律</b>：本类是让分判据的唯一事实源，Orleans 权威端、DS 权威端、编辑器预览与客户端名牌
/// <b>四方都必须消费同一份</b>。因此它物理上放在 <c>Horizon.Game.Sim.Contracts</c>（客户端清单里唯一
/// 装的仿真程序集），命名空间仍是 <c>Horizon.Game.Sim.Monsters</c>，服务端照旧经 <c>Horizon.Game.Sim</c>
/// 的传递引用解析到同一 DLL。任何一方"自己再算一遍"都会造成名牌颜色与经验数字不一致。
/// </para>
/// <para>把本文件挪回 <c>Horizon.Game.Sim</c> 会当场打断客户端名牌编译——那个程序集不在
/// <c>Game.Build.cs</c> 的 <c>dlls[]</c> 清单里（权威算法不下发给客户端）。</para>
/// <para>
/// <b>本工程口径（决策 D7 已拍板）</b>：WoW 的 ZD/GML 分段只定义到 60 级，而本作上限 300 级、
/// 分七境界。故衰减分段**按境界重设**而不是照抄 60 封顶外推——让"打低境界的怪不再有意义"
/// 这件事发生在境界边界上，而不是某个凭空的等级数字上。
/// </para>
/// <para>
/// <b>境界表与 <c>ProgressionCurve.StageStartLevels</c> 是两份清单</b>：声明了却没人核对的清单一定会漂。
/// 因此 <see cref="StageStartLevels"/> 逐字照抄，并由 <c>MonsterDifficultyCoreTests</c> 断言两处序列相等。
/// </para>
/// </summary>
public static class MonsterDifficultyCore
{
    /// <summary>难度内核版本（进金轨迹对拍锚点）。</summary>
    public const int Version = 1;

    /// <summary>玩家等级上限（与 <c>ProgressionCurve.MaxLevel</c> 同值，由对账测试钉住）。</summary>
    public const int MaxPlayerLevel = 300;

    ///
    /// 境界起始等级表；索引 = 境界号（0 与 1 同为 1 级起点，沿用 <c>ProgressionCurve</c> 的既有语义）。
    ///
    public static readonly int[] StageStartLevels = { 1, 1, 31, 76, 131, 201, 300 };

    /// <summary>
    /// 灰名等级差（GML = CL − GrayGapByStage[境界]）。
    /// <para>索引 = 境界号。WoW 的灰名窗口从 5 级起随等级缓慢增大（6-39 段是 5→8），
    /// 这里按境界给到 5→12，保证 300 级玩家眼里"新手村整片变灰"而不是"还能勉强蹭经验"。</para>
    /// </summary>
    public static readonly int[] GrayGapByStage = { 5, 5, 6, 7, 9, 10, 12 };

    /// <summary>
    /// 零差值系数 ZD（低等级怪经验线性衰减的分母），索引 = 境界号。
    /// <para>必须 <b>≥</b> <see cref="GrayGapByStage"/> 同境界值：否则线性公式在到达灰名边界**之前**
    /// 就已经归零，"绿色档"会在还有余量时凭空显示成 0 经验（WoW 里是反过来的——灰名硬切，
    /// 于是边界处有一个可见的落差，这个落差是设计而非 bug：见 <see cref="Of"/> 的灰名优先判定）。</para>
    /// </summary>
    public static readonly int[] ZeroDifferenceByStage = { 5, 6, 8, 11, 15, 20, 24 };

    /// <summary>
    /// 越级奖励的差值封顶与系数（逆向规格 §2.2：打高 10 级也只按高 4 级算，上限 1.2 倍）。
    /// 这两个值即 WoW 的 <c>min(ML−CL, 4)</c> 与 <c>0.05</c>，保留为常量而非表项，
    /// 因为它们是"形状"而不是"平衡旋钮"（平衡旋钮是 ZD/GML）。
    /// </summary>
    public const int HighLevelBonusCapLevels = 4;
    public const double HighLevelBonusRatePerLevel = 0.05;

    /// <summary>取等级所处的境界号（0..6）；找不到返回 0（非法等级不抛异常，与 <c>MonsterTemplateCatalog.Get</c> 同纪律）。</summary>
    public static int StageIndexOf(int level)
    {
        if (level < 1) return 0;
        var stage = 0;
        for (var i = 0; i < StageStartLevels.Length; i++)
        {
            if (level >= StageStartLevels[i]) stage = i;
        }
        return stage;
    }

    /// <summary>灰名等级：击杀 ≤ 该等级的怪**零经验**。非法/超界等级钳到 [0, <paramref name="playerLevel"/>]。</summary>
    public static int GrayLevel(int playerLevel)
    {
        if (playerLevel <= 5) return 0;
        if (playerLevel > MaxPlayerLevel) playerLevel = MaxPlayerLevel;
        var gap = GrayGapByStage[StageIndexOf(playerLevel)];
        var gray = playerLevel - gap;
        return gray < 0 ? 0 : gray;
    }

    /// <summary>零差值系数 ZD：等级差达到 ZD 时线性公式归零（灰名边界在此之前，故实际由 <see cref="GrayLevel"/> 断供）。</summary>
    public static int ZeroDifferenceValue(int playerLevel)
    {
        if (playerLevel < 1) playerLevel = 1;
        if (playerLevel > MaxPlayerLevel) playerLevel = MaxPlayerLevel;
        return ZeroDifferenceByStage[StageIndexOf(playerLevel)];
    }

    /// <summary>
    /// 让分难度判据（<b>唯一</b>的名牌颜色/经验断供来源）。
    /// </summary>
    /// <param name="monsterLevel">怪物等级；未知（首领级怪常显示 ??）传 <see cref="UnknownLevel"/>。</param>
    /// <param name="playerLevel">玩家等级。</param>
    /// <param name="rank">怪物 rank；≥ <see cref="MonsterRank.Boss"/> 一律骷髅（WoW 观测：BOSS 不显示数字等级）。</param>
    public const int UnknownLevel = int.MinValue;

    public static MonsterDifficulty Of(int monsterLevel, int playerLevel, MonsterRank rank = MonsterRank.Normal)
    {
        if (rank >= MonsterRank.Boss) return MonsterDifficulty.Skull;
        if (monsterLevel == UnknownLevel) return MonsterDifficulty.Skull;
        if (playerLevel < 1) playerLevel = 1;

        var diff = monsterLevel - playerLevel;
        if (diff >= 10) return MonsterDifficulty.Skull;
        if (diff >= 5) return MonsterDifficulty.Red;
        if (diff >= 3) return MonsterDifficulty.Orange;
        if (diff >= -2) return MonsterDifficulty.Yellow;
        // 绿色档的下界由灰名边界决定，而不是固定 −5：这正是"换号看同一只怪颜色会变"的来源。
        if (monsterLevel <= GrayLevel(playerLevel)) return MonsterDifficulty.Gray;
        return MonsterDifficulty.Green;
    }

    /// <summary>
    /// 低等级怪的经验衰减系数 <c>max(0, 1 − (CL − ML) / ZD)</c>（逆向规格 §2.2）。
    /// <para>返回 0..1；灰名档请由调用方按 <see cref="Of"/> 断供，本函数只负责曲线形状。</para>
    /// </summary>
    public static double LowGapDecay(int monsterLevel, int playerLevel)
    {
        if (playerLevel < 1) playerLevel = 1;
        var gap = playerLevel - monsterLevel;
        if (gap <= 0) return 1.0;
        var zd = ZeroDifferenceValue(playerLevel);
        if (zd <= 0) return 0.0;
        var factor = 1.0 - (double)gap / zd;
        if (factor < 0.0) factor = 0.0;
        if (factor > 1.0) factor = 1.0;
        return factor;
    }

    /// <summary>
    /// 高等级怪的经验奖励系数 <c>1 + 0.05 × min(ML − CL, 4)</c>，上限 1.2（打高 10 级与打高 4 级同酬）。
    /// </summary>
    public static double HighGapBonus(int monsterLevel, int playerLevel)
    {
        var gap = monsterLevel - playerLevel;
        if (gap <= 0) return 1.0;
        if (gap > HighLevelBonusCapLevels) gap = HighLevelBonusCapLevels;
        return 1.0 + HighLevelBonusRatePerLevel * gap;
    }

    /// <summary>
    /// 让分曲线的完整形状（编辑器的经验曲线视图调这个，不要自己拼公式）。
    /// <para>横轴是"怪等级 − 我等级"，纵轴是相对于基准经验的倍率；灰名段返回 0，形成 WoW 那个可见硬落差。</para>
    /// </summary>
    public static double ConcessionFactor(int monsterLevel, int playerLevel, MonsterRank rank = MonsterRank.Normal)
    {
        if (Of(monsterLevel, playerLevel, rank) == MonsterDifficulty.Gray) return 0.0;
        if (monsterLevel > playerLevel) return HighGapBonus(monsterLevel, playerLevel);
        return LowGapDecay(monsterLevel, playerLevel);
    }
}

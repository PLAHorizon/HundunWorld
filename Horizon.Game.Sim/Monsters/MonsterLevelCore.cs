using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 怪物**生成时等级**的唯一主人（内容表 <c>level_min</c> / <c>level_max</c> 那两格的消费者）。
/// <para>
/// <b>为什么要有这个类</b>：那两格从库里读出来、被快照抄进运行期配置，然后**没有任何人掷它**
/// （消费者普查实测：运行期命中 0，只有 CLI 与插件在读写）。于是策划配"6~8 级"，
/// 游戏里每一只都是基准等级——而血量、攻击力、击杀经验三处都跟着等级走，等于那一格白配。
/// </para>
/// <para>
/// <b>逆向依据（AzerothCore）</b>：<c>Creature::SelectLevel()</c>
/// （<c>src/server/game/Entities/Creature/Creature.cpp:1478-1480</c>）：
/// <code>
/// uint8 minlevel = std::min(cInfo->maxlevel, cInfo->minlevel);
/// uint8 maxlevel = std::max(cInfo->maxlevel, cInfo->minlevel);
/// uint8 level = minlevel == maxlevel ? minlevel : urand(minlevel, maxlevel);
/// </code>
/// 三个要点照搬：① min/max 先归一化（写反了不报错，按区间取用）；
/// ② <c>min == max</c> 时**不掷**（退化区间就恒定，这正是今天全部种子内容的形状）；
/// ③ <c>urand</c> 是**闭区间**（两端都可能出）。AC 在每次 <c>Respawn()</c> 里重跑这段，
/// 所以同一只怪每次刷新等级可以不同——本作照做（掷骰键里带生成那一拍的 tick）。</para>
/// <para>
/// 随机源不另起一份：走 <see cref="CombatCore.Roll01"/> 这个本作唯一的确定性掷骰口
/// （键 = 实体 id + 生成 tick），所以"同种子同 tick ⇒ 同等级"与掉落/暴击同一套纪律；
/// 而实体 id 由 <c>(刷怪点 &lt;&lt; 32) | (槽位+1)</c> 确定性给出，槽位之间也互不影响。</para>
/// </summary>
public static class MonsterLevelCore
{
    /// <summary>掷等级的盐值：与掉落/战斗那些盐值分开，避免同一对键在不同用途上给出同一个数。</summary>
    public const uint LevelSalt = 0x4C7E35;

    /// <summary>
    /// 这一只怪生成时的等级：闭区间 <c>[levelMin, levelMax]</c> 内均匀掷。
    /// <para><paramref name="fallbackBase"/> 是模板基准：区间没配（两端都 ≤0）时用它，
    /// 基准也没有时收成 1——等级绝不能是 0，那会把让分曲线与灰名判据推到未定义的区域。</para>
    /// <para><c>min == max</c> 时一次掷骰都不做（AC 那句 <c>minlevel == maxlevel ? minlevel</c>），
    /// 这就是今天全部种子内容的形状 ⇒ 接进这一格不改变任何现有表现。</para>
    /// </summary>
    public static int Roll(int levelMin, int levelMax, int fallbackBase, ulong entityKey, long serverTick)
    {
        // AC 的 std::min/std::max：写反的区间按区间取用，不报错也不静默失效。
        var lo = Math.Min(levelMin, levelMax);
        var hi = Math.Max(levelMin, levelMax);

        if (lo <= 0)
        {
            // 只有一端配了（或都没配）：退回基准，别把 0 当等级。
            lo = hi <= 0 ? fallbackBase : hi;
            hi = lo;
        }

        if (hi <= lo) return ClampLevel(lo > 0 ? lo : fallbackBase);

        var draw = CombatCore.Roll01(entityKey, (ulong)(lo * 397 + hi * 31), serverTick, LevelSalt);
        var span = hi - lo + 1;                       // 闭区间：AC 的 urand(min, max) 含两端
        var offset = (int)(draw * span);              // draw ∈ [0,1)
        if (offset >= span) offset = span - 1;        // 1.0 的边界兜底（Roll01 承诺 <1，这里不靠承诺）
        return ClampLevel(lo + offset);
    }

    /// <summary>等级的下限收口：0 级会把让分曲线与灰名断供推进未定义区，一律收成 1。</summary>
    public static int ClampLevel(int level) => level > 0 ? level : 1;
}

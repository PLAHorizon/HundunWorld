using System;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 角色成长曲线：经验 → 等级。纯函数，方便把数值钉在回归测试里。
    /// <para>
    /// 为什么要有这么一个类：`CharacterEntity.experience` 列一直在，但创建时被置 0 后**全仓再无任何写入方**，
    /// 也没有升等逻辑；而 §14 的黄/玄/地/天阶门槛读的是 `learn_level_require`。等级不可变，
    /// 等于整条阶梯（除白阶）在游戏里根本不可达——门槛写得再对也只是装饰。
    /// </para>
    /// <para>
    /// 曲线本身是**可调的默认值**，不是最终平衡：这里给的是一条线性可预期的口径
    /// （1→2 需 100，之后每级 +25），真正要定节奏时需要拿目标升级速度反推。
    /// 之所以写成纯函数并把上限钉住：升等一旦能被一次超大经验一口气推过上限，
    /// "等级门槛"就会被一条奖励直接跳过，而那正是它要挡的东西。
    /// </para>
    /// </summary>
    public static class ProgressionCurve
    {
        /// <summary>
        /// 等级上限 = 300，对齐方案 §2.1 的阶梯顶（道阶 "Lv300 + 前置"，混沌终式同 300）。
        /// 上限取在这里而不是取"某个更大数"：阶梯最高门槛就是 300，再往上的等级没有任何一张表在读它，
        /// 只会让经验条变成一个无人解释的大数。
        /// </summary>
        public const int MaxLevel = 300;

        /// <summary>升到下一级所需经验：100 + 25×(当前等级-1)。</summary>
        public static long ExpToNext(int level)
        {
            if (level < 1) level = 1;
            return 100L + 25L * (level - 1);
        }

        /// <summary>
        /// 把一笔经验结算成"新等级 + 余量"。可一次跨多级（每级都扣掉当级需求）。
        /// 到 <see cref="MaxLevel"/> 后经验停止累积：满级还涨经验只会让背包里出现一个没人读的大数。
        /// </summary>
    public static (int Level, long Experience, int LevelsGained) ApplyExp(int level, long experience, long amount)
        {
            if (amount <= 0) return (Math.Max(1, level), Math.Max(0L, experience), 0);

            int current = Math.Clamp(level < 1 ? 1 : level, 1, MaxLevel);
            long exp = Math.Max(0L, experience) + amount;
            int gained = 0;

            while (current < MaxLevel && exp >= ExpToNext(current))
            {
                exp -= ExpToNext(current);
                current++;
                gained++;
            }

            if (current >= MaxLevel) exp = 0L;   // 满级：余量清零，不攒一个永远花不掉的数

            return (current, exp, gained);
        }

        /// <summary>
        /// 各阶的起等级（方案 §2.1：白 Lv1 / 黄 Lv31 / 玄 Lv76 / 地 Lv131 / 天 Lv201 / 道 Lv300），
        /// 下标即阶序 1..6。与技能表的 <c>learn_realm_require</c> 用同一套编号。
        /// </summary>
        public static readonly int[] StageStartLevels = { 1, 1, 31, 76, 131, 201, 300 };

        /// <summary>由等级推导境界（阶序 1..6）。等级是唯一来源，所以两条轴不可能互相矛盾。</summary>
        public static int RealmForLevel(int level)
        {
            int realm = 1;
            for (int i = 1; i < StageStartLevels.Length; i++)
            {
                if (level >= StageStartLevels[i]) realm = i;
            }
            return realm;
        }

        /// <summary>某境界（阶序）对应的起等级，用于把"还差多少"讲成人话。</summary>
        public static int StartLevelForRealm(int realm)
            => realm >= 1 && realm < StageStartLevels.Length ? StageStartLevels[realm] : 1;

        /// <summary>
        /// 境界门槛判定。表里 <c>learn_realm_require</c> 有值却没人判过——数据在、代码不判，
        /// 表现就是"这一阶的门槛好像生效了又没有"，比压根没这列更难查。
        /// <para>
        /// 之所以按等级派生而不是读角色身上的 RealmLevel：那条列同样没有任何写入方，
        /// 拿一个没人维护的数去卡玩家，只会把可达的内容判成不可达。
        /// </para>
        /// </summary>
        public static string? RealmBlocker(int level, int realmRequire)
        {
            if (realmRequire <= 0) return null;
            int realm = RealmForLevel(level);
            if (realm >= realmRequire) return null;
            return $"境界不足：需 {realmRequire} 阶（Lv{StartLevelForRealm(realmRequire)} 起），当前 {realm} 阶";
        }
    }
}

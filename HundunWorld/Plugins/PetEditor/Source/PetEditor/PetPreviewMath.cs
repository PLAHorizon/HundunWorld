using System;

namespace PetEditor
{
    /// <summary>
    /// 编辑器的即时预览数学（<b>内核的第二份实现</b>，不是内壳调用）。
    /// <para>
    /// <b>为什么要写第二份</b>：Flax 脚本域引不到 <c>Horizon.Game.Sim</c>，但编辑器的滑杆必须
    /// 拖动就出数——不能每动一格就去起一个 CLI 子进程。所以这里逐字照抄内核的表与公式，
    /// 由 <c>PetEditorContractTests</c> 把两侧的表做文本对账，并由窗口上的"试算"按钮
    /// 拿 CLI 的结果交叉核对一次：<b>两处不一致就在控制台报红</b>。
    /// </para>
    /// <para>
    /// <b>这份副本是负债，不是资产</b>：任何只改一侧的改动都会让编辑器显示的数与服务端算出的数不同，
    /// 而玩家看到的永远是编辑器那份。改表之前先想清楚能不能改成"只在 CLI 侧算"。
    /// </para>
    /// </summary>
    public static class PetPreviewMath
    {
        /// <summary>与内核 <c>PetStatsCore.Version</c> 对齐的副本版本号（对账用）。</summary>
        public const int Version = 1;

        // ---- 号段（内核 PetIdentityCore）----

        public const int MinSpeciesId = 500_000;
        public const int MaxSpeciesId = 799_999;
        public const long InstanceSequenceBase = 100_000L;
        public const int MaxInstancesPerSpecies = 99_999;

        /// <summary>物种号是否落在宠物号段。</summary>
        public static bool IsSpeciesId(int speciesId) => speciesId >= MinSpeciesId && speciesId <= MaxSpeciesId;

        /// <summary>合成个体编号；非法输入返回 0（与内核同纪律：哨兵而不是抛异常）。</summary>
        public static long NewInstanceId(int speciesId, int sequence)
        {
            if (!IsSpeciesId(speciesId)) return 0L;
            if (sequence < 1 || sequence > MaxInstancesPerSpecies) return 0L;
            return (long)speciesId * InstanceSequenceBase + sequence;
        }

        /// <summary>号段 → 稀有度档（0未知 1普通 2变异 3神兽 4活动）。</summary>
        public static int RarityTierOf(int speciesId)
        {
            if (speciesId < 500_000 || speciesId > 799_999) return 0;
            if (speciesId < 600_000) return 1;
            if (speciesId < 650_000) return 2;
            if (speciesId < 700_000) return 3;
            return 4;
        }

        // ---- 资质与属性（内核 PetStatsCore）----

        public const int GrowthScale = 10_000;
        public const int GrowthMinBps = 7_950;
        public const int GrowthMaxBps = 15_000;
        public const int MinQuality = 1;
        public const int MaxQuality = 6_000;
        public const int PotentialPointsPerLevel = 5;
        public const int MaxPetLevel = 175;

        /// <summary>悟性加成表（bps）。与 <c>PetStatsCore.ComprehensionBonusBps</c> 逐字相同。</summary>
        public static readonly int[] ComprehensionBonusBps =
            { 100, 150, 210, 300, 800, 1100, 1450, 2350, 3000, 3930 };

        /// <summary>灵性加成表——普通灵（bps）。</summary>
        public static readonly int[] SpiritBonusNormalBps =
            { 100, 433, 767, 1100, 1433, 1767, 2100, 2433, 2767, 3100 };

        /// <summary>灵性加成表——超灵（bps）。</summary>
        public static readonly int[] SpiritBonusSuperBps =
            { 100, 478, 856, 1233, 1611, 1989, 2367, 2744, 3122, 3400 };

        /// <summary>成长档位 → bps（与内核同一张映射）。</summary>
        public static int GrowthBpsOf(int tier)
        {
            switch (tier)
            {
                case 0: return 10_000;
                case 1: return 10_500;
                case 2: return 11_000;
                case 3: return 11_500;
                case 4: return 12_000;
                default: return 10_000;
            }
        }

        public static int ClampGrowth(int growthBps)
        {
            if (growthBps < GrowthMinBps) return GrowthMinBps;
            if (growthBps > GrowthMaxBps) return GrowthMaxBps;
            return growthBps;
        }

        /// <summary>
        /// 裸资 → 有效资质（天龙公式：×(1+悟性)×(1+灵性)×变异放大）。
        /// <para>与内核逐条同序：<c>q → ×(10000+comp)/10000 → ×(10000+spi)/10000 → ×mut/10000 → 钳上限</c>。
        /// 顺序换了结果就不同（整数截断不可交换），所以这里连"先乘哪个"都照抄。</para>
        /// </summary>
        public static int EffectiveQuality(int baseQuality, int comprehensionLevel, int spiritLevel,
            bool superSpirit, int mutationBonusBps)
        {
            var q = baseQuality < MinQuality ? MinQuality : baseQuality;
            var comp = comprehensionLevel <= 0 ? 0 : BonusAt(ComprehensionBonusBps, comprehensionLevel);
            var spi = spiritLevel <= 0 ? 0 : BonusAt(superSpirit ? SpiritBonusSuperBps : SpiritBonusNormalBps, spiritLevel);
            if (mutationBonusBps < GrowthScale) mutationBonusBps = GrowthScale;

            long value = q;
            value = value * (10_000L + comp) / 10_000L;
            value = value * (10_000L + spi) / 10_000L;
            value = value * mutationBonusBps / 10_000L;
            if (value > MaxQuality) return MaxQuality;
            return (int)value;
        }

        /// <summary>战斗属性六元组（与内核 <c>PetCombatStats</c> 同字段同顺序）。</summary>
        public struct CombatStats
        {
            public int Hp;
            public int Mp;
            public int Attack;
            public int Defense;
            public int Speed;
            public int SpiritPower;
        }

        /// <summary>资质 + 等级 + 成长 + 五维分配点 → 战斗属性（梦幻后验公式族，逐条照抄内核）。</summary>
        public static CombatStats ComputeStats(int attackQuality, int defenseQuality, int staminaQuality,
            int manaQuality, int speedQuality, int level, int growthBps,
            int strength, int endurance, int constitution, int spirit, int agility,
            int speedFlat = 0, int spiritPowerFlat = 0)
        {
            if (level < 1) level = 1;
            if (level > MaxPetLevel) level = MaxPetLevel;
            var g = ClampGrowth(growthBps);

            var atkQ = AtLeast(attackQuality);
            var defQ = AtLeast(defenseQuality);
            var staQ = AtLeast(staminaQuality);
            var manaQ = AtLeast(manaQuality);
            var spdQ = AtLeast(speedQuality);

            var hp = level * staQ / 1000 + Mul(constitution, g, 6, 1);
            var mp = level * manaQ / 500 + Mul(spirit, g, 3, 1);
            var attack = (int)(level * (long)atkQ * (140_000L + 10L * g) / (7550L * 10_000L))
                         + Mul(strength, g, 1, 1);
            var defense = level * defQ / 433 + Mul(endurance, g, 4, 3);
            var speedTotal = agility + speedFlat;
            var speed = spdQ * speedTotal / 1000;
            var mixed = constitution * 3000L + spirit * 7000L + strength * 4000L + endurance * 2000L;
            var spiritPower = (int)(mixed / 10_000L) + spiritPowerFlat;

            return new CombatStats
            {
                Hp = ClampNonNegative(hp), Mp = ClampNonNegative(mp), Attack = ClampNonNegative(attack),
                Defense = ClampNonNegative(defense), Speed = ClampNonNegative(speed),
                SpiritPower = ClampNonNegative(spiritPower),
            };
        }

        // ---- 默契四轴（内核 PetBondCore）----

        public const int LoyaltyMax = 100;
        public const int HappinessMax = 1000;
        public const int LoyaltyRefuseThreshold = 70;
        public const int HappinessBlockThreshold = 600;
        public const int LifespanBlockThreshold = 51;
        public const int HappinessGreenAt = 667;
        public const int HappinessYellowAt = 334;

        /// <summary>快乐三档的伤害倍率（bps）：0红 1黄 2绿。</summary>
        public static readonly int[] DamageMultiplierBps = { 7_500, 10_000, 12_500 };

        /// <summary>心情档位（0红 1黄 2绿）。</summary>
        public static int MoodOf(int happiness)
        {
            if (happiness >= HappinessGreenAt) return 2;
            if (happiness >= HappinessYellowAt) return 1;
            return 0;
        }

        public static int DamageMultiplierFor(int happiness)
        {
            var mood = MoodOf(happiness);
            return DamageMultiplierBps[mood < 0 || mood >= DamageMultiplierBps.Length ? 1 : mood];
        }

        /// <summary>低忠诚的拒绝出战概率（bps）：70 → 0，0 → 10000。</summary>
        public static int RefusalChanceBps(int loyalty)
        {
            if (loyalty >= LoyaltyRefuseThreshold) return 0;
            if (loyalty <= 0) return 10_000;
            return (LoyaltyRefuseThreshold - loyalty) * 10_000 / LoyaltyRefuseThreshold;
        }

        /// <summary>出战门槛原因码（0可出 1忠诚 2快乐 3寿命 4超主级 5未孵化 6不可用）。</summary>
        public static int CheckDeployable(int loyalty, int happiness, int lifespan, int petLevel, int ownerLevel,
            int ownerLevelSlack, bool immortalLifespan, bool hatched, bool available)
        {
            // 判据顺序与内核一致：状态 → 快乐 → 寿命 → 等级 → 忠诚（软门槛不在这里，见 RefusalChanceBps）
            if (!hatched) return 5;
            if (!available) return 6;
            if (happiness < HappinessBlockThreshold) return 2;
            if (!immortalLifespan && lifespan < LifespanBlockThreshold) return 3;
            if (ownerLevel > 0 && petLevel > ownerLevel + ownerLevelSlack) return 4;
            return 0;
        }

        // ---- 成长/孵化/技能（内核 PetGrowthCore / PetSkillCore）----

        public static readonly int[] MarriageLevels = { 30, 50, 70, 90 };

        public static int MarriageLevelFor(int breedingCount)
        {
            if (breedingCount < 0) breedingCount = 0;
            if (breedingCount < MarriageLevels.Length) return MarriageLevels[breedingCount];
            return MarriageLevels[MarriageLevels.Length - 1] + (breedingCount - MarriageLevels.Length + 1) * 20;
        }

        public static int XpToNextLevel(int nextLevel)
        {
            if (nextLevel < 1) return 0;
            if (nextLevel > MaxPetLevel) return int.MaxValue;
            var l = (long)nextLevel * nextLevel;
            return (int)(15L + l * 3L / 2L);
        }

        /// <summary>内丹格数（梦幻按参战等级 1–6）。</summary>
        public static int InnerCoreSlotsFor(int battleLevel)
        {
            if (battleLevel <= 0) return 1;
            if (battleLevel < 35) return 1;
            if (battleLevel < 75) return 2;
            if (battleLevel < 105) return 3;
            if (battleLevel < 145) return 4;
            if (battleLevel < 175) return 5;
            return 6;
        }

        /// <summary>当前开放技能格数（品级表与悟性双轨；与内核 <c>PetSkillCore.MaxSlotsFor</c> 同序同钳位）。</summary>
        public static int MaxSlotsFor(int baseSlots, int absoluteMaxSlots, int[] slotsByGrade,
            int comprehensionUnlockA, int comprehensionUnlockB, int grade, int comprehensionLevel)
        {
            var slots = baseSlots < 1 ? 1 : baseSlots;
            if (slotsByGrade != null && slotsByGrade.Length > 0)
            {
                var idx = grade < 0 ? 0 : (grade >= slotsByGrade.Length ? slotsByGrade.Length - 1 : grade);
                slots = slotsByGrade[idx];
            }
            if (comprehensionUnlockA > 0 && comprehensionLevel >= comprehensionUnlockA) slots++;
            if (comprehensionUnlockB > 0 && comprehensionLevel >= comprehensionUnlockB) slots++;
            if (slots < 1) slots = 1;
            if (slots > absoluteMaxSlots) slots = absoluteMaxSlots;
            return slots;
        }

        /// <summary>等级 → 累计潜能点。</summary>
        public static int PotentialTotal(int level)
            => (level < 0 ? 0 : level) * PotentialPointsPerLevel;

        // ---- 助手（刻意与内核各写一份，不复用，以便对账时两侧独立）----

        private static int AtLeast(int quality) => quality < MinQuality ? MinQuality : quality;

        private static int BonusAt(int[] table, int level)
        {
            if (table == null || table.Length == 0) return 0;
            if (level <= 1) return table[0];
            if (level > table.Length) return table[table.Length - 1];
            return table[level - 1];
        }

        private static int Mul(int value, int growthBps, int num, int den)
        {
            if (den <= 0) den = 1;
            var v = value < 0 ? 0 : value;
            return (int)(v * (long)growthBps * num / ((long)GrowthScale * den));
        }

        private static int ClampNonNegative(int value) => value < 0 ? 0 : value;
    }
}

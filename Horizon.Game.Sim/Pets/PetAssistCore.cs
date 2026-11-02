using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 宠物可给主人的辅助类型。
/// <para>天龙 §D11 把这条链做得最完整（辅主特技 / 护主 / 附体三通路），大话只有一条
/// "伤害 15% 转化主人生命/法力"（§C11），梦幻与 WoW 基本无反哺（§B11/§A11）。
/// 所以这个枚举是**四家并集**，且每一种都必须能被模板关掉——反哺是差异最大的维度，
/// 把它写成"永远生效"就等于替所有物种选了天龙档。</para>
/// </summary>
public enum PetAssistKind : byte
{
    /// <summary>借力 → 主人力量（外功攻击）。天龙 §D11 [高]。</summary>
    OwnerStrength = 0,

    /// <summary>凝神 → 主人灵气（内功攻击）。</summary>
    OwnerSpirit = 1,

    /// <summary>强身 → 主人体力（生命上限 + 外防）。</summary>
    OwnerVitality = 2,

    /// <summary>瞬影 → 主人身法（命中/闪避/会心）。</summary>
    OwnerAgility = 3,

    /// <summary>移魂 → 主人定力（内防）。</summary>
    OwnerSteadiness = 4,

    /// <summary>共生 → 按宠物生命比例提升主人生命上限。</summary>
    OwnerHpFromPetHp = 5,

    /// <summary>血迹 → 按宠物体力资质/等级回复主人内力。</summary>
    OwnerManaRegen = 6,

    /// <summary>忠心 → 概率替主人承受会心伤害（不是属性，是行为修饰，但仍归"辅助"面板）。</summary>
    OwnerCritTankChance = 7,

    /// <summary>吸气 → 减少目标气（对敌 debuff，走主人出手）。</summary>
    EnemyManaDrain = 8,

    /// <summary>伤害转化 → 把宠物物理伤害的 15% 转成主人生命/法力（大话 §C11）。</summary>
    OwnerLeechFromDamage = 9,
}

/// <summary>辅助加成的来源，用于把最终数字归因回具体那一项。</summary>
public readonly struct PetBonusSource
{
    /// <summary>加成类型。</summary>
    public readonly PetAssistKind Kind;

    /// <summary>贡献的技能 id；0 = 来自附体位而不是技能。</summary>
    public readonly int SkillId;

    /// <summary>贡献的宠物实例编号（<see cref="PetIdentityCore.NewInstanceId"/>）。</summary>
    public readonly long FromInstanceId;

    /// <summary>加成的属性名（显示用；<b>不参与判等</b>）。</summary>
    public readonly string AttributeLabel;

    /// <summary>该来源贡献的数值（整数属性点或 bps，语义由 <see cref="IsBasisPoints"/> 决定）。</summary>
    public readonly int Value;

    /// <summary>true = <see cref="Value"/> 是 bps（万分比）；false = 是点数。</summary>
    public readonly bool IsBasisPoints;

    public PetBonusSource(PetAssistKind kind, int skillId, long fromInstanceId, string attributeLabel,
        int value, bool isBasisPoints)
    {
        Kind = kind;
        SkillId = skillId;
        FromInstanceId = fromInstanceId;
        AttributeLabel = attributeLabel;
        Value = value;
        IsBasisPoints = isBasisPoints;
    }
}

/// <summary>宠物对主人的属性加成汇总——角色属性面板消费的产物。</summary>
public struct PetOwnerBonus
{
    /// <summary>力量（外功）。</summary>
    public int Strength;

    /// <summary>灵气（内功）。</summary>
    public int Spirit;

    /// <summary>体力。</summary>
    public int Vitality;

    /// <summary>身法。</summary>
    public int Agility;

    /// <summary>定力。</summary>
    public int Steadiness;

    /// <summary>生命上限加成（点）。</summary>
    public int HpFlat;

    /// <summary>内力回复加成（点/秒）。</summary>
    public int ManaRegen;

    /// <summary>替主人承受会心的概率（bps）。</summary>
    public int CritTankChanceBps;

    /// <summary>宠物物理伤害转化主人生命的比例（bps）。大话口径 1500 = 15%。</summary>
    public int DamageLeechBps;

    /// <summary>逐来源清单；<b>合计必须等于上面的各列</b>，由测试断言，
    /// 否则面板显示"宠物给了 300 力量"但没人说得清是哪只哪技能给的。</summary>
    public PetBonusSource[] Sources;
}

/// <summary>
/// 辅助（对角色的属性加成）的确定性纯函数（逆向规格 §A11/§B11/§C11/§D11）。
/// <para>
/// <b>三条通路分开算是刻意的</b>（决策 D5）：
/// ① 辅主特技按<b>技能等级</b>给（天龙借力/凝神/强身/瞬影/移魂）；
/// ② 附体位按<b>等级与资质</b>给，且资料明确"技能等级<b>不影响</b>属性/战力加成"（§D11）；
/// ③ 护主类按<b>资质/等级</b>折算成回复或概率。
/// 合成一个公式的话，②的那条"技能等级不影响"就必然被写错——而它是唯一有反向断言价值的细节。
/// </para>
/// </summary>
public static class PetAssistCore
{
    /// <summary>辅助内核版本。</summary>
    public const int Version = 1;

    /// <summary>辅主特技每级给主人的属性点（天龙只说"技能加在主人身上"未给逐级数字；5 点/级是可调初值）。</summary>
    public const int AssistSkillPointsPerLevel = 5;

    /// <summary>高级辅主技（"高级借力"）的倍率（bps）；资料把高级列为独立技能，此处取 2 倍。</summary>
    public const int AdvancedAssistSkillMultiplierBps = 20_000;

    /// <summary>附体位每级的加成（bps × 资质比）。天龙只给"按等级/资质给"，未给系数。</summary>
    public const int PossessionLevelCoefficient = 4;

    /// <summary>共生的生命转化基准（bps）：宠物生命 × 该比例进主人生命上限，体力越高转化越多（§D11）。</summary>
    public const int SymbiosisBaseBps = 500;

    /// <summary>共生随体力资质增长的额外 bps（资质每 100 点 +50bps，上限 2000bps）。</summary>
    public const int SymbiosisPer100StaminaBps = 50;
    public const int SymbiosisMaxBps = 2_500;

    /// <summary>血迹：按体力资质/等级回复主人内力（§D11）。每 1000 体力资质每级给 1 点。</summary>
    public const int BloodlustStaminaQualDivisor = 1000;

    /// <summary>忠心替挡会心的每级概率（bps）。天龙原文为"一定概率"，未给数字。</summary>
    public const int LoyaltyCritTankBpsPerLevel = 300;

    /// <summary>伤害转化率（大话 §C11：物理伤害的 15% 转化为主人生命/法力）。</summary>
    public const int DamageLeechBps = 1_500;

    /// <summary>辅主特技的技能 id → 属性映射（配置表，编辑器可覆盖）。</summary>
    public static PetAssistKind KindOf(int assistSkillId) => assistSkillId switch
    {
        800901 => PetAssistKind.OwnerStrength,       // 借力
        800902 => PetAssistKind.OwnerSpirit,         // 凝神
        800903 => PetAssistKind.OwnerVitality,       // 强身
        800904 => PetAssistKind.OwnerAgility,        // 瞬影
        800905 => PetAssistKind.OwnerSteadiness,     // 移魂
        800911 => PetAssistKind.OwnerHpFromPetHp,    // 共生
        800912 => PetAssistKind.OwnerManaRegen,      // 血迹
        800913 => PetAssistKind.OwnerCritTankChance, // 忠心
        800914 => PetAssistKind.EnemyManaDrain,      // 吸气
        800915 => PetAssistKind.OwnerLeechFromDamage,// 伤害转化（大话）
        _ => PetAssistKind.OwnerStrength,
    };

    /// <summary>辅主特技的属性点数：等级 × 每级点数（高级技再 ×2）。</summary>
    public static int AssistSkillPoints(int skillLevel, bool advanced)
    {
        var lv = skillLevel < 1 ? 1 : (skillLevel > PetSkillCore.MaxSkillLevel ? PetSkillCore.MaxSkillLevel : skillLevel);
        var points = lv * AssistSkillPointsPerLevel;
        return advanced ? (int)(points * (long)AdvancedAssistSkillMultiplierBps / 10_000L) : points;
    }

    /// <summary>
    /// 附体位给主人的单属性点数。
    /// <para><b>参数里没有 skillLevel，这是设计而不是遗漏</b>：天龙 §D11 明文
    /// "附体珍兽的技能等级不影响属性/战力加成"。若将来有人要加进去，必须改这条注释并留证据。</para>
    /// </summary>
    /// <param name="petLevel">附体宠等级。</param>
    /// <param name="quality">对应维度有效资质（<c>PetStatsCore.EffectiveQuality</c>）。</param>
    public static int PossessionPoints(int petLevel, int quality)
    {
        var lv = petLevel < 0 ? 0 : petLevel;
        var q = quality < PetStatsCore.MinQuality ? PetStatsCore.MinQuality : quality;
        // 资质以 1000 为基准刻度：资质 1000 时贡献 ×1，2000 时 ×2。
        return (int)((long)lv * PossessionLevelCoefficient * q / 1000L);
    }

    /// <summary>共生的生命转化比例（bps）：基准 + 体力资质缩放，钳到 <see cref="SymbiosisMaxBps"/>。</summary>
    public static int SymbiosisBps(int staminaQuality)
    {
        var q = staminaQuality < 0 ? 0 : staminaQuality;
        var bonus = q / 100 * SymbiosisPer100StaminaBps;
        var total = SymbiosisBaseBps + bonus;
        return total > SymbiosisMaxBps ? SymbiosisMaxBps : total;
    }

    /// <summary>血迹给主人的内力回复（点/秒）：体力资质 / 1000 × 等级（§D11"按体力资质/等级"）。</summary>
    public static int BloodlustManaRegen(int petLevel, int staminaQuality)
    {
        if (petLevel <= 0 || staminaQuality <= 0) return 0;
        return petLevel * staminaQuality / BloodlustStaminaQualDivisor;
    }

    /// <summary>忠心替挡会心概率（bps），10 级封顶 3000bps（30%）。</summary>
    public static int CritTankChanceBps(int skillLevel)
    {
        var lv = skillLevel < 1 ? 1 : (skillLevel > PetSkillCore.MaxSkillLevel ? PetSkillCore.MaxSkillLevel : skillLevel);
        return lv * LoyaltyCritTankBpsPerLevel;
    }

    /// <summary>
    /// 汇总一只宠对主人的全部加成。
    /// </summary>
    /// <param name="petLevel">宠物等级。</param>
    /// <param name="qualities">有效资质（已含悟性/灵性/变异加成）。</param>
    /// <param name="petHp">宠物生命上限（共生要用）。</param>
    /// <param name="assistSkillIds">该宠携带的辅助技能 id（可空 = 纯附体档，只走通路 ②）。</param>
    /// <param name="assistSkillLevels">与 <paramref name="assistSkillIds"/> 同序的技能等级。</param>
    /// <param name="advancedAssistMask">与 <paramref name="assistSkillIds"/> 同序；true = 高级版技能。</param>
    /// <param name="instanceId">宠物实例编号（进来源清单，供界面归因）。</param>
    /// <param name="inBattle">false 时辅主特技不生效——天龙 §D11 的措辞是"出战后"才加。</param>
    /// <param name="possessed">true = 走附体位（不占出战位，§D11）。</param>
    /// <remarks>
    /// 返回的 <see cref="PetOwnerBonus.Sources"/> 与五个属性列是<b>同一份数据的两种投影</b>，
    /// 二者的对账由 <c>PetEditorKernelTests</c> 用独立暴力求和验证；不做对账的话，
    /// "面板数字"和"逐条来源"迟早分叉（记忆 <c>derived-copy-needs-byte-equality-guard</c>）。
    /// </remarks>
    public static PetOwnerBonus ComputeOwnerBonus(int petLevel, in PetQualities qualities, int petHp,
        int[] assistSkillIds, int[] assistSkillLevels, bool[] advancedAssistMask, long instanceId,
        bool inBattle = true, bool possessed = false)
    {
        var bonus = new PetOwnerBonus();
        var sources = new System.Collections.Generic.List<PetBonusSource>(8);

        // 通路 ② 附体位：只看等级与资质，五维各自取对应资质。
        if (possessed)
        {
            Add(ref bonus.Strength, PossessionPoints(petLevel, qualities.Attack), sources,
                PetAssistKind.OwnerStrength, 0, instanceId, "力量（附体）", false);
            Add(ref bonus.Spirit, PossessionPoints(petLevel, qualities.Mana), sources,
                PetAssistKind.OwnerSpirit, 0, instanceId, "灵气（附体）", false);
            Add(ref bonus.Vitality, PossessionPoints(petLevel, qualities.Stamina), sources,
                PetAssistKind.OwnerVitality, 0, instanceId, "体力（附体）", false);
            Add(ref bonus.Agility, PossessionPoints(petLevel, qualities.Speed), sources,
                PetAssistKind.OwnerAgility, 0, instanceId, "身法（附体）", false);
            Add(ref bonus.Steadiness, PossessionPoints(petLevel, qualities.Defense), sources,
                PetAssistKind.OwnerSteadiness, 0, instanceId, "定力（附体）", false);
        }

        // 通路 ① / ③ 技能：出战才生效。
        if (!inBattle || assistSkillIds is null || assistSkillIds.Length == 0)
        {
            bonus.Sources = sources.ToArray();
            return bonus;
        }

        for (var i = 0; i < assistSkillIds.Length; i++)
        {
            var skillId = assistSkillIds[i];
            if (skillId <= 0) continue;
            var level = assistSkillLevels is not null && assistSkillLevels.Length > i ? assistSkillLevels[i] : 1;
            var advanced = advancedAssistMask is not null && advancedAssistMask.Length > i && advancedAssistMask[i];
            var kind = KindOf(skillId);

            switch (kind)
            {
                case PetAssistKind.OwnerStrength:
                    Add(ref bonus.Strength, AssistSkillPoints(level, advanced), sources, kind, skillId, instanceId, "力量", false);
                    break;
                case PetAssistKind.OwnerSpirit:
                    Add(ref bonus.Spirit, AssistSkillPoints(level, advanced), sources, kind, skillId, instanceId, "灵气", false);
                    break;
                case PetAssistKind.OwnerVitality:
                    Add(ref bonus.Vitality, AssistSkillPoints(level, advanced), sources, kind, skillId, instanceId, "体力", false);
                    break;
                case PetAssistKind.OwnerAgility:
                    Add(ref bonus.Agility, AssistSkillPoints(level, advanced), sources, kind, skillId, instanceId, "身法", false);
                    break;
                case PetAssistKind.OwnerSteadiness:
                    Add(ref bonus.Steadiness, AssistSkillPoints(level, advanced), sources, kind, skillId, instanceId, "定力", false);
                    break;
                case PetAssistKind.OwnerHpFromPetHp:
                    {
                        var bps = SymbiosisBps(qualities.Stamina);
                        var hp = (int)((long)petHp * bps / 10_000L);
                        Add(ref bonus.HpFlat, hp, sources, kind, skillId, instanceId, "生命上限", false);
                        break;
                    }
                case PetAssistKind.OwnerManaRegen:
                    Add(ref bonus.ManaRegen, BloodlustManaRegen(petLevel, qualities.Stamina), sources, kind, skillId, instanceId, "内力回复", false);
                    break;
                case PetAssistKind.OwnerCritTankChance:
                    AddBps(ref bonus.CritTankChanceBps, CritTankChanceBps(level), sources, kind, skillId, instanceId, "替挡会心");
                    break;
                case PetAssistKind.OwnerLeechFromDamage:
                    AddBps(ref bonus.DamageLeechBps, DamageLeechBps, sources, kind, skillId, instanceId, "伤害转化");
                    break;
                case PetAssistKind.EnemyManaDrain:
                    // 吸气是对敌效果，不进主人属性；仍登记来源，避免界面"少了个技能"被当成 bug。
                    sources.Add(new PetBonusSource(kind, skillId, instanceId, "敌方内力削减", AssistSkillPoints(level, advanced), false));
                    break;
            }
        }

        bonus.Sources = sources.ToArray();
        return bonus;
    }

    private static void Add(ref int target, int value, System.Collections.Generic.List<PetBonusSource> sources,
        PetAssistKind kind, int skillId, long instanceId, string label, bool isBps)
    {
        if (value <= 0) return;
        target += value;
        sources.Add(new PetBonusSource(kind, skillId, instanceId, label, value, isBps));
    }

    /// <summary>主人自己的基础战斗属性（宠物加成要叠在它上面）。</summary>
    public struct OwnerBaseStats
    {
        /// <summary>基础攻击。</summary>
        public int Attack;

        /// <summary>基础生命上限。</summary>
        public int Hp;

        /// <summary>基础防御。</summary>
        public int Defense;

        /// <summary>基础命中（身法折算）。</summary>
        public int Hit;

        /// <summary>基础内力回复。</summary>
        public int ManaRegen;

        /// <summary>会心被替挡的基准概率（bps）；宠物的忠心与它取最大而不是相加。</summary>
        public int BaseCritTakenBps;
    }

    /// <summary>叠加宠物加成后的主人属性，附带"每一项加了多少"的归因。</summary>
    public readonly struct OwnerFinalStats
    {
        public readonly int Attack;
        public readonly int Hp;
        public readonly int Defense;
        public readonly int Hit;
        public readonly int ManaRegen;

        /// <summary>会心被替挡的最终概率（bps）。</summary>
        public readonly int CritTakenBps;

        /// <summary>宠物贡献的攻击总量（面板要单列显示"其中宠物给了多少"）。</summary>
        public readonly int FromPetsAttack;
        public readonly int FromPetsHp;
        public readonly int FromPetsDefense;
        public readonly int FromPetsHit;

        public OwnerFinalStats(int attack, int hp, int defense, int hit, int manaRegen,
            int critTakenBps, int fromPetsAttack, int fromPetsHp, int fromPetsDefense, int fromPetsHit)
        {
            Attack = attack;
            Hp = hp;
            Defense = defense;
            Hit = hit;
            ManaRegen = manaRegen;
            CritTakenBps = critTakenBps;
            FromPetsAttack = fromPetsAttack;
            FromPetsHp = fromPetsHp;
            FromPetsDefense = fromPetsDefense;
            FromPetsHit = fromPetsHit;
        }
    }

    /// <summary>每点主人自身属性对应的战斗量换算（本工程口径，四款游戏都不公开这一层）。</summary>
    public const int AttackPerStrength = 2;
    public const int AttackPerSpirit = 2;
    public const int DefensePerSteadiness = 2;
    public const int DefensePerVitality = 1;
    public const int HpPerVitality = 8;
    public const int HitPerAgility = 3;

    /// <summary>
    /// 把宠物反哺加成叠到主人自己的属性上。
    /// <para>
    /// <b>为什么要单独一个函数而不是让 DS 直接改面板数字</b>：属性面板必须能回答
    /// "这 500 攻击里有多少来自宠物"——一旦 DS 把加成混进基础值，玩家与策划都无法核对，
    /// 而"宠物给的算不算进战力"是四款游戏口径不一致的地方（WoW 完全不算、天龙全算）。
    /// 所以这里<b>同时返回合算值与宠物贡献量</b>，两侧各自消费。
    /// </para>
    /// <para>
    /// <b>替挡概率取最大</b>（与 <see cref="AggregateOwnerBonuses"/> 同一口径）：
    /// 一次会心要么被挡要么没被挡，多个来源相加会超过 100%。
    /// </para>
    /// </summary>
    public static OwnerFinalStats ApplyToOwnerStats(in PetOwnerBonus bonus, in OwnerBaseStats baseStats)
    {
        // 先乘后加全部走 long 再钳位：直接 int 相乘会在 SafeAdd 之前就回绕成负数，
        // 那时钳位看到的是一个"负加成"，于是宠物给了几百万攻击反而把主人属性打回基础值——
        // 这是本函数唯一的静默失效模式，测试 属性相加不许溢出成负数 就是守它。
        var fromAttack = ClampToInt((long)bonus.Strength * AttackPerStrength + (long)bonus.Spirit * AttackPerSpirit);
        var fromDefense = ClampToInt((long)bonus.Steadiness * DefensePerSteadiness + (long)bonus.Vitality * DefensePerVitality);
        var fromHp = ClampToInt((long)bonus.Vitality * HpPerVitality + bonus.HpFlat);
        var fromHit = ClampToInt((long)bonus.Agility * HitPerAgility);

        var crit = baseStats.BaseCritTakenBps;
        if (bonus.CritTankChanceBps > crit) crit = bonus.CritTankChanceBps;
        if (crit > 10_000) crit = 10_000;
        if (crit < 0) crit = 0;

        return new OwnerFinalStats(
            SafeAdd(baseStats.Attack, fromAttack),
            SafeAdd(baseStats.Hp, fromHp),
            SafeAdd(baseStats.Defense, fromDefense),
            SafeAdd(baseStats.Hit, fromHit),
            SafeAdd(baseStats.ManaRegen, bonus.ManaRegen),
            crit,
            fromAttack, fromHp, fromDefense, fromHit);
    }

    /// <summary>long → int 的饱和钳位（负数一律归 0：属性加成没有"负贡献"这一说）。</summary>
    private static int ClampToInt(long value)
    {
        if (value <= 0) return 0;
        if (value > int.MaxValue) return int.MaxValue;
        return (int)value;
    }

    /// <summary>加法防溢出：属性叠成负数（回绕）比"顶到上限"更难被发现，所以宁可钳位。</summary>
    private static int SafeAdd(int baseValue, int add)
    {
        if (baseValue < 0) baseValue = 0;
        if (add <= 0) return baseValue;
        if (baseValue > int.MaxValue - add) return int.MaxValue;
        return baseValue + add;
    }

    /// <summary>一个参与反哺的宠物（出战位或附体位）。</summary>
    public readonly struct ActivePet
    {
        /// <summary>宠物实例编号（进来源清单，界面据此说"是哪只给的"）。</summary>
        public readonly long InstanceId;

        /// <summary>宠物等级。</summary>
        public readonly int Level;

        /// <summary>有效资质（已含悟性/灵性/变异）。</summary>
        public readonly PetQualities Qualities;

        /// <summary>宠物生命上限（共生折算用）。</summary>
        public readonly int PetHp;

        /// <summary>携带的辅助技能 id。</summary>
        public readonly int[] AssistSkillIds;

        /// <summary>与 <see cref="AssistSkillIds"/> 同序的技能等级。</summary>
        public readonly int[] AssistSkillLevels;

        /// <summary>与 <see cref="AssistSkillIds"/> 同序；true = 高级版技能。</summary>
        public readonly bool[] AdvancedMask;

        /// <summary>true = 走附体位（不占出战位，且<b>不受技能等级影响</b>）。</summary>
        public readonly bool Possessed;

        /// <summary>出战位上的宠物才算 inBattle；附体位的宠物主人被打时不触发护主。</summary>
        public readonly bool InBattle;

        public ActivePet(long instanceId, int level, in PetQualities qualities, int petHp,
            int[] assistSkillIds, int[] assistSkillLevels, bool[] advancedMask, bool possessed, bool inBattle)
        {
            InstanceId = instanceId;
            Level = level;
            Qualities = qualities;
            PetHp = petHp;
            AssistSkillIds = assistSkillIds;
            AssistSkillLevels = assistSkillLevels;
            AdvancedMask = advancedMask;
            Possessed = possessed;
            InBattle = inBattle;
        }
    }

    /// <summary>
    /// 把多只宠物（1 只出战 + 至多 <see cref="PetAiCore.MaxPossessedSlots"/> 只附体）的加成合成一份主人面板数据。
    /// <para>
    /// <b>属性列相加，概率列取最大</b>——这是本函数唯一值得解释的决定：
    /// 力量/灵气这类是"面板数值"，多来源叠加是四款游戏一致的口径；
    /// 而"替挡会心概率""伤害转化率"是**一次事件只能生效一次**的判定，
    /// 若也相加，3 只忠心 30% 会得出 90%、4 只就超 100%，玩家看到的是"基本必挡"，
    /// 而天龙/大话的实际表现是"有会挡的宠时按最强的那只算"。
    /// </para>
    /// <para>
    /// <b>来源清单不按列合并</b>：合成结果里每个贡献者都留一条，界面才能回答
    /// "这 300 力量是谁给的"；只回一个合计的面板迟早被质疑成黑箱。
    /// </para>
    /// </summary>
    /// <param name="pets">参与反哺的宠物；顺序无关。</param>
    /// <param name="excludedSources">被挡下的条目数与原因（超出附体位上限、实例号非法等）。</param>
    public static PetOwnerBonus AggregateOwnerBonuses(ActivePet[] pets, out int excluded, out string exclusionReason)
    {
        var total = new PetOwnerBonus();
        var merged = new System.Collections.Generic.List<PetBonusSource>(16);
        excluded = 0;
        exclusionReason = string.Empty;

        if (pets is null || pets.Length == 0)
        {
            total.Sources = merged.ToArray();
            return total;
        }

        var battlingUsed = false;
        var possessedUsed = 0;
        var notes = new System.Collections.Generic.List<string>();

        for (var i = 0; i < pets.Length; i++)
        {
            var pet = pets[i];

            if (pet.InstanceId == PetIdentityCore.UnassignedInstanceId)
            {
                excluded++;
                notes.Add("有宠物缺少个体编号，已跳过（编号未分配的个体不参与反哺）");
                continue;
            }

            if (pet.Possessed)
            {
                if (possessedUsed >= PetAiCore.MaxPossessedSlots)
                {
                    excluded++;
                    notes.Add($"附体位只有 {PetAiCore.MaxPossessedSlots} 个，超出的宠物未参与加成");
                    continue;
                }
                possessedUsed++;
            }
            else
            {
                if (battlingUsed)
                {
                    excluded++;
                    notes.Add($"出战位只能有 {PetAiCore.MaxBattlingPets} 只，多出的宠物未参与加成");
                    continue;
                }
                battlingUsed = true;
            }

            var one = ComputeOwnerBonus(pet.Level, pet.Qualities, pet.PetHp, pet.AssistSkillIds,
                pet.AssistSkillLevels, pet.AdvancedMask, pet.InstanceId, pet.InBattle, pet.Possessed);

            // 属性列相加
            total.Strength += one.Strength;
            total.Spirit += one.Spirit;
            total.Vitality += one.Vitality;
            total.Agility += one.Agility;
            total.Steadiness += one.Steadiness;
            total.HpFlat += one.HpFlat;
            total.ManaRegen += one.ManaRegen;

            // 概率列取最大：见方法注释
            if (one.CritTankChanceBps > total.CritTankChanceBps) total.CritTankChanceBps = one.CritTankChanceBps;
            if (one.DamageLeechBps > total.DamageLeechBps) total.DamageLeechBps = one.DamageLeechBps;

            if (one.Sources is { Length: > 0 }) merged.AddRange(one.Sources);
        }

        total.Sources = merged.ToArray();
        exclusionReason = string.Join("；", notes);
        return total;
    }

    private static void AddBps(ref int target, int value, System.Collections.Generic.List<PetBonusSource> sources,
        PetAssistKind kind, int skillId, long instanceId, string label)
    {
        if (value <= 0) return;
        target += value;
        if (target > 10_000) target = 10_000; // 概率类只许钳到 100%
        sources.Add(new PetBonusSource(kind, skillId, instanceId, label, value, true));
    }
}

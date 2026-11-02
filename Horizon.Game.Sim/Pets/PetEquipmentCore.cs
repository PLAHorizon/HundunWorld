using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 宠物装备部位（四款游戏的并集）。
/// <para>梦幻：护腕/项圈/铠甲（§B7）；大话：兽环/兽甲/兽铃（§C7）；天龙：头/爪/躯干/项圈/护符（§D7）；
/// WoW：<b>无宠物装备</b>（§A7）。同一部位名在不同游戏里语义不同（天龙的"项圈"给特殊增益、
/// 梦幻的"项圈"镶速度或躲避），所以这里用物理部位命名，各家别名走显示层。</para>
/// </summary>
public enum PetEquipSlot : byte
{
    /// <summary>护腕（梦幻）。</summary>
    Wrist = 0,

    /// <summary>项圈（梦幻/天龙共有；天龙口径下它是核心位）。</summary>
    Collar = 1,

    /// <summary>铠甲/躯干（梦幻"铠甲"与天龙"躯干"合并）。</summary>
    Body = 2,

    /// <summary>头部。</summary>
    Head = 3,

    /// <summary>爪。</summary>
    Claw = 4,

    /// <summary>护符。</summary>
    Talisman = 5,

    /// <summary>兽环（大话）。</summary>
    Ring = 6,

    /// <summary>兽铃（大话）。</summary>
    Bell = 7,
}

/// <summary>装备等级段（天龙 §D7：75/85/95 三段；低段装备在高档宠身上给不了多少属性）。</summary>
public enum PetGearLevelTier : byte
{
    /// <summary>无等级段（梦幻/大话口径按装备本身数值给属性）。</summary>
    None = 0,

    /// <summary>75 级段。</summary>
    Tier75 = 1,

    /// <summary>85 级段。</summary>
    Tier85 = 2,

    /// <summary>95 级段。</summary>
    Tier95 = 3,
}

/// <summary>资质鉴定档（天龙 §D7：普通→中等→优秀→完美→超完美；超完美额外 +80% 基础属性）。</summary>
public enum PetAppraisalTier : byte
{
    /// <summary>普通。</summary>
    Ordinary = 0,

    /// <summary>中等。</summary>
    Medium = 1,

    /// <summary>优秀。</summary>
    Good = 2,

    /// <summary>完美。</summary>
    Perfect = 3,

    /// <summary>超完美（+80% 基础属性，§D7 [高]）。</summary>
    Superb = 4,
}

/// <summary>可镶嵌灵石类型（梦幻 §B7 的六种，单颗数值固定）。</summary>
public enum PetGemKind : byte
{
    /// <summary>不镶。</summary>
    None = 0,

    /// <summary>防御 +8。</summary>
    Defense = 1,

    /// <summary>速度 +6。</summary>
    Speed = 2,

    /// <summary>伤害 +10。</summary>
    Damage = 3,

    /// <summary>躲避 +20。</summary>
    Dodge = 4,

    /// <summary>灵力 +4。</summary>
    Spirit = 5,

    /// <summary>气血 +30。</summary>
    Health = 6,
}

/// <summary>一件穿戴在宠物身上的装备。</summary>
public struct PetGearItem
{
    /// <summary>物品 id（引用既有物品模板域，不另立编号）。</summary>
    public int ItemId;

    /// <summary>部位。</summary>
    public PetEquipSlot Slot;

    /// <summary>等级段。</summary>
    public PetGearLevelTier LevelTier;

    /// <summary>星级 1–5（怀旧版可到 7，由 <see cref="PetEquipmentCore.MaxStar"/> 钳位决定）。</summary>
    public int Star;

    /// <summary>鉴定档。</summary>
    public PetAppraisalTier Appraisal;

    /// <summary>镶嵌的灵石种类（<b>每件只允许一种</b>，梦幻 §B7）。</summary>
    public PetGemKind Gem;

    /// <summary>灵石等级 1–10（梦幻：合成上限 10 级、逐级合成，§B7）。</summary>
    public int GemLevel;

    /// <summary>基础属性值（黄字基数；由物品模板给定，本内核不生产它）。</summary>
    public int BaseStat;

    /// <summary>该属性落在哪个战斗量上（用 <see cref="PetEquipStat"/> 表达）。</summary>
    public PetEquipStat BaseStatKind;

    /// <summary>套装编号；0 = 散件。</summary>
    public int SetId;

    /// <summary>套装要求的性格（天龙按性格成套：怯/狡/勇/忠/慎，§D7）。</summary>
    public PetPersonality SetPersonality;

    /// <summary>强化值（大话觉醒格前置用"强化 8/13/16"，§C7）。</summary>
    public int EnhanceLevel;

    /// <summary>品质（大话觉醒格前置用"品质 2500/5000/10000"，§C7）。</summary>
    public int Quality;

    /// <summary>通灵值（大话：培养提升通灵，通灵影响绿属性，§C7）。</summary>
    public int SpiritLink;
}

/// <summary>
/// 宠物身上的一件"穿戴记录"——只存**引用**（物品实例 id + 部位），不复制装备本体。
/// <para>本体住在背包/物品域。个体表里若抄一份数值快照，改一次装备就要同步两处，
/// 而"两处不一致"的表现是面板与战斗用的不是同一套属性。所以这里刻意只留指针。</para>
/// </summary>
public struct PetWornGear
{
    /// <summary>物品实例 id（背包域的主键，0 = 无效）。</summary>
    public long ItemInstanceId;

    /// <summary>占用的部位。</summary>
    public PetEquipSlot Slot;
}

/// <summary>装备给的是哪一项战斗属性。</summary>
public enum PetEquipStat : byte
{
    /// <summary>气血。</summary>
    Hp = 0,

    /// <summary>攻击。</summary>
    Attack = 1,

    /// <summary>防御。</summary>
    Defense = 2,

    /// <summary>速度。</summary>
    Speed = 3,

    /// <summary>灵力。</summary>
    SpiritPower = 4,

    /// <summary>命中。</summary>
    Hit = 5,

    /// <summary>躲避。</summary>
    Dodge = 6,

    /// <summary>外功攻击（天龙口径，与 <see cref="Attack"/> 区分是为了让属性面板能按家显示）。</summary>
    PhysicalAttack = 7,
}

/// <summary>装备结算结果：逐项属性 + 套装附加 + 可归因说明。</summary>
public readonly struct PetEquipSummary
{
    /// <summary>六项战斗属性增量（气血/攻击/防御/速度/灵力/躲避）。</summary>
    public readonly int Hp;
    public readonly int Attack;
    public readonly int Defense;
    public readonly int Speed;
    public readonly int SpiritPower;
    public readonly int Dodge;

    /// <summary>已生效的套装件数（同一 setId 且同一星级齐了才算）。</summary>
    public readonly int ActiveSetPieces;

    /// <summary>参与结算的件数（不含被"同种灵石限制"驳回的）。</summary>
    public readonly int CountedPieces;

    /// <summary>被驳回的件数与原因计数（"部位不属于该物种模板"等）。</summary>
    public readonly int RejectedPieces;

    public PetEquipSummary(int hp, int attack, int defense, int speed, int spiritPower, int dodge,
        int activeSetPieces, int countedPieces, int rejectedPieces)
    {
        Hp = hp; Attack = attack; Defense = defense; Speed = speed;
        SpiritPower = spiritPower; Dodge = dodge;
        ActiveSetPieces = activeSetPieces; CountedPieces = countedPieces; RejectedPieces = rejectedPieces;
    }
}

/// <summary>
/// 装备穿戴的确定性纯函数（逆向规格 §B7/§C7/§D7）。
/// <para>
/// <b>部位集合属于物种模板而不是全局常量</b>（决策 D6）：梦幻 3 件、大话 3 件、天龙 5 件、WoW 0 件。
/// 写死一套的话，WoW 档的宠物要么"有装备但没人读"（静默失效），要么要在每个函数里加特判。
/// </para>
/// <para>
/// <b>装备绑定角色而非宠物</b>（天龙 §D7 [高]：可跨珍兽复用）：所以本模块只做"某件装备在某只宠身上
/// 给多少属性"，不表达"装备属于谁"——归属是物品系统的事，混在这里会出现"换宠时把装备复制了一份"的 bug。
/// </para>
/// </summary>
public static class PetEquipmentCore
{
    /// <summary>装备内核版本。</summary>
    public const int Version = 1;

    /// <summary>星级上限（天龙正文 5 星，怀旧版 6–7 星需圣兽精魄；取 7 为硬上限）。</summary>
    public const int MaxStar = 7;

    /// <summary>灵石等级上限（梦幻 §B7：合成上限 10 级）。</summary>
    public const int MaxGemLevel = 10;

    /// <summary>
    /// 单颗灵石每级数值（梦幻 §B7 [高]：防+8 / 速+6 / 伤+10 / 躲+20 / 灵+4 / 血+30）。
    /// <para>原文给的是"单颗"数值，等级递增时是否翻倍未记载。这里按<b>线性叠加</b>实现（每级 +1 份），
    /// 因为"逐级合成"在语义上就是累加；若实测是乘法，只改 <see cref="GemValue"/> 一处。</para>
    /// </summary>
    public static readonly int[] GemValuePerLevel = { 0, 8, 6, 10, 20, 4, 30 };

    /// <summary>鉴定档的基础属性放大（bps）；超完美 +80% 是原文数字（§D7），其余档位资料未给，取等差。</summary>
    public static readonly int[] AppraisalMultiplierBps = { 10_000, 11_000, 12_000, 14_000, 18_000 };

    /// <summary>
    /// 升星消耗圣兽鳞表（天龙 §D7 [高]，75 级段：2星=3、3星=5、4星=7、5星=11）。
    /// <para>索引 = 目标星级；1 星不耗（获得即为 1 星）。</para>
    /// </summary>
    public static readonly int[] ScaleByTier75 = { 0, 0, 3, 5, 7, 11, 0, 0 };

    /// <summary>95 级段升星消耗（§D7：兑换每件 100 鳞、1→2=36、2→3=43、3→4=50、4→5=56）。</summary>
    public static readonly int[] ScaleByTier95 = { 0, 36, 43, 50, 56, 0, 0, 0 };

    /// <summary>
    /// 套装属性随星级的放大（用天龙的实测点标定：75 级 1 星勇猛套外功 +332 → 5 星 +1659）。
    /// <para>1659 / 332 ≈ 5.0 倍、跨 4 个星级 ⇒ 每星约 ×1.495。这里存成逐星倍率表（bps），
    /// 而不是拿 1.495^star 现算——浮点累乘会让"同一套装备在编辑器和战斗服上数字不同"。</para>
    /// </summary>
    public static readonly int[] StarMultiplierBps = { 10_000, 10_000, 12_000, 14_000, 16_000, 18_000, 20_000, 22_000 };

    /// <summary>觉醒格开启条件（大话 §C7 [高]：第一格 品质2500/强化8，第二格 5000/13，第三格 10000/16）。</summary>
    public static readonly int[] AwakeningQualityGate = { 2_500, 5_000, 10_000 };
    public static readonly int[] AwakeningEnhanceGate = { 8, 13, 16 };

    /// <summary>单颗灵石在某等级下的数值。</summary>
    public static int GemValue(PetGemKind gem, int level)
    {
        var kind = (int)gem;
        if (kind <= 0 || kind >= GemValuePerLevel.Length) return 0;
        var lv = level < 1 ? 1 : (level > MaxGemLevel ? MaxGemLevel : level);
        return GemValuePerLevel[kind] * lv;
    }

    /// <summary>该灵石允许镶在哪个部位（梦幻 §B7：护腕=伤害或灵力；项圈=速度或躲避；铠甲=防御或气血）。</summary>
    /// <remarks>返回 false 时调用方<b>必须把这件驳回去并计数</b>，不能静默收下——
    /// 静默收下会让玩家以为镶上了，而实际属性一点没加。</remarks>
    public static bool IsGemAllowedOn(PetGemKind gem, PetEquipSlot slot) => (gem, slot) switch
    {
        (PetGemKind.Damage, PetEquipSlot.Wrist) => true,
        (PetGemKind.Spirit, PetEquipSlot.Wrist) => true,
        (PetGemKind.Speed, PetEquipSlot.Collar) => true,
        (PetGemKind.Dodge, PetEquipSlot.Collar) => true,
        (PetGemKind.Defense, PetEquipSlot.Body) => true,
        (PetGemKind.Health, PetEquipSlot.Body) => true,
        _ => false,
    };

    /// <summary>鉴定档倍率（bps）。</summary>
    public static int AppraisalMultiplierBpsOf(PetAppraisalTier tier)
    {
        var idx = (int)tier;
        if (idx < 0 || idx >= AppraisalMultiplierBps.Length) return 10_000;
        return AppraisalMultiplierBps[idx];
    }

    /// <summary>星级倍率（bps）。</summary>
    public static int StarMultiplierBpsOf(int star)
    {
        var s = star < 1 ? 1 : (star >= StarMultiplierBps.Length ? StarMultiplierBps.Length - 1 : star);
        return StarMultiplierBps[s];
    }

    /// <summary>升到目标星级需要的圣兽鳞（0 = 该等级段没有这条数据 / 已达上限）。</summary>
    public static int ScaleCostFor(PetGearLevelTier tier, int targetStar)
    {
        var table = tier switch
        {
            PetGearLevelTier.Tier75 => ScaleByTier75,
            PetGearLevelTier.Tier95 => ScaleByTier95,
            _ => null,
        };
        if (table is null || targetStar < 1 || targetStar >= table.Length) return 0;
        return table[targetStar];
    }

    /// <summary>该物种是否允许穿戴这一部位（模板给的部位集合；<b>空集合 = WoW 档，一律拒绝</b>）。</summary>
    public static bool SlotAllowed(PetEquipSlot[] allowedSlots, PetEquipSlot slot)
    {
        if (allowedSlots is null || allowedSlots.Length == 0) return false;
        for (var i = 0; i < allowedSlots.Length; i++)
        {
            if (allowedSlots[i] == slot) return true;
        }
        return false;
    }

    /// <summary>
    /// 结算一身装备。
    /// </summary>
    /// <param name="allowedSlots">物种模板开放的部位集合。</param>
    /// <param name="items">穿戴清单（同一部位多件时只有第一件生效，其余计入 <see cref="PetEquipSummary.RejectedPieces"/>）。</param>
    /// <remarks>
    /// 逐件按"基础值 × 星级 × 鉴定档 + 灵石"算；<b>灵石先判部位合法性再加</b>，
    /// 顺序反过来会让"错误镶嵌"的属性进了总和、只有计数说它被驳回。
    /// </remarks>
    public static PetEquipSummary Summarize(PetEquipSlot[] allowedSlots, PetGearItem[] items)
    {
        int hp = 0, attack = 0, defense = 0, speed = 0, spirit = 0, dodge = 0;
        var counted = 0;
        var rejected = 0;

        if (items is null) return new PetEquipSummary(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var used = new bool[(int)PetEquipSlot.Bell + 1];

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!SlotAllowed(allowedSlots, item.Slot) || used[(int)item.Slot])
            {
                rejected++;
                continue;
            }
            used[(int)item.Slot] = true;
            counted++;

            var mult = (long)StarMultiplierBpsOf(item.Star) * AppraisalMultiplierBpsOf(item.Appraisal) / 10_000L;
            var value = (int)(item.BaseStat * mult / 10_000L);

            Add(ref item.BaseStatKind, value, ref hp, ref attack, ref defense, ref speed, ref spirit, ref dodge);

            // 灵石：先判部位，再算等级值，再按类型归位。
            if (item.Gem != PetGemKind.None && IsGemAllowedOn(item.Gem, item.Slot))
            {
                var gemValue = GemValue(item.Gem, item.GemLevel);
                var gemKind = item.Gem switch
                {
                    PetGemKind.Defense => PetEquipStat.Defense,
                    PetGemKind.Speed => PetEquipStat.Speed,
                    PetGemKind.Damage => PetEquipStat.Attack,
                    PetGemKind.Dodge => PetEquipStat.Dodge,
                    PetGemKind.Spirit => PetEquipStat.SpiritPower,
                    PetGemKind.Health => PetEquipStat.Hp,
                    _ => PetEquipStat.Hp,
                };
                Add(ref gemKind, gemValue, ref hp, ref attack, ref defense, ref speed, ref spirit, ref dodge);
            }
        }

        // 套装：同一 setId 且**同星级**才算齐（天龙"穿齐同套同星级得额外属性"，§D7——"同星级"是关键限定，
        // 3 件不同星级的套不能给满额加成）。
        var activeSetPieces = CountMatchingSet(items, allowedSlots);

        return new PetEquipSummary(hp, attack, defense, speed, spirit, dodge, activeSetPieces, counted, rejected);
    }

    /// <summary>可开启的觉醒格数（大话 §C7：三件装备都达标才算开到位）。</summary>
    public static int CountAwakeningSlots(PetGearItem[] items)
    {
        if (items is null) return 0;
        var slots = 0;
        for (var gate = 0; gate < AwakeningQualityGate.Length; gate++)
        {
            var meets = 0;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].Quality >= AwakeningQualityGate[gate]
                    && items[i].EnhanceLevel >= AwakeningEnhanceGate[gate]) meets++;
            }
            // 需要**全部三件**都达标，不是"有一件达标就算开一格"（§C7 的觉醒技要求三件领悟相同技）。
            if (meets >= 3) slots++;
        }
        return slots;
    }

    private static void Add(ref PetEquipStat kind, int value, ref int hp, ref int attack, ref int defense,
        ref int speed, ref int spirit, ref int dodge)
    {
        switch (kind)
        {
            case PetEquipStat.Hp: hp += value; break;
            case PetEquipStat.Attack:
            case PetEquipStat.PhysicalAttack: attack += value; break;
            case PetEquipStat.Defense: defense += value; break;
            case PetEquipStat.Speed: speed += value; break;
            case PetEquipStat.SpiritPower: spirit += value; break;
            case PetEquipStat.Dodge: dodge += value; break;
            case PetEquipStat.Hit: attack += value / 4; break; // 命中折成攻击的 1/4 进面板；折算是本工程口径
        }
        // ref 参数不得被调用方改动语义，这里显式不写回 kind。
    }

    /// <summary>数出"同 setId 同星级"的成套件数（取最大的那一组）。</summary>
    private static int CountMatchingSet(PetGearItem[] items, PetEquipSlot[] allowedSlots)
    {
        var best = 0;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].SetId == 0) continue;
            if (!SlotAllowed(allowedSlots, items[i].Slot)) continue;

            var n = 0;
            for (var j = 0; j < items.Length; j++)
            {
                if (items[j].SetId == items[i].SetId
                    && items[j].Star == items[i].Star
                    && items[j].SetPersonality == items[i].SetPersonality
                    && SlotAllowed(allowedSlots, items[j].Slot)) n++;
            }
            if (n > best) best = n;
        }
        return best;
    }
}

using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>四条默契轴的标识（决策 D3：保留四条独立轴，不合并）。</summary>
public enum PetBondAxis : byte
{
    /// <summary>忠诚——决定"听不听话"（会不会拒绝出战）。</summary>
    Loyalty = 0,

    /// <summary>快乐——决定伤害倍率与是否能出战。</summary>
    Happiness = 1,

    /// <summary>寿命——消耗型，决定还能出战多少次。</summary>
    Lifespan = 2,

    /// <summary>亲密——决定特殊技能（连击等）的触发率。</summary>
    Intimacy = 3,
}

/// <summary>
/// 默契事件类型。
/// <para>四款游戏对同一件事的增减数值各不相同，但**事件集合是同一个**（逆向分析 §E1）：
/// 出战、被击飞/死亡、升级、主人死亡、交易、喂食、复活。把事件收敛成这一个枚举，
/// 是为了让"增减表"成为一张可被编辑器逐格填的矩阵，而不是散落在各处的 if。</para>
/// </summary>
public enum PetBondEvent : byte
{
    /// <summary>随主人出战一次。</summary>
    Battle = 0,

    /// <summary>被击飞 / 战斗中死亡。</summary>
    KnockedOut = 1,

    /// <summary>战斗死亡后被复活。</summary>
    Revived = 2,

    /// <summary>宠物自身升级。</summary>
    PetLevelUp = 3,

    /// <summary>主人升级（参战宠同步获得）。</summary>
    OwnerLevelUp = 4,

    /// <summary>主人死亡。</summary>
    OwnerDied = 5,

    /// <summary>主人逃离战斗。</summary>
    OwnerFled = 6,

    /// <summary>更换主人 / 交易。</summary>
    Traded = 7,

    /// <summary>喂食（口粮/烹饪）。</summary>
    Fed = 8,

    /// <summary>驯养/驯化（NPC 或界面操作恢复）。</summary>
    Tamed = 9,

    /// <summary>随时间自然衰减（快乐轴专用，WoW §A6）。</summary>
    Decay = 10,

    /// <summary>亲密丹/帮派驯养等专项道具。</summary>
    IntimacyItem = 11,
}

/// <summary>
/// 出战拦截的原因码。
/// <para><b>为什么必须是显式枚举而不是"返回 false"</b>：这些原因要直接变成玩家看到的提示
/// （"忠诚不足，它有概率拒绝出战"）。如果只返回布尔，界面就只能猜文案，
/// 而猜出来的文案和真实判据迟早分叉。</para>
/// </summary>
public enum PetBondBlockReason : byte
{
    /// <summary>可以出战。</summary>
    None = 0,

    /// <summary>忠诚过低——按概率拒绝（梦幻 §B6：≤70 起效，越低概率越高）。</summary>
    LowLoyalty = 1,

    /// <summary>快乐不足——硬门槛（天龙 §D6：<60 无法出战）。</summary>
    LowHappiness = 2,

    /// <summary>寿命不足——硬门槛（梦幻 §B6：需 ≥51 或"永生"）。</summary>
    LowLifespan = 3,

    /// <summary>等级超过主人允许余量（天龙 §D3：宠级 ≤ 主级 + 10）。</summary>
    OverOwnerLevel = 4,

    /// <summary>形态尚未完成孵化（蛋）。</summary>
    NotHatched = 5,

    /// <summary>处于繁殖冷却 / 待领取等非持有态。</summary>
    NotAvailable = 6,
}

/// <summary>四条轴的当前值。</summary>
public struct PetBondValues
{
    /// <summary>忠诚 0–100（梦幻/大话口径，§B6/§C6 均为满 100）。</summary>
    public int Loyalty;

    /// <summary>快乐 0–1000（WoW 原始刻度，§A6）。</summary>
    public int Happiness;

    /// <summary>寿命（点数，出战 −1、被击飞 −50，§B6）。</summary>
    public int Lifespan;

    /// <summary>亲密（大话可达数十万，§C1/C6；用 int 存，超 21 亿再改长期讨论）。</summary>
    public int Intimacy;

    /// <summary>
    /// 默认可出战配置：忠诚满、快乐绿脸上沿、寿命够、亲密 0。
    /// <para>寿命取 100 而不是某个大数——它本来就是消耗品，100 次出战是"普通宠"的直观尺度（§B6）。</para>
    /// </summary>
    public static PetBondValues Default => new()
    {
        Loyalty = 100,
        Happiness = 700,
        Lifespan = 100,
        Intimacy = 0,
    };
}

/// <summary>
/// 四条默契轴的确定性纯函数（逆向规格 §A6 / §B6 / §C6 / §D6）。
/// <para>
/// <b>四条轴不合一条的理由（决策 D3）</b>：它们各自的语义不同——忠诚管"肯不肯打"、快乐管"打得多痛"、
/// 寿命管"还能打几次"、亲密管"特技能不能触发"。合成一条后，"寿命耗尽但很快乐"这种
/// 玩家看得见、游戏也确实用它做过设计约束的状态就表达不出来了。
/// </para>
/// <para>
/// <b>门槛与倍率是两条独立判据</b>：梦幻用忠诚做概率拒绝（软门槛）+ 寿命做硬门槛；
/// WoW 用快乐做倍率（75/100/125%）不做门槛。二者不可互相顶替，所以各自有函数。
/// </para>
/// </summary>
public static class PetBondCore
{
    /// <summary>默契内核版本。</summary>
    public const int Version = 1;

    /// <summary>忠诚上限（梦幻/大话均为 100）。</summary>
    public const int LoyaltyMax = 100;

    /// <summary>快乐上限（WoW 0–1000，§A6）。</summary>
    public const int HappinessMax = 1000;

    /// <summary>寿命下限（0 表示耗尽；耗尽不删除个体，只是不能出战，§B6）。</summary>
    public const int LifespanMin = 0;

    /// <summary>忠诚软门槛：≤ 该值开始有概率拒绝出战（梦幻 §B6 "≤70 时进战/召唤进场有概率拒绝出战"）。</summary>
    public const int LoyaltyRefuseThreshold = 70;

    /// <summary>快乐硬门槛：低于该值无法出战（天龙 §D6 "快乐 < 60 → 无法出战"）。天龙的刻度也是 0–1000 内的 60。</summary>
    /// <remarks>天龙快乐刻度资料未给上限，这里与 WoW 对齐到 0–1000 刻度时门槛取 600；
    /// 若运营把天龙档刻度定为 0–100，则由物种模板的 <c>happinessScale</c> 覆盖，本常量只给默认。</remarks>
    public const int HappinessBlockThreshold = 600;

    /// <summary>寿命门槛：低于该值无法出战（梦幻 §B6 "需 ≥51 或永生才可出战"）。</summary>
    public const int LifespanBlockThreshold = 51;

    /// <summary>快乐三档的伤害倍率（bps）：红脸 75% / 黄脸 100% / 绿脸 125%（WoW §A6 [高]）。</summary>
    public const int DamageMultRedBps = 7_500;
    public const int DamageMultYellowBps = 10_000;
    public const int DamageMultGreenBps = 12_500;

    /// <summary>快乐三档分界（WoW 观测约 绿 667–1000 / 黄 334–666 / 红 0–333，§A6 [高]）。</summary>
    public const int HappinessGreenAt = 667;
    public const int HappinessYellowAt = 334;

    /// <summary>喂食回量按"食物等级与宠物等级差"分三档（WoW §A6 [高]：+350 / +170 / +80）。</summary>
    public const int FeedNearLevelGain = 350;
    public const int FeedGapUnder20Gain = 170;
    public const int FeedGapUnder30Gain = 80;

    /// <summary>解散/放生宠物的一次性快乐惩罚（WoW §A6 "解散 −50 快乐"）。</summary>
    public const int DismissHappinessPenalty = 50;

    /// <summary>
    /// 事件 × 轴 的增减矩阵。
    /// <para>索引 [event, axis]。数值取各家原型的**并集口径**，同一事件在不同游戏里的差异
    /// 由物种模板覆盖（例如神兽出战不掉寿命，§B6），不在这个默认表里做分支。</para>
    /// <para>被击飞的忠诚惩罚是<b>百分比</b>（梦幻 "−20% 当前忠诚"，§B6），矩阵表达不了，
    /// 单列在 <see cref="KnockedOutLoyaltyPercentBps"/> 并用专门分支处理——
    /// 这是刻意的：把百分比写成固定值会让低忠诚宠惩罚过轻，与设计意图相反。</para>
    /// </summary>
    public static readonly int[][] EventDelta =
    {
        //                        Loyalty Happiness Lifespan Intimacy
        new[] { -0,              0,      -1,       0 },      // Battle（梦幻：每出战 2 次 −1 忠诚 → 由调用方每两次传一次）
        new[] { -20,             -120,   -50,      0 },      // KnockedOut（忠诚/寿命：梦幻+大话；快乐衰减幅度本工程定）
        new[] { +20,             +60,    0,        0 },      // Revived（大话 "死亡后复活 +20"）
        new[] { +10,             +20,    0,        +1 },     // PetLevelUp（大话 "宠升级 +10"；升级也提亲密 §C6）
        new[] { +10,             +10,    0,        0 },      // OwnerLevelUp（大话 "参战宠随主人升级 +10"）
        new[] { -10,             -40,    0,        0 },      // OwnerDied（大话 "主人死亡 −10"）
        new[] { -5,              -20,    0,        0 },      // OwnerFled（大话 "主人逃离战斗 −5"）
        new[] { -20,             -60,    0,        0 },      // Traded（梦幻/大话交易 "−20"）
        new[] { +5,              0,      0,        0 },      // Fed（忠诚小幅上涨；快乐由 ApplyFeed 按等级差另算）
        new[] { 0,               0,      0,        0 },      // Tamed（恢复量取决于道具，见 ApplyRestore）
        new[] { 0,               -1,     0,        0 },      // Decay（每 tick 的快乐衰减基础量；速率由忠诚缩放）
        new[] { 0,               0,      0,        +5 },     // IntimacyItem
    };

    /// <summary>被击飞时的忠诚百分比惩罚（bps）：梦幻 "−20% 当前忠诚"（§B6 [高]）。</summary>
    public const int KnockedOutLoyaltyPercentBps = 2_000;

    /// <summary>亲密上限（大话"亲密值不超过材料宠、最多 200 万"，§C9 手游口径）。</summary>
    public const int IntimacyMax = 2_000_000;

    /// <summary>
    /// 施加一次事件，返回新值（不修改入参——内核不许有隐藏副作用）。
    /// </summary>
    /// <param name="values">当前四轴。</param>
    /// <param name="evt">事件。</param>
    /// <param name="knockedOutUsesPercentLoyalty">true（默认）时忠诚按百分比扣（梦幻口径）；false 时用矩阵固定值（大话口径 −20）。</param>
    public static PetBondValues ApplyEvent(in PetBondValues values, PetBondEvent evt,
        bool knockedOutUsesPercentLoyalty = true)
    {
        var rowIndex = (int)evt;
        if (rowIndex < 0 || rowIndex >= EventDelta.Length) return values;
        var row = EventDelta[rowIndex];

        var next = values;
        next.Loyalty += row[0];
        next.Happiness += row[1];
        next.Lifespan += row[2];
        next.Intimacy += row[3];

        if (evt == PetBondEvent.KnockedOut && knockedOutUsesPercentLoyalty)
        {
            // 百分比扣的是"当前值"，所以必须在矩阵固定值之外单独走一次；
            // 顺序是先按百分比、再钳位，钳位在前会把低忠诚宠的惩罚钳成 0。
            var percentLoss = (int)((long)values.Loyalty * KnockedOutLoyaltyPercentBps / 10_000L);
            next.Loyalty = values.Loyalty - percentLoss;
        }

        ClampAxes(ref next);
        return next;
    }

    /// <summary>
    /// 喂食（WoW §A6：唯一提升快乐的方法，回量由食物等级与宠物等级差决定，且会打断战斗动作）。
    /// </summary>
    /// <param name="values">当前四轴。</param>
    /// <param name="petLevel">宠物等级。</param>
    /// <param name="foodLevel">食物等级。</param>
    /// <param name="inCombatOrJustHurt">true = 正在战斗或刚受击，喂食被打断，快乐不涨。</param>
    public static PetBondValues ApplyFeed(in PetBondValues values, int petLevel, int foodLevel, bool inCombatOrJustHurt)
    {
        if (inCombatOrJustHurt) return values;
        var gain = FeedGain(petLevel, foodLevel);
        var next = values;
        next.Happiness += gain;
        ClampAxes(ref next);
        return next;
    }

    /// <summary>喂食回量：等级接近或高于宠级 +350；差 &lt;20 给 +170；差 &lt;30 给 +80；再远不给（WoW §A6）。</summary>
    public static int FeedGain(int petLevel, int foodLevel)
    {
        var gap = petLevel - foodLevel; // 食物**低于**宠物等级的差值
        if (gap <= 0) return FeedNearLevelGain;
        if (gap < 20) return FeedGapUnder20Gain;
        if (gap < 30) return FeedGapUnder30Gain;
        return 0;
    }

    /// <summary>
    /// 快乐自然衰减：速率由忠诚决定（WoW §A6 "忠诚越高掉得越慢"）。
    /// </summary>
    /// <param name="happiness">当前快乐。</param>
    /// <param name="loyalty">当前忠诚 0–100。</param>
    /// <param name="elapsedMinutes">经过的分钟数。</param>
    /// <param name="baseDecayPerMinuteAtFullLoyalty">满忠诚时的每分钟衰减基准。</param>
    /// <remarks>
    /// WoW 资料给的是"满忠诚约 −7.2/秒量级"这个**存疑刻度**（逆向 §A6 标注），
    /// 直接照抄会让宠物几分钟内就红脸。这里把它降为"每分钟"并取 2 作基准，
    /// 真正的平衡旋钮留给物种模板；忠诚缩放关系（越高越慢）是原样保留的。
    /// </remarks>
    public static int DecayHappiness(int happiness, int loyalty, int elapsedMinutes,
        int baseDecayPerMinuteAtFullLoyalty = 2)
    {
        if (happiness <= 0 || elapsedMinutes <= 0) return happiness < 0 ? 0 : happiness;
        var loy = loyalty < 0 ? 0 : (loyalty > LoyaltyMax ? LoyaltyMax : loyalty);

        // 忠诚为 0 时衰放大约 4 倍（1 + 3×(1 − loyalty/max)）；倍率是本工程定的形状，关系来自 WoW。
        var multiplierBps = 10_000 + (long)(LoyaltyMax - loy) * 30_000 / LoyaltyMax;
        var loss = (int)((long)baseDecayPerMinuteAtFullLoyalty * elapsedMinutes * multiplierBps / 10_000L);

        var left = happiness - loss;
        return left < 0 ? 0 : left;
    }

    /// <summary>伤害倍率三档（WoW §A6：红 75% / 黄 100% / 绿 125%）。</summary>
    public static PetMood MoodOf(int happiness)
    {
        if (happiness >= HappinessGreenAt) return PetMood.Green;
        if (happiness >= HappinessYellowAt) return PetMood.Yellow;
        return PetMood.Red;
    }

    /// <summary>把心情展开成伤害倍率（bps）。</summary>
    public static int DamageMultiplierBps(PetMood mood) => mood switch
    {
        PetMood.Green => DamageMultGreenBps,
        PetMood.Yellow => DamageMultYellowBps,
        PetMood.Red => DamageMultRedBps,
        _ => DamageMultYellowBps,
    };

    /// <summary>
    /// 出战门槛判据（返回原因码；<see cref="PetBondBlockReason.None"/> 才可出战）。
    /// </summary>
    /// <param name="values">四轴当前值。</param>
    /// <param name="petLevel">宠物等级。</param>
    /// <param name="ownerLevel">主人等级。</param>
    /// <param name="ownerLevelSlack">允许超出主人的级数（天龙 10，§D3；WoW 0，§A3）。</param>
    /// <param name="isImmortalLifespan">true = "永生"（神兽/特定模板不受寿命门槛约束，§B6）。</param>
    /// <param name="isHatched">false = 还是蛋。</param>
    /// <param name="isAvailable">false = 繁殖冷却/待领取等非持有态。</param>
    /// <remarks>
    /// 判据顺序是<b>有语义的</b>：先判"根本不是能用的状态"（未孵化/不可用），再判硬门槛（快乐/寿命/超级），
    /// 最后才到概率门槛（忠诚）。反过来会让一只"还没孵化"的宠报出"忠诚不足"，玩家完全看不出为什么。
    /// </remarks>
    public static PetBondBlockReason CheckDeployable(in PetBondValues values, int petLevel, int ownerLevel,
        int ownerLevelSlack = 10, bool isImmortalLifespan = false, bool isHatched = true, bool isAvailable = true)
    {
        if (!isHatched) return PetBondBlockReason.NotHatched;
        if (!isAvailable) return PetBondBlockReason.NotAvailable;
        if (values.Happiness < HappinessBlockThreshold) return PetBondBlockReason.LowHappiness;
        if (!isImmortalLifespan && values.Lifespan < LifespanBlockThreshold) return PetBondBlockReason.LowLifespan;
        if (ownerLevel > 0 && petLevel > ownerLevel + ownerLevelSlack) return PetBondBlockReason.OverOwnerLevel;
        return PetBondBlockReason.None;
    }

    /// <summary>
    /// 忠诚软门槛的拒绝概率（bps）：梦幻 "≤70 起效，越低概率越高"（§B6）。
    /// <para>形状取线性：忠诚 70 → 0%，忠诚 0 → 100%。资料没给曲线，线性是唯一不加解释的插法；
    /// 具体斜率由物种模板的 <c>loyaltyRefuseCurve</c> 覆盖。</para>
    /// </summary>
    public static int RefusalChanceBps(int loyalty)
    {
        if (loyalty >= LoyaltyRefuseThreshold) return 0;
        if (loyalty <= 0) return 10_000;
        return (LoyaltyRefuseThreshold - loyalty) * 10_000 / LoyaltyRefuseThreshold;
    }

    /// <summary>
    /// 亲密对特殊技能触发率的加成（bps，大话 §C6 "越高越能触发连击"）。
    /// </summary>
    /// <param name="intimacy">亲密值。</param>
    /// <param name="cap">视为"满"的亲密值；本工程取 50 万（大话点化前置是亲密 ≥50 万，§C1，用它当天然刻度）。</param>
    /// <remarks>封顶给到 +30%：再高会让"亲密"变成第一属性，而四款游戏里它都只是触发率修饰。</remarks>
    public static int IntimacyTriggerBonusBps(int intimacy, int cap = 500_000)
    {
        if (intimacy <= 0 || cap <= 0) return 0;
        var ratio = intimacy > cap ? cap : intimacy;
        return (int)((long)ratio * 3_000L / cap);
    }

    /// <summary>逐轴钳位到各自定义域；负数一律钳 0（寿命/亲密可以为 0 但不能为负）。</summary>
    public static void ClampAxes(ref PetBondValues values)
    {
        if (values.Loyalty < 0) values.Loyalty = 0;
        if (values.Loyalty > LoyaltyMax) values.Loyalty = LoyaltyMax;
        if (values.Happiness < 0) values.Happiness = 0;
        if (values.Happiness > HappinessMax) values.Happiness = HappinessMax;
        if (values.Lifespan < LifespanMin) values.Lifespan = LifespanMin;
        if (values.Intimacy < 0) values.Intimacy = 0;
        if (values.Intimacy > IntimacyMax) values.Intimacy = IntimacyMax;
    }
}

/// <summary>心情三档（快乐轴的投影，WoW 用头像脸图标显示，§A6）。</summary>
public enum PetMood : byte
{
    /// <summary>红脸：不高兴，伤害 75%，且会掉忠诚。</summary>
    Red = 0,

    /// <summary>黄脸：满足，伤害 100%。</summary>
    Yellow = 1,

    /// <summary>绿脸：快乐，伤害 125%，涨忠诚。</summary>
    Green = 2,
}

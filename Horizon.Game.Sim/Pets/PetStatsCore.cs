using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 五维资质（攻/防/体/法/速）。
/// <para>
/// <b>为什么是五维而不是六维</b>：梦幻西游、大话西游、天龙八部三家的资质面板都是五维同构
/// （梦幻 攻击/防御/体力/法力/速度 §B3；天龙 力量/灵气/体力/定力/身法 §D3；大话用"初值四项 + 根骨灵性攻击敏捷"
/// 表达同一组量 §C3）。字段名各家不同，但转化成的战斗量是同一组，故内核只保留一套命名，
/// 各家的原始名由 <see cref="PetStatsCore.AliasNames"/> 做显示层映射。
/// </para>
/// </summary>
public struct PetQualities
{
    /// <summary>攻击资质（梦幻 攻资 / 天龙 力量资质）。</summary>
    public int Attack;

    /// <summary>防御资质（梦幻 防资 / 天龙 定力资质）。</summary>
    public int Defense;

    /// <summary>体力资质（梦幻 体资 / 天龙 体力资质）。</summary>
    public int Stamina;

    /// <summary>法力资质（梦幻 法资 / 天龙 灵气资质）。</summary>
    public int Mana;

    /// <summary>速度资质（梦幻 速资 / 天龙 身法资质）。</summary>
    public int Speed;

    /// <summary>全 1000 的基准资质（1000 是各家资质表的常用中位刻度）。</summary>
    public static PetQualities Base => new()
    {
        Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000,
    };

    /// <summary>逐项取 min 与 max 之间的钳位值（用于把随机 roll 收进物种区间）。</summary>
    public static PetQualities Clamp(PetQualities value, PetQualities min, PetQualities max) => new()
    {
        Attack = ClampOne(value.Attack, min.Attack, max.Attack),
        Defense = ClampOne(value.Defense, min.Defense, max.Defense),
        Stamina = ClampOne(value.Stamina, min.Stamina, max.Stamina),
        Mana = ClampOne(value.Mana, min.Mana, max.Mana),
        Speed = ClampOne(value.Speed, min.Speed, max.Speed),
    };

    /// <summary>逐项取均值（合成/繁殖的资质基准，梦幻 §B9：(A+B)/2 再乘随机带）。</summary>
    public static PetQualities Mean(in PetQualities a, in PetQualities b) => new()
    {
        Attack = (a.Attack + b.Attack) / 2,
        Defense = (a.Defense + b.Defense) / 2,
        Stamina = (a.Stamina + b.Stamina) / 2,
        Mana = (a.Mana + b.Mana) / 2,
        Speed = (a.Speed + b.Speed) / 2,
    };

    /// <summary>逐项加临时/永久上限加成（bps）。</summary>
    public PetQualities ScaledBy(int basisPoints)
    {
        if (basisPoints <= 0) return this;
        return new PetQualities
        {
            Attack = (int)((long)Attack * basisPoints / 10_000L),
            Defense = (int)((long)Defense * basisPoints / 10_000L),
            Stamina = (int)((long)Stamina * basisPoints / 10_000L),
            Mana = (int)((long)Mana * basisPoints / 10_000L),
            Speed = (int)((long)Speed * basisPoints / 10_000L),
        };
    }

    private static int ClampOne(int v, int lo, int hi)
    {
        if (hi < lo) hi = lo;
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }
}

/// <summary>
/// 五维属性点分配（力量/耐力/体质/灵性/敏捷）。
/// <para>升级时获得的"潜能"由玩家自由分配（梦幻 §B3：每级 5 点；天龙 §D3：每级 5 点）。
/// 内核把它存成显式五维而不是一个总数——分配策略本身就是玩家可见的养成决策。</para>
/// </summary>
public struct PetAttributePoints
{
    /// <summary>力量（→攻击）。</summary>
    public int Strength;

    /// <summary>耐力（→防御）。</summary>
    public int Endurance;

    /// <summary>体质（→气血）。</summary>
    public int Constitution;

    /// <summary>灵性（→法力）。</summary>
    public int Spirit;

    /// <summary>敏捷（→速度）。</summary>
    public int Agility;

    /// <summary>已分配点总数。</summary>
    public readonly int Total => Strength + Endurance + Constitution + Spirit + Agility;

    /// <summary>全 0。</summary>
    public static PetAttributePoints Zero => default;
}

/// <summary>
/// 成长率档位（五档）。
/// <para>逆向出处：天龙 普通/优秀/杰出/卓越/完美（§D3）与梦幻"初始分 5 档"（§B3）完全同构，
/// 大话用连续区间（0.795–0.994 / 1.02–1.275，§C3）——连续区间由 <see cref="PetStatsCore.GrowthMinBps"/>
/// 与 <see cref="PetStatsCore.GrowthMaxBps"/> 承载，档位只是把区间离散化给编辑器用的高层旋钮。</para>
/// </summary>
public enum PetGrowthTier : byte
{
    /// <summary>普通。</summary>
    Ordinary = 0,

    /// <summary>优秀。</summary>
    Good = 1,

    /// <summary>杰出。</summary>
    Fine = 2,

    /// <summary>卓越。</summary>
    Superior = 3,

    /// <summary>完美。</summary>
    Perfect = 4,
}

/// <summary>战斗属性六元组（宠物面板上"后天"那一半）。</summary>
public readonly struct PetCombatStats
{
    /// <summary>气血上限。</summary>
    public readonly int Hp;

    /// <summary>魔法上限。</summary>
    public readonly int Mp;

    /// <summary>攻击。</summary>
    public readonly int Attack;

    /// <summary>防御。</summary>
    public readonly int Defense;

    /// <summary>速度（决定出手顺序）。</summary>
    public readonly int Speed;

    /// <summary>灵力（梦幻 §B3 的第六项；元素伤害的基数）。</summary>
    public readonly int SpiritPower;

    public PetCombatStats(int hp, int mp, int attack, int defense, int speed, int spiritPower)
    {
        Hp = hp < 0 ? 0 : hp;
        Mp = mp < 0 ? 0 : mp;
        Attack = attack < 0 ? 0 : attack;
        Defense = defense < 0 ? 0 : defense;
        Speed = speed < 0 ? 0 : speed;
        SpiritPower = spiritPower < 0 ? 0 : spiritPower;
    }
}

/// <summary>
/// 宠物资质→战斗属性的确定性纯函数（逆向规格 §B3 / §C3 / §D3）。
/// <para>
/// <b>定点数纪律</b>：成长率用 1e-4 整数（11500 = 1.1500）存储，所有乘法链走 long 再整除。
/// 用 float 的话"同种同资质同等级在两个宿主上算出不同面板"是必然会发生的，
/// 而这类差异只表现为"数字差 1"，测试不写到逐位对比就永远抓不住。
/// </para>
/// <para>
/// <b>公式来源必须诚实标注</b>：梦幻的面板公式是社区后验拟合（逆向分析 §B3 标 [中]，作者自述
/// "不代表程序内部设计"），天龙的悟性/灵性公式才是官方口径（§D3 标 [高]）。这里把前者当**可调初值**
/// 而不是权威，每张表都可被物种模板覆盖；改平衡只改本文件，编辑器与客户端都只是消费者。
/// </para>
/// </summary>
public static class PetStatsCore
{
    /// <summary>属性内核版本（进金轨迹对拍锚点）。</summary>
    public const int Version = 1;

    /// <summary>资质下限（0 会让"打不出伤害"和"配置漏填"在数值上不可区分，故留 1）。</summary>
    public const int MinQuality = 1;

    /// <summary>资质硬上限（含变异加成后的天花板；超出钳位，不做回绕）。</summary>
    public const int MaxQuality = 6000;

    /// <summary>成长率定点刻度：10000 = 1.0000。</summary>
    public const int GrowthScale = 10_000;

    /// <summary>成长率下界（大话观测最低约 0.795，§C3 标 [中]）。</summary>
    public const int GrowthMinBps = 7_950;

    /// <summary>成长率上界（神兽进阶后可逼近 1.3，§B8；留到 1.5 给编辑器余量）。</summary>
    public const int GrowthMaxBps = 15_000;

    /// <summary>每级潜能点数（梦幻 §B3 与天龙 §D3 均为 5，两源一致 → [高]）。</summary>
    public const int PotentialPointsPerLevel = 5;

    /// <summary>宠物等级上限（梦幻最高参战段 175，§B1）。</summary>
    public const int MaxPetLevel = 175;

    /// <summary>
    /// 悟性加成表（bps，索引 0 = 1 级悟性）。<b>官方口径</b>：天龙 1%–39.3%（§D3 [高]）。
    /// </summary>
    public static readonly int[] ComprehensionBonusBps =
        { 100, 150, 210, 300, 800, 1100, 1450, 2350, 3000, 3930 };

    /// <summary>
    /// 灵性加成表——普通灵（bps）。
    /// <para><b>这是拟合值，不是原值</b>：原始资料只给了端点 1%→31%（§D3），
    /// 中间按线性插取并四舍五入。若日后拿到逐档官方表，只改本数组，别动调用方。</para>
    /// </summary>
    public static readonly int[] SpiritBonusNormalBps =
        { 100, 433, 767, 1100, 1433, 1767, 2100, 2433, 2767, 3100 };

    /// <summary>超灵加成表（bps）；同样只有端点 1%→34% 是原值，中间为线性插取。</summary>
    public static readonly int[] SpiritBonusSuperBps =
        { 100, 478, 856, 1233, 1611, 1989, 2367, 2744, 3122, 3400 };

    /// <summary>
    /// 变异对资质上限的放大带（梦幻 §B1：可达原上限 1.01–1.08 倍，另"额外 8 档"）。
    /// </summary>
    public const int MutantQualityCapMinBps = 10_100;
    public const int MutantQualityCapMaxBps = 10_800;

    /// <summary>变异相对普通种额外可用的资质档数（梦幻"额外 8 档"，§B3）。</summary>
    public const int MutantExtraQualitySteps = 8;

    /// <summary>梦幻野生种从最高资质到最低的档数（§B3：平均 21 档，= 天生最高资质 / 1.2）。</summary>
    public const int WildQualitySteps = 21;

    /// <summary>
    /// 天龙资质公式的**唯一**实现：现有资质 = 裸资 ×(1+悟性加成)×(1+灵性加成)。
    /// <para>逆向 §D3 给的可复核算例：裸资 2000、悟性 10、灵性 10 普通 ⇒ 2000×1.393×1.31 ≈ 3650；
    /// 超灵 ⇒ ≈3733。<b>这两个数字由 <c>PetEditorKernelTests</c> 逐条钉住</b>，
    /// 因为它们是"文档里写了例子的公式"——例子是最强的参照。</para>
    /// </summary>
    /// <param name="baseQuality">裸资（未加成）。</param>
    /// <param name="comprehensionLevel">悟性 0–10；0 表示从未提升，<b>不是"1% 加成"</b>。</param>
    /// <param name="spiritLevel">灵性 0–10；同上。</param>
    /// <param name="superSpirit">true = 超灵档。</param>
    /// <param name="mutationBonusBps">变异/临时变质的额外上限放大（bps，10000 = 不放大）。</param>
    public static int EffectiveQuality(int baseQuality, int comprehensionLevel, int spiritLevel,
        bool superSpirit, int mutationBonusBps = GrowthScale)
    {
        var q = baseQuality < MinQuality ? MinQuality : baseQuality;

        // 等级 0 与等级 1 必须区分：0 是"没提过"，1 已经带 1% 加成（表就是这么定的）。
        var comp = comprehensionLevel <= 0 ? 0 : BonusAt(ComprehensionBonusBps, comprehensionLevel);
        var spi = spiritLevel <= 0 ? 0 : BonusAt(superSpirit ? SpiritBonusSuperBps : SpiritBonusNormalBps, spiritLevel);

        long value = q;
        value = value * (10_000L + comp) / 10_000L;
        value = value * (10_000L + spi) / 10_000L;
        var mut = mutationBonusBps < GrowthScale ? GrowthScale : mutationBonusBps;
        value = value * mut / 10_000L;

        if (value > MaxQuality) return MaxQuality;
        return (int)value;
    }

    /// <summary>资质五维整表加成（<see cref="EffectiveQuality"/> 的向量版，逐项独立）。</summary>
    public static PetQualities EffectiveQualities(in PetQualities baseQualities, int comprehensionLevel,
        int spiritLevel, bool superSpirit, int mutationBonusBps = GrowthScale) => new()
        {
            Attack = EffectiveQuality(baseQualities.Attack, comprehensionLevel, spiritLevel, superSpirit, mutationBonusBps),
            Defense = EffectiveQuality(baseQualities.Defense, comprehensionLevel, spiritLevel, superSpirit, mutationBonusBps),
            Stamina = EffectiveQuality(baseQualities.Stamina, comprehensionLevel, spiritLevel, superSpirit, mutationBonusBps),
            Mana = EffectiveQuality(baseQualities.Mana, comprehensionLevel, spiritLevel, superSpirit, mutationBonusBps),
            Speed = EffectiveQuality(baseQualities.Speed, comprehensionLevel, spiritLevel, superSpirit, mutationBonusBps),
        };

    /// <summary>把成长率档位展开成 bps 初值（天龙的五档语义，梦幻"5 档"同构）。</summary>
    /// <remarks>档位→bps 的映射是本工程拍的平衡初值，**不是逆向得到的原值**（四款游戏都没公开过档位对应数字）。
    /// 取 1.000/1.050/1.100/1.150/1.200 是因为它落在大话实测区间 0.795–1.275 的中段（§C3）。</remarks>
    public static int GrowthBpsOf(PetGrowthTier tier) => tier switch
    {
        PetGrowthTier.Ordinary => 10_000,
        PetGrowthTier.Good => 10_500,
        PetGrowthTier.Fine => 11_000,
        PetGrowthTier.Superior => 11_500,
        PetGrowthTier.Perfect => 12_000,
        _ => 10_000,
    };

    /// <summary>成长率钳位到 [<see cref="GrowthMinBps"/>, <see cref="GrowthMaxBps"/>]。</summary>
    public static int ClampGrowth(int growthBps)
    {
        if (growthBps < GrowthMinBps) return GrowthMinBps;
        if (growthBps > GrowthMaxBps) return GrowthMaxBps;
        return growthBps;
    }

    /// <summary>
    /// 有效资质 → 战斗属性六元组（梦幻后验公式族，逆向 §B3 [中]）。
    /// <para>逐项对应原文：
    /// 气血 = ⌊等级×体资/1000⌋ + ⌊体质×成长×6⌋；
    /// 魔法 = ⌊等级×法资/500⌋ + ⌊灵性×成长×3⌋；
    /// 攻击 = ⌊等级×攻资×(14+10×成长)/7550⌋ + ⌊力量×成长⌋；
    /// 防御 = ⌊等级×防资/433⌋ + ⌊耐力×成长×4/3⌋；
    /// 速度 = ⌊速资×总速度点/1000⌋；
    /// 灵力 = 拟合项（原文含 0.3/0.7/0.4/0.2 的混合系数，§B3）。</para>
    /// <para>装备/灵石/内丹给的附加量由 <paramref name="hpFlat"/> 等参数带入——本函数只管"由资质和点生成的那一半"，
    /// 附加来源各自有 core，不在此处重算。</para>
    /// </summary>
    public static PetCombatStats Compute(in PetQualities qualities, int level, int growthBps,
        in PetAttributePoints points, int hpFlat = 0, int mpFlat = 0, int attackFlat = 0,
        int defenseFlat = 0, int speedFlat = 0, int spiritPowerFlat = 0)
    {
        if (level < 1) level = 1;
        if (level > MaxPetLevel) level = MaxPetLevel;
        var g = ClampGrowth(growthBps);

        var q = qualities;
        if (q.Attack < MinQuality) q.Attack = MinQuality;
        if (q.Defense < MinQuality) q.Defense = MinQuality;
        if (q.Stamina < MinQuality) q.Stamina = MinQuality;
        if (q.Mana < MinQuality) q.Mana = MinQuality;
        if (q.Speed < MinQuality) q.Speed = MinQuality;

        // 成长参与乘法一律走 (long)value * g / GrowthScale，等价于 float 版再向下取整。
        var hp = level * q.Stamina / 1000 + Mul(points.Constitution, g, 6, 1);
        var mp = level * q.Mana / 500 + Mul(points.Spirit, g, 3, 1);
        var attack = (int)(level * (long)q.Attack * (140_000L + 10L * g) / (7550L * 10_000L))
                     + Mul(points.Strength, g, 1, 1);
        var defense = level * q.Defense / 433 + Mul(points.Endurance, g, 4, 3);

        // 速度按"总速度点"而非单一属性：敏捷 + 装备/宝石带来的速度点合并计入。
        var speedTotal = points.Agility + speedFlat;
        var speed = q.Speed * speedTotal / 1000;

        // 灵力：用原文的混合系数（体力0.3/法力0.7/力量0.4/耐力0.2，§B3），同样走 bps 避免 float。
        var mixed = points.Constitution * 3000L + points.Spirit * 7000L
                    + points.Strength * 4000L + points.Endurance * 2000L;
        var spiritPower = (int)(mixed / 10_000L) + spiritPowerFlat;

        return new PetCombatStats(
            hp + hpFlat,
            mp + mpFlat,
            attack + attackFlat,
            defense + defenseFlat,
            speed,
            spiritPower);
    }

    /// <summary>(value × growth × num / den) 的定点式，向下取整——对应原文里每一个"取整(...)"。</summary>
    private static int Mul(int value, int growthBps, int num, int den)
    {
        if (den <= 0) den = 1;
        var v = value < 0 ? 0 : value;
        return (int)(v * (long)growthBps * num / ((long)GrowthScale * den));
    }

    /// <summary>按 1 基等级取加成表；越界钳到末项（坏配置钳位不抛）。</summary>
    private static int BonusAt(int[] table, int level)
    {
        if (table is null || table.Length == 0) return 0;
        if (level <= 1) return table[0];
        if (level > table.Length) return table[^1];
        return table[level - 1];
    }

    /// <summary>
    /// 各家原始字段名的显示层映射（编辑器下拉框用它展示"这是梦幻口径还是天龙口径"）。
    /// <para><b>名字只用于显示，绝不参与任何判等</b>（记忆 <c>display-text-must-not-be-identity</c>）。</para>
    /// </summary>
    public static string[] AliasNames(string family) => family switch
    {
        "xyq" => new[] { "攻击资质", "防御资质", "体力资质", "法力资质", "速度资质" },
        "tlbb" => new[] { "力量资质", "定力资质", "体力资质", "灵气资质", "身法资质" },
        "xy2" => new[] { "攻击初值", "防御初值", "气血初值", "法力初值", "速度初值" },
        _ => new[] { "Attack", "Defense", "Stamina", "Mana", "Speed" },
    };
}

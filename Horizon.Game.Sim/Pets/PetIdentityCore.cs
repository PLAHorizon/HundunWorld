using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 宠物个体的持久状态。
/// <para>
/// <b>为什么状态要单独建枚举而不是用几个布尔位拼</b>：宠物被抓住后**永久存在**（决策口径），
/// 一只宠在生命周期里可能同时"在背包里"和"不是出战宠"，但"待领取的繁殖产物"与"蛋"是互斥的，
/// 用布尔位拼会出现 (在背包 ∧ 待领取) 这种在业务上根本不存在的组合，而库里没有任何东西拦住它。
/// </para>
/// </summary>
public enum PetInstanceState : byte
{
    /// <summary>尚未被捕获（野外可捕捉模板本身，不是个体）。</summary>
    Wild = 0,

    /// <summary>蛋/未孵化。</summary>
    Egg = 1,

    /// <summary>已孵化或已捕获的幼体/个体，在玩家持有中。</summary>
    Owned = 2,

    /// <summary>放养跟随（被"放出"在世界里跟着主人，非出战）。</summary>
    Following = 3,

    /// <summary>出战中（每玩家同时只许一只，见 <see cref="PetAiCore"/>）。</summary>
    Battling = 4,

    /// <summary>附体位占用（给主人加成，不占出战位）。</summary>
    Possessed = 5,

    /// <summary>兽栏寄存。</summary>
    Stable = 6,

    /// <summary>繁殖冷却中（父母被占用）。</summary>
    Breeding = 7,

    /// <summary>繁殖产物待领取——<b>48 小时不领取则回归自然</b>（天龙逆向 §D9）。</summary>
    PendingClaim = 8,

    /// <summary>已放生/归还自然（终态，但记录必须保留，不能删）。</summary>
    Released = 9,
}

/// <summary>
/// 宠物编号（species id）与个体编号（instance id）的确定性纯函数（设计文档 §2.3）。
/// <para>
/// <b>两层编号不可混用</b>：<c>species_id</c> 是编辑器里制定的"这一种宠"，全局唯一且长期稳定；
/// <c>instance_id</c> 是"某一只具体宠物"，被抓后永久存在。四款被逆向的游戏里
/// <b>没有任何一款给玩家看过全局唯一 ID</b>（逆向分析 §E3）——它们全靠
/// "昵称 + 等级 + 资质面板"让玩家区分同名的两只宠。但服务端不能这么办：昵称可改、可翻译，
/// 拿它当身份一次改名就废掉全部旧存档（见记忆 <c>display-text-must-not-be-identity</c>）。
/// 所以本工程把两件事分开：**身份用 <see cref="NewInstanceId"/>，展示用昵称+指纹**。
/// </para>
/// <para>
/// <b>重复副本是本模块存在的首要理由</b>：玩家抓了三只同名吸血鬼，三个 instance 都指向同一
/// <c>species_id</c>，必须能共存、必须能被分别寻址、必须在"计数"和"实例清单"两处对得上。
/// 任何按 (owner, species) 去重的写法都会静默吃掉玩家的第二、第三只宠。
/// </para>
/// </summary>
public static class PetIdentityCore
{
    /// <summary>编号内核版本（进金轨迹对拍锚点）。</summary>
    public const int Version = 1;

    /// <summary>物种号段下界；1–499999 留给既有域（怪物 1–99999/…300000+、技能 800000+）。</summary>
    public const int MinSpeciesId = 500_000;

    /// <summary>
    /// 物种号段上界（含）。四个子段的设计理由见 <see cref="RarityTierOf"/>：
    /// 500000–599999 普通、600000–649999 变异/稀有、650000–699999 神兽、700000+ 活动限定。
    /// </summary>
    public const int MaxSpeciesId = 799_999;

    /// <summary>个体序号的位宽基数：instance = species × <see cref="InstanceSequenceBase"/> + sequence。</summary>
    public const long InstanceSequenceBase = 100_000L;

    /// <summary>单种可容纳的个体数上限（超出必须换号段或改判据，绝不允许静默回绕）。</summary>
    public const int MaxInstancesPerSpecies = 99_999;

    /// <summary>个体编号的"未分配"哨兵。<b>0 不是合法 ID</b>——结构体默认值会伪装成合法下标（见记忆同名条目）。</summary>
    public const long UnassignedInstanceId = 0L;

    /// <summary>序号的"从未分配过"哨兵；与合法序号 1 区分开。</summary>
    public const int NoSequence = 0;

    /// <summary>
    /// 由物种号与序号合成个体编号。
    /// </summary>
    /// <param name="speciesId">合法物种号（必须落在 [<see cref="MinSpeciesId"/>, <see cref="MaxSpeciesId"/>]）。</param>
    /// <param name="sequence">单调递增序号，取值 [1, <see cref="MaxInstancesPerSpecies"/>]。</param>
    /// <returns>个体编号；<paramref name="speciesId"/> 或 <paramref name="sequence"/> 非法时返回 <see cref="UnassignedInstanceId"/>。</returns>
    /// <remarks>
    /// 非法输入**返回哨兵而不是抛异常**：本函数会在读档、补发、对账三条路径上被调用，
    /// 抛异常会让一条脏数据打死整批加载；哨兵则由调用方按"无法解析"大声上报。
    /// </remarks>
    public static long NewInstanceId(int speciesId, int sequence)
    {
        if (!IsSpeciesId(speciesId)) return UnassignedInstanceId;
        if (sequence < 1 || sequence > MaxInstancesPerSpecies) return UnassignedInstanceId;
        return (long)speciesId * InstanceSequenceBase + sequence;
    }

    /// <summary>
    /// 反解个体编号。<b>判"能不能解析"必须同时满足</b>：返回值 true、物种号落在合法号段、序号非 0。
    /// <para>只看返回值会把 (species=0, seq=0) 这种垃圾也解析成"合法"。</para>
    /// </summary>
    public static bool TryParseInstanceId(long instanceId, out int speciesId, out int sequence)
    {
        speciesId = 0;
        sequence = 0;
        if (instanceId <= UnassignedInstanceId) return false;

        var species = (int)(instanceId / InstanceSequenceBase);
        var seq = (int)(instanceId % InstanceSequenceBase);
        if (!IsSpeciesId(species)) return false;
        if (seq < 1 || seq > MaxInstancesPerSpecies) return false;

        speciesId = species;
        sequence = seq;
        return true;
    }

    /// <summary>物种号是否落在宠物号段（含四种子段）。注意 0 与负数一律 false。</summary>
    public static bool IsSpeciesId(int speciesId)
        => speciesId >= MinSpeciesId && speciesId <= MaxSpeciesId;

    /// <summary>
    /// 从号段推断稀有度档位——<b>号段就是身份的一部分</b>，编辑器和运营改一个数就会改变一类的定性，
    /// 因此这个映射只许在本函数里写一次（唯一事实源）。
    /// </summary>
    /// <param name="speciesId">物种号；非法返回 <see cref="PetRarityTier.Unknown"/>。</param>
    public static PetRarityTier RarityTierOf(int speciesId)
    {
        if (speciesId < 500_000 || speciesId > 799_999) return PetRarityTier.Unknown;
        if (speciesId < 600_000) return PetRarityTier.Common;
        if (speciesId < 650_000) return PetRarityTier.Mutant;
        if (speciesId < 700_000) return PetRarityTier.Divine;
        return PetRarityTier.Event;
    }

    /// <summary>给出下一个可用物种号（编辑器"新建"按钮调这个，不要自己 +1）。</summary>
    /// <param name="usedSpeciesIds">已用物种号集合；顺序无关。</param>
    public static int NextSpeciesId(int[] usedSpeciesIds)
    {
        var candidate = MinSpeciesId;
        if (usedSpeciesIds is null || usedSpeciesIds.Length == 0) return candidate;

        // 线性扫描而不是排序：号段基数（30 万）远大于实际条目数，且编辑器不需要这个热点。
        // 用 HashSet 会把"号段内已用"这个语义洗成"集合成员"，调试时看不出跳过了谁。
        while (candidate <= MaxSpeciesId)
        {
            var used = false;
            for (var i = 0; i < usedSpeciesIds.Length; i++)
            {
                if (usedSpeciesIds[i] == candidate) { used = true; break; }
            }
            if (!used) return candidate;
            candidate++;
        }
        return UnassignedSpeciesId;
    }

    /// <summary>号段耗尽的哨兵（与 0 区分：0 是"从没填过"，这个是"填满了"）。</summary>
    public const int UnassignedSpeciesId = -1;

    /// <summary>
    /// 重复副本指纹——把"这两只是不是各自的副本"变成可比较的整数哈希。
    /// <para>
    /// <b>它只用于展示与对账，绝不用于身份判定</b>：指纹相同代表"面板看起来一样"，
    /// 两只完全不同的宠撞出同一指纹在数学上是可能的。任何按指纹去重的实现都是 bug。
    /// </para>
    /// </summary>
    /// <param name="speciesId">物种号。</param>
    /// <param name="generation">形态阶段（<see cref="PetGeneration"/> 的序号）。</param>
    /// <param name="qualityHash">资质五维的稳定哈希（由调用方用 <see cref="HashOf"/> 算）。</param>
    /// <param name="growthBps">成长率定点值（1e-4）。</param>
    public static long DuplicateFingerprint(int speciesId, int generation, int qualityHash, int growthBps)
    {
        // FNV-1a 64 位：跨平台逐位一致，且没有浮点参与。
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var h = offset;
        h = Mix(h, speciesId);
        h = Mix(h, generation);
        h = Mix(h, qualityHash);
        h = Mix(h, growthBps);
        return (long)h;

        static ulong Mix(ulong acc, int value)
        {
            var v = (ulong)(uint)value;
            acc ^= v & 0xFF; acc *= prime;
            acc ^= (v >> 8) & 0xFF; acc *= prime;
            acc ^= (v >> 16) & 0xFF; acc *= prime;
            acc ^= (v >> 24) & 0xFF; acc *= prime;
            return acc;
        }
    }

    /// <summary>把资质五维按**固定顺序**压成一个稳定哈希。顺序写死在这里，调用方不得自行排列。</summary>
    public static int HashOf(PetQualities qualities)
    {
        // 同一个 FNV 思路的 32 位版；逐字节而非按 int 混合，避免符号扩展差异。
        const uint prime = 16777619U;
        var h = 2166136261U;
        h = Mix(h, qualities.Attack);
        h = Mix(h, qualities.Defense);
        h = Mix(h, qualities.Stamina);
        h = Mix(h, qualities.Mana);
        h = Mix(h, qualities.Speed);
        return (int)h;

        static uint Mix(uint acc, int value)
        {
            var v = (uint)value;
            acc ^= v & 0xFF; acc *= prime;
            acc ^= (v >> 8) & 0xFF; acc *= prime;
            acc ^= (v >> 16) & 0xFF; acc *= prime;
            acc ^= (v >> 24) & 0xFF; acc *= prime;
            return acc;
        }
    }

    /// <summary>
    /// 对账：某 (owner, species) 的实例数与实例清单是否一致。
    /// <para>
    /// <b>这条检查存在的全部理由</b>：宠物永久存在 ⇒ 计数列与实例行是两处写入。任何一处漏写
    /// （比如放生只减了计数没置状态）都会让玩家看到"有 2 只但列表里只有 1 只"，
    /// 而这种不一致不会报错，只会静默改变可见数字。
    /// </para>
    /// </summary>
    /// <param name="declaredCount">计数字段声称的数量。</param>
    /// <param name="instanceIds">实际实例编号清单（同 species）；可含非本物种的号，会被计入 <see cref="foreignSpecies"/>。</param>
    /// <param name="foreignSpecies">清单里物种号与 <paramref name="speciesId"/> 不符的条数。</param>
    /// <returns>true = 一致。</returns>
    public static bool ReconcileDuplicates(int speciesId, int declaredCount, long[] instanceIds, out int foreignSpecies)
    {
        foreignSpecies = 0;
        if (instanceIds is null) return declaredCount == 0;

        var mine = 0;
        for (var i = 0; i < instanceIds.Length; i++)
        {
            if (!TryParseInstanceId(instanceIds[i], out var parsedSpecies, out _))
            {
                foreignSpecies++;
                continue;
            }
            if (parsedSpecies == speciesId) mine++;
            else foreignSpecies++;
        }
        return mine == declaredCount;
    }
}

/// <summary>
/// 物种稀有度档（由号段决定，见 <see cref="PetIdentityCore.RarityTierOf"/>）。
/// <para>逆向出处：梦幻的 野生/宝宝/变异/神兽（§B1）、天龙的 宝宝/成年/变异/二代（§D14）、
/// WoW BP 的 Poor/Uncommon/Rare/Epic（§A8）。四家的分档不完全同构，这里取**并集**：
/// <see cref="Divine"/> 对应神兽（野外不可捕、仅活动产出），<see cref="Event"/> 对应限时/绝版。</para>
/// </summary>
public enum PetRarityTier : byte
{
    /// <summary>号段非法或尚未指定。</summary>
    Unknown = 0,

    /// <summary>普通（500000 段）：野外可捕，资质有上限。</summary>
    Common = 1,

    /// <summary>变异/稀有（600000 段）：资质上限 ×1.01–1.08，独有颜色，<b>不可用于炼妖</b>（梦幻 §B1）。</summary>
    Mutant = 2,

    /// <summary>神兽（650000 段）：野外不出现、不可捕；出战/被击飞不掉寿命（梦幻 §B6）。</summary>
    Divine = 3,

    /// <summary>活动/限时（700000 段）：有领取或存续期限。</summary>
    Event = 4,
}

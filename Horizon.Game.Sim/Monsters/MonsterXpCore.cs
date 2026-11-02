using System;

namespace Horizon.Game.Sim.Monsters;

/// <summary>
/// 击杀经验配置（编辑器可逐怪覆盖的一组系数，逆向规格 §2.3）。
/// </summary>
/// <param name="XpBase">经验基准截距。WoW 旧世界观测为 45，外域档为 235。</param>
/// <param name="XpPerLevel">每级增量（WoW 观测为 5）。</param>
/// <param name="RankXpScaleByIndex">按 <see cref="MonsterRank"/> 索引的倍率表（普通 1、精英 2、首领 4）。</param>
/// <remarks>
/// <b>定基口径（决策 D3 已拍板）</b>：基准由<b>怪物等级</b>决定
/// （<c>XpBase + XpPerLevel × 怪物等级</c>），玩家等级只通过 <see cref="MonsterDifficultyCore.ConcessionFactor"/>
/// 影响结果。选它而不选 WoW 原式（<c>45 + 5 × 玩家等级</c>）的理由：后者让基线跟着玩家等级涨，
/// 大号回头刷低级怪时基线反而更高、全靠衰减项压回去，调平衡时两个旋钮互相掩盖。
/// 两条曲线<b>形状相同、锚点不同</b>。
/// </remarks>
public readonly struct MonsterXpConfig
{
    public readonly int XpBase;
    public readonly int XpPerLevel;

    /// <summary>按 <see cref="MonsterRank"/> 索引的经验倍率表；长度必须覆盖 <see cref="MonsterRank.WorldBoss"/>。</summary>
    public readonly double[] RankXpScale;

    /// <summary>
    /// 这只怪的经验倍率基点（10000 = ×1）——魔兽的 <c>CreatureTemplate::ModExperience</c>
    /// （AzerothCore <c>Miscellaneous/Formulas.cpp:90</c> 把它乘进 <c>xpMod</c>，与精英 ×2 同一阶段）。
    /// <para>为什么需要逐模板而不是只靠 rank：世界 BOSS 之外的"剧情怪"、"任务链怪"都要在
    /// 同等级同档位下给更多/更少经验，而 rank 一格表达不了。</para>
    /// </summary>
    public readonly int XpModBps;

    /// <summary>
    /// "血薄的怪给更少经验"那一条的阈值基点——魔兽的 <c>CreatureTemplate::ModHealth</c>
    /// （AzerothCore <c>Entities/Player/KillRewarder.cpp:136-138</c>）。
    /// <para>它照抄了魔兽一个容易被误读的地方：**只有 0..7500 这一档生效**。
    /// 9000 与 10000 一样是"不削减"，15000 也不会放大经验。</para>
    /// </summary>
    public readonly int HealthModBps;

    public MonsterXpConfig(int xpBase, int xpPerLevel, double[] rankXpScale, int xpModBps = 10000, int healthModBps = 10000)
    {
        XpBase = xpBase;
        XpPerLevel = xpPerLevel;
        RankXpScale = rankXpScale ?? throw new ArgumentNullException(nameof(rankXpScale));
        XpModBps = xpModBps;
        HealthModBps = healthModBps;
    }

    /// <summary>
    /// 缺省 rank 倍率表（普通 1、精英 2、稀有精英 2.5、首领/世界 BOSS 4）。
    /// <para>做成**工厂**而不是共享数组：这条表原先在装配层、校验器、命令行试算里各抄了一份字面量，
    /// 三处独立漂移时没人会报错（而它是经验曲线的乘子）。所有想拿默认表的地方都从这里取，
    /// 每次给一个新数组，谁都不共享可变引用。</para>
    /// </summary>
    public static double[] DefaultRankScale() => new double[] { 1.0, 2.0, 2.5, 4.0, 4.0 };

    /// <summary>旧世界（艾泽拉斯）口径的默认表：45 + 5×怪等级，精英 ×2、首领 ×4；逐模板修正全不开。</summary>
    public static MonsterXpConfig Default => new MonsterXpConfig(
        45, 5, DefaultRankScale());

    /// <summary>取该 rank 的经验倍率；表长不足时返回 1.0（漏配不该放大经验）。</summary>
    public double ScaleForRank(MonsterRank rank)
    {
        var i = (int)rank;
        if (RankXpScale == null || i < 0 || i >= RankXpScale.Length) return 1.0;
        return RankXpScale[i];
    }
}

/// <summary>一名击杀参与者（服务端从"摸过这只怪"的集合里挑出）。</summary>
public struct MonsterXpParticipant
{
    /// <summary>角色 ID（稳定排序键）。</summary>
    public ulong CharacterId;

    /// <summary>角色等级。</summary>
    public int Level;

    /// <summary>最后一次对该怪造成有效行为的 tick（决定参与者优先级）。</summary>
    public long LastContributionTick;
}

/// <summary>经验试算的逐因子分解（编辑器"试算"面板与合同测试都读它，而不是只读最终值）。</summary>
public struct MonsterXpBreakdown
{
    /// <summary>基准经验（未乘任何系数）。</summary>
    public double Base;

    /// <summary>让分系数（含灰名断供 = 0）。</summary>
    public double Concession;

    /// <summary>rank 倍率。</summary>
    public double RankScale;

    /// <summary>逐模板经验倍率（魔兽的 ModExperience；1.0 = 没配）。</summary>
    public double XpMod;

    /// <summary>
    /// "血薄削减"实际乘上的系数。1.0 有两种含义——没配，或配的值落在 <c>7501..∞</c> 那段（魔兽口径里那段不生效）；
    /// 想知道是哪一种请读配置本身，别拿这个数反推。
    /// </summary>
    public double HealthMod;

    /// <summary>最终值（两段取整之后）。</summary>
    public long Final;

    /// <summary>难度档（供 UI 上色）。</summary>
    public MonsterDifficulty Difficulty;
}

/// <summary>
/// 击杀贡献经验的确定性内核（逆向规格 §2）。
/// <para>
/// <b>为什么放 Sim</b>：Orleans 权威端与 DS 权威端必须发同一个数，否则同一只怪在两条同步路径上
/// 给不同经验，而玩家会立刻看到（飘字与经验条）。本类是纯函数、无 IO、无随机。
/// </para>
/// <para>
/// <b>参与人数上限 5</b>：WoW 的观测是"摸过就有、但只有前 5 人分"。这一条不是防作弊，
/// 是为了让"20 人围一只怪"不会把刷怪效率变成线性放大。
/// </para>
/// <para>
/// <b>v3（本轮）＝逐模板两段修正</b>：魔兽在"等级曲线 × 难度档"之外还有两个**逐模板**的乘子，
/// 本机权威源码都看得到：
/// ① <c>CreatureTemplate::ModExperience</c>（<c>Miscellaneous/Formulas.cpp:90</c>，与精英 ×2 同一个
/// <c>xpMod</c>，在**同一次取整之前**乘进去）；
/// ② <c>CreatureTemplate::ModHealth</c>（<c>Entities/Player/KillRewarder.cpp:136-138</c> 那句
/// "npcs with relatively low hp give lower exp"，在**已经取整的经验上再截一次**，
/// 而且只有 <c>0 &lt;= ModHealth &lt;= 0.75</c> 才生效——填 9000 与不填一模一样，填 15000 也不会放大）。
/// 没有它们的话，"同为 20 级精英，剧情怪给三倍经验、洞穴小怪血薄给三成"只能靠改 <c>xp_base</c>
/// 或改等级去凑，而那两个旋钮同时影响着别的怪。
/// <b>两个缺省都是 10000</b>：现有 11 只种子怪与既有全部金轨迹在新代码下逐字不变。
/// </para>
/// </summary>
public static class MonsterXpCore
{
    /// <summary>
    /// 版本（进金轨迹锚点）。
    /// <para>v2：组队击杀从"每人各算自己的全额"改成 AzerothCore 的**共享池子**形状
    /// （人数加成 × 等级比例，灰名拿 0，有人灰名则全队减半）。单人击杀的数字逐字不变，
    /// 变的是两个及以上参与者时的总和——这属于"改了手感"，所以要进版本。</para>
    /// </summary>
    /// <summary>
    /// 判据版本。v2=组队分池（pool × rate × 等级比例）；v3=逐模板两段修正（ModExperience 与"血薄削减"）。
    /// <para>v3 在两个倍率都取缺省 10000 时与 v2 **逐字相同**，所以既有金轨迹不会红。</para>
    /// </summary>
    public const int Version = 3;

    /// <summary>可分经验的最大参与人数（超出者按参与优先级截断）。</summary>
    public const int MaxParticipants = 5;

    /// <summary>
    /// 单人击杀经验（完整链：基准 × 让分 × rank 倍率）。
    /// <para>灰名怪返回 0，且 0 与"未配置经验"同值——调用方要区分请读 <see cref="Decompose"/> 的
    /// <see cref="MonsterXpBreakdown.Difficulty"/>。</para>
    /// </summary>
    public static long Compute(MonsterXpConfig cfg, int monsterLevel, int playerLevel, MonsterRank rank)
        => Decompose(cfg, monsterLevel, playerLevel, rank).Final;

    /// <summary>逐因子分解（UI 与测试用最终值之外的东西）。</summary>
    public static MonsterXpBreakdown Decompose(MonsterXpConfig cfg, int monsterLevel, int playerLevel, MonsterRank rank)
    {
        var difficulty = MonsterDifficultyCore.Of(monsterLevel, playerLevel, rank);
        var baseXp = (double)cfg.XpBase + (double)cfg.XpPerLevel * Math.Max(1, monsterLevel);
        var concession = MonsterDifficultyCore.ConcessionFactor(monsterLevel, playerLevel, rank);
        var rankScale = cfg.ScaleForRank(rank);

        // 逐模板倍率与 rank 倍率在魔兽里是**同一次取整之前**乘进去的（Formulas.cpp 的 xpMod），
        // 而 ModHealth 是在那个 uint32 之后**再截一次**（KillRewarder.cpp:138）。
        // 两次截断合成一次会让某些边界值差 1 点经验——所以这里保持同样的两段顺序。
        var xpMod = cfg.XpModBps < 0 ? 1.0 : cfg.XpModBps / 10000.0;   // 负数=坏值，按"没配"处理，绝不让经验归零
        var raw = baseXp * concession * rankScale * xpMod;

        // 负数与 NaN 一律收成 0：让分系数理论上钳在 [0,1.2]，但配置表可以被编辑写成坏值。
        if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < 0.0) raw = 0.0;
        var firstStage = (long)Math.Round(raw, MidpointRounding.AwayFromZero);

        // "血薄削减"只认 0..7500 这一档（魔兽原样：`ModHealth <= 0.75f && >= 0.0f`）。
        var healthModApplied = cfg.HealthModBps >= 0 && cfg.HealthModBps <= HealthModEffectiveCapBps;
        var healthMod = healthModApplied ? cfg.HealthModBps / 10000.0 : 1.0;
        var final = healthModApplied ? (long)(firstStage * healthMod) : firstStage;  // 这一段的取整方式是"截断"，与 AC 的 uint32 一致

        return new MonsterXpBreakdown
        {
            Base = baseXp,
            Concession = concession,
            RankScale = rankScale,
            XpMod = xpMod,
            HealthMod = healthMod,
            Difficulty = difficulty,
            Final = final,
        };
    }

    /// <summary>
    /// "血薄削减"那一条生效的上界（基点）：魔兽写的是 <c>ModHealth &lt;= 0.75f</c>，
    /// 于是 7501..10000 这一段与"没配"完全等价，超过 10000 也不会放大经验。
    /// </summary>
    public const int HealthModEffectiveCapBps = 7500;

    /// <summary>
    /// 为参与者集合逐个算经验，写入 <paramref name="outXp"/>（与 <paramref name="outParticipant"/> 同序）。
    /// </summary>
    /// <remarks>
    /// <b>确定性</b>：参与者必须先按 <see cref="SortParticipantsByPriority"/> 排序
    /// （最后贡献时刻倒序，同刻按 CharacterId 升序），再截断到 <see cref="MaxParticipants"/>。
    /// 顺序由宿主决定而内核不重排的话，同一场战斗在两台机器上会截掉不同的人。
    /// </remarks>
    /// <param name="cfg">经验配置。</param>
    /// <param name="monsterLevel">怪物等级。</param>
    /// <param name="rank">怪物 rank。</param>
    /// <param name="participants">已排序的参与者（长度 &gt; <see cref="MaxParticipants"/> 时只取前 N）。</param>
    /// <param name="outXp">输出缓冲，长度至少 <c>min(participants.Length, MaxParticipants)</c>。</param>
    /// <returns>实际获得经验的人数。</returns>
    public static int ComputeForParticipants(
        MonsterXpConfig cfg,
        int monsterLevel,
        MonsterRank rank,
        ReadOnlySpan<MonsterXpParticipant> participants,
        Span<long> outXp)
    {
        var n = participants.Length;
        if (n > MaxParticipants) n = MaxParticipants;
        if (outXp.Length < n) return 0;

        for (var i = 0; i < n; i++)
        {
            outXp[i] = Decompose(cfg, monsterLevel, participants[i].Level, rank).Final;
        }
        return n;
    }

    /// <summary>
    /// 组队分经验的奖励距离（米）。
    /// <para>逆向依据：AzerothCore `Entities/Player/Player.cpp:12769` 的
    /// <c>GetDistance(source) &lt;= CONFIG_GROUP_XP_DISTANCE</c>，
    /// 而那个配置的默认值写在 `World/WorldConfig.cpp:181`：<c>"MaxGroupXPDistance", 74.0f</c>。
    /// 单位按本项目的世界尺度当米用（与 <c>assist_radius</c>、<c>leash_radius</c> 同一口径）。</para>
    /// </summary>
    public const float GroupRewardDistance = 74f;

    /// <summary>
    /// 组队加成率（按"在场且活着的参与人数"）。
    /// <para>逆向依据：AzerothCore `Miscellaneous/Formulas.h` 的 <c>xp_in_group_rate</c>：
    /// 1/2/3 人 → 1.0；3 人 → 1.166；4 人 → 1.3；5 人及以上 → 1.4。
    /// 读法：<c>count &lt;= 2</c> 才是 1.0——源码里 0/1/2 三个 case 共用一个分支，3 单独是 1.166。</para>
    /// <para>这条与"分给几个人"是两件事：整组拿的是 <b>pool × rate</b>（不是每人一份 pool），
    /// 所以 5 人组队打一只怪总共只比单人多 40%，而不是五倍。</para>
    /// </summary>
    public static double GroupRateForCount(int count)
    {
        if (count <= 2) return 1.0;
        if (count == 3) return 1.166;
        if (count == 4) return 1.3;
        return 1.4;
    }

    /// <summary>组队分配的逐因子回执（排查"我这队怎么才拿这么点"要靠它，不是只看最终数字）。</summary>
    /// <remarks>刻意是**可变**结构（不是 readonly）：与 <see cref="MonsterXpBreakdown"/> 同形，
    /// 由内核在算的过程中逐项填，宿主与编辑器都只是读。</remarks>
    public struct MonsterXpGroupReceipt
    {
        /// <summary>参与分经验的人数（已按上限截断）。</summary>
        public int Count;

        /// <summary>池子的让分按谁的等级定——AC 用的是"最高的那个还没灰名的队员"。</summary>
        public int PoolLevel;

        /// <summary>等级和（分母）。</summary>
        public long SumLevels;

        /// <summary>人数加成率。</summary>
        public double Rate;

        /// <summary>全队是否被减半（队里有人已经灰名 ⇒ 其余人拿一半）。</summary>
        public bool Halved;

        /// <summary>因灰名拿 0 的人数。</summary>
        public int GrayExcluded;

        /// <summary>整组实际发出的经验总和。</summary>
        public long TotalGranted;
    }

    /// <summary>
    /// 组队击杀的经验分配（AzerothCore <c>KillRewarder</c> 的形状，定基口径仍按决策 D3 取<b>怪物等级</b>）。
    /// <para>四条规则一条都不能少，每一条都是玩家看得见的：</para>
    /// ① 整组拿 <c>pool × rate(count)</c> 这一份，再<b>按等级比例分</b>——低级队员分到更少，
    ///    而不是"每人一份全额"（那是把组队变成刷经验放大器）；
    /// ② 池子的让分按"最高的那个还没灰名的队员"定（AC 的 <c>_maxNotGrayMember</c>），
    ///    全队都灰名则一个人都拿不到；
    /// ③ 已经灰名的队员拿 <b>0</b>；
    /// ④ 队里<b>只要有人</b>灰名，其余人的份额再<b>减半</b>（AC：<c>xp*rate/2 + 1</c>，
    ///    那个 +1 是它防止整除成 0 的写法，照抄以保持形状）。
    /// <para>与 <see cref="ComputeForParticipants"/> 的分工：那个是"每人各算自己的全额"，
    /// 用于不共享经验池的通路（NPC 击杀）；怪物击杀走这个。</para>
    /// </summary>
    /// <param name="cfg">经验配置。</param>
    /// <param name="monsterLevel">怪物等级（定基用它，决策 D3）。</param>
    /// <param name="rank">怪物 rank。</param>
    /// <param name="participants">已排序并截断的<b>在场且活着</b>的参与者（过滤由宿主做：死人不在场是运行期知识，内核不猜）。</param>
    /// <param name="outXp">输出缓冲，长度至少 <c>min(participants.Length, MaxParticipants)</c>。</param>
    /// <param name="receipt">逐因子回执。</param>
    /// <returns>写入条目的数量（缓冲不够时 0）。</returns>
    public static int ComputeGroupShares(
        MonsterXpConfig cfg,
        int monsterLevel,
        MonsterRank rank,
        ReadOnlySpan<MonsterXpParticipant> participants,
        Span<long> outXp,
        out MonsterXpGroupReceipt receipt)
    {
        var n = participants.Length;
        if (n > MaxParticipants) n = MaxParticipants;
        receipt = default;
        receipt.Count = n;
        if (outXp.Length < n) return 0;
        if (n == 0) return 0;

        // ②池子的等级：最高的"没灰名"队员。全组都灰名 ⇒ 一个人都拿不到（AC 的 _maxNotGrayMember 为空）。
        var poolLevel = 0;
        var maxLevel = 0;
        long sumLevels = 0;
        for (var i = 0; i < n; i++)
        {
            var lvl = participants[i].Level;
            sumLevels += lvl;
            if (lvl > maxLevel) maxLevel = lvl;
            if (MonsterDifficultyCore.Of(monsterLevel, lvl, rank) != MonsterDifficulty.Gray && lvl > poolLevel)
            {
                poolLevel = lvl;
            }
        }
        receipt.PoolLevel = poolLevel;
        receipt.SumLevels = sumLevels;
        if (poolLevel == 0)
        {
            // 全队灰名：这一组一只怪的经验池根本不存在。
            for (var i = 0; i < n; i++) outXp[i] = 0;
            receipt.Rate = GroupRateForCount(n);
            receipt.Halved = false;
            receipt.GrayExcluded = n;
            return n;
        }

        var pool = Decompose(cfg, monsterLevel, poolLevel, rank).Final;
        var rate = GroupRateForCount(n);
        receipt.Rate = rate;
        // ④"是不是所有人都没灰名"——AC 用 _maxLevel == _maxNotGrayMember->GetLevel() 判这一点。
        var isFullXp = maxLevel == poolLevel;
        receipt.Halved = !isFullXp;

        var total = 0L;
        var gray = 0;
        for (var i = 0; i < n; i++)
        {
            var lvl = participants[i].Level;
            if (lvl > poolLevel)
            {
                // ③这个人已经灰名（等级比"最高的没灰名的人"还高 ⇒ 对他这只怪是灰的）
                outXp[i] = 0;
                gray++;
                continue;
            }
            var factor = rate * lvl / sumLevels;
            var share = isFullXp
                ? (long)(pool * factor)
                : (long)(pool * factor / 2.0) + 1;      // AC 的 +1：减半后不许因为整除掉到 0
            if (share < 0) share = 0;
            outXp[i] = share;
            total += share;
        }
        receipt.GrayExcluded = gray;
        receipt.TotalGranted = total;
        return n;
    }

    /// <summary>
    /// 按参与优先级就地排序（插入排序，与 <c>MonsterAiCore.SortTargetsByEntityId</c> 同风格：
    /// 小数组更快，且消除不稳定排序带来的截断歧义）。
    /// </summary>
    public static void SortParticipantsByPriority(Span<MonsterXpParticipant> participants)
    {
        for (var i = 1; i < participants.Length; i++)
        {
            var key = participants[i];
            var j = i - 1;
            // 贡献时刻**新**的在前；同刻时 CharacterId 小的在前。
            while (j >= 0 && (participants[j].LastContributionTick < key.LastContributionTick ||
                              (participants[j].LastContributionTick == key.LastContributionTick &&
                               participants[j].CharacterId > key.CharacterId)))
            {
                participants[j + 1] = participants[j];
                j--;
            }
            participants[j + 1] = key;
        }
    }
}

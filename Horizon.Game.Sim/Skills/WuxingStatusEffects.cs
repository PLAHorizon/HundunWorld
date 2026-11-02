using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Skills;

/// <summary>
/// 状态效果的**计划层**（方案 §15）：把一式 <c>skill_effects</c> 读成"该给谁挂什么、挂多久、多久跳一次"，
/// 但**不写状态、不碰存储、不引用任何框架类型**。
/// <para>
/// <b>为什么单开这一层</b>：§15.1 查实，容器（<c>CombatState.ActiveEffects</c>）与读侧聚合
/// （<c>RefreshElementVectors</c>，普攻/技能/受击每条伤害路径都会调）都已经在跑，缺的只有"把效果倒进去"
/// 那一句。而那一句最终要三处分工（Orleans 判、DS 同步、客户端表现）——判据若散在 Grain 里就会长出
/// 三个真相源（本仓在 HP 权威与坐标换轴上各踩过一次同款），所以判据只住这里。
/// </para>
/// <para>
/// <b>与 <see cref="WuxingSkillEffects"/> 的分工</b>：账本记的是"引擎兑现了哪些键"，本类只是计划。
/// 键从 Unresolved 挪进 Applied 的唯一凭据是真有消费方按这里的计划落了账，
/// 所以本类不动账本：写完计划器不等于效果生效。
/// </para>
/// </summary>
public static class WuxingStatusEffects
{
    /// <summary>
    /// 比例类效果（削甲 / 脆弱 / 承伤下降）在**数据没配 <c>durationSec</c>** 时的引擎时长。
    /// <para>
    /// 为什么是常量而不是"留空当作永久"：现容器的 <c>IsExpired</c> 把 <c>Duration &lt;= 0</c> 判成永久，
    /// 所以"没配"若直通下去就是"永久削甲"——那不是保守，那是最激进的读法。
    /// 实测 91 行里 <c>enemyArmorDown</c> 与 <c>vulnerability</c> 各只有 1 行、且都没配时长，
    /// 正是这一档。<see cref="PlannedStatus.DurationFromData"/> 会把"这个 6 秒是引擎给的"标出来，
    /// 接线与排错时都能看见出处。
    /// </para>
    /// </summary>
    public const double DefaultProportionalDurationSeconds = 6d;

    /// <summary>DoT 默认节奏（每 1 秒一跳）。数据里 <c>dotRatio</c> 那 13 行都只配了 <c>dotSec</c>（总时长），
    /// 没有配间隔；间隔属于"引擎怎么切这 4 秒"，不该逼内容作者逐式重写一遍。有 <c>tickEvery</c>/<c>tickSec</c> 时以数据为准。</summary>
    public const double DefaultDotTickSeconds = 1d;

    /// <summary>计划里的效果类别。每个值都必须有真消费方，否则它就是下一个"没人读的字段"。</summary>
    public enum StatusKind
    {
        /// <summary>目标护甲/防御下降：写成受击方的负防御向量，走既有聚合。</summary>
        ArmorDown,
        /// <summary>目标受到伤害放大：同为负防御向量，与 <see cref="ArmorDown"/> 分开是因为数据里
        /// 两者语义不同（一个动护甲、一个动承伤），合并会在种子改数值时改错列。</summary>
        Vulnerability,
        /// <summary>自身受到伤害下降：正防御向量。</summary>
        DamageTakenDown,
        /// <summary>定身：既不能施放也不能移动。</summary>
        Stun,
        /// <summary>禁足：只拦移动，不拦施放。</summary>
        Root,
        /// <summary>减速：按 <c>slowPct</c> 缩放移速。</summary>
        Slow,
        /// <summary>持续伤害：每 <c>TicksEverySec</c> 秒结算一次 <c>DamageRatio × 施法者攻击力</c>。</summary>
        Dot,
    }

    /// <summary>一条计划好的状态。幅度与时长都**必须为正**，否则这一种根本不会被计划出来。</summary>
    /// <param name="Kind">类别。</param>
    /// <param name="SourceKey">来源数据键，回显与排错要它点名是哪条数据。</param>
    /// <param name="ToTarget">挂在被命中方身上（true）还是挂在施法者自己身上（false）。</param>
    /// <param name="MagnitudePct">比例类幅度（0.3 = 30%）；纯时长类（控制）为 0。</param>
    /// <param name="DurationSec">时长（秒），恒 &gt; 0。</param>
    /// <param name="DurationFromData">时长是否真来自数据。<c>false</c> 表示用的是引擎常量。</param>
    /// <param name="TicksEverySec">仅 <see cref="StatusKind.Dot"/>：每跳间隔。</param>
    /// <param name="DamageRatio">仅 <see cref="StatusKind.Dot"/>：每跳占施法者攻击力的比例。</param>
    public readonly record struct PlannedStatus(
        StatusKind Kind,
        string SourceKey,
        bool ToTarget,
        double MagnitudePct,
        double DurationSec,
        bool DurationFromData,
        double TicksEverySec,
        double DamageRatio);

    /// <summary>解出这一式**当前支持的**状态计划；顺序稳定，便于三端与测试逐条对账。</summary>
    public static List<PlannedStatus> Plan(string? effectsJson)
    {
        var list = new List<PlannedStatus>();
        if (string.IsNullOrWhiteSpace(effectsJson)) return list;

        // 控制类：秒数本身就是时长，写在键里（stunSec / rootSec），不借 durationSec。
        AddControl(list, StatusKind.Stun, "stunSec", effectsJson);
        AddControl(list, StatusKind.Root, "rootSec", effectsJson);

        double? slowPct = WuxingSkillEffects.ReadNumber(effectsJson, "slowPct");
        if (slowPct is > 0d and < 1d)     // 只有真分数才配当比例；1.5 那种写法不是"减速 150%"
        {
            double? slowSec = WuxingSkillEffects.ReadNumber(effectsJson, "slowSec");
            list.Add(new PlannedStatus(StatusKind.Slow, "slowPct", ToTarget: true,
                slowPct.Value, slowSec ?? DefaultProportionalDurationSeconds,
                DurationFromData: slowSec is > 0d, 0d, 0d));
        }

        AddProportional(list, StatusKind.ArmorDown, "enemyArmorDown", effectsJson, toTarget: true);
        AddProportional(list, StatusKind.Vulnerability, "vulnerability", effectsJson, toTarget: true);
        AddProportional(list, StatusKind.DamageTakenDown, "damageTakenDown", effectsJson, toTarget: false);

        double? dotRatio = WuxingSkillEffects.ReadNumber(effectsJson, "dotRatio");
        double? dotSec = WuxingSkillEffects.ReadNumber(effectsJson, "dotSec");
        if (dotRatio is > 0d && dotSec is > 0d)
        {
            double? tick = WuxingSkillEffects.ReadNumber(effectsJson, "tickEvery")
                           ?? WuxingSkillEffects.ReadNumber(effectsJson, "tickSec");
            list.Add(new PlannedStatus(StatusKind.Dot, "dotRatio", ToTarget: true, 0d,
                dotSec.Value, DurationFromData: true,
                tick is > 0d ? tick.Value : DefaultDotTickSeconds, dotRatio.Value));
        }

        return list;
    }

    /// <summary>
    /// 等级收益（#30 已定："先给等级一点真收益"）：<c>level_effects</c> 里配 <c>{"damagePerLevel":0.02}</c>
    /// 表示**每多一重 +2% 最终伤害**（第 1 重为基准，不加）。
    /// <para>
    /// <b>键必须在顶层</b>：读数的 <see cref="WuxingSkillEffects.ReadNumber"/> 只看根对象，
    /// 写成 <c>{"perLevel":{"damagePerLevel":0.02}}</c> 会安静地读成"没配"→ 每级 +0%。
    /// 契约与读取器必须同形，这条已由测试钉住。
    /// </para>
    /// <para>
    /// <b>为什么要补这个读者</b>：今天 <c>skill.Level</c> 在伤害结算里一个读者都没有
    /// （91 行 <c>level_effects</c> 全空），于是"练满一式"的净效果反而是更费蓝；
    /// 而 §6.2 要求"源式练满"才能演化，那条前置就变成纯税。
    /// </para>
    /// </summary>
    public static double ReadLevelDamagePct(string? levelEffectsJson, int level)
    {
        if (level <= 1) return 0d;
        double? perLevel = WuxingSkillEffects.ReadNumber(levelEffectsJson, "damagePerLevel");
        return perLevel is > 0d ? perLevel.Value * (level - 1) : 0d;
    }

    private static void AddControl(List<PlannedStatus> list, StatusKind kind, string key, string? json)
    {
        double? sec = WuxingSkillEffects.ReadNumber(json, key);
        if (sec is > 0d)
            list.Add(new PlannedStatus(kind, key, ToTarget: true, 0d, sec.Value, true, 0d, 0d));
    }

    /// <summary>比例类：幅度缺失或非正一律不计划（"没配"不是"配了 0"）；时长缺了走引擎常量并标明出处。</summary>
    private static void AddProportional(List<PlannedStatus> list, StatusKind kind, string key,
        string? json, bool toTarget)
    {
        double? magnitude = WuxingSkillEffects.ReadNumber(json, key);
        if (magnitude is not > 0d) return;

        double? duration = WuxingSkillEffects.ReadNumber(json, "durationSec");
        list.Add(new PlannedStatus(kind, key, toTarget, magnitude.Value,
            duration ?? DefaultProportionalDurationSeconds,
            DurationFromData: duration is > 0d, 0d, 0d));
    }
}

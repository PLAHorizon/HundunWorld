using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Horizon.Game.Sim.Skills;

/// <summary>
/// 技能效果字典（<c>Game_HunduShijie_SkillTemplate.skill_effects</c> 那列 JSON）的**键表与结算状态**。
/// <para>
/// 为什么要专门立这个类：91 式各自带一份效果 JSON，键位共 69 个（控制 / DOT / 治疗 / 护盾 / 反射 / 位移…）。
/// 而战斗结算只认真实装过的少数几键，其余**躺在数据里没人读**——症状不是报错，是
/// "技能树与详情写着'定身 2 秒'，游戏里那点根本没发生"。这类"数据有、行为无"的落差
/// 在客户端还会被显示成事实，玩家按它做决策，比缺功能更糟。
/// </para>
/// <para>
/// 这个类的价值不是把效果实现掉，而是把"哪些真的结算、哪些只是描述"**变成可枚举的清单**：
/// 新增一键必须在这里登记；登记成 <see cref="EffectRole.Unresolved"/> 就等于公开挂账，
/// 再由 <c>WuxingSkillEffectContractTests</c> 用种子数据反查，漏登记会红。
/// </para>
/// </summary>
public enum EffectRole
{
    /// <summary>战斗/成长引擎**当前真的会结算**的键。</summary>
    Applied,
    /// <summary>习得门槛类：由 <c>WuxingDynamicGates</c> / <c>WuxingEvolveRules</c> 消费。</summary>
    Gate,
    /// <summary>表现类：客户端按它画形状与提示，不参与数值结算。</summary>
    Presentation,
    /// <summary>公开挂账：数据里有、引擎不结算。实现一条就把它移到 <see cref="Applied"/>。</summary>
    Unresolved,
}

/// <summary>
/// 效果键表。分类依据 2026-10-06 对全仓的读者普查（服务端 <c>Horizon.Game.Core</c> /
/// <c>Horizon.Orleans.Grains</c> / <c>Horizon.Game.Sim</c> / <c>Horizon.DsHost</c>
/// 与客户端 <c>HundunWorld/Source/Game</c>），逐个键名按单词边界匹配。
/// </summary>
public static class WuxingSkillEffects
{
    /// <summary>
    /// 键 → 当前角色。普查（2026-10-06）当时的结论是：69 键里引擎真的结算的只有 1 个（<c>pen</c>），
    /// 门槛类 4 个，表现类 6 个，其余 <b>59 个无人结算</b>。<b>G26/G27 起</b>：<c>healPct</c> 与 <c>hpCost</c> 已真结算
    /// （Applied=3、挂账=57）。别把这张表当"应当如此"，它是"此刻确实如此"的账，每实现一条就往 <see cref="EffectRole.Applied"/> 挪一行。
    /// </summary>
    private static readonly Dictionary<string, EffectRole> Roles = new(StringComparer.Ordinal)
    {
        // 引擎结算
        ["pen"] = EffectRole.Applied,                       // 破甲/穿透：CombatGrain 的伤害公式在读
        ["healPct"] = EffectRole.Applied,                   // 占生命上限的回血：SkillGrain.CastSkillAsync 真调 RequestHpChangeAsync
        ["hpCost"] = EffectRole.Applied,                   // 占生命上限的代价：同一处，血不够承代价时直接拒绝施放（G27）

        // 习得门槛（§14）——只登记**确实出现在 skill_effects 里**的键：
        // favorNeed / materials / attrNeed / evolveFrom / consume / pair 属于 learn_cost 那一列的词表，
        // 由 WuxingEvolveRules 与 WuxingDynamicGates 各自登记，混进来只会留下永远命中不到的孤儿。
        ["grade"] = EffectRole.Gate,
        ["gradeStage"] = EffectRole.Gate,
        ["loopNeed"] = EffectRole.Gate,
        ["needPairsCompleted"] = EffectRole.Gate,

        // 客户端表现（形状与位移提示；服务端不据此结算多重命中）
        ["rangeType"] = EffectRole.Presentation,
        ["aoeRadius"] = EffectRole.Presentation,
        ["maxTargets"] = EffectRole.Presentation,
        ["spread"] = EffectRole.Presentation,
        ["dash"] = EffectRole.Presentation,
        ["stack"] = EffectRole.Presentation,              // 效果图标名（纯文案）

        // —— 以下全是"数据说了、引擎没做"的公开挂账 ——
        ["buffRatio"] = EffectRole.Unresolved,
        ["blockBonus"] = EffectRole.Unresolved,
        ["cheatDeath"] = EffectRole.Unresolved,
        ["damageTakenDown"] = EffectRole.Unresolved,
        ["deferBurst"] = EffectRole.Unresolved,
        ["detonate"] = EffectRole.Unresolved,
        ["detonateRadius"] = EffectRole.Unresolved,
        ["disarmSec"] = EffectRole.Unresolved,
        ["dispel"] = EffectRole.Unresolved,
        ["dispelControl"] = EffectRole.Unresolved,
        ["dispelCount"] = EffectRole.Unresolved,
        ["dispelSec"] = EffectRole.Unresolved,
        ["dotRatio"] = EffectRole.Unresolved,
        ["dotSec"] = EffectRole.Unresolved,
        ["dotTempo"] = EffectRole.Unresolved,
        ["durationSec"] = EffectRole.Unresolved,
        ["enemyArmorDown"] = EffectRole.Unresolved,
        ["enemyMiss"] = EffectRole.Unresolved,
        ["energyPenalty"] = EffectRole.Unresolved,
        ["energySave"] = EffectRole.Unresolved,
        ["freeRiposte"] = EffectRole.Unresolved,
        ["groundSec"] = EffectRole.Unresolved,
        ["guaranteeGenerateSec"] = EffectRole.Unresolved,
        // healPct 已在上面登记为 Applied（G26）：字典初始化器里同名键是"后者覆盖前者"且不报错，
        // 保留这一行会让账本悄悄退回 Unresolved——这条重复就是被契约测试的 AppliedKeys 精确断言抓出来的。
        ["healTakenBonus"] = EffectRole.Unresolved,
        ["healTakenDown"] = EffectRole.Unresolved,
        // hpCost 已在上面登记为 Applied（G27）：字典初始化器里同名键"后者静默覆盖前者且不报错"，
        // 这里再留一行就会把账本退回"没实现"。WuxingSkillEffectContractTests 用键总数钉住这种重复。
        ["immuneBreak"] = EffectRole.Unresolved,
        ["immuneSec"] = EffectRole.Unresolved,
        ["knockback"] = EffectRole.Unresolved,
        ["loopScale"] = EffectRole.Unresolved,
        ["loopWindowBonus"] = EffectRole.Unresolved,
        ["onGenerate"] = EffectRole.Unresolved,
        ["overhealToRoot"] = EffectRole.Unresolved,
        ["perStack"] = EffectRole.Unresolved,
        ["pierce"] = EffectRole.Unresolved,
        ["pullSec"] = EffectRole.Unresolved,
        ["reflect"] = EffectRole.Unresolved,
        ["reflectControl"] = EffectRole.Unresolved,
        ["riposteOnShieldBreak"] = EffectRole.Unresolved,
        ["rootSec"] = EffectRole.Unresolved,
        ["shieldBurnBack"] = EffectRole.Unresolved,
        ["shieldRatio"] = EffectRole.Unresolved,
        ["slowPct"] = EffectRole.Unresolved,
        ["slowSec"] = EffectRole.Unresolved,
        ["splashPct"] = EffectRole.Unresolved,
        ["stackMax"] = EffectRole.Unresolved,
        ["stealBuff"] = EffectRole.Unresolved,
        ["stealPerSec"] = EffectRole.Unresolved,
        ["stunSec"] = EffectRole.Unresolved,
        ["tauntSec"] = EffectRole.Unresolved,
        ["tickEvery"] = EffectRole.Unresolved,
        ["tickSec"] = EffectRole.Unresolved,
        ["toAllies"] = EffectRole.Unresolved,
        ["undispellable"] = EffectRole.Unresolved,
        ["vsDot"] = EffectRole.Unresolved,
        ["vsShield"] = EffectRole.Unresolved,
        ["vulnerability"] = EffectRole.Unresolved,
    };

    /// <summary>已知键集合（拼写守卫用）。</summary>
    public static IReadOnlyCollection<string> KnownKeys => Roles.Keys;

    /// <summary>某个键的当前角色；未登记返回 null（调用方据此判定"出现了新键"）。</summary>
    public static EffectRole? RoleOf(string key) => Roles.TryGetValue(key, out var role) ? role : null;

    /// <summary>引擎当前真的结算的键（<see cref="EffectRole.Applied"/>）。</summary>
    public static IReadOnlyCollection<string> AppliedKeys => Keys(EffectRole.Applied);

    /// <summary>公开挂账、尚未实现的键（<see cref="EffectRole.Unresolved"/>）。</summary>
    public static IReadOnlyCollection<string> UnresolvedKeys => Keys(EffectRole.Unresolved);

    private static IReadOnlyCollection<string> Keys(EffectRole role) =>
        Roles.Where(kv => kv.Value == role).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 解析一行效果 JSON，返回它用到的键；null/空串 → 空表。
    /// 只做形状解析、不解释数值：数值语义要等对应效果真的实现时才定，
    /// 现在猜出来的口径会比没有更误导。
    /// </summary>
    public static IReadOnlyList<string> KeysIn(string? skillEffectsJson)
    {
        if (string.IsNullOrWhiteSpace(skillEffectsJson)) return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(skillEffectsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();

            var keys = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
                keys.Add(property.Name);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        catch (JsonException)
        {
            // 数据坏了不该静默当成"没有效果"：调用方（契约测试）会看到空表并在那里报错
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 一行效果 JSON 里<b>未登记</b>的键——契约测试用它把"新增了键却没进表"变成红。
    /// </summary>
    public static IReadOnlyList<string> UnknownKeysIn(string? skillEffectsJson) =>
        KeysIn(skillEffectsJson).Where(k => !Roles.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 读一个数值型效果键。缺失、非数值、JSON 坏了都返回 null —— 调用方据此<b>什么都不做</b>，
    /// 不许在这里给默认值：效果数值是数据（种子/策划表）的职责，实现侧猜一个数会把"没配"变成"配了个错的"。
    /// </summary>
    public static double? ReadNumber(string? skillEffectsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(skillEffectsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(skillEffectsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(key, out var value)) return null;
            return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 一行效果 JSON 里引擎<b>不会兑现</b>的键：只算 <see cref="EffectRole.Unresolved"/> 与未登记的键，
    /// 表现类（形状/位移提示）与门槛类都不算缺口——它们本来就被各自的主人兑现了。
    /// UI 若要避免把"定身 2 秒"写成事实，就用这个函数决定哪些键只能当风味描述。
    /// </summary>
    public static IReadOnlyList<string> NotAppliedKeysIn(string? skillEffectsJson) =>
        KeysIn(skillEffectsJson)
            .Where(k => !Roles.TryGetValue(k, out var role) || role == EffectRole.Unresolved)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
}

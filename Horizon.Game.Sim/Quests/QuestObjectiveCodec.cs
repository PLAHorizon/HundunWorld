using System;
using System.Globalization;

namespace Horizon.Game.Sim.Quests;

/// <summary>任务完成条件的类别。</summary>
public enum QuestObjectiveKind : byte
{
    /// <summary>击杀指定怪物模板。</summary>
    KillMonster = 1,
    /// <summary>击杀某五行域的任意怪（沿用既有 <c>kill_domain:N</c> 口径）。</summary>
    KillDomain = 2,
    /// <summary>与指定 NPC 对话。</summary>
    TalkNpc = 3,
    /// <summary>采集/持有指定物品。</summary>
    CollectItem = 4,
    /// <summary>使用指定物品。</summary>
    UseItem = 5,
    /// <summary>到达指定区域。</summary>
    ReachZone = 6,
    /// <summary>与场景物件交互。</summary>
    InteractProp = 7,
}

/// <summary>
/// 一条已解码的完成条件（模板串的结构化视图）。
/// </summary>
/// <param name="Kind">类别。</param>
/// <param name="TemplateId">对象模板 id；<see cref="QuestObjectiveKind.KillDomain"/> 时是五行域号，
/// <see cref="QuestObjectiveKind.ReachZone"/> 时是区域 id。</param>
public readonly record struct QuestObjectiveSpec(QuestObjectiveKind Kind, int TemplateId);

/// <summary>
/// 目标类型串的编解码：库里/线上只存一个字符串（<c>objectives</c> JSON 的 <c>type</c> 字段），
/// 编辑器与校验器看到的是结构化视图，两侧都从这一份语法出发，避免"两套口径各改一半"。
/// <para>
/// 语法：<c>kill_domain:&lt;1..5&gt;</c>、<c>kill_monster:&lt;模板id&gt;</c>、<c>talk_npc:&lt;模板id&gt;</c>、
/// <c>collect_item:&lt;物品id&gt;</c>、<c>use_item:&lt;物品id&gt;</c>、<c>reach_zone:&lt;区域id&gt;</c>、
/// <c>interact_prop:&lt;模板id&gt;</c>。
/// </para>
/// <para>
/// 注意 <c>kill_domain</c> 与既有的 <c>KillObjectiveTypeMatcher</c>、012 种子完全一致，不能改名；
/// 裸 <c>kill</c>（任意击杀）是 matcher 里的兜底词，属于历史宽容，新草稿不再产出它。
/// </para>
/// </summary>
public static class QuestObjectiveCodec
{
    /// <summary>既有域击杀前缀（与 <c>KillObjectiveTypeMatcher.DomainPrefixToken</c> 同值）。</summary>
    public const string KillDomainToken = "kill_domain";

    /// <summary>把结构化条件编成类型串；越界返回 null 交由调用方报错。</summary>
    public static string? Encode(QuestObjectiveSpec spec)
    {
        var verb = VerbOf(spec.Kind);
        if (verb == null) return null;
        if (spec.TemplateId <= 0) return null;
        if (spec.Kind == QuestObjectiveKind.KillDomain && (spec.TemplateId < 1 || spec.TemplateId > 5)) return null;
        return verb + ":" + spec.TemplateId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>解析类型串；不认识时返回 false。历史裸 <c>kill</c> 归一为"任意击杀"。</summary>
    public static bool TryDecode(string? type, out QuestObjectiveSpec spec)
    {
        spec = default;
        var text = (type ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        var colon = text.IndexOf(':');
        var head = (colon < 0 ? text : text.Substring(0, colon)).Trim().ToLowerInvariant();

        if (head == KillDomainToken)
        {
            if (colon < 0 || !int.TryParse(Tail(text, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var domain))
                return false;
            spec = new QuestObjectiveSpec(QuestObjectiveKind.KillDomain, domain);
            return domain >= 1 && domain <= 5;
        }

        var kind = head switch
        {
            "kill_monster" => QuestObjectiveKind.KillMonster,
            "talk_npc" => QuestObjectiveKind.TalkNpc,
            "collect_item" => QuestObjectiveKind.CollectItem,
            "use_item" => QuestObjectiveKind.UseItem,
            "reach_zone" => QuestObjectiveKind.ReachZone,
            "interact_prop" => QuestObjectiveKind.InteractProp,
            // 兼容既有数据：任何含击杀语义词但没有可解析对象的历史串。
            "kill" or "hunt" => QuestObjectiveKind.KillMonster,
            _ => (QuestObjectiveKind?)null,
        };
        if (kind == null) return false;

        var id = colon < 0 ? 0 : ParseInt(Tail(text, colon));
        // 历史串 "kill"（无对象）按"任意击杀"入库：模板 id 留 0，matcher 仍按关键词放行。
        spec = new QuestObjectiveSpec(kind.Value, id);
        return true;
    }

    /// <summary>类别 → 串动词；无对应语法返回 null。</summary>
    public static string? VerbOf(QuestObjectiveKind kind) => kind switch
    {
        QuestObjectiveKind.KillMonster => "kill_monster",
        QuestObjectiveKind.KillDomain => KillDomainToken,
        QuestObjectiveKind.TalkNpc => "talk_npc",
        QuestObjectiveKind.CollectItem => "collect_item",
        QuestObjectiveKind.UseItem => "use_item",
        QuestObjectiveKind.ReachZone => "reach_zone",
        QuestObjectiveKind.InteractProp => "interact_prop",
        _ => null,
    };

    /// <summary>
    /// 该类别当前是否有**事件源**（服务端真的会推它前进）。
    /// <para>没有源的条件接了任务就永远推不动，所以校验器把它判成错误而不是警告：
    /// 与其让玩家拿一个卡死的任务，不如让编辑者在入库前就看见它。</para>
    /// </summary>
    public static bool HasProducer(QuestObjectiveKind kind) => kind switch
    {
        QuestObjectiveKind.KillMonster => true,
        QuestObjectiveKind.KillDomain => true,
        // 对话/物件交互的权威源在 §11 落地后才存在：DS 把 (entityId → 模板 id) 上行给网关注册表，
        // 网关按"实体存在 + 类别可交互 + 离玩家够近"三条核对后才推进（QuestInteractionAuthority）。
        // 客户端自报模板 id 的那条路始终不采信——事件源是服务端判定，不是客户端声明。
        QuestObjectiveKind.TalkNpc => true,
        QuestObjectiveKind.InteractProp => true,
        QuestObjectiveKind.CollectItem => false,
        QuestObjectiveKind.UseItem => false,
        QuestObjectiveKind.ReachZone => false,
        _ => false,
    };

    /// <summary>无事件源的类别清单（给校验信息与编辑器提示复用，避免两处各写一遍）。</summary>
    public static QuestObjectiveKind[] KindsWithoutProducer()
        => new[]
        {
            QuestObjectiveKind.CollectItem,
            QuestObjectiveKind.UseItem,
            QuestObjectiveKind.ReachZone,
        };

    private static string Tail(string text, int colon) => text.Substring(colon + 1).Trim();

    private static int ParseInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

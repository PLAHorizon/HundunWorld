using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.Game.Sim.Quests;

/// <summary>任务类型（与 <c>quest_type</c> 列既有语义同序，不可重排）。</summary>
public enum QuestTypeCode : byte
{
    /// <summary>主线。</summary>
    Main = 0,
    /// <summary>支线。</summary>
    Side = 1,
    /// <summary>日常。</summary>
    Daily = 2,
    /// <summary>周常。</summary>
    Weekly = 3,
}

/// <summary>任务发布状态（<c>publish_state</c> 列）。与 <c>IsValid</c> 分开：后者是"行是否作废"，前者是"玩法是否开放"。</summary>
public enum QuestPublishState : byte
{
    /// <summary>下线：不在 NPC 的可接列表里出现。</summary>
    Offline = 0,
    /// <summary>已发布：实时对符合门槛的玩家开放。</summary>
    Online = 1,
}

/// <summary>草稿里的实体引用（发布者在 NPC/物品/物件之间选）。</summary>
public sealed class QuestEntityRefDraft
{
    /// <summary>类别串：<c>npc</c> / <c>monster</c> / <c>item</c> / <c>prop</c> / <c>zone</c>。</summary>
    public string Kind { get; set; } = "npc";
    /// <summary>模板 id。</summary>
    public int TemplateId { get; set; }
    /// <summary>限定区域，0 = 不限。</summary>
    public int ZoneId { get; set; }
    /// <summary>限定生成点，0 = 不限。</summary>
    public int SpawnPointId { get; set; }

    /// <summary>转成运行时引用（类别串不认识时落到 Npc，由校验器另发一条错误）。</summary>
    public QuestEntityRef ToRef()
    {
        QuestEntityRef.TryParseKind(Kind, out var kind);
        return new QuestEntityRef(kind, TemplateId, ZoneId, SpawnPointId);
    }

    /// <summary>从运行时引用还原草稿。</summary>
    public static QuestEntityRefDraft FromRef(in QuestEntityRef reference) => new QuestEntityRefDraft
    {
        Kind = QuestEntityRef.KindToToken(reference.Kind),
        TemplateId = reference.TemplateId,
        ZoneId = reference.ZoneId,
        SpawnPointId = reference.SpawnPointId,
    };

    /// <summary>是否等同于"没填"。</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Kind) && TemplateId <= 0;
}

/// <summary>草稿里的一条完成条件。</summary>
public sealed class QuestObjectiveDraft
{
    /// <summary>类别串（与 <see cref="QuestObjectiveCodec.VerbOf"/> 同名）。</summary>
    public string Kind { get; set; } = "kill_monster";
    /// <summary>对象模板 id；<c>kill_domain</c> 时为五行域号、<c>reach_zone</c> 时为区域 id。</summary>
    public int TemplateId { get; set; }
    /// <summary>需要完成的次数。</summary>
    public int Count { get; set; } = 1;
    /// <summary>展示给玩家的描述。</summary>
    public string Description { get; set; } = "";
    /// <summary>是否必须（当前实现按"全部必须"结算，可选位留给后续多目标分支）。</summary>
    public bool Mandatory { get; set; } = true;

    /// <summary>取类别枚举（解析失败返回 null）。</summary>
    public QuestObjectiveKind? KindOrNone()
    {
        var parsed = ParseVerb(Kind);
        if (parsed != null) return parsed;
        return System.Enum.TryParse(ToEnumName(Kind), true, out QuestObjectiveKind byName) ? byName : null;
    }

    /// <summary>编成入库的类型串；无法编码时返回 null。</summary>
    public string? ToTypeString()
    {
        var kind = KindOrNone();
        return kind == null ? null : QuestObjectiveCodec.Encode(new QuestObjectiveSpec(kind.Value, TemplateId));
    }

    /// <summary>从库里的类型串还原草稿条目。</summary>
    public static QuestObjectiveDraft FromTypeString(string? type, int count, string description)
    {
        var draft = new QuestObjectiveDraft { Count = count, Description = description ?? "" };
        if (!QuestObjectiveCodec.TryDecode(type, out var spec))
        {
            draft.Kind = (type ?? "kill_monster").Trim();
            return draft;
        }
        draft.Kind = QuestObjectiveCodec.VerbOf(spec.Kind) ?? spec.Kind.ToString().ToLowerInvariant();
        draft.TemplateId = spec.TemplateId;
        return draft;
    }

    private static QuestObjectiveKind? ParseVerb(string? token)
    {
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "kill_monster": return QuestObjectiveKind.KillMonster;
            case "kill_domain": return QuestObjectiveKind.KillDomain;
            case "talk_npc": return QuestObjectiveKind.TalkNpc;
            case "collect_item": return QuestObjectiveKind.CollectItem;
            case "use_item": return QuestObjectiveKind.UseItem;
            case "reach_zone": return QuestObjectiveKind.ReachZone;
            case "interact_prop": return QuestObjectiveKind.InteractProp;
            default: return null;
        }
    }

    private static string ToEnumName(string? token)
    {
        var trimmed = (token ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "kill_monster";
        return trimmed.Replace("_", string.Empty);
    }
}

/// <summary>
/// 任务草稿：编辑器与 CLI 之间唯一的交换格式，也是 <c>Game_HunduShijie_QuestTemplate</c> 一行的可读视图。
/// </summary>
public sealed class QuestDraft
{
    /// <summary>任务模板 id。</summary>
    public int QuestId { get; set; }
    /// <summary>任务名。</summary>
    public string QuestName { get; set; } = "";
    /// <summary>任务描述。</summary>
    public string Description { get; set; } = "";
    /// <summary>0主线 1支线 2日常 3周常。</summary>
    public int QuestType { get; set; } = (int)QuestTypeCode.Side;
    /// <summary>接取所需等级。</summary>
    public int LevelRequire { get; set; }
    /// <summary>归属五行域 1金…5土，0=通用。</summary>
    public int WuxingDomain { get; set; }
    /// <summary>可重复完成次数，0=一次性。</summary>
    public int RepeatLimit { get; set; }
    /// <summary>发布状态。</summary>
    public int PublishState { get; set; } = (int)QuestPublishState.Offline;
    /// <summary>行是否有效（对应 <c>IsValid</c>）。</summary>
    public bool IsValid { get; set; } = true;

    /// <summary>发布者（NPC/物品/物件）。</summary>
    public QuestEntityRefDraft Giver { get; set; } = new QuestEntityRefDraft();
    /// <summary>交付者；为 null 时按"与发布者同一对象"处理。</summary>
    public QuestEntityRefDraft? TurnIn { get; set; }

    /// <summary>前置任务 id 列表。</summary>
    public List<int> PrerequisiteQuests { get; set; } = new List<int>();

    /// <summary>完成条件。</summary>
    public List<QuestObjectiveDraft> Objectives { get; set; } = new List<QuestObjectiveDraft>();

    /// <summary>奖励。</summary>
    public QuestRewardSpec Rewards { get; set; } = new QuestRewardSpec();

    /// <summary>内容版本号：每次入库自增，客户端据此判断"我手上的任务描述是不是旧的"。</summary>
    public int ContentVersion { get; set; }

    /// <summary>序列化选项：字段用驼峰、忽略 null，编辑器与 CLI 两侧同一份约定。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>转 JSON（编辑器落盘/发 CLI 用）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>从 JSON 解析；语法错时返回 null 并给出异常消息。</summary>
    public static QuestDraft? TryParse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "空的草稿 JSON。"; return null; }
        try
        {
            return JsonSerializer.Deserialize<QuestDraft>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = "草稿 JSON 解析失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>从 JSON 解析（忽略错误，测试与内部流程用）。</summary>
    public static QuestDraft? Parse(string? json) => TryParse(json, out _);

    /// <summary>把条件列表编成 <c>objectives</c> 列的 JSON 数组（与 012 种子同形：<c>{"type":..,"desc":..,"count":..}</c>）。</summary>
    public string BuildObjectivesJson()
    {
        var builder = new System.Text.StringBuilder(256);
        builder.Append('[');
        var appended = false;
        foreach (var objective in Objectives)
        {
            if (objective == null) continue;
            var type = objective.ToTypeString();
            if (type == null) continue;

            if (appended) builder.Append(',');
            builder.Append("{\"type\":").Append(JsonValue(type))
                   .Append(",\"desc\":").Append(JsonValue(objective.Description ?? ""))
                   .Append(",\"count\":").Append(objective.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                   .Append(",\"mandatory\":").Append(objective.Mandatory ? "true" : "false")
                   .Append('}');
            appended = true;
        }
        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>读 <c>objectives</c> 列 → 草稿条件列表。</summary>
    public static List<QuestObjectiveDraft> ParseObjectives(string? json)
    {
        var list = new List<QuestObjectiveDraft>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var type = ReadString(element, "type");
                var desc = ReadString(element, "desc") ?? ReadString(element, "description") ?? "";
                var count = ReadInt(element, "count");
                var mandatory = !element.TryGetProperty("mandatory", out var flag) || flag.ValueKind != JsonValueKind.False;
                var parsed = QuestObjectiveDraft.FromTypeString(type, count, desc);
                parsed.Mandatory = mandatory;
                list.Add(parsed);
            }
        }
        catch (JsonException)
        {
            // 坏数据交给校验器点名，这里不猜。
        }
        return list;
    }

    /// <summary>读 <c>prerequisite_quests</c> 列（逗号分隔）。</summary>
    public static List<int> ParsePrerequisites(string? csv)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var part in csv.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 && !list.Contains(id))
                list.Add(id);
        }
        return list;
    }

    /// <summary>写 <c>prerequisite_quests</c> 列。</summary>
    public string BuildPrerequisitesCsv()
        => string.Join(",", PrerequisiteQuests.ToArray());

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : 0;

    private static string JsonValue(string text) => JsonSerializer.Serialize(text);
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Horizon.Game.Sim.Quests;

/// <summary>一条物品奖励。</summary>
public sealed class QuestRewardItem
{
    /// <summary>物品模板 id（覆盖全部物品：装备/丹药/材料/宝石/任务物品/秘籍 800000+技能id）。</summary>
    public int TemplateId { get; set; }
    /// <summary>数量，必须 &gt; 0。</summary>
    public int Count { get; set; }
}

/// <summary>一条五行域好感奖励。</summary>
public sealed class QuestRewardFavor
{
    /// <summary>五行域 1金 2木 3水 4火 5土。</summary>
    public int Domain { get; set; }
    /// <summary>好感点数。</summary>
    public int Points { get; set; }
}

/// <summary>
/// 任务奖励的结构化视图，与库里 <c>rewards</c> 列的那串 JSON 双向可逆。
/// <para>
/// 线上键约定（沿用既有消费方，不可改名）：<c>item:&lt;物品模板id&gt;</c> = 数量、
/// <c>favor:&lt;域&gt;</c> = 点数、<c>exp</c>、<c>gold</c>。
/// 消费端只认 <b>数字</b> 值（<c>QuestGrain.ParseRewards</c> 对字符串值直接丢弃），
/// 所以编码侧永远写数字。
/// </para>
/// </summary>
public sealed class QuestRewardSpec
{
    /// <summary>经验。</summary>
    public long Exp { get; set; }
    /// <summary>铜币（金币）。</summary>
    public long Gold { get; set; }
    /// <summary>物品奖励。</summary>
    public List<QuestRewardItem> Items { get; set; } = new List<QuestRewardItem>();
    /// <summary>五行域好感奖励。</summary>
    public List<QuestRewardFavor> Favor { get; set; } = new List<QuestRewardFavor>();

    /// <summary>是否什么都没有（发布上线的任务不该长这样）。计算属性，不进协议。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Exp <= 0 && Gold <= 0 && Items.Count == 0 && Favor.Count == 0;

    /// <summary>序列化为 <c>rewards</c> 列的内容。</summary>
    public string ToJson()
    {
        var builder = new StringBuilder(128);
        builder.Append('{');
        var appended = false;

        if (Exp > 0) { AppendPair(builder, ref appended, "exp", Exp); }
        if (Gold > 0) { AppendPair(builder, ref appended, "gold", Gold); }

        foreach (var item in Items)
        {
            if (item == null || item.TemplateId <= 0 || item.Count <= 0) continue;
            AppendPair(builder, ref appended, "item:" + item.TemplateId.ToString(CultureInfo.InvariantCulture), item.Count);
        }

        foreach (var favor in Favor)
        {
            if (favor == null || favor.Domain < 1 || favor.Domain > 5 || favor.Points <= 0) continue;
            AppendPair(builder, ref appended, "favor:" + favor.Domain.ToString(CultureInfo.InvariantCulture), favor.Points);
        }

        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// 解析 <c>rewards</c> 列；坏键只跳过不抛异常。
    /// 一份写坏的奖励不该让整条任务读不出来——但要在校验器里被点名（见 <see cref="ParseRejectedKeyCount"/>）。
    /// </summary>
    public static QuestRewardSpec FromJson(string? json, out int rejectedKeys)
    {
        var spec = new QuestRewardSpec();
        rejectedKeys = 0;
        if (string.IsNullOrWhiteSpace(json)) return spec;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                rejectedKeys = 1;
                return spec;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt64(out var value) || value <= 0)
                {
                    rejectedKeys++;
                    continue;
                }

                var key = prop.Name.Trim();
                if (key == "exp") spec.Exp = value;
                else if (key == "gold") spec.Gold = value;
                else if (key.StartsWith("item:", System.StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(key.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) && itemId > 0)
                    spec.Items.Add(new QuestRewardItem { TemplateId = itemId, Count = (int)value });
                else if (key.StartsWith("favor:", System.StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(key.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var domain)
                         && domain >= 1 && domain <= 5)
                    spec.Favor.Add(new QuestRewardFavor { Domain = domain, Points = (int)value });
                else rejectedKeys++;
            }
        }
        catch (JsonException)
        {
            rejectedKeys = 1;
        }

        return spec;
    }

    /// <summary>便捷重载：只要结果不要计数。</summary>
    public static QuestRewardSpec FromJson(string? json) => FromJson(json, out _);

    /// <summary>
    /// 运行时形态（<c>QuestData.Rewards</c> 那个 <c>Dictionary&lt;string,int&gt;</c>）→ 结构化奖励。
    /// <para>任务接取时目标与奖励会被展平成字典存进角色状态（既有口径），下发给客户端之前
    /// 必须有一条唯一的还原路径；否则"发奖表"与"界面显示的奖励"会是两套解读。</para>
    /// </summary>
    public static QuestRewardSpec FromLegacyDictionary(IDictionary<string, int>? rewards)
    {
        var spec = new QuestRewardSpec();
        if (rewards == null) return spec;

        foreach (var entry in rewards)
        {
            var key = (entry.Key ?? string.Empty).Trim();
            if (key.Length == 0 || entry.Value <= 0) continue;

            if (key == "exp") spec.Exp = entry.Value;
            else if (key == "gold") spec.Gold = entry.Value;
            else if (key.StartsWith("item:", System.StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(key.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) && itemId > 0)
                spec.Items.Add(new QuestRewardItem { TemplateId = itemId, Count = entry.Value });
            else if (key.StartsWith("favor:", System.StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(key.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var domain)
                     && domain >= 1 && domain <= 5)
                spec.Favor.Add(new QuestRewardFavor { Domain = domain, Points = entry.Value });
        }

        return spec;
    }

    /// <summary>结构化奖励 → 运行时字典（与 <see cref="FromLegacyDictionary"/> 严格互逆）。</summary>
    public Dictionary<string, int> ToLegacyDictionary()
    {
        var map = new Dictionary<string, int>();
        if (Exp > 0) map["exp"] = (int)System.Math.Min(Exp, int.MaxValue);
        if (Gold > 0) map["gold"] = (int)System.Math.Min(Gold, int.MaxValue);
        foreach (var item in Items)
        {
            if (item == null || item.TemplateId <= 0 || item.Count <= 0) continue;
            map["item:" + item.TemplateId.ToString(CultureInfo.InvariantCulture)] = item.Count;
        }
        foreach (var favor in Favor)
        {
            if (favor == null || favor.Domain < 1 || favor.Domain > 5 || favor.Points <= 0) continue;
            map["favor:" + favor.Domain.ToString(CultureInfo.InvariantCulture)] = favor.Points;
        }
        return map;
    }

    /// <summary>解析被丢弃的键数（校验器用它把"写了但不会发"的键挑出来）。</summary>
    public static int ParseRejectedKeyCount(string? json)
    {
        FromJson(json, out var rejected);
        return rejected;
    }

    private static void AppendPair(StringBuilder builder, ref bool appended, string key, long value)
    {
        if (appended) builder.Append(',');
        builder.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        appended = true;
    }
}

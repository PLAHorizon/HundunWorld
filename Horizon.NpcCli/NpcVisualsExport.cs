using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Npcs;

namespace Horizon.NpcCli;

/// <summary>
/// 客户端 NPC 立场表（<c>npcId / modelKey → stance</c>）的构建。
/// <para>
/// <b>为什么只导立场与名字，不导 prefab</b>：NPC 的外观表与怪物是同一套形状，但本轮先把
/// "名牌绿/黄/红"这条最刺眼的观感接通；外观分流（model_key → prefab）走怪物那条链的第二步。
/// 少导一列不会造成两边对不上——读侧只读这里写出的键。
/// </para>
/// <para>
/// <b>键名是跨脚本域协议</b>：读侧在 <c>HundunWorld/Source/Game/Selection/NpcStanceCatalog.cs</c>
/// （Flax 脚本域，与本程序集互相引用不到），它按 <c>npcs / npcId / modelKey / stance</c> 取值。
/// 改名必须两侧同时改，<c>NpcEditorContractTests</c> 按字面量钉住这几个键。
/// </para>
/// <para>
/// <b>stance 写成枚举整数</b>（0 友 / 1 中 / 2 敌），由 <see cref="NpcStance"/> 直接强转——
/// 客户端不引用 Sim 程序集，靠这份"导出口按枚举原样写出"的约定对齐，
/// 而契约测试断言枚举值就是 0/1/2，防止有人在枚举中间插一项把线上颜色整体挪位。
/// </para>
/// </summary>
public static class NpcVisualsExport
{
    /// <summary>
    /// 读侧真正消费的键（与 <c>NpcStanceCatalog</c> 一一对应）。
    /// <para><b><c>name</c>/<c>title</c> 刻意不在协议里</b>：它们只为人 review 服务
    /// （名字由服务端随 Identity 下发，客户端不读表里的名字）。把"没被读的列"也算进协议，
    /// 会让契约测试断言一个根本不存在的耦合。</para>
    /// </summary>
    public static readonly string[] ClientKeys =
    {
        "npcs", "npcId", "modelKey", "stance", "services",
        // 外观：逐只指认的预制体（空串 = 没配，客户端回退占位人形）。
        "prefab",
        // 训练师窗口的展示数据（只做显示；学费与能不能学仍由服务端裁决）。
        "trainerRows", "skillId", "kind", "gold", "itemId", "itemCount", "seq", "label",
        // 对话菜单（点 NPC 先看到"能做什么"；动作执行仍走各自的服务端通路）。
        // text = 台词正文（023 可选列）：**只在非空时写出**，空串走客户端的种类合成标签——
        // 这样没配台词的随包产物与上一版逐字节相同，不必为了一个空键重导整份表。
        "gossipOptions", "arg", "minLevel", "maxLevel", "text",
        // 台词分组：gossipGroupId = 这位 NPC 默认展示的组；group = 每个选项所属组（0=通用）。
        "gossipGroupId", "group",
    };

    /// <summary>
    /// 字符串序列化选项：<b>不要转义非 ASCII</b>。
    /// 表要随包发布并进版本库，<c>\u94F8\u5251\u5E08</c> 这种串既看不懂也没法 review；
    /// 而读侧只按 UTF-8 读文件，不依赖转义形式。
    /// </summary>
    private static readonly JsonSerializerOptions TextOptions = new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>从内容快照构建 JSON 文本。<b>只导已发布的模板</b>由调用方保证。</summary>
    public static string Build(NpcContentSnapshot snapshot, out int count)
    {
        var rows = new List<(int NpcId, string ModelKey, int Stance, string Name, string Title, uint Services, string Prefab, int GossipGroupId)>(snapshot.Templates.Count);

        // 服务位要和运行期装配**同一套判据**：开了商人位却没货架行时，客户端不该出现"看看货架"
        // （点下去服务端只会回 not_a_vendor）。这里用与 NpcContentLoader 相同的 PruneDataEmpty。
        var vendorCounts = new Dictionary<int, int>();
        for (var i = 0; i < snapshot.VendorRows.Count; i++)
        {
            var row = snapshot.VendorRows[i];
            // 与装配同口径：坏货架行（ItemId<=0）不会进内容包，不能算作"有货"。
            if (row.ItemId <= 0) continue;
            vendorCounts[row.NpcId] = vendorCounts.TryGetValue(row.NpcId, out var n) ? n + 1 : 1;
        }

        var skillCounts = new Dictionary<int, int>();
        var recipeCounts = new Dictionary<int, int>();
        for (var i = 0; i < snapshot.TrainerRows.Count; i++)
        {
            var row = snapshot.TrainerRows[i];
            var target = row.ServiceKind switch { 1 => skillCounts, 2 => recipeCounts, _ => null };
            if (target == null) continue;
            target[row.NpcId] = target.TryGetValue(row.NpcId, out var n) ? n + 1 : 1;
        }

        static int Count(Dictionary<int, int> counts, int npcId)
            => counts.TryGetValue(npcId, out var n) ? n : 0;

        for (var i = 0; i < snapshot.Templates.Count; i++)
        {
            var t = snapshot.Templates[i];
            if (t.NpcId <= 0) continue;

            // 两样都没有才跳过：**模型键只是分组**，逐只指认的预制体 GUID 已经足够让客户端认出这一只。
            // 以前"模型键为空就整行丢掉"，于是新建的 NPC（默认没键）即使配好 GUID 也进不了表——
            // 表现是"库里配了美术，世界里还是占位人形"，而表里根本查无此人。
            if (string.IsNullOrWhiteSpace(t.ModelKey) && string.IsNullOrWhiteSpace(t.PrefabGuid)) continue;

            var services = NpcServiceCore.PruneDataEmpty(
                t.ServiceFlags, Count(vendorCounts, t.NpcId), Count(skillCounts, t.NpcId), Count(recipeCounts, t.NpcId));

            rows.Add((t.NpcId, t.ModelKey, StanceValue((NpcStance)t.Reaction), t.Name, t.Title, services, t.PrefabGuid ?? string.Empty, t.GossipGroupId));
        }

        // 按编号排序：同一份库导出两次要逐字节相同，否则版本库里会出现无意义的 diff。
        rows.Sort(static (a, b) => a.NpcId.CompareTo(b.NpcId));
        count = rows.Count;

        var builder = new StringBuilder(256 + rows.Count * 96);
        builder.Append("{\n  \"npcs\": [\n");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            builder.Append("    {")
                .Append("\"npcId\": ").Append(r.NpcId).Append(", ")
                .Append("\"modelKey\": ").Append(JsonSerializer.Serialize(r.ModelKey, TextOptions)).Append(", ")
                // 预制体 GUID：空串 = 没配（客户端回退占位人形）。配了就按这一个资产渲染这只 NPC，
                // 不吃"怪物按模型键查目录"那条路——NPC 的美术是逐只指认的，键名只是分组。
                .Append("\"prefab\": ").Append(JsonSerializer.Serialize(r.Prefab, TextOptions)).Append(", ")
                .Append("\"stance\": ").Append(r.Stance).Append(", ")
                .Append("\"name\": ").Append(JsonSerializer.Serialize(r.Name, TextOptions)).Append(", ")
                .Append("\"title\": ").Append(JsonSerializer.Serialize(r.Title, TextOptions)).Append(", ")
                .Append("\"services\": ").Append(r.Services).Append(", ")
                // 台词组：客户端按这一组筛菜单（0 = 不筛，兼容旧表）。作息时段换组还要状态增量，见设计文档 §11.63。
                .Append("\"gossipGroupId\": ").Append(r.GossipGroupId).Append(", ")
                .Append("\"trainerRows\": [");

            var taught = new List<NpcTrainerRowDto>();
            for (var k = 0; k < snapshot.TrainerRows.Count; k++)
            {
                if (snapshot.TrainerRows[k].NpcId == r.NpcId) taught.Add(snapshot.TrainerRows[k]);
            }
            taught.Sort(static (a, b) =>
            {
                var bySeq = a.Seq.CompareTo(b.Seq);
                return bySeq != 0 ? bySeq : a.SkillId.CompareTo(b.SkillId);
            });

            for (var k = 0; k < taught.Count; k++)
            {
                var t = taught[k];
                if (k > 0) builder.Append(", ");

                // 配方行的名字从配方表带过来：客户端没有配方目录，只给编号时菜单里只能显示"配方#900001"。
                var label = string.Empty;
                if (t.ServiceKind == 2)
                {
                    for (var ri = 0; ri < snapshot.Recipes.Count; ri++)
                    {
                        if (snapshot.Recipes[ri].RecipeId != t.SkillId) continue;
                        label = snapshot.Recipes[ri].Name ?? string.Empty;
                        break;
                    }
                }

                builder.Append("{\"skillId\": ").Append(t.SkillId)
                    .Append(", \"kind\": ").Append(t.ServiceKind)
                    .Append(", \"gold\": ").Append(t.CostGold)
                    .Append(", \"itemId\": ").Append(t.CostItemId)
                    .Append(", \"itemCount\": ").Append(t.CostItemCount)
                    .Append(", \"seq\": ").Append(t.Seq)
                    .Append(", \"label\": ").Append(JsonSerializer.Serialize(label, TextOptions))
                    .Append('}');
            }

            builder.Append(']');

            // 对话菜单：动作种类与参数由内容表给，菜单文字由客户端按种类合成（内容表里 text_id=0）。
            builder.Append(", \"gossipOptions\": [");
            var options = new List<NpcGossipRowDto>();
            for (var k = 0; k < snapshot.GossipRows.Count; k++)
            {
                if (snapshot.GossipRows[k].NpcId == r.NpcId) options.Add(snapshot.GossipRows[k]);
            }
            options.Sort(static (a, b) =>
            {
                var byGroup = a.GroupId.CompareTo(b.GroupId);
                if (byGroup != 0) return byGroup;
                var bySeq = a.Seq.CompareTo(b.Seq);
                return bySeq != 0 ? bySeq : a.ActionKind.CompareTo(b.ActionKind);
            });
            for (var k = 0; k < options.Count; k++)
            {
                var g = options[k];
                if (k > 0) builder.Append(", ");
                builder.Append("{\"kind\": ").Append(g.ActionKind)
                    .Append(", \"arg\": ").Append(g.ActionArg)
                    // 选项所属台词组：0 = 通用（任何组都显示）。
                    .Append(", \"group\": ").Append(g.GroupId)
                    .Append(", \"seq\": ").Append(g.Seq)
                    .Append(", \"minLevel\": ").Append(g.ReqMinLevel)
                    .Append(", \"maxLevel\": ").Append(g.ReqMaxLevel);
                if (!string.IsNullOrEmpty(g.Text))
                    builder.Append(", \"text\": ").Append(JsonSerializer.Serialize(g.Text, TextOptions));
                builder.Append('}');
            }
            builder.Append("]}");
            if (i < rows.Count - 1) builder.Append(',');
            builder.Append('\n');
        }

        builder.Append("  ]\n}\n");
        return builder.ToString();
    }

    /// <summary>
    /// 没连上库时的兜底：用硬编码目录的 3 条（与 <c>NpcTemplateCatalog</c> 同源）导一份"全友好"的表。
    /// <para>这不是假装成功：调用方会在回执里写明来源是 <c>catalog_fallback</c>，让人知道这份表没经过库。</para>
    /// </summary>
    public static string BuildFallback(out int count)
    {
        var snapshot = new NpcContentSnapshot();
        var catalog = NpcTemplateCatalog.RegisteredTemplates();
        for (var i = 0; i < catalog.Length; i++)
        {
            // 服务位与 022 种子里那三行保持同值：兜底表也要能让"商人"入口出现，
            // 否则"没连库"会表现为"铁匠点不开商店"，而不是"表里还没导服务位"。
            snapshot.Templates.Add(new NpcTemplateSnapshot
            {
                NpcId = catalog[i].TemplateId,
                Name = catalog[i].Name,
                Title = catalog[i].Title,
                ModelKey = catalog[i].ModelKey,
                Reaction = (byte)NpcStance.Friendly,
                ServiceFlags = catalog[i].TemplateId switch
                {
                    1 => (uint)(NpcServiceBits.Gossip | NpcServiceBits.Vendor | NpcServiceBits.Trainer
                                | NpcServiceBits.CraftTrainer | NpcServiceBits.CraftStation),
                    2 => (uint)(NpcServiceBits.Gossip | NpcServiceBits.QuestGiver | NpcServiceBits.QuestTurnIn),
                    3 => (uint)(NpcServiceBits.Gossip | NpcServiceBits.Vendor),
                    _ => 0u,
                },
            });
        }

        return Build(snapshot, out count);
    }

    /// <summary>立场 → 客户端整数。<b>越界立场按友好处理</b>（与内核的"坏数据不让它变成红名"一致）。</summary>
    public static int StanceValue(NpcStance stance) => stance switch
    {
        NpcStance.Neutral => 1,
        NpcStance.Hostile => 2,
        _ => 0,
    };
}

using Horizon.Model.GameModel;
using Horizon.Orleans.Interface.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Horizon.Orleans.Silo.Startup;

/// <summary>
/// 把物品模板表折成"按五行域的掉落表"（方案 §14.2 的产出侧）。
/// <para>
/// 规则刻意做成可回归的纯函数：只有 <c>drop_rate&gt;0</c> 且 <c>source_detail.via</c> 含 <c>loot</c>
/// 的行才进表，表 ID 直接用五行域编号（1金…5土）。
/// </para>
/// <para>
/// 为什么以 <c>via</c> 而不是 <c>source_type</c> 为准：需求要的是"初级技能书**既走任务也走刷怪**"，
/// 而 <c>source_type</c> 是单值列（1=任务产出 2=战斗产出），表达不了一个物品有多条产出渠道。
/// 用它当闸门会静默砍掉这条路——白/黄阶书的 <c>source_type</c> 正是 1，于是"刷怪掉初级书"整条设计
/// 变成一句空话，而且不掉落的表现在任何日志里都没有一行。所以 <c>source_type</c> 只当粗粒度标签留着，
/// 进不进掉落表一律看 <c>via</c>。
/// </para>
/// <para>
/// 哪只怪属于哪一域是内容数据，本类不猜：猜出来的映射会让掉落与好感两条链同时错，
/// 而且从任何报错里都看不出来（表现是"金域的信物从火域怪身上掉"）。
/// </para>
/// </summary>
public static class WuxingLootTables
{
    /// <summary>五行域编号上限（1金 2木 3水 4火 5土）。</summary>
    public const int DomainCount = 5;

    /// <summary>
    /// 世界 BOSS 掉落表的 ID 起点：表 ID = <c>WorldBossTableBase + 五行域</c>，域 0（无域，如混沌）落在本身上。
    /// <para>
    /// 必须与 <c>Horizon.Game.Sim.MonsterTemplateCatalog.WorldBossTableBase</c> 同一个数，
    /// 而 Silo 侧不便为这一个常量新增一条到 Sim 的工程引用，所以两处各写一次、
    /// 由 <c>WuxingBossLootTests.WorldBossTableBase_IsTheSameNumberOnBothSides</c> 兜住分叉。
    /// 分叉的表现很难看：BOSS 掉不出天阶、而道阶演化又要求天阶练满——整条顶端断链且没有任何报错。
    /// </para>
    /// </summary>
    public const int WorldBossTableBase = 100;

    /// <summary>会进掉落表的物品类型：2 消耗品（信物、丹药）、3 材料（灵材）、6 秘籍。</summary>
    private static readonly HashSet<int> LootableItemTypes = new() { 2, 3, 6 };

    /// <summary>读 <c>source_detail.via</c> 里是否含某个通道（大小写不敏感）。</summary>
    private static bool HasVia(string? sourceDetail, string channel)
    {
        if (string.IsNullOrWhiteSpace(sourceDetail)) return false;
        try
        {
            using var doc = JsonDocument.Parse(sourceDetail);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("via", out var via) || via.ValueKind != JsonValueKind.Array) return false;
            foreach (var v in via.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String
                    && string.Equals(v.GetString(), channel, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>世界 BOSS 那一档的表 ID；不该进 BOSS 表时返回 0。</summary>
    public static int ReadBossTableId(string? sourceDetail)
    {
        if (!HasVia(sourceDetail, "worldBoss")) return 0;
        var domain = ReadBossDomain(sourceDetail);
        return domain == 0 ? WorldBossTableBase : WorldBossTableBase + domain;
    }

    private static int ReadBossDomain(string? sourceDetail)
    {
        try
        {
            using var doc = JsonDocument.Parse(sourceDetail!);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            if (!doc.RootElement.TryGetProperty("domain", out var domain) || domain.ValueKind != JsonValueKind.Number) return 0;
            if (!domain.TryGetInt32(out var d)) return 0;
            return d >= 0 && d <= DomainCount ? d : 0;   // 0 允许：无域的行进总表
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// 解析 <c>source_detail</c>（形如 <c>{"via":["loot","quest"],"domain":3}</c>）。
    /// 缺域、脏 JSON、不含 loot 通道，一律返回 0 表示"这张表里没有它"。
    /// </summary>
    public static int ReadLootDomain(string? sourceDetail)
    {
        if (string.IsNullOrWhiteSpace(sourceDetail)) return 0;

        try
        {
            using var doc = JsonDocument.Parse(sourceDetail);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return 0;

            if (!root.TryGetProperty("via", out var via) || via.ValueKind != JsonValueKind.Array) return 0;
            bool byLoot = false;
            foreach (var v in via.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String
                    && string.Equals(v.GetString(), "loot", StringComparison.OrdinalIgnoreCase))
                    byLoot = true;
            }
            if (!byLoot) return 0;

            // TryGetInt32 遇到字符串元素是抛 InvalidOperationException，不是返回 false。
            // 一个手工编辑过的物品行（"domain":"3"）就能让整张掉落表装配崩掉，所以先看类型再取值。
            if (!root.TryGetProperty("domain", out var domain) || domain.ValueKind != JsonValueKind.Number) return 0;
            if (!domain.TryGetInt32(out var d)) return 0;
            return d >= 1 && d <= DomainCount ? d : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// 按通道分表：域表（表 ID=1..5，日常刷怪）与世界 BOSS 表（表 ID=100+域，天阶与混沌）。
    /// 表内条目按物品 id 稳定排序，保证同一份数据装配出同一张表。
    /// </summary>
    public static Dictionary<int, LootTableConfig> Build(IEnumerable<ItemTemplateEntity> rows)
    {
        var byTable = new Dictionary<int, List<LootEntry>>();

        foreach (var t in rows ?? Enumerable.Empty<ItemTemplateEntity>())
        {
            if (t == null || !LootableItemTypes.Contains(t.ItemType)) continue;
            if (t.DropRate <= 0m) continue;           // 设计值 0 = 明确不靠掉落产出（道阶书等）

            // 通道以 via 为权威：一行可以同时属于多路产出（既刷得到又在 BOSS 身上掉）。
            var tableIds = new List<int>(2);
            int domain = ReadLootDomain(t.SourceDetail);
            if (domain > 0) tableIds.Add(domain);
            int bossTable = ReadBossTableId(t.SourceDetail);
            if (bossTable > 0 && !tableIds.Contains(bossTable)) tableIds.Add(bossTable);
            if (tableIds.Count == 0) continue;        // 纯任务书 / evolve：不是刷出来的东西

            var entry = new LootEntry
            {
                ItemId = t.Id,
                ItemName = t.ItemName ?? string.Empty,
                DropRate = (float)Math.Min(1d, Math.Max(0d, (double)t.DropRate)),
                MinCount = 1,
                // 低阶允许一次掉两枚；高阶一张就是学问，也让"运气好"不至于一次掉一摞书
                MaxCount = t.Rarity >= 4 ? 1 : 2,
                Rarity = Math.Clamp((int)t.Rarity, 1, 5),
                IsPityEligible = t.Rarity >= 4,
            };

            foreach (var tableId in tableIds)
            {
                if (!byTable.TryGetValue(tableId, out var list)) byTable[tableId] = list = new List<LootEntry>();
                list.Add(entry);
            }
        }

        var tables = new Dictionary<int, LootTableConfig>();
        foreach (var kv in byTable.OrderBy(k => k.Key))
        {
            if (kv.Value.Count == 0) continue;
            tables[kv.Key] = new LootTableConfig
            {
                TableId = kv.Key,
                Name = kv.Key >= WorldBossTableBase
                    ? $"世界BOSS·五行域{kv.Key - WorldBossTableBase}产出"
                    : $"五行域{kv.Key}产出",
                Entries = kv.Value.OrderBy(e => e.ItemId).ToArray(),
            };
        }
        return tables;
    }
}

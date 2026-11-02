using System;
using System.Collections.Generic;
using System.Text.Json;
using Horizon.Game.Core.Sim.Server;

namespace Horizon.NpcCli;

/// <summary>
/// 编辑器保存单条 NPC 时发给 CLI 的草稿。
/// <para>
/// <b>为什么草稿直接复用<b>快照 DTO</b>而不是另立一套契约</b>：两套契约必然漂
/// （记忆 [派生副本要有与来源逐字相同的断言] 的同族）。这里只加"一个 NPC + 它名下的关系行"这一层组合，
/// 字段本身全部来自 <see cref="NpcContentSnapshot"/> 那一族——编辑器的序列化代码与导出文件因此是同一种形状。
/// </para>
/// <para>
/// <b>校验口径</b>：<see cref="ToSnapshot"/> 把草稿包成一份最小快照，交给
/// <c>NpcContentLoader.Compile</c> 跑装配级校验。<b>CLI 的 validate 与 DsHost 的装配用的是同一套判据</b>，
/// 不存在"编辑器说合法、运行期发现问题"的分叉。
/// </para>
/// </summary>
public sealed class NpcDraft
{
    /// <summary>模板本体。<b>必须非空</b>（缺了它整份草稿没有主体，直接判坏）。</summary>
    public NpcTemplateSnapshot? Template { get; set; }

    public List<NpcSpawnSnapshotDto> SpawnPoints { get; set; } = new();
    public List<NpcRouteSnapshot> Routes { get; set; } = new();
    public List<NpcVendorRowDto> VendorRows { get; set; } = new();
    public List<NpcTrainerRowDto> TrainerRows { get; set; } = new();
    public List<NpcRecipeSnapshotDto> Recipes { get; set; } = new();
    public List<NpcGossipRowDto> GossipRows { get; set; } = new();
    public List<NpcScheduleRowDto> ScheduleRows { get; set; } = new();
    public List<NpcQuestLinkDto> QuestLinks { get; set; } = new();

    /// <summary>技能挂载（<c>Casts</c> 的数据源；表不存在时为空）。</summary>
    public List<NpcSkillMountDto> SkillMounts { get; set; } = new();

    /// <summary>
    /// 掉落绑定（复用怪物那套 DTO，<c>MonsterTemplateId</c> 就填本 NPC 的编号）。
    /// <b>编辑器在这里配"这个 NPC 掉什么"</b>，不必再去怪物编辑器给一个 NPC 编号配表。
    /// </summary>
    public List<MonsterLootBindingSnapshotDto> LootBindings { get; set; } = new();

    /// <summary>内容版本（乐观锁）：保存时必须等于库里的当前值，否则拒（防止覆盖别人的编辑）。</summary>
    public int ContentVersion { get; set; }

    public static NpcDraft? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<NpcDraft>(json, NpcSnapshotJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 包成一份最小内容快照（只含这一个 NPC 及其关系行），供装配级校验。
    /// <para><b>路线的处理</b>：草稿里的路线单独收；刷点引用的是草稿里的路线 id，
    /// 若引用了草稿外的路线，装配校验会报 <c>spawn_unknown_route</c>——这正是我们要的行为
    /// （一个 NPC 不能悄悄依赖别人没交出来的路线）。</para>
    /// </summary>
    public NpcContentSnapshot ToSnapshot(int revision = 1)
    {
        var snap = new NpcContentSnapshot { ContentRevision = revision };
        if (Template is not null) snap.Templates.Add(Template);

        var npcId = Template?.NpcId ?? 0;

        // 关系行统一改写成这条 NPC 的 id：草稿的主体就是 Template，行里若写了别的 id，
        // 那是编辑器 bug 的产物——按"行属于本次保存的 NPC"归一，避免半份数据挂到别的 NPC 名下。
        foreach (var s in SpawnPoints) { s.NpcId = npcId; snap.SpawnPoints.Add(s); }
        foreach (var r in Routes) snap.Routes.Add(r);
        foreach (var v in VendorRows) { v.NpcId = npcId; snap.VendorRows.Add(v); }
        foreach (var t in TrainerRows) { t.NpcId = npcId; snap.TrainerRows.Add(t); }
        foreach (var g in GossipRows) { g.NpcId = npcId; snap.GossipRows.Add(g); }
        foreach (var s in ScheduleRows) { s.NpcId = npcId; snap.ScheduleRows.Add(s); }
        foreach (var l in QuestLinks) { l.NpcId = npcId; snap.QuestLinks.Add(l); }
        foreach (var m in SkillMounts) { m.NpcId = npcId; snap.SkillMounts.Add(m); }

        // 归属一律改写成这条 NPC：草稿的主体就是 Template，行里写了别的 owner 是编辑器 bug 的产物。
        foreach (var b in LootBindings) { b.MonsterTemplateId = npcId; snap.LootBindings.Add(b); }
        foreach (var r in Recipes) snap.Recipes.Add(r);

        return snap;
    }

    /// <summary>把一条完成装配后的运行期配置回填成草稿的模板形态（编辑器"从库加载"用）。</summary>
    public static NpcDraft FromSnapshotForNpc(NpcContentSnapshot snapshot, int npcId)
    {
        var draft = new NpcDraft();

        for (var i = 0; i < snapshot.Templates.Count; i++)
        {
            if (snapshot.Templates[i].NpcId != npcId) continue;
            draft.Template = snapshot.Templates[i];
            break;
        }

        foreach (var s in snapshot.SpawnPoints) if (s.NpcId == npcId) draft.SpawnPoints.Add(s);
        foreach (var v in snapshot.VendorRows) if (v.NpcId == npcId) draft.VendorRows.Add(v);
        foreach (var t in snapshot.TrainerRows) if (t.NpcId == npcId) draft.TrainerRows.Add(t);
        foreach (var g in snapshot.GossipRows) if (g.NpcId == npcId) draft.GossipRows.Add(g);
        foreach (var s in snapshot.ScheduleRows) if (s.NpcId == npcId) draft.ScheduleRows.Add(s);
        foreach (var l in snapshot.QuestLinks) if (l.NpcId == npcId) draft.QuestLinks.Add(l);
        foreach (var m in snapshot.SkillMounts) if (m.NpcId == npcId) draft.SkillMounts.Add(m);
        foreach (var b in snapshot.LootBindings) if (b.MonsterTemplateId == npcId) draft.LootBindings.Add(b);

        // 配方按"被这条 NPC 教"来收（NpcRecipeTaught 关系在库里，快照里没有这一层；
        // 这里按 TrainerRows 的 service_kind=2 反查）。
        var taught = new HashSet<int>();
        foreach (var t in draft.TrainerRows) if (t.ServiceKind == 2) taught.Add(t.SkillId);
        foreach (var r in snapshot.Recipes) if (taught.Contains(r.RecipeId)) draft.Recipes.Add(r);

        // 路线：收集被自己刷点**或作息档**引用的那些。
        // 只看刷点会漏掉"只在某个时段才走这条线"的路线——那样的 NPC 在编辑器里打开后
        // 路线是空的，保存一次就把作息切路线的数据"补丢"了。
        var routeIds = new HashSet<int>();
        foreach (var s in draft.SpawnPoints) if (s.RouteId > 0) routeIds.Add(s.RouteId);
        foreach (var s in draft.ScheduleRows) if (s.RouteId > 0) routeIds.Add(s.RouteId);
        foreach (var r in snapshot.Routes) if (routeIds.Contains(r.PathId)) draft.Routes.Add(r);

        return draft;
    }
}

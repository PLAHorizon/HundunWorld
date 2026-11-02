using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>一次"服务入口预览"的结果（编辑器与 CLI 共用；纯数据，可直接打印）。</summary>
public readonly struct NpcServicePreview(
    int npcId,
    uint declaredFlags,
    uint effectiveFlags,
    List<NpcServiceEntry> entries,
    NpcQuestCounts questCounts)
{
    public readonly int NpcId = npcId;

    /// <summary>模板里声明的位图（作者勾了什么）。</summary>
    public readonly uint DeclaredFlags = declaredFlags;

    /// <summary>去掉"内容为空"的服务位之后真正生效的位图。</summary>
    public readonly uint EffectiveFlags = effectiveFlags;

    /// <summary>按展示顺序逐条求值（含不可见项与理由）。</summary>
    public readonly List<NpcServiceEntry> Entries = entries;

    /// <summary>求值时使用的任务关系统计（预览的任务位判据）。</summary>
    public readonly NpcQuestCounts QuestCounts = questCounts;

    /// <summary>可见入口个数。</summary>
    public int VisibleCount
    {
        get
        {
            if (Entries == null) return 0;
            var n = 0;
            for (var i = 0; i < Entries.Count; i++) if (Entries[i].Visible) n++;
            return n;
        }
    }
}

/// <summary>
/// 服务入口预览：把"编译后的内容包 + 玩家态"送进 <see cref="NpcServiceCore"/>，回答
/// "这位 NPC 对这名玩家会露出哪些入口、其余为什么不可见"。
/// <para>
/// <b>为什么编辑器需要它</b>：作者勾的是位图，玩家看到的是入口。中间那一跳受内容（货架/教学行为空）
/// 与玩家态（等级、任务关系）影响；不做预览时，症状是"编辑器里勾了商人、游戏里点开是空窗"。
/// </para>
/// <para><b>纯函数</b>：不查库、不调 Grain；任务关系由调用方用 <see cref="NpcQuestCounts"/> 传入。</para>
/// </summary>
public static class NpcServicePreviewCore
{
    public static NpcServicePreview Build(
        NpcContentBundle bundle, int npcId, int playerLevel, NpcQuestCounts questCounts)
    {
        if (bundle == null || npcId <= 0 || !bundle.ByNpcId.TryGetValue(npcId, out var cfg) || cfg == null)
            return default;

        var vendorRows = bundle.VendorRows.TryGetValue(npcId, out var vendor) && vendor != null ? vendor.Count : 0;

        int skillRows = 0, recipeRows = 0;
        if (bundle.TrainerRows.TryGetValue(npcId, out var trainers) && trainers != null)
        {
            for (var i = 0; i < trainers.Count; i++)
            {
                if (trainers[i].Service == NpcTrainerService.Skill) skillRows++;
                else if (trainers[i].Service == NpcTrainerService.Recipe) recipeRows++;
            }
        }

        // 装配后的 ServiceFlags 是**清洗过**的；诊断要回答"为什么不可见"，得从声明值出发。
        var declared = cfg.DeclaredServiceFlags != 0 ? cfg.DeclaredServiceFlags : cfg.ServiceFlags;
        var effective = NpcServiceCore.PruneDataEmpty(declared, vendorRows, skillRows, recipeRows);
        var ctx = new NpcServiceContext(playerLevel, vendorRows, skillRows, recipeRows, questCounts);
        // 逐条求值用**声明位图**：先摘位再求值会把"内容为空"统一压成 BitOff，
        // 预览要回答的正是"为什么不可见"（NoVendorStock / NoTrainerRows / NoQuestForThisNpc…）。
        // 两条判据对可见集合是一致的（PruneDataEmpty 镜像了 Evaluate 的三条数据规则），
        // 所以这里的 visible 不会和运行期分歧；差异只在**理由**更具体。
        var entries = NpcServiceCore.Resolve(declared, ctx);

        return new NpcServicePreview(npcId, declared, effective, entries, questCounts);
    }
}

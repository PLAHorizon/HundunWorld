using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 一个 NPC 的运行期配置（模板 + 已展开的 AI 位）。
/// <para>
/// <b>装配期就把"档位 + 强制开/关"展平成 <see cref="Flags"/></b>：运行期每 tick 都在问"会不会走图"，
/// 现场展开会把档位表变成热路径上的第二份判据。与怪物侧 <c>MonsterRuntimeConfig</c> 同一取舍。
/// </para>
/// </summary>
public sealed class NpcRuntimeConfig
{
    public int NpcId;
    public string Name = string.Empty;
    public string Title = string.Empty;
    public string ModelKey = string.Empty;
    public string PrefabGuid = string.Empty;
    public float Scale = 1f;

    public int LevelBase = 1;
    public int LevelVariance;

    /// <summary>立场（0 友 1 中 2 敌）。名牌色与可否攻击的唯一输入。</summary>
    public NpcStance Stance;

    /// <summary>服务位图（原始声明值，可见性还要过 <see cref="NpcServiceCore"/>）。</summary>
    public uint ServiceFlags;

    /// <summary>
    /// 作者在模板里**声明**的原始位图（清洗前）。<see cref="ServiceFlags"/> 是清洗后的生效值。
    /// <para>只服务诊断与编辑器预览——"我勾了商人却没入口"要能回答"是被内容为空摘掉的"，
    /// 拿生效值去求值只会得到 BitOff，理由全被压平。运行期判据一律读 <see cref="ServiceFlags"/>。</para>
    /// </summary>
    public uint DeclaredServiceFlags;

    /// <summary>已展开的 AI 能力位。</summary>
    public NpcAiFlags Flags;

    /// <summary>档位（展示与诊断用；真开关是 <see cref="Flags"/>）。</summary>
    public NpcAiTier Tier;

    /// <summary>可攻击覆盖：−1 跟立场 0 强制不可 1 强制可。</summary>
    public int AttackableOverride = -1;

    public float SpeedWalk = 1.6f;
    public float SpeedRun = 4f;
    public int AlertRadiusMeters = NpcBehaviorCore.DefaultAlertRadiusMeters;
    public int GossipGroupId;
    public byte Rank;
    public int WuxingDomain;
    public int KillCreditNpcId;

    /// <summary>装配期发现的坏位/冲突位（非 0 就必须在装配回执里被念出来，不许静默修好）。</summary>
    public uint AiConflictBits;
    public uint AiUnknownBits;
    public uint ServiceUnknownBits;

    /// <summary>反应求值（每次取都用同一判据，别在调用方各写一遍）。</summary>
    public NpcReactionVerdict Reaction() => NpcReactionCore.Of(Stance, AttackableOverride);

    /// <summary>ASCII 自证串。</summary>
    public string Describe() =>
        $"npc={NpcId} name={Name} stance={(byte)Stance} svc=0x{ServiceFlags:X} tier={(byte)Tier} " +
        $"lv={LevelBase}±{LevelVariance} {Flags.Describe()}";
}

/// <summary>NPC 刷点快照。</summary>
public readonly struct NpcSpawnSnapshot(
    int spawnPointId, int zoneId, long shardId,
    float x, float y, float z, float yaw,
    int npcId, int levelOverride, int maxAlive,
    float respawnSeconds, int routeId, byte facingMode)
{
    public readonly int SpawnPointId = spawnPointId;
    public readonly int ZoneId = zoneId;
    public readonly long ShardId = shardId;
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Z = z;
    public readonly float Yaw = yaw;
    public readonly int NpcId = npcId;

    /// <summary>≤0 = 不覆盖模板等级。</summary>
    public readonly int LevelOverride = levelOverride;

    public readonly int MaxAlive = maxAlive;
    public readonly float RespawnSeconds = respawnSeconds;
    public readonly int RouteId = routeId;
    public readonly byte FacingMode = facingMode;

    /// <summary>实际生效等级：刷点覆盖优先；否则按模板基准 ± 等级浮动掷一次。</summary>
    /// <remarks>
    /// <b>种子用"刷点 + 编号"而不是 tick</b>：NPC 的等级会显示在名牌上，
    /// 同一点复活后等级跳变是玩家立刻能看见的错；同一点多次生成必须是同一个等级。
    /// 区间按闭区间掷（与 <see cref="Horizon.Game.Sim.Monsters.MonsterLevelCore.Roll"/> 同口径）。
    /// </remarks>
    public int ResolveLevel(in NpcRuntimeConfig config)
    {
        if (LevelOverride > 0) return LevelOverride;

        var variance = config.LevelVariance;
        if (variance <= 0) return config.LevelBase;

        var seed = (ulong)((long)SpawnPointId * 1000003L + config.NpcId);
        return Horizon.Game.Sim.Monsters.MonsterLevelCore.Roll(
            config.LevelBase - variance, config.LevelBase + variance, config.LevelBase, seed, 0);
    }
}

/// <summary>路线快照（复用怪物侧 <c>MonsterPatrolCore</c> 的数据形状）。</summary>
public readonly struct NpcPathSnapshot(
    int pathId,
    byte mode,
    float pauseAtPointSeconds,
    bool useNavMesh,
    IReadOnlyList<Horizon.Game.Sim.Monsters.MonsterPatrolPoint> points)
{
    public readonly int PathId = pathId;
    public readonly byte Mode = mode;
    public readonly float PauseAtPointSeconds = pauseAtPointSeconds;
    public readonly bool UseNavMesh = useNavMesh;
    public readonly IReadOnlyList<Horizon.Game.Sim.Monsters.MonsterPatrolPoint> Points = points;
}

/// <summary>任务关系（这个 NPC 发布/交付哪条任务）。</summary>
public readonly struct NpcQuestLinkSnapshot(int questId, bool isGiver, bool isTurnIn, int orderSeq)
{
    public readonly int QuestId = questId;
    public readonly bool IsGiver = isGiver;
    public readonly bool IsTurnIn = isTurnIn;
    public readonly int OrderSeq = orderSeq;
}

/// <summary>
/// 装配后的 NPC 内容包（不可变快照）。
/// <para><b>与 <c>MonsterContentBundle</c> 分开、但同一 rev 语义</b>：设计文档 §4 要求 NPC 走同一个装配纪律，
/// 一份配置一次整替一个号；两者合并成一份快照会让"只改了商人货架"也触发怪物内容重装配。</para>
/// </summary>
public sealed class NpcContentBundle
{
    public int ContentRevision;

    public readonly Dictionary<int, NpcRuntimeConfig> ByNpcId = new Dictionary<int, NpcRuntimeConfig>();
    public readonly List<NpcSpawnSnapshot> SpawnPoints = new List<NpcSpawnSnapshot>();
    public readonly Dictionary<int, NpcPathSnapshot> Paths = new Dictionary<int, NpcPathSnapshot>();

    /// <summary>货架（按 NPC 分组，组内按 slot 升序——<b>顺序是数据</b>，装配时就排好，运行期不再排）。</summary>
    public readonly Dictionary<int, List<NpcVendorRow>> VendorRows = new Dictionary<int, List<NpcVendorRow>>();

    /// <summary>教学行（按 NPC 分组，组内按 seq 升序）。</summary>
    public readonly Dictionary<int, List<NpcTrainerRow>> TrainerRows = new Dictionary<int, List<NpcTrainerRow>>();

    /// <summary>配方（按配方 id 索引）。</summary>
    public readonly Dictionary<int, NpcCraftRecipeSnapshot> Recipes = new Dictionary<int, NpcCraftRecipeSnapshot>();

    /// <summary>台词（按组号索引）。</summary>
    public readonly Dictionary<int, List<NpcGossipOption>> GossipGroups = new Dictionary<int, List<NpcGossipOption>>();

    /// <summary>技能挂载（按 NPC 分组、组内按 slot_seq 升序）。<b>顺序是数据</b>：平局时按下标决定先放哪个。</summary>
    public readonly Dictionary<int, Horizon.Game.Sim.Monsters.MonsterSkillMount[]> SkillMounts =
        new Dictionary<int, Horizon.Game.Sim.Monsters.MonsterSkillMount[]>();

    /// <summary>任务关系（按 NPC 索引）。<b>这是头顶指示器的倒排索引</b>：
    /// 不预建就得每帧从任务表反查一次，而 UI 里还会长出第二份排序。</summary>
    public readonly Dictionary<int, List<NpcQuestLinkSnapshot>> QuestLinks = new Dictionary<int, List<NpcQuestLinkSnapshot>>();

    /// <summary>作息档（按 NPC 索引，组内按 seq 升序）。</summary>
    public readonly Dictionary<int, List<NpcScheduleSlot>> ScheduleSlots = new Dictionary<int, List<NpcScheduleSlot>>();

    /// <summary>掉落绑定（按 NPC 编号索引；结构与怪物侧同一款，见 <c>MonsterLootBindingSnapshot</c>）。</summary>
    public readonly Dictionary<int, List<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot>> LootBindings = new Dictionary<int, List<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot>>();

    public int TemplateCount => ByNpcId.Count;
    public int SpawnPointCount => SpawnPoints.Count;
}

/// <summary>配方快照（材料 + 成功档曲线）。JSON 解析在装配器，这里只放解析好的数。</summary>
public sealed class NpcCraftRecipeSnapshot
{
    public int RecipeId;
    public string Name = string.Empty;
    public int ProductItemId;
    public long CostGold;
    public int StationNpcId;
    public int SkillReq;
    public byte LearnSourceKind;
    public NpcMaterialNeed[] Materials = Array.Empty<NpcMaterialNeed>();
    public NpcCraftChanceCurve Curve;

    /// <summary>曲线表是否整（不整的配方必须在装配回执里被念出来，不能"做不出但不报错"）。</summary>
    public bool CurveUsable => Curve.IsUsable;
}

/// <summary>NPC 模板的注入来源（与 <c>IMonsterTemplateSource</c> 同形，让目录类只有一个可替换的读法）。</summary>
public interface INpcTemplateSource
{
    bool TryGet(int npcId, out NpcRuntimeConfig config);
    IReadOnlyCollection<int> RegisteredNpcIds();
}

/// <summary>
/// NPC 内容装配点。
/// <para>
/// <b>为什么用"安装 + 读回"</b>：与怪物侧同一条理由——目录类今天是静态查表，
/// 把存储换成可写字典会让"谁在什么时候改了内容"无从追溯。整份替换引用 = 一次装配一个版本号。
/// </para>
/// <para>
/// <b>与 <c>NpcTemplateCatalog</c> 的关系</b>：装配后目录类的 <c>TryGetRegistered</c> 先查这份、再退回硬编码三行；
/// 没装配时行为与今天逐字一致（这条"未装配 == 现状"的等价性由合同测试钉住）。
/// </para>
/// </summary>
public static class NpcContent
{
    private static INpcTemplateSource? _installed;

    /// <summary>当前装配来源；null = 用硬编码目录兜底。</summary>
    public static INpcTemplateSource? Installed => _installed;

    /// <summary>最近一次装配的内容号。</summary>
    public static int Revision { get; private set; }

    /// <summary>装配（原子整替），返回被换掉的旧内容号。</summary>
    public static int Install(NpcContentBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        var previous = Revision;
        _installed = new BundleSource(bundle);
        Revision = bundle.ContentRevision;
        return previous;
    }

    /// <summary>退回硬编码目录（测试与"库里还没东西"的开发期）。</summary>
    public static void ResetToHardcoded()
    {
        _installed = null;
        Revision = 0;
    }

    /// <summary>取配置；未装配返回 false（调用方据此决定是否回退硬编码）。</summary>
    public static bool TryGetConfig(int npcId, out NpcRuntimeConfig config)
    {
        var source = _installed;
        if (source is not null && source.TryGet(npcId, out config!)) return true;

        config = null!;
        return false;
    }

    /// <summary>已装配编号（未装配为空）。</summary>
    public static IReadOnlyCollection<int> RegisteredIds()
    {
        var source = _installed;
        return source?.RegisteredNpcIds() ?? Array.Empty<int>();
    }

    private static NpcContentBundle? Bundle() => (_installed as BundleSource)?.Bundle;

    /// <summary>刷点列表。</summary>
    public static IReadOnlyList<NpcSpawnSnapshot> SpawnPoints()
    {
        var b = Bundle();
        return b is null ? Array.Empty<NpcSpawnSnapshot>() : b.SpawnPoints;
    }

    /// <summary>取路线；不存在返回 null。</summary>
    public static NpcPathSnapshot? Path(int pathId)
    {
        var b = Bundle();
        if (b is null || pathId <= 0) return null;
        return b.Paths.TryGetValue(pathId, out var p) ? p : null;
    }

    /// <summary>货架行（已按 slot 升序）。<b>返回空表不等于"这个 NPC 不是商人"</b>——
    /// 位图才是声明，这里是内容；两者的不一致由 <see cref="NpcServiceCore"/> 判可见性。</summary>
    public static IReadOnlyList<NpcVendorRow> VendorRows(int npcId)
    {
        var b = Bundle();
        if (b is null) return Array.Empty<NpcVendorRow>();
        return b.VendorRows.TryGetValue(npcId, out var rows) ? rows : Array.Empty<NpcVendorRow>();
    }

    /// <summary>教学行（已按 seq 升序）。</summary>
    public static IReadOnlyList<NpcTrainerRow> TrainerRows(int npcId)
    {
        var b = Bundle();
        if (b is null) return Array.Empty<NpcTrainerRow>();
        return b.TrainerRows.TryGetValue(npcId, out var rows) ? rows : Array.Empty<NpcTrainerRow>();
    }

    /// <summary>配方。</summary>
    public static NpcCraftRecipeSnapshot? Recipe(int recipeId)
    {
        var b = Bundle();
        if (b is null || recipeId <= 0) return null;
        return b.Recipes.TryGetValue(recipeId, out var r) ? r : null;
    }

    /// <summary>全部配方（制作清单下行要用；顺序按配方 id 稳定）。</summary>
    public static IReadOnlyList<NpcCraftRecipeSnapshot> AllRecipes()
    {
        var b = Bundle();
        if (b is null || b.Recipes.Count == 0) return Array.Empty<NpcCraftRecipeSnapshot>();

        var list = new List<NpcCraftRecipeSnapshot>(b.Recipes.Count);
        foreach (var pair in b.Recipes) list.Add(pair.Value);
        list.Sort(static (a, b2) => a.RecipeId.CompareTo(b2.RecipeId));
        return list;
    }

    /// <summary>
    /// 这位 NPC 会放的技能（空数组 = 不会施法；运行期还要看 <c>Casts</c> 位）。
    /// <para>返回**数组**：施法内核与 <c>ResolveMonsterCast</c> 都按数组下标索引挂载，每 tick 转换会平白分配。</para>
    /// </summary>
    public static Horizon.Game.Sim.Monsters.MonsterSkillMount[] SkillMounts(int npcId)
    {
        var b = Bundle();
        if (b is null || npcId <= 0) return Array.Empty<Horizon.Game.Sim.Monsters.MonsterSkillMount>();
        return b.SkillMounts.TryGetValue(npcId, out var mounts) && mounts != null
            ? mounts
            : Array.Empty<Horizon.Game.Sim.Monsters.MonsterSkillMount>();
    }

    /// <summary>台词组。</summary>
    public static IReadOnlyList<NpcGossipOption> GossipGroup(int groupId)
    {
        var b = Bundle();
        if (b is null || groupId <= 0) return Array.Empty<NpcGossipOption>();
        return b.GossipGroups.TryGetValue(groupId, out var rows) ? rows : Array.Empty<NpcGossipOption>();
    }

    /// <summary>任务关系（指示器的取数入口）。</summary>
    public static IReadOnlyList<NpcQuestLinkSnapshot> QuestLinks(int npcId)
    {
        var b = Bundle();
        if (b is null) return Array.Empty<NpcQuestLinkSnapshot>();
        return b.QuestLinks.TryGetValue(npcId, out var rows) ? rows : Array.Empty<NpcQuestLinkSnapshot>();
    }

    /// <summary>
    /// 取某个 NPC 的掉落绑定。<b>没有绑定返回空表</b>（不是 null）：调用方据此得到"这只怪不掉东西"。
    /// </summary>
    public static IReadOnlyList<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot> LootBindings(int npcId)
    {
        var b = Bundle();
        if (b is null) return Array.Empty<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot>();
        return b.LootBindings.TryGetValue(npcId, out var rows)
            ? rows
            : Array.Empty<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot>();
    }

    /// <summary>作息档。</summary>
    public static IReadOnlyList<NpcScheduleSlot> ScheduleSlots(int npcId)
    {
        var b = Bundle();
        if (b is null) return Array.Empty<NpcScheduleSlot>();
        return b.ScheduleSlots.TryGetValue(npcId, out var rows) ? rows : Array.Empty<NpcScheduleSlot>();
    }

    /// <summary>装配回执的一行 ASCII 自证（DsHost 启动时要把它念出来：读到了不等于装上了）。</summary>
    public static string DescribeInstall()
    {
        var b = Bundle();
        if (b is null) return "npc_content=none";

        int vendorCount = 0, trainerCount = 0, linkCount = 0, slotCount = 0, gossipCount = 0, badCurve = 0;
        foreach (var list in b.VendorRows.Values) vendorCount += list.Count;
        foreach (var list in b.TrainerRows.Values) trainerCount += list.Count;
        foreach (var list in b.QuestLinks.Values) linkCount += list.Count;
        foreach (var list in b.ScheduleSlots.Values) slotCount += list.Count;
        foreach (var list in b.GossipGroups.Values) gossipCount += list.Count;
        foreach (var r in b.Recipes.Values)
        {
            if (!r.CurveUsable) badCurve++;
        }

        int lootCount = 0;
        foreach (var list in b.LootBindings.Values) lootCount += list.Count;

        return $"npc_content rev={Revision} templates={b.TemplateCount} spawns={b.SpawnPointCount} loot_bindings={lootCount} " +
               $"paths={b.Paths.Count} vendor_rows={vendorCount} trainer_rows={trainerCount} " +
               $"recipes={b.Recipes.Count} recipes_bad_curve={badCurve} gossip_rows={gossipCount} " +
               $"quest_links={linkCount} schedule_rows={slotCount}";
    }

    /// <summary>把内容包包装成来源接口（保持只有一个查表实现）。</summary>
    private sealed class BundleSource : INpcTemplateSource
    {
        public readonly NpcContentBundle Bundle;

        public BundleSource(NpcContentBundle bundle) => Bundle = bundle;

        public bool TryGet(int npcId, out NpcRuntimeConfig config)
        {
            if (Bundle.ByNpcId.TryGetValue(npcId, out var found))
            {
                config = found;
                return true;
            }

            config = null!;
            return false;
        }

        public IReadOnlyCollection<int> RegisteredNpcIds() => Bundle.ByNpcId.Keys;
    }
}

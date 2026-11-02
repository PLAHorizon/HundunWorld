using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim.Npcs;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// NPC 内容快照（<c>NpcContentBundle</c> 的可序列化形态）。
/// <para>
/// <b>为什么与怪物侧一样走"快照文件"而不是让 DS 直连数据库</b>：<c>Horizon.DsHost</c> 不引用 EF/SqlClient，
/// 把 EF 拖进权威进程会让"DS 起不起得来"取决于数据库可用性；而 NPC 内容属于**发布产物**
/// （编辑 → 校验 → 发布 → 导出），DS 只要拿到那份不可变快照就能确定性启动。
/// </para>
/// <para>生产链：<c>Horizon.NpcCli export-content --out …</c> 从 SQL Server 生成 →
/// 版本库/发布包 → DsHost 以 <c>--npc-content</c> 装入。开发期可带一份与种子脚本等价的 fixture。</para>
/// <para>
/// <b>本文件所有 DTO 一律用属性（<c>{ get; set; }</c>）而不是公开字段</b>：
/// <c>System.Text.Json</c> 默认不绑定公开字段，写成字段会得到"反序列化成功但全是初值"的静默空内容
/// （记忆 [STJ 默认不绑公开字段]）。
/// </para>
/// </summary>
public sealed class NpcContentSnapshot
{
    /// <summary>台词正文的库列宽度（<c>nvarchar(200)</c>，见 <c>scripts/sql/023_npc_gossip_text.sql</c>）。
    /// 编辑器、CLI 校验与写库共用这一个数字。</summary>
    public const int GossipTextMaxLength = 200;

    public int ContentRevision { get; set; } = 1;

    public List<NpcTemplateSnapshot> Templates { get; set; } = new();

    public List<NpcSpawnSnapshotDto> SpawnPoints { get; set; } = new();

    public List<NpcRouteSnapshot> Routes { get; set; } = new();

    public List<NpcVendorRowDto> VendorRows { get; set; } = new();

    public List<NpcTrainerRowDto> TrainerRows { get; set; } = new();

    public List<NpcRecipeSnapshotDto> Recipes { get; set; } = new();

    public List<NpcGossipRowDto> GossipRows { get; set; } = new();

    /// <summary>NPC 技能挂载（<c>Casts</c> 的数据源；表不存在时为空，见 024）。</summary>
    public List<NpcSkillMountDto> SkillMounts { get; set; } = new();

    public List<NpcQuestLinkDto> QuestLinks { get; set; } = new();

    public List<NpcScheduleRowDto> ScheduleRows { get; set; } = new();

    /// <summary>
    /// 掉落绑定（<b>复用怪物那套结构</b>，按 <c>monsterTemplateId = npc_id</c> 索引）。
    /// <para>敌对 NPC 可被击杀却"不掉东西"是 §12 A11 的缺口；这里让 NPC 内容自带掉落，
    /// 不必再让运营去怪物编辑器里给一个 NPC 编号配表。</para>
    /// </summary>
    public List<MonsterLootBindingSnapshotDto> LootBindings { get; set; } = new();

    /// <summary>导出/装配期发现的问题。<b>必须有这一栏</b>：
    /// "某行被跳过"与"库里本来就没有这行"在结果上同形，而前者的表现是"我配了，游戏里没有"。</summary>
    public List<string> Problems { get; set; } = new();

    /// <summary>
    /// "这一节库还没准备好，所以没读"的如实记录（例如掉落表尚未建）。
    /// 与 <see cref="Problems"/> 分开：Problems 会把导出判成失败，Skipped 只是**可见的缺口**——
    /// 019 未执行时把整份 NPC 导出判红，等于让 NPC 内容卡在一个与它无关的前置上。
    /// </summary>
    public List<string> Skipped { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, NpcSnapshotJson.Options);

    public static NpcContentSnapshot? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<NpcContentSnapshot>(json, NpcSnapshotJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>快照序列化选项（与怪物侧同口径：缩进、驼峰、忽略 null）。</summary>
public static class NpcSnapshotJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>NPC 模板快照。<b>AI 档位保留原始三件套</b>（档位 + 强开 + 强关），
/// 由装配期展开——快照里存展开结果会让"编辑器里改档位"看不出影响。</summary>
public sealed class NpcTemplateSnapshot
{
    public int NpcId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ModelKey { get; set; } = string.Empty;
    public string PrefabGuid { get; set; } = string.Empty;
    public float Scale { get; set; } = 1f;
    public int Level { get; set; } = 1;
    public int LevelVariance { get; set; }
    public byte Reaction { get; set; }
    public uint ServiceFlags { get; set; }
    public byte AiTier { get; set; }
    public uint AiBitsForceOn { get; set; }
    public uint AiBitsForceOff { get; set; }
    public int AttackableOverride { get; set; } = -1;
    public float SpeedWalk { get; set; } = 1.6f;
    public float SpeedRun { get; set; } = 4f;
    public int AlertRadiusMeters { get; set; } = NpcBehaviorCore.DefaultAlertRadiusMeters;
    public int GossipGroupId { get; set; }
    public byte Rank { get; set; }
    public int WuxingDomain { get; set; }
    public int KillCreditNpcId { get; set; }
    public int FactionGroup { get; set; }
}

public sealed class NpcSpawnSnapshotDto
{
    public int SpawnPointId { get; set; }
    public int ZoneId { get; set; }
    public long ShardId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public int NpcId { get; set; }
    public int LevelOverride { get; set; }
    public int MaxAlive { get; set; } = 1;
    public float RespawnSeconds { get; set; }
    public int RouteId { get; set; }
    public byte FacingMode { get; set; }
}

public sealed class NpcRouteSnapshot
{
    public int PathId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Mode { get; set; }
    public float PauseAtPointSeconds { get; set; }
    public bool UseNavMesh { get; set; } = true;
    public List<NpcRoutePointSnapshot> Points { get; set; } = new();
}

public sealed class NpcRoutePointSnapshot
{
    public int Seq { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float PauseSeconds { get; set; }
}

public sealed class NpcVendorRowDto
{
    public int NpcId { get; set; }
    public int Slot { get; set; }
    public int ItemId { get; set; }
    public long BuyPriceOverride { get; set; }
    public long SellPriceOverride { get; set; }
    public int StockMax { get; set; }
    public int RestockSeconds { get; set; }
    public int Category { get; set; }
}

public sealed class NpcTrainerRowDto
{
    public int NpcId { get; set; }
    public int SkillId { get; set; }
    public byte ServiceKind { get; set; }
    public long CostGold { get; set; }
    public int CostItemId { get; set; }
    public int CostItemCount { get; set; }
    public int Seq { get; set; }
}

public sealed class NpcRecipeSnapshotDto
{
    public int RecipeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProductItemId { get; set; }
    public long CostGold { get; set; }
    public int StationNpcId { get; set; }
    public int SkillReq { get; set; }
    public byte LearnSourceKind { get; set; } = 1;

    /// <summary>材料清单（导出口负责把表里的 JSON 解析成这一份；<b>解析失败要进 Problems</b>）。</summary>
    public List<NpcMaterialNeedDto> Materials { get; set; } = new();

    /// <summary>成功档技能差上限，<b>由易到难</b>（橙黄绿灰），与 <see cref="QualityChanceBps"/> 同序。</summary>
    public List<int> QualityGapCaps { get; set; } = new();

    public List<int> QualityChanceBps { get; set; } = new();
}

public sealed class NpcMaterialNeedDto
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}

public sealed class NpcGossipRowDto
{
    public int NpcId { get; set; }
    public int GroupId { get; set; }
    public int Seq { get; set; }
    public int TextId { get; set; }

    /// <summary>
    /// 台词正文（023 才有这一列）。空串 = 没配，客户端按 <see cref="ActionKind"/> 合成标签。
    /// <para>
    /// 读侧由 <c>NpcStore</c> 用**列存在性守卫**取：023 未执行时这一段整段跳过，
    /// 因此本属性有值不代表库里有列（导出里会带 <c>gossip_text=absent</c> 的回执）。
    /// </para>
    /// </summary>
    public string Text { get; set; } = string.Empty;

    public byte ActionKind { get; set; }
    public int ActionArg { get; set; }
    public int ReqMinLevel { get; set; }
    public int ReqMaxLevel { get; set; }
}

/// <summary>
/// NPC 技能挂载（一行 = 这只 NPC 会放的一个技能，对应 <c>Game_HunduShijie_NpcSkillMount</c>，见 024）。
/// <para>字段与怪物侧 <c>MonsterSkillMount</c> 同形且**同单位**（概率基点 / 吟唱毫秒 / 距离米），
/// 装配时一次换算，运行期不再各写一遍。</para>
/// </summary>
public sealed class NpcSkillMountDto
{
    public int NpcId { get; set; }
    public int SkillId { get; set; }
    public int SlotSeq { get; set; }

    /// <summary>0战斗 1非战斗 2残血应急 3受控时。</summary>
    public byte SlotType { get; set; }

    /// <summary>独立释放概率（基点 0..10000）。</summary>
    public int ChanceBps { get; set; } = 10000;

    public int CastMs { get; set; }
    public int CooldownMs { get; set; } = 5000;
    public float MinRange { get; set; }
    public float MaxRange { get; set; } = 8f;

    /// <summary>0当前威胁 1仇恨内随机 2最近 3血最低 4以目标为圆心 AOE。</summary>
    public byte TargetSelect { get; set; }

    public int TargetCount { get; set; } = 1;
    public float AoeRadius { get; set; }
    public int SchoolMask { get; set; }
    public byte Interruptible { get; set; } = 1;
    public byte AllowMovement { get; set; }

    /// <summary>伤害倍率基点（10000 = ×1）。</summary>
    public int DamageScaleBps { get; set; } = 10000;

    public int StatusId { get; set; }
    public float StatusDurationSeconds { get; set; }
    public int StartEmote { get; set; }

    /// <summary>残血阈值百分比（SlotType=2 专用）。</summary>
    public int EmergencyHpPercent { get; set; } = 30;
}

public sealed class NpcQuestLinkDto
{    public int NpcId { get; set; }
    public int QuestId { get; set; }

    /// <summary>1 发布 2 交付。</summary>
    public byte Role { get; set; }

    public int OrderSeq { get; set; }
}

public sealed class NpcScheduleRowDto
{
    public int NpcId { get; set; }
    public int Seq { get; set; }
    public int FromSeconds { get; set; }
    public int ToSeconds { get; set; }
    public int RouteId { get; set; }
    public byte Posture { get; set; }
    public int GossipGroupId { get; set; }
}

/// <summary>装配置回执（DsHost 启动时必须把它念出来：读到了不等于装上了）。</summary>
public sealed class NpcContentLoadReceipt
{
    public int Revision { get; set; }
    public int Templates { get; set; }
    public int SpawnPoints { get; set; }
    public int Routes { get; set; }
    public int VendorRows { get; set; }
    public int TrainerRows { get; set; }
    public int Recipes { get; set; }
    public int GossipRows { get; set; }
    public int QuestLinks { get; set; }
    public int ScheduleRows { get; set; }

    /// <summary>装配进来的掉落绑定条数（NPC 掉落独立于怪物内容）。</summary>
    public int LootBindings { get; set; }
    /// <summary>装配进来的技能挂载条数（NPC 施法的内容源；空 = 不会施法）。</summary>
    public int SkillMounts { get; set; }

    public int ProblemCount { get; set; }
    public List<string> Problems { get; set; } = new();

    /// <summary>因"开了位但内容为空"被摘掉的服务位个数（不是错误，是内容清洗的回执）。</summary>
    public int PrunedServiceBits { get; set; }

    /// <summary>逐条摘位明细（<c>service_bit_pruned:{npc}:0x{mask}</c>），供编辑器/运维对账。</summary>
    public List<string> PrunedServices { get; set; } = new();

    /// <summary>装配结果的一行 ASCII 自证（中文标签会吞掉紧随其后的数字，故键名全 ASCII）。</summary>
    public string Describe() =>
        $"npc_content_loaded rev={Revision} templates={Templates} spawns={SpawnPoints} routes={Routes} " +
        $"vendor_rows={VendorRows} trainer_rows={TrainerRows} recipes={Recipes} gossip_rows={GossipRows} " +
        $"quest_links={QuestLinks} schedule_rows={ScheduleRows} loot_bindings={LootBindings} " +
        $"skill_mounts={SkillMounts} svc_pruned={PrunedServiceBits} problems={ProblemCount}";
}

/// <summary>
/// NPC 内容快照装配器（快照 → <see cref="NpcContentBundle"/> → <c>NpcContent.Install</c>）。
/// <para>
/// <b>装配期就把档位展开、把列表按"数据顺序"排好、把坏行数进回执</b>：
/// 运行期每 tick 都在问"能不能打/走哪条路"，那里不该再出现档位查表或排序。
/// </para>
/// </summary>
public static class NpcContentLoader
{
    /// <summary>默认内容文件名（相对于 <c>HundunWorld/Data/Npcs/</c>）。</summary>
    public const string DefaultContentFileName = "db_content.json";

    /// <summary>默认内容目录（相对于仓库根）。</summary>
    public const string DefaultContentDirectory = "HundunWorld/Data/Npcs";

    /// <summary>
    /// 在给定目录里找默认内容快照（不抛异常、不联网）。返回空串 = 没找到。
    /// <para>把"探查清单"作为参数收进来，是为了让单测能对着临时目录逐条断言，
    /// 不必依赖本机仓库布局。</para>
    /// </summary>
    public static string ResolveDefaultPath(IReadOnlyList<string> probeDirectories)
    {
        var count = probeDirectories?.Count ?? 0;
        for (var i = 0; i < count; i++)
        {
            var dir = probeDirectories![i];
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, DefaultContentFileName);
            if (File.Exists(candidate)) return candidate;
        }

        return string.Empty;
    }

    /// <summary>
    /// 生产环境用的探查清单：从进程目录向上逐级找 <c>HundunWorld/Data/Npcs/</c>。
    /// <para>为什么默认就要装：不装的话 NPC 内容整条链是空的——刷点、货架、教学、作息全在快照里，
    /// 而服务默认不带 <c>--npc-content</c>，于是"编辑器里配好了、游戏里一个 NPC 也没有"，
    /// 且两边都不报错。装了也照常念回执，运维一眼能看到用的是哪份内容。</para>
    /// </summary>
    public static string ResolveDefaultPath()
    {
        var dirs = new List<string>();
        try
        {
            var dir = AppContext.BaseDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(dir); depth++)
            {
                // 两种部署形态：内容跟着二进制走，或内容留在仓库里（开发机）。
                dirs.Add(Path.Combine(dir, "Data", "Npcs"));
                dirs.Add(Path.Combine(dir, "HundunWorld", "Data", "Npcs"));
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch (Exception)
        {
            // 目录探查失败不该阻住启动：返回空串，调用方走"没有 NPC 内容"的既有分支。
        }

        return ResolveDefaultPath(dirs);
    }

    /// <summary>从文件装入。文件缺失/解析失败不抛，回 <c>null</c> 并带 error（DS 可以选择"没有 NPC 内容"继续跑）。</summary>
    public static NpcContentLoadReceipt? LoadFile(string path, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "path_empty";
            return null;
        }

        if (!File.Exists(path))
        {
            error = $"file_not_found:{path}";
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            error = $"read_failed:{ex.GetType().Name}";
            return null;
        }

        var snapshot = NpcContentSnapshot.FromJson(json);
        if (snapshot is null)
        {
            error = "json_invalid";
            return null;
        }

        var (bundle, receipt) = Build(snapshot);
        NpcContent.Install(bundle);
        return receipt;
    }

    /// <summary>
    /// 装配（纯内存，不装全局）：产出内容包 + 回执。<b>把"装配"与"生效"分开</b>，
    /// 单测可以反复跑装配而不污染全局状态。
    /// </summary>
    public static (NpcContentBundle Bundle, NpcContentLoadReceipt Receipt) Compile(NpcContentSnapshot snapshot)
        => Build(snapshot);

    private static (NpcContentBundle Bundle, NpcContentLoadReceipt Receipt) Build(NpcContentSnapshot snapshot)
    {
        var receipt = new NpcContentLoadReceipt { Revision = snapshot.ContentRevision };
        var problems = receipt.Problems;
        var bundle = new NpcContentBundle { ContentRevision = snapshot.ContentRevision };

        // 服务位的数据可用性要在模板之前算：位图是服务身份，开着却没有数据时
        // 客户端会显示入口、服务端只回拒绝（见 NpcServiceCore.PruneDataEmpty）。
        // 计数口径必须与下面各段"真正装进 bundle 的行"一致：坏货架行（ItemId<=0）不进包，也就不能算数据。
        var vendorCounts = CountByNpc(snapshot.VendorRows, static r => r.NpcId, static r => r.ItemId > 0);
        var skillRowCounts = CountByNpc(snapshot.TrainerRows, static r => r.NpcId, static r => r.ServiceKind == 1);
        var recipeRowCounts = CountByNpc(snapshot.TrainerRows, static r => r.NpcId, static r => r.ServiceKind == 2);

        // ── 模板：展开 AI 档位、拒绝坏立场、收死位 ──
        for (var i = 0; i < snapshot.Templates.Count; i++)
        {
            var t = snapshot.Templates[i];
            if (t.NpcId <= 0)
            {
                problems.Add($"template_bad_id:{t.NpcId}");
                continue;
            }

            if (bundle.ByNpcId.ContainsKey(t.NpcId))
            {
                problems.Add($"template_duplicate:{t.NpcId}");
                continue;
            }

            // 号段即身份：不在已登记号段里的编号一律拒绝装载。
            // 放行的话它会一路正常跑起来，直到有人按号段做统计/分组才发现它落在无人认领的洞里。
            if (!NpcIdSegmentCore.IsAllowed(t.NpcId))
            {
                problems.Add($"npc_id_out_of_segment:{t.NpcId}");
                continue;
            }

            // 敌对号段与服务号段的立场必须对得上：把山贼配成"友好"会让它站在服务号段的语义里，
            // 却永远打不到；反之把商人配成"敌对"会让玩家能砍掉一个本该提供服务的 NPC。
            var segment = NpcIdSegmentCore.SegmentOf(t.NpcId);
            if (segment == NpcIdSegment.Hostile && (NpcStance)t.Reaction != NpcStance.Hostile)
                problems.Add($"npc_segment_stance_mismatch:{t.NpcId}:{t.Reaction}");
            if (segment == NpcIdSegment.Service && (NpcStance)t.Reaction == NpcStance.Hostile)
                problems.Add($"npc_segment_stance_mismatch:{t.NpcId}:{t.Reaction}");

            var stance = (NpcStance)t.Reaction;
            if (!NpcReactionCore.IsStanceKnown(stance))
            {
                problems.Add($"template_bad_reaction:{t.NpcId}:{t.Reaction}");
            }

            var ai = NpcBehaviorCore.Resolve((NpcAiTier)t.AiTier, t.AiBitsForceOn, t.AiBitsForceOff);
            if (ai.ConflictBits != 0) problems.Add($"ai_force_conflict:{t.NpcId}:0x{ai.ConflictBits:X}");
            if (!ai.TierKnown) problems.Add($"ai_tier_unknown:{t.NpcId}:{t.AiTier}");

            var svcUnknown = NpcServiceCore.UnknownBits(t.ServiceFlags);
            if (svcUnknown != 0) problems.Add($"service_unknown_bits:{t.NpcId}:0x{svcUnknown:X}");

            // 数据为空的三个服务位在装配期摘掉（客户端入口与服务端裁决因此同源，见内核注释）。
            var prunedFlags = NpcServiceCore.PruneDataEmpty(
                t.ServiceFlags,
                CountOf(vendorCounts, t.NpcId),
                CountOf(skillRowCounts, t.NpcId),
                CountOf(recipeRowCounts, t.NpcId));
            if (prunedFlags != t.ServiceFlags)
            {
                // 计的是**位**不是 NPC：一只 NPC 可能同时被摘掉商人位与训练位。
                receipt.PrunedServiceBits += System.Numerics.BitOperations.PopCount(t.ServiceFlags & ~prunedFlags);
                receipt.PrunedServices.Add(
                    $"service_bit_pruned:{t.NpcId}:0x{t.ServiceFlags & ~prunedFlags:X}");
            }

            if (ai.Flags.Has(NpcAiBits.CallsReinforcement) && t.AlertRadiusMeters <= 0)
                problems.Add($"alert_radius_not_positive:{t.NpcId}");

            bundle.ByNpcId[t.NpcId] = new NpcRuntimeConfig
            {
                NpcId = t.NpcId,
                Name = t.Name,
                Title = t.Title,
                ModelKey = t.ModelKey,
                PrefabGuid = t.PrefabGuid,
                Scale = t.Scale <= 0 ? 1f : t.Scale,
                LevelBase = t.Level <= 0 ? 1 : t.Level,
                LevelVariance = t.LevelVariance,
                Stance = stance,
                ServiceFlags = prunedFlags,
                DeclaredServiceFlags = t.ServiceFlags,
                Flags = ai.Flags,
                Tier = (NpcAiTier)t.AiTier,
                AttackableOverride = t.AttackableOverride,
                SpeedWalk = t.SpeedWalk <= 0 ? 1.6f : t.SpeedWalk,
                SpeedRun = t.SpeedRun <= 0 ? 4f : t.SpeedRun,
                AlertRadiusMeters = t.AlertRadiusMeters,
                GossipGroupId = t.GossipGroupId,
                Rank = t.Rank,
                WuxingDomain = t.WuxingDomain,
                KillCreditNpcId = t.KillCreditNpcId,
                AiConflictBits = ai.ConflictBits,
                AiUnknownBits = NpcBehaviorCore.UnknownBits(t.AiBitsForceOn | t.AiBitsForceOff),
                ServiceUnknownBits = svcUnknown,
            };
        }

        receipt.Templates = bundle.ByNpcId.Count;

        // ── 刷点：指向未登记模板的刷点是"配了但刷不出来"，必须报 ──
        for (var i = 0; i < snapshot.SpawnPoints.Count; i++)
        {
            var s = snapshot.SpawnPoints[i];
            if (s.SpawnPointId <= 0) { problems.Add($"spawn_bad_id:{s.SpawnPointId}"); continue; }
            if (!bundle.ByNpcId.ContainsKey(s.NpcId)) { problems.Add($"spawn_unknown_npc:{s.SpawnPointId}:{s.NpcId}"); continue; }
            if (s.RouteId > 0 && snapshot.Routes.Find(r => r.PathId == s.RouteId) is null)
                problems.Add($"spawn_unknown_route:{s.SpawnPointId}:{s.RouteId}");

            bundle.SpawnPoints.Add(new NpcSpawnSnapshot(
                s.SpawnPointId, s.ZoneId, s.ShardId, s.X, s.Y, s.Z, s.Yaw,
                s.NpcId, s.LevelOverride, s.MaxAlive <= 0 ? 1 : s.MaxAlive,
                s.RespawnSeconds, s.RouteId, s.FacingMode));
        }

        receipt.SpawnPoints = bundle.SpawnPoints.Count;

        // ── 路线 ──
        for (var i = 0; i < snapshot.Routes.Count; i++)
        {
            var r = snapshot.Routes[i];
            if (r.PathId <= 0) { problems.Add($"route_bad_id:{r.PathId}"); continue; }

            var points = new List<Horizon.Game.Sim.Monsters.MonsterPatrolPoint>(r.Points.Count);
            for (var p = 0; p < r.Points.Count; p++)
            {
                var pt = r.Points[p];
                points.Add(new Horizon.Game.Sim.Monsters.MonsterPatrolPoint(pt.X, pt.Y, pt.Z, pt.PauseSeconds));
            }

            if (points.Count == 0) problems.Add($"route_no_points:{r.PathId}");

            bundle.Paths[r.PathId] = new NpcPathSnapshot(r.PathId, r.Mode, r.PauseAtPointSeconds, r.UseNavMesh, points);
        }

        receipt.Routes = bundle.Paths.Count;

        // ── 货架：按 slot 升序（顺序是数据，装配时排一次） ──
        var vendorGroups = new Dictionary<int, List<NpcVendorRow>>();
        for (var i = 0; i < snapshot.VendorRows.Count; i++)
        {
            var v = snapshot.VendorRows[i];
            if (!bundle.ByNpcId.ContainsKey(v.NpcId)) { problems.Add($"vendor_unknown_npc:{v.NpcId}"); continue; }
            if (v.ItemId <= 0) { problems.Add($"vendor_bad_item:{v.NpcId}:{v.ItemId}"); continue; }
            if (v.StockMax > 0 && v.RestockSeconds <= 0)
                problems.Add($"vendor_stock_never_restocks:{v.NpcId}:{v.ItemId}");
            // 经济字段的**结构**校验（不查物品表，纯内容关系）：
            // 负数库存/补货是坏数据；两头都给了正价却"卖得比买得贵"等于让 NPC 白送钱。
            if (v.StockMax < 0) problems.Add($"vendor_bad_stock:{v.NpcId}:{v.ItemId}:{v.StockMax}");
            if (v.RestockSeconds < 0) problems.Add($"vendor_bad_restock:{v.NpcId}:{v.ItemId}:{v.RestockSeconds}");
            if (v.BuyPriceOverride < 0 || v.SellPriceOverride < 0)
                problems.Add($"vendor_bad_price:{v.NpcId}:{v.ItemId}:{v.BuyPriceOverride}:{v.SellPriceOverride}");
            if (v.BuyPriceOverride > 0 && v.SellPriceOverride > v.BuyPriceOverride)
                problems.Add($"vendor_sell_above_buy:{v.NpcId}:{v.ItemId}:{v.SellPriceOverride}>{v.BuyPriceOverride}");

            if (!vendorGroups.TryGetValue(v.NpcId, out var list))
            {
                list = new List<NpcVendorRow>();
                vendorGroups[v.NpcId] = list;
            }

            list.Add(new NpcVendorRow(v.ItemId, v.Slot, v.BuyPriceOverride, v.SellPriceOverride, v.StockMax, v.RestockSeconds));
        }

        foreach (var kv in vendorGroups)
        {
            kv.Value.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
            bundle.VendorRows[kv.Key] = kv.Value;
            receipt.VendorRows += kv.Value.Count;
        }

        // ── 教学行：按 seq 升序 ──
        var trainerGroups = new Dictionary<int, List<NpcTrainerRow>>();
        for (var i = 0; i < snapshot.TrainerRows.Count; i++)
        {
            var t = snapshot.TrainerRows[i];
            if (!bundle.ByNpcId.ContainsKey(t.NpcId)) { problems.Add($"trainer_unknown_npc:{t.NpcId}"); continue; }

            var row = new NpcTrainerRow(t.SkillId, (NpcTrainerService)t.ServiceKind, t.CostGold, t.CostItemId, t.CostItemCount, t.Seq);
            if (!row.IsUsable) problems.Add($"trainer_bad_row:{t.NpcId}:{t.SkillId}");

            if (!trainerGroups.TryGetValue(t.NpcId, out var list))
            {
                list = new List<NpcTrainerRow>();
                trainerGroups[t.NpcId] = list;
            }

            list.Add(row);
        }

        foreach (var kv in trainerGroups)
        {
            NpcTrainerCore.SortBySeq(kv.Value);
            bundle.TrainerRows[kv.Key] = kv.Value;
            receipt.TrainerRows += kv.Value.Count;
        }

        // ── 技能挂载：按 slot_seq 升序（顺序是数据，平局时先放哪个由它决定） ──
        var mountGroups = new Dictionary<int, List<Horizon.Game.Sim.Monsters.MonsterSkillMount>>();
        var mountSeqs = new Dictionary<int, List<int>>();
        for (var i = 0; i < snapshot.SkillMounts.Count; i++)
        {
            var m = snapshot.SkillMounts[i];
            if (!bundle.ByNpcId.ContainsKey(m.NpcId)) { problems.Add($"skill_mount_unknown_npc:{m.NpcId}"); continue; }
            if (m.SkillId <= 0) { problems.Add($"skill_mount_bad_skill:{m.NpcId}:{m.SkillId}"); continue; }
            if (m.ChanceBps < 0 || m.ChanceBps > Horizon.Game.Sim.Monsters.MonsterCastingCore.ChanceBasisPointsMax)
                problems.Add($"skill_mount_bad_chance:{m.NpcId}:{m.SkillId}:{m.ChanceBps}");
            if (m.SlotType > (byte)Horizon.Game.Sim.Monsters.MonsterSkillSlot.OnControlled)
                problems.Add($"skill_mount_bad_slot:{m.NpcId}:{m.SkillId}:{m.SlotType}");

            var mount = new Horizon.Game.Sim.Monsters.MonsterSkillMount
            {
                SkillId = m.SkillId,
                Slot = (Horizon.Game.Sim.Monsters.MonsterSkillSlot)m.SlotType,
                ChanceBasisPoints = m.ChanceBps,
                CastMs = m.CastMs,
                CooldownMs = m.CooldownMs,
                MinRange = m.MinRange,
                MaxRange = m.MaxRange,
                TargetSelect = (Horizon.Game.Sim.Monsters.MonsterTargetSelect)m.TargetSelect,
                TargetCount = m.TargetCount,
                AoeRadius = m.AoeRadius,
                SchoolMask = m.SchoolMask,
                Interruptible = m.Interruptible != 0,
                AllowMovement = m.AllowMovement != 0,
                DamageScale = m.DamageScaleBps > 0 ? m.DamageScaleBps / 10000f : 1f,
                StatusId = m.StatusId,
                StatusDurationSeconds = m.StatusDurationSeconds,
                StartEmoteId = m.StartEmote,
                EmergencyHpPercent = m.EmergencyHpPercent,
            };

            if (!mountGroups.TryGetValue(m.NpcId, out var list))
            {
                list = new List<Horizon.Game.Sim.Monsters.MonsterSkillMount>();
                mountGroups[m.NpcId] = list;
                mountSeqs[m.NpcId] = new List<int>();
            }

            list.Add(mount);
            mountSeqs[m.NpcId].Add(m.SlotSeq);
        }

        foreach (var kv in mountGroups)
        {
            var seqs = mountSeqs[kv.Key];
            // 与教学行同一个排序纪律：按 seq 稳定排（数组下标即挂载下标，运行期冷却表按它索引）。
            var order = new int[kv.Value.Count];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => seqs[a] != seqs[b] ? seqs[a].CompareTo(seqs[b]) : a.CompareTo(b));
            var sorted = new Horizon.Game.Sim.Monsters.MonsterSkillMount[order.Length];
            for (var i = 0; i < order.Length; i++) sorted[i] = kv.Value[order[i]];

            bundle.SkillMounts[kv.Key] = sorted;
            receipt.SkillMounts += sorted.Length;
        }

        // ── 配方 ──
        for (var i = 0; i < snapshot.Recipes.Count; i++)
        {
            var r = snapshot.Recipes[i];
            if (r.RecipeId <= 0) { problems.Add($"recipe_bad_id:{r.RecipeId}"); continue; }
            if (r.ProductItemId <= 0) problems.Add($"recipe_bad_product:{r.RecipeId}");
            if (r.Materials.Count == 0) problems.Add($"recipe_no_materials:{r.RecipeId}");

            var mats = new NpcMaterialNeed[r.Materials.Count];
            for (var m = 0; m < mats.Length; m++) mats[m] = new NpcMaterialNeed(r.Materials[m].ItemId, r.Materials[m].Count);

            var curve = new NpcCraftChanceCurve(r.QualityGapCaps.ToArray(), r.QualityChanceBps.ToArray());
            if (!curve.IsUsable) problems.Add($"recipe_bad_curve:{r.RecipeId}");

            bundle.Recipes[r.RecipeId] = new NpcCraftRecipeSnapshot
            {
                RecipeId = r.RecipeId,
                Name = r.Name,
                ProductItemId = r.ProductItemId,
                CostGold = r.CostGold,
                StationNpcId = r.StationNpcId,
                SkillReq = r.SkillReq,
                LearnSourceKind = r.LearnSourceKind,
                Materials = mats,
                Curve = curve,
            };
        }

        receipt.Recipes = bundle.Recipes.Count;

        // ── 台词：按组号分组、组内按 seq（同一组内顺序是玩家看到的菜单顺序） ──
        var gossipGroups = new Dictionary<int, List<NpcGossipOption>>();
        for (var i = 0; i < snapshot.GossipRows.Count; i++)
        {
            var g = snapshot.GossipRows[i];
            if (!bundle.ByNpcId.ContainsKey(g.NpcId)) { problems.Add($"gossip_unknown_npc:{g.NpcId}"); continue; }
            if (!string.IsNullOrEmpty(g.Text) && g.Text.Length > NpcContentSnapshot.GossipTextMaxLength)
                problems.Add($"gossip_text_too_long:{g.NpcId}:{g.Seq}");

            var opt = new NpcGossipOption(g.Seq, g.GroupId, g.TextId, (NpcGossipAction)g.ActionKind, g.ActionArg, g.ReqMinLevel, g.ReqMaxLevel);

            // 动作引用了没开的服务位 = 点了开空窗：装配期就得报，别等玩家点。
            if (bundle.ByNpcId.TryGetValue(g.NpcId, out var cfg) && !NpcGossipCore.ActionServiceAvailable(in opt, cfg.ServiceFlags))
                problems.Add($"gossip_action_bit_off:{g.NpcId}:{(byte)g.ActionKind}");

            if (!gossipGroups.TryGetValue(g.GroupId, out var list))
            {
                list = new List<NpcGossipOption>();
                gossipGroups[g.GroupId] = list;
            }

            list.Add(opt);
        }

        foreach (var kv in gossipGroups)
        {
            kv.Value.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));
            bundle.GossipGroups[kv.Key] = kv.Value;
            receipt.GossipRows += kv.Value.Count;
        }

        // ── 任务关系（倒排索引） ──
        for (var i = 0; i < snapshot.QuestLinks.Count; i++)
        {
            var l = snapshot.QuestLinks[i];
            if (!bundle.ByNpcId.ContainsKey(l.NpcId)) { problems.Add($"questlink_unknown_npc:{l.NpcId}"); continue; }
            if (l.QuestId <= 0) { problems.Add($"questlink_bad_quest:{l.NpcId}:{l.QuestId}"); continue; }
            if (l.Role is not (1 or 2)) { problems.Add($"questlink_bad_role:{l.NpcId}:{l.Role}"); continue; }

            // 链接与服务位必须成对：挂了"发布/交付"却没有对应位，客户端会画出 `!`/`?` 而服务端一律拒绝，
            // 表现就是"看得见任务、接不了/交不了"（2026-11-01 的 400002 与 90006 就是这一对不一致）。
            var linkedConfig = bundle.ByNpcId[l.NpcId];
            if (l.Role == 1 && (linkedConfig.ServiceFlags & (uint)NpcServiceBits.QuestGiver) == 0)
                problems.Add($"questlink_giver_without_service:{l.NpcId}:{l.QuestId}");
            if (l.Role == 2 && (linkedConfig.ServiceFlags & (uint)NpcServiceBits.QuestTurnIn) == 0)
                problems.Add($"questlink_turnin_without_service:{l.NpcId}:{l.QuestId}");

            if (!bundle.QuestLinks.TryGetValue(l.NpcId, out var list))
            {
                list = new List<NpcQuestLinkSnapshot>();
                bundle.QuestLinks[l.NpcId] = list;
            }

            list.Add(new NpcQuestLinkSnapshot(l.QuestId, l.Role == 1, l.Role == 2, l.OrderSeq));
        }

        foreach (var kv in bundle.QuestLinks) receipt.QuestLinks += kv.Value.Count;

        // ── 作息：按 seq 排序；覆盖普查只在"有洞"时报（不做成硬错误，作息可以故意留空档） ──
        var scheduleGroups = new Dictionary<int, List<NpcScheduleSlot>>();
        for (var i = 0; i < snapshot.ScheduleRows.Count; i++)
        {
            var s = snapshot.ScheduleRows[i];
            if (!bundle.ByNpcId.ContainsKey(s.NpcId)) { problems.Add($"schedule_unknown_npc:{s.NpcId}"); continue; }

            if (!scheduleGroups.TryGetValue(s.NpcId, out var list))
            {
                list = new List<NpcScheduleSlot>();
                scheduleGroups[s.NpcId] = list;
            }

            list.Add(new NpcScheduleSlot(s.FromSeconds, s.ToSeconds, s.RouteId, (NpcPosture)s.Posture, s.GossipGroupId));
        }

        foreach (var kv in scheduleGroups)
        {
            kv.Value.Sort(static (a, b) => a.FromSeconds.CompareTo(b.FromSeconds));
            bundle.ScheduleSlots[kv.Key] = kv.Value;
            receipt.ScheduleRows += kv.Value.Count;

            var uncovered = NpcScheduleCore.UncoveredSeconds(kv.Value, NpcScheduleCore.DefaultDayCycleSeconds);
            if (uncovered > 0)
                problems.Add($"schedule_gap:{kv.Key}:{uncovered}s");
        }

        // ── 掉落绑定（按 npc_id 分组；结构和怪物侧同一款）──
        for (var i = 0; i < snapshot.LootBindings.Count; i++)
        {
            var binding = snapshot.LootBindings[i];
            if (!bundle.ByNpcId.ContainsKey(binding.MonsterTemplateId))
            {
                // 绑定挂在不存在的 NPC 上 = "配了掉落但没人会掉"，必须报。
                problems.Add($"loot_binding_unknown_npc:{binding.MonsterTemplateId}");
                continue;
            }

            if (!bundle.LootBindings.TryGetValue(binding.MonsterTemplateId, out var list))
            {
                list = new List<Horizon.Game.Sim.Monsters.MonsterLootBindingSnapshot>();
                bundle.LootBindings[binding.MonsterTemplateId] = list;
            }

            list.Add(binding.ToSnapshot());
        }

        foreach (var list in bundle.LootBindings.Values) receipt.LootBindings += list.Count;

        receipt.ProblemCount = problems.Count;
        return (bundle, receipt);
    }

    /// <summary>按 NPC 计数（可选谓词），供服务位的数据可用性判据使用。</summary>
    private static Dictionary<int, int> CountByNpc<T>(
        List<T> rows, System.Func<T, int> npcId, System.Func<T, bool>? predicate = null)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (predicate != null && !predicate(row)) continue;
            var id = npcId(row);
            counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1;
        }

        return counts;
    }

    private static int CountOf(Dictionary<int, int> counts, int npcId)
        => counts.TryGetValue(npcId, out var n) ? n : 0;
}

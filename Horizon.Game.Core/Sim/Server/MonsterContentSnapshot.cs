using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim.Monsters;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 怪物内容快照（<c>MonsterContentBundle</c> 的可序列化形态）。
/// <para>
/// <b>为什么走"快照文件"而不是让 DS 直连数据库</b>：<c>Horizon.DsHost</c> 现在不引用 EF/SqlClient，
/// 把 EF 拖进权威进程会让"DS 起不起得来"取决于数据库可用性；而刷怪内容属于**发布产物**
/// （编辑 → 校验 → 发布 → 导出），DS 只要拿到那一份不可变快照就能确定性启动。
/// Silo 侧同理可用：<c>MonsterContentLoader.LoadFile</c> 谁都调得了。
/// </para>
/// <para>生产链：<c>Horizon.MonsterCli export-content --out …</c> 从 SQL Server 生成 → 版本库/发布包 →
/// DsHost 以 <c>--monster-content</c> 装入。开发期带一份与种子脚本等价的 fixture（见 Data/Monsters/）。</para>
/// </summary>
public sealed class MonsterContentSnapshot
{
    public int ContentRevision { get; set; } = 1;

    public List<MonsterTemplateSnapshot> Templates { get; set; } = new();

    public List<MonsterSpawnSnapshotDto> SpawnPoints { get; set; } = new();

    public List<MonsterRouteSnapshot> Routes { get; set; } = new();

    public List<MonsterLootBindingSnapshotDto> LootBindings { get; set; } = new();

    /// <summary>
    /// 喊话/起手文本（草稿 + 快照文件这一层，**库里没有这张表**）。
    /// <para>为什么不走库：文本是展示物，与外观表同一口径（客户端按内容包旁挂的
    /// <c>client_texts.json</c> 解析），线上只发文本号；
    /// 库里那一格 <c>aggro_say_id</c> 只存"念第几号"。</para>
    /// </summary>
    public List<MonsterTextSnapshotDto> Texts { get; set; } = new();

    /// <summary>
    /// 导出/构建期发现的问题（不参与运行期判据，只随快照带出来给人看）。
    /// <para>必须有这一栏：装配时"某行被跳过"与"库里本来就没有这行"在结果上同形，
    /// 而前者的表现是"我在编辑器里配了，游戏里没有"。</para>
    /// </summary>
    public List<string> Problems { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static MonsterContentSnapshot? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MonsterContentSnapshot>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>装配成运行期内容包（整份构造，成功后才替换 —— 半份装配会让 tick 读到混合内容）。</summary>
    public MonsterContentBundle ToBundle(out List<string> problems)
    {
        problems = new List<string>();
        var bundle = new MonsterContentBundle { ContentRevision = ContentRevision };

        foreach (var template in Templates)
        {
            var config = template.ToConfig(problems);
            if (config == null) continue;
            if (bundle.ByTemplateId.ContainsKey(config.TemplateId))
            {
                problems.Add($"模板 {config.TemplateId} 重复出现，后者被忽略");
                continue;
            }
            bundle.ByTemplateId[config.TemplateId] = config;
        }

        foreach (var route in Routes)
        {
            var built = route.ToRoute();
            if (built == null)
            {
                // 以前这里是 `continue` 而不留任何话：一条只画了一个点的巡逻线**静默消失**，
                // 而引用它的行为表照旧装配成功——策划看到的还是"我配了线，怪不走"。
                // 拒收的判据本身住在 `MonsterRouteSnapshot.ToRoute`（点数 < 2 或 id 非法），这里只念出来。
                problems.Add($"巡逻线 {route.RouteId} 被丢弃：至少要两个点（实得 {route.Points?.Count ?? 0} 个）" +
                             "——引用它的那只怪会退回按 wander_radius 游荡");
                continue;
            }
            bundle.Routes[built.Value.RouteId] = built.Value;
        }

        foreach (var spawn in SpawnPoints)
        {
            if (!bundle.ByTemplateId.ContainsKey(spawn.MonsterTemplateId))
            {
                // 引用完整性：点位指向一只不存在的怪 = 这个点整片不刷怪，且没有任何错误日志。
                problems.Add($"刷怪点 {spawn.SpawnPointId} 引用未登记模板 {spawn.MonsterTemplateId}（已跳过该点）");
                continue;
            }
            bundle.SpawnPoints.Add(spawn.ToSnapshot());
        }

        foreach (var binding in LootBindings)
        {
            if (!bundle.LootBindings.TryGetValue(binding.MonsterTemplateId, out var list))
            {
                list = new List<MonsterLootBindingSnapshot>();
                bundle.LootBindings[binding.MonsterTemplateId] = list;
            }
            list.Add(binding.ToSnapshot());
        }

        foreach (var text in Texts)
        {
            if (!bundle.ByTemplateId.ContainsKey(text.MonsterTemplateId))
            {
                problems.Add($"文本 {text.SayId} 挂在未登记的模板 {text.MonsterTemplateId}（已跳过该条）");
                continue;
            }
            if (text.SayId <= 0)
            {
                // 0 是"没配"的哨兵（行为表那一格 0 = 不念）；让它进表会让所有配了 0 的怪
                // 都能"指到一条空文本"，那条引用检查就永远查不出东西。
                problems.Add($"模板 {text.MonsterTemplateId} 的文本号非法（{text.SayId}）：0 是没配的哨兵，不许占一条");
                continue;
            }
            if (!bundle.Texts.TryGetValue(text.MonsterTemplateId, out var texts))
            {
                texts = new List<MonsterTextSnapshot>();
                bundle.Texts[text.MonsterTemplateId] = texts;
            }
            texts.Add(text.ToSnapshot());
        }

        // 巡逻线的引用完整性：指针配了、指针那头没有这条线（上面那条只管"线自己不合格"）。
        // 装配期不说，运行期就是"怪安静地不沿线上走"，而那与"这一格没配"在屏幕上长得一模一样。
        // 两条通路（v1 与 v2）现在都会读这条线，所以这一格也不再是"只有开了能力位的怪才关心"。
        foreach (var content in bundle.ByTemplateId.Values)
        {
            var routeId = content.Behavior.PatrolRouteId;
            if (routeId > 0 && !bundle.Routes.ContainsKey(routeId))
                problems.Add($"模板 {content.TemplateId}：行为表指向巡逻线 {routeId}，而这份内容包里没有它" +
                             "（这只怪会退回按 wander_radius 游荡）");
            // 喊话号与巡逻线是同一个形状的悬空引用：以前 `aggro_say_id` 只有"这一格没人读"那一句警告，
            // 现在文本表存在了，就得能问出"你指的这一号在不在"。判据与运行期共用 bundle 那一份。
            if (content.AggroSayId > 0 && !bundle.HasText(content.TemplateId, content.AggroSayId))
                problems.Add($"模板 {content.TemplateId}：行为表起手喊话指向文本 {content.AggroSayId}，" +
                             "而这份内容包里没有它（进战时什么都不会念）");
        }
        foreach (var spawn in bundle.SpawnPoints)
        {
            if (spawn.RouteOverrideId > 0 && !bundle.Routes.ContainsKey(spawn.RouteOverrideId))
                problems.Add($"刷怪点 {spawn.SpawnPointId}：路线覆盖指向 {spawn.RouteOverrideId}，而这份内容包里没有它");
        }

        return bundle;
    }
}

/// <summary>模板快照（含行为与技能挂载 —— 一只怪的全部发布内容）。</summary>
public sealed class MonsterTemplateSnapshot
{
    public int MonsterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int Species { get; set; }
    public int WuxingDomain { get; set; }
    public string ModelKey { get; set; } = string.Empty;
    public string? PrefabGuid { get; set; }
    public float ModelScale { get; set; } = 1f;
    public int StandState { get; set; }
    public int LevelBase { get; set; } = 1;
    public int LevelMin { get; set; } = 1;
    public int LevelMax { get; set; } = 1;
    public int AggroSayId { get; set; }
    public float CorpseSeconds { get; set; } = 60f;
    /// <summary>尸体可拾取窗口秒（AC 缺省 60；可逐怪配置）。</summary>
    public float LootWindowSeconds { get; set; } = 60f;
    /// <summary>拾取交互距离米（AC INTERACTION_DISTANCE 缺省 5.5）。</summary>
    public float PickupDistanceMeters { get; set; } = 5.5f;
    public MonsterStatsSnapshot Stats { get; set; } = new();
    public MonsterXpSnapshot Xp { get; set; } = new();
    public float RespawnBaseSeconds { get; set; } = 15f;
    public float RespawnPerLevelSeconds { get; set; }
    public MonsterBehaviorSnapshot Behavior { get; set; } = new();
    public List<MonsterSkillSnapshot> Skills { get; set; } = new();

    internal MonsterRuntimeConfig? ToConfig(List<string> problems)
    {
        if (MonsterId <= 0)
        {
            problems.Add("存在编号非法（<=0）的模板，已跳过");
            return null;
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add($"模板 {MonsterId} 名称为空：客户端会退回 Monster_{MonsterId} 显示");
        }
        if (LevelMin > LevelMax)
        {
            problems.Add($"模板 {MonsterId} 等级区间非法（{LevelMin}>{LevelMax}），已按基准等级收拢");
            LevelMin = LevelBase;
            LevelMax = LevelBase;
        }

        var behavior = Behavior.ToConfig(problems, MonsterId);
        var config = new MonsterRuntimeConfig
        {
            TemplateId = MonsterId,
            Name = Name,
            Rank = (MonsterRank)Math.Clamp(Rank, 0, 4),
            Species = Species,
            WuxingDomain = WuxingDomain,
            ModelKey = ModelKey,
            PrefabGuid = PrefabGuid,
            ModelScale = ModelScale <= 0f ? 1f : ModelScale,
            StandState = StandState,
            LevelBase = LevelBase < 1 ? 1 : LevelBase,
            LevelMin = LevelMin,
            LevelMax = LevelMax,
            AggroSayId = AggroSayId,
            CorpseSeconds = CorpseSeconds,
            LootWindowSeconds = LootWindowSeconds,
            PickupDistanceMeters = PickupDistanceMeters,
            Stats = Stats.ToConfig(),
            Xp = Xp.ToConfig(),
            RespawnBaseSeconds = RespawnBaseSeconds,
            RespawnPerLevelSeconds = RespawnPerLevelSeconds,
            Behavior = behavior,
        };

        // 技能挂载必须按 SlotSeq 升序：内核按下标遍历并用下标做掷骰盐值，
        // 顺序变了同一天同一 tick 的施法选择就变了（金轨迹会红，但红得很难解释）。
        var sorted = new List<MonsterSkillSnapshot>(Skills);
        sorted.Sort((a, b) =>
        {
            var bySeq = a.SlotSeq.CompareTo(b.SlotSeq);
            return bySeq != 0 ? bySeq : a.SkillId.CompareTo(b.SkillId);
        });
        var mounts = new MonsterSkillMount[sorted.Count];
        for (var i = 0; i < sorted.Count; i++) mounts[i] = sorted[i].ToMount();
        config.Skills = mounts;
        return config;
    }
}

/// <summary>属性公式快照。</summary>
public sealed class MonsterStatsSnapshot
{
    public int HpBase { get; set; } = 100;
    public int HpPerLevel { get; set; }
    public int AtkBase { get; set; } = 10;
    public int AtkPerLevel { get; set; }
    public int DefBase { get; set; }
    public int DefPerLevel { get; set; }
    public int HpOverrideBps { get; set; }
    public int AtkOverrideBps { get; set; }

    internal MonsterStatsConfig ToConfig() => new MonsterStatsConfig
    {
        HpBase = HpBase,
        HpPerLevel = HpPerLevel,
        AtkBase = AtkBase,
        AtkPerLevel = AtkPerLevel,
        DefBase = DefBase,
        DefPerLevel = DefPerLevel,
        HpOverrideBps = HpOverrideBps,
        AtkOverrideBps = AtkOverrideBps,
    };
}

/// <summary>经验系数快照。</summary>
public sealed class MonsterXpSnapshot
{
    public int XpBase { get; set; } = 45;
    public int XpPerLevel { get; set; } = 5;

    /// <summary>逐模板经验倍率基点（10000=×1）；魔兽的 CreatureTemplate::ModExperience。</summary>
    public int XpModBps { get; set; } = 10000;

    /// <summary>
    /// "血薄削减"基点（魔兽的 CreatureTemplate::ModHealth）；<b>只有 0..7500 生效</b>，
    /// 缺省 10000 = 不削减。旧文件里没这个键时落 10000，与加这一列之前逐字相同。
    /// </summary>
    public int HealthModBps { get; set; } = 10000;

    internal MonsterXpConfig ToConfig() => new MonsterXpConfig(
        XpBase, XpPerLevel, Horizon.Game.Sim.Monsters.MonsterXpConfig.DefaultRankScale(),
        XpModBps, HealthModBps);
}

/// <summary>行为快照（能力位用可空整数：0 覆盖关 / 1 覆盖开 / null 跟档位）。</summary>
public sealed class MonsterBehaviorSnapshot
{
    public int AiProfile { get; set; }
    public int? FlagUseThreat { get; set; }
    public int? FlagUseSkills { get; set; }
    public int? FlagUseNavMesh { get; set; }
    public int? FlagUseAssist { get; set; }
    public int? FlagUseTactics { get; set; }
    public int IsAggressive { get; set; } = 1;
    public float AggroRadius { get; set; } = 10f;
    public float AggroConeDegrees { get; set; } = 360f;
    public float LeashRadius { get; set; } = 30f;
    public float EvadeImmunitySeconds { get; set; } = 3f;
    public float AssistRadius { get; set; } = 10f;
    public int DetectMask { get; set; } = 1;
    public float WanderRadius { get; set; } = 6f;
    public float IdleToPatrolSeconds { get; set; } = 3f;
    public float WalkSpeed { get; set; } = 2.5f;
    public float MoveSpeed { get; set; } = 4f;
    public int FollowType { get; set; }
    public float MeleeSwingSeconds { get; set; } = 1.5f;
    public float RangedMinDistance { get; set; }
    public float RangedMaxDistance { get; set; }
    public int FleeHpPercent { get; set; }
    public int PatrolRouteId { get; set; }

    /// <summary>能力位展开（档位 + 逐位覆盖）走唯一主人 <c>MonsterAiFlags.Resolve</c>。</summary>
    /// <remarks>公开它是因为回执也要问"这只怪到底会不会施法"：导出回执以前只数模板/刷怪点/路线/掉落，
    /// 技能挂载整段掉在回执之外——那正是"库里有、包里没有"最难发现的一种形状。
    /// 让它在这里算一次，而不是每个消费者自己再抄一遍档位表（抄了就会与内核静默分叉）。</remarks>
    internal MonsterAiFlags ResolveFlags() => MonsterAiFlags.Resolve(
        (MonsterAiProfile)Math.Clamp(AiProfile, 0, 3),
        FlagUseThreat, FlagUseSkills, FlagUseNavMesh, FlagUseAssist, FlagUseTactics);

    internal MonsterBehaviorConfig ToConfig(List<string> problems, int monsterId)
    {
        var cfg = MonsterBehaviorConfig.Default;
        cfg.Flags = ResolveFlags();
        cfg.IsAggressive = IsAggressive != 0;
        cfg.AggroRadius = AggroRadius;
        cfg.AggroConeDegrees = AggroConeDegrees <= 0f ? 360f : AggroConeDegrees;
        cfg.LeashRadius = LeashRadius;
        cfg.EvadeImmunitySeconds = EvadeImmunitySeconds;
        cfg.AssistRadius = AssistRadius;
        cfg.DetectMask = DetectMask;
        cfg.WanderRadius = WanderRadius;
        cfg.IdleToPatrolSeconds = IdleToPatrolSeconds;
        cfg.WalkSpeed = WalkSpeed <= 0f ? 2.5f : WalkSpeed;
        cfg.MoveSpeed = MoveSpeed <= 0f ? 4f : MoveSpeed;
        cfg.FollowType = (MonsterFollowType)Math.Clamp(FollowType, 0, 3);
        cfg.MeleeSwingSeconds = MeleeSwingSeconds <= 0f ? 1.5f : MeleeSwingSeconds;
        cfg.RangedMinDistance = RangedMinDistance;
        cfg.RangedMaxDistance = RangedMaxDistance;
        cfg.FleeHpPercent = Math.Clamp(FleeHpPercent, 0, 100);
        cfg.PatrolRouteId = PatrolRouteId;

        // 装配期就把"配了但互相矛盾"的项说出来，而不是留到运行期用行为表现去发现。
        if (cfg.LeashRadius > 0f && cfg.AggroRadius > cfg.LeashRadius)
            problems.Add($"模板 {monsterId}：索敌半径 {cfg.AggroRadius} 大于脱战半径 {cfg.LeashRadius}，怪会索敌即回位");
        // 索敌半径不是正数 ⇒ 非法值（2026-10-27 拍板：0 既不是"关闭这一格"也不是"没配、交给兜底"）。
        // 作者侧校验器已经拒收，这一侧管的是手改过的快照文件与旧导出产物：把话说破，
        // 并如实写明运行期会怎样（主人是 MonsterAiGate.AggroRadiusUsable），
        // 而不是在这里补一个凭空出现的距离——那正是两条 AI 通路此前不一致的来源。
        if (cfg.AggroRadius <= 0f)
            problems.Add($"模板 {monsterId}：索敌半径 {cfg.AggroRadius} 不是正数（非法值），运行期这只怪不会主动索敌；" +
                         "不想让它先手请配 is_aggressive=0，想让它索敌请把半径填成正数");
        if (cfg.Flags.UseSkills && PatrolRouteId > 0)
        {
            // 不是错误，只是提醒：技能与路线各自独立生效，别把"挂了路线"当成"会施法"
        }
        return cfg;
    }
}

/// <summary>技能挂载快照。</summary>
public sealed class MonsterSkillSnapshot
{
    public int SkillId { get; set; }
    public int SlotSeq { get; set; }
    public int SlotType { get; set; }
    public int ChanceBps { get; set; } = 10000;
    public int CastMs { get; set; }
    public int CooldownMs { get; set; } = 5000;
    public float MinRange { get; set; }
    public float MaxRange { get; set; } = 8f;
    public int TargetSelect { get; set; }
    public int TargetCount { get; set; } = 1;
    public float AoeRadius { get; set; }
    public int SchoolMask { get; set; }
    public int Interruptible { get; set; } = 1;
    public int AllowMovement { get; set; }
    public int DamageScaleBps { get; set; } = 10000;
    public int StatusId { get; set; }
    public float StatusDurationSeconds { get; set; }

    /// <summary>状态强度（基点；今天只有减速用，0 = 没填、不生效）。</summary>
    public int StatusMagnitudeBps { get; set; }
    public int StartEmote { get; set; }
    public int EmergencyHpPercent { get; set; } = 30;

    internal MonsterSkillMount ToMount() => new MonsterSkillMount
    {
        SkillId = SkillId,
        Slot = (MonsterSkillSlot)Math.Clamp(SlotType, 0, 3),
        ChanceBasisPoints = Math.Clamp(ChanceBps, 0, MonsterCastingCore.ChanceBasisPointsMax),
        CastMs = CastMs < 0 ? 0 : CastMs,
        CooldownMs = CooldownMs < 0 ? 0 : CooldownMs,
        MinRange = MinRange,
        MaxRange = MaxRange <= 0f ? 8f : MaxRange,
        TargetSelect = (MonsterTargetSelect)Math.Clamp(TargetSelect, 0, 4),
        TargetCount = TargetCount < 1 ? 1 : TargetCount,
        AoeRadius = AoeRadius,
        SchoolMask = SchoolMask,
        Interruptible = Interruptible != 0,
        AllowMovement = AllowMovement != 0,
        DamageScale = DamageScaleBps > 0 ? DamageScaleBps / 10000f : 1f,
        StatusId = StatusId,
        StatusDurationSeconds = StatusDurationSeconds,
        StatusMagnitudeBps = StatusMagnitudeBps,
        StartEmoteId = StartEmote,
        EmergencyHpPercent = Math.Clamp(EmergencyHpPercent, 0, 100),
    };
}

/// <summary>路线快照。</summary>
public sealed class MonsterRouteSnapshot
{
    public int RouteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Mode { get; set; } = 1;
    public float PauseAtPointSeconds { get; set; } = 2f;
    public int UseNavMesh { get; set; } = 1;
    public List<MonsterPointSnapshot> Points { get; set; } = new();

    internal MonsterPatrolRoute? ToRoute()
    {
        if (RouteId <= 0 || Points.Count < 2) return null;
        var sorted = new List<MonsterPointSnapshot>(Points);
        sorted.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        var pts = new MonsterPatrolPoint[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            var p = sorted[i];
            pts[i] = new MonsterPatrolPoint(p.X, p.Y, p.Z, p.PauseSeconds ?? -1f);
        }
        return new MonsterPatrolRoute
        {
            RouteId = RouteId,
            Mode = (MonsterPatrolMode)Math.Clamp(Mode, 0, 2),
            PauseAtPointSeconds = PauseAtPointSeconds < 0f ? 0f : PauseAtPointSeconds,
            UseNavMesh = UseNavMesh != 0,
            Points = pts,
        };
    }
}

/// <summary>巡逻点快照。</summary>
public sealed class MonsterPointSnapshot
{
    public int Seq { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float? PauseSeconds { get; set; }
}

/// <summary>刷怪点快照。</summary>
public sealed class MonsterSpawnSnapshotDto
{
    public int SpawnPointId { get; set; }
    public int ZoneId { get; set; }
    public long ShardId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public int MonsterTemplateId { get; set; }
    public int? LevelOverride { get; set; }
    public int MaxAlive { get; set; } = 3;
    public float SlotSpacing { get; set; } = 8f;
    public float RespawnOverrideSeconds { get; set; }
    public int RouteOverrideId { get; set; }

    internal MonsterSpawnSnapshot ToSnapshot() => new MonsterSpawnSnapshot(
        SpawnPointId, ZoneId, ShardId, X, Y, Z, Yaw, MonsterTemplateId,
        LevelOverride, MaxAlive < 1 ? 1 : MaxAlive,
        SlotSpacing <= 0f ? 8f : SlotSpacing, RespawnOverrideSeconds, RouteOverrideId);
}

/// <summary>喊话/起手文本快照（草稿与快照文件里的形状）。</summary>
public sealed class MonsterTextSnapshotDto
{
    public int MonsterTemplateId { get; set; }
    public int SayId { get; set; }
    public int Group { get; set; }
    public int Seq { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Emote { get; set; }

    internal MonsterTextSnapshot ToSnapshot() => new MonsterTextSnapshot
    {
        SayId = SayId,
        Group = Group,
        Seq = Seq,
        Text = Text ?? string.Empty,
        Emote = Emote,
    };
}

/// <summary>掉落绑定快照。</summary>
public sealed class MonsterLootBindingSnapshotDto
{
    public int MonsterTemplateId { get; set; }
    public int LootTableId { get; set; }
    public int RollChanceBps { get; set; }
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public int ConditionKind { get; set; }
    public int QuestId { get; set; }
    public int MoneyMin { get; set; }
    public int MoneyMax { get; set; }
    public List<MonsterDropSnapshotDto> Entries { get; set; } = new();

    /// <summary>
    /// 转成运行期快照：逐件条目排序、概率与数量区间收拢。
    /// <para>公开是因为命令行预览需要同一条转换路径——它以前自己抄了一遍"逐件概率相加"，
    /// 加了互斥组以后那份抄件会系统性高估（同组每次最多出一件）。</para>
    /// </summary>
    public MonsterLootBindingSnapshot ToSnapshot()
    {
        // 逐件条目必须按「掷骰顺序」升序：内核按下标遍历，并把下标当掷骰盐值的一部分。
        // 库里的 seq 列注释写的就是"掷骰顺序"，而它以前在这一层被丢掉了——
        // 于是同一个绑定在查询返回顺序变化时（加一行、改索引、换执行计划）会让每件物品换一颗骰子，
        // 掉率随之整体漂移，而编辑器、库、快照三处的数字都没变。
        // 排序键不含组号，规则本身住在内核里（只有一个主人）。
        var entries = new MonsterDropSnapshot[Entries.Count];
        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            entries[i] = new MonsterDropSnapshot(e.ItemId, Math.Clamp(e.ChanceBps, 0, 10000),
                e.CountMin < 1 ? 1 : e.CountMin, e.CountMax < 1 ? e.CountMin : e.CountMax, e.Seq, e.GroupId,
                // 条件收拢到 -1..3：-1 是"继承绑定"，0..3 是四种已知条件；再往上的值内核只会走
                // "判不了→不给"那一档，但留着会让库里一个手滑的 99 永远查不出是谁写的。
                Math.Clamp(e.ConditionKind, -1, 3), e.QuestId < 0 ? 0 : e.QuestId);
        }
        MonsterLootCore.SortEntriesForRoll(entries);
        return new MonsterLootBindingSnapshot(LootTableId, RollChanceBps, MinCount, MaxCount,
            ConditionKind, QuestId, MoneyMin, MoneyMax, entries);
    }
}

/// <summary>逐件掉落快照。</summary>
public sealed class MonsterDropSnapshotDto
{
    public int ItemId { get; set; }
    public int ChanceBps { get; set; } = 1000;
    public int CountMin { get; set; } = 1;
    public int CountMax { get; set; } = 1;

    /// <summary>掷骰顺序（与库列 <c>seq</c> 同名同义）；装配时按它升序排，下标即盐值。</summary>
    public int Seq { get; set; }

    /// <summary>
    /// 掉落组号（与库列 <c>group_id</c> 同名同义）：0 = 逐件独立掷，&gt;0 = 同组互斥抽一件。
    /// <para>缺省必须是 0——旧快照文件里没有这个键，反序列化后落到初值 0，
    /// 掉率与加组之前逐字相同；若初值写成 -1 之类，老数据会被划进一个不存在的组而整体不掉。</para>
    /// </summary>
    public int GroupId { get; set; }

    /// <summary>
    /// 这一件自己的掉落条件（与库列 <c>condition_kind</c> 同名同义）：<b>-1 = 继承所属绑定</b>。
    /// <para>缺省必须是 -1 而不是 0：0 恰好就是"无条件"，而旧快照文件里根本没有这个键，
    /// 那样一来所有任务物品都会变成"人人可拿"，而编辑器、库、文件三处都看不出区别。</para>
    /// </summary>
    public int ConditionKind { get; set; } = -1;

    /// <summary>条件=任务持有者时的小任务 id（与库列 <c>quest_id</c> 同名同义）；0 = 用绑定上那一份。</summary>
    public int QuestId { get; set; }
}

/// <summary>装配回执。</summary>
public sealed class MonsterContentLoadReceipt
{
    public bool Ok { get; set; }
    public string Path { get; set; } = string.Empty;
    public int ContentRevision { get; set; }
    public int Templates { get; set; }
    public int SpawnPoints { get; set; }
    public int Routes { get; set; }
    public int LootBindings { get; set; }
    public List<string> Problems { get; set; } = new();

    /// <summary>
    /// 一行自证文本（DS 启动日志里要看得见装了几条，"文件读到了"不等于"内容装上了"）。
    /// <para><b>问题必须逐条念出来，不能只报个数</b>：实测生产包在 DS 侧是 `问题=1`，
    /// 而 CLI 的导出回执里 <c>problems</c> 是空的——DS 校验的是**快照文件**、CLI 校验的是**库里的草稿**，
    /// 两份清单本来就不同源。只报个数时，那条差异在 DS 日志里查不出是什么，
    /// 于是"怪的表现与编辑器里不一样"永远归因不到内容包这一层（与"配了没人读"同族）。</para>
    /// </summary>
    public string Describe()
    {
        var text = string.Format(
            "怪物内容 rev={0} 模板={1} 刷怪点={2} 路线={3} 掉落绑定={4} 问题={5}",
            ContentRevision, Templates, SpawnPoints, Routes, LootBindings, Problems.Count);
        if (Problems.Count == 0) return text;
        // 上限 3 条：这一行还要装得下计数，全念出来会让启动日志的一条摘要变成一堵墙。
        const int cap = 3;
        var head = string.Join("；", Problems.GetRange(0, Math.Min(cap, Problems.Count)));
        return Problems.Count > cap
            ? text + " 明细=" + head + string.Format(" …另有 {0} 条未列出", Problems.Count - cap)
            : text + " 明细=" + head;
    }
}

/// <summary>
/// 怪物内容快照装载器。
/// </summary>
public static class MonsterContentLoader
{
    /// <summary>
    /// 从 JSON 文件装入内容包并安装为进程内权威源。
    /// <para><b>大声失败</b>：文件缺失/格式坏/内容为空都返回 <c>Ok=false</c> 并带问题清单，
    /// 由宿主决定是回退硬编码目录还是拒绝启动。绝不"静默装成空内容"——那会让所有怪都退回
    /// Lv1/100 血的兜底模板，表现是"怪突然变软"，没人会归因到快照文件。</para>
    /// </summary>
    public static MonsterContentLoadReceipt LoadFile(string path)
    {
        var receipt = new MonsterContentLoadReceipt { Path = path ?? string.Empty };
        if (string.IsNullOrWhiteSpace(path))
        {
            receipt.Problems.Add("未提供怪物内容快照路径");
            return receipt;
        }
        if (!File.Exists(path))
        {
            receipt.Problems.Add("快照文件不存在：" + path);
            return receipt;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            receipt.Problems.Add("快照读取失败：" + ex.GetBaseException().Message);
            return receipt;
        }

        var snapshot = MonsterContentSnapshot.FromJson(json);
        if (snapshot == null)
        {
            receipt.Problems.Add("快照 JSON 解析失败");
            return receipt;
        }
        if (snapshot.Templates.Count == 0)
        {
            receipt.Problems.Add("快照里没有任何模板（拒绝安装空内容；如需退回代码目录请不要给 --monster-content）");
            return receipt;
        }

        var bundle = snapshot.ToBundle(out var problems);
        receipt.Problems.AddRange(problems);
        receipt.ContentRevision = bundle.ContentRevision;
        receipt.Templates = bundle.ByTemplateId.Count;
        receipt.SpawnPoints = bundle.SpawnPoints.Count;
        receipt.Routes = bundle.Routes.Count;
        foreach (var list in bundle.LootBindings.Values) receipt.LootBindings += list.Count;

        if (bundle.ByTemplateId.Count == 0)
        {
            receipt.Problems.Add("全部模板都被跳过，未安装");
            return receipt;
        }

        MonsterContent.Install(bundle);
        receipt.Ok = true;
        return receipt;
    }
}

/// <summary>
/// 导航网格文本装载器（决策 D5 一期：高度图网格）。
/// <para>格式（一行一格，行优先，Y 从 0 起）：
/// <c>header: MONSTERNAV &lt;width&gt; &lt;height&gt; &lt;cellSize&gt; &lt;originX&gt; &lt;originY&gt;</c>，
/// 之后 <c>height</c> 行、每行 <c>width</c> 个整数代价（0=阻挡，10=标准）。
/// 一期由工具/PCG 侧导出；文件缺失时 DS 只是走直线，不影响启动。</para>
/// </summary>
public static class MonsterNavGridLoader
{
    public const string HeaderToken = "MONSTERNAV";

    /// <summary>
    /// 缺省网格文件名（**真场景烘焙**的产物名）。
    /// <para>刻意<b>不</b>用 `dev_nav.grid`：那份是 32×32、4 米格、全可走的玩具图（只为跑通通路），
    /// 自动探到它会让"没烘真图"看起来像"装好了"——怪照旧走得动，而它绕过什么、绕不过什么全是假的。</para>
    /// </summary>
    public const string DefaultFileName = "monster_nav.grid";

    /// <summary>按给定目录清单找缺省网格（测试用；找不到返回空串）。</summary>
    public static string ResolveDefaultPath(IReadOnlyList<string> probeDirectories)
    {
        var count = probeDirectories?.Count ?? 0;
        for (var i = 0; i < count; i++)
        {
            var dir = probeDirectories![i];
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, DefaultFileName);
            if (File.Exists(candidate)) return candidate;
        }
        return string.Empty;
    }

    /// <summary>
    /// 生产环境用的探查清单：从进程目录向上逐级找 <c>Data/Monsters/monster_nav.grid</c>。
    /// <para>与 NPC/怪物内容同一条纪律：内容要求走图（<c>flagUseNavMesh</c>）而宿主没装网格时，
    /// 内核安静退回直线——"编辑器里勾了走图、怪照样穿墙"且没人报错。探到就装并念回执；
    /// 探不到也不报错（直线是合法退路），但摘要里 <c>导航缺图=</c> 那一格会把它说出口。</para>
    /// </summary>
    public static string ResolveDefaultPath()
    {
        var dirs = new List<string>();
        try
        {
            var dir = AppContext.BaseDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(dir); depth++)
            {
                dirs.Add(Path.Combine(dir, "Data", "Monsters"));
                dirs.Add(Path.Combine(dir, "HundunWorld", "Data", "Monsters"));
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch
        {
            return string.Empty;
        }
        return ResolveDefaultPath(dirs);
    }

    /// <summary>读网格；失败返回无效网格（<c>IsValid == false</c>）并给出原因。</summary>
    public static MonsterNavGrid Load(string path, out string error)
    {
        error = string.Empty;
        var empty = new MonsterNavGrid();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "导航网格文件不存在：" + path;
            return empty;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) { error = "空文件"; return empty; }
            var header = lines[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length < 6 || header[0] != HeaderToken)
            {
                error = "首行不是 MONSTERNAV 头";
                return empty;
            }
            var w = int.Parse(header[1], CultureInfo.InvariantCulture);
            var h = int.Parse(header[2], CultureInfo.InvariantCulture);
            var cell = float.Parse(header[3], CultureInfo.InvariantCulture);
            var ox = float.Parse(header[4], CultureInfo.InvariantCulture);
            var oy = float.Parse(header[5], CultureInfo.InvariantCulture);
            if (w <= 0 || h <= 0 || cell <= 0f)
            {
                error = "头部数值非法";
                return empty;
            }
            if (lines.Length < 1 + h)
            {
                error = string.Format("行数不足：期望 {0} 行格数据，实得 {1}", h, lines.Length - 1);
                return empty;
            }

            var costs = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                var parts = lines[1 + y].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < w)
                {
                    error = string.Format("第 {0} 行只有 {1} 个值（期望 {2}）", y + 1, parts.Length, w);
                    return empty;
                }
                for (var x = 0; x < w; x++)
                {
                    var v = int.Parse(parts[x], CultureInfo.InvariantCulture);
                    costs[y * w + x] = (byte)Math.Clamp(v, 0, 255);
                }
            }

            return new MonsterNavGrid
            {
                Width = w,
                Height = h,
                CellSize = cell,
                OriginX = ox,
                OriginY = oy,
                CellCost = costs,
            };
        }
        catch (Exception ex)
        {
            error = "导航网格解析失败：" + ex.GetBaseException().Message;
            return empty;
        }
    }

    /// <summary>
    /// 装载回执那一行（宿主只负责 <c>Log</c>，判据在这里）。
    /// <para>为什么要抽出来：DsHost 的 <c>Program.Main</c> 里的日志语句没法被测试调用，
    /// 于是"空图要自己承认等于没装"这一半只能靠**文本针**钉——而文本针挡不住"字符串还在、
    /// 条件被改成永不成立"那种坏法（本轮反证 F2 就是这么跑绿的）。抽成纯函数之后
    /// 三种读数（没装 / 装了且挡得住 / 装了但什么都没挡住）各断一条。</para>
    /// </summary>
    public static string DescribeLoad(Horizon.Game.Sim.Monsters.MonsterNavGrid grid, string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return "导航网格未装入（全部走直线）：" + error;
        }
        var census = grid.Census();
        return string.Format("导航网格已装入：{0}x{1} cell={2:0.##}m 不可走格={3}/{4} 难行格={5}{6}",
            grid.Width, grid.Height, grid.CellSize, census.Blocked, census.Total, census.Hard,
            census.Blocked == 0
                ? "（这张图一个格子都没挡住 ⇒ 开了导航位的怪仍然只会走直线，等价于没装）"
                : string.Empty);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Horizon.Entities;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Horizon.MonsterCli;

/// <summary>一次写入的结果（与 QuestCli 的 StoreResult 同形，编辑器解析代码因此可以复用）。</summary>
public sealed class MonsterStoreResult
{
    public bool Ok { get; set; }
    public int MonsterId { get; set; }
    public int ContentVersion { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();

    /// <summary>本次写入实际落库的行数（每个写方都要自带状态位：只回 ok=true 说不清"写了几张表"）。</summary>
    public Dictionary<string, int> WrittenRows { get; set; } = new();
}

/// <summary>列表行摘要。</summary>
public sealed class MonsterListRow
{
    public int MonsterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int LevelBase { get; set; }
    public int WuxingDomain { get; set; }
    public int PublishState { get; set; }
    public int ContentVersion { get; set; }
    public int SkillCount { get; set; }
    public int LootBindingCount { get; set; }
    public int SpawnPointCount { get; set; }
    public int PatrolRouteId { get; set; }
}

/// <summary>
/// 怪物表的数据访问；同时是校验器的引用对账实现。
/// <para>
/// <b>写顺序 = 引用完整性顺序</b>：模板 → 行为 → 技能 → 掉落 → 路线 → 刷怪点。
/// 反过来写会让"刷怪点指向一个还不存在的编号"这一刻真实存在，而运行时只读已发布行，
/// 于是那个点整片不刷怪且没有任何日志。
/// </para>
/// </summary>
public sealed class MonsterStore : IMonsterReferenceIndex, IDisposable
{
    private readonly GameEntityContext _db;

    public MonsterStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new GameEntityContext(options);
        // 解析连接串里的 Database 而不是等连接打开：回执要在"连都没连"时也能说出目标库名。
        // 这条信息是给"以为在导排练库、其实导的是生产库"那类事故用的——回执不念库名，
        // 两个库导出的回执长得一模一样。
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        DatabaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "(未指定)" : builder.InitialCatalog;
    }

    /// <summary>本 store 连的库名（回执里逐条念；不念的话"导的是哪个库"只能靠人回忆环境变量）。</summary>
    public string DatabaseName { get; }

    /// <summary>主表是否可达（用来区分"没配怪"与"库还没迁移"——这两件事在只读侧长得一模一样）。</summary>
    public bool TemplateTableReachable(out string error)
    {
        try
        {
            _ = _db.MonsterTemplates.Count();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    // ---------- 引用索引（校验器要用） ----------

    public HashSet<int> SkillIds() => _db.SkillTemplates.AsNoTracking().Select(s => s.Id).ToHashSet();
    public HashSet<int> ItemIds() => _db.ItemTemplates.AsNoTracking().Select(s => s.Id).ToHashSet();
    public HashSet<int> QuestIds() => _db.QuestTemplates.AsNoTracking().Select(s => s.Id).ToHashSet();
    public IReadOnlyDictionary<int, int> QuestPublishStates()
        => _db.QuestTemplates.AsNoTracking().ToDictionary(s => s.Id, s => s.PublishState);
    public HashSet<int> MonsterIds() => _db.MonsterTemplates.AsNoTracking().Select(s => s.Id).ToHashSet();

    // ---------- 读 ----------

    public List<MonsterListRow> List()
    {
        var rows = _db.MonsterTemplates.AsNoTracking().OrderBy(r => r.Id).ToList();
        var behaviors = _db.MonsterBehaviors.AsNoTracking().ToDictionary(r => r.Id, r => r);
        var skillCounts = _db.MonsterSkillMounts.AsNoTracking()
            .GroupBy(m => m.MonsterId).ToDictionary(g => g.Key, g => g.Count());
        var lootCounts = _db.MonsterLootBindings.AsNoTracking()
            .GroupBy(m => m.MonsterId).ToDictionary(g => g.Key, g => g.Count());
        var spawnCounts = _db.MonsterSpawnPoints.AsNoTracking()
            .GroupBy(m => m.MonsterId).ToDictionary(g => g.Key, g => g.Count());

        var outRows = new List<MonsterListRow>(rows.Count);
        foreach (var r in rows)
        {
            behaviors.TryGetValue(r.Id, out var b);
            outRows.Add(new MonsterListRow
            {
                MonsterId = r.Id,
                Name = r.Name,
                Rank = r.Rank,
                LevelBase = r.LevelBase,
                WuxingDomain = r.WuxingDomain,
                PublishState = r.PublishState,
                ContentVersion = r.ContentVersion,
                SkillCount = skillCounts.TryGetValue(r.Id, out var sc) ? sc : 0,
                LootBindingCount = lootCounts.TryGetValue(r.Id, out var lc) ? lc : 0,
                SpawnPointCount = spawnCounts.TryGetValue(r.Id, out var spc) ? spc : 0,
                PatrolRouteId = b?.PatrolRouteId ?? 0,
            });
        }
        return outRows;
    }

    /// <summary>把六张表拼回一份草稿（编辑器的"打开"走这里）。</summary>
    public MonsterDraft? FindDraft(int monsterId)
    {
        var t = _db.MonsterTemplates.AsNoTracking().FirstOrDefault(r => r.Id == monsterId);
        if (t == null) return null;

        var draft = new MonsterDraft
        {
            MonsterId = t.Id,
            Name = t.Name,
            Rank = t.Rank,
            Species = t.Species,
            WuxingDomain = t.WuxingDomain,
            ModelKey = t.ModelKey,
            PrefabGuid = t.PrefabGuid,
            PrefabPath = t.PrefabPath,
            ModelScale = (double)t.ModelScale,
            StandState = t.StandState,
            LevelBase = t.LevelBase,
            LevelMin = t.LevelMin,
            LevelMax = t.LevelMax,
            RespawnBaseSeconds = t.RespawnBaseSeconds,
            RespawnPerLevelSeconds = t.RespawnPerLevelSeconds,
            CorpseSeconds = t.CorpseSeconds,
            LootWindowSeconds = t.LootWindowSeconds,
            PickupDistanceMeters = t.PickupDistanceMeters,
            AggroSayId = t.AggroSayId,
            PublishState = t.PublishState,
            ContentVersion = t.ContentVersion,
            Remark = t.Remark,
            Stats = new MonsterStatsDraft
            {
                HpBase = t.HpBase,
                HpPerLevel = t.HpPerLevel,
                AtkBase = t.AtkBase,
                AtkPerLevel = t.AtkPerLevel,
                DefBase = t.DefBase,
                DefPerLevel = t.DefPerLevel,
                HpOverrideBps = t.HpOverrideBps,
                AtkOverrideBps = t.AtkOverrideBps,
            },
            Xp = new MonsterXpDraft
            {
                XpBase = t.XpBase, XpPerLevel = t.XpPerLevel,
                XpModBps = t.XpModBps, HealthModBps = t.HealthModBps,
            },
        };

        var b = _db.MonsterBehaviors.AsNoTracking().FirstOrDefault(r => r.Id == monsterId);
        if (b != null)
        {
            draft.Behavior = new MonsterBehaviorDraft
            {
                AiProfile = b.AiProfile,
                FlagUseThreat = b.FlagUseThreat,
                FlagUseSkills = b.FlagUseSkills,
                FlagUseNavMesh = b.FlagUseNavMesh,
                FlagUseAssist = b.FlagUseAssist,
                FlagUseTactics = b.FlagUseTactics,
                IsAggressive = b.IsAggressive,
                AggroRadius = b.AggroRadius,
                AggroConeDegrees = b.AggroConeDegrees,
                LeashRadius = b.LeashRadius,
                EvadeImmunitySeconds = b.EvadeImmunitySeconds,
                AssistRadius = b.AssistRadius,
                DetectMask = b.DetectMask,
                WanderRadius = b.WanderRadius,
                IdleToPatrolSeconds = b.IdleToPatrolSeconds,
                WalkSpeed = b.WalkSpeed,
                MoveSpeed = b.MoveSpeed,
                FollowType = b.FollowType,
                MeleeSwingSeconds = b.MeleeSwingSeconds,
                RangedMinDistance = b.RangedMinDistance,
                RangedMaxDistance = b.RangedMaxDistance,
                FleeHpPercent = b.FleeHpPercent,
                PatrolRouteId = b.PatrolRouteId,
            };
        }

        draft.Skills = _db.MonsterSkillMounts.AsNoTracking()
            .Where(m => m.MonsterId == monsterId).OrderBy(m => m.SlotSeq).Select(m => new MonsterSkillDraft
            {
                SkillId = m.SkillId,
                SlotSeq = m.SlotSeq,
                SlotType = m.SlotType,
                ChanceBps = m.ChanceBps,
                CastMs = m.CastMs,
                CooldownMs = m.CooldownMs,
                MinRange = m.MinRange,
                MaxRange = m.MaxRange,
                TargetSelect = m.TargetSelect,
                TargetCount = m.TargetCount,
                AoeRadius = m.AoeRadius,
                SchoolMask = m.SchoolMask,
                Interruptible = m.Interruptible,
                AllowMovement = m.AllowMovement,
                DamageScaleBps = m.DamageScaleBps,
                StatusId = m.StatusId,
                StatusDurationSeconds = m.StatusDurationSeconds,
                StatusMagnitudeBps = m.StatusMagnitudeBps,
                StartEmote = m.StartEmote,
                EmergencyHpPercent = m.EmergencyHpPercent,
            }).ToList();

        var bindings = _db.MonsterLootBindings.AsNoTracking()
            .Where(l => l.MonsterId == monsterId).OrderBy(l => l.Id).ToList();
        var entryMap = _db.MonsterDropEntries.AsNoTracking().ToList()
            .GroupBy(e => e.BindingId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());
        draft.LootBindings = bindings.Select(l => new MonsterLootBindingDraft
        {
            LootTableId = l.LootTableId,
            RollChanceBps = l.RollChanceBps,
            MinCount = l.MinCount,
            MaxCount = l.MaxCount,
            ConditionKind = l.ConditionKind,
            QuestId = l.QuestId,
            MoneyMin = l.MoneyMin,
            MoneyMax = l.MoneyMax,
            Entries = entryMap.TryGetValue(l.Id, out var es)
                ? es.Select(e => new MonsterDropDraft
                {
                    ItemId = e.ItemId, ChanceBps = e.ChanceBps,
                    CountMin = e.CountMin, CountMax = e.CountMax, Seq = e.Seq, GroupId = e.GroupId,
                    ConditionKind = e.ConditionKind, QuestId = e.QuestId,
                }).ToList()
                : new List<MonsterDropDraft>(),
        }).ToList();

        var routeId = draft.Behavior.PatrolRouteId;
        if (routeId > 0)
        {
            var route = _db.MonsterPatrolRoutes.AsNoTracking().FirstOrDefault(r => r.Id == routeId);
            if (route != null)
            {
                draft.PatrolRoute = new MonsterRouteDraft
                {
                    RouteId = route.Id,
                    Name = route.Name,
                    Mode = route.Mode,
                    PauseAtPointSeconds = route.PauseAtPointSeconds,
                    UseNavMesh = route.UseNavMesh,
                    Points = _db.MonsterPatrolPoints.AsNoTracking()
                        .Where(p => p.RouteId == routeId).OrderBy(p => p.Seq)
                        .Select(p => new MonsterPointDraft
                        {
                            Seq = p.Seq, X = p.X, Y = p.Y, Z = p.Z, PauseSeconds = p.PauseSeconds,
                        }).ToList(),
                };
            }
        }

        draft.SpawnPoints = _db.MonsterSpawnPoints.AsNoTracking()
            .Where(sp => sp.MonsterId == monsterId).OrderBy(sp => sp.Id).Select(sp => new MonsterSpawnDraft
            {
                SpawnPointId = sp.Id,
                ZoneId = sp.ZoneId,
                ShardId = sp.ShardId,
                X = sp.X,
                Y = sp.Y,
                Z = sp.Z,
                Yaw = sp.Yaw,
                LevelOverride = sp.LevelOverride,
                MaxAlive = sp.MaxAlive,
                SlotSpacing = sp.SlotSpacing,
                RespawnOverrideSeconds = sp.RespawnOverrideSeconds,
                RouteOverrideId = sp.RouteOverrideId,
                Enabled = sp.Enabled,
            }).ToList();

        return draft;
    }

    // ---------- 写 ----------

    /// <summary>
    /// 落库一份草稿（模板 + 行为 + 技能 + 掉落 + 路线 + 刷怪点，六张表一起成或一起不成）。
    /// </summary>
    /// <remarks>
    /// 用显式事务：半份草稿进库会让"编辑器的保存成功"与"运行时的内容"分家，
    /// 而分家的表现是"改了没反应"，最难查。
    /// </remarks>
    public MonsterStoreResult Upsert(MonsterDraft draft)
    {
        var result = new MonsterStoreResult { MonsterId = draft.MonsterId };
        using var tx = _db.Database.BeginTransaction();
        try
        {
            // --- 1. 模板 ---
            var template = _db.MonsterTemplates.Local.FirstOrDefault(r => r.Id == draft.MonsterId)
                           ?? _db.MonsterTemplates.FirstOrDefault(r => r.Id == draft.MonsterId);
            var isNew = template == null;
            if (isNew)
            {
                template = new MonsterTemplateEntity { Id = draft.MonsterId, ContentVersion = 0 };
                _db.MonsterTemplates.Add(template);
            }
            else if (draft.ContentVersion > 0 && template.ContentVersion != draft.ContentVersion)
            {
                // 乐观锁：编辑器拿的是旧版本，不能默默覆盖别人的改动。
                result.Ok = false;
                result.Message = $"内容版本冲突：库里是 v{template.ContentVersion}，草稿是 v{draft.ContentVersion}。请重新载入后再保存。";
                return result;
            }

            template.Name = draft.Name;
            template.Rank = (byte)Math.Clamp(draft.Rank, 0, 4);
            template.Species = (byte)Math.Clamp(draft.Species, 0, 7);
            template.WuxingDomain = draft.WuxingDomain;
            template.ModelKey = draft.ModelKey;
            template.PrefabGuid = string.IsNullOrWhiteSpace(draft.PrefabGuid) ? null : draft.PrefabGuid;
            template.PrefabPath = string.IsNullOrWhiteSpace(draft.PrefabPath) ? null : draft.PrefabPath.Trim();
            template.ModelScale = (decimal)draft.ModelScale;
            template.StandState = (byte)Math.Clamp(draft.StandState, 0, 3);
            template.LevelBase = draft.LevelBase;
            template.LevelMin = draft.LevelMin;
            template.LevelMax = draft.LevelMax;
            template.HpBase = draft.Stats.HpBase;
            template.HpPerLevel = draft.Stats.HpPerLevel;
            template.AtkBase = draft.Stats.AtkBase;
            template.AtkPerLevel = draft.Stats.AtkPerLevel;
            template.DefBase = draft.Stats.DefBase;
            template.DefPerLevel = draft.Stats.DefPerLevel;
            template.HpOverrideBps = draft.Stats.HpOverrideBps;
            template.AtkOverrideBps = draft.Stats.AtkOverrideBps;
            template.XpBase = draft.Xp.XpBase;
            template.XpPerLevel = draft.Xp.XpPerLevel;
            template.XpModBps = draft.Xp.XpModBps;
            template.HealthModBps = draft.Xp.HealthModBps;
            template.RespawnBaseSeconds = (float)draft.RespawnBaseSeconds;
            template.RespawnPerLevelSeconds = (float)draft.RespawnPerLevelSeconds;
            template.CorpseSeconds = (float)draft.CorpseSeconds;
            template.LootWindowSeconds = (float)draft.LootWindowSeconds;
            template.PickupDistanceMeters = (float)draft.PickupDistanceMeters;
            template.AggroSayId = draft.AggroSayId;
            template.Remark = draft.Remark;
            // publish_state 只在这里"降级"，不在 save 里"升级"：发布必须走 publish 动词，
            // 否则每次保存都自动上线（PublishState 在草稿保存路径上被留成旧值正是这个意思）。
            if (isNew) template.PublishState = (byte)Math.Clamp(draft.PublishState, 0, 2);
            template.ContentVersion = template.ContentVersion + 1;
            result.ContentVersion = template.ContentVersion;
            result.WrittenRows["template"] = 1;

            // --- 2. 行为 ---
            var behavior = _db.MonsterBehaviors.FirstOrDefault(r => r.Id == draft.MonsterId);
            if (behavior == null)
            {
                behavior = new MonsterBehaviorEntity { Id = draft.MonsterId };
                _db.MonsterBehaviors.Add(behavior);
            }
            var b = draft.Behavior;
            behavior.AiProfile = (byte)Math.Clamp(b.AiProfile, 0, 3);
            behavior.FlagUseThreat = ToByte(b.FlagUseThreat);
            behavior.FlagUseSkills = ToByte(b.FlagUseSkills);
            behavior.FlagUseNavMesh = ToByte(b.FlagUseNavMesh);
            behavior.FlagUseAssist = ToByte(b.FlagUseAssist);
            behavior.FlagUseTactics = ToByte(b.FlagUseTactics);
            behavior.IsAggressive = (byte)(b.IsAggressive != 0 ? 1 : 0);
            behavior.AggroRadius = (float)b.AggroRadius;
            behavior.AggroConeDegrees = (float)b.AggroConeDegrees;
            behavior.LeashRadius = (float)b.LeashRadius;
            behavior.EvadeImmunitySeconds = (float)b.EvadeImmunitySeconds;
            behavior.AssistRadius = (float)b.AssistRadius;
            behavior.DetectMask = b.DetectMask;
            behavior.WanderRadius = (float)b.WanderRadius;
            behavior.IdleToPatrolSeconds = (float)b.IdleToPatrolSeconds;
            behavior.WalkSpeed = (float)b.WalkSpeed;
            behavior.MoveSpeed = (float)b.MoveSpeed;
            behavior.FollowType = (byte)Math.Clamp(b.FollowType, 0, 3);
            behavior.MeleeSwingSeconds = (float)b.MeleeSwingSeconds;
            behavior.RangedMinDistance = (float)b.RangedMinDistance;
            behavior.RangedMaxDistance = (float)b.RangedMaxDistance;
            behavior.FleeHpPercent = b.FleeHpPercent;
            result.WrittenRows["behavior"] = 1;

            // --- 3. 巡逻路线（先建，行为的 route_id 要指它） ---
            if (draft.PatrolRoute != null)
            {
                var route = draft.PatrolRoute;
                MonsterPatrolRouteEntity routeRow;
                var isNewRoute = false;
                if (route.RouteId > 0)
                {
                    // 草稿指名了编号 ⇒ 照那个号存（route_id 由应用赋值，不是 IDENTITY）。
                    // 上一轮这里写的是 NextId(...)，等于把作者指的号悄悄换成另一个号，
                    // 而且新行只在 Id==0 时才 Add——新行永远不是 0，于是这条线根本没入库，
                    // 行为表的 route_id 指着一条不存在的路线。
                    routeRow = _db.MonsterPatrolRoutes.FirstOrDefault(r => r.Id == route.RouteId);
                    if (routeRow == null)
                    {
                        routeRow = new MonsterPatrolRouteEntity { Id = route.RouteId };
                        isNewRoute = true;
                    }
                }
                else
                {
                    routeRow = new MonsterPatrolRouteEntity { Id = NextId(_db.MonsterPatrolRoutes) };
                    isNewRoute = true;
                }
                if (isNewRoute) _db.MonsterPatrolRoutes.Add(routeRow);
                routeRow.Name = string.IsNullOrWhiteSpace(route.Name) ? $"{draft.MonsterId} 巡逻" : route.Name;
                routeRow.Mode = (byte)Math.Clamp(route.Mode, 0, 2);
                routeRow.PauseAtPointSeconds = (float)route.PauseAtPointSeconds;
                routeRow.UseNavMesh = (byte)(route.UseNavMesh != 0 ? 1 : 0);
                // point_count 是这个"声明列"的唯一写方；漏写会让列表恒为 0。
                routeRow.PointCount = route.Points.Count;
                routeRow.PublishState = 1;

                // 点列整体替换（增量 diff 会让"删掉的点"活在库里并继续被走）。
                var old = _db.MonsterPatrolPoints.Where(p => p.RouteId == routeRow.Id).ToList();
                _db.MonsterPatrolPoints.RemoveRange(old);
                var seq = 0;
                foreach (var p in route.Points)
                {
                    _db.MonsterPatrolPoints.Add(new MonsterPatrolPointEntity
                    {
                        // 点号由库发（point_id 是 IDENTITY）：这里给值会让 INSERT 撞上
                        // 「IDENTITY_INSERT 为 OFF 时不许插显式值」，整次保存回滚。
                        RouteId = routeRow.Id,
                        Seq = p.Seq > 0 ? p.Seq : seq,
                        X = (float)p.X,
                        Y = (float)p.Y,
                        Z = (float)p.Z,
                        PauseSeconds = p.PauseSeconds.HasValue ? (float?)p.PauseSeconds.Value : null,
                    });
                    seq++;
                }
                behavior.PatrolRouteId = routeRow.Id;
                route.RouteId = routeRow.Id;
                result.WrittenRows["patrolRoute"] = 1;
                result.WrittenRows["patrolPoint"] = route.Points.Count;
            }

            // --- 4. 技能挂载（整组替换，理由同上） ---
            var oldSkills = _db.MonsterSkillMounts.Where(m => m.MonsterId == draft.MonsterId).ToList();
            _db.MonsterSkillMounts.RemoveRange(oldSkills);
            var mountSeq = 0;
            foreach (var s in draft.Skills)
            {
                _db.MonsterSkillMounts.Add(new MonsterSkillMountEntity
                {
                    MonsterId = draft.MonsterId,
                    SkillId = s.SkillId,
                    SlotSeq = s.SlotSeq > 0 ? s.SlotSeq : mountSeq,
                    SlotType = (byte)Math.Clamp(s.SlotType, 0, 3),
                    ChanceBps = s.ChanceBps,
                    CastMs = s.CastMs,
                    CooldownMs = s.CooldownMs,
                    MinRange = (float)s.MinRange,
                    MaxRange = (float)s.MaxRange,
                    TargetSelect = (byte)Math.Clamp(s.TargetSelect, 0, 4),
                    TargetCount = s.TargetCount,
                    AoeRadius = (float)s.AoeRadius,
                    SchoolMask = s.SchoolMask,
                    Interruptible = (byte)(s.Interruptible != 0 ? 1 : 0),
                    AllowMovement = (byte)(s.AllowMovement != 0 ? 1 : 0),
                    DamageScaleBps = s.DamageScaleBps,
                    StatusId = s.StatusId,
                    StatusDurationSeconds = (float)s.StatusDurationSeconds,
                    StatusMagnitudeBps = s.StatusMagnitudeBps,
                    StartEmote = s.StartEmote,
                    EmergencyHpPercent = s.EmergencyHpPercent,
                });
                mountSeq++;
            }
            result.WrittenRows["skillMount"] = draft.Skills.Count;

            // --- 5. 掉落绑定 + 逐件条目 ---
            var oldBindings = _db.MonsterLootBindings.Where(l => l.MonsterId == draft.MonsterId).ToList();
            var oldBindingIds = oldBindings.Select(o => o.Id).ToHashSet();
            var oldEntries = _db.MonsterDropEntries.Where(e => oldBindingIds.Contains(e.BindingId)).ToList();
            _db.MonsterLootBindings.RemoveRange(oldBindings);
            _db.MonsterDropEntries.RemoveRange(oldEntries);
            var entryTotal = 0;
            foreach (var lb in draft.LootBindings)
            {
                var bindingRow = new MonsterLootBindingEntity
                {
                    MonsterId = draft.MonsterId,
                    LootTableId = lb.LootTableId,
                    RollChanceBps = lb.RollChanceBps,
                    MinCount = lb.MinCount,
                    MaxCount = lb.MaxCount,
                    ConditionKind = (byte)Math.Clamp(lb.ConditionKind, 0, 3),
                    QuestId = lb.QuestId,
                    MoneyMin = lb.MoneyMin,
                    MoneyMax = lb.MoneyMax,
                };
                _db.MonsterLootBindings.Add(bindingRow);
                _db.SaveChanges(); // 需要真实 binding_id 给条目外键用（在同一事务内）
                var seq2 = 0;
                foreach (var e in lb.Entries)
                {
                    _db.MonsterDropEntries.Add(new MonsterDropEntryEntity
                    {
                        BindingId = bindingRow.Id,
                        ItemId = e.ItemId,
                        ChanceBps = e.ChanceBps,
                        CountMin = e.CountMin,
                        CountMax = e.CountMax,
                        Seq = e.Seq > 0 ? e.Seq : seq2,
                        // 组号原样落库：装配层按 (seq, itemId) 排序、按数组下标掷骰，
                        // 而"这件属不属于互斥组"是数据，不能靠写入顺序或查询顺序推断。
                        GroupId = e.GroupId,
                        // 逐件条件也原样落库：-1 是"继承绑定"这个**判定**，不能被夹成 0（无条件），
                        // 否则库里存的"任务物品"到运行时就成了人人可拿。
                        ConditionKind = Math.Clamp(e.ConditionKind, -1, 3),
                        QuestId = e.QuestId < 0 ? 0 : e.QuestId,
                    });
                    seq2++;
                    entryTotal++;
                }
            }
            result.WrittenRows["lootBinding"] = draft.LootBindings.Count;
            result.WrittenRows["dropEntry"] = entryTotal;

            // --- 6. 刷怪点（整组替换：草稿就是"这只怪有哪些刷怪点"的权威清单） ---
            // 原来这里只 add/update：从草稿里删掉一个点，save 照样回"已更新"、writtenRows 也数得对，
            // 但库里那一行还活着（enabled=1）、导出包照带、DsHost 照它刷怪（2026-10-27 实测：
            // 加一个点再删掉，两轮 save 之后那一行还在）。策划看到的表现就是"删了，进游戏还在"。
            // 所以写完草稿点名的行之后，把这只怪名下**没被点名**的行一起删掉，并把条数报进回执。
            var keptSpawnIds = draft.SpawnPoints.Select(s => s.SpawnPointId).ToHashSet();
            var spawnTotal = 0;
            foreach (var sp in draft.SpawnPoints)
            {
                var row = _db.MonsterSpawnPoints.FirstOrDefault(r => r.Id == sp.SpawnPointId);
                if (row == null)
                {
                    row = new MonsterSpawnPointEntity { Id = sp.SpawnPointId };
                    _db.MonsterSpawnPoints.Add(row);
                }
                row.MonsterId = draft.MonsterId;
                row.ZoneId = sp.ZoneId;
                row.ShardId = sp.ShardId;
                row.X = (float)sp.X;
                row.Y = (float)sp.Y;
                row.Z = (float)sp.Z;
                row.Yaw = (float)sp.Yaw;
                row.LevelOverride = sp.LevelOverride;
                row.MaxAlive = sp.MaxAlive;
                row.SlotSpacing = (float)sp.SlotSpacing;
                row.RespawnOverrideSeconds = (float)sp.RespawnOverrideSeconds;
                // route_override_id：0 = 用行为表的路线；>0 时必须在库里存在，否则这个点静默走直线。
                row.RouteOverrideId = sp.RouteOverrideId;
                row.Enabled = (byte)(sp.Enabled != 0 ? 1 : 0);
                row.PublishState = 1;
                row.ContentVersion = row.ContentVersion <= 0 ? 1 : row.ContentVersion + 1;
                spawnTotal++;
            }
            var droppedSpawns = _db.MonsterSpawnPoints
                .Where(r => r.MonsterId == draft.MonsterId && !keptSpawnIds.Contains(r.Id))
                .ToList();
            _db.MonsterSpawnPoints.RemoveRange(droppedSpawns);
            result.WrittenRows["spawnPoint"] = spawnTotal;
            result.WrittenRows["spawnPointRemoved"] = droppedSpawns.Count;
            if (droppedSpawns.Count > 0)
                result.Warnings.Add($"删掉了 {droppedSpawns.Count} 个草稿里不再列出的刷怪点（编号 {string.Join("、", droppedSpawns.Select(d => d.Id))}）：整组替换的语义，草稿就是权威清单");

            _db.SaveChanges();
            tx.Commit();
            result.Ok = true;
            result.Message = isNew ? $"已创建怪物 {draft.MonsterId}（v{result.ContentVersion}）"
                                   : $"已更新怪物 {draft.MonsterId}（v{result.ContentVersion}）";
            return result;
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { /* 回滚失败不掩盖原始异常 */ }
            result.Ok = false;
            result.Message = "写入失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    /// <summary>设置发布状态（0草稿 1已发布 2停用）。</summary>
    public MonsterStoreResult SetPublishState(int monsterId, int publishState)
    {
        var result = new MonsterStoreResult { MonsterId = monsterId };
        var row = _db.MonsterTemplates.FirstOrDefault(r => r.Id == monsterId);
        if (row == null)
        {
            result.Message = $"怪物 {monsterId} 不存在";
            return result;
        }
        if (publishState == 1)
        {
            // 上线前的引用检查：行为行必须在（否则这只怪会用默认行为跑，而编辑器里显示的是策划配的那份）。
            if (!_db.MonsterBehaviors.Any(r => r.Id == monsterId))
            {
                result.Message = $"怪物 {monsterId} 缺行为行，拒绝发布（发布后它会以默认行为运行，而编辑器显示的仍是配置值）";
                return result;
            }
            // 刷怪点引用它时，点本身必须合法——否则"发布成功"与"游戏里看得见"是两件事。
            var broken = _db.MonsterSpawnPoints.Count(sp => sp.MonsterId == monsterId && sp.Enabled == 1
                       && (sp.MaxAlive < 1 || sp.SlotSpacing <= 0f));
            if (broken > 0)
            {
                result.Message = $"怪物 {monsterId} 有 {broken} 个刷怪点参数非法，拒绝发布";
                return result;
            }
        }
        row.PublishState = (byte)Math.Clamp(publishState, 0, 2);
        row.ContentVersion += 1;
        _db.SaveChanges();
        result.Ok = true;
        result.ContentVersion = row.ContentVersion;
        result.WrittenRows["template"] = 1;
        result.Message = $"怪物 {monsterId} 发布状态 → {publishState}";
        return result;
    }

    /// <summary>作废（保留行、断开运行）。已被刷怪点引用时拒绝，并回显是哪几个点。</summary>
    public MonsterStoreResult Retire(int monsterId)
    {
        var result = new MonsterStoreResult { MonsterId = monsterId };
        var row = _db.MonsterTemplates.FirstOrDefault(r => r.Id == monsterId);
        if (row == null)
        {
            result.Message = $"怪物 {monsterId} 不存在";
            return result;
        }
        var referencing = _db.MonsterSpawnPoints.AsNoTracking()
            .Where(sp => sp.MonsterId == monsterId && sp.Enabled == 1)
            .OrderBy(sp => sp.Id).Select(sp => sp.Id).ToList();
        if (referencing.Count > 0)
        {
            // 拒绝理由要给出可操作的清单，而不是"被引用中"三个字。
            result.Message = $"怪物 {monsterId} 仍被 {referencing.Count} 个启用中的刷怪点引用（id: "
                             + string.Join(",", referencing.Take(10))
                             + (referencing.Count > 10 ? " …" : "") + "）。请先停用或改指这些点。";
            return result;
        }
        row.PublishState = 2;
        row.ContentVersion += 1;
        _db.SaveChanges();
        result.Ok = true;
        result.ContentVersion = row.ContentVersion;
        result.Message = $"怪物 {monsterId} 已作废";
        return result;
    }

    /// <summary>删除（只允许删已作废的；连带删技能/掉落/路线/刷怪点）。</summary>
    public MonsterStoreResult Delete(int monsterId)
    {
        var result = new MonsterStoreResult { MonsterId = monsterId };
        var row = _db.MonsterTemplates.FirstOrDefault(r => r.Id == monsterId);
        if (row == null)
        {
            result.Message = $"怪物 {monsterId} 不存在";
            return result;
        }
        if (row.PublishState != 2)
        {
            result.Message = $"怪物 {monsterId} 尚未作废（当前状态 {row.PublishState}）；删除只允许对已作废的行执行";
            return result;
        }

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var bindings = _db.MonsterLootBindings.Where(l => l.MonsterId == monsterId).ToList();
            var bindingIds = bindings.Select(b2 => b2.Id).ToHashSet();
            _db.MonsterDropEntries.RemoveRange(_db.MonsterDropEntries.Where(e => bindingIds.Contains(e.BindingId)));
            _db.MonsterLootBindings.RemoveRange(bindings);
            _db.MonsterSkillMounts.RemoveRange(_db.MonsterSkillMounts.Where(m => m.MonsterId == monsterId));

            var routeIds = _db.MonsterBehaviors.Where(b3 => b3.Id == monsterId && b3.PatrolRouteId > 0)
                .Select(b3 => b3.PatrolRouteId).ToList();
            _db.MonsterPatrolPoints.RemoveRange(_db.MonsterPatrolPoints.Where(p => routeIds.Contains(p.RouteId)));
            _db.MonsterPatrolRoutes.RemoveRange(_db.MonsterPatrolRoutes.Where(r => routeIds.Contains(r.Id)));

            _db.MonsterSpawnPoints.RemoveRange(_db.MonsterSpawnPoints.Where(sp => sp.MonsterId == monsterId));
            _db.MonsterBehaviors.RemoveRange(_db.MonsterBehaviors.Where(b4 => b4.Id == monsterId));
            _db.MonsterTemplates.Remove(row);

            _db.SaveChanges();
            tx.Commit();
            result.Ok = true;
            result.Message = $"怪物 {monsterId} 及其全部附件已删除";
            return result;
        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }
            result.Message = "删除失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    /// <summary>
    /// 从六张表构建内容快照（<c>export-content</c> 的实现）。
    /// <para>一次读全表再在内存里拼：逐怪逐表地查会让"库里有 300 只怪"变成 300 次往返，
    /// 也让掉落绑定与逐件条目的归属关系散在多个查询里。</para>
    /// </summary>
    public Horizon.Game.Core.Sim.Server.MonsterContentSnapshot BuildContentSnapshot()
    {
        var snapshot = new Horizon.Game.Core.Sim.Server.MonsterContentSnapshot
        {
            ContentRevision = (int)(DateTime.UtcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
        };

        var templates = _db.MonsterTemplates.AsNoTracking()
            .Where(r => r.PublishState == 1).OrderBy(r => r.Id).ToList();
        var behaviors = _db.MonsterBehaviors.AsNoTracking().ToDictionary(r => r.Id, r => r);
        var mounts = _db.MonsterSkillMounts.AsNoTracking().ToList().GroupBy(m => m.MonsterId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SlotSeq).ToList());
        var routes = _db.MonsterPatrolRoutes.AsNoTracking().ToList();
        var routePoints = _db.MonsterPatrolPoints.AsNoTracking().ToList().GroupBy(p => p.RouteId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());
        var bindings = _db.MonsterLootBindings.AsNoTracking().ToList();
        var entries = _db.MonsterDropEntries.AsNoTracking().ToList().GroupBy(e => e.BindingId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());

        foreach (var t in templates)
        {
            var item = new Horizon.Game.Core.Sim.Server.MonsterTemplateSnapshot
            {
                MonsterId = t.Id,
                Name = t.Name,
                Rank = t.Rank,
                Species = t.Species,
                WuxingDomain = t.WuxingDomain,
                ModelKey = t.ModelKey,
                PrefabGuid = t.PrefabGuid,
                ModelScale = (float)t.ModelScale,
                StandState = t.StandState,
                LevelBase = t.LevelBase,
                LevelMin = t.LevelMin,
                LevelMax = t.LevelMax,
                AggroSayId = t.AggroSayId,
                CorpseSeconds = t.CorpseSeconds,
            LootWindowSeconds = t.LootWindowSeconds,
            PickupDistanceMeters = t.PickupDistanceMeters,
                RespawnBaseSeconds = t.RespawnBaseSeconds,
                RespawnPerLevelSeconds = t.RespawnPerLevelSeconds,
                Stats = new Horizon.Game.Core.Sim.Server.MonsterStatsSnapshot
                {
                    HpBase = t.HpBase,
                    HpPerLevel = t.HpPerLevel,
                    AtkBase = t.AtkBase,
                    AtkPerLevel = t.AtkPerLevel,
                    DefBase = t.DefBase,
                    DefPerLevel = t.DefPerLevel,
                    HpOverrideBps = t.HpOverrideBps,
                    AtkOverrideBps = t.AtkOverrideBps,
                },
                Xp = new Horizon.Game.Core.Sim.Server.MonsterXpSnapshot
                {
                    XpBase = t.XpBase,
                    XpPerLevel = t.XpPerLevel,
                    // 两个逐模板倍率也必须带上：丢了它们，编辑器里配的"剧情怪三倍经验"
                    // 到运行时就是 1 倍，而库、编辑器、快照三处的数字都没变。
                    XpModBps = t.XpModBps,
                    HealthModBps = t.HealthModBps,
                },
            };

            if (behaviors.TryGetValue(t.Id, out var b))
            {
                item.Behavior = new Horizon.Game.Core.Sim.Server.MonsterBehaviorSnapshot
                {
                    AiProfile = b.AiProfile,
                    FlagUseThreat = b.FlagUseThreat,
                    FlagUseSkills = b.FlagUseSkills,
                    FlagUseNavMesh = b.FlagUseNavMesh,
                    FlagUseAssist = b.FlagUseAssist,
                    FlagUseTactics = b.FlagUseTactics,
                    IsAggressive = b.IsAggressive,
                    AggroRadius = b.AggroRadius,
                    AggroConeDegrees = b.AggroConeDegrees,
                    LeashRadius = b.LeashRadius,
                    EvadeImmunitySeconds = b.EvadeImmunitySeconds,
                    AssistRadius = b.AssistRadius,
                    DetectMask = b.DetectMask,
                    WanderRadius = b.WanderRadius,
                    IdleToPatrolSeconds = b.IdleToPatrolSeconds,
                    WalkSpeed = b.WalkSpeed,
                    MoveSpeed = b.MoveSpeed,
                    FollowType = b.FollowType,
                    MeleeSwingSeconds = b.MeleeSwingSeconds,
                    RangedMinDistance = b.RangedMinDistance,
                    RangedMaxDistance = b.RangedMaxDistance,
                    FleeHpPercent = b.FleeHpPercent,
                    PatrolRouteId = b.PatrolRouteId,
                };
            }
            else
            {
                snapshot.Problems.Add($"模板 {t.Id} 缺行为行：装配时会用默认行为（AI 全关）");
            }

            if (mounts.TryGetValue(t.Id, out var list))
            {
                foreach (var m in list)
                {
                    item.Skills.Add(new Horizon.Game.Core.Sim.Server.MonsterSkillSnapshot
                    {
                        SkillId = m.SkillId,
                        SlotSeq = m.SlotSeq,
                        SlotType = m.SlotType,
                        ChanceBps = m.ChanceBps,
                        CastMs = m.CastMs,
                        CooldownMs = m.CooldownMs,
                        MinRange = m.MinRange,
                        MaxRange = m.MaxRange,
                        TargetSelect = m.TargetSelect,
                        TargetCount = m.TargetCount,
                        AoeRadius = m.AoeRadius,
                        SchoolMask = m.SchoolMask,
                        Interruptible = m.Interruptible,
                        AllowMovement = m.AllowMovement,
                        DamageScaleBps = m.DamageScaleBps,
                        StatusId = m.StatusId,
                        StatusDurationSeconds = m.StatusDurationSeconds,
                        StatusMagnitudeBps = m.StatusMagnitudeBps,
                        StartEmote = m.StartEmote,
                        EmergencyHpPercent = m.EmergencyHpPercent,
                    });
                }
            }
            snapshot.Templates.Add(item);
        }

        foreach (var r in routes)
        {
            if (!routePoints.TryGetValue(r.Id, out var pts)) continue;
            var route = new Horizon.Game.Core.Sim.Server.MonsterRouteSnapshot
            {
                RouteId = r.Id,
                Name = r.Name,
                Mode = r.Mode,
                PauseAtPointSeconds = r.PauseAtPointSeconds,
                UseNavMesh = r.UseNavMesh,
            };
            foreach (var p in pts)
            {
                route.Points.Add(new Horizon.Game.Core.Sim.Server.MonsterPointSnapshot
                {
                    Seq = p.Seq,
                    X = p.X,
                    Y = p.Y,
                    Z = p.Z,
                    PauseSeconds = p.PauseSeconds,
                });
            }
            snapshot.Routes.Add(route);
        }

        foreach (var sp in _db.MonsterSpawnPoints.AsNoTracking()
                     .Where(s => s.Enabled == 1 && s.PublishState == 1).OrderBy(s => s.Id).ToList())
        {
            snapshot.SpawnPoints.Add(new Horizon.Game.Core.Sim.Server.MonsterSpawnSnapshotDto
            {
                SpawnPointId = sp.Id,
                ZoneId = sp.ZoneId,
                ShardId = sp.ShardId,
                X = sp.X,
                Y = sp.Y,
                Z = sp.Z,
                Yaw = sp.Yaw,
                MonsterTemplateId = sp.MonsterId,
                LevelOverride = sp.LevelOverride,
                MaxAlive = sp.MaxAlive,
                SlotSpacing = sp.SlotSpacing,
                RespawnOverrideSeconds = sp.RespawnOverrideSeconds,
                RouteOverrideId = sp.RouteOverrideId,
            });
        }

        foreach (var binding in bindings.OrderBy(b => b.Id).ToList())
        {
            var dto = new Horizon.Game.Core.Sim.Server.MonsterLootBindingSnapshotDto
            {
                MonsterTemplateId = binding.MonsterId,
                LootTableId = binding.LootTableId,
                RollChanceBps = binding.RollChanceBps,
                MinCount = binding.MinCount,
                MaxCount = binding.MaxCount,
                ConditionKind = binding.ConditionKind,
                QuestId = binding.QuestId,
                MoneyMin = binding.MoneyMin,
                MoneyMax = binding.MoneyMax,
            };
            if (entries.TryGetValue(binding.Id, out var es))
            {
                foreach (var e in es)
                {
                    dto.Entries.Add(new Horizon.Game.Core.Sim.Server.MonsterDropSnapshotDto
                    {
                        ItemId = e.ItemId,
                        ChanceBps = e.ChanceBps,
                        CountMin = e.CountMin,
                        CountMax = e.CountMax,
                        // 掷骰顺序必须带上：装配层按下标做盐，丢掉它等于把"哪件用哪颗骰子"交给查询顺序。
                        Seq = e.Seq,
                        // 组号也必须带上：丢了它，库里配的互斥组到了运行时全变逐件独立掷，
                        // 而编辑器、库、快照三处的概率数字一个都没变。
                        GroupId = e.GroupId,
                        // 逐件条件与任务 id 同样必须带上：丢了它们，"只给做了任务的人"的那件
                        // 会在运行时按**整张表**的条件出账——表是普通货时任务件就变成人人可拿。
                        ConditionKind = e.ConditionKind,
                        QuestId = e.QuestId,
                    });
                }
            }
            snapshot.LootBindings.Add(dto);
        }

        return snapshot;
    }

    /// <summary>取已发布模板（运行装配器与 catalog 用）。</summary>
    public List<MonsterTemplateEntity> PublishedTemplates()
        => _db.MonsterTemplates.AsNoTracking().Where(r => r.PublishState == 1).OrderBy(r => r.Id).ToList();

    /// <summary>取已发布行为行。</summary>
    public List<MonsterBehaviorEntity> PublishedBehaviors()
        => _db.MonsterBehaviors.AsNoTracking().ToList();

    /// <summary>取已发布刷怪点。</summary>
    public List<MonsterSpawnPointEntity> PublishedSpawnPoints()
        => _db.MonsterSpawnPoints.AsNoTracking().Where(sp => sp.Enabled == 1).OrderBy(sp => sp.Id).ToList();

    /// <summary>
    /// 子表里指向"怪物表与 NPC 表里都没有这只"的行（孤儿）。
    /// <para>
    /// 为什么要单独查：实测（2026-10-27）生产库唯一的那一行掉落绑定（连同 2 行逐件条目）挂在
    /// <c>monster_id=500001</c> 上，而怪物模板表只有 1..11——**11 只怪一行掉落都没有**，
    /// 可导出回执却写着"掉落绑定 1"。这种行永远不会被任何一只怪读到，
    /// 但会让"掉落那一格配了没有"这个问题得到错误的答案（回执数的是它，不是内容）。
    /// </para>
    /// <para>
    /// <b>为什么键要并上 NPC 表</b>：500001 一开始被判成了孤儿，那是**我的判据错**——
    /// 它是 NPC 编辑器的"敌对样板"（山贼头目，<c>EntityType=Monster</c>、Level 32，022 种子带的掉落），
    /// 与怪物编辑器共用同一张掉落从表。所以"存不存在"要问两张模板表，
    /// 否则会把一条合法内容报成数据缺陷（这类红必须当成判据错去修，不是把检查调松）。
    /// </para>
    /// <para>
    /// <b>NPC 表读不到时要降级，而且要把降级说出来</b>：排练库（<c>Game_MonsterRT</c>）只跑了 019/020，
    /// 没有 021/022 建的 NPC 模板表，直接查会让整条普查被一个 SqlException 打断
    /// （实测如此）。宁可念一句"这一半没查成、可能误报"，也不要要么崩、要么静默当成"没有孤儿"。
    /// </para>
    /// </summary>
    public (List<(string Table, int RowId, int MonsterId)> Rows, bool NpcTableReadable) OrphanChildRows()
    {
        var known = _db.MonsterTemplates.AsNoTracking().Select(t => t.Id).ToHashSet();
        var npcTableReadable = true;
        try
        {
            foreach (var id in _db.NpcTemplates.AsNoTracking().Select(t => t.Id)) known.Add(id);
        }
        catch (Exception)
        {
            npcTableReadable = false;   // 这个库里没有 NPC 模板表（021/022 未执行）
        }
        var orphans = new List<(string, int, int)>();
        foreach (var b in _db.MonsterLootBindings.AsNoTracking().Where(b => !known.Contains(b.MonsterId)).ToList())
            orphans.Add(("掉落绑定", b.Id, b.MonsterId));
        foreach (var m in _db.MonsterSkillMounts.AsNoTracking().Where(m => !known.Contains(m.MonsterId)).ToList())
            orphans.Add(("技能挂载", m.Id, m.MonsterId));
        foreach (var s in _db.MonsterSpawnPoints.AsNoTracking().Where(s => !known.Contains(s.MonsterId)).ToList())
            orphans.Add(("刷怪点", s.Id, s.MonsterId));
        var bindingIds = _db.MonsterLootBindings.AsNoTracking().Select(b => b.Id).ToHashSet();
        foreach (var e in _db.MonsterDropEntries.AsNoTracking().ToList().Where(e => !bindingIds.Contains(e.BindingId)))
            orphans.Add(("逐件条目", e.Id, -e.BindingId));   // 条目挂在"不存在的绑定"上，用负号把绑定号带出来
        return (orphans, npcTableReadable);
    }

    public List<MonsterSkillMountEntity> AllSkillMounts() => _db.MonsterSkillMounts.AsNoTracking().ToList();
    public List<MonsterLootBindingEntity> AllLootBindings() => _db.MonsterLootBindings.AsNoTracking().ToList();
    public List<MonsterDropEntryEntity> AllDropEntries() => _db.MonsterDropEntries.AsNoTracking().ToList();
    public List<MonsterPatrolRouteEntity> AllRoutes() => _db.MonsterPatrolRoutes.AsNoTracking().ToList();
    public List<MonsterPatrolPointEntity> AllRoutePoints() => _db.MonsterPatrolPoints.AsNoTracking().ToList();

    /// <summary>
    /// 取实体上真正生效的那一格编号。
    /// <para>
    /// 这些表都在派生类里用 <c>public new int Id</c> 重新声明了编号（业务键不许自增），
    /// 而不带 DeclaredOnly 的 <c>GetProperty("Id")</c> 会同时看见派生类与基类那两格，
    /// 于是抛 AmbiguousMatchException。上一轮这句被整个吞进 catch，库内最大值因此静默算成 0，
    /// 新分配的号从 1 起——下一次就撞主键。
    /// </para>
    /// </summary>
    private static PropertyInfo? IdPropertyOf(Type type)
        => type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
           ?? type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// 给"编号由 CLI 代填"的那张表发号：目前只有巡逻线（route_id 不是 IDENTITY，
    /// 草稿可以指名一条既有路线，也可以留 0 让这里发号）。
    /// <para>
    /// 逐件条目／掉落绑定／巡逻点／技能挂载**不许**走这里：那四张仍是 IDENTITY，
    /// 给了显式值 INSERT 会被 SQL Server 顶回来（"IDENTITY_INSERT 为 OFF 时不许插显式值"），
    /// 整次保存一起回滚。上一轮正是这句在四张表上都写了一遍，编辑器存不进任何带子表的怪。
    /// </para>
    /// <para>软删历史会让 Max 变小吗？不会——Max 取的是现存行，删过的号可能被复用，
    /// 这是可接受的（route 不是跨表引用键，草稿下次会带回来的 RouteId 覆盖它）。</para>
    /// </summary>
    private static int NextId<T>(DbSet<T> set) where T : class
    {
        var prop = IdPropertyOf(typeof(T));
        if (prop == null) return 1;

        // 用本地 + 库内双取：同一次 SaveChanges 里已经 Add 但尚未落库的行，库查询看不见。
        var maxDb = 0;
        foreach (var o in set.AsNoTracking().ToList())
        {
            var v = Convert.ToInt32(prop.GetValue(o));
            if (v > maxDb) maxDb = v;
        }
        var maxLocal = 0;
        foreach (var ent in set.Local)
        {
            var v = Convert.ToInt32(prop.GetValue(ent));
            if (v > maxLocal) maxLocal = v;
        }
        return Math.Max(maxDb, maxLocal) + 1;
    }

    private static byte? ToByte(int? value) => value.HasValue ? (byte)(value.Value != 0 ? 1 : 0) : null;

    public void Dispose() => _db.Dispose();
}


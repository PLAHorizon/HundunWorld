using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Horizon.Entities;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Npcs;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Horizon.NpcCli;

/// <summary>任务链面板要用的任务简介（名字/描述/目标 JSON 原样，解析交给权威映射器）。</summary>
public sealed record NpcQuestBrief(string Name, string Description, string ObjectivesJson);

/// <summary>
/// 任务链的显示文本规则（**唯一一份**）：CLI 出的是"已经拼好的行"，插件只负责逐行打印——
/// 两边各拼一次时，"×3" 这种小规则会先漂，而漂了谁都不报错。
/// </summary>
public static class QuestChainText
{
    /// <summary>目标行："击杀目标 ×10"；数量 ≤1 时不带 ×N（"与指定人物对话"）。</summary>
    public static string ObjectiveLine(string description, int required)
    {
        var text = string.IsNullOrWhiteSpace(description) ? "完成目标" : description.Trim();
        return required > 1 ? text + " ×" + required : text;
    }
}

/// <summary>一次读/写的结果（与 MonsterStoreResult 同构：ok + 消息 + 计数 + 问题清单）。</summary>
public sealed class NpcStoreResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>受影响的表 → 行数。<b>必须逐表计数</b>：只回"成功"时，"其实一行没写"与"写了 12 行"同形。</summary>
    public Dictionary<string, int> WrittenRows { get; } = new();

    public List<string> Problems { get; } = new();

    public static NpcStoreResult Fail(string message)
    {
        return new NpcStoreResult { Ok = false, Message = message };
    }
}

/// <summary>
/// NPC 数据的读写端（EF 直连）。<b>只被 CLI 使用</b>——DsHost 走快照文件，Silo 同理，
/// 理由见 <see cref="NpcContentSnapshot"/> 的注释。
/// </summary>
public sealed class NpcStore : IDisposable
{
    private readonly GameEntityContext _db;

    public NpcStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new GameEntityContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ────────────────────────────── 读 ──────────────────────────────

    /// <summary>
    /// 货架行涉及物品的模板价（买/卖）。<b>价格闸门必须问权威表</b>：物品表今天 `buy_price` 常为 0、
    /// 靠商人 override 抬价；没 override 又没模板价的行，商店会显示 0 价且买不动。
    /// </summary>
    public Dictionary<int, (long Buy, long Sell)> ItemPrices(IEnumerable<int> itemIds)
    {
        var ids = itemIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, (long Buy, long Sell)>();

        return _db.ItemTemplates.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.BuyPrice, x.SellPrice })
            .ToDictionary(x => x.Id, x => ((long)x.BuyPrice, (long)x.SellPrice));
    }

    /// <summary>
    /// 编辑器列表用的轻量目录：**从库里读**（含草稿），不依赖进程内是否装配过内容。
    /// <para>
    /// 以前 `<c>catalog</c>` 取的是 <c>NpcTemplateCatalog.RegisteredTemplates()</c>——那是"硬编码 1/2/3 +
    /// 本进程已装配内容"的并集。CLI 每次都是新进程、从不装配内容，于是新建的 NPC 永远进不了列表：
    /// 编辑器看得到编号却"读不到数据"，任务编辑器的 NPC 下拉也少人。
    /// </para>
    /// </summary>
    public List<NpcCatalogRow> ListTemplates()
    {
        return _db.NpcTemplates.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new NpcCatalogRow
            {
                NpcId = x.Id,
                Name = x.Name,
                Title = x.Title,
                ModelKey = x.ModelKey,
                WuxingDomain = x.WuxingDomain,
                PublishState = x.PublishState,
            })
            .ToList();
    }

    /// <summary>目录行（只读投影，够列表/下拉用）。</summary>
    public sealed class NpcCatalogRow
    {
        public int NpcId { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string ModelKey { get; set; } = "";
        public int WuxingDomain { get; set; }
        public byte PublishState { get; set; }
    }

    /// <summary>
    /// 读出内容快照。
    /// </summary>
    /// <param name="publishedOnly">true = 只导已发布（这是给 DsHost 吃的口径）；
    /// false = 编辑器列表要看到草稿，但**导出内容必须传 true**。</param>
    public NpcContentSnapshot LoadSnapshot(bool publishedOnly)
    {
        var snap = new NpcContentSnapshot { ContentRevision = 1 };
        var problems = snap.Problems;

        IQueryable<NpcTemplateEntity> templates = _db.NpcTemplates.AsNoTracking();
        if (publishedOnly) templates = templates.Where(x => x.PublishState == 1);

        var templateIds = new HashSet<int>();
        var maxVersion = 0;

        foreach (var t in templates.OrderBy(x => x.Id).ToList())
        {
            templateIds.Add(t.Id);
            maxVersion = Math.Max(maxVersion, t.ContentVersion);

            snap.Templates.Add(new NpcTemplateSnapshot
            {
                NpcId = t.Id,
                Name = t.Name,
                Title = t.Title,
                ModelKey = t.ModelKey,
                PrefabGuid = t.PrefabGuid,
                Scale = t.Scale,
                Level = t.Level,
                LevelVariance = t.LevelVariance,
                Reaction = t.Reaction,
                ServiceFlags = unchecked((uint)t.ServiceFlags),
                AiTier = t.AiTier,
                AiBitsForceOn = unchecked((uint)t.AiBitsForceOn),
                AiBitsForceOff = unchecked((uint)t.AiBitsForceOff),
                AttackableOverride = t.AttackableOverride,
                SpeedWalk = t.SpeedWalk,
                SpeedRun = t.SpeedRun,
                AlertRadiusMeters = t.AlertRadiusMeters,
                GossipGroupId = t.GossipGroupId,
                Rank = t.Rank,
                WuxingDomain = t.WuxingDomain,
                KillCreditNpcId = t.KillCreditNpcId,
                FactionGroup = t.FactionGroup,
            });
        }

        // 内容号：取模板 content_version 的最大值 +1（没行时 1）。
        // 用 max+1 而不是"每次导出 +1"：导出是幂等的，同一份库导出两次应得到同一个号，
        // 否则 DsHost 每次重启都像换了内容，金轨迹对账就没了基线。
        snap.ContentRevision = Math.Max(1, maxVersion);

        bool Wanted(int npcId) => !publishedOnly || templateIds.Contains(npcId);

        foreach (var s in _db.NpcSpawnPoints.AsNoTracking().Where(x => x.Enabled == 1).ToList())
        {
            if (!Wanted(s.NpcId)) continue;
            snap.SpawnPoints.Add(new NpcSpawnSnapshotDto
            {
                SpawnPointId = s.Id, ZoneId = s.ZoneId, ShardId = s.ShardId,
                X = s.X, Y = s.Y, Z = s.Z, Yaw = s.Yaw,
                NpcId = s.NpcId, LevelOverride = s.LevelOverride, MaxAlive = s.MaxAlive,
                RespawnSeconds = s.RespawnSeconds, RouteId = s.RouteId, FacingMode = s.FacingMode,
            });
        }

        var routeIds = new HashSet<int>(snap.SpawnPoints.Where(x => x.RouteId > 0).Select(x => x.RouteId));

        foreach (var r in _db.NpcWaypointPaths.AsNoTracking().ToList())
        {
            // 路线：已发布的都导（多个 NPC 可能共用一条），草稿路线只在该 NPC 被选中时导出。
            if (publishedOnly && r.PublishState != 1) continue;
            if (!publishedOnly && r.PublishState != 1 && !routeIds.Contains(r.Id)) continue;

            var nodes = _db.NpcWaypointNodes.AsNoTracking()
                .Where(n => n.PathId == r.Id)
                .OrderBy(n => n.Seq)
                .ToList();

            var route = new NpcRouteSnapshot
            {
                PathId = r.Id, Name = r.Name, Mode = r.Mode,
                PauseAtPointSeconds = r.PauseAtPointSeconds,
                UseNavMesh = r.UseNavMesh != 0,
            };

            foreach (var n in nodes)
            {
                route.Points.Add(new NpcRoutePointSnapshot
                {
                    Seq = n.Seq, X = n.X, Y = n.Y, Z = n.Z, PauseSeconds = n.PauseSeconds,
                });
            }

            if (route.Points.Count == 0) problems.Add($"route_no_points:{r.Id}");
            snap.Routes.Add(route);
        }

        foreach (var v in _db.NpcVendorEntries.AsNoTracking().Where(x => x.Enabled == 1).OrderBy(x => x.NpcId).ThenBy(x => x.Slot).ToList())
        {
            if (!Wanted(v.NpcId)) continue;
            snap.VendorRows.Add(new NpcVendorRowDto
            {
                NpcId = v.NpcId, Slot = v.Slot, ItemId = v.ItemId,
                BuyPriceOverride = v.BuyPriceOverride, SellPriceOverride = v.SellPriceOverride,
                StockMax = v.StockMax, RestockSeconds = v.RestockSeconds, Category = v.Category,
            });
        }

        foreach (var t in _db.NpcTrainerEntries.AsNoTracking().Where(x => x.Enabled == 1).OrderBy(x => x.NpcId).ThenBy(x => x.Seq).ToList())
        {
            if (!Wanted(t.NpcId)) continue;
            snap.TrainerRows.Add(new NpcTrainerRowDto
            {
                NpcId = t.NpcId, SkillId = t.SkillId, ServiceKind = t.ServiceKind,
                CostGold = t.CostGold, CostItemId = t.CostItemId, CostItemCount = t.CostItemCount, Seq = t.Seq,
            });
        }

        foreach (var r in _db.NpcCraftRecipes.AsNoTracking().ToList())
        {
            if (publishedOnly && r.PublishState != 1) continue;

            var dto = new NpcRecipeSnapshotDto
            {
                RecipeId = r.Id, Name = r.Name, ProductItemId = r.ProductItemId,
                CostGold = r.CostGold, StationNpcId = r.StationNpcId, SkillReq = r.SkillReq,
                LearnSourceKind = r.LearnSourceKind,
            };

            if (!TryParseMaterials(r.MaterialsJson, out var materials, out var matError))
            {
                problems.Add($"recipe_materials_json_bad:{r.Id}:{matError}");
            }
            else
            {
                dto.Materials = materials;
            }

            if (!TryParseInts(r.QualityGapCapsJson, out var caps, out var capsError))
                problems.Add($"recipe_gap_caps_json_bad:{r.Id}:{capsError}");
            else
                dto.QualityGapCaps = caps;

            if (!TryParseInts(r.QualityChanceBpsJson, out var chances, out var chanceError))
                problems.Add($"recipe_chance_json_bad:{r.Id}:{chanceError}");
            else
                dto.QualityChanceBps = chances;

            snap.Recipes.Add(dto);
        }

        foreach (var g in _db.NpcGossipOptions.AsNoTracking().Where(x => x.Enabled == 1).OrderBy(x => x.NpcId).ThenBy(x => x.Seq).ToList())
        {
            if (!Wanted(g.NpcId)) continue;
            snap.GossipRows.Add(new NpcGossipRowDto
            {
                NpcId = g.NpcId, GroupId = g.GroupId, Seq = g.Seq, TextId = g.TextId,
                ActionKind = g.ActionKind, ActionArg = g.ActionArg,
                ReqMinLevel = g.ReqMinLevel, ReqMaxLevel = g.ReqMaxLevel,
            });
        }

        ReadGossipText(snap);
        ReadSkillMounts(snap, Wanted);

        foreach (var l in _db.NpcQuestLinks.AsNoTracking().OrderBy(x => x.NpcId).ThenBy(x => x.OrderSeq).ToList())
        {
            if (!Wanted(l.NpcId)) continue;
            snap.QuestLinks.Add(new NpcQuestLinkDto
            {
                NpcId = l.NpcId, QuestId = l.QuestId, Role = l.Role, OrderSeq = l.OrderSeq,
            });
        }

        foreach (var s in _db.NpcScheduleSlots.AsNoTracking().OrderBy(x => x.NpcId).ThenBy(x => x.Seq).ToList())
        {
            if (!Wanted(s.NpcId)) continue;
            snap.ScheduleRows.Add(new NpcScheduleRowDto
            {
                NpcId = s.NpcId, Seq = s.Seq, FromSeconds = s.FromSeconds, ToSeconds = s.ToSeconds,
                RouteId = s.RouteId, Posture = s.Posture, GossipGroupId = s.GossipGroupId,
            });
        }

        // 掉落绑定：与怪物共用同两张表，owner 列里放 npc_id（D4：扩 owner 而不是另立一套掉落表）。
        // 019 未执行时这两张表还不存在：那时整份导出不该失败（NPC 内容不依赖它们），
        // 但"没读到"必须写进 Skipped——否则缺表与"这个 NPC 不掉东西"在结果上一模一样。
        // **只认敌对 NPC**：友方 NPC 不可能被打死，同 id 的行其实是怪物种子的掉落（两边共用整数 id 空间），
        // 不过滤就会把"铁匠掉 6 件"这种不可能的配置导进随包内容。
        var hostileIds = new HashSet<int>();
        foreach (var tpl in snap.Templates)
        {
            if (tpl.Reaction == (byte)Horizon.Game.Sim.Npcs.NpcStance.Hostile) hostileIds.Add(tpl.NpcId);
        }

        if (HasTable("Game_HunduShijie_MonsterLootBinding") && HasTable("Game_HunduShijie_MonsterDropEntry"))
        {
            foreach (var b in _db.MonsterLootBindings.AsNoTracking().OrderBy(x => x.Id).ToList())
            {
                if (!Wanted(b.MonsterId)) continue;
                if (!hostileIds.Contains(b.MonsterId)) continue;

                var dto = new MonsterLootBindingSnapshotDto
                {
                    MonsterTemplateId = b.MonsterId,
                    LootTableId = b.LootTableId,
                    RollChanceBps = b.RollChanceBps,
                    MinCount = b.MinCount,
                    MaxCount = b.MaxCount,
                    ConditionKind = b.ConditionKind,
                    QuestId = b.QuestId,
                    MoneyMin = b.MoneyMin,
                    MoneyMax = b.MoneyMax,
                };

                foreach (var e in _db.MonsterDropEntries.AsNoTracking().Where(x => x.BindingId == b.Id).OrderBy(x => x.Seq).ToList())
                {
                    dto.Entries.Add(new MonsterDropSnapshotDto
                    {
                        ItemId = e.ItemId,
                        ChanceBps = e.ChanceBps,
                        CountMin = e.CountMin,
                        CountMax = e.CountMax,
                        Seq = e.Seq,
                        GroupId = e.GroupId,
                        // 逐件条件列**已经存在**（MonsterDropEntry.condition_kind/quest_id），
                        // 这里以前写死 -1/0：读回来永远"继承绑定"，保存一次就把库里配好的
                        // "任务持有者才掉/同组互斥"改掉，而草稿对账两边都是 -1 看不出来。
                        ConditionKind = e.ConditionKind,
                        QuestId = e.QuestId,
                    });
                }

                snap.LootBindings.Add(dto);
            }
        }
        else
        {
            snap.Skipped.Add("loot_bindings_not_read:monster_loot_tables_missing");
        }

        return snap;
    }

    /// <summary>表是否存在（掉落与怪物侧共表，019 未执行时不存在）。</summary>
    private bool HasTable(string tableName)
    {
        var rows = _db.Database
            .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(@p0, N'U') IS NULL THEN 0 ELSE 1 END AS Value", tableName)
            .ToList();
        return rows.Count > 0 && rows[0] == 1;
    }

    // ────────────────────────────── 台词正文（023 可选列）──────────────────────────────

    /// <summary>023 的 <c>text</c> 列是否存在。<b>缓存一次</b>：同一进程里表结构不会中途变化。</summary>
    private bool? _hasGossipText;

    private bool HasGossipTextColumn()
    {
        if (_hasGossipText is { } cached) return cached;

        var rows = _db.Database
            .SqlQueryRaw<int>(
                "SELECT CASE WHEN COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text') IS NULL THEN 0 ELSE 1 END AS Value")
            .ToList();
        var exists = rows.Count > 0 && rows[0] == 1;
        _hasGossipText = exists;
        return exists;
    }

    /// <summary>
    /// 把台词正文补到快照里。列不存在（023 未执行）时整段跳过，并在 <see cref="NpcContentSnapshot.Skipped"/>
    /// 留一条可检索的痕迹——"没配台词"与"库还没这一列"在导出回执里必须不同形。
    /// </summary>
    private void ReadGossipText(NpcContentSnapshot snap)
    {
        if (snap.GossipRows.Count == 0) return;

        if (!HasGossipTextColumn())
        {
            snap.Skipped.Add("gossip_text_column_absent");
            return;
        }

        // 按 (npc, group, seq) 回填：草稿/导出都以这个三元组认一行，option_id 不进草稿。
        var texts = _db.Database
            .SqlQueryRaw<GossipTextRow>(
                "SELECT [npc_id] AS NpcId, [group_id] AS GroupId, [seq] AS Seq, [text] AS Text " +
                "FROM [Game_HunduShijie_NpcGossipOption] WHERE [text] <> N''")
            .ToList();
        if (texts.Count == 0) return;

        var map = new Dictionary<(int NpcId, int GroupId, int Seq), string>(texts.Count);
        foreach (var row in texts) map[(row.NpcId, row.GroupId, row.Seq)] = row.Text ?? string.Empty;

        for (var i = 0; i < snap.GossipRows.Count; i++)
        {
            var g = snap.GossipRows[i];
            if (map.TryGetValue((g.NpcId, g.GroupId, g.Seq), out var text)) g.Text = text;
        }
    }

    /// <summary>
    /// 保存时回写台词正文。<b>必须与删除+重插在同一事务里</b>：先删光再插会把正文抹成默认值，
    /// 只有把草稿里带来的那一份写回去，015 之后"编辑器保存一次就丢台词"的坑才不会重演。
    /// </summary>
    /// <returns>写入行数；列不存在时回 −1（调用方记进回执，别把它当 0 行成功）。</returns>
    private int WriteGossipText(int npcId, List<NpcGossipRowDto> rows, List<string> problems)
    {
        if (rows.Count == 0) return 0;
        if (!HasGossipTextColumn()) return -1;

        var written = 0;
        foreach (var g in rows)
        {
            var text = g.Text ?? string.Empty;
            if (text.Length > NpcGossipTextMaxLength)
            {
                // 列是 nvarchar(200)：截断并记一条问题，别让一次保存整包失败（其余列已经写进去了）。
                problems.Add($"gossip_text_too_long:{npcId}:{g.Seq}");
                text = text.Substring(0, NpcGossipTextMaxLength);
            }

            _db.Database.ExecuteSqlRaw(
                "UPDATE [Game_HunduShijie_NpcGossipOption] SET [text] = {0} " +
                "WHERE [npc_id] = {1} AND [group_id] = {2} AND [seq] = {3}",
                text, npcId, g.GroupId, g.Seq);
            written++;
        }

        return written;
    }

    // ────────────────────────────── 技能挂载（024 可选表）──────────────────────────────

    /// <summary>
    /// 读技能挂载。**表不存在（024 未执行）时整段跳过**，不报错也不猜——与 023 的台词列同一条守卫纪律。
    /// </summary>
    private void ReadSkillMounts(NpcContentSnapshot snap, Func<int, bool> wanted)
    {
        const string table = "Game_HunduShijie_NpcSkillMount";
        if (!HasTable(table))
        {
            snap.Skipped.Add("npc_skill_mount_table_absent");
            return;
        }

        var rows = _db.Database
            .SqlQueryRaw<SkillMountRow>(
                "SELECT [npc_id] AS NpcId, [skill_id] AS SkillId, [slot_seq] AS SlotSeq, [slot_type] AS SlotType, " +
                "[chance_bps] AS ChanceBps, [cast_ms] AS CastMs, [cooldown_ms] AS CooldownMs, " +
                "[min_range] AS MinRange, [max_range] AS MaxRange, [target_select] AS TargetSelect, " +
                "[target_count] AS TargetCount, [aoe_radius] AS AoeRadius, [school_mask] AS SchoolMask, " +
                "[interruptible] AS Interruptible, [allow_movement] AS AllowMovement, " +
                "[damage_scale_bps] AS DamageScaleBps, [status_id] AS StatusId, " +
                "[status_duration_seconds] AS StatusDurationSeconds, [start_emote] AS StartEmote, " +
                "[emergency_hp_percent] AS EmergencyHpPercent " +
                "FROM [Game_HunduShijie_NpcSkillMount] WHERE [IsValid] = 1")
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!wanted(r.NpcId)) continue;
            snap.SkillMounts.Add(new NpcSkillMountDto
            {
                NpcId = r.NpcId,
                SkillId = r.SkillId,
                SlotSeq = r.SlotSeq,
                SlotType = r.SlotType,
                ChanceBps = r.ChanceBps,
                CastMs = r.CastMs,
                CooldownMs = r.CooldownMs,
                MinRange = (float)r.MinRange,
                MaxRange = (float)r.MaxRange,
                TargetSelect = r.TargetSelect,
                TargetCount = r.TargetCount,
                AoeRadius = (float)r.AoeRadius,
                SchoolMask = r.SchoolMask,
                Interruptible = r.Interruptible,
                AllowMovement = r.AllowMovement,
                DamageScaleBps = r.DamageScaleBps,
                StatusId = r.StatusId,
                StatusDurationSeconds = (float)r.StatusDurationSeconds,
                StartEmote = r.StartEmote,
                EmergencyHpPercent = r.EmergencyHpPercent,
            });
        }
    }

    /// <summary>
    /// 写技能挂载：**整组替换**（与刷点同一条语义：草稿就是这只 NPC 的全部挂载）。
    /// 表不存在时回 −1（调用方记进回执，别把它当"写了 0 行"）。
    /// </summary>
    private int WriteSkillMounts(int npcId, List<NpcSkillMountDto> rows, List<string> problems)
    {
        const string table = "Game_HunduShijie_NpcSkillMount";
        // 先删后判空：草稿里一条挂载都没有 = 作者清空了这一组，库里旧的必须跟着消失。
        // 反过来（空列表直接 return）会让"清空后保存"变成 no-op，库里永远留着旧行。
        if (!HasTable(table)) return -1;

        _db.Database.ExecuteSqlRaw($"DELETE FROM [{table}] WHERE [npc_id] = {{0}}", npcId);
        if (rows.Count == 0) return 0;

        var written = 0;
        foreach (var m in rows)
        {
            if (m.SkillId <= 0)
            {
                problems.Add($"skill_mount_bad_skill:{npcId}:{m.SkillId}");
                continue;
            }

            _db.Database.ExecuteSqlRaw(
                $"INSERT INTO [{table}] ([IsValid],[npc_id],[skill_id],[slot_seq],[slot_type],[chance_bps],[cast_ms]," +
                "[cooldown_ms],[min_range],[max_range],[target_select],[target_count],[aoe_radius],[school_mask]," +
                "[interruptible],[allow_movement],[damage_scale_bps],[status_id],[status_duration_seconds],[start_emote]," +
                "[emergency_hp_percent],[GameId],[AreaId],[ServerId],[GameUserId]) " +
                "VALUES (1,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},0,0,0,{20})",
                npcId, m.SkillId, m.SlotSeq, m.SlotType, m.ChanceBps, m.CastMs, m.CooldownMs,
                (double)m.MinRange, (double)m.MaxRange, m.TargetSelect, m.TargetCount, (double)m.AoeRadius,
                m.SchoolMask, m.Interruptible, m.AllowMovement, m.DamageScaleBps, m.StatusId,
                (double)m.StatusDurationSeconds, m.StartEmote, m.EmergencyHpPercent, Guid.Empty);
            written++;
        }

        return written;
    }

    /// <summary><c>SqlQueryRaw</c> 的投影行（SQL <c>float</c> 按 double 读，再窄化成 float）。</summary>
    private sealed class SkillMountRow
    {
        public int NpcId { get; set; }
        public int SkillId { get; set; }
        public int SlotSeq { get; set; }
        public byte SlotType { get; set; }
        public int ChanceBps { get; set; }
        public int CastMs { get; set; }
        public int CooldownMs { get; set; }
        public double MinRange { get; set; }
        public double MaxRange { get; set; }
        public byte TargetSelect { get; set; }
        public int TargetCount { get; set; }
        public double AoeRadius { get; set; }
        public int SchoolMask { get; set; }
        public byte Interruptible { get; set; }
        public byte AllowMovement { get; set; }
        public int DamageScaleBps { get; set; }
        public int StatusId { get; set; }
        public double StatusDurationSeconds { get; set; }
        public int StartEmote { get; set; }
        public int EmergencyHpPercent { get; set; }
    }

    /// <summary>库列宽度（023）。UI 与校验共用这一个数字（权威在 <see cref="NpcContentSnapshot"/>）。</summary>
    public const int NpcGossipTextMaxLength = NpcContentSnapshot.GossipTextMaxLength;

    /// <summary><see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.SqlQueryRaw{TResult}"/> 的投影行。</summary>
    private sealed class GossipTextRow
    {
        public int NpcId { get; set; }
        public int GroupId { get; set; }
        public int Seq { get; set; }
        public string? Text { get; set; }
    }

    /// <summary>
    /// 读某 NPC 的任务链：本 NPC 关联的任务 + 它们的**传递前置**（这样链头也能画出来）。
    /// <para>
    /// 只读链接表与模板表，不在库里推断"谁该接谁"——链的形状由 <c>prerequisite_quests</c> 决定，
    /// 排序与连边交给 <c>QuestChainCore</c>，两处各做一半就不会出现"编辑器一个顺序、运行期另一个"。
    /// </para>
    /// </summary>
    public (List<Horizon.Game.Sim.Quests.QuestChainNode> Nodes, List<string> Problems, string Source,
            Dictionary<int, NpcQuestBrief> Briefs) LoadQuestChain(int npcId)
    {
        var nodes = new List<Horizon.Game.Sim.Quests.QuestChainNode>();
        var problems = new List<string>();
        var briefs = new Dictionary<int, NpcQuestBrief>();
        if (npcId <= 0) return (nodes, problems, "none", briefs);

        var links = _db.NpcQuestLinks.AsNoTracking()
            .Where(x => x.NpcId == npcId && x.IsValid)
            .ToList();

        var orderByQuest = new Dictionary<int, int>();
        foreach (var link in links)
        {
            if (link.QuestId <= 0) continue;
            if (!orderByQuest.TryGetValue(link.QuestId, out var seq) || link.OrderSeq < seq)
                orderByQuest[link.QuestId] = link.OrderSeq;
        }

        var source = "links";

        // 链接表为空时退到**模板反查**（giver/turn_in 指向本 NPC）：
        // 链接表由"发布流程"生成，存量任务没人回填过；只信链接表会让老任务链整片消失，
        // 而玩家进游戏明明接得到这些任务。
        if (orderByQuest.Count == 0)
        {
            source = "templates";
            var byGiver = _db.QuestTemplates.AsNoTracking()
                .Where(x => x.GiverKind == 1 && x.GiverTemplateId == npcId && x.PublishState == 1)
                .Select(x => x.Id)
                .ToList();
            var byTurnIn = _db.QuestTemplates.AsNoTracking()
                .Where(x => x.TurnInTemplateId == npcId && x.PublishState == 1)
                .Select(x => x.Id)
                .ToList();

            var seq = 0;
            foreach (var id in byGiver.Concat(byTurnIn).Distinct().OrderBy(x => x))
                orderByQuest[id] = seq++;
        }

        if (orderByQuest.Count == 0) return (nodes, problems, source, briefs);

        var wanted = new HashSet<int>(orderByQuest.Keys);
        var queue = new Queue<int>(wanted);
        var templates = new Dictionary<int, QuestTemplateEntity>();

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (templates.ContainsKey(id)) continue;

            var row = _db.QuestTemplates.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (row == null)
            {
                problems.Add($"quest_missing:{id}");
                continue;
            }

            templates[id] = row;
            foreach (var prereq in Horizon.Game.Sim.Quests.QuestDraft.ParsePrerequisites(row.PrerequisiteQuests))
            {
                if (prereq <= 0) continue;
                if (wanted.Add(prereq)) queue.Enqueue(prereq);
            }
        }

        foreach (var pair in templates)
        {
            var row = pair.Value;
            var prereqs = Horizon.Game.Sim.Quests.QuestDraft.ParsePrerequisites(row.PrerequisiteQuests).ToArray();
            nodes.Add(new Horizon.Game.Sim.Quests.QuestChainNode(
                row.Id,
                row.QuestName ?? string.Empty,
                row.GiverTemplateId,
                row.TurnInTemplateId,
                prereqs,
                orderByQuest.TryGetValue(row.Id, out var seq) ? seq : 0));

            // 链图要能看出"这一环让人干什么"：描述与目标 JSON 逐条带回（解析交给权威映射器，
            // 这里不落第二份解析规则）。
            briefs[row.Id] = new NpcQuestBrief(row.QuestName ?? string.Empty, row.Description ?? string.Empty,
                row.Objectives ?? string.Empty);
        }

        return (nodes, problems, source, briefs);
    }

    /// <summary>读一条 NPC 的草稿形态（编辑器"从库加载"）。不存在返回 null。</summary>
    public NpcDraft? LoadDraft(int npcId)
    {
        var snapshot = LoadSnapshot(publishedOnly: false);
        var draft = NpcDraft.FromSnapshotForNpc(snapshot, npcId);
        if (draft.Template is null) return null;

        // 草稿必须带**库里那一份**版本号：乐观锁在保存时与它比对。
        // 不填的话每个草稿都是 0，而库里的行是 1——编辑器"加载→保存"永远报
        // "内容版本冲突"，而且看回执像是别人先改了（实际谁都没动）。
        draft.ContentVersion = GetContentVersion(npcId);
        return draft;
    }

    /// <summary>库里当前的 content_version（乐观锁用）。不存在返回 −1。</summary>
    public int GetContentVersion(int npcId)
    {
        var row = _db.NpcTemplates.AsNoTracking().FirstOrDefault(x => x.Id == npcId);
        return row?.ContentVersion ?? -1;
    }

    /// <summary>
    /// 在号段里分配一个新编号（见 <see cref="NpcIdSegmentCore.NextFree"/>）。
    /// <para>
    /// 只查本号段区间：全表拉回内存再筛，退役多年的行也要跟着读一遍，
    /// 而且"别的号段里有个更大号"会让分配结果莫名跳号。
    /// </para>
    /// </summary>
    public (bool Ok, int NpcId, string Message) AllocateId(NpcIdSegment segment)
    {
        var (min, max) = NpcIdSegmentCore.RangeOf(segment);
        if (min == 0) return (false, 0, "号段不合法：" + NpcIdSegmentCore.NameOf(segment));

        var used = _db.NpcTemplates.AsNoTracking()
            .Where(x => x.Id >= min && x.Id <= max)
            .Select(x => x.Id)
            .ToList();

        var id = NpcIdSegmentCore.NextFree(used, segment);
        return id > 0
            ? (true, id, $"号段 {NpcIdSegmentCore.NameOf(segment)} 的下一个编号：{id}")
            : (false, 0, $"号段 {NpcIdSegmentCore.NameOf(segment)} 已用尽（{min}–{max}）");
    }

    // ────────────────────────────── 写 ──────────────────────────────

    /// <summary>
    /// 保存一条 NPC 及其名下关系行。
    /// <para>
    /// <b>关系行采用"整包替换"</b>：先删该 NPC 名下全部启用行，再按草稿重插。
    /// 逐行 diff 会引入"草稿里删掉一行"这种最难表达的语义（缺行到底是删除还是没填？），
    /// 而编辑器每次提交的都是完整快照，整包替换语义最清楚。
    /// </para>
    /// <para><b>乐观锁</b>：<paramref name="expectedVersion"/> 与库里不一致直接拒，不做覆盖。</para>
    /// </summary>
    public NpcStoreResult Upsert(NpcDraft draft, int expectedVersion)
    {
        if (draft.Template is null) return NpcStoreResult.Fail("草稿缺少 Template");

        var t = draft.Template;
        if (t.NpcId <= 0) return NpcStoreResult.Fail("模板 npc_id 必须为正数");

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var now = _db.NpcTemplates.FirstOrDefault(x => x.Id == t.NpcId);
            if (now is not null && expectedVersion >= 0 && now.ContentVersion != expectedVersion)
            {
                return NpcStoreResult.Fail(
                    $"内容版本冲突：库里 content_version={now.ContentVersion}，草稿基于 {expectedVersion}（请重新加载后再改）");
            }

            var nextVersion = (now?.ContentVersion ?? 0) + 1;

            if (now is null)
            {
                now = new NpcTemplateEntity { Id = t.NpcId };
                _db.NpcTemplates.Add(now);
            }

            now.Name = t.Name;
            now.Title = t.Title;
            now.ModelKey = t.ModelKey;
            now.PrefabGuid = t.PrefabGuid;
            now.Scale = t.Scale;
            now.Level = t.Level;
            now.LevelVariance = t.LevelVariance;
            now.Reaction = t.Reaction;
            now.FactionGroup = t.FactionGroup;
            now.WuxingDomain = t.WuxingDomain;
            now.ServiceFlags = unchecked((int)t.ServiceFlags);
            now.AiTier = t.AiTier;
            now.AiBitsForceOn = unchecked((int)t.AiBitsForceOn);
            now.AiBitsForceOff = unchecked((int)t.AiBitsForceOff);
            now.AttackableOverride = t.AttackableOverride;
            now.SpeedWalk = t.SpeedWalk;
            now.SpeedRun = t.SpeedRun;
            now.AlertRadiusMeters = t.AlertRadiusMeters;
            now.GossipGroupId = t.GossipGroupId;
            now.Rank = t.Rank;
            now.KillCreditNpcId = t.KillCreditNpcId;
            now.ContentVersion = nextVersion;
            // 保存**保持**发布态（2026-10-27 拍板）：已发布的 NPC 改完仍在线。
            // 首次创建的新模板默认就是草稿，发布依旧是显式动作（publish 动词）；
            // 旧的"保存一律打回草稿"会让作者以为保存没生效，还会把线上 NPC 静默下线。

            _db.SaveChanges();

            // ---- 关系行整包替换 ----
            var result = new NpcStoreResult { Ok = true, Message = "saved" };

            _db.NpcVendorEntries.Where(x => x.NpcId == t.NpcId).ExecuteDelete();
            foreach (var v in draft.VendorRows)
            {
                _db.NpcVendorEntries.Add(new NpcVendorEntryEntity
                {
                    NpcId = t.NpcId, Slot = v.Slot, ItemId = v.ItemId,
                    BuyPriceOverride = v.BuyPriceOverride, SellPriceOverride = v.SellPriceOverride,
                    StockMax = v.StockMax, RestockSeconds = v.RestockSeconds, Category = v.Category,
                });
            }

            _db.NpcTrainerEntries.Where(x => x.NpcId == t.NpcId).ExecuteDelete();
            foreach (var x in draft.TrainerRows)
            {
                _db.NpcTrainerEntries.Add(new NpcTrainerEntryEntity
                {
                    NpcId = t.NpcId, SkillId = x.SkillId, ServiceKind = x.ServiceKind,
                    CostGold = x.CostGold, CostItemId = x.CostItemId, CostItemCount = x.CostItemCount, Seq = x.Seq,
                });
            }

            _db.NpcGossipOptions.Where(x => x.NpcId == t.NpcId).ExecuteDelete();
            foreach (var g in draft.GossipRows)
            {
                _db.NpcGossipOptions.Add(new NpcGossipOptionEntity
                {
                    NpcId = t.NpcId, GroupId = g.GroupId, Seq = g.Seq, TextId = g.TextId,
                    ActionKind = g.ActionKind, ActionArg = g.ActionArg,
                    ReqMinLevel = g.ReqMinLevel, ReqMaxLevel = g.ReqMaxLevel,
                });
            }

            _db.NpcScheduleSlots.Where(x => x.NpcId == t.NpcId).ExecuteDelete();
            foreach (var s in draft.ScheduleRows)
            {
                _db.NpcScheduleSlots.Add(new NpcScheduleSlotEntity
                {
                    NpcId = t.NpcId, Seq = s.Seq, FromSeconds = s.FromSeconds, ToSeconds = s.ToSeconds,
                    RouteId = s.RouteId, Posture = s.Posture, GossipGroupId = s.GossipGroupId,
                });
            }

            // 路线：草稿里带来的按 path_id 覆盖（不存在就新建）。
            // 只增改、不清扫：删一条路线会连带影响**引用它的其它 NPC** 的刷点与作息，
            // 那是跨 NPC 的破坏性操作，不该由"保存这一只 NPC"顺手做掉（删路线走专门的退役流程）。
            // path_id 是 IDENTITY（与 021 一致），新建时要显式指定编号——用 SET IDENTITY_INSERT，
            // 与 022 种子同一套做法；否则节点会挂到 EF 另给的新 id 上，刷点仍指向旧号。
            var writtenPaths = 0;
            var writtenNodes = 0;
            foreach (var r in draft.Routes)
            {
                var path = r.PathId > 0
                    ? _db.NpcWaypointPaths.FirstOrDefault(x => x.Id == r.PathId)
                    : null;

                var creating = path is null;
                if (creating)
                {
                    var nextPathId = (_db.NpcWaypointPaths.Max(x => (int?)x.Id) ?? 0) + 1;
                    path = new NpcWaypointPathEntity { Id = nextPathId };
                    _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointPath] ON");
                    try
                    {
                        _db.NpcWaypointPaths.Add(path);
                        path.Name = r.Name ?? string.Empty;
                        path.Mode = r.Mode;
                        path.PauseAtPointSeconds = r.PauseAtPointSeconds;
                        path.UseNavMesh = (byte)(r.UseNavMesh ? 1 : 0);
                        path.PointCount = r.Points.Count;
                        path.PublishState = 0;
                        path.IsValid = true;
                        _db.SaveChanges();
                    }
                    finally
                    {
                        _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointPath] OFF");
                    }
                }
                else
                {
                    path!.Name = r.Name ?? string.Empty;
                    path.Mode = r.Mode;
                    path.PauseAtPointSeconds = r.PauseAtPointSeconds;
                    path.UseNavMesh = (byte)(r.UseNavMesh ? 1 : 0);
                    path.PointCount = r.Points.Count;
                    // 已有路线保留发布态：已发布 NPC 的巡街线不能在保存时被顺手下线（新建的仍从草稿开始）。
                    path.IsValid = true;
                }

                var pathId = path.Id;
                _db.NpcWaypointNodes.Where(x => x.PathId == pathId).ExecuteDelete();

                var seq = 0;
                foreach (var p in r.Points)
                {
                    _db.NpcWaypointNodes.Add(new NpcWaypointNodeEntity
                    {
                        PathId = pathId,
                        Seq = p.Seq != 0 ? p.Seq : seq,
                        X = p.X, Y = p.Y, Z = p.Z,
                        PauseSeconds = p.PauseSeconds,
                        IsValid = true,
                    });
                    seq++;
                }

                writtenPaths++;
                writtenNodes += r.Points.Count;
            }

            // 刷点整包替换，但**保住草稿带来的编号**：spawn_point_id 是 IDENTITY，
            // 以前"删光重插"会让每次保存都换号（1→7→8…），往返对账永远有一格对不上，
            // 而号段/日志/对账表都按它指认"哪一点"。
            var spawnIds = new List<int>(draft.SpawnPoints.Count);
            var nextSpawnId = (_db.NpcSpawnPoints.Max(x => (int?)x.Id) ?? 0) + 1;
            foreach (var s in draft.SpawnPoints)
                spawnIds.Add(s.SpawnPointId > 0 ? s.SpawnPointId : nextSpawnId++);

            _db.NpcSpawnPoints.Where(x => x.NpcId == t.NpcId).ExecuteDelete();

            if (draft.SpawnPoints.Count > 0)
            {
                _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcSpawnPoint] ON");
                try
                {
                    for (var si = 0; si < draft.SpawnPoints.Count; si++)
                    {
                        var s = draft.SpawnPoints[si];
                        _db.NpcSpawnPoints.Add(new NpcSpawnPointEntity
                        {
                            Id = spawnIds[si],
                            ZoneId = s.ZoneId, ShardId = s.ShardId, X = s.X, Y = s.Y, Z = s.Z, Yaw = s.Yaw,
                            NpcId = t.NpcId, LevelOverride = s.LevelOverride, MaxAlive = s.MaxAlive,
                            RespawnSeconds = s.RespawnSeconds, RouteId = s.RouteId, FacingMode = s.FacingMode,
                            Enabled = 1, PublishState = 0, ContentVersion = nextVersion,
                        });
                    }

                    _db.SaveChanges();
                }
                finally
                {
                    _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcSpawnPoint] OFF");
                }
            }

            // 配方：草稿里的按 recipe_id 覆盖。与路线同一条纪律——配方是**全局表**，
            // 别的 NPC 也可能教同一张，所以只增改、不清扫（删配方不属于"保存这一只 NPC"）。
            // 以前这里整段没有写方：编辑器里改的产物/材料/成功率保存后**全部消失**，
            // 而 validate 照绿（它只看草稿），运行期继续用库里的旧行。
            var writtenRecipes = 0;
            var writtenTaught = 0;
            foreach (var r in draft.Recipes)
            {
                if (r.ProductItemId <= 0) continue;

                var recipe = r.RecipeId > 0
                    ? _db.NpcCraftRecipes.FirstOrDefault(x => x.Id == r.RecipeId)
                    : null;

                if (recipe is null)
                {
                    var nextRecipeId = (_db.NpcCraftRecipes.Max(x => (int?)x.Id) ?? 0) + 1;
                    recipe = new NpcCraftRecipeEntity { Id = nextRecipeId };
                    _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcCraftRecipe] ON");
                    try
                    {
                        _db.NpcCraftRecipes.Add(recipe);
                        ApplyRecipeFields(recipe, r, nextVersion);
                        _db.SaveChanges();
                    }
                    finally
                    {
                        _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [Game_HunduShijie_NpcCraftRecipe] OFF");
                    }
                }
                else
                {
                    ApplyRecipeFields(recipe, r, nextVersion);
                }

                writtenRecipes++;

                // 教学关系：这只 NPC 教这张配方。学费取草稿里 service_kind=2 那一行，没有就用配方工费。
                var cost = r.CostGold;
                foreach (var row in draft.TrainerRows)
                {
                    if (row.ServiceKind == 2 && row.SkillId == r.RecipeId) { cost = row.CostGold; break; }
                }

                var link = _db.NpcRecipeTaught.FirstOrDefault(x => x.NpcId == t.NpcId && x.RecipeId == recipe.Id);
                if (link is null)
                {
                    _db.NpcRecipeTaught.Add(new NpcRecipeTaughtEntity
                    {
                        RecipeId = recipe.Id,
                        NpcId = t.NpcId,
                        CostGold = cost,
                        Enabled = 1,
                        IsValid = true,
                    });
                }
                else
                {
                    link.CostGold = cost;
                    link.Enabled = 1;
                    link.IsValid = true;
                }

                writtenTaught++;
            }

            // 任务关系是"发布流程同步生成"的倒排索引；草稿里带来的是**手工补充**（如剧情 NPC 只收不发布）。
            // 同样整包替换，保证与编辑器所见一致。
            // 掉落：先删该 NPC 的旧绑定（连同条目），再按草稿重插。
            // 绑定与条目在怪物侧共用两张表，owner 列放 npc_id —— 删的时候必须按 binding_id 精确删条目，
            // 只删绑定会留下孤儿条目，下一次给同一 binding_id 插入时会撞主键。
            // **只写敌对 NPC**：友方 NPC 不会被打死，同 id 的行其实是怪物种子的掉落；
            // 保存铁匠/守门人绝不能顺手把那些行删掉或改掉（两边共用整数 id 空间）。
            var canWriteLoot = t.Reaction == (byte)Horizon.Game.Sim.Npcs.NpcStance.Hostile;
            var oldBindingIds = canWriteLoot
                ? _db.MonsterLootBindings
                    .Where(x => x.MonsterId == t.NpcId)
                    .Select(x => x.Id)
                    .ToList()
                : new List<int>();

            if (oldBindingIds.Count > 0)
            {
                _db.MonsterDropEntries.Where(x => oldBindingIds.Contains(x.BindingId)).ExecuteDelete();
                _db.MonsterLootBindings.Where(x => x.MonsterId == t.NpcId).ExecuteDelete();
            }

            foreach (var b in canWriteLoot ? draft.LootBindings : new List<MonsterLootBindingSnapshotDto>())
            {
                var binding = new MonsterLootBindingEntity
                {
                    MonsterId = t.NpcId,
                    LootTableId = b.LootTableId,
                    RollChanceBps = b.RollChanceBps,
                    MinCount = b.MinCount,
                    MaxCount = b.MaxCount,
                    // 绑定表的 condition_kind 是 tinyint；DTO 是 int，收拢到已知档 0..3。
                    ConditionKind = (byte)Math.Clamp(b.ConditionKind, 0, 3),
                    QuestId = b.QuestId,
                    MoneyMin = b.MoneyMin,
                    MoneyMax = b.MoneyMax,
                };

                _db.MonsterLootBindings.Add(binding);
                _db.SaveChanges();   // 先拿到自增 binding_id，条目才能挂上去

                foreach (var e in b.Entries)
                {
                    _db.MonsterDropEntries.Add(new MonsterDropEntryEntity
                    {
                        BindingId = binding.Id,
                        ItemId = e.ItemId,
                        ChanceBps = e.ChanceBps,
                        CountMin = e.CountMin,
                        CountMax = e.CountMax,
                        Seq = e.Seq,
                        GroupId = e.GroupId,
                        ConditionKind = e.ConditionKind,
                        QuestId = e.QuestId,
                    });
                }
            }

            _db.NpcQuestLinks.Where(x => x.NpcId == t.NpcId).ExecuteDelete();
            foreach (var l in draft.QuestLinks)
            {
                _db.NpcQuestLinks.Add(new NpcQuestLinkEntity
                {
                    NpcId = t.NpcId, QuestId = l.QuestId, Role = l.Role, OrderSeq = l.OrderSeq,
                });
            }

            _db.SaveChanges();

            // 台词正文（023 可选列）：必须在上面这次 SaveChanges 之后——那时 gossip 行才有自增 option_id。
            var gossipTextWritten = WriteGossipText(t.NpcId, draft.GossipRows, result.Problems);
            var skillMountsWritten = WriteSkillMounts(t.NpcId, draft.SkillMounts, result.Problems);

            result.WrittenRows["NpcTemplate"] = 1;
            result.WrittenRows["NpcVendorEntry"] = draft.VendorRows.Count;
            result.WrittenRows["NpcTrainerEntry"] = draft.TrainerRows.Count;
            result.WrittenRows["NpcGossipOption"] = draft.GossipRows.Count;
            result.WrittenRows["NpcGossipText"] = gossipTextWritten;
            result.WrittenRows["NpcSkillMount"] = skillMountsWritten;
            result.WrittenRows["NpcScheduleSlot"] = draft.ScheduleRows.Count;
            result.WrittenRows["NpcSpawnPoint"] = draft.SpawnPoints.Count;
            result.WrittenRows["NpcWaypointPath"] = writtenPaths;
            result.WrittenRows["NpcWaypointNode"] = writtenNodes;
            result.WrittenRows["NpcCraftRecipe"] = writtenRecipes;
            result.WrittenRows["NpcRecipeTaught"] = writtenTaught;
            result.WrittenRows["NpcQuestLink"] = draft.QuestLinks.Count;

            var lootRows = 0;
            foreach (var b in draft.LootBindings) lootRows += 1 + b.Entries.Count;
            result.WrittenRows["MonsterLootBinding+Entry"] = lootRows;

            tx.Commit();
            return result;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return NpcStoreResult.Fail($"写入失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 把草稿里的配方写到实体上。JSON 两列按"由易到难"原序序列化——
    /// 反过来写不会报错，只会让成功率曲线整体倒过来（灰档变成必成）。
    /// </summary>
    private static void ApplyRecipeFields(NpcCraftRecipeEntity recipe, NpcRecipeSnapshotDto dto, int contentVersion)
    {
        var materials = new List<Dictionary<string, int>>(dto.Materials.Count);
        foreach (var m in dto.Materials)
        {
            if (m.ItemId <= 0 || m.Count <= 0) continue;
            materials.Add(new Dictionary<string, int> { ["item"] = m.ItemId, ["count"] = m.Count });
        }

        recipe.Name = string.IsNullOrWhiteSpace(dto.Name) ? ("配方#" + dto.RecipeId) : dto.Name;
        recipe.ProductItemId = dto.ProductItemId;
        recipe.MaterialsJson = JsonSerializer.Serialize(materials);
        recipe.CostGold = dto.CostGold;
        recipe.StationNpcId = dto.StationNpcId;
        recipe.SkillReq = dto.SkillReq;
        recipe.QualityGapCapsJson = JsonSerializer.Serialize(dto.QualityGapCaps);
        recipe.QualityChanceBpsJson = JsonSerializer.Serialize(dto.QualityChanceBps);
        recipe.LearnSourceKind = dto.LearnSourceKind;
        // 配方今天不在"保存即下线"的发布开关里（SetPublishState 只管模板/刷点/路线），
        // 所以保存时直接置发布态，免得"改了配方、游戏里还是旧的"。
        recipe.PublishState = 1;
        recipe.ContentVersion = Math.Max(1, contentVersion);
        recipe.IsValid = true;
    }

    /// <summary>发布/退役/收回草稿。返回受影响的模板行数（0 = 没有这个编号）。</summary>
    public NpcStoreResult SetPublishState(int npcId, byte state)
    {
        var row = _db.NpcTemplates.FirstOrDefault(x => x.Id == npcId);
        if (row is null) return NpcStoreResult.Fail($"NPC {npcId} 不存在");

        row.PublishState = state;
        row.ContentVersion += 1;

        // 刷点跟随模板的发布态：只发布模板而不发布刷点，结果就是"配了 NPC 但世界里没有他"。
        foreach (var sp in _db.NpcSpawnPoints.Where(x => x.NpcId == npcId))
        {
            sp.PublishState = state;
            sp.ContentVersion = row.ContentVersion;
        }

        // 路线也要跟随：刷点/作息引用的路线没发布，导出时路线整条缺失，
        // 装配器随即报 `spawn_unknown_route`——表现是"发布了 NPC，世界里却没有他"，
        // 而库里每一张表看起来都写对了。只发布本 NPC 引用的那些，不碰别的 NPC 的路线。
        var routeIds = new HashSet<int>();
        foreach (var sp in _db.NpcSpawnPoints.AsNoTracking().Where(x => x.NpcId == npcId))
            if (sp.RouteId > 0) routeIds.Add(sp.RouteId);
        foreach (var slot in _db.NpcScheduleSlots.AsNoTracking().Where(x => x.NpcId == npcId))
            if (slot.RouteId > 0) routeIds.Add(slot.RouteId);

        var publishedRoutes = 0;
        if (routeIds.Count > 0)
        {
            foreach (var path in _db.NpcWaypointPaths.Where(x => routeIds.Contains(x.Id)))
            {
                path.PublishState = state;
                publishedRoutes++;
            }
        }

        _db.SaveChanges();

        var result = new NpcStoreResult { Ok = true, Message = state == 1 ? "published" : "unpublished" };
        result.WrittenRows["NpcTemplate"] = 1;
        result.WrittenRows["NpcWaypointPath"] = publishedRoutes;
        return result;
    }

    // ────────────────────────────── JSON 列解析 ──────────────────────────────

    /// <summary>解析 <c>materials_json</c>：<c>[{"item":501,"count":3}]</c>。</summary>
    public static bool TryParseMaterials(string? json, out List<NpcMaterialNeedDto> materials, out string error)
    {
        materials = new List<NpcMaterialNeedDto>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "not_array";
                return false;
            }

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var item = e.TryGetProperty("item", out var i) ? i.GetInt32() : 0;
                var count = e.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
                materials.Add(new NpcMaterialNeedDto { ItemId = item, Count = count });
            }

            // 空数组不是"解析成功但没材料"：它是空配方，必须让调用方按坏数据报出来。
            if (materials.Count == 0)
            {
                error = "empty_array";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>解析整型数组列（成功档上限 / 成功率）。</summary>
    public static bool TryParseInts(string? json, out List<int> values, out string error)
    {
        values = new List<int>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "not_array";
                return false;
            }

            foreach (var e in doc.RootElement.EnumerateArray()) values.Add(e.GetInt32());

            if (values.Count == 0)
            {
                error = "empty_array";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

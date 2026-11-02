using System.Globalization;
using System.Text.Json;
using Horizon.Entities;
using Horizon.Game.Sim.Items;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Horizon.ItemCli;

/// <summary>一次写入的结果（与 PetCli/MonsterCli 的 StoreResult 同形，编辑器解析代码可复用）。</summary>
public sealed class ItemStoreResult
{
    public bool Ok { get; set; }
    public int TemplateId { get; set; }
    public int ContentVersion { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();

    /// <summary>本次实际落库的行数。<b>每个写方都自带状态位</b>：只回 ok=true 说不清"写了几张表"。</summary>
    public Dictionary<string, int> WrittenRows { get; set; } = new();
}

/// <summary>列表行摘要。</summary>
public sealed class ItemListRow
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ItemClass { get; set; }
    public int SubType { get; set; }
    public int Quality { get; set; }
    public int ItemLevel { get; set; }
    public int InventoryType { get; set; }
    public int PublishState { get; set; }
    public int ContentVersion { get; set; }
    public int StatCount { get; set; }
    public int SourceCount { get; set; }
    public string Segment { get; set; } = string.Empty;
}

/// <summary>
/// 物品表的数据访问；同时是校验器的引用对账实现。
/// <para>
/// <b>写顺序 = 引用完整性顺序</b>：套装/词缀 → 模板主表 → 属性/伤害/插槽/法术/出处/多语言。
/// 反过来写会让"物品指向一个还不存在的套装"这一刻真实存在，而运行时只读已发布行。
/// </para>
/// </summary>
public sealed class ItemStore : IDisposable
{
    private readonly GameEntityContext _db;

    public ItemStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new GameEntityContext(options);
    }

    /// <summary>主表是否可达（区分"没配物品"与"库还没迁 023"——这两件事在只读侧长得一样）。</summary>
    public bool TemplateTableReachable(out string error)
    {
        try
        {
            // 新列也一并探一次：只查 Count() 的话，023 没跑时会"连通"然后第一次保存才炸。
            _ = _db.ItemTemplates.AsNoTracking().Select(t => new { t.Id, t.InventoryType, t.PublishState }).FirstOrDefault();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    // ---------------- 跨域对账 ----------------
    // 查不到不等于不存在：表不可达时按"存在"处理，把这条对账降级为无信息，
    // 而不是让一次库连接失败去否决一份合法草稿。

    public bool SkillExists(int skillId)
    {
        try { return _db.SkillTemplates.Any(s => s.Id == skillId); }
        catch (Exception) { return true; }
    }

    public bool SetExists(int setId)
    {
        try { return _db.ItemSets.Any(s => s.Id == setId); }
        catch (Exception) { return true; }
    }

    public bool AffixExists(int affixId)
    {
        try { return _db.ItemAffixes.Any(a => a.Id == affixId); }
        catch (Exception) { return true; }
    }

    // ---------------- 读 ----------------

    public List<ItemListRow> List(int? itemClass = null, int? quality = null, ItemSegment? segment = null, int limit = 500)
    {
        var query = _db.ItemTemplates.AsNoTracking().AsQueryable();
        if (itemClass.HasValue) query = query.Where(t => t.ItemType == itemClass.Value);
        if (quality.HasValue) query = query.Where(t => t.BaseQuality == quality.Value);
        if (segment.HasValue)
        {
            var (min, max) = ItemCore.RangeOf(segment.Value);
            query = query.Where(t => t.Id >= min && t.Id <= max);
        }

        var templates = query.OrderBy(t => t.Id).Take(limit).ToList();
        var ids = templates.Select(t => t.Id).ToList();
        var statCounts = _db.ItemStats.AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => s.ItemId).AsEnumerable().GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var sourceCounts = _db.ItemSources.AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => s.ItemId).AsEnumerable().GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());

        return templates.Select(t => new ItemListRow
        {
            TemplateId = t.Id,
            Name = t.ItemName,
            ItemClass = t.ItemType,
            SubType = t.SubType,
            Quality = t.BaseQuality,
            ItemLevel = t.ItemLevel,
            InventoryType = t.InventoryType,
            PublishState = t.PublishState,
            ContentVersion = t.ContentVersion,
            StatCount = statCounts.GetValueOrDefault(t.Id),
            SourceCount = sourceCounts.GetValueOrDefault(t.Id),
            Segment = ItemCore.SegmentName(ItemCore.SegmentOf(t.Id)),
        }).ToList();
    }

    /// <summary>把一件物品读成草稿（主表 + 从表 + 套装/词缀）。</summary>
    public ItemDraft? GetDraft(int templateId, out string error)
    {
        error = string.Empty;
        var row = _db.ItemTemplates.AsNoTracking().FirstOrDefault(t => t.Id == templateId);
        if (row is null)
        {
            error = $"物品 {templateId} 不存在";
            return null;
        }

        var spec = new ItemSpec
        {
            TemplateId = row.Id,
            Name = row.ItemName,
            Description = row.Description ?? string.Empty,
            ItemClass = row.ItemType,
            SubType = row.SubType,
            Quality = row.BaseQuality,
            Rarity = row.Rarity,
            ItemLevel = row.ItemLevel,
            InventoryType = row.InventoryType,
            Element = row.Element,
            BindType = row.BindType,
            Flags = row.ItemFlags,
            MaterialGrade = row.MaterialGrade,
            GatherType = row.GatherType ?? 0,
            SourceType = row.SourceType,
            ProfessionLimit = row.ProfessionLimit ?? string.Empty,
            LevelRequire = row.LevelRequire,
            RealmRequire = row.RealmRequire,
            RequiredSkill = row.RequiredSkill,
            RequiredSkillRank = row.RequiredSkillRank,
            AllowableClass = row.AllowableClass,
            AllowableRace = row.AllowableRace,
            MaxStack = row.MaxStack <= 0 ? 1 : row.MaxStack,
            MaxCount = row.MaxCount,
            ContainerSlots = row.ContainerSlots,
            Durability = row.Durability,
            Armor = row.Armor,
            Block = row.BlockValue,
            AttackDelayMs = row.AttackDelayMs,
            ResistanceElement = row.ResistanceElement,
            ResistanceValue = row.ResistanceValue,
            BuyPrice = row.BuyPrice,
            SellPrice = row.SellPrice,
            MinMoneyLoot = row.MinMoneyLoot,
            MaxMoneyLoot = row.MaxMoneyLoot,
            SocketBonus = row.SocketBonus,
            GemProperties = row.GemProperties,
            SetId = row.SetId ?? 0,
            RandomProperty = row.RandomProperty,
            RandomSuffix = row.RandomSuffix,
            DisenchantId = row.DisenchantId,
            BagFamily = row.BagFamily,
            FoodType = row.FoodType,
            DurationSeconds = row.DurationSeconds,
            ValidDays = row.ValidDays,
            StartQuest = row.StartQuest,
            PageText = row.PageText,
            Language = row.LanguageId,
            CanTrade = row.CanTrade,
            CanDestroy = row.CanDestroy,
            IsUnique = row.IsUnique,
            IconPath = row.IconPath ?? string.Empty,
            ModelPath = row.ModelPath ?? string.Empty,
            Remark = row.Remark ?? string.Empty,
            SourceDetail = row.SourceDetail ?? string.Empty,
            UseEffect = row.UseEffect ?? string.Empty,
        };

        var statRows = _db.ItemStats.AsNoTracking().Where(s => s.ItemId == templateId)
            .OrderBy(s => s.Seq).ToList();
        if (statRows.Count > 0)
            spec.Stats = statRows.Select(s => new ItemStatLine { Type = s.StatType, Value = s.StatValue }).ToArray();
        else
            // 老种子物品只有 base_attributes JSON：读回属性行，避免"打开再保存"把它们抹掉。
            spec.Stats = ItemCore.ParseAttributeJson(row.BaseAttributes).ToArray();

        spec.Damage = _db.ItemDamages.AsNoTracking().Where(d => d.ItemId == templateId).OrderBy(d => d.Seq)
            .Select(d => new ItemDamageLine { Type = d.DamageType, Min = d.DamageMin, Max = d.DamageMax })
            .ToList().ToArray();
        spec.Sockets = _db.ItemSockets.AsNoTracking().Where(s => s.ItemId == templateId).OrderBy(s => s.Seq)
            .Select(s => new ItemSocketLine { Color = s.SocketColor, Bonus = s.SocketBonus })
            .ToList().ToArray();
        spec.Spells = _db.ItemSpells.AsNoTracking().Where(s => s.ItemId == templateId).OrderBy(s => s.Seq)
            .Select(s => new ItemSpellLine
            {
                SpellId = s.SpellId, Trigger = s.SpellTrigger, Charges = s.Charges,
                PpmRate = s.PpmRate, CooldownMs = s.CooldownMs, CategoryCooldownMs = s.CategoryCooldownMs,
            }).ToList().ToArray();
        spec.Sources = _db.ItemSources.AsNoTracking().Where(s => s.ItemId == templateId).OrderBy(s => s.Seq)
            .Select(s => new ItemSourceLine
            {
                SourceType = s.SourceType, RefType = s.RefType, RefId = s.RefId, Note = s.Note,
            }).ToList().ToArray();

        var draft = ItemDraft.FromSpec(spec, row.PublishState, row.ContentVersion);

        draft.Localizations = _db.ItemLocalizations.AsNoTracking().Where(l => l.ItemId == templateId)
            .Select(l => new ItemLocalizationDraft
            {
                Locale = l.Locale, Name = l.ItemName, Description = l.Description,
            }).ToList();

        if (spec.SetId > 0) draft.Set = LoadSet(spec.SetId);
        var affixId = spec.RandomProperty > 0 ? spec.RandomProperty : spec.RandomSuffix;
        if (affixId > 0) draft.Affix = LoadAffix(affixId);

        return draft;
    }

    /// <summary>公开读套装（tooltip 预览要拿名字与档位；不存在时返回只有 id 的骨架）。</summary>
    public ItemSetDraft? GetSet(int setId) => LoadSet(setId);

    private ItemSetDraft? LoadSet(int setId)    {
        var row = _db.ItemSets.AsNoTracking().FirstOrDefault(s => s.Id == setId);
        if (row is null) return new ItemSetDraft { SetId = setId };

        var tiers = _db.ItemSetBonuses.AsNoTracking().Where(b => b.SetId == setId).OrderBy(b => b.Pieces).ToList();
        var stats = _db.ItemSetBonusStats.AsNoTracking().Where(s => s.SetId == setId).ToList();
        var draft = new ItemSetDraft
        {
            SetId = row.Id,
            Name = row.SetName,
            Description = row.Description,
            ItemLevel = row.ItemLevel,
            PublishState = row.PublishState,
        };
        foreach (var tier in tiers)
        {
            draft.Tiers.Add(new ItemSetTierDraft
            {
                Pieces = tier.Pieces,
                Description = tier.BonusDescription,
                SpellId = tier.SpellId,
                Stats = stats.Where(s => s.Pieces == tier.Pieces)
                    .Select(s => new ItemStatDraft { StatType = s.StatType, StatValue = s.StatValue }).ToList(),
            });
        }
        return draft;
    }

    private ItemAffixDraft? LoadAffix(int affixId)
    {
        var row = _db.ItemAffixes.AsNoTracking().FirstOrDefault(a => a.Id == affixId);
        if (row is null) return new ItemAffixDraft { AffixId = affixId };
        return new ItemAffixDraft
        {
            AffixId = row.Id,
            Kind = row.AffixKind,
            Name = row.AffixName,
            Description = row.Description,
            ValueMin = row.ValueMin,
            ValueMax = row.ValueMax,
            PublishState = row.PublishState,
            Stats = _db.ItemAffixStats.AsNoTracking().Where(s => s.AffixId == affixId).OrderBy(s => s.Seq)
                .Select(s => new ItemStatDraft { StatType = s.StatType, StatValue = s.StatValue }).ToList(),
        };
    }

    // ---------------- 写 ----------------

    /// <summary>
    /// 保存草稿（主表 upsert + 从表删重建 + 可选的套装/词缀）。
    /// <para><b>新建时才采用草稿里的 publishState</b>：更新路径沿用旧值，
    /// 否则"存个草稿"会顺手把物品发布上线。</para>
    /// </summary>
    public ItemStoreResult Save(ItemDraft draft)
    {
        var result = new ItemStoreResult { TemplateId = draft.TemplateId };
        if (ItemCore.SegmentOf(draft.TemplateId) == ItemSegment.Invalid)
        {
            result.Message = $"物品编号 {draft.TemplateId} 不在任何号段内，拒绝写入";
            return result;
        }

        var spec = draft.ToSpec();
        using var tx = _db.Database.BeginTransaction();
        var identityInsertOn = false;
        const string templateTable = "Game_HunduShijie_ItemTemplate";
        try
        {
            // 1) 套装与词缀先写：主表要引用它们的 id。
            if (draft.Set is { SetId: > 0 } setDraft)
            {
                result.WrittenRows["set"] = UpsertSet(setDraft, out var setWarnings);
                result.Warnings.AddRange(setWarnings);
            }
            if (draft.Affix is { AffixId: > 0 } affixDraft)
            {
                result.WrittenRows["affix"] = UpsertAffix(affixDraft);
            }

            // 2) 主表
            var row = _db.ItemTemplates.FirstOrDefault(t => t.Id == draft.TemplateId);
            var isNew = row is null;
            if (isNew)
            {
                row = new ItemTemplateEntity { Id = draft.TemplateId };
                // item_id 在库里是 IDENTITY（种子脚本用 IDENTITY_INSERT 写入），而编辑器分配的编号是**业务键**、
                // 必须原样落库。EF 允许显式给 store-generated 键赋值，但 SQL Server 需要 IDENTITY_INSERT ON。
                // 不检测、直接开会在"非 identity 列"的部署上报错，所以先问一次系统表。
                identityInsertOn = IsIdentityColumn(templateTable, "item_id");
                if (identityInsertOn)
                    _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [" + templateTable + "] ON");
                _db.ItemTemplates.Add(row);

                // 老库里这 13 个字符串列都是 NOT NULL（历史 schema 没给默认值）。
                // 新建行必须显式给空串，否则 INSERT 直接失败——演练库里量到的第一处就是这样。
                // 更新路径**不碰**它们：编辑器不编辑这些列，覆盖等于悄悄清空别人的数据。
                row.RandomAttributes = string.Empty;
                row.RandomAttrCount = string.Empty;
                row.GemSlotRates = string.Empty;
                row.SynthesisRecipe = string.Empty;
                row.InheritRate = string.Empty;
                row.UseEffect = string.Empty;
            }

            row.ItemName = draft.Name ?? string.Empty;
            row.Description = draft.Description ?? string.Empty;
            row.ItemType = draft.ItemClass;
            row.SubType = draft.SubType;
            row.BaseQuality = draft.Quality;
            row.Rarity = draft.Rarity;
            row.ItemLevel = draft.ItemLevel;
            row.InventoryType = draft.InventoryType;
            row.Element = draft.Element;
            row.BindType = draft.BindType;
            row.ItemFlags = draft.Flags;
            row.MaterialGrade = draft.MaterialGrade;
            row.GatherType = draft.GatherType;
            row.SourceType = draft.SourceType;
            row.ProfessionLimit = draft.ProfessionLimit ?? string.Empty;
            row.LevelRequire = draft.LevelRequire;
            row.RealmRequire = draft.RealmRequire;
            row.RequiredSkill = draft.RequiredSkill;
            row.RequiredSkillRank = draft.RequiredSkillRank;
            row.AllowableClass = draft.AllowableClass;
            row.AllowableRace = draft.AllowableRace;
            row.MaxStack = draft.MaxStack;
            row.MaxCount = draft.MaxCount;
            row.ContainerSlots = draft.ContainerSlots;
            row.Durability = draft.Durability;
            row.Armor = draft.Armor;
            row.BlockValue = draft.Block;
            row.AttackDelayMs = draft.AttackDelayMs;
            row.ResistanceElement = draft.ResistanceElement;
            row.ResistanceValue = draft.ResistanceValue;
            row.BuyPrice = draft.BuyPrice;
            row.SellPrice = draft.SellPrice;
            row.MinMoneyLoot = draft.MinMoneyLoot;
            row.MaxMoneyLoot = draft.MaxMoneyLoot;
            row.SocketBonus = draft.SocketBonus;
            row.GemProperties = draft.GemProperties;
            row.SetId = draft.SetId > 0 ? draft.SetId : null;
            row.RandomProperty = draft.RandomProperty;
            row.RandomSuffix = draft.RandomSuffix;
            row.DisenchantId = draft.DisenchantId;
            row.BagFamily = draft.BagFamily;
            row.FoodType = draft.FoodType;
            row.DurationSeconds = draft.DurationSeconds;
            row.ValidDays = draft.ValidDays;
            row.StartQuest = draft.StartQuest;
            row.PageText = draft.PageText;
            row.LanguageId = draft.Language;
            row.CanTrade = draft.CanTrade;
            row.CanDestroy = draft.CanDestroy;
            row.IsUnique = draft.IsUnique;
            row.IconPath = draft.IconPath ?? string.Empty;
            row.ModelPath = draft.ModelPath ?? string.Empty;
            row.Remark = draft.Remark ?? string.Empty;
            // 使用效果是消耗品"能不能用"的数据源；编辑器读草稿时带回来、保存时原样写回。
            row.UseEffect = draft.UseEffect ?? string.Empty;
            // 属性行是权威，base_attributes 由它派生（服务端旧读取路径因此不用改）。
            row.BaseAttributes = ItemCore.BuildAttributeJson(spec.Stats);
            row.SourceDetail = BuildSourceDetail(spec.Sources);
            row.ContentVersion = isNew ? Math.Max(1, draft.ContentVersion) : Math.Max(row.ContentVersion + 1, draft.ContentVersion);
            if (isNew) row.PublishState = draft.PublishState > 2 ? (byte)0 : draft.PublishState;
            if (isNew)
            {
                row.CreateTime = DateTime.UtcNow;
                row.IsValid = true;
            }
            row.UpdateTime = DateTime.UtcNow;
            result.WrittenRows["template"] = 1;

            // 3) 从表：删旧写新（留着旧行会让"删掉一条属性"看起来失败）
            _db.ItemStats.RemoveRange(_db.ItemStats.Where(s => s.ItemId == draft.TemplateId));
            var seq = 0;
            foreach (var line in spec.Stats)
            {
                seq++;
                _db.ItemStats.Add(new ItemStatTemplateEntity
                {
                    ItemId = draft.TemplateId, Seq = seq, StatType = line.Type, StatValue = line.Value,
                });
            }
            result.WrittenRows["stats"] = spec.Stats.Length;

            _db.ItemDamages.RemoveRange(_db.ItemDamages.Where(d => d.ItemId == draft.TemplateId));
            seq = 0;
            foreach (var line in spec.Damage)
            {
                seq++;
                _db.ItemDamages.Add(new ItemDamageTemplateEntity
                {
                    ItemId = draft.TemplateId, Seq = seq,
                    DamageType = line.Type, DamageMin = line.Min, DamageMax = line.Max,
                });
            }
            result.WrittenRows["damage"] = spec.Damage.Length;

            _db.ItemSockets.RemoveRange(_db.ItemSockets.Where(s => s.ItemId == draft.TemplateId));
            seq = 0;
            foreach (var line in spec.Sockets)
            {
                seq++;
                _db.ItemSockets.Add(new ItemSocketTemplateEntity
                {
                    ItemId = draft.TemplateId, Seq = seq, SocketColor = line.Color, SocketBonus = line.Bonus,
                });
            }
            result.WrittenRows["sockets"] = spec.Sockets.Length;

            _db.ItemSpells.RemoveRange(_db.ItemSpells.Where(s => s.ItemId == draft.TemplateId));
            seq = 0;
            foreach (var line in spec.Spells)
            {
                seq++;
                _db.ItemSpells.Add(new ItemSpellTemplateEntity
                {
                    ItemId = draft.TemplateId, Seq = seq, SpellId = line.SpellId, SpellTrigger = line.Trigger,
                    Charges = line.Charges, PpmRate = line.PpmRate, CooldownMs = line.CooldownMs,
                    CategoryCooldownMs = line.CategoryCooldownMs,
                });
            }
            result.WrittenRows["spells"] = spec.Spells.Length;

            _db.ItemSources.RemoveRange(_db.ItemSources.Where(s => s.ItemId == draft.TemplateId));
            seq = 0;
            foreach (var line in spec.Sources)
            {
                seq++;
                _db.ItemSources.Add(new ItemSourceEntity
                {
                    ItemId = draft.TemplateId, Seq = seq, SourceType = line.SourceType,
                    RefType = line.RefType, RefId = line.RefId, Note = line.Note ?? string.Empty,
                });
            }
            result.WrittenRows["sources"] = spec.Sources.Length;

            _db.ItemLocalizations.RemoveRange(_db.ItemLocalizations.Where(l => l.ItemId == draft.TemplateId));
            foreach (var loc in draft.Localizations ?? new List<ItemLocalizationDraft>())
            {
                if (string.IsNullOrWhiteSpace(loc.Locale)) continue;
                _db.ItemLocalizations.Add(new ItemLocalizationEntity
                {
                    ItemId = draft.TemplateId, Locale = loc.Locale.Trim(),
                    ItemName = loc.Name ?? string.Empty, Description = loc.Description ?? string.Empty,
                });
            }
            result.WrittenRows["localizations"] = draft.Localizations?.Count ?? 0;

            _db.SaveChanges();
            if (identityInsertOn)
            {
                _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [" + templateTable + "] OFF");
                identityInsertOn = false;
            }
            tx.Commit();

            result.Ok = true;
            result.ContentVersion = row.ContentVersion;
            result.Message = $"物品 {draft.TemplateId} 已保存（{(isNew ? "新建" : "更新")}）";
            return result;
        }
        catch (Exception ex)
        {
            // 先把会话级开关复位再回滚：IDENTITY_INSERT 是**连接**状态，漏关会污染连接池里的下一位使用者。
            if (identityInsertOn)
            {
                try { _db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [" + templateTable + "] OFF"); }
                catch (Exception) { /* 连接已坏时这条也没意义 */ }
            }
            tx.Rollback();
            result.Ok = false;
            // 事务回滚了，这次一条都没落库。把半路填进去的行数清掉——
            // 留着会让编辑器打印"template=1, stats=3"而实际上什么都没写（比不报更坏）。
            result.WrittenRows.Clear();
            result.Message = "保存失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    /// <summary>该列是不是 IDENTITY（决定插入时要不要开 IDENTITY_INSERT）。</summary>
    private bool IsIdentityColumn(string table, string column)
    {
        return _db.Database.SqlQueryRaw<int>(
                "SELECT CAST(ISNULL((SELECT c.is_identity FROM sys.columns c " +
                "WHERE c.object_id = OBJECT_ID({0}) AND c.name = {1}), 0) AS int) AS Value",
                "dbo." + table, column)
            .AsEnumerable().FirstOrDefault() != 0;
    }

    private int UpsertSet(ItemSetDraft draft, out List<string> warnings)
    {
        warnings = new List<string>();
        if (!ItemCore.IsInSegment(draft.SetId, ItemSegment.Set))
            warnings.Add($"套装 id {draft.SetId} 不在 920000..929999 段，仍按给定 id 写入");

        var row = _db.ItemSets.Find(draft.SetId);
        if (row is null)
        {
            row = new ItemSetEntity { Id = draft.SetId };
            _db.ItemSets.Add(row);
        }
        row.SetName = draft.Name ?? string.Empty;
        row.Description = draft.Description ?? string.Empty;
        row.ItemLevel = draft.ItemLevel;
        row.PublishState = draft.PublishState;

        _db.ItemSetBonuses.RemoveRange(_db.ItemSetBonuses.Where(b => b.SetId == draft.SetId));
        _db.ItemSetBonusStats.RemoveRange(_db.ItemSetBonusStats.Where(s => s.SetId == draft.SetId));
        foreach (var tier in draft.Tiers ?? new List<ItemSetTierDraft>())
        {
            if (tier.Pieces <= 0) continue;
            _db.ItemSetBonuses.Add(new ItemSetBonusEntity
            {
                SetId = draft.SetId, Pieces = tier.Pieces,
                BonusDescription = tier.Description ?? string.Empty, SpellId = tier.SpellId,
            });
            foreach (var stat in tier.Stats ?? new List<ItemStatDraft>())
            {
                if (stat.StatType <= 0 || stat.StatValue == 0) continue;
                _db.ItemSetBonusStats.Add(new ItemSetBonusStatEntity
                {
                    SetId = draft.SetId, Pieces = tier.Pieces, StatType = stat.StatType, StatValue = stat.StatValue,
                });
            }
        }
        return 1 + (draft.Tiers?.Count ?? 0);
    }

    private int UpsertAffix(ItemAffixDraft draft)
    {
        var row = _db.ItemAffixes.Find(draft.AffixId);
        if (row is null)
        {
            row = new ItemRandomAffixEntity { Id = draft.AffixId };
            _db.ItemAffixes.Add(row);
        }
        row.AffixKind = draft.Kind;
        row.AffixName = draft.Name ?? string.Empty;
        row.Description = draft.Description ?? string.Empty;
        row.ValueMin = draft.ValueMin;
        row.ValueMax = draft.ValueMax;
        row.PublishState = draft.PublishState;

        _db.ItemAffixStats.RemoveRange(_db.ItemAffixStats.Where(s => s.AffixId == draft.AffixId));
        var seq = 0;
        foreach (var stat in draft.Stats ?? new List<ItemStatDraft>())
        {
            if (stat.StatType <= 0 || stat.StatValue == 0) continue;
            seq++;
            _db.ItemAffixStats.Add(new ItemRandomAffixStatEntity
            {
                AffixId = draft.AffixId, Seq = seq, StatType = stat.StatType, StatValue = stat.StatValue,
            });
        }
        return 1 + seq;
    }

    private static string BuildSourceDetail(IReadOnlyList<ItemSourceLine> sources)
    {
        if (sources == null || sources.Count == 0) return string.Empty;
        var rows = sources.Select(s => new Dictionary<string, object?>
        {
            ["sourceType"] = s.SourceType,
            ["refType"] = s.RefType,
            ["refId"] = s.RefId,
            ["note"] = s.Note ?? string.Empty,
        }).ToList();
        return JsonSerializer.Serialize(rows);
    }

    /// <summary>发布/停用/退回草稿。</summary>
    public ItemStoreResult SetPublishState(int templateId, int publishState)
    {
        var result = new ItemStoreResult { TemplateId = templateId };
        var row = _db.ItemTemplates.FirstOrDefault(t => t.Id == templateId);
        if (row is null)
        {
            result.Message = $"物品 {templateId} 不存在";
            return result;
        }
        row.PublishState = (byte)Math.Clamp(publishState, 0, 2);
        row.UpdateTime = DateTime.UtcNow;
        _db.SaveChanges();
        result.Ok = true;
        result.ContentVersion = row.ContentVersion;
        result.WrittenRows["template"] = 1;
        result.Message = publishState switch
        {
            1 => $"物品 {templateId} 已发布",
            2 => $"物品 {templateId} 已停用",
            _ => $"物品 {templateId} 退回草稿",
        };
        return result;
    }

    /// <summary>删除模板及其从表。<b>玩家背包里的实例不删</b>：有实例时直接拒绝。</summary>
    public ItemStoreResult Delete(int templateId)
    {
        var result = new ItemStoreResult { TemplateId = templateId };
        var row = _db.ItemTemplates.FirstOrDefault(t => t.Id == templateId);
        if (row is null)
        {
            result.Message = $"物品 {templateId} 不存在";
            return result;
        }

        var instances = _db.Items.Count(i => i.ItemId == templateId);
        if (instances > 0)
        {
            // 拦下来而不是级联删：删掉模板会让玩家背包里那 instances 件物品从此解析成"未知"。
            result.Message = $"物品 {templateId} 仍被玩家持有 {instances} 件，拒绝删除模板"
                             + "（请先改 publish_state=2 停用，不要删玩家资产）";
            return result;
        }

        using var tx = _db.Database.BeginTransaction();
        try
        {
            _db.ItemTemplates.Remove(row);
            _db.ItemStats.RemoveRange(_db.ItemStats.Where(s => s.ItemId == templateId));
            _db.ItemDamages.RemoveRange(_db.ItemDamages.Where(s => s.ItemId == templateId));
            _db.ItemSockets.RemoveRange(_db.ItemSockets.Where(s => s.ItemId == templateId));
            _db.ItemSpells.RemoveRange(_db.ItemSpells.Where(s => s.ItemId == templateId));
            _db.ItemSources.RemoveRange(_db.ItemSources.Where(s => s.ItemId == templateId));
            _db.ItemLocalizations.RemoveRange(_db.ItemLocalizations.Where(s => s.ItemId == templateId));
            _db.SaveChanges();
            tx.Commit();
            result.Ok = true;
            result.Message = $"物品 {templateId} 及六张从表已删除";
            result.WrittenRows["template"] = 1;
            return result;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            result.Message = "删除失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    // ---------------- 反查 ----------------

    /// <summary>
    /// 只读反查：这件物品被哪些掉落表 / 商店引用。表不存在时如实回 0 而不是失败
    /// （那些表属于别的编辑器，本工具有义务在它们缺失时仍能工作）。
    /// </summary>
    public List<Dictionary<string, object?>> ReferencesOf(int templateId)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            foreach (var drop in _db.MonsterDropEntries.AsNoTracking()
                         .Where(d => d.ItemId == templateId).Take(200).ToList())
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["kind"] = "drop",
                    ["refId"] = drop.BindingId,
                    ["detail"] = $"掉落表 {drop.BindingId} 第 {drop.Seq} 条，概率 {drop.ChanceBps / 100.0}%",
                });
            }
        }
        catch (Exception) { /* 怪物表未建：如实当作 0 条 */ }

        try
        {
            foreach (var vendor in _db.NpcVendorEntries.AsNoTracking()
                         .Where(v => v.ItemId == templateId).Take(200).ToList())
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["kind"] = "vendor",
                    ["refId"] = vendor.NpcId,
                    ["detail"] = $"NPC {vendor.NpcId} 商店槽位 {vendor.Slot}",
                });
            }
        }
        catch (Exception) { /* NPC 表未建：如实当作 0 条 */ }

        return rows;
    }

    // ---------------- 导出 ----------------

    /// <summary>把已发布物品装配成客户端内容表。</summary>
    public Dictionary<string, object?> BuildClientBundle(out int publishedCount, out List<string> problems)
    {
        publishedCount = 0;
        problems = new List<string>();
        var rows = new List<Dictionary<string, object?>>();
        var setIds = new HashSet<int>();
        var affixIds = new HashSet<int>();

        foreach (var template in _db.ItemTemplates.AsNoTracking().Where(t => t.PublishState == 1)
                         .OrderBy(t => t.Id).ToList())
        {
            try
            {
                var draft = GetDraft(template.Id, out var error);
                if (draft is null)
                {
                    problems.Add($"{template.Id}: 读草稿失败 {error}");
                    continue;
                }
                var spec = draft.ToSpec();
                if (!ItemCore.IsClientExportable(spec, out var exportReason))
                {
                    problems.Add($"{template.Id} ({template.ItemName}): {exportReason}");
                    continue;
                }
                rows.Add(draft.ToClientRow(spec));
                if (spec.SetId > 0) setIds.Add(spec.SetId);
                if (spec.RandomProperty > 0) affixIds.Add(spec.RandomProperty);
                if (spec.RandomSuffix > 0) affixIds.Add(spec.RandomSuffix);
                publishedCount++;
            }
            catch (Exception ex)
            {
                // 单条坏数据只记问题，不打断整批：一件脏行不该让 174 件好物品导不出去。
                problems.Add($"{template.Id}: {ex.GetBaseException().Message}");
            }
        }

        var sets = setIds.Select(id =>
        {
            var set = LoadSet(id);
            return new Dictionary<string, object?>
            {
                ["setId"] = id,
                ["name"] = set?.Name ?? string.Empty,
                ["tiers"] = set?.Tiers.Select(t => new Dictionary<string, object?>
                {
                    ["pieces"] = t.Pieces,
                    ["description"] = t.Description,
                    ["spellId"] = t.SpellId,
                    ["stats"] = t.Stats.Select(s => new Dictionary<string, object?>
                    {
                        ["type"] = s.StatType, ["value"] = s.StatValue,
                    }).ToArray(),
                }).ToArray() ?? Array.Empty<object>(),
            };
        }).ToList();

        var affixes = affixIds.Select(id =>
        {
            var affix = LoadAffix(id);
            return new Dictionary<string, object?>
            {
                ["affixId"] = id,
                ["kind"] = affix?.Kind ?? 0,
                ["name"] = affix?.Name ?? string.Empty,
                ["valueMin"] = affix?.ValueMin ?? 0,
                ["valueMax"] = affix?.ValueMax ?? 0,
            };
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["clientKeys"] = ClientKeys,
            ["itemCount"] = rows.Count,
            ["items"] = rows,
            ["setCount"] = sets.Count,
            ["sets"] = sets,
            ["affixCount"] = affixes.Count,
            ["affixes"] = affixes,
            ["problems"] = problems,
        };
    }

    /// <summary>
    /// 客户端覆盖层**实际读取**的键名。<b>这是跨进程协议</b>：改一个字母会让整条展示链静默回退硬编码，
    /// 而回退的表现是"编辑器保存成功、游戏里还是旧名字"。
    /// <para>导出文件里另有 itemLevel/damage/tooltip/sets/affixes 等字段，是给后续 UI 与工具用的；
    /// 它们**不**列在这里——声明"客户端会读"却没人读，下一次改字段名时就没人知道该改哪儿。</para>
    /// </summary>
    public static readonly string[] ClientKeys =
    {
        "items", "templateId", "name", "description", "itemClass", "quality", "element",
        "inventoryType", "stats", "sockets", "setId", "sets",
        "hpRestore", "energyRestore", "gemElement", "gemAffinity",
    };

    public void Dispose() => _db.Dispose();
}

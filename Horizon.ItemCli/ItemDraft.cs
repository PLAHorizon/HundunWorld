using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim.Items;

namespace Horizon.ItemCli;

/// <summary>
/// 物品草稿：CLI 与 Flax 插件的**跨宿主 JSON 协议**。
/// <para>
/// 插件侧有一份同名字段的字面副本（Flax 脚本域引不到仓库工程），两份键集合由
/// <c>ItemEditorContractTests</c> 按源文本逐字对账。这里改字段名 = 改协议。
/// </para>
/// <para>
/// <b>必须用属性而不是公共字段</b>：System.Text.Json 默认不绑定公开字段（IncludeFields=false），
/// 写成字段会"反序列化成功但全是初值"，而两侧同为初值时自校验反而全绿。
/// </para>
/// </summary>
public sealed class ItemDraft
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int ItemClass { get; set; }
    public int SubType { get; set; }
    public int Quality { get; set; }
    public int Rarity { get; set; }
    public int ItemLevel { get; set; }
    public int InventoryType { get; set; }
    public int Element { get; set; }
    public int BindType { get; set; }
    public int Flags { get; set; }
    public int MaterialGrade { get; set; }
    public int GatherType { get; set; }
    public int SourceType { get; set; }
    public string ProfessionLimit { get; set; } = string.Empty;

    public int LevelRequire { get; set; }
    public int RealmRequire { get; set; }
    public int RequiredSkill { get; set; }
    public int RequiredSkillRank { get; set; }
    public int AllowableClass { get; set; }
    public int AllowableRace { get; set; }

    public int MaxStack { get; set; } = 1;
    public int MaxCount { get; set; }
    public int ContainerSlots { get; set; }
    public int Durability { get; set; }

    public int Armor { get; set; }
    public int Block { get; set; }
    public int AttackDelayMs { get; set; }
    public int ResistanceElement { get; set; } = 5;
    public int ResistanceValue { get; set; }

    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
    public int MinMoneyLoot { get; set; }
    public int MaxMoneyLoot { get; set; }

    public int SocketBonus { get; set; }
    public int GemProperties { get; set; }
    public int SetId { get; set; }
    public int RandomProperty { get; set; }
    public int RandomSuffix { get; set; }
    public int DisenchantId { get; set; }
    public int BagFamily { get; set; }
    public int FoodType { get; set; }
    public int DurationSeconds { get; set; }
    public int ValidDays { get; set; }
    public int StartQuest { get; set; }
    public int PageText { get; set; }
    public int Language { get; set; }

    public bool CanTrade { get; set; } = true;
    public bool CanDestroy { get; set; } = true;
    public bool IsUnique { get; set; }

    public string IconPath { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ScriptName { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string SourceDetail { get; set; } = string.Empty;
    /// <summary>使用效果 JSON（消耗品回血/回气；形状见 ItemCore.ParseRestoreFromUseEffect）。</summary>
    public string UseEffect { get; set; } = string.Empty;
    public byte PublishState { get; set; }
    public int ContentVersion { get; set; } = 1;

    public List<ItemStatDraft> Stats { get; set; } = new();
    public List<ItemDamageDraft> Damage { get; set; } = new();
    public List<ItemSocketDraft> Sockets { get; set; } = new();
    public List<ItemSpellDraft> Spells { get; set; } = new();
    public List<ItemSourceDraft> Sources { get; set; } = new();
    public ItemSetDraft? Set { get; set; }
    public ItemAffixDraft? Affix { get; set; }
    public List<ItemLocalizationDraft> Localizations { get; set; } = new();

    /// <summary>顶层已知键。<b>与插件 <c>ItemDraftModel.KnownTopLevelKeys</c> 逐字相同</b>。</summary>
    public static readonly string[] KnownTopLevelKeys =
    {
        "templateId", "name", "description", "itemClass", "subType", "quality", "rarity", "itemLevel",
        "inventoryType", "element", "bindType", "flags",
        "materialGrade", "gatherType", "sourceType", "professionLimit",
        "levelRequire", "realmRequire", "requiredSkill", "requiredSkillRank", "allowableClass", "allowableRace",
        "maxStack", "maxCount", "containerSlots", "durability",
        "armor", "block", "attackDelayMs", "resistanceElement", "resistanceValue",
        "buyPrice", "sellPrice", "minMoneyLoot", "maxMoneyLoot",
        "socketBonus", "gemProperties", "setId", "randomProperty", "randomSuffix", "disenchantId",
        "bagFamily", "foodType", "durationSeconds", "validDays", "startQuest", "pageText", "language",
        "canTrade", "canDestroy", "isUnique",
        "iconPath", "modelPath", "scriptName", "remark", "sourceDetail", "useEffect", "publishState", "contentVersion",
        "stats", "damage", "sockets", "spells", "sources", "set", "affix", "localizations",
    };

    /// <summary>子段键集合（按段名索引）。</summary>
    public static readonly Dictionary<string, string[]> KnownSectionKeys = new()
    {
        ["stats"] = ItemStatDraft.KnownKeys,
        ["damage"] = ItemDamageDraft.KnownKeys,
        ["sockets"] = ItemSocketDraft.KnownKeys,
        ["spells"] = ItemSpellDraft.KnownKeys,
        ["sources"] = ItemSourceDraft.KnownKeys,
        ["set"] = ItemSetDraft.KnownKeys,
        ["set.tiers"] = ItemSetTierDraft.KnownKeys,
        ["set.tiers.stats"] = ItemStatDraft.KnownKeys,
        ["affix"] = ItemAffixDraft.KnownKeys,
        ["affix.stats"] = ItemStatDraft.KnownKeys,
        ["localizations"] = ItemLocalizationDraft.KnownKeys,
    };

    /// <summary>两侧共用的 JSON 约定：camelCase、不写 null、缩进。</summary>
    public static readonly JsonSerializerOptions Contract = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Contract);

    public static ItemDraft? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ItemDraft>(json, Contract);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 新建草稿：默认落在装备段（防具/身体），品质普通。
    /// <para><b>这里刻意写数字而不是枚举</b>：插件侧是同一组数字的字面副本，
    /// 契约测试按 CreateNew 里的数值逐项对账；写成枚举名会让那项对账无法按文本比较。</para>
    /// </summary>
    public static ItemDraft CreateNew(int suggestedTemplateId)
    {
        return new ItemDraft
        {
            TemplateId = suggestedTemplateId,
            Name = "新物品_" + suggestedTemplateId,
            Description = string.Empty,
            ItemClass = 1,          // 防具
            SubType = 0,
            Quality = 1,            // 普通
            Rarity = 0,
            ItemLevel = 1,
            InventoryType = 5,      // 身体（=客户端 EquipmentSlot.Body 4 + 1）
            Element = 5,            // 无
            BindType = 2,           // 装备绑定
            LevelRequire = 1,
            MaxStack = 1,
            ResistanceElement = 5,  // 无
            CanTrade = true,
            CanDestroy = true,
            PublishState = 0,
            ContentVersion = 1,
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Set = new ItemSetDraft { SetId = 0 },
            Affix = new ItemAffixDraft { AffixId = 0 },
        };
    }

    /// <summary>草稿 → 内核输入。内核只认 <see cref="ItemSpec"/>。</summary>
    public ItemSpec ToSpec(ItemSetDraft? setOverride = null, ItemAffixDraft? affixOverride = null)
    {
        var spec = new ItemSpec
        {
            TemplateId = TemplateId,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            ItemClass = ItemClass,
            SubType = SubType,
            Quality = Quality,
            Rarity = Rarity,
            ItemLevel = ItemLevel,
            InventoryType = InventoryType,
            Element = Element,
            BindType = BindType,
            Flags = Flags,
            MaterialGrade = MaterialGrade,
            GatherType = GatherType,
            SourceType = SourceType,
            ProfessionLimit = ProfessionLimit ?? string.Empty,
            LevelRequire = LevelRequire,
            RealmRequire = RealmRequire,
            RequiredSkill = RequiredSkill,
            RequiredSkillRank = RequiredSkillRank,
            AllowableClass = AllowableClass,
            AllowableRace = AllowableRace,
            MaxStack = MaxStack,
            MaxCount = MaxCount,
            ContainerSlots = ContainerSlots,
            Durability = Durability,
            Armor = Armor,
            Block = Block,
            AttackDelayMs = AttackDelayMs,
            ResistanceElement = ResistanceElement,
            ResistanceValue = ResistanceValue,
            BuyPrice = BuyPrice,
            SellPrice = SellPrice,
            MinMoneyLoot = MinMoneyLoot,
            MaxMoneyLoot = MaxMoneyLoot,
            SocketBonus = SocketBonus,
            GemProperties = GemProperties,
            SetId = SetId,
            RandomProperty = RandomProperty,
            RandomSuffix = RandomSuffix,
            DisenchantId = DisenchantId,
            BagFamily = BagFamily,
            FoodType = FoodType,
            DurationSeconds = DurationSeconds,
            ValidDays = ValidDays,
            StartQuest = StartQuest,
            PageText = PageText,
            Language = Language,
            CanTrade = CanTrade,
            CanDestroy = CanDestroy,
            IsUnique = IsUnique,
            IconPath = IconPath ?? string.Empty,
            ModelPath = ModelPath ?? string.Empty,
            ScriptName = ScriptName ?? string.Empty,
            Remark = Remark ?? string.Empty,
            SourceDetail = SourceDetail ?? string.Empty,
            UseEffect = UseEffect ?? string.Empty,
            Stats = (Stats ?? new()).Where(s => s.StatType > 0 && s.StatValue != 0)
                .Select(s => new ItemStatLine { Type = s.StatType, Value = s.StatValue }).ToArray(),
            Damage = (Damage ?? new()).Select(d => new ItemDamageLine
            {
                Type = d.DamageType, Min = d.DamageMin, Max = d.DamageMax,
            }).ToArray(),
            Sockets = (Sockets ?? new()).Select(s => new ItemSocketLine
            {
                Color = s.SocketColor, Bonus = s.SocketBonus,
            }).ToArray(),
            Spells = (Spells ?? new()).Select(s => new ItemSpellLine
            {
                SpellId = s.SpellId, Trigger = s.SpellTrigger, Charges = s.Charges,
                PpmRate = s.PpmRate, CooldownMs = s.CooldownMs, CategoryCooldownMs = s.CategoryCooldownMs,
            }).ToArray(),
            Sources = (Sources ?? new()).Select(s => new ItemSourceLine
            {
                SourceType = s.SourceType, RefType = s.RefType, RefId = s.RefId, Note = s.Note ?? string.Empty,
            }).ToArray(),
        };
        return spec;
    }

    /// <summary>内核结果 / 数据库行 → 草稿（读库与测试共用）。</summary>
    public static ItemDraft FromSpec(ItemSpec spec, byte publishState = 0, int contentVersion = 1)
    {
        var draft = new ItemDraft
        {
            TemplateId = spec.TemplateId,
            Name = spec.Name,
            Description = spec.Description,
            ItemClass = spec.ItemClass,
            SubType = spec.SubType,
            Quality = spec.Quality,
            Rarity = spec.Rarity,
            ItemLevel = spec.ItemLevel,
            InventoryType = spec.InventoryType,
            Element = spec.Element,
            BindType = spec.BindType,
            Flags = spec.Flags,
            MaterialGrade = spec.MaterialGrade,
            GatherType = spec.GatherType,
            SourceType = spec.SourceType,
            ProfessionLimit = spec.ProfessionLimit,
            LevelRequire = spec.LevelRequire,
            RealmRequire = spec.RealmRequire,
            RequiredSkill = spec.RequiredSkill,
            RequiredSkillRank = spec.RequiredSkillRank,
            AllowableClass = spec.AllowableClass,
            AllowableRace = spec.AllowableRace,
            MaxStack = spec.MaxStack,
            MaxCount = spec.MaxCount,
            ContainerSlots = spec.ContainerSlots,
            Durability = spec.Durability,
            Armor = spec.Armor,
            Block = spec.Block,
            AttackDelayMs = spec.AttackDelayMs,
            ResistanceElement = spec.ResistanceElement,
            ResistanceValue = spec.ResistanceValue,
            BuyPrice = spec.BuyPrice,
            SellPrice = spec.SellPrice,
            MinMoneyLoot = spec.MinMoneyLoot,
            MaxMoneyLoot = spec.MaxMoneyLoot,
            SocketBonus = spec.SocketBonus,
            GemProperties = spec.GemProperties,
            SetId = spec.SetId,
            RandomProperty = spec.RandomProperty,
            RandomSuffix = spec.RandomSuffix,
            DisenchantId = spec.DisenchantId,
            BagFamily = spec.BagFamily,
            FoodType = spec.FoodType,
            DurationSeconds = spec.DurationSeconds,
            ValidDays = spec.ValidDays,
            StartQuest = spec.StartQuest,
            PageText = spec.PageText,
            Language = spec.Language,
            CanTrade = spec.CanTrade,
            CanDestroy = spec.CanDestroy,
            IsUnique = spec.IsUnique,
            IconPath = spec.IconPath,
            ModelPath = spec.ModelPath,
            ScriptName = spec.ScriptName,
            Remark = spec.Remark,
            SourceDetail = spec.SourceDetail,
            UseEffect = spec.UseEffect,
            PublishState = publishState,
            ContentVersion = contentVersion,
        };
        foreach (var s in spec.Stats) draft.Stats.Add(new ItemStatDraft { StatType = s.Type, StatValue = s.Value });
        foreach (var d in spec.Damage)
            draft.Damage.Add(new ItemDamageDraft { DamageType = d.Type, DamageMin = d.Min, DamageMax = d.Max });
        foreach (var s in spec.Sockets)
            draft.Sockets.Add(new ItemSocketDraft { SocketColor = s.Color, SocketBonus = s.Bonus });
        foreach (var s in spec.Spells)
            draft.Spells.Add(new ItemSpellDraft
            {
                SpellId = s.SpellId, SpellTrigger = s.Trigger, Charges = s.Charges,
                PpmRate = s.PpmRate, CooldownMs = s.CooldownMs, CategoryCooldownMs = s.CategoryCooldownMs,
            });
        foreach (var s in spec.Sources)
            draft.Sources.Add(new ItemSourceDraft
            {
                SourceType = s.SourceType, RefType = s.RefType, RefId = s.RefId, Note = s.Note ?? string.Empty,
            });
        return draft;
    }

    /// <summary>生成客户端内容表里的一条（导出用；不含只服务于编辑器的字段）。</summary>
    public Dictionary<string, object?> ToClientRow(ItemSpec spec)
    {
        var tooltip = ItemCore.BuildTooltip(spec);
        ItemCore.ParseRestoreFromUseEffect(spec.UseEffect, out var hpRestore, out var energyRestore);
        var gemAffinity = 0;
        foreach (var stat in spec.Stats)
        {
            if (stat.Type >= (int)ItemStatType.MetalAffinity && stat.Type <= (int)ItemStatType.EarthAffinity)
            {
                gemAffinity = stat.Value;
                break;
            }
        }
        var gemElement = spec.Element is >= 0 and <= 4 ? spec.Element : -1;

        return new Dictionary<string, object?>
        {
            ["templateId"] = spec.TemplateId,
            ["name"] = spec.Name,
            ["description"] = spec.Description,
            ["itemClass"] = spec.ItemClass,
            ["subType"] = spec.SubType,
            ["quality"] = spec.Quality,
            ["qualityColor"] = ItemCore.QualityColorHex(spec.Quality),
            ["itemLevel"] = spec.ItemLevel,
            ["inventoryType"] = spec.InventoryType,
            ["element"] = spec.Element,
            ["bindType"] = spec.BindType,
            ["maxStack"] = spec.MaxStack,
            ["levelRequire"] = spec.LevelRequire,
            ["canTrade"] = spec.CanTrade,
            ["isUnique"] = spec.IsUnique,
            ["iconPath"] = spec.IconPath,
            ["modelPath"] = spec.ModelPath,
            ["sellPrice"] = spec.SellPrice,
            ["buyPrice"] = spec.BuyPrice,
            ["armor"] = spec.Armor,
            ["block"] = spec.Block,
            ["attackDelayMs"] = spec.AttackDelayMs,
            // 消耗品：客户端 ItemDefinition.HpRestore/EnergyRestore
            ["hpRestore"] = hpRestore,
            ["energyRestore"] = energyRestore,
            ["useEffect"] = spec.UseEffect,
            // 宝石：客户端按 id 段识别；专属字段是元素与亲和加成
            ["gemElement"] = gemElement,
            ["gemAffinity"] = gemAffinity,
            // 材料品阶（展示/排序用）
            ["materialTier"] = spec.MaterialGrade,
            ["stats"] = spec.Stats.Select(s => new Dictionary<string, object?>
            {
                ["type"] = s.Type, ["name"] = ItemCore.StatTypeName(s.Type), ["value"] = s.Value,
            }).ToArray(),
            ["damage"] = spec.Damage.Select(d => new Dictionary<string, object?>
            {
                ["type"] = d.Type, ["min"] = d.Min, ["max"] = d.Max,
            }).ToArray(),
            ["sockets"] = spec.Sockets.Select(s => new Dictionary<string, object?>
            {
                ["color"] = s.Color, ["colorName"] = ItemCore.SocketColorName(s.Color), ["bonus"] = s.Bonus,
            }).ToArray(),
            ["setId"] = spec.SetId,
            ["tooltip"] = tooltip.Select(l => new Dictionary<string, object?>
            {
                ["kind"] = l.Kind, ["text"] = l.Text,
            }).ToArray(),
        };
    }
}

public sealed class ItemStatDraft
{
    public int StatType { get; set; }
    public int StatValue { get; set; }

    public static readonly string[] KnownKeys = { "statType", "statValue" };
}

public sealed class ItemDamageDraft
{
    public int DamageType { get; set; }
    public int DamageMin { get; set; }
    public int DamageMax { get; set; }

    public static readonly string[] KnownKeys = { "damageType", "damageMin", "damageMax" };
}

public sealed class ItemSocketDraft
{
    public int SocketColor { get; set; } = 4;
    public int SocketBonus { get; set; }

    public static readonly string[] KnownKeys = { "socketColor", "socketBonus" };
}

public sealed class ItemSpellDraft
{
    public int SpellId { get; set; }
    public int SpellTrigger { get; set; }
    public int Charges { get; set; }
    public int PpmRate { get; set; }
    public int CooldownMs { get; set; }
    public int CategoryCooldownMs { get; set; }

    public static readonly string[] KnownKeys =
    {
        "spellId", "spellTrigger", "charges", "ppmRate", "cooldownMs", "categoryCooldownMs",
    };
}

public sealed class ItemSourceDraft
{
    public int SourceType { get; set; }
    public int RefType { get; set; }
    public int RefId { get; set; }
    public string Note { get; set; } = string.Empty;

    public static readonly string[] KnownKeys = { "sourceType", "refType", "refId", "note" };
}

public sealed class ItemSetDraft
{
    public int SetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ItemLevel { get; set; }
    public byte PublishState { get; set; }
    public List<ItemSetTierDraft> Tiers { get; set; } = new();

    public static readonly string[] KnownKeys =
    {
        "setId", "name", "description", "itemLevel", "publishState", "tiers",
    };

    /// <summary>草稿 → 内核套装（tooltip 生成用，不做任何数值换算）。</summary>
    public ItemSetSpec ToKernelSet()
    {
        return new ItemSetSpec
        {
            SetId = SetId,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            ItemLevel = ItemLevel,
            Tiers = (Tiers ?? new List<ItemSetTierDraft>()).Select(t => new ItemSetBonusTier
            {
                Pieces = t.Pieces,
                Description = t.Description ?? string.Empty,
                SpellId = t.SpellId,
                Stats = (t.Stats ?? new List<ItemStatDraft>())
                    .Select(s => new ItemStatLine { Type = s.StatType, Value = s.StatValue }).ToArray(),
            }).ToArray(),
        };
    }
}

public sealed class ItemSetTierDraft
{
    public int Pieces { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SpellId { get; set; }
    public List<ItemStatDraft> Stats { get; set; } = new();

    public static readonly string[] KnownKeys = { "pieces", "description", "spellId", "stats" };
}

public sealed class ItemAffixDraft
{
    public int AffixId { get; set; }
    public int Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ValueMin { get; set; } = 80;
    public int ValueMax { get; set; } = 120;
    public byte PublishState { get; set; }
    public List<ItemStatDraft> Stats { get; set; } = new();

    public static readonly string[] KnownKeys =
    {
        "affixId", "kind", "name", "description", "valueMin", "valueMax", "publishState", "stats",
    };
}

public sealed class ItemLocalizationDraft
{
    public string Locale { get; set; } = "zhCN";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public static readonly string[] KnownKeys = { "locale", "name", "description" };
}

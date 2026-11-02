using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItemEditor
{
    /// <summary>
    /// 物品草稿的插件侧模型（与 <c>Horizon.ItemCli.ItemDraft</c> 是同一份 JSON 协议的两个宿主）。
    /// <para>
    /// <b>为什么这里要另存一份而不是引用 CLI 的程序集</b>：Flax 脚本域引不到仓库里的 .NET 工程
    /// （引擎的编译图与解决方案是两套），所以字段名协议在两侧各有一份字面副本。
    /// 这不是可以省掉的重复——<c>ItemEditorContractTests</c> 会按**源文本**把两侧的键集合逐字比对，
    /// 漂一个字母就当场红。
    /// </para>
    /// <para>
    /// <b>必须用属性而不是公共字段</b>：<c>System.Text.Json</c> 默认不绑定公开字段
    /// （<c>IncludeFields=false</c>）。写成字段的话反序列化"成功"但全是初值，
    /// 而两侧同为初值时自校验反而全绿——这是最难查的那种静默失效。
    /// </para>
    /// </summary>
    public sealed class ItemDraftModel
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
        /// <summary>使用效果 JSON（消耗品回血/回气，如 <c>{"hp":300}</c>）。</summary>
        public string UseEffect { get; set; } = string.Empty;
        public byte PublishState { get; set; }
        public int ContentVersion { get; set; } = 1;

        public List<ItemStatModel> Stats { get; set; } = new List<ItemStatModel>();
        public List<ItemDamageModel> Damage { get; set; } = new List<ItemDamageModel>();
        public List<ItemSocketModel> Sockets { get; set; } = new List<ItemSocketModel>();
        public List<ItemSpellModel> Spells { get; set; } = new List<ItemSpellModel>();
        public List<ItemSourceModel> Sources { get; set; } = new List<ItemSourceModel>();
        public ItemSetModel Set { get; set; } = new ItemSetModel();
        public ItemAffixModel Affix { get; set; } = new ItemAffixModel();
        public List<ItemLocalizationModel> Localizations { get; set; } = new List<ItemLocalizationModel>();

        /// <summary>顶层已知键。<b>与 CLI 的 KnownTopLevelKeys 逐字相同</b>（契约测试按文本对账）。</summary>
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
        public static readonly Dictionary<string, string[]> KnownSectionKeys = new Dictionary<string, string[]>
        {
            ["stats"] = ItemStatModel.KnownKeys,
            ["damage"] = ItemDamageModel.KnownKeys,
            ["sockets"] = ItemSocketModel.KnownKeys,
            ["spells"] = ItemSpellModel.KnownKeys,
            ["sources"] = ItemSourceModel.KnownKeys,
            ["set"] = ItemSetModel.KnownKeys,
            ["set.tiers"] = ItemSetTierModel.KnownKeys,
            ["set.tiers.stats"] = ItemStatModel.KnownKeys,
            ["affix"] = ItemAffixModel.KnownKeys,
            ["affix.stats"] = ItemStatModel.KnownKeys,
            ["localizations"] = ItemLocalizationModel.KnownKeys,
        };

        /// <summary>
        /// 两侧共用的 JSON 约定：camelCase、不写 null、缩进。
        /// <para>刻意<b>不设 Encoder</b>：Flax 脚本域里没有 <c>System.Text.Encodings.Web</c>
        /// （离线 csc 闸门会当场报 CS0012）。</para>
        /// </summary>
        public static readonly JsonSerializerOptions Contract = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Contract);

        public static ItemDraftModel FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<ItemDraftModel>(json, Contract);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>新建草稿：各段填本工程口径的默认值（与 CLI <c>ItemDraft.CreateNew</c> 逐条相同）。</summary>
        public static ItemDraftModel CreateNew(int suggestedTemplateId)
        {
            return new ItemDraftModel
            {
                TemplateId = suggestedTemplateId,
                Name = "新物品_" + suggestedTemplateId,
                ItemClass = 1,
                SubType = 0,
                Quality = 1,
                Rarity = 0,
                ItemLevel = 1,
                InventoryType = 5,
                Element = 5,
                BindType = 2,
                LevelRequire = 1,
                MaxStack = 1,
                ResistanceElement = 5,
                CanTrade = true,
                CanDestroy = true,
                PublishState = 0,
                ContentVersion = 1,
                IconPath = "Content/Icons/Equipment/DefaultBody.flax",
                Set = new ItemSetModel { SetId = 0 },
                Affix = new ItemAffixModel { AffixId = 0 },
            };
        }
    }

    public sealed class ItemStatModel
    {
        public int StatType { get; set; }
        public int StatValue { get; set; }

        public static readonly string[] KnownKeys = { "statType", "statValue" };
    }

    public sealed class ItemDamageModel
    {
        public int DamageType { get; set; }
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }

        public static readonly string[] KnownKeys = { "damageType", "damageMin", "damageMax" };
    }

    public sealed class ItemSocketModel
    {
        public int SocketColor { get; set; } = 4;
        public int SocketBonus { get; set; }

        public static readonly string[] KnownKeys = { "socketColor", "socketBonus" };
    }

    public sealed class ItemSpellModel
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

    public sealed class ItemSourceModel
    {
        public int SourceType { get; set; }
        public int RefType { get; set; }
        public int RefId { get; set; }
        public string Note { get; set; } = string.Empty;

        public static readonly string[] KnownKeys = { "sourceType", "refType", "refId", "note" };
    }

    public sealed class ItemSetModel
    {
        public int SetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int ItemLevel { get; set; }
        public byte PublishState { get; set; }
        public List<ItemSetTierModel> Tiers { get; set; } = new List<ItemSetTierModel>();

        public static readonly string[] KnownKeys =
        {
            "setId", "name", "description", "itemLevel", "publishState", "tiers",
        };
    }

    public sealed class ItemSetTierModel
    {
        public int Pieces { get; set; }
        public string Description { get; set; } = string.Empty;
        public int SpellId { get; set; }
        public List<ItemStatModel> Stats { get; set; } = new List<ItemStatModel>();

        public static readonly string[] KnownKeys = { "pieces", "description", "spellId", "stats" };
    }

    public sealed class ItemAffixModel
    {
        public int AffixId { get; set; }
        public int Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int ValueMin { get; set; } = 80;
        public int ValueMax { get; set; } = 120;
        public byte PublishState { get; set; }
        public List<ItemStatModel> Stats { get; set; } = new List<ItemStatModel>();

        public static readonly string[] KnownKeys =
        {
            "affixId", "kind", "name", "description", "valueMin", "valueMax", "publishState", "stats",
        };
    }

    public sealed class ItemLocalizationModel
    {
        public string Locale { get; set; } = "zhCN";
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public static readonly string[] KnownKeys = { "locale", "name", "description" };
    }
}

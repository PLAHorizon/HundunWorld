using System;
using System.Collections.Generic;

namespace NarrativePro.Items
{
    /// <summary>
    /// 表示向背包添加物品的结果。
    /// 适配 UE5 FItemAddResult。
    /// </summary>
    public class ItemAddResult
    {
        /// <summary>尝试添加的物品类 ID</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>创建的新堆叠</summary>
        public List<NarrativeItem> Stacks { get; set; } = new List<NarrativeItem>();

        /// <summary>尝试添加的数量</summary>
        public int AmountToGive { get; set; } = 0;

        /// <summary>实际添加的数量（可能因容量/重量限制少于请求数量）</summary>
        public int AmountGiven { get; set; } = 0;

        /// <summary>失败时的原因文本</summary>
        public string ErrorText { get; set; } = "";

        public bool AddedAllItems => AmountGiven == AmountToGive && AmountToGive > 0;
        public bool AddedSomeItems => AmountGiven > 0 && AmountGiven < AmountToGive;
        public bool AddedNoItems => AmountGiven == 0;

        public static ItemAddResult AddedNone(int itemQuantity, string errorText)
        {
            return new ItemAddResult { AmountToGive = itemQuantity, AmountGiven = 0, ErrorText = errorText };
        }

        public static ItemAddResult AddedSome(List<NarrativeItem> items, int itemQuantity, int actualAmountGiven, string errorText)
        {
            return new ItemAddResult
            {
                Stacks = items,
                AmountToGive = itemQuantity,
                AmountGiven = actualAmountGiven,
                ErrorText = errorText
            };
        }

        public static ItemAddResult AddedAll(List<NarrativeItem> items, int itemQuantity)
        {
            return new ItemAddResult
            {
                Stacks = items,
                AmountToGive = itemQuantity,
                AmountGiven = itemQuantity
            };
        }
    }

    /// <summary>物品与数量的组合。</summary>
    public class ItemWithQuantity
    {
        public string ItemClassId { get; set; } = "";
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// 物品集合，用于分组（如套装、武器套装）。适配 UE5 UItemCollection
    /// （<c>UItemCollection : UDataAsset</c>，成员 <c>Items: TArray&lt;FItemWithQuantity&gt;</c>，
    /// 由 <c>TryAddFromLootTable</c> 在 <c>Item.Quantity &gt; 0</c> 时逐个授予）。
    /// </summary>
    public class ItemCollection
    {
        public string CollectionId { get; set; } = "";
        public List<ItemWithQuantity> Items { get; set; } = new List<ItemWithQuantity>();

        /// <summary>集合 JSON 的默认内容目录（对应 UE5 的 UItemCollection 资产）。</summary>
        public const string DefaultDirectory = "Content/NarrativePro/Collections";

        // 必须大小写不敏感：作者侧 JSON 用 camelCase，本类成员是 PascalCase 属性，
        // 而 System.Text.Json 默认大小写敏感——"文件读到了、对象也构造出来了、Items 是空表"，
        // 发放时静默什么都不给，且没有任何报错。
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions =
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>从一份集合 JSON 读出定义（相对路径按工程根归一：编辑器/PIE/打包的工作目录不一致）。</summary>
        public static ItemCollection LoadFromFile(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;
            try
            {
                string resolved = NarrativePro.Data.NarrativeDataPaths.Resolve(jsonFilePath);
                if (!System.IO.File.Exists(resolved)) return null;
                var dto = System.Text.Json.JsonSerializer.Deserialize<ItemCollection>(
                    System.IO.File.ReadAllText(resolved), _jsonOptions);
                if (dto != null && string.IsNullOrEmpty(dto.CollectionId))
                {
                    dto.CollectionId = System.IO.Path.GetFileNameWithoutExtension(resolved);
                }
                return dto;
            }
            catch (Exception ex)
            {
                NarrativePro.Core.NarrativeLog.LogWarning(
                    $"[ItemCollection] 解析 '{jsonFilePath}' 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>按集合 ID 从默认目录加载（UE5 里等价于直接引用那份 DataAsset）。</summary>
        public static ItemCollection LoadById(string collectionId)
        {
            if (string.IsNullOrEmpty(collectionId)) return null;
            return LoadFromFile(System.IO.Path.Combine(DefaultDirectory, collectionId + ".json"));
        }
    }

    /// <summary>
    /// 战利品表数据（行的集合）。对应 UE5 的 <c>UDataTable</c>（<c>LootTable.TableToRoll</c>）。
    /// <para>
    /// UE5 用 DataTable 的 FName 行名做随机选取；Flax 无 DataTable，这里用带 <see cref="RowName"/>
    /// 的行列表 + 按名查找来等价 <c>GetRowNames()</c> / <c>FindRow&lt;FLootTableRow&gt;()</c>。
    /// </para>
    /// </summary>
    public class LootTableData
    {
        /// <summary>表 ID（用于日志与子表递归守卫的身份）。</summary>
        public string TableId { get; set; } = "";

        /// <summary>表中的全部行。</summary>
        public List<LootTableRow> Rows { get; set; } = new List<LootTableRow>();

        /// <summary>行名列表。对应 UE5 <c>GetRowNames()</c>。</summary>
        public List<string> GetRowNames()
        {
            var names = new List<string>();
            if (Rows == null) return names;
            foreach (var row in Rows)
            {
                if (row != null && !string.IsNullOrEmpty(row.RowName)) names.Add(row.RowName);
            }
            return names;
        }

        /// <summary>按行名查找行。对应 UE5 <c>FindRow&lt;FLootTableRow&gt;(Name, ContextString)</c>。未找到返回 null。</summary>
        public LootTableRow FindRow(string rowName)
        {
            if (string.IsNullOrEmpty(rowName) || Rows == null) return null;
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                if (row != null && row.RowName == rowName) return row;
            }
            return null;
        }

        /// <summary>行数。对应 UE5 表行数（&lt;=0 表示不可掷）。</summary>
        public int RowCount => Rows?.Count ?? 0;

        /// <summary>
        /// 从 JSON 定义加载一张战利品表（对应 UE5 直接引用 DataTable 资产）。
        /// 找不到文件或解析失败返回 null 并写警告，调用方按"此表不掷"处理。
        /// </summary>
        public static LootTableData LoadFromJson(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;
            try
            {
                if (!System.IO.File.Exists(jsonFilePath)) return null;
                string json = System.IO.File.ReadAllText(jsonFilePath);
                return LoadFromJsonString(json);
            }
            catch (System.Exception ex)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[Loot] 加载战利品表 '{jsonFilePath}' 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>从 JSON 字符串加载战利品表。</summary>
        public static LootTableData LoadFromJsonString(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var table = System.Text.Json.JsonSerializer.Deserialize<LootTableData>(json, LootJsonOptions);
                if (table != null && table.Rows != null)
                {
                    // 行没写 RowName 时按序号补一个，保证随机取行的键非空
                    for (int i = 0; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        if (row == null) continue;
                        if (string.IsNullOrEmpty(row.RowName)) row.RowName = "Row_" + i;
                    }
                }
                return table;
            }
            catch (System.Exception ex)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[Loot] 解析战利品表 JSON 失败: {ex.Message}");
                return null;
            }
        }

        private static readonly System.Text.Json.JsonSerializerOptions LootJsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    /// <summary>
    /// 战利品表的一次掷骰。
    /// 对应 UE5 <c>FLootTableRoll</c>：必定授予的物品 + 集合 + 行随机掷取（含子表递归）。
    /// </summary>
    public class LootTableRoll
    {
        /// <summary>必定授予的物品</summary>
        public List<ItemWithQuantity> ItemsToGrant { get; set; } = new List<ItemWithQuantity>();

        /// <summary>必定授予的物品集合 ID</summary>
        public List<string> ItemCollectionsToGrant { get; set; } = new List<string>();

        /// <summary>要滚动的子表（嵌套）</summary>
        public List<LootTableRoll> SubTablesToRoll { get; set; } = new List<LootTableRoll>();

        /// <summary>滚动次数</summary>
        public int NumRolls { get; set; } = 1;

        /// <summary>每次滚动成功概率（0~1）</summary>
        public float Chance { get; set; } = 1f;

        /// <summary>
        /// 要掷取行的表。对应 UE5 <c>UDataTable* TableToRoll</c>。
        /// 为空表示本 roll 只做必定授予，不掷行。
        /// </summary>
        public LootTableData TableToRoll { get; set; }

        /// <summary>本 roll 内子表的最大递归深度（防御 A→B→A 这类环造成栈溢出）。0 表示不额外限制。</summary>
        public int MaxRecursionDepth { get; set; } = 8;

        public bool CanRoll() => NumRolls > 0 && Chance > 0f;
    }

    /// <summary>
    /// 战利品表的一行。对应 UE5 <c>FLootTableRow</c>（FTableRowBase 派生）。
    /// </summary>
    public class LootTableRow
    {
        /// <summary>行名。对应 UE5 DataTable 的 FName 行键，随机掷行用的就是它。</summary>
        public string RowName { get; set; } = "";

        public List<ItemWithQuantity> ItemsToGrant { get; set; } = new List<ItemWithQuantity>();
        public List<string> ItemCollectionsToGrant { get; set; } = new List<string>();
        public List<LootTableRoll> SubTablesToRoll { get; set; } = new List<LootTableRoll>();

        /// <summary>本行被命中的概率（0~1）。对应 UE5 <c>FLootTableRow::Chance</c>。</summary>
        public float Chance { get; set; } = 1f;
    }

    /// <summary>用于存档的物品数据。</summary>
    public class SavedItem
    {
        public string ItemClassId { get; set; } = "";
        public Guid ItemGUID { get; set; }
        public int Quantity { get; set; } = 0;
        public bool bActive { get; set; } = false;
        public bool bFavourite { get; set; } = false;
        /// <summary>物品自定义存档数据（JSON 字符串）</summary>
        public string CustomData { get; set; } = "";

        /// <summary>
        /// 片段存档。对应 UE5 <c>UNarrativeItem::SavedFragments</c>
        /// （<c>TArray&lt;FSavedItemFragment&gt;</c>，NarrativeItem.h:501）。
        /// </summary>
        public List<SavedItemFragment> Fragments { get; set; } = new List<SavedItemFragment>();
    }

    /// <summary>
    /// 已保存的物品片段：类型键 + 序列化状态。对应 UE5 <c>FSavedItemFragment</c>
    /// （NarrativeItem.h:48，<c>TSubclassOf&lt;UNarrativeItemFragment&gt; + TArray&lt;uint8&gt; Data</c>）。
    /// UE5 用 UPROPERTY 反射产出字节流，端口用片段类型名 + JSON 串达到同一效果
    /// （Flax 存档走 JSON 资产/字符串，二进制字节流没有对应物）。
    /// </summary>
    public class SavedItemFragment
    {
        /// <summary>片段类型键（= CLR 类型名 = ItemFactory 注册键）。对应 UE5 <c>Fragment</c> 子类引用。</summary>
        public string FragmentTypeId { get; set; } = "";

        /// <summary>片段实例状态 JSON。对应 UE5 <c>Data</c> 字节流。</summary>
        public string Data { get; set; } = "";
    }
}

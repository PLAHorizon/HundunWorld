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
    /// 物品集合，用于分组（如套装、武器套装）。适配 UE5 UItemCollection。
    /// </summary>
    public class ItemCollection
    {
        public string CollectionId { get; set; } = "";
        public List<ItemWithQuantity> Items { get; set; } = new List<ItemWithQuantity>();
    }

    /// <summary>战利品表的一次掷骰。</summary>
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

        public bool CanRoll() => NumRolls > 0 && Chance > 0f;
    }

    /// <summary>战利品表的一行。</summary>
    public class LootTableRow
    {
        public List<ItemWithQuantity> ItemsToGrant { get; set; } = new List<ItemWithQuantity>();
        public List<string> ItemCollectionsToGrant { get; set; } = new List<string>();
        public List<LootTableRoll> SubTablesToRoll { get; set; } = new List<LootTableRoll>();
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
    }
}

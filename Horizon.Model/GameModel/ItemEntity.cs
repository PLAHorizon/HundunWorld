using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 物品实体
    /// </summary>
    [Table("Game_HunduShijie_Item"), TableDescription(Name = "Game_HunduShijie_Item", Order = "HunduShijie_005", Description = "物品信息")]
    [Comment("物品信息表")]
    [EntityStorage("Game")]
    public class ItemEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 物品唯一ID
        /// </summary>
        [Key]
        [Column("item_uid", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "item_uid", Order = "1", Description = "物品唯一ID")]
        [Comment("物品唯一ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 物品模板ID
        /// </summary>
        [Column("item_id", TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "item_id", Order = "2", Description = "物品模板ID")]
        [Comment("物品模板ID")]
        public int ItemId { get; set; }
        
        /// <summary>
        /// 拥有者ID
        /// </summary>
        [Column("owner_id", TypeName = "bigint", Order = 3), TableDescription(TypeName = "bigint", Name = "owner_id", Order = "3", Description = "拥有者ID")]
        [Comment("拥有者ID（角色ID）")]
        public long OwnerId { get; set; }
        
        /// <summary>
        /// 物品类型
        /// </summary>
        [Column("item_type", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "item_type", Order = "4", Description = "物品类型")]
        [Comment("物品类型 0-武器 1-防具 2-饰品 3-消耗品 4-材料")]
        public int ItemType { get; set; }
        
        /// <summary>
        /// 物品品质
        /// </summary>
        [Column("quality", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "quality", Order = "5", Description = "物品品质")]
        [Comment("物品品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神器")]
        public int Quality { get; set; }
        
        /// <summary>
        /// 数量
        /// </summary>
        [Column("quantity", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "quantity", Order = "6", Description = "数量")]
        [Comment("数量")]
        public int Quantity { get; set; }
        
        /// <summary>
        /// 强化等级
        /// </summary>
        [Column("enhance_level", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "enhance_level", Order = "7", Description = "强化等级")]
        [Comment("强化等级")]
        public int EnhanceLevel { get; set; }
        
        /// <summary>
        /// 宝石槽数量
        /// </summary>
        [Column("gem_slots", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "gem_slots", Order = "8", Description = "宝石槽数量")]
        [Comment("宝石槽数量 0-5")]
        public int GemSlots { get; set; }
        
        /// <summary>
        /// 五行属性
        /// </summary>
        [Column("element", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "element", Order = "9", Description = "五行属性")]
        [Comment("五行属性 0-金 1-木 2-水 3-火 4-土")]
        public int? Element { get; set; }
        
        /// <summary>
        /// 套装ID
        /// </summary>
        [Column("set_id", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "set_id", Order = "10", Description = "套装ID")]
        [Comment("套装ID")]
        public int? SetId { get; set; }
        
        /// <summary>
        /// 绑定类型
        /// </summary>
        [Column("bind_type", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "bind_type", Order = "11", Description = "绑定类型")]
        [Comment("绑定类型 0-不绑定 1-拾取绑定 2-装备绑定 3-使用绑定")]
        public int BindType { get; set; }
        
        /// <summary>
        /// 是否已绑定
        /// </summary>
        [Column("is_bound", TypeName = "bit", Order = 12), TableDescription(TypeName = "bit", Name = "is_bound", Order = "12", Description = "是否已绑定")]
        [Comment("是否已绑定")]
        public bool IsBound { get; set; }
        
        /// <summary>
        /// 是否已装备
        /// </summary>
        [Column("is_equipped", TypeName = "bit", Order = 13), TableDescription(TypeName = "bit", Name = "is_equipped", Order = "13", Description = "是否已装备")]
        [Comment("是否已装备")]
        public bool IsEquipped { get; set; }
        
        /// <summary>
        /// 装备位置
        /// </summary>
        [Column("equip_slot", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "equip_slot", Order = "14", Description = "装备位置")]
        [Comment("装备位置")]
        public int? EquipSlot { get; set; }
        
        /// <summary>
        /// 位置类型
        /// </summary>
        [Column("location_type", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "location_type", Order = "15", Description = "位置类型")]
        [Comment("位置类型 0-背包 1-仓库 2-邮件 3-交易")]
        public int LocationType { get; set; }
        
        /// <summary>
        /// 背包位置
        /// </summary>
        [Column("bag_slot", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "bag_slot", Order = "16", Description = "背包位置")]
        [Comment("背包位置")]
        public int? BagSlot { get; set; }
        
        /// <summary>
        /// 耐久度
        /// </summary>
        [Column("durability", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "durability", Order = "17", Description = "耐久度")]
        [Comment("耐久度")]
        public int? Durability { get; set; }
        
        /// <summary>
        /// 最大耐久度
        /// </summary>
        [Column("max_durability", TypeName = "int", Order = 18), TableDescription(TypeName = "int", Name = "max_durability", Order = "18", Description = "最大耐久度")]
        [Comment("最大耐久度")]
        public int? MaxDurability { get; set; }
        
        /// <summary>
        /// 附魔ID
        /// </summary>
        [Column("enchant_id", TypeName = "int", Order = 19), TableDescription(TypeName = "int", Name = "enchant_id", Order = "19", Description = "附魔ID")]
        [Comment("附魔ID")]
        public int? EnchantId { get; set; }
        
        /// <summary>
        /// 附魔等级
        /// </summary>
        [Column("enchant_level", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "enchant_level", Order = "20", Description = "附魔等级")]
        [Comment("附魔等级")]
        public int? EnchantLevel { get; set; }
        
        /// <summary>
        /// 过期时间
        /// </summary>
        [Column("expire_time", TypeName = "datetime", Order = 21), TableDescription(TypeName = "datetime", Name = "expire_time", Order = "21", Description = "过期时间")]
        [Comment("过期时间")]
        public DateTime? ExpireTime { get; set; }
        
        /// <summary>
        /// 获得时间
        /// </summary>
        [Column("acquire_time", TypeName = "datetime", Order = 22), TableDescription(TypeName = "datetime", Name = "acquire_time", Order = "22", Description = "获得时间")]
        [Comment("获得时间")]
        public DateTime AcquireTime { get; set; }
        
        /// <summary>
        /// 是否锁定
        /// </summary>
        [Column("is_locked", TypeName = "bit", Order = 23), TableDescription(TypeName = "bit", Name = "is_locked", Order = "23", Description = "是否锁定")]
        [Comment("是否锁定")]
        public bool IsLocked { get; set; }
        
        /// <summary>
        /// 合成材料来源
        /// </summary>
        [Column("synthesis_materials", TypeName = "nvarchar(500)", Order = 24), TableDescription(TypeName = "nvarchar(500)", Name = "synthesis_materials", Order = "24", Description = "合成材料来源")]
        [Comment("合成材料来源（JSON）")]
        public string SynthesisMaterials { get; set; }
    }
}

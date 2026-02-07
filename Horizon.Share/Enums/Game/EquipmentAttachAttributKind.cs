using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Enums.Game
{
    /// <summary>
    /// 装备/物品 类型
    /// </summary>

    public enum EquipmentType
    {

        /// <summary>
        /// 头部
        /// </summary>
        [Description("头部")] Head = 0,
        /// <summary>
        /// 披风
        /// </summary>
        [Description("披风")] Face = 1,
        /// <summary>
        /// 项链
        /// </summary>
        [Description("项链")] Necklace = 2,

        /// <summary>
        /// 衣服
        /// </summary>
        [Description("衣服")] Clothes = 3,

        /// <summary>
        /// 腰带
        /// </summary>
        [Description("腰带")] Belt = 4,
        /// <summary>
        /// 裤子
        /// </summary>
        [Description("裤子")] Trousers = 5,

        /// <summary>
        /// 鞋子
        /// </summary>
        [Description("鞋子")] Shoes = 6,

        /// <summary>
        /// 手
        /// </summary>
        [Description("手")] Hand = 7,

        /// <summary>
        /// 左戒指
        /// </summary>
        [Description("左戒指")] LeftRing = 9,
        /// <summary>
        /// 右戒指
        /// </summary>
        [Description("右戒指")] RightRing = 10,

        /// <summary>
        /// 腰坠
        /// </summary>
        [Description("腰坠")] BeltOrnament = 11,

        /// <summary>
        /// 武器
        /// </summary>
        [Description("武器")] Weapon = 12,

        /// <summary>
        /// 副手武器
        /// </summary>
        [Description("副手武器")] SecondaryWeapon = 13,
        /// <summary>
        /// 其它装备
        /// </summary>
        [Description("其它装备")] Other = 14,

        /// <summary>
        /// 可镶嵌物品
        /// </summary>
        [Description("可镶嵌物品")] Inlaid = 15,

        /// <summary>
        /// 魔法药水
        /// </summary>
        [Description("魔法药水")] MagicPointConsume = 100,
        /// <summary>
        /// 生命药水
        /// </summary>
        [Description("生命药水")] HealthPointConsume = 101,
    }
    /// <summary>
    /// 装备附加属性类型
    /// </summary>
    public enum EquipmentAttachAttributKind
    {
        /// <summary>
        /// 生命
        /// </summary>
        [Description("生命")]
        Health = 0,
        /// <summary>
        /// 内力
        /// </summary>
        [Description("内力")]
        InternalForce = 1,
        /// <summary>
        /// 根骨
        /// </summary>
        [Description("根骨")]
        RootBone = 2,
        /// <summary>
        /// 智慧
        /// </summary>
        [Description("智慧")]
        Wisdom = 3,
        /// <summary>
        /// 敏捷
        /// </summary>
        [Description("敏捷")]
        Agile = 4,
        /// <summary>
        /// 耐力
        /// </summary>
        [Description("耐力")]
        Endurance = 5,
        /// <summary>
        /// 速度
        /// </summary>
        [Description("速度")]
        Speed = 6,
        /// <summary>
        /// 爆发力
        /// </summary>
        [Description("爆发力")]
        ExplosivePower = 7
    }


    /// <summary>
    /// 装备插槽类型
    /// </summary>
    public enum EquipmentAttachSlotKind
    { /// <summary>
      /// 金
      /// </summary>
        [Description("金")]
        Gold = 0,
        /// <summary>
        /// 木
        /// </summary>
        [Description("木")]
        Wood = 1,
        /// <summary>
        /// 水
        /// </summary>
        [Description("水")]
        Water = 2,
        /// <summary>
        /// 火
        /// </summary>
        [Description("火")]
        Fire = 3,
        /// <summary>
        /// 土
        /// </summary>
        [Description("土")]
        Soil = 4
    }
    /// <summary>
    /// 装备品质
    /// </summary>
    public enum EquipmentQuality
    {
        /// <summary>
        /// 白
        /// </summary>
        [Description("白")]
        White = 0,
        /// <summary>
        /// 绿
        /// </summary>
        [Description("绿")]
        Green = 1,
        /// <summary>
        /// 蓝
        /// </summary>
        [Description("蓝")]
        Blue = 2,
        /// <summary>
        /// 紫
        /// </summary>
        [Description("紫")]
        Purple = 3,
        /// <summary>
        /// 红
        /// </summary>
        [Description("红")]
        Red = 4,
        /// <summary>
        /// 橙
        /// </summary>
        [Description("橙")]
        Orange = 5,
        /// <summary>
        /// 褐
        /// </summary>
        [Description("褐")]
        Brown = 6
    }
}

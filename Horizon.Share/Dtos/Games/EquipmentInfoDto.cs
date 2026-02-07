
using Horizon.Share.Enums.Game;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 装备信息
    /// </summary>
    public class EquipmentInfoDto
    {
        /// <summary>
        /// 装备Id
        /// </summary>
        public long Id { get; set; }
        /// <summary>
        /// 装备名称
        /// </summary>

        public string Name { get; set; }
        /// <summary>
        /// 装备描述
        /// </summary>


        public string Description { get; set; }
        /// <summary>
        /// 装备类型
        /// </summary>


        public EquipmentType EquipmentType { get; set; }
        /// <summary>
        /// 装备图标编号
        /// </summary>


        public long IconId { get; set; }
        /// <summary>
        /// 图标前缀,文件系统的存储也依循此前缀作为文件夹来组织文件
        /// </summary>

        public string PrefixName { get; set; }
        /// <summary>
        /// 是否是自制
        /// </summary>
        public bool IsCustomize { get; set; }
        /// <summary>
        /// 自制信息
        /// </summary>
        public string CustomizeInfo { get; set; }
        /// <summary>
        /// 耐久度
        /// </summary>
        public ushort Durability { get; set; }
        /// <summary>
        /// 最大耐久度
        /// </summary>
        public ushort MaxDurability { get; set; }
        /// <summary>
        /// 最小耐久度
        /// </summary>
        public ushort MinDurability { get; set; }
        /// <summary>
        /// 重量
        /// </summary>
        public decimal Weight { get; set; }
        /// <summary>
        /// 装备品质
        /// </summary>
        public EquipmentQuality Quality { get; set; }
        /// <summary>
        /// 装备等级
        /// </summary>
        public ushort Level { get; set; }
        /// <summary>
        /// 附加属性
        /// </summary>
        public EquipmentAttachDto[] Attachs { get; set; }
        /// <summary>
        /// 插槽属性
        /// </summary>
        public EquipmnetSlotDto[] EquipmnetSlots { get; set; }


    }

    /// <summary>
    /// 装备附加属性
    /// </summary>
    public class EquipmentAttachDto
    {
        /// <summary>
        /// 附加属性类型
        /// </summary>
        public EquipmentAttachAttributKind AttachAttributKind { get; set; }

        /// <summary>
        /// 附加值
        /// </summary>

        public decimal AttachValue { get; set; }

        /// <summary>
        /// 附加值说明
        /// </summary>

        public string AttachRemark { get; set; }

        public short Time { get; set; }


    }
    /// <summary>
    /// 装备插槽属性
    /// </summary>
    public class EquipmnetSlotDto
    {
        public long EquipmentSlotId { get; set; }
        /// <summary>
        /// 插槽类型
        /// </summary>
        public EquipmentAttachSlotKind Kind { get; set; }
        /// <summary>
        /// 插槽物品的附加属性
        /// </summary>
        public EquipmentAttachDto[] Attachs { get; set; }


    }
}

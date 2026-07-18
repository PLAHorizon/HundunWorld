using FlaxEngine;
using Game.Character.Attributes;
using System.Collections.Generic;

namespace HundunWorld.Game.Equipment
{
    /// <summary>
    /// 装备类型
    /// </summary>
    public enum EquipmentType
    {
        /// <summary>身体装备</summary>
        Body,

        /// <summary>配饰</summary>
        Accessory,

        /// <summary>武器</summary>
        Weapon,

        /// <summary>背包，用于扩展背包格子数</summary>
        Bag
    }

    /// <summary>
    /// 装备槽位
    /// </summary>
    public enum EquipmentSlot
    {
        /// <summary>头部</summary>
        Head,

        /// <summary>颈部</summary>
        Neck,

        /// <summary>肩部</summary>
        Shoulder,

        /// <summary>背部</summary>
        Back,

        /// <summary>身体</summary>
        Body,

        /// <summary>腰部</summary>
        Waist,

        /// <summary>腿部</summary>
        Legs,

        /// <summary>脚部</summary>
        Feet,

        /// <summary>右手</summary>
        RightHand,

        /// <summary>左手</summary>
        LeftHand,

        /// <summary>右戒指</summary>
        RightRing,

        /// <summary>左戒指</summary>
        LeftRing,

        /// <summary>右手腕</summary>
        RightWrist,

        /// <summary>左手腕</summary>
        LeftWrist,

        /// <summary>面部</summary>
        Face
    }

    /// <summary>
    /// 装备/配饰/武器数据模型
    /// </summary>
    public class EquipmentData
    {
        /// <summary>装备ID</summary>
        public int Id;

        /// <summary>装备名称</summary>
        public string Name;

        /// <summary>图标路径</summary>
        public string IconPath;

        /// <summary>装备类型</summary>
        public EquipmentType Type;

        /// <summary>装备槽位</summary>
        public EquipmentSlot Slot;

        /// <summary>身体装备模型（身体装备用）</summary>
        public SkinnedModel BodyModel;

        /// <summary>覆盖材质（仅切换材质时用）</summary>
        public MaterialBase OverrideMaterial;

        /// <summary>静态网格模型资产（配饰/武器用）</summary>
        public Model StaticMesh;

        /// <summary>物品预制体（配饰/武器用，优先于 StaticMesh）</summary>
        public Prefab ItemPrefab;

        /// <summary>挂载骨骼名，如 "RightHand"、"Head"</summary>
        public string AttachmentBoneName;

        /// <summary>挂载位置偏移</summary>
        public Vector3 AttachmentOffset = Vector3.Zero;

        /// <summary>挂载旋转偏移</summary>
        public Quaternion AttachmentRotation = Quaternion.Identity;

        /// <summary>挂载缩放</summary>
        public Vector3 AttachmentScale = Vector3.One;

        /// <summary>装备描述</summary>
        public string Description;

        /// <summary>基础属性加成，如 {"Attack": 10, "Defense": 5}</summary>
        public Dictionary<string, float> BaseStats = new Dictionary<string, float>();

        /// <summary>五行属性加成</summary>
        public Dictionary<WuxingElement, int> WuxingBonus = new Dictionary<WuxingElement, int>();

        /// <summary>需求等级</summary>
        public int RequiredLevel;

        /// <summary>装备品质，0=白,1=绿,2=蓝,3=紫,4=橙,5=红</summary>
        public int Quality;

        /// <summary>物品等级（装等）</summary>
        public int ItemLevel;

        /// <summary>背包扩展格子数（仅对 EquipmentType.Bag 有效，其他类型为 0）</summary>
        public int ExtraSlots = 0;
    }
}

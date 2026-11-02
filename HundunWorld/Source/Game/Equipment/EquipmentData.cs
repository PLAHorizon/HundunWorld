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
        Bag,

        /// <summary>外观装备：不提供战斗属性，只改变角色整体外形</summary>
        Appearance
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
        Face,

        /// <summary>外观：独立于战斗装备的整体外形层</summary>
        Appearance
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

        /// <summary>五行属性加成（雷达图口径的"亲和"，不参与伤害公式）</summary>
        public Dictionary<WuxingElement, int> WuxingBonus = new Dictionary<WuxingElement, int>();

        // 装备的五行战斗三行：与基础属性面板里的"五行攻击/五行防御/五行破防"一一对应，
        // 键名与服务端物品模板 base_attributes JSON 的键（MetalAttack / MetalDefense / MetalPenetration…）
        // 同一套词表，两端各自求和/取高后必须得到同一个数，否则面板显示一套、服务端结算另一套。

        /// <summary>五行攻击力加成（按元素分行；求和侧）</summary>
        public Dictionary<WuxingElement, int> WuxingAttack = new Dictionary<WuxingElement, int>();

        /// <summary>五行防御力加成（按元素分行；与其余来源取最高）</summary>
        public Dictionary<WuxingElement, int> WuxingDefense = new Dictionary<WuxingElement, int>();

        /// <summary>五行破防值加成（按元素分行，存攻方本行；与其余来源取最高）</summary>
        public Dictionary<WuxingElement, int> WuxingPenetration = new Dictionary<WuxingElement, int>();

        /// <summary>需求等级</summary>
        public int RequiredLevel;

        /// <summary>装备品质，0=白,1=绿,2=蓝,3=紫,4=橙,5=红</summary>
        public int Quality;

        /// <summary>物品等级（装等）</summary>
        public int ItemLevel;

        /// <summary>背包扩展格子数（仅对 EquipmentType.Bag 有效，其他类型为 0）</summary>
        public int ExtraSlots = 0;

        /// <summary>
        /// 外观装备使用的全身模型路径（仅 <see cref="EquipmentType.Appearance"/> 使用）。
        /// 与 <see cref="BodyModel"/> 二选一：字段留空时按路径延迟加载，
        /// 避免静态初始化阶段访问 Content 系统。
        /// </summary>
        public string BodyModelPath;

        /// <summary>限定性别：-1 = 不限，0 = 男，1 = 女（仅外观装备使用）。</summary>
        public int RequiredGender = -1;

        /// <summary>
        /// 外观装备对整套模型的缩放（仅 <see cref="EquipmentType.Appearance"/> 使用）。
        /// 模型原点在脚底，因此按比例放大/收窄不会让角色悬空；
        /// 这是当前没有正式时装网格时的"整体外形"载体，接入新资产后可改回 1。
        /// </summary>
        public Vector3 BodyScale = Vector3.One;
    }
}

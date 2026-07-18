using FlaxEngine;
using Game.Character.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Equipment
{
    /// <summary>
    /// 装备/配饰/武器数据库
    /// </summary>
    public static class EquipmentDatabase
    {
        /// <summary>默认身体装备ID</summary>
        public static int DefaultBodyId => 10001;

        /// <summary>默认长剑ID</summary>
        public static int DefaultLongswordId => 20001;

        /// <summary>默认头巾ID</summary>
        public static int DefaultHeadScarfId => 30001;

        /// <summary>默认项链ID</summary>
        public static int DefaultNecklaceId => 10002;

        /// <summary>默认护肩ID</summary>
        public static int DefaultShoulderGuardId => 10003;

        /// <summary>默认披风ID</summary>
        public static int DefaultCloakId => 10004;

        /// <summary>默认腰带ID</summary>
        public static int DefaultBeltId => 10005;

        /// <summary>默认长裤ID</summary>
        public static int DefaultLeggingsId => 10006;

        /// <summary>默认布鞋ID</summary>
        public static int DefaultShoesId => 10007;

        /// <summary>默认短剑ID</summary>
        public static int DefaultDaggerId => 10008;

        /// <summary>默认右戒指ID</summary>
        public static int DefaultRightRingId => 10009;

        /// <summary>默认左戒指ID</summary>
        public static int DefaultLeftRingId => 10010;

        /// <summary>默认右手腕ID</summary>
        public static int DefaultRightWristId => 10011;

        /// <summary>默认左手腕ID</summary>
        public static int DefaultLeftWristId => 10012;

        /// <summary>默认面具ID</summary>
        public static int DefaultMaskId => 10013;

        /// <summary>粗布小包ID</summary>
        public static int SmallClothBagId => 2001;

        /// <summary>皮革中包ID</summary>
        public static int MediumLeatherBagId => 2002;

        /// <summary>丝绸大包ID</summary>
        public static int LargeSilkBagId => 2003;

        /// <summary>龙皮巨包ID</summary>
        public static int HugeDragonBagId => 2004;

        /// <summary>默认身体模型资产 GUID</summary>
        public static readonly Guid DefaultBodyModelGuid = new Guid("c7c70820409088e4d96db396a43c410f");

        /// <summary>默认身体模型资产路径（必须带 .flax 后缀）</summary>
        public const string DefaultBodyModelPath = "Content/Character/Models/skm_uefn_mannequin.flax";

        /// <summary>
        /// 按需加载并缓存默认身体模型
        /// </summary>
        public static SkinnedModel GetDefaultBodyModel()
        {
            if (DefaultBody.BodyModel != null)
            {
                if (DefaultBody.BodyModel.IsLoaded) return DefaultBody.BodyModel;
                DefaultBody.BodyModel.WaitForLoaded(30000.0);
                if (DefaultBody.BodyModel.IsLoaded) return DefaultBody.BodyModel;
            }

            var asset = Content.Load<SkinnedModel>(DefaultBodyModelGuid);
            if (asset != null && asset.IsLoaded)
            {
                DefaultBody.BodyModel = asset;
                return asset;
            }
            if (asset != null)
            {
                asset.WaitForLoaded(30000.0);
                if (asset.IsLoaded)
                {
                    DefaultBody.BodyModel = asset;
                    return asset;
                }
            }

            asset = Content.LoadAsync<SkinnedModel>(DefaultBodyModelPath);
            if (asset != null && asset.WaitForLoaded(30000.0) && asset.IsLoaded)
            {
                DefaultBody.BodyModel = asset;
                return asset;
            }

            Debug.LogError($"[EquipmentDatabase] 无法加载默认身体模型: {DefaultBodyModelGuid}, {DefaultBodyModelPath}");
            return null;
        }

        /// <summary>
        /// 默认衣服
        /// </summary>
        public static readonly EquipmentData DefaultBody = new EquipmentData
        {
            Id = 10001,
            Name = "默认衣服",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Body,
            // BodyModel 延迟加载：避免在类型静态初始化时访问 Content 系统
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "新手初始衣物，提供基础防护。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 10f },
                { "HP", 50f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 20 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认长剑
        /// </summary>
        public static readonly EquipmentData DefaultLongsword = new EquipmentData
        {
            Id = 20001,
            Name = "默认长剑",
            IconPath = "Content/Icons/Equipment/DefaultLongsword.flax",
            Type = EquipmentType.Weapon,
            Slot = EquipmentSlot.RightHand,
            StaticMesh = null,
            AttachmentBoneName = "RightHand",
            Description = "新手练习用长剑，锋锐不足但足以防身。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 15f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 30 }
            },
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 8
        };

        /// <summary>
        /// 默认头巾
        /// </summary>
        public static readonly EquipmentData DefaultHeadScarf = new EquipmentData
        {
            Id = 30001,
            Name = "默认头巾",
            IconPath = "Content/Icons/Equipment/DefaultHeadScarf.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Head,
            StaticMesh = null,
            AttachmentBoneName = "Head",
            Description = "朴素头巾，可略微提升内力恢复。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 30f },
                { "Defense", 5f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 15 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认项链
        /// </summary>
        public static readonly EquipmentData DefaultNecklace = new EquipmentData
        {
            Id = 10002,
            Name = "新手项链",
            IconPath = "Content/Icons/Equipment/DefaultNecklace.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Neck,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "朴素的木质项链，可略微提升内力恢复。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认护肩
        /// </summary>
        public static readonly EquipmentData DefaultShoulderGuard = new EquipmentData
        {
            Id = 10003,
            Name = "新手护肩",
            IconPath = "Content/Icons/Equipment/DefaultShoulderGuard.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Shoulder,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "皮质护肩，能缓冲部分冲击。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 5f },
                { "HP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认披风
        /// </summary>
        public static readonly EquipmentData DefaultCloak = new EquipmentData
        {
            Id = 10004,
            Name = "新手披风",
            IconPath = "Content/Icons/Equipment/DefaultCloak.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "粗糙的亚麻披风，可遮挡风尘。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认腰带
        /// </summary>
        public static readonly EquipmentData DefaultBelt = new EquipmentData
        {
            Id = 10005,
            Name = "新手腰带",
            IconPath = "Content/Icons/Equipment/DefaultBelt.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Waist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "结实的布腰带，可悬挂随身物品。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 30f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认长裤
        /// </summary>
        public static readonly EquipmentData DefaultLeggings = new EquipmentData
        {
            Id = 10006,
            Name = "新手长裤",
            IconPath = "Content/Icons/Equipment/DefaultLeggings.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Legs,
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "耐穿的布裤，适合长途跋涉。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 6f },
                { "HP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 15 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认布鞋
        /// </summary>
        public static readonly EquipmentData DefaultShoes = new EquipmentData
        {
            Id = 10007,
            Name = "新手布鞋",
            IconPath = "Content/Icons/Equipment/DefaultShoes.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Feet,
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "轻便的布鞋，行走时悄无声息。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 4f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认短剑
        /// </summary>
        public static readonly EquipmentData DefaultDagger = new EquipmentData
        {
            Id = 10008,
            Name = "新手短剑",
            IconPath = "Content/Icons/Equipment/DefaultDagger.flax",
            Type = EquipmentType.Weapon,
            Slot = EquipmentSlot.LeftHand,
            StaticMesh = null,
            AttachmentBoneName = "LeftHand",
            Description = "新手练习用短剑，灵活轻巧。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 20 }
            },
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 8
        };

        /// <summary>
        /// 默认右戒指
        /// </summary>
        public static readonly EquipmentData DefaultRightRing = new EquipmentData
        {
            Id = 10009,
            Name = "新手右戒指",
            IconPath = "Content/Icons/Equipment/DefaultRightRing.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.RightRing,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "铁环戒指，微微提升力量。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认左戒指
        /// </summary>
        public static readonly EquipmentData DefaultLeftRing = new EquipmentData
        {
            Id = 10010,
            Name = "新手左戒指",
            IconPath = "Content/Icons/Equipment/DefaultLeftRing.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.LeftRing,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "木质戒指，蕴含一丝自然灵气。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 15f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认右手腕
        /// </summary>
        public static readonly EquipmentData DefaultRightWrist = new EquipmentData
        {
            Id = 10011,
            Name = "新手右手腕",
            IconPath = "Content/Icons/Equipment/DefaultRightWrist.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.RightWrist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "皮质护腕，保护右手腕关节。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 4f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认左手腕
        /// </summary>
        public static readonly EquipmentData DefaultLeftWrist = new EquipmentData
        {
            Id = 10012,
            Name = "新手左手腕",
            IconPath = "Content/Icons/Equipment/DefaultLeftWrist.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.LeftWrist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "布质护腕，便于左手灵活施法。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认面具
        /// </summary>
        public static readonly EquipmentData DefaultMask = new EquipmentData
        {
            Id = 10013,
            Name = "新手面具",
            IconPath = "Content/Icons/Equipment/DefaultMask.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Face,
            StaticMesh = null,
            AttachmentBoneName = "Head",
            Description = "木制面具，可威慑弱小的野兽。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 2f },
                { "Defense", 2f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Fire, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 粗布小包
        /// </summary>
        public static readonly EquipmentData SmallClothBag = new EquipmentData
        {
            Id = 2001,
            Name = "粗布小包",
            IconPath = "Content/Icons/Equipment/SmallClothBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "粗布缝制的小型背包，可扩展 6 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 5 }
            },
            RequiredLevel = 1,
            Quality = 0,
            ItemLevel = 5,
            ExtraSlots = 6
        };

        /// <summary>
        /// 皮革中包
        /// </summary>
        public static readonly EquipmentData MediumLeatherBag = new EquipmentData
        {
            Id = 2002,
            Name = "皮革中包",
            IconPath = "Content/Icons/Equipment/MediumLeatherBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "坚韧皮革缝制的中型背包，可扩展 12 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 20f },
                { "Defense", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 10,
            Quality = 1,
            ItemLevel = 15,
            ExtraSlots = 12
        };

        /// <summary>
        /// 丝绸大包
        /// </summary>
        public static readonly EquipmentData LargeSilkBag = new EquipmentData
        {
            Id = 2003,
            Name = "丝绸大包",
            IconPath = "Content/Icons/Equipment/LargeSilkBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "丝绸织就的大型背包，轻便而结实，可扩展 18 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 30f },
                { "Defense", 6f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 15 }
            },
            RequiredLevel = 30,
            Quality = 2,
            ItemLevel = 35,
            ExtraSlots = 18
        };

        /// <summary>
        /// 龙皮巨包
        /// </summary>
        public static readonly EquipmentData HugeDragonBag = new EquipmentData
        {
            Id = 2004,
            Name = "龙皮巨包",
            IconPath = "Content/Icons/Equipment/HugeDragonBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "以龙皮缝制的巨型背包，蕴含龙气，可扩展 20 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 50f },
                { "Defense", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Fire, 20 }
            },
            RequiredLevel = 50,
            Quality = 3,
            ItemLevel = 60,
            ExtraSlots = 20
        };

        /// <summary>
        /// 根据装备ID获取装备数据
        /// </summary>
        /// <param name="id">装备ID</param>
        /// <returns>装备数据，未找到则返回 null</returns>
        public static EquipmentData GetEquipment(int id)
        {
            return id switch
            {
                10001 => DefaultBody,
                10002 => DefaultNecklace,
                10003 => DefaultShoulderGuard,
                10004 => DefaultCloak,
                10005 => DefaultBelt,
                10006 => DefaultLeggings,
                10007 => DefaultShoes,
                10008 => DefaultDagger,
                10009 => DefaultRightRing,
                10010 => DefaultLeftRing,
                10011 => DefaultRightWrist,
                10012 => DefaultLeftWrist,
                10013 => DefaultMask,
                2001 => SmallClothBag,
                2002 => MediumLeatherBag,
                2003 => LargeSilkBag,
                2004 => HugeDragonBag,
                20001 => DefaultLongsword,
                30001 => DefaultHeadScarf,
                _ => null
            };
        }

        /// <summary>
        /// 获取所有装备数据
        /// </summary>
        /// <returns>所有装备列表</returns>
        public static List<EquipmentData> GetAllEquipments()
        {
            return new List<EquipmentData>
            {
                DefaultBody,
                DefaultNecklace,
                DefaultShoulderGuard,
                DefaultCloak,
                DefaultBelt,
                DefaultLeggings,
                DefaultShoes,
                DefaultDagger,
                DefaultRightRing,
                DefaultLeftRing,
                DefaultRightWrist,
                DefaultLeftWrist,
                DefaultMask,
                DefaultLongsword,
                DefaultHeadScarf,
                SmallClothBag,
                MediumLeatherBag,
                LargeSilkBag,
                HugeDragonBag
            };
        }

        /// <summary>
        /// 根据装备槽位获取装备数据
        /// </summary>
        /// <param name="slot">装备槽位</param>
        /// <returns>该槽位下的装备列表</returns>
        public static List<EquipmentData> GetEquipmentsBySlot(EquipmentSlot slot)
        {
            return GetAllEquipments().Where(e => e.Slot == slot).ToList();
        }
    }
}

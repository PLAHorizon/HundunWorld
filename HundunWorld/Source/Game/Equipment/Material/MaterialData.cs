using FlaxEngine;
using Game.Character.Attributes;

namespace Game.Equipment.Material
{
    /// <summary>
    /// 材料等级
    /// </summary>
    public enum MaterialTier
    {
        /// <summary>初级材料（武侠前期1-20级）</summary>
        Basic = 1,
        
        /// <summary>中级材料（武侠中期21-40级）</summary>
        Intermediate = 2,
        
        /// <summary>高级材料（武侠后期41-50级）</summary>
        Advanced = 3,
        
        /// <summary>仙级材料（仙侠阶段51-150级）</summary>
        Immortal = 4,
        
        /// <summary>神级材料（玄幻阶段151-300级）</summary>
        Divine = 5
    }

    /// <summary>
    /// 材料数据
    /// </summary>
    public class MaterialData
    {
        /// <summary>材料ID</summary>
        public int MaterialId;

        /// <summary>材料名称</summary>
        public string MaterialName;

        /// <summary>材料描述</summary>
        public string Description;

        /// <summary>五行属性</summary>
        public WuxingElement Element;

        /// <summary>材料等级</summary>
        public MaterialTier Tier;

        /// <summary>堆叠上限</summary>
        public int MaxStack = 999;

        /// <summary>出售价格</summary>
        public int SellPrice = 1;

        /// <summary>图标路径</summary>
        public string IconPath;

        /// <summary>
        /// 获取材料品质颜色
        /// </summary>
        public Color GetQualityColor()
        {
            return Tier switch
            {
                MaterialTier.Basic => Color.White,        // 白色
                MaterialTier.Intermediate => Color.Green, // 绿色
                MaterialTier.Advanced => Color.Blue,      // 蓝色
                MaterialTier.Immortal => Color.Purple,    // 紫色
                MaterialTier.Divine => Color.Red,         // 红色
                _ => Color.Gray
            };
        }

        /// <summary>
        /// 获取五行属性颜色
        /// </summary>
        public Color GetElementColor()
        {
            return Element switch
            {
                WuxingElement.Metal => new Color(1f, 0.84f, 0f),   // 金色
                WuxingElement.Wood => new Color(0f, 1f, 0f),       // 绿色
                WuxingElement.Water => new Color(0f, 0.5f, 1f),    // 蓝色
                WuxingElement.Fire => new Color(1f, 0.25f, 0f),    // 红色
                WuxingElement.Earth => new Color(0.8f, 0.6f, 0.2f),// 土黄色
                _ => Color.White
            };
        }
    }

    /// <summary>
    /// 预定义材料数据
    /// </summary>
    public static class MaterialDatabase
    {
        /// <summary>
        /// 初级金材料：铁矿石
        /// </summary>
        public static MaterialData IronOre = new MaterialData
        {
            MaterialId = 10001,
            MaterialName = "铁矿石",
            Description = "最基础的金属矿石，蕴含微弱的金属性能量",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Basic,
            MaxStack = 999,
            SellPrice = 1,
            IconPath = "Content/Icons/Materials/IronOre"
        };

        /// <summary>
        /// 初级木材料：青竹
        /// </summary>
        public static MaterialData GreenBamboo = new MaterialData
        {
            MaterialId = 10002,
            MaterialName = "青竹",
            Description = "生长在山林间的青竹，蕴含生机",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Basic,
            MaxStack = 999,
            SellPrice = 1,
            IconPath = "Content/Icons/Materials/GreenBamboo"
        };

        /// <summary>
        /// 初级水材料：寒泉水
        /// </summary>
        public static MaterialData ColdSpringWater = new MaterialData
        {
            MaterialId = 10003,
            MaterialName = "寒泉水",
            Description = "从深山寒泉中汲取的泉水，透心清凉",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Basic,
            MaxStack = 999,
            SellPrice = 1,
            IconPath = "Content/Icons/Materials/ColdSpringWater"
        };

        /// <summary>
        /// 初级火材料：赤焰草
        /// </summary>
        public static MaterialData FlameGrass = new MaterialData
        {
            MaterialId = 10004,
            MaterialName = "赤焰草",
            Description = "生长在火山附近的草药，含有微弱火元素",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Basic,
            MaxStack = 999,
            SellPrice = 1,
            IconPath = "Content/Icons/Materials/FlameGrass"
        };

        /// <summary>
        /// 初级土材料：黄土块
        /// </summary>
        public static MaterialData YellowEarth = new MaterialData
        {
            MaterialId = 10005,
            MaterialName = "黄土块",
            Description = "厚重的黄土，蕴含大地之力",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Basic,
            MaxStack = 999,
            SellPrice = 1,
            IconPath = "Content/Icons/Materials/YellowEarth"
        };

        /// <summary>
        /// 中级金材料：精铁
        /// </summary>
        public static MaterialData RefinedIron = new MaterialData
        {
            MaterialId = 20001,
            MaterialName = "精铁",
            Description = "经过提炼的精铁，金属性更加纯粹",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 10,
            IconPath = "Content/Icons/Materials/RefinedIron"
        };

        /// <summary>
        /// 中级木材料：紫檀木
        /// </summary>
        public static MaterialData PurpleSandalwood = new MaterialData
        {
            MaterialId = 20002,
            MaterialName = "紫檀木",
            Description = "珍贵的紫檀木，木质坚硬，蕴含浓郁木属性能量",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 10,
            IconPath = "Content/Icons/Materials/PurpleSandalwood"
        };

        /// <summary>
        /// 中级水材料：灵泉露
        /// </summary>
        public static MaterialData SpiritDew = new MaterialData
        {
            MaterialId = 20003,
            MaterialName = "灵泉露",
            Description = "清晨采集的灵泉水珠，蕴含纯净水属性能量",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 10,
            IconPath = "Content/Icons/Materials/SpiritDew"
        };

        /// <summary>
        /// 中级火材料：炎阳花
        /// </summary>
        public static MaterialData SolarFlameFlower = new MaterialData
        {
            MaterialId = 20004,
            MaterialName = "炎阳花",
            Description = "只在正午绽放的奇花，蕴含炽热火属性能量",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 10,
            IconPath = "Content/Icons/Materials/SolarFlameFlower"
        };

        /// <summary>
        /// 中级土材料：黑曜石
        /// </summary>
        public static MaterialData Obsidian = new MaterialData
        {
            MaterialId = 20005,
            MaterialName = "黑曜石",
            Description = "天然形成的黑曜石，质地坚硬，蕴含厚重土属性能量",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 10,
            IconPath = "Content/Icons/Materials/Obsidian"
        };

        /// <summary>
        /// 高级金材料：玄铁精
        /// </summary>
        public static MaterialData DarksteelEssence = new MaterialData
        {
            MaterialId = 30001,
            MaterialName = "玄铁精",
            Description = "千年玄铁提炼出的精华，锋利无比，蕴含极强金属性能量",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 100,
            IconPath = "Content/Icons/Materials/DarksteelEssence"
        };

        /// <summary>
        /// 高级木材料：万年灵芝
        /// </summary>
        public static MaterialData MillenniumGinseng = new MaterialData
        {
            MaterialId = 30002,
            MaterialName = "万年灵芝",
            Description = "生长千年的灵芝，蕴含强大生命能量",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 100,
            IconPath = "Content/Icons/Materials/MillenniumGinseng"
        };

        /// <summary>
        /// 高级水材料：冰晶髓
        /// </summary>
        public static MaterialData IceCrystalMarrow = new MaterialData
        {
            MaterialId = 30003,
            MaterialName = "冰晶髓",
            Description = "万年寒冰核心凝结而成，蕴含极致水属性能量",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 100,
            IconPath = "Content/Icons/Materials/IceCrystalMarrow"
        };

        /// <summary>
        /// 高级火材料：九幽炎晶
        /// </summary>
        public static MaterialData NetherflameCrystal = new MaterialData
        {
            MaterialId = 30004,
            MaterialName = "九幽炎晶",
            Description = "来自九幽深处的火焰结晶，蕴含毁灭性的火属性能量",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 100,
            IconPath = "Content/Icons/Materials/NetherflameCrystal"
        };

        /// <summary>
        /// 高级土材料：地脉灵土
        /// </summary>
        public static MaterialData EarthVeinSpiritSoil = new MaterialData
        {
            MaterialId = 30005,
            MaterialName = "地脉灵土",
            Description = "汇聚地脉精华的灵土，蕴含厚重的地属性能量",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 100,
            IconPath = "Content/Icons/Materials/EarthVeinSpiritSoil"
        };

        /// <summary>
        /// 仙级金材料：星辰陨铁
        /// </summary>
        public static MaterialData StellarMeteorite = new MaterialData
        {
            MaterialId = 40001,
            MaterialName = "星辰陨铁",
            Description = "从天外坠落的星辰陨铁，蕴含宇宙金属性能量",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Immortal,
            MaxStack = 999,
            SellPrice = 1000,
            IconPath = "Content/Icons/Materials/StellarMeteorite"
        };

        /// <summary>
        /// 仙级木材料：不死神木
        /// </summary>
        public static MaterialData ImmortalWood = new MaterialData
        {
            MaterialId = 40002,
            MaterialName = "不死神木",
            Description = "传说中的不死神木，蕴含永恒的生命能量",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Immortal,
            MaxStack = 999,
            SellPrice = 1000,
            IconPath = "Content/Icons/Materials/ImmortalWood"
        };

        /// <summary>
        /// 仙级水材料：九天玄露
        /// </summary>
        public static MaterialData CelestialDew = new MaterialData
        {
            MaterialId = 40003,
            MaterialName = "九天玄露",
            Description = "从九天之上凝聚而下的玄妙露水，蕴含纯净的水属性能量",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Immortal,
            MaxStack = 999,
            SellPrice = 1000,
            IconPath = "Content/Icons/Materials/CelestialDew"
        };

        /// <summary>
        /// 仙级火材料：太阳真火
        /// </summary>
        public static MaterialData SolarTrueFire = new MaterialData
        {
            MaterialId = 40004,
            MaterialName = "太阳真火",
            Description = "来自太阳核心的真火，蕴含极致的火属性能量",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Immortal,
            MaxStack = 999,
            SellPrice = 1000,
            IconPath = "Content/Icons/Materials/SolarTrueFire"
        };

        /// <summary>
        /// 仙级土材料：混沌元土
        /// </summary>
        public static MaterialData ChaosPrimordialSoil = new MaterialData
        {
            MaterialId = 40005,
            MaterialName = "混沌元土",
            Description = "开天辟地时形成的混沌之土，蕴含原始的土属性能量",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Immortal,
            MaxStack = 999,
            SellPrice = 1000,
            IconPath = "Content/Icons/Materials/ChaosPrimordialSoil"
        };

        /// <summary>
        /// 神级金材料：太虚神金
        /// </summary>
        public static MaterialData VoidDivineMetal = new MaterialData
        {
            MaterialId = 50001,
            MaterialName = "太虚神金",
            Description = "存在于太虚之中的神金，蕴含超越凡俗的金属性能量",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Divine,
            MaxStack = 999,
            SellPrice = 10000,
            IconPath = "Content/Icons/Materials/VoidDivineMetal"
        };

        /// <summary>
        /// 神级木材料：创世神木
        /// </summary>
        public static MaterialData CreationDivineWood = new MaterialData
        {
            MaterialId = 50002,
            MaterialName = "创世神木",
            Description = "创造世界时诞生的神木，蕴含创世的生命能量",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Divine,
            MaxStack = 999,
            SellPrice = 10000,
            IconPath = "Content/Icons/Materials/CreationDivineWood"
        };

        /// <summary>
        /// 神级水材料：时光之泉
        /// </summary>
        public static MaterialData TimeSpring = new MaterialData
        {
            MaterialId = 50003,
            MaterialName = "时光之泉",
            Description = "流淌着时光之力的神秘泉水，蕴含时间的水属性能量",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Divine,
            MaxStack = 999,
            SellPrice = 10000,
            IconPath = "Content/Icons/Materials/TimeSpring"
        };

        /// <summary>
        /// 神级火材料：混沌圣火
        /// </summary>
        public static MaterialData ChaosSacredFire = new MaterialData
        {
            MaterialId = 50004,
            MaterialName = "混沌圣火",
            Description = "燃烧在混沌边缘的圣火，蕴含毁灭与重生的火属性能量",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Divine,
            MaxStack = 999,
            SellPrice = 10000,
            IconPath = "Content/Icons/Materials/ChaosSacredFire"
        };

        /// <summary>
        /// 神级土材料：乾坤元土
        /// </summary>
        public static MaterialData HeavenEarthPrimordialSoil = new MaterialData
        {
            MaterialId = 50005,
            MaterialName = "乾坤元土",
            Description = "承载天地乾坤的元始之土，蕴含宇宙的土属性能量",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Divine,
            MaxStack = 999,
            SellPrice = 10000,
            IconPath = "Content/Icons/Materials/HeavenEarthPrimordialSoil"
        };

        #region 特殊材料和装饰性材料

        /// <summary>
        /// 特殊材料：龙鳞
        /// </summary>
        public static MaterialData DragonScale = new MaterialData
        {
            MaterialId = 60001,
            MaterialName = "龙鳞",
            Description = "传说中神龙脱落的鳞片，蕴含强大的龙族力量",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Divine,
            MaxStack = 99,
            SellPrice = 50000,
            IconPath = "Content/Icons/Materials/DragonScale"
        };

        /// <summary>
        /// 特殊材料：凤凰羽
        /// </summary>
        public static MaterialData PhoenixFeather = new MaterialData
        {
            MaterialId = 60002,
            MaterialName = "凤凰羽",
            Description = "涅槃重生的凤凰之羽，蕴含不死的火焰之力",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Divine,
            MaxStack = 99,
            SellPrice = 50000,
            IconPath = "Content/Icons/Materials/PhoenixFeather"
        };

        /// <summary>
        /// 特殊材料：麒麟角
        /// </summary>
        public static MaterialData QilinHorn = new MaterialData
        {
            MaterialId = 60003,
            MaterialName = "麒麟角",
            Description = "祥瑞神兽麒麟的角，蕴含祥和的生命之力",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Divine,
            MaxStack = 99,
            SellPrice = 50000,
            IconPath = "Content/Icons/Materials/QilinHorn"
        };

        /// <summary>
        /// 装饰材料：夜明珠
        /// </summary>
        public static MaterialData NightPearl = new MaterialData
        {
            MaterialId = 70001,
            MaterialName = "夜明珠",
            Description = "夜间发光的宝珠，具有装饰价值",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 500,
            IconPath = "Content/Icons/Materials/NightPearl"
        };

        /// <summary>
        /// 装饰材料：翡翠原石
        /// </summary>
        public static MaterialData JadeRoughStone = new MaterialData
        {
            MaterialId = 70002,
            MaterialName = "翡翠原石",
            Description = "未经雕琢的翡翠原石，具有收藏价值",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/JadeRoughStone"
        };

        #endregion

        #region 功能性材料

        /// <summary>
        /// 强化材料：强化石
        /// </summary>
        public static MaterialData EnhancementStone = new MaterialData
        {
            MaterialId = 80001,
            MaterialName = "强化石",
            Description = "用于装备强化的基础材料",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 20,
            IconPath = "Content/Icons/Materials/EnhancementStone"
        };

        /// <summary>
        /// 强化材料：高级强化石
        /// </summary>
        public static MaterialData AdvancedEnhancementStone = new MaterialData
        {
            MaterialId = 80002,
            MaterialName = "高级强化石",
            Description = "用于高级装备强化的材料",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 200,
            IconPath = "Content/Icons/Materials/AdvancedEnhancementStone"
        };

        /// <summary>
        /// 宝石材料：红宝石碎片
        /// </summary>
        public static MaterialData RubyFragment = new MaterialData
        {
            MaterialId = 90001,
            MaterialName = "红宝石碎片",
            Description = "镶嵌在装备上可增加攻击力的宝石碎片",
            Element = WuxingElement.Fire,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/RubyFragment"
        };

        /// <summary>
        /// 宝石材料：蓝宝石碎片
        /// </summary>
        public static MaterialData SapphireFragment = new MaterialData
        {
            MaterialId = 90002,
            MaterialName = "蓝宝石碎片",
            Description = "镶嵌在装备上可增加防御力的宝石碎片",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/SapphireFragment"
        };

        /// <summary>
        /// 宝石材料：绿宝石碎片
        /// </summary>
        public static MaterialData EmeraldFragment = new MaterialData
        {
            MaterialId = 90003,
            MaterialName = "绿宝石碎片",
            Description = "镶嵌在装备上可增加生命值的宝石碎片",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/EmeraldFragment"
        };

        /// <summary>
        /// 宝石材料：黄宝石碎片
        /// </summary>
        public static MaterialData TopazFragment = new MaterialData
        {
            MaterialId = 90004,
            MaterialName = "黄宝石碎片",
            Description = "镶嵌在装备上可增加暴击率的宝石碎片",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/TopazFragment"
        };

        /// <summary>
        /// 宝石材料：白宝石碎片
        /// </summary>
        public static MaterialData DiamondFragment = new MaterialData
        {
            MaterialId = 90005,
            MaterialName = "白宝石碎片",
            Description = "镶嵌在装备上可增加闪避率的宝石碎片",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 50,
            IconPath = "Content/Icons/Materials/DiamondFragment"
        };

        #endregion

        #region 炼丹和任务材料

        /// <summary>
        /// 炼丹材料：聚灵草
        /// </summary>
        public static MaterialData SpiritGatheringGrass = new MaterialData
        {
            MaterialId = 100001,
            MaterialName = "聚灵草",
            Description = "能够聚集灵气的神奇草药，是炼制丹药的重要材料",
            Element = WuxingElement.Wood,
            Tier = MaterialTier.Intermediate,
            MaxStack = 999,
            SellPrice = 30,
            IconPath = "Content/Icons/Materials/SpiritGatheringGrass"
        };

        /// <summary>
        /// 炼丹材料：凝神花
        /// </summary>
        public static MaterialData MindFocusFlower = new MaterialData
        {
            MaterialId = 100002,
            MaterialName = "凝神花",
            Description = "能够帮助修炼者凝神静气的花朵，常用于炼制辅助丹药",
            Element = WuxingElement.Water,
            Tier = MaterialTier.Advanced,
            MaxStack = 999,
            SellPrice = 150,
            IconPath = "Content/Icons/Materials/MindFocusFlower"
        };

        /// <summary>
        /// 任务材料：古老卷轴
        /// </summary>
        public static MaterialData AncientScroll = new MaterialData
        {
            MaterialId = 110001,
            MaterialName = "古老卷轴",
            Description = "记载着古代秘密的卷轴，是完成某些任务的关键物品",
            Element = WuxingElement.Earth,
            Tier = MaterialTier.Advanced,
            MaxStack = 99,
            SellPrice = 500,
            IconPath = "Content/Icons/Materials/AncientScroll"
        };

        /// <summary>
        /// 任务材料：神秘钥匙
        /// </summary>
        public static MaterialData MysticKey = new MaterialData
        {
            MaterialId = 110002,
            MaterialName = "神秘钥匙",
            Description = "散发着神秘气息的钥匙，似乎能打开某些特殊的门锁",
            Element = WuxingElement.Metal,
            Tier = MaterialTier.Immortal,
            MaxStack = 1,
            SellPrice = 5000,
            IconPath = "Content/Icons/Materials/MysticKey"
        };

        #endregion

        /// <summary>
        /// 根据材料ID获取材料数据
        /// </summary>
        /// <param name="materialId">材料ID</param>
        /// <returns>材料数据，如果未找到则返回null</returns>
        public static MaterialData GetMaterial(int materialId)
        {
            return materialId switch
            {
                10001 => IronOre,
                10002 => GreenBamboo,
                10003 => ColdSpringWater,
                10004 => FlameGrass,
                10005 => YellowEarth,
                20001 => RefinedIron,
                20002 => PurpleSandalwood,
                20003 => SpiritDew,
                20004 => SolarFlameFlower,
                20005 => Obsidian,
                30001 => DarksteelEssence,
                30002 => MillenniumGinseng,
                30003 => IceCrystalMarrow,
                30004 => NetherflameCrystal,
                30005 => EarthVeinSpiritSoil,
                40001 => StellarMeteorite,
                40002 => ImmortalWood,
                40003 => CelestialDew,
                40004 => SolarTrueFire,
                40005 => ChaosPrimordialSoil,
                50001 => VoidDivineMetal,
                50002 => CreationDivineWood,
                50003 => TimeSpring,
                50004 => ChaosSacredFire,
                50005 => HeavenEarthPrimordialSoil,
                60001 => DragonScale,
                60002 => PhoenixFeather,
                60003 => QilinHorn,
                70001 => NightPearl,
                70002 => JadeRoughStone,
                80001 => EnhancementStone,
                80002 => AdvancedEnhancementStone,
                90001 => RubyFragment,
                90002 => SapphireFragment,
                90003 => EmeraldFragment,
                90004 => TopazFragment,
                90005 => DiamondFragment,
                100001 => SpiritGatheringGrass,
                100002 => MindFocusFlower,
                110001 => AncientScroll,
                110002 => MysticKey,
                _ => null
            };
        }
    }
}

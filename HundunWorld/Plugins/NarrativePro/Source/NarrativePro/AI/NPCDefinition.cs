using System;
using System.Collections.Generic;
using NarrativePro.Character;
using NarrativePro.Items;

namespace NarrativePro.AI
{
    /// <summary>
    /// NPC 定义。对应 UE5 UNPCDefinition。
    /// 继承角色定义，增加 NPC 特有属性：ID、名称、等级范围、唯一性、类引用、对话、商店、活动调度等。
    /// NPC 信息存储在数据资产中而非 Actor 上，便于在未加载 NPC 类时访问其信息。
    /// </summary>
    [Serializable]
    public class NPCDefinition : CharacterDefinition
    {
        /// <summary>NPC 的唯一 ID（用于对话、序列等系统引用）</summary>
        public string NPCID = "";

        /// <summary>NPC 显示名（用于交互和导航标记）</summary>
        public string NPCName = "";

        /// <summary>NPC 等级下限</summary>
        public int MinLevel = 1;

        /// <summary>NPC 等级上限</summary>
        public int MaxLevel = 1;

        /// <summary>是否允许多实例（普通敌人可为 true，剧情角色一般为 false）</summary>
        public bool bAllowMultipleInstances = true;

        /// <summary>唯一 NPC 的存档 GUID（bAllowMultipleInstances 为 false 时使用）</summary>
        public string UniqueNPCGUID = "";

        /// <summary>NPC 类路径（Prefab 路径占位，运行时加载）</summary>
        public string NPCClassPath = "";

        /// <summary>交互时触发的对话路径</summary>
        public string DialoguePath = "";

        /// <summary>标签对话集路径（自由移动对话，如 TaggedDialogue.Taunt/Greet）</summary>
        public string TaggedDialogueSetPath = "";

        /// <summary>是否为商店商人</summary>
        public bool bIsVendor = false;

        /// <summary>商人初始货币</summary>
        public int TradingCurrency = 0;

        /// <summary>收购物品时的价格百分比（0-1）</summary>
        public float BuyItemPercentage = 0.5f;

        /// <summary>出售物品时的价格百分比（0-1）</summary>
        public float SellItemPercentage = 1.0f;

        /// <summary>商人默认物品掉落表</summary>
        public List<LootTableRoll> TradingItemLoadout = new List<LootTableRoll>();

        /// <summary>商店友好名称</summary>
        public string ShopFriendlyName = "";

        /// <summary>NPC 活动调度路径列表</summary>
        public List<string> ActivitySchedulePaths = new List<string>();

        /// <summary>NPC 活动配置路径</summary>
        public string ActivityConfigurationPath = "";

        /// <summary>获取随机的 NPC 等级（MinLevel 到 MaxLevel 之间）</summary>
        public int GetRandomLevel()
        {
            if (MaxLevel <= MinLevel) return MinLevel;
            return MinLevel + _random.Next(MaxLevel - MinLevel + 1);
        }

        private static readonly System.Random _random = new System.Random();
    }

    /// <summary>
    /// Ped NPC 定义。对应 UE5 UPedNPCDefinition。
    /// 用于群体 AI（Mass Entity）的简化 NPC 定义。
    /// 注：Flax 无 Mass Entity 系统，此处仅保留数据结构。
    /// </summary>
    [Serializable]
    public class PedNPCDefinition : NPCDefinition
    {
        /// <summary>Ped 类型标签（用于群体生成器分类）</summary>
        public string PedTypeTag = "";
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
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
    /// Ped NPC 定义。对应 UE5 <c>UPedNPCDefinition</c>（<c>Public/AI/PedNPCDefinition.h:14-30</c>）。
    /// 用于群体 AI（Mass Entity）的简化 NPC 定义：只提供表现资产，不含交互/商店等重数据。
    /// </summary>
    [Serializable]
    public class PedNPCDefinition : NPCDefinition
    {
        /// <summary>
        /// 烘焙后的群体外观（低精度、合批用的静态网格）。
        /// 对应 UE5 <c>BakedPedMesh</c>（<c>PedNPCDefinition.h:21-22</c>，TSoftObjectPtr&lt;UStaticMesh&gt;）。
        /// Flax 中以资产路径表达惰性引用，由 <c>MassPedRepresentationSubsystem</c> 解析成可视化描述句柄。
        /// </summary>
        public string BakedPedMeshPath = "";

        /// <summary>
        /// 高精度行人骨骼网格。对应 UE5 <c>PedMesh</c>（<c>PedNPCDefinition.h:25-26</c>，TSoftObjectPtr&lt;USkeletalMesh&gt;）。
        /// 近景 Actor 表现用它替换烘焙网格。
        /// </summary>
        public string PedMeshPath = "";

        /// <summary>
        /// 高精度行人网格的动画蓝图/动画实例类。对应 UE5 <c>PedMeshAnimBP</c>
        /// （<c>PedNPCDefinition.h:28-29</c>，TSoftClassPtr&lt;UAnimInstance&gt;）。
        /// Flax 无 AnimBlueprint，对应物是 <c>AnimInstance</c> 脚本类型或动画资产路径。
        /// </summary>
        public string PedMeshAnimBPPath = "";

        /// <summary>Ped 类型标签（用于群体生成器分类）</summary>
        public string PedTypeTag = "";

        /// <summary>取烘焙群体网格资产。对应 UE5 <c>BakedPedMesh.LoadSynchronous()</c>。Flax 静态网格资产类型为 <c>Model</c>。</summary>
        public Model BakedPedMesh => LoadAsset<Model>(BakedPedMeshPath);

        /// <summary>取高精度骨骼网格资产。对应 UE5 <c>PedMesh.LoadSynchronous()</c>。</summary>
        public SkinnedModel PedMesh => LoadAsset<SkinnedModel>(PedMeshPath);

        private static T LoadAsset<T>(string path) where T : FlaxEngine.Asset
        {
            if (string.IsNullOrEmpty(path)) return null;
            var manager = NarrativePro.UnrealFramework.NarrativeAssetManager.Get();
            var asset = manager != null ? manager.GetAsset<T>(path) : null;
            if (asset == null)
            {
                try
                {
                    asset = FlaxEngine.Content.Load<T>(path);
                }
                catch
                {
                    // 资产缺失：保持 null，调用方按「无烘焙网格」降级
                    return null;
                }
            }
            return asset;
        }
    }
}

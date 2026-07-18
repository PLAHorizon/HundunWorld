using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.AI.Cover;
using NarrativePro.Music;

namespace NarrativePro.Core
{
    /// <summary>
    /// Narrative Pro 全局配置。对应 UE5 UArsenalSettings。
    /// UE5 中通过 DefaultEngine.ini 配置，Flax 中以单例 + JSON 序列化实现。
    /// </summary>
    [Serializable]
    public class ArsenalSettings
    {
        /// <summary>默认存档名。</summary>
        public string DefaultSaveName = "NarrativeSave";

        /// <summary>默认用户名（空则使用系统分配）。</summary>
        public string DefaultUsername = "";

        /// <summary>游戏入口地图路径。</summary>
        public string GameEntryMap = "";

        /// <summary>角色创建器地图路径。</summary>
        public string CharacterCreatorMap = "";

        /// <summary>开始新游戏时是否加载角色创建器。</summary>
        public bool bLoadCharacterCreatorOnNewGame = false;

        /// <summary>存档槽位数量。</summary>
        public int NumSaveSlots = 20;

        /// <summary>存档元数据文件名。</summary>
        public string MetadataSaveFileName = "NarrativeMetadata";

        // ===== 黑板键名（Flax 无 BehaviorTree，保留以备扩展） =====
        public string BBKey_TargetLocation = "TargetLocation";
        public string BBKey_TargetRotation = "TargetRotation";
        public string BBKey_Delay = "Delay";
        public string BBKey_PlayerPawn = "PlayerPawn";
        public string BBKey_FollowTarget = "FollowTarget";
        public string BBKey_AttackTarget = "AttackTarget";

        // ===== 音频（Flax 中以路径占位） =====
        public string MasterSoundClass = "";
        public string SFXSoundClass = "";
        public string UISoundClass = "";
        public string DialogueSoundClass = "";
        public string MusicSoundClass = "";
        public string MasterMetaSound = "";
        public string DefaultMusicSet = "";

        // ===== GAS 路径 =====
        public string HealGameplayEffect_SetByCaller = "";
        public string DamageGameplayEffect_SetByCaller = "";
        public string DynamicTagGameplayEffect = "";

        /// <summary>标签友好显示名映射。</summary>
        public Dictionary<string, string> TagFriendlyDisplayNames = new Dictionary<string, string>();

        // ===== 碰撞/追踪通道 =====
        public string InteractionTraceProfile = "Interaction";
        public int WeaponTraceChannel = 0;
        public int InteractionTraceChannel = 0;

        /// <summary>默认交互物拾取路径。</summary>
        public string DefaultInteractablePickup = "";

        // ===== 掩护生成参数 =====
        public float ChainLinkAngleTolerance = 45f;
        public float SmallestChainLinkLength = 100f;
        public float ChainLinkCorrectionAngleTolerance = 15f;
        public int CorrectionIterationCount = 2;
        public CoverTraceConfig CoverTraceConfig = new CoverTraceConfig();

        /// <summary>编辑器：项目设置不匹配时是否弹窗。</summary>
        public bool bDisplayProjectSettingsNotification = true;

        /// <summary>单例实例。</summary>
        public static ArsenalSettings Instance { get; set; } = LoadDefault();

        private static ArsenalSettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax Content/Settings 或 JSON 加载。暂时返回默认实例。
            return new ArsenalSettings();
        }

        /// <summary>获取标签的友好显示名。</summary>
        public string GetTagFriendlyDisplayName(GameplayTag tag)
        {
            if (tag == null || !tag.IsValid()) return "";
            var key = tag.ToString();
            if (TagFriendlyDisplayNames != null && TagFriendlyDisplayNames.TryGetValue(key, out var text))
                return text;
            return "";
        }
    }
}

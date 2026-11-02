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

        /// <summary>
        /// 配置文件默认路径。对应 UE5 config=Engine 的落盘位置；
        /// 文件不存在时按构造默认值运行。
        /// </summary>
        public const string DefaultConfigPath = "Content/NarrativePro/Settings/ArsenalSettings.json";

        /// <summary>单例实例。</summary>
        public static ArsenalSettings Instance { get; set; } = LoadDefault();

        /// <summary>读取默认配置：优先 JSON 配置文件，缺失或损坏时回退到构造默认值。</summary>
        private static ArsenalSettings LoadDefault()
        {
            return LoadFromFile(DefaultConfigPath) ?? new ArsenalSettings();
        }

        /// <summary>
        /// 从 JSON 文件加载设置。文件不存在返回 null（调用方按默认值处理），
        /// 解析失败写警告后返回 null，不拖垮启动。
        /// </summary>
        public static ArsenalSettings LoadFromFile(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;

            try
            {
                if (!System.IO.File.Exists(jsonFilePath)) return null;

                string json = System.IO.File.ReadAllText(jsonFilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var loaded = System.Text.Json.JsonSerializer.Deserialize<ArsenalSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    IncludeFields = true
                });
                if (loaded != null)
                {
                    NarrativeLog.Log($"ArsenalSettings 已从 {jsonFilePath} 加载。");
                }
                return loaded;
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"加载 Arsenal 设置 '{jsonFilePath}' 失败，改用默认值: {ex.Message}");
                return null;
            }
        }

        /// <summary>把当前实例写回 JSON 文件（编辑器/调试面板改完设置后持久化）。</summary>
        public void SaveToFile(string jsonFilePath = DefaultConfigPath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return;

            try
            {
                var directory = System.IO.Path.GetDirectoryName(jsonFilePath);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);

                string json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true
                });
                System.IO.File.WriteAllText(jsonFilePath, json);
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"保存 Arsenal 设置 '{jsonFilePath}' 失败: {ex.Message}");
            }
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

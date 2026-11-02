using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Settings
{
    /// <summary>
    /// Narrative 游戏难度枚举。对应 UE5 ENarrativeGameplayDifficulty。
    /// </summary>
    public enum ENarrativeGameplayDifficulty
    {
        /// <summary>简单</summary>
        Easy = 0,
        /// <summary>中等</summary>
        Medium = 1,
        /// <summary>困难</summary>
        Hard = 2,
        /// <summary>极难</summary>
        Insane = 3
    }

    /// <summary>
    /// 战斗相关开发者设置。对应 UE5 UNarrativeCombatDeveloperSettings。
    /// UE5 中使用 UCLASS(config=Engine, defaultconfig)，Flax 中以 [Serializable] 类 + 静态 Instance 单例实现。
    /// </summary>
    [Serializable]
    public class NarrativeCombatDeveloperSettings
    {
        /// <summary>若为 true，对敌人造成伤害时其头顶将显示伤害数字弹窗。</summary>
        public bool bEnableDamageNumbers = true;

        /// <summary>若为 true，玩家自身受到伤害时也会在头顶显示伤害数字弹窗。</summary>
        public bool bEnableDamageNumberOnSelf = false;

        /// <summary>
        /// 每个难度下授予玩家的攻击令牌数量。
        /// 对应 UE5 AvailableAttackTokens（TMap&lt;ENarrativeGameplayDifficulty, int32&gt;）。
        /// </summary>
        public Dictionary<ENarrativeGameplayDifficulty, int> AvailableAttackTokens = new Dictionary<ENarrativeGameplayDifficulty, int>
        {
            { ENarrativeGameplayDifficulty.Easy, 3 },
            { ENarrativeGameplayDifficulty.Medium, 3 },
            { ENarrativeGameplayDifficulty.Hard, 2 },
            { ENarrativeGameplayDifficulty.Insane, 2 }
        };

        /// <summary>
        /// 抢夺令牌者必须处于现有距离的此比例以内才能抢夺令牌。
        /// 例如 0.2 表示与目标距离为现有令牌持有者 0.2 倍时可抢夺。范围 0.01~1.0。
        /// </summary>
        public float StealTokenProximity = 0.2f;

        /// <summary>
        /// 令牌存活超过此秒数后可被抢夺，给其他攻击者留出机会。最小 0.01。
        /// </summary>
        public float TokenStealableAgeSeconds = 2.0f;

        /// <summary>
        /// AI 在各难度下的攻击频率倍率（相对射速的倍数）。
        /// 例如 3.0 表示以 3 倍射速攻击。对应 UE5 NPCAttackFrequencies。
        /// </summary>
        public Dictionary<ENarrativeGameplayDifficulty, float> NPCAttackFrequencies = new Dictionary<ENarrativeGameplayDifficulty, float>
        {
            { ENarrativeGameplayDifficulty.Easy, 0.5f },
            { ENarrativeGameplayDifficulty.Medium, 1.0f },
            { ENarrativeGameplayDifficulty.Hard, 1.5f },
            { ENarrativeGameplayDifficulty.Insane, 2.0f }
        };

        /// <summary>
        /// 当 NPC 开始攻击敌人时，此距离内的队友会被通知一同参战。
        /// 调低可避免整座城市卷入战斗（例如只让 50 米内的队友参战）。最小 10。
        /// </summary>
        public float NotifyTeammatesToFightRange = 50.0f;

        /// <summary>
        /// 读取近战攻击动画时采样的帧数。数值越小性能越好但精度越低。范围 1~100。
        /// </summary>
        public int MeleeCombatAnimSampleAmount = 10;

        /// <summary>是否允许同阵营单位互相造成伤害（友军伤害）。</summary>
        public bool bAllowFriendlyFire = false;

        /// <summary>
        /// 配置文件默认路径。对应 UE5 config=Engine 的落盘位置；
        /// 文件不存在时按构造默认值运行（等价 UE5 无 ini 覆盖时用 CDO 默认）。
        /// </summary>
        public const string DefaultConfigPath = "Content/NarrativePro/Settings/NarrativeCombatDeveloperSettings.json";

        /// <summary>单例实例。</summary>
        public static NarrativeCombatDeveloperSettings Instance { get; set; } = LoadDefault();

        /// <summary>读取默认配置：优先 JSON 配置文件，缺失或损坏时回退到构造默认值。</summary>
        private static NarrativeCombatDeveloperSettings LoadDefault()
        {
            return LoadFromFile(DefaultConfigPath) ?? new NarrativeCombatDeveloperSettings();
        }

        /// <summary>
        /// 从 JSON 文件加载设置。文件不存在返回 null（调用方按默认值处理），
        /// 解析失败写一条警告后同样返回 null，不让配置错误拖垮启动。
        /// 难度键以枚举名字符串序列化（System.Text.Json 自 .NET 5 起支持枚举字典键）。
        /// </summary>
        public static NarrativeCombatDeveloperSettings LoadFromFile(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath)) return null;

            try
            {
                // 相对路径按工程根目录归一（编辑器/PIE/打包构建的当前目录不一致，见 NarrativeDataPaths）。
                jsonFilePath = NarrativePro.Data.NarrativeDataPaths.Resolve(jsonFilePath);
                if (!System.IO.File.Exists(jsonFilePath)) return null;

                string json = System.IO.File.ReadAllText(jsonFilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var loaded = System.Text.Json.JsonSerializer.Deserialize<NarrativeCombatDeveloperSettings>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    IncludeFields = true
                });
                if (loaded != null)
                {
                    NarrativeLog.Log($"NarrativeCombatDeveloperSettings 已从 {jsonFilePath} 加载。");
                }
                return loaded;
            }
            catch (System.Exception ex)
            {
                NarrativeLog.LogWarning($"加载战斗设置 '{jsonFilePath}' 失败，改用默认值: {ex.Message}");
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
                NarrativeLog.LogWarning($"保存战斗设置 '{jsonFilePath}' 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定难度下的攻击令牌数量。
        /// 对应 UE5 GetAttackTokensForDifficulty。
        /// </summary>
        public int GetAttackTokensForDifficulty(ENarrativeGameplayDifficulty Difficulty)
        {
            if (AvailableAttackTokens != null && AvailableAttackTokens.TryGetValue(Difficulty, out var tokens))
                return tokens;
            NarrativeLog.LogWarning($"未配置难度 {Difficulty} 的攻击令牌数量，返回 0。");
            return 0;
        }

        /// <summary>
        /// 获取指定难度下的 AI 攻击频率倍率。
        /// 对应 UE5 GetAttackFrequencyForDifficulty。
        /// </summary>
        public float GetAttackFrequencyForDifficulty(ENarrativeGameplayDifficulty Difficulty)
        {
            if (NPCAttackFrequencies != null && NPCAttackFrequencies.TryGetValue(Difficulty, out var freq))
                return freq;
            NarrativeLog.LogWarning($"未配置难度 {Difficulty} 的 AI 攻击频率，返回 1.0。");
            return 1.0f;
        }
    }
}

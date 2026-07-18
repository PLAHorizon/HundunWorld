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

        /// <summary>单例实例。</summary>
        public static NarrativeCombatDeveloperSettings Instance { get; set; } = LoadDefault();

        private static NarrativeCombatDeveloperSettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax 引擎配置或 JSON 文件加载持久化设置。暂时返回默认实例。
            var settings = new NarrativeCombatDeveloperSettings();
            NarrativeLog.Log("NarrativeCombatDeveloperSettings 已使用默认值初始化。");
            return settings;
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

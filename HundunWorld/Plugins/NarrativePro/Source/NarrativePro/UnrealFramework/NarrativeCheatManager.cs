using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 作弊管理器。对应 UE5 UNarrativeCheatManager。
    /// UE5 中继承 UCheatManager；Flax 无 CheatManager 基类，改为普通 [Serializable] class，方法用 public virtual。
    /// 包含 Narrative Pro 的控制台命令，这些命令在打包游戏中会被剥离。
    /// 简化点：
    /// - 移除 UE5 UFUNCTION(Exec)，改为普通方法（Flax-不兼容: UE5 Exec 机制在 Flax 无对应物，保留占位）
    /// - 通过 TargetCharacter 指定目标玩家角色
    /// </summary>
    [Serializable]
    public class NarrativeCheatManager
    {
        /// <summary>当前目标玩家控制器（用于获取玩家角色/PlayerState 等）。</summary>
        [NonSerialized]
        public NarrativePlayerController TargetController;

        /// <summary>当前目标玩家角色。</summary>
        [NonSerialized]
        public NarrativePlayerCharacter TargetCharacter;

        /// <summary>初始化作弊管理器。对应 UE5 InitCheats。</summary>
        public virtual void InitCheats(NarrativePlayerController inController)
        {
            TargetController = inController;
            TargetCharacter = inController?.GetOwnedCharacter();
        }

        // ===== 技能点 / 货币 =====

        /// <summary>给予玩家指定数量的技能点。对应 UE5 GiveSkillPoints(Points=1)。</summary>
        /// <param name="points">要给予的技能点数。</param>
        public virtual void GiveSkillPoints(int points = 1)
        {
            if (TargetCharacter == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] GiveSkillPoints: 无目标角色");
                return;
            }
            // TODO [需接入 SkillTreeComponent 系统]: 通过 SkillTreeComponent 增加技能点
            NarrativeLog.Log($"[NarrativeCheatManager] GiveSkillPoints: +{points}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }

        /// <summary>给予玩家指定数量的货币。对应 UE5 GiveCurrency(Currency=1)。</summary>
        /// <param name="currency">要给予的货币数量。</param>
        public virtual void GiveCurrency(int currency = 1)
        {
            if (TargetCharacter == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] GiveCurrency: 无目标角色");
                return;
            }
            // TODO [需接入 InventoryComponent 系统]: 通过 InventoryComponent 增加货币
            NarrativeLog.Log($"[NarrativeCheatManager] GiveCurrency: +{currency}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }

        // ===== 无敌 =====

        /// <summary>设置角色是否无敌。对应 UE5 SetInvulnerable(bIsInvulnerable)。</summary>
        public virtual void SetInvulnerable(bool bIsInvulnerable)
        {
            if (TargetCharacter == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] SetInvulnerable: 无目标角色");
                return;
            }
            // TODO [需接入 ASC 系统]: 通过 ASC 应用/移除无敌 GameplayEffect
            NarrativeLog.Log($"[NarrativeCheatManager] SetInvulnerable = {bIsInvulnerable}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }

        // ===== 时间 =====

        /// <summary>推进游戏内时间。对应 UE5 AdvanceTime(Amount)，100 = 1 小时。</summary>
        /// <param name="amount">推进的时间量（100 = 1 小时）。</param>
        public virtual void AdvanceTime(float amount)
        {
            // TODO [需接入 TimeOfDay 系统]: 通过 TimeOfDay 子系统推进时间
            NarrativeLog.Log($"[NarrativeCheatManager] AdvanceTime: +{amount}");
        }

        /// <summary>推进游戏内时间至指定时刻。对应 UE5 AdvanceToTime(Time)。</summary>
        /// <param name="time">目标时间（小时）。</param>
        public virtual void AdvanceToTime(float time)
        {
            // TODO [需接入 TimeOfDay 系统]: 通过 TimeOfDay 子系统推进到目标时间
            NarrativeLog.Log($"[NarrativeCheatManager] AdvanceToTime: {time}");
        }

        // ===== GameplayTag =====

        /// <summary>为角色添加 GameplayTag。对应 UE5 AddGameplayTag(TagName)。</summary>
        /// <param name="tagName">标签名（如 "Narrative.State.Cheated"）。</param>
        public virtual void AddGameplayTag(string tagName)
        {
            if (TargetCharacter == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] AddGameplayTag: 无目标角色");
                return;
            }
            var tag = new GameplayTag(tagName);
            var asc = TargetCharacter.GetAbilitySystemComponent();
            // TODO [需接入 ASC 系统]: 通过 ASC 添加标签
            NarrativeLog.Log($"[NarrativeCheatManager] AddGameplayTag: {tagName}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }

        /// <summary>从角色移除 GameplayTag。对应 UE5 RemoveGameplayTag(TagName)。</summary>
        /// <param name="tagName">标签名。</param>
        public virtual void RemoveGameplayTag(string tagName)
        {
            if (TargetCharacter == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] RemoveGameplayTag: 无目标角色");
                return;
            }
            var tag = new GameplayTag(tagName);
            var asc = TargetCharacter.GetAbilitySystemComponent();
            // TODO [需接入 ASC 系统]: 通过 ASC 移除标签
            NarrativeLog.Log($"[NarrativeCheatManager] RemoveGameplayTag: {tagName}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;
using NarrativePro.SkillTrees;

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

        /// <summary>无敌作弊的动态标签效果句柄。对应 UE5 的 GodMode GameplayEffect。</summary>
        [NonSerialized]
        private ActiveGameplayEffectHandle _invulnerableHandle = ActiveGameplayEffectHandle.Invalid;

        /// <summary>作弊添加的松散标签 → 动态标签效果句柄。对应 UE5 AddLooseGameplayTag。</summary>
        [NonSerialized]
        private readonly Dictionary<string, ActiveGameplayEffectHandle> _cheatTagEffects = new Dictionary<string, ActiveGameplayEffectHandle>(StringComparer.Ordinal);

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
            // Flax-已实现: 委托给 PlayerState 上的 SkillTreeComponent（对应 UE5 GetPlayerState()->SkillTreeComponent->GiveSkillPoints）
            var skillTree = TargetCharacter.GetNarrativePlayerState()?.GetSkillTreeComponent();
            if (skillTree != null)
            {
                skillTree.GiveSkillPoints(points);
            }
            else
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] GiveSkillPoints: 目标角色无 PlayerState/SkillTreeComponent");
            }
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
            // Flax-已实现: 通过 InventoryComponent.AddCurrency 增加货币（对应 UE5 Inventory->AddCurrency）
            var inventory = TargetCharacter.GetInventoryComponent();
            if (inventory != null)
            {
                inventory.AddCurrency(currency);
            }
            else
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] GiveCurrency: 目标角色无 InventoryComponent");
            }
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
            // Flax-已实现: 通过 ASC 动态标签效果应用/移除无敌（对应 UE5 GodMode GE 的 GrantedTags 路径）
            var asc = TargetCharacter.GetAbilitySystemComponent();
            if (asc == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] SetInvulnerable: 目标角色无 ASC");
                return;
            }
            if (bIsInvulnerable)
            {
                if (_invulnerableHandle.IsValid) return;
                var tags = new GameplayTagContainer();
                tags.AddTag(new GameplayTag("Status.Invulnerable"));
                _invulnerableHandle = asc.AddDynamicTagsGameplayEffect(tags);
            }
            else
            {
                if (_invulnerableHandle.IsValid)
                {
                    asc.RemoveActiveEffect(_invulnerableHandle);
                    _invulnerableHandle = ActiveGameplayEffectHandle.Invalid;
                }
            }
            NarrativeLog.Log($"[NarrativeCheatManager] SetInvulnerable = {bIsInvulnerable}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }

        // ===== 时间 =====

        /// <summary>推进游戏内时间。对应 UE5 AdvanceTime(Amount)，100 = 1 小时。</summary>
        /// <param name="amount">推进的时间量（100 = 1 小时）。</param>
        public virtual void AdvanceTime(float amount)
        {
            // Flax-已实现: 委托 NarrativeGameState.AdvanceTimeOfDay（对应 UE5 GetWorldState()->AdvanceTimeOfDay）
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState != null)
            {
                gameState.AdvanceTimeOfDay(amount);
            }
            else
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] AdvanceTime: 场景中无 NarrativeGameState");
            }
            NarrativeLog.Log($"[NarrativeCheatManager] AdvanceTime: +{amount}");
        }

        /// <summary>推进游戏内时间至指定时刻。对应 UE5 AdvanceToTime(Time)。</summary>
        /// <param name="time">目标时间（小时）。</param>
        public virtual void AdvanceToTime(float time)
        {
            // Flax-已实现: 委托 NarrativeGameState.AdvanceToTimeOfDay（对应 UE5 GetWorldState()->AdvanceToTimeOfDay）
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState != null)
            {
                gameState.AdvanceToTimeOfDay(time);
            }
            else
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] AdvanceToTime: 场景中无 NarrativeGameState");
            }
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
            // Flax-已实现: 通过动态标签 GameplayEffect 添加松散标签（对应 UE5 AddLooseGameplayTag）
            if (asc == null || string.IsNullOrEmpty(tagName) || _cheatTagEffects.ContainsKey(tagName))
            {
                if (asc == null) NarrativeLog.LogWarning("[NarrativeCheatManager] AddGameplayTag: 目标角色无 ASC");
                return;
            }
            var tags = new GameplayTagContainer();
            tags.AddTag(tag);
            _cheatTagEffects[tagName] = asc.AddDynamicTagsGameplayEffect(tags);
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
            // Flax-已实现: 移除对应的动态标签效果（对应 UE5 RemoveLooseGameplayTag）
            if (asc == null)
            {
                NarrativeLog.LogWarning("[NarrativeCheatManager] RemoveGameplayTag: 目标角色无 ASC");
                return;
            }
            if (!string.IsNullOrEmpty(tagName) && _cheatTagEffects.Remove(tagName, out var handle))
            {
                asc.RemoveActiveEffect(handle);
            }
            NarrativeLog.Log($"[NarrativeCheatManager] RemoveGameplayTag: {tagName}（目标: {TargetCharacter.Actor?.Name ?? "Unknown"}）");
        }
    }
}

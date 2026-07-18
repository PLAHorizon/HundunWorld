using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;
using NarrativePro.SkillTrees;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 玩家状态。对应 UE5 ANarrativePlayerState。
    /// UE5 中继承 APlayerState；Flax 无 PlayerState 基类，改为 Script。
    /// 通常挂载到玩家角色 Actor 上，存储跨生命周期的玩家数据（阵营、技能树、属性等）。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_Faction、OnRep_PlayerName 等），改为本地逻辑 + 事件回调
    /// - IAbilitySystemInterface/INarrativeTeamAgentInterface/INarrativeCharacterOwner 通过委托实现
    /// </summary>
    public class NarrativePlayerState : Script, INarrativeCharacterOwner
    {
        // ===== 配置字段 =====

        /// <summary>玩家阵营（SaveGame）。对应 UE5 Factions。</summary>
        public GameplayTagContainer Factions = new GameplayTagContainer();

        // ===== 运行时引用 =====

        /// <summary>能力系统组件。</summary>
        [NonSerialized]
        protected NarrativeAbilitySystemComponent AbilitySystemComponent;

        /// <summary>属性集基类。</summary>
        [NonSerialized]
        protected NarrativeAttributeSetBase AttributeSetBase;

        /// <summary>技能树组件。</summary>
        [NonSerialized]
        protected SkillTreeComponent SkillTreeComponent;

        /// <summary>关联的玩家角色。</summary>
        [NonSerialized]
        protected NarrativePlayerCharacter OwnerCharacter;

        // ===== 标签 =====

        /// <summary>死亡标签。</summary>
        public GameplayTag DeadTag = new GameplayTag("Narrative.State.IsDead");

        /// <summary>死亡时移除效果标签。</summary>
        public GameplayTag EffectRemoveOnDeathTag = new GameplayTag("Narrative.State.RemoveOnDeath");

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 查找子组件
            AbilitySystemComponent = Actor.GetScript<NarrativeAbilitySystemComponent>();
            AttributeSetBase = Actor.GetScript<NarrativeAttributeSetBase>();
            SkillTreeComponent = Actor.GetScript<SkillTreeComponent>();
            OwnerCharacter = Actor.GetScript<NarrativePlayerCharacter>();

            // 反向注册到角色
            if (OwnerCharacter != null)
            {
                OwnerCharacter.SetCachedPlayerState(this);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        // ===== INarrativeCharacterOwner 实现 =====

        /// <summary>返回与此 PlayerState 关联的 NarrativeCharacter。</summary>
        public virtual NarrativeCharacter GetNarrativeCharacter() => OwnerCharacter;

        // ===== IAbilitySystemInterface 等价 =====

        /// <summary>获取能力系统组件。对应 UE5 GetAbilitySystemComponent。</summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent() => AbilitySystemComponent;

        /// <summary>获取属性集基类。对应 UE5 GetAttributeSetBase。</summary>
        public virtual NarrativeAttributeSetBase GetAttributeSetBase() => AttributeSetBase;

        // ===== INarrativeTeamAgentInterface 等价 =====

        /// <summary>获取阵营。对应 UE5 GetFactions。</summary>
        public virtual GameplayTagContainer GetFactions() => Factions;

        /// <summary>添加阵营。对应 UE5 AddFaction。</summary>
        public virtual void AddFaction(GameplayTag faction)
        {
            if (faction.IsValid())
            {
                Factions.AddTag(faction);
                OnFactionChanged();
            }
        }

        /// <summary>移除阵营。对应 UE5 RemoveFaction。</summary>
        public virtual void RemoveFaction(GameplayTag faction)
        {
            if (faction.IsValid())
            {
                Factions.RemoveTag(faction);
                OnFactionChanged();
            }
        }

        /// <summary>设置阵营。对应 UE5 SetFactions。</summary>
        public virtual void SetFactions(GameplayTagContainer newFactions)
        {
            Factions = newFactions ?? new GameplayTagContainer();
            OnFactionChanged();
        }

        /// <summary>阵营改变时的本地处理。对应 UE5 OnRep_Faction。</summary>
        protected virtual void OnFactionChanged()
        {
            // 通知角色刷新阵营
            if (OwnerCharacter != null)
            {
                // OwnerCharacter.OnFactionUpdated?.Invoke();
            }
        }

        // ===== 生命/属性查询 =====

        /// <summary>是否存活。对应 UE5 IsAlive。</summary>
        public virtual bool IsAlive()
        {
            if (AbilitySystemComponent == null) return true;
            return !AbilitySystemComponent.IsDead;
        }

        /// <summary>获取当前生命值。对应 UE5 GetHealth。</summary>
        public virtual float GetHealth()
        {
            return AttributeSetBase?.Health.CurrentValue ?? 0f;
        }

        // ===== 玩家名 =====

        /// <summary>玩家名改变时的本地处理。对应 UE5 OnRep_PlayerName。</summary>
        protected virtual void OnPlayerNameChanged()
        {
            // TODO [需接入 UI 系统]: 通知 UI 更新
        }

        // ===== 技能树 =====

        /// <summary>获取技能树组件。对应 UE5 GetSkillTreeComponent。</summary>
        public virtual SkillTreeComponent GetSkillTreeComponent() => SkillTreeComponent;

        // ===== 关联角色管理 =====

        /// <summary>设置关联的玩家角色（供 PlayerController/GameMode 调用）。</summary>
        public virtual void SetOwnerCharacter(NarrativePlayerCharacter character)
        {
            OwnerCharacter = character;
        }
    }
}

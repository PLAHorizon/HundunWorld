using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// Narrativo Pro 自定义能力基类。对应 UE5 UNarrativeGameplayAbility。
    /// 简化点：
    /// - 移除 UE5 输入系统（Enhanced Input）改为 InputTag 配置
    /// - InputAction 用字符串路径占位（TODO [需接入 Flax 输入系统]: 接入 Flax InputEvent 配置）
    /// - 移除网络预测/Local/Remote 区分
    /// - 移除 RPC，改为本地逻辑
    /// - Cost/Cooldown 通过内联配置 + GameplayEffect 管线实现
    /// </summary>
    public class NarrativeGameplayAbility : Script
    {
        // ===== 配置字段 =====

        /// <summary>能力名称。</summary>
        public string AbilityName = "";

        /// <summary>输入标签（绑定到具体输入，可空）。</summary>
        public GameplayTag InputTag = GameplayTag.None;

        /// <summary>能力激活时授予自身的标签。</summary>
        public GameplayTagContainer AbilityTags = new GameplayTagContainer();

        /// <summary>激活此能力所需拥有的标签（必须有）。</summary>
        public GameplayTagContainer ActivationRequiredTags = new GameplayTagContainer();

        /// <summary>激活此能力所需阻断的标签（如果有则不能激活）。</summary>
        public GameplayTagContainer ActivationBlockedTags = new GameplayTagContainer();

        /// <summary>Cost 效果路径（如消耗耐力）。若配置了资产加载系统，优先使用此路径加载 GameplayEffect。</summary>
        public string CostGameplayEffectPath = "";

        /// <summary>Cost 属性名（内联配置，如 "Stamina"、"Health"）。当 CostGameplayEffectPath 为空时使用。</summary>
        public string CostAttributeName = "Stamina";

        /// <summary>Cost 消耗量（正数表示消耗量）。当 CostGameplayEffectPath 为空时使用。</summary>
        public float CostAmount = 0f;

        /// <summary>Cooldown 效果路径。若配置了资产加载系统，优先使用此路径加载 Cooldown GameplayEffect。</summary>
        public string CooldownGameplayEffectPath = "";

        /// <summary>Cooldown 持续时间（秒）。</summary>
        public float CooldownDuration = 0f;

        /// <summary>Cooldown 标签（可选）。激活时授予此标签，标签存在期间能力不可再次激活。与 CooldownDuration 配合使用。</summary>
        public GameplayTag CooldownTag = GameplayTag.None;

        /// <summary>能力等级。</summary>
        public float AbilityLevel = 1f;

        // ===== 运行时状态 =====

        /// <summary>拥有此能力的 ASC。</summary>
        [NonSerialized]
        public NarrativeAbilitySystemComponent OwningASC;

        /// <summary>当前激活的句柄。</summary>
        protected GameplayAbilitySpecHandle _currentHandle = GameplayAbilitySpecHandle.Invalid;

        /// <summary>是否已激活。</summary>
        public bool bIsActive => _currentHandle.IsValid && OwningASC?.FindAbilitySpec(_currentHandle)?.bIsActive == true;

        /// <summary>剩余 Cooldown 时间（秒，0 表示可激活）。</summary>
        public float CooldownRemaining = 0f;

        /// <summary>Cooldown 开始时间。</summary>
        public float CooldownStartTime = 0f;

        // ===== 激活流程 =====

        /// <summary>检查能力是否可激活（Cost/Cooldown/Tag 检查）。</summary>
        public virtual bool CanActivateAbility(NarrativeAbilitySystemComponent asc)
        {
            if (asc == null) return false;
            if (asc.IsDead) return false;

            // Cooldown 检查（计时器方式）
            if (CooldownRemaining > 0f) return false;

            // Cooldown 标签检查（标签方式，若 ASC 仍持有 CooldownTag 则不可激活）
            if (CooldownTag.IsValid() && asc.HasMatchingGameplayTag(CooldownTag)) return false;

            // 必需标签检查
            if (ActivationRequiredTags != null)
            {
                foreach (var tag in ActivationRequiredTags.GetTags())
                {
                    if (!asc.HasMatchingGameplayTag(new GameplayTag(tag))) return false;
                }
            }

            // 阻断标签检查
            if (ActivationBlockedTags != null)
            {
                foreach (var tag in ActivationBlockedTags.GetTags())
                {
                    if (asc.HasMatchingGameplayTag(new GameplayTag(tag))) return false;
                }
            }

            // Cost 检查：验证属性是否足够支付消耗
            if (!CanAffordCost(asc)) return false;

            return true;
        }

        /// <summary>检查 ASC 是否有足够属性支付 Cost。</summary>
        protected virtual bool CanAffordCost(NarrativeAbilitySystemComponent asc)
        {
            if (CostAmount <= 0f) return true; // 无消耗
            if (asc.AttributeSet == null) return false;

            var attr = asc.AttributeSet.GetAttribute(CostAttributeName);
            if (attr == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[GAS] Cost 属性 '{CostAttributeName}' 不存在，跳过 Cost 检查");
                return true;
            }

            return attr.CurrentValue >= CostAmount;
        }

        /// <summary>激活能力。由 ASC 调用。</summary>
        public virtual void ActivateAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            _currentHandle = handle;

            // 应用 Cooldown（计时器 + 可选标签）
            if (CooldownDuration > 0f)
            {
                CooldownStartTime = Time.GameTime;
                CooldownRemaining = CooldownDuration;

                // 通过 GameplayEffect 授予 CooldownTag（持续期 = CooldownDuration）
                if (CooldownTag.IsValid() && OwningASC != null)
                {
                    ApplyCooldownEffect();
                }
            }

            // 授予 AbilityTags
            if (OwningASC != null && AbilityTags != null)
            {
                OwningASC.AddDynamicTagsGameplayEffect(AbilityTags);
            }

            // 应用 Cost：扣除属性消耗
            ApplyCost();
        }

        /// <summary>应用 Cost 消耗（通过 ASC 的 GameplayEffect 管线扣除属性）。</summary>
        protected virtual void ApplyCost()
        {
            if (CostAmount <= 0f || OwningASC == null) return;

            // 构建一个 Instant 类型的 GameplayEffect 来扣除属性
            var costEffect = new GameplayEffect($"Cost_{AbilityName}")
            {
                DurationType = EGameplayEffectDurationType.Instant,
                Modifiers = new List<GameplayModifierInfo>
                {
                    new GameplayModifierInfo
                    {
                        AttributeName = CostAttributeName,
                        ModifierOp = EGameplayModOp.Add,
                        Magnitude = -CostAmount // 负值表示消耗
                    }
                }
            };

            var spec = new GameplayEffectSpec(costEffect, OwningASC, AbilityLevel);
            OwningASC.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
        }

        /// <summary>应用 Cooldown 效果（通过 Duration 类型 GameplayEffect 授予 CooldownTag）。</summary>
        protected virtual void ApplyCooldownEffect()
        {
            if (OwningASC == null || !CooldownTag.IsValid()) return;

            var cooldownEffect = new GameplayEffect($"Cooldown_{AbilityName}")
            {
                DurationType = EGameplayEffectDurationType.Duration,
                Duration = CooldownDuration,
                GrantedTags = new GameplayTagContainer(new[] { CooldownTag.TagName })
            };

            var spec = new GameplayEffectSpec(cooldownEffect, OwningASC, AbilityLevel);
            OwningASC.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
        }

        /// <summary>结束能力（由能力本身调用）。</summary>
        public virtual void EndAbility()
        {
            if (OwningASC != null && _currentHandle.IsValid)
            {
                OwningASC.EndAbility(_currentHandle, false);
            }
            _currentHandle = GameplayAbilitySpecHandle.Invalid;
        }

        /// <summary>取消能力（由外部调用）。</summary>
        public virtual void CancelAbility()
        {
            if (OwningASC != null && _currentHandle.IsValid)
            {
                OwningASC.EndAbility(_currentHandle, true);
            }
            _currentHandle = GameplayAbilitySpecHandle.Invalid;
        }

        /// <summary>能力被授予时调用（GiveAbility 中触发）。</summary>
        public virtual void OnAbilityGranted() { }

        // ===== 输入处理 =====

        /// <summary>输入标签按下。</summary>
        public virtual void InputTagPressed(GameplayTag inputTag) { }

        /// <summary>输入标签释放。</summary>
        public virtual void InputTagReleased(GameplayTag inputTag) { }

        // ===== 输入映射（简化版） =====

        /// <summary>当前输入动作路径（Flax 输入系统占位）。</summary>
        public string CurrentInputActionPath = "";

        /// <summary>获取当前输入动作路径。</summary>
        public virtual string GetCurrentInputActionPath() => CurrentInputActionPath;

        /// <summary>从输入标签获取输入动作路径（占位，TODO [需接入 Flax 输入系统]: 接入 Flax InputEvent 配置）。</summary>
        public virtual string GetInputActionPathFromTag(GameplayTag inputTag)
        {
            // TODO [需接入 Flax 输入系统]: 接入 Flax InputEvent 配置
            return "";
        }

        /// <summary>从输入动作路径获取输入标签（占位）。</summary>
        public virtual GameplayTag GetInputTagFromActionPath(string actionPath)
        {
            // TODO [需接入 Flax 输入系统]: 反向查询 InputMapping
            return GameplayTag.None;
        }

        /// <summary>获取能力输入映射资产路径。</summary>
        public virtual string GetAbilityInputMappingPath() => "";

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            if (OwningASC == null)
            {
                OwningASC = Actor.GetScript<NarrativeAbilitySystemComponent>();
            }
        }

        public override void OnDisable()
        {
            if (bIsActive)
            {
                CancelAbility();
            }
            base.OnDisable();
        }

        /// <summary>每帧更新 Cooldown。</summary>
        public override void OnUpdate()
        {
            if (CooldownRemaining > 0f)
            {
                CooldownRemaining -= Time.DeltaTime;
                if (CooldownRemaining <= 0f)
                {
                    CooldownRemaining = 0f;
                    CooldownStartTime = 0f;
                }
            }
        }
    }
}

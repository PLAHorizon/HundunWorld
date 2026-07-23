using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 死亡事件委托。对应 UE5 FOnDied。
    /// </summary>
    /// <param name="killedActor">死亡的 Actor。</param>
    /// <param name="killedASC">死亡的 ASC。</param>
    public delegate void OnDiedDelegate(FlaxEngine.Actor killedActor, NarrativeAbilitySystemComponent killedASC);

    /// <summary>
    /// 治疗事件委托。对应 UE5 FOnHealedBy。
    /// </summary>
    /// <param name="healer">治疗者 ASC。</param>
    /// <param name="amount">治疗量。</param>
    public delegate void OnHealedByDelegate(NarrativeAbilitySystemComponent healer, float amount, GameplayEffectSpec spec);

    /// <summary>
    /// 受到伤害事件委托。对应 UE5 FOnDamagedBy。
    /// </summary>
    /// <param name="damagerASC">伤害源 ASC。</param>
    /// <param name="damage">伤害量。</param>
    /// <param name="spec">效果规格。</param>
    public delegate void OnDamagedByDelegate(NarrativeAbilitySystemComponent damagerASC, float damage, GameplayEffectSpec spec);

    /// <summary>
    /// 造成伤害事件委托。对应 UE5 FOnDealtDamage。
    /// </summary>
    /// <param name="damagedASC">被伤害 ASC。</param>
    /// <param name="damage">伤害量。</param>
    /// <param name="spec">效果规格。</param>
    public delegate void OnDealtDamageDelegate(NarrativeAbilitySystemComponent damagedASC, float damage, GameplayEffectSpec spec);

    /// <summary>
    /// 能力规格。对应 UE5 FGameplayAbilitySpec。
    /// 一个能力在 ASC 上的实例化数据。
    /// </summary>
    [Serializable]
    public class GameplayAbilitySpec
    {
        /// <summary>能力实例。</summary>
        public NarrativeGameplayAbility Ability;

        /// <summary>能力等级。</summary>
        public float Level = 1f;

        /// <summary>输入标签（绑定到具体输入）。</summary>
        public GameplayTag InputTag = GameplayTag.None;

        /// <summary>句柄 ID（每个 spec 唯一）。</summary>
        public int HandleId = 0;

        /// <summary>是否已激活。</summary>
        public bool bIsActive = false;

        public GameplayAbilitySpec() { }

        public GameplayAbilitySpec(NarrativeGameplayAbility ability, float level = 1f)
        {
            Ability = ability;
            Level = level;
        }
    }

    /// <summary>
    /// 能力规格句柄。对应 UE5 FGameplayAbilitySpecHandle。
    /// </summary>
    [Serializable]
    public struct GameplayAbilitySpecHandle
    {
        public int HandleId;

        public bool IsValid => HandleId != 0;

        public GameplayAbilitySpecHandle(int id) { HandleId = id; }

        public static readonly GameplayAbilitySpecHandle Invalid = new GameplayAbilitySpecHandle(0);
    }

    /// <summary>
    /// Narrativo Pro 能力系统组件。对应 UE5 UNarrativeAbilitySystemComponent。
    /// 管理角色能力、属性、效果、标签，是 GAS 的核心。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（改为本地逻辑）
    /// - 移除网络预测/Local/Remote 区分
    /// - INarrativeSavableComponent 接口通过 PrepareForSave/Load 方法支持（TODO [需接入存档系统]: 实现序列化保存/恢复）
    /// - FGameplayAbilityTargetData 简化为基础结构
    /// - 伤害/治疗元属性通过 NarrativeAttributeSetBase.PostGameplayEffectExecute 应用
    /// </summary>
    public class NarrativeAbilitySystemComponent : Script
    {
        // ===== 配置字段 =====

        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesEffectPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        /// <summary>能力等级。</summary>
        public int Level = 1;

        // ===== 运行时状态 =====

        /// <summary>属性集合（运行时实例）。</summary>
        public NarrativeAttributeSetBase AttributeSet;

        /// <summary>所有已授予的能力规格。</summary>
        public List<GameplayAbilitySpec> GrantedAbilities = new List<GameplayAbilitySpec>();

        /// <summary>所有激活的游戏效果。</summary>
        public List<ActiveGameplayEffect> ActiveEffects = new List<ActiveGameplayEffect>();

        /// <summary>拥有的标签容器（含效果授予的 + 基础）。</summary>
        public GameplayTagContainer GameplayTags = new GameplayTagContainer();

        // ===== 事件 =====

        /// <summary>死亡事件。在 server 和所有客户端触发。</summary>
        public event OnDiedDelegate OnDied;

        /// <summary>被治疗事件。</summary>
        public event OnHealedByDelegate OnHealedBy;

        /// <summary>受到伤害事件。</summary>
        public event OnDamagedByDelegate OnDamagedBy;

        /// <summary>造成伤害事件。</summary>
        public event OnDealtDamageDelegate OnDealtDamage;

        // ===== 内部 =====

        private int _nextEffectHandleId = 1;
        private int _nextAbilityHandleId = 1;
        private bool _bIsDead = false;

        /// <summary>是否已死亡。</summary>
        public bool IsDead => _bIsDead;

        /// <summary>获取 Avatar Actor（UE 不暴露到 BP）。</summary>
        public FlaxEngine.Actor GetAvatarOwner() => Actor;

        /// <summary>获取属性集合。</summary>
        public NarrativeAttributeSetBase GetAttributeSet() => AttributeSet;

        /// <summary>初始化属性集合（如果未设置）。</summary>
        public virtual void InitializeAttributes()
        {
            if (AttributeSet == null)
            {
                AttributeSet = Actor.GetScript<NarrativeAttributeSetBase>();
                if (AttributeSet == null)
                {
                    // Flax 中 Script 不能 AddChild，需用 AddScript 添加到 Actor
                    AttributeSet = Actor.AddScript<NarrativeAttributeSetBase>();
                }
            }

            // 应用默认属性效果（设置 BaseValue）
            if (!string.IsNullOrEmpty(DefaultAttributesEffectPath))
            {
                // TODO [需接入 GameplayEffect 资产加载机制]: 从路径加载 GameplayEffect 资产并应用
                NarrativePro.Core.NarrativeLog.LogWarning("DefaultAttributesEffectPath 加载待实现: " + DefaultAttributesEffectPath);
            }

            // 应用启动效果
            if (StartupEffectPaths != null)
            {
                foreach (var path in StartupEffectPaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        // TODO [需接入 GameplayEffect 资产加载机制]: 加载并应用 StartupEffect
                        NarrativePro.Core.NarrativeLog.LogWarning("StartupEffectPath 加载待实现: " + path);
                    }
                }
            }

            // 初始化基础属性
            if (AttributeSet != null)
            {
                AttributeSet.Health.SetCurrentValue(AttributeSet.MaxHealth.CurrentValue);
                AttributeSet.Stamina.SetCurrentValue(AttributeSet.MaxStamina.CurrentValue);
            }
        }

        /// <summary>授予能力。</summary>
        /// <param name="ability">能力实例。</param>
        /// <param name="level">等级。</param>
        /// <param name="inputTag">输入标签（可空）。</param>
        /// <returns>能力规格句柄。</returns>
        public virtual GameplayAbilitySpecHandle GiveAbility(NarrativeGameplayAbility ability, float level = 1f, GameplayTag inputTag = default)
        {
            if (ability == null) return GameplayAbilitySpecHandle.Invalid;

            var spec = new GameplayAbilitySpec(ability, level)
            {
                HandleId = _nextAbilityHandleId++,
                InputTag = inputTag.IsValid() ? inputTag : GameplayTag.None
            };
            GrantedAbilities.Add(spec);
            ability.OwningASC = this;
            ability.OnAbilityGranted();
            return new GameplayAbilitySpecHandle(spec.HandleId);
        }

        /// <summary>通过句柄查找能力规格。</summary>
        public virtual GameplayAbilitySpec FindAbilitySpec(GameplayAbilitySpecHandle handle)
        {
            if (!handle.IsValid) return null;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.HandleId == handle.HandleId) return spec;
            }
            return null;
        }

        /// <summary>查找绑定到指定输入标签的所有能力规格。</summary>
        public virtual void FindAbilitiesWithTag(GameplayTag inputTag, List<GameplayAbilitySpecHandle> outAbilitySpecs)
        {
            if (outAbilitySpecs == null) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag)
                {
                    outAbilitySpecs.Add(new GameplayAbilitySpecHandle(spec.HandleId));
                }
            }
        }

        /// <summary>通过句柄激活能力。</summary>
        public virtual bool TryActivateAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return false;
            if (spec.bIsActive) return false;

            // 检查能力是否可激活（Cost/Cooldown/Tag 检查，Cost 通过 Ability.CanAffordCost 验证）
            if (!spec.Ability.CanActivateAbility(this))
            {
                return false;
            }

            spec.bIsActive = true;
            spec.Ability.OwningASC = this;
            spec.Ability.ActivateAbility(handle, spec);
            return true;
        }

        // （GameplayAbilitySpecHandle.IsValid 是属性，不是方法，下文使用时不应加括号）

        /// <summary>通过输入标签激活能力。</summary>
        public virtual void AbilityInputTagPressed(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && !spec.bIsActive)
                {
                    var handle = new GameplayAbilitySpecHandle(spec.HandleId);
                    if (handle.IsValid)
                    {
                        TryActivateAbility(handle);
                    }
                }
            }
        }

        /// <summary>通过输入标签释放能力。</summary>
        public virtual void AbilityInputTagReleased(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.bIsActive && spec.Ability != null)
                {
                    spec.Ability.InputTagReleased(inputTag);
                }
            }
        }

        /// <summary>取消能力。</summary>
        public virtual void CancelAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return;
            if (!spec.bIsActive) return;

            spec.bIsActive = false;
            spec.Ability.CancelAbility();
        }

        /// <summary>结束能力（由能力本身调用）。</summary>
        public virtual void EndAbility(GameplayAbilitySpecHandle handle, bool bWasCancelled = false)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null) return;
            spec.bIsActive = false;
        }

        // ===== 效果应用 =====

        /// <summary>应用 GameplayEffect 到自身。</summary>
        /// <param name="specHandle">效果规格句柄。</param>
        /// <returns>激活效果句柄（Instant 类型返回 Invalid）。</returns>
        public virtual ActiveGameplayEffectHandle ApplyGameplayEffectSpecToSelf(GameplayEffectSpecHandle specHandle)
        {
            if (!specHandle.IsValid) return ActiveGameplayEffectHandle.Invalid;
            var spec = specHandle.Spec;
            var effect = spec.Effect;
            if (effect == null) return ActiveGameplayEffectHandle.Invalid;

            // 检查 RequiredTags
            if (effect.RequiredTags != null)
            {
                foreach (var tag in effect.RequiredTags.GetTags())
                {
                    if (!GameplayTags.HasTag(tag))
                    {
                        // 缺少必需标签，不应用
                        return ActiveGameplayEffectHandle.Invalid;
                    }
                }
            }

            // 应用修饰器
            if (AttributeSet != null && effect.Modifiers != null)
            {
                foreach (var mod in effect.Modifiers)
                {
                    ApplyModifier(AttributeSet, mod, spec);
                }
            }

            // 应用元属性处理
            if (AttributeSet != null)
            {
                AttributeSet.PostGameplayEffectExecute();
            }

            // 应用授予标签
            if (effect.GrantedTags != null)
            {
                foreach (var tag in effect.GrantedTags.GetTags())
                {
                    GameplayTags.AddTag(new GameplayTag(tag));
                }
            }

            // 触发伤害/治疗事件
            if (AttributeSet != null)
            {
                if (AttributeSet.Damage.CurrentValue > 0f)
                {
                    OnDamagedBy?.Invoke(spec.SourceASC, AttributeSet.Damage.CurrentValue, spec);
                }
                if (AttributeSet.Heal.CurrentValue > 0f)
                {
                    OnHealedBy?.Invoke(spec.SourceASC, AttributeSet.Heal.CurrentValue, spec);
                }
            }

            // 检查死亡
            if (!_bIsDead && AttributeSet != null && AttributeSet.Health.CurrentValue <= 0f)
            {
                _bIsDead = true;
                OnDied?.Invoke(Actor, this);
            }

            // 持续型效果加入 ActiveEffects
            if (effect.DurationType == EGameplayEffectDurationType.Duration ||
                effect.DurationType == EGameplayEffectDurationType.Infinite)
            {
                var active = new ActiveGameplayEffect(effect)
                {
                    HandleId = _nextEffectHandleId++,
                    OwnerASC = this,
                    StartTime = Time.GameTime,
                    RemainingDuration = effect.DurationType == EGameplayEffectDurationType.Duration ? effect.Duration : float.MaxValue,
                    NextPeriodTime = Time.GameTime + (effect.Period > 0f ? effect.Period : float.MaxValue)
                };
                ActiveEffects.Add(active);
                return new ActiveGameplayEffectHandle(active.HandleId);
            }

            // Instant 类型不进入 ActiveEffects
            return ActiveGameplayEffectHandle.Invalid;
        }

        /// <summary>应用 GameplayEffect Spec 到指定目标 ASC。</summary>
        public virtual List<ActiveGameplayEffectHandle> ApplyGameplayEffectSpecToTargetData(GameplayEffectSpecHandle specHandle, List<NarrativeAbilitySystemComponent> targetData)
        {
            var result = new List<ActiveGameplayEffectHandle>();
            if (targetData == null) return result;
            foreach (var target in targetData)
            {
                if (target == null) continue;
                var handle = target.ApplyGameplayEffectSpecToSelf(specHandle);
                if (handle.IsValid) result.Add(handle);

                // 触发 DealtDamage 事件（来自本 ASC 造成的伤害）
                if (AttributeSet != null && target.AttributeSet != null && target.AttributeSet.Damage.CurrentValue > 0f)
                {
                    OnDealtDamage?.Invoke(target, target.AttributeSet.Damage.CurrentValue, specHandle.Spec);
                }
            }
            return result;
        }

        /// <summary>应用单个修饰器到属性集。</summary>
        protected virtual void ApplyModifier(NarrativeAttributeSetBase attrSet, GameplayModifierInfo mod, GameplayEffectSpec spec)
        {
            var attr = attrSet.GetAttribute(mod.AttributeName);
            if (attr == null) return;

            float oldValue = attr.CurrentValue;
            float newValue = oldValue;

            switch (mod.ModifierOp)
            {
                case EGameplayModOp.Add:
                    // 加法修改 BaseValue（Instant）或当前值（持续）
                    attr.BaseValue += mod.Magnitude;
                    newValue = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                    break;
                case EGameplayModOp.Multiply:
                    attr.BaseValue *= mod.Magnitude;
                    newValue = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                    break;
                case EGameplayModOp.Divide:
                    if (mod.Magnitude != 0f)
                    {
                        attr.BaseValue /= mod.Magnitude;
                        newValue = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                    }
                    break;
                case EGameplayModOp.Override:
                    attr.SetBaseValue(mod.Magnitude);
                    attr.SetCurrentValue(mod.Magnitude);
                    newValue = mod.Magnitude;
                    break;
            }

            // 处理元属性（Heal/Damage 不立即 clamp）
            if (mod.AttributeName == "Heal" || mod.AttributeName == "Damage")
            {
                attr.SetCurrentValue(newValue);
            }
            // 处理 Health/Stamina（被 Max 限制）
            else if (mod.AttributeName == "Health")
            {
                newValue = Mathf.Clamp(newValue, 0f, attrSet.MaxHealth.CurrentValue);
                attr.SetCurrentValue(newValue);
            }
            else if (mod.AttributeName == "Stamina")
            {
                newValue = Mathf.Clamp(newValue, 0f, attrSet.MaxStamina.CurrentValue);
                attr.SetCurrentValue(newValue);
            }
            else
            {
                attr.SetCurrentValue(newValue);
            }

            if (Mathf.Abs(newValue - oldValue) > 0.0001f)
            {
                attrSet.NotifyAttributeChanged(mod.AttributeName, newValue, oldValue);
            }
        }

        /// <summary>重算属性当前值（基于 BaseValue + 激活效果修饰器，简化版仅返回 BaseValue）。</summary>
        protected virtual float RecalcAttributeWithModifiers(NarrativeAttributeSetBase attrSet, string attributeName)
        {
            var attr = attrSet.GetAttribute(attributeName);
            if (attr == null) return 0f;

            // 简化版：直接用 BaseValue 作为当前值
            // 完整实现需要遍历所有 ActiveEffects 中的修饰器
            float result = attr.BaseValue;
            foreach (var active in ActiveEffects)
            {
                if (!active.bIsActive || active.Effect == null || active.Effect.Modifiers == null) continue;
                foreach (var mod in active.Effect.Modifiers)
                {
                    if (mod.AttributeName != attributeName) continue;
                    switch (mod.ModifierOp)
                    {
                        case EGameplayModOp.Add: result += mod.Magnitude; break;
                        case EGameplayModOp.Multiply: result *= mod.Magnitude; break;
                        case EGameplayModOp.Divide:
                            if (mod.Magnitude != 0f) result /= mod.Magnitude;
                            break;
                        case EGameplayModOp.Override: result = mod.Magnitude; break;
                    }
                }
            }
            return result;
        }

        /// <summary>移除激活效果。</summary>
        public virtual void RemoveActiveEffect(ActiveGameplayEffectHandle handle)
        {
            if (!handle.IsValid) return;
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
            {
                var active = ActiveEffects[i];
                if (active.HandleId == handle.HandleId)
                {
                    // 移除授予的标签
                    if (active.Effect != null && active.Effect.GrantedTags != null)
                    {
                        foreach (var tag in active.Effect.GrantedTags.GetTags())
                        {
                            GameplayTags.RemoveTag(new GameplayTag(tag));
                        }
                    }
                    active.bIsActive = false;
                    ActiveEffects.RemoveAt(i);

                    // 重算属性
                    if (AttributeSet != null)
                    {
                        RecalculateAllAttributes();
                    }
                    return;
                }
            }
        }

        /// <summary>重算所有属性。</summary>
        public virtual void RecalculateAllAttributes()
        {
            if (AttributeSet == null) return;

            var attrNames = new[] { "XP", "Health", "MaxHealth", "Stamina", "MaxStamina", "StaminaRegenRate",
                                     "AttackRating", "Armor", "AttackDamage", "StealthRating" };
            foreach (var name in attrNames)
            {
                var attr = AttributeSet.GetAttribute(name);
                if (attr == null) continue;
                float oldValue = attr.CurrentValue;
                float newValue = RecalcAttributeWithModifiers(AttributeSet, name);

                // clamp Health/Stamina
                if (name == "Health") newValue = Mathf.Clamp(newValue, 0f, AttributeSet.MaxHealth.CurrentValue);
                else if (name == "Stamina") newValue = Mathf.Clamp(newValue, 0f, AttributeSet.MaxStamina.CurrentValue);

                attr.SetCurrentValue(newValue);
                if (Mathf.Abs(newValue - oldValue) > 0.0001f)
                {
                    AttributeSet.NotifyAttributeChanged(name, newValue, oldValue);
                }
            }
        }

        /// <summary>添加动态标签（通过临时 GameplayEffect）。</summary>
        public virtual ActiveGameplayEffectHandle AddDynamicTagsGameplayEffect(GameplayTagContainer tagsToAdd)
        {
            if (tagsToAdd == null || !tagsToAdd.GetTags().Any()) return ActiveGameplayEffectHandle.Invalid;

            // 创建 Infinite 类型的效果，仅包含 GrantedTags
            var effect = new GameplayEffect("DynamicTags")
            {
                DurationType = EGameplayEffectDurationType.Infinite,
                GrantedTags = new GameplayTagContainer(tagsToAdd.GetTags())
            };
            var spec = new GameplayEffectSpec(effect, this);
            return ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
        }

        /// <summary>直接造成伤害（不通过 GameplayEffect，用于掉落伤害等）。</summary>
        public virtual void DealDamage(float damage)
        {
            if (AttributeSet == null) return;
            if (damage <= 0f) return;

            float oldDamage = AttributeSet.Damage.CurrentValue;
            AttributeSet.Damage.SetCurrentValue(oldDamage + damage);
            AttributeSet.PostGameplayEffectExecute();

            // 检查死亡
            if (!_bIsDead && AttributeSet.Health.CurrentValue <= 0f)
            {
                _bIsDead = true;
                OnDied?.Invoke(Actor, this);
            }
        }

        /// <summary>直接治疗（不通过 GameplayEffect）。</summary>
        public virtual void Heal(float amount)
        {
            if (AttributeSet == null) return;
            if (amount <= 0f) return;

            float oldHeal = AttributeSet.Heal.CurrentValue;
            AttributeSet.Heal.SetCurrentValue(oldHeal + amount);
            AttributeSet.PostGameplayEffectExecute();
        }

        /// <summary>立即击杀。</summary>
        public virtual void Instakill()
        {
            if (AttributeSet == null) return;
            if (_bIsDead) return;

            AttributeSet.Health.SetCurrentValue(0f);
            _bIsDead = true;
            OnDied?.Invoke(Actor, this);
        }

        /// <summary>获取 Bot 攻击频率（指定 InputTag）。</summary>
        public virtual float GetBotAttackFrequency(GameplayTag inputTag)
        {
            // 查找绑定到 inputTag 的能力，返回其 DefaultBotAttackFrequency
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.Ability is NarrativeCombatAbility combat)
                {
                    return combat.DefaultBotAttackFrequency;
                }
            }
            return 0f;
        }

        /// <summary>获取 Bot 攻击范围（指定 InputTag）。</summary>
        public virtual float GetBotAttackRange(GameplayTag inputTag)
        {
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.Ability is NarrativeCombatAbility combat)
                {
                    return combat.DefaultBotAttackRange;
                }
            }
            return 0f;
        }

        // ===== 标签查询（IGameplayTagAssetInterface 等价） =====

        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            if (tagContainer == null) return;
            foreach (var tag in GameplayTags.GetTags())
            {
                tagContainer.AddTag(new GameplayTag(tag));
            }
        }

        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            return GameplayTags.HasTag(tagToCheck);
        }

        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return GameplayTags.HasAll(tagsToCheck);
        }

        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return GameplayTags.HasAny(tagsToCheck);
        }

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            if (AttributeSet == null)
            {
                AttributeSet = Actor.GetScript<NarrativeAttributeSetBase>();
            }
            // 初始化属性
            InitializeAttributes();

            // 绑定死亡事件以处理 HandleDeath
            OnDied += HandleDeathInternal;
        }

        public override void OnDisable()
        {
            OnDied -= HandleDeathInternal;
            base.OnDisable();
        }

        /// <summary>每帧 Tick：更新激活效果的持续期，应用耐力恢复。</summary>
        public override void OnUpdate()
        {
            // 更新激活效果
            float dt = Time.DeltaTime;
            float gameTime = Time.GameTime;
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
            {
                var active = ActiveEffects[i];
                if (!active.Tick(dt, gameTime))
                {
                    // 过期，移除
                    if (active.Effect != null && active.Effect.GrantedTags != null)
                    {
                        foreach (var tag in active.Effect.GrantedTags.GetTags())
                        {
                            GameplayTags.RemoveTag(new GameplayTag(tag));
                        }
                    }
                    ActiveEffects.RemoveAt(i);
                }
            }

            // 耐力恢复
            if (AttributeSet != null)
            {
                AttributeSet.TickStaminaRegen(dt);
            }
        }

        /// <summary>内部死亡处理。</summary>
        protected virtual void HandleDeathInternal(FlaxEngine.Actor killedActor, NarrativeAbilitySystemComponent killedASC)
        {
            NarrativePro.Core.NarrativeLog.Log($"[ASC] Actor {killedActor?.Name} died.");
        }
    }
}

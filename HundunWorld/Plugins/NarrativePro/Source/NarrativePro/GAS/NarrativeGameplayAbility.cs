using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// Narrativo Pro 自定义能力基类。对应 UE5 UNarrativeGameplayAbility。
    /// 简化点：
    /// - 移除 UE5 输入系统（Enhanced Input）改为 InputTag + <see cref="NarrativeAbilityInputMapping"/> 表
    /// - 移除网络预测/Local/Remote 区分（Commit/TargetData 用本地等价实现保留时序语义）
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

        /// <summary>
        /// 生成本能力实例的数据定义键（<c>Content/NarrativePro/GAS/Abilities/{key}.json</c>）。
        /// 由 <see cref="NarrativeGASAssetRegistry.CreateAbilityInstance"/> 回填，
        /// 供授予路径在武器没有显式配置输入标签时反查定义里的 <c>inputTag</c>。
        /// </summary>
        public string AbilityDefinitionKey = "";

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

        /// <summary>
        /// 授予即激活。对应 UE5 <c>bActivateAbilityOnGranted</c>
        /// （NarrativeGameplayAbility.h:29，逻辑见 NarrativeGameplayAbility.cpp:19-33）。
        /// 装备/交互驱动的能力（如"拿起武器""上马"）常用它省掉手动激活。
        /// </summary>
        public bool bActivateAbilityOnGranted = false;

        /// <summary>
        /// 以这些 gameplay 事件为触发源：事件在 ASC 上广播时本能力会被触发。
        /// 对应 UE5 能力的 TriggerSource = GameplayEvent + TriggerTag。
        /// </summary>
        public GameplayTagContainer TriggerOnGameplayEventTags = new GameplayTagContainer();

        /// <summary>
        /// 本能力激活时是否需要提交（Commit，扣 Cost/起冷却）。
        /// 对应 UE5 CommitAbility 的 Cost/Cooldown GameplayEffect 提交流程。
        /// </summary>
        public bool bCommitAbilityOnActivation = true;

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

        /// <summary>
        /// 是否已完成提交（Cost 已扣、Cooldown 已启动）。
        /// 对应 UE5 <c>UGameplayAbility::bIsAbilityCommiting</c> / CommitAbility 后置位。
        /// </summary>
        protected bool _bCommitted = false;

        /// <summary>
        /// 能力自身的作用域锁深度。对应 UE5 <c>UGameplayAbility::ScopeLockCount</c>：
        /// 锁未释放时 EndAbility 会被延后为 <see cref="_waitingToExecute"/> 中的回调。
        /// </summary>
        protected int _scopeLockCount = 0;

        /// <summary>锁释放后要补执行的收尾动作。对应 UE5 <c>FPostLockDelegate</c> 队列。</summary>
        private readonly List<Action> _waitingToExecute = new List<Action>();

        /// <summary>最近一次触发本能力的事件负载。对应 UE5 GetCurrentAbilityEventData。</summary>
        [NonSerialized]
        protected GameplayEventData _triggerEventData;

        /// <summary>本能力当前绑定的目标数据委托句柄。对应 UE5 OnTargetDataReadyCallbackDelegateHandle。</summary>
        protected int _targetDataDelegateHandle = 0;

        /// <summary>Avatar（宿主 Actor）被设定为本能力归属时调用。对应 UE5 <c>OnAvatarSet</c>。</summary>
        /// <param name="asc">持有本能力的 ASC。</param>
        /// <param name="spec">本能力的能力规格。</param>
        public virtual void OnAvatarSet(NarrativeAbilitySystemComponent asc, GameplayAbilitySpec spec)
        {
            if (asc == null) return;
            OwningASC = asc;

            // 对应 UE5：授予时若声明了"授予即激活"，直接走 ASC 的激活流程
            if (bActivateAbilityOnGranted && spec != null && !spec.bIsActive)
            {
                asc.TryActivateAbility(new GameplayAbilitySpecHandle(spec.HandleId));
            }

            // 拥有者角色可能在能力授予之后才确定（例如先挂 ASC 再绑定角色），这里补一次解析
            if (_characterOwner == null)
            {
                _characterOwner = asc.GetCharacterOwner();
            }
        }

        /// <summary>Avatar 被清除（能力被移除）时调用。对应 UE5 FGameplayAbilitySpec 移除后的清理。</summary>
        public virtual void OnAvatarClear()
        {
            _bCommitted = false;
            _scopeLockCount = 0;
            _waitingToExecute.Clear();
            _targetDataDelegateHandle = 0;
            _triggerEventData = null;
            CharacterOwner = null;
        }

        private NarrativePro.UnrealFramework.NarrativeCharacter _characterOwner;

        /// <summary>
        /// 拥有本能力的角色。对应 UE5 <c>CharacterOwner</c>。
        /// Flax 中宿主 Actor 可能换（例如上了载具），因此保留旧值不覆盖，与 UE5 OnAvatarSet 一致。
        /// </summary>
        public NarrativePro.UnrealFramework.NarrativeCharacter CharacterOwner
        {
            get
            {
                if (_characterOwner == null && OwningASC != null)
                {
                    _characterOwner = OwningASC.GetCharacterOwner();
                }
                return _characterOwner;
            }
            protected set => _characterOwner = value;
        }

        // ===== 提交（Commit） =====

        /// <summary>
        /// 提交能力：应用 Cost 与 Cooldown。对应 UE5 <c>CommitAbility</c> → <c>CommitExecute</c>。
        /// 由 ASC 在激活前调用；重复调用只生效一次。
        /// </summary>
        public virtual bool CommitAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            if (_bCommitted) return true;
            if (OwningASC == null) return false;

            // Cost 校验：不够支付则提交失败（对应 UE5 CommitAbility 的 CheckCost 分支）
            if (!CanAffordCost(OwningASC)) return false;

            _bCommitted = true;
            CommitExecute(handle, spec);
            return true;
        }

        /// <summary>
        /// 提交的实际执行：扣 Cost、起 Cooldown、授予能力标签。
        /// 对应 UE5 <c>CommitExecute</c>（NarrativeCombatAbility.cpp:48-52 直接转发基类）。
        /// </summary>
        public virtual void CommitExecute(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            if (!_bCommitted) _bCommitted = true;

            // 应用 Cooldown（计时器 + 可选标签）
            if (CooldownDuration > 0f)
            {
                CooldownStartTime = Time.GameTime;
                CooldownRemaining = CooldownDuration;

                if (CooldownTag.IsValid())
                {
                    ApplyCooldownEffect();
                }
            }

            // 授予 AbilityTags（对应 UE5 CommitAbility 里应用 ActivationOwnedTags）
            if (OwningASC != null && AbilityTags != null && AbilityTags.Count > 0)
            {
                OwningASC.AddDynamicTagsGameplayEffect(AbilityTags);
            }

            // 应用 Cost：扣除属性消耗
            ApplyCost();
        }

        /// <summary>撤销提交：回滚 Cost 与 Cooldown。对应 UE5 <c>UAbilitySystemComponent::UncommitAbility</c>。</summary>
        public virtual void TryUncommit()
        {
            if (!_bCommitted) return;
            _bCommitted = false;
            UnapplyCost();
        }

        /// <summary>是否已提交。</summary>
        public bool IsCommitted => _bCommitted;

        /// <summary>外部（ASC）用的 Cost 校验入口。对应 UE5 K2_CanActivateAbility 的 Cost 部分。</summary>
        public bool CanAffordCostPublic(NarrativeAbilitySystemComponent asc) => CanAffordCost(asc);

        /// <summary>退还 Cost 消耗（撤销提交时用）。</summary>
        protected virtual void UnapplyCost()
        {
            if (CostAmount <= 0f || OwningASC?.AttributeSet == null) return;

            var attr = OwningASC.AttributeSet.GetAttribute(CostAttributeName);
            if (attr == null) return;

            float old = attr.CurrentValue;
            float refunded = attr.BaseValue + CostAmount;
            if (CostAttributeName == "Health")
            {
                refunded = Mathf.Clamp(refunded, 0f, OwningASC.AttributeSet.MaxHealth.CurrentValue);
            }
            else if (CostAttributeName == "Stamina")
            {
                refunded = Mathf.Clamp(refunded, 0f, OwningASC.AttributeSet.MaxStamina.CurrentValue);
            }
            attr.BaseValue = refunded;
            attr.SetCurrentValue(refunded);
            OwningASC.AttributeSet.NotifyAttributeChanged(CostAttributeName, refunded, old);
        }

        /// <summary>
        /// 判定 EndAbility 是否仍然有效。对应 UE5 <c>UGameplayAbility::IsEndAbilityValid</c>：
        /// 能力必须仍属于这个 ASC、句柄仍是当前句柄、且 ASC 上该 spec 仍存在。
        /// </summary>
        public virtual bool IsEndAbilityValid(GameplayAbilitySpecHandle handle, NarrativeAbilitySystemComponent asc)
        {
            if (asc == null || asc != OwningASC) return false;
            if (!handle.IsValid || handle.HandleId != _currentHandle.HandleId) return false;
            return asc.FindAbilitySpec(handle) != null;
        }

        /// <summary>
        /// 进入能力作用域锁。对应 UE5 <c>SCOPE_LOCK(ABILITY_SCOPE_LOCK_NAME, ScopeLockCount)</c>。
        /// 锁期间 EndAbility 会被延后到锁释放，避免收尾与进行中的逻辑交错。
        /// </summary>
        public IDisposable BeginAbilityScopeLock()
        {
            return new AbilityScopeLock(this);
        }

        /// <summary>能力作用域锁当前深度。</summary>
        public int ScopeLockCount => _scopeLockCount;

        private void AcquireScopeLock() => _scopeLockCount++;

        private void ReleaseScopeLock()
        {
            if (_scopeLockCount > 0) _scopeLockCount--;
            if (_scopeLockCount == 0)
            {
                FlushWaitingToExecute();
            }
        }

        /// <summary>把延后的收尾动作排入队列（锁期间调用 EndAbility 的等价 UE5 WaitingToExecute.Add）。</summary>
        protected virtual void AddWaitingToExecute(Action action)
        {
            if (action == null) return;
            _waitingToExecute.Add(action);
        }

        /// <summary>锁释放时执行全部延后收尾。对应 UE5 FPostLockDelegate 队列冲刷。</summary>
        protected virtual void FlushWaitingToExecute()
        {
            if (_waitingToExecute.Count == 0) return;
            var pending = new List<Action>(_waitingToExecute);
            _waitingToExecute.Clear();
            foreach (var action in pending)
            {
                action?.Invoke();
            }
        }

        private sealed class AbilityScopeLock : IDisposable
        {
            private NarrativeGameplayAbility _ability;

            public AbilityScopeLock(NarrativeGameplayAbility ability)
            {
                _ability = ability;
                _ability?.AcquireScopeLock();
            }

            public void Dispose()
            {
                var ability = _ability;
                _ability = null;
                ability?.ReleaseScopeLock();
            }
        }

        /// <summary>写入触发本能力的事件负载。由 ASC 在事件触发路径调用。</summary>
        public virtual void SetTriggerEventData(GameplayEventData payload)
        {
            _triggerEventData = payload;
        }

        /// <summary>取触发本能力的事件负载（手动激活时为 null）。对应 UE5 GetCurrentAbilityEventData。</summary>
        public GameplayEventData GetTriggerEventData() => _triggerEventData;

        /// <summary>当前激活句柄。对应 UE5 CurrentSpecHandle。</summary>
        public GameplayAbilitySpecHandle CurrentSpecHandle => _currentHandle;

        /// <summary>取本能力所在的 ASC。对应 UE5 GetAbilitySystemComponentActorInfo。</summary>
        public NarrativeAbilitySystemComponent GetAbilitySystemComponent() => OwningASC;

        /// <summary>
        /// 取拥有本能力的控制器。对应 UE5 <c>GetOwningController</c>：
        /// 先取 PlayerController，AI 则取 Pawn 的 Controller。
        /// Flax 无 Controller 体系，故返回宿主 Actor 上挂载的控制器脚本
        /// （<c>NarrativePlayerController</c> 或 <c>NarrativeNPCController</c>），以 object 承载，
        /// 与本仓库 <c>NarrativeGASStatics.ResolveCharacterFromContext</c> 的约定一致。
        /// </summary>
        public virtual object GetOwningController()
        {
            var actor = OwningASC != null ? OwningASC.Actor : Actor;
            if (actor == null) return null;

            var playerController = actor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            if (playerController != null) return playerController;

            var npcController = actor.GetScript<NarrativePro.AI.NarrativeNPCController>();
            if (npcController != null) return npcController;

            return null;
        }

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

        /// <summary>
        /// 激活能力。由 ASC 调用（ASC 已在调用前完成 Commit）。
        /// 对应 UE5 <c>ActivateAbility</c>：这里不再重复扣 Cost/起 Cooldown，
        /// 若能力被直接 Activate 而未走 ASC 的提交路径，则补一次提交以保证 Cost/Cooldown 生效。
        /// </summary>
        public virtual void ActivateAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            _currentHandle = handle;

            // 兜底提交：外部绕过 ASC 直接激活时，仍要扣 Cost / 起冷却
            if (bCommitAbilityOnActivation && !_bCommitted)
            {
                CommitAbility(handle, spec);
            }

            // 若以事件触发，把 spec 上的暂存目标数据交给能力消费
            if (spec != null && spec.PendingTargetData != null)
            {
                ConsumePendingTargetData(handle);
            }
        }

        /// <summary>
        /// 消费 ASC 上为本能力暂存的目标数据。
        /// 对应 UE5 <c>ConsumeClientReplicatedTargetData</c>。
        /// </summary>
        protected virtual List<CombatHitResult> ConsumePendingTargetData(GameplayAbilitySpecHandle handle)
        {
            if (OwningASC == null) return null;
            var data = OwningASC.ConsumeTargetData(handle);
            var spec = OwningASC.FindAbilitySpec(handle);
            if (spec != null) spec.PendingTargetData = null;
            return data;
        }

        /// <summary>结束能力（由能力本身调用）。</summary>
        public virtual void EndAbility()
        {
            var handle = _currentHandle;
            var asc = OwningASC;

            // 对应 UE5 EndAbility 的 IsEndAbilityValid 门槛：句柄/归属失效就不再收尾
            if (!IsEndAbilityValid(handle, asc))
            {
                _currentHandle = GameplayAbilitySpecHandle.Invalid;
                return;
            }

            // 对应 UE5 ScopeLockCount > 0 时把 EndAbility 挂到 WaitingToExecute 里延后
            if (ScopeLockCount > 0)
            {
                AddWaitingToExecute(() => EndAbility());
                return;
            }

            InternalEndAbility(handle, asc, false);
        }

        /// <summary>真正的收尾：清目标数据与委托、复位提交标记、通知 ASC。对应 UE5 Super::EndAbility。</summary>
        protected virtual void InternalEndAbility(GameplayAbilitySpecHandle handle, NarrativeAbilitySystemComponent asc, bool bWasCancelled)
        {
            if (asc != null)
            {
                // 对应 UE5：能力结束时移除目标数据委托并消费掉残留目标数据
                asc.UnbindTargetDataSetDelegate(handle);
                asc.ConsumeTargetData(handle);
                asc.ClearTargetDataForSpec(handle);
                asc.EndAbility(handle, bWasCancelled);
            }

            _targetDataDelegateHandle = 0;
            _bCommitted = false;
            _triggerEventData = null;
            _currentHandle = GameplayAbilitySpecHandle.Invalid;
        }

        /// <summary>取消能力（由外部调用）。</summary>
        public virtual void CancelAbility()
        {
            var handle = _currentHandle;
            var asc = OwningASC;

            if (asc != null && handle.IsValid)
            {
                asc.UnbindTargetDataSetDelegate(handle);
                asc.ClearTargetDataForSpec(handle);
                asc.EndAbility(handle, true);
            }

            _targetDataDelegateHandle = 0;
            _bCommitted = false;
            _triggerEventData = null;
            _currentHandle = GameplayAbilitySpecHandle.Invalid;
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

        /// <summary>能力被授予时调用（GiveAbility 中触发）。</summary>
        public virtual void OnAbilityGranted() { }

        // ===== 输入处理 =====

        /// <summary>输入标签按下。</summary>
        public virtual void InputTagPressed(GameplayTag inputTag) { }

        /// <summary>输入标签释放。</summary>
        public virtual void InputTagReleased(GameplayTag inputTag) { }

        // ===== 输入映射 =====

        /// <summary>当前输入动作路径（对应 Flax 输入动作/映射名）。</summary>
        public string CurrentInputActionPath = "";

        /// <summary>本能力使用的输入映射资产。为空时回退到全局默认映射。</summary>
        public NarrativeAbilityInputMapping InputMapping;

        /// <summary>全局默认输入映射（对应 UE5 项目设置里的那份输入映射资产）。</summary>
        public static NarrativeAbilityInputMapping DefaultInputMapping { get; set; }

        /// <summary>获取当前输入动作路径。没有显式配置时按输入标签从映射反查。</summary>
        public virtual string GetCurrentInputActionPath()
        {
            if (!string.IsNullOrEmpty(CurrentInputActionPath)) return CurrentInputActionPath;
            return GetInputActionPathFromTag(InputTag);
        }

        /// <summary>
        /// 从输入标签查输入动作路径。
        /// 对应 UE5 <c>GetInputActionPathFromTag</c>；Flax 侧走 <see cref="NarrativeAbilityInputMapping"/>
        /// 的表（本能力的映射优先，其次 <see cref="DefaultInputMapping"/>）。
        /// </summary>
        public virtual string GetInputActionPathFromTag(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return "";

            var mapping = InputMapping != null ? InputMapping : DefaultInputMapping;
            if (mapping == null) return "";

            // 映射表存的是能力路径，这里把它转成输入动作名：
            // 输入动作以 "Input.<标签末段>" 命名，与 Flax InputAction 资产名对齐
            string abilityPath = mapping.FindAbilityPath(inputTag);
            if (string.IsNullOrEmpty(abilityPath)) return "";

            return InputActionNameForTag(inputTag, abilityPath);
        }

        /// <summary>从输入动作路径反查输入标签（映射表的双向查询）。</summary>
        public virtual GameplayTag GetInputTagFromActionPath(string actionPath)
        {
            if (string.IsNullOrEmpty(actionPath)) return GameplayTag.None;

            var mapping = InputMapping != null ? InputMapping : DefaultInputMapping;
            if (mapping == null || mapping.InputAbilities == null) return GameplayTag.None;

            foreach (var entry in mapping.InputAbilities)
            {
                if (entry == null || string.IsNullOrEmpty(entry.AbilityPath)) continue;
                if (InputActionNameForTag(entry.InputTag, entry.AbilityPath) == actionPath) return entry.InputTag;
            }
            return GameplayTag.None;
        }

        /// <summary>取能力输入映射资产路径（对应 UE5 GetAbilityInputMappingPath）。</summary>
        public virtual string GetAbilityInputMappingPath()
        {
            return InputMapping != null ? InputMapping.MappingPath : "";
        }

        /// <summary>
        /// 输入动作名规则：Input.&lt;标签末段&gt;_&lt;能力名&gt;。
        /// 集中一处定义，保证正反查询一致。
        /// </summary>
        private static string InputActionNameForTag(GameplayTag inputTag, string abilityPath)
        {
            string tagLeaf = inputTag.IsValid() ? inputTag.TagName : "";
            int lastDot = tagLeaf.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < tagLeaf.Length - 1) tagLeaf = tagLeaf.Substring(lastDot + 1);

            string abilityLeaf = abilityPath ?? "";
            int slash = abilityLeaf.LastIndexOf('/');
            if (slash >= 0 && slash < abilityLeaf.Length - 1) abilityLeaf = abilityLeaf.Substring(slash + 1);
            int dot = abilityLeaf.LastIndexOf('.');
            if (dot >= 0 && dot < abilityLeaf.Length - 1) abilityLeaf = abilityLeaf.Substring(dot + 1);

            return "Input." + tagLeaf + "_" + abilityLeaf;
        }

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

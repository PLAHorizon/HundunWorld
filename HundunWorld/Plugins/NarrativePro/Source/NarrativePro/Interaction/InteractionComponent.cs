using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互组件基类。挂在 Pawn/Controller 上，使其能与 InteractableComponent 交互。
    /// 适配 UE5 UNarrativeInteractionComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// GAS 交互通过 Actor 上的 NarrativeAbilitySystemComponent 授予并激活能力。
    /// 实现 INarrativeSavableComponent 通过 PrepareForSave/Load 方法。
    /// </summary>
    public class NarrativeInteractionComponent : Script, NarrativePro.SaveSystem.INarrativeSavableComponent
    {
        /// <summary>拥有者 Pawn（Actor）</summary>
        public Actor OwningPawn { get; protected set; }

        /// <summary>拥有者 Controller（Actor 或 null）</summary>
        public Actor OwningController { get; protected set; }

        /// <summary>当前交互槽位占用句柄</summary>
        public InteractionSlotClaimHandle InteractionSlotClaimHandle { get; set; } = InteractionSlotClaimHandle.InvalidHandle();

        /// <summary>当前占用并交互中的 InteractableComponent</summary>
        public NarrativeInteractableComponent OccupiedInteractable { get; protected set; }

        /// <summary>占用槽位的 Actor 软引用（存档恢复用）</summary>
        public string OccupiedInteractableSoftOwnerPath { get; set; } = "";

        /// <summary>占用槽位的索引</summary>
        public int OccupiedInteractableSlotIdx { get; set; } = -1;

        /// <summary>当前交互能力句柄（对应 UE5 FGameplayAbilitySpecHandle CurrentInteractAbilityHandle）。</summary>
        public GameplayAbilitySpecHandle CurrentInteractAbilityHandle { get; protected set; } = GameplayAbilitySpecHandle.Invalid;

        /// <summary>当前交互能力句柄 ID（字符串视图，便于日志与外部序列化）。</summary>
        public string CurrentInteractAbilityHandleId
        {
            get => CurrentInteractAbilityHandle.IsValid ? CurrentInteractAbilityHandle.HandleId.ToString() : "";
            set
            {
                if (int.TryParse(value, out var id)) CurrentInteractAbilityHandle = new GameplayAbilitySpecHandle(id);
                else CurrentInteractAbilityHandle = GameplayAbilitySpecHandle.Invalid;
            }
        }

        // ===== 交互上下文（对应 UE5 传给能力的 FGameplayEventData）=====
        // UE5 用 OptionalObject=Interactable、OptionalObject2=StealingFrom、EventMagnitude=SlotIndex、
        // EventTag=Interact/Interact_Steal 传参。Flax GAS 没有 GameplayEventData，
        // 这些上下文在此组件上暴露，能力实例通过 Owner Actor 取回本组件读取。

        /// <summary>本次交互的目标可交互组件。</summary>
        public NarrativeInteractableComponent CurrentInteractionInteractable { get; protected set; }

        /// <summary>本次交互占用的槽位索引。</summary>
        public int CurrentInteractionSlotIndex { get; protected set; } = -1;

        /// <summary>本次交互是否为偷取他人槽位。</summary>
        public bool bCurrentInteractionIsStealing { get; protected set; } = false;

        /// <summary>被偷取槽位的原占用者。</summary>
        public NarrativeInteractionComponent CurrentInteractionStealingFrom { get; protected set; }

        /// <summary>开始使用 Interactable 时触发（含槽位交互和快速交互）</summary>
        public event Action<Actor, NarrativeInteractableComponent> OnBeginUseInteractable;

        /// <summary>结束使用 Interactable 时触发</summary>
        public event Action<Actor, NarrativeInteractableComponent> OnFinishUseInteractable;

        /// <summary>
        /// 广播 <see cref="OnBeginUseInteractable"/>。
        /// UE5 里是 FOnUseInteractable::Broadcast(InteractableActor, InteractableComponent)
        /// （见 NarrativeArsenal/Private/Interaction/PlayerInteractionComponent.cpp:123）；
        /// C# 的 event 只能在声明类型内部触发，所以派生组件一律通过这个受保护入口触发
        /// —— 正是 UE5 BlueprintAssignable 委托的等价写法。
        /// </summary>
        /// <param name="interactableActor">被使用的可交互对象宿主 Actor</param>
        /// <param name="interactable">被使用的可交互组件</param>
        protected virtual void BroadcastOnBeginUseInteractable(Actor interactableActor, NarrativeInteractableComponent interactable)
        {
            OnBeginUseInteractable?.Invoke(interactableActor, interactable);
        }

        /// <summary>
        /// 广播 <see cref="OnFinishUseInteractable"/>。对应 UE5 的
        /// OnFinishUseInteractable.Broadcast(InteractableActor, InteractableComponent)
        /// （见 NarrativeArsenal/Private/Interaction/PlayerInteractionComponent.cpp:124）。
        /// </summary>
        /// <param name="interactableActor">被使用的可交互对象宿主 Actor</param>
        /// <param name="interactable">被使用的可交互组件</param>
        protected virtual void BroadcastOnFinishUseInteractable(Actor interactableActor, NarrativeInteractableComponent interactable)
        {
            OnFinishUseInteractable?.Invoke(interactableActor, interactable);
        }

        // 已授予能力的句柄缓存，避免同一能力反复 GiveAbility 造成 spec 泄漏
        private readonly Dictionary<NarrativeGameplayAbility, GameplayAbilitySpecHandle> _grantedAbilityHandles = new Dictionary<NarrativeGameplayAbility, GameplayAbilitySpecHandle>();

        public override void OnEnable()
        {
            base.OnEnable();
            OwningPawn = Actor;
            // Controller 暂不区分，Flax 项目通常由 Actor 自身兼任
            OwningController = Actor;
        }

        public override void OnDisable()
        {
            // 卸载时释放占用槽位
            if (OccupiedInteractable != null)
            {
                ReleaseInteractionSlot();
                OccupiedInteractable = null;
            }
            _grantedAbilityHandles.Clear();
            base.OnDisable();
        }

        /// <summary>是否正在占用交互对象。</summary>
        public bool HasOccupiedInteractable() => OccupiedInteractable != null;

        /// <summary>组件是否活跃（对应 UE5 ActorComponent::IsActive，交互 Tick 的门）。</summary>
        public virtual bool IsActive()
        {
            if (Actor == null) return false;
            if (!Actor.IsActiveInHierarchy) return false;
            return IsEnabledInHierarchy;
        }

        /// <summary>
        /// 取得拥有者的能力系统组件。对应 UE5 的 OwningPawn→Controller→INarrativeCharacterOwner 三级回退。
        /// </summary>
        public NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            var asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(OwningPawn);
            if (asc != null) return asc;
            if (OwningController != null && OwningController != OwningPawn)
            {
                asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(OwningController);
                if (asc != null) return asc;
            }
            return ArsenalStatics.GetNarrativeAbilitySystemComponent(Actor);
        }

        /// <summary>取得当前占用槽位上的交互行为（可能为 null）。</summary>
        public InteractionSlotBehavior GetCurrentSlotBehavior()
        {
            var interactable = InteractionSlotClaimHandle.IsValidHandle()
                ? InteractionSlotClaimHandle.HandleOwner
                : OccupiedInteractable;
            if (interactable == null) return null;

            int idx = InteractionSlotClaimHandle.HandleIndex;
            if (idx < 0) idx = OccupiedInteractableSlotIdx;
            var config = interactable.GetConfigAtSlot(idx);
            return config?.SlotInteractBehavior;
        }

        /// <summary>占用指定 Interactable 的指定槽位。返回是否成功。</summary>
        public bool ClaimInteractionSlot(NarrativeInteractableComponent interactable, int slotIdx)
        {
            if (interactable == null) return false;
            if (slotIdx < 0) return false;

            // 先释放我们已持有的旧槽位（对应 UE5 InteractionComponent.cpp:88-95）
            if (InteractionSlotClaimHandle.IsValidHandle())
            {
                var currentInteractable = InteractionSlotClaimHandle.HandleOwner;
                if (currentInteractable != null && !ReferenceEquals(currentInteractable, interactable))
                {
                    currentInteractable.UpdateSlotStatus(this, InteractionSlotClaimHandle.HandleIndex, EInteractionSlotStatus.Free);
                }
            }

            var handle = interactable.ClaimSlot(this, slotIdx, false);
            if (!handle.IsValidHandle()) return false;

            InteractionSlotClaimHandle = handle;
            SetOccupiedInteractable(interactable, slotIdx);
            return true;
        }

        /// <summary>
        /// 释放已占用的交互槽位。
        /// 对应 UE5：只有当我们仍是该槽位的 SlotUser 时才写回 Free，
        /// 槽位若已被偷走则只作废自己的句柄。
        /// </summary>
        public void ReleaseInteractionSlot()
        {
            NarrativeInteractableComponent claimedInteractable = null;

            if (InteractionSlotClaimHandle.IsValidHandle())
            {
                claimedInteractable = InteractionSlotClaimHandle.HandleOwner;
                int idx = InteractionSlotClaimHandle.HandleIndex;
                if (claimedInteractable != null && idx >= 0 && idx < claimedInteractable.SlotStatuses.Count &&
                    ReferenceEquals(claimedInteractable.SlotStatuses[idx].SlotUser, this))
                {
                    claimedInteractable.UpdateSlotStatus(this, idx, EInteractionSlotStatus.Free);
                }
            }

            InteractionSlotClaimHandle = InteractionSlotClaimHandle.InvalidHandle();

            // 只有当占用记录指向我们刚释放的那个可交互对象时才清空
            if (OccupiedInteractable == null || ReferenceEquals(OccupiedInteractable, claimedInteractable))
            {
                OccupiedInteractable = null;
            }
            OccupiedInteractableSlotIdx = -1;
        }

        /// <summary>设置当前占用的 Interactable（内部使用）。</summary>
        protected virtual void SetOccupiedInteractable(NarrativeInteractableComponent interactable, int slotIdx)
        {
            OccupiedInteractable = interactable;
            OccupiedInteractableSlotIdx = slotIdx;
        }

        /// <summary>
        /// 启动当前占用槽位的交互行为。对应 UE5 RunInteractBehavior。
        /// 通过拥有者 ASC 授予并一次性激活槽位行为配置的交互能力；
        /// 无交互能力时退化为纯事件广播（保持既有调用方可用）。
        /// </summary>
        public bool RunInteractBehavior(bool bIsStealing, NarrativeInteractionComponent stealingFrom = null)
        {
            if (!InteractionSlotClaimHandle.IsValidHandle())
            {
                NarrativeLog.LogWarning("[Interaction] 无占用槽位，无法运行交互行为");
                return false;
            }

            var currentInteractable = InteractionSlotClaimHandle.HandleOwner;
            if (currentInteractable == null) return false;

            var slotConfig = currentInteractable.GetConfigAtSlot(InteractionSlotClaimHandle.HandleIndex);
            var behavior = slotConfig?.SlotInteractBehavior;

            // 记录交互上下文（等价 UE5 的 FGameplayEventData 传参）
            CurrentInteractionInteractable = currentInteractable;
            CurrentInteractionSlotIndex = InteractionSlotClaimHandle.HandleIndex;
            bCurrentInteractionIsStealing = bIsStealing;
            CurrentInteractionStealingFrom = stealingFrom;

            if (behavior == null || behavior.GetInteractAbility() == null)
            {
                // 无交互能力定义：走事件路径（快速交互 / 纯回调槽位）
                BroadcastOnBeginUseInteractable(currentInteractable.Actor, currentInteractable);
                return true;
            }

            var asc = GetAbilitySystemComponent();
            if (asc == null) return false;

            var ability = behavior.ResolveInteractAbilityInstance(asc.Actor);
            if (ability == null) return false;

            if (!_grantedAbilityHandles.TryGetValue(ability, out var specHandle) || !asc.GrantedAbilities.Exists(s => s.HandleId == specHandle.HandleId))
            {
                specHandle = asc.GiveAbility(ability, 1f);
                _grantedAbilityHandles[ability] = specHandle;
            }

            if (!asc.TryActivateAbility(specHandle))
            {
                return false;
            }

            CurrentInteractAbilityHandle = specHandle;
            BroadcastOnBeginUseInteractable(currentInteractable.Actor, currentInteractable);
            return true;
        }

        /// <summary>
        /// 停止当前交互行为。对应 UE5 StopInteractBehavior。
        /// bWasStolen: 是否因槽位被偷而停止；optionalStealer: 偷取者。
        /// 返回 false 表示无法退出交互（槽位行为否决，或我们本来就没有交互在进行），
        /// 此时 ClaimSlot 的偷取握手会中止。
        /// </summary>
        public bool StopInteractBehavior(bool bWasStolen, NarrativeInteractionComponent optionalStealer = null, string optionalPayloadId = "")
        {
            var interactable = InteractionSlotClaimHandle.IsValidHandle()
                ? InteractionSlotClaimHandle.HandleOwner
                : OccupiedInteractable;

            // 槽位行为可以否决退出（对应 UE5 UNarrativeInteractAbility::TryFinishInteraction）
            var behavior = GetCurrentSlotBehavior();
            if (behavior != null && !behavior.TryFinishInteraction(bWasStolen, optionalStealer, this))
            {
                return false;
            }

            var ability = GetInteractAbility();
            if (ability != null)
            {
                var asc = GetAbilitySystemComponent();
                asc?.CancelAbility(CurrentInteractAbilityHandle);
            }

            CurrentInteractAbilityHandle = GameplayAbilitySpecHandle.Invalid;

            if (interactable == null)
            {
                // 我们没有任何交互在进行
                return false;
            }

            BroadcastOnFinishUseInteractable(interactable.Actor, interactable);

            // 能力结束后释放槽位（UE5 由 InteractAbility 的 EndAbility 完成，此处显式收敛）
            ReleaseInteractionSlot();

            ClearInteractionContext();
            return true;
        }

        /// <summary>清理交互上下文字段。</summary>
        protected void ClearInteractionContext()
        {
            CurrentInteractionInteractable = null;
            CurrentInteractionSlotIndex = -1;
            bCurrentInteractionIsStealing = false;
            CurrentInteractionStealingFrom = null;
        }

        /// <summary>
        /// 获取当前正在运行的交互能力。对应 UE5 GetInteractAbility()：
        /// 只有当我们持有已认领的槽位、且 ASC 上的对应 spec 仍激活时才返回。
        /// </summary>
        public NarrativeGameplayAbility GetInteractAbility()
        {
            if (!InteractionSlotClaimHandle.IsValidHandle() || InteractionSlotClaimHandle.HandleOwner == null)
            {
                return null;
            }

            if (!CurrentInteractAbilityHandle.IsValid) return null;

            var asc = GetAbilitySystemComponent();
            if (asc == null) return null;

            var spec = asc.FindAbilitySpec(CurrentInteractAbilityHandle);
            if (spec == null || !spec.bIsActive) return null;

            return spec.Ability as NarrativeGameplayAbility;
        }

        /// <summary>获取拥有者位置。</summary>
        public Vector3 GetOwnerPosition()
        {
            return OwningPawn != null ? OwningPawn.Position : Vector3.Zero;
        }

        /// <summary>存档：准备保存数据。</summary>
        public virtual void PrepareForSave()
        {
            if (OccupiedInteractable != null)
            {
                var actor = OccupiedInteractable.Actor;
                OccupiedInteractableSoftOwnerPath = actor != null ? actor.Name : "";
            }
            else
            {
                OccupiedInteractableSoftOwnerPath = "";
            }
        }

        /// <summary>读档：恢复状态。</summary>
        public virtual void Load()
        {
            // 玩家通常不恢复占用状态，NPC 由交互目标恢复
            // 子类可覆盖以实现恢复逻辑
        }
    }
}

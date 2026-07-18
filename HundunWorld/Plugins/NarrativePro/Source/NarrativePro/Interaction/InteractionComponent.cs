using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互组件基类。挂在 Pawn/Controller 上，使其能与 InteractableComponent 交互。
    /// 适配 UE5 UNarrativeInteractionComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// GAS 相关（InteractAbility）以字符串 ID 引用占位，待 GAS 阶段填充。
    /// 实现 INarrativeSavableComponent 通过 PrepareForSave/Load 方法。
    /// </summary>
    public class NarrativeInteractionComponent : Script
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

        /// <summary>当前交互能力句柄（字符串 ID 占位，对应 UE FGameplayAbilitySpecHandle）</summary>
        public string CurrentInteractAbilityHandleId { get; set; } = "";

        /// <summary>开始使用 Interactable 时触发（含槽位交互和快速交互）</summary>
        public event Action<Actor, NarrativeInteractableComponent> OnBeginUseInteractable;

        /// <summary>结束使用 Interactable 时触发</summary>
        public event Action<Actor, NarrativeInteractableComponent> OnFinishUseInteractable;

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
            base.OnDisable();
        }

        /// <summary>是否正在占用交互对象。</summary>
        public bool HasOccupiedInteractable() => OccupiedInteractable != null;

        /// <summary>占用指定 Interactable 的指定槽位。返回是否成功。</summary>
        public bool ClaimInteractionSlot(NarrativeInteractableComponent interactable, int slotIdx)
        {
            if (interactable == null) return false;
            if (slotIdx < 0) return false;

            var handle = interactable.ClaimSlot(this, slotIdx, bMarkTargeted: false);
            if (!handle.IsValidHandle()) return false;

            InteractionSlotClaimHandle = handle;
            OccupiedInteractable = interactable;
            OccupiedInteractableSlotIdx = slotIdx;
            return true;
        }

        /// <summary>释放已占用的交互槽位。</summary>
        public void ReleaseInteractionSlot()
        {
            if (OccupiedInteractable == null) return;
            if (OccupiedInteractableSlotIdx >= 0)
            {
                OccupiedInteractable.UpdateSlotStatus(this, OccupiedInteractableSlotIdx, EInteractionSlotStatus.Free);
            }
            OccupiedInteractable = null;
            InteractionSlotClaimHandle = InteractionSlotClaimHandle.InvalidHandle();
            OccupiedInteractableSlotIdx = -1;
        }

        /// <summary>设置当前占用的 Interactable（内部使用）。</summary>
        protected virtual void SetOccupiedInteractable(NarrativeInteractableComponent interactable, int slotIdx)
        {
            OccupiedInteractable = interactable;
            OccupiedInteractableSlotIdx = slotIdx;
        }

        /// <summary>启动当前占用槽位的交互行为。返回是否成功。
        /// GAS 阶段接入实际能力系统。</summary>
        public bool RunInteractBehavior(bool bIsStealing, NarrativeInteractionComponent stealingFrom = null)
        {
            if (OccupiedInteractable == null)
            {
                NarrativeLog.LogWarning("[Interaction] 无占用槽位，无法运行交互行为");
                return false;
            }
            var slotConfig = OccupiedInteractable.GetConfigAtSlot(OccupiedInteractableSlotIdx);
            if (slotConfig?.SlotInteractBehavior == null)
            {
                // 无行为定义，直接触发 OnBeginUseInteractable 事件
                OnBeginUseInteractable?.Invoke(OwningPawn, OccupiedInteractable);
                return true;
            }

            // TODO [需接入 GAS 系统]: 启动 SlotInteractBehavior.SlotInteractBehaviorId 对应的 InteractAbility
            // 当前仅记录能力句柄 ID，待 GAS 系统接入后激活实际能力
            CurrentInteractAbilityHandleId = slotConfig.SlotInteractBehavior.SlotInteractBehaviorId;
            OnBeginUseInteractable?.Invoke(OwningPawn, OccupiedInteractable);
            return true;
        }

        /// <summary>停止当前交互行为。返回是否成功。
        /// bWasStolen: 是否因槽位被偷而停止
        /// OptionalStealer: 偷取者
        /// OptionalPayloadId: 额外数据 ID（对应 UE FGameplayEventData，简化为字符串）</summary>
        public bool StopInteractBehavior(bool bWasStolen, NarrativeInteractionComponent optionalStealer = null, string optionalPayloadId = "")
        {
            if (OccupiedInteractable == null) return false;

            // TODO [需接入 GAS 系统]: 结束 CurrentInteractAbilityHandleId 对应的 InteractAbility
            // 待 GAS 系统接入后执行能力取消逻辑
            var interactable = OccupiedInteractable;
            CurrentInteractAbilityHandleId = "";
            OnFinishUseInteractable?.Invoke(OwningPawn, interactable);
            return true;
        }

        /// <summary>获取当前交互能力对象（GAS 阶段填充，当前返回 null）。</summary>
        public object GetInteractAbility() => null;

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

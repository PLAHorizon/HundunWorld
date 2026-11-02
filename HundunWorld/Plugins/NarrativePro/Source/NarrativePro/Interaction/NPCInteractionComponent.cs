using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 目标交互槽位改变事件参数。
    /// 适配 UE5 FOnTargetedInteractionSlotChanged。
    /// </summary>
    public class TargetedInteractionSlotChangedEventArgs : EventArgs
    {
        public NarrativeInteractableComponent InteractableComponent { get; set; }
        public int NewSlotIdx { get; set; }
    }

    /// <summary>
    /// NPC 交互组件，专用于 NPCCharacter。
    /// 适配 UE5 UNPCInteractionComponent：NPC 通过 ClaimSlot(bMarkTargeted:true) 预约槽位，
    /// 并订阅 OnTargetedSlotTaken 以便预约被抢时重新挑选。
    /// </summary>
    public class NPCInteractionComponent : NarrativeInteractionComponent
    {
        /// <summary>当目标槽位改变时触发（供 NPC 活动 BB 更新）</summary>
        public event EventHandler<TargetedInteractionSlotChangedEventArgs> OnTargetedInteractionSlotChanged;

        /// <summary>若槽位被偷是否自动寻找新槽位（对应 UE5 构造函数的 false）</summary>
        public bool bFindNewSlotIfSlotTaken { get; set; } = false;

        /// <summary>当前目标槽位索引（未目标化为 -1）。</summary>
        public int CurrentTargetedSlotIdx => InteractionSlotClaimHandle.IsValidHandle() ? InteractionSlotClaimHandle.HandleIndex : -1;

        /// <summary>目标化的 Interactable（即当前认领句柄的归属）。</summary>
        public NarrativeInteractableComponent TargetedInteractable => InteractionSlotClaimHandle.HandleOwner;

        // 本组件订阅过 OnTargetedSlotTaken 的可交互对象，用于精确退订（对应 UE5 AddUniqueDynamic 的反向操作）
        private readonly List<NarrativeInteractableComponent> _subscribedInteractables = new List<NarrativeInteractableComponent>();

        /// <summary>
        /// 目标化最佳交互槽位。对应 UE5 TargetBestInteractionSlot ——
        /// 不接纳已被他人 Targeted 的槽位，但仍接纳可偷取的 Occupied 槽位。
        /// </summary>
        public bool TargetBestInteractionSlot(NarrativeInteractableComponent interactable, bool bInFindNewSlotIfSlotTaken)
        {
            if (interactable == null) return false;

            int bestInteractionSlot = interactable.GetBestAvailableSlot(this, interactable.GetAvailableSlots(this, false));

            return TargetInteractionSlot(interactable, bestInteractionSlot, bInFindNewSlotIfSlotTaken);
        }

        /// <summary>
        /// 目标化指定槽位。对应 UE5 TargetInteractionSlot：
        /// 先释放旧认领，再通过 ClaimSlot(bMarkTargeted:true) 认领；认领失败返回 false。
        /// </summary>
        public bool TargetInteractionSlot(NarrativeInteractableComponent interactable, int index, bool bInFindNewSlotIfSlotTaken)
        {
            if (interactable == null) return false;
            if (index < 0) return false;

            bFindNewSlotIfSlotTaken = bInFindNewSlotIfSlotTaken;

            // 释放我们已持有的旧槽位
            if (InteractionSlotClaimHandle.IsValidHandle())
            {
                var currentInteractable = InteractionSlotClaimHandle.HandleOwner;
                if (currentInteractable != null)
                {
                    currentInteractable.UpdateSlotStatus(this, InteractionSlotClaimHandle.HandleIndex, EInteractionSlotStatus.Free);
                }
            }

            var handle = interactable.ClaimSlot(this, index, true);
            if (!handle.IsValidHandle()) return false;

            InteractionSlotClaimHandle = handle;
            SetOccupiedInteractable(interactable, handle.HandleIndex);

            // 认领成功后才监听槽位被抢，以便重新预约
            if (!_subscribedInteractables.Contains(interactable))
            {
                interactable.OnTargetedSlotTaken += OnTargetSlotTaken;
                _subscribedInteractables.Add(interactable);
            }

            OnTargetedInteractionSlotChanged?.Invoke(this, new TargetedInteractionSlotChangedEventArgs
            {
                InteractableComponent = interactable,
                NewSlotIdx = handle.HandleIndex
            });
            return true;
        }

        /// <summary>
        /// 当前目标槽位被他人抢走时调用。对应 UE5 OnTargetSlotTaken：
        /// 必须确认是别人抢的、且抢的正是我们目标化的槽位。
        /// </summary>
        public void OnTargetSlotTaken(int slot, NarrativeInteractionComponent stealerComp, NarrativeInteractableComponent interactableComp)
        {
            if (stealerComp == this) return;
            if (!InteractionSlotClaimHandle.IsValidHandle()) return;
            if (InteractionSlotClaimHandle.HandleIndex != slot) return;
            if (InteractionSlotClaimHandle.HandleOwner != interactableComp) return;

            var currentInteractable = InteractionSlotClaimHandle.HandleOwner;
            if (currentInteractable == null) return;

            // 槽位已被抢走：作废句柄（槽位不再属于我们，绝不能去释放它）
            InteractionSlotClaimHandle = InteractionSlotClaimHandle.InvalidHandle();

            if (bFindNewSlotIfSlotTaken)
            {
                TargetBestInteractionSlot(currentInteractable, true);
            }
            else
            {
                SetOccupiedInteractable(null, -1);
                OnTargetedInteractionSlotChanged?.Invoke(this, new TargetedInteractionSlotChangedEventArgs
                {
                    InteractableComponent = currentInteractable,
                    NewSlotIdx = -1
                });
            }
        }

        private void UnsubscribeTargetEvents()
        {
            for (int i = _subscribedInteractables.Count - 1; i >= 0; i--)
            {
                var target = _subscribedInteractables[i];
                if (target != null) target.OnTargetedSlotTaken -= OnTargetSlotTaken;
            }
            _subscribedInteractables.Clear();
        }

        public override void OnDisable()
        {
            UnsubscribeTargetEvents();
            base.OnDisable();
        }
    }
}

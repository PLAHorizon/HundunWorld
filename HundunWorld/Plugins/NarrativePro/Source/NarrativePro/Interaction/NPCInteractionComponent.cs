using System;
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
    /// 适配 UE5 UNPCInteractionComponent。
    /// </summary>
    public class NPCInteractionComponent : NarrativeInteractionComponent
    {
        /// <summary>当目标槽位改变时触发（供 NPC 活动 BB 更新）</summary>
        public event EventHandler<TargetedInteractionSlotChangedEventArgs> OnTargetedInteractionSlotChanged;

        /// <summary>若槽位被偷是否自动寻找新槽位</summary>
        public bool bFindNewSlotIfSlotTaken { get; set; } = true;

        /// <summary>当前目标槽位索引（未目标化为 -1）</summary>
        public int CurrentTargetedSlotIdx { get; protected set; } = -1;

        /// <summary>目标化的 Interactable</summary>
        public NarrativeInteractableComponent TargetedInteractable { get; protected set; }

        /// <summary>开始目标化最佳交互槽位。返回是否成功。</summary>
        public bool TargetBestInteractionSlot(NarrativeInteractableComponent interactable, bool bFindNewSlotIfSlotTaken)
        {
            if (interactable == null) return false;

            this.bFindNewSlotIfSlotTaken = bFindNewSlotIfSlotTaken;

            var availableSlots = interactable.GetAvailableSlots(this, true, true);
            if (availableSlots.Count == 0)
            {
                NarrativeLog.Log($"[NPCInteraction] Interactable 无可用槽位");
                return false;
            }

            int bestSlot = interactable.GetBestAvailableSlot(this, availableSlots);
            if (bestSlot < 0)
            {
                NarrativeLog.Log($"[NPCInteraction] 未找到最佳槽位");
                return false;
            }

            return TargetInteractionSlot(interactable, bestSlot, bFindNewSlotIfSlotTaken);
        }

        /// <summary>目标化指定槽位。返回是否成功。</summary>
        public bool TargetInteractionSlot(NarrativeInteractableComponent interactable, int index, bool bAutoUpdateIfSlotStolen)
        {
            if (interactable == null) return false;
            if (index < 0 || index >= interactable.InteractionSlots.Count)
            {
                NarrativeLog.LogWarning($"[NPCInteraction] 槽位索引越界: {index}");
                return false;
            }

            this.bFindNewSlotIfSlotTaken = bAutoUpdateIfSlotStolen;

            // 释放旧目标
            if (TargetedInteractable != null && CurrentTargetedSlotIdx >= 0)
            {
                TargetedInteractable.UpdateSlotStatus(this, CurrentTargetedSlotIdx, EInteractionSlotStatus.Free);
            }

            // 注册到 interactable 的 OnTargetedSlotTaken 事件
            // 注意：Flax 中事件需要先解绑旧的再绑定新的，防止多次订阅
            if (TargetedInteractable != interactable)
            {
                if (TargetedInteractable != null)
                {
                    TargetedInteractable.OnTargetedSlotTaken -= OnTargetSlotTaken;
                }
                interactable.OnTargetedSlotTaken += OnTargetSlotTaken;
                TargetedInteractable = interactable;
            }

            // 目标化槽位（标记为 Targeted）
            interactable.UpdateSlotStatus(this, index, EInteractionSlotStatus.Targeted);
            CurrentTargetedSlotIdx = index;

            // 触发事件
            OnTargetedInteractionSlotChanged?.Invoke(this, new TargetedInteractionSlotChangedEventArgs
            {
                InteractableComponent = interactable,
                NewSlotIdx = index
            });

            return true;
        }

        /// <summary>当前目标槽位被偷时调用。</summary>
        public void OnTargetSlotTaken(int slot, NarrativeInteractionComponent stealerComp, NarrativeInteractableComponent interactableComp)
        {
            if (slot != CurrentTargetedSlotIdx) return;
            if (interactableComp != TargetedInteractable) return;

            int oldSlot = CurrentTargetedSlotIdx;
            CurrentTargetedSlotIdx = -1;

            if (bFindNewSlotIfSlotTaken)
            {
                // 尝试寻找新槽位
                bool foundNew = TargetBestInteractionSlot(interactableComp, bFindNewSlotIfSlotTaken);
                if (!foundNew)
                {
                    NarrativeLog.Log($"[NPCInteraction] 槽位被偷且无新槽位可用");
                }
            }
            else
            {
                OnTargetedInteractionSlotChanged?.Invoke(this, new TargetedInteractionSlotChangedEventArgs
                {
                    InteractableComponent = interactableComp,
                    NewSlotIdx = -1
                });
            }
        }

        public override void OnDisable()
        {
            // 清理事件订阅
            if (TargetedInteractable != null)
            {
                TargetedInteractable.OnTargetedSlotTaken -= OnTargetSlotTaken;
                if (CurrentTargetedSlotIdx >= 0)
                {
                    TargetedInteractable.UpdateSlotStatus(this, CurrentTargetedSlotIdx, EInteractionSlotStatus.Free);
                }
                TargetedInteractable = null;
                CurrentTargetedSlotIdx = -1;
            }
            base.OnDisable();
        }
    }
}

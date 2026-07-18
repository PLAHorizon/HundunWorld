using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互槽位状态。适配 UE5 EInteractionSlotStatus。
    /// </summary>
    public enum EInteractionSlotStatus
    {
        /// <summary>空闲</summary>
        Free = 0,
        /// <summary>已被瞄准（未实际占用，可用于 NPC 准备前往）</summary>
        Targeted = 1,
        /// <summary>已占用（正在交互中）</summary>
        Occupied = 2
    }

    /// <summary>
    /// 活跃交互槽位。适配 UE5 FActiveInteractionSlot。
    /// </summary>
    [Serializable]
    public class ActiveInteractionSlot
    {
        public EInteractionSlotStatus SlotStatus { get; set; } = EInteractionSlotStatus.Free;

        /// <summary>占用此槽位的交互组件</summary>
        public NarrativeInteractionComponent SlotUser { get; set; }
    }

    /// <summary>
    /// 调试可视化槽位。适配 UE5 FInteractionDebugVisualizeSlot。
    /// </summary>
    [Serializable]
    public class InteractionDebugVisualizeSlot
    {
        public Transform SlotDebugTransform { get; set; } = Transform.Identity;
        public Color SlotDebugColor { get; set; } = Color.Black;
        public string SlotDebugString { get; set; } = "";
    }

    /// <summary>
    /// 交互槽位行为。挂到槽位上的自定义行为类。
    /// 适配 UE5 UInteractionSlotBehavior。GAS 相关以字符串 ID 引用占位。
    /// </summary>
    [Serializable]
    public abstract class InteractionSlotBehavior
    {
        /// <summary>触发此槽位交互时启动的能力 ID（对应 UE 的 UNarrativeInteractAbility）</summary>
        public string SlotInteractBehaviorId { get; set; } = "";

        /// <summary>提示用户结束交互的文本</summary>
        public string FinishInteractText { get; set; } = "";

        /// <summary>默认是否可被偷窃，最终由 IsStealable 决定</summary>
        public bool bIsStealableByDefault { get; set; } = false;

        /// <summary>若非空，仅允许此列表中的交互组件使用此槽位（用于任务锁定）</summary>
        public List<NarrativeInteractionComponent> AllowedInteractors { get; set; } = new List<NarrativeInteractionComponent>();

        /// <summary>获取此槽位的调试可视化信息。</summary>
        public virtual List<InteractionDebugVisualizeSlot> GetDebugSlots(Transform slotTransform, Transform ownerTransform)
        {
            return new List<InteractionDebugVisualizeSlot>();
        }

        /// <summary>此槽位是否可被偷窃。</summary>
        public virtual bool IsStealable(int ourSlot, NarrativeInteractableComponent ourInteractable, NarrativeInteractionComponent interactor)
        {
            return bIsStealableByDefault;
        }

        /// <summary>此槽位是否可被指定交互者使用。</summary>
        public virtual bool IsUsable(int ourSlot, NarrativeInteractableComponent ourInteractable, NarrativeInteractionComponent interactor)
        {
            if (AllowedInteractors == null || AllowedInteractors.Count == 0) return true;
            return AllowedInteractors.Contains(interactor);
        }
    }

    /// <summary>
    /// 交互槽位配置。适配 UE5 FInteractionSlotConfig。
    /// </summary>
    [Serializable]
    public class InteractionSlotConfig
    {
        /// <summary>槽位标签（可选）</summary>
        public GameplayTag SlotTag { get; set; } = GameplayTag.None;

        /// <summary>调试颜色</summary>
        public Color DebugColor { get; set; } = Color.Green;

        /// <summary>关联槽位索引列表（同一逻辑槽位的多个物理位置）</summary>
        public List<int> LinkedSlots { get; set; } = new List<int>();

        /// <summary>槽位世界变换（NPC 移动目标、玩家 motion warp 目标）</summary>
        public Transform SlotTransform { get; set; } = Transform.Identity;

        /// <summary>槽位行为实例</summary>
        public InteractionSlotBehavior SlotInteractBehavior { get; set; }
    }

    /// <summary>
    /// 交互槽位占用句柄。适配 UE5 FInteractionSlotClaimHandle。
    /// </summary>
    [Serializable]
    public class InteractionSlotClaimHandle
    {
        public int HandleIndex { get; set; } = -1;
        public NarrativeInteractableComponent HandleOwner { get; set; }

        public InteractionSlotClaimHandle() { }

        public InteractionSlotClaimHandle(int inHandleIndex, NarrativeInteractableComponent inHandleOwner)
        {
            HandleIndex = inHandleIndex;
            HandleOwner = inHandleOwner;
        }

        public bool IsValidHandle()
        {
            return HandleIndex != -1 && HandleOwner != null;
        }

        public static InteractionSlotClaimHandle InvalidHandle()
        {
            return new InteractionSlotClaimHandle { HandleIndex = -1 };
        }
    }

    /// <summary>
    /// 可交互组件，挂到可被交互的 Actor 上。
    /// 适配 UE5 UNarrativeInteractableComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// 包含交互槽位系统（轻量级 SmartObject 实现，支持 NPC/玩家交互）。
    /// </summary>
    public class NarrativeInteractableComponent : Script
    {
        protected NarrativeInteractionComponent _friendInteractionComp;

        /// <summary>交互槽位配置列表</summary>
        public List<InteractionSlotConfig> InteractionSlots { get; set; } = new List<InteractionSlotConfig>();

        /// <summary>活跃交互槽位状态列表</summary>
        public List<ActiveInteractionSlot> SlotStatuses { get; set; } = new List<ActiveInteractionSlot>();

        /// <summary>玩家需按住交互键的时长（秒）</summary>
        public float InteractionTime { get; set; } = 0f;

        /// <summary>最大交互距离</summary>
        public float InteractionDistance { get; set; } = 200f;

        /// <summary>玩家注视时显示的名称</summary>
        public string InteractableNameText { get; set; } = "";

        /// <summary>交互动词（如"坐"、"吃"、"点燃"）</summary>
        public string InteractableActionText { get; set; } = "";

        /// <summary>聚焦时叠加到 Owner 网格上的材质资源路径</summary>
        public string FocusedOverlayMaterialPath { get; set; } = "";

        // 事件
        public event Action<Actor, NarrativeInteractionComponent> OnBeginInteracted;
        public event Action<Actor, NarrativeInteractionComponent> OnEndInteracted;
        public event Action<Actor, NarrativeInteractionComponent> OnBeginFocus;
        public event Action<Actor, NarrativeInteractionComponent> OnEndFocus;
        public event Action<Actor, NarrativeInteractionComponent> OnInteracted;
        public event Action<int, NarrativeInteractionComponent, NarrativeInteractableComponent> OnTargetedSlotTaken;

        public override void OnEnable()
        {
            base.OnEnable();
            // 初始化槽位状态列表
            if (SlotStatuses == null) SlotStatuses = new List<ActiveInteractionSlot>();
            while (SlotStatuses.Count < InteractionSlots.Count)
            {
                SlotStatuses.Add(new ActiveInteractionSlot());
            }
            // 注册到交互子系统
            InteractionSubsystem.Instance?.CacheInteractable(this);
        }

        public override void OnDisable()
        {
            // 注销交互子系统
            InteractionSubsystem.Instance?.UncacheInteractable(this);
            base.OnDisable();
        }

        /// <summary>获取交互名称文本（可被子类覆盖以动态生成）。</summary>
        public virtual string GetInteractableNameText(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            return InteractableNameText;
        }

        /// <summary>获取交互动作文本（可被子类覆盖）。</summary>
        public virtual string GetInteractableActionText(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            return InteractableActionText;
        }

        /// <summary>设置交互名称文本。</summary>
        public void SetInteractableNameText(string newNameText)
        {
            InteractableNameText = newNameText;
        }

        /// <summary>设置交互动作文本。</summary>
        public void SetInteractableActionText(string newActionText)
        {
            InteractableActionText = newActionText;
        }

        /// <summary>返回最接近 claimer 的可用槽位索引，找不到返回 -1。</summary>
        public virtual int GetBestAvailableSlot(NarrativeInteractionComponent claimer, List<int> slotsToCheck)
        {
            if (claimer == null) return -1;
            Vector3 claimerPos = claimer.GetOwnerPosition();

            int bestIdx = -1;
            float bestDist = float.MaxValue;

            List<int> candidates = slotsToCheck ?? GetAvailableSlots(claimer, true, true);

            foreach (int idx in candidates)
            {
                if (idx < 0 || idx >= InteractionSlots.Count) continue;
                if (SlotStatuses[idx].SlotStatus == EInteractionSlotStatus.Occupied) continue;

                Vector3 slotPos = Actor.Transform.LocalToWorld(InteractionSlots[idx].SlotTransform.Translation);
                float d = Vector3.Distance(claimerPos, slotPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = idx;
                }
            }
            return bestIdx;
        }

        /// <summary>返回 claimer 可用的所有槽位索引。</summary>
        public List<int> GetAvailableSlots(NarrativeInteractionComponent claimer, bool bIncludeTargeted = true, bool bIncludeStealable = true)
        {
            var result = new List<int>();
            for (int i = 0; i < SlotStatuses.Count; i++)
            {
                var status = SlotStatuses[i].SlotStatus;
                if (status == EInteractionSlotStatus.Free)
                {
                    result.Add(i);
                }
                else if (status == EInteractionSlotStatus.Targeted && bIncludeTargeted)
                {
                    result.Add(i);
                }
                else if (status == EInteractionSlotStatus.Occupied && bIncludeStealable)
                {
                    // 检查是否可偷窃
                    var behavior = i < InteractionSlots.Count ? InteractionSlots[i].SlotInteractBehavior : null;
                    if (behavior != null && behavior.IsStealable(i, this, claimer))
                    {
                        result.Add(i);
                    }
                }
            }
            return result;
        }

        /// <summary>返回指定索引处的所有关联槽位（含自身）。</summary>
        public List<int> GetSlotsAtIndex(int slotIndex)
        {
            var result = new List<int> { slotIndex };
            if (slotIndex >= 0 && slotIndex < InteractionSlots.Count)
            {
                var cfg = InteractionSlots[slotIndex];
                if (cfg.LinkedSlots != null)
                {
                    foreach (int linked in cfg.LinkedSlots)
                    {
                        if (!result.Contains(linked)) result.Add(linked);
                    }
                }
            }
            return result;
        }

        /// <summary>占用槽位，返回占用句柄。</summary>
        public InteractionSlotClaimHandle ClaimSlot(NarrativeInteractionComponent claimer, int slotToClaimIdx, bool bMarkTargeted = false)
        {
            if (slotToClaimIdx < 0 || slotToClaimIdx >= SlotStatuses.Count) return InteractionSlotClaimHandle.InvalidHandle();

            // 释放所有关联槽位
            foreach (int linked in GetSlotsAtIndex(slotToClaimIdx))
            {
                if (linked >= 0 && linked < SlotStatuses.Count)
                {
                    SlotStatuses[linked].SlotUser = claimer;
                    SlotStatuses[linked].SlotStatus = bMarkTargeted ? EInteractionSlotStatus.Targeted : EInteractionSlotStatus.Occupied;
                }
            }
            return new InteractionSlotClaimHandle(slotToClaimIdx, this);
        }

        /// <summary>更新槽位状态。</summary>
        public void UpdateSlotStatus(NarrativeInteractionComponent claimer, int slotIndex, EInteractionSlotStatus newStatus)
        {
            if (slotIndex < 0 || slotIndex >= SlotStatuses.Count) return;
            foreach (int linked in GetSlotsAtIndex(slotIndex))
            {
                if (linked >= 0 && linked < SlotStatuses.Count)
                {
                    if (newStatus == EInteractionSlotStatus.Free)
                    {
                        SlotStatuses[linked].SlotUser = null;
                    }
                    else
                    {
                        SlotStatuses[linked].SlotUser = claimer;
                    }
                    SlotStatuses[linked].SlotStatus = newStatus;
                }
            }
        }

        /// <summary>返回指定槽位的配置。</summary>
        public InteractionSlotConfig GetConfigAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= InteractionSlots.Count) return null;
            return InteractionSlots[slotIndex];
        }

        /// <summary>是否有可用槽位。</summary>
        public bool HasSlotAvailable(bool bAllowTargeted)
        {
            foreach (var s in SlotStatuses)
            {
                if (s.SlotStatus == EInteractionSlotStatus.Free) return true;
                if (bAllowTargeted && s.SlotStatus == EInteractionSlotStatus.Targeted) return true;
            }
            return false;
        }

        /// <summary>获取交互对象的包围盒。</summary>
        public BoundingBox GetInteractableBounds()
        {
            var actor = Actor;
            if (actor == null) return new BoundingBox(Vector3.Zero, Vector3.Zero);
            return actor.Box;
        }

        // 内部回调（由 PlayerInteractionComponent 触发）
        internal void BeginFocus(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnBeginFocus?.Invoke(interactor, interactionComp);
        }

        internal void EndFocus(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnEndFocus?.Invoke(interactor, interactionComp);
        }

        internal void BeginInteract(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnBeginInteracted?.Invoke(interactor, interactionComp);
        }

        internal void EndInteract(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnEndInteracted?.Invoke(interactor, interactionComp);
        }

        public virtual bool Interact(Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            OnInteracted?.Invoke(interactor, interactionComp);
            return true;
        }

        /// <summary>是否可交互（可由子类覆盖）。</summary>
        public virtual bool CanInteract(Actor interactor, NarrativeInteractionComponent interactionComp, out string errorText)
        {
            errorText = "";
            return true;
        }

        /// <summary>槽位状态改变回调（替代 UE OnRep_SlotStatuses）。</summary>
        protected virtual void OnSlotStatusesChanged(List<ActiveInteractionSlot> oldStatuses)
        {
        }

        /// <summary>触发槽位被偷事件。</summary>
        internal void FireTargetedSlotTaken(int slot, NarrativeInteractionComponent interactionComp, NarrativeInteractableComponent interactableComp)
        {
            OnTargetedSlotTaken?.Invoke(slot, interactionComp, interactableComp);
        }
    }
}

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互静态工具库。对应 UE5 UInteractionFunctionLibrary
    /// （UE5 侧为空壳，实际逻辑都在组件上；此处收敛端口需要的跨组件查询，
    /// 供 Widgets/CommonUI/AI 复用，避免各自重复实现同一段解析逻辑）。
    /// </summary>
    public static class InteractionFunctionLibrary
    {
        /// <summary>
        /// 取 Actor（或其控制器）上的交互组件。对应 UE5 从 Pawn/Controller 两侧解析拥有的模式。
        /// </summary>
        public static NarrativeInteractionComponent GetInteractionComponent(Actor owner)
        {
            if (owner == null) return null;

            var comp = owner.GetScript<NarrativeInteractionComponent>();
            if (comp != null) return comp;

            // Actor 自身可能只是 Pawn，交互组件挂在控制器 Actor 上
            foreach (var script in owner.GetScripts<NarrativeInteractionComponent>())
            {
                if (script != null) return script;
            }

            return null;
        }

        /// <summary>取 Actor 上的可交互组件。</summary>
        public static NarrativeInteractableComponent GetInteractableComponent(Actor owner)
        {
            if (owner == null) return null;
            return owner.GetScript<NarrativeInteractableComponent>();
        }

        /// <summary>该 Actor（或其控制器）当前是否正在占用某个交互槽位。</summary>
        public static bool IsOccupyingInteractable(Actor owner)
        {
            var comp = GetInteractionComponent(owner);
            return comp != null && comp.HasOccupiedInteractable();
        }

        /// <summary>可交互对象是否还有空闲槽位。</summary>
        public static bool HasFreeSlot(NarrativeInteractableComponent interactable, bool bAllowTargeted)
        {
            if (interactable == null) return false;
            if (interactable.InteractionSlots.Count == 0) return true;
            return interactable.HasSlotAvailable(bAllowTargeted);
        }

        /// <summary>
        /// 槽位的世界空间位置（槽位变换定义在可交互对象的本地空间）。
        /// 对应 UE5 的 (SlotTransform * GetOwner()->GetActorTransform()).GetLocation()。
        /// </summary>
        public static bool TryGetSlotWorldLocation(NarrativeInteractableComponent interactable, int slotIndex, out Vector3 worldLocation)
        {
            worldLocation = Vector3.Zero;
            if (interactable == null || interactable.Actor == null) return false;
            if (slotIndex < 0 || slotIndex >= interactable.InteractionSlots.Count) return false;

            var slot = interactable.InteractionSlots[slotIndex];
            worldLocation = interactable.Actor.Transform.LocalToWorld(slot.SlotTransform.Translation);
            return true;
        }

        /// <summary>槽位的世界空间朝向（对应 UE5 的 SlotTransform 旋转分量）。</summary>
        public static bool TryGetSlotWorldRotation(NarrativeInteractableComponent interactable, int slotIndex, out Quaternion worldRotation)
        {
            worldRotation = Quaternion.Identity;
            if (interactable == null || interactable.Actor == null) return false;
            if (slotIndex < 0 || slotIndex >= interactable.InteractionSlots.Count) return false;

            var slot = interactable.InteractionSlots[slotIndex];
            // Flax 的 Actor / Transform 用 Orientation 表达朝向（UE5 的 GetActorTransform().Rotator()）
            worldRotation = interactable.Actor.Orientation * slot.SlotTransform.Orientation;
            return true;
        }

        /// <summary>槽位的世界空间变换。</summary>
        public static bool TryGetSlotWorldTransform(NarrativeInteractableComponent interactable, int slotIndex, out Transform worldTransform)
        {
            worldTransform = Transform.Identity;
            if (!TryGetSlotWorldLocation(interactable, slotIndex, out var location)) return false;
            if (!TryGetSlotWorldRotation(interactable, slotIndex, out var rotation)) return false;

            var ownerScale = interactable.Actor.Scale;
            var slot = interactable.InteractionSlots[slotIndex].SlotTransform;
            worldTransform = new Transform
            {
                Translation = location,
                Orientation = rotation,
                Scale = new Vector3(
                    slot.Scale.X * ownerScale.X,
                    slot.Scale.Y * ownerScale.Y,
                    slot.Scale.Z * ownerScale.Z)
            };
            return true;
        }

        /// <summary>
        /// 组合交互提示文本（"名称 · 动词"），供 HUD / 交互 Widget 复用。
        /// 对应 UE5 交互提示里 GetInteractableNameText + GetInteractableActionText 的呈现。
        /// </summary>
        public static string BuildInteractPrompt(NarrativeInteractableComponent interactable, Actor interactor, NarrativeInteractionComponent interactionComp)
        {
            if (interactable == null) return string.Empty;

            string name = interactable.GetInteractableNameText(interactor, interactionComp) ?? string.Empty;
            string action = interactable.GetInteractableActionText(interactor, interactionComp) ?? string.Empty;

            if (name.Length == 0) return action;
            if (action.Length == 0) return name;
            return action + " " + name;
        }

        /// <summary>
        /// 指定交互者当前的可用槽位集合（用于 AI 侧挑选目标槽位）。
        /// </summary>
        public static List<int> GetSlotsUsableBy(NarrativeInteractableComponent interactable, NarrativeInteractionComponent interactor, bool bIncludeTargeted, bool bIncludeStealable)
        {
            if (interactable == null) return new List<int>();
            return interactable.GetAvailableSlots(interactor, bIncludeTargeted, bIncludeStealable);
        }
    }
}

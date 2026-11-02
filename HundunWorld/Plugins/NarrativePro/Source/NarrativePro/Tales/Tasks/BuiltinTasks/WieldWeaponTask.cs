using System;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「把指定武器拿在手里」的任务条目。对应 UE5 蓝图任务 <c>BPT_WieldWeapon</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_WieldWeapon.uasset）。
    /// <para>
    /// 二进制证据：<c>CallFunc_GetEquipmentComponent_ReturnValue</c> +
    /// <c>CallFunc_GetWieldedWeaponAtSlot_ReturnValue</c> 逐个持握槽位查询，槽位候选来自
    /// <c>CallFunc_BreakGameplayTagContainer_GameplayTags</c>（武器 CDO 的
    /// <c>EquippableSlots</c> 容器展开成标签数组后 <c>ForEachLoop</c>），物品类型用
    /// <c>ClassIsChildOf</c> 比对；命中走 <c>Add_IntInt</c>。
    /// </para>
    /// <para>
    /// 端口差异：UE5 靠装备组件的持握广播驱动，端口 <see cref="EquipmentComponent"/> 持握
    /// （<c>WieldWeapon</c>）不发 <c>OnEquipmentChanged</c>，故用 <see cref="NarrativeTask.TickInterval"/>
    /// 轮询 <c>GetWieldedWeaponAtSlot</c> / <see cref="WeaponItem.IsWielded"/>；
    /// 判定顺序与 UE5 一致（先看指定/声明槽位，再看武器自述状态）。
    /// </para>
    /// </summary>
    [Serializable]
    public class WieldWeaponTask : NarrativeTask
    {
        /// <summary>要持握的武器类 ID。对应 UE5 <c>WeaponClass</c>。</summary>
        public string WeaponClassId { get; set; } = "";

        /// <summary>指定持握槽位；留空则遍历武器声明的槽位。对应 UE5 <c>Slot</c>。</summary>
        public string WieldSlotTag { get; set; } = "";

        public string WeaponDisplayName { get; set; } = "";

        /// <summary>开始时代玩家持握（默认 false，等玩家按键换武）。</summary>
        public bool bWieldOnBegin { get; set; } = false;

        protected override void OnBeginTask()
        {
            if (string.IsNullOrEmpty(WeaponClassId))
            {
                NarrativeLog.LogWarning("WieldWeaponTask: WeaponClassId 为空，无持握目标");
                return;
            }

            if (IsWielding())
            {
                CompleteTask();
                return;
            }

            if (bWieldOnBegin)
            {
                var inventory = BuiltinTaskSupport.GetInventory(this);
                if (inventory == null || !inventory.WieldWeaponOfClass(WeaponClassId))
                    NarrativeLog.LogWarning($"WieldWeaponTask: 代为持握 '{WeaponClassId}' 失败");
            }

            TickInterval = 0.25f;
        }

        protected override void TickTask()
        {
            if (IsWielding())
                CompleteTask();
        }

        /// <summary>
        /// 持握判定：UE5 的「遍历武器槽位 → GetWieldedWeaponAtSlot」在端口等价实现为
        /// 先查装备组件表（指定槽位或武器自述槽位），再看武器自身 <c>WieldedSlot</c>。
        /// </summary>
        private bool IsWielding()
        {
            var inventory = BuiltinTaskSupport.GetInventory(this);
            if (!(inventory?.FindItemOfClass(WeaponClassId) is WeaponItem weapon)) return false;

            var equipment = BuiltinTaskSupport.GetEquipment(this);
            var wanted = new GameplayTag(WieldSlotTag);

            if (wanted.IsValid())
            {
                var atSlot = equipment?.GetWieldedWeaponAtSlot(wanted);
                return atSlot != null && ReferenceEquals(atSlot, weapon);
            }

            if (weapon.IsWielded())
            {
                if (equipment == null) return true;
                var held = equipment.GetWieldedWeaponAtSlot(weapon.WieldedSlot);
                return held == null || ReferenceEquals(held, weapon);
            }

            if (equipment == null) return false;

            // 武器自述未持握时，装备组件表里仍挂着它也算（两边状态在端口不完全同步）。
            foreach (var kvp in equipment.WieldedWeapons)
            {
                if (ReferenceEquals(kvp.Value, weapon)) return true;
            }

            return false;
        }

        protected override void OnEndTask()
        {
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            string name = !string.IsNullOrEmpty(WeaponDisplayName) ? WeaponDisplayName : WeaponClassId;
            return $"手持 {name}";
        }
    }
}

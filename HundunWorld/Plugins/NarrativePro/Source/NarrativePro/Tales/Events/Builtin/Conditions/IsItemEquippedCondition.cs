using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 物品已装备条件。对应 UE5 蓝图资产 <c>BPC_IsItemEquipped</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetComponentByClass</c>（EquipmentComponent）→
    /// <c>CallFunc_GetEquippedItemAtSlot</c> → <c>CallFunc_GetObjectClass</c> +
    /// <c>CallFunc_EqualEqual_ClassClass</c> 与蓝图变量 <c>Item</c> 比对，槽位取 <c>Slot</c>/<c>CurrentSlot</c>。
    /// <c>端口差异</c>：Flax 端口物品以 <c>ItemClassId</c> 字符串标识（同 <c>HasItemCondition</c>），
    /// 端口用 <c>EquipmentComponent.GetEquippedItem(GameplayTag)</c> 取槽位物品后比对 ItemClassId。
    /// </para>
    /// </summary>
    public class IsItemEquippedCondition : NarrativeCondition
    {
        /// <summary>要检查的物品类标识。对应 UE5 蓝图变量 <c>Item</c>（物品类）。</summary>
        public string ItemClassId { get; set; } = "";

        /// <summary>限定装备槽（GameplayTag）；为空则检查全部已装备槽位。对应 UE5 蓝图变量 <c>Slot</c>。</summary>
        public string Slot { get; set; } = "";

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(ItemClassId))
            {
                NarrativeLog.LogWarning("IsItemEquippedCondition: ItemClassId 为空，条件按失败处理");
                return false;
            }

            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var equipment = character?.GetEquipmentComponent();
            if (equipment == null)
                return false;

            var slotTag = new GameplayTag(Slot);
            if (slotTag.IsValid())
            {
                var equipped = equipment.GetEquippedItem(slotTag);
                return equipped != null && equipped.ItemClassId == ItemClassId;
            }

            foreach (var item in equipment.EquippedItems.Values)
            {
                if (item != null && item.ItemClassId == ItemClassId)
                    return true;
            }
            return false;
        }

        public override string GetGraphDisplayText() => $"Is Item Equipped: {ItemClassId}{(string.IsNullOrEmpty(Slot) ? "" : $" @ {Slot}")}";
    }
}

using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 已掏出武器条件。对应 UE5 蓝图资产 <c>BPC_HasWeaponOut</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 DynamicCast 到 NarrativeCharacter →
    /// <c>CallFunc_IsValid</c> 检查 <c>EquippedWeapon</c>（EquipmentComponent 的持握武器）是否有效。
    /// <c>端口差异</c>：Flax 的 <c>EquipmentComponent</c> 以 <c>WieldedWeapons</c> 槽位表建模持握，
    /// 没有单一 EquippedWeapon 引用；端口判「任一持握槽位有武器」。
    /// </para>
    /// </summary>
    public class HasWeaponOutCondition : NarrativeCondition
    {
        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var equipment = character?.GetEquipmentComponent();
            if (equipment == null)
                return false;

            foreach (var weapon in equipment.WieldedWeapons.Values)
            {
                if (weapon != null)
                    return true;
            }
            return false;
        }

        public override string GetGraphDisplayText() => "Has Weapon Out";
    }
}

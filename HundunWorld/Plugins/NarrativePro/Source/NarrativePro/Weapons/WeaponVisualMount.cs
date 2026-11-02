using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器视觉挂载入口。对应 UE5 持握武器时由
    /// <c>UNarrativeCharacterVisual::AddWeaponVisual</c> 生成 <c>AWeaponVisual</c> 并按
    /// <c>UWeaponItem::GetWeaponVisualAttachBone</c> 挂到骨骼上。
    /// <para>
    /// 端口里这条链断在两处：一是从没有人调用 <c>AddWeaponVisual</c>（角色视觉里的实现是活的但没有调用方），
    /// 二是 <c>NarrativeCombatAbility.GetAbilityWeaponVisualScript</c> 只扫角色 Actor 自身的脚本，
    /// 而视觉脚本在生成的武器预制体上。本类把两端接起来：调用角色视觉生成预制体，
    /// 再把生成的 <see cref="WeaponVisual"/> 与来源武器/持握槽位绑定，使其可被近战攻击反查。
    /// </para>
    /// </summary>
    public static class WeaponVisualMount
    {
        /// <summary>武器 → 已绑定的视觉实例（用于收起时回查并清理）。</summary>
        private static readonly Dictionary<WeaponItem, WeaponVisual> _boundVisuals =
            new Dictionary<WeaponItem, WeaponVisual>(ReferenceEqualityComparer<WeaponItem>.Instance);

        /// <summary>
        /// 持握武器时挂载并绑定武器视觉。
        /// 武器没有配置视觉预制体时返回 null（近战攻击会退回能力自身的扇形扫查，不会因此打不出伤害）。
        /// </summary>
        public static WeaponVisual Attach(WeaponItem weapon, NarrativePro.UnrealFramework.NarrativeCharacter character)
        {
            if (weapon == null || character == null || character.Actor == null) return null;

            var characterActor = character.Actor;
            var visualScript = characterActor.GetScript<NarrativePro.Character.NarrativeCharacterVisual>();
            if (visualScript == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning(
                    $"[WeaponVisualMount] 角色 '{characterActor.Name}' 没有 NarrativeCharacterVisual，无法挂载武器视觉。");
                return null;
            }

            // 已经绑定过（反复穿戴/换槽位）：只刷新挂载状态，不重复生成预制体
            var existing = WeaponVisual.FindVisualForWeapon(weapon, characterActor);
            if (existing != null)
            {
                ApplyBinding(existing, weapon, character, visualScript);
                return existing;
            }

            if (string.IsNullOrEmpty(weapon.WeaponVisualPath))
            {
                NarrativePro.Core.NarrativeLog.Log(
                    $"[WeaponVisualMount] 武器 '{weapon.DisplayName}' 未配置 weaponVisualPath，近战命中退回能力扇扫。");
                return null;
            }

            // 调用角色视觉既有实现：加载 Prefab 并挂到角色视觉根节点下
            if (!visualScript.AddWeaponVisual(weapon.WeaponVisualPath))
            {
                NarrativePro.Core.NarrativeLog.LogWarning(
                    $"[WeaponVisualMount] 生成武器视觉失败：'{weapon.WeaponVisualPath}'。");
                return null;
            }

            var spawned = WeaponVisual.TakeUnboundVisual(characterActor);
            if (spawned == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning(
                    $"[WeaponVisualMount] 预制体 '{weapon.WeaponVisualPath}' 生成成功但上面没有 WeaponVisual 脚本，" +
                    "近战无法按武器反查视觉（会退回能力扇扫）。");
                return null;
            }

            ApplyBinding(spawned, weapon, character, visualScript);
            _boundVisuals[weapon] = spawned;
            return spawned;
        }

        /// <summary>收起武器时解绑视觉：清掉武器来源，使后续攻击不再反查到它。</summary>
        public static void Detach(WeaponItem weapon)
        {
            if (weapon == null) return;
            if (!_boundVisuals.TryGetValue(weapon, out var visual))
            {
                visual = WeaponVisual.FindVisualForWeapon(weapon, null);
            }
            _boundVisuals.Remove(weapon);
            if (visual == null) return;

            visual.WeaponOwner = null;
            visual.AttachState.WeaponOwner = null;
            visual.OnHolstered();
        }

        /// <summary>把挂载信息一次性写入视觉（对应 UE5 复制 AttachState 后 OnRep_AttachState）。</summary>
        private static void ApplyBinding(
            WeaponVisual visual,
            WeaponItem weapon,
            NarrativePro.UnrealFramework.NarrativeCharacter character,
            NarrativePro.Character.NarrativeCharacterVisual visualScript)
        {
            visual.WeaponOwner = weapon;
            visual.CharacterOwner = character;
            visual.VisualOwner = visualScript;

            visual.AttachState.WeaponOwner = weapon;
            visual.AttachState.CharOwner = character;
            visual.AttachState.VisualOwner = visualScript;
            visual.AttachState.EquippedSlot = weapon.CurrentSlot;
            visual.AttachState.WieldedSlot = weapon.WieldedSlot;

            visual.OnWielded();
        }
    }

    /// <summary>
    /// 引用相等比较器：物品对象没有稳定的自定义 Equals，
    /// 用默认引用语义避免 <see cref="object"/> 值相等造成的错绑。
    /// </summary>
    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}

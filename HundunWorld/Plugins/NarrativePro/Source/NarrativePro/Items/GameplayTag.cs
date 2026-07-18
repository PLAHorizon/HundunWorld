using System;
using System.Collections.Generic;

namespace NarrativePro.Items
{
    /// <summary>
    /// 轻量级 GameplayTag 实现，替代 UE5 FGameplayTag。
    /// 使用点分层级字符串（如 "Narrative.Equipment.Slot.Head"）标识分类标签。
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
    {
        public static readonly GameplayTag None = new GameplayTag("");

        public string TagName { get; }

        public GameplayTag(string tagName)
        {
            TagName = tagName ?? "";
        }

        public bool IsValid() => !string.IsNullOrEmpty(TagName);

        /// <summary>
        /// 返回是否匹配给定标签或其子标签。例如 "Narrative.Equipment" 匹配 "Narrative.Equipment.Slot.Head"。
        /// </summary>
        public bool Matches(GameplayTag other)
        {
            if (!IsValid()) return false;
            if (!other.IsValid()) return false;
            if (TagName == other.TagName) return true;
            return other.TagName.StartsWith(TagName + ".", StringComparison.Ordinal);
        }

        public bool Equals(GameplayTag other) => TagName == other.TagName;
        public override bool Equals(object obj) => obj is GameplayTag t && Equals(t);
        public override int GetHashCode() => TagName?.GetHashCode() ?? 0;
        public int CompareTo(GameplayTag other) => string.Compare(TagName, other.TagName, StringComparison.Ordinal);
        public override string ToString() => TagName;

        public static bool operator ==(GameplayTag a, GameplayTag b) => a.Equals(b);
        public static bool operator !=(GameplayTag a, GameplayTag b) => !a.Equals(b);
        public static implicit operator GameplayTag(string s) => new GameplayTag(s);
        public static implicit operator string(GameplayTag t) => t.TagName;
    }

    /// <summary>
    /// 标签容器，替代 UE5 FGameplayTagContainer。
    /// </summary>
    public class GameplayTagContainer
    {
        private readonly HashSet<string> _tags = new HashSet<string>(StringComparer.Ordinal);

        public GameplayTagContainer() { }
        public GameplayTagContainer(IEnumerable<string> tags)
        {
            if (tags != null)
                foreach (var t in tags) _tags.Add(t);
        }

        public int Count => _tags.Count;
        public bool HasTag(GameplayTag tag) => tag.IsValid() && _tags.Contains(tag.TagName);

        public bool HasAny(GameplayTagContainer other)
        {
            if (other == null) return false;
            foreach (var t in other._tags)
                if (_tags.Contains(t)) return true;
            return false;
        }

        public bool HasAll(GameplayTagContainer other)
        {
            if (other == null) return true;
            foreach (var t in other._tags)
                if (!_tags.Contains(t)) return false;
            return true;
        }

        public void AddTag(GameplayTag tag)
        {
            if (tag.IsValid()) _tags.Add(tag.TagName);
        }

        public void RemoveTag(GameplayTag tag)
        {
            if (tag.IsValid()) _tags.Remove(tag.TagName);
        }

        public IEnumerable<string> GetTags() => _tags;
    }

    /// <summary>
    /// 常用 Narrative 标签定义。
    /// </summary>
    public static class NarrativeGameplayTags
    {
        public static class Equipment
        {
            public static readonly GameplayTag Slot = "Narrative.Equipment.Slot";
            public static readonly GameplayTag Slot_Head = "Narrative.Equipment.Slot.Head";
            public static readonly GameplayTag Slot_Torso = "Narrative.Equipment.Slot.Torso";
            public static readonly GameplayTag Slot_Legs = "Narrative.Equipment.Slot.Legs";
            public static readonly GameplayTag Slot_Feet = "Narrative.Equipment.Slot.Feet";
            public static readonly GameplayTag Slot_Hands = "Narrative.Equipment.Slot.Hands";
            public static readonly GameplayTag Slot_Weapon = "Narrative.Equipment.Slot.Weapon";
            public static readonly GameplayTag Slot_Ammo = "Narrative.Equipment.Slot.Ammo";

            public static readonly GameplayTag WieldSlot = "Narrative.Equipment.WieldSlot";
            public static readonly GameplayTag WieldSlot_Mainhand = "Narrative.Equipment.WieldSlot.Mainhand";
            public static readonly GameplayTag WieldSlot_Offhand = "Narrative.Equipment.WieldSlot.Offhand";

            public static readonly GameplayTag Weapon_AttachSlot = "Narrative.Equipment.Weapon.AttachSlot";
            public static readonly GameplayTag Weapon_AttachSlot_Scope = "Narrative.Equipment.Weapon.AttachSlot.Scope";
            public static readonly GameplayTag Weapon_AttachSlot_Muzzle = "Narrative.Equipment.Weapon.AttachSlot.Muzzle";
            public static readonly GameplayTag Weapon_AttachSlot_Grip = "Narrative.Equipment.Weapon.AttachSlot.Grip";
            public static readonly GameplayTag Weapon_AttachSlot_Magazine = "Narrative.Equipment.Weapon.AttachSlot.Magazine";
        }
    }
}

using System;
using System.Collections.Generic;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 角色动画对（1P 和 3P 蒙太奇）。对应 UE5 FNarrativeCharacterAnimation。
    /// 简化点：UE5 UAnimMontage 用字符串路径占位（Flax 中用 AnimationAsset 或 Asset 路径）。
    /// </summary>
    [Serializable]
    public class NarrativeCharacterAnimation
    {
        /// <summary>1P 蒙太奇路径。</summary>
        public string Montage1PPath = "";

        /// <summary>3P 蒙太奇路径。</summary>
        public string Montage3PPath = "";

        public NarrativeCharacterAnimation() { }

        public NarrativeCharacterAnimation(string montage1P, string montage3P)
        {
            Montage1PPath = montage1P;
            Montage3PPath = montage3P;
        }
    }

    /// <summary>
    /// 动画集。对应 UE5 UNarrativeAnimSet。
    /// 一组可重用的动画对（1P/3P），用于连击、硬直等。
    /// </summary>
    [Serializable]
    public class NarrativeAnimSet
    {
        /// <summary>动画对列表。</summary>
        public List<NarrativeCharacterAnimation> CharacterAnims = new List<NarrativeCharacterAnimation>();

        public NarrativeAnimSet() { }

        /// <summary>按索引获取动画对，越界返回默认。</summary>
        public NarrativeCharacterAnimation GetAnim(int index)
        {
            if (CharacterAnims != null && index >= 0 && index < CharacterAnims.Count)
            {
                return CharacterAnims[index];
            }
            return new NarrativeCharacterAnimation();
        }

        /// <summary>动画对数量。</summary>
        public int Count => CharacterAnims?.Count ?? 0;
    }

    /// <summary>
    /// 攻击连击动画集。对应 UE5 AttackComboAnimSet.h 中的相关用法。
    /// 包含普通攻击和重攻击的连击动画序列。
    /// </summary>
    [Serializable]
    public class AttackComboAnimSet
    {
        /// <summary>普通攻击连击动画集列表。</summary>
        public List<NarrativeAnimSet> AttackCombos = new List<NarrativeAnimSet>();

        /// <summary>重攻击连击动画集列表。</summary>
        public List<NarrativeAnimSet> HeavyAttackCombos = new List<NarrativeAnimSet>();

        public AttackComboAnimSet() { }
    }
}

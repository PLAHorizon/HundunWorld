using System;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator.Options
{
    /// <summary>
    /// 标量值选项。对应 UE5 UCharacterCreatorOption_Scalar。
    /// 全局标量值选项，如身高、瞳孔大小等。
    /// </summary>
    [Serializable]
    public class CharacterCreatorOption_Scalar : CharacterCreatorOption
    {
        /// <summary>标量值标签 ID（Narrative.CharacterCreator.Scalars）</summary>
        public GameplayTag ScalarTagID = GameplayTag.None;

        /// <summary>最小值</summary>
        public float MinValue = 0f;

        /// <summary>最大值</summary>
        public float MaxValue = 1f;

        /// <summary>滑块步进值</summary>
        public float StepValue = 0.1f;

        /// <summary>默认值</summary>
        public float DefaultValue = 0.5f;
    }
}

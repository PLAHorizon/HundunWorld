using System;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator.Options
{
    /// <summary>
    /// 向量值选项。对应 UE5 UCharacterCreatorOption_Vector。
    /// 颜色选项，从色板中选择。
    /// </summary>
    [Serializable]
    public class CharacterCreatorOption_Vector : CharacterCreatorOption
    {
        /// <summary>向量值标签 ID（Narrative.CharacterCreator.Vectors）</summary>
        public GameplayTag VectorTagID = GameplayTag.None;

        /// <summary>可选颜色色板路径</summary>
        public string AvailableOptionsSwatchPath = "";
    }
}

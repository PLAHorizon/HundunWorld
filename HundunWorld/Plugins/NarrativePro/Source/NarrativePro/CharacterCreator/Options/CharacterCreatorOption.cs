using System;

namespace NarrativePro.CharacterCreator.Options
{
    /// <summary>
    /// 角色创建器选项基类。对应 UE5 UCharacterCreatorOption。
    /// 创建器分区由多个选项组成，此为选项基类。
    /// </summary>
    [Serializable]
    public abstract class CharacterCreatorOption
    {
        /// <summary>选项显示名</summary>
        public string OptionDisplayName = "";
    }
}

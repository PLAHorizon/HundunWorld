using System;
using System.Collections.Generic;
using NarrativePro.CharacterCreator.Items;

namespace NarrativePro.CharacterCreator.Options
{
    /// <summary>
    /// 毛发选项。对应 UE5 UCharacterCreatorOption_Groom。
    /// 允许从毛发项列表中选择一个毛发（睫毛、胡须等）。
    /// Flax 无原生毛发系统，资源引用以路径占位。
    /// </summary>
    [Serializable]
    public class CharacterCreatorOption_Groom : CharacterCreatorOption
    {
        /// <summary>可选的毛发项列表</summary>
        public List<CharacterCreatorItem_Groom> Grooms = new List<CharacterCreatorItem_Groom>();
    }
}

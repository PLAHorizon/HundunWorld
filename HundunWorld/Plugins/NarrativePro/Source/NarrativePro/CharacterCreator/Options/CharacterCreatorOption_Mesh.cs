using System;
using System.Collections.Generic;
using NarrativePro.CharacterCreator.Items;

namespace NarrativePro.CharacterCreator.Options
{
    /// <summary>
    /// 网格选项。对应 UE5 UCharacterCreatorOption_Mesh。
    /// 允许从网格项列表中选择一个网格。
    /// </summary>
    [Serializable]
    public class CharacterCreatorOption_Mesh : CharacterCreatorOption
    {
        /// <summary>可选的网格项列表</summary>
        public List<CharacterCreatorItem_Mesh> Meshes = new List<CharacterCreatorItem_Mesh>();
    }
}

using System;

namespace NarrativePro.CharacterCreator.Items
{
    /// <summary>
    /// 角色创建器项基类。对应 UE5 UCharacterCreatorItem。
    /// 一些选项（如毛发和网格）会展示可选项列表，此类是这些项的基类。
    /// </summary>
    [Serializable]
    public class CharacterCreatorItem
    {
        /// <summary>项的显示名</summary>
        public string ItemDisplayName = "";

        /// <summary>项的缩略图路径</summary>
        public string ItemThumbnailPath = "";
    }
}

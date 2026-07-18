using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator.Items
{
    /// <summary>
    /// 毛发项。对应 UE5 UCharacterCreatorItem_Groom。
    /// Flax 无原生毛发系统，资源引用以路径占位。
    /// </summary>
    // Flax-不兼容: UE5 的 Groom 系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Groom 等价物
    [Serializable]
    public class CharacterCreatorItem_Groom : CharacterCreatorItem
    {
        /// <summary>此毛发应用的 slot 标签（Narrative.Equipment.Slot.Groom）</summary>
        public GameplayTag Slot = GameplayTag.None;

        /// <summary>毛发资源路径（Flax 无原生毛发系统，以路径字符串占位）</summary>
        public string GroomAssetPath = "";

        /// <summary>毛发绑定资源路径</summary>
        public string GroomBindingAssetPath = "";

        /// <summary>应用到毛发的材质选项列表</summary>
        public List<CreatorMeshMaterialOption> GroomMaterials = new List<CreatorMeshMaterialOption>();
    }
}

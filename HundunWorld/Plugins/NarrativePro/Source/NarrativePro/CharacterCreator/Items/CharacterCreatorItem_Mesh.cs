using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator.Items
{
    /// <summary>
    /// 网格材质选项。对应 UE5 FCreatorMeshMaterialOption。
    /// 描述可应用到网格上的材质及其可调参数。
    /// </summary>
    [Serializable]
    public class CreatorMeshMaterialOption
    {
        /// <summary>要应用的材质资源路径</summary>
        public string MaterialPath = "";

        /// <summary>材质在网格上的索引</summary>
        public int MaterialIdx = 0;

        /// <summary>可调标量参数列表</summary>
        public List<CreatorMeshMaterialParam_Scalar> ScalarMaterialParams = new List<CreatorMeshMaterialParam_Scalar>();

        /// <summary>可调向量参数列表</summary>
        public List<CreatorMeshMaterialParam_Vector> VectorMaterialParams = new List<CreatorMeshMaterialParam_Vector>();

        /// <summary>获取默认材质（含参数默认值）</summary>
        public CreatorMeshMaterial GetDefaultMaterial()
        {
            var newMeshMat = new CreatorMeshMaterial();
            // 按 MaterialPath 加载材质资产（对应 UE5 FCreatorMeshMaterial.Material 软引用）
            newMeshMat.MaterialPath = MaterialPath;
            if (!string.IsNullOrEmpty(MaterialPath))
            {
                newMeshMat.Material = Content.Load<Material>(MaterialPath);
            }

            foreach (var vParam in VectorMaterialParams)
            {
                var param = new CreatorMeshMaterialParam_Vector();
                param.ParameterNames = vParam.ParameterNames;
                param.VectorTagID = vParam.VectorTagID;
                newMeshMat.VectorParams.Add(param);
            }

            foreach (var sParam in ScalarMaterialParams)
            {
                var param = new CreatorMeshMaterialParam_Scalar();
                param.ParameterNames = sParam.ParameterNames;
                param.ScalarTagID = sParam.ScalarTagID;
                newMeshMat.ScalarParams.Add(param);
            }

            return newMeshMat;
        }
    }

    /// <summary>
    /// 网格项。对应 UE5 UCharacterCreatorItem_Mesh。
    /// 可选择应用到玩家的网格项，包含网格、材质选项、Morph、嵌套选项。
    /// </summary>
    [Serializable]
    public class CharacterCreatorItem_Mesh : CharacterCreatorItem
    {
        /// <summary>此网格应用的 slot 标签（Narrative.Equipment.Slot.Mesh）</summary>
        public GameplayTag Slot = GameplayTag.None;

        /// <summary>骨骼网格资源路径（Flax SkinnedModel）</summary>
        public string MeshPath = "";

        /// <summary>第一人称（1P）骨骼网格资源路径。对应 UE5 MeshAttributes.Mesh1P；为空则 1P 复用 3P 网格。</summary>
        public string Mesh1PPath = "";

        /// <summary>网格动画蓝图路径（不使用 Leader Pose 时使用）</summary>
        public string MeshAnimBPPath = "";

        /// <summary>是否使用 Leader Pose</summary>
        public bool bUseLeaderPose = true;

        /// <summary>附加骨骼名（Flax 无 Socket 概念，按骨骼名附着）。对应 UE5 MeshAttributes.MeshAttachSocket。</summary>
        public string MeshAttachSocket = "";

        /// <summary>附加偏移变换。对应 UE5 MeshAttributes.MeshAttachOffset。</summary>
        public Transform MeshAttachOffset = Transform.Identity;

        /// <summary>装备此网格时需要隐藏的 slot 列表。对应 UE5 MeshAttributes.HideSlots。</summary>
        public List<GameplayTag> HideSlots = new List<GameplayTag>();

        /// <summary>可选择的材质选项列表</summary>
        public List<CreatorMeshMaterialOption> MaterialOptions = new List<CreatorMeshMaterialOption>();

        /// <summary>应用到网格的 Morph 列表（复用 CreatorMeshMorph）</summary>
        public List<CreatorMeshMorph> Morphs = new List<CreatorMeshMorph>();

        /// <summary>
        /// 嵌套的标量选项列表。对应 UE5 UCharacterCreatorItem_Mesh::ScalarOptions
        /// （CharacterCreatorItem_Mesh.h:184-186，TArray&lt;UCharacterCreatorOption_Scalar*&gt;）。
        /// 例如连帽衫内嵌“连帽颜色 A”等可调参数。
        /// </summary>
        public List<CharacterCreatorOption_Scalar> ScalarOptions = new List<CharacterCreatorOption_Scalar>();

        /// <summary>
        /// 嵌套的向量选项列表。对应 UE5 UCharacterCreatorItem_Mesh::VectorOptions
        /// （CharacterCreatorItem_Mesh.h:187-188，TArray&lt;UCharacterCreatorOption_Vector*&gt;）。
        /// </summary>
        public List<CharacterCreatorOption_Vector> VectorOptions = new List<CharacterCreatorOption_Vector>();

        /// <summary>
        /// 将本项转换为可保存/应用的网格属性。对应 UE5 SetCreatorDataMesh 中的 MeshAttributes 拷贝。
        /// 路径字段由调用方（NarrativeSaveWithCreatorData / 工厂）解析为资产引用。
        /// </summary>
        public CharacterCreatorAttribute_Mesh ToMeshAttribute()
        {
            var attr = new CharacterCreatorAttribute_Mesh
            {
                bUseLeaderPose = bUseLeaderPose,
                MeshAnimBPPath = MeshAnimBPPath,
                MeshAttachSocket = MeshAttachSocket,
                MeshAttachOffset = MeshAttachOffset,
                // 路径随属性一起带走：SkinnedModel 引用进不了 JSON 存档，路径才是可持久化的身份
                MeshPath = MeshPath,
                Mesh1PPath = Mesh1PPath,
            };

            foreach (var morph in Morphs)
            {
                if (morph != null) attr.Morphs.Add(morph);
            }
            foreach (var hideSlot in HideSlots)
            {
                if (hideSlot.IsValid()) attr.HideSlots.Add(hideSlot);
            }
            foreach (var matOption in MaterialOptions)
            {
                if (matOption != null) attr.MeshMaterials.Add(matOption.GetDefaultMaterial());
            }
            return attr;
        }
    }
}


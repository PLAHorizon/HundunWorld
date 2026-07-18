using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator;
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
            newMeshMat.Material = null; // 运行时按 MaterialPath 加载

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

        /// <summary>网格动画蓝图路径（不使用 Leader Pose 时使用）</summary>
        public string MeshAnimBPPath = "";

        /// <summary>是否使用 Leader Pose</summary>
        public bool bUseLeaderPose = true;

        /// <summary>可选择的材质选项列表</summary>
        public List<CreatorMeshMaterialOption> MaterialOptions = new List<CreatorMeshMaterialOption>();

        /// <summary>应用到网格的 Morph 列表（复用 CreatorMeshMorph）</summary>
        public List<CreatorMeshMorph> Morphs = new List<CreatorMeshMorph>();

        /// <summary>嵌套的标量选项列表</summary>
        public List<string> ScalarOptionPaths = new List<string>();

        /// <summary>嵌套的向量选项列表</summary>
        public List<string> VectorOptionPaths = new List<string>();
    }
}


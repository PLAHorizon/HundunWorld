using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 角色创建器属性基类。对应 UE5 FCharacterCreatorAttribute。
    /// </summary>
    [Serializable]
    public class CharacterCreatorAttribute
    {
        // ID/Slot 已注释在 UE5 源文件中，保留空基类
    }

    /// <summary>
    /// Morph 目标属性。对应 UE5 FCharacterCreatorAttribute_Morph。
    /// </summary>
    [Serializable]
    public class CharacterCreatorAttribute_Morph : CharacterCreatorAttribute
    {
        /// <summary>Morph 名称。</summary>
        public string MorphName = "";

        /// <summary>Morph 值（0-1）。</summary>
        public float MorphValue = 0f;
    }

    /// <summary>
    /// 网格材质参数基类。对应 UE5 FCreatorMeshMaterialParam。
    /// </summary>
    [Serializable]
    public class CreatorMeshMaterialParam
    {
        /// <summary>要影响的材质参数名列表。</summary>
        public List<string> ParameterNames = new List<string>();
    }

    /// <summary>
    /// 向量材质参数。对应 UE5 FCreatorMeshMaterialParam_Vector。
    /// 通过 VectorTagID 关联到全局向量值表。
    /// </summary>
    [Serializable]
    public class CreatorMeshMaterialParam_Vector : CreatorMeshMaterialParam
    {
        public GameplayTag VectorTagID = GameplayTag.None;
    }

    /// <summary>
    /// 标量材质参数。对应 UE5 FCreatorMeshMaterialParam_Scalar。
    /// 通过 ScalarTagID 关联到全局标量值表。
    /// </summary>
    [Serializable]
    public class CreatorMeshMaterialParam_Scalar : CreatorMeshMaterialParam
    {
        public GameplayTag ScalarTagID = GameplayTag.None;
    }

    /// <summary>
    /// Morph 选项。对应 UE5 FCreatorMeshMorph。
    /// 用标量值驱动多个 morph。
    /// </summary>
    [Serializable]
    public class CreatorMeshMorph
    {
        public GameplayTag ScalarTag = GameplayTag.None;
        public List<string> MorphNames = new List<string>();
    }

    /// <summary>
    /// 网格材质。对应 UE5 FCreatorMeshMaterial。
    /// 描述应用到网格上的材质及其参数。
    /// </summary>
    [Serializable]
    public class CreatorMeshMaterial
    {
        /// <summary>要应用的材质资源路径（Flax Material 引用）。</summary>
        public Material Material;

        /// <summary>向量参数列表。</summary>
        public List<CreatorMeshMaterialParam_Vector> VectorParams = new List<CreatorMeshMaterialParam_Vector>();

        /// <summary>标量参数列表。</summary>
        public List<CreatorMeshMaterialParam_Scalar> ScalarParams = new List<CreatorMeshMaterialParam_Scalar>();
    }

    /// <summary>
    /// 网格属性。对应 UE5 FCharacterCreatorAttribute_Mesh。
    /// 描述一个网格及其材质/绑定/动画设置。
    /// </summary>
    [Serializable]
    public class CharacterCreatorAttribute_Mesh : CharacterCreatorAttribute
    {
        /// <summary>骨骼网格资源（Flax SkinnedModel 等价）。</summary>
        public SkinnedModel Mesh;

        /// <summary>是否使用 Leader Pose（驱动其他网格）。</summary>
        public bool bUseLeaderPose = true;

        /// <summary>是否使用静态网格而非骨骼网格。</summary>
        public bool bIsStaticMesh = false;

        /// <summary>静态网格资源。</summary>
        public Model StaticMesh;

        /// <summary>附加到骨骼的 socket 名。</summary>
        public string MeshAttachSocket = "";

        /// <summary>附加偏移变换。</summary>
        public Transform MeshAttachOffset = Transform.Identity;

        /// <summary>动画蓝图路径（Flax 中用 AnimGraph 资源路径占位）。</summary>
        // TODO [需接入 AnimGraph 加载系统]: Flax 中用 AnimGraph 资源路径占位，需接入实际加载逻辑
        public string MeshAnimBPPath = "";

        /// <summary>材质列表（按材质索引对应）。</summary>
        public List<CreatorMeshMaterial> MeshMaterials = new List<CreatorMeshMaterial>();

        /// <summary>Morph 列表。</summary>
        public List<CreatorMeshMorph> Morphs = new List<CreatorMeshMorph>();
    }

    /// <summary>
    /// Groom（毛发）属性。对应 UE5 FCharacterCreatorAttribute_Groom。
    /// Flax 中无 Groom 等价物，资源引用以路径字符串占位。
    /// </summary>
    // Flax-不兼容: UE5 的 Groom 系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Groom 等价物
    [Serializable]
    public class CharacterCreatorAttribute_Groom : CharacterCreatorAttribute
    {
        /// <summary>Groom 资源路径（Flax 无原生毛发系统，以路径字符串占位）。</summary>
        public string GroomAssetPath = "";

        /// <summary>Groom Binding 资源路径。</summary>
        public string GroomBindingAssetPath = "";

        /// <summary>Groom 材质列表。</summary>
        public List<CreatorMeshMaterial> GroomMaterials = new List<CreatorMeshMaterial>();
    }

    /// <summary>
    /// 角色创建器属性集合。对应 UE5 FCharacterCreatorAttributeSet。
    /// 完整描述一个角色的外观（基础网格、各种 slot 网格、毛发、Morph、全局标量/向量）。
    /// </summary>
    [Serializable]
    public class CharacterCreatorAttributeSet
    {
        /// <summary>角色体型标签（Narrative.CharacterCreator.Forms）。</summary>
        public GameplayTag FormTag = GameplayTag.None;

        /// <summary>视觉类路径（替代 UE5 TSubclassOf&lt;ANarrativeCharacterVisual&gt;）。</summary>
        public string CharacterVisualClassPath = "";

        /// <summary>基础骨骼网格（leader pose 网格，通常隐藏）。</summary>
        public SkinnedModel BaseMesh;

        /// <summary>是否隐藏基础网格。</summary>
        public bool bHideBaseMesh = true;

        /// <summary>基础网格动画蓝图路径。</summary>
        public string BaseMeshAnimBPPath = "";

        /// <summary>本地（1P）网格动画蓝图路径。</summary>
        public string BaseLocalMeshAnimBPPath = "";

        /// <summary>徒手动画层路径。</summary>
        public string UnarmedAnimLayerPath = "";

        /// <summary>各 slot 上的网格属性（按 GameplayTag 索引）。</summary>
        public List<MeshAttributeEntry> MeshEntries = new List<MeshAttributeEntry>();

        /// <summary>各 slot 上的 Groom 属性。</summary>
        public List<GroomAttributeEntry> GroomEntries = new List<GroomAttributeEntry>();

        /// <summary>Morph 列表。</summary>
        public List<CharacterCreatorAttribute_Morph> Morphs = new List<CharacterCreatorAttribute_Morph>();

        /// <summary>全局标量值。</summary>
        public List<ScalarValueEntry> ScalarEntries = new List<ScalarValueEntry>();

        /// <summary>全局向量值。</summary>
        public List<VectorValueEntry> VectorEntries = new List<VectorValueEntry>();

        /// <summary>获取向量值。</summary>
        public Color GetVectorValue(GameplayTag tag)
        {
            if (VectorEntries != null)
            {
                foreach (var e in VectorEntries)
                {
                    if (e != null && e.Tag == tag) return e.Value;
                }
            }
            return Color.Black;
        }

        /// <summary>获取标量值。</summary>
        public float GetScalarValue(GameplayTag tag)
        {
            if (ScalarEntries != null)
            {
                foreach (var e in ScalarEntries)
                {
                    if (e != null && e.Tag == tag) return e.Value;
                }
            }
            return 0f;
        }
    }

    /// <summary>网格属性条目（用于序列化）。</summary>
    [Serializable]
    public class MeshAttributeEntry
    {
        public GameplayTag Slot;
        public CharacterCreatorAttribute_Mesh Attribute;
    }

    /// <summary>Groom 属性条目。</summary>
    [Serializable]
    public class GroomAttributeEntry
    {
        public GameplayTag Slot;
        public CharacterCreatorAttribute_Groom Attribute;
    }

    /// <summary>标量值条目。</summary>
    [Serializable]
    public class ScalarValueEntry
    {
        public GameplayTag Tag;
        public float Value;
    }

    /// <summary>向量值条目。</summary>
    [Serializable]
    public class VectorValueEntry
    {
        public GameplayTag Tag;
        public Color Value;
    }
}

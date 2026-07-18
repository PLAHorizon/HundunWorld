using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator;
using NarrativePro.Items;

namespace NarrativePro.Character
{
    /// <summary>
    /// 外观基类。对应 UE5 UCharacterAppearanceBase。
    /// </summary>
    [Serializable]
    public class CharacterAppearanceBase
    {
        // 空基类，仅用于类型识别
    }

    /// <summary>
    /// 网格变化集合。对应 UE5 FCharacterCreatorVariation_Mesh。
    /// 包含一个 slot 上多个随机可选的网格属性，运行时随机选其一。
    /// </summary>
    [Serializable]
    public class CharacterCreatorVariation_Mesh
    {
        /// <summary>可选网格列表。</summary>
        public List<CharacterCreatorAttribute_Mesh> RandomMeshes = new List<CharacterCreatorAttribute_Mesh>();

        /// <summary>按索引获取；越界返回默认。</summary>
        public CharacterCreatorAttribute_Mesh GetMesh(int index)
        {
            if (RandomMeshes != null && index >= 0 && index < RandomMeshes.Count)
            {
                return RandomMeshes[index];
            }
            return new CharacterCreatorAttribute_Mesh();
        }

        /// <summary>随机获取一个网格属性。</summary>
        public CharacterCreatorAttribute_Mesh Get()
        {
            if (RandomMeshes == null || RandomMeshes.Count == 0) return new CharacterCreatorAttribute_Mesh();
            int idx = _rng.Next(0, RandomMeshes.Count);
            return GetMesh(idx);
        }

        private static readonly System.Random _rng = new System.Random();
    }

    /// <summary>
    /// 标量变化。对应 UE5 FScalarVariation。
    /// 从一个范围内随机选取标量值。
    /// </summary>
    [Serializable]
    public class ScalarVariation
    {
        /// <summary>下界。</summary>
        public float LowerBound = 0f;
        /// <summary>上界。</summary>
        public float UpperBound = 0f;

        public ScalarVariation() { }

        /// <summary>随机获取一个标量值。</summary>
        public float Get()
        {
            if (UpperBound <= LowerBound) return LowerBound;
            return (float)(_rng.NextDouble() * (UpperBound - LowerBound) + LowerBound);
        }

        private static readonly System.Random _rng = new System.Random();
    }

    /// <summary>
    /// 向量变化。对应 UE5 FVectorVariation。
    /// 从 Swatch 资源或硬编码列表中随机选取颜色。
    /// </summary>
    [Serializable]
    public class VectorVariation
    {
        /// <summary>色板资源路径（Flax 中无原生 swatch 概念，用字符串占位；需通过自定义资产加载系统使用）。</summary>
        public string SwatchPath = "";

        /// <summary>颜色列表（SwatchPath 为空时使用）。</summary>
        public List<Color> Vectors = new List<Color>();

        /// <summary>按索引获取；越界返回白色。</summary>
        public Color GetVector(int index)
        {
            if (Vectors != null && index >= 0 && index < Vectors.Count)
            {
                return Vectors[index];
            }
            return Color.White;
        }

        /// <summary>随机获取一个颜色。</summary>
        public Color Get()
        {
            if (Vectors == null || Vectors.Count == 0) return Color.White;
            // TODO [需接入 Swatch 资源加载系统]: SwatchPath 当前未实现加载，仅使用硬编码 Vectors 列表
            int idx = _rng.Next(0, Vectors.Count);
            return GetVector(idx);
        }

        private static readonly System.Random _rng = new System.Random();
    }

    /// <summary>
    /// 角色创建器变化集合。对应 UE5 FCharacterCreatorVariationSet。
    /// 包含网格、标量、向量变化，用于生成随机外观。
    /// </summary>
    [Serializable]
    public class CharacterCreatorVariationSet
    {
        /// <summary>网格变化条目。</summary>
        public List<MeshVariationEntry> MeshEntries = new List<MeshVariationEntry>();

        /// <summary>全局标量变化。</summary>
        public List<ScalarVariationEntry> ScalarEntries = new List<ScalarVariationEntry>();

        /// <summary>全局向量变化。</summary>
        public List<VectorVariationEntry> VectorEntries = new List<VectorVariationEntry>();
    }

    /// <summary>网格变化条目。</summary>
    [Serializable]
    public class MeshVariationEntry
    {
        public GameplayTag Slot;
        public CharacterCreatorVariation_Mesh Variation;
    }

    /// <summary>标量变化条目。</summary>
    [Serializable]
    public class ScalarVariationEntry
    {
        public GameplayTag Tag;
        public ScalarVariation Variation;
    }

    /// <summary>向量变化条目。</summary>
    [Serializable]
    public class VectorVariationEntry
    {
        public GameplayTag Tag;
        public VectorVariation Variation;
    }

    /// <summary>
    /// 角色外观。对应 UE5 UCharacterAppearance。
    /// 定义角色的基础外观与随机变化集合。
    /// </summary>
    [Serializable]
    public class CharacterAppearance : CharacterAppearanceBase
    {
        /// <summary>角色基础属性（无变化时使用）。</summary>
        public CharacterCreatorAttributeSet CharacterAttributes = new CharacterCreatorAttributeSet();

        /// <summary>变化集合，用于生成随机外观。</summary>
        public CharacterCreatorVariationSet Variations = new CharacterCreatorVariationSet();

        /// <summary>
        /// 获取最终属性集合。基础属性 + 随机变化合并。
        /// </summary>
        public virtual CharacterCreatorAttributeSet GetAppearanceAttributes()
        {
            // 浅拷贝基础属性，避免修改原始数据
            var result = new CharacterCreatorAttributeSet
            {
                FormTag = CharacterAttributes.FormTag,
                CharacterVisualClassPath = CharacterAttributes.CharacterVisualClassPath,
                BaseMesh = CharacterAttributes.BaseMesh,
                bHideBaseMesh = CharacterAttributes.bHideBaseMesh,
                BaseMeshAnimBPPath = CharacterAttributes.BaseMeshAnimBPPath,
                BaseLocalMeshAnimBPPath = CharacterAttributes.BaseLocalMeshAnimBPPath,
                UnarmedAnimLayerPath = CharacterAttributes.UnarmedAnimLayerPath,
            };

            // 复制基础条目（共享元素引用，运行时按只读对待）
            result.MeshEntries = CharacterAttributes.MeshEntries != null
                ? new List<MeshAttributeEntry>(CharacterAttributes.MeshEntries)
                : new List<MeshAttributeEntry>();
            result.GroomEntries = CharacterAttributes.GroomEntries != null
                ? new List<GroomAttributeEntry>(CharacterAttributes.GroomEntries)
                : new List<GroomAttributeEntry>();
            result.Morphs = CharacterAttributes.Morphs != null
                ? new List<CharacterCreatorAttribute_Morph>(CharacterAttributes.Morphs)
                : new List<CharacterCreatorAttribute_Morph>();
            result.ScalarEntries = CharacterAttributes.ScalarEntries != null
                ? new List<ScalarValueEntry>(CharacterAttributes.ScalarEntries)
                : new List<ScalarValueEntry>();
            result.VectorEntries = CharacterAttributes.VectorEntries != null
                ? new List<VectorValueEntry>(CharacterAttributes.VectorEntries)
                : new List<VectorValueEntry>();

            // 合并 Variations 中的随机变化到 result
            if (Variations != null)
            {
                // 网格变化：按 Slot 覆盖或追加
                if (Variations.MeshEntries != null)
                {
                    foreach (var entry in Variations.MeshEntries)
                    {
                        if (entry?.Variation == null) continue;
                        var meshAttr = entry.Variation.Get();
                        int idx = result.MeshEntries.FindIndex(e => e != null && e.Slot == entry.Slot);
                        if (idx >= 0)
                            result.MeshEntries[idx].Attribute = meshAttr;
                        else
                            result.MeshEntries.Add(new MeshAttributeEntry { Slot = entry.Slot, Attribute = meshAttr });
                    }
                }

                // 标量变化：按 Tag 覆盖或追加
                if (Variations.ScalarEntries != null)
                {
                    foreach (var entry in Variations.ScalarEntries)
                    {
                        if (entry?.Variation == null) continue;
                        float value = entry.Variation.Get();
                        int idx = result.ScalarEntries.FindIndex(e => e != null && e.Tag == entry.Tag);
                        if (idx >= 0)
                            result.ScalarEntries[idx].Value = value;
                        else
                            result.ScalarEntries.Add(new ScalarValueEntry { Tag = entry.Tag, Value = value });
                    }
                }

                // 向量变化：按 Tag 覆盖或追加
                if (Variations.VectorEntries != null)
                {
                    foreach (var entry in Variations.VectorEntries)
                    {
                        if (entry?.Variation == null) continue;
                        Color value = entry.Variation.Get();
                        int idx = result.VectorEntries.FindIndex(e => e != null && e.Tag == entry.Tag);
                        if (idx >= 0)
                            result.VectorEntries[idx].Value = value;
                        else
                            result.VectorEntries.Add(new VectorValueEntry { Tag = entry.Tag, Value = value });
                    }
                }
            }

            return result;
        }
    }
}

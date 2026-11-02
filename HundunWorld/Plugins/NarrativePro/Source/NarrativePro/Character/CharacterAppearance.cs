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

        /// <summary>
        /// 确定性种子合成（FNV-1a）。对应 UE5 中以角色 RandomSeed 初始化 FRandomStream 的做法，
        /// 保证同一 (seed, key) 在任意进程/任意次运行中产生同一结果。
        /// </summary>
        public static int CombineSeeds(int baseSeed, string key)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(key))
                {
                    foreach (char c in key)
                    {
                        hash = (hash ^ c) * 16777619u;
                    }
                }
                hash = (hash ^ (uint)baseSeed) * 16777619u;
                return (int)hash;
            }
        }
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

        /// <summary>
        /// 按确定性种子获取一个网格属性。对应 UE5 Variation.Get(RandomSeed)：
        /// 同一角色（同一种子）每次运行得到同一外观。
        /// </summary>
        public CharacterCreatorAttribute_Mesh Get(int seed)
        {
            if (RandomMeshes == null || RandomMeshes.Count == 0) return new CharacterCreatorAttribute_Mesh();
            int idx = new System.Random(seed).Next(RandomMeshes.Count);
            return GetMesh(idx);
        }

        /// <summary>随机获取一个网格属性（以实例稳定哈希为种子，保证确定性）。</summary>
        public CharacterCreatorAttribute_Mesh Get()
        {
            return Get(CharacterAppearanceBase.CombineSeeds(0, $"mesh:{RandomMeshes?.Count}"));
        }
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

        /// <summary>按确定性种子获取一个标量值。对应 UE5 Variation.Get(RandomSeed)。</summary>
        public float Get(int seed)
        {
            if (UpperBound <= LowerBound) return LowerBound;
            return (float)(new System.Random(seed).NextDouble() * (UpperBound - LowerBound) + LowerBound);
        }

        /// <summary>随机获取一个标量值（以实例稳定哈希为种子，保证确定性）。</summary>
        public float Get()
        {
            return Get(CharacterAppearanceBase.CombineSeeds(0, $"scalar:{LowerBound}:{UpperBound}"));
        }
    }

    /// <summary>
    /// 向量变化。对应 UE5 FVectorVariation。
    /// 从 Swatch 资源或列表中按种子确定性选取颜色。
    /// </summary>
    [Serializable]
    public class VectorVariation
    {
        /// <summary>
        /// 色板 JSON 文件路径（或经 <see cref="SwatchResolver"/> 解析的资产路径）。
        /// 非空时优先从色板取色，对应 UE5 VectorVariation 的 Swatch 引用。
        /// </summary>
        public string SwatchPath = "";

        /// <summary>颜色列表（SwatchPath 为空或未解析时使用）。</summary>
        public List<Color> Vectors = new List<Color>();

        /// <summary>
        /// 可选：外部色板解析器（路径 → 颜色列表）。默认使用
        /// <c>CharacterCreatorFactory.LoadColorSwatch</c>（JSON 文件 + 缓存）；
        /// 游戏侧可注入以支持自定义资产系统。
        /// </summary>
        public static Func<string, List<Color>> SwatchResolver = path =>
        {
            var swatch = CharacterCreatorFactory.LoadColorSwatch(path);
            return swatch?.Colors;
        };

        /// <summary>解析本变化可用的颜色集合（色板优先，其次内联列表）。</summary>
        public List<Color> ResolveColors()
        {
            if (!string.IsNullOrEmpty(SwatchPath))
            {
                var resolver = SwatchResolver;
                if (resolver != null)
                {
                    var colors = resolver.Invoke(SwatchPath);
                    if (colors != null && colors.Count > 0) return colors;
                }
            }
            return Vectors;
        }

        /// <summary>按索引获取；越界返回白色。</summary>
        public Color GetVector(int index)
        {
            var colors = ResolveColors();
            if (colors != null && index >= 0 && index < colors.Count)
            {
                return colors[index];
            }
            return Color.White;
        }

        /// <summary>按确定性种子获取颜色。对应 UE5 Variation.Get(RandomSeed)。</summary>
        public Color Get(int seed)
        {
            var colors = ResolveColors();
            if (colors == null || colors.Count == 0) return Color.White;
            int idx = new System.Random(seed).Next(colors.Count);
            return GetVector(idx);
        }

        /// <summary>随机获取一个颜色（以实例稳定哈希为种子，保证确定性）。</summary>
        public Color Get()
        {
            return Get(CharacterAppearanceBase.CombineSeeds(0, $"vector:{SwatchPath}:{Vectors?.Count}"));
        }
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
        /// 获取最终属性集合。基础属性 + 按角色种子生成的确定性变化。
        /// 对应 UE5 UCharacterAppearance::GetAppearanceAttributes(Requester)（CharacterAppearance.cpp:61-66）：
        /// 以请求角色的 CharacterRandomSeed 驱动所有 Variation，使同一角色每次得到同一外观。
        /// </summary>
        /// <param name="randomSeed">角色随机种子（来自 NarrativeCharacter.GetCharacterRandomSeed()）。</param>
        public virtual CharacterCreatorAttributeSet GetAppearanceAttributes(int randomSeed)
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

            // 合并 Variations 中的确定性变化到 result（每个条目按 角色种子+slot/tag 派生子种子，
            // 等价 UE5 中按 slot 顺序消费同一条随机流）
            if (Variations != null)
            {
                // 网格变化：按 Slot 覆盖或追加
                if (Variations.MeshEntries != null)
                {
                    foreach (var entry in Variations.MeshEntries)
                    {
                        if (entry?.Variation == null) continue;
                        int subSeed = CharacterAppearanceBase.CombineSeeds(randomSeed, $"mesh:{entry.Slot.TagName}");
                        var meshAttr = entry.Variation.Get(subSeed);
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
                        int subSeed = CharacterAppearanceBase.CombineSeeds(randomSeed, $"scalar:{entry.Tag.TagName}");
                        float value = entry.Variation.Get(subSeed);
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
                        int subSeed = CharacterAppearanceBase.CombineSeeds(randomSeed, $"vector:{entry.Tag.TagName}");
                        Color value = entry.Variation.Get(subSeed);
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

        /// <summary>
        /// 获取最终属性集合（无角色种子的兼容入口）。
        /// 使用以 FormTag 派生的稳定默认种子：结果确定但不随角色变化；
        /// 需要按角色差异化时请调用 <see cref="GetAppearanceAttributes(int)"/>。
        /// </summary>
        public virtual CharacterCreatorAttributeSet GetAppearanceAttributes()
        {
            return GetAppearanceAttributes(CharacterAppearanceBase.CombineSeeds(0, $"form:{CharacterAttributes?.FormTag.TagName}"));
        }
    }
}

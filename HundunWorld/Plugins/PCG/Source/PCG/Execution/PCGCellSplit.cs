// // PCG 插件 - 结构化生成各节点共用的“切分”原语
// // 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.29–§5.1.31。

using System;
using System.Collections.Generic;
using System.Globalization;
using FlaxEngine;
using PCG.Nodes;

namespace PCG.Execution
{
    /// <summary>切分出来的一格，带它自己的坐标系。</summary>
    /// <remarks>
    /// 坐标系来自种子：切分沿**格子自己的轴**发生，所以转过三十度的一宗地块是
    /// 沿它自己的正面切开。`Size` 是整格尺寸，不是半宽。
    /// </remarks>
    internal struct PCGCell
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;
        public readonly Quaternion Rotation;
        public readonly float Density;
        public readonly int Seed;
        public readonly int Depth;

        public PCGCell(Vector3 center, Vector3 size, Quaternion rotation, float density, int seed, int depth)
        {
            Center = center;
            Size = size;
            Rotation = rotation;
            Density = density;
            Seed = seed;
            Depth = depth;
        }
    }

    /// <summary>
    /// 轴与比例的解析，以及每个结构化生成节点都建在其上的那一小块几何：把一格
    /// 沿它自己的一条轴切成成比例的若干片。
    /// </summary>
    /// <remarks>
    /// 共用而不是每个节点各写一遍，不只是为了省行数。必须守住的那条性质——
    /// 子格**正好铺满**父格，不留缝、不重叠——恰恰是写错也照样能在别的可观察层面
    /// 满足的那一条（件数一样、尺寸一样、图只是“差一点”），所以它要的是
    /// 一份实现与一组断言，而不是两份会各自漂移的。
    /// </remarks>
    internal static class PCGCellSplit
    {
        /// <summary>一次切分最多出几片。</summary>
        public const int MaxSlices = 16;

        /// <summary>归一化之后，一片至少能占多少。</summary>
        private const float MinSlice = 0.0001f;

        /// <summary>读一个轴字母：X / Y / Z，忽略大小写与两侧空白。</summary>
        public static bool TryParseAxis(string text, out int axis, out string error)
        {
            error = null;

            switch ((text ?? string.Empty).Trim().ToUpperInvariant())
            {
            case "X": axis = 0; return true;
            case "Y": axis = 1; return true;
            case "Z": axis = 2; return true;
            default:
                axis = 0;
                error = $"'{text}' is not X, Y or Z";
                return false;
            }
        }

        /// <summary>
        /// 读一份逗号分隔的正比例表，并归一化成和为 1。
        /// </summary>
        /// <remarks>
        /// 归一化才让人能按“想的那样”写比例：<c>0.5,0.5</c>、
        /// <c>1,1</c>、<c>3,2</c> 这类写法都在描述同一种切法，而且哪一个都不会在格的末端
        /// 留下一条缝——按绝对长度去读才会犯那个错。
        /// </remarks>
        public static bool TryParseRatios(string text, out float[] slices, out string error)
        {
            slices = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "no ratios were given";
                return false;
            }

            var parts = text.Split(',');

            if (parts.Length > MaxSlices)
            {
                error = $"{parts.Length} slices is more than the {MaxSlices} allowed";
                return false;
            }

            var values = new float[parts.Length];
            float total = 0.0f;

            for (int i = 0; i < parts.Length; i++)
            {
                string piece = parts[i].Trim();

                if (!float.TryParse(piece, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    values[i] <= 0.0f || float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    error = $"slice {i + 1} ('{piece}') is not a positive number";
                    return false;
                }

                total += values[i];
            }

            for (int i = 0; i < values.Length; i++)
                values[i] = Math.Max(MinSlice, values[i] / total);

            slices = values;
            return true;
        }

        /// <summary>格子沿**即将切的那条轴**还够宽时为真。</summary>
        /// <remarks>
        /// 量的是 <b>要切的那条轴</b>，不是格子的最长边。一条只切 X 的规则
        /// 永远不动 Z，于是拿最长边做的“格子变小就停”判断对它**永远不触发**——
        /// X 被削到零、Z 一直还有 1000 深。这个检查真正在问的是
        /// “按<i>这个方向</i>还切得动吗”，那是个轴向问题。
        /// </remarks>
        public static bool CanCut(in PCGCell cell, int axis, float minCellSize) =>
            minCellSize <= 0.0f || Axis(cell.Size, axis) >= minCellSize;

        /// <summary>
        /// 把 <paramref name="cell"/> 沿 <paramref name="axis"/> 按每个比例切一片，
        /// 子格按从左到右写进 <paramref name="into"/>。
        /// </summary>
        /// <returns>写出的子格数，也就是比例表里的条数。</returns>
        public static int Split(in PCGCell cell, int axis, float[] slices, PCGCell[] into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            if (slices == null || slices.Length == 0)
                return 0;

            if (into.Length < slices.Length)
                throw new ArgumentException($"Need room for {slices.Length} children.", nameof(into));

            float length = Axis(cell.Size, axis);
            float cursor = -length * 0.5f;

            for (int i = 0; i < slices.Length; i++)
            {
                float slice = length * slices[i];

                var local = new Vector3(
                    axis == 0 ? cursor + slice * 0.5f : 0.0f,
                    axis == 1 ? cursor + slice * 0.5f : 0.0f,
                    axis == 2 ? cursor + slice * 0.5f : 0.0f);

                var size = cell.Size;

                if (axis == 0) size.X = slice;
                else if (axis == 1) size.Y = slice;
                else size.Z = slice;

                // // 子格的中心是“在父格自己坐标系里的偏移”换算回世界空间；它的朝向
                // // 沿用父格的，所以切出来的一格与它切自的那一块保持对齐。
                into[i] = new PCGCell(
                    cell.Center + Vector3.Transform(local, cell.Rotation), size, cell.Rotation,
                    cell.Density, cell.Seed, cell.Depth + 1);

                cursor += slice;
            }

            return slices.Length;
        }

        /// <summary>把切完之后的叶格写进一份新的点集。</summary>
        /// <remarks>
        /// 集中在这里，是因为这个包围盒比看上去重要：叶格的尺寸是下游每一个
        /// 裁剪判断要过的那一关，所以它由格子的尺寸与旋转直接算出来，而不是走
        /// <c>UpdateBounds</c> —— 那个不会把点的缩放算进去。
        /// </remarks>
        public static PCGData ToPoints(List<PCGCell> leaves)
        {
            var result = PCGData.Create(leaves.Count);
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                var cell = leaves[i];

                points[i] = PCGPoint.Create(cell.Center, cell.Density, unchecked(cell.Seed + i));
                points[i].Rotation = cell.Rotation;
                points[i].Scale = new Float3(cell.Size.X, cell.Size.Y, cell.Size.Z);

                var half = new Vector3(cell.Size.X * 0.5f, cell.Size.Y * 0.5f, cell.Size.Z * 0.5f);
                points[i].Bounds = PCGPointTransform.MakeBox(
                    cell.Center, PCGPointTransform.EnclosingExtents(half, cell.Rotation));
            }

            return result;
        }

        /// <summary>在这段代码刚造出来的点集上声明一列 float，长度按点数。</summary>
        public static float[] DeclareColumn(PCGData data, string name)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null)
                return null;

            if (!attributes.Contains(name))
                attributes.DeclareAttributeUnsafe(name, PCGAttributeType.Float);

            return attributes.GetWritableFloatArray(name);
        }

        /// <summary>在这段代码刚造出来的点集上声明一列字符串。</summary>
        public static string[] DeclareTextColumn(PCGData data, string name)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null)
                return null;

            if (!attributes.Contains(name))
                attributes.DeclareAttributeUnsafe(name, PCGAttributeType.String);

            return attributes.GetWritableStringArray(name);
        }

        private static float Axis(Vector3 size, int axis) =>
            axis == 0 ? size.X : axis == 1 ? size.Y : size.Z;
    }
}

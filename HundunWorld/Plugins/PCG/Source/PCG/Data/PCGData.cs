// PCG 插件 - 点数据容器
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §4.3。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG
{
    /// <summary>
    /// 沿图的边流动的一组点。
    /// </summary>
    /// <remarks>
    /// 节点之间交换 <see cref="PCGData"/> 实例，可以**直接**把某个输入返回回去（全留的过滤器），也可以另建一份。
    /// 属性存储是按引用共享的，所以请看 <see cref="PCGAttributeSet"/> 上的写时复制约定：把同一份存储交给**额外**一份
    /// 数据集时，建立者要调用一次 <see cref="PCGAttributeSet.Share"/>。
    /// </remarks>
    public sealed class PCGData
    {
        /// <summary>一份空数据集。</summary>
        public static readonly PCGData Empty = new PCGData(Array.Empty<PCGPoint>());

        /// <summary>这份数据集里的点。</summary>
        public PCGPoint[] Points;

        /// <summary>
        /// 由这些点共享的自定义属性存储；这份数据集不带自定义属性时为 null。
        /// </summary>
        public PCGAttributeSet SharedAttributes;

        /// <summary>
        /// 这份数据集随点一起携带的曲线。
        /// </summary>
        /// <remarks>
        /// 一份数据集就是"点 + 图还在传的别的东西"，而样条是 UE5 认的第二种。样条**不是逐点的行**，所以过滤或
        /// 收拢点都不动它们；合并各层时把它们拼起来，因为进去的每条曲线都还在。
        /// </remarks>
        public PCGSpline[] Splines;

        /// <summary>
        /// 这一层携带的网格；几何节点产出时是每个点一个。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 图往下传的第三种载荷（在点与曲线之后）：来自结构类节点的程序化几何。<b>与 <see cref="Points"/> 下标对齐</b>，
        /// 而不是一份松散的列表，因为一个网格**属于**某个点——它就是那个点的形状——两者必须一起扛过同一批操作。
        /// 比点更短或更长的数组会被读成"那些点没有网格"而不是报错，于是"只给一部分点挂几何"仍然表达得出来。
        /// </para>
        /// <para>
        /// 与曲线不同，网格**是**逐点数据，所以 <see cref="Gather"/> 把对应的那些一起带走，而不是全部保留：
        /// 丢掉一个点就必须丢掉它的形状，否则下一个节点会把石头的网格配到树的位置上。
        /// </para>
        /// </remarks>
        public PCGMesh[] Meshes;

        /// <summary>
        /// 这一层携带的**独立几何**：自带摆放的网格，不挂在任何点上。没有时为 null。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这是 <see cref="Meshes"/> 之外的第二种几何承载，补的是"图里只有几何、没有点"这一格
        /// （UE5 把 DynamicMesh 当空间数据类型用的那种用法：一块地形、一面墙、一根柱子可以不属于任何点）。
        /// 两者的分工是**配对方式**：<see cref="Meshes"/> 与点下标对齐（配对零成本），
        /// 这里每一项自己带 <see cref="PCGGeometryItem.Placement"/>。
        /// </para>
        /// <para>
        /// 与曲线同一个道理，它**不是逐点的行**：过滤或收拢点都不动它，因为一块不属于任何点的几何
        /// 不该因为某个点被筛掉而消失；合并各层时把它们拼起来。
        /// </para>
        /// <para>
        /// 这条规则有一个必须写下来的后果：**一份"没有点、只有几何"的数据集是合法的**，所以
        /// <see cref="Merge"/> 不能再用"点数为 0"当作"整份为空"——否则几何会在合并那一步被静默丢掉。
        /// </para>
        /// </remarks>
        public PCGGeometryItem[] Geometry;

        /// <summary>
        /// 这份数据集是"把若干来源聚到一个多输入引脚上"得来的时候，这里放各个来源；否则为 null。
        /// </summary>
        /// <remarks>
        /// 组合类节点把它的多连线输入收成**一个聚合体**，这样执行器的输入数组类型始终统一。需要分来源推理的组合器
        /// （Merge、Difference、Intersection）读这个字段，各自独立处理每一部分，而不是在一坨已经被压平、无从归属的
        /// 数据上作业。
        /// </remarks>
        public PCGData[] Parts;

        /// <summary>这份数据集是若干部分的聚合体（而不是一份普通点集）时为 true。</summary>
        public bool IsAggregate => Parts != null;

        /// <summary>创建一份空数据集。</summary>
        public PCGData()
        {
            Points = Array.Empty<PCGPoint>();
        }

        /// <summary>在已有的点数组上创建一份数据集，并接管它的所有权。</summary>
        public PCGData(PCGPoint[] points)
        {
            Points = points ?? Array.Empty<PCGPoint>();
        }

        /// <summary>在已有的点数组与属性存储上创建一份数据集。</summary>
        public PCGData(PCGPoint[] points, PCGAttributeSet attributes)
        {
            Points = points ?? Array.Empty<PCGPoint>();
            SharedAttributes = attributes;
        }

        /// <summary>取点的个数。</summary>
        public int Count => Points?.Length ?? 0;

        /// <summary>这份数据集里没有点时为 true。</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// 新建一份按 <paramref name="count"/> 个点开好、且**自己拥有**属性存储的数据集。
        /// 采样器产出的都是新点，所以在采样器里用这个。
        /// </summary>
        public static PCGData Create(int count)
        {
            var points = count > 0 ? new PCGPoint[count] : Array.Empty<PCGPoint>();
            var attributes = new PCGAttributeSet(count);
            return new PCGData(points, attributes);
        }

        /// <summary>
        /// 返回这份数据集的一份副本，其属性存储可以安全写入。
        /// </summary>
        /// <remarks>
        /// 点是按值复制的，而按值复制会把 <see cref="PCGPoint.Attributes"/> 这个**引用**也复制过去；存储要另行
        /// 脱离。改动一份来自别的节点、可能被共享的数据集的点时，先调这个。
        /// </remarks>
        public PCGData Clone(bool clonePoints = true, bool cloneAttributes = true)
        {
            var points = clonePoints ? (PCGPoint[])Points.Clone() : Points;
            var attributes = cloneAttributes && SharedAttributes != null ? SharedAttributes.Clone() : SharedAttributes;
            var result = new PCGData(points, attributes);

            // 曲线本身不可变、共享很便宜；复制的是装它们的那只数组，免得往下游追加一条样条的节点反手改到它读的那份。
            result.Splines = Splines != null ? (PCGSpline[])Splines.Clone() : null;

            // 网格是**共享**而不是复制：网格是建出来的成品，不是下游节点就地修改的东西，而每次克隆都复制几千个顶点
            // 会让数据模型本身变成一次运行里最贵的部分。
            result.Meshes = Meshes;

            // 独立几何复制的是装它们的**那只数组**（元素是 struct：网格仍按引用共享，拷贝很便宜）。
            // 与曲线同一个理由：往下游追加一块几何的节点不该反手改到它读的那份。
            result.Geometry = Geometry != null ? (PCGGeometryItem[])Geometry.Clone() : null;

            // 把每个点重新指向新数据集自己拥有的那份存储；否则这些点还指着旧的共享实例，克隆就白做了。
            if (cloneAttributes && attributes != null)
            {
                for (int i = 0; i < result.Points.Length; i++)
                {
                    result.Points[i].Attributes = attributes;
                }
            }

            return result;
        }

        /// <summary>
        /// 新建一份只含 <paramref name="indices"/> 里那些行、并按顺序压紧的数据集。
        /// </summary>
        public static PCGData Gather(PCGData source, int[] indices, int count)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (indices == null)
                throw new ArgumentNullException(nameof(indices));

            if (count < 0 || count > indices.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            var points = count > 0 ? new PCGPoint[count] : Array.Empty<PCGPoint>();
            for (int i = 0; i < count; i++)
                points[i] = source.Points[indices[i]];

            var attributes = source.SharedAttributes?.Gather(indices, count);

            if (attributes != null)
            {
                for (int i = 0; i < count; i++)
                    points[i].Attributes = attributes;
            }

            // 丢点不丢曲线：样条不是一行数据，否则路径类图中间随便一个过滤器就把那条路悄悄删掉了。
            var gathered = new PCGData(points, attributes)
            {
                Splines = source.Splines != null ? (PCGSpline[])source.Splines.Clone() : null,

                // 独立几何与曲线同一条规则：它不属于任何点，所以筛点／收拢都不动它。
                Geometry = source.Geometry != null ? (PCGGeometryItem[])source.Geometry.Clone() : null,
            };

            // 网格是逐点的，所以活下来的点要带着各自的形状走：见字段上的说明。
            if (source.Meshes != null && source.Meshes.Length > 0)
            {
                var meshes = new PCGMesh[count];
                int copied = 0;

                for (int i = 0; i < count; i++)
                {
                    if (indices[i] >= 0 && indices[i] < source.Meshes.Length)
                    {
                        meshes[i] = source.Meshes[indices[i]];
                        copied++;
                    }
                }

                gathered.Meshes = copied > 0 ? meshes : null;
            }

            return gathered;
        }

        /// <summary>
        /// 把若干数据集拼成一个。属性列按名字取并集；合并规则见 docs/PCG_PLUGIN_DESIGN.md §5.1.4 注 10。
        /// </summary>
        /// <param name="parts">要合并的数据集。null 项跳过。</param>
        /// <param name="error">合并被拒绝时给出原因，否则为 null。</param>
        public static PCGData Merge(PCGData[] parts, out string error)
        {
            error = null;

            if (parts == null || parts.Length == 0)
                return Empty;

            int total = 0;
            int geometryCount = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null)
                    continue;

                total += parts[i].Count;
                geometryCount += parts[i].Geometry?.Length ?? 0;
            }

            // "点数为 0"**不等于**"整份为空"：一份只有独立几何的数据集是合法的（几何不属于任何点），
            // 拿点数当空判断会在合并这一步把它静默丢掉。两者都空才是真的空。
            if (total == 0 && geometryCount == 0)
                return Empty;

            // 先把属性名的并集收出来、并当场拒掉类型冲突，这样出错时绝不会产出一个"半成品结果"。
            var merged = new System.Collections.Generic.Dictionary<string, PCGAttributeType>(StringComparer.Ordinal);
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part?.SharedAttributes == null)
                    continue;

                foreach (var descriptor in part.SharedAttributes.GetCustomDescriptors())
                {
                    if (merged.TryGetValue(descriptor.Name, out var existingType))
                    {
                        if (existingType != descriptor.Type)
                        {
                            error = $"Merge rejected: attribute '{descriptor.Name}' is {existingType} in one input " +
                                    $"and {descriptor.Type} in another.";
                            return null;
                        }
                    }
                    else
                    {
                        merged.Add(descriptor.Name, descriptor.Type);
                    }
                }
            }

            var points = new PCGPoint[total];
            PCGAttributeSet attributes = merged.Count > 0 ? new PCGAttributeSet(total) : null;

            if (attributes != null)
            {
                foreach (var kv in merged)
                    attributes.DeclareAttributeUnsafe(kv.Key, kv.Value);
            }

            int offset = 0;
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part == null || part.Count == 0)
                    continue;

                Array.Copy(part.Points, 0, points, offset, part.Count);

                if (attributes != null)
                {
                    // 逐列复制，某一部分没有这个属性时填该类型的默认值。
                    // **有意不发明数值**：缺的属性就是零/默认，并由节点报出来，而不是悄悄编一份看着合理的数据。
                    foreach (var kv in merged)
                    {
                        CopyColumn(part.SharedAttributes, kv.Key, kv.Value, attributes, offset, part.Count);
                    }
                }

                offset += part.Count;
            }

            if (attributes != null)
            {
                for (int i = 0; i < total; i++)
                    points[i].Attributes = attributes;
            }

            // 各部分的曲线在合并后都留着：两层并成一层之后仍然描述着两条路，在这里丢掉会让路径类图合并一次就少一半路。
            int splineCount = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i]?.Splines != null)
                    splineCount += parts[i].Splines.Length;
            }

            var mergedSplines = splineCount > 0 ? new PCGSpline[splineCount] : null;
            if (mergedSplines != null)
            {
                int splineIndex = 0;

                for (int i = 0; i < parts.Length; i++)
                {
                    var partSplines = parts[i]?.Splines;
                    if (partSplines == null)
                        continue;

                    Array.Copy(partSplines, 0, mergedSplines, splineIndex, partSplines.Length);
                    splineIndex += partSplines.Length;
                }
            }

            // 网格按部分顺序拼接，于是合并后的数组仍与合并后的点下标对齐。
            int meshCount = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i]?.Meshes != null)
                    meshCount += parts[i].Meshes.Length;
            }

            var mergedMeshes = meshCount > 0 ? new PCGMesh[meshCount] : null;
            if (mergedMeshes != null)
            {
                int meshIndex = 0;

                for (int i = 0; i < parts.Length; i++)
                {
                    var partMeshes = parts[i]?.Meshes;
                    if (partMeshes == null)
                        continue;

                    Array.Copy(partMeshes, 0, mergedMeshes, meshIndex, partMeshes.Length);
                    meshIndex += partMeshes.Length;
                }
            }

            // 独立几何也按部分顺序拼接：进去的每一块都还在。
            var mergedGeometry = geometryCount > 0 ? new PCGGeometryItem[geometryCount] : null;
            if (mergedGeometry != null)
            {
                int geometryIndex = 0;

                for (int i = 0; i < parts.Length; i++)
                {
                    var partGeometry = parts[i]?.Geometry;
                    if (partGeometry == null)
                        continue;

                    Array.Copy(partGeometry, 0, mergedGeometry, geometryIndex, partGeometry.Length);
                    geometryIndex += partGeometry.Length;
                }
            }

            return new PCGData(points, attributes)
            {
                Splines = mergedSplines,
                Meshes = mergedMeshes,
                Geometry = mergedGeometry,
            };
        }

        private static void CopyColumn(
            PCGAttributeSet source, string name, PCGAttributeType type,
            PCGAttributeSet destination, int destinationOffset, int count)
        {
            // 某一部分不带这个属性就等于贡献默认值，而目标列里本来就已经是默认值。
            if (source == null || !source.Contains(name))
                return;

            switch (type)
            {
            case PCGAttributeType.Float:
            {
                var src = source.TryGetFloatArray(name);
                if (src == null) return;
                var dst = destination.GetWritableFloatArray(name);
                Array.Copy(src, 0, dst, destinationOffset, count);
                break;
            }
            case PCGAttributeType.Vector3:
            {
                var src = source.TryGetVector3Array(name);
                if (src == null) return;
                var dst = destination.GetWritableVector3Array(name);
                Array.Copy(src, 0, dst, destinationOffset, count);
                break;
            }
            case PCGAttributeType.Int:
            {
                if (!source.Contains(name)) return;
                var dst = destination.GetWritableIntArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetInt(name, i);
                break;
            }
            case PCGAttributeType.Bool:
            {
                var dst = destination.GetWritableBoolArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetBool(name, i);
                break;
            }
            case PCGAttributeType.Vector2:
            {
                var dst = destination.GetWritableVector2Array(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetVector2(name, i);
                break;
            }
            case PCGAttributeType.Vector4:
            {
                var dst = destination.GetWritableVector4Array(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetVector4(name, i);
                break;
            }
            case PCGAttributeType.Quaternion:
            {
                var dst = destination.GetWritableQuaternionArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetQuaternion(name, i);
                break;
            }
            case PCGAttributeType.String:
            {
                var dst = destination.GetWritableStringArray(name);
                for (int i = 0; i < count; i++) dst[destinationOffset + i] = source.GetString(name, i);
                break;
            }
            }
        }

        /// <summary>
        /// 把若干数据集包成一个聚合体，供多连线引脚投递。各部分**不复制**，在聚合体被消费之前只是被引用。
        /// </summary>
        public static PCGData Aggregate(PCGData[] parts)
        {
            if (parts == null || parts.Length == 0)
                return Empty;

            // 只有一部分时不需要包。
            if (parts.Length == 1)
                return parts[0] ?? Empty;

            return new PCGData(Array.Empty<PCGPoint>())
            {
                Parts = parts,
            };
        }

        /// <summary>
        /// 把这份数据集摊平成它的组成部分。普通点集返回它自己。
        /// </summary>
        public PCGData[] Flatten()
        {
            if (Parts == null)
                return new[] { this };

            var result = new List<PCGData>(Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                if (Parts[i] == null)
                    continue;

                if (Parts[i].Parts != null)
                    result.AddRange(Parts[i].Flatten());
                else
                    result.Add(Parts[i]);
            }

            return result.ToArray();
        }

        /// <summary>返回所有点合起来的世界空间包围盒；没有点时返回空盒。</summary>
        public BoundingBox ComputeBounds()
        {
            if (Count == 0)
                return BoundingBox.Empty;

            var result = Points[0].Bounds;
            for (int i = 1; i < Points.Length; i++)
                result = BoundingBox.Merge(result, Points[i].Bounds);

            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PCGData(Points={Count}, Attributes={(SharedAttributes != null ? SharedAttributes.AttributeCount : 0)})";
        }
    }
}

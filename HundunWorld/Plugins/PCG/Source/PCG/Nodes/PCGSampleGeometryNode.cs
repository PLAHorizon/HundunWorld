// PCG 插件 - 几何采样器（把载荷里的网格采成点）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.43。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Data;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把**载荷里的**几何采成点：每块网格按面积权重撒 <c>Points Per Mesh</c> 个点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"Geometry 是数据类型"的另一个方向。此前只有 <c>MeshSampler</c> 能采网格，而它采的是**资产**
    /// （参数是资产 Guid、位置取自某个 Actor）——随图流动的几何没人能采。于是几何"进得去、出不来"，
    /// 那就不算数据类型。有了它，图里可以"造几何 → 采样 → 按点长东西"，与 UE5 用 DynamicMesh 采样的用法对位。
    /// </para>
    /// <para>
    /// **两种承载都认**：与点下标对齐的 <see cref="PCGData.Meshes"/>（摆放取自它所属的那个点）与
    /// 自带摆放的 <see cref="PCGData.Geometry"/>（独立几何，不属于任何点）。两块几何都按"网格建在自己的
    /// 局部原点"来读，于是采样就是"把网格摆到世界里的那个位置再撒点"。
    /// </para>
    /// <para>
    /// 每块几何用**各自错开**的随机流（种子按块下标再混一个大质数）：共用一条流会让同形状的每一块
    /// 在各自局部坐标里采出同一批点，一片同形状的东西看起来就像盖章。
    /// </para>
    /// <para>
    /// 一层没有几何可采时**原样透传**并留下 <see cref="PCGNodeBase.LastError"/>，不把这一层清空——
    /// "忘了接几何"的可解释行为是"这一层没变"，而清空会在下游表现为"那里什么都没长"。
    /// </para>
    /// <para>
    /// 产出**只带点**，不带几何：采样器的产出是新点，源几何留在上游那一支上（同一个节点被两个消费者读时
    /// 执行器只算一次，所以"既采样又保留几何"是接线的事，不是这个节点的副作用）。
    /// </para>
    /// </remarks>
    public sealed class PCGSampleGeometryNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "SampleGeometry";

        // 参数下标。顺序是存档格式的一部分；只能往后追加。
        private const int PPointsPerMesh = 0;
        private const int PDensity = 1;
        private const int PSeedOffset = 2;

        /// <summary>这个节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        /// <summary>每块几何采多少个点。</summary>
        public const int DefaultPointsPerMesh = 20;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>每块网格采多少个点。</summary>
        public int PointsPerMesh = DefaultPointsPerMesh;

        /// <summary>写进每个点的密度。</summary>
        public float Density = 1.0f;

        /// <summary>把两个几何采样器区分开。</summary>
        public int SeedOffset;

        /// <summary>按序列化参数创建节点。</summary>
        public PCGSampleGeometryNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            PointsPerMesh = _params.Get(PPointsPerMesh, DefaultPointsPerMesh);
            Density = _params.Get(PDensity, 1.0f);
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Sample Geometry";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Geometry to sample: either per-point meshes or standalone geometry."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points scattered over the geometry's surface, carrying its normals."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"{PointsPerMesh} point(s) per mesh, density={Density:F2}, seedOffset={SeedOffset}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null)
                return PCGData.Empty;

            if (PointsPerMesh <= 0)
            {
                LastError = "Sample Geometry has Points Per Mesh at zero, so nothing is sampled.";
                return input;
            }

            var pieces = new List<PCGData>(4);
            int item = 0;

            // 与点对齐的那一半：网格是**那个点的形状**，所以摆在点自己的变换里。
            for (int i = 0; i < input.Count; i++)
            {
                var mesh = input.Meshes != null && i < input.Meshes.Length ? input.Meshes[i] : null;

                if (mesh == null || mesh.VertexCount == 0)
                    continue;

                var point = input.Points[i];
                var placement = new Transform(point.Position, point.Rotation, ToVector3(point.Scale));
                var sampled = Sample(mesh, placement, context, item++);

                if (sampled == null)
                    return input;

                pieces.Add(sampled);
            }

            // 独立几何那一半：它自己带摆放。
            if (input.Geometry != null)
            {
                for (int i = 0; i < input.Geometry.Length; i++)
                {
                    var geometry = input.Geometry[i];

                    if (!geometry.IsUsable)
                        continue;

                    var sampled = Sample(geometry.Mesh, geometry.Placement, context, item++);

                    if (sampled == null)
                        return input;

                    pieces.Add(sampled);
                }
            }

            if (pieces.Count == 0)
            {
                LastError = "Sample Geometry found no geometry on its input; the layer passes through unchanged.";
                return input;
            }

            return PCGData.Merge(pieces.ToArray(), out _);
        }

        /// <summary>采一块几何；失败时写 <see cref="PCGNodeBase.LastError"/> 并返回 null。</summary>
        private PCGData Sample(PCGMesh mesh, Transform placement, PCGExecutionContext context, int itemIndex)
        {
            var triangles = new uint[mesh.Triangles.Length];

            for (int i = 0; i < triangles.Length; i++)
                triangles[i] = (uint)mesh.Triangles[i];

            // 每块各自错开种子：共用一条流会让同形状的每块在各自局部坐标里采出同一批点。
            int seed = unchecked(context.Seed + SeedOffset + itemIndex * 7919);

            var sampled = PCGMeshSurfaceSampling.Generate(
                mesh.Vertices, mesh.Normals, triangles, placement, PointsPerMesh, seed,
                context.CurrentNodeId, Density, out string error);

            if (error != null)
            {
                LastError = error;
                return null;
            }

            return sampled;
        }

        /// <summary>把 Float3 收成 Vector3（Flax 的两套向量类型不能直接互转）。</summary>
        private static Vector3 ToVector3(Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}

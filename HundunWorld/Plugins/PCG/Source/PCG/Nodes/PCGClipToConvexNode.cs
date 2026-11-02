// PCG 插件 - 把每个网格裁进一个凸区域（A ∩ B）：盒或圆柱
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.44 与 §7.6。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把每个网格裁进一个**凸**区域（盒或圆柱）：只保留落在里面的部分（<c>A ∩ B</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="PCGCutNode"/> 的分工：`Cut` 是一把**平面**刀（一刀到底，削平顶、切斜角），这里是**一个凸体**——
    /// "只留这个盒子/圆柱里的部分"。用途是"把一层的几何裁进一个形状"：出一块方料、出一个圆柱形底座、
    /// 把一个体积里的零碎几何收进一个罐子里。
    /// </para>
    /// <para>
    /// <b>它不做"挖方孔"</b>（<c>A − B</c>）。原因见 <see cref="PCGMeshBoolean"/> 的注解：凸体的补集是
    /// 朝外半空间的**并集**，而"按面依次裁"算的是**交**，交集是空集——所以那条路走不通（实测：区域放远处时
    /// 网格整个消失）。真正的差集需要"面在里/外"的分类，仍未做。
    /// </para>
    /// <para>
    /// 区域的位置/尺寸都在**形状自己的坐标系**里（网格都建在自己原点，摆放由点或独立几何的 `Placement` 决定），
    /// 所以同一个节点处理一片柱子时，每根柱子裁在同一个相对位置上。
    /// </para>
    /// <para>
    /// 它是**纯数据变换**（不碰场景），所以预览/判定运行**照常裁**——`DryRun` 的契约是"不调引擎"，不是"不干活"。
    /// 裁不动的那一块**原样保留**并把原因记进 <see cref="PCGNodeBase.LastError"/>；只经 `LastError` 报告，
    /// 不调用 <c>Debug.*</c>（纯算术节点会被离屏执行，DllNotFound 会直接崩）。
    /// </para>
    /// </remarks>
    public sealed class PCGClipToConvexNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "ClipToConvex";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PShape = 0;
        private const int PCenter = 1;
        private const int PSize = 2;
        private const int PSegments = 3;

        /// <summary>参数个数（槽位表要与之等长）。</summary>
        public const int ParameterCount = 4;

        /// <summary>盒区域的形状编号。</summary>
        public const int BoxShape = 0;

        /// <summary>圆柱区域的形状编号。</summary>
        public const int CylinderShape = 1;

        /// <summary>区域形状（<see cref="BoxShape"/> / <see cref="CylinderShape"/>）。</summary>
        public int Shape;

        /// <summary>区域中心（形状自己的坐标系）。</summary>
        public Vector3 Center;

        /// <summary>盒：三条边长；圆柱：X 是直径、Y 是高（Z 不用）。</summary>
        public Vector3 Size = new Vector3(100.0f, 100.0f, 100.0f);

        /// <summary>圆柱一圈多少边（棱柱近似）。</summary>
        public int Segments = 16;

        private readonly PCGNodeRegistry.PCGNodeSerializationData _params;

        /// <summary>按存档数据造节点。</summary>
        public PCGClipToConvexNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _params = data;
            Shape = _params.Get(PShape, BoxShape);
            Center = _params.Get(PCenter, Vector3.Zero);
            Size = _params.Get(PSize, new Vector3(100.0f, 100.0f, 100.0f));
            Segments = _params.Get(PSegments, 16);
        }

        /// <inheritdoc />
        public override string TypeId => Id;

        /// <inheritdoc />
        public override string DisplayName => "Clip To Convex";

        /// <inheritdoc />
        public override string DescribeParameters() =>
            (Shape == CylinderShape
                ? $"cylinder d {Size.X:F0} h {Size.Y:F0} x {Segments} sides"
                : $"box {Size.X:F0}x{Size.Y:F0}x{Size.Z:F0}") +
            (Center != Vector3.Zero ? $" at {Center}" : string.Empty);

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, tooltip: "The meshes to clip."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Only the parts inside the convex region."),
        };

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            var input = inputs[0];

            if (input == null)
                return PCGData.Empty;

            bool hasAligned = input.Meshes != null && input.Meshes.Length > 0;
            bool hasLoose = input.Geometry != null && input.Geometry.Length > 0;

            if (!hasAligned && !hasLoose)
            {
                LastError = "Clip To Convex found no geometry on its input; the layer passes through unchanged.";
                return input;
            }

            // 区域的面只算一次：逐个网格去算会把同一份棱柱摊 N 遍（N 是这一层的物件数）。
            var tool = Shape == CylinderShape
                ? PCGMeshBoolean.CylinderPlanes(Center, Math.Max(0.01f, Size.X * 0.5f), Math.Max(0.01f, Size.Y), Segments)
                : PCGMeshBoolean.BoxPlanes(Center, Size);

            var result = input.Clone(cloneAttributes: false);
            int refused = 0;
            string firstRefusal = null;

            if (hasAligned)
            {
                var meshes = new PCGMesh[input.Meshes.Length];

                for (int i = 0; i < input.Meshes.Length; i++)
                {
                    context.ThrowIfCancelled(i);
                    meshes[i] = Clip(input.Meshes[i], tool, ref refused, ref firstRefusal, i);
                }

                result.Meshes = meshes;
            }

            if (hasLoose)
            {
                var geometry = new PCGGeometryItem[input.Geometry.Length];

                for (int i = 0; i < input.Geometry.Length; i++)
                {
                    context.ThrowIfCancelled(i);

                    var item = input.Geometry[i];

                    if (item.IsUsable)
                        item.Mesh = Clip(item.Mesh, tool, ref refused, ref firstRefusal, i);

                    geometry[i] = item;
                }

                result.Geometry = geometry;
            }

            if (refused > 0)
            {
                LastError = $"{refused} mesh(es) could not be clipped and were kept as they were; " +
                            $"first reason: {firstRefusal}";
            }

            return result;
        }

        /// <summary>裁一块；裁不动就**原样交回**并记账（整层丢掉比留一块没裁的更糟，只留日志又看不见）。</summary>
        private static PCGMesh Clip(in PCGMesh mesh, List<PCGConvexPlane> tool,
            ref int refused, ref string firstRefusal, int index)
        {
            if (mesh == null || mesh.TriangleCount == 0)
                return mesh;

            if (!PCGMeshBoolean.TryIntersectConvex(mesh, tool, out var clipped, out string error))
            {
                refused++;
                firstRefusal ??= $"mesh {index}: {error}";
                return mesh;
            }

            // 网格整个落在区域外：交集是空的，交回空网格（"裁没了"是合法结果）。
            return clipped;
        }
    }
}

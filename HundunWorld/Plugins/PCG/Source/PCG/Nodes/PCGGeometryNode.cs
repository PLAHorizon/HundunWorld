// PCG 插件 - 内建几何图元（结构）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.33。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 给每个点一份形状，由点自身的尺寸程序化地建出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件本可以只摆资产、不做几何：点向生成器要一个模型，模型是美术师做成什么样的就是什么样。
    /// 这个节点走的是另一条路——形状是算出来的，所以一堵墙、一段楼梯、一块楼板或一段栅栏
    /// 不必为每个尺寸准备一个资产，一张图也因此能"产出建筑"而不只是"把东西撒开"。
    /// 它是 UE5 程序化几何工作流里的网格那一半，建网格的部分留在
    /// <see cref="PCGMeshBuilder"/> 里，这样不打开编辑器也能验。
    /// </para>
    /// <para>
    /// 网格写进 <see cref="PCGData.Meshes"/>，与点按下标对齐，而且<b>在各自点的局部空间里</b>：
    /// 点保留它自己的位置、旋转与缩放，谁把网格实体化谁就施加那个变换。
    /// 把形状留在局部，正是"一份建好的网格能服务每一个共享同一尺寸的点"的前提，
    /// 而不是把世界坐标烤进顶点里。
    /// </para>
    /// <para>
    /// 没有输入时本节点表现得像一个采样器：它在生成边界的中心造一个点，
    /// 尺寸由 <c>Cell Size</c> 决定。一个什么都没产出的源会让整张图连一块楼板都建不出来，
    /// 而一张图纸通常就是这样开始的。
    /// </para>
    /// </remarks>
    public sealed class PCGGeometryNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Geometry";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PShape = 0;
        private const int PSegments = 1;
        private const int PSteps = 2;
        private const int PCellSize = 3;
        private const int PUsePointScale = 4;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 5;

        /// <summary>形状种类，按编辑器列出的顺序。</summary>
        public const int ShapeBox = 0;
        public const int ShapeGrid = 1;
        public const int ShapeCylinder = 2;
        public const int ShapeStairs = 3;

        /// <summary>楔形（矩形底 + 沿 X 的屋脊）：带斜屋顶的房子。</summary>
        public const int ShapeWedge = 4;

        /// <summary>房屋形（墙 + 屋脊合成一个封闭实体）：描述直接长出一栋房，不需要外部模型资产。</summary>
        public const int ShapeHouse = 5;

        /// <summary>树形（树干 + 圆锥树冠合成一个封闭实体）：森林不再是浮空的锥。</summary>
        public const int ShapeTree = 6;

        /// <summary>编辑器提供的形状数目上限。</summary>
        public const int ShapeCount = 7;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>建哪一种形状。</summary>
        public int Shape = ShapeBox;

        /// <summary>圆柱周身的边数，或网格每轴的格子数。</summary>
        public int Segments = 8;

        /// <summary>楼梯里的踏步数。</summary>
        public int Steps = 4;

        /// <summary>无输入那一路用的尺寸，也用于缩放描述不出形状的那些点。</summary>
        public Vector3 CellSize = new Vector3(1000.0f, 200.0f, 1000.0f);

        /// <summary>设置时，点自身的缩放就是形状的尺寸。</summary>
        public bool UsePointScale = true;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGGeometryNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Shape = _params.Get(PShape, ShapeBox);
            Segments = Math.Max(1, _params.Get(PSegments, 8));
            Steps = Math.Max(1, _params.Get(PSteps, 4));
            CellSize = _params.GetVector3(PCellSize, new Vector3(1000.0f, 200.0f, 1000.0f));
            UsePointScale = _params.Get(PUsePointScale, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Geometry Primitives";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input,
                tooltip: "Points to shape. Unconnected, one point is made at the generation bounds centre."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying a mesh in its local space."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"{ShapeName(Shape)}, segments {Segments}, steps {Steps}" +
            (UsePointScale ? ", size = point scale" : $", size = {CellSize}");

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            // 认不出的形状下标正是一张更新的插件写出的图会长成的样子：退回立方体
            // 让这一层仍然可用、这一次运行仍然诚实，而不是产出一份没人解释得清的空结果。
            int shape = Shape >= 0 && Shape < ShapeCount ? Shape : ShapeBox;

            if (input == null || input.Count == 0)
            {
                var size = ClampedCellSize();
                var point = PCGPoint.Create(context.GenerationBounds.Center, 1.0f, context.Seed);
                point.Scale = new Float3(size.X, size.Y, size.Z);
                point.UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                var single = PCGData.Create(1);
                single.Points[0] = point;
                single.Meshes = new[] { Build(shape, size) };
                return single;
            }

            var result = input.Clone();
            var meshes = new PCGMesh[result.Count];

            for (int i = 0; i < result.Count; i++)
            {
                context.ThrowIfCancelled(i);

                var fromPoint = new Vector3(
                    result.Points[i].Scale.X, result.Points[i].Scale.Y, result.Points[i].Scale.Z);

                // 一个描述不出实体的缩放（从没碰过它的采样器，或某个轴留在 0）
                // 退回配置好的尺寸，于是形状存在，而不是塌成一片平面。
                var size = UsePointScale && IsUsable(fromPoint) ? fromPoint : ClampedCellSize();

                meshes[i] = Build(shape, size);
            }

            result.Meshes = meshes;
            return result;
        }

        /// <summary>按给定尺寸，在形状自己的局部空间里建出一份形状。</summary>
        public PCGMesh Build(int shape, Vector3 size)
        {
            switch (shape)
            {
            case ShapeGrid:
                return PCGMeshBuilder.Grid(new Vector2(Math.Abs(size.X), Math.Abs(size.Z)), Segments);

            case ShapeCylinder:
                // 圆柱由它的 X/Z 占位范围界定，所以半径取两者里较小的那个：
                // 取较大的那个会让形状戳出它本来按尺寸适配进去的那个格子。
                return PCGMeshBuilder.Cylinder(
                    Math.Max(0.001f, Math.Min(Math.Abs(size.X), Math.Abs(size.Z)) * 0.5f), Math.Abs(size.Y), Segments);

            case ShapeStairs:
                return PCGMeshBuilder.Stairs(size, Steps);

            case ShapeWedge:
                return PCGMeshBuilder.Wedge(size);

            case ShapeHouse:
                return PCGMeshBuilder.House(size);

            case ShapeTree:
                return PCGMeshBuilder.Tree(size, Segments);

            default:
                return PCGMeshBuilder.Box(size);
            }
        }

        /// <summary>形状的名字，给参数摘要用。</summary>
        public static string ShapeName(int shape)
        {
            switch (shape)
            {
            case ShapeGrid: return "Grid";
            case ShapeCylinder: return "Cylinder";
            case ShapeStairs: return "Stairs";
            case ShapeWedge: return "Wedge";
            case ShapeHouse: return "House";
            case ShapeTree: return "Tree";
            default: return "Box";
            }
        }

        private Vector3 ClampedCellSize() =>
            new Vector3(
                Math.Max(0.001f, CellSize.X),
                Math.Max(0.001f, CellSize.Y),
                Math.Max(0.001f, CellSize.Z));

        private static bool IsUsable(Vector3 size) =>
            size.X > 0.0f && size.Y > 0.0f && size.Z > 0.0f;
    }
}

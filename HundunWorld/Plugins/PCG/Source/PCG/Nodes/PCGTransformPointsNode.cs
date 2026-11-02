// PCG 插件 - 变换点节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.3。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 按一个固定的增量移动、旋转并缩放每一个点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这个增量可以按世界轴表达，也可以按每个点自己的坐标系表达。局部空间才是
    /// 让一堆朝向一致的道具保住朝向的东西："把每块石头沿它自己的 up 抬起"，
    /// 一旦采样器把它们对齐到坡面上，不许变成"把每块石头沿世界 Y 抬起"。
    /// </para>
    /// <para>
    /// 包围盒由进来的半范围在增量旋转之下重建。因为进来的盒子
    /// 本来就已经是旋转内容的一个轴对齐近似，一个经过这个节点被旋转两次的
    /// 点会让它的盒子略微膨胀。误差的方向是刻意的：偏大的盒子只会多裁，
    /// 从不漏裁，于是重叠检验仍然安全。
    /// </para>
    /// </remarks>
    public sealed class PCGTransformPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "TransformPoints";

        // 参数下标。顺序属于存档图格式，只能追加。
        private const int PTranslation = 0;
        private const int PRotationDegrees = 1;
        private const int PScaleMultiplier = 2;
        private const int PLocalSpace = 3;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 4;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>位置增量。</summary>
        public Vector3 Translation;

        /// <summary>以角度计的旋转增量，绕点自己的中心施加。</summary>
        public Vector3 RotationDegrees;

        /// <summary>按分量的缩放乘数。</summary>
        public Vector3 ScaleMultiplier = Vector3.One;

        /// <summary>设置时，平移与旋转按每个点的局部坐标系理解。</summary>
        public bool LocalSpace;

        /// <summary>由序列化的参数创建节点。</summary>
        public PCGTransformPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Translation = _params.GetVector3(PTranslation, Vector3.Zero);
            RotationDegrees = _params.GetVector3(PRotationDegrees, Vector3.Zero);
            ScaleMultiplier = _params.GetVector3(PScaleMultiplier, Vector3.One);
            LocalSpace = _params.Get(PLocalSpace, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Transform Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to transform."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Transformed points."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"translation={Translation}, rotation={RotationDegrees}, scale={ScaleMultiplier}, local={LocalSpace}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            // 写入之前先拿到存储的所属权：输入可能还在别处被引用。
            var result = input.Clone();
            var points = result.Points;

            bool identityRotation = RotationDegrees.X == 0f && RotationDegrees.Y == 0f && RotationDegrees.Z == 0f;
            var delta = identityRotation ? Quaternion.Identity : PCGPointTransform.EulerDegrees(RotationDegrees);

            PCG.Execution.PCGParallel.For(points.Length, i =>
            {
                context.ThrowIfCancelled(i);

                var point = points[i];

                var offset = LocalSpace
                    ? Vector3.Transform(Translation, point.Rotation)
                    : Translation;

                // 局部空间在右边复合（在该点的坐标系里旋转），世界空间在左边复合
                // （绕世界轴旋转）；这个顺序就是两种模式之间的全部区别。
                point.Position = point.Position + offset;
                if (!identityRotation)
                {
                    point.Rotation = LocalSpace
                        ? Quaternion.Multiply(point.Rotation, delta)
                        : Quaternion.Multiply(delta, point.Rotation);
                }

                point.Scale = new Float3(
                    point.Scale.X * ScaleMultiplier.X,
                    point.Scale.Y * ScaleMultiplier.Y,
                    point.Scale.Z * ScaleMultiplier.Z);

                var half = PCGPointTransform.EnclosingExtents(
                    PCGPointTransform.HalfExtents(point.Bounds), identityRotation ? Quaternion.Identity : delta);

                point.Bounds = PCGPointTransform.MakeBox(point.Position, new Vector3(
                    half.X * ScaleMultiplier.X,
                    half.Y * ScaleMultiplier.Y,
                    half.Z * ScaleMultiplier.Z));

                points[i] = point;
            });

            return result;
        }
    }
}

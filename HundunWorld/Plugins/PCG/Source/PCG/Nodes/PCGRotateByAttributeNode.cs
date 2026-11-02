// PCG 插件 - 按属性旋转（modifier）
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.28。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 让每个点绕**它自己**的竖轴转一个从某一列取来的角度——就是让散点朝着某个方向
    /// 而不是随机朝向的那一步。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"把一列还回变换里"那三个节点里的第三个：<see cref="PCGScaleByAttributeNode"/> 把一列花在尺寸上，
    /// <see cref="PCGOffsetByAttributeNode"/> 花在位置上，这一个花在指向上。没有它，一张图能改的
    /// 一切就是物件的大小与位置，唯独朝哪边看改不了——Random Offset 的抖动是碰运气，而
    /// Transform Points 的旋转是个常数，于是"顺着流向朝前"这种话根本写不出来。
    /// </para>
    /// <para>
    /// 这一转绕的是点**自己的**竖轴，走的也是生成器用的那个助手，
    /// 所以一个贴着坡面对齐过的点，转起来仍然站在坡上而不是被掀开。角度用度数，
    /// 与图里其他旋转参数是同一个单位。
    /// </para>
    /// <para>
    /// 点的包围盒是**重新算**的，不是放着不管：盒子是轴对齐的，所以点一转、它真正盖住的东西
    /// 也跟着转，留着一个旧盒子会让剪枝去测这形状早就换掉的旧脚印。
    /// </para>
    /// </remarks>
    public sealed class PCGRotateByAttributeNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "RotateByAttribute";

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PAttribute = 0;
        private const int PYawMin = 1;
        private const int PYawMax = 2;
        private const int PInvert = 3;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 4;

        private const float DegToRad = MathF.PI / 180f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>角度从哪一列读。留空则读点的密度。</summary>
        public string AttributeName = string.Empty;

        /// <summary>该列为 0 时施加的转角，单位度。</summary>
        public float YawMin;

        /// <summary>该列为 1 时施加的转角，单位度。</summary>
        public float YawMax = 360.0f;

        /// <summary>置位时，列值越低转得越大。</summary>
        public bool Invert;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGRotateByAttributeNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            AttributeName = _params.Get(PAttribute, string.Empty) ?? string.Empty;
            YawMin = _params.Get(PYawMin, 0.0f);
            YawMax = _params.Get(PYawMax, 360.0f);
            Invert = _params.Get(PInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Rotate By Attribute";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points to turn."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, turned about their own up axis."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            return $"{name} -> yaw [{YawMin:F1},{YawMax:F1}] deg, invert={Invert}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            bool byDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            float[] values = null;

            if (!byDensity)
            {
                var attributes = input.SharedAttributes;
                values = attributes != null ? attributes.TryGetFloatArray(AttributeName) : null;

                if (values == null)
                {
                    LastError = $"Rotate By Attribute could not read '{AttributeName}': no such float attribute " +
                                "on this layer. The input passes through unchanged.";
                    return input.Clone();
                }
            }

            var result = input.Clone();
            var points = result.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);

                float value = byDensity ? points[i].Density : values[i];

                // 一个权重，钳法与另外两个"由列驱动"的节点一致：一个乱跑的值不该把物件
                // 转上好几圈。
                value = Math.Min(1.0f, Math.Max(0.0f, value));

                if (Invert)
                    value = 1.0f - value;

                float degrees = YawMin + (YawMax - YawMin) * value;

                if (degrees == 0.0f)
                    continue;

                float radians = degrees * DegToRad;
                var delta = Quaternion.RotationAxis(Vector3.Up, radians);

                // 盒子是显式设进去的，没走 UpdateBounds：那个助手会拿点**当前**的变换重建盒子，
                // 把已经转过的尺寸再交给它，就等于把这一转乘了两次、脚印原地不动。
                // Transform Points 走的是同一条路，理由也一样。
                var half = PCGPointTransform.EnclosingExtents(PCGPointTransform.HalfExtents(points[i].Bounds), delta);

                points[i].Rotation = PCGPointTransform.YawAboutLocalUp(points[i].Rotation, radians);
                points[i].Bounds = PCGPointTransform.MakeBox(points[i].Position, half);
            }

            return result;
        }
    }
}

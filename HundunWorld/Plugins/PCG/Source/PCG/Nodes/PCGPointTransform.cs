// PCG 插件 - 被各 modifier 节点共用的点变换助手
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.3。

using System;
using FlaxEngine;

namespace PCG.Nodes
{
    /// <summary>
    /// 移动、旋转、缩放点时的数学，同时让它们的包围盒保持诚实。
    /// </summary>
    internal static class PCGPointTransform
    {
        private const float Rad2Deg = 180f / MathF.PI;

        /// <summary>
        /// 让一个旋转绕它**自己**的竖轴再转一下——散点要的就是这种自转：物件不会全都朝一个方向，
        /// 也不会因此从它们本来贴着的表面上被抬起来。
        /// </summary>
        /// <remarks>
        /// 复合写在**右边**，也就是局部坐标系——这与 <see cref="PCGTransformPointsNode"/>
        /// 给自己的局部模式所定的规则是同一条、同一个理由。生成器过去按世界 +Y 建偏航、
        /// 并把它复合在**左边**，于是只要对齐开着，物件就被坡度角<i>掀离</i>了表面——
        /// 而那恰恰是对齐这一步存在的意义所在，同时注释还声称对齐保持完好。
        /// 现在它在离线门里被断言：旋转这种东西没法目测，
        /// 而引擎侧的调用方没有场景就够不着。
        /// </remarks>
        public static Quaternion YawAboutLocalUp(in Quaternion rotation, float radians)
        {
            return Quaternion.Multiply(rotation, Quaternion.RotationAxis(Vector3.Up, radians));
        }

        /// <summary>
        /// 一个有向包围盒的半尺寸，被装进轴对齐盒子之后所需的半尺寸。
        /// </summary>
        /// <remarks>
        /// 正好是 <c>|M| * h</c>：把旋转后的基轴按绝对值相加。
        /// 这样做而不去变换八个角点，是为了在每点的循环里保持零分配——
        /// 那种循环一跑就是几十万点。
        /// </remarks>
        public static Vector3 EnclosingExtents(Vector3 halfExtents, Quaternion rotation)
        {
            var x = Vector3.Transform(new Vector3(halfExtents.X, 0f, 0f), rotation);
            var y = Vector3.Transform(new Vector3(0f, halfExtents.Y, 0f), rotation);
            var z = Vector3.Transform(new Vector3(0f, 0f, halfExtents.Z), rotation);

            return new Vector3(
                MathF.Abs(x.X) + MathF.Abs(y.X) + MathF.Abs(z.X),
                MathF.Abs(x.Y) + MathF.Abs(y.Y) + MathF.Abs(z.Y),
                MathF.Abs(x.Z) + MathF.Abs(y.Z) + MathF.Abs(z.Z));
        }

        /// <summary>点当前包围盒的半尺寸。</summary>
        public static Vector3 HalfExtents(in BoundingBox box)
        {
            return new Vector3(
                (box.Maximum.X - box.Minimum.X) * 0.5f,
                (box.Maximum.Y - box.Minimum.Y) * 0.5f,
                (box.Maximum.Z - box.Minimum.Z) * 0.5f);
        }

        /// <summary>以给定中心与半尺寸搭一个盒子。</summary>
        public static BoundingBox MakeBox(Vector3 center, Vector3 halfExtents)
        {
            return new BoundingBox(
                new Vector3(center.X - halfExtents.X, center.Y - halfExtents.Y, center.Z - halfExtents.Z),
                new Vector3(center.X + halfExtents.X, center.Y + halfExtents.Y, center.Z + halfExtents.Z));
        }

        /// <summary>由欧拉角（度数）构造旋转。</summary>
        /// <remarks>
        /// 角度直接传进去：<c>Quaternion.Euler(x, y, z)</c> 本来就吃度数
        /// （吃弧度的是同胞的 <c>Quaternion.RotationYawPitchRoll</c>；见 Source/Engine/Core/Math/Quaternion.h）。
        /// 这里过去先乘 180/pi，于是 90 度的偏航被转成 116.62 度——而因为它读起来
        /// 像"大约四分之一圈"，没人去追；一个 Copy Points 测试反而围着这个症状被放宽了，
        /// 于是 Transform Points / Copy Points / Random Offset 上每一个旋转参数都静默地错了。
        /// 现在这个单位是被测试钉住的，不再是一句注释。
        /// </remarks>
        public static Quaternion EulerDegrees(Vector3 degrees)
        {
            return Quaternion.Euler(degrees.X, degrees.Y, degrees.Z);
        }

        /// <summary>由点的朝向算出表面陡度（度数，落在 [0,90]）。</summary>
        /// <remarks>
        /// 取绝对值的点积是故意的：采样器把坡度报成"离竖直多少度"，
        /// 不管法线朝上还是朝下。所以一个被翻了 180 度的点，
        /// 必须仍然读成同一个陡度。两套口径混着用，就会让一个只做旋转的 modifier
        /// 静默地把点搬过坡度阈值。
        /// </remarks>
        public static float SlopeDegrees(in PCGPoint point)
        {
            var normal = Vector3.Transform(Vector3.Up, point.Rotation);
            float dot = Vector3.Dot(normal, Vector3.Up);
            dot = MathF.Min(1f, MathF.Max(-1f, dot));
            return MathF.Acos(MathF.Abs(dot)) * Rad2Deg;
        }
    }
}

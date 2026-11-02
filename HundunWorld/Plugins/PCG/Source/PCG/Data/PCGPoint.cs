// PCG 插件 —— 点的数据模型
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md。

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// 一个程序化内容生成的点。
    /// </summary>
    /// <remarks>
    /// 布局按 docs/PCG_PLUGIN_DESIGN.md §3.1：
    /// <list type="bullet">
    /// <item><see cref="Position"/> 用 <see cref="Vector3"/>（float32），因为 <c>Float3</c> 是半精度类型，
    /// 在世界坐标上丢精度丢得很厉害。</item>
    /// <item><see cref="Scale"/> 用 <see cref="Float3"/>，与 <c>Transform.Scale</c> / <c>Actor.Scale</c>
    /// 完全一致，省掉每个生成实例一次转换。</item>
    /// <item><see cref="Seed"/> 用 <see cref="int"/>，因为 Flax 的 <c>RandomStream</c> 只收 int。</item>
    /// <item><see cref="Attributes"/> 是引用类型，在点之间共享；每一条写路径都必须走
    /// <see cref="PCGAttributeSet.EnsureOwned"/>。见 §3.2 的写时复制契约。</item>
    /// </list>
    /// </remarks>
    [Serializable]
    public struct PCGPoint
    {
        /// <summary>世界坐标位置。</summary>
        public Vector3 Position;

        /// <summary>旋转。</summary>
        public Quaternion Rotation;

        /// <summary>缩放。与引擎原生的 <c>Float3</c> 缩放类型一致。</summary>
        public Float3 Scale;

        /// <summary>[0,1] 之间的密度。驱动筛选与生成概率。</summary>
        public float Density;

        /// <summary>逐点的随机种子。增量重跑要确定性就靠它。</summary>
        public int Seed;

        /// <summary>轴对齐的世界包围盒，用于剪枝与重叠测试。</summary>
        /// <remarks>
        /// Flax 的 <see cref="BoundingBox"/> 是轴对齐的，所以剪枝考虑不到点的旋转。
        /// 真需要时改用 <c>OrientedBoundingBox</c>；这里刻意不拿它当默认。
        /// </remarks>
        public BoundingBox Bounds;

        /// <summary>共享的逐点属性存储。可能为 null，也可能在点之间共享。</summary>
        public PCGAttributeSet Attributes;

        /// <summary>取出这个点所表示的变换。</summary>
        public Transform GetTransform()
        {
            return new Transform(Position, Rotation, Scale);
        }

        /// <summary>造一个旋转为单位、缩放为 1 的点。</summary>
        public static PCGPoint Create(Vector3 position, float density = 1.0f, int seed = 0)
        {
            return new PCGPoint
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Float3.One,
                Density = Mathf.Saturate(density),
                Seed = seed,
                Bounds = new BoundingBox(position, position),
                Attributes = null,
            };
        }

        /// <summary>造一个带指定变换的点。</summary>
        public static PCGPoint Create(in Transform transform, float density = 1.0f, int seed = 0)
        {
            return new PCGPoint
            {
                Position = transform.Translation,
                Rotation = transform.Orientation,
                Scale = transform.Scale,
                Density = Mathf.Saturate(density),
                Seed = seed,
                Bounds = new BoundingBox(transform.Translation, transform.Translation),
                Attributes = null,
            };
        }

        /// <summary>
        /// 按局部空间的尺寸盒（绕原点的半长）重算 <see cref="Bounds"/>。
        /// 改过 Position/Rotation/Scale 之后，或采样器赋了资产尺寸之后，都要调一次。
        /// </summary>
        /// <param name="localExtents">这个点内容在局部空间的半长。给 0 就得到一盒退化的包围盒。</param>
        public void UpdateBounds(Vector3 localExtents)
        {
            var local = new BoundingBox(-localExtents, localExtents);
            Bounds = BoundingBox.Transform(local, GetTransform());
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PCGPoint(Pos={Position}, Density={Density:F3}, Seed={Seed})";
        }
    }
}

// PCG 插件 - 独立几何（Geometry 数据类型的第二种承载）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.43。

using FlaxEngine;
using PCG.Execution;

namespace PCG
{
    /// <summary>
    /// 一块**自带摆放**的几何：网格 + 它在世界（或宿主）空间里的位置/旋转/缩放。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"几何能独立于点存在"的那一半。另一种承载是 <see cref="PCGData.Meshes"/>：那里的网格与**点下标对齐**，
    /// 摆放来自它所属的点（网格建在原点、由点的变换摆出去），配对是零成本的——所以那条快路一条都不动。
    /// 但"图里只有几何、没有点"用那种承载表达不出来，而它正是 UE5 把 DynamicMesh 当**空间数据类型**的意思：
    /// 一块造出来的地形/一面墙/一根柱子可以不属于任何点。
    /// </para>
    /// <para>
    /// 独立几何没有点来承载摆放，所以它**自己带一个 <see cref="Transform"/>**。这不是可选字段：
    /// 少了它，两块几何就都待在原点、只能靠顶点坐标区分，而"把它挪到那儿"这件事就没有地方可写。
    /// </para>
    /// <para>
    /// 做成 <c>struct</c> 是有意的：数组按值复制、里面的网格按引用共享，于是"克隆一份数据"不会复制几千个顶点
    /// （与 <see cref="PCGData.Meshes"/> 的取舍一致），而给某个元素重新赋值也不会反手改到别人读的那一份。
    /// </para>
    /// </remarks>
    public struct PCGGeometryItem
    {
        /// <summary>几何本身；建在**自己的局部原点**上。</summary>
        public PCGMesh Mesh;

        /// <summary>它摆在哪儿。</summary>
        public Transform Placement;

        /// <summary>这块几何有没有东西可画（没有网格或没有顶点时为 false）。</summary>
        public bool IsUsable => Mesh != null && Mesh.VertexCount > 0;

        /// <summary>把一块网格放到一个位置（不旋转、不缩放）。</summary>
        public static PCGGeometryItem At(PCGMesh mesh, Vector3 position) =>
            new PCGGeometryItem { Mesh = mesh, Placement = new Transform(position, Quaternion.Identity, Vector3.One) };

        /// <summary>把一块网格按给定的摆放放好。</summary>
        public static PCGGeometryItem At(PCGMesh mesh, Transform placement) =>
            new PCGGeometryItem { Mesh = mesh, Placement = placement };

        /// <summary>顶点数（诊断用；几何列表里每块多少顶点）。</summary>
        public int VertexCount => Mesh?.VertexCount ?? 0;

        /// <inheritdoc />
        public override string ToString() =>
            IsUsable ? $"{Mesh.VertexCount} vertex/vertices @ {Placement.Translation}" : "empty geometry";
    }
}

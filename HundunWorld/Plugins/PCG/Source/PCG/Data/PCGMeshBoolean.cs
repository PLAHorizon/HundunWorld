// PCG 插件 - 把网格裁进一个**凸**区域（A ∩ B）：布尔"交"里能证明的那一档，纯核
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.44 与 §7.6。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>凸区域的一个面：面上一点 + 朝外的法线。</summary>
    public struct PCGConvexPlane
    {
        /// <summary>面上的一点。</summary>
        public Vector3 Point;

        /// <summary>朝外的法线（不必归一，零长度会被拒）。</summary>
        public Vector3 OutwardNormal;

        /// <summary>造一个面。</summary>
        public static PCGConvexPlane At(Vector3 point, Vector3 outwardNormal) =>
            new PCGConvexPlane { Point = point, OutwardNormal = outwardNormal };
    }

    /// <summary>
    /// 把网格裁进一个**凸**区域：<c>A ∩ B</c>（只保留落在凸体里的部分）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么是"交"而不是"差"（这条是踩出来的，值得记）。</b>我原本以为 <c>A − B</c>（挖方孔）对**凸**的 B
    /// 也能这么证：凸体的补集"正好是它每个面的朝外半空间"——<b>这句话是错的</b>。补集是那些半空间的**并集**，
    /// 而"按每个面各截一刀、每次都保留外面那一侧"算的是它们的**交集**，而一个立体的所有朝外半空间之交是**空集**。
    /// 实测就是这么炸的：区域放在离立方体一百米的地方（本该"够不着、一点不少"），结果立方体**整个消失**
    /// （体积 64 → 0）。只有**半空间**的补集还是半空间（那正是 `Cut` 能证的那一格），所以挨着交付的这条能力只能是"交"。
    /// 真正的 <c>A − B</c> 需要"面在里/外"的分类 + 用 B 的面重建边界，那是 §5.1.44 判据④ 刻意推迟的那一格。
    /// </para>
    /// <para>
    /// <b>做法</b>：凸区域是若干"朝内半空间"的交，于是"只保留里面的部分"就是按每个面依次裁一刀、每次都保留
    /// **朝内**那一侧。每一步都是 <see cref="PCGMeshCut.TryCutByPlane"/>，而它已经保证"要么给出封闭实体、
    /// 要么拒绝"（§7.5、§7.6），所以产物仍然封闭、切口仍然封上，且能被解析体积逐点钉住。
    /// </para>
    /// <para>
    /// <b>边界（不装作没有）</b>：① 区域必须**凸**（盒、圆柱/棱柱都可以）；② 结果可能被切成多个连通块
    /// （同一个网格里的三角形互不相邻，合法）；③ 区域与网格不相交时交回空网格（交集本来就是空的）。
    /// </para>
    /// </remarks>
    public static class PCGMeshBoolean
    {
        /// <summary>
        /// <c>A ∩ B</c>：按区域的每个面依次截一刀，每次都保留**朝内**那一侧。
        /// </summary>
        /// <param name="mesh">被裁的网格（应当是封闭的）。</param>
        /// <param name="tool">区域的面；必须围出一个**凸**体。</param>
        /// <param name="result">裁完的网格（失败或交集为空时是空网格）。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>是否裁成功。</returns>
        public static bool TryIntersectConvex(in PCGMesh mesh, IReadOnlyList<PCGConvexPlane> tool,
            out PCGMesh result, out string error)
        {
            result = mesh;
            error = null;

            if (tool == null || tool.Count < 4)
            {
                error = "A convex region needs at least four faces (a tetrahedron).";
                result = PCGMesh.Empty;
                return false;
            }

            for (int i = 0; i < tool.Count; i++)
            {
                // 每一步都保留**朝内**那一侧（`keepPositiveSide: false`，而面给的是朝外法线）
                // ⇒ 交集就是"由这些面围出来的凸体" ⇒ 结果 = A ∩ B。
                if (!PCGMeshCut.TryCutByPlane(result, tool[i].Point, tool[i].OutwardNormal, false,
                        out var next, out string cutError))
                {
                    error = $"Clipping refused at region face {i}: {cutError}";
                    result = PCGMesh.Empty;
                    return false;
                }

                // 已经被裁空（网格整个在区域外）：后面几刀不必再走，空网格会被拒。
                if (next == null || next.TriangleCount == 0)
                {
                    result = PCGMesh.Empty;
                    return true;
                }

                result = next;
            }

            return true;
        }

        /// <summary>盒区域的面（6 个），<paramref name="size"/> 是三条边长、<paramref name="center"/> 是中心。</summary>
        public static List<PCGConvexPlane> BoxPlanes(Vector3 center, Vector3 size)
        {
            var half = new Vector3(Math.Abs(size.X), Math.Abs(size.Y), Math.Abs(size.Z)) * 0.5f;

            return new List<PCGConvexPlane>(6)
            {
                PCGConvexPlane.At(center + new Vector3(half.X, 0, 0), Vector3.UnitX),
                PCGConvexPlane.At(center - new Vector3(half.X, 0, 0), -Vector3.UnitX),
                PCGConvexPlane.At(center + new Vector3(0, half.Y, 0), Vector3.UnitY),
                PCGConvexPlane.At(center - new Vector3(0, half.Y, 0), -Vector3.UnitY),
                PCGConvexPlane.At(center + new Vector3(0, 0, half.Z), Vector3.UnitZ),
                PCGConvexPlane.At(center - new Vector3(0, 0, half.Z), -Vector3.UnitZ),
            };
        }

        /// <summary>
        /// 圆柱区域的面：轴沿着 Y、<paramref name="radius"/> 半径、<paramref name="height"/> 高，
        /// 用 <paramref name="sides"/> 个侧面（棱柱）近似。
        /// </summary>
        public static List<PCGConvexPlane> CylinderPlanes(Vector3 center, float radius, float height, int sides)
        {
            int edges = Math.Max(3, Math.Min(128, sides));
            float r = Math.Abs(radius);
            float half = Math.Abs(height) * 0.5f;

            var planes = new List<PCGConvexPlane>(edges + 2);

            for (int i = 0; i < edges; i++)
            {
                float angle = (float)(Math.PI * 2.0 * i / edges);
                var outward = new Vector3((float)Math.Cos(angle), 0.0f, (float)Math.Sin(angle));

                // 侧面与圆柱相切：面上的一点就是"圆心 + 法线 × 半径"。
                planes.Add(PCGConvexPlane.At(center + outward * r, outward));
            }

            planes.Add(PCGConvexPlane.At(center + new Vector3(0, half, 0), Vector3.UnitY));
            planes.Add(PCGConvexPlane.At(center - new Vector3(0, half, 0), -Vector3.UnitY));

            return planes;
        }
    }
}

// PCG 插件 - 网格表面采样（纯几何）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.1。

using System;
using FlaxEngine;

namespace PCG.Data
{
    /// <summary>
    /// 三角面表面采样：不碰任何引擎对象，所以它能离屏测量、离屏断言。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用它的节点（<c>PCGMeshSamplerNode</c>）要穿过一个已加载的资产才拿得到自己的
    /// 几何，而依赖资产的测试只能说"它跑过了"。把算术拆出来，是为了让真正要紧的那几条
    /// 性质——点落在自己那个三角形里、被选中的概率与面积成正比、
    /// 退化三角形永不被选中、重跑复现同一套布局——都能对着
    /// 手工搭出来的、答案已知的几何逐条核过。
    /// </para>
    /// </remarks>
    public static class PCGMeshSurfaceSampling
    {
        /// <summary>面积小于这个值的三角形按退化处理。</summary>
        /// <remarks>
        /// 导出的网格里满是零面积三角形（压扁的接缝、拆成多块后的 UV 壳）。权重给 0 把它们挡在选取之外，
        /// 而不是让采样点堆到一条线上去。
        /// </remarks>
        public const float MinTriangleArea = 1e-8f;

        /// <summary>用相对原点的向量算这个三角形的面积。</summary>
        public static float TriangleArea(in Vector3 a, in Vector3 b, in Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            return Cross(ab, ac).Length * 0.5f;
        }

        /// <summary>
        /// 每个三角形的面积累加权重，按三角形出现的顺序排。
        /// </summary>
        /// <param name="positions">顶点位置。</param>
        /// <param name="triangles">三角索引，每个三角形三个。</param>
        /// <param name="lodIndex">为与节点的 LOD 处理对齐而预留，暂未使用。</param>
        /// <param name="totalArea">可用三角形面积之和；没有可采的东西时是 0。</param>
        /// <returns>面积累加表；输入描述不出三角形时给 null。</returns>
        public static float[] BuildCumulativeAreas(Vector3[] positions, uint[] triangles, out float totalArea)
        {
            totalArea = 0.0f;

            if (positions == null || triangles == null || triangles.Length % 3 != 0)
                return null;

            int count = triangles.Length / 3;
            if (count == 0)
                return null;

            var cumulative = new float[count];

            for (int t = 0; t < count; t++)
            {
                uint ia = triangles[t * 3];
                uint ib = triangles[t * 3 + 1];
                uint ic = triangles[t * 3 + 2];

                if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                    return null;

                float area = TriangleArea(positions[ia], positions[ib], positions[ic]);
                if (area < MinTriangleArea)
                    area = 0.0f;

                totalArea += area;
                cumulative[t] = totalArea;
            }

            if (totalArea <= 0.0f)
                return null;

            return cumulative;
        }

        /// <summary>
        /// 面积累加表中包含 <paramref name="roll"/> 的那一段所属三角形的下标。
        /// </summary>
        /// <param name="cumulative">来自 <see cref="BuildCumulativeAreas"/>。</param>
        /// <param name="totalArea">它的总面积。</param>
        /// <param name="roll">取值落在 [0, totalArea) 里。</param>
        /// <remarks>
        /// 退化三角形自己没有那一段（它们的累加项重复前一个值），所以二分查找只要返回
        /// "第一个够到 roll 的段"就绝不会落在退化三角形上——这也是为什么查找找的是
        /// 严格大于的边界，而不是大于等于。
        /// </remarks>
        public static int PickTriangle(float[] cumulative, float totalArea, float roll)
        {
            if (cumulative == null || cumulative.Length == 0)
                return -1;

            float target = Math.Max(0.0f, Math.Min(roll, totalArea));

            int low = 0;
            int high = cumulative.Length - 1;

            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (cumulative[mid] > target)
                    high = mid;
                else
                    low = mid + 1;
            }

            return low;
        }

        /// <summary>
        /// 重心坐标 (1-u-v, u, v) 落在三角形内部的那个点。
        /// </summary>
        /// <remarks>
        /// 其中一个坐标取平方根，才让这套映射在整个三角形上是均匀的：把 u 和 v 各自
        /// 当独立均匀数来取，会把采样点堆到那两条边相交的那个顶点上。
        /// </remarks>
        /// <param name="a">第一个角。</param>
        /// <param name="b">第二个角。</param>
        /// <param name="c">第三个角。</param>
        /// <param name="u">[0,1) 上的均匀数。</param>
        /// <param name="v">[0,1) 上的均匀数。</param>
        /// <param name="normalU">第一个角的法线。</param>
        /// <param name="normalV">第二个角的法线。</param>
        /// <param name="normalW">第三个角的法线。</param>
        /// <param name="position">采到的位置。</param>
        /// <param name="normal">插值出来的法线；它自己有方向时会被归一化。</param>
        public static void SampleTriangle(
            in Vector3 a, in Vector3 b, in Vector3 c, float u, float v,
            in Vector3 normalU, in Vector3 normalV, in Vector3 normalW,
            out Vector3 position, out Vector3 normal)
        {
            float su = Mathf.Sqrt(Mathf.Max(0.0f, u));
            float w = 1.0f - su;
            float tv = v * su;

            position = a * w + b * tv + c * (su - tv);

            normal = normalU * w + normalV * tv + normalW * (su - tv);
            float length = normal.Length;
            if (length > 1e-6f)
                normal = normal / length;
        }

        /// <summary>
        /// 在给定网格表面上铺出一套点集，结果是世界空间里的。
        /// </summary>
        /// <param name="positions">顶点位置（在网格自己的空间里）。</param>
        /// <param name="normals">逐顶点法线；给 null 就用每个三角形自己的面法线。</param>
        /// <param name="triangles">三角索引，每个三角形三个。</param>
        /// <param name="toWorld">把网格空间的点送进场景的那个变换。</param>
        /// <param name="count">要产出多少个点。</param>
        /// <param name="seed">这一轮的种子。</param>
        /// <param name="nodeId">节点下标，让同一张图里的两个网格采样器给出不同的点。</param>
        /// <param name="density">写到每个点上的密度。</param>
        /// <param name="error">结果为空时，说明为什么产不出东西。</param>
        public static PCGData Generate(
            Vector3[] positions, Vector3[] normals, uint[] triangles, Transform toWorld,
            int count, int seed, int nodeId, float density, out string error)
        {
            error = null;

            if (count <= 0)
            {
                error = "A mesh sampler with a point count of zero produces nothing.";
                return PCGData.Empty;
            }

            var cumulative = BuildCumulativeAreas(positions, triangles, out float totalArea);
            if (cumulative == null)
            {
                error = "The mesh has no triangles with a usable area.";
                return PCGData.Empty;
            }

            var points = new PCGPoint[count];

            // 每个元素只依赖自己的下标——随机流按 (seed, 节点下标, 下标) 取键，
            // 而每个 worker 只写自己那一格——正因如此，同一个循环无论是
            // 就地跑还是摊到多个 worker 上跑，都能给出同样顺序的同样一批点。
            Execution.PCGParallel.For(count, i =>
            {
                // 每个点下标一条流，取键的方式与别的节点一致，所以
                // 重跑复现同一套布局，只重算一部分也和全量算的一致。
                var rng = new PCGDeterministicRng(seed, nodeId, i);
                float roll = rng.NextFloat() * totalArea;

                // 只要 BuildCumulativeAreas 收下了这张累加表，roll 就一定落在某一段里，
                // 于是这里没有"逐点失败"要报——从这儿报反而会跟共享的错误槽位抢。
                // 取值是钳住而不是轻信，所以哪怕这条路上本该没有越界，
                // 它也没法把整套点弄坏。
                int triangle = Math.Max(0, Math.Min(PickTriangle(cumulative, totalArea, roll), cumulative.Length - 1));

                int ia = (int)triangles[triangle * 3];
                int ib = (int)triangles[triangle * 3 + 1];
                int ic = (int)triangles[triangle * 3 + 2];

                var na = normals != null ? normals[ia] : Vector3.Zero;
                var nb = normals != null ? normals[ib] : Vector3.Zero;
                var nc = normals != null ? normals[ic] : Vector3.Zero;

                SampleTriangle(
                    positions[ia], positions[ib], positions[ic], rng.NextFloat(), rng.NextFloat(),
                    na, nb, nc, out var local, out var localNormal);

                var orientation = toWorld.Orientation;

                // 法线是方向，所以位置的平移分量不许渗进去。
                var worldNormal = Vector3.Transform(localNormal, orientation);

                if (normals == null || localNormal.Length <= 1e-6f)
                    worldNormal = FaceNormal(positions[ia], positions[ib], positions[ic], orientation);

                var scaled = new Vector3(
                    local.X * toWorld.Scale.X,
                    local.Y * toWorld.Scale.Y,
                    local.Z * toWorld.Scale.Z);

                var world = Vector3.Transform(scaled, orientation) + toWorld.Translation;

                // 点的坐标系带上表面朝向，下游的生成器或"贴法线"的消费者读到的
                // 就是网格自己的明暗，而不是世界的上轴。构造方式与地形采样器相同，
                // 两个采样器交给下一个节点的约定因此也相同。
                var up = worldNormal;
                var forward = Vector3.Normalize(Cross(up, Vector3.Right));
                if (forward.LengthSquared < 1e-6f)
                    forward = Vector3.Normalize(Cross(up, Vector3.Forward));

                var point = PCGPoint.Create(world, density, i);
                point.Rotation = Quaternion.LookRotation(
                    new Float3(forward.X, forward.Y, forward.Z), new Float3(up.X, up.Y, up.Z));

                // 尺度取平均：网格采样器自己没有逐点尺度，而 Actor 的尺度是
                // 唯一值得往下传的那个。
                point.Scale = new Float3(
                    (toWorld.Scale.X + toWorld.Scale.Y + toWorld.Scale.Z) / 3.0f);

                points[i] = point;
            });

            return new PCGData(points);
        }

        /// <summary>三角形的面法线，换成世界空间的朝向。</summary>
        private static Vector3 FaceNormal(in Vector3 a, in Vector3 b, in Vector3 c, Quaternion orientation)
        {
            var n = Cross(b - a, c - a);
            float length = n.Length;
            if (length <= 1e-8f)
                n = Vector3.Up;
            else
                n = n / length;

            return Vector3.Transform(n, orientation);
        }

        private static Vector3 Cross(in Vector3 a, in Vector3 b) =>
            new Vector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
    }
}

// PCG 插件 - 程序化几何核心
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.33。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 由插件建出来的三角网格：位置、三角下标、法线与 UV。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 刻意就是 Flax 自己的网格上传所收的那四个数组（<c>Mesh.UpdateMesh</c> 与
    /// <c>MeshAccessor</c>），于是一份产出的网格不需要一道"可能和测试各说一套"的
    /// 转换就能进到渲染器。尺寸不放在网格里：由调用方缩放它，这样"这个有多大"
    /// 只有一个真值来源，而不是一份按 2 单位建的网格配一个写着 3 的点。
    /// </para>
    /// <para>
    /// <b>绕序与法线刻意保持一致。</b>渲染器按顶点顺序判断三角形的哪一侧朝向相机，
    /// 并按法线缓冲给它着色；两者一旦不一致，背面剔除剔掉的恰好是
    /// 本该看得见的那些面。这里每一个建造器发出的三角形，其几何法线——
    /// <c>cross(b - a, c - a)</c>——都背离实体朝外，并把同一个方向存到顶点上。
    /// 测试把它当成成品网格的性质来查，而不是信任建造器本身。
    /// </para>
    /// </remarks>
    public sealed class PCGMesh
    {
        /// <summary>顶点位置。</summary>
        public Vector3[] Vertices;

        /// <summary>三角下标，每个三角形三个，绕序使每个面的法线朝外。</summary>
        public int[] Triangles;

        /// <summary>每个顶点一条法线，与绕序一致。</summary>
        public Vector3[] Normals;

        /// <summary>每个顶点一个 UV。</summary>
        public Vector2[] UVs;

        /// <summary>顶点数。</summary>
        public int VertexCount => Vertices?.Length ?? 0;

        /// <summary>三角形数。</summary>
        public int TriangleCount => (Triangles?.Length ?? 0) / 3;

        /// <summary>一份什么都没有的空网格。</summary>
        public static PCGMesh Empty => new PCGMesh
        {
            Vertices = Array.Empty<Vector3>(),
            Triangles = Array.Empty<int>(),
            Normals = Array.Empty<Vector3>(),
            UVs = Array.Empty<Vector2>(),
        };

        /// <inheritdoc />
        public override string ToString() => $"{VertexCount} vertex/vertices, {TriangleCount} triangle(s)";
    }

    /// <summary>
    /// 散点层或结构化层需要的那些网格建造器，保持成一个纯文件，好让这份算术
    /// 不需要场景也能验。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是程序化几何里不需要编辑器的另一半：给它一个尺寸和一个种类，它返回
    /// 三角形。这里不读也不写任何网格资产——那是宿主的工作——而把它挡在外面，正是
    /// 下面每一条性质（封闭体积、外法线、精确体积与面积）都能离屏断言的原因。
    /// </para>
    /// <para>
    /// 每个建造器返回的都以原点为中心，所以摆放它是调用方的变换，而建造器
    /// 永远不需要知道那个点在哪儿。
    /// </para>
    /// </remarks>
    public static class PCGMeshBuilder
    {
        /// <summary>旋转体一周最多的分段数，也是楼梯最多的步数。</summary>
        public const int MaxSegments = 256;

        /// <summary>楼梯最多可以有多少级踏步。</summary>
        public const int MaxSteps = 512;

        /// <summary>
        /// 一个六个面、硬棱的盒子。
        /// </summary>
        /// <remarks>
        /// 24 个顶点而不是 8 个：一个被三个面共用的角只能带一条法线，所以
        /// 8 个顶点会把盒子平滑成一个着色起来像球的东西。这份重复是
        /// 这个建造器的目的，不是疏漏。
        /// </remarks>
        public static PCGMesh Box(Vector3 size)
        {
            var half = Abs(size) * 0.5f;

            var mesh = New(24, 36);

            // 每个面由它的向外法线加上两条面内轴构成，两条轴的叉积就是那条法线，于是
            // 沿这两条轴以 (0,1,2) (0,2,3) 绕序的一个四边形自然就朝对的方向，不需要逐面特例。
            int vertex = 0;
            int triangle = 0;

            vertex = AddQuad(mesh, vertex, triangle, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), half, half.Z);
            triangle += 6;
            vertex = AddQuad(mesh, vertex, triangle, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), half, half.Z);
            triangle += 6;
            vertex = AddQuad(mesh, vertex, triangle, new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), half, half.X);
            triangle += 6;
            vertex = AddQuad(mesh, vertex, triangle, new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), half, half.X);
            triangle += 6;
            vertex = AddQuad(mesh, vertex, triangle, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), half, half.Y);
            triangle += 6;
            AddQuad(mesh, vertex, triangle, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), half, half.Y);

            return mesh;
        }

        /// <summary>
        /// 楔形：矩形底加一条沿 X 走的屋脊——"带斜屋顶的房子"那一档。
        /// </summary>
        /// <remarks>
        /// 尺寸语义与盒子一致（全尺寸，不是半径）：X 是开间、Y 是脊高、Z 是进深，都取绝对值；
        /// 底面在 <c>y = -Y/2</c>，屋脊在 <c>y = +Y/2</c> 并在 Z 上居中。
        /// 有屋脊就意味着有**斜**面，而 <see cref="Box"/> 的 <c>AddQuad</c> 只会摆轴对齐的面；
        /// 所以这里另走一条路：每个面给一个解析算出来的外法线，由 <see cref="AddFace"/> 用叉积
        /// 验一遍绕序、反了就翻环——于是"绕序与法线一致"这个本文件的核心不变量不依赖我把顶点顺序记对。
        /// 顶点按面拆开（18 个而非共享 6 个），理由与盒子相同：硬边上的一个角只能带一个法线。
        /// </remarks>
        public static PCGMesh Wedge(Vector3 size)
        {
            var half = Abs(size) * 0.5f;

            var mesh = New(18, 24);

            var a = new Vector3(-half.X, -half.Y, -half.Z);
            var b = new Vector3(half.X, -half.Y, -half.Z);
            var c = new Vector3(half.X, -half.Y, half.Z);
            var d = new Vector3(-half.X, -half.Y, half.Z);
            var r1 = new Vector3(-half.X, half.Y, 0.0f);
            var r2 = new Vector3(half.X, half.Y, 0.0f);

            int vertex = 0;
            int triangle = 0;

            // 底面与两端山墙：外法线就是坐标轴。
            vertex = AddFace(mesh, vertex, ref triangle, new[] { a, b, c, d }, new Vector3(0.0f, -1.0f, 0.0f));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { d, a, r1 }, new Vector3(-1.0f, 0.0f, 0.0f));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { b, c, r2 }, new Vector3(1.0f, 0.0f, 0.0f));

            // 两块斜坡：脊线方向 × 下坡方向 = 外向法线，即 (0, 进深之半, ∓脊高之半) 归一化。
            var front = Normalize(new Vector3(0.0f, half.Z, -half.Y));
            var back = Normalize(new Vector3(0.0f, half.Z, half.Y));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { a, r1, r2, b }, front);
            AddFace(mesh, vertex, ref triangle, new[] { c, r2, r1, d }, back);

            return mesh;
        }

        /// <summary>
        /// 按外法线放下一个面（三角形或四边形）：几何法线与给定外法线反着就翻环。
        /// </summary>
        private static int AddFace(PCGMesh mesh, int vertex, ref int triangle, Vector3[] ring, Vector3 outward)
        {
            var geometric = Vector3.Cross(ring[1] - ring[0], ring[2] - ring[0]);

            var order = ring;

            if (Vector3.Dot(geometric, outward) < 0.0f)
            {
                // 保持第一个顶点、其余倒序：还是同一个多边形，只是绕向反过来。
                order = new Vector3[ring.Length];
                order[0] = ring[0];

                for (int i = 1; i < ring.Length; i++)
                    order[i] = ring[ring.Length - i];
            }

            for (int i = 0; i < order.Length; i++)
            {
                mesh.Vertices[vertex + i] = order[i];
                mesh.Normals[vertex + i] = outward;
            }

            if (order.Length == 4)
            {
                mesh.UVs[vertex + 0] = new Vector2(0.0f, 1.0f);
                mesh.UVs[vertex + 1] = new Vector2(1.0f, 1.0f);
                mesh.UVs[vertex + 2] = new Vector2(1.0f, 0.0f);
                mesh.UVs[vertex + 3] = new Vector2(0.0f, 0.0f);
            }
            else
            {
                mesh.UVs[vertex + 0] = new Vector2(0.0f, 0.0f);
                mesh.UVs[vertex + 1] = new Vector2(0.5f, 1.0f);
                mesh.UVs[vertex + 2] = new Vector2(1.0f, 0.0f);
            }

            // 扇形拆面：三角形只有 1 片，四边形 2 片。绕序已经由上面保证。
            for (int i = 1; i + 1 < order.Length; i++)
            {
                mesh.Triangles[triangle + 0] = vertex;
                mesh.Triangles[triangle + 1] = vertex + i;
                mesh.Triangles[triangle + 2] = vertex + i + 1;
                triangle += 3;
            }

            return vertex + order.Length;
        }

        /// <summary>
        /// 房屋形：矩形墙 + 沿 X 的屋脊，合成**一个封闭实体**（屋顶直接坐在檐口上，没有内部面）。
        /// </summary>
        /// <remarks>
        /// 尺寸口径与盒子一致（全尺寸）：X 开间、Y **总高**、Z 进深。屋顶占顶部三分之一、墙占下面三分之二——
        /// 这个比例是定死的，为的是描述里只需写一个熟悉的高度，而不是"墙高 + 脊高"两个数；
        /// 代价是屋坡度随进深变（进深越大屋顶越平），换来的是 `geometry-size 12 8 10` 就是一栋房。
        /// 有向体积因此是解析的：墙 <c>X·Z·(2Y/3)</c> 加屋顶楔形 <c>X·Z·(Y/3)/2</c> = <c>(5/6)·X·Y·Z</c>。
        /// 七个面（底 + 两堵墙 + 两块坡 + 两端山尖五边形）各给一个解析外法线，绕序由
        /// <see cref="AddFace"/> 用叉积验、反了就翻；顶点按面拆开，与盒子/楔形同一个理由。
        /// </remarks>
        public static PCGMesh House(Vector3 size)
        {
            var half = Abs(size) * 0.5f;

            // 14 个真实角按面拆开是 30 个顶点（5 个四边形 + 2 个五边形），16 片三角形 = 48 个下标。
            var mesh = New(30, 48);

            float hx = half.X;
            float hy = half.Y;
            float hz = half.Z;
            float eave = hy / 3.0f;                       // 檐口高度：总高的下三分之二之上
            Vector3 ridgeSlope = Normalize(new Vector3(0.0f, hy - eave, hz));

            var a = new Vector3(-hx, -hy, -hz);
            var b = new Vector3(hx, -hy, -hz);
            var c = new Vector3(hx, -hy, hz);
            var d = new Vector3(-hx, -hy, hz);
            var e = new Vector3(-hx, eave, -hz);
            var f = new Vector3(hx, eave, -hz);
            var g = new Vector3(hx, eave, hz);
            var h = new Vector3(-hx, eave, hz);
            var r1 = new Vector3(-hx, hy, 0.0f);
            var r2 = new Vector3(hx, hy, 0.0f);

            int vertex = 0;
            int triangle = 0;

            vertex = AddFace(mesh, vertex, ref triangle, new[] { a, b, c, d }, new Vector3(0.0f, -1.0f, 0.0f));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { a, b, f, e }, new Vector3(0.0f, 0.0f, -1.0f));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { c, d, h, g }, new Vector3(0.0f, 0.0f, 1.0f));

            // 两端山尖：五边形，从第一个顶点扇形拆开（这个五边形是凸的，所以扇形安全）。
            vertex = AddFace(mesh, vertex, ref triangle, new[] { d, h, r1, e, a }, new Vector3(-1.0f, 0.0f, 0.0f));
            vertex = AddFace(mesh, vertex, ref triangle, new[] { b, f, r2, g, c }, new Vector3(1.0f, 0.0f, 0.0f));

            // 两块坡面：外法线 = 脊线方向 × 下坡方向，两面对称、只是 Z 分量反号。
            vertex = AddFace(mesh, vertex, ref triangle, new[] { e, f, r2, r1 },
                             Normalize(new Vector3(0.0f, ridgeSlope.Z, -ridgeSlope.Y)));
            AddFace(mesh, vertex, ref triangle, new[] { h, r1, r2, g },
                    Normalize(new Vector3(0.0f, ridgeSlope.Z, ridgeSlope.Y)));

            return mesh;
        }

        /// <summary>
        /// 树形：一段圆柱树干 + 一个圆锥树冠，合成**一个封闭实体**（树冠直接坐在树干顶上，没有内部面）。
        /// </summary>
        /// <remarks>
        /// 尺寸口径与圆柱一致：X/Z 里较小的是**树冠直径**（大的一端会被削到直径上，免得戳出自己那格），
        /// Y 是**总高**（含树冠）；树干占底部三分之一、半径取树冠的四分之一。
        /// 之所以要这一档：森林此前只能长成"一串浮在半空的锥"——`cylinder` 是干、`wedge`/`cone` 是冠，
        /// 但两样分开发就必然带一个内部相接面（配对四次的边），而目标要的是"树"。
        /// 实体由三组面拼成：底面扇形（n-2 片）、树干侧面（n 个四边形）、树冠（n 个三角形）
        /// ⇒ 顶点 <c>10n-6</c>、三角形 <c>4n-2</c>；树冠的锥面斜法线只按**符号**给（<see cref="AddFace"/>
        /// 会用叉积验绕序、反了就翻环），所以真正的判据来自测试里那条与解析体积的对账。
        /// </remarks>
        public static PCGMesh Tree(Vector3 size, int segments)
        {
            int sides = System.Math.Max(3, System.Math.Min(MaxSegments, segments));

            float canopyRadius = System.Math.Max(0.001f, System.Math.Min(System.Math.Abs(size.X), System.Math.Abs(size.Z)) * 0.5f);
            float total = System.Math.Abs(size.Y);
            float trunkRadius = canopyRadius * 0.25f;
            float bottom = -total * 0.5f;
            float eave = bottom + total / 3.0f;          // 树冠底沿：总高的下三分之一之上
            float coneHeight = total - total / 3.0f;

            // 底面 n-2 片 + 树干 n×2 + 树冠 n = 4n-2 片三角形，每片三个顶点但四边形是 4 个角点，
            // 于是按面拆开后顶点是 3(n-2) + 4n + 3n = 10n-6。
            var mesh = New(10 * sides - 6, (4 * sides - 2) * 3);

            var baseRing = new Vector3[sides];
            var topRing = new Vector3[sides];

            for (int i = 0; i < sides; i++)
            {
                float angle = (float)(i * System.Math.PI * 2.0 / sides);
                float cos = (float)System.Math.Cos(angle);
                float sin = (float)System.Math.Sin(angle);

                baseRing[i] = new Vector3(cos * trunkRadius, bottom, sin * trunkRadius);
                topRing[i] = new Vector3(cos * canopyRadius, eave, sin * canopyRadius);
            }

            var apex = new Vector3(0.0f, bottom + total, 0.0f);

            int vertex = 0;
            int triangle = 0;

            // 底面：扇形拆开，外法线朝下。
            for (int i = 1; i + 1 < sides; i++)
                vertex = AddFace(mesh, vertex, ref triangle,
                    new[] { baseRing[0], baseRing[i], baseRing[i + 1] }, new Vector3(0.0f, -1.0f, 0.0f));

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;

                // 树干侧面：竖直的四边形，外法线取两角点中点的径向。
                var radial = new Vector3(
                    baseRing[i].X + baseRing[next].X,
                    0.0f,
                    baseRing[i].Z + baseRing[next].Z);

                vertex = AddFace(mesh, vertex, ref triangle,
                    new[] { baseRing[i], baseRing[next], topRing[next], topRing[i] }, Normalize(radial));

                // 树冠：从树干顶沿（其实是树冠底沿）到顶点的一块三角面。
                // 斜面的外向法线 = 径向 × 锥高 + 上 × 底半径（只要符号对，绕序由 AddFace 保证）。
                var slant = Normalize(radial * coneHeight + new Vector3(0.0f, canopyRadius * 2.0f, 0.0f));

                vertex = AddFace(mesh, vertex, ref triangle,
                    new[] { topRing[i], topRing[next], apex }, slant);
            }

            return mesh;
        }

        /// <summary>XZ 平面上朝上的一片规则四边形网格。</summary>
        /// <param name="size">X 与 Z 的完整范围。</param>
        /// <param name="subdivisions">每轴的格子数；至少 1。</param>
        public static PCGMesh Grid(Vector2 size, int subdivisions)
        {
            int cells = Math.Max(1, Math.Min(MaxSegments, subdivisions));
            int side = cells + 1;

            var mesh = New(side * side, cells * cells * 6);

            float width = Math.Abs(size.X);
            float depth = Math.Abs(size.Y);

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    float u = (float)x / cells;
                    float v = (float)z / cells;

                    mesh.Vertices[i] = new Vector3((u - 0.5f) * width, 0.0f, (v - 0.5f) * depth);
                    mesh.Normals[i] = new Vector3(0, 1, 0);
                    mesh.UVs[i] = new Vector2(u, v);
                }
            }

            int write = 0;

            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int a = z * side + x;
                    int b = a + 1;
                    int c = a + side;
                    int d = c + 1;

                    // 绕序使 cross(b - a, c - a) 为 +Y：那是地面网格被看到的那一面。
                    mesh.Triangles[write++] = a;
                    mesh.Triangles[write++] = c;
                    mesh.Triangles[write++] = b;
                    mesh.Triangles[write++] = b;
                    mesh.Triangles[write++] = c;
                    mesh.Triangles[write++] = d;
                }
            }

            return mesh;
        }

        /// <summary>立在 XZ 平面上、以原点为中心的圆柱。</summary>
        /// <param name="radius">半径；负值会被取正。</param>
        /// <param name="height">沿 Y 的完整高度。</param>
        /// <param name="segments">周身的边数；至少 3。</param>
        /// <param name="capped">是否封闭顶面与底面。</param>
        public static PCGMesh Cylinder(float radius, float height, int segments, bool capped = true)
        {
            int sides = Math.Max(3, Math.Min(MaxSegments, segments));
            float r = Math.Abs(radius);
            float half = Math.Abs(height) * 0.5f;

            int ringVertices = (sides + 1) * 2;
            int extra = capped ? 2 : 0;

            var mesh = New(ringVertices + extra, sides * 6 + (capped ? sides * 6 : 0));

            for (int i = 0; i <= sides; i++)
            {
                float angle = Mathf.TwoPi * i / sides;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);

                int bottom = i;
                int top = sides + 1 + i;

                mesh.Vertices[bottom] = new Vector3(x * r, -half, z * r);
                mesh.Vertices[top] = new Vector3(x * r, half, z * r);

                // 墙是竖直的，所以它的法线没有 Y：把它留在 XZ 平面里，才让绕一圈的着色
                // 是连续的，而不是一节一节的。
                mesh.Normals[bottom] = new Vector3(x, 0, z);
                mesh.Normals[top] = new Vector3(x, 0, z);

                mesh.UVs[bottom] = new Vector2((float)i / sides, 1.0f);
                mesh.UVs[top] = new Vector2((float)i / sides, 0.0f);
            }

            int write = 0;

            for (int i = 0; i < sides; i++)
            {
                int b0 = i;
                int b1 = i + 1;
                int t0 = sides + 1 + i;
                int t1 = sides + 1 + i + 1;

                mesh.Triangles[write++] = b0;
                mesh.Triangles[write++] = t1;
                mesh.Triangles[write++] = b1;
                mesh.Triangles[write++] = b0;
                mesh.Triangles[write++] = t0;
                mesh.Triangles[write++] = t1;
            }

            if (!capped)
                return mesh;

            int bottomCentre = ringVertices;
            int topCentre = ringVertices + 1;

            mesh.Vertices[bottomCentre] = new Vector3(0, -half, 0);
            mesh.Vertices[topCentre] = new Vector3(0, half, 0);
            mesh.Normals[bottomCentre] = new Vector3(0, -1, 0);
            mesh.Normals[topCentre] = new Vector3(0, 1, 0);
            mesh.UVs[bottomCentre] = new Vector2(0.5f, 0.5f);
            mesh.UVs[topCentre] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < sides; i++)
            {
                // 绕序使每个封盖朝外，而对底面来说这意味着这个扇形的绕行方向
                // 与顶面相反。两者曾一度写成同一种绕法，于是体积算出来正好是 pi r^2 h / 3——
                // 那是两个封盖朝内的签名：侧面是对的，而封盖在做减法。
                mesh.Triangles[write++] = bottomCentre;
                mesh.Triangles[write++] = i;
                mesh.Triangles[write++] = i + 1;

                mesh.Triangles[write++] = topCentre;
                mesh.Triangles[write++] = sides + 1 + i + 1;
                mesh.Triangles[write++] = sides + 1 + i;
            }

            return mesh;
        }

        /// <summary>
        /// 一段 <paramref name="steps"/> 级踏步的楼梯，沿 +Y 上升、沿 +Z 前进。
        /// </summary>
        /// <remarks>
        /// 它是沿一条剖面绕一圈的结果，不是一个图元；盒子没法代替它的原因是：
        /// 踏面与踢面朝向不同，所以一个盒子的平面着色会有一半表面是错的。
        /// 它也是算术最容易出微妙错误的那个形状——步高上差一个仍然看着像楼梯——
        /// 所以测试量的是它的体积，而不是三角形个数。
        /// <para>
        /// 建成一层层变高的板叠，而不是一次挤出那条剖面，于是两块板相接处留下内部面。
        /// 这是楼梯接受的取舍：替代方案是对一条非凸剖面做耳切三角化，而那些内部面
        /// 被每一个不透明材质挡住。所以测试不主张楼梯是水密的；
        /// 它们主张的是尺寸对、体积对，
        /// 且没有任何一个三角形与自己的法线绕向相反。
        /// </para>
        /// </remarks>
        /// <param name="size">整体范围：X 是宽，Y 是总升高，Z 是总进深。</param>
        /// <param name="steps">踏面数；至少 1。</param>
        public static PCGMesh Stairs(Vector3 size, int steps)
        {
            int count = Math.Max(1, Math.Min(MaxSteps, steps));

            float width = Math.Abs(size.X);
            float rise = Math.Abs(size.Y);
            float run = Math.Abs(size.Z);

            float stepRise = rise / count;
            float stepRun = run / count;

            var parts = new List<PCGMesh>(count);

            for (int i = 0; i < count; i++)
            {
                float height = stepRise * (i + 1);

                // 每级踏步一块板，越往上越高：叠起来时，露出的正面就是踢面，
                // 每块板的顶面就是它的踏面。每块摆放得让这一叠恰好铺满请求的体积。
                var slab = Box(new Vector3(width, height, stepRun));

                parts.Add(Offset(slab, new Vector3(
                    0.0f,
                    -rise * 0.5f + height * 0.5f,
                    -run * 0.5f + stepRun * (i + 0.5f))));
            }

            return Merge(parts);
        }

        /// <summary>按一个向量平移网格，法线不动。</summary>
        public static PCGMesh Offset(in PCGMesh mesh, Vector3 delta)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return mesh ?? PCGMesh.Empty;

            var result = CopyOf(mesh);

            for (int i = 0; i < result.Vertices.Length; i++)
                result.Vertices[i] += delta;

            return result;
        }

        /// <summary>
        /// 摆放一个网格：位置、旋转与缩放，并从结果重建法线。
        /// </summary>
        /// <remarks>
        /// 法线是重算的而不是旋转的，因为非均匀缩放需要逆转置，
        /// 而只旋转它们会让方向出错——"道具被压扁之后着色不对"这种事
        /// 是一张图能看出来、而计数永远看不出来的那一类。
        /// </remarks>
        public static PCGMesh Transform(in PCGMesh mesh, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return PCGMesh.Empty;

            var result = CopyOf(mesh);

            for (int i = 0; i < result.Vertices.Length; i++)
            {
                var local = new Vector3(
                    result.Vertices[i].X * scale.X,
                    result.Vertices[i].Y * scale.Y,
                    result.Vertices[i].Z * scale.Z);

                result.Vertices[i] = position + Vector3.Transform(local, rotation);
            }

            RecalculateNormals(result);
            return result;
        }

        /// <summary>
        /// 沿一条折线扫出一个圆管——洞穴的隧道壁就是它的内壁。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 环的朝向用**平行输运**：每一环的参考向量由上一环沿路径最小旋转而来，而不是每环各自取"世界向上叉方向"
        /// ——后者在路径接近竖直时会突然翻转，管子会拧一下，而那种缺陷在点数与长度上都看不出来。法线一律沿半径
        /// 朝外（<paramref name="inward"/> 为真时朝内并同时翻转绕序），因为**法线方向与绕序必须一致**是这个几何核心
        /// 一直守住的性质：不一致时背面剔除会恰好剔掉该看见的那一面。
        /// </para>
        /// <para>
        /// <paramref name="inward"/> 是给洞穴用的：站在隧道里看到的应该是洞壁的内侧，所以法线朝内。于是有向体积
        /// 会变成负值——这正好是"翻转确实生效"的可断言证据。
        /// </para>
        /// </remarks>
        /// <param name="path">折线，至少两个点。</param>
        /// <param name="radius">管半径。</param>
        /// <param name="sides">一圈多少边，至少三。</param>
        /// <param name="capped">两端是否封口。</param>
        /// <param name="inward">法线朝内（洞壁）还是朝外（管子）。</param>
        public static PCGMesh Tube(IReadOnlyList<Vector3> path, float radius, int sides, bool capped, bool inward)
        {
            if (path == null || path.Count < 2)
                return PCGMesh.Empty;

            int edges = Math.Max(3, Math.Min(MaxSegments, sides));
            float r = Math.Abs(radius);
            int rings = path.Count;

            // 每环多一个顶点：环的接缝处要重复一份，否则 UV 会在接缝上绕回去。
            int ringVertices = (edges + 1) * rings;
            int capVertices = capped ? 2 : 0;
            int ringTriangles = (rings - 1) * edges * 6;
            int capTriangles = capped ? edges * 6 : 0;

            var mesh = New(ringVertices + capVertices, ringTriangles + capTriangles);

            // 第一环的参考向量：取一个与切线不平行的方向，再由它算出第一个正交基。
            var tangent = Normalize(path[1] - path[0]);
            var reference = Math.Abs(Vector3.Dot(tangent, Vector3.Up)) > 0.9f ? Vector3.Right : Vector3.Up;
            var normal = Normalize(Vector3.Cross(reference, tangent));
            var binormal = Vector3.Cross(tangent, normal);

            for (int ring = 0; ring < rings; ring++)
            {
                if (ring > 0)
                {
                    // 平行输运：把上一环的基绕"上一段到这一段的轴"转过来，避免扭转。
                    var previousTangent = tangent;
                    tangent = ring == rings - 1
                        ? Normalize(path[ring] - path[ring - 1])
                        : Normalize(path[ring + 1] - path[ring - 1]);

                    var axis = Vector3.Cross(previousTangent, tangent);
                    float turn = Mathf.Clamp(Vector3.Dot(previousTangent, tangent), -1.0f, 1.0f);

                    if (turn < 1.0f - 1e-6f)
                    {
                        Vector3 rotationAxis;

                        if (axis.Length > 1e-4f)
                        {
                            rotationAxis = axis / axis.Length;
                        }
                        else
                        {
                            // 折返（夹角接近 180 度）时叉积的模长趋近零，方向是数值噪声。此时必须另取一根与切向
                            // 垂直的定轴，否则基准框会朝随机方向翻过去，管壁就有一小段的法线反了。
                            var fallback = Math.Abs(Vector3.Dot(previousTangent, Vector3.Up)) > 0.9f
                                ? Vector3.Right
                                : Vector3.Up;
                            var perpendicular = Vector3.Cross(fallback, previousTangent);
                            rotationAxis = perpendicular.Length > 1e-6f
                                ? perpendicular / perpendicular.Length
                                : Vector3.Right;
                        }

                        float angle = (float)Math.Acos(turn);
                        normal = Normalize(Vector3.Transform(normal, Quaternion.RotationAxis(rotationAxis, angle)));
                    }

                    binormal = Vector3.Cross(tangent, normal);
                }

                for (int edge = 0; edge <= edges; edge++)
                {
                    float angle = Mathf.TwoPi * edge / edges;
                    var offset = normal * (Mathf.Cos(angle) * r) + binormal * (Mathf.Sin(angle) * r);
                    var radial = Normalize(offset);
                    int vertex = ring * (edges + 1) + edge;

                    mesh.Vertices[vertex] = path[ring] + offset;
                    mesh.Normals[vertex] = inward ? -radial : radial;
                    mesh.UVs[vertex] = new Vector2((float)edge / edges, (float)ring / (rings - 1));
                }
            }

            int write = 0;

            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int edge = 0; edge < edges; edge++)
                {
                    int a = ring * (edges + 1) + edge;
                    int b = a + 1;
                    int c = a + edges + 1;
                    int d = c + 1;

                    // 绕序随法线一起翻：朝内时把两个三角形都反过来。
                    if (inward)
                    {
                        mesh.Triangles[write++] = a;
                        mesh.Triangles[write++] = c;
                        mesh.Triangles[write++] = b;
                        mesh.Triangles[write++] = b;
                        mesh.Triangles[write++] = c;
                        mesh.Triangles[write++] = d;
                    }
                    else
                    {
                        mesh.Triangles[write++] = a;
                        mesh.Triangles[write++] = b;
                        mesh.Triangles[write++] = c;
                        mesh.Triangles[write++] = b;
                        mesh.Triangles[write++] = d;
                        mesh.Triangles[write++] = c;
                    }
                }
            }

            if (!capped)
                return mesh;

            int startCentre = ringVertices;
            int endCentre = ringVertices + 1;
            var startNormal = Normalize(path[1] - path[0]);
            var endNormal = Normalize(path[path.Count - 1] - path[path.Count - 2]);

            mesh.Vertices[startCentre] = path[0];
            mesh.Vertices[endCentre] = path[path.Count - 1];
            mesh.Normals[startCentre] = inward ? startNormal : -startNormal;
            mesh.Normals[endCentre] = inward ? -endNormal : endNormal;
            mesh.UVs[startCentre] = new Vector2(0.5f, 0.5f);
            mesh.UVs[endCentre] = new Vector2(0.5f, 0.5f);

            for (int edge = 0; edge < edges; edge++)
            {
                int lastRing = (rings - 1) * (edges + 1);

                if (inward)
                {
                    mesh.Triangles[write++] = startCentre;
                    mesh.Triangles[write++] = edge;
                    mesh.Triangles[write++] = edge + 1;
                    mesh.Triangles[write++] = endCentre;
                    mesh.Triangles[write++] = lastRing + edge + 1;
                    mesh.Triangles[write++] = lastRing + edge;
                }
                else
                {
                    mesh.Triangles[write++] = startCentre;
                    mesh.Triangles[write++] = edge + 1;
                    mesh.Triangles[write++] = edge;
                    mesh.Triangles[write++] = endCentre;
                    mesh.Triangles[write++] = lastRing + edge;
                    mesh.Triangles[write++] = lastRing + edge + 1;
                }
            }

            return mesh;
        }

        /// <summary>把向量归一化；零向量回退到世界向上，避免出现 NaN。</summary>
        private static Vector3 Normalize(Vector3 value)
        {
            float length = value.Length;

            return length > 1e-6f ? value / length : Vector3.Up;
        }

        /// <summary>把多个网格串成一个，并重新基化下标。</summary>
        public static PCGMesh Merge(IReadOnlyList<PCGMesh> meshes)
        {
            if (meshes == null || meshes.Count == 0)
                return PCGMesh.Empty;

            int vertices = 0;
            int indices = 0;

            for (int i = 0; i < meshes.Count; i++)
            {
                var part = meshes[i];

                if (part == null)
                    continue;

                vertices += part.VertexCount;
                indices += part.Triangles?.Length ?? 0;
            }

            if (vertices == 0 || indices == 0)
                return PCGMesh.Empty;

            var result = New(vertices, indices);

            int vertexBase = 0;
            int indexBase = 0;

            for (int i = 0; i < meshes.Count; i++)
            {
                var part = meshes[i];

                if (part == null || part.VertexCount == 0)
                    continue;

                Array.Copy(part.Vertices, 0, result.Vertices, vertexBase, part.Vertices.Length);
                Array.Copy(part.Normals, 0, result.Normals, vertexBase, part.Normals.Length);
                Array.Copy(part.UVs, 0, result.UVs, vertexBase, part.UVs.Length);

                for (int t = 0; t < part.Triangles.Length; t++)
                    result.Triangles[indexBase + t] = part.Triangles[t] + vertexBase;

                vertexBase += part.VertexCount;
                indexBase += part.Triangles.Length;
            }

            return result;
        }

        /// <summary>
        /// 一块网格的**去重键**：形状相同就给同一个字符串。
        /// </summary>
        /// <remarks>
        /// 用途有两处：①"同形状的网格只建一次虚拟 Model、只占一个缓存槽"（`SpawnGeometry` 的按形状共享就是这个思路，
        /// 只是它原来的键更弱）；②合并还是实例化的**判据**（同一形状摆很多位置 vs 形状各异）。
        /// 顶点坐标按 **1e-3**（毫米）量化后再算：两条不同代码路径算出的"同一个形状"在最低位上会差一点，
        /// 不量化就会把它们当成两种形状，于是缓存永不命中、实例化永远退化成逐块。三角面的**数量与绕序**
        /// 都参与：顶点集合相同而绕序相反的两块是两块不同的几何（背面剔除会差一整面）。
        /// </remarks>
        public static string ShapeKey(in PCGMesh mesh)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return "empty";

            const float quantum = 1000.0f;
            ulong hash = 1469598103934665603UL;   // FNV-1a 64 位偏移基准

            void Mix(long value)
            {
                unchecked
                {
                    hash ^= (ulong)value;
                    hash *= 1099511628211UL;
                }
            }

            Mix(mesh.VertexCount);
            Mix(mesh.TriangleCount);

            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                var vertex = mesh.Vertices[i];

                Mix((long)MathF.Round(vertex.X * quantum));
                Mix((long)MathF.Round(vertex.Y * quantum));
                Mix((long)MathF.Round(vertex.Z * quantum));
            }

            for (int i = 0; mesh.Triangles != null && i < mesh.Triangles.Length; i++)
                Mix(mesh.Triangles[i]);

            return $"{mesh.VertexCount}v{mesh.TriangleCount}t{hash:x16}";
        }

        /// <summary>
        /// 把若干**自带摆放**的几何合并成一锅三角面：每块先按自己的 Placement 烘进顶点，再拼起来。
        /// </summary>
        /// <remarks>
        /// 为一个非常实际的原因存在：`Model.LODs` 与 `ModelLOD.Meshes` 都是**只读数组**（引擎事实，
        /// 用 `Tools/FlaxApiDump` 实测），所以一个虚拟 Model 只有一个可写网格槽——"一个 actor 画一整片几何"
        /// 只能靠先合并成一锅三角面。**摆放要烘进顶点**而不是留给 actor：一个 actor 只有一个 Transform，
        /// 表达不了"每块各有各的位置"。法线一律重算（与 <see cref="Transform"/> 同一个理由）：
        /// 非等比缩放要逆置转置，转一下法线就错了。
        /// </remarks>
        public static PCGMesh MergePlaced(IReadOnlyList<PCGGeometryItem> items)
        {
            if (items == null || items.Count == 0)
                return PCGMesh.Empty;

            var placed = new List<PCGMesh>(items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (!item.IsUsable)
                    continue;

                var placement = item.Placement;
                placed.Add(Transform(item.Mesh, placement.Translation, placement.Orientation, placement.Scale));
            }

            return Merge(placed);
        }

        /// <summary>网格占据的包围盒，好让一次生成照它定自己的尺寸。</summary>
        public static BoundingBox Bounds(in PCGMesh mesh)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            var minimum = mesh.Vertices[0];
            var maximum = mesh.Vertices[0];

            for (int i = 1; i < mesh.Vertices.Length; i++)
            {
                var v = mesh.Vertices[i];

                minimum = new Vector3(
                    Math.Min(minimum.X, v.X), Math.Min(minimum.Y, v.Y), Math.Min(minimum.Z, v.Z));
                maximum = new Vector3(
                    Math.Max(maximum.X, v.X), Math.Max(maximum.Y, v.Y), Math.Max(maximum.Z, v.Z));
            }

            return new BoundingBox(minimum, maximum);
        }

        /// <summary>
        /// 由三角形重算每一条法线，并按面积加权。
        /// </summary>
        /// <remarks>
        /// 那些叉积是未归一化地累加的，于是每个三角形的贡献与它的面积成正比：
        /// 这是给平均值加权省事的做法，也是一片细长三角形没法
        /// 在它所属的那个面上投出更多票。
        /// </remarks>
        public static void RecalculateNormals(PCGMesh mesh)
        {
            if (mesh == null || mesh.VertexCount == 0 || mesh.Triangles == null)
                return;

            mesh.Normals ??= new Vector3[mesh.VertexCount];

            if (mesh.Normals.Length != mesh.VertexCount)
                mesh.Normals = new Vector3[mesh.VertexCount];

            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.Normals[i] = Vector3.Zero;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int a = mesh.Triangles[t];
                int b = mesh.Triangles[t + 1];
                int c = mesh.Triangles[t + 2];

                if (a < 0 || b < 0 || c < 0 ||
                    a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount)
                    continue;

                var normal = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);

                mesh.Normals[a] += normal;
                mesh.Normals[b] += normal;
                mesh.Normals[c] += normal;
            }

            for (int i = 0; i < mesh.Normals.Length; i++)
            {
                float length = mesh.Normals[i].Length;

                // 没有任何三角形用到的顶点没有方向可给；取 up 读起来是"不倾斜"，
                // 而不是一个卡住的零向量——而零法线在着色器那里本来就是这个下场。
                mesh.Normals[i] = length > 1e-8f ? mesh.Normals[i] / length : new Vector3(0, 1, 0);
            }
        }

        private static PCGMesh New(int vertices, int indices) => new PCGMesh
        {
            Vertices = new Vector3[vertices],
            Normals = new Vector3[vertices],
            UVs = new Vector2[vertices],
            Triangles = new int[indices],
        };

        private static PCGMesh CopyOf(in PCGMesh mesh) => new PCGMesh
        {
            Vertices = (Vector3[])mesh.Vertices.Clone(),
            Triangles = (int[])mesh.Triangles.Clone(),
            Normals = (Vector3[])mesh.Normals.Clone(),
            UVs = (Vector2[])mesh.UVs.Clone(),
        };

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Math.Abs(value.X), Math.Abs(value.Y), Math.Abs(value.Z));

        /// <summary>
        /// 往盒子的一个面写入一个四边形，并返回下一个空闲的顶点下标。
        /// </summary>
        /// <remarks>
        /// 两条面内轴按"其叉积就是向外法线"的顺序给出，这就是全部的技巧：
        /// 同样的两个三角形服务所有六个面，于是一个面不会
        /// 因为某个 switch 分支里的笔误而绕反。
        /// </remarks>
        /// <param name="mesh">正在填写的网格。</param>
        /// <param name="vertex">第一个空闲顶点下标。</param>
        /// <param name="triangle">第一个空闲的下标缓冲槽位。</param>
        /// <param name="normal">这个面的向外法线。</param>
        /// <param name="u">第一条面内轴，单位方向。</param>
        /// <param name="v">第二条面内轴，单位方向。</param>
        /// <param name="half">盒子的半范围。</param>
        /// <param name="alongNormal">沿法线自己那一轴的半范围。</param>
        /// <returns>下一个空闲的顶点下标。</returns>
        private static int AddQuad(
            PCGMesh mesh, int vertex, int triangle, Vector3 normal, Vector3 u, Vector3 v, Vector3 half,
            float alongNormal)
        {
            var centre = normal * alongNormal;
            var du = u * Extent(u, half);
            var dv = v * Extent(v, half);

            mesh.Vertices[vertex + 0] = centre - du - dv;
            mesh.Vertices[vertex + 1] = centre + du - dv;
            mesh.Vertices[vertex + 2] = centre + du + dv;
            mesh.Vertices[vertex + 3] = centre - du + dv;

            mesh.UVs[vertex + 0] = new Vector2(0, 1);
            mesh.UVs[vertex + 1] = new Vector2(1, 1);
            mesh.UVs[vertex + 2] = new Vector2(1, 0);
            mesh.UVs[vertex + 3] = new Vector2(0, 0);

            for (int i = 0; i < 4; i++)
                mesh.Normals[vertex + i] = normal;

            mesh.Triangles[triangle + 0] = vertex + 0;
            mesh.Triangles[triangle + 1] = vertex + 1;
            mesh.Triangles[triangle + 2] = vertex + 2;
            mesh.Triangles[triangle + 3] = vertex + 0;
            mesh.Triangles[triangle + 4] = vertex + 2;
            mesh.Triangles[triangle + 5] = vertex + 3;

            return vertex + 4;
        }

        /// <summary>某个单位方向所对应的那条轴的半范围。</summary>
        private static float Extent(Vector3 direction, Vector3 half) =>
            direction.X != 0.0f ? half.X : direction.Y != 0.0f ? half.Y : half.Z;
    }
}

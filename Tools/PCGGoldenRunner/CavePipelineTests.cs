// PCG 插件 - 洞穴输出的**下游消费**（Geometry 通道）可执行断言
//
// 洞穴把洞壁从 `Meshes` 改到 `Geometry` 独立几何之后（见 docs/PCG_PLUGIN_DESIGN.md §7.4①），
// 光断言"它出的是几何、摆放对"还不够：还得证明**下游真的认得**这条通道。这一组就是那句话的断言——
// 切一刀（`Cut`）与把几何采成点（`SampleGeometry`）都要在洞穴的输出上真的干活。
//
// 三条链各自有判别力：
//   ① Cut：切完仍然水密、体积**变小**、件数不变（不是"原样返回"）；
//   ② SampleGeometry：采出来的点落在**管子自己的世界包围盒**里；
//   ③ 对照：把摆放抹成单位变换后，同一批采样点会掉到原点附近——证明 ② 测的是摆放，不是"点恰好都在框里"。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class CavePipelineTests
    {
        internal static void Run(CheckDelegate check)
        {
            var box = new BoundingBox(new Vector3(-500, -400, -500), new Vector3(500, 400, 500));
            var context = new PCGExecutionContext { Seed = 20261008, GenerationBounds = box, SourceBounds = box };

            var tubes = MakeCave().Execute(context, new[] { (PCGData)null });

            // 前提：洞穴确实出了独立几何。没有这条，"下游消费不了"与"上游没有东西"会给出同一个红。
            bool hasTubes = tubes != null && tubes.Geometry != null && tubes.Geometry.Length > 0;

            check("前提：洞穴确实出了独立几何", hasTubes,
                tubes?.Geometry?.Length.ToString() ?? "null");

            if (!hasTubes)
                return;

            int tubeCount = tubes.Geometry.Length;
            var tubeBounds = TubesWorldBounds(tubes.Geometry, out bool pureTranslation);

            check("前提：洞壁的摆放是纯平移（下面按平移算世界包围盒才成立）", pureTranslation);

            // 前提：**切割之前**那几根管子本身是水密的。少了这条，下面报"开口"时分不清是切割的错
            // 还是管子的错（这一组一开始就差点被这一点误判）。
            bool inputClosed = true;
            string inputReport = null;

            for (int i = 0; i < tubes.Geometry.Length; i++)
            {
                int edges = EdgeReport(tubes.Geometry[i].Mesh).boundary;

                if (edges != 0)
                {
                    inputClosed = false;
                    inputReport ??= $"#{i} 切割前就有 {edges} 条破口边";
                }
            }

            check("前提：切割之前每根洞壁都是水密的（否则分不清是谁的错）", inputClosed, inputReport);

            // ---- ① Cut 认这条通道 ----
            // 刀口在**形状自己的坐标系**里（`Cut` 的契约：网格都建在自己原点，所以 y=0 就是"把这个形状切一半"）。
            // 不能拿世界坐标去想：洞壁已经被 PlaceTube 搬到了自己的局部原点，写 y=-100 会从管子外面切过去
            // （实测：6 块全被吃空、"总体积 1830714 → 0"——那不是切割错了，是刀没落在料上）。
            var cut = new PCGCutNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCutNode.Id,
                new object[] { Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), false },
                0));

            var cutResult = cut.Execute(context, new[] { tubes });

            check("Cut 认洞穴的几何且件数不变",
                cutResult.Geometry != null && cutResult.Geometry.Length == tubeCount,
                cutResult.Geometry?.Length.ToString() ?? "null");

            float volumeBefore = TotalVolume(tubes);
            float volumeAfter = TotalVolume(cutResult);

            check("Cut 真的削掉了东西，但没有把管子整根吃掉（总体积变小且仍为正）",
                volumeAfter > 0.0f && volumeAfter < volumeBefore * 0.999f,
                $"{volumeBefore:F1} → {volumeAfter:F1}");

            bool cutNonEmpty = true;
            int keptWoundBreaks = 0;
            int cutWoundBreaks = 0;
            int openRings = 0;
            int gapAtTenth = 0;
            string cutReport = null;
            string openReport = null;

            for (int i = 0; i < cutResult.Geometry.Length; i++)
            {
                var mesh = cutResult.Geometry[i].Mesh;

                if (mesh == null || mesh.TriangleCount == 0)
                {
                    cutNonEmpty = false;
                    cutReport ??= $"#{i} 被吃空（{cut.LastError ?? "无错误信息"}）";
                    continue;
                }

                int edges = EdgeReport(mesh).boundary;

                if (edges != 0)
                {
                    openRings++;

                    // 放大合并半径再数一遍：数字不随尺子变，就说明**不是**焊接容差的问题，是真的开着口。

                    // 三角面数没变 ⇒ 这一刀被拒了、原样保留（`PCGCutNode.CutOrKeep`），开口是**输入**带来的；
                    // 变了 ⇒ 这一刀真的下过，开口是切割产生的。分不清这两者就找不到该修谁。
                    int before = tubes.Geometry[i].Mesh?.TriangleCount ?? 0;

                    openReport ??= $"#{i} 开口 {edges} 条（合并半径放大到 0.1 后 {EdgeReport(mesh, 1e-1f).boundary} 条）；" +
                                   $"三角面 {before}→{mesh.TriangleCount}（{(before == mesh.TriangleCount ? "没变，这一刀被拒/原样保留" : "变了，这一刀真的下过")}）；" +
                                   $"节点报告：{cut.LastError ?? "无"}";
                }

                // 洞壁是**朝内**的：绕序与法线一致这条性质在切完之后也必须还在。
                // "被切过的"与"被拒后原样保留的"分开数：两者的结论不一样（见下面两条断言），混在一起就分不清。
                if (!WindingAgrees(mesh))
                {
                    int intact = tubes.Geometry[i].Mesh?.TriangleCount ?? 0;

                    if (intact == mesh.TriangleCount)
                        keptWoundBreaks++;
                    else
                        cutWoundBreaks++;

                    // 输入的有向体积就是"盖该往哪边"的判据来源（`PCGMeshCut` 里 `orientation` 那一段：
                    // <0 ⇒ 内向 ⇒ 盖跟着反过来）。把它一起打出来，才能判"是符号判错了"还是别的原因。
                    float inputVolume = SignedVolume(tubes.Geometry[i].Mesh);

                    cutReport ??= $"#{i} 绕序与法线不一致（三角面 {intact}→{mesh.TriangleCount}）{WindingBreakDetail(mesh)}" +
                                  $"｜输入有向体积 {inputVolume:F2}（<0 就该判内向、盖反过来）";
                }
            }

            check("Cut 之后每块洞壁都还在（没有整块被吃空）", cutNonEmpty, cutReport);
            check("原样保留（被拒）的那些洞壁，绕序与法线仍然一致", keptWoundBreaks == 0, cutReport);
            // 绕序本身（拓扑）用**与法线无关**的判据再数一遍：被切过的那几块，有向边必须两两相反。
            int orientBreaks = 0;
            string orientReport = null;

            for (int i = 0; i < cutResult.Geometry.Length; i++)
            {
                var mesh = cutResult.Geometry[i].Mesh;
                int intact = tubes.Geometry[i].Mesh?.TriangleCount ?? 0;

                if (mesh == null || mesh.TriangleCount == 0 || intact == mesh.TriangleCount)
                    continue;   // 被拒/原样保留的那些不算（它们的绕序本来就是原样的）

                if (!OrientationConsistent(mesh))
                {
                    orientBreaks++;
                    orientReport ??= $"#{i} 有向边未两两相反";
                }
            }

            // **绕序自洽（拓扑级）**：被切过的那几块，每条有向边都必须有恰好一条反向边与之配对。
            // 这一条曾经是红的（内向洞壁的盖子整片反着走）——修法是让盖的朝向跟**输入自己的约定**走
            // （有向体积为负 ⇒ 内向，盖反过来），见 `PCGMeshCut` 里 `orientation` 那段注解。
            // 它与上面那条"与法线均值不同向"不是一回事：后者是"硬边共享一个法线"的**表示**问题（仍未修），
            // 前者是**拓扑**问题（已修）。两条分开判，正是为了不让表示问题把拓扑问题盖住。
            check("被切过的洞壁，绕序自洽（有向边两两相反，与法线无关）",
                orientBreaks == 0, orientReport ?? "自洽");

            // 表示层的**已知限制**（不是绕序问题）：累加器按位置焊接、保留**先插进来的**法线，所以"洞壁"
            // 与"切口盖"共享的那圈棱上只带一个法线 ⇒ 被切过的那几块必然与"三个顶点法线的均值"不同向。
            // 实测最差那块面积 1.04、余弦 0.00（两者垂直）。要修就得在焊接时按"位置 + 法线"拆点，
            // 而那会动到判据①（按位置配对）的前提：属单独的一格。修好之后本行会红。
            // **2026-10-11 修掉了**：根因不是"硬边共享一个法线"那类**表示**问题，而是**扇形三角化假设了环是简单环**——
            // 弯曲洞壁横切时，环上的点在 1e-4 焊接里被合掉之后环会**自交**，扇形于是产出**方向相反**的三角形
            // （量到：8 片、面积 394、余弦 −0.98），而 `TryBuildCap` 的绕向判据用的是**整圈的有向面积**，
            // 那个量对非简单环看不出这件事。修法：在 `PCGMeshCut` 里**逐片验盖的朝向**（退化片跳过，
            // 余弦 ≤ −0.5 才判反），不一致就**整刀拒绝**——宁可这两块不动，也不交出盖反了的网格。
            // 于是这条从"钉着的缺陷"翻成**正面断言**：被切过的洞壁里不该再有盖反的。
            check("被切过的洞壁里没有**盖反**的（绕序与顶点法线一致）", cutWoundBreaks == 0,
                $"{cutWoundBreaks} 块被切过的洞壁与法线均值不同向；{cutReport ?? "无"}");
            check("Cut 之后每块洞壁仍然水密（切口的盖真的封上了）", openRings == 0,
                $"{openRings} 块开口；{openReport ?? "无"}");

            // ---- ①.5 最小复现（与洞穴无关的合成夹具） ----
            // 上面那条是从洞穴整条链里冒出来的红，复现成本高（要先跑洞穴再切）。这里把它压到一根合成的长细管上，
            // 于是"改切割核心 → 看结果"是一秒钟的回路，而不是每次跑整条链去猜。
            // 两种夹具只差**刀口是否正好落在一圈顶点上**：
            //   · rings=9 根管，路径从 -240 到 +240、每 60 一环 ⇒ 第 5 环正好压在 y=0；
            //   · 对照组把整根管子平移 3.7，于是最近的环离刀口 3.7——这一档才是"正常切割"。
            var exactRing = StraightTube(offset: 0.0f);
            var nearRing = StraightTube(offset: 3.7f);

            bool nearCut = PCGMeshCut.TryCutByPlane(nearRing, Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), false,
                out var nearHalf, out string nearError);

            check("合成对照（刀口离最近的环 3.7，正常切割）：切成功且结果水密",
                nearCut && EdgeReport(nearHalf).boundary == 0,
                nearCut
                    ? $"{nearRing.TriangleCount} → {nearHalf.TriangleCount} 面；破口边 {EdgeReport(nearHalf).boundary}"
                    : $"被拒：{nearError}");

            bool exactCut = PCGMeshCut.TryCutByPlane(exactRing, Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), false,
                out var exactHalf, out string exactError);

            // 2026-10-11 修好（见文档 §7.5）：刀口**正好穿过一圈顶点**曾经会切出一个开口网格；现在同一档
            // 要么切成功且水密，要么明确拒绝——两条都算过。这里钉的是**修好之后的行为**，不再是缺陷本身。
            bool exactOk = exactCut && EdgeReport(exactHalf).boundary == 0;

            check("刀口正好穿过一圈顶点时：切成功且水密（修好之前这里会切出一个开口网格）",
                exactOk,
                exactCut
                    ? $"切成功：{exactRing.TriangleCount} → {exactHalf.TriangleCount} 面；" +
                      $"破口边 1e-4→{EdgeReport(exactHalf).boundary}、1e-1→{EdgeReport(exactHalf, 1e-1f).boundary}"
                    : $"被拒：{exactError}");

            // ---- ② SampleGeometry 认这条通道 ----
            var sampler = new PCGSampleGeometryNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGSampleGeometryNode.Id,
                new object[] { 24, 1.0f, 0 },
                0));

            var sampled = sampler.Execute(context, new[] { tubes });

            check("SampleGeometry 从洞穴几何里采出了点", sampled != null && sampled.Count > 0,
                sampled?.Count.ToString() ?? "null");
            check("每块洞壁都贡献了采样点（不是只有第一块在出点）",
                sampled != null && sampled.Count >= tubeCount,
                $"{sampled?.Count} 点 / {tubeCount} 块");

            // 这一条是"摆放真的生效"的判据：采样点在管子自己的世界里，而不是原点附近。
            float worst = FarthestOutside(sampled, tubeBounds);

            check("采出来的点都在管子的世界包围盒里（摆放真的生效）", worst <= 0.01f,
                $"最大越界 {worst:F2}");

            // ---- ③ 对照：把摆放抹掉，同一条链该把点采到原点附近 ----
            var identity = WithoutPlacement(tubes);

            check("对照的前提：抹掉摆放后几何块数不变",
                identity.Geometry != null && identity.Geometry.Length == tubeCount);

            var flatSampled = sampler.Execute(context, new[] { identity });
            float flatWorst = FarthestOutside(flatSampled, tubeBounds);

            check("对照：抹掉摆放后，采样点不再落在管子的世界包围盒里（②测的是摆放）",
                flatWorst > 1.0f, $"最大越界 {flatWorst:F2}");
        }

        // ---- 夹具 ------------------------------------------------------------------------------------------

        private static PCGCaveNode MakeCave() =>
            new PCGCaveNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCaveNode.Id,
                new object[] { 60.0f, 0.25f, 0.4f, 30.0f, 200.0f, 8, 20.0f, 0.3f, 6, 10, true, false },
                0));

        /// <summary>一根合成长细管：半径 20、10 边、两端封口、法线朝内（与洞壁同一档形状）。</summary>
        private static PCGMesh StraightTube(float offset)
        {
            var path = new List<Vector3>(9);

            for (int i = 0; i < 9; i++)
                path.Add(new Vector3(0.0f, -240.0f + i * 60.0f + offset, 0.0f));

            return PCGMeshBuilder.Tube(path, 20.0f, 10, true, true);
        }

        /// <summary>所有洞壁的世界包围盒并集；<paramref name="pureTranslation"/> 报告摆放里有没有旋转/缩放。</summary>
        private static BoundingBox TubesWorldBounds(PCGGeometryItem[] geometry, out bool pureTranslation)
        {
            pureTranslation = true;

            var minimum = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var maximum = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < geometry.Length; i++)
            {
                var item = geometry[i];
                var bounds = PCGMeshBuilder.Bounds(item.Mesh);
                var offset = item.Placement.Translation;

                if ((item.Placement.Orientation != Quaternion.Identity) ||
                    (item.Placement.Scale - Float3.One).Length > 1e-4f)
                    pureTranslation = false;

                minimum = Vector3.Min(minimum, bounds.Minimum + offset);
                maximum = Vector3.Max(maximum, bounds.Maximum + offset);
            }

            return new BoundingBox(minimum, maximum);
        }

        /// <summary>点集里离这个盒子最远的越界距离（都在盒里时为 0）。</summary>
        private static float FarthestOutside(in PCGData data, BoundingBox box)
        {
            if (data == null || data.Count == 0)
                return float.MaxValue;

            float worst = 0.0f;

            for (int i = 0; i < data.Count; i++)
            {
                var p = data.Points[i].Position;
                float outside = 0.0f;

                outside = Math.Max(outside, box.Minimum.X - p.X);
                outside = Math.Max(outside, p.X - box.Maximum.X);
                outside = Math.Max(outside, box.Minimum.Y - p.Y);
                outside = Math.Max(outside, p.Y - box.Maximum.Y);
                outside = Math.Max(outside, box.Minimum.Z - p.Z);
                outside = Math.Max(outside, p.Z - box.Maximum.Z);
                worst = Math.Max(worst, outside);
            }

            return worst;
        }

        /// <summary>同一份数据的副本，只是把每块几何的摆放换成单位变换——用来给"摆放生效"找一条反向对照。</summary>
        private static PCGData WithoutPlacement(PCGData source)
        {
            var copy = PCGData.Create(source.Count);

            for (int i = 0; i < source.Count; i++)
                copy.Points[i] = source.Points[i];

            copy.SharedAttributes = source.SharedAttributes;

            var geometry = new PCGGeometryItem[source.Geometry.Length];

            for (int i = 0; i < source.Geometry.Length; i++)
                geometry[i] = new PCGGeometryItem
                {
                    Mesh = source.Geometry[i].Mesh,
                    Placement = new Transform(Vector3.Zero, Quaternion.Identity, Float3.One),
                };

            copy.Geometry = geometry;
            return copy;
        }

        private static float TotalVolume(in PCGData data)
        {
            if (data?.Geometry == null)
                return 0.0f;

            float total = 0.0f;

            for (int i = 0; i < data.Geometry.Length; i++)
                total += Math.Abs(SignedVolume(data.Geometry[i].Mesh));

            return total;
        }

        private static float SignedVolume(PCGMesh mesh)
        {
            if (mesh == null || mesh.Triangles == null)
                return 0.0f;

            double sum = 0.0;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var a = mesh.Vertices[mesh.Triangles[t * 3]];
                var b = mesh.Vertices[mesh.Triangles[t * 3 + 1]];
                var c = mesh.Vertices[mesh.Triangles[t * 3 + 2]];
                sum += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }

            return (float)sum;
        }

        /// <summary>统计非流形边（顶点按位置合并，否则接缝上重复的顶点会被误判成边界）。</summary>
        private static (int boundary, int total) EdgeReport(PCGMesh mesh, float weld = 1e-4f)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null)
                return (0, 0);

            var merged = Weld(mesh.Vertices, weld);
            var counts = new Dictionary<(int, int), int>();

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = merged[mesh.Triangles[t * 3]];
                int b = merged[mesh.Triangles[t * 3 + 1]];
                int c = merged[mesh.Triangles[t * 3 + 2]];

                Bump(counts, a, b);
                Bump(counts, b, c);
                Bump(counts, c, a);
            }

            int boundary = 0;

            foreach (var pair in counts)
                if (pair.Value != 2)
                    boundary++;

            return (boundary, counts.Count);
        }

        /// <summary>把每个顶点映到"同一个位置"的下标：合并半径 <paramref name="epsilon"/> 内的点。</summary>
        /// <remarks>
        /// 判据是**按位置配对**（本插件对水密的一贯定义），所以合并必须按**距离**做，而不是按"量化后的桶"做：
        /// 桶式的键会在桶边界上劈叉——两点相距 1e-9 却因为跨了 .5 那条线而落进两个桶，于是"本该同一条边"
        /// 在两个三角形里各算一次，看起来就是一条边界边。实测就是这个症状在飘：同一份网格在 1e-3 下报 10 条、
        /// 1e-4 下报 4 条、1e-5 下报 12 条——**报数随尺子变，说明毛病在尺子**（桶本身），不在网格。
        /// 这里按 3×3×3 邻域找已有点，簇的半径就与桶边界无关了。
        /// <summary>
        /// 绕序是否**自洽**：同一条边被两个三角形共用时，两个方向必须相反（顶点先按位置焊）。
        /// </summary>
        /// <remarks>
        /// 与法线无关，所以不会被"硬边上共享一个法线"这种**表示**问题带偏。§7.5 那条"切口那一圈的盖朝向
        /// 与法线相反"实测就是后者：累加器按位置焊接顶点、焊接时保留**先插进来的**法线，于是"洞壁"与"盖"
        /// 共享的那圈棱上只带一个法线，硬边上的三角形必然与"三个顶点法线的均值"不同向。换成这个判据之后，
        /// 同一批网格给出的是关于**绕序本身**的结论。
        /// </remarks>
        private static bool OrientationConsistent(PCGMesh mesh)
        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null || mesh.TriangleCount == 0)
                return false;

            var merged = Weld(mesh.Vertices, 1e-4f);
            var directed = new Dictionary<(int, int), int>();

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int a = merged[mesh.Triangles[t * 3]];
                int b = merged[mesh.Triangles[t * 3 + 1]];
                int c = merged[mesh.Triangles[t * 3 + 2]];

                BumpDirected(directed, a, b);
                BumpDirected(directed, b, c);
                BumpDirected(directed, c, a);
            }

            foreach (var pair in directed)
            {
                if (pair.Key.Item1 == pair.Key.Item2)
                    continue;

                if (!directed.TryGetValue((pair.Key.Item2, pair.Key.Item1), out int opposite) || opposite != pair.Value)
                    return false;
            }

            return true;
        }

        private static void BumpDirected(Dictionary<(int, int), int> directed, int a, int b)
        {
            directed.TryGetValue((a, b), out int count);
            directed[(a, b)] = count + 1;
        }

        /// </remarks>
        private static int[] Weld(Vector3[] vertices, float epsilon = 1e-4f)
        {
            var buckets = new Dictionary<(long, long, long), List<int>>();
            var positions = new List<Vector3>();
            var ids = new int[vertices.Length];
            float cell = Math.Max(epsilon * 2.0f, 1e-6f);
            float squared = epsilon * epsilon;

            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var cellIndex = Cell(p, cell);
                int found = -1;

                for (long dx = -1; dx <= 1 && found < 0; dx++)
                    for (long dy = -1; dy <= 1 && found < 0; dy++)
                        for (long dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!buckets.TryGetValue((cellIndex.Item1 + dx, cellIndex.Item2 + dy, cellIndex.Item3 + dz),
                                    out var list))
                                continue;

                            for (int k = 0; k < list.Count; k++)
                            {
                                if ((positions[list[k]] - p).LengthSquared <= squared)
                                {
                                    found = list[k];
                                    break;
                                }
                            }
                        }

                if (found < 0)
                {
                    found = positions.Count;
                    positions.Add(p);

                    if (!buckets.TryGetValue(cellIndex, out var list))
                    {
                        list = new List<int>(4);
                        buckets.Add(cellIndex, list);
                    }

                    list.Add(found);
                }

                ids[i] = found;
            }

            return ids;
        }

        private static (long, long, long) Cell(Vector3 p, float cell) =>
            ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));

        private static void Bump(Dictionary<(int, int), int> counts, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        /// <summary>绕序与法线是否一致：几何法线必须与三个顶点法线的均值同向。</summary>
        /// <summary>绕序不一致的三角形**有几个**、以及最差那个的**余弦与面积**。</summary>
        /// <remarks>
        /// `WindingAgrees` 的判据是"几何法线与三顶点法线之和的点积 ≤ 0"——也就是**超过 90°**才算不一致。
        /// 所以报出来的这几个不是"浅口子"那种容差噪声，而是**真方向相反**。要判"是碎片还是真三角形"，
        /// 就得把最差那个的**面积**也打印出来（碎片的几何法线本身就不稳定）。
        /// </remarks>
        private static string WindingBreakDetail(PCGMesh mesh)
        {
            int bad = 0;
            float worstCosine = 1.0f;
            float worstArea = 0.0f;
            int worstTri = -1;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int ia = mesh.Triangles[t * 3];
                int ib = mesh.Triangles[t * 3 + 1];
                int ic = mesh.Triangles[t * 3 + 2];
                var geometric = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia],
                                              mesh.Vertices[ic] - mesh.Vertices[ia]);
                var vertexNormal = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];
                float dot = Vector3.Dot(geometric, vertexNormal);

                if (dot > 0.0f)
                    continue;

                bad++;

                float area = geometric.Length * 0.5f;
                float cosine = geometric.Length > 1e-20f && vertexNormal.Length > 1e-20f
                    ? dot / (geometric.Length * vertexNormal.Length)
                    : 0.0f;

                if (bad == 1 || cosine < worstCosine)
                {
                    worstCosine = cosine;
                    worstArea = area;
                    worstTri = t;
                }
            }

            if (bad == 0)
                return "（其实自洽）";

            // 最差那个三角形的**原始数据**：三个顶点的 y（洞壁被切在局部 y=0 上 ⇒ 全在平面上就是盖三角形）、
            // 三个顶点各自的法线、以及几何法线。余弦不是 ±1 就说明"三个顶点法线不都同向"——
            // 那要么是盖与墙共用了顶点、要么是盖的顶点被焊到了墙上，两种都有各自的下游影响。
            int wa = mesh.Triangles[worstTri * 3];
            int wb = mesh.Triangles[worstTri * 3 + 1];
            int wc = mesh.Triangles[worstTri * 3 + 2];
            var ga = Vector3.Cross(mesh.Vertices[wb] - mesh.Vertices[wa], mesh.Vertices[wc] - mesh.Vertices[wa]);

            return $"：{bad} 个三角形，最差余弦 {worstCosine:F3}、面积 {worstArea:0.######e+00}" +
                   $"；最差三角形顶点 y=({mesh.Vertices[wa].Y:F3},{mesh.Vertices[wb].Y:F3},{mesh.Vertices[wc].Y:F3})" +
                   $" 法线=({Format(mesh.Normals[wa])})({Format(mesh.Normals[wb])})({Format(mesh.Normals[wc])})" +
                   $" 几何法线=({Format(ga)})";
        }

        /// <summary>向量按三位小数写成 <c>x,y,z</c>（诊断用）。</summary>
        private static string Format(Vector3 v) => $"{v.X:F2},{v.Y:F2},{v.Z:F2}";

        private static bool WindingAgrees(PCGMesh mesh)        {
            if (mesh == null || mesh.Normals == null || mesh.Triangles == null)
                return false;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int ia = mesh.Triangles[t * 3];
                int ib = mesh.Triangles[t * 3 + 1];
                int ic = mesh.Triangles[t * 3 + 2];
                var geometric = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia],
                                              mesh.Vertices[ic] - mesh.Vertices[ia]);

                if (geometric.Length < 1e-9f)
                    continue;

                var vertexNormal = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];

                if (Vector3.Dot(geometric, vertexNormal) <= 0.0f)
                    return false;
            }

            return true;
        }
    }
}

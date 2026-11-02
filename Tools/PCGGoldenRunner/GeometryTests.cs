// PCG Plugin - Executable tests for the procedural geometry core (docs/PCG_PLUGIN_DESIGN.md §5.1.33).
//
// Geometry is the kind of code where "it looks like a box" is not evidence: a face wound the wrong way, a hole
// where two faces should meet and a normal that disagrees with its winding all produce a shape that counts
// correctly and renders wrong. So nothing here counts vertices. The validator below is written independently of
// the builders - closed volume by the divergence theorem, watertightness by position-keyed edges, and winding
// against the stored normals - and the expected numbers are the ones a person can work out on paper.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class GeometryTests
    {
        internal static void Run(CheckDelegate check)
        {
            BoxTests(check);
            VolumeTests(check);
            OpenSurfaceTests(check);
            TransformTests(check);
            MergeTests(check);
            NodeTests(check);
            CarrierTests(check);
            IndependentGeometryTests(check);
            SampleGeometryTests(check);
        }

        /// <summary>
        /// The node that consumes the builders: one mesh per point, in the point's local space.
        /// </summary>
        private static void NodeTests(CheckDelegate check)
        {
            var points = PCGData.Create(3);

            for (int i = 0; i < points.Count; i++)
            {
                points.Points[i] = PCGPoint.Create(new Vector3(i * 100.0f, 0.0f, 0.0f), 1.0f, i);
                points.Points[i].Scale = new Float3(4.0f, 2.0f, 6.0f);
                points.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            var node = MakeGeometry(PCGGeometryNode.ShapeBox, 8, 4, usePointScale: true);
            var result = node.Execute(Context(), new[] { points });

            check("every point comes back with a mesh, index aligned",
                result.Meshes != null && result.Meshes.Length == result.Count && result.Count == 3,
                result.Meshes == null ? "no meshes" : result.Meshes.Length.ToString());

            check("the shape is sized from the point",
                Near(PCGMeshBuilder.Bounds(result.Meshes[0]).Size.X, 4.0f) &&
                Near(PCGMeshBuilder.Bounds(result.Meshes[0]).Size.Y, 2.0f) &&
                Near(PCGMeshBuilder.Bounds(result.Meshes[0]).Size.Z, 6.0f),
                PCGMeshBuilder.Bounds(result.Meshes[0]).Size.ToString());

            // Local space: the mesh is centred on the origin and the point keeps its own place, which is what lets
            // one built shape serve every point that shares a size.
            check("the mesh is built around the origin and the point is left where it was",
                Near(PCGMeshBuilder.Bounds(result.Meshes[2]).Center.X, 0.0f) &&
                Near(result.Points[2].Position.X, 200.0f),
                PCGMeshBuilder.Bounds(result.Meshes[2]).Center.ToString());

            check("the points keep their transforms",
                Near(result.Points[0].Scale.X, 4.0f) && Near(result.Points[0].Position.X, 0.0f));

            // Each shape kind has to actually differ, or the enum would be decorative.
            var grid = MakeGeometry(PCGGeometryNode.ShapeGrid, 4, 4, true).Execute(Context(), new[] { points });
            var cylinder = MakeGeometry(PCGGeometryNode.ShapeCylinder, 8, 4, true).Execute(Context(), new[] { points });
            var stairs = MakeGeometry(PCGGeometryNode.ShapeStairs, 8, 4, true).Execute(Context(), new[] { points });

            check("the four shapes build different meshes",
                result.Meshes[0].TriangleCount == 12 &&
                grid.Meshes[0].TriangleCount == 4 * 4 * 2 &&
                cylinder.Meshes[0].TriangleCount == 8 * 4 &&
                stairs.Meshes[0].TriangleCount == 4 * 12,
                $"{result.Meshes[0].TriangleCount}/{grid.Meshes[0].TriangleCount}/" +
                $"{cylinder.Meshes[0].TriangleCount}/{stairs.Meshes[0].TriangleCount}");

            check("every shape the node builds is wound consistently with its normals",
                WindingAgrees(result.Meshes[0]) && WindingAgrees(grid.Meshes[0]) &&
                WindingAgrees(cylinder.Meshes[0]) && WindingAgrees(stairs.Meshes[0]));

            // An unknown shape index is what a graph from a newer plugin looks like: it falls back rather than
            // producing nothing.
            var unknown = MakeGeometry(99, 8, 4, true).Execute(Context(), new[] { points });

            check("an unknown shape index falls back to the box",
                unknown.Meshes[0].TriangleCount == 12 && unknown.Count == 3);

            // No input: one point at the generation bounds centre carrying the configured cell's shape.
            // A declared pin means the caller passes one entry; null is how an unconnected optional pin arrives.
            var single = MakeGeometry(PCGGeometryNode.ShapeBox, 8, 4, true).Execute(Context(), new PCGData[] { null });

            check("with no input the node makes one point carrying one shape",
                single.Count == 1 && single.Meshes != null && single.Meshes.Length == 1 &&
                Near(PCGMeshBuilder.Bounds(single.Meshes[0]).Size.Y, 200.0f),
                $"{single.Count} point(s)");

            check("two runs over the same points build the same shapes",
                SameArrays(result.Meshes[1], result.Meshes[0]));
        }

        /// <summary>
        /// The carrier on the data model: a mesh belongs to a point, so the operations that move points move shapes.
        /// </summary>
        private static void CarrierTests(CheckDelegate check)
        {
            var points = PCGData.Create(3);

            for (int i = 0; i < points.Count; i++)
            {
                points.Points[i] = PCGPoint.Create(new Vector3(i, 0, 0), 1.0f, i);
                points.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            points.Meshes = new[]
            {
                PCGMeshBuilder.Box(new Vector3(1, 1, 1)),
                PCGMeshBuilder.Box(new Vector3(2, 2, 2)),
                PCGMeshBuilder.Box(new Vector3(3, 3, 3)),
            };

            var gathered = PCGData.Gather(points, new[] { 2, 0 }, 2);

            // The reason the carrier is per-point: keeping all three would pair the third point's shape with the
            // first point's position.
            check("gathering points takes their shapes with them, in the same order",
                gathered.Meshes != null && gathered.Meshes.Length == 2 &&
                Near(PCGMeshBuilder.Bounds(gathered.Meshes[0]).Size.X, 3.0f) &&
                Near(PCGMeshBuilder.Bounds(gathered.Meshes[1]).Size.X, 1.0f),
                gathered.Meshes == null ? "no meshes" : gathered.Meshes.Length.ToString());

            var merged = PCGData.Merge(new[] { points, points }, out string mergeError);

            check("merging concatenates the shapes in part order",
                mergeError == null && merged.Meshes != null && merged.Meshes.Length == 6 &&
                Near(PCGMeshBuilder.Bounds(merged.Meshes[3]).Size.X, 1.0f),
                mergeError ?? $"{merged.Meshes?.Length} mesh(es)");

            var cloned = points.Clone();

            check("a clone carries the shapes too", cloned.Meshes != null && cloned.Meshes.Length == 3);

            // A layer with no geometry must not gain an empty array: "no meshes" and "an array of nulls" have to be
            // distinguishable, or every consumer would have to check element by element.
            var bare = PCGData.Create(2);

            check("a layer with no geometry stays with no geometry through a gather",
                PCGData.Gather(bare, new[] { 0 }, 1).Meshes == null);

            SpawnKeyTests(check);
        }

        /// <summary>
        /// The key that decides which points share one built model.
        /// </summary>
        /// <remarks>
        /// Wrong in either direction and the run looks almost right: too coarse and a point gets a shape built for a
        /// different size, too fine and every point pays for its own upload. It is pure, so it is checked here even
        /// though the spawner around it needs a scene.
        /// </remarks>
        private static void SpawnKeyTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 6.0f));
            var same = PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 6.0f));
            var bigger = PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 7.0f));

            // Two equal shapes arrive as two distinct objects and have to share one model.
            check("two equal shapes share a key",
                PCGSpawnGeometryNode.KeyOf(box) == PCGSpawnGeometryNode.KeyOf(same),
                PCGSpawnGeometryNode.KeyOf(box));

            check("a different size gets its own key",
                PCGSpawnGeometryNode.KeyOf(box) != PCGSpawnGeometryNode.KeyOf(bigger));

            // The staircase has half a unit of the box's volume in a different arrangement, so a size-only key would
            // hand one of them the other's geometry. Its extents match the box above on purpose.
            var stairs = PCGMeshBuilder.Stairs(new Vector3(2.0f, 4.0f, 6.0f), 3);

            check("a different shape of the same extents gets its own key",
                PCGSpawnGeometryNode.KeyOf(box) != PCGSpawnGeometryNode.KeyOf(stairs),
                $"{PCGSpawnGeometryNode.KeyOf(box)} vs {PCGSpawnGeometryNode.KeyOf(stairs)}");

            check("the key says nothing about the mesh's identity as an object",
                PCGSpawnGeometryNode.KeyOf(box) == PCGSpawnGeometryNode.KeyOf(PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 6.0f))));

            // 旧键是"三角形数 + 包围盒尺寸"，对这一对**撞车**：顶点和包围盒一模一样，只有绕序反了。
            // 撞车时缓存会把另一块几何交给这个点（屏幕上是"某个道具长成了别的样子"），所以这条是
            // 换键的正面证据，不是锦上添花。
            var flipped = FlippedWinding(box);

            check("绕序相反的两块不共享键（旧键在这里会撞车）",
                PCGSpawnGeometryNode.KeyOf(box) != PCGSpawnGeometryNode.KeyOf(flipped),
                $"{PCGSpawnGeometryNode.KeyOf(box)} vs {PCGSpawnGeometryNode.KeyOf(flipped)}");

            check("只差千分之一以下的浮点噪声仍共享键（量化确实在起作用）",
                PCGSpawnGeometryNode.KeyOf(box) == PCGSpawnGeometryNode.KeyOf(Noisy(box, 1e-6f)));

            var spawner = new PCGSpawnGeometryNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGSpawnGeometryNode.Id, new object[] { 1000, 0.0f, 0 }, 0));

            check("the spawner describes its own limits",
                spawner.DescribeParameters().Contains("1000") &&
                spawner.DescribeParameters().Contains("actor"),
                spawner.DescribeParameters());

            // 参数是**只能往后追加**的存档格式：个数与两档输出形态一起钉住，免得后来插一个把老图整体挪位。
            check("参数个数与槽位表一致（上限/密度/种子/合并/实例化/层标签）",
                PCGSpawnGeometryNode.ParameterCount == 6,
                PCGSpawnGeometryNode.ParameterCount.ToString());

            // 追加参数的两条命：老数组照旧能读、前五档不被挤位（下标错位不抛异常，只会整体平移）。
            var legacy = new PCG.Nodes.PCGSpawnGeometryNode(
                new PCGNodeRegistry.PCGNodeSerializationData(
                    PCG.Nodes.PCGSpawnGeometryNode.Id,
                    new object[] { 7, 0.25f, 3, true, false }, 0));

            check("五值老数组读出前五档不变（追加没挤位）",
                legacy.MaxActors == 7 && System.Math.Abs(legacy.DensityThreshold - 0.25f) < 1e-5f &&
                legacy.SeedOffset == 3 && legacy.MergeIntoOneActor && !legacy.ToFoliageHost,
                $"{legacy.MaxActors} / {legacy.DensityThreshold} / {legacy.SeedOffset} / "
                + $"{legacy.MergeIntoOneActor} / {legacy.ToFoliageHost}");

            check("老数组缺的第六档退回空标签（不抛）",
                legacy.Label == string.Empty, legacy.Label ?? "null");

            var labeled = new PCG.Nodes.PCGSpawnGeometryNode(
                new PCGNodeRegistry.PCGNodeSerializationData(
                    PCG.Nodes.PCGSpawnGeometryNode.Id,
                    new object[] { 7, 0.25f, 3, false, false, "房屋 2" }, 0));

            check("第六档真被读进 Label", labeled.Label == "房屋 2", labeled.Label ?? "null");
        }

        /// <summary>
        /// **独立几何**（`PCGData.Geometry`）：不属于任何点、自带摆放的那种承载。
        /// </summary>
        /// <remarks>
        /// 这是"Geometry 是数据类型"的另一半：一块造出来的地形/墙/柱子可以没有点。断言围着它的三条传播规则
        /// 与那个必须修掉的合并early-out——**一份没有点、只有几何的数据集是合法的**，而合并原来拿"点数为 0"
        /// 当"整份为空"，几何会在那一步被静默丢掉（那种缺陷在图上完全看不出来，只是几何没了）。
        /// </remarks>
        private static void IndependentGeometryTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            // 只有几何、没有点。
            var geometryOnly = new PCGData(Array.Empty<PCGPoint>())
            {
                Geometry = new[] { PCGGeometryItem.At(box, new Vector3(10.0f, 0.0f, 0.0f)) },
            };

            check("只有几何没有点的数据集是合法的（Count 为 0，但几何在）",
                geometryOnly.Count == 0 && geometryOnly.Geometry != null && geometryOnly.Geometry.Length == 1,
                $"{geometryOnly.Count} point(s), {geometryOnly.Geometry?.Length ?? 0} geometry item(s)");

            // 与曲线同一条规则：不是逐点的行，筛点/收拢都不动它。
            var gathered = PCGData.Gather(geometryOnly, Array.Empty<int>(), 0);

            check("收拢点不动独立几何（它不属于任何点）",
                gathered.Geometry != null && gathered.Geometry.Length == 1 &&
                Near(gathered.Geometry[0].Placement.Translation.X, 10.0f),
                gathered.Geometry == null ? "no geometry" : gathered.Geometry.Length.ToString());

            // 合并：一份"没有点、只有几何"的层，与一份"有点、没有几何"的层，两样都得活下来。
            var pointsOnly = PCGData.Create(2);

            for (int i = 0; i < 2; i++)
            {
                pointsOnly.Points[i] = PCGPoint.Create(new Vector3(i * 5.0f, 0.0f, 0.0f), 1.0f, i);
                pointsOnly.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            var mixed = PCGData.Merge(new[] { geometryOnly, pointsOnly }, out string mixedError);

            check("合并：几何层与点层都不丢（几何层的点数为 0 不等于整份为空）",
                mixedError == null && mixed.Count == 2 &&
                mixed.Geometry != null && mixed.Geometry.Length == 1,
                mixedError ?? $"{mixed.Count} point(s), {mixed.Geometry?.Length ?? 0} geometry item(s)");

            var twoGeometry = PCGData.Merge(new[]
            {
                geometryOnly,
                new PCGData(Array.Empty<PCGPoint>())
                {
                    Geometry = new[] { PCGGeometryItem.At(box, new Vector3(20.0f, 0.0f, 0.0f)) },
                },
            }, out string twoError);

            check("合并两块独立几何：按部分顺序拼起来，而不是被判成空",
                twoError == null && twoGeometry.Geometry != null && twoGeometry.Geometry.Length == 2 &&
                Near(twoGeometry.Geometry[0].Placement.Translation.X, 10.0f) &&
                Near(twoGeometry.Geometry[1].Placement.Translation.X, 20.0f),
                twoError ?? $"{twoGeometry.Geometry?.Length ?? 0} geometry item(s)");

            // 克隆复制的是装它们的数组：改克隆那一份不该反手改到原件（元素是 struct，网格仍共享）。
            var cloned = geometryOnly.Clone();
            cloned.Geometry[0].Placement.Translation.X = 999.0f;

            check("克隆的几何数组是自己的（改克隆不影响原件）",
                Near(geometryOnly.Geometry[0].Placement.Translation.X, 10.0f),
                geometryOnly.Geometry[0].Placement.Translation.X.ToString());

            // 合并时把**摆放烘进顶点**：一个 actor 只有一个 Transform，表达不了"每块各有各的位置"。
            var merged = PCGMeshBuilder.MergePlaced(geometryOnly.Geometry);

            var bounds = PCGMeshBuilder.Bounds(merged);

            check("MergePlaced 把摆放烘进顶点（两块各在各自的位置上）",
                merged.VertexCount == box.VertexCount && merged.TriangleCount == box.TriangleCount &&
                Near(bounds.Minimum.X, 9.0f) && Near(bounds.Maximum.X, 11.0f),
                $"{bounds.Minimum.X:F1}..{bounds.Maximum.X:F1}");

            var two = PCGMeshBuilder.MergePlaced(twoGeometry.Geometry);
            var twoBounds = PCGMeshBuilder.Bounds(two);

            check("MergePlaced 合并多块：顶点相加、包围盒覆盖全部",
                two.VertexCount == box.VertexCount * 2 &&
                Near(twoBounds.Minimum.X, 9.0f) && Near(twoBounds.Maximum.X, 21.0f),
                $"{two.VertexCount} vertex/vertices, x {twoBounds.Minimum.X:F1}..{twoBounds.Maximum.X:F1}");

            check("MergePlaced 跳过空项、没有输入时给空网格",
                PCGMeshBuilder.MergePlaced(new[] { new PCGGeometryItem(), geometryOnly.Geometry[0] })
                    .VertexCount == box.VertexCount &&
                PCGMeshBuilder.MergePlaced(null) != null &&
                PCGMeshBuilder.MergePlaced(null).VertexCount == 0);
        }

        /// <summary>同一份网格，只把最后一个三角形的绕序反过来——顶点、包围盒、三角形数都不变。</summary>
        private static PCGMesh FlippedWinding(in PCGMesh mesh)
        {
            var copy = new PCGMesh
            {
                Vertices = (Vector3[])mesh.Vertices.Clone(),
                Triangles = (int[])mesh.Triangles.Clone(),
                Normals = (Vector3[])mesh.Normals.Clone(),
                UVs = (Vector2[])mesh.UVs.Clone(),
            };

            int last = copy.Triangles.Length - 3;
            (copy.Triangles[last], copy.Triangles[last + 1]) = (copy.Triangles[last + 1], copy.Triangles[last]);

            return copy;
        }

        /// <summary>同一份网格，每个顶点加一点远小于量化步长的噪声（应当仍被认成同一形状）。</summary>
        private static PCGMesh Noisy(in PCGMesh mesh, float amplitude)
        {
            var copy = new PCGMesh
            {
                Vertices = new Vector3[mesh.Vertices.Length],
                Triangles = (int[])mesh.Triangles.Clone(),
                Normals = (Vector3[])mesh.Normals.Clone(),
                UVs = (Vector2[])mesh.UVs.Clone(),
            };

            for (int i = 0; i < copy.Vertices.Length; i++)
            {
                var v = mesh.Vertices[i];
                copy.Vertices[i] = new Vector3(v.X + amplitude, v.Y - amplitude, v.Z + amplitude);
            }

            return copy;
        }

        /// <summary>
        /// **几何采样器**：把载荷里的几何采成点——"Geometry 是数据类型"的另一个方向（造得出，也采得出）。
        /// </summary>
        /// <remarks>
        /// 判据刻意落在"法线来自表面"与"摆放真的生效"两件事上：只数点的话，一个把点全撒在原点、
        /// 朝向全用世界向上轴实现，点数与确定性都对得上，而屏幕上是"一片贴在地上的点"。
        /// </remarks>
        private static void SampleGeometryTests(CheckDelegate check)
        {
            // 平面网格（法线 +Y）、摆在原点：采出来的点必须都朝上、都落在网格平面上。
            var grid = PCGMeshBuilder.Grid(new Vector2(40.0f, 40.0f), 2);

            var loose = new PCGData(Array.Empty<PCGPoint>())
            {
                Geometry = new[] { PCGGeometryItem.At(grid, Vector3.Zero) },
            };

            var sampler = MakeSampler(pointsPerMesh: 12);
            var sampled = sampler.Execute(Context(), new[] { loose });

            check("独立几何（没有点）也能被采样——「几何是数据类型」的那一半",
                sampled != null && sampled.Count == 12,
                sampler.LastError ?? $"{(sampled == null ? "null" : sampled.Count.ToString())} point(s)");

            if (sampled == null || sampled.Count != 12)
                return;

            float worstUp = 1.0f;
            float worstOnPlane = 0.0f;
            float worstOutOfGrid = 0.0f;

            for (int i = 0; i < sampled.Count; i++)
            {
                var point = sampled.Points[i];
                var up = Vector3.Transform(Vector3.Up, point.Rotation);

                worstUp = Math.Min(worstUp, up.Y);
                worstOnPlane = Math.Max(worstOnPlane, Math.Abs(point.Position.Y));
                worstOutOfGrid = Math.Max(worstOutOfGrid,
                    Math.Max(Math.Abs(point.Position.X), Math.Abs(point.Position.Z)));
            }

            check("朝向来自三角形法线（平面网格上全部朝 +Y，不是世界向上轴凑巧对）",
                worstUp > 0.99f, $"最差 up.Y {worstUp:F4}");

            check("点落在网格自己的平面上（Y≈0）",
                worstOnPlane < 1e-3f, $"最差 |Y| {worstOnPlane:F4}");

            check("点落在网格的范围里（±20 之内）",
                worstOutOfGrid <= 20.0f + 1e-3f, $"最远 {worstOutOfGrid:F2}");

            // 与点对齐的承载：每个点一份网格，摆在**点自己的变换**里。
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));
            var aligned = PCGData.Create(3);

            for (int i = 0; i < 3; i++)
            {
                aligned.Points[i] = PCGPoint.Create(new Vector3(i * 100.0f, 0.0f, 0.0f), 1.0f, i);
                aligned.Points[i].Scale = Float3.One;
                aligned.Points[i].UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));
            }

            aligned.Meshes = new[] { box, box, box };

            var fromAligned = MakeSampler(pointsPerMesh: 4).Execute(Context(), new[] { aligned });

            check("与点对齐的网格也能采：3 个点 × 每块 4 点 = 12",
                fromAligned != null && fromAligned.Count == 12,
                fromAligned == null ? "null" : $"{fromAligned.Count} point(s)");

            if (fromAligned != null && fromAligned.Count == 12)
            {
                int nearFirst = 0, nearSecond = 0, nearThird = 0;

                for (int i = 0; i < fromAligned.Count; i++)
                {
                    float x = fromAligned.Points[i].Position.X;

                    if (x < 50.0f) nearFirst++;
                    else if (x < 150.0f) nearSecond++;
                    else nearThird++;
                }

                check("每块网格都摆在它那个点的位置上（三簇各 4 个点）",
                    nearFirst == 4 && nearSecond == 4 && nearThird == 4,
                    $"{nearFirst}/{nearSecond}/{nearThird}");
            }

            // 两种承载同时存在时都要被采到。
            aligned.Geometry = new[] { PCGGeometryItem.At(box, new Vector3(1000.0f, 0.0f, 0.0f)) };

            var both = MakeSampler(pointsPerMesh: 4).Execute(Context(), new[] { aligned });

            check("两种承载都在时都会被采（对齐 12 + 独立 4 = 16）",
                both != null && both.Count == 16, both == null ? "null" : $"{both.Count} point(s)");

            // 确定性，以及 Seed Offset 真的把两条流分开。
            var first = MakeSampler(pointsPerMesh: 6).Execute(Context(), new[] { loose });
            var again = MakeSampler(pointsPerMesh: 6).Execute(Context(), new[] { loose });
            var shifted = MakeSampler(pointsPerMesh: 6, seedOffset: 5).Execute(Context(), new[] { loose });

            check("同种子两次采样逐点相同",
                SamePoints(first, again),
                first == null ? "null" : $"{first.Count} point(s)");

            check("Seed Offset 换掉之后布局就不同了",
                !SamePoints(first, shifted),
                "两个采样器的点完全一致");

            // 每块几何的随机流是**错开**的：同形状、同朝向的两块，局部图案不该一模一样。
            var twoSame = new PCGData(Array.Empty<PCGPoint>())
            {
                Geometry = new[]
                {
                    PCGGeometryItem.At(grid, new Vector3(0.0f, 0.0f, 0.0f)),
                    PCGGeometryItem.At(grid, new Vector3(500.0f, 0.0f, 0.0f)),
                },
            };

            var pair = MakeSampler(pointsPerMesh: 8).Execute(Context(), new[] { twoSame });

            check("同形状的两块几何采出的局部图案不同（每块一条错开的流，不是盖章）",
                pair != null && pair.Count == 16 && LocalPatternsDiffer(pair, 8),
                pair == null ? "null" : $"{pair.Count} point(s)");

            // 没有几何 / 每块点数为 0：原样透传 + 说原因（清空会在下游变成"那里什么都没长"）。
            var noGeometry = PCGData.Create(2);
            var bareSampler = MakeSampler(pointsPerMesh: 4);
            var passed = bareSampler.Execute(Context(), new[] { noGeometry });

            check("没有几何时原样透传并说明原因",
                passed != null && passed.Count == 2 && !string.IsNullOrEmpty(bareSampler.LastError),
                bareSampler.LastError ?? $"{passed?.Count} point(s)");

            var zeroSampler = MakeSampler(pointsPerMesh: 0);
            var zero = zeroSampler.Execute(Context(), new[] { loose });

            check("每块点数为 0 时透传（几何还在）并说明原因",
                zero != null && zero.Geometry != null && zero.Geometry.Length == 1 &&
                !string.IsNullOrEmpty(zeroSampler.LastError),
                zeroSampler.LastError ?? "没有报原因");
        }

        /// <summary>两块同形状的几何，采样点各自减去自己的摆放之后，局部图案是否**不同**。</summary>
        private static bool LocalPatternsDiffer(PCGData data, int perMesh)
        {
            float[] offsets = new float[perMesh];

            for (int i = 0; i < perMesh; i++)
            {
                var first = data.Points[i].Position;
                var second = data.Points[perMesh + i].Position;

                offsets[i] = Math.Abs((second.X - 500.0f) - first.X) +
                             Math.Abs(second.Y - first.Y) +
                             Math.Abs(second.Z - first.Z);
            }

            for (int i = 0; i < perMesh; i++)
            {
                if (offsets[i] > 1e-4f)
                    return true;
            }

            return false;
        }

        private static bool SamePoints(PCGData a, PCGData b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!Near(a.Points[i].Position.X, b.Points[i].Position.X, 1e-5f) ||
                    !Near(a.Points[i].Position.Y, b.Points[i].Position.Y, 1e-5f) ||
                    !Near(a.Points[i].Position.Z, b.Points[i].Position.Z, 1e-5f))
                {
                    return false;
                }
            }

            return true;
        }

        private static PCGSampleGeometryNode MakeSampler(int pointsPerMesh, int seedOffset = 0) =>
            new PCGSampleGeometryNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGSampleGeometryNode.Id, new object[] { pointsPerMesh, 1.0f, seedOffset }, 0));

        private static PCGGeometryNode MakeGeometry(int shape, int segments, int steps, bool usePointScale)
        {
            return new PCGGeometryNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGGeometryNode.Id,
                new object[] { shape, segments, steps, new Vector3(1000.0f, 200.0f, 1000.0f), usePointScale },
                0));
        }

        private static PCGExecutionContext Context() =>
            new PCGExecutionContext
            {
                Seed = 20261008,
                GenerationBounds = new BoundingBox(new Vector3(-2000.0f, -100.0f, -2000.0f),
                                                   new Vector3(2000.0f, 100.0f, 2000.0f)),
            };

        private static void BoxTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 6.0f));

            check("a box has twenty-four vertices and twelve triangles",
                box.VertexCount == 24 && box.TriangleCount == 12, box.ToString());

            check("normals and uvs line up with the vertices",
                box.Normals.Length == box.VertexCount && box.UVs.Length == box.VertexCount &&
                box.Triangles.Length == box.TriangleCount * 3);

            // Six faces, and each one flat: a box built with eight shared vertices would carry averaged normals at
            // the corners and shade like a ball, which no count would reveal.
            var directions = new List<Vector3>();

            for (int i = 0; i < box.Normals.Length; i++)
            {
                if (!Contains(directions, box.Normals[i]))
                    directions.Add(box.Normals[i]);
            }

            check("a box has six distinct face normals rather than smoothed corners",
                directions.Count == 6, $"{directions.Count} distinct normal(s)");

            check("every face normal is a unit axis",
                AllUnitAxes(box.Normals), First(box.Normals).ToString());

            // The property that decides whether the shape is visible at all: the winding a renderer culls by has to
            // agree with the normal it shades by.
            check("winding agrees with the stored normals on every triangle", WindingAgrees(box), WindingReport(box));

            // Watertight: every edge - the twelve cube edges and the six face diagonals - belongs to exactly two
            // triangles. A missing face, a duplicated triangle or a hole all break this, and nothing else here does.
            check("a box is closed: every edge is shared by two triangles", EdgeReport(box).unbalanced == 0,
                EdgeReport(box).ToString());

            check("a box encloses its exact volume",
                Near(SignedVolume(box), 2.0f * 4.0f * 6.0f, 1e-3f), SignedVolume(box).ToString("F3"));

            check("a box has its exact surface area",
                Near(SurfaceArea(box), 2.0f * (2 * 4 + 4 * 6 + 2 * 6), 1e-3f), SurfaceArea(box).ToString("F3"));

            var bounds = PCGMeshBuilder.Bounds(box);

            check("a box is centred on the origin and measures what it was asked for",
                Near(bounds.Minimum.X, -1.0f) && Near(bounds.Maximum.X, 1.0f) &&
                Near(bounds.Minimum.Y, -2.0f) && Near(bounds.Maximum.Y, 2.0f) &&
                Near(bounds.Minimum.Z, -3.0f) && Near(bounds.Maximum.Z, 3.0f),
                bounds.ToString());

            // The degenerate twin: a box with no size must stay a valid mesh - no NaN from a normalised zero - even
            // though it encloses nothing.
            var flat = PCGMeshBuilder.Box(Vector3.Zero);

            check("a zero-size box is still a valid mesh with no NaN",
                flat.TriangleCount == 12 && Near(SignedVolume(flat), 0.0f) && NoNaN(flat), flat.ToString());

            // Determinism matters here because two cells of a partitioned build must produce the same prop.
            var again = PCGMeshBuilder.Box(new Vector3(2.0f, 4.0f, 6.0f));

            check("two runs build the same mesh", SameArrays(box, again));
        }

        private static void VolumeTests(CheckDelegate check)
        {
            // The staircase is a loop over a profile, and its arithmetic is the easiest to get subtly wrong: an
            // off-by-one in the step height still looks like a staircase. The volume it must enclose is
            // width * run/n * (stepRise * (1 + 2 + ... + n)), which is arithmetic on paper.
            var stairs = PCGMeshBuilder.Stairs(new Vector3(2.0f, 8.0f, 4.0f), 4);

            const float expectedStairs = 2.0f * (4.0f / 4.0f) * ((8.0f / 4.0f) * (1 + 2 + 3 + 4));

            check("a staircase encloses the volume its loop is supposed to produce",
                Near(SignedVolume(stairs), expectedStairs, 1e-2f),
                $"{SignedVolume(stairs):F3} vs {expectedStairs:F3}");

            // Not "watertight": the staircase is a stack of slabs and therefore has internal faces where two slabs
            // meet (see the builder's remarks). What has to hold is that nothing is wound against its own normals,
            // because that is what a renderer culls by.
            check("a staircase has no triangle wound against its normals",
                WindingAgrees(stairs), WindingReport(stairs));

            check("a staircase has no degenerate triangle",
                NoDegenerateTriangles(stairs), DegenerateReport(stairs));

            var stairsBounds = PCGMeshBuilder.Bounds(stairs);

            check("a staircase fills exactly the extents it was given",
                Near(stairsBounds.Size.X, 2.0f) && Near(stairsBounds.Size.Y, 8.0f) && Near(stairsBounds.Size.Z, 4.0f),
                stairsBounds.Size.ToString());

            // One step is a box, and its volume has to match the single-step form of the same formula.
            var single = PCGMeshBuilder.Stairs(new Vector3(2.0f, 8.0f, 4.0f), 1);

            check("a one-step staircase is a single slab of the full rise",
                Near(SignedVolume(single), 2.0f * 4.0f * 8.0f, 1e-2f), SignedVolume(single).ToString("F3"));

            // A cylinder: the volume of an inscribed n-gon, which differs from pi r^2 h by the polygon's area error -
            // 0.16% at sixty-four segments, so the tolerance is what separates "a real cylinder" from "a prism".
            var cylinder = PCGMeshBuilder.Cylinder(2.0f, 5.0f, 64);

            float exact = Mathf.Pi * 4.0f * 5.0f;

            check("a sixty-four sided cylinder is within a fifth of a percent of pi r squared h",
                Math.Abs(SignedVolume(cylinder) - exact) / exact < 0.002f,
                $"{SignedVolume(cylinder):F3} vs {exact:F3}");

            // 楔形（斜屋顶）：村庄/城市的房子需要**斜**面，而盒子里切不出斜屋顶。
            // 三条各管一件独立的事：有界（顶点/面数）、有内容且方向对（体积 = 底 × 高 / 2 且为正）、
            // 闭合且绕序自洽（`EdgeReport` 的 unbalanced 就是"有向边没两两相反"那块，正是绕序与法线一致的判据）。
            var wedge = PCGMeshBuilder.Wedge(new Vector3(8.0f, 6.0f, 10.0f));
            var (wedgeUnbalanced, wedgeEdges) = EdgeReport(wedge);
            var wedgeBounds = PCGMeshBuilder.Bounds(wedge);

            check("楔形：18 个顶点、8 片三角形（硬边按面拆开，与盒子同一个理由）",
                wedge.VertexCount == 18 && wedge.TriangleCount == 8, wedge.ToString());

            check("楔形：有向体积 = 开间 × 进深 × 脊高 / 2，且为正（朝外）",
                Near(SignedVolume(wedge), 8.0f * 10.0f * 6.0f * 0.5f, 1e-3f), SignedVolume(wedge).ToString("F3"));

            // 12 = 9 条实体棱（6 个角、5 个面，欧拉核对得上）+ 3 片扇形对角线（底面与两块斜坡各一条）。
            // `unbalanced == 0` 才是"水密且绕序自洽"那半；这条总数挡住的是"少发了一片面"。
            check("楔形：水密且绕序自洽（每条边恰好两个三角形），总棱数 12",
                wedgeUnbalanced == 0 && wedgeEdges == 12, $"{wedgeUnbalanced} / {wedgeEdges}");

            check("楔形：包围盒尺寸就是写下的尺寸（尺寸语义与盒子一致，不是半径）",
                Near(wedgeBounds.Size.X, 8.0f) && Near(wedgeBounds.Size.Y, 6.0f) &&
                Near(wedgeBounds.Size.Z, 10.0f), wedgeBounds.Size.ToString());

            // 反向对照：负尺寸必须与取绝对值**同形**而不是把网格翻面——
            // 没有这条，"取绝对值"这一步写成"照原样用"也能过前面四条里的体积那条。
            var wedgeNegative = PCGMeshBuilder.Wedge(new Vector3(-8.0f, -6.0f, -10.0f));
            var (negativeUnbalanced, _) = EdgeReport(wedgeNegative);

            check("楔形：负尺寸与正尺寸同形（体积相同、照样水密朝外）",
                Near(SignedVolume(wedgeNegative), SignedVolume(wedge), 1e-3f) && negativeUnbalanced == 0,
                SignedVolume(wedgeNegative).ToString("F3"));

            // 退化输入不许产出自相矛盾的网格：脊高为零时它塌成一片平四边形，
            // 体积必须为零而不是留下两片重合的斜面上下一对。
            var wedgeFlat = PCGMeshBuilder.Wedge(new Vector3(8.0f, 0.0f, 10.0f));

            check("楔形：脊高为零时塌成一片（体积为零、不 NaN）",
                Near(SignedVolume(wedgeFlat), 0.0f, 1e-4f) && NoNaN(wedgeFlat),
                SignedVolume(wedgeFlat).ToString("F3"));

            // 房屋形（墙 + 屋顶合成一个封闭实体）：描述里 `geometry house` 就是它，不需要外部模型资产。
            // 体积是解析可推的：墙 = X·Z·(2Y/3)，屋顶楔形 = X·Z·(Y/3)/2 ⇒ 合计 (5/6)·X·Y·Z。
            var house = PCGMeshBuilder.House(new Vector3(12.0f, 9.0f, 10.0f));
            var (houseUnbalanced, houseEdges) = EdgeReport(house);
            var houseBounds = PCGMeshBuilder.Bounds(house);

            check("房屋：30 个顶点、16 片三角形（5 个四边形 + 2 个五边形山尖，按面拆开）",
                house.VertexCount == 30 && house.TriangleCount == 16, house.ToString());

            check("房屋：有向体积 = (5/6) × 开间 × 总高 × 进深，且为正（朝外）",
                Near(SignedVolume(house), (5.0f / 6.0f) * 12.0f * 9.0f * 10.0f, 1e-2f),
                SignedVolume(house).ToString("F3"));

            // 棱数按欧拉核对（第一版我从参数推成 28，实测 24 ⇒ 我数错了角点）：
            // 10 个真实角（底 4 + 檐口 4 + 屋脊 2）、7 个面 ⇒ 实体棱 15（10 + 7 - 2）；
            // 扇形拆面另加 5 个四边形各一条 + 2 个五边形各两条 = 9 条对角线 ⇒ 15 + 9 = 24。
            check("房屋：水密且绕序自洽，棱总数 24（15 实体 + 9 扇形对角线）",
                houseUnbalanced == 0 && houseEdges == 24, $"{houseUnbalanced} / {houseEdges}");

            check("房屋：包围盒尺寸就是写下的尺寸（Y 是**总高**，含屋顶）",
                Near(houseBounds.Size.X, 12.0f) && Near(houseBounds.Size.Y, 9.0f) &&
                Near(houseBounds.Size.Z, 10.0f), houseBounds.Size.ToString());

            // 檐口必须在总高的下三分之二之上：这条挡住的是"把比例写成对半"或"屋顶坐在地上"这类
            // 看起来仍然水密、体积也仍然对得上一半的错法。数的是**不同高度上的顶点条数**
            // （底 4、檐口 4、屋脊 2 —— 按面拆开前是这些，拆开后每个角按面重复，所以按位置去重再数）。
            var levels = new SortedDictionary<int, int>();

            foreach (var v in house.Vertices)
            {
                int bucket = (int)System.Math.Round(v.Y * 10.0f);

                levels.TryGetValue(bucket, out int seen);
                levels[bucket] = seen + 1;
            }

            check("房屋：三层高度（底 / 檐口 / 屋脊）都有顶点，檐口正好在总高的三分之一处",
                levels.Count == 3 && levels.ContainsKey(-45) && levels.ContainsKey(15) && levels.ContainsKey(45),
                string.Join(" ", System.Linq.Enumerable.Select(levels, p => $"{p.Key / 10.0f}:{p.Value}")));

            // 总高为零时它**塌成一张平面**：所有角点压到同一高度，实体棱不再两两配对——
            // 这是几何事实而不是缺陷（`Box` 给零尺寸同样不封闭），所以这里只要求"不产出 NaN、体积为零"，
            // 并把"此时它不是一个封闭实体"如实写进判据，不假装多了一条契约。
            var houseFlat = PCGMeshBuilder.House(new Vector3(12.0f, 0.0f, 10.0f));
            var (flatHouseUnbalanced, _) = EdgeReport(houseFlat);

            check("房屋：总高为零 ⇒ 塌成一张平面（体积为零、不 NaN；此时不再封闭，这是几何事实）",
                Near(SignedVolume(houseFlat), 0.0f, 1e-4f) && NoNaN(houseFlat) && flatHouseUnbalanced > 0,
                $"{SignedVolume(houseFlat):F3} / 未配对边 {flatHouseUnbalanced}");

            // 树形（树干 + 圆锥树冠，一个封闭实体）：森林此前只能长成浮空的锥。
            // 体积按解析对照：树干是台体（r₁ 底、r₂ 冠底、h = 总高/3），树冠是圆锥（h = 总高 2/3）。
            const int treeSides = 24;
            var tree = PCGMeshBuilder.Tree(new Vector3(10.0f, 30.0f, 10.0f), treeSides);
            var (treeUnbalanced, treeEdges) = EdgeReport(tree);
            var treeBounds = PCGMeshBuilder.Bounds(tree);

            float trunkR = 5.0f * 0.25f;
            float crownR = 5.0f;
            float trunkH = 30.0f / 3.0f;
            float coneH = 30.0f - trunkH;
            float exactTree = (float)(System.Math.PI * trunkH / 3.0 *
                (trunkR * trunkR + trunkR * crownR + crownR * crownR)) +
                (float)(System.Math.PI * crownR * crownR * coneH / 3.0);
            float treeError = System.Math.Abs(SignedVolume(tree) - exactTree) / exactTree;

            check("树：顶点 10n-6、三角形 4n-2（n=24 ⇒ 234 / 94）",
                tree.VertexCount == 10 * treeSides - 6 && tree.TriangleCount == 4 * treeSides - 2,
                $"{tree.VertexCount} / {tree.TriangleCount}");

            check("树：有向体积与解析（台体树干 + 圆锥树冠）差在 2% 内，且为正（朝外）",
                treeError < 0.02f && SignedVolume(tree) > 0.0f,
                $"{SignedVolume(tree):F1} vs {exactTree:F1}（相对误差 {treeError * 100.0f:F2}%）");

            check("树：水密且绕序自洽（树冠坐在树干顶上，没有内部面）",
                treeUnbalanced == 0, $"{treeUnbalanced} / 棱 {treeEdges}");

            check("树：包围盒 = 树冠直径 × 总高 × 树冠直径（X/Z 取较小者当直径）",
                Near(treeBounds.Size.X, 10.0f, 1e-3f) && Near(treeBounds.Size.Y, 30.0f, 1e-3f) &&
                Near(treeBounds.Size.Z, 10.0f, 1e-3f), treeBounds.Size.ToString());

            // 三个高度层必须都在：底圈、冠底（树冠坐在树干顶上的那一圈）、顶点。
            // 这条挡住的是"冠底错设在总高的中点"那类写法——它仍然水密、体积也仍然落在 2% 里。
            var treeLevels = new SortedDictionary<int, int>();

            foreach (var v in tree.Vertices)
            {
                int bucket = (int)System.Math.Round(v.Y * 10.0f);

                treeLevels.TryGetValue(bucket, out int seen);
                treeLevels[bucket] = seen + 1;
            }

            check("树：三层高度（底 / 冠底 / 顶点）都在，冠底正好在总高的下三分之一处",
                treeLevels.Count == 3 && treeLevels.ContainsKey(-150) &&
                treeLevels.ContainsKey(-50) && treeLevels.ContainsKey(150),
                string.Join(" ", System.Linq.Enumerable.Select(treeLevels, p => $"{p.Key / 10.0f}:{p.Value}")));

            // 边数下限是 3（少于 3 就没有体），这一条挡住"段数被直接拿去用、造出一条退化的两棱柱"。
            var thinTree = PCGMeshBuilder.Tree(new Vector3(6.0f, 12.0f, 6.0f), 2);
            var (thinUnbalanced, _) = EdgeReport(thinTree);

            check("树：段数 2 被抬到 3（最低能成体的边数），产物仍然水密",
                thinTree.TriangleCount == 4 * 3 - 2 && thinUnbalanced == 0,
                $"{thinTree.TriangleCount} 片 / 未配对边 {thinUnbalanced}");

            check("a capped cylinder is closed and outward wound",
                EdgeReport(cylinder).unbalanced == 0 && WindingAgrees(cylinder) &&
                NoDegenerateTriangles(cylinder),
                EdgeReport(cylinder).ToString());

            // The wall's normals must stay in the XZ plane: a wall is vertical, and a normal with a Y component
            // would band the shading around the circumference.
            // Only the wall ring is checked: the two cap centres legitimately carry straight up and down normals,
            // and asserting over every vertex made this fail on a perfectly good cylinder.
            int wallVertices = (64 + 1) * 2;
            bool wallsHorizontal = true;

            for (int i = 0; i < wallVertices && wallsHorizontal; i++)
                wallsHorizontal = Math.Abs(cylinder.Normals[i].Y) < 1e-4f;

            check("a cylinder's wall normals have no vertical component", wallsHorizontal);

            var uncapped = PCGMeshBuilder.Cylinder(2.0f, 5.0f, 32, capped: false);

            // ...and the twin: without caps it is genuinely open, which is what makes the closed check above mean
            // something rather than passing for both.
            check("an uncapped cylinder is open, so the closed check above is not vacuous",
                EdgeReport(uncapped).unbalanced > 0 && uncapped.TriangleCount == 32 * 2,
                $"{EdgeReport(uncapped).unbalanced} unbalanced edge(s)");
        }

        private static void OpenSurfaceTests(CheckDelegate check)
        {
            var grid = PCGMeshBuilder.Grid(new Vector2(10.0f, 20.0f), 4);

            check("a four by four grid has twenty-five vertices and thirty-two triangles",
                grid.VertexCount == 25 && grid.TriangleCount == 32, grid.ToString());

            check("a grid covers exactly the area it was given",
                Near(SurfaceArea(grid), 200.0f, 1e-3f), SurfaceArea(grid).ToString("F3"));

            check("every grid normal points up",
                AllDirections(grid.Normals, new Vector3(0, 1, 0)), First(grid.Normals).ToString());

            check("a grid is open by design and says so through its edges",
                EdgeReport(grid).unbalanced > 0, EdgeReport(grid).ToString());

            var coarse = PCGMeshBuilder.Grid(new Vector2(10.0f, 20.0f), 1);

            check("a coarse grid covers the same area with a quarter of the triangles",
                coarse.TriangleCount == 2 && Near(SurfaceArea(coarse), 200.0f, 1e-3f), coarse.ToString());

            check("a grid subdivision of zero is treated as one cell",
                PCGMeshBuilder.Grid(new Vector2(1.0f, 1.0f), 0).TriangleCount == 2);
        }

        private static void TransformTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            var moved = PCGMeshBuilder.Transform(
                box, new Vector3(10.0f, 0.0f, 0.0f), Quaternion.Identity, new Vector3(2.0f, 2.0f, 2.0f));

            // Scaling by two in every axis multiplies the volume by eight: the check that the transform reached the
            // vertices rather than only the bounds.
            check("scaling a box scales its volume by the cube of the factor",
                Near(SignedVolume(moved), 8.0f * 8.0f, 1e-2f), SignedVolume(moved).ToString("F3"));

            var movedBounds = PCGMeshBuilder.Bounds(moved);

            check("a transformed mesh lands where it was placed",
                Near(movedBounds.Center.X, 10.0f) && Near(movedBounds.Size.X, 4.0f),
                movedBounds.ToString());

            // Normals are recomputed rather than carried over, so they stay unit length and keep agreeing with the
            // winding even under a non-uniform scale - the case a rotation-only normal transform gets wrong.
            var squashed = PCGMeshBuilder.Transform(
                box, Vector3.Zero, Quaternion.Identity, new Vector3(4.0f, 1.0f, 1.0f));

            check("a non-uniformly scaled mesh still has unit normals that agree with its winding",
                AllUnitLength(squashed.Normals) && WindingAgrees(squashed), WindingReport(squashed));

            var turned = PCGMeshBuilder.Transform(
                box, Vector3.Zero, PCGPointTransform.EulerDegrees(new Vector3(0.0f, 90.0f, 0.0f)), Vector3.One);

            var turnedBounds = PCGMeshBuilder.Bounds(turned);

            // A cube turned a quarter turn is the same size, but a rectangular box would swap two extents: using a
            // non-cubic box here makes the check able to fail.
            var slab = PCGMeshBuilder.Box(new Vector3(4.0f, 2.0f, 1.0f));

            var slabTurned = PCGMeshBuilder.Transform(
                slab, Vector3.Zero, PCGPointTransform.EulerDegrees(new Vector3(0.0f, 90.0f, 0.0f)), Vector3.One);

            var slabBounds = PCGMeshBuilder.Bounds(slabTurned);

            check("turning a box a quarter turn swaps the extents across the turn",
                Near(turnedBounds.Size.X, 2.0f) &&
                Near(slabBounds.Size.X, 1.0f) && Near(slabBounds.Size.Z, 4.0f),
                slabBounds.Size.ToString());

            check("the original mesh is left alone by a transform", SameArrays(box, PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f))));
        }

        private static void MergeTests(CheckDelegate check)
        {
            var two = PCGMeshBuilder.Merge(new[]
            {
                PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f)),
                PCGMeshBuilder.Offset(PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f)), new Vector3(100.0f, 0, 0)),
            });

            check("merging adds the vertices and triangles up",
                two.VertexCount == 48 && two.TriangleCount == 24, two.ToString());

            check("a merged pair of disjoint boxes encloses the sum of their volumes",
                Near(SignedVolume(two), 16.0f, 1e-2f), SignedVolume(two).ToString("F3"));

            check("a merged mesh keeps its winding agreement across both parts", WindingAgrees(two));

            // Indices are re-based, so the second part's triangles must not point into the first part's vertices -
            // which is exactly what a merge that forgot the offset would do, and what the volume above would catch
            // only by accident.
            bool inRange = true;

            for (int i = 0; i < two.Triangles.Length && inRange; i++)
                inRange = two.Triangles[i] >= 0 && two.Triangles[i] < two.VertexCount;

            check("every merged index points inside the merged buffer", inRange);

            check("merging nothing gives an empty mesh",
                PCGMeshBuilder.Merge(Array.Empty<PCGMesh>()).TriangleCount == 0 &&
                PCGMeshBuilder.Merge(null).TriangleCount == 0);
        }

        // ---- the validator: written from the properties, not from the builders ---------------------------------

        /// <summary>Enclosed volume by the divergence theorem: the sum over outward-wound triangles of a.bxc / 6.</summary>
        private static float SignedVolume(PCGMesh mesh)
        {
            double total = 0.0;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                total += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }

            return (float)total;
        }

        private static float SurfaceArea(PCGMesh mesh)
        {
            float total = 0.0f;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                total += Vector3.Cross(b - a, c - a).Length * 0.5f;
            }

            return total;
        }

        /// <summary>
        /// Counts edges, keyed by position, that are not shared by exactly two triangles.
        /// </summary>
        /// <remarks>
        /// Keyed by position rather than by index on purpose: the builders duplicate vertices at hard edges, so an
        /// index-keyed test would report an open mesh for a perfectly closed box. Positions are rounded to a
        /// thousandth so two coincident vertices count as the same corner.
        /// </remarks>
        private static (int unbalanced, int edges) EdgeReport(PCGMesh mesh)
        {
            var counts = new Dictionary<(long, long, long, long, long, long), int>();

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = Key(mesh.Vertices[mesh.Triangles[t]]);
                var b = Key(mesh.Vertices[mesh.Triangles[t + 1]]);
                var c = Key(mesh.Vertices[mesh.Triangles[t + 2]]);

                Bump(counts, a, b);
                Bump(counts, b, c);
                Bump(counts, c, a);
            }

            int unbalanced = 0;

            foreach (var pair in counts)
            {
                if (pair.Value != 2)
                    unbalanced++;
            }

            return (unbalanced, counts.Count);
        }

        private static void Bump(Dictionary<(long, long, long, long, long, long), int> counts,
            (long, long, long) a, (long, long, long) b)
        {
            // Sorted so the two directions of the same edge land on the same key.
            var key = Compare(a, b) <= 0
                ? (a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3)
                : (b.Item1, b.Item2, b.Item3, a.Item1, a.Item2, a.Item3);

            counts.TryGetValue(key, out int seen);
            counts[key] = seen + 1;
        }

        private static int Compare((long, long, long) a, (long, long, long) b)
        {
            if (a.Item1 != b.Item1) return a.Item1.CompareTo(b.Item1);
            if (a.Item2 != b.Item2) return a.Item2.CompareTo(b.Item2);
            return a.Item3.CompareTo(b.Item3);
        }

        private static (long, long, long) Key(Vector3 v) =>
            ((long)Math.Round(v.X * 1000.0f), (long)Math.Round(v.Y * 1000.0f), (long)Math.Round(v.Z * 1000.0f));

        /// <summary>True when every triangle's geometric normal agrees with the normals its vertices carry.</summary>
        private static bool WindingAgrees(PCGMesh mesh)
        {
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int ia = mesh.Triangles[t];
                int ib = mesh.Triangles[t + 1];
                int ic = mesh.Triangles[t + 2];

                var face = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia], mesh.Vertices[ic] - mesh.Vertices[ia]);

                if (face.Length < 1e-8f)
                    continue;

                var stored = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];

                if (Vector3.Dot(face, stored) <= 0.0f)
                    return false;
            }

            return true;
        }

        private static string WindingReport(PCGMesh mesh)
        {
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                int ia = mesh.Triangles[t];
                int ib = mesh.Triangles[t + 1];
                int ic = mesh.Triangles[t + 2];

                var face = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia], mesh.Vertices[ic] - mesh.Vertices[ia]);
                var stored = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];

                if (face.Length >= 1e-8f && Vector3.Dot(face, stored) <= 0.0f)
                    return $"triangle {t / 3}: face={face} stored={stored}";
            }

            return "all triangles agree";
        }

        /// <summary>True when no triangle has two coincident corners, which would shade as garbage.</summary>
        private static bool NoDegenerateTriangles(PCGMesh mesh)
        {
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                if (Vector3.Cross(b - a, c - a).Length < 1e-8f)
                    return false;
            }

            return true;
        }

        private static string DegenerateReport(PCGMesh mesh)
        {
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                if (Vector3.Cross(b - a, c - a).Length < 1e-8f)
                    return $"triangle {t / 3} of {mesh.TriangleCount}";
            }

            return "none";
        }

        private static bool NoNaN(PCGMesh mesh)
        {
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                if (float.IsNaN(mesh.Vertices[i].X) || float.IsNaN(mesh.Vertices[i].Y) || float.IsNaN(mesh.Vertices[i].Z))
                    return false;
            }

            for (int i = 0; i < mesh.Normals.Length; i++)
            {
                if (float.IsNaN(mesh.Normals[i].X) || float.IsNaN(mesh.Normals[i].Y) || float.IsNaN(mesh.Normals[i].Z))
                    return false;
            }

            return true;
        }

        private static bool SameArrays(PCGMesh a, PCGMesh b)
        {
            if (a.VertexCount != b.VertexCount || a.Triangles.Length != b.Triangles.Length)
                return false;

            for (int i = 0; i < a.Vertices.Length; i++)
            {
                if (!Near(a.Vertices[i].X, b.Vertices[i].X) || !Near(a.Vertices[i].Y, b.Vertices[i].Y) ||
                    !Near(a.Vertices[i].Z, b.Vertices[i].Z))
                    return false;
            }

            for (int i = 0; i < a.Triangles.Length; i++)
            {
                if (a.Triangles[i] != b.Triangles[i])
                    return false;
            }

            return true;
        }

        private static bool AllUnitAxes(Vector3[] normals)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                var n = normals[i];
                float axis = Math.Abs(n.X) + Math.Abs(n.Y) + Math.Abs(n.Z);

                if (Math.Abs(axis - 1.0f) > 1e-4f)
                    return false;
            }

            return true;
        }

        private static bool AllUnitLength(Vector3[] normals)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                if (Math.Abs(normals[i].Length - 1.0f) > 1e-3f)
                    return false;
            }

            return true;
        }

        private static bool AllDirections(Vector3[] normals, Vector3 expected)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                if (Vector3.Dot(normals[i], expected) < 0.999f)
                    return false;
            }

            return true;
        }

        private static bool Contains(List<Vector3> values, Vector3 value)
        {
            foreach (var existing in values)
            {
                if (Near(existing.X, value.X) && Near(existing.Y, value.Y) && Near(existing.Z, value.Z))
                    return true;
            }

            return false;
        }

        private static Vector3 First(Vector3[] values) => values.Length > 0 ? values[0] : Vector3.Zero;

        private static bool Near(float a, float b, float tolerance = 1e-4f) => Math.Abs(a - b) <= tolerance;
    }
}

// PCG 插件 - 网格平面切割的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.44）
//
// 这一层的四条判据就是它的全部承诺，所以每一条都单独验：① 结果水密（逐边配对）② 体积守恒
// （对解析可算的形状手算）③ 没跨平面的三角形一个顶点都不重算 ④ 判不出来（开网格、多处截断）
// 必须报错而不是产出一个"看着对、其实自交"的网格。校验器与实现**无关**：体积走散度定理、
// 水密走按位置配对的边表，都不碰 PCGMeshCut 自己的任何中间量。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class MeshCutTests
    {
        internal static void Run(CheckDelegate check)
        {
            VolumeTests(check);
            UntouchedTests(check);
            DegenerateTests(check);
            RefusalTests(check);
            DeterminismTests(check);
            NodeTests(check);
        }

        /// <summary>
        /// 节点那一层：默认切掉法线那一侧、预览不动它、切不动的**原样保留但把原因报出来**。
        /// </summary>
        private static void NodeTests(CheckDelegate check)
        {
            var input = PCGData.Create(2);
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            for (int i = 0; i < 2; i++)
            {
                input.Points[i] = PCGPoint.Create(new Vector3(i * 10.0f, 0, 0), 1.0f, i);
                input.Points[i].UpdateBounds(new Vector3(1, 1, 1));
            }

            input.Meshes = new[] { box, box };

            var node = MakeCut(new Vector3(0, 0, 0), new Vector3(0, 1, 0), invert: false);
            var cut = node.Execute(Context(dryRun: false), new[] { input });

            check("节点：每个网格都被切了（体积减半）",
                cut != null && cut.Meshes != null && cut.Meshes.Length == 2 &&
                Near(Volume(cut.Meshes[0]), 4.0f, 1e-3f) && Near(Volume(cut.Meshes[1]), 4.0f, 1e-3f),
                node.LastError ?? $"{Volume(cut?.Meshes?[0]):F3}");

            check("节点：切好的网格仍水密", cut?.Meshes != null && IsWatertight(cut.Meshes[0]));

            check("节点：输入那一份**没有被就地改掉**（切出来的是新数据）",
                Near(Volume(box), 8.0f, 1e-3f), Volume(box).ToString("F3"));

            // 预览：**照样切**——切割是纯数据变换、不碰场景，而"预览显示没削、烘焙显示削过的"是同一张图
            // 两种样子（第一版在这里早退过，被演示那条断言抓了出来）。
            var preview = node.Execute(Context(dryRun: true), new[] { input });

            check("节点：预览运行也切（与烘焙同一份数据，DryRun 只挡建 Actor 那类）",
                preview != null && preview.Meshes != null &&
                Near(Volume(preview.Meshes[0]), Volume(cut.Meshes[0]), 1e-4f),
                $"预览 {Volume(preview?.Meshes?[0]):F3} vs 烘焙 {Volume(cut?.Meshes?[0]):F3}");

            // 切不动的那一种（开放网格）：**原样保留**但必须说出原因。
            var open = PCGData.Create(1);
            open.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            open.Points[0].UpdateBounds(new Vector3(1, 1, 1));
            open.Meshes = new[] { PCGMeshBuilder.Grid(new Vector2(4.0f, 4.0f), 2) };

            var refusing = MakeCut(new Vector3(1, 0, 0), new Vector3(1, 0, 0), invert: false);
            var kept = refusing.Execute(Context(dryRun: false), new[] { open });

            check("节点：切不动时原样保留，并把原因写进 LastError（不是悄悄留一个没切的）",
                kept != null && kept.Meshes != null && kept.Meshes[0].TriangleCount > 0 &&
                !string.IsNullOrEmpty(refusing.LastError) && refusing.LastError.Contains("could not be cut"),
                refusing.LastError ?? "没有报原因");

            // 没有几何的输入：透传 + 说明，与其余几何节点同一条规则。
            var bare = PCGData.Create(2);
            var bareNode = MakeCut(Vector3.Zero, Vector3.UnitY, invert: false);
            var passed = bareNode.Execute(Context(dryRun: false), new[] { bare });

            check("节点：输入没有几何时透传并说明原因",
                passed != null && passed.Count == 2 && !string.IsNullOrEmpty(bareNode.LastError),
                bareNode.LastError ?? $"{passed?.Count} point(s)");

            check("节点：参数个数与槽位表一致（三位：刀口点/法线/取反）",
                PCG.Nodes.PCGCutNode.ParameterCount == 3);
        }

        private static PCG.Nodes.PCGCutNode MakeCut(Vector3 point, Vector3 normal, bool invert) =>
            new PCG.Nodes.PCGCutNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCG.Nodes.PCGCutNode.Id, new object[] { point, normal, invert }, 0));

        private static PCGExecutionContext Context(bool dryRun) => new PCGExecutionContext
        {
            Seed = 20261010,
            DryRun = dryRun,
            GenerationBounds = new BoundingBox(new Vector3(-500, -500, -500), new Vector3(500, 500, 500)),
        };

        /// <summary>
        /// 判据①②：切一半之后体积正好是一半，且结果仍然水密。
        /// </summary>
        private static void VolumeTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            check("夹具自证：立方体是 8 的体积、且本来水密",
                Near(Volume(box), 8.0f, 1e-3f) && IsWatertight(box),
                $"volume {Volume(box):F3}, watertight {IsWatertight(box)}");

            // 水平切一半：保留 y ≥ 0 的那半，体积必须正好是 4。
            var cut = Cut(box, new Vector3(0, 0, 0), new Vector3(0, 1, 0), keepPositive: true, out string error);

            check("水平切一半：切得出来", cut != null, error ?? "ok");

            if (cut == null)
                return;

            check("判据② 体积守恒：切掉一半之后正好剩一半（4.0）",
                Near(Volume(cut), 4.0f, 1e-3f), Volume(cut).ToString("F3"));

            check("判据① 水密：切口的盖真正封上了（每条边恰好被两个面共享）",
                IsWatertight(cut), $"volume {Volume(cut):F3}, {cut.TriangleCount} triangle(s)");

            // 斜切一半：平面**过中心**且不穿过任何顶点（法线取一个不整齐的方向，避开"顶点正好在平面上"
            // 那个更难的分类）。对中心对称的立方体，过中心的任何平面都把体积平分，所以还是 4.0——
            // 这一条压的是斜截面的封盖（截面是个斜四边形，不再是轴对齐的）。
            var diagonal = Cut(box, Vector3.Zero, new Vector3(0.3f, 1.0f, 0.2f), keepPositive: true, out string diagonalError);

            check("斜切一半：同样是一半体积且水密",
                diagonal != null && Near(Volume(diagonal), 4.0f, 1e-3f) && IsWatertight(diagonal),
                diagonalError ?? $"volume {Volume(diagonal):F3}, watertight {IsWatertight(diagonal)}");

            // 换个方向保留（负侧）：体积还是一半，而**切口朝向必须跟着翻**——否则背剔会把这个盖吃掉。
            var otherSide = Cut(box, Vector3.Zero, new Vector3(0, 1, 0), keepPositive: false, out string otherError);

            check("保留另一侧：体积仍是一半、水密，且切口法线朝向跟着翻（保留下半 ⇒ 切面朝上）",
                otherSide != null && Near(Volume(otherSide), 4.0f, 1e-3f) && IsWatertight(otherSide) &&
                CapFacesAlong(otherSide, expectUp: true),
                otherError ?? $"watertight {IsWatertight(otherSide)}");

            // 圆柱（带端盖）横切一半：截面是圆，扇形三角化要能封住。
            var cylinder = PCGMeshBuilder.Cylinder(1.0f, 4.0f, 12, capped: true);
            var cutCylinder = Cut(cylinder, Vector3.Zero, new Vector3(0, 1, 0), keepPositive: true, out string cylinderError);

            check("圆柱横切一半：体积约为原体积的一半且水密（截面是圆，扇形封盖要成立）",
                cutCylinder != null && IsWatertight(cutCylinder) &&
                Near(Volume(cutCylinder), Volume(cylinder) * 0.5f, Volume(cylinder) * 0.01f),
                cylinderError ?? $"{Volume(cutCylinder):F3} of {Volume(cylinder):F3}, watertight {IsWatertight(cutCylinder)}");
        }

        /// <summary>
        /// 判据③：一刀没碰到的三角形**一个顶点都不重算**（原样搬过去）。
        /// </summary>
        private static void UntouchedTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            // 平面在网格**下方**：整块都在保留侧，等于什么都没切。
            var kept = Cut(box, new Vector3(0, -5, 0), new Vector3(0, 1, 0), keepPositive: true, out string keptError);

            check("判据③ 平面在网格下方时：三角形一个不少、体积不变",
                kept != null && kept.TriangleCount == box.TriangleCount && Near(Volume(kept), Volume(box), 1e-4f),
                keptError ?? $"{kept?.TriangleCount} of {box.TriangleCount}, volume {Volume(kept):F3}");

            // 顶点数**会**变少，而且这是对的：装配时按位置焊接，立方体那 24 个重复角点收敛成 8 个。
            // 所以"原样保留"要按**三角形的位置**去验，不是按顶点下标。
            if (kept != null && kept.TriangleCount == box.TriangleCount)
            {
                bool identical = true;

                for (int t = 0; t + 2 < box.Triangles.Length && identical; t += 3)
                {
                    var original = (box.Vertices[box.Triangles[t]],
                                    box.Vertices[box.Triangles[t + 1]],
                                    box.Vertices[box.Triangles[t + 2]]);
                    var after = (kept.Vertices[kept.Triangles[t]],
                                 kept.Vertices[kept.Triangles[t + 1]],
                                 kept.Vertices[kept.Triangles[t + 2]]);

                    identical = SamePosition(original.Item1, after.Item1) &&
                                SamePosition(original.Item2, after.Item2) &&
                                SamePosition(original.Item3, after.Item3);
                }

                check("判据③ 那些三角形是**逐顶点原样**的（没被重算成近似值）", identical);
            }

            // 平面在网格**上方**：整块都被切掉，结果是空的（不是报错、更不是原样留着）。
            var dropped = Cut(box, new Vector3(0, 5, 0), new Vector3(0, 1, 0), keepPositive: true, out string droppedError);

            check("平面在网格上方时：结果为空且不报错（这一刀把整块都切掉了）",
                dropped != null && dropped.TriangleCount == 0, droppedError ?? $"{dropped?.TriangleCount} triangle(s)");
        }

        /// <summary>退化输入：零法线要报错；贴着平面的刀口（顶点正好在平面上）不能产出 NaN。</summary>
        private static void DegenerateTests(CheckDelegate check)
        {
            var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));

            bool ok = PCGMeshCut.TryCutByPlane(box, Vector3.Zero, Vector3.Zero, true, out _, out string error);

            check("零长度法线：报错而不是除零",
                !ok && !string.IsNullOrEmpty(error) && error.Contains("direction"), error);

            // 顶点正好在平面上（切在 y=±1 上）：不许出现 NaN、不许抛。
            var onVertex = Cut(box, new Vector3(0, 1, 0), new Vector3(0, 1, 0), keepPositive: true, out string onVertexError);

            // 顶点正好在平面上（切在 y=+1，即顶面）：这是**边界那一档**。原来这里只要求"没有 NaN"——那是
            // 下界断言（一个开口的网格照样能过）。2026-10-11 修完那个"刀口压在一圈顶点上"的缺陷之后按契约收严：
            // 要么明确拒绝、要么给出一个**封闭**实体（全被切掉时的空网格也算过）。
            check("刀口正好压在顶面上：要么明确拒绝，要么给出封闭实体（不再只查 NaN）",
                onVertexError != null || onVertex == null || onVertex.TriangleCount == 0 ||
                (IsWatertight(onVertex) && !HasNaN(onVertex)),
                onVertexError ?? $"{onVertex?.TriangleCount} triangle(s)、水密 " +
                                 $"{(onVertex != null && onVertex.TriangleCount > 0 ? IsWatertight(onVertex).ToString() : "n/a")}");

            // 轴向切刀（文档 §7.6）：曾经**看轴向**——同一颗过中心的立方体，法线 ±Y 切得动、±X 被拒
            // （`two segments leave the same point`）。根因不在三角化，而在接环那一步**要求线段连成有向环**：
            // 线段方向来自各三角形自己的走序，而刀口穿过的那些面朝向各不相同，方向在拐角处可能不齐
            // （实测角点 (0,-1,-1) 上两条线段都朝外）。改成"无向接环 + 末尾叉积定向"之后两个轴向都通了。
            // 这两条断言就是那次的回归钉子：任何一个轴向退回被拒，这里立刻红。
            for (int i = 0; i < 2; i++)
            {
                var axis = i == 0 ? Vector3.UnitX : Vector3.UnitY;
                var cube = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));
                bool axisOk = PCGMeshCut.TryCutByPlane(cube, Vector3.Zero, axis, false, out var sliced, out string axisError);

                check("轴向切刀（法线 " + (i == 0 ? "X" : "Y") + "）：切得动且水密",
                    axisOk && IsWatertight(sliced),
                    axisOk ? $"{sliced.TriangleCount} 面" : axisError);
            }

            // 诊断（文档 §7.6 的入口）：把刀口依次落在"过中心(法线 Y)"、"过中心(法线 X)"、"正好过一条**边**"、
            // "正好过一个**顶点**"，每一刀断言**契约**（要么切成功且水密、要么明确拒绝），并把**分档计数**打进报告。
            // 剩下那几处（多刀裁完绕序会翻、棱柱侧面相切被拒、§7.5 内向盖朝向）都怀疑同一个根——
            // "擦过边/顶点时落进了第三档"；而"第三档到底收了多少"只能靠这组数字看，不能靠读代码推。
            var probes = new[]
            {
                ("过中心 / 法线 Y", Vector3.Zero, Vector3.UnitY),
                ("过中心 / 法线 X", Vector3.Zero, Vector3.UnitX),
                ("正好过一条边", new Vector3(1.0f, 1.0f, 0.0f), Vector3.Normalize(new Vector3(1.0f, -1.0f, 0.0f))),
                ("正好过一个顶点", new Vector3(1.0f, 1.0f, 1.0f), Vector3.Normalize(new Vector3(1.0f, 1.0f, 1.0f))),
            };

            foreach (var probe in probes)
            {
                var probeCube = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));
                bool probeOk = PCGMeshCut.TryCutByPlane(probeCube, probe.Item2, probe.Item3, false,
                    out var probeCut, out string probeError, out var probeStats);

                bool contract = !probeOk ||
                                (probeCut != null && probeCut.TriangleCount > 0 && IsWatertight(probeCut));

                check($"刀口{probe.Item1}：要么切成功且水密、要么明确拒绝｜{probeStats}",
                    contract,
                    probeOk ? $"{probeCut.TriangleCount} 面、水密 {IsWatertight(probeCut)}；{probeStats}"
                            : $"{probeStats}；被拒：{probeError}");
            }

            // 单刀那几个位置都正常（上面四条），所以"多刀"才是要盯的那一档：把一个 4³ 立方体按 2×6×2 的盒区域
            // **依次**裁六刀（`ClipToConvex` 走的就是这条路），**逐刀**把分档计数与面数打出来。
            // 哪一刀之后面数/水密性不对，问题就在那一刀——比"整条链红/绿"有用得多。
            var walked = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));
            var walkPlanes = new[]
            {
                new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0), new Vector3(0, -1, 0),
                new Vector3(0, 0, 1), new Vector3(0, 0, -1),
            };

            for (int i = 0; i < walkPlanes.Length; i++)
            {
                var normal = walkPlanes[i];
                var point = new Vector3(normal.X * 1.0f, normal.Y * 3.0f, normal.Z * 1.0f);

                bool stepOk = PCGMeshCut.TryCutByPlane(walked, point, normal, false,
                    out var next, out string stepError, out var stepStats);

                if (stepOk && next != null && next.TriangleCount > 0)
                    walked = next;

                check($"多刀第 {i + 1} 刀（法线 {normal}）：成功且水密｜{stepStats}",
                    !stepOk || (next != null && next.TriangleCount > 0 && IsWatertight(next)),
                    stepOk ? $"{next?.TriangleCount} 面、水密 {IsWatertight(next)}；{stepStats}"
                           : $"{stepStats}；被拒：{stepError}");
            }

            // 诊断（给"按体积符号判朝向"那个想法定前提）：**各建造器出来网格的有向体积符号**。
            // 判"盖要不要反过来"时用到的"朝外为正、朝内为负"就是这条前提；它要不是普遍成立，那条路就不成立。
            var builders = new (string Label, PCGMesh Mesh, bool Outward)[]
            {
                ("Box 2³（朝外）", PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f)), true),
                ("Cylinder 封口（朝外）", PCGMeshBuilder.Cylinder(1.0f, 4.0f, 12, capped: true), true),
                ("Tube 朝外", PCGMeshBuilder.Tube(
                    new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 4, 0) }, 1.0f, 8, true, false), true),
                ("Tube 朝内（洞壁）", PCGMeshBuilder.Tube(
                    new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 4, 0) }, 1.0f, 8, true, true), false),
            };

            foreach (var builder in builders)
            {
                float signed = SignedVolumeOf(builder.Mesh);

                check($"诊断：{builder.Label} 的有向体积{(builder.Outward ? "为正" : "为负")}",
                    builder.Outward ? signed > 0.0f : signed < 0.0f,
                    $"{signed:F4}");
            }

            // 边界②的**最小复现**（棱柱那 18 刀太大，改不动）：先切一刀造出"切口上的顶点"，
            // 再用**正好穿过那个顶点**的平面切第二刀——证据指向的就是这个形状（断口落在一条直线段的内部
            // ⇒ 刀口穿过了网格的某个顶点）。两刀的统计并排打出来，就是"过顶点这一档"的那组数字。
            var two = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));
            bool firstOk = PCGMeshCut.TryCutByPlane(two, new Vector3(1, 0, 0), Vector3.UnitX, false,
                out var afterFirst, out string firstError, out var firstStats);

            // 第二刀**无条件**调用（第一刀失败时 afterFirst 是空网格，这一刀会以"没有三角形"失败）——
            // 写成 `firstOk && TryCut(...)` 会让编译器判"out 参数可能没赋值"。
            bool secondOk = PCGMeshCut.TryCutByPlane(afterFirst,
                new Vector3(1.0f, 2.0f, 2.0f), Vector3.Normalize(new Vector3(1.0f, -1.0f, 1.0f)), false,
                out var afterSecond, out string secondError, out var secondStats);

            check($"两刀最小复现：第二刀正好穿过第一刀切口上的顶点（要么成功且水密、要么明确拒绝）｜{firstStats} → {secondStats}",
                firstOk && (!secondOk || (afterSecond != null && afterSecond.TriangleCount > 0 && IsWatertight(afterSecond))),
                !firstOk ? $"第一刀就失败：{firstError}"
                         : secondOk ? $"{afterSecond?.TriangleCount} 面、水密 {IsWatertight(afterSecond)}；{firstStats} → {secondStats}"
                                    : $"{firstStats} → {secondStats}；被拒：{secondError}");

            static float SignedVolumeOf(PCGMesh mesh)
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
        }

        /// <summary>
        /// 判据④：判不出来的情形必须**报错**——开网格接不成环、多处截断接出不止一个环。
        /// </summary>
        private static void RefusalTests(CheckDelegate check)
        {
            // 开放网格（格栅没有底面）：切口接不成闭合环。两处刀口都要避开"正好压在格线上"——
            // 格栅的顶点在 x=−2/0/2 上，拿 x=0 去切等于沿着它自己的一条格线切，一个顶点都不跨。
            var grid = PCGMeshBuilder.Grid(new Vector2(4.0f, 4.0f), 2);
            var cutGrid = Cut(grid, new Vector3(1, 0, 0), new Vector3(1, 0, 0), keepPositive: true, out string gridError);

            check("判据④ 开放网格：拒绝封盖并说明原因，而不是产出一个开着口的'实体'",
                cutGrid == null && !string.IsNullOrEmpty(gridError) &&
                gridError.Contains("close"),
                gridError ?? "没有报错");

            // 台阶是"堆叠板 + 内部面"（设计里写明的取舍），也不该被当成封闭实体切开。
            var stairs = PCGMeshBuilder.Stairs(new Vector3(6.0f, 4.0f, 6.0f), 3);
            var cutStairs = Cut(stairs, Vector3.Zero, new Vector3(0, 1, 0), keepPositive: true, out string stairsError);

            check("判据④ 台阶（按设计不水密）：同样被拒绝而不是产出自交网格",
                cutStairs == null && !string.IsNullOrEmpty(stairsError),
                stairsError ?? "没有报错");
        }

        /// <summary>同样的输入切两次：逐顶点、逐三角形都一样（切割不许依赖任何隐藏状态）。</summary>
        private static void DeterminismTests(CheckDelegate check)
        {
            var cylinder = PCGMeshBuilder.Cylinder(1.5f, 3.0f, 9, capped: true);

            var first = Cut(cylinder, Vector3.Zero, new Vector3(0.3f, 1.0f, -0.2f), keepPositive: true, out _);
            var second = Cut(cylinder, Vector3.Zero, new Vector3(0.3f, 1.0f, -0.2f), keepPositive: true, out _);

            bool identical = first != null && second != null &&
                             first.TriangleCount == second.TriangleCount &&
                             first.VertexCount == second.VertexCount;

            for (int i = 0; identical && i < first.VertexCount; i++)
                identical = SamePosition(first.Vertices[i], second.Vertices[i]);

            check("同一把刀切两次：逐顶点一致", identical,
                $"{first?.VertexCount} vs {second?.VertexCount}");
        }

        // ---- 夹具与校验器（与实现无关） ------------------------------------------------------

        private static PCGMesh Cut(in PCGMesh mesh, Vector3 point, Vector3 normal, bool keepPositive, out string error)
        {
            bool ok = PCGMeshCut.TryCutByPlane(mesh, point, normal, keepPositive, out var result, out error);
            return ok ? result : null;
        }

        /// <summary>散度定理算封闭体积：Σ dot(a, cross(b, c)) / 6。实现里没有任何中间量参与。</summary>
        private static float Volume(in PCGMesh mesh)
        {
            float total = 0.0f;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                total += Vector3.Dot(a, Vector3.Cross(b, c));
            }

            return Math.Abs(total) / 6.0f;
        }

        /// <summary>每条有向边都必须有反向孪生、且不重复出现——这才是"逐边恰好两面"。</summary>
        /// <remarks>
        /// 先把有向边全收下来，**再**逐条查反向：边是两两成对的，先到的那条当然还没有孪生，
        /// 边收边查会把一个完好的网格判成不水密（第一版就是这么错的）。
        /// </remarks>
        private static bool IsWatertight(in PCGMesh mesh)
        {
            var edges = new HashSet<(long, long, long, long, long, long)>();

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = Key(mesh.Vertices[mesh.Triangles[t]]);
                var b = Key(mesh.Vertices[mesh.Triangles[t + 1]]);
                var c = Key(mesh.Vertices[mesh.Triangles[t + 2]]);

                if (!edges.Add((a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3)) ||
                    !edges.Add((b.Item1, b.Item2, b.Item3, c.Item1, c.Item2, c.Item3)) ||
                    !edges.Add((c.Item1, c.Item2, c.Item3, a.Item1, a.Item2, a.Item3)))
                {
                    return false;
                }
            }

            foreach (var edge in edges)
            {
                if (!edges.Contains((edge.Item4, edge.Item5, edge.Item6, edge.Item1, edge.Item2, edge.Item3)))
                    return false;
            }

            return true;
        }

        /// <summary>切口那几张面朝哪边：保留上半时朝 -Y、保留下半时朝 +Y（都要**背离**保留下来的那块）。</summary>
        private static bool CapFacesAlong(in PCGMesh mesh, bool expectUp)
        {
            int matching = 0;

            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[t]];
                var b = mesh.Vertices[mesh.Triangles[t + 1]];
                var c = mesh.Vertices[mesh.Triangles[t + 2]];

                if (Math.Abs(a.Y) > 1e-3f || Math.Abs(b.Y) > 1e-3f || Math.Abs(c.Y) > 1e-3f)
                    continue;

                // 三个顶点都在切口平面上：法线（叉积）的 Y 符号就是朝向。
                float normalY = Vector3.Cross(b - a, c - a).Y;

                if (expectUp ? normalY > 0.0f : normalY < 0.0f)
                    matching++;
            }

            return matching > 0;
        }

        private static bool HasNaN(in PCGMesh mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];

                if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z))
                    return true;
            }

            return false;
        }

        private static (long, long, long) Key(Vector3 value) =>
            ((long)Math.Round(value.X * 10000.0), (long)Math.Round(value.Y * 10000.0), (long)Math.Round(value.Z * 10000.0));

        private static bool SamePosition(Vector3 a, Vector3 b) =>
            Math.Abs(a.X - b.X) < 1e-5f && Math.Abs(a.Y - b.Y) < 1e-5f && Math.Abs(a.Z - b.Z) < 1e-5f;

        private static bool Near(float a, float b, float tolerance) => Math.Abs(a - b) < tolerance;
    }
}

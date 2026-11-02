// PCG 插件 - 洞穴隧道网络的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.38）
//
// 洞穴有两半，各错各的：**几何**那一半（管道）错了会让洞壁漏光、法线朝外、体积对不上；**巷道**那一半错了会让洞
// 钻穿地心、分叉连不回父隧道、半径不受参数约束。所以断言分三层——先在**手造的路径**上验管道的体积/水密/朝向，
// 再在**规划器**上验深度上限、段长与半径边界、分叉的父子连通性，最后在**节点**层验四列、样条与网格的件数。
//
// 这里刻意不复用 GeometryTests 的私有助手：体积与绕序的算法各写一份，两边同时写错才可能同时绿。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class CaveTests
    {
        internal static void Run(CheckDelegate check)
        {
            TubeTests(check);
            PlannerTests(check);
            NodeTests(check);
            PartitionedEntranceTests(check);
            VolumeClipTests(check);
            RegionTests(check);
        }

        // ---- 区域：洞道只准在框内挖（产生器输入，不是事后删点）------------------------------------------------

        /// <summary>
        /// 区域约束要证的不是"图里有个盒子"，而是**洞真的没出去**、而且**出去过**（否则这条约束在空转）。
        /// </summary>
        /// <remarks>
        /// 与散布/聚落那三层的 <c>within</c> 不是一条链：那三层筛的是互相独立的实例点，洞穴的点是**洞道截面序列**，
        /// 事后删点等于在管壁上开洞。所以这里约束的是走法本身——挖到边界前截短步长，落点与方向都不搬。
        /// 判据因此配一对：无区域时至少要有一个种子跑出框（证明框真的会被撞），有区域时一个点都不许在框外。
        /// </remarks>
        private static void RegionTests(CheckDelegate check)
        {
            var options = new PCGCaveOptions
            {
                SegmentLength = 120.0f,
                SegmentJitter = 0.4f,
                BranchChance = 0.2f,
                MaxTurnDegrees = 35.0f,
                MaxDepth = 900.0f,
                MaxSegments = 24,
                Radius = 60.0f,
                RadiusJitter = 0.3f,
                MaxTunnels = 12,
            };

            var region = new BoundingBox(new Vector3(-150.0f, -100000.0f, -150.0f),
                new Vector3(150.0f, 100000.0f, 150.0f));

            var tunnels = new List<PCGTunnel>();

            int seedsThatEscaped = 0;
            int outsideWithoutRegion = 0;
            int totalWithoutRegion = 0;

            for (int seed = 0; seed < 8; seed++)
            {
                PCGCavePlanner.Plan(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f),
                    4000 + seed, in options, tunnels);

                bool escaped = false;

                foreach (var tunnel in tunnels)
                    foreach (var point in tunnel.Centerline)
                    {
                        totalWithoutRegion++;

                        if (Math.Abs(point.X) > 150.0f || Math.Abs(point.Z) > 150.0f)
                        {
                            escaped = true;
                            outsideWithoutRegion++;
                        }
                    }

                if (escaped)
                    seedsThatEscaped++;
            }

            // 对照自己要先成立：一个从不撞界的夹具，"有区域也没事"那句话什么都证明不了。
            check("不设区域时洞道确实会爬出 300×300 这块地（否则下面的区域断言是空转）",
                totalWithoutRegion > 0 && seedsThatEscaped > 0,
                $"{seedsThatEscaped}/8 个种子出过界，共 {outsideWithoutRegion}/{totalWithoutRegion} 个点在界外");

            var bounded = options;
            bounded.Region = region;

            int outsideWithRegion = 0;
            int pointsWithRegion = 0;
            int tunnelCount = 0;
            int singlePointTunnels = 0;

            for (int seed = 0; seed < 8; seed++)
            {
                PCGCavePlanner.Plan(new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f),
                    4000 + seed, in bounded, tunnels);

                tunnelCount += tunnels.Count;

                foreach (var tunnel in tunnels)
                {
                    if (tunnel.Centerline.Length < 2)
                        singlePointTunnels++;

                    foreach (var point in tunnel.Centerline)
                    {
                        pointsWithRegion++;

                        if (Math.Abs(point.X) > 150.0f + 1e-3f || Math.Abs(point.Z) > 150.0f + 1e-3f)
                            outsideWithRegion++;
                    }
                }
            }

            check("设了区域之后一个点都不许在框外（约束的是走法本身）",
                outsideWithRegion == 0 && pointsWithRegion > 0,
                $"{outsideWithRegion}/{pointsWithRegion} 个点在框外");

            check("区域没有把洞穴一起取消掉（隧道与点都还在，不是靠产出零蒙过去）",
                tunnelCount > 0 && pointsWithRegion > tunnelCount,
                $"{tunnelCount} 条 / {pointsWithRegion} 个点，其中 {singlePointTunnels} 条只剩入口点" +
                "（任何截断都可能这样，体积边界那条同理；洞壁对少于两点的中心线已有回退）");

            // 入口在框外：这一条洞整条都不该存在（节点里那条守卫的纯核版本）。
            var outsideEntrance = new List<PCGTunnel>();
            PCGCavePlanner.Plan(new Vector3(900.0f, 0.0f, 900.0f), new Vector3(0.0f, -1.0f, 0.0f),
                7, in bounded, outsideEntrance);

            int entranceOutsidePoints = 0;

            foreach (var tunnel in outsideEntrance)
                entranceOutsidePoints += tunnel.Centerline.Length;

            check("入口落在框外时明确不产出洞道（而不是挖到边界上停住）",
                outsideEntrance.Count == 0 || entranceOutsidePoints == 0,
                $"{outsideEntrance.Count} 条 / {entranceOutsidePoints} 个点");

            // 同种子两次必须逐点一致：区域只是多一道截断，不该引入任何新的不确定性。
            var first = new List<PCGTunnel>();
            var second = new List<PCGTunnel>();
            PCGCavePlanner.Plan(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), 31, in bounded, first);
            PCGCavePlanner.Plan(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), 31, in bounded, second);

            bool same = first.Count == second.Count;

            for (int i = 0; same && i < first.Count; i++)
            {
                if (first[i].Centerline.Length != second[i].Centerline.Length)
                {
                    same = false;
                    break;
                }

                for (int p = 0; p < first[i].Centerline.Length; p++)
                {
                    if ((first[i].Centerline[p] - second[i].Centerline[p]).Length > 1e-4f)
                    {
                        same = false;
                        break;
                    }
                }
            }

            check("带区域的洞穴仍与种子一一对应（同种子两次逐点相同）",
                same && first.Count > 0, $"{first.Count} vs {second.Count} 条");

            // ---- 追加槽位的存档兼容（参数顺序就是存档格式，只能往后接）----

            // 老图只有 12 个值：读到区域这一档必须回退成"不设区域"，而不是凭空框出一块把洞穴闷死。
            check("老图（12 个值）回退成不设区域",
                !Make().Options.Region.HasValue, "见判据");

            var withRegion = new PCGCaveNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCaveNode.Id,
                new object[]
                {
                    60.0f, 0.25f, 0.4f, 30.0f, 300.0f, 10, 20.0f, 0.3f, 8, 8, true, true,
                    new Vector3(-150.0f, 0.0f, -120.0f), new Vector3(150.0f, 0.0f, 180.0f),
                },
                0));

            var box = withRegion.Options.Region;
            bool cornersOk = box.HasValue &&
                             Math.Abs(box.Value.Minimum.X + 150.0f) < 1e-3f &&
                             Math.Abs(box.Value.Minimum.Z + 120.0f) < 1e-3f &&
                             Math.Abs(box.Value.Maximum.X - 150.0f) < 1e-3f &&
                             Math.Abs(box.Value.Maximum.Z - 180.0f) < 1e-3f;

            check("新槽位落在 12/13 两个下标上（顺序就是存档格式，错一位读到的是别人的值）",
                cornersOk, box.HasValue ? $"{box.Value.Minimum} .. {box.Value.Maximum}" : "没有区域");

            // 两个角写反：按"不设区域"处理。这一档的判据是"上界必须大于下界"，
            // 写反了却当成一个空盒子，结局是整条洞穴在第一步就作废——比没有区域糟得多。
            var reversed = new PCGCaveNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCaveNode.Id,
                new object[]
                {
                    60.0f, 0.25f, 0.4f, 30.0f, 300.0f, 10, 20.0f, 0.3f, 8, 8, true, true,
                    new Vector3(150.0f, 0.0f, 180.0f), new Vector3(-150.0f, 0.0f, -120.0f),
                },
                0));

            check("两个角写反时按『不设区域』处理（而不是框出一个空盒子把洞穴闷死）",
                !reversed.Options.Region.HasValue,
                reversed.Options.Region.HasValue ? "框出了一个空区域" : "无区域");
        }

        // ---- 几何：管道 ------------------------------------------------------------------------------------

        private static void TubeTests(CheckDelegate check)
        {
            const float radius = 2.0f;
            const int sides = 12;

            // 竖直直管：正多边形截面面积 × 长度，就是体积的解析值。
            var straight = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 10, 0) };
            var outward = PCGMeshBuilder.Tube(straight, radius, sides, true, false);

            float section = 0.5f * sides * radius * radius * (float)Math.Sin(2.0 * Math.PI / sides);
            float exact = section * 10.0f;

            check("直管体积等于截面面积乘长度",
                Math.Abs(SignedVolume(outward) - exact) / exact < 0.005f,
                $"{SignedVolume(outward):F4} vs {exact:F4}");

            check("直管是水密的（每条边恰好两个三角形）", EdgeReport(outward).boundary == 0);
            check("直管绕序与法线一致", WindingAgrees(outward));
            check("直管没有 NaN", NoNaN(outward));

            // 内向翻转：法线与绕序一起翻，于是有向体积整好取负——这就是"翻对了"的证据。
            var inward = PCGMeshBuilder.Tube(straight, radius, sides, true, true);
            float outwardVolume = SignedVolume(outward);

            check("内向管的有向体积是外向的相反数",
                Math.Abs(SignedVolume(inward) + outwardVolume) < 1e-3f,
                $"{SignedVolume(inward):F4} vs {-outwardVolume:F4}");

            check("内向管同样水密", EdgeReport(inward).boundary == 0);
            check("内向管绕序仍与法线一致", WindingAgrees(inward));

            // 不封口：两端各留一圈边界边，正好 2×边数——封口不是"顺带"发生的。
            var open = PCGMeshBuilder.Tube(straight, radius, sides, false, false);

            check("不封口的管子两端各留一圈边界边",
                EdgeReport(open).boundary == sides * 2, EdgeReport(open).boundary.ToString());
            check("不封口少两个三角形的圈数（每端 sides 个三角形）",
                open.TriangleCount == outward.TriangleCount - sides * 2,
                $"{open.TriangleCount} vs {outward.TriangleCount}");

            // 折弯的路径：截面法线要跟着转（平行输运），否则拐弯处会扭出破洞。
            var elbow = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 10), new Vector3(0, 10, 10) };
            var bent = PCGMeshBuilder.Tube(elbow, 1.5f, 8, true, false);

            check("折弯的管道仍然水密", EdgeReport(bent).boundary == 0, EdgeReport(bent).boundary.ToString());
            check("折弯的管道绕序与法线一致", WindingAgrees(bent));
            check("折弯的管道没有 NaN", NoNaN(bent));

            float bentSection = 0.5f * 8 * 1.5f * 1.5f * (float)Math.Sin(2.0 * Math.PI / 8);
            float bentExact = bentSection * 20.0f;
            float measured = SignedVolume(bent);

            // 拐弯处的截面是**刚性**的：八边形跟着平行输运转过去，而不是被斜切面拉长，所以体积只可能比
            // "截面面积×中心线长"少——上界是硬事实，下界要靠独立方法给。
            check("折弯管道的体积为正且不超过 截面面积×中心线长",
                measured > 0.0f && measured <= bentExact * 1.001f, $"{measured:F2} vs {bentExact:F2}");

            // 独立参照：在包围盒里撒点、用射线穿过的奇偶性判内外。它不走散度定理那条路，两边同时错才可能同时绿。
            float sampled = SampledVolume(bent, 200000);

            check("折弯管道的体积与独立采样法一致（三成以内为界，实测差异在一成内）",
                Math.Abs(measured - sampled) / sampled < 0.03f,
                $"散度定理 {measured:F2} vs 采样 {sampled:F2}");

            // 退化输入：一个点或空路径不给网格，而不是抛异常。
            check("少于两个点的路径回退成空网格",
                PCGMeshBuilder.Tube(new List<Vector3> { Vector3.Zero }, 1.0f, 8, true, false).TriangleCount == 0 &&
                PCGMeshBuilder.Tube(null, 1.0f, 8, true, false).TriangleCount == 0);
        }

        // ---- 规划：巷道网络 --------------------------------------------------------------------------------

        private static void PlannerTests(CheckDelegate check)
        {
            var entrance = new Vector3(100.0f, 500.0f, -200.0f);
            var options = Options();

            var first = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 4242, options, first);

            // 段数是上限而不是承诺：深度用尽时隧道会提前收尾，但绝不会超过段数、也不会短到画不出管子。
            bool lengthsOk = first.Count > 0;

            for (int t = 0; t < first.Count && lengthsOk; t++)
                lengthsOk = first[t].Centerline.Length >= 2 &&
                            first[t].Centerline.Length <= options.MaxSegments + 1;

            check("中心线长度落在 [2, 段数+1] 内",
                lengthsOk, string.Join(",", first.ConvertAll(t => t.Centerline.Length)));

            // 深度上限：所有点都不得比入口深过一个 MaxDepth。
            float floor = entrance.Y - options.MaxDepth;
            bool withinDepth = true;

            for (int t = 0; t < first.Count && withinDepth; t++)
                for (int i = 0; i < first[t].Centerline.Length; i++)
                    withinDepth &= first[t].Centerline[i].Y >= floor - 1e-3f;

            check("没有点比入口深过 MaxDepth", withinDepth, floor.ToString("F1"));

            // 体积边界：给了盒子就整张网都在盒里，靠**截短步长**（不是事后把落点搬进来——搬了会与朝向分家，
            // 下一步就在边界上掉头）。同一个入口同一种子作对照：不设盒子时它确实会走到盒外，上面那条不是空转。
            var boxed = Options();
            boxed.Bounds = new BoundingBox(new Vector3(-400, -600, -400), new Vector3(400, 600, 400));

            var bounded = new List<PCGTunnel>();
            PCGCavePlanner.Plan(new Vector3(0, 300, 0), new Vector3(0, -1, 0), 999, boxed, bounded);

            var limit = boxed.Bounds.Value;
            bool inside = bounded.Count > 0;
            string outsideReport = $"{bounded.Count} 条隧道";

            for (int t = 0; t < bounded.Count && inside; t++)
                for (int i = 0; i < bounded[t].Centerline.Length; i++)
                {
                    var p = bounded[t].Centerline[i];

                    if (p.X < limit.Minimum.X - 1e-3f || p.X > limit.Maximum.X + 1e-3f ||
                        p.Y < limit.Minimum.Y - 1e-3f || p.Y > limit.Maximum.Y + 1e-3f ||
                        p.Z < limit.Minimum.Z - 1e-3f || p.Z > limit.Maximum.Z + 1e-3f)
                    {
                        inside = false;
                        outsideReport = $"隧道 {t} 第 {i} 点 {p} 出界";
                    }
                }

            check("给了体积盒时，所有中心线点都留在盒里", inside, outsideReport);

            var free = new List<PCGTunnel>();
            PCGCavePlanner.Plan(new Vector3(0, 300, 0), new Vector3(0, -1, 0), 999, Options(), free);

            bool escaped = false;

            for (int t = 0; t < free.Count && !escaped; t++)
                for (int i = 0; i < free[t].Centerline.Length && !escaped; i++)
                {
                    var p = free[t].Centerline[i];
                    escaped = p.X < limit.Minimum.X || p.X > limit.Maximum.X ||
                              p.Z < limit.Minimum.Z || p.Z > limit.Maximum.Z;
                }

            check("对照：不设盒子时同一张网会走到盒外", escaped);

            // 段长边界：夹到深度上限的那一步会被截短，所以只对未截短的步要下界，上界处处成立。
            float maxStep = options.SegmentLength * (1.0f + options.SegmentJitter) + 1e-3f;
            float minStep = options.SegmentLength * (1.0f - options.SegmentJitter) - 1e-3f;
            bool stepsOk = true;
            int clampedSteps = 0;

            for (int t = 0; t < first.Count && stepsOk; t++)
            {
                var line = first[t].Centerline;

                for (int i = 1; i < line.Length; i++)
                {
                    float distance = (line[i] - line[i - 1]).Length;

                    if (distance > maxStep)
                        stepsOk = false;

                    if (line[i].Y > floor + 1e-3f)
                        stepsOk &= distance >= minStep;
                    else
                        clampedSteps++;
                }
            }

            check("每段长度不超过 段长×(1+抖动)", stepsOk, $"截短的步数 {clampedSteps}");

            // 半径边界：抖动只能把半径留在 [base×(1−抖动), base×(1+抖动)] 内。
            float radiusLow = options.Radius * (1.0f - options.RadiusJitter) - 1e-3f;
            float radiusHigh = options.Radius * (1.0f + options.RadiusJitter) + 1e-3f;

            check("半径落在参数区间内",
                AllWithin(first, t => t.Radius, radiusLow, radiusHigh),
                string.Join(",", first.ConvertAll(t => t.Radius.ToString("F1"))));

            // 分叉：父下标必须早于自己，分叉段号要落在父的中心线上，且分叉点位置真的接得上。
            bool parentsOk = true;
            int branches = 0;
            string parentReport = "无分叉";

            for (int t = 0; t < first.Count && parentsOk; t++)
            {
                var tunnel = first[t];

                if (tunnel.Parent < 0)
                {
                    parentsOk &= tunnel.ParentSegment == -1;
                    continue;
                }

                branches++;

                var parent = first[tunnel.Parent];

                parentsOk &= tunnel.Parent < t;
                parentsOk &= tunnel.ParentSegment >= 0 && tunnel.ParentSegment < parent.Centerline.Length;
                parentsOk &= (tunnel.Centerline[0] - parent.Centerline[tunnel.ParentSegment]).Length < 1e-3f;
                parentReport = $"隧道 {t} 接在 {tunnel.Parent} 的第 {tunnel.ParentSegment} 段";
            }

            check("分叉的父指针指向更早的隧道且分叉点接得上", parentsOk, parentReport);
            check("默认参数下确实分叉了", branches > 0, $"分叉 {branches} 条");

            // 条数上限：无论怎么分叉都不许超过 MaxTunnels。
            var crowded = Options();
            crowded.BranchChance = 1.0f;
            crowded.MaxTunnels = 6;

            var many = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 7, crowded, many);

            check("分叉再密也不超过 MaxTunnels", many.Count == 6, many.Count.ToString());

            // 关掉分叉就是一条独苗。
            var solo = Options();
            solo.BranchChance = 0.0f;

            var single = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 7, solo, single);

            check("关掉分叉只挖一条", single.Count == 1, single.Count.ToString());
            check("独苗没有父", single[0].Parent == -1 && single[0].ParentSegment == -1);

            // 确定性：同种子逐点相同，换种子必须不同（否则"种子"是摆设）。
            var again = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 4242, options, again);
            var other = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 4243, options, other);

            check("同种子两次挖出的洞逐点相同", SameNetwork(first, again));
            check("换种子挖出的洞不一样", !SameNetwork(first, other));

            // 掉头是管壁翻面的充要条件：相邻两段一旦反向，管道在折返处就会自重叠、法线反向。所以这里不只是
            // 在默认参数下看一眼，而是拿"每步最大 89 度、分叉拉满"的压力参数扫种子。
            bool noUTurn = true;
            string uTurnReport = null;

            var stress = Options();
            stress.MaxTurnDegrees = 89.0f;
            stress.BranchChance = 1.0f;
            stress.MaxSegments = 40;
            stress.MaxTunnels = 12;
            stress.MaxDepth = 120.0f;

            for (int seed = 1; seed <= 64 && noUTurn; seed++)
            {
                var net = new List<PCGTunnel>();
                PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), seed, stress, net);

                for (int t = 0; t < net.Count && noUTurn; t++)
                    noUTurn = NoReversal(net[t].Centerline, out uTurnReport, $"种子 {seed} 隧道 {t}");
            }

            check("压力参数扫 64 个种子都不掉头", noUTurn, uTurnReport);

            bool fixtureNoUTurn = true;

            for (int t = 0; t < first.Count && fixtureNoUTurn; t++)
                fixtureNoUTurn = NoReversal(first[t].Centerline, out uTurnReport, $"隧道 {t}");

            check("默认参数下相邻段也不掉头", fixtureNoUTurn, uTurnReport);

            // 零方向：按正下方处理，而不是产生 NaN 或原地打转。
            var degenerate = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, Vector3.Zero, 11, options, degenerate);

            check("零方向按正下方处理", degenerate.Count > 0 && !NetworkHasNaN(degenerate));

            // MaxTunnels 为 1 时，即使概率拉满也只有一条。
            var capped = Options();
            capped.BranchChance = 1.0f;
            capped.MaxTunnels = 1;

            var only = new List<PCGTunnel>();
            PCGCavePlanner.Plan(entrance, new Vector3(0, -1, 0), 3, capped, only);

            check("MaxTunnels 为 1 时只有一条", only.Count == 1, only.Count.ToString());
        }

        // ---- 节点 ------------------------------------------------------------------------------------------

        private static void NodeTests(CheckDelegate check)
        {
            var bounds = new BoundingBox(new Vector3(-500, 0, -500), new Vector3(500, 800, 500));
            var context = new PCGExecutionContext { Seed = 20261008, GenerationBounds = bounds };

            var node = Make();
            var result = node.Execute(context, new[] { (PCGData)null });

            var tunnels = new List<PCGTunnel>();
            PCGCavePlanner.Plan(bounds.Center, new Vector3(0, -1, 0), context.Seed, node.Options, tunnels);

            int expectedPoints = 0;
            for (int t = 0; t < tunnels.Count; t++)
                expectedPoints += tunnels[t].Centerline.Length;

            check("不接输入时以生成范围中心为入口，点数等于中心线长度之和",
                result.Count == expectedPoints, $"{result.Count} vs {expectedPoints}");

            check("隧道条数被记下来", node.LastTunnelCount == tunnels.Count,
                $"{node.LastTunnelCount} vs {tunnels.Count}");

            // 四列都要在，且长度与点数一致。
            var ids = result.SharedAttributes?.TryGetFloatArray(PCGCaveNode.TunnelColumn);
            var distances = result.SharedAttributes?.TryGetFloatArray(PCGCaveNode.DistanceColumn);
            var depths = result.SharedAttributes?.TryGetFloatArray(PCGCaveNode.DepthColumn);
            var radii = result.SharedAttributes?.TryGetFloatArray(PCGCaveNode.RadiusColumn);

            check("四列齐备且与点数等长",
                ids != null && ids.Length == result.Count &&
                distances != null && distances.Length == result.Count &&
                depths != null && depths.Length == result.Count &&
                radii != null && radii.Length == result.Count);

            // 隧道号必须落在 [0, 条数)；里程在每条隧道内从 0 起单调不减；半径与深度与中心线一致。
            bool idsOk = true;
            bool distanceOk = true;
            bool depthOk = true;
            float previousTunnel = -1.0f;
            float previousDistance = 0.0f;
            string mismatch = null;

            for (int i = 0; i < result.Count; i++)
            {
                idsOk &= ids[i] >= 0.0f && ids[i] < tunnels.Count;

                if (ids[i] != previousTunnel)
                {
                    distanceOk &= ids[i] > previousTunnel; // 隧道号按输出顺序递增
                    distanceOk &= Math.Abs(distances[i]) < 1e-3f;
                    previousTunnel = ids[i];
                }
                else
                {
                    distanceOk &= distances[i] >= previousDistance - 1e-3f;
                }

                previousDistance = distances[i];

                var tunnel = tunnels[(int)ids[i]];
                float entranceY = tunnel.Centerline[0].Y;
                float expectedDepth = entranceY - result.Points[i].Position.Y;

                if (Math.Abs(depths[i] - expectedDepth) > 1e-2f)
                {
                    depthOk = false;
                    mismatch ??= $"{depths[i]:F2} vs {expectedDepth:F2}";
                }

                if (Math.Abs(radii[i] - tunnel.Radius) > 1e-3f)
                {
                    depthOk = false;
                    mismatch ??= $"半径 {radii[i]:F2} vs {tunnel.Radius:F2}";
                }
            }

            check("隧道号落在有效范围内且按输出顺序递增", idsOk);
            check("每条隧道的里程从 0 起单调不减", distanceOk);
            check("深度列等于入口高度减点高度，半径列等于该隧道半径", depthOk, mismatch);

            // 点也必须指向这列存储，否则下游按 point.Attributes 读会是空的。
            bool linked = result.Count > 0 && ReferenceEquals(result.Points[0].Attributes, result.SharedAttributes) &&
                          ReferenceEquals(result.Points[result.Count - 1].Attributes, result.SharedAttributes);

            check("点指向本数据的属性存储", linked);

            // 样条：每条隧道一条，至少两个点。
            check("样条数与隧道数一致",
                result.Splines != null && result.Splines.Length == tunnels.Count,
                result.Splines?.Length.ToString() ?? "null");

            bool splinesOk = result.Splines != null;
            for (int t = 0; splinesOk && t < result.Splines.Length; t++)
                splinesOk = result.Splines[t].Points.Length >= 2;

            check("每条样条至少两个点", splinesOk);

            // 管道走**独立几何**：一条隧道一块、自带摆放。承载契约就在这里钉死——洞壁"在哪儿"只有 Placement
            // 有地方写；记在 Meshes 上会被按点下标配对（一条隧道只有一个网格、却有很多点），实测整片洞壁飞到
            // 体积外两倍远的地方。
            check("管道以独立几何承载，一条隧道一块",
                result.Geometry != null && result.Geometry.Length == tunnels.Count,
                result.Geometry?.Length.ToString() ?? "null");

            bool meshesOk = result.Geometry != null;
            string meshReport = null;

            for (int t = 0; meshesOk && t < result.Geometry.Length; t++)
            {
                var mesh = result.Geometry[t].Mesh;
                var edges = EdgeReport(mesh);
                bool wound = WindingAgrees(mesh);
                bool clean = NoNaN(mesh);
                float volume = SignedVolume(mesh);

                if (edges.boundary != 0 || !wound || !clean || volume >= 0.0f)
                    meshReport ??= $"#{t}：破口边 {edges.boundary}、绕序一致 {wound}（{FirstWindingBreak(mesh)}）、" +
                                   $"无 NaN {clean}、体积 {volume:F1}";

                meshesOk &= edges.boundary == 0 && wound && clean && volume < 0.0f;
            }

            check("每个洞壁网格水密、绕序一致、有向体积为负（朝内）", meshesOk, meshReport);

            // 摆放契约的两半：网格必须已经搬回自己的局部原点，摆放必须落在它那条隧道的中心线上。
            // 前半只有正面：管子本来是**世界坐标**的（Tube 写 path[ring]+offset），所以"直接建出来的那根"
            // 中心不在原点——用它当对照，证明"搬一次"确实在做事，而不是恒等。
            var rawTube = PCGMeshBuilder.Tube(tunnels[0].Centerline, tunnels[0].Radius, 14, true, true);
            bool rawOffOrigin = (PCGMeshBuilder.Bounds(rawTube).Center - Vector3.Zero).Length > 1.0f;

            check("对照：直接建出来的管子是世界坐标的（中心不在原点）", rawOffOrigin,
                (PCGMeshBuilder.Bounds(rawTube).Center).ToString());

            bool centredOk = result.Geometry != null;
            var placementReport = $"{result.Geometry?.Length ?? 0} 块几何";

            for (int t = 0; centredOk && t < result.Geometry.Length; t++)
            {
                var item = result.Geometry[t];
                var centre = PCGMeshBuilder.Bounds(item.Mesh).Center;

                if (centre.Length > 1e-3f)
                {
                    centredOk = false;
                    placementReport = $"#{t} 网格中心 {centre}";
                    break;
                }

                // 摆放要落在这条隧道中心线的包围盒里（按管半径放宽）——独立算一遍，不复用 PlaceTube 的算法。
                var lineBounds = LineBounds(tunnels[t].Centerline, tunnels[t].Radius + 1e-3f);
                var placement = item.Placement.Translation;

                if (placement.X < lineBounds.Minimum.X || placement.X > lineBounds.Maximum.X ||
                    placement.Y < lineBounds.Minimum.Y || placement.Y > lineBounds.Maximum.Y ||
                    placement.Z < lineBounds.Minimum.Z || placement.Z > lineBounds.Maximum.Z)
                {
                    centredOk = false;
                    placementReport = $"#{t} 摆放 {placement} 不在中心线包围盒 {lineBounds}";
                }
            }

            check("每块管道都建在自己的局部原点上，且摆放落在它那条隧道的中心线里", centredOk, placementReport);

            // 关掉几何：只出点与样条。
            var noMesh = Make(makeMeshes: false);
            var noMeshResult = noMesh.Execute(context, new[] { (PCGData)null });

            check("关掉 Make Meshes 后不输出几何",
                noMeshResult.Geometry == null || noMeshResult.Geometry.Length == 0);
            check("关掉几何不影响点数", noMeshResult.Count == result.Count);

            // 关掉深度列：那一列就不该存在。
            var noDepth = Make(writeDepth: false);
            var noDepthResult = noDepth.Execute(context, new[] { (PCGData)null });

            check("关掉 Write Depth 后没有深度列",
                noDepthResult.SharedAttributes == null ||
                !noDepthResult.SharedAttributes.Contains(PCGCaveNode.DepthColumn));

            // 多个入口：各挖一套，条数按入口累加，且隧道号仍然连续覆盖。
            var layer = PCGData.Create(3);

            for (int i = 0; i < layer.Count; i++)
            {
                layer.Points[i] = PCGPoint.Create(new Vector3(i * 300.0f - 300.0f, 400.0f, 0.0f), 1.0f, i);
                layer.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            var multi = Make();
            var multiResult = multi.Execute(context, new[] { layer });

            // 预测按"逐入口各规划一次再求和"，不是"单入口 × 3"：巷道被体积边界截断之后，走到边上的那些会提前
            // 收尾，能排下的分叉也跟着少，各入口的条数并不相同（实测 23 而不是 8×3）。
            int expectedMulti = 0;

            for (int i = 0; i < layer.Count; i++)
            {
                var perEntrance = new List<PCGTunnel>();
                PCGCavePlanner.Plan(layer.Points[i].Position, new Vector3(0, -1, 0),
                    unchecked(context.Seed + i * 7919), multi.Options, perEntrance);
                expectedMulti += perEntrance.Count;
            }

            check("每个入口各挖一套，条数按入口累加",
                multi.LastTunnelCount == expectedMulti, $"{multi.LastTunnelCount} vs {expectedMulti}");

            var multiIds = multiResult.SharedAttributes?.TryGetFloatArray(PCGCaveNode.TunnelColumn);
            float highest = -1.0f;

            for (int i = 0; multiIds != null && i < multiIds.Length; i++)
                highest = Math.Max(highest, multiIds[i]);

            check("多入口时隧道号覆盖到 条数×入口数−1",
                multiIds != null && Math.Abs(highest - (multi.LastTunnelCount - 1)) < 1e-3f, highest.ToString());

            // 确定性：同种子两次跑出来的点逐点相同。
            var repeat = Make();
            var repeatResult = repeat.Execute(context, new[] { (PCGData)null });
            bool same = repeatResult.Count == result.Count;

            for (int i = 0; same && i < result.Count; i++)
                same = (repeatResult.Points[i].Position - result.Points[i].Position).Length < 1e-6f;

            check("同种子两次运行的点逐点相同", same);

            // 没有生成范围也没有输入：仍然给一个入口，不能抛异常。
            var bare = Make();
            var bareResult = bare.Execute(new PCGExecutionContext { Seed = 5 }, new[] { (PCGData)null });

            check("没有任何范围时也能跑（入口取默认）", bareResult != null && bareResult.Count > 0);
        }

        // ---- 夹具 ------------------------------------------------------------------------------------------

        private static PCGCaveOptions Options() => new PCGCaveOptions
        {
            SegmentLength = 60.0f,
            SegmentJitter = 0.25f,
            BranchChance = 0.4f,
            MaxTurnDegrees = 30.0f,
            MaxDepth = 300.0f,
            MaxSegments = 10,
            Radius = 20.0f,
            RadiusJitter = 0.3f,
            MaxTunnels = 8,
        };

        /// <summary>
        /// 分区运行时入口从哪来：**体积自己的范围**中心，而不是这一格的中心。
        /// </summary>
        /// <remarks>
        /// 一次分区运行只覆盖一格的（带余量）范围。默认入口若取"这次运行的范围中心"，16 格就会各自从自己的
        /// 中心起洞——长出来是 **16 张迷你洞网**（实测同一个体积分区后 48 件、整块时 12 件，而它们本该是
        /// 同一张网按格裁开）。这里用**两向对照**钉住入口那一点：给了体积范围时它就在体积中心，只给格范围时
        /// 它在格中心（离体积中心 ≈989）。
        /// </remarks>
        private static void PartitionedEntranceTests(CheckDelegate check)
        {
            var volume = new BoundingBox(new Vector3(-700, -400, -700), new Vector3(700, 400, 700));

            // 一格：体积东北那 1/4；它的（带余量）范围中心在水平面 (350, 350) 上。
            var cell = new BoundingBox(new Vector3(0, -400, 0), new Vector3(1400, 400, 1400));
            var origin = Vector3.Zero;

            var fromVolume = Make(makeMeshes: false).Execute(
                new PCGExecutionContext { Seed = 7, GenerationBounds = cell, SourceBounds = volume },
                new[] { (PCGData)null });

            var fromCell = Make(makeMeshes: false).Execute(
                new PCGExecutionContext { Seed = 7, GenerationBounds = cell },
                new[] { (PCGData)null });

            check("分区那一格两种情形都挖出了东西（下面对照的前提）",
                fromVolume.Count > 0 && fromCell.Count > 0,
                $"{fromVolume.Count} / {fromCell.Count}");

            if (fromVolume.Count == 0 || fromCell.Count == 0)
                return;

            // 看**入口那一点**（隧道 0 的第一个点就是入口），而不是"离中心最近的点"：巷道本身会往外走，
            // 拿最近点当入口会随步长抖动（而且现在巷道已被体积盒截住，最近点必然比入口更靠近中心）。
            float entranceFromVolume = Horizontal(fromVolume.Points[0].Position, origin);
            float entranceFromCell = Horizontal(fromCell.Points[0].Position, origin);

            check("给了体积范围：入口落在**体积**中心（离它几乎是 0）",
                entranceFromVolume < 1.0f, entranceFromVolume.ToString("F1"));

            check("对照：只给格范围时入口落在**格**中心（离体积中心 ≈989）",
                entranceFromCell > 400.0f, entranceFromCell.ToString("F1"));

            // 巷道不许走出体积盒——这是"洞穴挖在**这个体积里**"那句话（靠截短步长实现，见 PCGCavePlanner）。
            var escaped = FirstOutside(fromVolume, volume);

            check("给了体积范围时，整张洞网都留在体积盒里", escaped == null, escaped);
        }

        /// <summary>两点在水平面上的距离（入口的 Y 会被贴地改掉，所以只比 XZ）。</summary>
        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>第一个跑出盒子的点（都在盒里就返回 null）——把"越界"报成具体坐标，而不是一个真假。</summary>
        private static string FirstOutside(in PCGData data, BoundingBox box)
        {
            for (int i = 0; i < data.Count; i++)
            {
                var p = data.Points[i].Position;

                if (p.X < box.Minimum.X - 1e-3f || p.X > box.Maximum.X + 1e-3f ||
                    p.Y < box.Minimum.Y - 1e-3f || p.Y > box.Maximum.Y + 1e-3f ||
                    p.Z < box.Minimum.Z - 1e-3f || p.Z > box.Maximum.Z + 1e-3f)
                    return $"#{i} {p} 出界 {box}";
            }

            return null;
        }

        /// <summary>
        /// 整块运行也要按体积裁剪（<see cref="PCGExecutionContext.ClipRegion"/>）。
        /// </summary>
        /// <remarks>
        /// 不裁的后果实测过：同一个洞、同一个种子、同一个盒子，整块出 12 件、分区出 9 件——差的那 3 件在体积
        /// 盒外。这里钉两件事：区域内外的判定，以及**边界取法与格子一致**（格子那片区域的并集把上界也包进来，
        /// 见 <see cref="PCGPartitionPlanner.IsInsideCell"/>，所以区域也必须是闭区间；否则正好落在最远边上的点
        /// 会"整块留、分区丢"）。
        /// </remarks>
        private static void VolumeClipTests(CheckDelegate check)
        {
            var box = new BoundingBox(new Vector3(-700, -800, -700), new Vector3(700, 700, 700));
            var region = new PCGExecutionContext { ClipRegion = box };

            check("整块运行：盒内的位置算在区域内", region.IsInsideClip(new Vector3(0, 0, 0)));
            check("整块运行：盒外的位置被裁掉",
                !region.IsInsideClip(new Vector3(900, 0, 0)) &&
                !region.IsInsideClip(new Vector3(0, -900, 0)) &&
                !region.IsInsideClip(new Vector3(0, 0, 800)));
            check("整块运行：盒子的两个最远角也算在区域内（闭区间，与格子并集一致）",
                region.IsInsideClip(box.Maximum) && region.IsInsideClip(box.Minimum));

            // 对照：既没有格子也没有区域时一律放行——编辑器与测试自己造上下文时走的就是这一条，
            // 不能被这次改动改掉（改了会让所有离屏夹具突然什么都生成不出来）。
            var bare = new PCGExecutionContext();
            check("对照：没给格子也没给区域时，任何位置都放行",
                bare.IsInsideClip(new Vector3(9999, 9999, 9999)));

            // 等价性：区域判定必须与"落在某一格里"逐点相同。格子那份参照由规划器独立摊出来——不是把区域
            // 的算法抄第二遍。探针压在格子的边界与盒子的边界上，取法不一致时这里最先红。
            var cells = PCGPartitionPlanner.Plan(box, 350.0f, 0.0f, out string error);
            bool equivalent = error == null && cells != null && cells.Count > 0;
            string mismatch = error;

            for (int x = -2; x <= 2 && equivalent; x++)
            {
                for (int z = -2; z <= 2 && equivalent; z++)
                {
                    var probe = new Vector3(x * 350.0f, 0.0f, z * 350.0f);
                    bool inRegion = region.IsInsideClip(probe);
                    bool inAnyCell = false;

                    for (int c = 0; c < cells.Count; c++)
                        inAnyCell |= PCGPartitionPlanner.IsInsideCell(cells[c], probe);

                    if (inRegion != inAnyCell)
                    {
                        equivalent = false;
                        mismatch = $"{probe}：区域 {inRegion} vs 格子并集 {inAnyCell}";
                    }
                }
            }

            check("整块与分区的存活判定逐点等价（含压在边界上的探针）", equivalent, mismatch);
        }

        /// <summary>中心线的包围盒，按半径放宽（摆放是否落在这条隧道里的独立参照）。</summary>
        private static BoundingBox LineBounds(Vector3[] line, float pad)
        {
            var minimum = line[0];
            var maximum = line[0];

            for (int i = 1; i < line.Length; i++)
            {
                minimum = Vector3.Min(minimum, line[i]);
                maximum = Vector3.Max(maximum, line[i]);
            }

            var padding = new Vector3(pad, pad, pad);
            return new BoundingBox(minimum - padding, maximum + padding);
        }

        private static PCGCaveNode Make(bool makeMeshes = true, bool writeDepth = true)
        {
            return new PCGCaveNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCaveNode.Id,
                new object[]
                {
                    60.0f, 0.25f, 0.4f, 30.0f, 300.0f, 10, 20.0f, 0.3f, 8, 8, makeMeshes, writeDepth,
                },
                0));
        }

        /// <summary>相邻两段是否反向（点积 ≤ 0）——一旦反向，管道折返处会自重叠、法线翻面。</summary>
        private static bool NoReversal(Vector3[] line, out string report, string label)
        {
            report = null;

            for (int i = 2; i < line.Length; i++)
            {
                var previous = line[i - 1] - line[i - 2];
                var current = line[i] - line[i - 1];

                if (previous.Length < 1e-5f || current.Length < 1e-5f)
                    continue;

                float cosine = Vector3.Dot(previous / previous.Length, current / current.Length);

                if (cosine <= 0.0f)
                {
                    report = $"{label} 第 {i} 段夹角余弦 {cosine:F3}";
                    return false;
                }
            }

            return true;
        }

        private static bool AllEqual(List<PCGTunnel> tunnels, Func<PCGTunnel, int> select, int expected)        {
            for (int i = 0; i < tunnels.Count; i++)
                if (select(tunnels[i]) != expected)
                    return false;

            return tunnels.Count > 0;
        }

        private static bool AllWithin(List<PCGTunnel> tunnels, Func<PCGTunnel, float> select, float low, float high)
        {
            for (int i = 0; i < tunnels.Count; i++)
            {
                float value = select(tunnels[i]);

                if (value < low || value > high)
                    return false;
            }

            return tunnels.Count > 0;
        }

        private static bool SameNetwork(List<PCGTunnel> a, List<PCGTunnel> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int t = 0; t < a.Count; t++)
            {
                if (a[t].Centerline.Length != b[t].Centerline.Length)
                    return false;

                if (a[t].Parent != b[t].Parent || a[t].ParentSegment != b[t].ParentSegment)
                    return false;

                for (int i = 0; i < a[t].Centerline.Length; i++)
                    if ((a[t].Centerline[i] - b[t].Centerline[i]).Length > 0.0f)
                        return false;
            }

            return true;
        }

        private static bool NetworkHasNaN(List<PCGTunnel> tunnels)
        {
            for (int t = 0; t < tunnels.Count; t++)
                for (int i = 0; i < tunnels[t].Centerline.Length; i++)
                    if (!IsFinite(tunnels[t].Centerline[i]))
                        return true;

            return false;
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.X) && !float.IsNaN(v.Y) && !float.IsNaN(v.Z) &&
            !float.IsInfinity(v.X) && !float.IsInfinity(v.Y) && !float.IsInfinity(v.Z);

        /// <summary>有向体积：对每个三角形取 dot(a, cross(b, c)) / 6 求和（散度定理）。</summary>
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

        /// <summary>
        /// 独立的体积参照：在包围盒里确定性撒点，用一条固定方向的射线穿过网格的**奇偶性**判断内外，
        /// 再乘上盒子体积。它不看绕序、不用散度定理，和 <see cref="SignedVolume"/> 是两条路。
        /// </summary>
        private static float SampledVolume(PCGMesh mesh, int samples)
        {
            var bounds = MeshBounds(mesh);
            var size = bounds.Maximum - bounds.Minimum;
            float box = Math.Max(0.0f, size.X) * Math.Max(0.0f, size.Y) * Math.Max(0.0f, size.Z);

            // 方向刻意取一个"不规整"的向量：正对坐标轴时射线容易正好扫过棱或顶点，奇偶性会抖。
            var direction = new Vector3(0.9971f, 0.0431f, 0.0611f);
            float length = direction.Length;
            direction = length > 1e-6f ? direction / length : Vector3.Up;

            var rng = new PCGDeterministicRng(101, 202, 0);
            int inside = 0;

            for (int i = 0; i < samples; i++)
            {
                var p = new Vector3(
                    bounds.Minimum.X + rng.NextFloat() * size.X,
                    bounds.Minimum.Y + rng.NextFloat() * size.Y,
                    bounds.Minimum.Z + rng.NextFloat() * size.Z);

                if (IsInside(mesh, p, direction))
                    inside++;
            }

            return box * inside / samples;
        }

        /// <summary>Möller–Trumbore：射线与三角形求交后数奇偶，奇数为内。</summary>
        private static bool IsInside(PCGMesh mesh, Vector3 origin, Vector3 direction)
        {
            int crossings = 0;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var a = mesh.Vertices[mesh.Triangles[t * 3]];
                var b = mesh.Vertices[mesh.Triangles[t * 3 + 1]];
                var c = mesh.Vertices[mesh.Triangles[t * 3 + 2]];
                var e1 = b - a;
                var e2 = c - a;
                var pvec = Vector3.Cross(direction, e2);
                float determinant = Vector3.Dot(e1, pvec);

                if (Math.Abs(determinant) < 1e-12f)
                    continue;

                float inverse = 1.0f / determinant;
                var tvec = origin - a;
                float u = Vector3.Dot(tvec, pvec) * inverse;

                if (u < 0.0f || u > 1.0f)
                    continue;

                var qvec = Vector3.Cross(tvec, e1);
                float v = Vector3.Dot(direction, qvec) * inverse;

                if (v < 0.0f || u + v > 1.0f)
                    continue;

                float distance = Vector3.Dot(e2, qvec) * inverse;

                if (distance > 1e-6f)
                    crossings++;
            }

            return (crossings & 1) == 1;
        }

        private static BoundingBox MeshBounds(PCGMesh mesh)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            var minimum = mesh.Vertices[0];
            var maximum = mesh.Vertices[0];

            for (int i = 1; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];
                minimum = Vector3.Min(minimum, v);
                maximum = Vector3.Max(maximum, v);
            }

            return new BoundingBox(minimum, maximum);
        }

        /// <summary>数出非流形边：每条边应恰好被两个三角形共用，否则就是破口。</summary>
        private static (int boundary, int total) EdgeReport(PCGMesh mesh)        {
            if (mesh == null || mesh.Vertices == null || mesh.Triangles == null)
                return (0, 0);

            // 顶点按位置量化合并：接缝处重复了一份顶点，不合并就会把接缝误判成两条边界边。
            var ids = new Dictionary<(long, long, long), int>();
            var merged = new int[mesh.VertexCount];

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var key = Quantise(mesh.Vertices[i]);

                if (!ids.TryGetValue(key, out int id))
                {
                    id = ids.Count;
                    ids.Add(key, id);
                }

                merged[i] = id;
            }

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

        private static void Bump(Dictionary<(int, int), int> counts, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        /// <summary>绕序与法线是否一致：几何法线必须与三个顶点法线的均值同向。</summary>
        private static bool WindingAgrees(PCGMesh mesh)
        {
            if (mesh == null || mesh.Normals == null || mesh.Triangles == null)
                return false;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int ia = mesh.Triangles[t * 3];
                int ib = mesh.Triangles[t * 3 + 1];
                int ic = mesh.Triangles[t * 3 + 2];
                var a = mesh.Vertices[ia];
                var b = mesh.Vertices[ib];
                var c = mesh.Vertices[ic];
                var geometric = Vector3.Cross(b - a, c - a);

                if (geometric.Length < 1e-9f)
                    continue;

                var vertexNormal = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];

                if (Vector3.Dot(geometric, vertexNormal) <= 0.0f)
                    return false;
            }

            return true;
        }

        /// <summary>找出第一个绕序与法线不一致的三角形，报出它的点积——用来分清"轻微扭曲"和"整圈翻面"。</summary>
        private static string FirstWindingBreak(PCGMesh mesh)
        {
            if (mesh == null || mesh.Normals == null || mesh.Triangles == null)
                return "网格为空";

            float worst = float.MaxValue;
            int worstTriangle = -1;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int ia = mesh.Triangles[t * 3];
                int ib = mesh.Triangles[t * 3 + 1];
                int ic = mesh.Triangles[t * 3 + 2];
                var geometric = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia],
                                              mesh.Vertices[ic] - mesh.Vertices[ia]);
                float length = geometric.Length;

                if (length < 1e-9f)
                    continue;

                var vertexNormal = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];
                float cosine = Vector3.Dot(geometric / length, vertexNormal) / Math.Max(1e-6f, vertexNormal.Length);

                if (cosine < worst)
                {
                    worst = cosine;
                    worstTriangle = t;
                }
            }

            return worstTriangle < 0
                ? "没有非退化三角形"
                : $"最差三角形 {worstTriangle} 余弦 {worst:F3}";
        }

        private static bool NoNaN(PCGMesh mesh)
        {
            if (mesh == null)
                return false;

            for (int i = 0; i < mesh.VertexCount; i++)
                if (!IsFinite(mesh.Vertices[i]))
                    return false;

            for (int i = 0; i < mesh.Normals.Length; i++)
                if (!IsFinite(mesh.Normals[i]))
                    return false;

            return true;
        }

        private static (long, long, long) Quantise(Vector3 v) =>
            ((long)Math.Round(v.X * 1000.0), (long)Math.Round(v.Y * 1000.0), (long)Math.Round(v.Z * 1000.0));
    }
}

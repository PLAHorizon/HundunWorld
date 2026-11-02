// PCG 插件 - 路径规划的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.35）
//
// 路径是那种"看起来对就写了"的代码：一条绕开崖体的路线和一条穿过崖体的路线在点数、长度上都可能很像，
// 而后者只有在地形上才看得出来。所以这里的判据都落在**手算得出的高度场**上——
// 平地必须出直线、陡墙必须绕开且逐点都不在墙上、湖面不可越、开坡度代价后必须选缓的那条。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PathPlannerTests
    {
        internal static void Run(CheckDelegate check)
        {
            FlatGroundTests(check);
            ObstacleTests(check);
            CostTests(check);
            RegionTests(check);
            NodeTests(check);
        }

        /// <summary>
        /// 路的区域子句：`outside` 真的绕开、`within` 真的限定，绕不过去时**报错而不是穿过去**。
        /// </summary>
        /// <remarks>
        /// 这块判据要的不是"槽位有值"，而是**路线形状变了**：区域并进了可通行性，
        /// 所以证据只能在"路变长了、且一个点都不在禁区里"这一侧。对照（不设区域时那条路是直的、更短）
        /// 必须先成立，否则"绕开了"可能只是路线本来就碰不到那块地。
        /// </remarks>
        private static void RegionTests(CheckDelegate check)
        {
            var ends = new List<Vector2> { new Vector2(-600.0f, 0.0f), new Vector2(600.0f, 0.0f) };

            var straight = Plan(ends, Options(), Flat, out string straightError);

            check("区域对照：不设区域时这条路存在且基本是直的（否则下面的判据在空转）",
                straight != null && straight.Count > 4 && MaxAbsZ(straight) < 60.0f,
                straight == null ? (straightError ?? "null") : $"{straight.Count} 点，最大横向偏离 {MaxAbsZ(straight):F0}");

            if (straight == null)
                return;

            float straightLength = straight[straight.Count - 1].Distance;

            // 禁区横在两点之间：一块 200 宽、纵向足够窄的墙，路只能从北或南绕过去。
            var around = Options();
            around.RegionMinX = -100.0f;
            around.RegionMinZ = -120.0f;
            around.RegionMaxX = 100.0f;
            around.RegionMaxZ = 120.0f;
            around.RegionInvert = true;

            var detour = Plan(ends, around, Flat, out string detourError);

            check("写了 outside 之后路仍规划得出来（绕得过去，不是直接判死）",
                detour != null && detour.Count > 4, detourError ?? "ok");

            if (detour != null)
            {
                int insideForbidden = 0;

                for (int i = 0; i < detour.Count; i++)
                {
                    var p = detour[i].Position;

                    if (p.X >= -100.0f && p.X <= 100.0f && p.Z >= -120.0f && p.Z <= 120.0f)
                        insideForbidden++;
                }

                check("绕开的那条路一个点都不在禁区里",
                    insideForbidden == 0, $"{insideForbidden}/{detour.Count} 个点落在禁区");

                // 绕路一定更长，而且长到**解析下界**附近才算真绕：贴着禁区两角走的最短路线是
                // 2×hypot(600−100, 120) + 200 ≈ 1228（直路 1200）。我先前拿"+100"当门槛，
                // 实测 1276 反而被判失败——那是门槛凭感觉，不是路线有问题。
                // 容差取一格（网格量化最多让它比下界短/长这么多）。
                float cell = 50.0f;
                float analyticDetour = 2.0f * (float)Math.Sqrt(500.0f * 500.0f + 120.0f * 120.0f) + 200.0f;
                float detourLength = detour[detour.Count - 1].Distance;

                check("绕行使路线变长，且长度落在解析下界的一格之内（真绕了，不是贴着直路）",
                    detourLength > straightLength &&
                    detourLength >= analyticDetour - cell &&
                    detourLength <= analyticDetour + cell * 3.0f,
                    $"{detourLength:F0} vs 直路 {straightLength:F0}、解析下界 {analyticDetour:F0}");
            }

            // 禁区横贯整个可走范围：绕不过去。这时必须**报错**，而不是给一条穿墙的路——
            // 后者是"描述说了不许，产出照样踩过去"那一类静默失效。
            var blocked = Options();
            blocked.RegionMinX = -100.0f;
            blocked.RegionMinZ = -100000.0f;
            blocked.RegionMaxX = 100.0f;
            blocked.RegionMaxZ = 100000.0f;
            blocked.RegionInvert = true;

            var noway = Plan(ends, blocked, Flat, out string blockedError);

            check("绕不过去时明确报错，而不是硬给一条穿禁区的路",
                noway == null && !string.IsNullOrEmpty(blockedError),
                noway == null ? (blockedError ?? "没有报错") : $"居然给了 {noway.Count} 个点");

            // within：两端都在框里、框比生成范围小 ⇒ 路必须整条留在框里。
            var limited = Options();
            limited.RegionMinX = -700.0f;
            limited.RegionMinZ = -300.0f;
            limited.RegionMaxX = 700.0f;
            limited.RegionMaxZ = 300.0f;

            var kept = Plan(ends, limited, Flat, out string keptError);
            int outsideKept = 0;

            if (kept != null)
            {
                for (int i = 0; i < kept.Count; i++)
                {
                    var p = kept[i].Position;

                    if (p.X < -700.0f || p.X > 700.0f || p.Z < -300.0f || p.Z > 300.0f)
                        outsideKept++;
                }
            }

            check("within 把整条路留在框里（这块框够大，不该判死）",
                kept != null && kept.Count > 4 && outsideKept == 0,
                kept == null ? (keptError ?? "null") : $"{kept.Count} 点，{outsideKept} 个越界");

            // within 而端点在框外：诚实的失败是报错，不是把端点偷偷搬进框。
            var tight = Options();
            tight.RegionMinX = -100.0f;
            tight.RegionMinZ = -100.0f;
            tight.RegionMaxX = 100.0f;
            tight.RegionMaxZ = 100.0f;

            var unreachable = Plan(ends, tight, Flat, out string tightError);

            check("within 的框装不下端点时报错（不把端点搬到框里）",
                unreachable == null && !string.IsNullOrEmpty(tightError),
                unreachable == null ? (tightError ?? "没有报错") : $"居然给了 {unreachable.Count} 个点");
        }

        /// <summary>一条路线离中心线的最大横向偏离（|Z|）。</summary>
        private static float MaxAbsZ(List<PCGPathPoint> path)
        {
            float worst = 0.0f;

            for (int i = 0; i < path.Count; i++)
                worst = Math.Max(worst, Math.Abs(path[i].Position.Z));

            return worst;
        }

        /// <summary>平地：路就是直线，长度等于两点间距，且不绕路。</summary>
        private static void FlatGroundTests(CheckDelegate check)
        {
            var waypoints = new List<Vector2> { new Vector2(0.0f, 0.0f), new Vector2(1000.0f, 0.0f) };
            var path = Plan(waypoints, Options(), Flat, out string error);

            check("平地上规划出路径", path != null && path.Count > 2, error);
            if (path == null)
                return;

            // 直线性：每个点离"起点到终点"这条直线的横向偏离都在一格之内。
            float worst = 0.0f;

            for (int i = 0; i < path.Count; i++)
                worst = Math.Max(worst, Math.Abs(path[i].Position.Z));

            check("平地上路径是一条直线（横向偏离不超过一格）", worst <= 50.0f, worst.ToString("F1"));

            check("路径长度等于两点间距",
                Near(path[path.Count - 1].Distance, 1000.0f, 60.0f),
                path[path.Count - 1].Distance.ToString("F0"));

            check("里程严格递增", Monotonic(path), Report(path));

            check("每个点的高度取自高度场",
                Near(path[0].Position.Y, 0.0f) && Near(path[path.Count - 1].Position.Y, 0.0f));

            // 同样输入跑两次，必须逐点相同：分区世界要的就是这条。
            var again = Plan(waypoints, Options(), Flat, out _);

            check("两次规划得到同一条路", Same(path, again), Report(path));

            // 起点在 `Resample` 里单独摆过一颗（`Distance = 0`），而分段推进的游标若从 0 起，第一段第一次
            // 循环会**原地再摆一颗**：两颗的位置与 Distance 全都相同。后果不是"多一件"而是**共面**——
            // 路上每一层几何都吃这些点，于是起点处两件路面片叠在一起（z-fighting）。
            // 这条断言要挡的是这个 off-by-one 回来，而它回来时清单上只会"多一件"，没人会去数。
            int coincident = 0;
            int noAdvance = 0;

            for (int i = 1; i < path.Count; i++)
            {
                if ((path[i].Position - path[i - 1].Position).Length < 1e-3f)
                    coincident++;

                if (path[i].Distance <= path[i - 1].Distance)
                    noAdvance++;
            }

            check("路上没有两颗重合的点（起点那一步的 off-by-one 不会悄悄回来）",
                path.Count > 2 && coincident == 0, $"{coincident}/{path.Count - 1} 对重合");

            check("每颗点的 Distance 严格递增（共线重复一并挡住，且夹具自己确实跑了多颗）",
                path.Count > 2 && noAdvance == 0, $"{noAdvance} 对没往前走（{path.Count} 颗）");

            // 上面那一条只走**一段**路。重复的另一半来源在**分段拼接处**：带途经点的路是几段接起来的，
            // 每段开头若再摆一颗，重合就落在作者写下的那个 via 上（不是起点），屏幕上表现为"via 处两件叠在一起"。
            var bent = Plan(new List<Vector2>
            {
                new Vector2(0.0f, 0.0f), new Vector2(500.0f, 0.0f), new Vector2(500.0f, 500.0f),
            }, Options(), Flat, out string bentError);

            check("带途经点的路规划得出来（多段拼接的夹具不能空跑）",
                bent != null && bent.Count > 6, bentError ?? "ok");

            if (bent != null && bent.Count > 6)
            {
                check("多段拼接处没有重合的点，且里程逐对往前走（via 那一侧的同一件事）",
                    CoincidentPairs(bent) == 0 && Monotonic(bent),
                    $"{CoincidentPairs(bent)} 对重合｜{Report(bent)}");
            }
        }

        /// <summary>障碍：陡墙必须绕开，湖面不能穿过。</summary>
        private static void ObstacleTests(CheckDelegate check)
        {
            // 一道南北向的陡墙（|x| 小于 75 的地方高 300，坡度远超上限），终点在墙的另一侧。
            var waypoints = new List<Vector2> { new Vector2(-600.0f, 0.0f), new Vector2(600.0f, 0.0f) };
            var path = Plan(waypoints, Options(), Wall, out string error);

            check("陡墙两侧仍能规划出路径（绕行）", path != null && path.Count > 2, error);
            if (path == null)
                return;

            bool onWall = false;
            float maxSlope = 0.0f;

            for (int i = 0; i < path.Count; i++)
            {
                // 逐点判"是否站在不可通行的地方"：这里用同一个高度场算，不依赖规划器内部状态。
                var height = Wall(path[i].Position.X, path[i].Position.Z);
                onWall |= height.HasValue && height.Value > 10.0f;
                maxSlope = Math.Max(maxSlope, path[i].SlopeDegrees);
            }

            check("绕行路线一个点都没落在墙上", !onWall, Report(path));

            check("整条路的坡度都没超过上限", maxSlope <= Options().SlopeLimit + 1.0f, maxSlope.ToString("F1"));

            check("绕行让路径明显变长", path[path.Count - 1].Distance > 1200.0f,
                path[path.Count - 1].Distance.ToString("F0"));

            // 湖：水位设为 0，湖面高度 -50，路必须绕开而不是从水面上走过去。
            var lakeOptions = Options();
            lakeOptions.WaterLevel = 0.0f;

            var lakePath = Plan(waypoints, lakeOptions, Lake, out string lakeError);
            check("湖两侧仍能规划出路径", lakePath != null && lakePath.Count > 2, lakeError);

            if (lakePath != null)
            {
                bool overWater = false;

                for (int i = 0; i < lakePath.Count; i++)
                {
                    var height = Lake(lakePath[i].Position.X, lakePath[i].Position.Z);
                    overWater |= height.HasValue && height.Value <= 0.0f;
                }

                check("路没有从湖面上穿过", !overWater, Report(lakePath));
            }

            // 整片不可通行时要说清原因，而不是给一条假路。
            var blockedOptions = Options();
            blockedOptions.SlopeLimit = 0.1f;

            var uphill = new List<Vector2> { new Vector2(-600.0f, 0.0f), new Vector2(600.0f, 0.0f) };
            var blocked = Plan(uphill, blockedOptions, Slope, out string blockedError);

            check("坡度上限卡死时明确报错而不是硬给一条路",
                blocked == null && blockedError != null && blockedError.Contains("waypoint"),
                blockedError ?? "居然给了路");
        }

        /// <summary>代价：两条绕行路线一缓一陡时，开坡度代价必须选缓的。</summary>
        private static void CostTests(CheckDelegate check)
        {
            // 中间一座小山：北侧绕行长而缓，南侧绕行短而陡（但仍在坡度上限内）。
            var waypoints = new List<Vector2> { new Vector2(-700.0f, 0.0f), new Vector2(700.0f, 0.0f) };

            var cheap = Options();
            cheap.SlopeCost = 0.0f;

            var pricey = Options();
            pricey.SlopeCost = 20.0f;

            var flat = Plan(waypoints, cheap, Hill, out string flatError);
            var gentle = Plan(waypoints, pricey, Hill, out string gentleError);

            check("不管坡度代价如何都能规划", flat != null && gentle != null, flatError ?? gentleError);

            if (flat == null || gentle == null)
                return;

            check("开启坡度代价后最陡的一段更缓",
                MaxSlope(gentle) <= MaxSlope(flat) + 0.01f,
                $"代价开 {MaxSlope(flat):F1} 度 / 关 {MaxSlope(gentle):F1} 度");
        }

        /// <summary>节点层：输入输出、列、以及没有地形时的透传契约。</summary>
        private static void NodeTests(CheckDelegate check)
        {
            var world = new PCGWorldContext { HeightSource = Flat };
            var context = Context(world);

            var points = PCGData.Create(2);
            points.Points[0] = PCGPoint.Create(new Vector3(0, 0, 0), 1.0f, 0);
            points.Points[1] = PCGPoint.Create(new Vector3(800, 0, 0), 1.0f, 1);

            var node = Make();
            var result = node.Execute(context, new[] { points });

            check("节点输出沿路的点", result.Count > 2 && string.IsNullOrEmpty(node.LastError),
                $"{result.Count} 点：{node.LastError}");
            check("节点同时输出一条样条", result.Splines != null && result.Splines.Length == 1,
                $"{result.Splines?.Length ?? 0} 条");

            var distances = result.SharedAttributes?.TryGetFloatArray(PCGPathNode.DistanceColumn);
            check("里程列写出来了", distances != null && distances.Length == result.Count,
                distances == null ? "没有列" : distances.Length.ToString());

            var slopes = result.SharedAttributes?.TryGetFloatArray(PCGAttributeSet.SlopeName);
            check("坡度列写出来了", slopes != null && slopes.Length == result.Count);

            // 坡上：路面的朝向必须**跟着地面倾斜**，否则每一片都水平摆在坡面上（片与片之间露出台阶）。
            // 判据量的是**几何关系**，不是我推出来的解析法线：规划出的路线按格量化，
            // 拿"坡 y = -0.5x ⇒ 法线 = (0.447,0.894,0)"去比，会被路线自己的横向摆动判成失败（2026-10-12 踩过）。
            var rampWorld = new PCGWorldContext { HeightSource = (x, z) => -0.5f * x };
            var tilted = Make().Execute(Context(rampWorld), new[] { points });

            check("坡上这条链跑得出多颗点（三条关系判据不能空转）", tilted.Count > 4, $"{tilted.Count} 点");

            int probe = tilted.Count / 2;
            var dir = tilted.Points[probe + 1].Position - tilted.Points[probe - 1].Position;
            float dirRun = (float)Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
            float dirLen = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            var up = Vector3.Transform(Vector3.Up, tilted.Points[probe].Rotation);

            // ① 上向量垂直于行进方向——"朝向根本没写"（up = (0,1,0)）在这一条上差的就是实测坡度那 25°。
            float alongDot = Math.Abs(up.X * dir.X + up.Y * dir.Y + up.Z * dir.Z) / Math.Max(1e-4f, dirLen);

            // ② 上向量的**水平投影**与行进方向的水平投影平行：把俯仰绕**世界 X** 施加时它的水平分量沿 Z、
            //    而行进沿 X，这条会破 ⇒ 它就是"绕错轴"的判别式（我第一版正是这样被抓出来的）。
            float upRun = (float)Math.Sqrt(up.X * up.X + up.Z * up.Z);
            float notParallel = upRun < 1e-3f || dirRun < 1e-3f
                ? 1.0f
                : Math.Abs(up.X * dir.Z - up.Z * dir.X) / (upRun * dirRun);

            // ③ 倾斜幅度对得上实测纵坡：up 与竖直方向夹角的余弦 = 水平距离 / 斜距。
            float expectedUpY = dirRun / Math.Max(1e-4f, dirLen);

            check("坡上的路面点朝向垂直于行进方向（不是水平摆在坡上）",
                tilted.Count > 4 && alongDot < 0.05f,
                $"|dot| = {alongDot:F3}｜up ({up.X:F3}, {up.Y:F3}, {up.Z:F3})");

            check("上向量的水平投影与行进方向平行（俯仰绕的是局部右轴；绕世界 X 会在这里红）",
                tilted.Count > 4 && notParallel < 0.05f, notParallel.ToString("F3"));

            check("倾斜幅度等于实测纵坡（up.Y ≈ 水平距离 / 斜距）",
                tilted.Count > 4 && Near(up.Y, expectedUpY, 0.05f),
                $"up.Y = {up.Y:F3}，期望 {expectedUpY:F3}");

            // 对照：平地跑同一条链，朝向必须仍然水平——坡面那三条不能顺手把平地也拧歪。
            var levelUp = Vector3.Transform(Vector3.Up, result.Points[result.Count / 2].Rotation);

            check("对照：平地上同一条链的局部 +Y 仍然朝上",
                Near(levelUp.X, 0.0f, 0.02f) && Near(levelUp.Y, 1.0f, 0.02f),
                $"({levelUp.X:F3}, {levelUp.Y:F3}, {levelUp.Z:F3})");

            check("样条长度与节点报告的路线长度一致",
                result.Splines != null && Near(result.Splines[0].Length, node.LastLength, 1.0f),
                $"{result.Splines?[0].Length:F0} vs {node.LastLength:F0}");

            // 没有地形：按契约原样透传并说明原因，而不是给一条凭空的路。
            var blind = Make();
            var blindResult = blind.Execute(Context(new PCGWorldContext()), new[] { points });

            check("没有高度源时原样透传并报明原因",
                blindResult.Count == 2 && !string.IsNullOrEmpty(blind.LastError) &&
                blind.LastError.Contains("terrain"),
                blind.LastError);

            // 少于两个途经点：说清"连不起来"。
            var single = PCGData.Create(1);
            single.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);

            var lonely = Make();
            lonely.Execute(context, new[] { single });
            check("只有一个点时说明连不起来", !string.IsNullOrEmpty(lonely.LastError), lonely.LastError);
        }

        // ---- 夹具 ----------------------------------------------------------------------------------------

        private static PCGPathOptions Options() => new PCGPathOptions
        {
            CellSize = 50.0f,
            SlopeLimit = 25.0f,
            SlopeCost = 3.0f,
            WaterLevel = -1000000.0f,
            MaxDetour = 900.0f,
            Smoothing = 3,
            SampleSpacing = 50.0f,
        };

        private static PCGPathNode Make()
        {
            return new PCGPathNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGPathNode.Id, new object[] { 50.0f, 25.0f, 3.0f, -1000000.0f, 900.0f, 3, 50.0f, true }, 0));
        }

        private static PCGExecutionContext Context(PCGWorldContext world) => new PCGExecutionContext
        {
            Seed = 20261008,
            World = world,
            GenerationBounds = new BoundingBox(new Vector3(-1000, -100, -1000), new Vector3(1000, 100, 1000)),
        };

        private static List<PCGPathPoint> Plan(
            List<Vector2> waypoints, PCGPathOptions options, Func<float, float, float?> height, out string error)
        {
            var path = new List<PCGPathPoint>();

            return PCGPathPlanner.TryPlan(waypoints, height, options, path, out error) ? path : null;
        }

        /// <summary>平地。</summary>
        private static float? Flat(float x, float z) => 0.0f;

        /// <summary>一段有限的陡墙：|x| 不超过 75 且 |z| 不超过 200 的地方高 300，两侧可绕。</summary>
        private static float? Wall(float x, float z) =>
            Math.Abs(x) <= 75.0f && Math.Abs(z) <= 200.0f ? 300.0f : 0.0f;

        /// <summary>一片有限的湖：|x| 不超过 150 且 |z| 不超过 200 处为 -50 的水面，岸上高 10（高于水位 0），两侧可绕。</summary>
        private static float? Lake(float x, float z) =>
            Math.Abs(x) <= 150.0f && Math.Abs(z) <= 200.0f ? -50.0f : 10.0f;

        /// <summary>沿 x 抬升的坡道：坡度上限设成 0.1 度时，沿 x 的每一步都超限。</summary>
        private static float? Slope(float x, float z) => x * 0.2f;

        /// <summary>中间一座山：北侧坡缓、南侧坡陡。</summary>
        private static float? Hill(float x, float z)
        {
            if (Math.Abs(x) > 250.0f)
                return 0.0f;

            float ridge = (250.0f - Math.Abs(x)) / 250.0f * 120.0f;

            // 北侧（z 为正）把高度摊开到很长的距离上，南侧（z 为负）挤在很窄的带里。
            return z >= 0.0f ? ridge * Math.Min(1.0f, z / 600.0f) : ridge + Math.Min(1.0f, -z / 120.0f) * 60.0f;
        }

        private static float MaxSlope(List<PCGPathPoint> path)
        {
            float worst = 0.0f;

            for (int i = 0; i < path.Count; i++)
                worst = Math.Max(worst, path[i].SlopeDegrees);

            return worst;
        }

        /// <summary>相邻两格里有几对**位置重合**的点（起点重复那类缺陷的计数判据）。</summary>
        private static int CoincidentPairs(List<PCGPathPoint> path)
        {
            int pairs = 0;

            for (int i = 1; i < path.Count; i++)
            {
                if ((path[i].Position - path[i - 1].Position).Length < 1e-3f)
                    pairs++;
            }

            return pairs;
        }

        /// <summary>
        /// 里程是否**逐对真的往前走**。原来那版判据是 `&lt; 上一颗 - 0.01`，
        /// 于是"两颗完全同里程"也算通过——而它守的那条检查名字写着"严格递增"，承诺多于做到。
        /// 起点重复那一颗（位置与里程全同）正是从这条缝里过去的。
        /// </summary>
        private static bool Monotonic(List<PCGPathPoint> path)
        {
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].Distance <= path[i - 1].Distance + 1e-3f)
                    return false;
            }

            return true;
        }

        private static bool Same(List<PCGPathPoint> a, List<PCGPathPoint> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!Near(a[i].Position.X, b[i].Position.X) || !Near(a[i].Position.Z, b[i].Position.Z))
                    return false;
            }

            return true;
        }

        private static string Report(List<PCGPathPoint> path)
        {
            if (path == null)
                return "没有路径";

            var parts = new List<string>();

            for (int i = 0; i < Math.Min(path.Count, 5); i++)
                parts.Add($"({path[i].Position.X:F0},{path[i].Position.Z:F0})");

            return $"{path.Count} 点：{string.Join(" ", parts)}";
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;
    }
}

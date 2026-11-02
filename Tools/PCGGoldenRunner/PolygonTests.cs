// PCG 插件 - 多边形（Polygon2D）与多边形筛选器的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.42）
//
// 这个类型只有一个承诺："点在不在这块地里"。而这块地**可以是凹的**——那正是它存在的理由：
// 矩形已经覆盖了凸的情形，凹形状（L 形院子、弯的河谷）在矩形下只能近似。所以断言的重心是
// **凹多边形的凹口必须算外面、而某个仍在地里但落在凸包之外的点必须算里面**（后者能杀掉"所有叉积同号"
// 那种凸性实现）。另外边界算里面、绕向不影响答案、写坏了不许静默清空，各有一条。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Data;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PolygonTests
    {
        /// <summary>L 形：从 (0,0) 起沿 +X 走 100、抬 +Z 走 40、回 -X 走 60、再抬 +Z 走 60、回 -X 走 40、落回。</summary>
        private const string LShape = "0 0; 100 0; 100 40; 40 40; 40 100; 0 100";

        private const string Square = "0 0; 100 0; 100 100; 0 100";

        internal static void Run(CheckDelegate check)
        {
            ParseTests(check);
            ContainmentTests(check);
            BoundaryTests(check);
            MeasureTests(check);
            NodeTests(check);
        }

        // ---- 顶点串 ---------------------------------------------------------

        private static void ParseTests(CheckDelegate check)
        {
            check("顶点串按顺序读成顶点表",
                PCGPolygon.TryParse(Square, out var square, out var squareError) && squareError == null &&
                square.Count == 4 &&
                Near(square.Vertices[0].X, 0.0f) && Near(square.Vertices[0].Y, 0.0f) &&
                Near(square.Vertices[1].X, 100.0f) && Near(square.Vertices[2].Y, 100.0f),
                square?.Describe());

            // 换行、多余空白、末尾分号：手写与从别处粘来的样子都得认。
            check("换行/多空白/末尾分号都容得下",
                PCGPolygon.TryParse("  0 0 ;\n 100 0;\n\t100 100 \n; 0 100 ; ", out var loose, out _) &&
                loose.Count == 4,
                loose?.Describe());

            check("空顶点串给一句能照着改的话",
                !PCGPolygon.TryParse("   ", out _, out var emptyError) &&
                emptyError != null && emptyError.Contains("三个顶点"),
                emptyError);

            check("某个顶点只写了一个数时，报的是**第几个**顶点",
                !PCGPolygon.TryParse("0 0; 100; 100 100", out _, out var shortError) &&
                shortError != null && shortError.Contains("第 2 个顶点") && shortError.Contains("1 个数"),
                shortError);

            check("某个顶点写了不是数的东西时，把那一段原样报出来",
                !PCGPolygon.TryParse("0 0; 100 0; x 100", out _, out var junkError) &&
                junkError != null && junkError.Contains("第 3 个顶点") && junkError.Contains("x 100"),
                junkError);

            // 少于三个顶点不算解析错误（"还没写完"与"写错了"是两件事），但**围不出面积**，判定恒不命中。
            check("两个顶点能解析、但判定什么都不收（不是全都收）",
                PCGPolygon.TryParse("0 0; 100 100", out var pair, out var pairError) &&
                pairError == null && !pair.IsUsable &&
                !pair.Contains(50.0f, 50.0f) && !pair.Contains(0.0f, 0.0f),
                pair?.Describe());
        }

        // ---- 在不在里面 -----------------------------------------------------

        private static void ContainmentTests(CheckDelegate check)
        {
            var square = Parse(Square);

            check("正方形：中心在里面、远处在外面",
                square.Contains(50.0f, 50.0f) && !square.Contains(150.0f, 50.0f) && !square.Contains(-1.0f, 50.0f),
                square.Describe());

            // 绕向不影响答案：奇偶规则与顶点顺序无关，而写描述的人不会关心自己按哪个方向描了一圈。
            var reversed = Parse("0 100; 100 100; 100 0; 0 0");

            bool sameAnswers = true;

            for (int x = -20; x <= 120; x += 10)
            {
                for (int z = -20; z <= 120; z += 10)
                {
                    if (square.Contains(x, z) != reversed.Contains(x, z))
                        sameAnswers = false;
                }
            }

            check("把顶点顺序倒过来，逐点答案一个字都不变（奇偶规则与绕向无关）", sameAnswers);

            // ---- 这一组是这份类型存在的理由：凹形状 ----
            var l = Parse(LShape);

            check("L 形：两条臂里各取一点，都在里面",
                l.Contains(80.0f, 20.0f) && l.Contains(20.0f, 80.0f),
                l.Describe());

            check("L 形的**凹口**判成外面（矩形做不到这件事）",
                !l.Contains(80.0f, 80.0f) && !l.Contains(60.0f, 90.0f),
                l.Describe());

            // 关键对照：这一点在 L 的凸包内、却仍在 L 地内（(20,60) 落在竖臂里）。
            // "所有叉积同号"的凸性判定会把**它**判成外面——所以这条断言正是用来杀掉那种实现的。
            check("对照：仍在地里但落在凸包之外的点必须算里面（凸性判定会误杀它）",
                l.Contains(20.0f, 60.0f) && l.Contains(60.0f, 20.0f),
                l.Describe());

            // 与矩形筛选器正面对照：同一个 L、同一批点，矩形只能给"含凹口那一版"的近似答案。
            var box = new PCGAreaFilterNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGAreaFilterNode.Id, new object[] { 0.0f, 100.0f, 0.0f, 100.0f, false }, 0));

            var probes = new PCGData(new[]
            {
                PCGPoint.Create(new Vector3(20.0f, 0.0f, 60.0f), 1.0f, 0),   // L 内、凸包内
                PCGPoint.Create(new Vector3(80.0f, 0.0f, 80.0f), 1.0f, 1),   // 凹口内、矩形内
            });

            int rectKept = box.Execute(Context(), new[] { probes })?.Count ?? -1;
            var polygon = Filter(probes, LShape, invert: false);

            check("同一批点：矩形把凹口里那个也收了，多边形只收地里的那个（能力差异的直接证据）",
                rectKept == 2 && polygon?.Count == 1 &&
                Near(polygon.Points[0].Position.X, 20.0f) && Near(polygon.Points[0].Position.Z, 60.0f),
                $"rect={rectKept} polygon={polygon?.Count ?? -1}");
        }

        // ---- 边界 -----------------------------------------------------------

        private static void BoundaryTests(CheckDelegate check)
        {
            var square = Parse(Square);

            check("正好落在边上的点算里面（沿地界摆一圈东西是很自然的用法）",
                square.Contains(0.0f, 50.0f) && square.Contains(50.0f, 100.0f) && square.Contains(100.0f, 100.0f),
                square.Describe());

            check("正好落在顶点上的点也算里面",
                square.Contains(0.0f, 0.0f) && square.Contains(100.0f, 0.0f),
                square.Describe());

            // 反面：贴着边但确实在外面一格的点不能被容差收进去——"贴边算里"不能变成"附近都算里"。
            check("紧贴外边（差 0.5）的点仍算外面（容差没有宽到把邻居收进来）",
                !square.Contains(-0.5f, 50.0f) && !square.Contains(50.0f, 100.5f),
                square.Describe());

            // 斜边上的中点：那边是浮点最容易出错的一条（叉积不为零但很小）。
            var wedge = Parse("0 0; 100 0; 0 100");

            check("斜边中点算里面、斜边外侧一点算外面",
                wedge.Contains(50.0f, 50.0f) && !wedge.Contains(60.0f, 60.0f),
                wedge.Describe());
        }

        // ---- 面积与包围盒 ---------------------------------------------------

        private static void MeasureTests(CheckDelegate check)
        {
            var square = Parse(Square);

            check("正方形面积 = 10000", Near(square.Area, 10000.0f), square.Area.ToString("F1"));

            var triangle = Parse("0 0; 10 0; 0 10");

            check("直角三角形面积 = 底 × 高 / 2", Near(triangle.Area, 50.0f), triangle.Area.ToString("F1"));

            // 有向面积的符号随绕向翻：它只被当作"绕向的符号"用，所以这里钉的就是"翻号"这件事本身。
            var forward = Parse(Square);
            var backward = Parse("0 100; 100 100; 100 0; 0 0");

            check("绕向反过来，有向面积翻号、面积不变",
                Math.Sign(forward.SignedArea) != Math.Sign(backward.SignedArea) &&
                Near(forward.Area, backward.Area),
                $"{forward.SignedArea:F1} vs {backward.SignedArea:F1}");

            var l = Parse(LShape);
            var (min, max) = l.Bounds;

            check("包围盒取到顶点范围的极值",
                Near(min.X, 0.0f) && Near(min.Y, 0.0f) && Near(max.X, 100.0f) && Near(max.Y, 100.0f),
                $"[{min.X},{min.Y}]..[{max.X},{max.Y}]");

            check("L 形的面积小于它的包围盒（形状确实缺了一块）",
                l.Area < 100.0f * 100.0f, l.Area.ToString("F1"));
        }

        // ---- 节点 -----------------------------------------------------------

        private static void NodeTests(CheckDelegate check)
        {
            var points = new PCGData(new[]
            {
                PCGPoint.Create(new Vector3(20.0f, 0.0f, 20.0f), 1.0f, 0),   // 里
                PCGPoint.Create(new Vector3(200.0f, 0.0f, 20.0f), 1.0f, 1),  // 外
                PCGPoint.Create(new Vector3(20.0f, 0.0f, 80.0f), 1.0f, 2),   // 里
                PCGPoint.Create(new Vector3(-50.0f, 0.0f, 50.0f), 1.0f, 3),  // 外
            });

            var kept = Filter(points, LShape, invert: false);

            check("节点：留下地里的两个、丢掉外面的两个，顺序不变",
                kept != null && kept.Count == 2 &&
                Near(kept.Points[0].Position.X, 20.0f) && Near(kept.Points[0].Position.Z, 20.0f) &&
                Near(kept.Points[1].Position.Z, 80.0f),
                kept == null ? "null" : $"{kept.Count} point(s)");

            var inverted = Filter(points, LShape, invert: true);

            check("Invert 反过来：留下刚才被丢掉的那两个",
                inverted != null && inverted.Count == 2 &&
                Near(inverted.Points[0].Position.X, 200.0f) && Near(inverted.Points[0].Position.Z, 20.0f),
                inverted == null ? "null" : $"{inverted.Count} point(s)");

            // 全中与全不中：两条边界路径都要走通（前者原样交下去，后者给空层）。
            // 注意这块"全都收"的地必须真的盖住四个点——夹具里那个点写在 x=-50。
            var all = Filter(points, "-500 -500; 500 -500; 500 500; -500 500", invert: false);

            check("全部命中时原样交出四个点",
                all != null && all.Count == 4, all?.Count.ToString() ?? "null");

            var none = Filter(points, "0 0; 1 0; 1 1; 0 1", invert: false);

            check("一个都不命中时给空层",
                none != null && none.Count == 0, none?.Count.ToString() ?? "null");

            // 写坏了：必须原样透传 + 说出原因，不能静默把整层扔空。
            var broken = Make("0 0; 100; 100 100", invert: false);
            var passed = broken.Execute(Context(), new[] { points });

            check("顶点串写坏时原样透传（不静默清空）并留下 LastError",
                passed != null && passed.Count == 4 && !string.IsNullOrEmpty(broken.LastError),
                broken.LastError);

            var tooFew = Make("0 0; 100 100", invert: false);

            tooFew.Execute(Context(), new[] { points });

            check("顶点少于三个时同样透传并说明围不出面积",
                !string.IsNullOrEmpty(tooFew.LastError) && tooFew.LastError.Contains("三个"),
                tooFew.LastError);

            check("这个节点标了线程安全（判定是纯算术、多边形只读）",
                Make(LShape, false).IsThreadSafe);

            check("参数个数与槽位表一致（两位：顶点串 + Invert）",
                PCGPolygonFilterNode.ParameterCount == 2);
        }

        // ---- 夹具 -----------------------------------------------------------

        private static PCGPolygon Parse(string text)
        {
            PCGPolygon.TryParse(text, out var polygon, out _);
            return polygon;
        }

        private static PCGPolygonFilterNode Make(string vertices, bool invert) =>
            new PCGPolygonFilterNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGPolygonFilterNode.Id, new object[] { vertices, invert }, 0));

        private static PCGData Filter(PCGData input, string vertices, bool invert) =>
            Make(vertices, invert).Execute(Context(), new[] { input });

        private static PCGExecutionContext Context() => new PCGExecutionContext
        {
            Seed = 20261010,
            GenerationBounds = new BoundingBox(new Vector3(-500.0f, -5.0f, -500.0f), new Vector3(500.0f, 5.0f, 500.0f)),
        };

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) < tolerance;
    }
}

// PCG 插件 - 把几何裁进一个凸区域（A ∩ B）的可执行断言
//
// 这一层的"对"是**解析体积**：立方体 ∩ 一个 2×6×2 的盒子（盒子比立方体高 ⇒ 只剩那一段 2×4×2）就是 16；
// ∩ 一个整块在里面的 2³ 就是 8；∩ 半径 1、高 6 的圆柱就是 πr²h。
//
// 另有三条反面对照，其中一条是**这一层设计被证伪的现场记录**：区域放在远处时结果必须为空——
// 这恰恰说明"按面依次裁"算的是**交**，所以它做不到"挖掉一个凸体"（凸体的补集是半空间的并集，
// 而所有朝外半空间的**交**是空集）。见 PCGMeshBoolean 的注解与文档 §7.6。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class ClipToConvexTests
    {
        internal static void Run(CheckDelegate check)
        {
            // ---- 纯核：解析体积 ----
            var cube = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));   // 64，跨 ±2

            check("夹具自证：立方体是 64 的体积、且本来水密",
                Near(Math.Abs(SignedVolume(cube)), 64.0f, 1e-3f) && EdgeReport(cube).boundary == 0,
                $"volume {Math.Abs(SignedVolume(cube)):F3}");

            // ① 盒子比立方体高（6 > 4）：交集是那一段 2×4×2 = 16。
            //    （这一刀曾经过不去：盒区域的第一个面法线就是 +X，而切割核心"沿 X 切盒子"曾经被拒——见 §7.6。）
            var slab = PCGMeshBoolean.BoxPlanes(Vector3.Zero, new Vector3(2.0f, 6.0f, 2.0f));
            bool slabOk = PCGMeshBoolean.TryIntersectConvex(cube, slab, out var clipped, out string slabError);

            check("裁进一个 2×6×2 的盒子：只剩那一段 2×4×2 = 16",
                slabOk && Near(Math.Abs(SignedVolume(clipped)), 16.0f, 1e-2f),
                slabError ?? $"{Math.Abs(SignedVolume(clipped)):F3}");
            check("裁完仍然水密（四个切口都封上了）",
                slabOk && EdgeReport(clipped).boundary == 0,
                slabOk ? EdgeReport(clipped).boundary.ToString() : slabError);

            // **已知边界**（与 §7.5 的残留同源）：**多刀**裁完之后，切口那一圈里会有三角形的几何法线与顶点法线相反
            // （单刀时 `MeshCutTests` 的"切口法线朝向"是对的，所以这条是多刀叠加才露出来的）。
            // 影响是那圈盖子在某些视角下被背面剔除掉；修好之后本行应改为"一致"。
            // 绕序本身（拓扑）用**与法线无关**的判据：同一条边被两个三角形共用时方向必须相反。
            check("多刀裁完之后绕序自洽（有向边两两相反）",
                slabOk && OrientationConsistent(clipped),
                slabOk ? (OrientationConsistent(clipped) ? "自洽" : WindingDetail(clipped)) : slabError);

            // **已修（同日）**：焊接加了"**法线同向**"这一条之后，硬边各自拆点、各带各的法线，于是这里的绕序
            // 与法线一致了（修之前最差那块面积 1.04、余弦 0.00——几何法线与那个法线**垂直**）。
            // 水密性一点没动：判据①按**位置**配对，拆点不影响它。
            check("裁完绕序与法线一致（硬边各自拆点之后，盖与墙各带各的法线）",
                slabOk && WindingAgrees(clipped),
                slabOk ? WindingDetail(clipped) : slabError);

            // ② 盒子整个在里头（2³）：交集就是那个小盒子 = 8。
            var inner = PCGMeshBoolean.BoxPlanes(Vector3.Zero, new Vector3(2.0f, 2.0f, 2.0f));

            check("盒子整块在里头时，交集就是它自己（2³ = 8）",
                PCGMeshBoolean.TryIntersectConvex(cube, inner, out var inside, out string insideError) &&
                Near(Math.Abs(SignedVolume(inside)), 8.0f, 1e-2f),
                insideError ?? "ok");

            // ③ **设计被证伪的现场**：区域在远处够不着 ⇒ 交集必须是空的。
            //    （第一版把这一档当成"挖凸体"，那时这里算出的是"整块消失"——正是它揭穿了那个前提。）
            var faraway = PCGMeshBoolean.BoxPlanes(new Vector3(100.0f, 0, 0), new Vector3(2.0f, 2.0f, 2.0f));

            check("区域在远处够不着时，交集为空（这一条揭穿了'按面依次裁 = 挖凸体'那个错前提）",
                PCGMeshBoolean.TryIntersectConvex(cube, faraway, out var far, out _) &&
                (far == null || far.TriangleCount == 0));

            // ④ 反面对照：区域把整块吞掉 ⇒ 原样保留 64。
            var swallow = PCGMeshBoolean.BoxPlanes(Vector3.Zero, new Vector3(20.0f, 20.0f, 20.0f));

            check("对照：区域比整块还大时，整块都留着（64）",
                PCGMeshBoolean.TryIntersectConvex(cube, swallow, out var kept, out _) &&
                Near(Math.Abs(SignedVolume(kept)), 64.0f, 1e-2f),
                $"{Math.Abs(SignedVolume(kept)):F3}");

            // ⑤ 圆柱区域：**已知边界**——64 边的棱柱里有侧面与立方体的表面**相切**时，那一刀的切口接不拢、
            // 整次裁剪被明确拒绝。早先的读数是 `shared by 1 segment(s), not 2`（点落在顶面 y=2、半径≈1 处），
            // 同日重跑已经换成盖子朝向那道逐片判据（见下面扫边数那段）——被拒这个事实没变，拒它的闸门变了。
            // 相切（平面正好擦过一条边/一个顶点）本来就是这一层的宽容档（见 §5.1.44 的边界与 §7.5），
            // 所以这里钉住"被拒绝"这个事实：修好之后本行会红，提示改成"切成功且水密、体积≈12.57"。
            // 盒区域那一组（16 / 8 / 64）已经把"解析体积"这半边钉住了，所以这里不必再靠弱断言凑数。
            var cylinder = PCGMeshBoolean.CylinderPlanes(Vector3.Zero, 1.0f, 6.0f, 64);

            bool cylinderOk = PCGMeshBoolean.TryIntersectConvex(cube, cylinder, out var bored, out string boreError);

            // **扫一遍边数**（8/16/32/64），每一档**逐面**切，报出"第几面开始失败"与那一刀的分档计数。
            // 目的是把"是不是擦边/相切"这件事变成数字：若只有细的棱柱被拒、粗的没事，就说明触发条件是
            // "某一面正好擦过网格的边/顶点"；若所有边数都被拒，那是另一回事。
            bool sixtyFourOk = false;
            string sixtyFourFailure = null;

            // 耳切那条支路的**自己**的证据：只靠"64 边多切过几面才撞墙"证明不了盖子真是耳接的
            // （死代码也能让失败点往后挪）。所以让分支自己报计数，并在**被采纳**的那一步当场验封闭性。
            int earAccepted = 0;
            int earBroken = 0;
            var sweep = new List<string>();

            foreach (int sides in new[] { 8, 16, 32, 64 })
            {
                var planes = PCGMeshBoolean.CylinderPlanes(Vector3.Zero, 1.0f, 6.0f, sides);
                var current = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));
                int failedAt = -1;
                string failure = null;
                var lastStats = default(PCGCutStats);

                for (int i = 0; i < planes.Count; i++)
                {
                    if (!PCGMeshCut.TryCutByPlane(current, planes[i].Point, planes[i].OutwardNormal, false,
                            out var next, out string stepError, out var stepStats))
                    {
                        failedAt = i;
                        failure = stepError;
                        lastStats = stepStats;

                        // **直接量那条假设**：有多少个顶点正好落在这张平面上（|d| ≤ ε 与 |d| ≤ 1e-3 各数一遍）。
                        // "刀口穿过网格的一个顶点"是前几轮从断口位置推出来的猜测；这里是它的直接证据——
                        // 若两个数都是 0，那条假设也就死了，得换一个方向（别再从"零长线段"那档下手）。
                        var unit = Vector3.Normalize(planes[i].OutwardNormal);
                        float epsilon = Math.Max(1.0f, PCGMeshBuilder.Bounds(current).Size.Length) * 1e-4f;
                        int onPlaneFine = 0;
                        int onPlaneCoarse = 0;

                        for (int v = 0; v < current.VertexCount; v++)
                        {
                            float d = Math.Abs(Vector3.Dot(current.Vertices[v] - planes[i].Point, unit));

                            if (d <= epsilon)
                                onPlaneFine++;

                            if (d <= 1e-3f)
                                onPlaneCoarse++;
                        }

                        failure += $"｜落在这张平面上的顶点：ε 内 {onPlaneFine} 个、1e-3 内 {onPlaneCoarse} 个" +
                                   $"（这张网格 {current.VertexCount} 顶点 / {current.TriangleCount} 面、ε={epsilon:0.######e+00}）";

                        // 顺手把"失败那一步的输入网格 + 这张平面"导出成夹具：手搓最小复现已失败两次、
                        // 纯推理失败七次，唯一的出路是**对着确切的输入**调（下游 `PrismFixtureTests` 用它复跑）。
                        DumpFixture(sides, current, planes[i], failure);
                        break;
                    }

                    lastStats = stepStats;

                    if (next != null && next.TriangleCount > 0)
                    {
                        // 采纳的那一步才计数：耳切跑了但结果被丢掉，不算它的证据。
                        earAccepted += stepStats.EarCaps;

                        if (stepStats.EarCaps > 0 && EdgeReport(next).boundary != 0)
                            earBroken++;

                        current = next;
                    }
                }

                bool ok = failedAt < 0;
                bool watertight = ok && EdgeReport(current).boundary == 0;

                if (sides == 64)
                {
                    sixtyFourOk = ok;
                    sixtyFourFailure = failure;
                }

                sweep.Add($"{sides} 边：{(ok ? $"成功 {Math.Abs(SignedVolume(current)):F3}、水密 {watertight}"
                                            : $"第 {failedAt} 面失败：{failure}")}｜末次 {lastStats}");

                // **8/16/32 边已经能做**（2026-10-11：去重判据改成"浮点噪声级"+ 接环清洗两步之后的读数）：
                // 这三档**必须**切成功且水密——**正面断言**，不再允许"明确拒绝"从旁边溜过去。
                // 64 边仍是已知缺陷，但**拒它的是哪一道闸门**要如实记（同日重跑读数）：
                // 现在拒它的是**盖子朝向那道逐片判据**（第 42 面：余弦 -0.994、面积 0.01，不是浮点噪声），
                // 更早两个读数（`shared by 1 segment(s), not 2`、出口判据①的未配对边）都已被前面的修复换掉。
                if (sides <= 32)
                    check($"{sides} 边棱柱：必须切成功且水密（低边数区域已经能做）", ok && watertight,
                        sweep[sweep.Count - 1]);
                else
                    check($"{sides} 边棱柱（已知缺陷 §7.6②）：要么切成功且水密、要么明确拒绝",
                        ok ? watertight : !string.IsNullOrEmpty(failure), sweep[sweep.Count - 1]);
            }

            // 判据要问"**今天是谁拒的**"：只问"拒了吗"，记录就会停在旧缺陷上而断言一直绿。
            // 2026-10-11 晚实测换代了一次：**盖子朝向那道已经不响了**（扇形翻面之后改走耳切，见 `PCGMeshCut`），
            // 于是同一档往后多切了 14 面，现在撞的是**出口判据①**（产物有未配对边）。
            // 所以这里钉两条：64 边仍被拒；而拒它的**必须**是出口判据①、**不再是**盖反——
            // 哪一条重新响，就说明对应的修法被改坏了。
            check("已知边界②：64 边棱柱仍被拒，且拒它的是出口水密判据（盖反那道已被耳切换掉；再变回来会红）",
                !sixtyFourOk && sixtyFourFailure != null &&
                sixtyFourFailure.Contains("not watertight") && !sixtyFourFailure.Contains("盖里有**反着**"),
                string.Join("；", sweep) + " ｜ 拒因原文：" + sixtyFourFailure);

            // 耳切这条路**真的跑过、且产物被采纳**——否则"多切了 14 面"可能只是别的原因。
            check("耳切这条路确实跑过并被采纳（不是只编译过的死分支）", earAccepted > 0,
                $"被采纳的耳切盖 {earAccepted} 个");
            check("耳切采纳的每一步都仍然水密", earBroken == 0,
                $"破口的采纳步 {earBroken} 个");

            // **反向对照**：规矩的盒子来一刀直的切口（正方形环，扇形必然成立）必须**不走**耳切。
            // 没有这一条，"耳切跑过了"也可能是在说"分支被无谓地天天进"——那等于扇形那条路已经废了。
            bool straightCut = PCGMeshCut.TryCutByPlane(PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f)),
                Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), true,
                out var halfBox, out string straightError, out var straightStats);

            check("反向对照：盒子的直刀仍走扇形（EarCaps 必须为 0，且产物水密、体积是一半）",
                straightCut && straightStats.EarCaps == 0 && EdgeReport(halfBox).boundary == 0 &&
                Near(Math.Abs(SignedVolume(halfBox)), 32.0f, 1e-3f),
                straightError ?? $"{straightStats}、体积 {Math.Abs(SignedVolume(halfBox)):F3}");

            // **演示那块石料的确切几何**（2026-10-11 补）：8×12×8 的方料裁进一个 12 边、直径 6、高 20 的圆柱。
            // 这一条是**被引擎侧重烘焙抓出来的回归钉**：`PCGMeshCut` 新加的"盖逐片验朝向"第一版把这种几何里的
            // **碎片**（面积 0.00、余弦 −0.852）当成"盖反了"，于是演示的 6 块石料全被"原样保留"——而当时
            // 离屏的盒子/棱柱夹具**全是绿的**（那种碎片只在"方料 ∩ 多边形圆柱"里出现）。所以把演示的**确切参数**
            // 钉在这里：它比"再多一个抽象夹具"更值钱，因为它就是那个漏网的形状。
            var stoneMaterial = PCGMeshBuilder.Box(new Vector3(8.0f, 12.0f, 8.0f));
            var stonePlanes = PCGMeshBoolean.CylinderPlanes(Vector3.Zero, 3.0f, 20.0f, 12);

            bool stoneOk = PCGMeshBoolean.TryIntersectConvex(stoneMaterial, stonePlanes,
                out var stoneClipped, out string stoneError);
            float stoneVolume = stoneOk && stoneClipped != null ? Math.Abs(SignedVolume(stoneClipped)) : 0.0f;

            check("演示石料（8×12×8 方料 ∩ 12 边圆柱）裁得动、水密、且真的被削掉了（回归钉：碎片不能当成盖反了）",
                stoneOk && stoneClipped != null && stoneClipped.TriangleCount > 0 &&
                EdgeReport(stoneClipped).boundary == 0 && stoneVolume > 1.0f && stoneVolume < 767.0f,
                stoneOk ? $"{stoneClipped?.TriangleCount} 面、破口边 {EdgeReport(stoneClipped).boundary}、体积 {stoneVolume:F2}"
                        : stoneError);

            // ⑥ "凸"这个前提是被检查过的：面数不够（围不出立体）就拒绝，而不是硬算。
            check("区域面数 < 4 时明确拒绝（说明'凸'这个前提真的被检查）",
                !PCGMeshBoolean.TryIntersectConvex(cube,
                    new List<PCGConvexPlane> { PCGConvexPlane.At(Vector3.Zero, Vector3.UnitY) }, out _, out _));

            // ---- 节点：两种承载、原样保留、DryRun ----
            var context = new PCGExecutionContext { Seed = 7 };
            var layer = PCGData.Create(2);
            layer.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 1);
            layer.Points[1] = PCGPoint.Create(new Vector3(50, 0, 0), 1.0f, 2);

            // 对齐承载：两块立方体（网格建在自己原点、摆放来自点）。
            var aligned = layer.Clone(cloneAttributes: false);
            aligned.Meshes = new[] { cube, cube };

            var alignedResult = MakeNode().Execute(context, new[] { aligned });

            check("对齐承载（Meshes）：件数不变、每块都被裁到 16",
                alignedResult.Meshes != null && alignedResult.Meshes.Length == 2 &&
                Near(Math.Abs(SignedVolume(alignedResult.Meshes[0])), 16.0f, 1e-2f) &&
                Near(Math.Abs(SignedVolume(alignedResult.Meshes[1])), 16.0f, 1e-2f),
                alignedResult.Meshes != null ? Math.Abs(SignedVolume(alignedResult.Meshes[0])).ToString("F3") : "null");

            // 独立几何承载：一块"只有几何没有点"的层也要被裁。
            var loose = layer.Clone(cloneAttributes: false);
            loose.Geometry = new[]
            {
                new PCGGeometryItem { Mesh = cube, Placement = new Transform(Vector3.Zero, Quaternion.Identity, Float3.One) },
            };

            var looseResult = MakeNode().Execute(context, new[] { loose });

            check("独立几何承载（Geometry）：一样被裁（64 → 16）",
                looseResult.Geometry != null && looseResult.Geometry.Length == 1 &&
                Near(Math.Abs(SignedVolume(looseResult.Geometry[0].Mesh)), 16.0f, 1e-2f),
                $"{Math.Abs(SignedVolume(looseResult.Geometry[0].Mesh)):F3}");

            // 裁不动的那一块**原样保留**，并把原因记下来（配正面同夹具：同一层里的封闭网格确实被裁了）。
            // 夹具是一块开放网格（格栅）——平面切割对开放网格会拒绝，于是"裁不动"这条真的被走到。
            var open = PCGMeshBuilder.Grid(new Vector2(4.0f, 4.0f), 2);
            var mixed = layer.Clone(cloneAttributes: false);
            mixed.Meshes = new[] { open, cube };

            var mixedNode = MakeNode();
            var mixedResult = mixedNode.Execute(context, new[] { mixed });

            check("裁不动的那一块原样保留（交回的还是同一份网格，没有被改过）",
                mixedResult.Meshes != null && ReferenceEquals(mixedResult.Meshes[0], open));
            check("同一层里的封闭那一块照裁（上一条的正面同夹具）",
                mixedResult.Meshes != null && Near(Math.Abs(SignedVolume(mixedResult.Meshes[1])), 16.0f, 1e-2f),
                $"{Math.Abs(SignedVolume(mixedResult.Meshes[1])):F3}");
            check("裁不动时原因写进 LastError（不是悄悄留一块没裁的）",
                !string.IsNullOrEmpty(mixedNode.LastError), mixedNode.LastError ?? "无");

            // DryRun（预览）照常裁：它是纯数据变换，`DryRun` 挡的是"建 Actor"那一类。
            var preview = MakeNode().Execute(context.AsDryRun(), new[] { aligned });

            check("预览运行也裁（与烘焙同一份数据）",
                preview.Meshes != null && Near(Math.Abs(SignedVolume(preview.Meshes[0])),
                    Math.Abs(SignedVolume(alignedResult.Meshes[0])), 1e-4f),
                $"{Math.Abs(SignedVolume(preview.Meshes[0])):F3} vs {Math.Abs(SignedVolume(alignedResult.Meshes[0])):F3}");

            // 没有几何的层：透传 + 说明原因。
            var bare = MakeNode();
            var bareResult = bare.Execute(context, new[] { layer.Clone(cloneAttributes: false) });

            check("输入没有几何时透传并说明原因",
                bareResult != null && !string.IsNullOrEmpty(bare.LastError),
                bare.LastError ?? "无");

            check("参数个数与槽位表一致（四位：形状/中心/尺寸/边数）",
                PCGClipToConvexNode.ParameterCount == 4);

            // 用 sweep 导出的夹具复跑那一刀——"一分钟回路"。
            PrismFixtureTests(check);

            // ---- 描述层：一句话就能调到它（`clip` 是今天才接上的词） ----
            bool parsed = PCGSpecParser.TryParse(
                "world 裁切试跑\nscatter pillars points 8 geometry box geometry-size 3 3 3 clip box 2 6 2",
                out var spec, out string parseError);

            check("描述层：`clip box <x> <y> <z>` 落进形状与尺寸两个字段",
                parsed && spec.Scatters.Count == 1 && spec.Scatters[0].ClipShape == "box" &&
                spec.Scatters[0].ClipSize.HasValue &&
                Near(spec.Scatters[0].ClipSize.Value.X, 2.0f) &&
                Near(spec.Scatters[0].ClipSize.Value.Y, 6.0f) &&
                Near(spec.Scatters[0].ClipSize.Value.Z, 2.0f),
                parseError ?? (parsed ? spec.Scatters[0].ClipShape : "没解析出来"));

            bool cylinderParsed = PCGSpecParser.TryParse(
                "world 裁切试跑\nscatter pillars points 8 geometry cylinder geometry-size 3 3 3 clip cylinder 2 6 24",
                out var cylinderSpec, out _);

            check("描述层：`clip cylinder <直径> <高> <边数>` 里第三个数是**边数**不是厚度",
                cylinderParsed && cylinderSpec.Scatters[0].ClipShape == "cylinder" &&
                Near(cylinderSpec.Scatters[0].ClipSize.Value.X, 2.0f) &&
                Near(cylinderSpec.Scatters[0].ClipSize.Value.Y, 6.0f) &&
                cylinderSpec.Scatters[0].ClipSegments == 24);

            // 编译之后图里**真的挂上**了那个节点（不是"解析过了但没人用"）。
            var compiled = PCGSpecCompiler.Compile(spec, name => default);
            bool hasClip = false;

            if (compiled.Ok && compiled.Graph != null)
                for (int i = 0; i < compiled.Graph.NodeCount; i++)
                    hasClip |= compiled.Graph.Nodes[i].TypeId == PCGClipToConvexNode.Id;

            check("描述层：编译后的图里真的挂了 ClipToConvex 节点", compiled.Ok && hasClip,
                compiled.Ok ? (hasClip ? "挂了" : "没挂上") : compiled.Error);

            // 对照：不写 clip 的同一份描述，图里不该有它（否则"挂上了"可能只是别的链带的）。
            PCGSpecParser.TryParse(
                "world 裁切试跑\nscatter pillars points 8 geometry box geometry-size 3 3 3",
                out var plain, out _);

            var plainCompiled = PCGSpecCompiler.Compile(plain, name => default);
            bool plainHasClip = false;

            if (plainCompiled.Ok && plainCompiled.Graph != null)
                for (int i = 0; i < plainCompiled.Graph.NodeCount; i++)
                    plainHasClip |= plainCompiled.Graph.Nodes[i].TypeId == PCGClipToConvexNode.Id;

            check("对照：同一份描述不写 clip 时，图里没有这个节点",
                plainCompiled.Ok && !plainHasClip);

            // 三条带行号的错误：不是几何层、形状不认识、圆柱边数越界。
            check("描述层：`clip` 写在非几何层上报错",
                !PCGSpecParser.TryParse("world x\nscatter trees points 4 model \"a.flax\" clip box 2 2 2",
                    out _, out string wrongLayer) && wrongLayer != null && wrongLayer.Contains("geometry"));

            check("描述层：不认识的裁切形状报错并列出能用的",
                !PCGSpecParser.TryParse("world x\nscatter p points 4 geometry box geometry-size 1 1 1 clip cone 1 1 1",
                    out _, out string badShape) && badShape != null && badShape.Contains("cone"));

            check("描述层：圆柱边数越界报错",
                !PCGSpecParser.TryParse("world x\nscatter p points 4 geometry box geometry-size 1 1 1 clip cylinder 1 1 2",
                    out _, out string badSides) && badSides != null && badSides.Contains("3..128"));
        }

        // ---- 夹具 ------------------------------------------------------------------------------------------

        /// <summary>
        /// 仓库里的夹具目录（从程序集目录往上找 `Tools/PCGGoldenRunner`）；找不到返回 null。
        /// </summary>
        private static string FixtureFolder()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "Tools", "PCGGoldenRunner");

                if (System.IO.Directory.Exists(candidate))
                {
                    string fixtures = System.IO.Path.Combine(candidate, "fixtures");
                    System.IO.Directory.CreateDirectory(fixtures);
                    return fixtures;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 把"失败那一步的**输入网格** + 那张平面"原样导出成一个夹具文件。
        /// </summary>
        /// <remarks>
        /// 为什么要它：这一档（棱柱区域被拒）已经用**手搓的**最小复现失败过两次、纯推理失败过七次。
        /// 手搓的夹具是"我以为的形状"，而真正失败的是一张 100~500 个三角形的碎片实体——**导出来**才谈得上调。
        /// 导出是确定性的，所以它既是诊断，也是一份可回归的真实输入。
        /// </remarks>
        private static void DumpFixture(int sides, PCGMesh mesh, PCGConvexPlane plane, string refusal)
        {
            string folder = FixtureFolder();

            if (folder == null || mesh == null || mesh.Triangles == null)
                return;

            var text = new System.Text.StringBuilder();
            text.AppendLine($"# prism-{sides}: 失败那一步的输入网格 + 那张平面（由 sweep 自动导出）");
            text.AppendLine($"# refusal: {refusal}");
            text.AppendLine($"plane {plane.Point.X:R} {plane.Point.Y:R} {plane.Point.Z:R} " +
                            $"{plane.OutwardNormal.X:R} {plane.OutwardNormal.Y:R} {plane.OutwardNormal.Z:R}");
            text.AppendLine($"counts {mesh.VertexCount} {mesh.TriangleCount}");

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];
                var n = mesh.Normals != null && i < mesh.Normals.Length ? mesh.Normals[i] : Vector3.Up;
                var uv = mesh.UVs != null && i < mesh.UVs.Length ? mesh.UVs[i] : Vector2.Zero;
                text.AppendLine($"v {v.X:R} {v.Y:R} {v.Z:R} {n.X:R} {n.Y:R} {n.Z:R} {uv.X:R} {uv.Y:R}");
            }

            for (int t = 0; t < mesh.TriangleCount; t++)
                text.AppendLine($"t {mesh.Triangles[t * 3]} {mesh.Triangles[t * 3 + 1]} {mesh.Triangles[t * 3 + 2]}");

            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, $"prism-{sides}.tsv"), text.ToString());
        }

        /// <summary>
        /// 用**导出的夹具**复跑那一刀——"一分钟回路"：改核心之后跑这一条就知道有没有动到它。
        /// </summary>
        /// <remarks>
        /// 夹具是 <c>sweep</c> 在失败时自己导出的（失败那一步的输入网格 + 那张平面）。没有夹具文件时**明说**
        /// 在跳过——"夹具缺失所以没跑"与"跑绿了"必须长得不一样。
        /// </remarks>
        private static void PrismFixtureTests(CheckDelegate check)
        {
            string folder = FixtureFolder();

            if (folder == null)
            {
                check("棱柱失败夹具可定位", false, "往上找不到 Tools/PCGGoldenRunner");
                return;
            }

            var files = System.IO.Directory.GetFiles(folder, "prism-*.tsv");

            check("棱柱失败夹具已导出（跑一次 sweep 就会生成；缺了这条会明说）", files.Length > 0, $"{files.Length} 份");

            foreach (var file in files)
            {
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                var planePoint = Vector3.Zero;
                var planeNormal = Vector3.UnitY;

                foreach (var line in System.IO.File.ReadAllLines(file))
                {
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    var parts = line.Split(' ');

                    if (parts[0] == "plane")
                    {
                        planePoint = new Vector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
                        planeNormal = new Vector3(Parse(parts[4]), Parse(parts[5]), Parse(parts[6]));
                    }
                    else if (parts[0] == "v")
                    {
                        vertices.Add(new Vector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                        normals.Add(new Vector3(Parse(parts[4]), Parse(parts[5]), Parse(parts[6])));
                        uvs.Add(new Vector2(Parse(parts[7]), Parse(parts[8])));
                    }
                    else if (parts[0] == "t")
                    {
                        triangles.Add(int.Parse(parts[1]));
                        triangles.Add(int.Parse(parts[2]));
                        triangles.Add(int.Parse(parts[3]));
                    }
                }

                var mesh = new PCGMesh
                {
                    Vertices = vertices.ToArray(),
                    Normals = normals.ToArray(),
                    UVs = uvs.ToArray(),
                    Triangles = triangles.ToArray(),
                };

                bool ok = PCGMeshCut.TryCutByPlane(mesh, planePoint, planeNormal, false,
                    out var result, out string error, out var stats);

                // **对着确切的输入**独立走一遍分类（照着 PCGMeshCut 的判据写），把"跨了平面却交不出两个点"
                // 的三角形揪出来——`onPlane.Count != 2` 是唯一还没排除的"静默丢线段"路径。
                var unit = Vector3.Normalize(planeNormal);
                float epsilon = Math.Max(1.0f, PCGMeshBuilder.Bounds(mesh).Size.Length) * 1e-4f;
                int insideTri = 0;
                int outsideTri = 0;
                int crossingTri = 0;
                int twoPoints = 0;
                int oddPoints = 0;
                var edgesHistogram = new SortedDictionary<int, int>();
                var suspects = new List<string>(4);

                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    float da = Vector3.Dot(mesh.Vertices[mesh.Triangles[t * 3]] - planePoint, unit);
                    float db = Vector3.Dot(mesh.Vertices[mesh.Triangles[t * 3 + 1]] - planePoint, unit);
                    float dc = Vector3.Dot(mesh.Vertices[mesh.Triangles[t * 3 + 2]] - planePoint, unit);

                    if (da > epsilon && db > epsilon && dc > epsilon)
                    {
                        insideTri++;
                        continue;
                    }

                    if (da < -epsilon && db < -epsilon && dc < -epsilon)
                    {
                        outsideTri++;
                        continue;
                    }

                    crossingTri++;

                    int edges = 0;

                    if ((da > epsilon) != (db > epsilon))
                        edges++;

                    if ((db > epsilon) != (dc > epsilon))
                        edges++;

                    if ((dc > epsilon) != (da > epsilon))
                        edges++;

                    edgesHistogram.TryGetValue(edges, out int seenEdges);
                    edgesHistogram[edges] = seenEdges + 1;

                    if (edges == 2)
                        twoPoints++;
                    else
                    {
                        oddPoints++;

                        if (suspects.Count < 4)
                            suspects.Add($"#{t} d=({da:0.#####e+00},{db:0.#####e+00},{dc:0.#####e+00}) 交点{edges}");
                    }
                }

                var histogramText = new List<string>(4);

                foreach (var pair in edgesHistogram)
                    histogramText.Add($"交点{pair.Key}×{pair.Value}");

                string classification = $"｜分类：内 {insideTri} / 外 {outsideTri} / 跨 {crossingTri}" +
                                        $"（{string.Join("、", histogramText)}；交出两点 {twoPoints}、否则 **{oddPoints}**）" +
                                        (suspects.Count > 0 ? "；" + string.Join("、", suspects) : string.Empty) +
                                        $"｜**输入自身**破口边 {EdgeReport(mesh).boundary}（距离焊接 1e-4）";

                // 落在容差带里的顶点（|d| ≤ ε）逐个列出来：上面那批"跨了平面却交不出两点"的三角形，
                // 嫌疑全在那几个顶点上；列出它们的下标与参与几个三角形，才谈得上判"该怎么归"。
                var bandVertices = new List<int>();

                for (int v = 0; v < mesh.VertexCount; v++)
                    if (Math.Abs(Vector3.Dot(mesh.Vertices[v] - planePoint, unit)) <= epsilon)
                        bandVertices.Add(v);

                var bandReport = new List<string>(2);

                for (int b = 0; b < bandVertices.Count && b < 4; b++)
                {
                    int v = bandVertices[b];
                    float d = Vector3.Dot(mesh.Vertices[v] - planePoint, unit);
                    int used = 0;

                    for (int t = 0; t < mesh.TriangleCount; t++)
                        if (mesh.Triangles[t * 3] == v || mesh.Triangles[t * 3 + 1] == v || mesh.Triangles[t * 3 + 2] == v)
                            used++;

                    bandReport.Add($"#{v} d={d:0.#####e+00} @{mesh.Vertices[v]}（{used} 个三角形）");
                }

                if (bandVertices.Count > 0)
                    classification += $"｜带内顶点 {bandVertices.Count} 个：" + string.Join("、", bandReport);

                // 夹具文件名里带着边数（`prism-16.tsv`）：低边数的那些**必须**切成功且水密，
                // 高边数的仍是已知缺陷（要么成功、要么明确拒绝）。低边数这一档从"钉着"翻成正面断言，
                // 是因为 2026-10-11 把"去重半径 = 焊接半径"对齐之后它们**真的**切出来了（16 边 628 面、破口边 0）。
                int fixtureSides = 0;
                string fileStem = System.IO.Path.GetFileNameWithoutExtension(file);
                int digitRun = 0;

                for (int c = 0; c < fileStem.Length; c++)
                {
                    if (fileStem[c] >= '0' && fileStem[c] <= '9')
                        digitRun = digitRun * 10 + (fileStem[c] - '0');
                    else if (digitRun > 0)
                        break;   // 第一段数字就是边数
                }

                fixtureSides = digitRun;

                bool fixtureWatertight = ok && result != null && result.TriangleCount > 0 &&
                                         EdgeReport(result).boundary == 0;
                string fixtureDetail = ok
                    ? $"{result.TriangleCount} 面、破口边 {EdgeReport(result).boundary}；{stats}{classification}"
                    : $"{error}；{stats}{classification}";

                if (fixtureSides > 0 && fixtureSides <= 32)
                    check($"夹具 {System.IO.Path.GetFileName(file)}（{vertices.Count} 顶点 / {triangles.Count / 3} 面）：必须切成功且水密",
                        fixtureWatertight, fixtureDetail);
                else
                    check($"夹具 {System.IO.Path.GetFileName(file)}（{vertices.Count} 顶点 / {triangles.Count / 3} 面）：" +
                          "要么切成功且水密、要么明确拒绝",
                        ok ? fixtureWatertight : true, fixtureDetail);
            }
        }

        private static float Parse(string token) =>
            float.Parse(token, System.Globalization.CultureInfo.InvariantCulture);

        private static PCGClipToConvexNode MakeNode() =>
            new PCGClipToConvexNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGClipToConvexNode.Id,
                new object[] { PCGClipToConvexNode.BoxShape, Vector3.Zero, new Vector3(2.0f, 6.0f, 2.0f), 16 },
                0));

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        /// <summary>有向体积（散度定理）。</summary>
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

        /// <summary>统计破口边（顶点按**距离**合并；桶式的键会在桶边界上劈叉，见文档 §7.5）。</summary>
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

        /// <summary>把每个顶点映到"同一个位置"的下标：合并半径内的点算同一个（3×3×3 邻域查表）。</summary>
        private static int[] Weld(Vector3[] vertices, float epsilon)
        {
            var buckets = new Dictionary<(long, long, long), List<int>>();
            var positions = new List<Vector3>();
            var ids = new int[vertices.Length];
            float cell = Math.Max(epsilon * 2.0f, 1e-6f);
            float squared = epsilon * epsilon;

            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var here = Cell(p, cell);
                int found = -1;

                for (long dx = -1; dx <= 1 && found < 0; dx++)
                    for (long dy = -1; dy <= 1 && found < 0; dy++)
                        for (long dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!buckets.TryGetValue((here.Item1 + dx, here.Item2 + dy, here.Item3 + dz), out var list))
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

                    if (!buckets.TryGetValue(here, out var list))
                    {
                        list = new List<int>(4);
                        buckets.Add(here, list);
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

        /// <summary>
        /// 绕序不一致里"最差那个三角形"的**面积**与余弦。
        /// </summary>
        /// <remarks>
        /// 用来分清三种东西：**整片翻面**（面积正常、余弦接近 −1）、**轻微扭曲**（余弦小负），以及
        /// **退化片**（面积≈0——那种三角形的几何法线本来就是噪声，绕序一致与否在那上面不是个有意义的性质）。
        /// 先量再判：不量就把"面积 1e-9 的碎片"当成缺陷，会去修一个不存在的问题。
        /// </remarks>
        private static string WindingDetail(PCGMesh mesh)
        {
            if (mesh == null || mesh.Normals == null || mesh.Triangles == null)
                return "网格为空";

            float worstCosine = float.MaxValue;
            float worstArea = 0.0f;
            int worst = -1;

            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                int ia = mesh.Triangles[t * 3];
                int ib = mesh.Triangles[t * 3 + 1];
                int ic = mesh.Triangles[t * 3 + 2];
                var geometric = Vector3.Cross(mesh.Vertices[ib] - mesh.Vertices[ia],
                                              mesh.Vertices[ic] - mesh.Vertices[ia]);
                float area = geometric.Length * 0.5f;
                var vertexNormal = mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic];
                float denominator = Math.Max(1e-12f, geometric.Length * vertexNormal.Length);
                float cosine = vertexNormal.Length < 1e-9f ? 1.0f : Vector3.Dot(geometric, vertexNormal) / denominator;

                if (cosine < worstCosine)
                {
                    worstCosine = cosine;
                    worstArea = area;
                    worst = t;
                }
            }

            return worst < 0
                ? "没有三角形"
                : $"最差三角形 #{worst} 面积 {worstArea:0.######e+00}、余弦 {worstCosine:F3}";
        }

        /// <summary>
        /// 绕序是否**自洽**：同一条边被两个三角形共用时，两个方向必须相反（顶点先按位置焊）。
        /// </summary>
        /// <remarks>
        /// 这是"绕序一致"的正身，而且**与法线无关**——所以它不会被"硬边上共享一个法线"这种**表示**问题带偏。
        /// 之前用"几何法线与三个顶点法线的均值同向"来判，实测把硬边上的三角形全判成"翻了"：
        /// 最差那个三角形面积 **1.04**（不是碎片）、余弦正好 **0.00**（几何法线与那个法线**垂直**）——
        /// 那正是"墙的三角形带着盖的法线"的样子（累加器按位置焊接顶点、焊接时保留**先插进来的**法线）。
        /// 拿表示问题当拓扑缺陷，会去修一个不存在的东西；判据换成"有向边两两相反"之后，
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
                // 每条有向边都必须有恰好一条反向边与之配对（退化边 a==b 不参与）。
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

        /// <summary>绕序与法线是否一致。</summary>
        private static bool WindingAgrees(PCGMesh mesh)
        {
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

                if (Vector3.Dot(geometric, mesh.Normals[ia] + mesh.Normals[ib] + mesh.Normals[ic]) <= 0.0f)
                    return false;
            }

            return true;
        }
    }
}

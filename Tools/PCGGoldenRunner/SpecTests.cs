// PCG 插件 - 设定描述层的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.39）
//
// "读一段描述生成场景"最怕两件事：描述写对了而图里没有（字段拼错、选项没落地），以及描述写错了却没人说
// （静默用默认值）。所以断言分两层——**解析**逐条钉住"值落到哪个字段"与"每类错都带行号报出来"，
// **编译**逐条钉住"值落到哪个槽位"与"连线接在哪个引脚上"，最后走一遍真编译真运行。

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class SpecTests
    {
        internal static void Run(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            ParseTests(check);
            ParseErrorTests(check);
            OptionCoverageTests(check);
            CompileTests(check);
            RepeatableFeatureTests(check);
            RoadViaTests(check);
            BiomeFilterTests(check);
            RelationTests(check);
            AreaTests(check);
            PolygonRegionTests(check);
            GeometryLayerTests(check);
            OutputFormTests(check);
            CutWordTests(check);
            ModelPathHygieneTests(check);
            BuiltinModelNameTests(check);
            SettlementGeometryTests(check);
            SymbolShapeDispatchTests(check);
            AreaDifferenceTests(check);
            SettlementRegionTests(check);
            RoadGeometryTests(check);
            DemoHoleTests(check);
            SinkCoverageTests(check);
            DemoPolygonEffectTests(check);
            PlacementTests(check);
            FeatureRunTests(check);
            TerrainSpecTests(check);
            ShippedSampleRunTests(check);
            FileTests(check);
            EndToEndTests(check);

            PCGNodeRegistry.Clear();
        }

        // ---- 解析 --------------------------------------------------------------------------------------

        private static void ParseTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(Full, out var spec, out string error);

            check("一份完整的设定描述能解析", parsed, error);

            if (!parsed)
                return;

            check("名字与种子落到规格上",
                spec.Name == "温带山谷" && spec.Seed == 20261008, $"{spec.Name} / {spec.Seed}");

            check("区域尺寸与高度都读到了",
                spec.Region != null && Near(V(spec.Region.SizeX), 2000.0f) && Near(V(spec.Region.SizeZ), 1600.0f) &&
                Near(V(spec.Region.Height), 800.0f));

            check("地貌底图的判据逐项落位",
                spec.HasBiome && Near(V(spec.Biome.CellSize), 50.0f) && Near(V(spec.Biome.WaterLevel), 12.0f) &&
                Near(V(spec.Biome.ReliefRadius), 200.0f) && Near(V(spec.Biome.RegionRadius), 1000.0f) &&
                I(spec.Biome.Samples) == 4 && Near(V(spec.Biome.HillSlope), 8.0f),
                $"cell={spec.Biome.CellSize} water={spec.Biome.WaterLevel} samples={spec.Biome.Samples}");

            check("散布层按写法顺序收集，名字保留原文",
                spec.Scatters.Count == 3 && spec.Scatters[0].Name == "forest" &&
                spec.Scatters[2].Name == "grass",
                string.Join("/", spec.Scatters.ConvertAll(s => s.Name)));

            var forest = spec.Scatters[0];

            check("两个值的选项分别落到两个字段上（slope 15 90）",
                Near(V(forest.MinSlope), 15.0f) && Near(V(forest.MaxSlope), 90.0f),
                $"{forest.MinSlope}..{forest.MaxSlope}");

            check("面积密度、密度、模型路径、尺寸倍率与开关都落位",
                Near(V(forest.PointsPerBlock), 1.5f) && Near(V(forest.Density), 0.6f) &&
                forest.ModelPath == "Assets/PCG/Pine Trees/Pine LOD0.flax" &&
                Near(V(forest.ModelScale), 1.2f) && forest.RandomYaw == true,
                $"{forest.PointsPerBlock} / {forest.Density} / {forest.ModelPath} / {forest.ModelScale}");

            check("固定点数的层拿到 points 而不是 per-block",
                I(spec.Scatters[1].Points) == 40 && spec.Scatters[1].PointsPerBlock == null);

            check("水、聚居地、洞穴各自成形",
                spec.HasWater && spec.Settlements.Count == 1 && spec.HasCave &&
                I(spec.Water.RiverThreshold) == 250 && Near(V(spec.Water.CellSize), 40.0f) &&
                I(spec.Settlements[0].Lots) == 12 && spec.Settlements[0].Rules == "X:0.5,0.5; Z:0.6,0.4" &&
                Near(V(spec.Cave.Depth), 400.0f) && I(spec.Cave.Segments) == 24 &&
                Near(V(spec.Cave.BranchChance), 0.25f),
                $"{spec.Water.RiverThreshold} / {spec.Settlements[0].Lots} / {spec.Cave.Depth}");

            check("路的两端点与走向约束都落位",
                spec.Roads.Count == 1 &&
                Near(V(spec.Roads[0].FromX), -800.0f) && Near(V(spec.Roads[0].FromZ), -600.0f) &&
                Near(V(spec.Roads[0].ToX), 900.0f) && Near(V(spec.Roads[0].ToZ), 700.0f) &&
                Near(V(spec.Roads[0].CellSize), 40.0f) && Near(V(spec.Roads[0].SlopeLimit), 20.0f) &&
                Near(V(spec.Roads[0].SampleSpacing), 120.0f) &&
                spec.Roads[0].ModelPath == "Assets/PCG/Road.flax",
                spec.Roads.Count > 0 ? $"{spec.Roads[0].FromX},{spec.Roads[0].FromZ} -> {spec.Roads[0].ToX},{spec.Roads[0].ToZ}" : "没有路");

            // 没写的选项必须是"没写"，而不是被塞了一个插件自选的默认值：节点自己的默认值才是唯一真相。
            bool minimal = PCGSpecParser.TryParse("world 小图\nbiome\nscatter rocks", out var bare, out error);

            check("只写指令不写选项也能解析", minimal, error);

            check("没写的选项保持为空（好让节点自己的默认值生效）",
                minimal && bare.Biome.CellSize == null && bare.Biome.WaterLevel == null &&
                bare.Scatters[0].Points == null && bare.Scatters[0].PointsPerBlock == null &&
                bare.Scatters[0].Density == null && bare.Scatters[0].RandomYaw == null &&
                bare.Scatters[0].ModelPath == null,
                minimal ? $"{bare.Scatters[0].RandomYaw}" : error);

            // 注释、空行与 CRLF 都只是排版。
            bool messy = PCGSpecParser.TryParse(
                "# 开场注释\r\n\r\n  seed 7   # 行尾注释\r\nworld 山谷 # 名字后面有注释\r\n", out var tidy, out error);

            check("注释、空行与 CRLF 不影响解析",
                messy && tidy.Seed == 7 && tidy.Name == "山谷", messy ? tidy.Name : error);

            // 引号里的空格是内容的一部分，不是分隔符。
            bool quoted = PCGSpecParser.TryParse("world x\nscatter trees model \"A B/C.flax\"", out var q, out error);

            check("引号里的空格原样保留", quoted && q.Scatters[0].ModelPath == "A B/C.flax",
                quoted ? q.Scatters[0].ModelPath : error);

            // 名字可以带空格（最后一段整段都算名字）。
            bool spaced = PCGSpecParser.TryParse("world 温带 山谷\nseed 3", out var s, out error);

            check("world 后面的整段都算名字", spaced && s.Name == "温带 山谷", spaced ? s.Name : error);
        }

        private static void ParseErrorTests(CheckDelegate check)
        {
            // 每条错都要带行号：没有行号的"解析失败"等于让人从头数。
            Error(check, "第 3 行", "world x\nseed 1\nplant trees", "不认识的指令");
            Error(check, "第 2 行", "world x\nscatter trees densityy 1", "不认识的选项");
            Error(check, "第 2 行", "world x\nseed abc", "种子不是整数");
            Error(check, "第 2 行", "world x\nscatter trees density heavy", "选项的值不是数字");
            Error(check, "第 2 行", "world x\nbiome cell", "选项缺值");
            Error(check, "第 2 行", "world x\nscatter trees model \"A B", "引号不成对");
            Error(check, "第 3 行", "world x\nseed 1\nseed 2", "单例指令写了两次");
            Error(check, "第 2 行", "world x\nscatter trees points 10 per-block 2", "点数与密度同时给");
            Error(check, "第 2 行", "world x\nscatter trees slope 90 15", "坡度过区间写反");
            Error(check, "第 2 行", "world x\nscatter trees yaw maybe", "开关值不合法");
            Error(check, "第 1 行", "region 10", "区域只写了一个尺寸");
            Error(check, "第 1 行", "road from 0 0", "路少了终点");
            Error(check, "第 1 行", "road from 0 0 to 1", "路的终点少一个数");
            Error(check, "第 2 行", "world x\nroad slopes 10", "路不认识的选项");
            Error(check, "第 2 行", "world x\ncity root District", "城市少了产生式");
            Error(check, "第 2 行", "world x\ncity productions \"A = B: X:1\" root", "城市的 root 少一个值");

            // 正面同夹具：把上面那条"写反的区间"改成正的，必须解析成功。否则那条断言可能只是对一个
            // 永远报错的解析器成立。
            bool positive = PCGSpecParser.TryParse("world x\nscatter trees slope 15 90", out var ok, out string error);

            check("坡度过区间写对了就能解析（上一条的正面同夹具）",
                positive && Near(V(ok.Scatters[0].MinSlope), 15.0f) && Near(V(ok.Scatters[0].MaxSlope), 90.0f), error);

            bool empty = PCGSpecParser.TryParse("   \n # 只有注释\n", out _, out string emptyError);

            check("只有空白与注释的描述算空描述并报出来",
                !empty && !string.IsNullOrEmpty(emptyError), emptyError);

            bool blank = PCGSpecParser.TryParse(null, out _, out string nullError);

            check("null 描述不抛异常，只报错", !blank && !string.IsNullOrEmpty(nullError), nullError);
        }

        private static void Error(CheckDelegate check, string expected, string text, string what)
        {
            bool ok = PCGSpecParser.TryParse(text, out _, out string error);

            check($"报错：{what}",
                !ok && !string.IsNullOrEmpty(error) && error.Contains(expected), error ?? "没有报错");
        }

        /// <summary>
        /// 每条指令声明的每个选项都要真能落位。
        /// </summary>
        /// <remarks>
        /// 这是选项表的**完整性绊线**：逐个选项造一行（值 "1" 对数字、开关、字符串都成立）跑一遍解析。于是
        /// "选项声明了但字段名拼错 / 字段类型不支持 / 取值个数不对"会当场炸掉，而不必等谁手写到那一行。
        /// 每个选项单独一行，也顺带绕开了 scatter 里 points 与 per-block 互斥这类组合约束。
        /// </remarks>
        private static void OptionCoverageTests(CheckDelegate check)
        {
            foreach (string directive in PCGSpecParser.Directives)
            {
                if (directive == "world" || directive == "seed")
                    continue;

                var schema = PCGSpecParser.OptionsFor(directive);

                if (schema == null)
                {
                    check($"'{directive}' 有一张选项表", false, "OptionsFor 返回 null");
                    continue;
                }

                foreach (var pair in schema)
                {
                    var line = new StringBuilder(directive);

                    // region 要尺寸、scatter 要层名、road 要两端点、city 要产生式表、terrain 要形状表
                    // （都是必填，不写就报错）。
                    line.Append(directive == "region" ? " 10 10"
                        : directive == "scatter" ? " probe"
                        : directive == "road" ? " from 1 1 to 2 2"
                        : directive == "city" ? " productions \"A = B: X:1\""
                        : directive == "terrain" ? " shapes \"dome 0 0 10 1\""
                        : string.Empty);
                    line.Append(' ').Append(pair.Key);

                    // 区域那四个数是"两个角"：四个一模一样的数会退化成一条线，而解析器明确要拒那种写法。
                    // 所以这里给一组真正的两个角，让覆盖断言测的是"能落位"而不是"被拒得对"。
                    // `except` 也是同样的四个数（它就是同一份解析代码），所以同样在列——
                    // 这张清单漏一个，那个选项的覆盖断言就会去测"被拒得对"，等于没测。
                    bool area = pair.Key == "within" || pair.Key == "outside" || pair.Key == "except";

                    // 形状表也是字符串语法：通用的 "1" 会被当场拒掉（它连五段都不到），于是覆盖断言
                    // 测的是"格式被拒"而不是"值落位"。给它一个真能解析的形状表。
                    // **洞掩码那一项也是同一条形状语法**（`holes "dome X Z R"`），所以同样要喂形状串。
                    bool shapeTable = directive == "terrain" && (pair.Key == "shapes" || pair.Key == "holes");

                    // 几何形状名同理：它要从一个固定表里挑（box/grid/cylinder/stairs），通用的 "1" 不是形状名。
                    // 而且解析器要求"写了几何就必须给尺寸"（节点默认是一公里大的格子尺寸，漏写比报错难查得多），
                    // 所以这一条要把尺寸一起写上——夹具一次只盖一个选项，这条依赖得在这里补。
                    bool shapeName = directive == "scatter" && pair.Key == "geometry";

                    // 输出形态同理：三个词之一，"1" 会被当场拒掉。
                    bool outputForm = directive == "scatter" && pair.Key == "output";

                    // 刀口要四个数（法线 + 偏移），而且解析器要求"写 cut 就必须是程序化几何"，
                    // 所以这一条得把 geometry 一起写上——夹具一次只盖一个选项，这条依赖在这里补。
                    bool cutPlane = directive == "scatter" && pair.Key == "cut";

                    // 裁切也是"词 + 数字"：形状词（box/cylinder）打头，然后三个数。"1" 会被当场拒掉。
                    bool clipShape = directive == "scatter" && pair.Key == "clip";

                    for (int i = 0; i < pair.Value.Arity; i++)
                        line.Append(area ? " " + (10 + i * 10).ToString()
                                         : shapeTable ? " \"basin 0 0 10 1 0.2\""
                                         : shapeName ? " box geometry-size 1 1 1"
                                         : outputForm ? " instanced"
                                         : cutPlane ? (i == 3 ? " 0" : " 1")
                                         : clipShape ? (i == 0 ? " box" : " 2")
                                         : " 1");

                    if (cutPlane || clipShape)
                        line.Append(" geometry cylinder geometry-size 1 1 1");

                    bool ok = PCGSpecParser.TryParse("world probe\n" + line, out _, out string error);

                    check($"'{directive} {pair.Key}' 能落位", ok, error);

                    // **值真的到了字段上**——只验"能解析"是不够的：一个声明在选项表里、却没被写进任何字段的
                    // 选项会照样解析成功，然后在编译期被当成"没写"（`scatter` 的 `jitter` 就这样静默丢过一次）。
                    string landed = FieldLandedReason(directive, line.ToString(), pair.Value.Field);

                    check($"'{directive} {pair.Key}' 的值真的写进了 {pair.Value.Field}",
                        landed == null, landed ?? pair.Value.Field);
                }
            }
        }

        // ---- 编译 --------------------------------------------------------------------------------------

        private static void CompileTests(CheckDelegate check)
        {
            PCGSpecParser.TryParse(Full, out var spec, out _);

            var result = PCGSpecCompiler.Compile(spec, Resolver);

            check("一份完整描述能编译成图", result.Ok, result.Error ?? "ok");

            if (!result.Ok)
                return;

            check("图名与默认种子跟着描述走",
                result.Graph.Name == "温带山谷" && result.Graph.Settings.DefaultSeed == 20261008);

            var kinds = new List<string>();

            foreach (var node in result.Graph.Nodes)
                kinds.Add(node.TypeId);

            // 三条散布链（一条带坡度过滤）+ 地貌 + 水 + 聚居地 + 洞穴 + 几何生成器 + 一条路。
            // 六个静态模型生成器：forest / rocks / grass / 水面 / 房屋 / 路面。
            check("节点表与描述一一对应",
                Count(kinds, PCGVolumeSamplerNode.Id) == 3 &&
                Count(kinds, PCGSlopeFilterNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 6 &&
                Count(kinds, PCGBiomeNode.Id) == 1 &&
                Count(kinds, PCGHydrologyNode.Id) == 1 &&
                Count(kinds, PCGSplitGrammarNode.Id) == 1 &&
                Count(kinds, PCGCaveNode.Id) == 1 &&
                Count(kinds, PCGSpawnGeometryNode.Id) == 1 &&
                Count(kinds, PCGPointListNode.Id) == 1 &&
                Count(kinds, PCGPathNode.Id) == 1,
                string.Join(", ", kinds));

            check("连线数 = 每个生成器各一条，加坡度过滤与路的端点与四条落地投影，共 13 条",
                result.Graph.EdgeCount == 13, result.Graph.EdgeCount.ToString());

            // 落地投影必须串在**坡度筛选之前**：坡度是从表面法线算出来的，投影若在筛选之后，
            // 筛选看到的仍是一片平地——`slope 15 90` 会把整层筛空，而屏幕上只表现为"这层什么都没长"。
            // 这条不变量是投影之所以放在那个位置的唯一理由，所以它得自己有一条断言钉着。
            int groundSampler = IndexOf(result.Graph, PCGVolumeSamplerNode.Id, 0);
            int groundNode = IndexOf(result.Graph, PCGProjectPointsNode.Id, 0);
            int slopeFilter = IndexOf(result.Graph, PCGSlopeFilterNode.Id, 0);

            bool samplerToGround = false;
            bool groundToSlope = false;

            foreach (var groundEdge in result.Graph.Edges)
            {
                if (groundEdge.OutputNodeIndex == groundSampler && groundEdge.InputNodeIndex == groundNode)
                    samplerToGround = true;

                if (groundEdge.OutputNodeIndex == groundNode && groundEdge.InputNodeIndex == slopeFilter)
                    groundToSlope = true;
            }

            check("落地投影串在坡度筛选之前（否则坡度读到的永远是平地，slope 会把整层筛空）",
                samplerToGround && groundToSlope && groundNode != slopeFilter,
                $"sampler={groundSampler} ground={groundNode} slope={slopeFilter}");

            check("解析得出资产的路径不产生提醒",
                result.Warnings.Count == 0, string.Join(" | ", result.Warnings));

            // 值必须落在**具名槽位**上。按下标写的话，节点加一个参数就会整体挪位，而图照跑、只是参数全错。
            // 节点顺序由编译器决定，所以这里一律按类型查下标，不假设第几个是谁。
            int sampler = IndexOf(result.Graph, PCGVolumeSamplerNode.Id, 0);
            int slope = IndexOf(result.Graph, PCGSlopeFilterNode.Id, 0);
            int spawner = IndexOf(result.Graph, PCGSpawnStaticModelNode.Id, 0);

            check("上游采样器的槽位按名字落位",
                Near(AsFloat(Slot(result.Graph, sampler, "Points Per Block")), 1.5f) &&
                Near(AsFloat(Slot(result.Graph, sampler, "Density")), 0.6f) &&
                AsInt(Slot(result.Graph, sampler, "Seed Offset")) == 0,
                Describe(result.Graph.Nodes[sampler].Values));

            check("坡度过滤器拿到的是坡度的上下界而不是别的",
                Near(AsFloat(Slot(result.Graph, slope, "Min Slope Degrees")), 15.0f) &&
                Near(AsFloat(Slot(result.Graph, slope, "Max Slope Degrees")), 90.0f),
                Describe(result.Graph.Nodes[slope].Values));

            check("生成器拿到解析出来的资产 id、尺寸倍率与随机转向",
                Slot(result.Graph, spawner, "Model") is Guid id && id == PineId &&
                Near(AsFloat(Slot(result.Graph, spawner, "Scale Multiplier")), 1.2f) &&
                Slot(result.Graph, spawner, "Random Yaw") is bool yaw && yaw,
                Describe(result.Graph.Nodes[spawner].Values));

            int secondSource = IndexOf(result.Graph, PCGVolumeSamplerNode.Id, 1);

            check("写的是 points 就一定不写 per-block",
                AsInt(Slot(result.Graph, secondSource, "Point Count")) == 40 &&
                Slot(result.Graph, secondSource, "Points Per Block") == null,
                Describe(result.Graph.Nodes[secondSource].Values));

            int cave = IndexOf(result.Graph, PCGCaveNode.Id, 0);

            check("洞穴的每个参数都落到自己的槽位上",
                Near(AsFloat(Slot(result.Graph, cave, "Max Depth")), 400.0f) &&
                AsInt(Slot(result.Graph, cave, "Segments")) == 24 &&
                Near(AsFloat(Slot(result.Graph, cave, "Radius")), 60.0f) &&
                AsInt(Slot(result.Graph, cave, "Max Tunnels")) == 10 &&
                Near(AsFloat(Slot(result.Graph, cave, "Branch Chance")), 0.25f) &&
                AsInt(Slot(result.Graph, cave, "Tube Sides")) == 12,
                Describe(result.Graph.Nodes[cave].Values));

            // 区域这一档**必须到得了槽位**：解析过了、值也落进描述对象了，编译器调用点若漏接，
            // 洞穴照样一声不响地穿墙——那正是本仓反复点名的「声明了却没人写」。
            var boundedCave = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 洞\ncave depth 200 segments 12 tunnels 3 within -150 -120 150 180"),
                Resolver);

            int boundedNode = boundedCave.Ok ? IndexOf(boundedCave.Graph, PCGCaveNode.Id, 0) : -1;

            check("洞穴写 within：区域两个角真的进到节点槽位（不是只落在描述对象里）",
                boundedCave.Ok && boundedNode >= 0 &&
                Slot(boundedCave.Graph, boundedNode, "Region Min") is Vector3 rMin &&
                Near(rMin.X, -150.0f) && Near(rMin.Z, -120.0f) &&
                Slot(boundedCave.Graph, boundedNode, "Region Max") is Vector3 rMax &&
                Near(rMax.X, 150.0f) && Near(rMax.Z, 180.0f),
                boundedNode >= 0 ? Describe(boundedCave.Graph.Nodes[boundedNode].Values)
                                 : (boundedCave.Error ?? "没找到洞穴节点"));

            // 对照：不写 within 时这两个槽位**不该被写**——留空才让节点自己那条「不设区域」的分支生效。
            check("对照：不写 within 的洞穴不碰区域槽位（默认那一档必须真的是无约束）",
                Slot(result.Graph, cave, "Region Min") == null &&
                Slot(result.Graph, cave, "Region Max") == null,
                Describe(result.Graph.Nodes[cave].Values));

            // 水系这一侧是**另一处调用点**：同一个道理要在它自己身上有证据，不能拿洞穴那条代证。
            var boundedWater = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 水\nwater cell 40 threshold 30 within -200 -150 200 150"), Resolver);
            int waterNode = boundedWater.Ok ? IndexOf(boundedWater.Graph, PCGHydrologyNode.Id, 0) : -1;

            check("水系写 within：区域两个角进到水文节点槽位（水与洞穴是两处调用点，各钉一次）",
                boundedWater.Ok && waterNode >= 0 &&
                Slot(boundedWater.Graph, waterNode, "Region Min") is Vector3 wMin &&
                Near(wMin.X, -200.0f) && Near(wMin.Z, -150.0f) &&
                Slot(boundedWater.Graph, waterNode, "Region Max") is Vector3 wMax &&
                Near(wMax.X, 200.0f) && Near(wMax.Z, 150.0f),
                waterNode >= 0 ? Describe(boundedWater.Graph.Nodes[waterNode].Values)
                               : (boundedWater.Error ?? "没找到水文节点"));

            var plainWaterSpec = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 水\nwater cell 40 threshold 30"), Resolver);
            int plainWater = plainWaterSpec.Ok ? IndexOf(plainWaterSpec.Graph, PCGHydrologyNode.Id, 0) : -1;

            check("对照：同一份水系描述不写 within 时不碰区域槽位",
                plainWater >= 0 && Slot(plainWaterSpec.Graph, plainWater, "Region Min") == null &&
                Slot(plainWaterSpec.Graph, plainWater, "Region Max") == null,
                plainWater >= 0 ? Describe(plainWaterSpec.Graph.Nodes[plainWater].Values) : "没有水文节点");

            // 水系这一档走的是**通用** `Options()` 分支（water 指令没有自己的循环），
            // 所以四个角应当天然到位——这一条是去证"它真的通了"，不是猜。
            bool waterRegionOk = PCGSpecParser.TryParse(
                "world 水\nwater cell 40 threshold 30 within -200 -150 200 150",
                out var waterRegionSpec, out string waterRegionError);

            var waterRegion = waterRegionOk && waterRegionSpec.HasWater ? waterRegionSpec.Water : null;

            check("水系写 within：四个角都写进规格对象（走的是通用反射分支）",
                waterRegion != null && waterRegion.AreaMinX == -200.0f && waterRegion.AreaMaxX == 200.0f &&
                waterRegion.AreaMinZ == -150.0f && waterRegion.AreaMaxZ == 150.0f,
                waterRegion != null
                    ? $"MinX={waterRegion.AreaMinX} MaxX={waterRegion.AreaMaxX} " +
                      $"MinZ={waterRegion.AreaMinZ} MaxZ={waterRegion.AreaMaxZ}"
                    : (waterRegionError ?? "解析失败"));

            var waterRegionGraph = waterRegionOk ? PCGSpecCompiler.Compile(waterRegionSpec, Resolver) : null;
            int waterSlotNode = waterRegionGraph != null && waterRegionGraph.Ok
                ? IndexOf(waterRegionGraph.Graph, PCGHydrologyNode.Id, 0) : -1;

            check("水系写 within：矩形进到水文节点槽位（产生器输入，不是事后删点）",
                waterSlotNode >= 0 &&
                Slot(waterRegionGraph.Graph, waterSlotNode, "Region Min") is Vector3 hwMin &&
                Near(hwMin.X, -200.0f) && Near(hwMin.Z, -150.0f) &&
                Slot(waterRegionGraph.Graph, waterSlotNode, "Region Max") is Vector3 hwMax &&
                Near(hwMax.X, 200.0f) && Near(hwMax.Z, 150.0f),
                waterSlotNode >= 0 ? Describe(waterRegionGraph.Graph.Nodes[waterSlotNode].Values)
                                   : (waterRegionGraph?.Error ?? "没接上"));

            // `outside` 那一档：同一块矩形，但 invert 必须为真——水系筛的是发源格，
            // 所以"这块地不许发源"与"只准在这块发源"是同一判据的取反，两条都得能写出来。
            bool waterOutOk = PCGSpecParser.TryParse(
                "world 水\nwater cell 40 threshold 6 outside -200 -150 200 150", out var waterOutSpec, out _);
            var waterOutGraph = waterOutOk ? PCGSpecCompiler.Compile(waterOutSpec, Resolver) : null;
            int waterOutNode = waterOutGraph != null && waterOutGraph.Ok
                ? IndexOf(waterOutGraph.Graph, PCGHydrologyNode.Id, 0) : -1;

            check("水系写 outside：矩形进槽位且 invert 为真（发源判据的取反，不是截断河身）",
                waterOutNode >= 0 &&
                Slot(waterOutGraph.Graph, waterOutNode, "Region Invert") is bool waterInvert && waterInvert &&
                Slot(waterOutGraph.Graph, waterOutNode, "Region Min") is Vector3 woMin && Near(woMin.X, -200.0f) &&
                Slot(waterOutGraph.Graph, waterOutNode, "Region Max") is Vector3 woMax && Near(woMax.Z, 150.0f),
                waterOutNode >= 0 ? Describe(waterOutGraph.Graph.Nodes[waterOutNode].Values)
                                  : (waterOutGraph?.Error ?? "没接上"));

            // 缺陷 #17 的定位实验：图里那位是 True，引擎里却没生效。这里**用同一份 values 造节点**，
            // 直接问节点的字段读到了什么——把"参数读取"这一段从"判据是否被用上"里分离出来。
            // 如果这里读到 true，缺陷就不在 `PCGHydrologyNode` 的取值，而在引擎跑的是哪一份程序集；
            // 如果读到 false，那是 `PCGNodeParameters.Get` 这一层的问题（槽位序或类型匹配）。
            string readBack = "没造起来";

            if (waterOutNode >= 0)
            {
                var values = waterOutGraph.Graph.Nodes[waterOutNode].Values;

                var probe = new PCGHydrologyNode(new PCGNodeRegistry.PCGNodeSerializationData(
                    PCGHydrologyNode.Id, values, 0));

                readBack = $"RegionInvert={probe.RegionInvert} " +
                           $"Min=({probe.RegionMin.X:F0},{probe.RegionMin.Z:F0}) " +
                           $"Max=({probe.RegionMax.X:F0},{probe.RegionMax.Z:F0})";
            }

            check("从同一份图 values 造出的水文节点，RegionInvert 必须读到 true（分离『取值』与『用判据』两段）",
                waterOutNode >= 0 && readBack.StartsWith("RegionInvert=True") &&
                readBack.Contains("Min=(-200,-150)") && readBack.Contains("Max=(200,150)"),
                readBack);

            // 把引擎里那趟干跑搬到离屏：同一份图、同一份高度场，看 `outside` 在离屏是不是也不筛。
            // 复现得了 ⇒ 缺陷在节点/判据本身（离屏改一版几十秒）；复现不了 ⇒ 差异在执行器或程序集侧。
            int outTotal = -1;
            int outInside = -1;

            if (waterOutNode >= 0 && PCGGraphCompiler.TryCompile(waterOutGraph.Graph, out var waterOutProgram))
            {
                var outRun = new PCGGraphExecutor(waterOutProgram).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 7,
                        World = new PCGWorldContext
                        {
                            // 两处等深洼地：一处在框内（原点），一处在框外（160,160）。
                            HeightSource = (x, z) => Math.Min(
                                x * x + z * z,
                                (x - 160.0f) * (x - 160.0f) + (z - 160.0f) * (z - 160.0f)) * 0.0004f,
                        },
                        GenerationBounds = new BoundingBox(
                            new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                    }.AsDryRun()).PrimaryOutput;

                outTotal = outRun?.Count ?? 0;
                outInside = 0;

                for (int i = 0; outRun != null && i < outTotal; i++)
                {
                    var p = outRun.Points[i].Position;

                    if (p.X >= -120.0f && p.X <= 120.0f && p.Z >= -120.0f && p.Z <= 120.0f)
                        outInside++;
                }
            }

            check("离屏复现 outside：产出里**没有一个**落在框内（两处洼地合并成一湖、湖心在框内 ⇒ 产出为空是正确结果，" +
                  "所以判据只能看'框内为 0'，不能要求产出非空）",
                waterOutNode >= 0 && outInside == 0, $"{outTotal} 点，框内 {outInside}");

            // 上面那条要配上限对照才不空转：同一份图**不写 outside** 时必须真的有产出落在框内，
            // 否则"框内 0 个"可能只是因为压根没有产出。
            int plainTotal = -1;
            int plainInside = -1;

            bool plainWaterOk = PCGSpecParser.TryParse(
                "world 水\nwater cell 40 threshold 6", out var noRegionWaterSpec, out _);
            var plainWaterGraph = plainWaterOk ? PCGSpecCompiler.Compile(noRegionWaterSpec, Resolver) : null;

            if (plainWaterGraph != null && plainWaterGraph.Ok &&
                PCGGraphCompiler.TryCompile(plainWaterGraph.Graph, out var plainWaterProgram))
            {
                var plainRun = new PCGGraphExecutor(plainWaterProgram).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 7,
                        World = new PCGWorldContext
                        {
                            HeightSource = (x, z) => Math.Min(
                                x * x + z * z,
                                (x - 160.0f) * (x - 160.0f) + (z - 160.0f) * (z - 160.0f)) * 0.0004f,
                        },
                        GenerationBounds = new BoundingBox(
                            new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                    }.AsDryRun()).PrimaryOutput;

                plainTotal = plainRun?.Count ?? 0;
                plainInside = 0;

                for (int i = 0; plainRun != null && i < plainTotal; i++)
                {
                    var p = plainRun.Points[i].Position;

                    if (p.X >= -120.0f && p.X <= 120.0f && p.Z >= -120.0f && p.Z <= 120.0f)
                        plainInside++;
                }
            }

            check("对照：同一份高度场不写 outside 时确实有产出落在框内（上一条的『框内 0』才不是空转）",
                plainInside > 0, $"{plainTotal} 点，框内 {plainInside}");

            // 引擎那一组报的是"无区域 2 点、outside 2 点其中 1 点在框内"，而上面离屏跑的是"11 点 → 0 点"。
            // 两组数字对不上，差在**描述文本**：引擎那份多写了 `region 400 400 height 20` 与 `min-lake-cells 4`。
            // 这里把引擎的文本原样搬离屏跑三趟（无约束 / within / outside），只把计数打出来——
            // 复现得了 ⇒ 缺陷在代码，可以离线快速迭代；复现不了 ⇒ 差异在引擎环境，别再改代码。
            string EngineSpec(string clause) =>
                "world 区域水系\nseed 7\nregion 400 400 height 20\nwater cell 40 threshold 6 min-lake-cells 4 " + clause;

            string engineProbe = "没跑成";
            float? ProbeBasin(float x, float z) => Math.Min(
                x * x + z * z,
                (x - 160.0f) * (x - 160.0f) + (z - 160.0f) * (z - 160.0f)) * 0.0004f;

            (int Total, int Inside) RunEngineText(string clause)
            {
                if (!PCGSpecParser.TryParse(EngineSpec(clause), out var runSpec, out _))
                    return (-1, -1);

                var runGraph = PCGSpecCompiler.Compile(runSpec, Resolver);

                if (!runGraph.Ok || !PCGGraphCompiler.TryCompile(runGraph.Graph, out var runProgram))
                    return (-2, -2);

                var run = new PCGGraphExecutor(runProgram).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 7,
                        World = new PCGWorldContext { HeightSource = ProbeBasin },
                        GenerationBounds = new BoundingBox(
                            new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                    }.AsDryRun()).PrimaryOutput;

                int total = run?.Count ?? 0;
                int inside = 0;

                for (int i = 0; run != null && i < total; i++)
                {
                    var pos = run.Points[i].Position;

                    if (pos.X >= -120.0f && pos.X <= 120.0f && pos.Z >= -120.0f && pos.Z <= 120.0f)
                        inside++;
                }

                return (total, inside);
            }

            var engFree = RunEngineText(string.Empty);
            var engWithin = RunEngineText("within -120 -120 120 120");
            var engOutside = RunEngineText("outside -120 -120 120 120");

            engineProbe = $"无约束 {engFree.Total}/{engFree.Inside}｜within {engWithin.Total}/{engWithin.Inside}" +
                          $"｜outside {engOutside.Total}/{engOutside.Inside}（总数/框内数）";

            // 先要这三趟真的跑出东西（-1/-2 都是"没跑成"的不同原因），否则下面的对照没有意义。
            check("引擎文本离屏三趟都跑得出产出（复现环境差异的前提）",
                engFree.Total >= 0 && engWithin.Total >= 0 && engOutside.Total >= 0, engineProbe);

            // 判据是**发源**，不是"产出点必须落在框外"：从框外发起的河可以一路流进框内，那正是 `outside`
            // 允许的事。我第一版把它写成"框内必须为 0"，于是把正确行为读成了缺陷，引擎与离屏两边都被我误判。
            // 正确的形状：outside 的产出是无约束产出的**真子集**（丢掉了框内发源的那处湖/那条河），
            // 而 within 同样是真子集，且它留下的点可以整片在框内。
            check("outside 丢掉框内发源的那部分水（是无约束的真子集，而不是把水全判光）",
                engOutside.Total > 0 && engOutside.Total < engFree.Total && engFree.Total > 0,
                engineProbe);

            check("within 同样只留下发源在框内的水（真子集，且留下的都在框内）",
                engWithin.Total > 0 && engWithin.Total < engFree.Total && engWithin.Inside == engWithin.Total,
                engineProbe);

            // 四字段区域判据现在有**两处实现**：`Options()` 里那段反射分支（散布/聚落/城/水走它），
            // 以及 `road` 指令循环里那一段（它有自己的循环，接不进通用分支）。
            // 抽成共用方法才是根治；在那之前先钉一条分家警报：同一句话在两条路上必须逐字段一致、
            // 且相同的写法都被拒。只改一处判据时，这几条会红。
            bool divScatter = PCGSpecParser.TryParse(
                "world 分家\nscatter s points 4 model \"Cube\" within -100 -120 100 120",
                out var divScatterSpec, out _);
            bool divRoad = PCGSpecParser.TryParse(
                "world 分家\nroad from 0 0 to 900 900 within -100 -120 100 120",
                out var divRoadSpec, out _);

            var divLayer = divScatterSpec != null && divScatterSpec.Scatters.Count > 0
                ? divScatterSpec.Scatters[0] : null;
            var divRoadItem = divRoadSpec != null && divRoadSpec.Roads.Count > 0 ? divRoadSpec.Roads[0] : null;

            check("同一句 within 在两条实现上逐字段一致（判据分家时这条会红）",
                divScatter && divRoad && divLayer != null && divRoadItem != null &&
                divLayer.AreaMinX == divRoadItem.AreaMinX &&
                divLayer.AreaMaxX == divRoadItem.AreaMaxX &&
                divLayer.AreaMinZ == divRoadItem.AreaMinZ &&
                divLayer.AreaMaxZ == divRoadItem.AreaMaxZ &&
                divLayer.AreaInvert == divRoadItem.AreaInvert,
                $"散布 {divLayer?.AreaMinX}/{divLayer?.AreaMaxX}/{divLayer?.AreaMinZ}/{divLayer?.AreaMaxZ}/" +
                $"{divLayer?.AreaInvert}｜路 {divRoadItem?.AreaMinX}/{divRoadItem?.AreaMaxX}/" +
                $"{divRoadItem?.AreaMinZ}/{divRoadItem?.AreaMaxZ}/{divRoadItem?.AreaInvert}");

            bool roadOutsideDiv = PCGSpecParser.TryParse(
                "world 分家\nroad from 0 0 to 900 900 outside -100 -120 100 120", out var outsideDiv, out _);
            bool scatterOutsideDiv = PCGSpecParser.TryParse(
                "world 分家\nscatter s points 4 model \"Cube\" outside -100 -120 100 120",
                out var outsideScatter, out _);

            var outsideDivRoad = outsideDiv != null && outsideDiv.Roads.Count > 0 ? outsideDiv.Roads[0] : null;
            var outsideDivLayer = outsideScatter != null && outsideScatter.Scatters.Count > 0
                ? outsideScatter.Scatters[0] : null;

            check("同一句 outside 在两条实现上都把 invert 翻成真（分家警报的第二半）",
                roadOutsideDiv && scatterOutsideDiv && outsideDivRoad != null && outsideDivLayer != null &&
                outsideDivRoad.AreaInvert && outsideDivLayer.AreaInvert,
                $"路 invert={outsideDivRoad?.AreaInvert}｜散布 invert={outsideDivLayer?.AreaInvert}");

            bool roadReversed = PCGSpecParser.TryParse(
                "world 分家\nroad from 0 0 to 900 900 within 100 120 -100 -120", out _, out string roadReversedError);
            bool scatterReversed = PCGSpecParser.TryParse(
                "world 分家\nscatter s points 4 model \"Cube\" within 100 120 -100 -120",
                out _, out string scatterReversedError);

            check("两个角写反时两条实现都拒（措辞可以不同，但都必须拒）",
                !roadReversed && !scatterReversed && roadReversedError != null && scatterReversedError != null,
                $"路：{roadReversedError ?? "放行"}｜散布：{scatterReversedError ?? "放行"}");

            bool roadTwice = PCGSpecParser.TryParse(
                "world 分家\nroad from 0 0 to 900 900 within -10 -10 10 10 outside -20 -20 20 20",
                out _, out string roadTwiceError);
            bool scatterTwice = PCGSpecParser.TryParse(
                "world 分家\nscatter s points 4 model \"Cube\" within -10 -10 10 10 outside -20 -20 20 20",
                out _, out string scatterTwiceError);

            check("一处写两次时两条实现都拒（within 与 outside 说的是同一块矩形）",
                !roadTwice && !scatterTwice && roadTwiceError != null && scatterTwiceError != null,
                $"路：{roadTwiceError ?? "放行"}｜散布：{scatterTwiceError ?? "放行"}");

            // 路这一档**暂时**不接受区域子句：`road` 的指令解析有自己的选项循环，没走 `Options()` 里
            // 那段"四个数 → 四个同名字段"的反射分支。实测后果不是报错而是**半生效**：
            // `outside -100 -120 100 120` 只写进 AreaMinX，另外三个 null、invert 恒为 false
            // ——`outside` 被当成 `within` 用，而编译器读空的可空字段会抛穿整个 runner。
            // 所以表里先不列这两个键，这里钉住"写它就被拒"。等 road 的选项循环接进共享分支之后，
            // 把这两条删掉、换成真正的槽位断言（纯核与节点那一侧的能力已经先证过了：
            // PathPlannerTests.RegionTests 一整套）。
            bool roadWithinAccepted = PCGSpecParser.TryParse(
                "world 路\nroad from -600 0 to 600 0 cell 50 within -100 -120 100 120",
                out _, out string roadWithinError);

            bool roadRegionOk = PCGSpecParser.TryParse(
                "world 路\nroad from -600 0 to 600 0 cell 50 outside -100 -120 100 120",
                out var roadRegionSpec, out string roadRegionError);

            var roadRegion = roadRegionOk && roadRegionSpec.Roads.Count > 0 ? roadRegionSpec.Roads[0] : null;

            check("路写 outside：四个角都到位、invert 为真（上一版只到位 AreaMinX 一个字段）",
                roadRegion != null && roadRegion.AreaMinX == -100.0f && roadRegion.AreaMaxX == 100.0f &&
                roadRegion.AreaMinZ == -120.0f && roadRegion.AreaMaxZ == 120.0f && roadRegion.AreaInvert,
                roadRegion != null
                    ? $"MinX={roadRegion.AreaMinX} MaxX={roadRegion.AreaMaxX} " +
                      $"MinZ={roadRegion.AreaMinZ} MaxZ={roadRegion.AreaMaxZ} invert={roadRegion.AreaInvert}"
                    : (roadRegionError ?? "解析失败"));

            var roadRegionGraph = roadRegionOk ? PCGSpecCompiler.Compile(roadRegionSpec, Resolver) : null;
            int roadSlotNode = roadRegionGraph != null && roadRegionGraph.Ok
                ? IndexOf(roadRegionGraph.Graph, PCGPathNode.Id, 0) : -1;

            check("路写 outside：矩形与 invert 一起进到路径节点槽位（绕开这块地）",
                roadSlotNode >= 0 &&
                Slot(roadRegionGraph.Graph, roadSlotNode, "Region Min") is Vector3 roadMin &&
                Near(roadMin.X, -100.0f) && Near(roadMin.Z, -120.0f) &&
                Slot(roadRegionGraph.Graph, roadSlotNode, "Region Max") is Vector3 roadMax &&
                Near(roadMax.X, 100.0f) && Near(roadMax.Z, 120.0f) &&
                Slot(roadRegionGraph.Graph, roadSlotNode, "Region Invert") is bool roadInvert && roadInvert,
                roadSlotNode >= 0 ? Describe(roadRegionGraph.Graph.Nodes[roadSlotNode].Values)
                                  : (roadRegionGraph?.Error ?? "没接上"));
            // 整块包在 try 里跑，报出的是 `InvalidOperationException: Nullable object must have a value`，
            // 且 `StackTrace` 为 null（这个构建里没有可用的行号）。也就是说抛点在**取槽位那一步**
            // （`Slot(graph, idx, "Region Min")` 这类按名取值的助手），而不是我担心的"解析给了 null"——
            // 所以前两次"崩在分发处"其实是同一个原因：助手对没找到的槽位名不是返回 null，而是 deref 一个空的 int?。
            // 下一次从这里接：先确认离线 harness 里 `PCGPathNode` 的槽位名表是否包含新加的三条
            // （注册表条数是钉死的断言），再决定是补表还是换一种取值写法。
            // 现状：字段级（覆盖闸门 `'road within'/'road outside' 的值真的写进了 AreaMinX`）与行为级
            // （PathPlannerTests.RegionTests 一整套，含"绕开=变长且贴着解析下界""绕不过去=报错""端点在框外=报错"）
            // 都有证据，缺的是中间这一级。

            int biome = IndexOf(result.Graph, PCGBiomeNode.Id, 0);

            check("地貌底图的判据也逐项落位",
                Near(AsFloat(Slot(result.Graph, biome, "Cell Size")), 50.0f) &&
                AsInt(Slot(result.Graph, biome, "Samples")) == 4,
                Describe(result.Graph.Nodes[biome].Values));

            int village = IndexOf(result.Graph, PCGSplitGrammarNode.Id, 0);

            // 地块尺寸是三元组：写三个数，槽位里也必须是一个 Vector3——写成单个 float 会让图编译器
            // 报"槽位要 Vector 而给的是 Single"，这条断言就是那个错误的离线版本。
            check("聚居地的规则表原样进槽位，地块尺寸是三元组",
                Slot(result.Graph, village, "Rules") as string == "X:0.5,0.5; Z:0.6,0.4" &&
                Slot(result.Graph, village, "Cell Size") is Vector3 cell &&
                Near(cell.X, 120.0f) && Near(cell.Y, 40.0f) && Near(cell.Z, 120.0f),
                Describe(result.Graph.Nodes[village].Values));

            // 连线必须接在**具名引脚**上，而不是碰巧第 0 号对第 0 号。
            bool wiresOk = result.Graph.EdgeCount > 0;
            string wireReport = null;

            foreach (var edge in result.Graph.Edges)
            {
                string outName = PinName(result.Graph, edge.OutputNodeIndex, edge.OutputPinIndex, true);
                string inName = PinName(result.Graph, edge.InputNodeIndex, edge.InputPinIndex, false);

                wiresOk &= outName == "Out" && inName == "In";
                wireReport ??= $"{outName} -> {inName}";
            }

            check("每条连线都接在具名的 Out 与 In 上", wiresOk, wireReport);

            // 路那一条链：端点写进 `Point List` 的前两个槽位，再接到 `Path`，最后接路面生成器。
            // 这一条同时钉住"字面点真的被用上了"——没有它，路只能靠猜的端点。
            int ends = IndexOf(result.Graph, PCGPointListNode.Id, 0);
            int path = IndexOf(result.Graph, PCGPathNode.Id, 0);

            check("路的端点原样写进 Point List 的前两个槽位",
                AsInt(Slot(result.Graph, ends, "Count")) == 2 &&
                Slot(result.Graph, ends, "Point 1") is Vector3 a && Near(a.X, -800.0f) && Near(a.Z, -600.0f) &&
                Slot(result.Graph, ends, "Point 2") is Vector3 b && Near(b.X, 900.0f) && Near(b.Z, 700.0f),
                Describe(result.Graph.Nodes[ends].Values));

            check("路走向约束落进 Path 的槽位",
                Near(AsFloat(Slot(result.Graph, path, "Cell Size")), 40.0f) &&
                Near(AsFloat(Slot(result.Graph, path, "Slope Limit")), 20.0f) &&
                Near(AsFloat(Slot(result.Graph, path, "Sample Spacing")), 120.0f),
                Describe(result.Graph.Nodes[path].Values));

            bool endsToPath = false;
            bool pathToSpawner = false;

            foreach (var edge in result.Graph.Edges)
            {
                if (edge.OutputNodeIndex == ends && edge.InputNodeIndex == path)
                    endsToPath = true;

                if (edge.OutputNodeIndex == path)
                    pathToSpawner = result.Graph.Nodes[edge.InputNodeIndex].TypeId == PCGSpawnStaticModelNode.Id;
            }

            check("路的链是 端点 → 规划 → 路面生成器", endsToPath && pathToSpawner);

            // 同一次描述编译两次必须完全一样：图会被存下来，编译结果不稳定就等于每次打开都不一样。
            var again = PCGSpecCompiler.Compile(spec, Resolver);

            check("同一份描述编译两次结果完全一致", SameGraph(result.Graph, again.Graph));

            // 最小描述：只有 world / seed / region。区域是**场景侧**的设置，不该在图里冒出节点。
            bool only = PCGSpecParser.TryParse("world 空图\nseed 5\nregion 2000 1600 height 800", out var bare, out _);
            var bareResult = PCGSpecCompiler.Compile(bare, Resolver);

            check("区域尺寸不进图（它由宿主设到体积上）",
                only && bareResult.Ok && bareResult.Graph.NodeCount == 0 &&
                Near(V(bare.Region.SizeX), 2000.0f) && Near(V(bare.Region.Height), 800.0f),
                bareResult.Graph?.NodeCount.ToString());
        }

        // ---- 同一个特征可以写多次 -------------------------------------------------------------------------

        /// <summary>
        /// 村子、城市、路、散布层都是**可重复**的：一段描述里写两个村子，就该有两套地块规则、两条生成链。
        /// </summary>
        /// <remarks>
        /// 这条曾经是缺口：村子与城市被写成单例，第二行 `village` 会顶掉第一行，两处只认得一个。单例与可重复
        /// 在**解析层**都只是"多了一条记录"，所以只数记录条数还不够——还得在图里看到两条各带自己参数的链，
        /// 并且两个节点没被摆到同一个位置（摆重了在编辑器里就是两框叠在一起，看着像只有一个）。
        /// </remarks>
        private static void RepeatableFeatureTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 两村一城\n" +
                "village lots 8 rules \"X:0.5,0.5\" cell 100 40 100 model \"Assets/PCG/A.flax\"\n" +
                "village lots 20 rules \"Z:0.6,0.4\" cell 160 40 160 model \"Assets/PCG/B.flax\"\n" +
                "city productions \"城区 = 街区: X:0.5,0.5; 街区 = 房子: Z:0.5,0.5\" " +
                "root 城区 cell 200 40 200 model \"Assets/PCG/C.flax\"",
                out var spec, out string error);

            check("两行 village 收成两条记录，各自的选项互不串", parsed &&
                spec.Settlements.Count == 2 &&
                I(spec.Settlements[0].Lots) == 8 && spec.Settlements[0].Rules == "X:0.5,0.5" &&
                I(spec.Settlements[1].Lots) == 20 && spec.Settlements[1].Rules == "Z:0.6,0.4" &&
                spec.Settlements[0].ModelPath == "Assets/PCG/A.flax" &&
                spec.Settlements[1].ModelPath == "Assets/PCG/B.flax",
                parsed ? string.Join(" | ", spec.Settlements.ConvertAll(s => $"{s.Lots}/{s.Rules}")) : error);

            // 正面同夹具：只写一行时仍然只有一条记录。上面那条"两条"若是靠"永远返回两倍"实现，这条就会红。
            bool one = PCGSpecParser.TryParse("world 一村\nvillage lots 9\n", out var singly, out string singleError);

            check("只写一行 village 就只有一条记录（上一条的正面同夹具）",
                one && singly.Settlements.Count == 1 && I(singly.Settlements[0].Lots) == 9, singleError);

            if (!parsed)
                return;

            var result = PCGSpecCompiler.Compile(spec, Resolver);

            check("两村一城编译成三条链", result.Ok, result.Error ?? "ok");

            if (!result.Ok)
                return;

            var kinds = new List<string>();

            foreach (var node in result.Graph.Nodes)
                kinds.Add(node.TypeId);

            // 两个村子 = 两个切分语法；一个城市 = 一个符号语法；三层各配一个房屋/城区生成器。
            check("节点数跟着写法走：两个切分 + 一个符号语法 + 三个生成器",
                Count(kinds, PCGSplitGrammarNode.Id) == 2 &&
                Count(kinds, PCGGrammarNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 3,
                string.Join(", ", kinds));
            check("三条链各一条连线（每条链还多一步落地投影）", result.Graph.EdgeCount == 6, result.Graph.EdgeCount.ToString());
            check("三条链各一条连线", result.Graph.EdgeCount == 6, result.Graph.EdgeCount.ToString());

            // 关键：第二个村子必须带**它自己**的参数。若第二次写入被忽略，这里读到的会是第一个村子的规则。
            int second = IndexOf(result.Graph, PCGSplitGrammarNode.Id, 1);

            check("第二个村子的规则与地块尺寸落在它自己的槽位上",
                Slot(result.Graph, second, "Rules") as string == "Z:0.6,0.4" &&
                Slot(result.Graph, second, "Cell Size") is Vector3 cell && Near(cell.X, 160.0f),
                Describe(result.Graph.Nodes[second].Values));

            // 摆位：三个语法节点必须各占一行。摆重了在编辑器里就是两个框叠在一起，看着像只有一个。
            int first = IndexOf(result.Graph, PCGSplitGrammarNode.Id, 0);
            int city = IndexOf(result.Graph, PCGGrammarNode.Id, 0);

            float y0 = result.Graph.Nodes[first].EditorPosition.Y;
            float y1 = result.Graph.Nodes[second].EditorPosition.Y;
            float y2 = result.Graph.Nodes[city].EditorPosition.Y;

            check("三个语法节点各占一行，没有叠在一起",
                !Near(y0, y1) && !Near(y0, y2) && !Near(y1, y2), $"{y0} / {y1} / {y2}");
        }

        // ---- 一条路可以拐弯 -------------------------------------------------------------------------------

        /// <summary>
        /// `via`：中途必须经过的点。
        /// </summary>
        /// <remarks>
        /// 只有两端点的"路"画不出盘山道。这一组证明途经点**真的被用上**——不是"解析器收下了、编译时丢了"。
        /// 最后一跑是关键：一条 deliberately 偏离直线的 via，如果规划出来的路仍然是一条直线，那它就是被忽略了。
        /// </remarks>
        private static void RoadViaTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 弯路\nroad from -300 -300 via 0 300 via 200 -400 to 300 -300 detour 1500",
                out var spec, out string error);

            check("带两个途经点的路能解析", parsed, error);

            if (!parsed)
                return;

            var waypoints = spec.Roads[0].Waypoints();

            check("途经点按 起点 → 各 via → 终点 的顺序排好",
                spec.Roads[0].Vias.Count == 2 && waypoints.Count == 4 &&
                Near(waypoints[0].X, -300.0f) && Near(waypoints[0].Y, -300.0f) &&
                Near(waypoints[1].X, 0.0f) && Near(waypoints[1].Y, 300.0f) &&
                Near(waypoints[2].X, 200.0f) && Near(waypoints[2].Y, -400.0f) &&
                Near(waypoints[3].X, 300.0f) && Near(waypoints[3].Y, -300.0f),
                string.Join(" | ", waypoints.ConvertAll(p => $"{p.X},{p.Y}")));

            // 上限与它的一行报错。八个槽位是字面点节点给的硬边界。
            Error(check, "第 2 行", "world x\nroad from 0 0 via 1 1 via 2 2 via 3 3 via 4 4 via 5 5 via 6 6 via 7 7 to 8 8",
                "途经点超过槽位数");
            Error(check, "第 2 行", "world x\nroad from 0 0 via 5 to 8 8", "via 少一个数");

            // 六个 via 正好填满八个槽位，必须能过——上一条的正面同侧。
            bool full = PCGSpecParser.TryParse(
                "world x\nroad from 0 0 via 1 1 via 2 2 via 3 3 via 4 4 via 5 5 via 6 6 to 8 8",
                out var packed, out error);

            check("六个 via 正好填满八个槽位（上一条的正面同侧）",
                full && packed.Roads[0].Waypoints().Count == 8, error);

            // 编译：全部途经点按顺序写进字面点的槽位，用不到的槽位留空（于是节点的默认值照旧生效）。
            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            check("带弯的路能编译", compiled.Ok, compiled.Error ?? "ok");

            if (!compiled.Ok)
                return;

            int ends = IndexOf(compiled.Graph, PCGPointListNode.Id, 0);

            check("全部途经点按顺序写进字面点的槽位",
                AsInt(Slot(compiled.Graph, ends, "Count")) == 4 &&
                Slot(compiled.Graph, ends, "Point 1") is Vector3 p1 && Near(p1.X, -300.0f) && Near(p1.Z, -300.0f) &&
                Slot(compiled.Graph, ends, "Point 2") is Vector3 p2 && Near(p2.X, 0.0f) && Near(p2.Z, 300.0f) &&
                Slot(compiled.Graph, ends, "Point 3") is Vector3 p3 && Near(p3.X, 200.0f) && Near(p3.Z, -400.0f) &&
                Slot(compiled.Graph, ends, "Point 4") is Vector3 p4 && Near(p4.X, 300.0f) && Near(p4.Z, -300.0f),
                Describe(compiled.Graph.Nodes[ends].Values));

            check("用不到的槽位留空，好让节点自己的默认值生效",
                Slot(compiled.Graph, ends, "Point 5") == null);

            // 真跑一遍：平地、预览模式，看这条路有没有真的绕经那个点。
            if (!PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                check("带弯的路能过图编译器", false, "compile failed");
                return;
            }

            var world = new PCGWorldContext { HeightSource = FlatHeight };
            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = world,
                GenerationBounds = new BoundingBox(new Vector3(-600, -100, -600), new Vector3(600, 100, 600)),
                DryRun = true,
            };

            var route = new PCGGraphExecutor(program).Execute(context).PrimaryOutput;

            check("带弯的路跑得出点", route != null && route.Count > 1,
                route == null ? "null" : $"{route.Count} point(s)");

            if (route == null || route.Count < 2)
                return;

            // 对照用的孪生夹具：同样的起终点、同样的参数，**只是没有 via**。有 via 与没 via 的差别只能由 via 解释。
            PCGSpecParser.TryParse(
                "world 直路\nroad from -300 -300 to 300 -300 detour 1500", out var straight, out _);

            var straightGraph = PCGSpecCompiler.Compile(straight, Resolver);
            var straightRoute = PCGGraphCompiler.TryCompile(straightGraph.Graph, out var straightProgram)
                ? new PCGGraphExecutor(straightProgram).Execute(context).PrimaryOutput
                : null;

            check("对照的那条直路跑得出点", straightRoute != null && straightRoute.Count > 1,
                straightRoute == null ? "null" : $"{straightRoute.Count} point(s)");

            if (straightRoute == null || straightRoute.Count < 2)
                return;

            // 求最大值的累加器必须从 float.MinValue 起：从 0 起的话，一条整段都在 Z=-300 的路会"报成 0"，
            // 于是对照组与实验组的差被凭空缩小，而断言看起来还是通过的。
            float viaNorth = float.MinValue;
            float viaClosest = float.MaxValue;
            float straightNorth = float.MinValue;

            for (int i = 0; i < route.Count; i++)
            {
                var p = route.Points[i].Position;
                viaNorth = Math.Max(viaNorth, p.Z);
                viaClosest = Math.Min(viaClosest, (new Vector2(p.X, p.Z) - new Vector2(0.0f, 300.0f)).Length);
            }

            for (int i = 0; i < straightRoute.Count; i++)
                straightNorth = Math.Max(straightNorth, straightRoute.Points[i].Position.Z);

            // 拐弯幅度**由对照给出**，而不是我拍一个数：via 离直线 600，路至少要走过去一半。
            // 不能要求"正好落在 via 上"——`Path` 默认有三遍切角平滑，一个尖角会被磨圆，见 §5.1.35。
            check("有 via 的路比没 via 的路明显更靠北（孪生对照）",
                viaNorth - straightNorth > 100.0f,
                $"有 via 最北 Z={viaNorth:F1}，没 via 最北 Z={straightNorth:F1}");

            check("路是朝着那个途经点去的（至少走完离线距离的一半）",
                viaClosest <= 300.0f, $"离 via 最近 {viaClosest:F1}（via 离线 600）");
        }

        // ---- 按地貌撒点 ---------------------------------------------------------------------------------

        /// <summary>
        /// `scatter … biome hill,mountain`：把"判出地貌"接到"按地貌生成"。
        /// </summary>
        /// <remarks>
        /// 这一条的要害在**对照**：只断言"过滤之后的点都在丘陵/山地"是不够的——一个什么都不产的链也满足它。
        /// 所以同时跑一遍 `biome plain`，并要求两组点离山顶的距离**明显不同**：一组能贴着山顶，另一组碰不到。
        /// </remarks>
        private static void BiomeFilterTests(CheckDelegate check)
        {
            const string terrain = "\nbiome cell 100 water 10 relief 120 region 600 hill-slope 6 mountain-slope 20 samples 4";

            var rocky = RunSpec("world 山地\n" + string.Empty + "biome cell 100 water 10 relief 120 region 600 " +
                                "hill-slope 6 mountain-slope 20 samples 4\nscatter rocky biome hill,mountain", Hill);

            var rockyBiomes = rocky?.SharedAttributes?.TryGetFloatArray(PCGBiomeNode.BiomeColumn);

            check("按地貌撒点跑得出点",
                rocky != null && rocky.Count > 0 && rockyBiomes != null,
                rocky == null ? "null" : $"{rocky.Count} point(s)");

            if (rocky == null || rocky.Count == 0 || rockyBiomes == null)
                return;

            // 每一个留下来点的 Biome 都必须落在写下的那一档里。
            int outside = 0;

            for (int i = 0; i < rocky.Count; i++)
                if (rockyBiomes[i] < 1.0f || rockyBiomes[i] > 2.0f)
                    outside++;

            check("留下来的点没有一个跑出 hill..mountain", outside == 0, $"{outside} 个越界");

            // 对照：同一片地形上只取平原，那组点应当碰不到山顶。
            var flat = RunSpec("world 平原\n" + string.Empty + "biome cell 100 water 10 relief 120 region 600 " +
                               "hill-slope 6 mountain-slope 20 samples 4\nscatter grass biome plain", Hill);

            check("对照（只取平原）也跑得出点",
                flat != null && flat.Count > 0, flat == null ? "null" : $"{flat.Count} point(s)");

            if (flat == null || flat.Count == 0)
                return;

            float rockyClosest = ClosestToPeak(rocky);
            float flatClosest = ClosestToPeak(flat);

            check("山地那组比平原那组更靠近山顶（对照证明过滤真的换了地方）",
                rockyClosest < flatClosest - 100.0f,
                $"山地最近 {rockyClosest:F0} vs 平原最近 {flatClosest:F0}");

            // 报错：不认识的名字、以及不连成一档的写法。两件都由**编译器**报出而不是解析器——
            // 地貌名字表在分类器那边，解析层只记下"写了什么、写在第几行"，于是名字只有一个真相；
            // 行号是解析器存下来的，所以编译器照样能指到那一行。
            string unknown = CompileError("world x\nscatter forest biome swamp");

            check("不认识的地貌名由编译器带行号报出",
                unknown != null && unknown.Contains("第 2 行") && unknown.Contains("不认识地貌"),
                unknown ?? "没有报错");

            string split = CompileError("world x\nscatter forest biome plain,mountain");

            check("不连成一档的地貌由编译器报出并说明怎么办",
                split != null && split.Contains("第 2 行") && split.Contains("连成一档"),
                split ?? "没有报错");

            // 正面同夹具：连成一档的写法必须能解析、也能编译过。
            bool positive = PCGSpecParser.TryParse("world x\nscatter forest biome hill,mountain", out var ok,
                out string error);

            check("连成一档的写法能解析（上一条的正面同侧）",
                positive && ok.Scatters[0].BiomeNames == "hill,mountain" && ok.Scatters[0].BiomeLine == 2, error);

            check("连成一档的写法也能编译过（上一条的正面同侧）",
                CompileError("world x\nbiome\nscatter forest biome hill,mountain") == null);

            // 抖动：按地貌撒点的来源是**等距格心**，不抖的话林子会长成整齐的果园。
            // 判据用"格阵性"而不是"离山顶多远"：不抖时每个点的 X 与首点的 X 之差都是格边长的整数倍，
            // 抖过之后绝大多数不是。写 `jitter 0` 就是明确要格阵（摆路灯那种），此时它必须仍然整齐。
            const string hillBiome = "biome cell 100 water 10 relief 120 region 600 hill-slope 6 mountain-slope 20 samples 4";

            var defaulted = RunSpec("world 山地\n" + hillBiome + "\nscatter forest biome hill,mountain", Hill);
            var pinned = RunSpec("world 山地\n" + hillBiome + "\nscatter forest biome hill,mountain jitter 0", Hill);

            check("默认给了抖动：点子不再落在格阵上",
                defaulted != null && defaulted.Count > 0 && OnLattice(defaulted, 100.0f) < defaulted.Count / 2,
                defaulted == null ? "null" : $"格阵上 {OnLattice(defaulted, 100.0f)}/{defaulted.Count}");

            check("写 jitter 0 时保持格阵（上一条的对照）",
                pinned != null && pinned.Count > 0 && OnLattice(pinned, 100.0f) == pinned.Count,
                pinned == null
                    ? "null"
                    : $"格阵上 {OnLattice(pinned, 100.0f)}/{pinned.Count}；X 前几个 " +
                      string.Join(",", FirstFewX(pinned)));
        }

        /// <summary>取前几个点的 X，供失败时把数字摆出来看。</summary>
        private static List<string> FirstFewX(PCGData data)
        {
            var values = new List<string>();

            for (int i = 0; i < data.Count && i < 6; i++)
                values.Add(data.Points[i].Position.X.ToString("F2"));

            return values;
        }

        /// <summary>X 与首点之差是格边长整数倍的点有几个——也就是"看起来还落在格阵上"的点数。</summary>
        private static int OnLattice(PCGData data, float cell)        {
            if (data == null || data.Count == 0)
                return 0;

            float origin = data.Points[0].Position.X;
            int onLattice = 0;

            for (int i = 0; i < data.Count; i++)
            {
                float steps = (data.Points[i].Position.X - origin) / cell;
                float fraction = steps - (float)Math.Floor(steps);
                bool aligned = fraction < 0.01f || fraction > 0.99f;

                if (aligned)
                    onLattice++;
            }

            return onLattice;
        }

        /// <summary>解析加编译一份描述，返回第一个错误；全过就给 null。</summary>
        private static string CompileError(string text)
        {
            if (!PCGSpecParser.TryParse(text, out var spec, out string parseError))
                return parseError;

            var result = PCGSpecCompiler.Compile(spec, Resolver);

            return result.Ok ? null : result.Error;
        }

        /// <summary>一组点里离山头（原点，夹具里的山就立在原点）最近的距离。</summary>
        private static float ClosestToPeak(PCGData data)
        {
            float closest = float.MaxValue;

            for (int i = 0; i < data.Count; i++)
            {
                var p = data.Points[i].Position;
                closest = Math.Min(closest, (float)Math.Sqrt(p.X * p.X + p.Z * p.Z));
            }

            return closest;
        }

        // ---- 每条特征链都真跑一遍 -------------------------------------------------------------------------

        /// <summary>
        /// 地貌、水、聚居地、洞穴这四条链**真跑**一遍，而不只是编译过。
        /// </summary>
        /// <remarks>
        /// 补这一组的原因是一次自查：规格层此前只有**散布链**有"编译之后真跑出点"的证据，另外五条
        /// （含道路，它后来在 <see cref="RoadViaTests"/> 里补上了）只被**编译**过。编译绿与"它真产出了东西"
        /// 是两件事——一个把输入原样吃掉或悄悄返回空层的节点，编译阶段一点毛病都看不出来。
        /// </remarks>
        // ---- 一层可以挨着另一层长 -------------------------------------------------------------------------

        /// <summary>
        /// `near` / `away`：一层的位置由**另一层**决定，而不是由它自己的属性决定。
        /// </summary>
        /// <remarks>
        /// 在它之前，"湖边的芦苇、路两侧的树、城外的小摊"都表达不出来：每一层只能按自己的坡度/密度/地貌筛。
        /// 落地形式是取另一条链已经产出的点当参照、逐点量出最近距离写进一列、再按那一列筛。这一组钉三件事：
        /// 两个值分别落到名字与半径上、参照接在 <b>Reference</b> 这个具名引脚上（不是碰巧第 0 号对第 0 号）、
        /// 以及真跑一遍之后**留下来的点确实在半径以内**（配一个"没写关系时确实有更远的点"的对照）。
        /// </remarks>
        private static void RelationTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 湖边\nwater cell 40\nscatter reeds per-block 4 near water 60",
                out var spec, out string error);

            check("near 的两个值分别落到名字与半径上",
                parsed && spec.Scatters[0].NearSource == "water" && Near(V(spec.Scatters[0].NearRadius), 60.0f) &&
                spec.Scatters[0].AwaySource == null,
                parsed ? $"{spec.Scatters[0].NearSource} {spec.Scatters[0].NearRadius}" : error);

            bool awayOk = PCGSpecParser.TryParse(
                "world 村外\nscatter trees per-block 2 away village 200", out var awaySpec, out string awayError);

            check("away 同样落位，而且不会把 near 也填上",
                awayOk && awaySpec.Scatters[0].AwaySource == "village" &&
                Near(V(awaySpec.Scatters[0].AwayRadius), 200.0f) && awaySpec.Scatters[0].NearSource == null,
                awayOk ? $"{awaySpec.Scatters[0].AwaySource} {awaySpec.Scatters[0].AwayRadius}" : awayError);

            // 每类错都要带行号——没有行号的"解析失败"等于让人从头数。
            Error(check, "第 2 行", "world x\nscatter trees near water 0", "near 的半径不是正数");
            Error(check, "第 2 行", "world x\nscatter trees near water", "near 少一个值");
            Error(check, "第 2 行", "world x\nscatter trees near water 10 away water 20",
                "一层同时写了 near 与 away");

            // 正面同夹具：把半径改成正的、把 away 放到**另一层**上，两条都必须能解析。
            bool twin = PCGSpecParser.TryParse(
                "world x\nscatter trees near water 1\nscatter rocks away water 20", out var both, out error);

            check("同一个名字可以分别被两层参照（上一条的正面同夹具）",
                twin && both.Scatters.Count == 2 && both.Scatters[0].NearSource == "water" &&
                both.Scatters[1].AwaySource == "water", error);

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            check("带位置关系的描述能编译", compiled.Ok, compiled.Error ?? "ok");

            if (compiled.Ok)
            {
                var kinds = new List<string>();

                foreach (var node in compiled.Graph.Nodes)
                    kinds.Add(node.TypeId);

                check("位置关系落成「距离 → 筛选」两个节点，串在生成器之前（散布链另多一步落地）",
                    Count(kinds, PCGDistanceToPointsNode.Id) == 1 &&
                    Count(kinds, PCGAttributeFilterNode.Id) == 1 &&
                    Count(kinds, PCGSpawnStaticModelNode.Id) == 2 &&
                    compiled.Graph.EdgeCount == 6,
                    string.Join(", ", kinds) + $" | {compiled.Graph.EdgeCount} edge(s)");

                int hydrology = IndexOf(compiled.Graph, PCGHydrologyNode.Id, 0);
                int distance = IndexOf(compiled.Graph, PCGDistanceToPointsNode.Id, 0);
                int range = IndexOf(compiled.Graph, PCGAttributeFilterNode.Id, 0);
                int spawner = IndexOf(compiled.Graph, PCGSpawnStaticModelNode.Id, 0);

                // 参照必须接在 Reference（第 1 号输入）上。接成第 0 号的话图照跑——只是把"要量距离的点"
                // 和"参照点"当成了同一层，量出来的数毫无意义，而且不会报任何错。
                bool referenceOk = false;
                bool distanceFeedsRange = false;
                bool rangeFeedsSpawner = false;

                foreach (var edge in compiled.Graph.Edges)
                {
                    if (edge.InputNodeIndex == distance && edge.OutputNodeIndex == hydrology)
                        referenceOk = edge.InputPinIndex == 1;

                    if (edge.OutputNodeIndex == distance && edge.InputNodeIndex == range)
                        distanceFeedsRange = true;

                    if (edge.OutputNodeIndex == range && edge.InputNodeIndex == spawner)
                        rangeFeedsSpawner = true;
                }

                check("参照接在 Reference 引脚上，且来自那条链的产出节点", referenceOk);
                check("链是 距离 → 筛选 → 生成器", distanceFeedsRange && rangeFeedsSpawner);

                check("筛选读的是距离那一列，半径进了上界、下界为零、不取反",
                    Slot(compiled.Graph, range, "Attribute") as string == PCGDistanceToPointsNode.DefaultAttribute &&
                    Near(AsFloat(Slot(compiled.Graph, range, "Max")), 60.0f) &&
                    Near(AsFloat(Slot(compiled.Graph, range, "Min")), 0.0f) &&
                    Slot(compiled.Graph, range, "Invert") is bool invert && !invert,
                    Describe(compiled.Graph.Nodes[range].Values));
            }

            // 前向引用：`near water` 写在 `water` 之前也必须成立——"湖边的芦苇"当然是先说芦苇。
            var forward = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 湖\nscatter reeds per-block 4 near water 60\nwater cell 40"), Resolver);

            check("引用写在被引用者之前也能编译（前向引用）", forward != null && forward.Ok,
                forward == null ? "没解析过" : forward.Error ?? "ok");

            if (forward != null && forward.Ok)
            {
                int water = IndexOf(forward.Graph, PCGHydrologyNode.Id, 0);
                int distance = IndexOf(forward.Graph, PCGDistanceToPointsNode.Id, 0);
                bool wired = false;

                foreach (var edge in forward.Graph.Edges)
                {
                    if (edge.InputNodeIndex == distance && edge.OutputNodeIndex == water)
                        wired = true;
                }

                check("前向引用也真的接到了那条链上", wired);
            }

            // 引用不到的名字：带行号说清楚，并把能用的名字列出来。
            var unknown = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world x\nscatter reeds per-block 4 near lava 60"), Resolver);

            check("引用不存在的名字是编译错误，并列出能用的名字",
                unknown != null && !unknown.Ok && unknown.Error.Contains("第 2 行") && unknown.Error.Contains("lava"),
                unknown == null ? "没解析过" : unknown.Error ?? "居然编译过了");

            // 两层互相以对方为参照就是环。不拦的话，其中一条会按对方**筛之前**的点去量——结果看着合理，最难查。
            var cycle = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world x\nscatter a per-block 2 near b 50\nscatter b per-block 2 near a 50"), Resolver);

            check("两层互相参照被报成环",
                cycle != null && !cycle.Ok && cycle.Error.Contains("环"),
                cycle == null ? "没解析过" : cycle.Error ?? "居然编译过了");

            // 多个村子时裸名 `village` 指不清是哪一个，得让写的人改用 village1 / village2。
            var ambiguous = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world x\nvillage lots 4\nvillage lots 6\nscatter t per-block 2 near village 50"),
                Resolver);

            check("同名的多个特征不能用裸名参照，报错里给出可用的名字",
                ambiguous != null && !ambiguous.Ok && ambiguous.Error.Contains("village1") &&
                ambiguous.Error.Contains("village2"),
                ambiguous == null ? "没解析过" : ambiguous.Error ?? "居然编译过了");

            // 真跑一遍：平地 + 一条从 (-400,0) 笔直向东到 (400,0) 的路（Z 恒为 0）。于是任一点到这条路的
            // **最近**距离恒 ≥ |Z|（在路段正上方时正好等于 |Z|），所以"留下来的点 |Z| ≤ 半径"是可判的。
            // 撒点用 600 个点（格距约 120），半径取 400——否则太稀的话可能一个点都不在走廊里，"筛完为空"会
            // 让下一条断言失去意义（第一版就是 60 点配 150 半径，结果一个都没留下）。
            const string road = "road from -400 0 to 400 0 cell 40 slope 20 spacing 100";
            const float corridor = 400.0f;

            var loose = RunSpecNode("world 全撒\nscatter trees points 600\n" + road,
                PCGSpawnStaticModelNode.Id, 0, FlatHeight);
            var tight = RunSpecNode("world 沿路\nscatter trees points 600 near road 400\n" + road,
                PCGSpawnStaticModelNode.Id, 0, FlatHeight);

            // 生成器没指模型会报"没有指定模型"，那是这一组夹具的有意省略，不是接线问题。真正要盯的是两个
            // **静默透传**的理由：参照层为空（Distance To Points）与列读不到（Attribute Filter）——这两条
            // 一旦出现，筛选就完全没生效，而图照跑、点数照旧。
            check("没有出现「参照层为空」或「列读不到」这类理由",
                !Mentions(loose.Warnings, "Distance To Points") && !Mentions(loose.Warnings, "Attribute Filter") &&
                !Mentions(tight.Warnings, "Distance To Points") && !Mentions(tight.Warnings, "Attribute Filter"),
                string.Join(" | ", tight.Warnings));

            check("两份描述都跑得出点（下一条的前提）",
                loose.Points != null && loose.Points.Count > 0 && tight.Points != null && tight.Points.Count > 0,
                $"{loose.Points?.Count ?? -1} / {tight.Points?.Count ?? -1}");

            if (loose.Points == null || tight.Points == null || loose.Points.Count == 0 || tight.Points.Count == 0)
                return;

            float looseFar = 0.0f;

            foreach (var point in loose.Points)
                looseFar = Math.Max(looseFar, Math.Abs(point.Z));

            float tightFar = 0.0f;

            foreach (var point in tight.Points)
                tightFar = Math.Max(tightFar, Math.Abs(point.Z));

            check("没写关系时确实有远在半径之外的点（下一条的对照）",
                looseFar > corridor, $"最远 |Z| = {looseFar:F1}");

            check("写了 near road 之后，留下来的点全部在半径以内",
                tightFar <= corridor + 0.5f, $"最远 |Z| = {tightFar:F1}（对照 {looseFar:F1}）");

            check("筛掉了一些点（不是「碰巧一个都没超」）",
                tight.Points.Count < loose.Points.Count, $"{tight.Points.Count} < {loose.Points.Count}");

            // `fade`：不动点的位置，把"到参照的距离"重映射成**密度**，于是越远越稀而不是一刀切。
            bool faded = PCGSpecParser.TryParse(
                "world 湖边\nwater cell 40\nscatter reeds per-block 4 fade water 300", out var fadeSpec,
                out string fadeError);

            check("fade 的两个值分别落到名字与半径上",
                faded && fadeSpec.Scatters[0].FadeSource == "water" &&
                Near(V(fadeSpec.Scatters[0].FadeRadius), 300.0f) &&
                fadeSpec.Scatters[0].NearSource == null && fadeSpec.Scatters[0].AwaySource == null,
                faded ? $"{fadeSpec.Scatters[0].FadeSource} {fadeSpec.Scatters[0].FadeRadius}" : fadeError);

            Error(check, "第 2 行", "world x\nscatter trees fade water 0", "fade 的半径不是正数");
            Error(check, "第 2 行", "world x\nscatter trees near water 10 fade water 20", "near 与 fade 一起写");

            // 正面同夹具：三种关系各写一层，必须都能解析。
            bool allThree = PCGSpecParser.TryParse(
                "world x\nscatter a near water 10\nscatter b away water 20\nscatter c fade water 30",
                out var three, out error);

            check("三种位置关系可以分别用在三层上（上一条的正面同夹具）",
                allThree && three.Scatters.Count == 3 && three.Scatters[0].NearSource == "water" &&
                three.Scatters[1].AwaySource == "water" && three.Scatters[2].FadeSource == "water", error);

            var fadedCompile = PCGSpecCompiler.Compile(fadeSpec, Resolver);

            check("带 fade 的描述能编译", fadedCompile.Ok, fadedCompile.Error ?? "ok");

            if (fadedCompile.Ok)
            {
                var kinds = new List<string>();

                foreach (var node in fadedCompile.Graph.Nodes)
                    kinds.Add(node.TypeId);

                // fade 用的是**重映射**而不是过滤器：一刀切与渐变是两件事，节点也就不是一个。
                check("fade 落成「距离 → 重映射」，没有过滤器（散布链另多一步落地）",
                    Count(kinds, PCGDistanceToPointsNode.Id) == 1 &&
                    Count(kinds, PCGRemapAttributeNode.Id) == 1 &&
                    Count(kinds, PCGAttributeFilterNode.Id) == 0 &&
                    fadedCompile.Graph.EdgeCount == 6,
                    string.Join(", ", kinds) + $" | {fadedCompile.Graph.EdgeCount} edge(s)");

                int remap = IndexOf(fadedCompile.Graph, PCGRemapAttributeNode.Id, 0);

                // **目标名留空**是这条链的关键：两个节点共用"空名 = 写点的密度本身"这条约定，所以密度才会被
                // 生成器读到。写成别的列名不会报错，只是密度没变、"越远越稀"完全没发生。
                check("重映射读距离那一列、写回密度本身，方向是 1 → 0",
                    Slot(fadedCompile.Graph, remap, "Source Attribute") as string ==
                        PCGDistanceToPointsNode.DefaultAttribute &&
                    Slot(fadedCompile.Graph, remap, "Target Attribute") as string == string.Empty &&
                    Near(AsFloat(Slot(fadedCompile.Graph, remap, "In Min")), 0.0f) &&
                    Near(AsFloat(Slot(fadedCompile.Graph, remap, "In Max")), 300.0f) &&
                    Near(AsFloat(Slot(fadedCompile.Graph, remap, "Out Min")), 1.0f) &&
                    Near(AsFloat(Slot(fadedCompile.Graph, remap, "Out Max")), 0.0f) &&
                    Slot(fadedCompile.Graph, remap, "Clamp") is bool clamp && clamp,
                    Describe(fadedCompile.Graph.Nodes[remap].Values));
            }

            // fade 改的是**密度**、不动点的位置，所以它的数据层结果和硬砍不一样：一个点都不少（稀释发生在
            // 生成器"按密度抽点"那一步，而那是烘焙才做的事，预览/DryRun 看不到）。这条断言专门钉住这个差别——
            // 谁要是哪天把 fade 也写成"顺便删点"，它会红。
            var soft = RunSpecNode("world 渐变\nscatter trees points 600 fade road 400\n" + road,
                PCGSpawnStaticModelNode.Id, 0, FlatHeight);

            check("fade 一份也跑得出点，且点数与没写关系时相同（改密度、不删点）",
                soft.Points != null && soft.Points.Count == loose.Points.Count &&
                !Mentions(soft.Warnings, "Distance To Points") && !Mentions(soft.Warnings, "Remap"),
                $"{soft.Points?.Count ?? -1} vs {loose.Points.Count} | {string.Join(" | ", soft.Warnings)}");

            // 语义那一半用手搭的**同款链**来证：越远密度越小、到半径处为零、再远钳在零。位置是摆出来的，
            // 期望值能逐个算出来，所以这是"渐变"的直接证据，而不是"少留一半"那种也能满足的近似说法。
            FadeChainTests(check);
        }

        /// <summary>
        /// 手搭一条与编译器输出**同款**的 `距离 → 重映射` 链，逐点核对密度就是"到参照的距离"的线性衰减。
        /// </summary>
        /// <remarks>
        /// 为什么要另搭一条而不是从编译结果里读：生成器在 DryRun 下把点原样交下去，密度既不删点也不会体现在
        /// 位置上，所以规格层跑不出密度的数字。这里用**同一张注册表、同一批槽位名**搭出同一条链，读数就直接
        /// 落在 <c>PCGPoint.Density</c> 上——槽位名写错会当场报出来，不会退化成"读了个默认值"。
        /// </remarks>
        private static void FadeChainTests(CheckDelegate check)
        {
            const float radius = 300.0f;

            // 参照就一个点（原点），候选点沿 Z 轴摆开——PlaneOnly 默认开着，于是每个点到参照的距离恰好是 |Z|。
            var reference = Points(new[] { Vector3.Zero });
            var candidates = Points(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 75.0f),
                new Vector3(0.0f, 0.0f, 150.0f),
                new Vector3(0.0f, 0.0f, 225.0f),
                new Vector3(0.0f, 0.0f, 300.0f),
                new Vector3(0.0f, 0.0f, 400.0f),
            });

            var distance = PCGNodeRegistry.CreateNode(SlotNode(PCGDistanceToPointsNode.Id,
                ("Attribute", PCGDistanceToPointsNode.DefaultAttribute),
                ("Max Distance", radius)), 0, out string distanceError);

            var remap = PCGNodeRegistry.CreateNode(SlotNode(PCGRemapAttributeNode.Id,
                ("Source Attribute", PCGDistanceToPointsNode.DefaultAttribute),
                ("Target Attribute", string.Empty),
                ("In Min", 0.0f),
                ("In Max", radius),
                ("Out Min", 1.0f),
                ("Out Max", 0.0f),
                ("Clamp", true)), 0, out string remapError);

            check("手搭的链造得出节点（槽位名与注册表一致）",
                distance != null && remap != null, distanceError ?? remapError ?? "ok");

            if (distance == null || remap == null)
                return;

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext(),
                GenerationBounds = new BoundingBox(new Vector3(-500, -100, -500), new Vector3(500, 100, 500)),
                DryRun = true,
            };

            var measured = distance.OnExecute(context, new[] { candidates, reference });
            var faded = remap.OnExecute(context, new[] { measured });

            check("链跑得出点，且点数不变", faded != null && faded.Count == candidates.Count,
                faded == null ? "null" : $"{faded.Count}");

            if (faded == null || faded.Count != candidates.Count)
                return;

            // 期望密度 = clamp(1 - |Z| / 半径, 0, 1)：0 处满密度、半径处为零、再远仍是零（钳位那一段）。
            float worst = 0.0f;
            string report = string.Empty;

            for (int i = 0; i < faded.Count; i++)
            {
                float z = Math.Abs(candidates.Points[i].Position.Z);
                float expected = Math.Max(0.0f, 1.0f - z / radius);
                float actual = faded.Points[i].Density;
                float error = Math.Abs(actual - expected);

                worst = Math.Max(worst, error);

                if (report.Length == 0)
                    report = $"{z:F0} → {actual:F3}（该是 {expected:F3}）";
            }

            check("密度就是「到参照的距离」的线性衰减（含半径外钳到零）", worst <= 1e-3f,
                $"最大偏差 {worst:F4}；{report}");

            check("半径处的密度是零、半径外也是零（衰减到零后不会反弹）",
                Math.Abs(faded.Points[4].Density) <= 1e-3f && Math.Abs(faded.Points[5].Density) <= 1e-3f,
                $"{faded.Points[4].Density:F3} / {faded.Points[5].Density:F3}");
        }

        /// <summary>按**槽位名**搭一个手工节点——和编译器走同一张参数表，名字写错这里就会报出来。</summary>
        private static PCGNode SlotNode(string typeId, params (string Slot, object Value)[] values)
        {
            PCGNodeRegistry.TryGet(typeId, out var info);

            var node = new PCGNode { TypeId = typeId, Values = new object[info.ParameterCount] };

            foreach (var (slot, value) in values)
            {
                for (int i = 0; i < info.Parameters.Length; i++)
                {
                    if (string.Equals(info.Parameters[i].Name, slot, StringComparison.Ordinal))
                    {
                        node.Values[i] = value;
                        break;
                    }
                }
            }

            return node;
        }

        /// <summary>造一层点（密度默认满），位置给定。</summary>
        private static PCGData Points(Vector3[] positions)
        {
            var data = PCGData.Create(positions.Length);

            for (int i = 0; i < positions.Length; i++)
            {
                var point = PCGPoint.Create(positions[i], 1.0f, i);
                point.UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));
                data.Points[i] = point;
            }

            return data;
        }

        /// <summary>告警里有没有提到某个节点（用来盯"静默透传"那两类理由）。</summary>
        private static bool Mentions(string[] warnings, string needle)
        {
            if (warnings == null)
                return false;

            foreach (string warning in warnings)
            {
                if (warning != null && warning.Contains(needle))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// `within` / `outside`：一层长在**哪片区域**。在它之前，描述能决定"长什么"却决定不了"长在哪"，
        /// 每一层都铺满整个生成范围——"北边的针叶林、南边的草原"说不出来。
        /// </summary>
        /// <remarks>
        /// 断言分三层：四个数落到四个字段上（含 outside 的取反）、编译成 `Area Filter` 并串在点源与生成器之间、
        /// 以及**真跑一遍看位置**——区域过滤改的是成员（点没了），所以它在数据层就是看得见的，这与 `fade` 那种
        /// 只改密度的做法不同，不需要另搭链来验。
        /// </remarks>
        private static void AreaTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 北坡\nscatter pines per-block 2 within -800 -1500 800 0", out var spec, out string error);

            check("within 的四个数按「最小角 → 最大角」落到四个字段上",
                parsed && Near(V(spec.Scatters[0].AreaMinX), -800.0f) && Near(V(spec.Scatters[0].AreaMinZ), -1500.0f) &&
                Near(V(spec.Scatters[0].AreaMaxX), 800.0f) && Near(V(spec.Scatters[0].AreaMaxZ), 0.0f) &&
                !spec.Scatters[0].AreaInvert,
                parsed ? $"{spec.Scatters[0].AreaMinX},{spec.Scatters[0].AreaMinZ} → " +
                         $"{spec.Scatters[0].AreaMaxX},{spec.Scatters[0].AreaMaxZ}" : error);

            bool outside = PCGSpecParser.TryParse(
                "world 湖区\nscatter reeds per-block 2 outside -500 -500 500 500", out var outsideSpec,
                out string outsideError);

            check("outside 落成同一片区域加上取反",
                outside && Near(V(outsideSpec.Scatters[0].AreaMinX), -500.0f) &&
                Near(V(outsideSpec.Scatters[0].AreaMaxZ), 500.0f) && outsideSpec.Scatters[0].AreaInvert,
                outside ? $"{outsideSpec.Scatters[0].AreaInvert}" : outsideError);

            // 每类错都带行号。
            Error(check, "第 2 行", "world x\nscatter trees within -100 -100 100 100 within 0 0 1 1",
                "一层写了两次区域");
            Error(check, "第 2 行", "world x\nscatter trees within 100 100 -100 -100", "区域写反了");
            Error(check, "第 2 行", "world x\nscatter trees within 0 0 0 100", "区域退化成一条线");

            // 正面同夹具：区域与位置关系叠在同一层上，必须都能解析——"村北 400 以内、且只在这片区域"是常态。
            bool stacked = PCGSpecParser.TryParse(
                "world x\nroad from 0 0 to 100 100\nscatter trees within -50 -50 50 50 near road 120",
                out var stackSpec, out error);

            check("区域与 near 可以叠在同一层上（上一条的正面同夹具）",
                stacked && stackSpec.Scatters[0].AreaMinX.HasValue && stackSpec.Scatters[0].NearSource == "road",
                error);

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            check("带区域的描述能编译", compiled.Ok, compiled.Error ?? "ok");

            if (compiled.Ok)
            {
                var kinds = new List<string>();

                foreach (var node in compiled.Graph.Nodes)
                    kinds.Add(node.TypeId);

                check("区域落成一个 Area Filter，串在点源与生成器之间（点源先落地）",
                    Count(kinds, PCGAreaFilterNode.Id) == 1 && Count(kinds, PCGVolumeSamplerNode.Id) == 1 &&
                    Count(kinds, PCGSpawnStaticModelNode.Id) == 1 && compiled.Graph.EdgeCount == 3,
                    string.Join(", ", kinds) + $" | {compiled.Graph.EdgeCount} edge(s)");

                int source = IndexOf(compiled.Graph, PCGVolumeSamplerNode.Id, 0);
                int ground = IndexOf(compiled.Graph, PCGProjectPointsNode.Id, 0);
                int area = IndexOf(compiled.Graph, PCGAreaFilterNode.Id, 0);
                int spawner = IndexOf(compiled.Graph, PCGSpawnStaticModelNode.Id, 0);

                bool sourceToGround = false;
                bool groundToArea = false;
                bool areaToSpawner = false;

                foreach (var edge in compiled.Graph.Edges)
                {
                    if (edge.OutputNodeIndex == source && edge.InputNodeIndex == ground)
                        sourceToGround = true;

                    if (edge.OutputNodeIndex == ground && edge.InputNodeIndex == area)
                        groundToArea = true;

                    if (edge.OutputNodeIndex == area && edge.InputNodeIndex == spawner)
                        areaToSpawner = true;
                }

                check("链是 点源 → 落地 → 区域 → 生成器（顺序不能反）",
                    sourceToGround && groundToArea && areaToSpawner);

                check("四个边界与取反都写进了它自己的槽位",
                    Near(AsFloat(Slot(compiled.Graph, area, "Min X")), -800.0f) &&
                    Near(AsFloat(Slot(compiled.Graph, area, "Max X")), 800.0f) &&
                    Near(AsFloat(Slot(compiled.Graph, area, "Min Z")), -1500.0f) &&
                    Near(AsFloat(Slot(compiled.Graph, area, "Max Z")), 0.0f) &&
                    Slot(compiled.Graph, area, "Invert") is bool invert && !invert,
                    Describe(compiled.Graph.Nodes[area].Values));
            }

            var outsideCompiled = PCGSpecCompiler.Compile(outsideSpec, Resolver);

            check("outside 编译出来的节点取反了",
                outsideCompiled.Ok &&
                Slot(outsideCompiled.Graph, IndexOf(outsideCompiled.Graph, PCGAreaFilterNode.Id, 0), "Invert")
                    is bool flipped && flipped,
                outsideCompiled.Error ?? "ok");

            // 真跑一遍：600 个点铺满整个范围，区域取"北半部以外"（Z ≤ 0 那半），留下来的点必须全在 Z ≤ 0。
            // 位置是看得见的，所以这里直接拿坐标断言，不需要另搭链。
            var everywhere = RunSpecNode("world 全铺\nscatter trees points 600", PCGSpawnStaticModelNode.Id, 0,
                FlatHeight);
            var north = RunSpecNode("world 北坡\nscatter trees points 600 within -1500 -1500 1500 0",
                PCGSpawnStaticModelNode.Id, 0, FlatHeight);

            check("两份都跑得出点（下一条的前提）",
                everywhere.Points != null && everywhere.Points.Count > 0 &&
                north.Points != null && north.Points.Count > 0,
                $"{everywhere.Points?.Count ?? -1} / {north.Points?.Count ?? -1}");

            if (everywhere.Points == null || north.Points == null || north.Points.Count == 0 ||
                everywhere.Points.Count == 0)
                return;

            float looseNorth = float.MinValue;
            float areaNorth = float.MinValue;

            foreach (var point in everywhere.Points)
                looseNorth = Math.Max(looseNorth, point.Z);

            foreach (var point in north.Points)
                areaNorth = Math.Max(areaNorth, point.Z);

            check("没写区域时确实有落在南半边的点（下一条的对照）", looseNorth > 0.0f, $"最北 Z = {looseNorth:F1}");

            check("写了 within 之后，留下来的点全部落在 Z ≤ 0 那半边",
                areaNorth <= 0.0f + 1e-3f, $"最北 Z = {areaNorth:F1}（对照 {looseNorth:F1}）");

            check("区域筛掉了一些点（不是「碰巧那半边本来就空」）",
                north.Points.Count < everywhere.Points.Count,
                $"{north.Points.Count} < {everywhere.Points.Count}");
        }

        /// <summary>
        /// `at &lt;x&gt; &lt;z&gt;`：把村子、城市、洞穴摆在写下的位置。在它之前，这三样都从**生成范围的中心**
        /// 起算——`Point List` 的存在理由里就写着"村庄的中心、洞穴的入口"，可描述层一直没接上。
        /// </summary>
        /// <remarks>
        /// 断言分三层：两个数落到 X 与 Z（三个指令各一遍）、编译成"字面点 → 语法/洞穴"、以及**真跑一遍看位置**。
        /// 后一层是关键：位置变化在数据里看得见，所以拿坐标断言就够了，不需要另搭链。
        /// </remarks>
        private static void PlacementTests(CheckDelegate check)
        {
            bool village = PCGSpecParser.TryParse(
                "world x\nvillage at 500 300 lots 8 rules \"X:0.5,0.5\" cell 200 40 200", out var villageSpec,
                out string villageError);

            check("village 的 at 两个数落到 X 与 Z",
                village && Near(V(villageSpec.Settlements[0].AtX), 500.0f) &&
                Near(V(villageSpec.Settlements[0].AtZ), 300.0f),
                village ? $"{villageSpec.Settlements[0].AtX},{villageSpec.Settlements[0].AtZ}" : villageError);

            bool city = PCGSpecParser.TryParse(
                "world x\ncity at -200 400 productions \"A = B,B: X:0.5,0.5\" cell 400 40 400", out var citySpec,
                out string cityError);

            check("city 的 at 同样落位",
                city && Near(V(citySpec.Cities[0].AtX), -200.0f) && Near(V(citySpec.Cities[0].AtZ), 400.0f),
                city ? $"{citySpec.Cities[0].AtX},{citySpec.Cities[0].AtZ}" : cityError);

            bool cave = PCGSpecParser.TryParse("world x\ncave at 400 -200 depth 200", out var caveSpec,
                out string caveError);

            check("cave 的 at 也落位（洞口）",
                cave && Near(V(caveSpec.Cave.AtX), 400.0f) && Near(V(caveSpec.Cave.AtZ), -200.0f),
                cave ? $"{caveSpec.Cave.AtX},{caveSpec.Cave.AtZ}" : caveError);

            // 少写一个数由选项表拦下，带行号。
            Error(check, "第 2 行", "world x\nvillage at 500", "village 的 at 少一个数");
            Error(check, "第 2 行", "world x\ncity at 5", "city 的 at 少一个数");

            var compiled = PCGSpecCompiler.Compile(villageSpec, Resolver);

            check("带 at 的村子能编译", compiled.Ok, compiled.Error ?? "ok");

            if (compiled.Ok)
            {
                int ends = IndexOf(compiled.Graph, PCGPointListNode.Id, 0);
                int grammar = IndexOf(compiled.Graph, PCGSplitGrammarNode.Id, 0);

                check("字面点写成一个位置，并接在切分语法的输入上",
                    ends >= 0 && grammar >= 0 &&
                    AsInt(Slot(compiled.Graph, ends, "Count")) == 1 &&
                    Slot(compiled.Graph, ends, "Point 1") is Vector3 p && Near(p.X, 500.0f) && Near(p.Z, 300.0f) &&
                    Near(p.Y, 0.0f),
                    Describe(compiled.Graph.Nodes[ends].Values));

                bool wired = false;

                foreach (var edge in compiled.Graph.Edges)
                {
                    if (edge.OutputNodeIndex == ends && edge.InputNodeIndex == grammar)
                        wired = true;
                }

                check("字面点 → 切分语法 这条线真的接上了", wired);
            }

            // 真跑一遍：同一份描述只差在有没有 `at`，切出来的地块中心应当从原点挪到写下的那个点。
            const string rules = "rules \"X:0.5,0.5\" cell 200 40 200";

            var centred = RunSpecNode("world 默认\nvillage " + rules, PCGSplitGrammarNode.Id, 0, FlatHeight);
            var placed = RunSpecNode("world 指定\nvillage at 500 300 " + rules, PCGSplitGrammarNode.Id, 0, FlatHeight);

            check("两份都切得出地块（下一条的前提）",
                centred.Points != null && centred.Points.Count > 0 &&
                placed.Points != null && placed.Points.Count > 0,
                $"{centred.Points?.Count ?? -1} / {placed.Points?.Count ?? -1}");

            if (centred.Points == null || placed.Points == null || placed.Points.Count == 0 ||
                centred.Points.Count == 0)
                return;

            float centreFar = 0.0f;

            foreach (var point in centred.Points)
                centreFar = Math.Max(centreFar, (float)Math.Sqrt(point.X * point.X + point.Z * point.Z));

            float placedFar = 0.0f;
            float placedNear = float.MaxValue;

            foreach (var point in placed.Points)
            {
                float dx = point.X - 500.0f;
                float dz = point.Z - 300.0f;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);

                placedFar = Math.Max(placedFar, distance);
                placedNear = Math.Min(placedNear, distance);
            }

            check("没写 at 时地块留在生成范围的中心附近（下一条的对照）",
                centreFar <= 200.0f, $"最远离原点 {centreFar:F1}");

            check("写了 at 500 300 之后地块挪到了那个点上（近的那块就贴着它）",
                placedNear <= 100.0f && placedFar <= 200.0f,
                $"最近 {placedNear:F1} / 最远 {placedFar:F1}");

            // `rotate`：切分语法按**种子的朝向**决定沿哪条轴切，所以转种子等于把整块地的排布跟着转。
            // 90 度最干净：转完之后两块应当落在 Z 轴上，而不是 X 轴——轴换了就是真的转了。
            bool rotated = PCGSpecParser.TryParse(
                "world x\nvillage at 0 0 rotate 90 rules \"X:0.5,0.5\" cell 200 40 200", out var rotatedSpec,
                out string rotateError);

            check("rotate 的度数落位（要和 at 一起写）",
                rotated && Near(V(rotatedSpec.Settlements[0].RotateDegrees), 90.0f) &&
                Near(V(rotatedSpec.Settlements[0].AtX), 0.0f),
                rotated ? $"{rotatedSpec.Settlements[0].RotateDegrees}" : rotateError);

            var rotatedCompile = PCGSpecCompiler.Compile(rotatedSpec, Resolver);

            check("带 rotate 的村子能编译", rotatedCompile != null && rotatedCompile.Ok,
                rotatedCompile?.Error ?? "ok");

            if (rotatedCompile != null && rotatedCompile.Ok)
            {
                int ends = IndexOf(rotatedCompile.Graph, PCGPointListNode.Id, 0);
                int turn = IndexOf(rotatedCompile.Graph, PCGTransformPointsNode.Id, 0);
                int grammar = IndexOf(rotatedCompile.Graph, PCGSplitGrammarNode.Id, 0);

                check("链是 字面点 → 转一下 → 切分语法（顺序不能反）",
                    ends >= 0 && turn >= 0 && grammar >= 0 && ends < turn && turn < grammar,
                    $"{ends} / {turn} / {grammar}");

                check("只转 Y 轴，度数进了 Rotation Degrees 槽位",
                    Slot(rotatedCompile.Graph, turn, "Rotation Degrees") is Vector3 spin &&
                    Near(spin.X, 0.0f) && Near(spin.Y, 90.0f) && Near(spin.Z, 0.0f),
                    Describe(rotatedCompile.Graph.Nodes[turn].Values));

                bool seedToTurn = false;
                bool turnToGrammar = false;

                foreach (var edge in rotatedCompile.Graph.Edges)
                {
                    if (edge.OutputNodeIndex == ends && edge.InputNodeIndex == turn)
                        seedToTurn = true;

                    if (edge.OutputNodeIndex == turn && edge.InputNodeIndex == grammar)
                        turnToGrammar = true;
                }

                check("两条线都接上了（字面点 → 转 → 语法）", seedToTurn && turnToGrammar);
            }

            // 没有位置就没有转轴：编译期带行号说清楚，而不是悄悄把地挪到原点去。
            var noPosition = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world x\nvillage rotate 90 rules \"X:0.5,0.5\""), Resolver);

            check("写了 rotate 却没写 at 是编译错误，带行号并说明原因",
                noPosition != null && !noPosition.Ok && noPosition.Error.Contains("第 2 行") &&
                noPosition.Error.Contains("at"),
                noPosition == null ? "没解析过" : noPosition.Error ?? "居然编译过了");

            // 真跑一遍看轴：同一份描述只差 rotate，两块地块应当从 X 轴挪到 Z 轴上。
            var straight = RunSpecNode("world 正\nvillage at 0 0 rules \"X:0.5,0.5\" cell 200 40 200",
                PCGSplitGrammarNode.Id, 0, FlatHeight);
            var turned = RunSpecNode("world 斜\nvillage at 0 0 rotate 90 rules \"X:0.5,0.5\" cell 200 40 200",
                PCGSplitGrammarNode.Id, 0, FlatHeight);

            check("两份都切得出地块（下一条的前提）",
                straight.Points != null && straight.Points.Count == 2 &&
                turned.Points != null && turned.Points.Count == 2,
                $"{straight.Points?.Count ?? -1} / {turned.Points?.Count ?? -1}");

            if (straight.Points == null || turned.Points == null || straight.Points.Count != 2 ||
                turned.Points.Count != 2)
                return;

            check("没转时两块沿 X 轴分（|X| ≈ 50、|Z| ≈ 0）",
                Near(Math.Abs(straight.Points[0].X), 50.0f) &&
                Near(straight.Points[1].X, -straight.Points[0].X) &&
                Math.Abs(straight.Points[0].Z) <= 0.01f,
                $"{straight.Points[0]} / {straight.Points[1]}");

            check("转了 90 度之后两块改沿 Z 轴分（轴真的换了，不是只改了朝向）",
                Math.Abs(turned.Points[0].X) <= 0.01f && Math.Abs(turned.Points[1].X) <= 0.01f &&
                Near(Math.Abs(turned.Points[0].Z), 50.0f) &&
                Near(turned.Points[1].Z, -turned.Points[0].Z),
                $"{turned.Points[0]} / {turned.Points[1]}");

            // 洞穴：洞口就是种子点，所以输出的点里必然有一个正好落在写下的坐标上。
            var tunnel = RunSpecNode("world 洞\ncave at 400 -200 depth 200 segments 6 radius 40 tunnels 1 sides 8",
                PCGCaveNode.Id, 0, FlatHeight);

            check("洞穴跑得出点（下一条的前提）", tunnel.Points != null && tunnel.Points.Count > 0,
                tunnel.Points == null ? "null" : $"{tunnel.Points.Count} point(s)");

            if (tunnel.Points == null || tunnel.Points.Count == 0)
                return;

            int atEntrance = 0;

            foreach (var point in tunnel.Points)
            {
                if (Math.Abs(point.X - 400.0f) <= 0.01f && Math.Abs(point.Z + 200.0f) <= 0.01f)
                    atEntrance++;
            }

            check("洞口的点正好落在写下的坐标上（种子真的传到了洞穴）", atEntrance >= 1,
                $"落在 (400,-200) 的点有 {atEntrance} 个");
        }

        /// <summary>
        /// 三份随仓库交出去的示例，逐个**整份真跑**：每个节点都要跑到，每条产出链都要真出东西。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 在这之前，示例只被两件事盯过：CLI 的"节点数 / 连线数"（只证明编译得出来）和个别链的叶子数。
        /// 而示例是**用户唯一的入口**（编辑器第二行点「生成」就在跑它们），一条链悄悄产出零个点，用户看到的是
        /// 一片空世界，而所有绿灯都还亮着。所以这里补两件只有整份跑起来才看得见的事：
        /// </para>
        /// <para>
        /// **① 每个节点都得跑到。** 执行是拉式的：接不进任何汇的节点**根本不会被求值**。于是"节点数 == 有记录的
        /// 节点数"就等于"没有孤儿节点"——这条对示例尤其值钱，因为示例里同时有六七条链，改动一处很容易把某条链
        /// 接丢了，而编译、连线数、别的链的断言全都不会响。
        /// </para>
        /// <para>
        /// **② 每条产出链至少出一个点。** 判定类型与最小点数写在下表里，都是"这个节点凭什么存在"的那一层：
        /// `Biome`/`Hydrology`/`SplitGrammar`/`Grammar`/`Cave`/`Path` 各自产出新内容，`Spawn*` 透传且随链走。
        /// 顺便盯住那两条**静默透传**的理由（列读不到、参照层为空）没在示例里出现。
        /// </para>
        /// </remarks>
        // ---- 地形合成（描述层） --------------------------------------------------------------------------

        /// <summary>
        /// `terrain` 指令：形状表解析、错误带行号、配方真的进图设置、以及配方能存下来读回来。
        /// </summary>
        /// <remarks>
        /// 写入端（真把高度写进引擎的 Terrain）只能在 PIE 里验，这里管的是它前面那一半：描述 → 配方。
        /// 这一半写错的代价是"地面静默地不长"，所以每条都要求**值真的到了字段上**，而不是"解析通过了"。
        /// </remarks>
        private static void TerrainSpecTests(CheckDelegate check)
        {
            const string text =
                "world 地形试跑\n" +
                "seed 7\n" +
                "terrain noise 25 noise-scale 900 " +
                "shapes \"plateau 0 0 600 120 0.4; basin 900 -400 500 80\"\n" +
                "scatter rocks points 3";

            bool parsed = PCGSpecParser.TryParse(text, out var spec, out string error);
            check("带 terrain 的描述能解析", parsed, error);

            if (!parsed)
                return;

            check("terrain 的选项都落到了字段上",
                spec.HasTerrain && Near(V(spec.Terrain.NoiseAmplitude), 25.0f) &&
                Near(V(spec.Terrain.NoiseScale), 900.0f),
                $"noise={spec.Terrain.NoiseAmplitude} scale={spec.Terrain.NoiseScale}");

            var features = spec.Terrain.Features;

            check("形状表逐项解析成特征（形状、中心、半径、幅度、边缘过渡）",
                features.Count == 2 &&
                features[0].Shape == PCGTerrainShape.Plateau &&
                Near(features[0].Center.X, 0.0f) && Near(features[0].Center.Y, 0.0f) &&
                Near(features[0].Radius, 600.0f) && Near(features[0].Height, 120.0f) &&
                Near(features[0].Falloff, 0.4f) &&
                features[1].Shape == PCGTerrainShape.Basin &&
                Near(features[1].Center.X, 900.0f) && Near(features[1].Center.Y, -400.0f) &&
                Near(features[1].Radius, 500.0f) && Near(features[1].Height, 80.0f) &&
                Near(features[1].Falloff, 0.0f),
                $"{features.Count} 项");

            // **洞掩码**：同一条形状语法、同一个解析器，但落到**独立**的一组特征上。
            // 这条同时是"值真的到了吗"的检查——只在描述层声明 `holes` 而没人写进配方，
            // 正是那种"解析照绿、字段永远空着"的静默失效。
            const string holedText =
                "world 开洞试跑\n" +
                "seed 7\n" +
                "terrain shapes \"dome 0 0 600 40\" holes \"dome 900 -400 250 0\"\n" +
                "scatter rocks points 3";

            bool holedOk = PCGSpecParser.TryParse(holedText, out var holed, out string holedError);
            check("带 holes 的描述能解析", holedOk, holedError);

            if (holedOk)
            {
                check("holes 落到独立的那一组特征上，高度那一组不受影响",
                    holed.Terrain.HoleFeatures.Count == 1 &&
                    Near(holed.Terrain.HoleFeatures[0].Center.X, 900.0f) &&
                    Near(holed.Terrain.HoleFeatures[0].Center.Y, -400.0f) &&
                    Near(holed.Terrain.HoleFeatures[0].Radius, 250.0f) &&
                    holed.Terrain.Features.Count == 1,
                    $"洞 {holed.Terrain.HoleFeatures.Count} 项 / 高度 {holed.Terrain.Features.Count} 项");

                check("不写 holes 的那份描述里那一组是空的（对照：不是总会冒出一个洞）",
                    spec.Terrain.HoleFeatures.Count == 0,
                    $"{spec.Terrain.HoleFeatures.Count} 项");
            }

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);
            var recipe = compiled.Ok ? compiled.Graph.Settings.Terrain : null;

            check("配方真的进了图设置（形状逐项带过去）",
                compiled.Ok && recipe != null &&
                recipe.Features != null && recipe.Features.Length == 2 &&
                recipe.Features[1].Shape == PCGTerrainShape.Basin,
                compiled.Ok ? "ok" : compiled.Error);

            // 存在文件里的形状编号必须与枚举顺序一致：那是"改了枚举顺序就会静默读出一片错地形"的唯一刹车。
            check("文件里的形状编号与枚举顺序一致（0 台地 / 1 盆地 / 2 土丘）",
                (int)PCGTerrainShape.Plateau == 0 && (int)PCGTerrainShape.Basin == 1 &&
                (int)PCGTerrainShape.Dome == 2);

            if (compiled.Ok)
            {
                string json = PCGGraphJson.ToJson(compiled.Graph);
                bool roundTripped = PCGGraphJson.TryParse(json, out var restored, out string parseError);
                var restoredRecipe = roundTripped ? restored.Settings.Terrain : null;

                check("地形配方能存进图文件再原样读回来",
                    roundTripped && restoredRecipe != null &&
                    Near(restoredRecipe.NoiseAmplitude, 25.0f) && Near(restoredRecipe.NoiseScale, 900.0f) &&
                    restoredRecipe.Features.Length == 2 &&
                    restoredRecipe.Features[0].Shape == PCGTerrainShape.Plateau &&
                    Near(restoredRecipe.Features[0].Radius, 600.0f) &&
                    Near(restoredRecipe.Features[0].Falloff, 0.4f) &&
                    Near(restoredRecipe.Features[1].Center.Y, -400.0f),
                    roundTripped ? "ok" : parseError);
            }

            // 没写 terrain 的描述必须保持"只判不造"：图设置里是 null，而不是一个空配方（决定 B）。
            PCGSpecParser.TryParse("world 无地形\nseed 1\nscatter rocks points 3", out var plain, out _);
            var plainCompiled = PCGSpecCompiler.Compile(plain, Resolver);

            check("没写 terrain 的图不带配方（写高度不做默认行为）",
                plainCompiled.Ok && plainCompiled.Graph.Settings.Terrain == null,
                plainCompiled.Ok
                    ? "Terrain=" + (plainCompiled.Graph.Settings.Terrain == null ? "null" : "非空")
                    : plainCompiled.Error);

            // 三条错误都要**带行号/带说明**：形状表是这一层唯一的字符串语法，
            // 静默吞掉的代价是"描述写了、地面什么都没变"。
            PCGSpecParser.TryParse("world 错形状\nterrain shapes \"hill 0 0 600 120\"", out _, out string badShape);
            check("不认识的形状带行号报错",
                badShape != null && badShape.Contains("第 2 行") && badShape.Contains("hill"), badShape);

            PCGSpecParser.TryParse("world 零半径\nterrain shapes \"dome 0 0 0 10\"", out _, out string zeroRadius);
            check("半径为零的形状被拒（它不会让地面发生任何变化）",
                zeroRadius != null && zeroRadius.Contains("半径"), zeroRadius);

            PCGSpecParser.TryParse("world 没有形状表\nterrain noise 10", out _, out string noShapes);
            check("写了 terrain 却没写形状表时带说明报错",
                noShapes != null && noShapes.Contains("形状表"), noShapes);

            PCGSpecParser.TryParse("world 段数不够\nterrain shapes \"dome 0 0 600\"", out _, out string tooFew);
            check("形状表段数不够时带段数说明报错",
                tooFew != null && tooFew.Contains("五段"), tooFew);
        }

        private static void ShippedSampleRunTests(CheckDelegate check)
        {
            var valleyProducers = new Dictionary<string, int>
            {
                { PCGBiomeNode.Id, 1 },
                { PCGHydrologyNode.Id, 1 },
                { PCGSplitGrammarNode.Id, 1 },
                { PCGCaveNode.Id, 1 },
                { PCGPathNode.Id, 1 },
            };

            var cityProducers = new Dictionary<string, int>
            {
                { PCGBiomeNode.Id, 1 },
                { PCGGrammarNode.Id, 1 },
                { PCGPathNode.Id, 1 },
                // 城外的林子是按**地貌**撒的（`biome plain,hill`），那种链的点源是 `Biome`、出口是那道按 Biome 列
                // 过滤的 `Attribute Filter`——它**没有 Volume Sampler**（第一次把它列进来，读到的是"0"，其实是没有
                // 这个节点）。名单要按链条的真实形状写，不然断言测的是我的想象。
                { PCGAttributeFilterNode.Id, 1 },
            };

            var townshipProducers = new Dictionary<string, int>
            {
                { PCGSplitGrammarNode.Id, 1 },
                { PCGGrammarNode.Id, 1 },
                { PCGPathNode.Id, 1 },
                { PCGVolumeSamplerNode.Id, 1 },
                { PCGAreaFilterNode.Id, 1 },
            };

            // 地形按每份描述自己的前提给：`valley` 里的水要的是**洼地**（平地上没有流向，河湖都出不来），
            // `city` 要的是**高于它自己水位的缓地**——它写着 `water 10`，所以放在高度 0 的平面上时**整片都在水下**，
            // 地貌判成 Water，那句 `biome plain,hill` 的林一棵不长（这不是缺陷，是描述与地形对上了）；
            // 放到丘陵上又会被它自己 `slope 15` 的路与 `mountain-slope 25` 的判据挡住。
            SampleRun(check, "valley", Bowl, valleyProducers);
            SampleRun(check, "city", (x, z) => 20.0f, cityProducers);
            SampleRun(check, "township", Hill, townshipProducers);

            // 演示场景里那四份描述也整份跑一遍。仓库里那三份示例有整份断言，而这四份是后加的产物：
            // "节点数与连线数对不对"看不出某条链被接丢（改编坐标时最容易发生），只有整份跑一遍才看得见。
            var caveProducers = new Dictionary<string, int>
            {
                { PCGCaveNode.Id, 1 },
                { PCGVolumeSamplerNode.Id, 1 },
            };

            // 地形按各自的前提给：演示描述是照着**平缓地面**写的（坡度筛选下界取 0、水位压到地面以下），
            // 所以给一条缓坡；山谷那份要出河湖，还是需要一点地形起伏。
            //
            // 演示用那四份的坐标是**世界坐标**（它们是摆在场景四个象限里的体积），所以箱子也要按各自
            // 体积的真实范围给：拿默认的原点 ±1500 去跑，跑出来的是"箱子外面什么都没生成"，
            // 而那既不说明描述写错了、也不说明链断了。
            // 演示那份的林地多了一条 `polygon` 子句（任意形状地块），所以它比仓库里那份示例多一个产出节点；
            // 字典是**产出点数下限**，两份描述各用各的清单——共用一份会让 `valley` 因为没有那块地而报红。
            var demoValleyProducers = new Dictionary<string, int>(valleyProducers)
            {
                { PCGPolygonFilterNode.Id, 1 },
                { PCGGeometryNode.Id, 1 },        // 石柱层：程序化几何
                { PCGSpawnGeometryNode.Id, 1 },   // 石柱层：写出来的是几何生成器，不是静态模型
                { PCGSpawnFoliageNode.Id, 1 },    // 草那层写了 output instanced ⇒ 走植被宿主
            };

            SampleRun(check, "demo_valley", GentleSlope, demoValleyProducers,
                Box(new Vector3(500, -600, 500), new Vector3(2500, 600, 2100)));
            SampleRun(check, "demo_city", (x, z) => 20.0f, cityProducers,
                Box(new Vector3(4100, -600, 100), new Vector3(5900, 600, 1900)));
            SampleRun(check, "demo_township", Hill, townshipProducers,
                Box(new Vector3(300, -800, 4000), new Vector3(3300, 800, 6400)));
            SampleRun(check, "demo_cave", GentleSlope, caveProducers,
                Box(new Vector3(4300, -800, 4300), new Vector3(5700, 700, 5700)));
        }

        /// <summary>缓坡：演示描述照着平缓地面写，用它跑就不会被自己的坡度筛选挡住。</summary>
        private static float? GentleSlope(float x, float z) => 20.0f + x * 0.02f;

        private static BoundingBox Box(Vector3 min, Vector3 max) => new BoundingBox(min, max);

        /// <summary>把一个示例整份跑一遍，然后核"没有孤儿节点"与"每条产出链都出东西"。</summary>
        /// <param name="bounds">
        /// 生成范围；缺省是原点 ±1500（仓库里那三份示例的坐标就在这个范围里）。演示用那几份的坐标
        /// 摆在场景的四个象限里，必须按各自体积的真实范围给，否则跑的是"箱子外面"。
        /// </param>
        private static void SampleRun(CheckDelegate check, string name, Func<float, float, float?> terrain,
            Dictionary<string, int> producers, BoundingBox? bounds = null)
        {
            var graph = CompileShipped(name);

            if (graph == null ||
                !PCGGraphCompiler.TryCompile(graph, out var program))
            {
                // 示例文件不在（内容缺失）时跳过并说明，与自检组对缺席文件的处理一致——不把布局问题报成功能缺陷。
                check($"示例 {name}.pcgspec 能编译（不在就跳过）", graph == null, "不在或编译不过");
                return;
            }

            var inspection = new PCGNodeInspection(1);
            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext { HeightSource = terrain },
                GenerationBounds = bounds ?? new BoundingBox(new Vector3(-1500, -400, -1500), new Vector3(1500, 600, 1500)),
                DryRun = true,
                Inspection = inspection,
            };

            var run = new PCGGraphExecutor(program).Execute(context);

            var ran = new Dictionary<int, int>();

            foreach (var record in inspection.Records)
                ran[record.NodeIndex] = record.PointCount;

            var orphans = new List<string>();

            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (!ran.ContainsKey(i))
                    orphans.Add($"{i}:{graph.Nodes[i].TypeId}");
            }

            check($"示例 {name}.pcgspec 的每个节点都跑到了（没有接不进任何汇的孤儿）",
                orphans.Count == 0,
                orphans.Count == 0 ? $"{graph.NodeCount} node(s)" : string.Join(", ", orphans));

            var report = new List<string>();

            foreach (var pair in producers)
            {
                int total = 0;

                for (int i = 0; i < graph.NodeCount; i++)
                {
                    if (graph.Nodes[i].TypeId == pair.Key && ran.TryGetValue(i, out int count))
                        total += count;
                }

                report.Add($"{pair.Key}={total}");
            }

            // 逐项判，但把全部实际值摆进理由：一条链是零时，一眼能看出是"整片没东西"还是"只有水没出来"。
            bool allProduced = true;

            foreach (var pair in producers)
            {
                int total = 0;

                for (int i = 0; i < graph.NodeCount; i++)
                {
                    if (graph.Nodes[i].TypeId == pair.Key && ran.TryGetValue(i, out int count))
                        total += count;
                }

                allProduced &= total >= pair.Value;
            }

            check($"示例 {name}.pcgspec 的每条产出链都真的出了东西",
                allProduced, string.Join(" ", report));

            check($"示例 {name}.pcgspec 跑起来没有「列读不到 / 参照层为空」这类静默失效",
                !Mentions(run.Warnings, "could not read") && !Mentions(run.Warnings, "no Reference layer"),
                string.Join(" | ", run.Warnings));

            // 每个**带密度抽签**的生成器都要有够格的期望产出。生成器的判据是 `point.Density >= rng.NextFloat()`，
            // 于是一个点的存活率就是它的密度、全层的期望件数就是密度之和：
            //   合计 0 ⇒ 这一层在引擎里**必然**摆出 0 件；合计 < 1 ⇒ 多半摆不出东西来。
            // 而离屏这一侧生成器在预览里把点数原样透传，"每条产出链都出了东西"那条照样绿——
            // 演示里"路肩灌木/林带"就在场景里一件不剩，所有门都亮着（本轮实测：密度合计 0.50）。
            var thin = new List<string>();
            var seenTypes = new List<string>();
            int examined = 0;

            if (run.Outputs != null && run.OutputNodeIndices != null)
            {
                for (int i = 0; i < run.Outputs.Length && i < run.OutputNodeIndices.Length; i++)
                {
                    int index = run.OutputNodeIndices[i];
                    var data = run.Outputs[i];

                    if (index < 0 || index >= graph.NodeCount || data == null || data.Count == 0)
                        continue;

                    string typeId = graph.Nodes[index].TypeId;

                    if (typeId == null || !typeId.StartsWith("Spawn", StringComparison.Ordinal))
                        continue;

                    // 把每条生成器的**到货点数**摆进读数：引擎里那个总体件数说不清是哪一层出的，
                    // 而"哪一层出了上千件"正是判断 output merged 该不该起作用的前提。
                    seenTypes.Add($"{index}:{typeId}={data.Count}点");

                    // 参数名按标签表里那份带空格的写法取；写成驼峰会读到 null，而 null 会被当成
                    // "这条不归我管"跳过去——整条检查于是变成什么都没看，却照样报绿（本轮实测踩过）。
                    float? threshold = (Slot(graph, index, "Density Threshold") as float?) ??
                                       (Slot(graph, index, "DensityThreshold") as float?);

                    // 没写 `density` 的层根本不做抽签（槽位缺席或阈值 0），密度列空着也照样摆——那不算薄。
                    if (!threshold.HasValue || threshold.Value <= 0.0f)
                        continue;

                    float sum = 0.0f;

                    for (int p = 0; p < data.Count; p++)
                        sum += data.Points[p].Density;

                    examined++;

                    if (sum < 1.0f)
                        thin.Add($"节点 {index}({typeId}) 收到 {data.Count} 个点、期望只产出 {sum:F2} 件");
                }
            }

            check($"示例 {name}.pcgspec 没有「期望产出不足一件」的那一层（带密度抽签的生成器不能收到全零或近乎零的密度）",
                examined == 0 || thin.Count == 0,
                examined == 0
                    ? $"跳过：这一份的 {seenTypes.Count} 条生成器都不做密度抽签（没写 density）"
                    : thin.Count == 0
                        ? $"每条生成器的到货点数：{string.Join(" | ", seenTypes)}"
                        : string.Join(" | ", thin));
        }

        /// <summary>按裸名编译仓库里那份示例，文件不在或编译不过时给 null。</summary>
        private static PCGGraph CompileShipped(string name)
        {
            string folder = FindSpecsFolder();

            if (folder == null)
                return null;

            string path = System.IO.Path.Combine(folder, name + ".pcgspec");

            if (!System.IO.File.Exists(path))
                return null;

            if (!PCGSpecParser.TryParse(System.IO.File.ReadAllText(path), out var spec, out _))
                return null;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            return compiled.Ok ? compiled.Graph : null;
        }

        /// <summary>解析失败时给 null，好让调用处把"没解析过"和"编译报错"分开说。</summary>
        private static PCGWorldSpec ParseSpecOrNull(string text) =>
            PCGSpecParser.TryParse(text, out var spec, out _) ? spec : null;

        /// <summary>
        /// 编译并跑一段描述，返回某个类型第 <paramref name="ordinal"/> 个节点的产出点。
        /// </summary>
        /// <remarks>
        /// 不能用 <c>PrimaryOutput</c>：它只是**最后一条链**的汇，多链图里拿它断言等于断言错了对象（本文件
        /// 已经为这个错误吃过一次亏）。这里按节点下标从逐节点检视里取，并且把采样上限调大——默认每个节点只留
        /// 8 个位置，8 个样本证不出"没有点落在 150 之外"。
        /// </remarks>
        private static List<Vector3> RunSpecNodePoints(string specText, string typeId, int ordinal,
            Func<float, float, float?> terrain) =>
            RunSpecNode(specText, typeId, ordinal, terrain).Points;

        /// <summary>
        /// 同 <see cref="RunSpecNodePoints"/>，但把执行器的告警一起交出来。
        /// </summary>
        /// <remarks>
        /// 告警要一起看：`Distance To Points` 没拿到参照层、`Attribute Filter` 读不到那一列，**两条都只会
        /// 记一条理由并把点原样透传**——于是"筛选什么都没筛掉"看起来就像筛选器在正常工作。不看告警就会把
        /// 一个静默失效的接线断言成通过。
        /// </remarks>
        private static (List<Vector3> Points, string[] Warnings) RunSpecNode(string specText, string typeId,
            int ordinal, Func<float, float, float?> terrain)
        {
            if (!PCGSpecParser.TryParse(specText, out var spec, out _))
                return (null, Array.Empty<string>());

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok || !PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
                return (null, Array.Empty<string>());

            int target = IndexOf(compiled.Graph, typeId, ordinal);

            if (target < 0)
                return (null, Array.Empty<string>());

            var world = new PCGWorldContext();

            if (terrain != null)
                world.HeightSource = terrain;

            var inspection = new PCGNodeInspection(20000);
            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = world,
                GenerationBounds = new BoundingBox(new Vector3(-1500, -400, -1500), new Vector3(1500, 600, 1500)),
                DryRun = true,
                Inspection = inspection,
            };

            var run = new PCGGraphExecutor(program).Execute(context);

            foreach (var record in inspection.Records)
            {
                if (record.NodeIndex == target)
                    return (new List<Vector3>(record.Samples), run.Warnings);
            }

            return (null, run.Warnings);
        }

        private static void FeatureRunTests(CheckDelegate check)
        {
            // 地貌底图：一座高斯山。底图要铺出来，而且必须**不止一档**——山顶与远坡该判成不同的地貌，
            // 否则"分类器把整片都判成同一档"也会让这条断言通过。
            var biome = RunSpec(
                "world 地貌\nbiome cell 100 water 10 relief 120 region 600 hill-slope 6 mountain-slope 20 samples 4",
                Hill);
            var biomes = biome?.SharedAttributes?.TryGetFloatArray(PCGBiomeNode.BiomeColumn);

            check("地貌底图跑得出点，且各档不止一档",
                biome != null && biome.Count > 0 && biomes != null &&
                Distinct(biomes, biome.Count) > 1,
                biome == null ? "null" : $"{biome.Count} point(s), {Distinct(biomes, biome.Count)} biome(s)");

            // 水：一个碗。碗底要积水，所以湖面列里得有正数——只断言"有点"会让"把整片都当成湖"通过。
            var water = RunSpec("world 水域\nwater cell 40 threshold 40 min-lake-cells 4", Bowl);
            var areas = water?.SharedAttributes?.TryGetFloatArray(PCGHydrologyNode.LakeAreaColumn);

            check("水链跑得出湖，且湖面列里有正数",
                water != null && water.Count > 0 && areas != null && AnyPositive(areas, water.Count),
                water == null ? "null" : $"{water.Count} point(s)");

            // 聚居地：不接输入时按生成范围切一次。规则表**按深度**取用，所以两条规则配 max-depth 2
            // 就是"第 0 层切 X、第 1 层切 Z"⇒ 恰好四块。只写一条规则只会切一刀（两块），
            // 那正是我第一版夹具写错的地方。
            var village = RunSpec(
                "world 聚居地\nvillage rules \"X:0.5,0.5; Z:0.5,0.5\" max-depth 2 cell 300 40 300", null);
            var depths = village?.SharedAttributes?.TryGetFloatArray(PCGSplitGrammarNode.DepthColumn);

            check("聚居地链按两条规则切出四块，并写出 Depth 列",
                village != null && village.Count == 4 && depths != null && depths.Length == 4,
                village == null ? "null" : $"{village.Count} leaf/leaves");

            // 洞穴：分叉概率默认只有 0.15，所以"三条隧道"是**概率**而不是承诺——夹具里把概率拉满，
            // 于是这个数字是确定的；而不管几条，隧道号都必须从 0 起连续（这是数据自身的不变量，
            // 不是我从参数推出来的数）。
            var cave = RunSpec("world 洞穴\ncave depth 200 segments 6 radius 40 tunnels 3 sides 8 branch 1", null);
            var tunnels = cave?.SharedAttributes?.TryGetFloatArray(PCGCaveNode.TunnelColumn);

            check("洞穴链跑得出点，且隧道号从 0 起连续、覆盖到条数减一",
                cave != null && cave.Count > 0 && tunnels != null &&
                Math.Abs(Max(tunnels, cave.Count) - 2.0f) < 1e-3f &&
                Distinct(tunnels, cave.Count) == 3,
                cave == null ? "null" : $"{cave.Count} point(s), 最高隧道号 {Max(tunnels, cave.Count):F0}");

            // 入口必须落在**地表**上，而不是包围盒的几何中心。这两者在平地上重合（都贴着 0），
            // 所以旧写法看不出问题；地形一旦有起伏就是"洞挖在山里、却没有洞口"。
            // 这里的判据是"有一个点就压在地表高度上"：几何中心的写法离 60 有整整 60 的差，一定会被抓到。
            var raisedCave = RunSpec(
                "world 洞穴入口\ncave depth 200 segments 6 radius 40 tunnels 2 sides 8 branch 1",
                (x, z) => 60.0f);

            float closestToSurface = float.MaxValue;

            if (raisedCave != null)
            {
                var raisedPoints = raisedCave.Points;

                for (int i = 0; i < raisedPoints.Length; i++)
                    closestToSurface = Math.Min(closestToSurface, Math.Abs(raisedPoints[i].Position.Y - 60.0f));
            }

            check("洞穴入口取的是地表高度，而不是包围盒中心的 Y",
                raisedCave != null && raisedCave.Count > 0 && closestToSurface <= 1.0f,
                raisedCave == null ? "null" : $"{raisedCave.Count} point(s)，离地表最近 {closestToSurface:F2}");

            // 城市：与聚居地同一条骨架，中间换成**符号产生式**，于是地块之间有了层级之别。
            // 两条产生式配 max-depth 2 ⇒ 城区 → 两个街区 → 四个房子，恰好四块。
            var city = RunSpec(
                "world 城市\ncity productions \"District = Block,Block: X:0.5,0.5; Block = House,House: Z:0.5,0.5\" " +
                "root District max-depth 2 cell 800 40 800",
                null);
            var symbols = city?.SharedAttributes?.TryGetFloatArray(PCGGrammarNode.SymbolColumn);
            var names = city?.SharedAttributes != null && city.SharedAttributes.Contains(PCGGrammarNode.SymbolNameColumn);

            check("城市链按产生式逐层改写，切出预期的四块并写出符号列",
                city != null && city.Count == 4 && symbols != null && symbols.Length == 4 && names,
                city == null ? "null" : $"{city.Count} leaf/leaves");

            // 随仓库一起交出去的那三份示例，也要**真跑**一遍才算数：编译得过不代表它长出东西。
            // 城市的层级是 城区→2 街区→每个 3 房子→每栋 2 房间，深度 3 ⇒ 12 块叶子。
            int shippedLeaves = ShippedSpecNodePoints("city", PCGGrammarNode.Id, out string note);

            check("仓库里的 city.pcgspec 的符号语法真跑出 12 块叶子",
                shippedLeaves == 12, note ?? $"{shippedLeaves} leaf/leaves");

            // 随仓库交出去的第三份示例专门演示"同一个特征写多行"：两村两城，各自的规则与层级都不同。
            // 每个类型必须各有**两条**产出记录，而且每条都真出点——只看第一处的话，第二行写丢了也是绿的。
            // 两村的规则表长短不同（切两层 vs 切一层），所以两个数字必须**不相等**：相等就说明第二行拿的是
            // 第一行的规则表。（注意规则表本身也封顶深度：一条规则的表切不到第二层。）
            var twoVillages = ShippedSpecTypeCounts("township", PCGSplitGrammarNode.Id, out string villageNote);

            check("示例 township.pcgspec 里两行 village 各出一条链、各用自己的规则表",
                twoVillages != null && twoVillages.Count == 2 &&
                twoVillages[0] > 0 && twoVillages[1] > 0 && twoVillages[0] != twoVillages[1],
                villageNote ?? (twoVillages == null ? "null" : string.Join("/", twoVillages)));

            var twoCities = ShippedSpecTypeCounts("township", PCGGrammarNode.Id, out string townshipNote);

            check("示例 township.pcgspec 里两行 city 各出一条链、各用自己的产生式",
                twoCities != null && twoCities.Count == 2 &&
                twoCities[0] > 0 && twoCities[1] > 0 && twoCities[0] != twoCities[1],
                townshipNote ?? (twoCities == null ? "null" : string.Join("/", twoCities)));
        }

        /// <summary>按裸名加载 `Content/PCG/Specs` 里随仓库交出去的那份描述并跑一遍。</summary>
        /// <remarks>
        /// <para>
        /// 不能走 <see cref="PCGSpecFile.ResolvePath"/>：那条路依赖引擎的 `ProjectContentFolder`，而离屏环境里
        /// 没有工程。这里从程序集所在目录往上找"仓库根"，找到就该找得到——找不到说明工作区布局变了，
        /// 那就跳过并说明（与引擎自检组对示例文件缺席的处理一致），而不是把一个布局问题报成功能缺陷。
        /// </para>
        /// <para>
        /// 看的是**逐节点检视**里某个类型的产出，而不是 <c>PrimaryOutput</c>：示例里同时有城市、道路与林地三条链，
        /// 主输出只是其中最后一条，拿它断言城市等于断言错了对象。
        /// </para>
        /// </remarks>
        private static int ShippedSpecNodePoints(string name, string typeId, out string note)
        {
            var records = RunShippedSpec(name, out note);

            if (records == null)
                return -1;

            foreach (var record in records)
                if (record.TypeId == typeId)
                    return record.PointCount;

            note = $"示例 {name}.pcgspec 跑完之后没有 {typeId} 节点的产出记录";
            return -1;
        }

        /// <summary>
        /// <paramref name="typeId"/> 的每个节点各产出多少点，按图里的先后顺序——一个类型出现多次时要看全部。
        /// </summary>
        /// <remarks>
        /// 只看第一处会把"可重复"这件事证成"只有一处"：第二行 `village` 完全可能产出零个点而这条断言照样绿。
        /// 返回的是**每一个**节点的点数，缺口是"少了一条记录"，不是"第一条是零"。
        /// </remarks>
        private static List<int> ShippedSpecTypeCounts(string name, string typeId, out string note)
        {
            var records = RunShippedSpec(name, out note);

            if (records == null)
                return null;

            var counts = new List<int>();

            foreach (var record in records)
                if (record.TypeId == typeId)
                    counts.Add(record.PointCount);

            if (counts.Count == 0)
                note = $"示例 {name}.pcgspec 跑完之后没有 {typeId} 节点的产出记录";

            return counts;
        }

        /// <summary>按裸名加载 `Content/PCG/Specs` 里随仓库交出去的那份描述并跑一遍，返回逐节点检视记录。</summary>
        /// <remarks>
        /// <para>
        /// 不能走 <see cref="PCGSpecFile.ResolvePath"/>：那条路依赖引擎的 `ProjectContentFolder`，而离屏环境里
        /// 没有工程。这里从程序集所在目录往上找"仓库根"，找到就该找得到——找不到说明工作区布局变了，
        /// 那就跳过并说明（与引擎自检组对示例文件缺席的处理一致），而不是把一个布局问题报成功能缺陷。
        /// </para>
        /// <para>
        /// 看的是**逐节点检视**里某个类型的产出，而不是 <c>PrimaryOutput</c>：示例里同时有城市、道路与林地三条链，
        /// 主输出只是其中最后一条，拿它断言城市等于断言错了对象。
        /// </para>
        /// </remarks>
        private static List<PCGNodeInspection.NodeRecord> RunShippedSpec(string name, out string note)
        {
            note = null;
            string folder = FindSpecsFolder();

            if (folder == null)
            {
                note = "找不到仓库里的 Content/PCG/Specs，跳过";
                return null;
            }

            string path = System.IO.Path.Combine(folder, name + ".pcgspec");

            if (!System.IO.File.Exists(path))
            {
                note = $"示例 {name}.pcgspec 不在";
                return null;
            }

            if (!PCGSpecParser.TryParse(System.IO.File.ReadAllText(path), out var spec, out string parseError))
            {
                note = $"示例 {name}.pcgspec 没解析过：{parseError}";
                return null;
            }

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok || !PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                note = $"示例 {name}.pcgspec 没编译过：{compiled.Error ?? "图编译器拒绝"}" ;
                return null;
            }

            var inspection = new PCGNodeInspection();
            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext(),
                GenerationBounds = new BoundingBox(new Vector3(-1500, -400, -1500), new Vector3(1500, 600, 1500)),
                DryRun = true,
                Inspection = inspection,
            };

            new PCGGraphExecutor(program).Execute(context);

            var records = new List<PCGNodeInspection.NodeRecord>(inspection.Records);

            return records;
        }

        /// <summary>从程序集所在目录往上找"仓库里那份 Specs 目录"。</summary>
        private static string FindSpecsFolder()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "HundunWorld", "Content", "PCG", "Specs");

                if (System.IO.Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>编译一张描述、跑一遍（预览模式），把主输出交回来。</summary>
        /// <summary>
        /// 描述层里的**任意形状地块**（`polygon "…"`）：与矩形同一条职责（回答"长在哪"），但形状自由。
        /// </summary>
        /// <remarks>
        /// 断言分三件：顶点串原样落到字段上、与 within/outside 互斥时报出带行号的错、以及**真跑起来点只落在地块里**。
        /// 最后那条的判据刻意与实现无关：凹口（X&gt;400 且 Z&gt;400 那一片）里一个点都不许有——
        /// 这是对 L 形几何本身的陈述，不是拿 <c>PCGPolygon</c> 再算一遍去自证自己。
        /// 对照用同一份描述把 polygon 换成 within（同一个种子、同一批点），矩形必须**多收**，
        /// 且多收的那些正好落在凹口里。
        /// </remarks>
        private static void PolygonRegionTests(CheckDelegate check)
        {
            // 放大到 0..1000，让地块在 ±1500 的生成范围里占到可观的份额（点太少会让"凹口为空"变成偶然）。
            const string L = "0 0; 1000 0; 1000 400; 400 400; 400 1000; 0 1000";

            bool parsed = PCGSpecParser.TryParse(
                $"world 地块\nscatter trees per-block 16 polygon \"{L}\"", out var spec, out string error);

            check("polygon 的顶点串原样落到字段上（引号由分词器剥掉）",
                parsed && spec.Scatters[0].PolygonText == L,
                parsed ? spec.Scatters[0].PolygonText : error);

            Error(check, "第 2 行",
                $"world x\nscatter trees per-block 16 polygon \"{L}\" within 0 0 1000 1000",
                "同时写了 polygon 与 within");

            if (!parsed)
                return;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok)
            {
                check("带地块的描述能编译", false, compiled.Error);
                return;
            }

            var kinds = new List<string>();

            foreach (var node in compiled.Graph.Nodes)
                kinds.Add(node.TypeId);

            check("地块落成一个 Polygon Filter，与点源/生成器各一个",
                Count(kinds, PCGPolygonFilterNode.Id) == 1 &&
                Count(kinds, PCGVolumeSamplerNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 1,
                string.Join(", ", kinds));

            int polygon = IndexOf(compiled.Graph, PCGPolygonFilterNode.Id, 0);

            check("顶点串落进多边形过滤器的 Vertices 槽位（按名字，不按下标）",
                Slot(compiled.Graph, polygon, "Vertices") as string == L,
                Slot(compiled.Graph, polygon, "Vertices")?.ToString());

            // 真跑（DryRun 下生成器只透传，输出就是筛过的点）。
            var inside = RunSpec($"world 地块\nscatter g per-block 16 polygon \"{L}\"", null);

            check("真跑：地块里确实长出了点（正面同夹具，挡住「空层也算过」）",
                inside != null && inside.Count > 0, inside == null ? "null" : $"{inside.Count} point(s)");

            if (inside == null || inside.Count == 0)
                return;

            int outsideBox = 0;
            int inNotch = 0;

            foreach (var point in inside.Points)
            {
                float x = point.Position.X;
                float z = point.Position.Z;

                if (x < 0.0f || x > 1000.0f || z < 0.0f || z > 1000.0f)
                    outsideBox++;

                if (x > 400.0f && z > 400.0f)
                    inNotch++;
            }

            check("真跑：没有一个点跑出地块的包围盒",
                outsideBox == 0, $"{outsideBox} 个出界");

            check("真跑：凹口里一个点都没有（矩形做不到这件事）",
                inNotch == 0, $"{inNotch} 个落进了凹口");

            // 对照：同一批点、同一个种子，只把地块换成它的包围盒（within）。矩形必须多收，且多收的落在凹口里。
            var boxed = RunSpec("world 地块\nscatter g per-block 16 within 0 0 1000 1000", null);

            int boxedNotch = 0;

            if (boxed != null)
            {
                foreach (var point in boxed.Points)
                {
                    if (point.Position.X > 400.0f && point.Position.Z > 400.0f)
                        boxedNotch++;
                }
            }

            check("对照：换成 within 的包围盒之后，凹口里就有点了（形状确实起了作用）",
                boxed != null && boxed.Count > inside.Count && boxedNotch > 0,
                boxed == null ? "null" : $"矩形 {boxed.Count} vs 地块 {inside.Count}，凹口 {boxedNotch}");
        }

        /// <summary>
        /// 演示描述里那块**任意形状地块**真的起了作用：林地那条链的点只落在 L 里，删掉 `polygon` 之后
        /// 同一个凹口里就有点了。
        /// </summary>
        /// <remarks>
        /// 为什么要专门验演示文件：几何性质在 <see cref="PolygonRegionTests"/> 里已经用夹具钉住了，但"演示里
        /// 那块空地"是另一回事——如果采样恰好几乎不往凹口放点，屏幕上就看不出差别，而"看不出差别"和
        /// "功能没生效"长得一样。所以这条断言两件事一起报：地块里还剩几个点、以及删掉子句之后凹口里多出几个。
        /// 认"林地那条链"靠它独有的两个槽位（`Scale Multiplier`=1.2 与 `Random Yaw`=true），不靠节点下标——
        /// 删掉子句之后下标会整体挪位。
        /// </remarks>
        private static void DemoPolygonEffectTests(CheckDelegate check)
        {
            string folder = FindSpecsFolder();
            string path = folder == null ? null : System.IO.Path.Combine(folder, "demo_valley.pcgspec");

            if (path == null || !System.IO.File.Exists(path))
            {
                check("演示描述的林地地块断言（文件不在就跳过）", true, "demo_valley.pcgspec 不在");
                return;
            }

            string text = System.IO.File.ReadAllText(path);
            int clause = text.IndexOf(" polygon \"", StringComparison.Ordinal);

            if (clause < 0)
            {
                check("演示描述的林地写了 polygon 子句", false, "没找到 polygon 子句");
                return;
            }

            int quote = text.IndexOf('"', clause + 10);

            if (quote < 0)
            {
                check("演示描述的 polygon 子句引号闭合", false, "引号没闭合");
                return;
            }

            // 对照：同一份描述，只把 `polygon "…"` 整段剪掉。
            string bare = text.Remove(clause, quote - clause + 1);

            int keptPoints = ForestPointsInNotch(text, out int keptTotal, out string keptError);

            check("带 polygon 的演示林地链能跑出点", keptTotal > 0 && keptError == null,
                keptError ?? $"{keptTotal} 个点");

            check("带 polygon：林地一个点都没落进那块空地（这就是场景里那块光地）",
                keptPoints == 0, $"{keptPoints} 个落在凹口");

            int barePoints = ForestPointsInNotch(bare, out int bareTotal, out string bareError);

            check("对照：删掉 polygon 之后，同一个凹口里有树了（证明空地是它挡出来的）",
                bareError == null && bareTotal > 0 && barePoints > 0,
                bareError ?? $"带子句 {keptTotal} 点 / 凹口 {keptPoints}；删掉 {bareTotal} 点 / 凹口 {barePoints}");

            check("对照：删掉 polygon 之后林地总数更多（凹口那些点回来了）",
                bareError == null && bareTotal > keptTotal,
                $"带 {keptTotal} vs 不带 {bareTotal}");

            // 演示里那句 `cut` 也要真的落到**网格**上：切过的石柱最高点必须被刀口压住。
            // 判据与实现无关——圆柱本来就跨到 y=+5，刀口（法线 0.35 1 0、沿法线推 2.6）之下不该再有点。
            float pillarTop = DemoPillarTop(text, out string pillarError);

            check("演示的石柱被**削掉了顶**（最高点被刀口压住，不是原样那根 12 高的柱子）",
                pillarError == null && pillarTop > 0.0f && pillarTop < 4.5f,
                pillarError ?? $"最高点 {pillarTop:F2}（未切时是 5.0）");
        }

        /// <summary>把演示描述跑一遍，返回**石柱那条链**网格里最高的那个顶点的高度。</summary>
        /// <remarks>
        /// 认那条链靠**结构**：从 `Cut` 节点顺边一直走到没有出边的那个汇（只有石柱那条链有 `cut`）。
        /// 第一版按"顶点数最多"去挑，挑中的是洞穴的管子（它的网格顶点多、还伸到 y≈65），
        /// 于是断言在测一个跟刀口无关的东西。
        /// </remarks>
        private static float DemoPillarTop(string specText, out string error)
        {
            error = null;

            if (!PCGSpecParser.TryParse(specText, out var spec, out string parseError))
            {
                error = "解析不过：" + parseError;
                return 0.0f;
            }

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok || !PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                error = "编译不过：" + (compiled.Error ?? "unknown");
                return 0.0f;
            }

            int cut = IndexOf(compiled.Graph, PCGCutNode.Id, 0);

            if (cut < 0)
            {
                error = "演示描述里没有 cut 节点";
                return 0.0f;
            }

            // 顺边走到汇：每轮取一条出边的下游，直到当前节点没有出边为止。
            int sink = cut;

            for (int guard = 0; guard < compiled.Graph.NodeCount; guard++)
            {
                int next = -1;

                foreach (var edge in compiled.Graph.Edges)
                {
                    if (edge.OutputNodeIndex == sink)
                    {
                        next = edge.InputNodeIndex;
                        break;
                    }
                }

                if (next < 0)
                    break;

                sink = next;
            }

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext { HeightSource = GentleSlope },
                GenerationBounds = Box(new Vector3(500, -600, 500), new Vector3(2500, 600, 2100)),
                DryRun = true,
            };

            var run = new PCGGraphExecutor(program).Execute(context);

            if (run.Outputs == null || run.OutputNodeIndices == null)
            {
                error = "没有汇出产出";
                return 0.0f;
            }

            for (int i = 0; i < run.OutputNodeIndices.Length; i++)
            {
                if (run.OutputNodeIndices[i] != sink)
                    continue;

                var data = run.Outputs[i];

                if (data?.Meshes == null || data.Meshes.Length == 0)
                {
                    error = "石柱那条链没有网格产出";
                    return 0.0f;
                }

                float top = float.MinValue;

                for (int m = 0; m < data.Meshes.Length; m++)
                {
                    var mesh = data.Meshes[m];

                    if (mesh == null)
                        continue;

                    for (int v = 0; v < mesh.VertexCount; v++)
                        top = Math.Max(top, mesh.Vertices[v].Y);
                }

                return top;
            }

            error = $"没在汇出列表里找到石柱那条链（tail={sink}）";
            return 0.0f;
        }

        /// <summary>
        /// 跑一遍描述，返回**林地那条链**落在凹口（x&gt;1300 且 z&gt;1200）里的点数，并给出该链的总点数。
        /// </summary>
        /// <remarks>
        /// 林地那条链用两个独有槽位认：`Scale Multiplier` = 1.2 且 `Random Yaw` = true（其余各层都没有这一对，
        /// 而这条链的下标会随子句增删而挪，按下标认会认错）。取不到槽位的汇出节点直接跳过。
        /// </remarks>
        private static int ForestPointsInNotch(string specText, out int total, out string error)
        {
            total = 0;
            error = null;

            if (!PCGSpecParser.TryParse(specText, out var spec, out string parseError))
            {
                error = "解析不过：" + parseError;
                return 0;
            }

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok || !PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                error = "编译不过：" + (compiled.Error ?? "unknown");
                return 0;
            }

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext { HeightSource = GentleSlope },
                GenerationBounds = Box(new Vector3(500, -600, 500), new Vector3(2500, 600, 2100)),
                DryRun = true,
            };

            var run = new PCGGraphExecutor(program).Execute(context);

            if (run.Outputs == null || run.OutputNodeIndices == null)
            {
                error = "没有汇出产出";
                return 0;
            }

            for (int i = 0; i < run.Outputs.Length; i++)
            {
                int index = run.OutputNodeIndices[i];
                var data = run.Outputs[i];

                if (data == null || index < 0 || index >= compiled.Graph.NodeCount)
                    continue;

                if (!IsForestSink(compiled.Graph, index))
                    continue;

                total = data.Count;

                int inNotch = 0;

                for (int p = 0; p < data.Count; p++)
                {
                    var position = data.Points[p].Position;

                    if (position.X > 1300.0f && position.Z > 1200.0f)
                        inNotch++;
                }

                return inNotch;
            }

            error = "没找到林地那条链的产出";
            return 0;
        }

        /// <summary>这个汇出节点是不是林地那条链（`Scale Multiplier` = 1.2 且 `Random Yaw` = true）。</summary>
        private static bool IsForestSink(PCGGraph graph, int index)
        {
            // 这条链的身份是"接在那个任意形状地块（`polygon` 子句）之后"，不是某几个附带参数。
            // 原来这里靠 `Random Yaw == true` + `Scale Multiplier == 1.2` 认林地——林地换成
            // `geometry tree` 之后那两个槽位根本不存在了，于是三条断言一起报"没找到林地那条链"。
            // 按参数认链本来就不该成立：改密度、改朝向、改输出形态都能把它弄丢，而那条 polygon 子句才是它。
            // 这条链的身份写在编译器给每个节点打的层名注释里（"散布层 forest：…"），
            // 于是它既不需要那个 `polygon` 节点（对照组里恰恰没有它），也不依赖任何附带参数。
            // 第一版按 `Random Yaw` + `Scale Multiplier` 认链：林地换成 `geometry tree` 之后那两个槽位消失，
            // 三条断言一起报"没找到链"；第二版按"有没有 PolygonFilter"认，对照组一删子句就又找不到——
            // **认一条链要认它是谁，不是认它此刻挂了哪些开关。**
            var reached = new bool[graph.NodeCount];

            for (int i = 0; i < graph.NodeCount; i++)
                if (graph.Nodes[i].Comment != null && graph.Nodes[i].Comment.Contains("散布层 forest"))
                    reached[i] = true;

            bool any = false;

            for (int i = 0; i < graph.NodeCount; i++)
                any |= reached[i];

            if (!any)
                return false;

            // 从这些节点往后走一遍：图按拓扑序存，所以前向松弛到不动点即可。
            for (int pass = 0; pass < graph.NodeCount; pass++)
            {
                bool moved = false;

                foreach (var edge in graph.Edges)
                {
                    if (edge == null || !reached[edge.OutputNodeIndex] || reached[edge.InputNodeIndex])
                        continue;

                    reached[edge.InputNodeIndex] = true;
                    moved = true;
                }

                if (!moved)
                    break;
            }

            return reached[index];
        }

        /// <summary>
        /// 描述层里的**程序化几何**（`geometry &lt;形状&gt; geometry-size …`）：不挂资产也能"长"出东西。
        /// </summary>
        /// <remarks>
        /// 这一格补的是"描述只能挂资产"：几何节点早就能建程序化网格，但一句描述够不着它。断言里有一条**防漂钉子**
        /// ——形状名表与 `PCGGeometryNode.Shape*` 的下标逐个对齐；两者一旦漂了，"说柱子长出盒子"会静默发生。
        /// </remarks>
        private static void GeometryLayerTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 石柱\nscatter pillars points 40 geometry cylinder geometry-size 3 12 3 geometry-segments 8",
                out var spec, out string error);

            check("geometry 的四个选项各自落到字段上",
                parsed && spec.Scatters[0].GeometryShape == "cylinder" &&
                spec.Scatters[0].GeometrySize.HasValue &&
                Near(spec.Scatters[0].GeometrySize.Value.X, 3.0f) &&
                Near(spec.Scatters[0].GeometrySize.Value.Y, 12.0f) &&
                spec.Scatters[0].GeometrySegments == 8,
                parsed ? $"{spec.Scatters[0].GeometryShape} {spec.Scatters[0].GeometrySize}" : error);

            if (!parsed)
                return;

            // 防漂：名字表只有一份，节点的下标只在这里翻译一次。漂了就是"说柱子长出盒子"。
            check("形状名与节点的下标逐个对齐（防漂钉子）",
                PCGSpecParser.GeometryShapeIndex("box") == PCGGeometryNode.ShapeBox &&
                PCGSpecParser.GeometryShapeIndex("grid") == PCGGeometryNode.ShapeGrid &&
                PCGSpecParser.GeometryShapeIndex("cylinder") == PCGGeometryNode.ShapeCylinder &&
                PCGSpecParser.GeometryShapeIndex("stairs") == PCGGeometryNode.ShapeStairs &&
                PCGSpecParser.GeometryShapeIndex("wedge") == PCGGeometryNode.ShapeWedge &&
                PCGSpecParser.GeometryShapeIndex("house") == PCGGeometryNode.ShapeHouse &&
                PCGSpecParser.GeometryShapeIndex("tree") == PCGGeometryNode.ShapeTree &&
                PCGSpecParser.GeometryShapes.Length == PCGGeometryNode.ShapeCount,
                string.Join("/", PCGSpecParser.GeometryShapes));

            // 新加的形状不能只活在那张表里：描述写 `geometry wedge` 之后，**图里那个几何节点真的收到 4**
            // 才算这条路通了（"选项声明了却没人把它写进槽位"是本仓反复踩过的那一族）。
            var wedgeRun = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 屋\nscatter roofs points 3 geometry wedge geometry-size 8 6 10"), Resolver);
            object wedgeSlot = null;
            int wedgeSlotLength = -1;

            if (wedgeRun.Ok)
                foreach (var node in wedgeRun.Graph.Nodes)
                    if (node.TypeId == PCGGeometryNode.Id && node.Values != null && node.Values.Length > 0)
                    {
                        wedgeSlot = node.Values[0];
                        wedgeSlotLength = node.Values.Length;
                        break;
                    }

            var houseRun = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 庄\nscatter homes points 6 geometry house geometry-size 12 9 10"), Resolver);
            object houseSlot = null;

            if (houseRun.Ok)
                foreach (var node in houseRun.Graph.Nodes)
                    if (node.TypeId == PCGGeometryNode.Id && node.Values != null && node.Values.Length > 0)
                    {
                        houseSlot = node.Values[0];
                        break;
                    }

            check("`geometry house` 编译后几何节点的 Shape 槽位收到的是房屋那一档",
                houseRun.Ok && houseSlot is int houseShape && houseShape == PCGGeometryNode.ShapeHouse,
                houseRun.Ok ? houseSlot?.ToString() : houseRun.Error);

            // 房屋那一条还要**真跑一遍**：几何节点在点源之后要真的产出网格（只断言槽位是数字，
            // 挡不住"这一层一个网格都没建出来"）。判据用性质而不是 `> 0`：网格件数 == 点数、
            // 每件的顶点数都是 30（同一形状重复），且包围盒随 `geometry-size` 走。
            var houseGraph = houseRun.Ok ? houseRun.Graph : null;
            int houseMeshes = 0;
            int houseVerts = 0;
            float houseY = 0.0f;

            if (houseGraph != null)
            {
                var program = PCG.Execution.PCGGraphCompiler.TryCompile(houseGraph, out var plan) ? plan : null;

                if (program != null)
                {
                    var runContext = new PCG.Execution.PCGExecutionContext
                    {
                        Seed = 4242,
                        GenerationBounds = new BoundingBox(new Vector3(-500, -100, -500), new Vector3(500, 100, 500)),
                    };

                    var output = new PCG.Execution.PCGGraphExecutor(program)
                        .Execute(runContext.AsDryRun()).PrimaryOutput;

                    if (output?.Meshes != null)
                    {
                        houseMeshes = output.Meshes.Length;

                        if (houseMeshes > 0)
                        {
                            houseVerts = output.Meshes[0].VertexCount;
                            houseY = PCGMeshBuilder.Bounds(output.Meshes[0]).Size.Y;
                        }
                    }
                }
            }

            check("真跑：`geometry house` 每点建出一栋房（件数 == 点数、每件 30 个顶点、总高跟着尺寸走）",
                houseMeshes == 6 && houseVerts == 30 && Near(houseY, 9.0f, 1e-3f),
                $"{houseMeshes} 件 / {houseVerts} 顶点 / 高 {houseY:F2}");

            // `geometry tree` 也要走到"槽位真的收到那一档"这一步，并且**真跑出网格**：
            // 森林那一条链此前只能写 `model "Cone"`（一个浮空的锥），现在一句 `geometry tree` 就是一棵有干的树。
            var treeRun = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 林\nscatter wood points 12 geometry tree geometry-size 6 14 6 geometry-segments 12"),
                Resolver);
            object treeSlot = null;
            int treeMeshes = 0;
            int treeVerts = 0;

            if (treeRun.Ok)
            {
                foreach (var node in treeRun.Graph.Nodes)
                    if (node.TypeId == PCGGeometryNode.Id && node.Values != null && node.Values.Length > 0)
                    {
                        treeSlot = node.Values[0];
                        break;
                    }

                var treeProgram = PCG.Execution.PCGGraphCompiler.TryCompile(treeRun.Graph, out var treePlan)
                    ? treePlan : null;

                if (treeProgram != null)
                {
                    var treeOutput = new PCG.Execution.PCGGraphExecutor(treeProgram).Execute(
                        new PCG.Execution.PCGExecutionContext
                        {
                            Seed = 99,
                            GenerationBounds = new BoundingBox(
                                new Vector3(-400, -50, -400), new Vector3(400, 50, 400)),
                        }.AsDryRun()).PrimaryOutput;

                    if (treeOutput?.Meshes != null)
                    {
                        treeMeshes = treeOutput.Meshes.Length;
                        treeVerts = treeMeshes > 0 ? treeOutput.Meshes[0].VertexCount : 0;
                    }
                }
            }

            check("`geometry tree` 编译后槽位收到树那一档，真跑每点一棵（12 点 ⇒ 12 件、每件 10n-6 顶点）",
                treeRun.Ok && treeSlot is int treeShape && treeShape == PCGGeometryNode.ShapeTree &&
                treeMeshes == 12 && treeVerts == 10 * 12 - 6,
                treeRun.Ok ? $"{treeSlot} / {treeMeshes} 件 / {treeVerts} 顶点" : treeRun.Error);

            check("`geometry wedge` 编译后几何节点的 Shape 槽位收到的是楔形那一档（不是默认的盒子）",
                wedgeRun.Ok && wedgeSlot is int shapeValue && shapeValue == PCGGeometryNode.ShapeWedge,
                wedgeRun.Ok
                    ? $"{wedgeSlot}（{wedgeSlotLength} 个槽位）"
                    : wedgeRun.Error);

            // 三类错都要带行号：形状名不认识、写了几何没给尺寸、几何与模型同时写。
            Error(check, "第 2 行",
                "world x\nscatter p points 8 geometry ball geometry-size 1 1 1", "不认识的几何形状");
            Error(check, "第 2 行", "world x\nscatter p points 8 geometry box", "没给尺寸");
            Error(check, "第 2 行",
                "world x\nscatter p points 8 geometry box geometry-size 1 1 1 model \"A.flax\"",
                "同时写了几何与模型");

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok)
            {
                check("带 geometry 的描述能编译", false, compiled.Error);
                return;
            }

            var kinds = new List<string>();

            foreach (var node in compiled.Graph.Nodes)
                kinds.Add(node.TypeId);

            check("落链是 点源 → 程序化几何 → 几何生成器（不是静态模型生成器）",
                Count(kinds, PCGGeometryNode.Id) == 1 &&
                Count(kinds, PCGSpawnGeometryNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 0,
                string.Join(", ", kinds));

            int primitives = IndexOf(compiled.Graph, PCGGeometryNode.Id, 0);

            check("几何节点的槽位按名字落位（形状下标 / 分段 / 尺寸 / 不用点缩放）",
                AsInt(Slot(compiled.Graph, primitives, "Shape")) == PCGGeometryNode.ShapeCylinder &&
                AsInt(Slot(compiled.Graph, primitives, "Segments")) == 8 &&
                Slot(compiled.Graph, primitives, "Cell Size") is Vector3 size &&
                Near(size.X, 3.0f) && Near(size.Y, 12.0f) && Near(size.Z, 3.0f) &&
                Slot(compiled.Graph, primitives, "Use Point Scale") is bool useScale && !useScale,
                Describe(compiled.Graph.Nodes[primitives].Values));

            // 真跑：这一层的点各自带上了网格——几何是**数据**，不是只有几个参数。
            var run = RunSpec(
                "world 石柱\nscatter pillars points 12 geometry cylinder geometry-size 3 12 3 geometry-segments 8", null);

            check("真跑：每个点都带上了建出来的网格（三角形数 > 0）",
                run != null && run.Count > 0 && run.Meshes != null && run.Meshes.Length == run.Count &&
                run.Meshes[0] != null && run.Meshes[0].TriangleCount > 0,
                run == null ? "null" : $"{run.Count} point(s), {run.Meshes?.Length ?? 0} mesh(es)");

            // 反面：同一句话去掉 geometry 就一个网格都没有（证明网格是这四段带来的，不是别处漏进来的）。
            var bare = RunSpec("world 石柱\nscatter pillars points 12", null);

            check("对照：去掉 geometry 之后这一层不带任何网格",
                bare != null && bare.Meshes == null,
                bare == null ? "null" : $"{bare.Meshes?.Length ?? 0} mesh(es)");
        }

        /// <summary>
        /// 描述层选**输出形态**（`output actors|instanced|merged`）：这一层的东西怎么写进场景。
        /// </summary>
        /// <remarks>
        /// 最有价值的那条是"资产 + `instanced`"：在加这个词之前，描述层永远走静态模型生成器，于是插件里早就有的
        /// `Spawn Foliage`（一片林子一个宿主）**一句描述也够不着**——"森林/草地"只能是几百个 Actor。
        /// </remarks>
        private static void OutputFormTests(CheckDelegate check)
        {
            // 资产 + instanced → 植被生成器，且模型/密度/缩放/偏航四档都跟着走。
            var foliage = CompileSpec(
                "world 林\nscatter trees points 40 density 0.5 model-scale 2 yaw on model \"A.flax\" output instanced",
                out var foliageGraph, out string foliageError);

            check("资产层写 output instanced 落成**植被生成器**（这条以前够不着）",
                foliage && Count(Kinds(foliageGraph), PCGSpawnFoliageNode.Id) == 1 &&
                Count(Kinds(foliageGraph), PCGSpawnStaticModelNode.Id) == 0,
                foliageError ?? string.Join(", ", Kinds(foliageGraph)));

            if (foliage)
            {
                int spawner = IndexOf(foliageGraph, PCGSpawnFoliageNode.Id, 0);

                check("实例化的四档参数都落到位（模型 id / 密度 / 缩放 / 随机偏航）",
                    Slot(foliageGraph, spawner, "Model") is Guid id && id == OtherId &&
                    Near(AsFloat(Slot(foliageGraph, spawner, "Density Threshold")), 0.5f) &&
                    Near(AsFloat(Slot(foliageGraph, spawner, "Scale Multiplier")), 2.0f) &&
                    Slot(foliageGraph, spawner, "Random Yaw") is bool yaw && yaw,
                    Describe(foliageGraph.Nodes[spawner].Values));
            }

            // 程序化几何 + merged / instanced：几何生成器的两个开关各就各位。
            var merged = CompileSpec(
                "world 柱\nscatter pillars points 20 geometry cylinder geometry-size 2 8 2 output merged",
                out var mergedGraph, out string mergedError);

            if (merged)
            {
                int spawner = IndexOf(mergedGraph, PCGSpawnGeometryNode.Id, 0);

                check("几何层写 output merged：几何生成器的合并开关置位、实例化开关关着",
                    Slot(mergedGraph, spawner, "Merge Into One Actor") is bool merge && merge &&
                    Slot(mergedGraph, spawner, "To Foliage Host") is bool host && !host,
                    Describe(mergedGraph.Nodes[spawner].Values));
            }
            else
            {
                check("几何层写 output merged 能编译", false, mergedError);
            }

            var instanced = CompileSpec(
                "world 草\nscatter tufts points 200 geometry box geometry-size 0.5 0.5 0.5 output instanced",
                out var instancedGraph, out string instancedError);

            if (instanced)
            {
                int spawner = IndexOf(instancedGraph, PCGSpawnGeometryNode.Id, 0);

                check("几何层写 output instanced：实例化开关置位、合并开关关着",
                    Slot(instancedGraph, spawner, "To Foliage Host") is bool host && host &&
                    Slot(instancedGraph, spawner, "Merge Into One Actor") is bool merge && !merge,
                    Describe(instancedGraph.Nodes[spawner].Values));
            }
            else
            {
                check("几何层写 output instanced 能编译", false, instancedError);
            }

            // 默认（不写 output）：两个开关都关着——老描述的行为一个字不变。
            var plain = CompileSpec(
                "world 柱\nscatter pillars points 20 geometry cylinder geometry-size 2 8 2",
                out var plainGraph, out string plainError);

            if (plain)
            {
                int spawner = IndexOf(plainGraph, PCGSpawnGeometryNode.Id, 0);

                check("不写 output 时是逐块建 Actor（两个开关都关）",
                    Slot(plainGraph, spawner, "Merge Into One Actor") is bool merge && !merge &&
                    Slot(plainGraph, spawner, "To Foliage Host") is bool host && !host,
                    Describe(plainGraph.Nodes[spawner].Values));
            }
            else
            {
                check("不写 output 的几何层能编译", false, plainError);
            }

            // 两类错都带行号：不认识的形态、以及 merged 用在了资产层上。
            Error(check, "第 2 行", "world x\nscatter p points 8 model \"A.flax\" output ball", "不认识的输出形态");
            Error(check, "第 2 行", "world x\nscatter p points 8 model \"A.flax\" output merged",
                "merged 只对程序化几何有意义");
        }

        /// <summary>把一段描述编译成图；返回是否成功。</summary>
        private static bool CompileSpec(string text, out PCGGraph graph, out string error)
        {
            graph = null;
            error = null;

            if (!PCGSpecParser.TryParse(text, out var spec, out error))
                return false;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok)
            {
                error = compiled.Error;
                return false;
            }

            graph = compiled.Graph;
            return true;
        }

        private static List<string> Kinds(PCGGraph graph)
        {
            var kinds = new List<string>();

            foreach (var node in graph.Nodes)
                kinds.Add(node.TypeId);

            return kinds;
        }

        /// <summary>
        /// 描述层的**刀口**（`cut &lt;nx&gt; &lt;ny&gt; &lt;nz&gt; &lt;offset&gt;`）：把每个形状按平面削一刀。
        /// </summary>
        /// <remarks>
        /// 四个数落进两个字段（法线 + 沿法线推多远），落链是 `… → 程序化几何 → 平面切割 → 生成器`。
        /// 刀口在**形状自己的坐标系**里——形状都建在自己原点，所以 `0 1 0 0` 就是"从中间横切"。
        /// </remarks>
        private static void CutWordTests(CheckDelegate check)
        {
            bool parsed = PCGSpecParser.TryParse(
                "world 石柱\nscatter pillars points 30 geometry cylinder geometry-size 3 12 3 cut 0 1 0 6",
                out var spec, out string error);

            check("cut 的四个数落进法线与偏移两个字段",
                parsed && spec.Scatters[0].GeometryCutNormal.HasValue &&
                Near(spec.Scatters[0].GeometryCutNormal.Value.Y, 1.0f) &&
                Near(spec.Scatters[0].GeometryCutOffset ?? -1.0f, 6.0f),
                parsed ? $"{spec.Scatters[0].GeometryCutNormal} @ {spec.Scatters[0].GeometryCutOffset}" : error);

            // 两类错都带行号：写了刀口却不是程序化几何、法线是零长度。
            Error(check, "第 2 行", "world x\nscatter p points 8 model \"A.flax\" cut 0 1 0 0",
                "cut 只对程序化几何有意义");
            Error(check, "第 2 行",
                "world x\nscatter p points 8 geometry box geometry-size 1 1 1 cut 0 0 0 0",
                "刀口法线是零长度");

            if (!parsed)
                return;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok)
            {
                check("带 cut 的描述能编译", false, compiled.Error);
                return;
            }

            var kinds = new List<string>();

            foreach (var node in compiled.Graph.Nodes)
                kinds.Add(node.TypeId);

            check("落链是 程序化几何 → 平面切割 → 几何生成器（刀口在几何之后、生成器之前）",
                Count(kinds, PCGGeometryNode.Id) == 1 && Count(kinds, PCGCutNode.Id) == 1 &&
                Count(kinds, PCGSpawnGeometryNode.Id) == 1,
                string.Join(", ", kinds));

            int primitives = IndexOf(compiled.Graph, PCGGeometryNode.Id, 0);
            int cut = IndexOf(compiled.Graph, PCGCutNode.Id, 0);

            check("几何 → 切割 那条线真的接上了（顺序不能反）",
                HasEdge(compiled.Graph, primitives, cut));

            check("刀口按名字落位：法线是原样那三个数，点位是**归一化后**沿法线推 offset",
                Slot(compiled.Graph, cut, "Plane Normal") is Vector3 normal &&
                Near(normal.Y, 1.0f) &&
                Slot(compiled.Graph, cut, "Plane Point") is Vector3 point &&
                Near(point.X, 0.0f) && Near(point.Y, 6.0f) && Near(point.Z, 0.0f) &&
                Slot(compiled.Graph, cut, "Invert") is bool invert && !invert,
                Describe(compiled.Graph.Nodes[cut].Values));
        }

        /// <summary>图里有没有一条 from → to 的边。</summary>
        private static bool HasEdge(PCGGraph graph, int from, int to)
        {
            foreach (var edge in graph.Edges)
            {
                if (edge.OutputNodeIndex == from && edge.InputNodeIndex == to)
                    return true;
            }

            return false;
        }

        private static PCGData RunSpec(string specText, Func<float, float, float?> terrain)
        {
            if (!PCGSpecParser.TryParse(specText, out var spec, out _))
                return null;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok || !PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
                return null;

            var world = new PCGWorldContext();

            if (terrain != null)
                world.HeightSource = terrain;

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = world,
                GenerationBounds = new BoundingBox(new Vector3(-1500, -400, -1500), new Vector3(1500, 600, 1500)),
                DryRun = true,
            };

            return new PCGGraphExecutor(program).Execute(context).PrimaryOutput;
        }

        private static int Distinct(float[] values, int count)
        {
            var seen = new List<float>();

            for (int i = 0; i < count && i < values.Length; i++)
            {
                bool known = false;

                for (int j = 0; j < seen.Count; j++)
                    known |= Math.Abs(seen[j] - values[i]) < 0.5f;

                if (!known)
                    seen.Add(values[i]);
            }

            return seen.Count;
        }

        private static bool AnyPositive(float[] values, int count)
        {
            for (int i = 0; i < count && i < values.Length; i++)
                if (values[i] > 0.0f)
                    return true;

            return false;
        }

        private static float Max(float[] values, int count)
        {
            float result = float.MinValue;

            for (int i = 0; i < count && i < values.Length; i++)
                result = Math.Max(result, values[i]);

            return result;
        }

        /// <summary>一座高斯山：中心高 200，衰减长度 150。</summary>
        private static float? Hill(float x, float z)
        {
            float squared = x * x + z * z;
            return 20.0f + 200.0f * (float)Math.Exp(-squared / (2.0 * 150.0 * 150.0));
        }

        /// <summary>一个碗：中心 20、边缘 100，半径 800 之外为 100。</summary>
        private static float? Bowl(float x, float z)
        {
            float distance = (float)Math.Sqrt(x * x + z * z);

            if (distance >= 800.0f)
                return 100.0f;

            return 100.0f - 80.0f * (float)Math.Cos(distance / 800.0f * Math.PI * 0.5);
        }

        // ---- 模型路径卫生 --------------------------------------------------------------------------------

        /// <summary>
        /// 模型路径写成绝对路径时的点名。这一条是给"描述编译得动、场景里却落不下东西"那类缺陷立的岗哨：
        /// 实测把引擎自带图元（工程 Content 之外）的绝对路径写进描述，编译期按路径读得到资产头、
        /// 报告里一片"资产库"，运行期按 Guid 却认不出——点数照出、Actor 落 0 个。
        /// </summary>
        private static void ModelPathHygieneTests(CheckDelegate check)
        {
            const string ParseFailed = "PARSE-FAILED ";
            const string Marker = "绝对路径";

            List<string> Warn(string text)
            {
                if (!PCGSpecParser.TryParse(text, out var spec, out string error))
                    return new List<string> { ParseFailed + error };

                return PCGSpecCompiler.Compile(spec, Resolver).Warnings;
            }

            int Count(List<string> warnings, string needle)
            {
                int hits = 0;

                for (int i = 0; i < warnings.Count; i++)
                {
                    if (warnings[i] != null && warnings[i].Contains(needle))
                        hits++;
                }

                return hits;
            }

            string[] absolute =
            {
                "C:/Program Files (x86)/Flax/Flax_1.12/Content/Editor/Primitives/Cone.flax",
                @"C:\Assets\Tree.flax",
                @"\\server\share\Tree.flax",
                "/mnt/assets/Tree.flax",
                "~/Assets/Tree.flax",
            };

            string[] relative =
            {
                "Content/PCG/Models/Pine.flax",
                "Assets/PCG/A.flax",
                "Tree.flax",
                "../Shared/Tree.flax",
            };

            foreach (string path in absolute)
            {
                var warnings = Warn("world 林\nscatter trees points 4 model \"" + path + "\"");

                check($"绝对路径「{path}」被点名（且不是靠解析失败混过去的）",
                      Count(warnings, Marker) == 1 && Count(warnings, ParseFailed) == 0,
                      string.Join(" | ", warnings));
            }

            foreach (string path in relative)
            {
                var warnings = Warn("world 林\nscatter trees points 4 model \"" + path + "\"");

                check($"相对路径「{path}」不该被点名",
                      Count(warnings, Marker) == 0 && Count(warnings, ParseFailed) == 0,
                      string.Join(" | ", warnings));
            }

            // 同一个路径写两次只点一次：多出来的那几句不增加信息，只会把提醒挤出视线。
            var twice = Warn("world 林\nscatter a points 2 model \"C:/x/Cone.flax\"\n" +
                             "scatter b points 2 model \"C:/x/Cone.flax\"");

            check("同一个绝对路径在一份描述里出现两次也只点名一次",
                  Count(twice, Marker) == 1, $"{Count(twice, Marker)} 次 | " + string.Join(" | ", twice));

            // 两个不同路径各点一次：去重是按路径的，不是"整份描述最多一句"。
            var twoPaths = Warn("world 林\nscatter a points 2 model \"C:/x/Cone.flax\"\n" +
                                 "scatter b points 2 model \"/mnt/y/Cube.flax\"");

            check("两个不同的绝对路径各点一次",
                  Count(twoPaths, Marker) == 2, $"{Count(twoPaths, Marker)} 次 | " + string.Join(" | ", twoPaths));

            // 仓库里那份示例是这条规矩的存量证据：改过一轮之后不该再有绝对模型路径。
            string folder = FindSpecsFolder();
            int found = 0;
            var offending = new List<string>();

            if (folder != null)
            {
                foreach (string file in System.IO.Directory.GetFiles(folder, "*.pcgspec"))
                {
                    found++;

                    if (Count(Warn(System.IO.File.ReadAllText(file)), Marker) > 0)
                        offending.Add(System.IO.Path.GetFileName(file));
                }
            }

            check("仓库里的示例描述不止一份（清单空了这条就白跑）", found >= 5, found.ToString());

            // 存量债已经**结清**：七份随仓库描述现在都写内建图元的裸名字（`model "Cone"`），
            // 既不再钉死这台机器的安装位置，也不再指向本仓库没有的资产。
            // 这条从"明账"翻成"零账"：以后谁再往描述里写绝对路径或指向不存在的资产，这里就红。
            string[] debt = Array.Empty<string>();

            offending.Sort();
            Array.Sort(debt);

            check("存量可移植性债已清零（描述里不该再有任何绝对模型路径）",
                  string.Join(",", offending) == string.Join(",", debt),
                  $"实测 {string.Join(", ", offending)}｜记账 {debt.Length} 项");

            // 同一条规矩也管**引擎自检夹具的默认值**：那里写死这台机器的引擎安装位置，
            // 换一台机器就有组静默进 skipped，而工具报出来的仍是"全绿"——门在无声中变小。
            // 这一条扫插件源码里的字符串字面量，并自证"确实扫到了那个文件"（扫描清单为空时它不能装成通过）。
            string pluginRoot = folder == null
                ? null
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    folder, "..", "..", "..", "Plugins", "PCG", "Source"));

            bool hasRoot = pluginRoot != null && System.IO.Directory.Exists(pluginRoot);

            check("插件源码根找得到（绝对路径扫描不能空跑）", hasRoot, pluginRoot ?? "null");

            int scanned = 0;
            bool sawSelfTest = false;
            var absoluteFixtures = new List<string>();

            if (hasRoot)
            {
                foreach (string file in System.IO.Directory.GetFiles(pluginRoot, "*.cs",
                             System.IO.SearchOption.AllDirectories))
                {
                    string normalised = file.Replace('/', '\\');

                    if (normalised.Contains("\\obj\\") || normalised.Contains("\\bin\\"))
                        continue;

                    scanned++;

                    if (normalised.EndsWith("\\PCGSelfTest.cs"))
                        sawSelfTest = true;

                    string text = System.IO.File.ReadAllText(file);

                    if (text.Contains("\"C:/") || text.Contains("\"C:\\"))
                        absoluteFixtures.Add(System.IO.Path.GetFileName(file));
                }
            }

            absoluteFixtures.Sort();

            check("扫描确实覆盖到引擎自检那份源码（否则下面那条绿灯没有意义）",
                  scanned > 0 && sawSelfTest, $"{scanned} 个源文件，含 PCGSelfTest.cs：{sawSelfTest}");

            check("插件源码里不该再有写死的引擎安装路径（夹具默认值也一样禁）",
                  hasRoot && scanned > 0 && absoluteFixtures.Count == 0,
                  string.Join(", ", absoluteFixtures));
        }

        /// <summary>
        /// 聚居地/城市这一侧的 `geometry`：共用的那条出口原先只会挂静态模型生成器，
        /// 现在一句 <c>village … geometry house</c> 就能让村子长出有屋顶的房子。
        /// </summary>
        private static void SettlementGeometryTests(CheckDelegate check)
        {
            const string village = "world 村\nvillage lots 4 rules \"X:0.5,0.5\" cell 400 40 400 " +
                                   "geometry house geometry-size 16 12 14";

            var result = PCGSpecCompiler.Compile(ParseSpecOrNull(village));
            var kinds = new List<string>();
            object shapeSlot = null;

            if (result.Ok)
            {
                foreach (var node in result.Graph.Nodes)
                {
                    kinds.Add(node.TypeId);

                    if (node.TypeId == PCGGeometryNode.Id && node.Values != null && node.Values.Length > 0)
                        shapeSlot = node.Values[0];
                }
            }

            check("聚居地写 geometry：编译得过，链是 切分 → 程序化几何 → 几何生成器（不再挂静态模型生成器）",
                result.Ok && Count(kinds, PCGSplitGrammarNode.Id) == 1 &&
                Count(kinds, PCGGeometryNode.Id) == 1 && Count(kinds, PCGSpawnGeometryNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 0,
                result.Ok ? string.Join(", ", kinds) : result.Error);

            check("聚居地的 geometry 形状落到槽位上（house 那一档，不是默认的盒子）",
                shapeSlot is int vShape && vShape == PCGGeometryNode.ShapeHouse,
                shapeSlot?.ToString() ?? "null");

            check("聚居地写了 geometry 就没必要报『没解析出模型』",
                result.Ok && Count(result.Warnings, "没有解析出资产") == 0,
                string.Join(" | ", result.Warnings));

            // 两个都写：几何赢，但 model 被忽略必须**点名**——安静地丢掉作者写下的一个选项，
            // 是本仓反复点名的那一族静默失效。
            var both = PCGSpecCompiler.Compile(ParseSpecOrNull(
                "world 村\nvillage lots 2 rules \"X:0.5,0.5\" cell 200 40 200 " +
                "geometry house geometry-size 8 6 8 model \"Cube\""));

            check("同时写 geometry 与 model 时点名『model 被忽略』（不是安静丢掉）",
                both.Ok && both.Warnings.Exists(w => w.Contains("model 被忽略")),
                string.Join(" | ", both.Warnings));

            // 只写 geometry 不给尺寸：默认 100³ 是格子级的数，会把房子建成一平方公里，
            // 所以这条也要点名（与 scatter 那边"尺寸必填"同一个理由）。
            var noSize = PCGSpecCompiler.Compile(ParseSpecOrNull(
                "world 村\nvillage lots 2 rules \"X:0.5,0.5\" cell 200 40 200 geometry house"));

            check("写 geometry 没给 geometry-size 时点名默认尺寸过大",
                noSize.Ok && noSize.Warnings.Exists(w => w.Contains("geometry-size")),
                string.Join(" | ", noSize.Warnings));

            // 真跑：地块上确实建出了网格（只断图里有几个节点，挡不住"这一层一个几何都没产出"）。
            if (result.Ok && PCGGraphCompiler.TryCompile(result.Graph, out var program))
            {
                var output = new PCGGraphExecutor(program).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 777,
                        GenerationBounds = new BoundingBox(new Vector3(-300, -50, -300), new Vector3(300, 50, 300)),
                    }.AsDryRun()).PrimaryOutput;

                int meshes = output?.Meshes?.Length ?? 0;
                int verts = meshes > 0 ? output.Meshes[0].VertexCount : 0;

                check("真跑：地块上建出了房子（件数 > 0 且每件是房屋的 30 顶点）",
                    meshes > 0 && verts == 30, $"{meshes} 件 / {verts} 顶点");
            }
            else
            {
                check("真跑：聚居地 geometry 那张图能编译出执行计划", false, "编译不出计划");
            }
            // 城市这一侧走的是同一条共用出口，但它是**另一处调用点**：上一轮只在引擎里用烘焙证明过，
            // 这里补一条离线断言（"改共用出口 = 四层一起生效"这句话要有两边的证据，不能只有一边）。
            var cityRun = PCGSpecCompiler.Compile(ParseSpecOrNull(
                "world 城\ncity productions \"District = House,House: X:0.5,0.5\" root District max-depth 1 " +
                "cell 400 40 400 geometry house geometry-size 30 20 26"));
            var cityKinds = new List<string>();
            object cityShapeSlot = null;

            if (cityRun.Ok)
                foreach (var node in cityRun.Graph.Nodes)
                {
                    cityKinds.Add(node.TypeId);

                    if (node.TypeId == PCGGeometryNode.Id && node.Values != null && node.Values.Length > 0)
                        cityShapeSlot = node.Values[0];
                }

            check("城市写 geometry：链是 符号语法 → 程序化几何 → 几何生成器（不挂静态模型生成器）",
                cityRun.Ok && Count(cityKinds, PCGGrammarNode.Id) == 1 &&
                Count(cityKinds, PCGGeometryNode.Id) == 1 && Count(cityKinds, PCGSpawnGeometryNode.Id) == 1 &&
                Count(cityKinds, PCGSpawnStaticModelNode.Id) == 0,
                cityRun.Ok ? string.Join(", ", cityKinds) : cityRun.Error);

            check("城市的 geometry 形状落到槽位上（house 那一档）",
                cityShapeSlot is int cShape && cShape == PCGGeometryNode.ShapeHouse,
                cityShapeSlot?.ToString() ?? "null");

            // 真跑：叶子数与产生式对得上（`District = House,House` 一个种子 ⇒ 2 片叶子，每件都是房屋的 30 顶点）。
            if (cityRun.Ok && PCGGraphCompiler.TryCompile(cityRun.Graph, out var cityProgram))
            {
                var cityOutput = new PCGGraphExecutor(cityProgram).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 31,
                        GenerationBounds = new BoundingBox(new Vector3(-300, -50, -300), new Vector3(300, 50, 300)),
                    }.AsDryRun()).PrimaryOutput;

                int cityMeshes = cityOutput?.Meshes?.Length ?? 0;
                int cityVerts = cityMeshes > 0 ? cityOutput.Meshes[0].VertexCount : 0;

                check("真跑：城市的符号语法切出 2 片叶子并各建出一栋房（件数==2、每件 30 顶点）",
                    cityMeshes == 2 && cityVerts == 30, $"{cityMeshes} 件 / {cityVerts} 顶点");
            }
            else
            {
                check("真跑：城市 geometry 那张图能编译出执行计划", false, "编译不出计划");
            }
        }

        /// <summary>
        /// 按符号分派形状（<c>geometry-map</c>）：一层里不同种类的叶子各长各的。
        /// 要守的不只是"链的形状对"，更是**符号与形状的配对没接错**——接错的产物每一样都"看着像长出来了"，
        /// 只有把下游几何节点的 Shape 与上游过滤器筛的序号对上才看得出来。
        /// </summary>
        private static void SymbolShapeDispatchTests(CheckDelegate check)
        {
            // 两个叶子符号：Room（被 House 改写出来）与 Plot（Block 直接留下的叶子）。
            const string productions =
                "District = Block,Block: X:0.5,0.5; Block = House,House,Plot: Z:0.4,0.3,0.3; " +
                "House = Room,Room: X:0.5,0.5";

            string Spec(string map) =>
                "world 城\ncity productions \"" + productions + "\" root District max-depth 2 cell 400 40 400 " + map;

            var result = PCGSpecCompiler.Compile(
                ParseSpecOrNull(Spec("geometry-map \"Room=house 30 20 26; Plot=grid 60 1 40\"")), Resolver);

            check("geometry-map 编译得过", result.Ok, result.Error ?? "ok");

            if (!result.Ok)
                return;

            var graph = result.Graph;
            int filters = 0;
            int geometries = 0;
            int geometrySpawners = 0;
            int modelSpawners = 0;

            foreach (var node in graph.Nodes)
            {
                if (node.TypeId == PCGAttributeFilterNode.Id)
                    filters++;
                else if (node.TypeId == PCGGeometryNode.Id)
                    geometries++;
                else if (node.TypeId == PCGSpawnGeometryNode.Id)
                    geometrySpawners++;
                else if (node.TypeId == PCGSpawnStaticModelNode.Id)
                    modelSpawners++;
            }

            check("两个符号 ⇒ 各一条 筛 → 几何 → 生成器（且不再挂静态模型生成器）",
                filters == 2 && geometries == 2 && geometrySpawners == 2 && modelSpawners == 0,
                $"{filters} 筛 / {geometries} 几何 / {geometrySpawners} 几何生成器 / {modelSpawners} 静态模型");

            // 配对：筛"序号 K"的那条链，下游几何节点必须是 K 那个符号写下的形状。
            var roomShape = PairedShape(graph, "Room");
            var plotShape = PairedShape(graph, "Plot");

            check("符号与形状逐一对上（Room→house、Plot→grid；接错时两条都绿不了）",
                roomShape == PCGGeometryNode.ShapeHouse && plotShape == PCGGeometryNode.ShapeGrid,
                $"Room→{roomShape}、Plot→{plotShape}（期望 {PCGGeometryNode.ShapeHouse} 与 {PCGGeometryNode.ShapeGrid}）");

            // 尺寸也要跟着符号走：接错时体积会差一个数量级，而链的形状与 Shape 都仍然"对"。
            var roomSize = PairedSize(graph, "Room");

            check("尺寸跟着符号走（Room 那一条是 30×20×26）",
                Near(roomSize.X, 30.0f) && Near(roomSize.Y, 20.0f) && Near(roomSize.Z, 26.0f),
                roomSize.ToString());

            // 三类必错的情形：点了语法里没有的符号、点了还会被改写的非叶子、漏掉一个叶子。
            var unknown = PCGSpecCompiler.Compile(ParseSpecOrNull(Spec("geometry-map \"Cellar=box 4 4 4\"")), Resolver);

            check("点了语法里没有的符号：报错并把它其实认识的符号列出来",
                !unknown.Ok && unknown.Error != null && unknown.Error.Contains("Cellar") &&
                unknown.Error.Contains("District"),
                unknown.Error ?? "竟然编译过了");

            var notLeaf = PCGSpecCompiler.Compile(ParseSpecOrNull(Spec("geometry-map \"Block=box 4 4 4\"")), Resolver);

            check("给还会被改写的符号指派形状：报错（那种形状永远没有产出会用到它）",
                !notLeaf.Ok && notLeaf.Error != null && notLeaf.Error.Contains("不是叶子"),
                notLeaf.Error ?? "竟然编译过了");

            var missing = PCGSpecCompiler.Compile(ParseSpecOrNull(Spec("geometry-map \"Room=house 30 20 26\"")), Resolver);

            check("漏掉一个叶子符号：报错并点名漏的那个（静默少一类正是本层最怕的）",
                !missing.Ok && missing.Error != null && missing.Error.Contains("Plot"),
                missing.Error ?? "竟然编译过了");

            // **真跑**：两条分支分到的叶子必须**不重不漏**——两支各自 > 0，且加起来等于语法切出的总数。
            // 只断言链的形状与配对，挡不住"过滤器读的列与语法写的列类型不同 ⇒ 两支都放行 0 件"，
            // 而那种产物的样子恰好是"城市变空了但没有任何地方报错"。
            // 真跑用的语法与上面那份**只差一条产生式**：这里 House 不再被改写，故它和 Plot 都是叶子。
            // 拿上面那份跑真跑会被"非叶子"闸门拦下——那正是上面第 5 条断言要的，不是这里要的。
            const string leafProductions =
                "District = Block,Block: X:0.5,0.5; Block = House,House,Plot: Z:0.4,0.3,0.3";

            var runResult = PCGSpecCompiler.Compile(ParseSpecOrNull(
                "world 城\ncity productions \"" + leafProductions +
                "\" root District max-depth 2 cell 400 40 400 geometry-map \"House=house 30 20 26; Plot=grid 60 1 40\""),
                Resolver);

            if (!runResult.Ok || !PCGGraphCompiler.TryCompile(runResult.Graph, out var dispatchProgram))
            {
                check("真跑：分派那张图能编译出执行计划", false, runResult.Error ?? "编译不出计划");
                return;
            }

            var dispatchInspection = new PCGNodeInspection();
            var dispatchContext = new PCGExecutionContext
            {
                Seed = 4711,
                World = new PCGWorldContext(),
                GenerationBounds = new BoundingBox(new Vector3(-600, -50, -600), new Vector3(600, 50, 600)),
                Inspection = dispatchInspection,
            };

            new PCGGraphExecutor(dispatchProgram).Execute(dispatchContext.AsDryRun());

            int leafTotal = 0;
            int branchSum = 0;
            int branches = 0;
            var branchCounts = new List<int>();

            foreach (var record in dispatchInspection.Records)
            {
                if (record.TypeId == PCGGrammarNode.Id)
                    leafTotal += record.PointCount;
                else if (record.TypeId == PCGAttributeFilterNode.Id)
                {
                    branchSum += record.PointCount;
                    branches++;
                    branchCounts.Add(record.PointCount);
                }
            }

            check("真跑：两条分支各接到叶子（不空转），且件数与语法切出的总数不重不漏",
                branches == 2 && branchCounts.TrueForAll(c => c > 0) && branchSum == leafTotal && leafTotal > 0,
                $"语法 {leafTotal} 片叶子 → 分支 {string.Join(" + ", branchCounts)} = {branchSum}");
        }

        /// <summary>
        /// 找出"筛这个符号"那条链下游的几何节点，交回它的形状与尺寸；找不到给 -1 / 零向量。
        /// </summary>
        private static int PairedShape(PCGGraph graph, string symbolName)
        {
            var filter = FilterFor(graph, symbolName);

            if (filter < 0)
                return -1;

            foreach (var edge in graph.Edges)
            {
                if (edge == null || edge.OutputNodeIndex != filter)
                    continue;

                var node = graph.Nodes[edge.InputNodeIndex];

                if (node.TypeId == PCGGeometryNode.Id && node.Values?.Length > 0 && node.Values[0] is int shape)
                    return shape;
            }

            return -1;
        }

        private static Vector3 PairedSize(PCGGraph graph, string symbolName)
        {
            var filter = FilterFor(graph, symbolName);

            if (filter < 0)
                return Vector3.Zero;

            foreach (var edge in graph.Edges)
            {
                if (edge == null || edge.OutputNodeIndex != filter)
                    continue;

                var node = graph.Nodes[edge.InputNodeIndex];

                if (node.TypeId == PCGGeometryNode.Id && node.Values?.Length > 3 && node.Values[3] is Vector3 size)
                    return size;
            }

            return Vector3.Zero;
        }

        /// <summary>
        /// 用**符号表自己**把符号名翻译成序号，再找 Min 等于那个序号的属性过滤器节点。
        /// 这里刻意不重新数一遍符号顺序：那等于把两处编号绑成一处，漂了就一起错而没人响。
        /// </summary>
        private static int FilterFor(PCGGraph graph, string symbolName)
        {
            int ordinal = -1;

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];

                if (node.TypeId != PCGGrammarNode.Id)
                    continue;

                string productions = node.Values?[0] as string;

                if (productions == null ||
                    !PCGGrammarNode.TryParseProductions(productions, out var grammar, out _) ||
                    !grammar.TryFindSymbol(symbolName, out ordinal))
                    return -1;

                break;
            }

            if (ordinal < 0)
                return -1;

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];

                if (node.TypeId != PCGAttributeFilterNode.Id || node.Values == null || node.Values.Length < 3)
                    continue;

                if (node.Values[0] as string != PCGGrammarNode.SymbolColumn)
                    continue;

                if (node.Values[1] is float min && node.Values[2] is float max &&
                    min == (float)ordinal && max == (float)ordinal)
                    return i;
            }

            return -1;
        }

        // ---- A − B：圈定的范围里再挖掉一块 ----------------------------------------------------

        /// <summary>
        /// 散布层的"长在哪"（within/outside/polygon）与"从中去掉哪"（except/except-polygon）是两个职责，
        /// 同写就是 A − B。真跑要证的是**减得对**：挖洞是又串一道筛而不是替掉圈地那道、少掉的那批正好都在洞里、
        /// 而矩形洞与同形状的多边形洞（两套各自实现的筛）必须一个个对得上。
        /// </summary>
        private static void AreaDifferenceTests(CheckDelegate check)
        {
            const string A = "0 0 1000 1000";
            const string B = "300 300 700 700";
            const string HoleVertices = "300 300; 700 300; 700 700; 300 700";

            bool parsed = PCGSpecParser.TryParse(
                "world 挖洞\nscatter trees per-block 16 within " + A + " except " + B,
                out var spec, out string error);

            check("within 与 except 同写不算冲突（一个说长在哪，一个说从中去掉哪）", parsed, error);

            if (!parsed)
                return;

            var layer = spec.Scatters[0];

            check("except 的四个数按「最小角 → 最大角」各自落进自己的字段",
                layer.ExceptMinX.HasValue && layer.ExceptMinZ.HasValue &&
                layer.ExceptMaxX.HasValue && layer.ExceptMaxZ.HasValue &&
                Near(layer.ExceptMinX.Value, 300.0f) && Near(layer.ExceptMinZ.Value, 300.0f) &&
                Near(layer.ExceptMaxX.Value, 700.0f) && Near(layer.ExceptMaxZ.Value, 700.0f),
                $"{layer.ExceptMinX} {layer.ExceptMinZ} {layer.ExceptMaxX} {layer.ExceptMaxZ}");

            check("except 不把圈地那条顶掉（within 的四个数与方向都还在）",
                layer.AreaMaxX.HasValue && Near(layer.AreaMaxX.Value, 1000.0f) && !layer.AreaInvert,
                $"AreaMaxX={layer.AreaMaxX} AreaInvert={layer.AreaInvert}");

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            check("带挖洞的描述能编译", compiled.Ok, compiled.Error ?? "ok");

            if (!compiled.Ok)
                return;

            var kinds = new List<string>();

            foreach (var node in compiled.Graph.Nodes)
                kinds.Add(node.TypeId);

            int keepArea = IndexOf(compiled.Graph, PCGAreaFilterNode.Id, 0);
            int cutArea = IndexOf(compiled.Graph, PCGAreaFilterNode.Id, 1);

            check("挖洞是**又串一道筛**，不是把圈地那道改成反向（两道 Area Filter 才叫 A − B）",
                Count(kinds, PCGAreaFilterNode.Id) == 2 && keepArea >= 0 && cutArea >= 0,
                string.Join(", ", kinds));

            check("前一道圈地（Invert=false）、后一道挖洞（Invert=true），且各带各的数",
                Slot(compiled.Graph, keepArea, "Invert") as bool? == false &&
                Slot(compiled.Graph, cutArea, "Invert") as bool? == true &&
                Slot(compiled.Graph, keepArea, "Max X") as float? == 1000.0f &&
                Slot(compiled.Graph, cutArea, "Min X") as float? == 300.0f &&
                Slot(compiled.Graph, cutArea, "Max Z") as float? == 700.0f,
                $"keep Invert={Slot(compiled.Graph, keepArea, "Invert")} " +
                $"cut Invert={Slot(compiled.Graph, cutArea, "Invert")} " +
                $"cut MinX={Slot(compiled.Graph, cutArea, "Min X")}");

            // 真跑：同一份点源、同一个种子，只有"有没有那道挖洞筛"这一个变量。
            var holed = RunSpec("world 挖洞\nscatter g per-block 16 within " + A + " except " + B, null);
            var plain = RunSpec("world 挖洞\nscatter g per-block 16 within " + A, null);

            check("真跑：挖完洞还剩点（空层会让下面几条空转地通过）",
                holed != null && holed.Count > 0 && plain != null && plain.Count > 0,
                $"洞后 {holed?.Count} / 洞前 {plain?.Count}");

            if (holed == null || holed.Count == 0 || plain == null || plain.Count == 0)
                return;

            int holedOutside = 0;
            int holedInHole = 0;

            foreach (var point in holed.Points)
            {
                if (!PointInRect(point.Position, 0.0f, 0.0f, 1000.0f, 1000.0f))
                    holedOutside++;

                if (PointInRect(point.Position, 300.0f, 300.0f, 700.0f, 700.0f))
                    holedInHole++;
            }

            check("真跑：洞里的点一个都不剩", holedInHole == 0, $"{holedInHole} 个还留在洞里");

            check("真跑：圈外一个都没有（挖洞没有顺带把圈地那道筛弄坏）",
                holedOutside == 0, $"{holedOutside} 个跑到了圈外");

            int plainInHole = 0;

            foreach (var point in plain.Points)
            {
                if (PointInRect(point.Position, 300.0f, 300.0f, 700.0f, 700.0f))
                    plainInHole++;
            }

            // 这条才是"减对了"：差值不是"变少了"这种弱判据，而是**等于洞里数出来的个数**，
            // 并且洞里必须有货——否则两份相同也照样通过，那就是空转。
            check("真跑：减掉的正好是洞里那批（洞前 − 洞后 == 洞里数出来的，且洞里非空）",
                plainInHole > 0 && plain.Count - holed.Count == plainInHole,
                $"{plain.Count} − {holed.Count} = {plain.Count - holed.Count}，洞里 {plainInHole} 个");

            // 同一块洞换成多边形写法：两套筛各自实现，减完必须**一个不差地相等**。
            var polyHoled = RunSpec(
                "world 挖洞\nscatter g per-block 16 within " + A + " except-polygon \"" + HoleVertices + "\"",
                null);

            check("真跑：同一块洞用多边形挖，减完的点数与矩形写法相等（两套独立实现的筛对得上）",
                polyHoled != null && polyHoled.Count == holed.Count,
                $"矩形洞 {holed.Count} vs 多边形洞 {polyHoled?.Count}");

            // 单独写 except（不圈地）= 整片范围挖掉一块。
            var minusOnly = RunSpec("world 挖洞\nscatter g per-block 16 except " + B, null);
            var whole = RunSpec("world 挖洞\nscatter g per-block 16", null);

            int wholeInHole = 0;

            if (whole != null)
            {
                foreach (var point in whole.Points)
                {
                    if (PointInRect(point.Position, 300.0f, 300.0f, 700.0f, 700.0f))
                        wholeInHole++;
                }
            }

            check("真跑：只写 except（不写 within）也成立——洞里空、洞外照旧长，减掉的仍是洞里那批",
                minusOnly != null && minusOnly.Count > 0 && whole != null && whole.Count > 0 &&
                wholeInHole > 0 && whole.Count - minusOnly.Count == wholeInHole &&
                CountInRect(minusOnly, 300.0f, 300.0f, 700.0f, 700.0f) == 0,
                $"整片 {whole?.Count} − 挖后 {minusOnly?.Count} = 洞里 {wholeInHole}");

            // 三类必错：写两次、角写反、以及 except 与 outside 都算"区域"时的重复。
            Error(check, "第 2 行",
                "world x\nscatter trees per-block 16 except " + B + " except 1 1 2 2",
                "不止一次 except");

            Error(check, "第 2 行",
                "world x\nscatter trees per-block 16 except 700 700 300 300",
                "写反了或退化成了一条线");

            Error(check, "第 2 行",
                "world x\nscatter trees per-block 16 within " + A + " within 1 1 2 2",
                "不止一次区域");
        }

        /// <summary>
        /// 聚居地的区域约束：`within` 圈地、`except` 从圈好的地里挖掉一块——与散布层同一对节点、同一条判据。
        /// 选项"能落位"只证明值到了字段上；这里要证的是**筛子真的接在链上**并且真跑有效——
        /// 一个写了却没人读的 except，在图上与没写一模一样，在场景里就是"村庄照样铺过湖去"。
        /// 洞取"圈地的左半边"：村庄默认摆在选择中心，所以左右两半都有地块是结构保证的，不必凭猜数它的尺寸。
        /// </summary>
        private static void SettlementRegionTests(CheckDelegate check)
        {
            const string Within = " within -800 -800 800 800";
            const string Hole = " except -800 -800 0 800";
            const string Base = "world 村\nvillage lots 25 rules \"X:0.5,0.5\" cell 200 20 200";

            string plainSpec = Base + Within;
            string holedSpec = Base + Within + Hole;

            if (!PCGSpecParser.TryParse(holedSpec, out var spec, out string error))
            {
                check("聚居地同写 within 与 except 能解析", false, error);
                return;
            }

            var result = PCGSpecCompiler.Compile(spec, Resolver);

            check("聚居地带区域约束能编译", result.Ok, result.Error ?? "ok");

            if (!result.Ok)
                return;

            var graph = result.Graph;
            int keep = -1;
            int cut = -1;
            int areas = 0;

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.Nodes[i].TypeId != PCGAreaFilterNode.Id)
                    continue;

                areas++;

                if (Slot(graph, i, "Invert") as bool? == true)
                    cut = i;
                else
                    keep = i;
            }

            check("圈地与挖洞是**两道筛**串在同一条链上（不是把圈地那道改成反向）",
                areas == 2 && keep >= 0 && cut >= 0, $"{areas} 道 Area Filter：留 {keep} / 挖 {cut}");

            check("挖洞那道带着 except 自己的四个数",
                Slot(graph, cut, "Min X") as float? == -800.0f &&
                Slot(graph, cut, "Max X") as float? == 0.0f &&
                Slot(graph, cut, "Max Z") as float? == 800.0f,
                $"MinX={Slot(graph, cut, "Min X")} MaxX={Slot(graph, cut, "Max X")} " +
                $"MaxZ={Slot(graph, cut, "Max Z")}");

            var plain = RunSpec(plainSpec, null);
            var holed = RunSpec(holedSpec, null);

            int plainInHole = 0;
            int holedInHole = 0;

            if (plain != null)
            {
                foreach (var point in plain.Points)
                {
                    if (PointInRect(point.Position, -800.0f, -800.0f, 0.0f, 800.0f))
                        plainInHole++;
                }
            }

            if (holed != null)
            {
                foreach (var point in holed.Points)
                {
                    if (PointInRect(point.Position, -800.0f, -800.0f, 0.0f, 800.0f))
                        holedInHole++;
                }
            }

            // 四条一起才是"洞有效"：两份都要有地块、洞里一个不剩、少的正好等于数出来的洞内块数、
            // 且洞里本来得有东西（否则两份相同也照样通过，那是空转）。
            check("真跑（聚居地）：洞里一个地块都不剩，减掉的正好是落在洞里的那批",
                plain != null && holed != null && plain.Count > 0 && holed.Count > 0 &&
                plainInHole > 0 && holedInHole == 0 && plain.Count - holed.Count == plainInHole,
                $"挖前 {plain?.Count ?? -1} → 挖后 {holed?.Count ?? -1}，洞里数出 {plainInHole}，" +
                $"挖后洞里剩 {holedInHole}");

            // 反向对照：`outside` 那道筛必须真的反过来——它与 within+except 该留下同一批地块。
            var outsideOnly = RunSpec(Base + " outside -800 -800 0 800", null);

            check("真跑（聚居地）：outside 与 within+except 留下的是同一批地块（同一条链的两种写法）",
                outsideOnly != null && holed != null && outsideOnly.Count == holed.Count,
                $"outside {outsideOnly?.Count ?? -1} vs within+except {holed?.Count ?? -1}");

            // 城市：两条产出消费者（按符号分派的筛链 / 普通生成器）都必须接在**挖洞之后**。
            // 上一轮就是因为没把两处都接完而刻意不放这些键——这里就是那条要求的证据。
            const string CityProductions =
                "District = Block,Block: X:0.5,0.5; Block = House,House,Plot: Z:0.4,0.3,0.3";
            const string CityBase = "world 城\ncity productions \"" + CityProductions +
                                    "\" root District max-depth 1 cell 400 40 400";
            const string CityRegion = " within -800 -800 800 800 except -800 -800 0 800";

            var cityGraph = CompileText(CityBase + CityRegion +
                                        " geometry-map \"House=box 20 20 20; Plot=box 40 1 40\"");
            var plainCityGraph = CompileText(CityBase + CityRegion + " geometry box geometry-size 20 20 20");

            check("城市带区域约束的两种写法都能编译",
                cityGraph != null && plainCityGraph != null,
                cityGraph != null && plainCityGraph != null
                    ? "两种写法都编译得过"
                    : $"按符号分派那份 {(cityGraph == null ? "编译不过" : "ok")}、" +
                      $"普通生成器那份 {(plainCityGraph == null ? "编译不过" : "ok")}");

            if (cityGraph != null && plainCityGraph != null)
            {
                int cityHole = FindCityHole(cityGraph);
                int plainHole = FindCityHole(plainCityGraph);

                // 从"洞"这一道筛往下走可达集；每条产出消费者都必须在这个集合里。
                var reachable = ReachableFrom(cityGraph, cityHole);
                int filters = 0;
                int filtersAfterHole = 0;

                for (int i = 0; i < cityGraph.Nodes.Length; i++)
                {
                    if (cityGraph.Nodes[i].TypeId != PCGAttributeFilterNode.Id)
                        continue;

                    filters++;

                    if (reachable.Contains(i))
                        filtersAfterHole++;
                }

                check("真编译（城市 · 按符号分派）：两道分派筛都在挖洞那道**之后**（少一道就是静默忽略）",
                    cityHole >= 0 && filters == 2 && filtersAfterHole == 2,
                    $"洞 = {cityHole}，分派筛 {filters} 道、其中在洞之后 {filtersAfterHole} 道");

                var plainSpawn = new List<int>();

                for (int i = 0; i < plainCityGraph.Nodes.Length; i++)
                {
                    if (plainCityGraph.Nodes[i].TypeId == PCGSpawnGeometryNode.Id)
                        plainSpawn.Add(i);
                }

                var plainReach = ReachableFrom(plainCityGraph, plainHole);
                int plainAfter = 0;

                foreach (int node in plainSpawn)
                {
                    if (plainReach.Contains(node))
                        plainAfter++;
                }

                check("真编译（城市 · 普通生成器）：生成器也接在挖洞那道之后",
                    plainHole >= 0 && plainSpawn.Count == 1 && plainAfter == 1,
                    $"洞 = {plainHole}，生成器 {plainSpawn.Count} 个、在洞之后 {plainAfter} 个");
            }

            // **层标签要三条链都走到**：程序化几何以前在大纲里一律叫 `Geometry`，
            // 上千件与几件长得一模一样——本会话两次归因（1330 件是谁摆的、摆件埋没埋）都因此只能猜。
            // 这里逐条钉"描述里那一层的名字真进了生成器槽位"，三种写法一条都不许漏。
            var labelScatter = CompileText(
                "world 石\nscatter boulders points 20 geometry box geometry-size 4 4 4");
            var labelVillage = CompileText(
                "world 村\nvillage lots 16 rules \"X:0.5,0.5\" cell 160 40 160 geometry house geometry-size 40 24 34");
            var labelCity = CompileText(CityBase + " geometry box geometry-size 20 20 20");

            string LabelOf(PCGGraph g)
            {
                int node = g == null ? -1 : IndexOf(g, PCGSpawnGeometryNode.Id, 0);
                return node < 0 ? null : Slot(g, node, "Label") as string;
            }

            check("散点层的名字进了几何生成器的 Label 槽（三条链的最后一条）",
                LabelOf(labelScatter) == "boulders",
                labelScatter == null ? "编译不过" : $"读数：{LabelOf(labelScatter)}");

            check("聚居地的名字进了几何生成器的 Label 槽",
                LabelOf(labelVillage) == "房屋 1",
                labelVillage == null ? "编译不过" : $"读数：{LabelOf(labelVillage)}");

            check("城市的名字进了几何生成器的 Label 槽",
                LabelOf(labelCity) == "城区 1",
                labelCity == null ? "编译不过" : $"读数：{LabelOf(labelCity)}");

            // 按符号分派的一座城会开出**好几条**生成器：如果它们共用一个标签，
            // 名字就会撞成两个 `城区2Merged`——那正是这条特性要消灭的歧义，所以当场钉住。
            var labelMap = CompileText(
                "world 城\ncity productions \"District = Block,Block: X:0.5,0.5; " +
                "Block = House,House,Plot: Z:0.4,0.3,0.3\" root District max-depth 2 cell 400 40 400" +
                " geometry-map \"House=box 20 20 20; Plot=box 40 1 40\"");
            var mapLabels = new List<string>();

            if (labelMap != null)
            {
                for (int i = 0; i < labelMap.Nodes.Length; i++)
                {
                    if (labelMap.Nodes[i].TypeId != PCGSpawnGeometryNode.Id)
                        continue;

                    var slot = Slot(labelMap, i, "Label") as string;

                    if (!string.IsNullOrEmpty(slot))
                        mapLabels.Add(slot);
                }
            }

            check("按符号分派的每一条生成器标签**各不相同**（撞名等于这条特性白做）",
                mapLabels.Count == 2 && mapLabels[0] != mapLabels[1],
                mapLabels.Count == 0 ? "编译不过或一个标签都没读到" : string.Join(" / ", mapLabels));

            // 反向对照：不写几何、只挂模型的层不该被硬塞一个标签（它走的是另一条生成器）。
            var labelModel = CompileText("world 林\nscatter pines points 12 model \"Cone\"");
            int geometryInModel = labelModel == null ? -1 : Count(
                new List<string>(System.Array.ConvertAll(
                    labelModel.Nodes, n => n.TypeId)), PCGSpawnGeometryNode.Id);

            check("只挂模型的层不会被凭空造出几何生成器（上一条的标签来源是真的层名）",
                labelModel != null && geometryInModel == 0,
                labelModel == null ? "编译不过" : $"几何生成器 {geometryInModel} 个");

            // `output` 要落到生成器的**槽位**上才算数：字段落了位而没人读，正是这一层最怕的那种静默失效
            // （`能落位` 那两条自动探针对此一无所知——它们只看描述对象上的字段）。
            bool SlotOn(PCGGraph g, string typeId, string slot, int which)
            {
                if (g == null)
                    return false;

                int seen = 0;

                for (int i = 0; i < g.Nodes.Length; i++)
                {
                    if (g.Nodes[i].TypeId != typeId)
                        continue;

                    if (seen++ != which)
                        continue;

                    return Slot(g, i, slot) as bool? == true;
                }

                return false;
            }

            int CountOf(PCGGraph g, string typeId)
            {
                int n = 0;

                if (g == null)
                    return 0;

                for (int i = 0; i < g.Nodes.Length; i++)
                {
                    if (g.Nodes[i].TypeId == typeId)
                        n++;
                }

                return n;
            }

            const string CityMapBase = "world 城\ncity productions \"District = Block,Block: X:0.5,0.5; " +
                                       "Block = House,House,Plot: Z:0.4,0.3,0.3\" root District max-depth 2 cell 400 40 400";

            var mergedPlain = CompileText(CityBase + " geometry box geometry-size 20 20 20 output merged");
            var instancedPlain = CompileText(CityBase + " geometry box geometry-size 20 20 20 output instanced");
            var mergedMap = CompileText(CityMapBase +
                " geometry-map \"House=box 20 20 20; Plot=box 40 1 40\" output merged");

            int mergedSpawner = mergedPlain == null ? -1 : IndexOf(mergedPlain, PCGSpawnGeometryNode.Id, 0);

            check("城市 output merged 落到**槽位**（普通那条路：合并置位、实例化不置位）",
                SlotOn(mergedPlain, PCGSpawnGeometryNode.Id, "Merge Into One Actor", 0) &&
                !SlotOn(mergedPlain, PCGSpawnGeometryNode.Id, "To Foliage Host", 0),
                mergedPlain == null ? "编译不过（下面两条也会红）"
                    : $"生成器节点 {mergedSpawner}：Merge={Slot(mergedPlain, mergedSpawner, "Merge Into One Actor")} " +
                      $"FoliageHost={Slot(mergedPlain, mergedSpawner, "To Foliage Host")}");

            int instSpawner = instancedPlain == null ? -1 : IndexOf(instancedPlain, PCGSpawnGeometryNode.Id, 0);

            check("城市 output instanced 落到**槽位**（换成植被宿主那一档）",
                SlotOn(instancedPlain, PCGSpawnGeometryNode.Id, "To Foliage Host", 0) &&
                !SlotOn(instancedPlain, PCGSpawnGeometryNode.Id, "Merge Into One Actor", 0),
                instancedPlain == null
                    ? "编译不过（上面一条也会红）"
                    : $"生成器节点 {instSpawner}：Merge={Slot(instancedPlain, instSpawner, "Merge Into One Actor")} " +
                      $"FoliageHost={Slot(instancedPlain, instSpawner, "To Foliage Host")}");

            // 按符号分派那条路是**另一半**：只接普通生成器的话，一座 geometry-map 的城会照旧一片一个 Actor。
            check("城市 output merged 也落到**按符号分派的每一支**（两支生成器都要合并）",
                CountOf(mergedMap, PCGSpawnGeometryNode.Id) == 2 &&
                SlotOn(mergedMap, PCGSpawnGeometryNode.Id, "Merge Into One Actor", 0) &&
                SlotOn(mergedMap, PCGSpawnGeometryNode.Id, "Merge Into One Actor", 1),
                mergedMap == null ? "编译不过" :
                    $"生成器 {CountOf(mergedMap, PCGSpawnGeometryNode.Id)} 个");

            // 挂模型的城合不成一个 Actor：这句话必须当场报错，不能被读成"没写 output"。
            bool badParsed = PCGSpecParser.TryParse(CityBase + " model \"Cube\" output merged", out var badSpec, out _);
            var badResult = badParsed ? PCGSpecCompiler.Compile(badSpec, Resolver) : null;

            check("城市挂着模型却写 output merged：报错并说清只对 geometry 有意义",
                badResult != null && !badResult.Ok &&
                badResult.Error != null && badResult.Error.Contains("不是程序化几何"),
                badResult?.Error ?? "编译过了或没能解析");

            // 反向对照：不写 output 时两个开关都得是关着的（默认值不能被上一段改动带成常开）。
            var bareCity = CompileText(CityBase + " geometry box geometry-size 20 20 20");

            int bareSpawner = bareCity == null ? -1 : IndexOf(bareCity, PCGSpawnGeometryNode.Id, 0);

            check("不写 output 时两个开关都关着（默认没被改动带成常开）",
                bareCity != null &&
                !SlotOn(bareCity, PCGSpawnGeometryNode.Id, "Merge Into One Actor", 0) &&
                !SlotOn(bareCity, PCGSpawnGeometryNode.Id, "To Foliage Host", 0),
                bareCity == null
                    ? "编译不过（上面几条也会红）"
                    : $"生成器节点 {bareSpawner}：Merge={Slot(bareCity, bareSpawner, "Merge Into One Actor")} " +
                      $"FoliageHost={Slot(bareCity, bareSpawner, "To Foliage Host")}");

            // 聚居地那份也要同样一条：`output` 只有落到生成器槽位上才算被读走（字段落位不算）。
            var villageMerged = CompileText(
                "world 村\nvillage lots 16 rules \"X:0.5,0.5\" cell 160 40 160 geometry house " +
                "geometry-size 40 24 34 output merged");
            int villageSpawner = villageMerged == null ? -1 : IndexOf(villageMerged, PCGSpawnGeometryNode.Id, 0);

            check("聚居地 output merged 落到生成器**槽位**（与城市同一对开关）",
                villageSpawner >= 0 &&
                Slot(villageMerged, villageSpawner, "Merge Into One Actor") as bool? == true &&
                Slot(villageMerged, villageSpawner, "To Foliage Host") as bool? == false,
                villageMerged == null
                    ? "编译不过"
                    : $"生成器节点 {villageSpawner}：Merge={Slot(villageMerged, villageSpawner, "Merge Into One Actor")} " +
                      $"FoliageHost={Slot(villageMerged, villageSpawner, "To Foliage Host")}");

            // 挂模型的村子写 output ⇒ 编译期报错，不能被读成"没写"。
            bool villageBadParsed = PCGSpecParser.TryParse(
                "world 村\nvillage lots 16 rules \"X:0.5,0.5\" cell 160 40 160 model \"Cube\" output merged",
                out var villageBadSpec, out _);
            var villageBad = villageBadParsed ? PCGSpecCompiler.Compile(villageBadSpec, Resolver) : null;

            check("聚居地挂着模型却写 output merged：报错（与城市同一条判据）",
                villageBad != null && !villageBad.Ok && villageBad.Error != null &&
                villageBad.Error.Contains("没写 geometry"),
                villageBad?.Error ?? "编译过了或没能解析");

            // 反向对照：不写 output 的村子两个开关都得关着。
            var villageBare = CompileText(
                "world 村\nvillage lots 16 rules \"X:0.5,0.5\" cell 160 40 160 geometry house geometry-size 40 24 34");
            int villageBareSpawner = villageBare == null ? -1 : IndexOf(villageBare, PCGSpawnGeometryNode.Id, 0);

            check("聚居地不写 output 时两个开关关着（默认没被带成常开）",
                villageBareSpawner >= 0 &&
                Slot(villageBare, villageBareSpawner, "Merge Into One Actor") as bool? == false &&
                Slot(villageBare, villageBareSpawner, "To Foliage Host") as bool? == false,
                villageBare == null
                    ? "编译不过"
                    : $"生成器节点 {villageBareSpawner}：Merge={Slot(villageBare, villageBareSpawner, "Merge Into One Actor")}");
        }

        /// <summary>找出图里那道**反向**的面积筛（即 `except` 落成的挖洞筛）；没有给 -1。</summary>
        private static int FindCityHole(PCGGraph graph)
        {
            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.Nodes[i].TypeId != PCGAreaFilterNode.Id)
                    continue;

                if (Slot(graph, i, "Invert") as bool? == true)
                    return i;
            }

            return -1;
        }

        /// <summary>从某个节点顺着连线能走到的全部下游节点（含起点自己）。</summary>
        private static HashSet<int> ReachableFrom(PCGGraph graph, int from)
        {
            var seen = new HashSet<int>();

            if (from < 0)
                return seen;

            var queue = new Queue<int>();
            queue.Enqueue(from);
            seen.Add(from);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                foreach (var edge in graph.Edges)
                {
                    if (edge == null || edge.OutputNodeIndex != current)
                        continue;

                    if (seen.Add(edge.InputNodeIndex))
                        queue.Enqueue(edge.InputNodeIndex);
                }
            }

            return seen;
        }

        /// <summary>点是否落在这段矩形里（含边界，与 Area Filter 的口径一致）。</summary>
        private static bool PointInRect(Vector3 position, float minX, float minZ, float maxX, float maxZ) =>
            position.X >= minX && position.X <= maxX && position.Z >= minZ && position.Z <= maxZ;

        private static int CountInRect(PCGData data, float minX, float minZ, float maxX, float maxZ)
        {
            int count = 0;

            foreach (var point in data.Points)
            {
                if (PointInRect(point.Position, minX, minZ, maxX, maxZ))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 路面改用**自己建的水平网格**（<c>road … geometry grid</c>），不再借 Flax 的 Plane 图元。
        /// </summary>
        /// <remarks>
        /// 起因是一条实测的引擎事实：Flax 的 Plane 是**竖直面片**，而路这条链在扫掠点上只写偏航，
        /// 于是随仓库的示例里每一条路都渲染成一排排墙（记忆 pcg-demo-roads-render-as-vertical-walls）。
        /// 这里要守的三层各是不同的一件事，少一层就会漏：
        /// ① 描述层能把 geometry 接进**路**这一条链（此前只有 scatter / 聚居地 / 城市能写）；
        /// ② 形状、尺寸、段数确实落到几何节点的槽位上（解析得过 ≠ 值到了）；
        /// ③ 跑出来的网格是**躺平的**——Y 跨度为 0、法线朝上、长宽落在 X 与 Z 上。
        /// 只断①②挡不住"网格依旧是竖的"；只断"有网格"挡不住尺寸接错轴（第二个数被当成厚度用掉，
        /// 路就变成一条 1 米宽的带子）。尺寸故意用三个互不相同的数，接错轴当场红。
        /// </remarks>
        private static void RoadGeometryTests(CheckDelegate check)
        {
            const string slab = "world 路\nroad from -300 0 to 300 0 cell 40 slope 15 spacing 100 " +
                                "geometry grid geometry-size 100 7 120 geometry-segments 2";

            var compiled = PCGSpecCompiler.Compile(ParseSpecOrNull(slab), Resolver);
            var kinds = new List<string>();

            if (compiled.Ok)
                foreach (var node in compiled.Graph.Nodes)
                    kinds.Add(node.TypeId);

            check("路写 geometry：链是 字面点 → 路径 → 程序化几何 → 几何生成器（不挂静态模型生成器）",
                compiled.Ok && Count(kinds, PCGPointListNode.Id) == 1 && Count(kinds, PCGPathNode.Id) == 1 &&
                Count(kinds, PCGGeometryNode.Id) == 1 && Count(kinds, PCGSpawnGeometryNode.Id) == 1 &&
                Count(kinds, PCGSpawnStaticModelNode.Id) == 0,
                compiled.Ok ? string.Join(", ", kinds) : compiled.Error);

            int geometry = IndexOf(compiled.Graph, PCGGeometryNode.Id, 0);
            int spawner = IndexOf(compiled.Graph, PCGSpawnGeometryNode.Id, 0);

            check("路的 geometry 三档参数各归各的槽位（格栅那一档 + 尺寸 + 段数）",
                compiled.Ok && geometry >= 0 &&
                Slot(compiled.Graph, geometry, "Shape") is int shape && shape == PCGGeometryNode.ShapeGrid &&
                Slot(compiled.Graph, geometry, "Cell Size") is Vector3 cell &&
                Near(cell.X, 100.0f) && Near(cell.Y, 7.0f) && Near(cell.Z, 120.0f) &&
                Slot(compiled.Graph, geometry, "Segments") is int segments && segments == 2,
                geometry >= 0 ? Describe(compiled.Graph.Nodes[geometry].Values) : "图里没有几何节点");

            // 生成器上的层标签：路这一条链写的是"路面"，靠它把 Actor 名对上描述的哪一行。
            check("路的几何生成器带上层标签『路面』（Actor 名可归因到描述的行）",
                compiled.Ok && spawner >= 0 && Slot(compiled.Graph, spawner, "Label") as string == "路面",
                spawner >= 0 ? Describe(compiled.Graph.Nodes[spawner].Values) : "图里没有几何生成器");

            // 真跑：数每一片路面，判据是"躺平"而不是"件数大于零"。
            int meshes = 0;
            int verts = 0;
            int standUp = 0;
            int wrongFootprint = 0;
            int normalDown = 0;

            if (compiled.Ok && PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                var output = new PCGGraphExecutor(program).Execute(
                    new PCGExecutionContext
                    {
                        Seed = 20261012,
                        World = new PCGWorldContext { HeightSource = FlatHeight },
                        GenerationBounds = new BoundingBox(
                            new Vector3(-600, -100, -600), new Vector3(600, 100, 600)),
                    }.AsDryRun()).PrimaryOutput;

                if (output?.Meshes != null)
                {
                    meshes = output.Meshes.Length;

                    foreach (var mesh in output.Meshes)
                    {
                        if (mesh == null || mesh.VertexCount == 0)
                            continue;

                        if (verts == 0)
                            verts = mesh.VertexCount;

                        var box = PCGMeshBuilder.Bounds(mesh);

                        // 厚度必须是 0：Plane 那种竖直面片的 Y 跨度就是它的"宽"，一眼能分。
                        if (box.Size.Y > 1e-3f)
                            standUp++;

                        if (!Near(box.Size.X, 100.0f, 1e-2f) || !Near(box.Size.Z, 120.0f, 1e-2f))
                            wrongFootprint++;

                        if (mesh.Normals == null || mesh.Normals.Length == 0 ||
                            !Near(mesh.Normals[0].Y, 1.0f, 1e-3f))
                            normalDown++;
                    }
                }
            }
            else
            {
                check("路 geometry 那张图能编译出执行计划", false, "编译不出计划");
            }

            check("真跑：路面片数不为零，每片都是 3×3 的格栅（段数 2 落到了建网格那一步）",
                meshes > 0 && verts == 9, $"{meshes} 片 / 每片 {verts} 顶点");

            check("真跑：每一片路面都是**躺平**的（Y 跨度为 0）——竖直面片那条缺陷的反向断言",
                meshes > 0 && standUp == 0,
                $"{standUp}/{meshes} 片的 Y 跨度不为零");

            check("真跑：路面的长宽落在 X 与 Z 上（100×120），第二个数没有被当成厚度接进面片",
                meshes > 0 && wrongFootprint == 0,
                $"{wrongFootprint}/{meshes} 片的落地面尺寸不对");

            check("真跑：每一片路面的法线朝上（+Y），从上方看得见路面",
                meshes > 0 && normalDown == 0, $"{normalDown}/{meshes} 片法线不朝上");

            // 对照：不写 geometry 的路**照旧**挂静态模型生成器——geometry 是选择，不是把路整条换掉。
            var control = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 路\nroad from -300 0 to 300 0 cell 40 spacing 100 model \"Cube\""),
                Resolver);
            var controlKinds = new List<string>();

            if (control.Ok)
                foreach (var node in control.Graph.Nodes)
                    controlKinds.Add(node.TypeId);

            check("对照：只写 model 的路仍是 路径 → 静态模型生成器（没有几何节点）",
                control.Ok && Count(controlKinds, PCGGeometryNode.Id) == 0 &&
                Count(controlKinds, PCGSpawnGeometryNode.Id) == 0 &&
                Count(controlKinds, PCGSpawnStaticModelNode.Id) == 1,
                control.Ok ? string.Join(", ", controlKinds) : control.Error);

            // 两处提醒要有**路自己的调用点**的证据：共用出口的警告只在 scatter 那侧钉过，
            // 路是新接的一条调用链，漏接的结局是"写了 geometry 没给尺寸"一路安静地建出 100³ 的盒子。
            var noSize = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 路\nroad from -300 0 to 300 0 cell 40 spacing 100 geometry grid"),
                Resolver);

            check("路写了 geometry 却没给 geometry-size 时点名（路的调用点，不是只钉过散布层）",
                noSize.Ok && noSize.Warnings.Exists(w => w.Contains("geometry-size")),
                noSize.Ok ? string.Join(" | ", noSize.Warnings) : noSize.Error);

            var both = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 路\nroad from -300 0 to 300 0 cell 40 spacing 100 " +
                                "geometry grid geometry-size 100 1 100 model \"Plane\""),
                Resolver);

            check("路同时写 geometry 与 model 时点名『model 被忽略』",
                both.Ok && both.Warnings.Exists(w => w.Contains("model 被忽略")),
                both.Ok ? string.Join(" | ", both.Warnings) : both.Error);

            // 随仓库的示例是这个缺陷的**入口**：那九条路必须真的改过来，只在节点里修好而示例照旧写 Plane，
            // 用户看到的仍然是一排墙。
            string folder = FindSpecsFolder();
            int roadLines = 0;
            int stillPlane = 0;
            int noSlab = 0;

            if (folder != null)
            {
                foreach (string file in System.IO.Directory.GetFiles(folder, "*.pcgspec"))
                {
                    foreach (string raw in System.IO.File.ReadAllLines(file))
                    {
                        string line = raw.TrimStart();

                        if (!line.StartsWith("road ", StringComparison.Ordinal))
                            continue;

                        roadLines++;

                        if (line.Contains("model \"Plane\""))
                            stillPlane++;

                        if (!line.Contains("geometry grid"))
                            noSlab++;
                    }
                }
            }

            check("示例里确实有路可查（清单为空这条就白跑）", roadLines > 0, $"{roadLines} 条路");

            check("随仓库的示例不再有第二条竖直面片的路（model \"Plane\" 已清干净）",
                roadLines > 0 && stillPlane == 0, $"{stillPlane}/{roadLines} 条仍写着 model \"Plane\"");

            check("随仓库的示例每一条路都改成了自己的水平网格",
                roadLines > 0 && noSlab == 0, $"{noSlab}/{roadLines} 条没有 geometry grid");

            // `model-scale` 与 `yaw` 在程序化几何这一档**没有对象**（尺寸只认 geometry-size、朝向由几何节点定）。
            // 安静丢掉作者写下的数，正是本仓反复点名的那一族静默失效 ⇒ 必须点名。
            var scaled = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 路\nroad from -300 0 to 300 0 cell 40 spacing 100 " +
                                "geometry grid geometry-size 100 1 100 model-scale 2 yaw on"),
                Resolver);

            check("路在 geometry 上写 model-scale 与 yaw 时点名『不生效』（两个数一起点出来）",
                scaled.Ok && scaled.Warnings.Exists(w =>
                    w.Contains("model-scale") && w.Contains("yaw") && w.Contains("不生效")),
                scaled.Ok ? string.Join(" | ", scaled.Warnings) : scaled.Error);

            // 正向同夹具对照：同一行**不写**这两个数时不该冒出这句，否则那条绿只是"消息一直响"。
            var plain = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 路\nroad from -300 0 to 300 0 cell 40 spacing 100 " +
                                "geometry grid geometry-size 100 1 100"),
                Resolver);

            check("对照：不写 model-scale 与 yaw 的路面行不该冒出『不生效』",
                plain.Ok && !plain.Warnings.Exists(w => w.Contains("不生效")),
                plain.Ok ? string.Join(" | ", plain.Warnings) : plain.Error);

            // 散布层是**另一处调用点**（它不走 AddSpawner 那条出口），同一个道理要在它自己那侧也钉一次。
            var scatterScaled = PCGSpecCompiler.Compile(
                ParseSpecOrNull("world 林\nscatter wood points 4 geometry box geometry-size 6 8 6 model-scale 1.5"),
                Resolver);

            check("散布层 geometry 上写 model-scale 同样点名（另一处调用点，不是只钉过路）",
                scatterScaled.Ok && scatterScaled.Warnings.Exists(w =>
                    w.Contains("model-scale") && w.Contains("不生效") && w.Contains("wood")),
                scatterScaled.Ok ? string.Join(" | ", scatterScaled.Warnings) : scatterScaled.Error);
        }

        /// <summary>
        /// 演示描述里那处 `except` 必须**真的挖掉东西**：一个不落点的洞在编译、连线数、别的链的断言下都是绿的，
        /// 而用户看到的是"林子照样长在村子上面"——示例是唯一的入口，所以这里按示例自己的坐标与地形跑一遍。
        /// </summary>
        private static void DemoHoleTests(CheckDelegate check)
        {
            var graph = CompileShipped("demo_township");
            if (graph == null)
            {
                check("demo_township 在盘上（不在就跳过这条）", false, "不在或解析不过");
                return;
            }

            // 认出那道洞筛：这一份描述里唯一一道**反向**的 Area Filter（不钉死它的四个数——
            // 演示的摆位改了不该让测试跟着改）。
            int hole = -1;
            int inverted = 0;

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.Nodes[i].TypeId != PCGAreaFilterNode.Id)
                    continue;

                if (Slot(graph, i, "Invert") as bool? != true)
                    continue;

                inverted++;
                hole = i;
            }

            check("演示的庄角房层带着那道挖洞筛（这一份里至少一道反向筛）", hole >= 0 && inverted >= 1,
                $"{inverted} 道反向筛，最后一道是图上第 {hole} 个");

            if (hole < 0)
                return;

            // 洞的**上一道**就是这一层筛到它之前的那个节点（边的 OutputNodeIndex 是源头、InputNodeIndex 是去处）。
            int band = -1;

            foreach (var edge in graph.Edges)
            {
                if (edge != null && edge.InputNodeIndex == hole)
                {
                    band = edge.OutputNodeIndex;
                    break;
                }
            }

            check("挖洞那道确实接在这一层筛到它之前的节点之后（同一条链，不是另起一支）", band >= 0,
                $"上游是第 {band} 个节点");

            if (band < 0)
                return;

            var minX = Slot(graph, hole, "Min X") as float?;
            var maxX = Slot(graph, hole, "Max X") as float?;
            var minZ = Slot(graph, hole, "Min Z") as float?;
            var maxZ = Slot(graph, hole, "Max Z") as float?;

            check("洞的四个数都真的写进了那道筛（声明了却没人填是静默失效）",
                minX.HasValue && maxX.HasValue && minZ.HasValue && maxZ.HasValue,
                $"{minX} {minZ} {maxX} {maxZ}");

            if (!minX.HasValue || !maxX.HasValue || !minZ.HasValue || !maxZ.HasValue)
                return;

            if (!PCGGraphCompiler.TryCompile(graph, out var program))
            {
                check("演示那份能编译出执行计划", false, "编译不出计划");
                return;
            }

            // 采样上限开大：这一层只有几十个点，全收下来才能**数**出洞里有多少个。
            var inspection = new PCGNodeInspection(400);
            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext { HeightSource = Hill },
                GenerationBounds = Box(new Vector3(300, -800, 4000), new Vector3(3300, 800, 6400)),
                Inspection = inspection,
            };

            new PCGGraphExecutor(program).Execute(context.AsDryRun());

            int bandCount = -1;
            int holeCount = -1;
            var bandSamples = new Vector3[0];

            foreach (var record in inspection.Records)
            {
                if (record.NodeIndex == band)
                {
                    bandCount = record.PointCount;
                    bandSamples = record.Samples ?? new Vector3[0];
                }
                else if (record.NodeIndex == hole)
                    holeCount = record.PointCount;
            }

            int inHole = 0;

            foreach (var position in bandSamples)
            {
                if (PointInRect(position, minX.Value, minZ.Value, maxX.Value, maxZ.Value))
                    inHole++;
            }

            // 四条一起才叫"示例里的洞真的在挖东西"：洞前有点、洞后没清空、收来的样本是**全量**
            // （否则数出来的洞内点数只是下界）、少的个数**等于**数出来的洞内点数。
            // 空转的洞在编译、连线数、别的链的断言下都是绿的，而用户看到的是"灌木照样长在村子里"。
            check("真跑（演示那份）：洞前有点、洞后没清空，少的正好是落在洞里的那批",
                bandCount > 0 && holeCount > 0 && bandCount == bandSamples.Length && inHole > 0 &&
                bandCount - holeCount == inHole,
                $"洞 x[{minX},{maxX}] z[{minZ},{maxZ}]：洞前 {bandCount} → 洞后 {holeCount}，洞里数出 {inHole} 个" +
                (inHole == 0 ? "；洞前的点在 " + string.Join(" ", Positions(bandSamples)) : string.Empty));

            // 这一份现在有**两处** `except`（庄角的房、第一座城绕开村心）。逐处都要真的挖掉东西：
            // 空转的洞在"能编译、点数没变、别的链照旧"这些粗判据下全是绿的，而屏幕上就是"城还压在村上"。
            var holesCutting = new List<string>();
            var holesIdling = new List<string>();

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                if (graph.Nodes[i].TypeId != PCGAreaFilterNode.Id)
                    continue;

                if (Slot(graph, i, "Invert") as bool? != true)
                    continue;

                int upstream = -1;

                foreach (var edge in graph.Edges)
                {
                    if (edge != null && edge.InputNodeIndex == i)
                    {
                        upstream = edge.OutputNodeIndex;
                        break;
                    }
                }

                int before = -1;
                int after = -1;

                foreach (var record in inspection.Records)
                {
                    if (record.NodeIndex == upstream)
                        before = record.PointCount;
                    else if (record.NodeIndex == i)
                        after = record.PointCount;
                }

                if (before > 0 && after >= 0 && after < before)
                    holesCutting.Add($"节点 {i}: {before} → {after}");
                else
                    holesIdling.Add($"节点 {i}: 上游 {before} → 本道 {after}");
            }

            check("演示里每一处 except 都真的挖掉了地块（一处都不许是空转的洞）",
                holesCutting.Count >= 2 && holesIdling.Count == 0,
                $"挖动过 {string.Join("、", holesCutting)}；空转 {string.Join("、", holesIdling)}");

            // 洞在链上的**位置**也得证：它排在 fade 之前，所以"筛掉 2 个"未必等于"屏幕少 2 丛"——
            // 那 2 个可能本来就被后面那道 fade 挡在门外，洞于是白挖。
            // 口径用整份描述的全部产出点数之和：两份文本只差那半句 except，差值就归因得到。
            string folder = FindSpecsFolder();

            if (folder == null)
                return;

            string shippedText = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "demo_township.pcgspec"));
            string plainText = System.Text.RegularExpressions.Regex.Replace(
                shippedText, @" except [0-9.\-]+ [0-9.\-]+ [0-9.\-]+ [0-9.\-]+", string.Empty);

            check("演示那份确实带着那半句 except（不然下面是在比两份一模一样的文本）",
                plainText != shippedText, $"{shippedText.Length} → {plainText.Length} 字符");

            var bounds = Box(new Vector3(300, -800, 4000), new Vector3(3300, 800, 6400));

            int withHole = TotalSinkOutput(graph, bounds);
            int withoutHole = TotalSinkOutput(CompileText(plainText), bounds);

            check("真跑（演示那份）：整份描述的最终产出因这道洞而变少（洞不是白挖的）",
                withHole > 0 && withoutHole > withHole,
                $"无洞 {withoutHole} → 有洞 {withHole}");
        }

        /// <summary>把一段描述文本编译成图（示例文件在盘上，但改过的文本要能当场跑）。</summary>
        private static PCGGraph CompileText(string text)
        {
            if (!PCGSpecParser.TryParse(text, out var spec, out _))
                return null;

            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            return compiled.Ok ? compiled.Graph : null;
        }

        /// <summary>整份干跑一遍，交回所有汇出的点数之和；编译不过给 -1（让调用处的断言报红而不是空转）。</summary>
        private static int TotalSinkOutput(PCGGraph graph, BoundingBox bounds)
        {
            if (graph == null || !PCGGraphCompiler.TryCompile(graph, out var program))
                return -1;

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                World = new PCGWorldContext { HeightSource = Hill },
                GenerationBounds = bounds,
            };

            var run = new PCGGraphExecutor(program).Execute(context.AsDryRun());

            if (run.Outputs == null)
                return -1;

            int total = 0;

            foreach (var data in run.Outputs)
            {
                if (data != null)
                    total += data.Count;
            }

            return total;
        }

        /// <summary>把位置收成简短的 "x,z" 串，最多列 8 个——够用来挑一块真正落得到点的洞。</summary>
        private static IEnumerable<string> Positions(Vector3[] samples)
        {
            for (int i = 0; i < samples.Length && i < 8; i++)
                yield return $"{samples[i].X:F0},{samples[i].Z:F0}";
        }

        // ---- 内建图元的名字 ------------------------------------------------------------------

        /// <summary>
        /// 描述层可以直接写的六个引擎图元名字：表本身、编译器优先查表（不经宿主）、
        /// 以及"七份示例描述真的只用这个名字"的存量对账。
        /// </summary>
        private static void BuiltinModelNameTests(CheckDelegate check)
        {
            string[] names = PCG.Graph.PCGBuiltinModels.Names;

            check("表里六个名字，且按书写顺序",
                  names.Length == 6 && names[0] == "Capsule" && names[5] == "Sphere",
                  string.Join(",", names));

            var ids = new List<Guid>();
            var badName = new List<string>();

            foreach (string name in names)
            {
                var id = PCG.Graph.PCGBuiltinModels.IdOf(name);

                if (id == Guid.Empty)
                    badName.Add(name);
                else
                    ids.Add(id);
            }

            check("六个名字都解析得出非零 Guid", badName.Count == 0, string.Join(",", badName));
            ids.Sort();
            int distinct = 0;

            for (int i = 0; i < ids.Count; i++)
                if (i == 0 || ids[i] != ids[i - 1])
                    distinct++;

            check("六枚 Guid 互不相同（撞车的两枚会让两个层长同一个东西而没人说话）",
                  distinct == 6, distinct.ToString());

            // 名字不是路径：带扩展名、带目录、空的、不认识的，都必须**不**命中表，交给宿主的口径。
            var mistaken = new List<string>();

            foreach (string notAName in new[] { "Cone.flax", "C:/x/Cone.flax", "Content/PCG/Models/Pine.flax", "", "Pine" })
                if (PCG.Graph.PCGBuiltinModels.TryResolve(notAName, out _))
                    mistaken.Add(notAName);

            check("带扩展名/带目录/空的/不在表里的名字都不命中表", mistaken.Count == 0, string.Join(" | ", mistaken));

            check("大小写不敏感（描述是人手写的）",
                  PCG.Graph.PCGBuiltinModels.IdOf("cone") == PCG.Graph.PCGBuiltinModels.IdOf("Cone"),
                  PCG.Graph.PCGBuiltinModels.IdOf("cone").ToString());

            // 编译器优先查表：**一个宿主解析器都不给**也要把 Guid 落进槽位，并且不许抱怨"解析不出"。
            var compiled = PCGSpecCompiler.Compile(ParseSpecOrNull("world 林\nscatter trees points 4 model \"Cone\""));            var spawner = FirstSpawner(compiled.Graph);
            var slot = spawner != null && spawner.Values != null && spawner.Values.Length > 0 ? spawner.Values[0] : null;

            check("裸名字不经任何宿主解析器就落进 Model 槽位",
                  slot is Guid gid && gid == PCG.Graph.PCGBuiltinModels.IdOf("Cone"),
                  slot?.ToString() ?? "null");
            check("裸名字不该被报成『没有解析出资产』",
                  compiled.Warnings.Count == 0, string.Join(" | ", compiled.Warnings));

            // 存量对账：七份示例描述里每一处 `model "…"` 都必须是表里的名字。
            string folder = FindSpecsFolder();
            var strays = new List<string>();
            int refs = 0;

            if (folder != null)
            {
                foreach (string file in System.IO.Directory.GetFiles(folder, "*.pcgspec"))
                {
                    foreach (string line in System.IO.File.ReadAllLines(file))
                    {
                        if (line.TrimStart().StartsWith("#"))
                            continue;

                        int at = line.IndexOf("model \"", StringComparison.Ordinal);

                        while (at >= 0)
                        {
                            int from = at + 7;
                            int to = line.IndexOf('"', from);

                            if (to < 0)
                                break;

                            string value = line.Substring(from, to - from);
                            refs++;

                            if (!PCG.Graph.PCGBuiltinModels.TryResolve(value, out _))
                                strays.Add(System.IO.Path.GetFileName(file) + ":" + value);

                            at = line.IndexOf("model \"", to, StringComparison.Ordinal);
                        }
                    }
                }
            }

            check("仓库描述的每一处 model 引用都是表里的名字（存量对账）",
                  refs >= 20 && strays.Count == 0, $"{refs} 处引用 | 出格 {string.Join(", ", strays)}");
        }

        /// <summary>取图里第一个生成器节点（静态模型或植被），用来读它的 Model 槽位。</summary>
        private static PCGNode FirstSpawner(PCGGraph graph)
        {
            if (graph?.Nodes == null)
                return null;

            foreach (var node in graph.Nodes)
                if (node != null &&
                    (node.TypeId == PCG.Nodes.PCGSpawnStaticModelNode.Id || node.TypeId == PCG.Nodes.PCGSpawnFoliageNode.Id))
                    return node;

            return null;
        }

        // ---- 汇覆盖 ---------------------------------------------------------------------------------------

        /// <summary>
        /// 编译图上的**汇覆盖**判据：凡是没有下游消费者的节点，就必须出现在 `SinkNodes` 里。
        /// 这是"没有分支被悄悄丢掉"唯一可判定的形式。
        /// </summary>
        /// <remarks>
        /// 这里原本守的是另一条：从 `SinkNodes` 反向走 `Links`，把走不到的节点点名。那条**结构上恒空**
        /// （2026-10-11 实测证伪）——编译器给 `SinkNodes` 的定义就是"没有消费者的节点"，所以一个节点
        /// 要么被某个消费者拉到，要么它自己就是汇。判据永不会红，等于没有判据，函数与调用点一起撤掉了。
        /// 撤掉之后要守的东西并没有消失：真正会出错的是**编译期**——若某个无消费者的节点既不进
        /// `SinkNodes` 也没人引用它，执行器一次都不会走到它，产出被直接丢掉且不留警告。
        /// 所以这条判据是那条死码的等价替身，而且它真的能红。
        /// </remarks>
        private static void SinkCoverageTests(CheckDelegate check)
        {
            // 没有任何节点把它当上游 ⇒ 它必须自己当汇，否则没人要求值它。
            static List<int> UnconsumedNotSink(PCG.Execution.PCGCompiledGraph plan)
            {
                var broken = new List<int>();

                int count = plan.Links?.Length ?? 0;
                var consumed = new bool[count];

                for (int node = 0; node < count; node++)
                {
                    var pins = plan.Links[node];

                    if (pins == null)
                        continue;

                    for (int pin = 0; pin < pins.Length; pin++)
                    {
                        var upstream = pins[pin];

                        if (upstream == null)
                            continue;

                        for (int k = 0; k < upstream.Length; k++)
                        {
                            int source = upstream[k].SourceNodeIndex;

                            if (source >= 0 && source < count)
                                consumed[source] = true;
                        }
                    }
                }

                var sinks = new bool[count];

                if (plan.SinkNodes != null)
                    for (int i = 0; i < plan.SinkNodes.Length; i++)
                    {
                        int sink = plan.SinkNodes[i];

                        if (sink >= 0 && sink < count)
                            sinks[sink] = true;
                    }

                for (int i = 0; i < count; i++)
                    if (!consumed[i] && !sinks[i])
                        broken.Add(i);

                return broken;
            }

            static PCG.Execution.PCGInputLink[] Link(int source) =>
                new[] { new PCG.Execution.PCGInputLink { SourceNodeIndex = source, SourcePinIndex = 0 } };

            // 空转对照：0→1，1 是汇，两个节点各得其所 ⇒ 清单为空。
            var wellFormed = new PCG.Execution.PCGCompiledGraph
            {
                Links = new[] { Array.Empty<PCG.Execution.PCGInputLink[]>(), new[] { Link(0) } },
                SinkNodes = new[] { 1 },
            };

            check("正常编译图给空清单（判据不在好图上误报）",
                  UnconsumedNotSink(wellFormed).Count == 0, string.Join(",", UnconsumedNotSink(wellFormed)));

            // **防空转断言**：把同一张图的汇清单故意清空——那条没人引用的分支就该被点名。
            // 没有这一条，上面那次的绿灯与"这道判据根本看不见任何东西"是同一个形状。
            var dropped = new PCG.Execution.PCGCompiledGraph
            {
                Links = wellFormed.Links,
                SinkNodes = Array.Empty<int>(),
            };

            check("把汇清单清空后判据会红（否则上一条守不住任何东西）",
                  UnconsumedNotSink(dropped).Count == 1 && UnconsumedNotSink(dropped)[0] == 1,
                  string.Join(",", UnconsumedNotSink(dropped)));

            // 退化输入不抛。
            check("Links 还没建好的图给空清单（不抛）",
                  UnconsumedNotSink(new PCG.Execution.PCGCompiledGraph { Links = null, SinkNodes = null }).Count == 0,
                  "ok");

            // 同一把尺子量真实编译产物：七份示例图里每个无消费者的节点都在汇清单上。
            foreach (string sample in new[]
            {
                "valley", "city", "township", "demo_valley", "demo_city", "demo_township", "demo_cave",
            })
            {
                var shipped = CompileShipped(sample);

                if (shipped == null)
                {
                    check($"示例 {sample}.pcgspec 那张图还在（缺了这条就白跑）", false, "CompileShipped 给了 null");
                    continue;
                }

                if (!PCG.Execution.PCGGraphCompiler.TryCompile(shipped, out var plan))
                {
                    check($"示例 {sample}.pcgspec 编译不出执行计划", false, "TryCompile 返回 false");
                    continue;
                }

                var broken = UnconsumedNotSink(plan);

                check($"示例 {sample}.pcgspec 的真实编译图上没有漏登记的汇（{plan.Nodes?.Length ?? 0} 个节点）",
                      broken.Count == 0, string.Join(",", broken));
            }
        }

        // ---- 描述文件 -----------------------------------------------------------------------------------

        /// <summary>
        /// 描述**文件**这一层：读写、扩展名、BOM、以及"读不到 / 写错了"两类失败的措辞。
        /// </summary>
        /// <remarks>
        /// 编辑器里那条路（「生成」按钮）只做三件事：读文件、编译、装进画布。前两件都在这里，第三件只能目视。
        /// 所以这一组是"按钮按下去之前"的全部证据。
        /// </remarks>
        private static void FileTests(CheckDelegate check)
        {
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcg_spec_tests");
            System.IO.Directory.CreateDirectory(folder);

            string path = System.IO.Path.Combine(folder, "valley.pcgspec");
            System.IO.File.WriteAllText(path, Full, new UTF8Encoding(false));

            check("描述文件的路径会补上扩展名",
                PCGSpecFile.ResolvePath(path) == System.IO.Path.GetFullPath(path));

            check("写下的描述文件认得出存在", PCGSpecFile.Exists(path));

            var loaded = PCGSpecFile.TryLoad(path, Resolver);

            check("读完整个描述文件能编译成图",
                loaded.Ok && loaded.Graph != null && loaded.Graph.NodeCount > 0 &&
                loaded.Graph.Name == "温带山谷",
                loaded.Error ?? "ok");

            // 同一个描述，文件里读出来的图与直接编译出来的图必须完全一样：文件这一层不该改变任何东西。
            PCGSpecParser.TryParse(Full, out var direct, out _);
            var directGraph = PCGSpecCompiler.Compile(direct, Resolver).Graph;

            check("文件读出来的图与直接编译的图完全一致",
                SameGraph(loaded.Graph, directGraph));

            // 带 BOM 的描述是编辑器里另存为最常见的结果；不剥掉的话第一行指令会认不出来。
            string bom = System.IO.Path.Combine(folder, "bom.pcgspec");
            System.IO.File.WriteAllText(bom, "world 带BOM\nseed 3\nscatter grass", new UTF8Encoding(true));

            var bomResult = PCGSpecFile.TryLoad(bom, Resolver);

            check("带 BOM 的描述文件照样能解析（第一行指令不被 BOM 吃掉）",
                bomResult.Ok && bomResult.Graph.Name == "带BOM", bomResult.Error ?? "ok");

            // 文件不存在：要报"读不到"并带上路径，而不是抛异常或返回一张空图。
            string missing = System.IO.Path.Combine(folder, "nope.pcgspec");
            var missingResult = PCGSpecFile.TryLoad(missing, Resolver);

            check("文件不存在时报读不到并带上路径",
                !missingResult.Ok && !string.IsNullOrEmpty(missingResult.Error) &&
                missingResult.Error.Contains("读不到") && missingResult.Error.Contains("nope.pcgspec"),
                missingResult.Error);

            check("不存在的描述文件不再被认为存在", !PCGSpecFile.Exists(missing));

            // 描述写错了：错误要带**文件名**与**行号**，否则一路找到底才知道错在哪。
            string broken = System.IO.Path.Combine(folder, "broken.pcgspec");
            System.IO.File.WriteAllText(broken, "world 坏的\nseed 1\nplant trees", new UTF8Encoding(false));

            var brokenResult = PCGSpecFile.TryLoad(broken, Resolver);

            check("描述写错时报出文件名与行号",
                !brokenResult.Ok && brokenResult.Error != null &&
                brokenResult.Error.Contains("broken.pcgspec") && brokenResult.Error.Contains("第 3 行"),
                brokenResult.Error);

            // 没给名字：不该去读路径，直接说没给。
            var anonymous = PCGSpecFile.TryLoad("  ", Resolver);

            check("没给描述文件名时如实说没给",
                !anonymous.Ok && anonymous.Error != null && anonymous.Error.Contains("没有给"),
                anonymous.Error);

            try
            {
                System.IO.Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // 临时目录删不掉不影响结论。
            }
        }

        // ---- 真编译真运行 -------------------------------------------------------------------------------

        private static void EndToEndTests(CheckDelegate check)
        {
            PCGSpecParser.TryParse(Full, out var spec, out _);
            var compiled = PCGSpecCompiler.Compile(spec, Resolver);

            if (!compiled.Ok)
            {
                check("描述编译出来的图能过图编译器", false, compiled.Error);
                return;
            }

            // 图编译器自己会按注册表校验槽位的种类：种类错了这里会以 Error 出现，而不是悄悄用默认值。
            if (!PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                string first = program?.Diagnostics != null && program.Diagnostics.Count > 0
                    ? program.Diagnostics[0].Message
                    : "compile failed";

                check("描述编译出来的图能过图编译器", false, first);
                return;
            }

            int errors = 0;

            if (program.Diagnostics != null)
            {
                foreach (var diagnostic in program.Diagnostics)
                {
                    if (diagnostic.Severity == PCGCompileSeverity.Error)
                        errors++;
                }
            }

            check("描述编译出来的图没有编译期错误", errors == 0, errors.ToString());

            check("这张图被判定为会写场景（里面有 5 个生成器）",
                PCGGraphEffects.GraphWritesScene(compiled.Graph, null));

            // 只留一条散布链跑一遍：预览模式（DryRun）下生成器不落地，所以能在离屏拿到"候选点"的个数。
            bool ok = PCGSpecParser.TryParse(
                "world 跑一遍\nseed 20261008\nscatter forest per-block 4 density 0.6 model \"Assets/PCG/Pine.flax\"",
                out var runnable, out string error);

            check("最小可跑描述能解析", ok, error);

            if (!ok)
                return;

            var one = PCGSpecCompiler.Compile(runnable, Resolver);

            if (!one.Ok || !PCGGraphCompiler.TryCompile(one.Graph, out var oneProgram))
            {
                check("最小可跑描述能过图编译器", false, one.Error ?? "compile failed");
                return;
            }

            var context = new PCGExecutionContext
            {
                Seed = 20261008,
                GenerationBounds = new BoundingBox(new Vector3(-100.0f, -10.0f, -100.0f),
                                                   new Vector3(100.0f, 10.0f, 100.0f)),
                DryRun = true,
            };

            var executed = new PCGGraphExecutor(oneProgram).Execute(context);
            var output = executed.PrimaryOutput;

            check("描述生成的图跑起来真有产出（预览模式下不落地）",
                output != null && output.Count > 0, $"{output?.Count ?? 0} point(s)");

            check("这一跑没有节点报错",
                executed.Warnings == null ||
                !Array.Exists(executed.Warnings, w => w != null && w.Contains("threw")),
                executed.Warnings == null ? "none" : string.Join(" | ", executed.Warnings));

            // 解析不出模型的路径：编译要成功、要留下提醒、槽位留空——不能抛异常，也不能假装有模型。
            bool missingOk = PCGSpecParser.TryParse("world x\nscatter trees model \"Nope.flax\"", out var missing, out _);
            var warned = PCGSpecCompiler.Compile(missing, path => Guid.Empty);

            check("路径解析不出资产时：编译仍成功，但留下提醒且槽位为空",
                missingOk && warned.Ok && warned.Warnings.Count == 1 &&
                Slot(warned.Graph, IndexOf(warned.Graph, PCGSpawnStaticModelNode.Id, 0), "Model") is Guid empty && empty == Guid.Empty,
                warned.Warnings.Count > 0 ? warned.Warnings[0] : "没有提醒");

            // 没给模型的那一层：编译要成功（只出点、给别人当参照的层是合法写法），但必须在**编译期**点名。
            // 否则描述说了要长草、图照编，跑起来只在运行时报一句"没有指定模型"——"说了没长出来"最典型的静默失效。
            bool bareOk = PCGSpecParser.TryParse("world x\nscatter grass per-block 8", out var bare, out _);
            var bareResult = PCGSpecCompiler.Compile(bare, path => OtherId);

            check("散布层没给模型：编译成功但在编译期点名",
                bareOk && bareResult.Ok && bareResult.Warnings.Count == 1 &&
                bareResult.Warnings[0].Contains("grass") && bareResult.Warnings[0].Contains("没有给模型"),
                bareResult.Warnings.Count > 0 ? bareResult.Warnings[0] : "没有提醒");

            // 对照（同一夹具的正面）：同一层给上模型就一句提醒都不该有——否则上面那条分不清
            // "因为没给模型才点名"与"这一层总要被念叨一遍"。
            bool dressedOk = PCGSpecParser.TryParse(
                "world x\nscatter grass per-block 8 model \"Assets/PCG/Grass.flax\"", out var dressed, out _);
            var dressedResult = PCGSpecCompiler.Compile(dressed, path => OtherId);

            check("对照：同一层给上模型就一句提醒都没有",
                dressedOk && dressedResult.Ok && dressedResult.Warnings.Count == 0,
                string.Join(" | ", dressedResult.Warnings));
        }

        // ---- 夹具 --------------------------------------------------------------------------------------

        private static readonly Guid PineId = new Guid("11111111-2222-3333-4444-555555555555");
        private static readonly Guid OtherId = new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        private const string Full =
            "# 一片温带山谷\n" +
            "world 温带山谷\n" +
            "seed 20261008\n" +
            "region 2000 1600 height 800\n" +
            "biome cell 50 water 12 relief 200 region 1000 samples 4 hill-slope 8\n" +
            "scatter forest per-block 1.5 density 0.6 slope 15 90 model \"Assets/PCG/Pine Trees/Pine LOD0.flax\" model-scale 1.2 yaw on\n" +
            "scatter rocks points 40 density 0.4 model \"Assets/PCG/Rock.flax\"\n" +
            "scatter grass per-block 8.0 model \"Assets/PCG/Grass.flax\" yaw off\n" +
            "water cell 40 threshold 250 min-lake-cells 12 model \"Assets/PCG/Water.flax\"\n" +
            "village lots 12 rules \"X:0.5,0.5; Z:0.6,0.4\" cell 120 40 120 model \"Assets/PCG/House.flax\"\n" +
            "cave depth 400 segments 24 radius 60 tunnels 10 branch 0.25 turn 30 sides 12\n" +
            "road from -800 -600 to 900 700 cell 40 slope 20 spacing 120 model \"Assets/PCG/Road.flax\"\n";

        /// <summary>假的资产解析：路径里带 Pine 的给一个固定 id，别的给另一个——只要不是空，就算"有资产"。</summary>
        private static Guid Resolver(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Guid.Empty;

            return path.Contains("Pine") ? PineId : OtherId;
        }

        /// <summary>
        /// 解析一行之后，<paramref name="fieldName"/> 指向的字段是不是真的被写上了值；没过就说明卡在哪一步。
        /// </summary>
        /// <remarks>
        /// 这是选项表那条"字段名"契约的镜像：表里写了 Field，就该有对应的字段被写上。声明了却没人写的选项
        /// 会让描述"看起来生效、实际没生效"，而且解析一律成功——只有把值掏出来看才能发现。
        /// </remarks>
        private static string FieldLandedReason(string directive, string line, string fieldName)
        {
            if (!PCGSpecParser.TryParse("world probe\n" + line, out var spec, out string error))
                return $"解析失败：{error}";

            object target;

            switch (directive)
            {
            case "region": target = spec.Region; break;
            case "biome": target = spec.Biome; break;
            case "terrain": target = spec.Terrain; break;
            case "scatter": target = spec.Scatters != null && spec.Scatters.Count > 0 ? spec.Scatters[0] : null; break;
            case "water": target = spec.Water; break;
            case "village": target = spec.Settlements != null && spec.Settlements.Count > 0 ? spec.Settlements[0] : null; break;
            case "city": target = spec.Cities != null && spec.Cities.Count > 0 ? spec.Cities[0] : null; break;
            case "cave": target = spec.Cave; break;
            case "road": target = spec.Roads != null && spec.Roads.Count > 0 ? spec.Roads[0] : null; break;
            default: return $"没有 '{directive}' 的规格对象";
            }

            if (target == null)
                return $"'{directive}' 的规格对象是 null";

            var field = target.GetType().GetField(fieldName);

            if (field == null)
                return $"{target.GetType().Name} 里没有字段 {fieldName}";

            return field.GetValue(target) == null ? $"{fieldName} 解析之后仍是 null" : null;
        }

        private static int Count(List<string> values, string value)
        {
            int total = 0;

            for (int i = 0; i < values.Count; i++)
                if (values[i] == value)
                    total++;

            return total;
        }

        private static int IndexOf(PCGGraph graph, string typeId, int ordinal)
        {
            int seen = 0;

            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (graph.Nodes[i].TypeId != typeId)
                    continue;

                if (seen == ordinal)
                    return i;

                seen++;
            }

            return -1;
        }

        /// <summary>按名字读一个槽位的值——和编译器走同一条路，所以"名字写错"在这里也会暴露。</summary>
        private static object Slot(PCGGraph graph, int node, string name)
        {
            if (graph == null || node < 0 || node >= graph.NodeCount)
                return null;

            if (!PCGNodeRegistry.TryGet(graph.Nodes[node].TypeId, out var info) || info.Parameters == null)
                return null;

            for (int i = 0; i < info.Parameters.Length; i++)
                if (info.Parameters[i].Name == name)
                    return graph.Nodes[node].Values[i];

            return null;
        }

        private static string PinName(PCGGraph graph, int node, int pin, bool output)
        {
            var instance = PCGNodeRegistry.CreateNode(graph.Nodes[node], node, out _);

            if (instance == null)
                return "?";

            var pins = output ? instance.OutputPins : instance.InputPins;

            return pins != null && pin >= 0 && pin < pins.Length ? pins[pin].Name : "?";
        }

        private static bool SameGraph(PCGGraph a, PCGGraph b)
        {
            if (a == null || b == null || a.NodeCount != b.NodeCount || a.EdgeCount != b.EdgeCount ||
                a.Name != b.Name || a.Settings.DefaultSeed != b.Settings.DefaultSeed)
            {
                return false;
            }

            for (int i = 0; i < a.NodeCount; i++)
            {
                if (a.Nodes[i].TypeId != b.Nodes[i].TypeId || a.Nodes[i].Comment != b.Nodes[i].Comment)
                    return false;

                var left = a.Nodes[i].Values;
                var right = b.Nodes[i].Values;

                if ((left == null) != (right == null))
                    return false;

                for (int v = 0; left != null && v < left.Length; v++)
                {
                    if (!Equals(left[v], right[v]))
                        return false;
                }
            }

            for (int i = 0; i < a.EdgeCount; i++)
            {
                if (a.Edges[i].OutputNodeIndex != b.Edges[i].OutputNodeIndex ||
                    a.Edges[i].OutputPinIndex != b.Edges[i].OutputPinIndex ||
                    a.Edges[i].InputNodeIndex != b.Edges[i].InputNodeIndex ||
                    a.Edges[i].InputPinIndex != b.Edges[i].InputPinIndex)
                {
                    return false;
                }
            }

            return true;
        }

        private static float V(float? value) => value ?? float.NaN;

        /// <summary>解析与规划用的平地高度场（这个文件里只需要"有地形可问"）。</summary>
        private static float? FlatHeight(float x, float z) => 0.0f;
        private static int I(int? value) => value ?? int.MinValue;

        private static float AsFloat(object value) => value == null ? float.NaN : Convert.ToSingle(value);

        private static int AsInt(object value) => value == null ? int.MinValue : Convert.ToInt32(value);

        private static string Describe(object[] values)
        {
            if (values == null)
                return "no values";

            var parts = new List<string>(values.Length);

            foreach (var value in values)
                parts.Add(value == null ? "null" : $"{value.GetType().Name}:{value}");

            return string.Join(" | ", parts);
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) =>
            !float.IsNaN(a) && Math.Abs(a - b) <= tolerance;
    }
}

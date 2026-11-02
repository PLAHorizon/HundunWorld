// PCG 插件 - 把设定描述编译成一张图
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.39。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Nodes;
using PCG.Runtime;

namespace PCG.Graph
{
    /// <summary>编译一段设定描述的结果：要么一张图，要么一个带位置的错误，外加一串"写了但没完全落地"的提醒。</summary>
    public sealed class PCGSpecCompileResult
    {
        /// <summary>编译出来的图；失败时为 null。</summary>
        public PCGGraph Graph;

        /// <summary>失败原因；成功时为 null。</summary>
        public string Error;

        /// <summary>能跑但值得看一眼的事（例如模型路径没解析出资产）。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>成功为 true。</summary>
        public bool Ok => string.IsNullOrEmpty(Error) && Graph != null;

        /// <inheritdoc />
        public override string ToString() =>
            Ok ? $"{Graph.Name}: {Graph.NodeCount} node(s), {Graph.EdgeCount} edge(s)" +
                 (Warnings.Count > 0 ? $", {Warnings.Count} warning(s)" : string.Empty)
               : $"failed: {Error}";
    }

    /// <summary>
    /// 把 <see cref="PCGWorldSpec"/> 编译成 <see cref="PCGGraph"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两条约定让它不容易腐坏。**第一，槽位永远按名字写**：注册表里每个类型都带一份具名参数表，这里用
    /// <c>Set(node, "Cell Size", 50f)</c> 这种方式填值，找不到那个名字就直接报错。按下标写的话，节点加一个参数
    /// 就会把后面所有值整体挪位，而图照跑、只是参数全错——这类错静默且致命。**第二，引脚也按名字连**：节点实例
    /// 会报出自己的引脚表，连线时查名字拿下标，不写死 0 和 0。
    /// </para>
    /// <para>
    /// 资产路径解析**由宿主注入**（<c>Func&lt;string, Guid&gt;</c>）：纯离屏环境下没有资产数据库，而这个类要在
    /// 离线断言里被反复调用，所以它不能自己去查引擎。解析不出来时记一条提醒并把槽位留空，让生成器在运行期
    /// 如实报"没有指定模型"，而不是在这里抛异常。
    /// </para>
    /// </remarks>
    public static class PCGSpecCompiler
    {
        /// <summary>编译一段设定描述。</summary>
        /// <param name="spec">已经解析好的描述。</param>
        /// <param name="modelResolver">资产路径 → 资产 id；为 null 时所有模型路径都会被当成解析不出来。</param>
        /// <returns>编译结果。</returns>
        public static PCGSpecCompileResult Compile(PCGWorldSpec spec, Func<string, Guid> modelResolver = null)
        {
            var result = new PCGSpecCompileResult();

            if (spec == null)
            {
                result.Error = "设定描述为空。";
                return result;
            }

            // 编译要按名字查槽位，所以注册表必须先就位。这一步是幂等的，重复调用只覆盖同一张表。
            PCGNodeRegistration.RegisterAll();

            var builder = new Builder();

            try
            {
                int biomeNode = -1;

                if (spec.HasBiome)
                    biomeNode = AddBiome(builder, spec.Biome);

                // 位置关系（`near` / `away`）要等**所有**特征都建完才接线：`scatter 芦苇 near water` 很可能
                // 写在 `water` 之前（"湖边的芦苇"当然是先说芦苇），而那时参照的链还不存在。所以这里先记下表，
                // 末尾统一插节点、改连线；顺带也就允许了前向引用。
                var sources = new Dictionary<string, int>(StringComparer.Ordinal);
                var relations = new List<ScatterRelation>();
                var scatterTails = new int[spec.Scatters.Count];
                var scatterSpawners = new int[spec.Scatters.Count];

                // 散布层排在固定行上，一行一条链，免得所有节点叠在原点。
                for (int layer = 0; layer < spec.Scatters.Count; layer++)
                {
                    var scatter = spec.Scatters[layer];
                    int row = 1 + layer;

                    // 这一层的链从第 1 列开始往右排，每加一段就占一列。写死列号的话，两个都写了的层
                    // （例如既按地貌筛又按坡度筛）会把两个框摆在同一个坐标上——图照跑，只是画布上看着像一个。
                    int nextColumn = 1;
                    int tail;

                    if (!string.IsNullOrEmpty(scatter.BiomeNames))
                    {
                        // 按地貌撒点：点源换成**地貌底图**，中间插一道按 Biome 列过滤的关卡。
                        // 这是"判出地貌"与"按地貌生成"之间那一步——没有它，描述只能说"坡度多大以内"，
                        // 说不出"只在丘陵和山地上长树"。
                        if (!TryResolveBiomes(scatter.BiomeNames, out int biomeMin, out int biomeMax, out string biomeError))
                        {
                            result.Error = $"第 {scatter.BiomeLine} 行：散布层 {scatter.Name} 的 biome 过滤：{biomeError}";
                            return result;
                        }

                        if (biomeNode < 0)
                        {
                            // 描述里没写 `biome`，但这一层要按地貌撒点：补一个默认的地貌底图，
                            // 并说清楚是自动补的、参数用的是默认值。
                            biomeNode = AddBiome(builder, null);
                            result.Warnings.Add("有一层按地貌撒点，但描述里没写 `biome`：已按默认参数补了一个地貌底图，" +
                                                "想调判据就补一段 `biome …`。");
                        }

                        int filter = builder.Add(PCGAttributeFilterNode.Id, nextColumn++, row);

                        builder.Set(filter, "Attribute", PCGBiomeNode.BiomeColumn);
                        builder.Set(filter, "Min", (float)biomeMin);
                        builder.Set(filter, "Max", (float)biomeMax);
                        builder.Note(filter, $"散布层 {scatter.Name}：只取地貌 {scatter.BiomeNames}");
                        builder.Wire(biomeNode, filter);
                        tail = filter;
                    }
                    else
                    {
                        int source = builder.Add(PCGVolumeSamplerNode.Id, 0, row);
                        builder.Note(source, $"散布层 {scatter.Name}");

                        // 点数与面积密度二选一；两个都没写就按每块一个点，至少撒得出东西来。
                        if (scatter.Points.HasValue)
                            Set(builder, source, "Point Count", scatter.Points);
                        else
                            Set(builder, source, "Points Per Block", scatter.PointsPerBlock ?? 1.0f);

                        Set(builder, source, "Density", scatter.Density);
                        Set(builder, source, "Scale", scatter.Scale);
                        Set(builder, source, "Seed Offset", layer);
                        Set(builder, source, "Random Rotation", false);
                        tail = source;
                    }

                    // 落地：把这一层的点压到地形表面。
                    // 位置必须在这里——**在坡度筛选之前**。坡度是从表面法线算的，不给它一个贴地的法线，
                    // `slope` 那道筛选在起伏地形上看到的仍是一片平地（写 15..90 会把树全筛掉，
                    // 而屏幕上只表现为"这层什么都没长"，看不出原因）。这也是决定 D 的另一半：
                    // 光把地形写出来不够，产出还得真的落在它上面，否则树会浮在半空或埋进山脊。
                    // 水面 / 道路 / 洞穴不走这里：它们的 Y 各有含义（水位、沿线高度、地下隧道），压平就毁了。
                    int ground = builder.Add(PCGProjectPointsNode.Id, nextColumn++, row);
                    builder.Set(ground, "Align To Normal", true);
                    builder.Note(ground, $"散布层 {scatter.Name}：落到地形表面");
                    builder.Wire(tail, ground);
                    tail = ground;

                    // 区域过滤（`within` / `outside` / `polygon` / `except` / `except-polygon`）：
                    // 与聚居地、城市**同一段代码**——同一个关键词在三处筛掉不同的东西，是没法解释给作者听的。
                    // 它管"长在哪"，与"长什么"（dispersion/density）、"挨着谁"（near/away/fade）互不干扰，所以可以叠着写。
                    var (regionTail, regionColumn) = AddRegionFilters(
                        builder, scatter, tail, nextColumn, row, $"散布层 {scatter.Name}");

                    tail = regionTail;
                    nextColumn = regionColumn;

                    // 抖动：按地貌撒点时点源是**等距的格心**，不抖的话林子会长成整齐的果园。
                    // 这条为空时给按地貌过滤的层补一个"半个格子"的默认值；写 0 才是明确不要抖动。
                    float jitter = scatter.Jitter ?? (scatter.BiomeNames != null ? DefaultBiomeHalfCell() : 0.0f);

                    if (jitter > 0.0f)
                    {
                        int offset = builder.Add(PCGRandomOffsetNode.Id, nextColumn++, row);

                        builder.Set(offset, "Min Offset", new Vector3(-jitter, 0.0f, -jitter));
                        builder.Set(offset, "Max Offset", new Vector3(jitter, 0.0f, jitter));
                        builder.Set(offset, "Seed Offset", layer);
                        builder.Wire(tail, offset);
                        tail = offset;
                    }

                    if (scatter.MinSlope.HasValue || scatter.MaxSlope.HasValue)
                    {
                        int slope = builder.Add(PCGSlopeFilterNode.Id, nextColumn++, row);

                        Set(builder, slope, "Min Slope Degrees", scatter.MinSlope);
                        Set(builder, slope, "Max Slope Degrees", scatter.MaxSlope);
                        builder.Wire(tail, slope);
                        tail = slope;
                    }

                    bool procedural = !string.IsNullOrWhiteSpace(scatter.GeometryShape);
                    string form = PCGSpecParser.NormalizeOutputForm(scatter.OutputForm);
                    bool instanced = form == "instanced";
                    bool merged = form == "merged";

                    if (procedural)
                    {
                        // 程序化几何：先把每个点变成一个建出来的网格（`Geometry Primitives`），再交给几何生成器。
                        // 尺寸按**世界单位**给（`Use Point Scale` 固定 false）——用点缩放那一档会让"3 米粗的柱子"
                        // 随点的缩放飞掉，而描述里的数字本意就是世界单位。
                        int primitives = builder.Add(PCGGeometryNode.Id, nextColumn++, row);

                        builder.Set(primitives, "Shape", PCGSpecParser.GeometryShapeIndex(scatter.GeometryShape));
                        Set(builder, primitives, "Segments", scatter.GeometrySegments);
                        Set(builder, primitives, "Steps", scatter.GeometrySteps);
                        builder.Set(primitives, "Cell Size", scatter.GeometrySize.Value);
                        builder.Set(primitives, "Use Point Scale", false);
                        builder.Note(primitives, $"散布层 {scatter.Name}：{scatter.GeometryShape} 程序化几何" +
                                                 $" {scatter.GeometrySize.Value}");
                        builder.Wire(tail, primitives);
                        tail = primitives;

                        // `clip`：把每个形状裁进一个**凸区域**（盒/圆柱）——只保留落在里面的部分。
                        // 排在 `cut` **前面**：先"裁进区域"再"削一刀"；反过来先削掉的那截就再也裁不回来了。
                        if (!string.IsNullOrWhiteSpace(scatter.ClipShape))
                        {
                            int clip = builder.Add(PCGClipToConvexNode.Id, nextColumn++, row);
                            var clipSize = scatter.ClipSize ?? new Vector3(100.0f, 100.0f, 100.0f);

                            builder.Set(clip, "Shape", scatter.ClipShape == "cylinder"
                                ? PCGClipToConvexNode.CylinderShape
                                : PCGClipToConvexNode.BoxShape);
                            builder.Set(clip, "Center", Vector3.Zero);
                            builder.Set(clip, "Size", clipSize);
                            Set(builder, clip, "Segments", scatter.ClipSegments);
                            builder.Note(clip, $"散布层 {scatter.Name}：裁进 {scatter.ClipShape} {clipSize}");
                            builder.Wire(tail, clip);
                            tail = clip;
                        }

                        // `cut`：把每个形状按平面削一刀（切口由节点自动封盖）。刀口在**形状自己的坐标系**里，
                        // 沿法线推 offset 这么远——形状都建在自己原点，所以"从中间横切"是 `cut 0 1 0 0`。
                        if (scatter.GeometryCutNormal.HasValue)
                        {
                            var normal = scatter.GeometryCutNormal.Value;
                            var unit = Vector3.Normalize(normal);

                            int cut = builder.Add(PCGCutNode.Id, nextColumn++, row);

                            builder.Set(cut, "Plane Point", unit * (scatter.GeometryCutOffset ?? 0.0f));
                            builder.Set(cut, "Plane Normal", normal);
                            builder.Set(cut, "Invert", false);
                            builder.Note(cut, $"散布层 {scatter.Name}：按平面 {normal} 偏移 " +
                                              $"{scatter.GeometryCutOffset ?? 0.0f} 削一刀");
                            builder.Wire(tail, cut);
                            tail = cut;
                        }
                    }

                    // 三种输出形态各自落到哪一类生成器：`instanced` 走**实例化**那两条路（实例不是 Actor），
                    // `merged` 只有程序化几何有（解析期已拦住别的写法），其余是逐块建 Actor。
                    int spawner = builder.Add(
                        procedural ? PCGSpawnGeometryNode.Id
                                   : instanced ? PCGSpawnFoliageNode.Id
                                   : PCGSpawnStaticModelNode.Id, nextColumn, row);

                    // 密度既是点的密度也是生成概率：阈值取同一个数，生成器才会真按点密度抽一次。
                    Set(builder, spawner, "Density Threshold", scatter.Density ?? 1.0f);

                    if (procedural)
                    {
                        // 几何生成器没有"模型/缩放/偏航"这三档：形状已经烘进网格、朝向由几何节点定。
                        // 作者在描述里写下的 `model-scale` / `yaw` 到这一档就是**没有对象**——留着不生效、
                        // 又不点名，就是"说了没长出来"那一族：编译通过、跑起来尺寸与朝向都不认。
                        var dropped = new List<string>();

                        if (scatter.ModelScale.HasValue)
                            dropped.Add("model-scale");

                        if (scatter.RandomYaw.HasValue)
                            dropped.Add("yaw");

                        if (dropped.Count > 0)
                        {
                            result.Warnings.Add($"第 {scatter.Line} 行：散布层 {scatter.Name} 是程序化几何，" +
                                                $"{string.Join(" 与 ", dropped)} 不生效" +
                                                "（尺寸只认 `geometry-size`、按世界单位给；朝向由几何节点定）。");
                        }

                        builder.Set(spawner, "Merge Into One Actor", merged);
                        builder.Set(spawner, "To Foliage Host", instanced);
                        builder.Set(spawner, "Label", scatter.Name);
                        builder.Wire(tail, spawner);
                    }
                    else
                    {
                        Set(builder, spawner, "Scale Multiplier", scatter.ModelScale);
                        Set(builder, spawner, "Random Yaw", scatter.RandomYaw);

                        if (!string.IsNullOrEmpty(scatter.ModelPath))
                        {
                            Set(builder, spawner, "Model", Resolve(modelResolver, scatter.ModelPath, result));
                        }
                        else
                        {
                            // 没给模型的那一层，图上照样挂着一个生成器，而它**永远长不出东西**：运行期只会
                            // 报一句"没有指定模型"然后原样放行。描述说了要长草、编译通过、跑起来什么都不长，
                            // 这种"说了没长出来"正是本插件最容易骗过作者的一类静默失效——所以在**编译期**
                            // 就点名它（不判错：只出点、给别的层当参照的层是合法的写法）。
                            result.Warnings.Add($"第 {scatter.Line} 行：散布层 {scatter.Name} 没有给模型，" +
                                                "这一层只出点、不长东西（要它真长出东西来就写一段 " +
                                                "`model <资产路径>`，或者写 `geometry <形状> geometry-size <x> <y> <z>`）。");
                        }

                        builder.Wire(tail, spawner);
                    }

                    // 这一层的"尾巴"与生成器都记下来：位置关系要插在两者之间。名字也登记成可被参照的源，
                    // 于是"挨着石头长树"（`scatter trees near rocks …`）这种层间关系也能写。
                    scatterTails[layer] = tail;
                    scatterSpawners[layer] = spawner;
                    sources[scatter.Name] = tail;

                    if (scatter.NearSource != null)
                        relations.Add(new ScatterRelation(layer, scatter.NearSource, scatter.NearRadius.Value,
                            RelationKind.Near, scatter.NearLine));
                    else if (scatter.AwaySource != null)
                        relations.Add(new ScatterRelation(layer, scatter.AwaySource, scatter.AwayRadius.Value,
                            RelationKind.Away, scatter.AwayLine));
                    else if (scatter.FadeSource != null)
                        relations.Add(new ScatterRelation(layer, scatter.FadeSource, scatter.FadeRadius.Value,
                            RelationKind.Fade, scatter.FadeLine));
                }

                // 往下每条特征各占一行，行号用一个递增的计数器走——写成"1 + 散布层数 + (有没有水 ? 1 : 0) + …"
                // 那种嵌套表达式，每加一个可重复的特征就要把前面每一处都改一遍，而且改错只会让节点叠在一起。
                int nextRow = 1 + spec.Scatters.Count;

                if (spec.HasWater)
                {
                    int water = builder.Add(PCGHydrologyNode.Id, 0, nextRow);

                    sources["water"] = water;

                    Set(builder, water, "Cell Size", spec.Water.CellSize);
                    Set(builder, water, "River Threshold", spec.Water.RiverThreshold);
                    Set(builder, water, "Min Lake Cells", spec.Water.MinLakeCells);
                    Set(builder, water, "Water Offset", spec.Water.WaterOffset);

                    // 区域走**产生器输入**，不接 `AddRegionFilters`：河与湖的产出是连续体，事后删点会断成两截。
                    // 节点把世界矩形换成"发源格在不在框里"，湖按湖心判（扫描顺序不该决定一片湖的有无）。
                    if (spec.Water.AreaMinX.HasValue)
                    {
                        builder.Set(water, "Region Min",
                            new Vector3(spec.Water.AreaMinX.Value, 0.0f, spec.Water.AreaMinZ.Value));
                        builder.Set(water, "Region Max",
                            new Vector3(spec.Water.AreaMaxX.Value, 0.0f, spec.Water.AreaMaxZ.Value));
                        builder.Set(water, "Region Invert", spec.Water.AreaInvert);
                        builder.Note(water, $"水系只在 x[{spec.Water.AreaMinX.Value:F0},{spec.Water.AreaMaxX.Value:F0}] " +
                                            $"z[{spec.Water.AreaMinZ.Value:F0},{spec.Water.AreaMaxZ.Value:F0}] 这片里发源");
                    }

                    AddSpawner(builder, water, 1, nextRow, spec.Water.ModelPath, spec.Water.ModelScale, null,
                        "水面", modelResolver, result);

                    nextRow++;
                }

                // 聚居地可以写多个：大世界里的村子不止一个。
                for (int i = 0; i < spec.Settlements.Count; i++)
                {
                    var settlement = spec.Settlements[i];

                    // 写了 `at` 就先摆一个**字面点**当种子：切分语法按它的位置切，于是村子落在写下的地方。
                    int villageSeed = AddSeedAt(builder, settlement.AtX, settlement.AtZ, 0, nextRow,
                        $"聚居地 {i + 1} 的位置");
                    int villageColumn = villageSeed >= 0 ? 1 : 0;

                    if (!AddTurn(builder, settlement.RotateDegrees, settlement.RotateLine, ref villageSeed,
                            ref villageColumn, nextRow, $"village", result))
                        return result;

                    int grammar = builder.Add(PCGSplitGrammarNode.Id, villageColumn, nextRow);

                    if (villageSeed >= 0)
                        builder.Wire(villageSeed, grammar);

                    // 一个村子时用 `village`，多个时用 `village1`/`village2`…；裸名留给"唯一"的那种情况，
                    // 否则"near village"到底指哪个村就只有写的人知道。
                    sources[$"village{i + 1}"] = grammar;

                    if (spec.Settlements.Count == 1)
                        sources["village"] = grammar;

                    Set(builder, grammar, "Rules", settlement.Rules);
                    Set(builder, grammar, "Max Depth", settlement.MaxDepth);
                    Set(builder, grammar, "Min Cell Size", settlement.MinCellSize);
                    Set(builder, grammar, "Cell Size", settlement.CellSize);
                    builder.Note(grammar, settlement.Lots.HasValue
                        ? $"聚居地 {i + 1}：{settlement.Lots} 个地块（上游点数决定实际个数）"
                        : $"聚居地 {i + 1}");

                    // 村庄 / 城市的产出同样要落地：语法只写出"地块在水平面上的位置"（Y 恒为 0），
                    // 不投影的话整座城会埋在抬高过的地形底下。水/路/洞穴同理不走这里。
                    int villageGround = builder.Add(PCGProjectPointsNode.Id, villageColumn + 1, nextRow);
                    builder.Set(villageGround, "Align To Normal", true);
                    builder.Note(villageGround, "聚居地：落到地形表面");
                    builder.Wire(grammar, villageGround);

                    string settlementForm = settlement.OutputForm?.Trim().ToLowerInvariant();
                    bool settlementMerged = settlementForm == "merged";
                    bool settlementInstanced = settlementForm == "instanced";

                    // 挂模型的村子合不成一个 Actor：当场报错，不能把这句读成"没写"。
                    if ((settlementMerged || settlementInstanced) &&
                        string.IsNullOrWhiteSpace(settlement.GeometryShape))
                    {
                        result.Error = $"聚居地 {i + 1}：写了 output {settlementForm}，但这一层没写 geometry；" +
                                       "`merged` / `instanced` 只对程序化几何那种写法有意义";
                        return result;
                    }

                    // 区域约束与城市同一段代码：不写的话村庄的地块会直接铺过湖面，
                    // 而"城建在水上/村压着田"是程序化聚落最扎眼的一种坏相。
                    var (villageTail, villageColumnNext) = AddRegionFilters(
                        builder, settlement, villageGround, villageColumn + 2, nextRow, $"聚居地 {i + 1}");

                    AddSpawner(builder, villageTail, villageColumnNext, nextRow, settlement.ModelPath,
                        settlement.ModelScale, settlement.RandomYaw, $"房屋 {i + 1}", modelResolver, result,
                        settlement.GeometryShape, settlement.GeometrySize, settlement.GeometrySegments,
                        settlementMerged, settlementInstanced);

                    nextRow++;
                }

                // 城市：与聚居地同一条"切分 → 生成"的骨架，但中间那段换成了**符号产生式**。
                // 于是地块之间有了种类之别（城区/街区/房子各是各的层级），这是"城市"与"村庄"的实际差别。
                for (int i = 0; i < spec.Cities.Count; i++)
                {
                    var city = spec.Cities[i];
                    int citySeed = AddSeedAt(builder, city.AtX, city.AtZ, 0, nextRow, $"城区 {i + 1} 的位置");
                    int cityColumn = citySeed >= 0 ? 1 : 0;

                    if (!AddTurn(builder, city.RotateDegrees, city.RotateLine, ref citySeed, ref cityColumn,
                            nextRow, "city", result))
                        return result;

                    int grammar = builder.Add(PCGGrammarNode.Id, cityColumn, nextRow);

                    if (citySeed >= 0)
                        builder.Wire(citySeed, grammar);

                    sources[$"city{i + 1}"] = grammar;

                    if (spec.Cities.Count == 1)
                        sources["city"] = grammar;

                    Set(builder, grammar, "Productions", city.Productions);
                    Set(builder, grammar, "Root", city.Root);
                    Set(builder, grammar, "Max Depth", city.MaxDepth);
                    Set(builder, grammar, "Min Cell Size", city.MinCellSize);
                    Set(builder, grammar, "Cell Size", city.CellSize);

                    int cityGround = builder.Add(PCGProjectPointsNode.Id, cityColumn + 1, nextRow);
                    builder.Set(cityGround, "Align To Normal", true);
                    builder.Note(cityGround, "城市：落到地形表面");
                    builder.Wire(grammar, cityGround);

                    // 区域约束：城市与村庄走同一段筛子（同一对节点、同一个判据），
                    // 两条产出消费者（按符号分派 / 普通生成器）都接在**筛完的那一端**——
                    // 只接一条的话，写了 geometry-map 的城会安静地忽略它的 except。
                    var (cityTail, cityColumnNext) = AddRegionFilters(
                        builder, city, cityGround, cityColumn + 2, nextRow, $"城区 {i + 1}");

                    string cityForm = city.OutputForm?.Trim().ToLowerInvariant();
                    bool cityMerged = cityForm == "merged";
                    bool cityInstanced = cityForm == "instanced";

                    // 写了 output 却既没 geometry 也没 geometry-map ⇒ 这一层挂的是模型，模型合不成一个 Actor：
                    // 当场报错，不能把这句话读成"没写"。
                    if ((cityMerged || cityInstanced) && string.IsNullOrWhiteSpace(city.GeometryShape) &&
                        string.IsNullOrWhiteSpace(city.SymbolShapes))
                    {
                        result.Error = $"城区 {i + 1}：写了 output {cityForm}，但这一层不是程序化几何" +
                                       "（既没写 geometry 也没写 geometry-map）；`output merged` / `instanced` " +
                                       "只对 geometry 那种写法有意义";
                        return result;
                    }

                    if (!string.IsNullOrWhiteSpace(city.SymbolShapes))
                    {
                        // 按符号分派形状：一整座城市里不同种类的叶子各长各的（房是房、空地是草坪），
                        // 于是这一层不再需要借同一个图元。
                        if (AddSymbolShapeSpawners(builder, cityTail, cityColumnNext, nextRow,
                                city, $"城区 {i + 1}", result) < 0)
                            return result;
                    }
                    else
                    {
                        AddSpawner(builder, cityTail, cityColumnNext, nextRow, city.ModelPath, city.ModelScale,
                            city.RandomYaw, $"城区 {i + 1}", modelResolver, result,
                            city.GeometryShape, city.GeometrySize, city.GeometrySegments,
                            cityMerged, cityInstanced);
                    }

                    nextRow++;
                }

                if (spec.HasCave)
                {
                    // 洞口也可以写死位置：洞穴从种子点往下挖，于是"洞口在这儿"是说得出、看得见的。
                    int caveSeed = AddSeedAt(builder, spec.Cave.AtX, spec.Cave.AtZ, 0, nextRow, "洞口的位置");
                    int cave = builder.Add(PCGCaveNode.Id, caveSeed >= 0 ? 1 : 0, nextRow);

                    if (caveSeed >= 0)
                        builder.Wire(caveSeed, cave);

                    sources["cave"] = cave;

                    Set(builder, cave, "Max Depth", spec.Cave.Depth);
                    Set(builder, cave, "Segments", spec.Cave.Segments);
                    Set(builder, cave, "Radius", spec.Cave.Radius);
                    Set(builder, cave, "Max Tunnels", spec.Cave.MaxTunnels);
                    Set(builder, cave, "Branch Chance", spec.Cave.BranchChance);
                    Set(builder, cave, "Max Turn", spec.Cave.MaxTurn);
                    Set(builder, cave, "Tube Sides", spec.Cave.TubeSides);

                    // 区域走**产生器输入**，不接 `AddRegionFilters`：洞穴的点是洞道截面序列，
                    // 事后删点等于在管壁上开洞。节点会在挖到边界前把步长截短（落点与方向都不搬）。
                    if (spec.Cave.AreaMinX.HasValue)
                    {
                        builder.Set(cave, "Region Min",
                            new Vector3(spec.Cave.AreaMinX.Value, 0.0f, spec.Cave.AreaMinZ.Value));
                        builder.Set(cave, "Region Max",
                            new Vector3(spec.Cave.AreaMaxX.Value, 0.0f, spec.Cave.AreaMaxZ.Value));
                        builder.Note(cave, $"洞穴只在 x[{spec.Cave.AreaMinX.Value:F0},{spec.Cave.AreaMaxX.Value:F0}] " +
                                           $"z[{spec.Cave.AreaMinZ.Value:F0},{spec.Cave.AreaMaxZ.Value:F0}] 这片里挖");
                    }

                    // 洞穴自己就出网格，所以下游是几何生成器而不是静态模型生成器：不需要指模型资产。
                    int materialise = builder.Add(PCGSpawnGeometryNode.Id, caveSeed >= 0 ? 2 : 1, nextRow);
                    builder.Note(materialise, "洞壁");
                    builder.Wire(cave, materialise);

                    nextRow++;
                }

                // 路：途经点（起点、若干 via、终点）喂给 `Point List`，再由 `Path` 在地形上找一条代价最小的走法。
                // 这一条链正是"字面点"这个节点的用处——没有它，"从这里修到那里、并且必须绕经某处"表达不出来。
                int roadRow = nextRow;

                for (int i = 0; i < spec.Roads.Count; i++)
                {
                    var road = spec.Roads[i];
                    var waypoints = road.Waypoints();

                    if (waypoints.Count < 2)
                    {
                        // 解析器已经拦过缺端点，这里是防线：真放进图里只会得到一条退化成一个点的"路"。
                        result.Error = $"路 {i + 1} 只有 {waypoints.Count} 个途经点，至少要起点与终点两个。";
                        return result;
                    }

                    if (waypoints.Count > PCGPointListNode.SlotCount)
                    {
                        result.Error = $"路 {i + 1} 有 {waypoints.Count} 个途经点，而字面点节点只有 " +
                                       $"{PCGPointListNode.SlotCount} 个槽位。";
                        return result;
                    }

                    int row = roadRow + i;

                    int ends = builder.Add(PCGPointListNode.Id, 0, row);
                    builder.Note(ends, $"路 {i + 1} 的途经点（{waypoints.Count} 个）");
                    builder.Set(ends, "Count", waypoints.Count);

                    for (int w = 0; w < waypoints.Count; w++)
                    {
                        builder.Set(ends, $"Point {w + 1}", new Vector3(waypoints[w].X, 0.0f, waypoints[w].Y));
                    }

                    int path = builder.Add(PCGPathNode.Id, 1, row);

                    sources[$"road{i + 1}"] = path;

                    if (spec.Roads.Count == 1)
                        sources["road"] = path;

                    Set(builder, path, "Cell Size", road.CellSize);
                    Set(builder, path, "Slope Limit", road.SlopeLimit);
                    Set(builder, path, "Max Detour", road.MaxDetour);
                    Set(builder, path, "Sample Spacing", road.SampleSpacing);
                    builder.Wire(ends, path);

                    // 路的区域并进**可通行性**（不是事后删点，也不是搬落点）：绕开或限定都由代价搜索自己决定，
                    // 绕不过去时它自己报"每条路都被挡住"——那才是诚实的失败。
                    if (road.AreaMinX.HasValue && road.AreaMaxX.HasValue &&
                        road.AreaMinZ.HasValue && road.AreaMaxZ.HasValue)
                    {
                        builder.Set(path, "Region Min",
                            new Vector3(road.AreaMinX.Value, 0.0f, road.AreaMinZ.Value));
                        builder.Set(path, "Region Max",
                            new Vector3(road.AreaMaxX.Value, 0.0f, road.AreaMaxZ.Value));
                        builder.Set(path, "Region Invert", road.AreaInvert);
                        builder.Note(path, $"路{(road.AreaInvert ? "不许经过" : "只准走")} " +
                                           $"x[{road.AreaMinX.Value:F0},{road.AreaMaxX.Value:F0}] " +
                                           $"z[{road.AreaMinZ.Value:F0},{road.AreaMaxZ.Value:F0}]");
                    }

                    AddSpawner(builder, path, 2, row, road.ModelPath, road.ModelScale, road.RandomYaw,
                        "路面", modelResolver, result,
                        road.GeometryShape, road.GeometrySize, road.GeometrySegments);
                }

                // 所有特征都建好了，这时才接位置关系——于是 `near` 可以指向写在它下面的特征。
                if (!WireRelations(builder, spec, relations, sources, scatterTails, scatterSpawners, result))
                    return result;
            }
            catch (SpecCompileError failure)
            {
                result.Error = failure.Message;
                result.Graph = null;
                return result;
            }

            var graph = new PCGGraph
            {
                Name = spec.Name,
                Nodes = builder.Nodes.ToArray(),
                Edges = builder.Edges.ToArray(),
                Settings = new PCGGraphSettings { DefaultSeed = spec.Seed, Terrain = ToTerrainRecipe(spec.Terrain) },
            };

            result.Graph = graph;
            return result;
        }

        /// <summary>描述层的地形 → 图设置里的配方；没写 `terrain` 时给 null（那张图不造地形）。</summary>
        private static PCGTerrainRecipe ToTerrainRecipe(PCGSpecTerrain terrain)
        {
            if (terrain == null)
                return null;

            return new PCGTerrainRecipe
            {
                NoiseAmplitude = terrain.NoiseAmplitude ?? 25.0f,
                NoiseScale = terrain.NoiseScale ?? 900.0f,
                Features = terrain.Features != null ? terrain.Features.ToArray() : Array.Empty<PCGTerrainFeature>(),
                Holes = terrain.HoleFeatures != null ? terrain.HoleFeatures.ToArray() : Array.Empty<PCGTerrainFeature>(),
            };
        }

        // ---- 内部 --------------------------------------------------------------------------------------

        /// <summary>编译期的硬错误：只在 <see cref="Compile"/> 内部流转。</summary>
        private sealed class SpecCompileError : Exception
        {
            public SpecCompileError(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// 地貌底图格边长的一半，取自**注册表里该节点的默认值**。
        /// </summary>
        /// <remarks>
        /// 不在这里写死 50：节点默认值只有一个真相，写死就是第二个，改一处忘一处。注册表本来就带具名参数表，
        /// 拿它的默认值即可。读不到（注册表没就位）时回退到一个保守的 25——抖动小一点只是不好看，不会错。
        /// </remarks>
        private static float DefaultBiomeHalfCell()
        {
            if (PCGNodeRegistry.TryGet(PCGBiomeNode.Id, out var info) && info.Parameters != null)
            {
                for (int i = 0; i < info.Parameters.Length; i++)
                {
                    if (string.Equals(info.Parameters[i].Name, "Cell Size", StringComparison.Ordinal) &&
                        info.Parameters[i].Default is float cell)
                    {
                        return Math.Max(1.0f, cell * 0.5f);
                    }
                }
            }

            return 25.0f;
        }

        /// <summary>建一个地貌底图节点；<paramref name="biome"/> 为 null 时用节点自己的默认判据。</summary>
        private static int AddBiome(Builder builder, PCGSpecBiome biome)
        {
            int node = builder.Add(PCGBiomeNode.Id, 0, 0);

            if (biome == null)
                return node;

            builder.Set(node, "Cell Size", biome.CellSize);
            builder.Set(node, "Water Level", biome.WaterLevel);
            builder.Set(node, "Water Margin", biome.WaterMargin);
            builder.Set(node, "Relief Radius", biome.ReliefRadius);
            builder.Set(node, "Region Radius", biome.RegionRadius);
            builder.Set(node, "Samples", biome.Samples);
            builder.Set(node, "Mountain Slope", biome.MountainSlope);
            builder.Set(node, "Mountain Relief", biome.MountainRelief);
            builder.Set(node, "Hill Slope", biome.HillSlope);
            builder.Set(node, "Hill Relief", biome.HillRelief);
            builder.Set(node, "Basin Depth", biome.BasinDepth);
            builder.Set(node, "Basin Relief", biome.BasinRelief);
            return node;
        }

        /// <summary>
        /// 把 `hill,mountain` 这样一串地貌名解析成可以交给区间过滤器的 [min,max]。
        /// </summary>
        /// <remarks>
        /// 要求写下的那一串**连成一档**。理由是这个过滤器只有一个区间：写 `plain,mountain` 时若允许它通过，
        /// 区间 [0,2] 会把中间的丘陵也顺带留下——那正是"看着对、其实多了东西"的缺陷。与其这样，不如报错并
        /// 让设计师拆成两层（各写一个地貌），代价是多一个生成器，好处是意图与结果一一对应。
        /// </remarks>
        private static bool TryResolveBiomes(string text, out int min, out int max, out string error)
        {
            min = int.MaxValue;
            max = int.MinValue;
            error = null;

            var seen = new List<int>();

            foreach (string raw in text.Split(','))
            {
                string name = raw.Trim();

                if (name.Length == 0)
                {
                    error = "地貌名列表里有一个空项（是不是多写了一个逗号？）";
                    return false;
                }

                int kind = -1;

                for (int i = 0; i < PCGBiomeKind.Count; i++)
                {
                    if (string.Equals(PCGBiomeKind.NameOf(i), name, StringComparison.OrdinalIgnoreCase))
                    {
                        kind = i;
                        break;
                    }
                }

                if (kind < 0)
                {
                    var names = new List<string>(PCGBiomeKind.Count);

                    for (int i = 0; i < PCGBiomeKind.Count; i++)
                        names.Add(PCGBiomeKind.NameOf(i));

                    error = $"不认识地貌 '{name}'（能用：{string.Join(" / ", names)}）";
                    return false;
                }

                if (!seen.Contains(kind))
                    seen.Add(kind);

                min = Math.Min(min, kind);
                max = Math.Max(max, kind);
            }

            if (max - min + 1 != seen.Count)
            {
                error = $"'{text}' 没有连成一档；一个区间过滤器会把中间那几档也留下，" +
                        "所以请拆成两层、每层各写一个地貌";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 往链尾接一个 `Spawn Static Model`。
        /// </summary>
        /// <summary>位置关系的三种形态：附近（硬砍）、以外（硬砍）、按距离渐变。</summary>
        private enum RelationKind
        {
            /// <summary>`near`：只留半径以内。</summary>
            Near,

            /// <summary>`away`：只留半径以外。</summary>
            Away,

            /// <summary>`fade`：不动点的位置，把"到参照的距离"重映射成密度，越远越稀。</summary>
            Fade,
        }

        /// <summary>三种位置的描述关键字，报错时照原样说出来（写的是哪个字就回哪个字）。</summary>
        private static string KeywordOf(RelationKind kind) => kind switch
        {
            RelationKind.Near => "near",
            RelationKind.Away => "away",
            _ => "fade",
        };

        /// <summary>
        /// 给"只出现在一个地方"的特征造一个**字面点**当种子（`at &lt;x&gt; &lt;z&gt;`），没写就返回 -1。
        /// </summary>
        /// <remarks>
        /// `Point List` 存在的理由里就写着"村庄的中心、洞穴的入口"，可描述层此前一直没接上：村子、城市、洞穴都从
        /// **生成范围的中心**起算，于是"把村子摆在这儿、洞口在那儿"说不出来。Y 取 0，与 `road` 的两端点同一约定
        /// （高度该由地形与后续节点决定，写死一个 Y 只在特定地形上成立）。
        /// </remarks>
        private static int AddSeedAt(Builder builder, float? x, float? z, int column, int row, string label)
        {
            if (!x.HasValue || !z.HasValue)
                return -1;

            int seed = builder.Add(PCGPointListNode.Id, column, row);

            builder.Note(seed, label);
            builder.Set(seed, "Count", 1);
            builder.Set(seed, "Point 1", new Vector3(x.Value, 0.0f, z.Value));
            return seed;
        }

        /// <summary>
        /// `rotate &lt;度&gt;`：在种子与语法之间插一个 `Transform Points`，让整块地绕**种子点**转一个角度。
        /// </summary>
        /// <remarks>
        /// `Transform Points` 的旋转是"绕点自己的中心"转，不移动位置——而切分语法按**种子的朝向**决定沿哪条轴切
        /// （它自己的文档就是这么写的："rotated 30 degrees subdivides along its own front"）。于是转种子等于把整块
        /// 地的排布跟着转，这正是"村子斜着沿路排"要的东西。
        /// </remarks>
        /// <returns>出错（写了 rotate 却没写 at）时返回 false，理由已经放进结果里。</returns>
        private static bool AddTurn(Builder builder, float? degrees, int line, ref int seed, ref int column,
            int row, string directive, PCGSpecCompileResult result)
        {
            if (!degrees.HasValue)
                return true;

            // 没有种子就没有转轴：切分语法不接输入时用的是**生成范围中心**，那个点在图里根本不存在，
            // 没法给它插一个上游节点。与其悄悄把地挪到原点，不如在编译期说清楚。
            if (seed < 0)
            {
                result.Error = $"第 {line} 行：'{directive}' 写了 rotate 却没有 at。旋转是绕**种子点**转的" +
                               "（切分语法按种子的朝向切），没有写死的位置就没有转轴；补一个 `at <x> <z>`。";
                return false;
            }

            int turn = builder.Add(PCGTransformPointsNode.Id, column++, row);

            builder.Set(turn, "Rotation Degrees", new Vector3(0.0f, degrees.Value, 0.0f));
            builder.Note(turn, $"{directive}：整体绕种子转 {degrees.Value:F0} 度");
            builder.Wire(seed, turn);
            seed = turn;
            return true;
        }

        /// <summary>描述里写的一条位置关系：哪一层、参照谁、多远、哪一种。</summary>
        private struct ScatterRelation
        {
            /// <summary>写这条关系的散布层下标。</summary>
            public readonly int Layer;

            /// <summary>参照的特征名（散布层名，或 water / village / city / cave / road）。</summary>
            public readonly string Source;

            /// <summary>半径，世界单位；解析期已经保证它是正数。</summary>
            public readonly float Radius;

            /// <summary>哪一类关系。</summary>
            public readonly RelationKind Kind;

            /// <summary>写在第几行，报错时引用。</summary>
            public readonly int Line;

            public ScatterRelation(int layer, string source, float radius, RelationKind kind, int line)
            {
                Layer = layer;
                Source = source;
                Radius = radius;
                Kind = kind;
                Line = line;
            }
        }

        /// <summary>
        /// 把描述里的位置关系接进图里：在"这一层的尾巴"与"它的生成器"之间插入
        /// <c>Distance To Points</c>（量出到参照层的最近距离、写进一列）与挂着那一列的 <c>Attribute Filter</c>。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 分两趟做是为了允许<b>前向引用</b>：`scatter 芦苇 near water` 写在 `water` 之前是最自然的写法，
        /// 而那时参照的链还不存在。所以特征全部建完之后，在这里统一插节点、改连线。
        /// </para>
        /// <para>
        /// 被参照的层如果自己也有位置关系，就<b>先接它</b>——量到的是它筛完之后的落点，而不是它筛之前撒出来的
        /// 点。这同时意味着"两层互相以对方为参照"会绕成环：用状态标记查出来并报成错误，而不是让其中一条
        /// 悄悄按未筛的点去量（那种结果看着合理，所以最难查）。
        /// </para>
        /// <para>
        /// `away` 用 <c>Invert</c> 表达，而不是"半径到无穷"——过滤器没有上界这一说。两边的搜索上限都取<b>两倍</b>
        /// 半径，而且**必须严格大于**半径：`Distance To Points` 会把超过上限的距离<b>钳到上限</b>，而过滤器的区间
        /// 是闭区间——上限取 R 的话，"在 R 之外"的点会被钳成正好 R，于是落在 [0,R] 里被当成"附近"而保留，
        /// 筛选看起来完全没生效（这个坑我第一版就踩了：断言"留存点全在半径内"直接红了，而节点一条错都没报）。
        /// 取 2R 就没有这个歧义：钳出来的值 2R 明确在区间之外，`near` 丢掉它、`away` 留下它。
        /// </para>
        /// </remarks>
        private static bool WireRelations(Builder builder, PCGWorldSpec spec, List<ScatterRelation> relations,
            Dictionary<string, int> sources, int[] tails, int[] spawners, PCGSpecCompileResult result)
        {
            if (relations.Count == 0)
                return true;

            var pendingByLayer = new int[spec.Scatters.Count];
            var state = new int[spec.Scatters.Count];

            for (int i = 0; i < pendingByLayer.Length; i++)
                pendingByLayer[i] = -1;

            for (int i = 0; i < relations.Count; i++)
                pendingByLayer[relations[i].Layer] = i;

            for (int i = 0; i < relations.Count; i++)
            {
                if (!Connect(relations[i]))
                    return false;
            }

            return true;

            // state：0 = 还没接，1 = 正在接（再遇到说明绕成了环），2 = 接好了。
            bool Connect(ScatterRelation relation)
            {
                int layer = relation.Layer;

                if (state[layer] == 1)
                {
                    result.Error = $"第 {relation.Line} 行：散布层 {spec.Scatters[layer].Name} 与另一层互相以对方为" +
                                   "参照（两边都写了 near/away 指向对方），绕成了环；位置关系是有向的，" +
                                   "拆掉一边，或让其中一层先按别的条件筛。";
                    return false;
                }

                if (state[layer] == 2)
                    return true;

                state[layer] = 1;

                // 参照的层自己也带关系时，先接它：量到的是它筛完之后的落点。
                for (int i = 0; i < spec.Scatters.Count; i++)
                {
                    if (!string.Equals(spec.Scatters[i].Name, relation.Source, StringComparison.Ordinal))
                        continue;

                    if (pendingByLayer[i] >= 0 && state[i] != 2 && !Connect(relations[pendingByLayer[i]]))
                        return false;

                    break;
                }

                if (!sources.TryGetValue(relation.Source, out int reference))
                {
                    result.Error = $"第 {relation.Line} 行：散布层 {spec.Scatters[layer].Name} 的 " +
                                   $"{KeywordOf(relation.Kind)} 指向 {relation.Source}，" +
                                   $"但描述里没有这个名字；能用的有（{string.Join(" / ", sources.Keys)}）";
                    return false;
                }

                // 位置关系接在**生成器当前所在列**上：这一层的链长短不一（区域/抖动/坡度可有可无），
                // 写死列号会让后加的节点叠在别的东西上。
                int spawnerColumn = (int)Math.Round(builder.Nodes[spawners[layer]].EditorPosition.X / 300.0f);
                int distance = builder.Add(PCGDistanceToPointsNode.Id, spawnerColumn, 1 + layer);
                bool fade = relation.Kind == RelationKind.Fade;

                builder.Note(distance, fade
                    ? $"散布层 {spec.Scatters[layer].Name}：到 {relation.Source} 的距离（用来算密度衰减）"
                    : $"散布层 {spec.Scatters[layer].Name}：到 {relation.Source} 的最近距离");
                builder.Set(distance, "Attribute", PCGDistanceToPointsNode.DefaultAttribute);
                // 上限的口径按用途分：`near`/`away` **必须严格大于**半径（钳到上限的值会落进闭区间被当成"附近"），
                // 而 `fade` 恰恰要取等于半径——超过半径的钳成半径，正好落在"衰减到零"那一端。
                builder.Set(distance, "Max Distance", fade ? relation.Radius : relation.Radius * 2.0f);
                builder.Wire(tails[layer], distance);
                builder.Wire(reference, distance, "Reference");

                int outlet;

                if (fade)
                {
                    // 把"距离"重映射成"密度"：贴着参照是 1，到半径处是 0，中间线性。**目标名留空**表示写点的
                    // 密度本身（两个节点共用这条约定），于是生成器随后按密度抽点，"越远越稀"就是这么落地的。
                    int remap = builder.Add(PCGRemapAttributeNode.Id, spawnerColumn + 1, 1 + layer);

                    builder.Note(remap, $"散布层 {spec.Scatters[layer].Name}：密度按到 {relation.Source} 的距离" +
                                        $"渐变，{relation.Radius:F0} 处为零");
                    builder.Set(remap, "Source Attribute", PCGDistanceToPointsNode.DefaultAttribute);
                    builder.Set(remap, "Target Attribute", string.Empty);
                    builder.Set(remap, "In Min", 0.0f);
                    builder.Set(remap, "In Max", relation.Radius);
                    builder.Set(remap, "Out Min", 1.0f);
                    builder.Set(remap, "Out Max", 0.0f);
                    builder.Set(remap, "Clamp", true);
                    builder.Wire(distance, remap);
                    outlet = remap;
                }
                else
                {
                    int range = builder.Add(PCGAttributeFilterNode.Id, spawnerColumn + 1, 1 + layer);

                    builder.Note(range, relation.Kind == RelationKind.Near
                        ? $"散布层 {spec.Scatters[layer].Name}：只留距 {relation.Source} {relation.Radius:F0} 以内"
                        : $"散布层 {spec.Scatters[layer].Name}：只留距 {relation.Source} {relation.Radius:F0} 以外");
                    builder.Set(range, "Attribute", PCGDistanceToPointsNode.DefaultAttribute);
                    builder.Set(range, "Min", 0.0f);
                    builder.Set(range, "Max", relation.Radius);
                    builder.Set(range, "Invert", relation.Kind == RelationKind.Away);
                    builder.Wire(distance, range);
                    outlet = range;
                }

                // 生成器改接到筛选/改写之后：先摘掉原来那条 tail → 生成器，再接上新的出口。
                builder.Unwire(tails[layer], spawners[layer]);
                builder.Wire(outlet, spawners[layer]);
                builder.Nodes[spawners[layer]].EditorPosition =
                    new Vector2((spawnerColumn + 2) * 300.0f, (1 + layer) * 180.0f);

                tails[layer] = outlet;
                sources[spec.Scatters[layer].Name] = outlet;
                state[layer] = 2;
                return true;
            }
        }

        private static int AddSpawner(Builder builder, int input, int column, int row, string modelPath,
            float? modelScale, bool? randomYaw, string label, Func<string, Guid> resolver,
            PCGSpecCompileResult result,
            string geometryShape = null, Vector3? geometrySize = null, int? geometrySegments = null,
            bool merged = false, bool instanced = false)
        {
            // 给了形状名就走**程序化几何**：先把每个点变成一个建出来的网格，再交给几何生成器。
            // 这一档原先只有 `scatter` 层能写；聚居地/城市共用这条出口，所以现在一句
            // `village … geometry house geometry-size 16 12 14` 就能让村子长出有屋顶的房子，
            // 而不是必须去借一个模型资产。
            bool procedural = !string.IsNullOrWhiteSpace(geometryShape);

            if (procedural && !string.IsNullOrWhiteSpace(modelPath))
            {
                result.Warnings.Add($"{label}：同时写了 geometry 与 model；按 geometry 建，model 被忽略。");
            }

            // 与散布层那一处同一个道理：程序化几何这一档没有"缩放/偏航"可写，作者留下的数必须点名它没对象，
            // 不能安静地丢掉（聚居地/城市/路三条链都从这一处出口走）。
            if (procedural)
            {
                var dropped = new List<string>();

                if (modelScale.HasValue)
                    dropped.Add("model-scale");

                if (randomYaw.HasValue)
                    dropped.Add("yaw");

                if (dropped.Count > 0)
                {
                    result.Warnings.Add($"{label}：是程序化几何，{string.Join(" 与 ", dropped)} 不生效" +
                                        "（尺寸只认 `geometry-size`、按世界单位给；朝向由几何节点定）。");
                }
            }

            var source = input;

            if (procedural)
            {
                int primitives = builder.Add(PCGGeometryNode.Id, column, row);

                builder.Set(primitives, "Shape", PCGSpecParser.GeometryShapeIndex(geometryShape));
                Set(builder, primitives, "Segments", geometrySegments);
                builder.Set(primitives, "Cell Size", geometrySize ?? new Vector3(100.0f, 100.0f, 100.0f));
                builder.Set(primitives, "Use Point Scale", false);
                builder.Note(primitives, $"{label}：程序化几何 {geometryShape} " +
                                         $"{geometrySize ?? new Vector3(100.0f, 100.0f, 100.0f)}");
                builder.Wire(input, primitives);
                source = primitives;
                column++;

                if (!geometrySize.HasValue)
                {
                    result.Warnings.Add($"{label}：写了 geometry 但没给 geometry-size，" +
                                        "尺寸取默认 100×100×100（世界单位，格子级的默认值会让房子大到离谱）。");
                }
            }

            int spawner = builder.Add(
                procedural ? PCGSpawnGeometryNode.Id : PCGSpawnStaticModelNode.Id, column, row);

            builder.Note(spawner, label);

            if (procedural)
            {
                // 几何生成器的两个开关都关：一层一件件摆，才看得见地块的疏密；合并/实例化是 `output` 那一档的事。
                builder.Set(spawner, "Merge Into One Actor", merged);
                builder.Set(spawner, "To Foliage Host", instanced);
                builder.Set(spawner, "Label", label);
            }
            else
            {
                Set(builder, spawner, "Scale Multiplier", modelScale);
                Set(builder, spawner, "Random Yaw", randomYaw);

                if (!string.IsNullOrEmpty(modelPath))
                    Set(builder, spawner, "Model", Resolve(resolver, modelPath, result));
            }

            builder.Wire(source, spawner);

            // 返回下标是给"位置关系"用的：那一步要把两个节点插进已有的链里，于是得知道这一层的生成器是谁，
            // 才能把 tail → 生成器 那条线改接到筛选之后。
            return spawner;
        }

        /// <summary>
        /// 按符号分派形状：<c>geometry-map "Room=box 12 3 12; Plot=grid 60 1 40"</c>。
        /// 每个条目在链上插出一条 <c>AttributeFilter(Symbol == 序号) → Geometry Primitives → Spawn Geometry</c>。
        /// </summary>
        /// <remarks>
        /// **序号不是这里另算一套**：符号表直接取自 <c>Grammar</c> 节点自己的 <c>TryParseProductions</c>，
        /// 两边同一份编号。另算一份的结局是"筛了一个没人等于它的序号、整层静默什么都不长"。
        /// 覆盖性是硬要求：语法里每一个**叶子**符号都必须在表里出现，缺一个就报错——
        /// 漏掉一类叶子正是描述层最怕的那种"说了没长出来"。
        /// 给一个还会被改写的符号指派形状同样报错：那不是叶子，永远没有点会带着它走到生成器。
        /// </remarks>
        private static int AddSymbolShapeSpawners(Builder builder, int input, int column, int row,
            PCGSpecCity city, string label, PCGSpecCompileResult result)
        {
            // 分派出来的每一支都吃同一个输出形态：不这么读的话 `output` 只作用于普通生成器那一条路，
            // 一座按符号分派的城照旧一片一个 Actor——半生效比不支持难查得多。
            string mapForm = city.OutputForm?.Trim().ToLowerInvariant();
            bool merged = mapForm == "merged";
            bool instanced = mapForm == "instanced";

            if (!string.IsNullOrWhiteSpace(city.GeometryShape) || !string.IsNullOrWhiteSpace(city.ModelPath))
            {
                result.Warnings.Add($"{label}：同时写了 geometry-map 与 geometry/model；按 geometry-map 分派，另一个被忽略。");
            }

            if (!PCGGrammarNode.TryParseProductions(city.Productions ?? string.Empty,
                    out var grammar, out string grammarError))
            {
                result.Error = $"{label}：{grammarError}";
                return -1;
            }

            var entries = new List<(int Symbol, string Shape, Vector3 Size)>();
            var named = new HashSet<int>();

            foreach (string rawEntry in city.SymbolShapes.Split(';'))
            {
                string entry = rawEntry.Trim();

                if (entry.Length == 0)
                    continue;

                int eq = entry.IndexOf('=');

                if (eq <= 0)
                {
                    result.Error = $"{label}：geometry-map 的一段「{entry}」缺了 `符号=形状 宽 高 深` 的等号。";
                    return -1;
                }

                string symbolName = entry.Substring(0, eq).Trim();
                var tokens = entry.Substring(eq + 1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (!grammar.TryFindSymbol(symbolName, out int symbol))
                {
                    result.Error = $"{label}：geometry-map 点了符号「{symbolName}」，这条语法里没有它" +
                                   grammar.SymbolList() + "。";
                    return -1;
                }

                if (grammar.TryGet(symbol, out _))
                {
                    result.Error = $"{label}：符号「{symbolName}」还会被改写，不是叶子——给它指派形状，" +
                                   "没有任何一件产出会用到它。";
                    return -1;
                }

                if (tokens.Length < 4)
                {
                    result.Error = $"{label}：符号 {symbolName} 少了几何尺寸，需要 `形状 宽 高 深` 四个词。";
                    return -1;
                }

                if (!PCGSpecParser.IsKnownGeometryShape(tokens[0]))
                {
                    result.Error = $"{label}：符号 {symbolName} 的形状「{tokens[0]}」不认识（能写的：" +
                                   string.Join(" / ", PCGSpecParser.GeometryShapes) + "）。";
                    return -1;
                }

                var size = new Vector3(Number(tokens[1]), Number(tokens[2]), Number(tokens[3]));

                if (size.X <= 0.0f || size.Y <= 0.0f || size.Z <= 0.0f)
                {
                    result.Error = $"{label}：符号 {symbolName} 的几何尺寸必须是三个正数。";
                    return -1;
                }

                entries.Add((symbol, tokens[0], size));
                named.Add(symbol);
            }

            if (entries.Count == 0)
            {
                result.Error = $"{label}：geometry-map 一个条目都没有。";
                return -1;
            }

            for (int s = 0; s < grammar.SymbolCount; s++)
            {
                if (grammar.TryGet(s, out _) || named.Contains(s))
                    continue;

                result.Error = $"{label}：语法里的叶子符号「{grammar.NameOf(s)}」没在 geometry-map 里指派形状。";
                return -1;
            }

            int built = 0;

            foreach (var entry in entries)
            {
                int filter = builder.Add(PCGAttributeFilterNode.Id, column + built * 3, row);

                builder.Set(filter, "Attribute", PCGGrammarNode.SymbolColumn);
                builder.Set(filter, "Min", (float)entry.Symbol);
                builder.Set(filter, "Max", (float)entry.Symbol);
                builder.Set(filter, "Invert", false);
                builder.Note(filter, $"{label}：只留符号 {grammar.NameOf(entry.Symbol)}（序号 {entry.Symbol}）");
                builder.Wire(input, filter);

                int primitives = builder.Add(PCGGeometryNode.Id, column + built * 3 + 1, row);

                builder.Set(primitives, "Shape", PCGSpecParser.GeometryShapeIndex(entry.Shape));
                builder.Set(primitives, "Cell Size", entry.Size);
                builder.Set(primitives, "Use Point Scale", false);
                builder.Note(primitives, $"{label}：{grammar.NameOf(entry.Symbol)} → {entry.Shape} {entry.Size}");
                builder.Wire(filter, primitives);

                int spawner = builder.Add(PCGSpawnGeometryNode.Id, column + built * 3 + 2, row);

                builder.Set(spawner, "Merge Into One Actor", merged);
                builder.Set(spawner, "To Foliage Host", instanced);
                builder.Set(spawner, "Label", label + grammar.NameOf(entry.Symbol));
                builder.Note(spawner, $"{label}：{entry.Shape}");
                builder.Wire(primitives, spawner);

                built++;
            }

            return built;
        }

        /// <summary>描述里的一个数；认不出就当 0 交给上层校验（三个数必须为正，所以报错会由调用方给）。</summary>
        /// <summary>
        /// 把"长在哪 / 从中去掉哪"这一串筛子接到链上：<c>within</c>/<c>outside</c> → <c>polygon</c> →
        /// <c>except</c> → <c>except-polygon</c>，交回链的新末端与下一个可用列号。
        /// </summary>
        /// <remarks>
        /// 散布层、聚居地、城市三份规格的这些字段**同名同义**（<c>AreaMinX…</c> / <c>PolygonText</c> /
        /// <c>ExceptMinX…</c>），所以这里按名字读而不是每个类型写一遍：写两遍迟早漂成两种语义，
        /// 而"同一个关键词在村里筛掉的东西和在城里不一样"是没法解释给作者听的。
        /// </remarks>
        private static (int Tail, int NextColumn) AddRegionFilters(
            Builder builder, object region, int tail, int column, int row, string label)
        {
            var type = region.GetType();

            float? Num(string name) => type.GetField(name)?.GetValue(region) as float?;
            string Text(string name) => type.GetField(name)?.GetValue(region) as string;

            bool invert = (bool)(type.GetField("AreaInvert")?.GetValue(region) ?? false);

            float? areaMin = Num("AreaMinX");
            float? areaMinZ = Num("AreaMinZ");
            float? areaMax = Num("AreaMaxX");
            float? areaMaxZ = Num("AreaMaxZ");

            if (areaMin.HasValue)
            {
                int area = builder.Add(PCGAreaFilterNode.Id, column++, row);

                builder.Set(area, "Min X", areaMin.Value);
                builder.Set(area, "Max X", areaMax.Value);
                builder.Set(area, "Min Z", areaMinZ.Value);
                builder.Set(area, "Max Z", areaMaxZ.Value);
                builder.Set(area, "Invert", invert);
                builder.Note(area, $"{label}：只{(invert ? "不要" : "要")}" +
                                  $" x[{areaMin.Value:F0},{areaMax.Value:F0}] " +
                                  $"z[{areaMinZ.Value:F0},{areaMaxZ.Value:F0}] 这片");
                builder.Wire(tail, area);
                tail = area;
            }

            string polygon = Text("PolygonText");

            if (!string.IsNullOrWhiteSpace(polygon))
            {
                int poly = builder.Add(PCGPolygonFilterNode.Id, column++, row);

                builder.Set(poly, "Vertices", polygon);
                builder.Set(poly, "Invert", false);
                builder.Note(poly, $"{label}：只在地块 {polygon} 里");
                builder.Wire(tail, poly);
                tail = poly;
            }

            float? holeMin = Num("ExceptMinX");
            float? holeMinZ = Num("ExceptMinZ");
            float? holeMax = Num("ExceptMaxX");
            float? holeMaxZ = Num("ExceptMaxZ");

            if (holeMin.HasValue)
            {
                int hole = builder.Add(PCGAreaFilterNode.Id, column++, row);

                builder.Set(hole, "Min X", holeMin.Value);
                builder.Set(hole, "Max X", holeMax.Value);
                builder.Set(hole, "Min Z", holeMinZ.Value);
                builder.Set(hole, "Max Z", holeMaxZ.Value);
                builder.Set(hole, "Invert", true);
                builder.Note(hole, $"{label}：从上面选中的范围里挖掉 " +
                                  $"x[{holeMin.Value:F0},{holeMax.Value:F0}] " +
                                  $"z[{holeMinZ.Value:F0},{holeMaxZ.Value:F0}] 这块");
                builder.Wire(tail, hole);
                tail = hole;
            }

            string holePolygon = Text("ExceptPolygonText");

            if (!string.IsNullOrWhiteSpace(holePolygon))
            {
                int hole = builder.Add(PCGPolygonFilterNode.Id, column++, row);

                builder.Set(hole, "Vertices", holePolygon);
                builder.Set(hole, "Invert", true);
                builder.Note(hole, $"{label}：挖掉地块 {holePolygon} 里");
                builder.Wire(tail, hole);
                tail = hole;
            }

            return (tail, column);
        }

        private static float Number(string text) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0.0f;

        /// <summary>按名字往槽位里写值；名字不存在直接报错，安静地少写一个参数比报错糟得多。</summary>
        private static void Set(Builder builder, int node, string slot, object value)
        {
            if (value == null)
                return;

            builder.Set(node, slot, value);
        }

        private static Guid Resolve(Func<string, Guid> resolver, string path, PCGSpecCompileResult result)
        {
            // 内建图元的六个名字直接查表，**不经过宿主解析器**：编辑器与命令行对同一份描述必须给出同一个 Guid，
            // 而描述也不再依赖引擎装在哪（见 `PCGBuiltinModels`）。
            if (PCGBuiltinModels.TryResolve(path, out var builtin))
                return builtin;

            WarnIfAbsolutePath(path, result);

            Guid id = resolver != null ? resolver(path) : Guid.Empty;

            if (id == Guid.Empty)
            {
                result.Warnings.Add($"模型「{path}」没有解析出资产；生成器会在运行时报「没有指定模型」，" +
                                    "把资产放到这个路径上再编译一次即可。");
            }

            return id;
        }

        /// <summary>
        /// 绝对路径点名。同一个路径在一份描述里出现多次只说一次——重复的那几句不增加信息，
        /// 只会把提醒挤出别人的视线。
        /// </summary>
        private static void WarnIfAbsolutePath(string path, PCGSpecCompileResult result)
        {
            if (!IsAbsolutePath(path))
                return;

            string marker = $"「{path}」写的是绝对路径";

            for (int i = 0; i < result.Warnings.Count; i++)
            {
                if (result.Warnings[i] != null && result.Warnings[i].Contains(marker))
                    return;
            }

            result.Warnings.Add($"模型{marker}：这份描述就此钉死在这台机器的安装位置上——" +
                                "换一台机器或换引擎安装目录，这一层就指不到资产了；工程内的资产请写相对路径" +
                                "（例如 Content/PCG/Models/Pine.flax）。" +
                                "**这条只是可移植性提醒**，不是说工程外的资产运行期认不得：实测引擎自带图元在编辑器里" +
                                "能正常落 Actor（演示城镇靠 Cube 出 1330 件），而把它字节复制进工程**反而**载不进来" +
                                "（负结果见 docs/PCG_PLUGIN_DESIGN.md §7.6）。");
        }

        /// <summary>写成绝对路径了吗：盘符、UNC、POSIX 根、`~/` 四种写法。</summary>
        private static bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (path[0] == '/' || path[0] == '~')
                return true;

            if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
                return true;

            return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
                   (path[2] == '\\' || path[2] == '/');
        }

        /// <summary>边攒节点边攒连线的小工具。</summary>
        private sealed class Builder
        {
            /// <summary>已产出的节点。</summary>
            public readonly List<PCGNode> Nodes = new List<PCGNode>();

            /// <summary>已产出的连线。</summary>
            public readonly List<PCGEdge> Edges = new List<PCGEdge>();

            private readonly Dictionary<int, PCGNodeBase> _instances = new Dictionary<int, PCGNodeBase>();

            /// <summary>加一个节点，按列行摆在画布上。</summary>
            public int Add(string typeId, int column, int row)
            {
                if (!PCGNodeRegistry.TryGet(typeId, out var info))
                {
                    throw new SpecCompileError($"节点类型 '{typeId}' 没有注册；图里不能放它。");
                }

                var node = new PCGNode
                {
                    TypeId = typeId,
                    Values = new object[info.ParameterCount],
                    EditorPosition = new Vector2(column * 300.0f, row * 180.0f),
                };

                Nodes.Add(node);
                return Nodes.Count - 1;
            }

            /// <summary>按名字往槽位里写值。</summary>
            public void Set(int node, string slot, object value)
            {
                if (!PCGNodeRegistry.TryGet(Nodes[node].TypeId, out var info))
                    throw new SpecCompileError($"节点类型 '{Nodes[node].TypeId}' 没有注册。");

                if (info.Parameters == null || info.Parameters.Length == 0)
                {
                    throw new SpecCompileError($"节点 '{Nodes[node].TypeId}' 没有声明参数，但设定描述想给它写 " +
                                               $"'{slot}'。");
                }

                for (int i = 0; i < info.Parameters.Length; i++)
                {
                    if (!string.Equals(info.Parameters[i].Name, slot, StringComparison.Ordinal))
                        continue;

                    if (!info.Parameters[i].IsCompatible(value))
                    {
                        throw new SpecCompileError(
                            $"节点 '{Nodes[node].TypeId}' 的槽位 '{slot}' 要 {info.Parameters[i].Kind}，" +
                            $"而设定描述给的是 {value.GetType().Name}。");
                    }

                    Nodes[node].Values[i] = value;
                    return;
                }

                throw new SpecCompileError($"节点 '{Nodes[node].TypeId}' 没有名为 '{slot}' 的槽位；" +
                                           "节点改过参数表就会这样，图不能按旧名字生成。");
            }

            /// <summary>写一句节点注释，让人在图里看得出这一排是干什么的。</summary>
            public void Note(int node, string text)
            {
                Nodes[node].Comment = text;
            }

            /// <summary>把上游的 Out 连到下游的 In；引脚下标按名字查，不写死。</summary>
            public void Wire(int from, int to) => Wire(from, to, "In");

            /// <summary>把上游的 Out 连到下游的<b>具名</b>输入，例如 `Distance To Points` 的 Reference。</summary>
            public void Wire(int from, int to, string inputName)
            {
                Edges.Add(new PCGEdge
                {
                    OutputNodeIndex = from,
                    OutputPinIndex = Pin(from, "Out", true),
                    InputNodeIndex = to,
                    InputPinIndex = Pin(to, inputName, false),
                });
            }

            /// <summary>摘掉一条 Out→In 的连线：把节点插进已有的链里时必须先断开原来那条。</summary>
            public void Unwire(int from, int to)
            {
                for (int i = Edges.Count - 1; i >= 0; i--)
                {
                    if (Edges[i].OutputNodeIndex != from || Edges[i].InputNodeIndex != to)
                        continue;

                    Edges.RemoveAt(i);
                    return;
                }

                throw new SpecCompileError($"想摘掉 '{Nodes[from].TypeId}' → '{Nodes[to].TypeId}' 的连线，" +
                                           "但图里没有这条线（这是插件的缺陷，不是描述写错了）。");
            }

            /// <summary>查一个引脚的序号：把节点按现有值造出来，读它自己报的引脚表。</summary>
            private int Pin(int node, string name, bool output)
            {
                if (!_instances.TryGetValue(node, out var instance))
                {
                    instance = PCGNodeRegistry.CreateNode(Nodes[node], node, out string error);

                    if (instance == null)
                        throw new SpecCompileError($"没能造出节点 '{Nodes[node].TypeId}' 来读它的引脚：{error}");

                    _instances[node] = instance;
                }

                var pins = output ? instance.OutputPins : instance.InputPins;

                if (pins == null)
                    throw new SpecCompileError($"节点 '{Nodes[node].TypeId}' 没有{(output ? "输出" : "输入")}引脚。");

                for (int i = 0; i < pins.Length; i++)
                {
                    if (string.Equals(pins[i].Name, name, StringComparison.Ordinal))
                        return i;
                }

                throw new SpecCompileError($"节点 '{Nodes[node].TypeId}' 没有名为 '{name}' 的" +
                                           $"{(output ? "输出" : "输入")}引脚。");
            }
        }
    }
}

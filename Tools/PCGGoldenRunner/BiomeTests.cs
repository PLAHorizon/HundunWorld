// PCG 插件 - 生物群系分类的可执行断言（见 docs/PCG_PLUGIN_DESIGN.md §5.1.37）
//
// 分类器的错法很隐蔽：阈值差一点、判据顺序换一换，图还是能跑，只是"山顶被当成平原、盆地被当成山谷"。
// 所以判据分成两层——先在**手造的采样值**上验每条规则本身（不依赖任何高度场），再在**解析地形**上验
// 山顶/碗底/水面各自归到该去的那一档；最后在节点层验"每个点恰好归一档、两列互相吻合"。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class BiomeTests
    {
        internal static void Run(CheckDelegate check)
        {
            MeasureTests(check);
            RuleTests(check);
            FieldTests(check);
            NodeTests(check);
        }

        /// <summary>量出来的三件事本身要对：坡度是几何事实，起伏是几何事实。</summary>
        private static void MeasureTests(CheckDelegate check)
        {
            var options = Options();

            // 沿 +x 每单位抬 0.2 的斜面：坡度应为 atan(0.2) = 11.31 度。
            var sample = PCGBiomeClassifier.Measure((x, z) => x * 0.2f, 0.0f, 0.0f, options);

            check("坡面上量出的坡度与几何一致",
                sample.Known && Math.Abs(sample.Slope - 11.31f) < 0.2f, sample.Slope.ToString("F2"));

            // 起伏 = 短半径内的高差 = 坡度 × 直径（两端各偏一个半径）。
            float expectedRelief = 0.2f * options.ReliefRadius * 2.0f;

            check("坡面上量出的起伏等于坡度乘直径",
                Math.Abs(sample.Relief - expectedRelief) < 1.0f,
                $"{sample.Relief:F1} vs {expectedRelief:F1}");

            // 对称采样下，斜面上某点的"周边平均"恰好等于它自己的高度。
            check("对称采样下斜面的周边平均等于自身高度",
                Math.Abs(sample.RegionMean - sample.Height) < 0.5f,
                $"{sample.RegionMean:F2} vs {sample.Height:F2}");

            // 拿不到高度时如实标记未知，而不是编一个 0。
            var blind = PCGBiomeClassifier.Measure((x, z) => null, 0.0f, 0.0f, options);
            check("高度未知时采样标记为未知", !blind.Known && float.IsNaN(blind.Height));
        }

        /// <summary>每条判据在手造的采样值上单独验一遍。</summary>
        private static void RuleTests(CheckDelegate check)
        {
            var options = Options();

            var flat = new PCGBiomeClassifier.Sample
            {
                Known = true, Height = 100.0f, RegionMean = 100.0f, Relief = 1.0f, Slope = 1.0f,
            };

            check("平地上是平原", PCGBiomeClassifier.Classify(flat, options) == PCGBiomeKind.Plain);

            // 水最先判：水面下的地形长什么样都不该长树。
            var underwater = flat;
            underwater.Height = options.WaterLevel - 5.0f;

            check("低于水面归水", PCGBiomeClassifier.Classify(underwater, options) == PCGBiomeKind.Water);

            var shoreline = flat;
            shoreline.Height = options.WaterLevel + 0.1f;

            check("水面余量之内仍归水", PCGBiomeClassifier.Classify(shoreline, options) == PCGBiomeKind.Water);

            // 盆地：明显低于周边、且底部平缓。
            var basin = flat;
            basin.Height = 50.0f;
            basin.RegionMean = 50.0f + options.BasinDepth + 5.0f;
            basin.Relief = options.BasinRelief - 1.0f;

            check("低洼且平缓的归盆地", PCGBiomeClassifier.Classify(basin, options) == PCGBiomeKind.Basin);

            // 陡壁的洼地仍然是山地：盆地判据要求底平，陡的就不算。
            var steepBowl = basin;
            steepBowl.Relief = options.BasinRelief + 1.0f;
            steepBowl.Slope = options.MountainSlope + 1.0f;

            check("陡壁洼地判成山地而不是盆地",
                PCGBiomeClassifier.Classify(steepBowl, options) == PCGBiomeKind.Mountain);

            // 阈值两侧各判一次：这是"阈值真的在起作用"的正反对照。
            var justUnder = flat;
            justUnder.Slope = options.MountainSlope - 0.5f;

            var justOver = flat;
            justOver.Slope = options.MountainSlope + 0.5f;

            check("坡度刚过山地阈值就归山地，没过则归丘陵",
                PCGBiomeClassifier.Classify(justUnder, options) == PCGBiomeKind.Hill &&
                PCGBiomeClassifier.Classify(justOver, options) == PCGBiomeKind.Mountain,
                $"{PCGBiomeClassifier.Classify(justUnder, options)} / {PCGBiomeClassifier.Classify(justOver, options)}");

            var rough = flat;
            rough.Relief = options.HillRelief + 1.0f;

            check("起伏超丘陵阈值归丘陵", PCGBiomeClassifier.Classify(rough, options) == PCGBiomeKind.Hill);

            check("五种地貌各有名字与下标",
                PCGBiomeKind.NameOf(PCGBiomeKind.Plain) == "Plain" &&
                PCGBiomeKind.NameOf(PCGBiomeKind.Hill) == "Hill" &&
                PCGBiomeKind.NameOf(PCGBiomeKind.Mountain) == "Mountain" &&
                PCGBiomeKind.NameOf(PCGBiomeKind.Basin) == "Basin" &&
                PCGBiomeKind.NameOf(PCGBiomeKind.Water) == "Water");
        }

        /// <summary>在解析地形上验：山顶归山地、碗底归盆地、远坡归平原。</summary>
        private static void FieldTests(CheckDelegate check)
        {
            var options = Options();

            // 高斯山：中心高 200，随距离衰减；半径 600 外基本平坦。
            int peak = PCGBiomeClassifier.Classify(Hill, 0.0f, 0.0f, options, out _);
            int slope = PCGBiomeClassifier.Classify(Hill, 300.0f, 0.0f, options, out _);
            int far = PCGBiomeClassifier.Classify(Hill, 3000.0f, 0.0f, options, out _);

            check("山顶归山地", peak == PCGBiomeKind.Mountain, PCGBiomeKind.NameOf(peak));
            check("山坡归丘陵或山地",
                slope == PCGBiomeKind.Hill || slope == PCGBiomeKind.Mountain, PCGBiomeKind.NameOf(slope));
            check("远离山体的平地归平原", far == PCGBiomeKind.Plain, PCGBiomeKind.NameOf(far));

            // 碗：中心 -60，四周抬到 0，碗底平缓 → 盆地。
            int bowl = PCGBiomeClassifier.Classify(Bowl, 0.0f, 0.0f, options, out var bowlSample);

            check("碗底归盆地", bowl == PCGBiomeKind.Basin,
                $"{PCGBiomeKind.NameOf(bowl)}（起伏 {bowlSample.Relief:F1}，比周边低 {bowlSample.RegionMean - bowlSample.Height:F1}）");

            // 水面：整个地形在水位之下。
            int lake = PCGBiomeClassifier.Classify((x, z) => options.WaterLevel - 10.0f, 0.0f, 0.0f, options, out _);
            check("水位之下归水", lake == PCGBiomeKind.Water);

            // 确定性与"同一点跑两次一样"（分类器无随机，但要钉住）。
            int again = PCGBiomeClassifier.Classify(Hill, 300.0f, 0.0f, options, out _);
            check("同一点分类两次一致", again == slope);
        }

        /// <summary>节点层：底图模式、打标签模式，以及两列互相吻合。</summary>
        private static void NodeTests(CheckDelegate check)
        {
            var bounds = new BoundingBox(new Vector3(0, -100, 0), new Vector3(1000, 200, 1000));
            var world = new PCGWorldContext { HeightSource = Hill };
            var context = Context(world, bounds);

            // 底图模式：不接输入，按 Cell Size 铺满生成范围。
            var node = Make();
            var map = node.Execute(context, new[] { (PCGData)null });

            int columns = (int)Math.Floor(1000.0f / 50.0f) + 1;
            int expected = columns * columns;

            check("底图模式按格子铺满生成范围",
                map.Count == expected, $"{map.Count} vs {expected}");

            var biomes = map.SharedAttributes?.TryGetFloatArray(PCGBiomeNode.BiomeColumn);
            var reliefs = map.SharedAttributes?.TryGetFloatArray(PCGBiomeNode.ReliefColumn);

            check("底图带 Biome 与 Relief 两列",
                biomes != null && biomes.Length == map.Count && reliefs != null && reliefs.Length == map.Count);

            // 每个点恰好归一档：各档计数之和必须等于点数。
            int sum = 0;

            for (int i = 0; i < node.LastCounts.Length; i++)
                sum += node.LastCounts[i];

            check("每个点恰好归一档（各档计数之和等于点数）", sum == map.Count, $"{sum} vs {map.Count}");

            check("山顶附近确实有山地", node.LastCounts[PCGBiomeKind.Mountain] > 0,
                string.Join("/", node.LastCounts));

            // 两列必须互相吻合：名字列不能与序号列说两套话。
            var names = new string[Math.Min(map.Count, 32)];
            bool namesMatch = map.SharedAttributes != null &&
                              map.SharedAttributes.Contains(PCGBiomeNode.BiomeNameColumn);

            for (int i = 0; namesMatch && i < names.Length; i++)
            {
                string name = map.SharedAttributes.GetString(PCGBiomeNode.BiomeNameColumn, i);
                namesMatch = name == PCGBiomeKind.NameOf((int)biomes[i]);
            }

            check("名字列与序号列互相吻合", namesMatch);

            // 打标签模式：接输入时点位与数量都不动，只多出列。
            var layer = PCGData.Create(3);

            for (int i = 0; i < layer.Count; i++)
            {
                layer.Points[i] = PCGPoint.Create(new Vector3(i * 400.0f, 0.0f, 0.0f), 1.0f, i);
                layer.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            var tagged = Make();
            var taggedResult = tagged.Execute(context, new[] { layer });

            check("打标签模式不改变点数",
                taggedResult.Count == 3, taggedResult.Count.ToString());

            check("打标签模式不移动点",
                Near(taggedResult.Points[0].Position.X, 0.0f) && Near(taggedResult.Points[2].Position.X, 800.0f));

            var taggedBiomes = taggedResult.SharedAttributes?.TryGetFloatArray(PCGBiomeNode.BiomeColumn);
            check("打标签模式写出了 Biome 列", taggedBiomes != null && taggedBiomes.Length == 3);

            // 没有地形：原样透传并说明原因。
            var blind = Make();
            var blindResult = blind.Execute(Context(new PCGWorldContext(), bounds), new[] { layer });

            check("没有高度源时原样透传并说明原因",
                blindResult.Count == 3 && !string.IsNullOrEmpty(blind.LastError) &&
                blind.LastError.Contains("terrain"),
                blind.LastError);
        }

        // ---- 夹具 ----------------------------------------------------------------------------------------

        private static PCGBiomeOptions Options() => new PCGBiomeOptions
        {
            WaterLevel = 0.0f,
            WaterMargin = 0.5f,
            ReliefRadius = 100.0f,
            RegionRadius = 500.0f,
            Samples = 4,
            MountainSlope = 20.0f,
            MountainRelief = 80.0f,
            HillSlope = 6.0f,
            HillRelief = 15.0f,
            BasinDepth = 25.0f,
            BasinRelief = 12.0f,
        };

        private static PCGBiomeNode Make()
        {
            return new PCGBiomeNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGBiomeNode.Id,
                new object[]
                {
                    50.0f, 0.0f, 0.5f, 100.0f, 500.0f, 4, 20.0f, 80.0f, 6.0f, 15.0f, 25.0f, 12.0f, true, true,
                },
                0));
        }

        private static PCGExecutionContext Context(PCGWorldContext world, BoundingBox bounds) => new PCGExecutionContext
        {
            Seed = 20261008,
            World = world,
            GenerationBounds = bounds,
        };

        /// <summary>高斯山立在 20 米高的台地上：中心再抬 200，衰减长度 100（够陡，够格叫山地）。</summary>
        /// <remarks>
        /// 台地高度是必须的：地面若正好落在水位线上，分类器会**如实**把远坡判成水——那是判据正确，
        /// 不是夹具想要的"平原"。
        /// </remarks>
        private static float? Hill(float x, float z)
        {
            float squared = x * x + z * z;
            return 20.0f + 200.0f * (float)Math.Exp(-squared / (2.0 * 100.0 * 100.0));
        }

        /// <summary>碗：中心 20、边缘 100，半径 800 之外为 100，中心附近平缓（在水位之上）。</summary>
        private static float? Bowl(float x, float z)
        {
            float distance = (float)Math.Sqrt(x * x + z * z);

            if (distance >= 800.0f)
                return 100.0f;

            // 余弦过渡：中心平、边缘陡，正是"底部平缓的洼地"，且整体高于水位线。
            float t = distance / 800.0f;
            return 100.0f - 80.0f * (float)Math.Cos(t * Math.PI * 0.5);
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;
    }
}

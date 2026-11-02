// PCG 插件 - 地形合成纯核的可执行断言
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §9 第 6 条。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    /// <summary>
    /// 地形合成纯核：形状、叠加、patch 尺寸契约与确定性。
    /// </summary>
    /// <remarks>
    /// 这一层刻意**不碰引擎**：真正写高度（`Terrain.SetupPatchHeightMap`）是破坏性且只写不读的，等 §9 第 6 条
    /// 的决定 C 拍板；而"该写什么数"是纯数学，可以在这里逐点钉住。所以本组断言的边界很清楚：**它证明的是
    /// 那组数是对的**，不证明引擎收下了——后者只能 PIE 看。
    /// </remarks>
    internal static class TerrainTests
    {
        internal static void Run(CheckDelegate check)
        {
            // ---- 尺寸契约（引擎的口径，不是我的） ----------------------------------------------------------

            check("高度场一边的采样点数就是 chunkSize*4+1",
                PCGTerrainSynthesizer.SamplesPerPatch(1) == 5 &&
                PCGTerrainSynthesizer.SamplesPerPatch(8) == 33 &&
                PCGTerrainSynthesizer.SamplesPerPatch(16) == 65,
                $"{PCGTerrainSynthesizer.SamplesPerPatch(8)} / {PCGTerrainSynthesizer.SamplesPerPatch(16)}");

            var one = new List<PCGTerrainFeature>();

            float[] wrong = new float[24];
            bool rejected = !PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 4.0f, one, 0.0f, 0.0f, 1, wrong,
                out string wrongError);

            // 尺寸写错在引擎里是一片静默的空地形，所以这里必须**按引擎口径**报出期望值：5^2 = 25。
            check("数组长度不对被拒，并且理由里给出引擎要的那个数",
                rejected && wrongError != null && wrongError.Contains("25"), wrongError ?? "居然通过了");

            float[] flat = new float[25];

            check("长度对了就通过（上一条的正面同夹具）",
                PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 4.0f, one, 0.0f, 0.0f, 1, flat,
                    out string okError),
                okError ?? "ok");

            // ---- 形状（逐点算，所以能用非格点上的位置） -----------------------------------------------------

            var plateau = new PCGTerrainFeature(PCGTerrainShape.Plateau, Vector2.Zero, 200.0f, 100.0f, 0.5f);

            check("台地：肩膀以内是满高，缓坡上是一半，半径处归零",
                Near(PCGTerrainSynthesizer.Sample(plateau, 0.0f, 0.0f), 100.0f) &&
                Near(PCGTerrainSynthesizer.Sample(plateau, 100.0f, 0.0f), 100.0f) &&
                Near(PCGTerrainSynthesizer.Sample(plateau, 150.0f, 0.0f), 50.0f) &&
                Near(PCGTerrainSynthesizer.Sample(plateau, 200.0f, 0.0f), 0.0f) &&
                Near(PCGTerrainSynthesizer.Sample(plateau, 900.0f, 0.0f), 0.0f),
                $"100@0 / {PCGTerrainSynthesizer.Sample(plateau, 150.0f, 0.0f):F1}@150");

            var hardEdge = new PCGTerrainFeature(PCGTerrainShape.Plateau, Vector2.Zero, 100.0f, 40.0f);

            check("falloff 为 0 时是硬边：一直到半径上都是满高",
                Near(PCGTerrainSynthesizer.Sample(hardEdge, 99.0f, 0.0f), 40.0f) &&
                Near(PCGTerrainSynthesizer.Sample(hardEdge, 100.0f, 0.0f), 0.0f));

            var basin = new PCGTerrainFeature(PCGTerrainShape.Basin, new Vector2(500.0f, -200.0f), 300.0f, -80.0f);

            // 半径上取 0（与台地同一条边界规则：`t >= 1` 就是外面），所以"里面"要探在半径**以内**。
            check("盆地：平底是负的，半径上归零，外面是零，写正数也照样往下压",
                Near(PCGTerrainSynthesizer.Sample(basin, 500.0f, -200.0f), -80.0f) &&
                Near(PCGTerrainSynthesizer.Sample(basin, 799.0f, -200.0f), -80.0f) &&
                Near(PCGTerrainSynthesizer.Sample(basin, 800.0f, -200.0f), 0.0f) &&
                Near(PCGTerrainSynthesizer.Sample(basin, 900.0f, -200.0f), 0.0f) &&
                Near(PCGTerrainSynthesizer.Sample(
                    new PCGTerrainFeature(PCGTerrainShape.Basin, Vector2.Zero, 100.0f, 50.0f), 0.0f, 0.0f), -50.0f),
                $"中心 {PCGTerrainSynthesizer.Sample(basin, 500.0f, -200.0f):F1} / " +
                $"半径内 {PCGTerrainSynthesizer.Sample(basin, 799.0f, -200.0f):F1} / " +
                $"半径上 {PCGTerrainSynthesizer.Sample(basin, 800.0f, -200.0f):F1} / " +
                $"半径外 {PCGTerrainSynthesizer.Sample(basin, 900.0f, -200.0f):F1}");

            var dome = new PCGTerrainFeature(PCGTerrainShape.Dome, Vector2.Zero, 100.0f, 60.0f);

            check("土丘：中心满高、半径处归零，而且是一路降下来的",
                Near(PCGTerrainSynthesizer.Sample(dome, 0.0f, 0.0f), 60.0f) &&
                Near(PCGTerrainSynthesizer.Sample(dome, 100.0f, 0.0f), 0.0f) &&
                PCGTerrainSynthesizer.Sample(dome, 20.0f, 0.0f) > PCGTerrainSynthesizer.Sample(dome, 50.0f, 0.0f) &&
                PCGTerrainSynthesizer.Sample(dome, 50.0f, 0.0f) > PCGTerrainSynthesizer.Sample(dome, 90.0f, 0.0f),
                $"{PCGTerrainSynthesizer.Sample(dome, 20.0f, 0.0f):F1} / " +
                $"{PCGTerrainSynthesizer.Sample(dome, 50.0f, 0.0f):F1} / " +
                $"{PCGTerrainSynthesizer.Sample(dome, 90.0f, 0.0f):F1}");

            // ---- 叠加 -------------------------------------------------------------------------------------

            var both = new List<PCGTerrainFeature>
            {
                new PCGTerrainFeature(PCGTerrainShape.Plateau, Vector2.Zero, 200.0f, 100.0f),
                new PCGTerrainFeature(PCGTerrainShape.Basin, Vector2.Zero, 100.0f, 30.0f),
            };

            check("叠加就是求和：台地上挖个盆地 = 两者相加",
                Near(PCGTerrainSynthesizer.SampleAll(both, 0.0f, 0.0f), 70.0f) &&
                Near(PCGTerrainSynthesizer.SampleAll(both, 150.0f, 0.0f), 100.0f),
                $"{PCGTerrainSynthesizer.SampleAll(both, 0.0f, 0.0f):F1}");

            // ---- patch 网格：两端压在 patch 的两个角上，行优先（先 Z 后 X） --------------------------------

            var corner = new List<PCGTerrainFeature>
            {
                // chunkSize 1 → 5 个采样点、边长 4 → 步长 1；特征放在 (4,4)，也就是**最大角**上，半径 1。
                new PCGTerrainFeature(PCGTerrainShape.Plateau, new Vector2(4.0f, 4.0f), 1.0f, 25.0f),
            };

            float[] grid = new float[25];

            PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 4.0f, corner, 0.0f, 0.0f, 1, grid, out _);

            check("采样两端正好压在 patch 的两个角上，且行优先（最后一个点才是最大角）",
                Near(grid[0], 0.0f) && Near(grid[24], 25.0f) && Near(grid[23], 0.0f),
                $"[0]={grid[0]:F1} [23]={grid[23]:F1} [24]={grid[24]:F1}");

            // ---- 噪声与确定性 -----------------------------------------------------------------------------

            var hills = new List<PCGTerrainFeature>
            {
                new PCGTerrainFeature(PCGTerrainShape.Dome, new Vector2(500.0f, 500.0f), 400.0f, 80.0f),
            };

            float[] quiet = new float[25];
            float[] noisy = new float[25];
            float[] noisyAgain = new float[25];

            PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 600.0f, hills, 0.0f, 0.01f, 7, quiet, out _);
            PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 600.0f, hills, 12.0f, 0.01f, 7, noisy, out _);
            PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 600.0f, hills, 12.0f, 0.01f, 7, noisyAgain, out _);

            bool same = true;
            bool differs = false;
            float spread = 0.0f;
            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (int i = 0; i < 25; i++)
            {
                if (noisy[i] != noisyAgain[i])
                    same = false;

                if (Math.Abs(noisy[i] - quiet[i]) > 1e-4f)
                    differs = true;

                lowest = Math.Min(lowest, noisy[i]);
                highest = Math.Max(highest, noisy[i]);
            }

            spread = highest - lowest;

            check("同样的输入算两遍逐点相同（纯函数）", same);

            check("噪声幅度 0 与 12 的结果不同（噪声真的接上了）", differs, $"幅度差 {spread:F2}");

            // 只有噪声、没有任何特征时，地面不该还是平板——否则"起伏的平原"仍然是一片平的。
            float[] plain = new float[25];

            PCGTerrainSynthesizer.TrySynthesize(1, Vector2.Zero, 600.0f, one, 12.0f, 0.01f, 7, plain, out _);

            float plainLow = float.MaxValue;
            float plainHigh = float.MinValue;

            for (int i = 0; i < 25; i++)
            {
                plainLow = Math.Min(plainLow, plain[i]);
                plainHigh = Math.Max(plainHigh, plain[i]);
            }

            check("没有任何特征、只有噪声时，地面是起伏的（「一片温带平原」）", plainHigh - plainLow > 1e-3f,
                $"起伏 {plainHigh - plainLow:F3}");

            // ---- 写入端读回来的那一侧（纯算术，引擎里读不回来，只能在这里量） --------------------------------
            //
            // 写入端把"刚写下去的那组数"喂给采样端时会做一次换算：世界 XZ → patch 局部顶点格 → 双线性。
            // 那一侧与合成器的"顶点格 → 世界 XZ"必须是互逆的；差一格就是每个点偏 100 世界单位，
            // 而那种错在屏幕上只表现为"东西摆得不太对"，最难查。所以这里用合成器真写出来的数组，
            // 按合成器那套换算取世界位置，再用写入端的读法读回来，比对该顶点的原值。

            const int chunk = 4;
            int samples = PCGTerrainSynthesizer.SamplesPerPatch(chunk);
            const float patchExtent = 900.0f;

            var seamFeatures = new List<PCGTerrainFeature>
            {
                new PCGTerrainFeature(PCGTerrainShape.Dome, new Vector2(450.0f, 450.0f), 300.0f, 120.0f),
            };

            var seamField = new float[samples * samples];

            check("写入端读回那一侧用的数组是合成器真写出来的",
                PCGTerrainSynthesizer.TrySynthesize(chunk, Vector2.Zero, patchExtent, seamFeatures,
                    0.0f, 900.0f, 3, seamField, out string seamError),
                seamError);

            // 挑一个非顶点也非中心的顶点格，避免"碰巧相等"。
            const int col = 5;
            const int row = 11;

            float vertexWorldX = (float)col / (samples - 1) * patchExtent;
            float vertexWorldZ = (float)row / (samples - 1) * patchExtent;

            float readBack = PCG.Runtime.PCGTerrainWriter.SamplePatch(seamField, samples,
                vertexWorldX / patchExtent * (samples - 1),
                vertexWorldZ / patchExtent * (samples - 1));

            check("世界位置换算回顶点格再读出来，等于合成器写在该顶点上的值（两侧换算是互逆的）",
                Near(readBack, seamField[row * samples + col]),
                $"读回 {readBack:F4}，原值 {seamField[row * samples + col]:F4}");

            // 线性插值本身：手搭一份 2x2，中点必须是四个角的平均。
            var quad = new[] { 0.0f, 10.0f, 20.0f, 40.0f };

            check("双线性插值：2x2 的中点等于四角平均",
                Near(PCG.Runtime.PCGTerrainWriter.SamplePatch(quad, 2, 0.5f, 0.5f), 17.5f),
                PCG.Runtime.PCGTerrainWriter.SamplePatch(quad, 2, 0.5f, 0.5f).ToString("F4"));

            check("顶格坐标越界时钳到边界上（不会读到数组外）",
                Near(PCG.Runtime.PCGTerrainWriter.SamplePatch(quad, 2, -5.0f, -5.0f), 0.0f) &&
                Near(PCG.Runtime.PCGTerrainWriter.SamplePatch(quad, 2, 9.0f, 9.0f), 40.0f));

            check("数组为 null 或边长不到 2 时给 0 而不是抛（采样端拿它当「没地面」之外的值也不会崩）",
                Near(PCG.Runtime.PCGTerrainWriter.SamplePatch(null, 4, 0.0f, 0.0f), 0.0f) &&
                Near(PCG.Runtime.PCGTerrainWriter.SamplePatch(new[] { 5.0f }, 1, 0.0f, 0.0f), 0.0f));

            // ---- 洞掩码（0 = 洞、255 = 有地面）。约定来自引擎源码 TerrainPatch::CacheHolesData：
            //      `holesMaskPtr[i] = isHole ? 0 : 255`，消费方 UpdateNormalsAndHoles 的判据是 `!mask[i]`。
            //      这一组证明的是"该写什么数"，不证明引擎收下了（后者在引擎自检里）。 ----------------------

            const int holeChunk = 4;
            int holeSamples = PCGTerrainSynthesizer.SamplesPerPatch(holeChunk);
            var holeMask = new byte[holeSamples * holeSamples];
            float holeStep = 400.0f / (holeSamples - 1);

            check("洞掩码长度不对被拒，理由里给出引擎要的那个数",
                !PCGTerrainSynthesizer.TryBuildHolesMask(holeChunk, Vector2.Zero, 400.0f,
                    new List<PCGTerrainFeature>(), new byte[3], out string badMaskError) &&
                badMaskError.Contains((holeSamples * holeSamples).ToString()),
                badMaskError);

            check("洞掩码数组为 null 也被拒（不是抛）",
                !PCGTerrainSynthesizer.TryBuildHolesMask(holeChunk, Vector2.Zero, 400.0f,
                    new List<PCGTerrainFeature>(), null, out string nullMaskError),
                nullMaskError);

            // patch 边长 400、一个半径 100 的洞放在正中心 ⇒ 顶点格距 25。
            var holeAtCentre = new List<PCGTerrainFeature>
            {
                new PCGTerrainFeature(PCGTerrainShape.Dome, new Vector2(200.0f, 200.0f), 100.0f, 0.0f),
            };

            check("造得出洞掩码",
                PCGTerrainSynthesizer.TryBuildHolesMask(holeChunk, Vector2.Zero, 400.0f, holeAtCentre,
                    holeMask, out string holeError),
                holeError);

            int holeCentre = (holeSamples / 2) * holeSamples + (holeSamples / 2);

            check("洞中心那个顶点是 0（0 才是洞），四个角是 255",
                holeMask[holeCentre] == 0 &&
                holeMask[0] == 255 &&
                holeMask[holeSamples - 1] == 255 &&
                holeMask[(holeSamples - 1) * holeSamples] == 255 &&
                holeMask[holeSamples * holeSamples - 1] == 255,
                $"中心 {holeMask[holeCentre]}、角 {holeMask[0]}");

            // 硬边：距离**正好等于**半径算洞（`<=`），再出去一格就不算。
            int rimRow = holeSamples / 2 + (int)Math.Round(100.0f / holeStep);
            int insideRow = rimRow - 2;
            int outsideRow = rimRow + 2;

            check("半径上是洞、出去两格就不是（掩码是硬的，没有过渡）",
                holeMask[rimRow * holeSamples + holeSamples / 2] == 0 &&
                holeMask[insideRow * holeSamples + holeSamples / 2] == 0 &&
                holeMask[outsideRow * holeSamples + holeSamples / 2] == 255,
                $"半径 {holeMask[rimRow * holeSamples + holeSamples / 2]}、内 {holeMask[insideRow * holeSamples + holeSamples / 2]}、外 {holeMask[outsideRow * holeSamples + holeSamples / 2]}");

            // 行优先、**先 Z 后 X**：把洞挪到 (x 大, z 小)，把它按下标对称写反的那个点必须还是 255——
            // 索引写反会让这一条红（而屏幕上只表现为"洞的位置不太对"）。
            var offCentre = new List<PCGTerrainFeature>
            {
                new PCGTerrainFeature(PCGTerrainShape.Basin, new Vector2(300.0f, 100.0f), 60.0f, 0.0f),
            };

            check("造得出偏心洞掩码（上一条的正面同夹具）",
                PCGTerrainSynthesizer.TryBuildHolesMask(holeChunk, Vector2.Zero, 400.0f, offCentre,
                    holeMask, out string offError),
                offError);

            int atX = (int)Math.Round(300.0f / holeStep);
            int atZ = (int)Math.Round(100.0f / holeStep);

            check("洞落在 (x,z) 对应的那一格上，镜像格不是洞（行优先、先 Z 后 X）",
                holeMask[atZ * holeSamples + atX] == 0 &&
                holeMask[atX * holeSamples + atZ] == 255,
                $"正 {holeMask[atZ * holeSamples + atX]} / 镜像 {holeMask[atX * holeSamples + atZ]}");

            check("没有洞特征时整张掩码都是 255（不挖洞 ≠ 全挖）",
                PCGTerrainSynthesizer.TryBuildHolesMask(holeChunk, Vector2.Zero, 400.0f,
                    new List<PCGTerrainFeature>(), holeMask, out string noneError) &&
                AllSolid(holeMask),
                noneError);
        }

        /// <summary>整张掩码都是"有地面"（255）。</summary>
        private static bool AllSolid(byte[] mask)
        {
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] != 255)
                    return false;

            return true;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) =>
            !float.IsNaN(a) && Math.Abs(a - b) <= tolerance;
    }
}

// PCG 插件 - 地形合成（纯核：按写下的特征算出某个 patch 的高度场）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §9 第 6 条（方案与待决清单）。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>一块地基的形状。</summary>
    public enum PCGTerrainShape
    {
        /// <summary>台地：平台加一圈边缘过渡（"这块地是平的，边上是坡"）。</summary>
        Plateau,

        /// <summary>盆地：低于四周的平底加边缘过渡（高度取绝对值后向下）。</summary>
        Basin,

        /// <summary>土丘：余弦鼓包，从中心平滑降到边缘（"缓丘"，也是拼山脊的砖）。</summary>
        Dome,
    }

    /// <summary>
    /// 一条地形特征：什么形状、在哪儿、多大、多高、边缘多软。
    /// </summary>
    /// <remarks>
    /// 这是"描述 → 地形"里**描述**那一半：一条特征对人来说就是一句话（"这里有个半径 300 的盆地，深 80"），
    /// 对代码来说就是五个数。叠加是**求和**，所以"丘陵"是几座土丘叠在一起、"台地上的盆地"是一正一负相加，
    /// 不需要为每种组合再发明一个形状。
    /// </remarks>
    public struct PCGTerrainFeature
    {
        /// <summary>形状。</summary>
        public PCGTerrainShape Shape;

        /// <summary>中心的 X 与 Z（世界单位）。</summary>
        public Vector2 Center;

        /// <summary>影响半径（世界单位）；半径外恒为 0。</summary>
        public float Radius;

        /// <summary>幅度（世界单位）。<see cref="PCGTerrainShape.Basin"/> 取绝对值后向下压。</summary>
        public float Height;

        /// <summary>
        /// 边缘过渡占半径的比例（0..1）：0 是硬边（到半径才落回 0），0.5 是从半径的一半就开始缓下去。
        /// <see cref="PCGTerrainShape.Dome"/> 自带平滑，不看这个数。
        /// </summary>
        public float Falloff;

        public PCGTerrainFeature(PCGTerrainShape shape, Vector2 center, float radius, float height,
            float falloff = 0.0f)
        {
            Shape = shape;
            Center = center;
            Radius = radius;
            Height = height;
            Falloff = falloff;
        }
    }

    /// <summary>
    /// 把一组特征（加一层噪声）算成"某个 patch 的高度场"。**纯函数**：不碰引擎、不写文件，同样的输入永远同样的输出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么先只做纯的那一半。</b>写入引擎那一步（`Terrain.SetupPatchHeightMap`）是**破坏性**的——它会覆盖
    /// 已有地形，且引擎只写不读（`Terrain.cs` 上没有任何高度 getter），所以"写错了再读回来看看"这条路不存在。
    /// 按本仓惯例，破坏性那半要等拍板（§9 第 6 条的决定 C）；而"该写什么数"是纯数学，可以在这里逐点断言。
    /// </para>
    /// <para>
    /// <b>尺寸契约来自引擎，不来自我。</b>`SetupPatchHeightMap` 要求高度场长度等于 `(chunkSize*4+1)^2`，这里的
    /// <see cref="SamplesPerPatch"/> 就是那个式子，<see cref="TrySynthesize"/> 会**按引擎的口径**校验数组长度并把
    /// 期望值写进理由——尺寸写错在引擎里是一个静默的空地形，在这里是一条能读的报错。
    /// </para>
    /// <para>
    /// <b>高度是"世界高度"而不是归一化值。</b>引擎那边高度场怎么换成世界 Y 仍有一处未结清的标定（§附录 B.2），
    /// 所以这一层只认"我要多高"，换算留给写入端——这样标定一旦定下来，纯核不用改。
    /// </para>
    /// <para>
    /// <b>坐标是 patch 局部的。</b>调用方给 patch 的世界原点与边长，采样点按 `span/(n-1)` 均匀铺开，两端**正好压在
    /// patch 的两个角上**（与 `chunkSize*4+1` 的取法一致：n-1 段覆盖 chunkSize*4 个 chunk）。
    /// </para>
    /// </remarks>
    public static class PCGTerrainSynthesizer
    {
        /// <summary>引擎契约里的 chunk 数：一个 patch 的高度场是 `(chunkSize*4+1)^2`。</summary>
        public const int ChunksPerPatch = 4;

        /// <summary>某个 chunkSize 下，一个 patch 的高度场一边有多少个采样点。</summary>
        public static int SamplesPerPatch(int chunkSize) => Math.Max(1, chunkSize) * ChunksPerPatch + 1;

        /// <summary>
        /// 造一个 patch 的**洞掩码**：落在洞形状半径之内的顶点写 <c>0</c>，其余写 <c>255</c>。
        /// </summary>
        /// <remarks>
        /// 0/255 这两个值**不是猜的**，是从引擎源码里读出来的：`TerrainPatch::CacheHolesData` 写的是
        /// <c>holesMaskPtr[i] = isHole ? 0 : 255</c>，而消费它的 `UpdateNormalsAndHoles` 用的判据是
        /// <c>if (holesMask &amp;&amp; !holesMask[i])</c> ⇒ **0 才是洞**。（设计文档 §9 第 6 条当初把这一步
        /// 记成"只有引擎运行期能确认、不想押猜的语义"，现在语义是查源码得来的。）
        /// 掩码是**硬**的、没有过渡：洞就是"这里没有地面"。
        /// 下标口径与高度场**完全一致**（行优先、先 Z 后 X、<c>row * samples + col</c>），
        /// 差一格就是整片洞错位——而屏幕上只表现为"洞的位置不太对"。
        /// </remarks>
        public static bool TryBuildHolesMask(int chunkSize, Vector2 patchOrigin, float patchExtent,
            IReadOnlyList<PCGTerrainFeature> holes, byte[] mask, out string error)
        {
            error = null;

            if (mask == null)
            {
                error = "洞掩码数组是 null。";
                return false;
            }

            int samples = SamplesPerPatch(chunkSize);
            int expected = samples * samples;

            if (mask.Length != expected)
            {
                error = $"洞掩码长度是 {mask.Length}，而 chunkSize {chunkSize} 要的是 {samples}^2 = {expected}" +
                        "（与高度场同一个口径）。";
                return false;
            }

            if (patchExtent <= 0.0f)
            {
                error = $"patch 的边长必须为正，收到 {patchExtent}。";
                return false;
            }

            int count = holes?.Count ?? 0;
            float step = patchExtent / (samples - 1);
            int at = 0;

            for (int row = 0; row < samples; row++)
            {
                float z = patchOrigin.Y + row * step;

                for (int col = 0; col < samples; col++)
                {
                    float x = patchOrigin.X + col * step;
                    bool hole = false;

                    for (int i = 0; i < count && !hole; i++)
                    {
                        var feature = holes[i];
                        float dx = x - feature.Center.X;
                        float dz = z - feature.Center.Y;
                        hole = dx * dx + dz * dz <= feature.Radius * feature.Radius;
                    }

                    mask[at++] = hole ? (byte)0 : (byte)255;
                }
            }

            return true;
        }

        /// <summary>一条特征在某点的贡献。</summary>
        public static float Sample(PCGTerrainFeature feature, float x, float z)
        {
            float radius = Math.Max(feature.Radius, 1e-3f);
            float dx = x - feature.Center.X;
            float dz = z - feature.Center.Y;
            float t = (float)Math.Sqrt(dx * dx + dz * dz) / radius;

            if (t >= 1.0f)
                return 0.0f;

            if (feature.Shape == PCGTerrainShape.Dome)
            {
                // 余弦鼓包：中心是 1、半径处是 0，中间平滑；不需要 Falloff 这个旋钮。
                return feature.Height * 0.5f * (1.0f + (float)Math.Cos(Math.PI * t));
            }

            float falloff = Math.Min(Math.Max(feature.Falloff, 0.0f), 1.0f);
            float amplitude = feature.Shape == PCGTerrainShape.Basin
                ? -Math.Abs(feature.Height)
                : feature.Height;

            if (falloff <= 0.0f)
                return amplitude;

            float shoulder = 1.0f - falloff;

            if (t <= shoulder)
                return amplitude;

            // 肩膀之外线性收到 0：这样"半径的一半开始缓下去"是字面意思。
            return amplitude * (1.0f - (t - shoulder) / falloff);
        }

        /// <summary>所有特征在某点的贡献之和（叠加就是求和）。</summary>
        public static float SampleAll(IReadOnlyList<PCGTerrainFeature> features, float x, float z)
        {
            if (features == null)
                return 0.0f;

            float total = 0.0f;

            for (int i = 0; i < features.Count; i++)
                total += Sample(features[i], x, z);

            return total;
        }

        /// <summary>
        /// 算出一个 patch 的高度场：<paramref name="heights"/> 按行优先（先 Z 后 X）填，长度必须等于
        /// `(chunkSize*4+1)^2`。
        /// </summary>
        /// <param name="chunkSize">patch 的 chunk 数（引擎那边的口径）。</param>
        /// <param name="patchOrigin">patch 最小角的世界 XZ。</param>
        /// <param name="patchExtent">patch 的世界边长（X 与 Z 同长）。</param>
        /// <param name="features">写下的地形特征；空列表表示只有噪声（"一片起伏的平原"）。</param>
        /// <param name="noiseAmplitude">噪声幅度（世界单位）；0 表示不要噪声。</param>
        /// <param name="noiseScale">噪声频率：世界 1 单位对应噪声坐标的多少（越小越平缓）。</param>
        /// <param name="seed">噪声种子。</param>
        /// <param name="heights">要填的数组。</param>
        /// <param name="error">失败原因；成功时为 null。</param>
        /// <returns>成功为 true。</returns>
        public static bool TrySynthesize(int chunkSize, Vector2 patchOrigin, float patchExtent,
            IReadOnlyList<PCGTerrainFeature> features, float noiseAmplitude, float noiseScale, int seed,
            float[] heights, out string error)
        {
            error = null;

            if (heights == null)
            {
                error = "高度场数组是 null。";
                return false;
            }

            int samples = SamplesPerPatch(chunkSize);
            int expected = samples * samples;

            if (heights.Length != expected)
            {
                // 这一句就是引擎契约的离线版：它要的正是 (chunkSize*4+1)^2，写错的话在引擎里是一片静默的空地形。
                error = $"高度场长度是 {heights.Length}，而 chunkSize {chunkSize} 要的是 {samples}^2 = {expected}" +
                        $"（引擎的 SetupPatchHeightMap 就是这个口径）。";
                return false;
            }

            if (patchExtent <= 0.0f)
            {
                error = $"patch 的边长必须为正，收到 {patchExtent}。";
                return false;
            }

            float step = patchExtent / (samples - 1);
            int at = 0;

            for (int row = 0; row < samples; row++)
            {
                float z = patchOrigin.Y + row * step;

                for (int col = 0; col < samples; col++)
                {
                    float x = patchOrigin.X + col * step;
                    float height = SampleAll(features, x, z);

                    if (noiseAmplitude != 0.0f && noiseScale != 0.0f)
                        height += PCGSeededNoise.Fbm2D(x * noiseScale, z * noiseScale, seed) * noiseAmplitude;

                    heights[at++] = height;
                }
            }

            return true;
        }
    }
}

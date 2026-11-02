// PCG 插件 - 生物群系分类：从地形的高低与起伏判定"这里算什么地貌"
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.37。

using System;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>地貌种类。数值即写进 <c>Biome</c> 列的下标，只允许在末尾追加。</summary>
    public static class PCGBiomeKind
    {
        /// <summary>平原。</summary>
        public const int Plain = 0;

        /// <summary>丘陵。</summary>
        public const int Hill = 1;

        /// <summary>山地（含山间陡坡）。</summary>
        public const int Mountain = 2;

        /// <summary>盆地（明显低于周边、且底部平缓）。</summary>
        public const int Basin = 3;

        /// <summary>水面或水下。</summary>
        public const int Water = 4;

        /// <summary>种类数量。</summary>
        public const int Count = 5;

        /// <summary>种类的名字，写进 <c>BiomeName</c> 列。</summary>
        public static string NameOf(int kind)
        {
            switch (kind)
            {
            case Hill: return "Hill";
            case Mountain: return "Mountain";
            case Basin: return "Basin";
            case Water: return "Water";
            default: return "Plain";
            }
        }
    }

    /// <summary>分类用的阈值。全部是"设计者能调的量"，不是内部魔法数。</summary>
    public struct PCGBiomeOptions
    {
        /// <summary>低于这个高度算水（配合 <see cref="WaterMargin"/> 使用）。</summary>
        public float WaterLevel;

        /// <summary>高于水面这么多以内也算水，避免岸边抖动被分到陆地上。</summary>
        public float WaterMargin;

        /// <summary>起伏半径：在这个半径内取最大最小高度差，作为"这里地有多碎"。</summary>
        public float ReliefRadius;

        /// <summary>参照半径：在这个半径内取平均高度，用于判断"我比周围低多少"。</summary>
        public float RegionRadius;

        /// <summary>每个半径上取多少个方向采样（4 = 四正，8 = 四正四斜）。</summary>
        public int Samples;

        /// <summary>坡度超过这个度数算山地（度）。</summary>
        public float MountainSlope;

        /// <summary>起伏超过这个高度差算山地。</summary>
        public float MountainRelief;

        /// <summary>坡度超过这个度数算丘陵（度）。</summary>
        public float HillSlope;

        /// <summary>起伏超过这个高度差算丘陵。</summary>
        public float HillRelief;

        /// <summary>低于周边平均这么多米、且底部平缓，算盆地。</summary>
        public float BasinDepth;

        /// <summary>盆地底部的起伏上限（超过它就更像山谷而不是盆地）。</summary>
        public float BasinRelief;
    }

    /// <summary>
    /// 把高度场上的一个位置判成平原/丘陵/山地/盆地/水面之一。
    /// </summary>
    /// <remarks>
    /// <para>
    /// "温带平原、丘陵、盆地"这些说法要落到图里，就得先有个东西回答"这一格算什么"。答案不能只看绝对高度
    /// ——高原上的平台也是平原、海边的坡也是坡——所以这里看两件事：**起伏**（短半径内的最大最小高差，回答
    /// "地有多碎"）与**相对高度**（长半径内的平均值减自身，回答"我比周围低多少"），再加上坡度。
    /// </para>
    /// <para>
    /// 判据顺序是有意的：水最先（水下的地形长什么样都不该长树），盆地先于山地（盆底也可能有陡壁），最后才落回
    /// 平原。全部是纯函数 + 注入的高度委托，所以能对着手算地形断言——平台必须是平原、山顶必须是山地、碗底必须是盆地。
    /// </para>
    /// </remarks>
    public static class PCGBiomeClassifier
    {
        /// <summary>八个方向的固定顺序（先四正后四斜），保证采样与结果可复现。</summary>
        private static readonly float[] DirectionX = { 1, -1, 0, 0, 0.70710678f, 0.70710678f, -0.70710678f, -0.70710678f };
        private static readonly float[] DirectionZ = { 0, 0, 1, -1, 0.70710678f, -0.70710678f, 0.70710678f, -0.70710678f };

        /// <summary>一个位置上的地形特征，分类与调试都用它。</summary>
        public struct Sample
        {
            /// <summary>该点高度；未知为 <see cref="float.NaN"/>。</summary>
            public float Height;

            /// <summary>短半径内的最大最小高差。</summary>
            public float Relief;

            /// <summary>长半径内的平均高度。</summary>
            public float RegionMean;

            /// <summary>坡度（度）。</summary>
            public float Slope;

            /// <summary>该点是否拿到了高度。</summary>
            public bool Known;
        }

        /// <summary>
        /// 在给定位置上量出判类需要的三件事：起伏、周边平均、坡度。
        /// </summary>
        /// <param name="height">地形高度查询；未知返回 null。</param>
        /// <param name="x">世界 X。</param>
        /// <param name="z">世界 Z。</param>
        /// <param name="options">两个半径与采样方向数。</param>
        public static Sample Measure(Func<float, float, float?> height, float x, float z, in PCGBiomeOptions options)
        {
            var sample = new Sample { Height = float.NaN, Known = false };

            if (height == null)
                return sample;

            var centre = height(x, z);

            if (!centre.HasValue)
                return sample;

            sample.Height = centre.Value;
            sample.Known = true;

            if (options.ReliefRadius <= 0.0f || options.RegionRadius <= 0.0f)
                return sample;

            int directions = options.Samples <= 4 ? 4 : 8;
            float minimum = centre.Value;
            float maximum = centre.Value;
            double regionSum = 0.0;
            int regionCount = 0;

            for (int d = 0; d < directions; d++)
            {
                float rx = DirectionX[d] * options.ReliefRadius;
                float rz = DirectionZ[d] * options.ReliefRadius;
                var near = height(x + rx, z + rz);

                if (near.HasValue)
                {
                    minimum = Math.Min(minimum, near.Value);
                    maximum = Math.Max(maximum, near.Value);

                    // 坡度取"中心与最近邻的高差 ÷ 水平距离"，比用最大最小去估稳。
                    float rise = Math.Abs(near.Value - centre.Value);
                    float run = (float)Math.Sqrt(rx * rx + rz * rz);

                    if (run > 1e-4f)
                        sample.Slope = Math.Max(sample.Slope, (float)Math.Atan2(rise, run) * 57.29578f);
                }

                float gx = DirectionX[d] * options.RegionRadius;
                float gz = DirectionZ[d] * options.RegionRadius;
                var far = height(x + gx, z + gz);

                if (far.HasValue)
                {
                    regionSum += far.Value;
                    regionCount++;
                }
            }

            sample.Relief = maximum - minimum;
            sample.RegionMean = regionCount > 0 ? (float)(regionSum / regionCount) : centre.Value;

            return sample;
        }

        /// <summary>
        /// 按判据把一个已量好的采样点归类。
        /// </summary>
        /// <remarks>
        /// 判据顺序：**水 → 盆地 → 山地 → 丘陵 → 平原**。水最先是因为水下的地形不该长东西；盆地先于山地是因为
        /// 盆底也可以有陡壁，而"低于周边"这条对它更本质；最后落回平原，让"都不是"有一个确定的答案而不是漏判。
        /// </remarks>
        public static int Classify(in Sample sample, in PCGBiomeOptions options)
        {
            if (!sample.Known)
                return PCGBiomeKind.Plain;

            if (sample.Height <= options.WaterLevel + Math.Max(0.0f, options.WaterMargin))
                return PCGBiomeKind.Water;

            if (sample.RegionMean - sample.Height >= options.BasinDepth &&
                sample.Relief <= options.BasinRelief)
                return PCGBiomeKind.Basin;

            if (sample.Slope >= options.MountainSlope || sample.Relief >= options.MountainRelief)
                return PCGBiomeKind.Mountain;

            if (sample.Slope >= options.HillSlope || sample.Relief >= options.HillRelief)
                return PCGBiomeKind.Hill;

            return PCGBiomeKind.Plain;
        }

        /// <summary>量一次再判一次，方便调用方一行拿到结果。</summary>
        public static int Classify(
            Func<float, float, float?> height, float x, float z, in PCGBiomeOptions options, out Sample sample)
        {
            sample = Measure(height, x, z, options);
            return Classify(sample, options);
        }
    }
}

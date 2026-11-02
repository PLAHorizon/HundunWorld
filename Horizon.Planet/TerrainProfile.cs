using System;

namespace Horizon.Planet;

/// <summary>
/// 混合高度场的可调口径（§12.4）。这些参数全都进 <see cref="ComputeTerrainVersion"/>，
/// 改任何一个都必须让 terrainVersion 变，否则双端会拿不同地形互相判对方作弊。
/// </summary>
public sealed class TerrainProfile
{
    /// <summary>算法口径版本；实现变了就升它。</summary>
    public const int AlgorithmVersion = 1;

    public TerrainProfile(long radiusCm, double baseFieldStepDeg,
                          double baseGain = 1.0,
                          double minWavelengthCm = PlanetConstants.TerrainUnitsPerVertex,
                          double finestBandAmplitudeCm = 15.0,
                          double hurst = 0.75,
                          bool detailUnderWater = false,
                          bool useCatmullRom = true,
                          double intertidalBandCm = 1500.0)
    {
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm));
        if (baseFieldStepDeg <= 0) throw new ArgumentOutOfRangeException(nameof(baseFieldStepDeg));
        if (minWavelengthCm <= 0) throw new ArgumentOutOfRangeException(nameof(minWavelengthCm));

        RadiusCm = radiusCm;
        BaseFieldStepDeg = baseFieldStepDeg;
        BaseGain = baseGain;
        MinWavelengthCm = minWavelengthCm;
        FinestBandAmplitudeCm = finestBandAmplitudeCm;
        Hurst = hurst;
        DetailUnderWater = detailUnderWater;
        UseCatmullRom = useCatmullRom;
        IntertidalBandCm = intertidalBandCm;

        CellWavelengthCm = CellWavelengthForStepCm(radiusCm, baseFieldStepDeg);
        CrossWavelengthCm = 2 * CellWavelengthCm;   // λ_c：基场的 Nyquist 波长
        if (CrossWavelengthCm <= MinWavelengthCm)
            throw new ArgumentException("λ_c 必须大于最细细节波长");

        BandCount = (int)Math.Ceiling(Math.Log2(CrossWavelengthCm / MinWavelengthCm));
        WavelengthsCm = new double[BandCount];
        AmplitudesCm = new double[BandCount];
        // A_k = A_0 · (λ_k / λ_0)^H，λ_k = λ_c / 2^k：只允许 ≤ λ_c 的波长出现
        double finestScaledAmp = FinestBandAmplitudeCm * Math.Pow(CrossWavelengthCm / MinWavelengthCm, Hurst);
        for (int k = 0; k < BandCount; k++)
        {
            WavelengthsCm[k] = CrossWavelengthCm / (1 << k);
            AmplitudesCm[k] = finestScaledAmp * Math.Pow(WavelengthsCm[k] / CrossWavelengthCm, Hurst);
        }
    }

    /// <summary>
    /// 一个基场像元在星球上的地面宽（厘米）：像元角宽 × 大圆周长 / 360。
    /// 实例与 LOD 选型都走这一个定义，避免出现第二份公式。
    /// </summary>
    public static double CellWavelengthForStepCm(long radiusCm, double stepDeg)
    {
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm));
        if (!(stepDeg > 0) || !double.IsFinite(stepDeg))
            throw new ArgumentOutOfRangeException(nameof(stepDeg), stepDeg, "角宽必须是正有限值（度）");
        return 2 * Math.PI * radiusCm * stepDeg / 360.0;
    }

    public long RadiusCm { get; }

    /// <summary>基场最细一级的角宽（度），来自 manifest，不硬编码。</summary>
    public double BaseFieldStepDeg { get; }

    /// <summary>一个基场像元在星球上的地面宽（厘米）。10 倍半径下 15″ ≈ 4.63 km。</summary>
    public double CellWavelengthCm { get; }

    /// <summary>基场能提供信息的最短波长（Nyquist）：λ_c = 2 × 像元宽。</summary>
    public double CrossWavelengthCm { get; }

    public double MinWavelengthCm { get; }
    public double FinestBandAmplitudeCm { get; }
    public double BaseGain { get; }
    public double Hurst { get; }
    public bool DetailUnderWater { get; }
    public bool UseCatmullRom { get; }

    /// <summary>潮间带厚度（厘米）：基场低于 −该值时默认不再叠细节，避免把 4.63 km 的洋底抹出假起伏。</summary>
    public double IntertidalBandCm { get; }
    public int BandCount { get; }

    /// <summary>各带波长（厘米），从 λ_c 起逐层减半。</summary>
    public double[] WavelengthsCm { get; }

    /// <summary>各带幅度（厘米），随波长单调递减。</summary>
    public double[] AmplitudesCm { get; }

    /// <summary>握手与缓存键用的地形版本；任何一个可调参数或基场标识变了都必须变。</summary>
    public uint ComputeTerrainVersion(long basefieldHashHi, int basefieldVersion)
    {
        unchecked
        {
            uint hash = (uint)AlgorithmVersion;
            hash = hash * 16777619u ^ (uint)(long)Math.Round(BaseGain * 1000);
            hash = hash * 16777619u ^ (uint)(long)Math.Round(CrossWavelengthCm);
            hash = hash * 16777619u ^ (uint)(long)Math.Round(MinWavelengthCm);
            hash = hash * 16777619u ^ (uint)(long)Math.Round(FinestBandAmplitudeCm);
            hash = hash * 16777619u ^ (uint)(long)Math.Round(Hurst * 10000);
            hash = hash * 16777619u ^ (DetailUnderWater ? 0x51u : 0x2Au);
            hash = hash * 16777619u ^ (UseCatmullRom ? 0xC4u : 0xB1u);
            hash = hash * 16777619u ^ (uint)(long)Math.Round(IntertidalBandCm);
            hash = hash * 16777619u ^ (uint)basefieldVersion;
            hash = hash * 16777619u ^ (uint)basefieldHashHi;
            hash = hash * 16777619u ^ (uint)(basefieldHashHi >> 32);
            hash = hash * 16777619u ^ (uint)RadiusCm;
            hash = hash * 16777619u ^ (uint)(RadiusCm >> 32);
            return hash == 0 ? 1u : hash;
        }
    }

    /// <summary>噪声种子：由基场哈希与 profile 口径派生，保证双端同种子。</summary>
    public long ComputeNoiseSeed(long basefieldHashHi, int basefieldVersion)
    {
        unchecked
        {
            ulong seed = (ulong)basefieldHashHi * 0x9E3779B97F4A7C15UL;
            seed ^= (ulong)(uint)basefieldVersion << 32 | (uint)AlgorithmVersion;
            seed ^= (ulong)(long)Math.Round(Hurst * 10000);
            seed ^= (ulong)(long)Math.Round(FinestBandAmplitudeCm);
            return (long)seed;
        }
    }
}

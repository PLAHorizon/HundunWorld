using System;
using Horizon.Planet.Dem;

namespace Horizon.Planet;

/// <summary>
/// 混合高度场（§12.4）：真实基场给出「地球长什么样」，程序化细节给出「脚下长什么样」。
/// <para>基场只允许取最细一级（λ_c 以上没有真实信息，读粗级等于把已经丢掉的细节当基准）；
/// 细节的各带波长从 λ_c 起逐层减半，绝不越过 λ_c，否则会和基场重复计数，全球一起变糊。</para>
/// </summary>
public sealed class HybridHeightSource : IHeightSource
{
    public HybridHeightSource(DemGrid baseField, TerrainProfile profile, long basefieldHashHi, int basefieldVersion)
    {
        BaseField = baseField ?? throw new ArgumentNullException(nameof(baseField));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (!BaseField.IsFinestLevel)
            throw new ArgumentException(
                $"近景高度场的基场必须用最细一级（当前首选 {BaseField.PreferredLevel}，最细 {BaseField.Manifest.Finest.Level}）",
                nameof(baseField));
        if (Math.Abs(profile.RadiusCm - PlanetConstants.PlanetRadiusCm) > 0)
            throw new ArgumentException("profile 的星球半径必须与全局口径一致", nameof(profile));

        BasefieldHashHi = basefieldHashHi;
        BasefieldVersion = basefieldVersion;
        TerrainVersion = profile.ComputeTerrainVersion(basefieldHashHi, basefieldVersion);
        _noise = new DeterministicValueNoise(profile.ComputeNoiseSeed(basefieldHashHi, basefieldVersion));
    }

    private readonly DeterministicValueNoise _noise;

    public DemGrid BaseField { get; }
    public TerrainProfile Profile { get; }
    public long BasefieldHashHi { get; }
    public int BasefieldVersion { get; }

    /// <summary>握手里的 terrainVersion；基场或任一可调口径变化都会变。</summary>
    public uint TerrainVersion { get; }

    public int SourceVersion => unchecked((int)TerrainVersion);

    public double SampleElevationCm(in PlanetVector3 unitDirection)
    {
        double baseCm = Profile.UseCatmullRom
            ? BaseField.SampleBicubicCm(unitDirection)
            : BaseField.SampleElevationCm(unitDirection);

        double detailCm = 0;
        bool deepWater = baseCm < -Profile.IntertidalBandCm;
        if (Profile.DetailUnderWater || !deepWater)
            detailCm = SampleDetailCm(unitDirection * Profile.RadiusCm);

        return Profile.BaseGain * baseCm + detailCm;
    }

    /// <summary>
    /// 细节叠加量（厘米）。输入必须是世界坐标本身（ECEF 厘米），这样跨 patch/跨区域的同一点必然同值；
    /// 用局部顶点下标当噪声键会在地块边界造出可见接缝。
    /// </summary>
    public double SampleDetailCm(in PlanetVector3 globalCm)
    {
        double sum = 0;
        for (int k = 0; k < Profile.BandCount; k++)
            sum += Profile.AmplitudesCm[k] * _noise.Sample(globalCm, Profile.WavelengthsCm[k]);
        return sum;
    }

    /// <summary>基场单独的高程（厘米），用于"海是水面还是可行空间"这类判定，不含细节。</summary>
    public double BaseFieldElevationCm(in PlanetVector3 unitDirection) =>
        Profile.UseCatmullRom ? BaseField.SampleBicubicCm(unitDirection) : BaseField.SampleElevationCm(unitDirection);
}

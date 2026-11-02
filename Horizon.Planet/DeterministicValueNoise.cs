using System;

namespace Horizon.Planet;

/// <summary>
/// 确定性三值格点噪声：输入是 ECEF 厘米坐标与波长，输出 [-1, 1]。
/// <para>只用整数哈希与 +−×÷，不用任何平台三角/超越函数，所以同一坐标在客户端与服务端逐位一致；
/// 采样点是世界坐标本身（不是顶点下标），因此跨 patch、跨区域都不会出现接缝错位。</para>
/// </summary>
public sealed class DeterministicValueNoise
{
    private readonly ulong _seed;

    public DeterministicValueNoise(long seed) => _seed = (ulong)seed;

    /// <summary>splitmix64 混合，标准 finalizer。</summary>
    public static ulong Mix(ulong x)
    {
        unchecked
        {
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return x;
        }
    }

    /// <summary>格点 (ix, iy, iz) 上的伪随机值，映射到 [-1, 1]。</summary>
    public double LatticeValue(long ix, long iy, long iz)
    {
        unchecked
        {
            ulong h = _seed;
            h = Mix(h ^ (ulong)ix);
            h = Mix(h ^ ((ulong)iy * 0x9E3779B97F4A7C15UL));
            h = Mix(h ^ ((ulong)iz * 0xC2B2AE3D27D4EB4FUL));
            return ((h >> 11) * (1.0 / 9007199254740992.0)) * 2.0 - 1.0;
        }
    }

    /// <summary>五次平滑（quintic smoothstep），一阶导在端点为 0，插值不会在格点处产生尖角。</summary>
    public static double Smooth(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    /// <summary>指定波长上的单带值噪声。</summary>
    public double Sample(in PlanetVector3 positionCm, double wavelengthCm)
    {
        double sx = positionCm.X / wavelengthCm;
        double sy = positionCm.Y / wavelengthCm;
        double sz = positionCm.Z / wavelengthCm;

        long ix0 = (long)Math.Floor(sx), iy0 = (long)Math.Floor(sy), iz0 = (long)Math.Floor(sz);
        double fx = Smooth(sx - ix0), fy = Smooth(sy - iy0), fz = Smooth(sz - iz0);

        double v000 = LatticeValue(ix0, iy0, iz0);
        double v100 = LatticeValue(ix0 + 1, iy0, iz0);
        double v010 = LatticeValue(ix0, iy0 + 1, iz0);
        double v110 = LatticeValue(ix0 + 1, iy0 + 1, iz0);
        double v001 = LatticeValue(ix0, iy0, iz0 + 1);
        double v101 = LatticeValue(ix0 + 1, iy0, iz0 + 1);
        double v011 = LatticeValue(ix0, iy0 + 1, iz0 + 1);
        double v111 = LatticeValue(ix0 + 1, iy0 + 1, iz0 + 1);

        double x00 = v000 + (v100 - v000) * fx;
        double x10 = v010 + (v110 - v010) * fx;
        double x01 = v001 + (v101 - v001) * fx;
        double x11 = v011 + (v111 - v011) * fx;
        double y0 = x00 + (x10 - x00) * fy;
        double y1 = x01 + (x11 - x01) * fy;
        return y0 + (y1 - y0) * fz;
    }
}

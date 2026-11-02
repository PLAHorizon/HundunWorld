using System;

namespace Horizon.Planet;

/// <summary>经纬度与单位方向的换算（ECEF 约定：+Z 指向北极，+X 指向 0° 经线，赤道面为 XY）。</summary>
public static class Geodetic
{
    public static PlanetVector3 DirectionFromLatLon(double latRadians, double lonRadians)
    {
        double cosLat = Math.Cos(latRadians);
        return new PlanetVector3(
            cosLat * Math.Cos(lonRadians),
            cosLat * Math.Sin(lonRadians),
            Math.Sin(latRadians));
    }

    /// <summary>任意非零向量 → 经纬度（弧度）；方向不必预先单位化。</summary>
    public static void LatLonFromDirection(in PlanetVector3 vector, out double latRadians, out double lonRadians)
    {
        double len = vector.Length;
        if (len <= 1e-300) throw new ArgumentException("零向量没有经纬度", nameof(vector));
        double sinLat = vector.Z / len;
        if (sinLat > 1.0) sinLat = 1.0;
        else if (sinLat < -1.0) sinLat = -1.0;
        latRadians = Math.Asin(sinLat);
        lonRadians = Math.Atan2(vector.Y, vector.X);
    }
}

/// <summary>高程采样接口。P0 的实现是 GEBCO 采样器；程序化细节层另接。</summary>
public interface IHeightSource
{
    /// <summary>数据源版本（进协议与缓存键）。</summary>
    int SourceVersion { get; }

    /// <summary>给定单位方向，返回相对海平面的高程（厘米）。</summary>
    double SampleElevationCm(in PlanetVector3 unitDirection);
}

/// <summary>恒定高程面（测试/占位用）。</summary>
public sealed class ConstantHeightSource : IHeightSource
{
    private readonly double _elevationCm;

    public ConstantHeightSource(double elevationCm, int sourceVersion = 0)
    {
        _elevationCm = elevationCm;
        SourceVersion = sourceVersion;
    }

    public int SourceVersion { get; }

    public double SampleElevationCm(in PlanetVector3 unitDirection) => _elevationCm;
}

/// <summary>半径 + 高程源 = 可换算的地表。</summary>
public sealed class PlanetTerrain
{
    public long RadiusCm { get; }
    public IHeightSource Heights { get; }

    public PlanetTerrain(long radiusCm, IHeightSource heights)
    {
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm));
        RadiusCm = radiusCm;
        Heights = heights ?? throw new ArgumentNullException(nameof(heights));
    }

    public double ElevationCm(in PlanetVector3 unitDirection) => Heights.SampleElevationCm(unitDirection);

    /// <summary>单位方向 → 地表点（ECEF 厘米，double）。</summary>
    public PlanetVector3 SurfacePointCm(in PlanetVector3 unitDirection) =>
        unitDirection * (RadiusCm + Heights.SampleElevationCm(unitDirection));

    /// <summary>经纬度 → 地表点（ECEF 厘米，double）。</summary>
    public PlanetVector3 SurfacePointCmByLatLon(double latRadians, double lonRadians) =>
        SurfacePointCm(Geodetic.DirectionFromLatLon(latRadians, lonRadians));

    /// <summary>地表点（ECEF 厘米）→ 单位方向 + 高程。</summary>
    public void UnprojectSurfacePoint(in PlanetVector3 surfaceCm, out PlanetVector3 unitDirection, out double elevationCm)
    {
        double len = surfaceCm.Length;
        if (len <= 1e-300) throw new ArgumentException("零向量不在星球表面上", nameof(surfaceCm));
        unitDirection = surfaceCm / len;
        elevationCm = len - RadiusCm;
    }

    /// <summary>把“沿方向抬升 h 厘米”的点转成整数 ECEF。</summary>
    public EcefCentimeters ToEcef(in PlanetVector3 surfaceCm) => EcefCentimeters.FromVector(surfaceCm);
}

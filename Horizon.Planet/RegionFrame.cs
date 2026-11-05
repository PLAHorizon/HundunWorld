using System;

namespace Horizon.Planet;

/// <summary>
/// 区域切平面坐标系：原点落在区域中心的海平面点，局部轴 X=东、Y=上、Z=南（与工程既有“北 = −Z、东 = +X”口径一致）。
/// <para>所有交给 Flax 的坐标都是这里的局部坐标（厘米，float 量级），全局 ECEF 只在 double/int64 层流动。</para>
/// </summary>
public readonly struct RegionFrame
{
    public RegionKey Key { get; }
    public long RadiusCm { get; }

    /// <summary>区域中心的单位方向（= 局部 Y 轴）。</summary>
    public PlanetVector3 Up { get; }

    /// <summary>区域中心的海平面原点（ECEF 厘米，double）。</summary>
    public PlanetVector3 OriginCm { get; }

    /// <summary>同一原点的整数厘米形式（协议/持久化用）。</summary>
    public EcefCentimeters Origin => EcefCentimeters.FromVector(OriginCm);

    public PlanetVector3 East { get; }
    public PlanetVector3 South { get; }

    private RegionFrame(RegionKey key, long radiusCm, PlanetVector3 up, PlanetVector3 originCm,
                        PlanetVector3 east, PlanetVector3 south)
    {
        Key = key;
        RadiusCm = radiusCm;
        Up = up;
        OriginCm = originCm;
        East = east;
        South = south;
    }

    public static RegionFrame Build(RegionKey key, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        PlanetVector3 up = key.CenterDirection();
        PlanetVector3 origin = up * radiusCm;
        PlanetVector3 east = ComputeEast(up, key.Face);
        PlanetVector3 south = PlanetVector3.Cross(east, up).Normalized();
        return new RegionFrame(key, radiusCm, up, origin, east, south);
    }

    /// <summary>
    /// 区域的局部东轴。两种口径，按<em>这个面本身</em>选，而不是按点选：
    /// <list type="number">
    /// <item><description><b>法线不平行极轴的面（0..3）</b>：地理东 = 极轴 × 上。
    /// 在这一族面里 up 与极轴至少差 45°，叉积不会退化，整个面内连续。</description></item>
    /// <item><description><b>法线就是极轴的面（±Z 那两个面）</b>：改用本面 u 轴在切平面上的投影。
    /// 地理东在这两个面的中心附近是<em>奇异</em>的——|极轴 × 上| = sinθ 可以小到 5e-4，
    /// "东"于是改由经度单独决定，一块 6 km 见方的区域里经度能变化好几弧度，
    /// 实测相邻区域的东轴差 <b>90°</b>（§13.47）。按点判退化（原来那条 1e-12 的阈值）救不了：
    /// 它只在恰好压在极点上才触发，而奇异的是整整一片邻域；按点切换还会把不连续挪到一个圈上。</description></item>
    /// </list>
    /// <para>选"按面"而不是"按点"的理由是这条判据要的东西：<em>同面内</em>相邻区域的帧必须连续。
    /// 跨面本来就要换帧（立方体在那里折 90°），跨面邻居由全局点反查重算（§13.15），不靠帧之间的连续性。</para>
    /// </summary>
    public static PlanetVector3 ComputeEast(in PlanetVector3 up, int face)
    {
        double polarAlignment = Math.Abs(PlanetVector3.Dot(CubeSphere.FaceNormal(face), PlanetVector3.AxisZ));
        if (polarAlignment > 0.9)
            return ProjectFaceU(up, face);

        PlanetVector3 east = PlanetVector3.Cross(PlanetVector3.AxisZ, up).Normalized();
        if (east.LengthSquared < 1e-12) return ProjectFaceU(up, face);   // 恰好压在极点上：本来就没有唯一东向
        return east;
    }

    /// <summary>本面 u 轴投影到该点的切平面：面内处处连续，且与 uv 网格 / RegionEdge 的语义天然一致。</summary>
    private static PlanetVector3 ProjectFaceU(in PlanetVector3 up, int face)
    {
        PlanetVector3 u = CubeSphere.FaceUAxis(face);
        return (u - up * PlanetVector3.Dot(u, up)).Normalized();
    }

    /// <summary>ECEF 厘米（double）→ 局部厘米。</summary>
    public PlanetVector3 LocalFromGlobalCm(in PlanetVector3 globalCm)
    {
        PlanetVector3 d = globalCm - OriginCm;
        return new PlanetVector3(
            PlanetVector3.Dot(d, East),
            PlanetVector3.Dot(d, Up),
            PlanetVector3.Dot(d, South));
    }

    /// <summary>局部厘米 → ECEF 厘米（double）。</summary>
    public PlanetVector3 GlobalFromLocalCm(in PlanetVector3 localCm) =>
        OriginCm + East * localCm.X + Up * localCm.Y + South * localCm.Z;

    public EcefCentimeters GlobalFromLocal(in PlanetVector3 localCm) =>
        EcefCentimeters.FromVector(GlobalFromLocalCm(localCm));

    public PlanetVector3 LocalFromEcef(in EcefCentimeters point) =>
        LocalFromGlobalCm(point.ToVector());

    /// <summary>局部点到原点的水平距离（厘米），用于 rebase 触发与曲率下垂估算。</summary>
    public double HorizontalDistanceCm(in PlanetVector3 localCm) =>
        Math.Sqrt(localCm.X * localCm.X + localCm.Z * localCm.Z);
}

/// <summary>区域几何度量：cube-sphere 的瓦片度量在面中心和面边缘不同，这里全部按大圆实测。</summary>
public static class RegionMetrics
{
    /// <summary>区域沿 u 方向的中心跨度（大圆厘米）。</summary>
    public static double CenterWidthCm(RegionKey key, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double v = (v0 + v1) * 0.5;
        return CubeSphere.GreatCircleCm(
            CubeSphere.ToDirection(key.Face, u0, v),
            CubeSphere.ToDirection(key.Face, u1, v),
            radiusCm);
    }

    /// <summary>区域沿 v 方向的中心跨度（大圆厘米）。</summary>
    public static double CenterHeightCm(RegionKey key, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double u = (u0 + u1) * 0.5;
        return CubeSphere.GreatCircleCm(
            CubeSphere.ToDirection(key.Face, u, v0),
            CubeSphere.ToDirection(key.Face, u, v1),
            radiusCm);
    }

    /// <summary>区域中心到最远角的球面距离（厘米）——局部平面的最大跨度，决定曲率下垂上界。</summary>
    public static double CenterToCornerCm(RegionKey key, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        PlanetVector3 c = key.CenterDirection();
        double max = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            double d = CubeSphere.GreatCircleCm(c, key.CornerDirection(corner), radiusCm);
            if (d > max) max = d;
        }
        return max;
    }

    /// <summary>把切平面“抬”回球面所需的下垂量（厘米）：R − √(R² − d²)。</summary>
    public static double CurvatureSagCm(double horizontalCm, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (horizontalCm < 0) horizontalCm = -horizontalCm;
        if (horizontalCm >= radiusCm) return radiusCm;
        double r = radiusCm;
        return r - Math.Sqrt(r * r - horizontalCm * horizontalCm);
    }
}

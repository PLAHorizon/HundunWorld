using System;
using System.Runtime.CompilerServices;

namespace Horizon.Planet;

/// <summary>立方体球（cube-sphere）映射：六个面各覆盖 90°×90° 立体角，uv ∈ [−1, 1]。</summary>
public static class CubeSphere
{
    public const int FaceCount = 6;

    /// <summary>面法线（外指）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PlanetVector3 FaceNormal(int face) => face switch
    {
        0 => PlanetVector3.AxisX,
        1 => -PlanetVector3.AxisX,
        2 => PlanetVector3.AxisY,
        3 => -PlanetVector3.AxisY,
        4 => PlanetVector3.AxisZ,
        5 => -PlanetVector3.AxisZ,
        _ => throw new ArgumentOutOfRangeException(nameof(face), face, "face 必须在 0..5"),
    };

    /// <summary>面的 u 轴（FaceNormal × uAxis × vAxis 构成右手系，见 CubeSphereAxisTable）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PlanetVector3 FaceUAxis(int face) => face switch
    {
        0 => new PlanetVector3(0, 0, -1),
        1 => new PlanetVector3(0, 0, 1),
        2 => PlanetVector3.AxisX,
        3 => PlanetVector3.AxisX,
        4 => PlanetVector3.AxisX,
        5 => -PlanetVector3.AxisX,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PlanetVector3 FaceVAxis(int face) => face switch
    {
        0 => PlanetVector3.AxisY,
        1 => PlanetVector3.AxisY,
        2 => new PlanetVector3(0, 0, -1),
        3 => PlanetVector3.AxisZ,
        4 => PlanetVector3.AxisY,
        5 => PlanetVector3.AxisY,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    /// <summary>uv → 单位方向。中心 (0,0) 落在面法线上，边角落在 45° 处。</summary>
    public static PlanetVector3 ToDirection(int face, double u, double v) =>
        (FaceNormal(face) + FaceUAxis(face) * u + FaceVAxis(face) * v).Normalized();

    /// <summary>方向所属面：取绝对值最大的分量。方向必须非零。</summary>
    public static int FaceOf(in PlanetVector3 direction)
    {
        double ax = Math.Abs(direction.X), ay = Math.Abs(direction.Y), az = Math.Abs(direction.Z);
        if (ax >= ay && ax >= az) return direction.X >= 0 ? 0 : 1;
        if (ay >= az) return direction.Y >= 0 ? 2 : 3;
        return direction.Z >= 0 ? 4 : 5;
    }

    /// <summary>方向 → 指定面的 uv；方向不在该面立体角内（|u| 或 |v| 明显 &gt; 1）时返回 false。</summary>
    public static bool TryToUv(in PlanetVector3 direction, int face, out double u, out double v)
    {
        u = 0;
        v = 0;
        double denom = PlanetVector3.Dot(direction, FaceNormal(face));
        if (Math.Abs(denom) < 1e-12) return false;
        u = PlanetVector3.Dot(direction, FaceUAxis(face)) / denom;
        v = PlanetVector3.Dot(direction, FaceVAxis(face)) / denom;
        return u >= -1.000001 && u <= 1.000001 && v >= -1.000001 && v <= 1.000001;
    }

    /// <summary>方向 → (面, u, v)，面由主导轴决定，因此 uv 一定在 [−1, 1] 内。</summary>
    public static void ToFaceUv(in PlanetVector3 direction, out int face, out double u, out double v)
    {
        face = FaceOf(direction);
        if (!TryToUv(direction, face, out u, out v))
            throw new InvalidOperationException($"方向 {direction} 无法落在面 {face} 的 uv 范围内");
    }

    /// <summary>球面大圆距离（厘米）；两个单位方向的夹角 × 星球半径。</summary>
    public static double GreatCircleCm(in PlanetVector3 unitA, in PlanetVector3 unitB, long radiusCm) =>
        PlanetVector3.AngleBetween(unitA, unitB) * radiusCm;
}

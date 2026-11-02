using System;
using System.Runtime.CompilerServices;

namespace Horizon.Planet;

/// <summary>双精度三维向量（ECEF 厘米空间用；刻意不引 System.Numerics，避免跨端按位差异与单精度截断）。</summary>
public readonly struct PlanetVector3 : IEquatable<PlanetVector3>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public PlanetVector3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static readonly PlanetVector3 Zero = new(0, 0, 0);
    public static readonly PlanetVector3 AxisX = new(1, 0, 0);
    public static readonly PlanetVector3 AxisY = new(0, 1, 0);
    public static readonly PlanetVector3 AxisZ = new(0, 0, 1);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(in PlanetVector3 a, in PlanetVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PlanetVector3 Cross(in PlanetVector3 a, in PlanetVector3 b) =>
        new(a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    public static PlanetVector3 operator +(in PlanetVector3 a, in PlanetVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static PlanetVector3 operator -(in PlanetVector3 a, in PlanetVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static PlanetVector3 operator *(in PlanetVector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static PlanetVector3 operator /(in PlanetVector3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public static PlanetVector3 operator -(in PlanetVector3 a) => new(-a.X, -a.Y, -a.Z);

    /// <summary>单位化；零向量返回 Zero（调用方需自己判退化，典型是极点处取切线）。</summary>
    public PlanetVector3 Normalized()
    {
        double len = Length;
        if (len <= 1e-300) return Zero;
        return new PlanetVector3(X / len, Y / len, Z / len);
    }

    /// <summary>与单位向量的夹角（弧度），用 atan2 形式避免 asin 在近平行时丢精度。</summary>
    public static double AngleBetween(in PlanetVector3 a, in PlanetVector3 b)
    {
        double cross = Cross(a, b).Length;
        double dot = Dot(a, b);
        return Math.Atan2(cross, dot);
    }

    public bool Equals(PlanetVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is PlanetVector3 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
}

/// <summary>星球上的权威点位置：ECEF 整数厘米。10 倍地球半径量级下 int64 有余量。</summary>
public readonly struct EcefCentimeters : IEquatable<EcefCentimeters>
{
    public readonly long X;
    public readonly long Y;
    public readonly long Z;

    public EcefCentimeters(long x, long y, long z) { X = x; Y = y; Z = z; }

    public PlanetVector3 ToVector() => new(X, Y, Z);

    public static EcefCentimeters FromVector(in PlanetVector3 cm) =>
        new(Round(cm.X), Round(cm.Y), Round(cm.Z));

    private static long Round(double v) => (long)(v >= 0 ? v + 0.5 : v - 0.5);

    public bool Equals(EcefCentimeters other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is EcefCentimeters e && Equals(e);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"Ecef({X},{Y},{Z})";
}

using System;

namespace Horizon.Planet;

/// <summary>
/// 星球区域键：cube-sphere 的某一面、某一层级、某一块瓦片。
/// <para><see cref="ToId"/> 打包成非负 long：face 3 bit（bit 58–60）｜level 4 bit（bit 54–57）｜x 27 bit｜y 27 bit。</para>
/// </summary>
public readonly struct RegionKey : IEquatable<RegionKey>
{
    public const int MaxLevel = 15;
    public const int TileBits = 27;
    private const long TileMask = (1L << TileBits) - 1;

    public int Face { get; }
    public int Level { get; }
    public int X { get; }
    public int Y { get; }

    public RegionKey(int face, int level, int x, int y)
    {
        if (face < 0 || face >= CubeSphere.FaceCount)
            throw new ArgumentOutOfRangeException(nameof(face), face, "face 必须在 0..5");
        if (level < 0 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, "level 必须在 0..15");
        int span = 1 << level;
        if (x < 0 || x >= span)
            throw new ArgumentOutOfRangeException(nameof(x), x, $"level {level} 下 x 必须在 0..{span - 1}");
        if (y < 0 || y >= span)
            throw new ArgumentOutOfRangeException(nameof(y), y, $"level {level} 下 y 必须在 0..{span - 1}");

        Face = face;
        Level = level;
        X = x;
        Y = y;
    }

    /// <summary>该瓦片在面上的 uv 区间（[0,1] 归一的左下角比例）。</summary>
    public (double U0, double U1, double V0, double V1) UvBounds()
    {
        double span = 1 << Level;
        double u0 = -1.0 + 2.0 * X / span;
        double v0 = -1.0 + 2.0 * Y / span;
        double step = 2.0 / span;
        return (u0, u0 + step, v0, v0 + step);
    }

    /// <summary>瓦片中心的单位方向。</summary>
    public PlanetVector3 CenterDirection()
    {
        var (u0, u1, v0, v1) = UvBounds();
        return CubeSphere.ToDirection(Face, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
    }

    /// <summary>瓦片四角的单位方向（顺序：(u0,v0) (u1,v0) (u0,v1) (u1,v1)）。</summary>
    public PlanetVector3 CornerDirection(int corner)
    {
        var (u0, u1, v0, v1) = UvBounds();
        double u = (corner & 1) == 0 ? u0 : u1;
        double v = (corner & 2) == 0 ? v0 : v1;
        return CubeSphere.ToDirection(Face, u, v);
    }

    public long ToId()
    {
        long id = (long)Face << 58;
        id |= (long)Level << 54;
        id |= (long)X << 27;
        id |= (long)Y;
        return id;
    }

    public static RegionKey FromId(long id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id), id, "RegionId 必须是非负 long");
        int face = (int)((id >> 58) & 0x7);
        int level = (int)((id >> 54) & 0xF);
        int x = (int)((id >> 27) & TileMask);
        int y = (int)(id & TileMask);
        return new RegionKey(face, level, x, y);
    }

    /// <summary>把任意单位方向归到指定层级的区域。</summary>
    public static RegionKey FromDirection(in PlanetVector3 direction, int level)
    {
        CubeSphere.ToFaceUv(direction, out int face, out double u, out double v);
        int span = 1 << level;
        int x = (int)Math.Floor((u + 1.0) * 0.5 * span);
        int y = (int)Math.Floor((v + 1.0) * 0.5 * span);
        if (x >= span) x = span - 1;
        if (y >= span) y = span - 1;
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        return new RegionKey(face, level, x, y);
    }

    /// <summary>同层级相邻区域的 x/y 偏移（可用于枚举邻区）。</summary>
    public bool TryNeighbour(int dx, int dy, out RegionKey neighbour)
    {
        neighbour = default;
        int span = 1 << Level;
        int x = X + dx, y = Y + dy;
        if (x < 0 || x >= span || y < 0 || y >= span) return false;
        neighbour = new RegionKey(Face, Level, x, y);
        return true;
    }

    public bool Equals(RegionKey other) =>
        Face == other.Face && Level == other.Level && X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is RegionKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(Face, Level, X, Y);
    public override string ToString() => $"Region(f{Face} L{Level} {X},{Y})";

    public static bool operator ==(RegionKey a, RegionKey b) => a.Equals(b);
    public static bool operator !=(RegionKey a, RegionKey b) => !a.Equals(b);
}

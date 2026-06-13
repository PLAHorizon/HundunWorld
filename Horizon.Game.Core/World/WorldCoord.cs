using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Core.World;

/// <summary>
/// 世界坐标与 ChunkCell 坐标的单位换算（P2-a）。<br/>
/// 所有 AOI / 订阅/持久化均以 <see cref="ChunkCoord"/>（整数 3D 块坐标）为粒度；
/// 渲染侧浮点坐标可通过 <see cref="ToChunk"/> 一次换算。
/// </summary>
public static class WorldCoord
{
    /// <summary>ChunkCell 边长（米）。改动此值等于破坏兼容性，需同步协议版本。</summary>
    public const float MetresPerChunkCell = 16f;

    /// <summary>1 / <see cref="MetresPerChunkCell"/>，预计算以避免循环中除法。</summary>
    public const float InverseMetresPerChunkCell = 1f / MetresPerChunkCell;

    /// <summary>把 (x, y, z) 浮点世界坐标换算到 ChunkCell 整数坐标（<see cref="MathF.Floor"/> 语义）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChunkCoord ToChunk(float x, float y, float z)
    {
        return new ChunkCoord(
            FloorToInt(x * InverseMetresPerChunkCell),
            FloorToInt(y * InverseMetresPerChunkCell),
            FloorToInt(z * InverseMetresPerChunkCell));
    }

    /// <summary>把 (x, y, z) 浮点世界坐标换算到 ChunkCell Morton 键。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToChunkMortonKey(float x, float y, float z)
    {
        var c = ToChunk(x, y, z);
        return MortonCodec.Encode3D(c.X, c.Y, c.Z);
    }

    /// <summary>ChunkCoord → ChunkCell 原点坐标（左下角，单位米）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float X, float Y, float Z) ChunkOriginMetres(ChunkCoord c) =>
        (c.X * MetresPerChunkCell, c.Y * MetresPerChunkCell, c.Z * MetresPerChunkCell);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorToInt(float f)
    {
        // MathF.Floor 再 cast，避免 (int) 对负数的截零行为差异
        return (int)MathF.Floor(f);
    }
}

/// <summary>整数 3D 块坐标（不可变、可做 dict key）。</summary>
public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    public ChunkCoord(int x, int y, int z) { X = x; Y = y; Z = z; }

    /// <summary>按本坐标编码的 Morton 键。</summary>
    public ulong MortonKey => MortonCodec.Encode3D(X, Y, Z);

    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is ChunkCoord o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"Chunk({X},{Y},{Z})";

    public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);
    public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
}

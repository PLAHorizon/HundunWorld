using System;
using System.Collections.Generic;
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

    /// <summary>
    /// 返回以 (centerChunkX, centerChunkY, centerChunkZ) 为中心，半径 radius 内的所有 chunk 的 MortonKey 集合。
    /// <para>
    /// 语义：以中心 chunk 为原点，沿 X/Y/Z 三轴各 ±radius 的立方体覆盖的所有 chunk。
    /// <c>radius=0</c> 表示只包含中心 chunk；<c>radius=1</c> 表示 3x3x3=27 chunk。
    /// </para>
    /// <para>
    /// 用于客户端 AOI 视野计算：玩家所在 chunk 经 <see cref="ToChunk"/> 得到中心坐标，
    /// 再以客户端配置的 ViewRadiusChunks 为 radius 调用本方法得到订阅 chunk 集合。
    /// </para>
    /// <para>
    /// 注意：底层 <see cref="MortonCodec.Encode3D"/> 会做范围校验，若中心 ± radius 超出
    /// <see cref="MortonCodec.AxisMin"/>~<see cref="MortonCodec.AxisMax"/> 会抛 <see cref="ArgumentOutOfRangeException"/>。
    /// 正常游戏场景下玩家位置远离世界边界，不会触发。
    /// </para>
    /// </summary>
    /// <param name="centerChunkX">中心 chunk 的 X 轴坐标（整数块坐标）。</param>
    /// <param name="centerChunkY">中心 chunk 的 Y 轴坐标。</param>
    /// <param name="centerChunkZ">中心 chunk 的 Z 轴坐标。</param>
    /// <param name="radius">视野半径（chunk 数），<c>&lt; 0</c> 视为 <c>0</c>（仅中心）。</param>
    /// <returns>覆盖范围内所有 chunk 的 MortonKey 集合。</returns>
    public static HashSet<ulong> GetChunksInView(int centerChunkX, int centerChunkY, int centerChunkZ, int radius)
    {
        // 防御性处理：负数半径等价于 0（仅中心 chunk）
        if (radius < 0)
        {
            radius = 0;
        }

        var chunks = new HashSet<ulong>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    chunks.Add(MortonCodec.Encode3D(centerChunkX + dx, centerChunkY + dy, centerChunkZ + dz));
                }
            }
        }
        return chunks;
    }

    /// <summary>
    /// 从世界坐标（浮点）计算视野范围内的所有 chunk 的 MortonKey 集合。
    /// 内部先经 <see cref="ToChunk"/> 换算到整数块坐标，再调用 <see cref="GetChunksInView(int, int, int, int)"/>。
    /// </summary>
    /// <param name="worldX">世界 X 坐标（米）。</param>
    /// <param name="worldY">世界 Y 坐标（米）。</param>
    /// <param name="worldZ">世界 Z 坐标（米）。</param>
    /// <param name="radius">视野半径（chunk 数），<c>&lt; 0</c> 视为 <c>0</c>（仅中心）。</param>
    /// <returns>覆盖范围内所有 chunk 的 MortonKey 集合。</returns>
    public static HashSet<ulong> GetChunksInView(float worldX, float worldY, float worldZ, int radius)
    {
        var c = ToChunk(worldX, worldY, worldZ);
        return GetChunksInView(c.X, c.Y, c.Z, radius);
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

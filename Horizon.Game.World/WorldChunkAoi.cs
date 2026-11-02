using System;
using System.Collections.Generic;

namespace Horizon.Game.Core.World;

/// <summary>
/// AOI 视野集计算（服务端）：<see cref="WorldCoord"/> 只保留跨端共享的分块换算，
/// 视野集是 Zone 分片订阅/扇出的服务端逻辑，单独留在本程序集。
/// </summary>
public static class WorldChunkAoi
{
    /// <summary>
    /// 返回以 (centerChunkX, centerChunkY, centerChunkZ) 为中心，半径 radius 内的所有 chunk 的 MortonKey 集合。
    /// <para>
    /// 语义：以中心 chunk 为原点，沿 X/Y/Z 三轴各 ±radius 的立方体覆盖的所有 chunk。
    /// <c>radius=0</c> 表示只包含中心 chunk；<c>radius=1</c> 表示 3x3x3=27 chunk。
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
        FillChunksInView(chunks, centerChunkX, centerChunkY, centerChunkZ, radius);
        return chunks;
    }

    /// <summary>
    /// 将视野范围内的所有 chunk 的 MortonKey 填入提供的 buffer（先清空再填充）。
    /// 零分配重载：适用于高频调用路径（如 ZoneShardGrain.UpdateSessionPositionAsync），
    /// 消除每次调用创建 185,193 条目 HashSet 的 ~3-4MB 分配。
    /// 调用方需保证 buffer 不被并发访问（Orleans grain 单线程模型天然保证）。
    /// </summary>
    /// <param name="buffer">复用的 HashSet 缓冲区（会被清空后重新填充）。</param>
    /// <param name="centerChunkX">中心 chunk 的 X 轴坐标。</param>
    /// <param name="centerChunkY">中心 chunk 的 Y 轴坐标。</param>
    /// <param name="centerChunkZ">中心 chunk 的 Z 轴坐标。</param>
    /// <param name="radius">视野半径（chunk 数）。</param>
    public static void FillChunksInView(HashSet<ulong> buffer, int centerChunkX, int centerChunkY, int centerChunkZ, int radius)
    {
        if (radius < 0) radius = 0;
        buffer.Clear();
        // 预估容量避免频繁扩容：HashSet 内部按质数容量分配，预估略大于实际可减少 rehash
        var estimated = (2 * radius + 1) * (2 * radius + 1) * (2 * radius + 1);
        buffer.EnsureCapacity(estimated);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    buffer.Add(MortonCodec.Encode3D(centerChunkX + dx, centerChunkY + dy, centerChunkZ + dz));
                }
            }
        }
    }

    /// <summary>
    /// 从世界坐标（浮点）计算视野范围内的所有 chunk 的 MortonKey 集合。
    /// 内部先经 <see cref="WorldCoord.ToChunk"/> 换算到整数块坐标，再按整数块坐标取视野集。
    /// </summary>
    /// <param name="worldX">世界 X 坐标（米）。</param>
    /// <param name="worldY">世界 Y 坐标（米）。</param>
    /// <param name="worldZ">世界 Z 坐标（米）。</param>
    /// <param name="radius">视野半径（chunk 数），<c>&lt; 0</c> 视为 <c>0</c>（仅中心）。</param>
    /// <returns>覆盖范围内所有 chunk 的 MortonKey 集合。</returns>
    public static HashSet<ulong> GetChunksInView(float worldX, float worldY, float worldZ, int radius)
    {
        var c = WorldCoord.ToChunk(worldX, worldY, worldZ);
        return GetChunksInView(c.X, c.Y, c.Z, radius);
    }
}

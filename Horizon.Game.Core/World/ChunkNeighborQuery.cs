using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Core.World;

/// <summary>
/// ChunkCell 邻域查询器（P2-a）。<br/>
/// 以一个中心 chunk 为原点，返回"中心 ± radius(块)"立方体内的所有 chunk 的 Morton 键，
/// 供玩家 AOI 订阅使用。
/// </summary>
/// <remarks>
/// - 纯函数、无分配（通过 yield）；GC 友好。<br/>
/// - 当 <c>radiusMetres</c> 为 0 时，只返回中心 chunk 本身。<br/>
/// - 边界情况：若邻域超出 <see cref="MortonCodec.AxisMin"/>/<see cref="MortonCodec.AxisMax"/>，
/// 超出部分会被跳过而不是抛异常。
/// </remarks>
public static class ChunkNeighborQuery
{
    /// <summary>
    /// 返回以 <paramref name="center"/> 为中心、<paramref name="radiusMetres"/> 米半径覆盖的
    /// 所有 ChunkCell Morton 键。包含中心自身。
    /// </summary>
    public static IEnumerable<ulong> QueryMortonKeys(ChunkCoord center, float radiusMetres)
    {
        var r = MetresToChunkRadius(radiusMetres);
        return QueryMortonKeys(center, r);
    }

    /// <summary>
    /// 返回以 <paramref name="center"/> 为中心、<paramref name="radiusChunks"/> 块为曼哈顿半径覆盖的所有 Morton 键。
    /// </summary>
    public static IEnumerable<ulong> QueryMortonKeys(ChunkCoord center, int radiusChunks)
    {
        if (radiusChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusChunks), radiusChunks, "半径不能为负。");

        for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
        for (int dy = -radiusChunks; dy <= radiusChunks; dy++)
        for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
        {
            int cx = center.X + dx;
            int cy = center.Y + dy;
            int cz = center.Z + dz;
            if (!MortonCodec.IsInRange(cx, cy, cz)) continue;
            yield return MortonCodec.Encode3D(cx, cy, cz);
        }
    }

    /// <summary>
    /// 计算"上一帧订阅集合 → 本帧订阅集合"的增量，供 <see cref="PlayerSessionState"/> 做差异订阅。
    /// 输出顺序无保证，调用方根据集合语义使用。
    /// </summary>
    /// <param name="previous">上一帧订阅的 Morton 键集合。</param>
    /// <param name="current">本帧预期订阅的 Morton 键集合。</param>
    /// <returns>(toSubscribe, toUnsubscribe) 两个集合。</returns>
    public static (HashSet<ulong> ToSubscribe, HashSet<ulong> ToUnsubscribe) Diff(
        IReadOnlyCollection<ulong> previous, IReadOnlyCollection<ulong> current)
    {
        var prev = previous is HashSet<ulong> hp ? hp : new HashSet<ulong>(previous);
        var curr = current is HashSet<ulong> hc ? hc : new HashSet<ulong>(current);

        var toSub = new HashSet<ulong>(curr);
        toSub.ExceptWith(prev);

        var toUnsub = new HashSet<ulong>(prev);
        toUnsub.ExceptWith(curr);

        return (toSub, toUnsub);
    }

    /// <summary>
    /// 把"米"换成"块半径"；最小为 0（半径 &lt; 1 chunk 时只返回中心）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MetresToChunkRadius(float metres)
    {
        if (metres <= 0f) return 0;
        // 向上取整确保覆盖足够
        var rFloat = metres * WorldCoord.InverseMetresPerChunkCell;
        return (int)MathF.Ceiling(rFloat);
    }

    /// <summary>
    /// 已知半径下的 chunk 总数（r=0→1, r=1→27, r=2→125）。
    /// 便于 AOI 容量预估 / 上限守卫。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountChunksInCube(int radiusChunks)
    {
        if (radiusChunks < 0) return 0;
        long side = 2L * radiusChunks + 1L;
        long total = side * side * side;
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}

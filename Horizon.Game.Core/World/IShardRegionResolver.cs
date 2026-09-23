using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Core.World;

/// <summary>
/// 阶段 4.3 — 跨 Shard 边界双重订阅的空间分片解析器。<br/>
/// 给定玩家世界位置与 AOI 视野半径（chunk 数），返回视口覆盖的全部 Shard 集合（含主 Shard）。
/// 玩家对覆盖集合中的每个 Shard 都建立 AOI 订阅，从而在 Shard 边界两侧平滑看到邻 Shard 的实体
/// （实体广播按实体所在 Shard 的 AOI 表扇出，订阅了对应 chunk 的 session 无论主 Shard 在哪都能收到），
/// 实现跨 Shard 无缝世界，无需实体副本迁移。
/// </summary>
/// <remarks>
/// 空间分片模型（2D X/Z 网格划分）：Shard (gx, gz) 覆盖 chunkX ∈ [gx*Cx, (gx+1)*Cx)、
/// chunkZ ∈ [gz*Cz, (gz+1)*Cz)，其中 Cx = <c>ChunksPerShardX</c>、Cz = <c>ChunksPerShardZ</c>（16m/chunk）。
/// 网格按行主序映射到 Shard ID：id(gx, gz) = gz * <c>ShardsPerRowX</c> + gx（末行可能不满，
/// id ≥ ShardCount 的单元无 Shard，跳过）。<br/>
/// 返回集合 = 位置视口（X/Z 双轴 chunk 区间）覆盖的网格单元 Shard ∪ {characterId 主 Shard}——
/// 保证角色永远能看到自己主 Shard 的实体，即使角色移动到其他 Shard 的空间区域。
/// 视口同时覆盖四个相邻 Shard 的角点时，四个 Shard 全部进入集合（角点边界双重订阅）。
/// <c>ChunksPerShardX</c>/<c>ChunksPerShardZ</c>/<c>ShardsPerRowX</c> 任一 &lt;= 0 或单 Shard 时
/// 禁用空间分片，仅返回主 Shard（行为与阶段 4.2 之前完全一致）。<br/>
/// 特例：<c>ShardsPerRowX &gt;= ShardCount</c> 时网格退化为单行（1D 带状划分，仅沿 X 轴分片）。
/// </remarks>
public interface IShardRegionResolver
{
    /// <summary>
    /// 解析玩家视口（位置 + AOI 半径）覆盖的全部 Shard（含主 Shard），按升序返回。
    /// </summary>
    /// <param name="characterId">角色 ID（用于解析主 Shard，保证主 Shard 恒在集合内）。</param>
    /// <param name="x">世界 X 坐标（米，Flax Y-up）。</param>
    /// <param name="y">世界 Y 坐标（米，Flax Y-up）。</param>
    /// <param name="z">世界 Z 坐标（米，Flax Y-up）。</param>
    /// <param name="viewRadiusChunks">AOI 视野半径（chunk 数，需与 <c>ZoneShardGrain.SessionAoiRadiusChunks</c> 一致）。</param>
    /// <returns>视口覆盖的 Shard ID 列表（升序、去重、恒含主 Shard）。</returns>
    IReadOnlyList<long> ResolveShards(long characterId, float x, float y, float z, int viewRadiusChunks);
}

/// <inheritdoc />
/// <remarks>
/// 线程安全：无状态（仅读 <see cref="IShardRouter"/> 与配置常量），可注册为 Singleton。
/// </remarks>
public sealed class ShardRegionResolver : IShardRegionResolver
{
    private readonly IShardRouter _router;
    private readonly int _chunksPerShardX;
    private readonly int _chunksPerShardZ;
    private readonly int _shardsPerRowX;

    /// <summary>
    /// 创建空间分片解析器。
    /// </summary>
    /// <param name="router">Shard 路由器（解析主 Shard 与 ShardCount）。</param>
    /// <param name="chunksPerShardX">每个 Shard 沿 X 轴覆盖的 chunk 数（16m/chunk）。<c>&lt;= 0</c> 禁用空间分片。</param>
    /// <param name="chunksPerShardZ">每个 Shard 沿 Z 轴覆盖的 chunk 数（16m/chunk）。<c>&lt;= 0</c> 禁用空间分片。</param>
    /// <param name="shardsPerRowX">网格每行（沿 X 轴）的 Shard 数；行主序 id(gx, gz) = gz*ShardsPerRowX + gx。<c>&lt;= 0</c> 禁用空间分片。</param>
    public ShardRegionResolver(IShardRouter router, int chunksPerShardX = 0, int chunksPerShardZ = 0, int shardsPerRowX = 0)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _chunksPerShardX = chunksPerShardX;
        _chunksPerShardZ = chunksPerShardZ;
        _shardsPerRowX = shardsPerRowX;
    }

    /// <inheritdoc />
    public IReadOnlyList<long> ResolveShards(long characterId, float x, float y, float z, int viewRadiusChunks)
    {
        var primary = _router.Resolve(characterId);
        if (_chunksPerShardX <= 0 || _chunksPerShardZ <= 0 || _shardsPerRowX <= 0 || _router.ShardCount <= 1)
        {
            return new[] { primary };
        }

        if (viewRadiusChunks < 0) viewRadiusChunks = 0;

        // 视口 X/Z 双轴 chunk 区间（2D 网格划分；Y 轴全 Shard 共享）。
        var centerChunkX = (int)MathF.Floor(x * WorldCoord.InverseMetresPerChunkCell);
        var centerChunkZ = (int)MathF.Floor(z * WorldCoord.InverseMetresPerChunkCell);
        var minChunkX = centerChunkX - viewRadiusChunks;
        var maxChunkX = centerChunkX + viewRadiusChunks;
        var minChunkZ = centerChunkZ - viewRadiusChunks;
        var maxChunkZ = centerChunkZ + viewRadiusChunks;

        // 覆盖网格单元区间：chunk / 每 Shard chunk 数（负数用向下取整除法），clamp 到网格边界。
        var minGx = Clamp(FloorDiv(minChunkX, _chunksPerShardX), 0, _shardsPerRowX - 1);
        var maxGx = Clamp(FloorDiv(maxChunkX, _chunksPerShardX), 0, _shardsPerRowX - 1);
        var gridRows = (_router.ShardCount + _shardsPerRowX - 1) / _shardsPerRowX;
        var minGz = Clamp(FloorDiv(minChunkZ, _chunksPerShardZ), 0, gridRows - 1);
        var maxGz = Clamp(FloorDiv(maxChunkZ, _chunksPerShardZ), 0, gridRows - 1);

        var result = new HashSet<long> { primary };
        for (var gz = minGz; gz <= maxGz; gz++)
        {
            for (var gx = minGx; gx <= maxGx; gx++)
            {
                var shardId = (long)gz * _shardsPerRowX + gx;
                // 末行可能不满：id ≥ ShardCount 的单元无 Shard，跳过。
                if (shardId < _router.ShardCount)
                {
                    result.Add(shardId);
                }
            }
        }
        return result.OrderBy(s => s).ToArray();
    }

    private static long Clamp(long value, long min, long max) => value < min ? min : (value > max ? max : value);

    /// <summary>向下取整除法（C# 的 / 对负数向零截断，需手动处理）。</summary>
    private static long FloorDiv(int value, int divisor)
    {
        var q = value / divisor;
        if ((value < 0) != (divisor < 0) && value % divisor != 0) q--;
        return q;
    }
}

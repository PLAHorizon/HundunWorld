using System;

namespace Horizon.Game.Core.World;

/// <summary>
/// Shard 路由策略接口（P1.2 多 Shard 路由基础设施）。<br/>
/// 根据角色 ID / Zone ID 决定目标 ZoneShardGrain 的 Grain Key。<br/>
/// 替换原 <c>SyncPacketHandler.DefaultShardId = 0</c> 硬编码，支持水平扩展。
/// </summary>
/// <remarks>
/// <para><b>路由策略演进路径</b>：</para>
/// <list type="number">
///   <item>Phase 1：<c>characterId % shardCount</c>（简单取模，验证路由正确性）。</item>
///   <item>Phase 2：按 Zone 负载动态路由（结合 <c>ZoneShardLoadMetrics</c>）。</item>
///   <item>Phase 3：一致性哈希 + 动态 Shard 分裂/合并。</item>
/// </list>
/// </remarks>
public interface IShardRouter
{
    /// <summary>
    /// 根据角色 ID 解析目标 Shard ID（ZoneShardGrain 的 Grain Key）。
    /// </summary>
    /// <param name="characterId">角色 ID。</param>
    /// <returns>目标 Shard ID（作为 IZoneShardGrain 的 Primary Key）。</returns>
    long Resolve(long characterId);

    /// <summary>
    /// 根据 Zone ID 和角色 ID 解析目标 Shard ID（多 Zone 场景）。
    /// </summary>
    /// <param name="zoneId">Zone ID（地图/区域标识）。</param>
    /// <param name="characterId">角色 ID。</param>
    /// <returns>目标 Shard ID。</returns>
    long Resolve(long zoneId, long characterId);

    /// <summary>
    /// 当前配置的 Shard 总数。
    /// </summary>
    int ShardCount { get; }
}

/// <summary>
/// 基于取模的 Shard 路由实现（Phase 1 简单策略）。<br/>
/// 路由公式：<c>shardId = (characterId % shardCount)</c>。<br/>
/// 多 Zone 场景：<c>shardId = (zoneId * shardCount + characterId % shardCount)</c>。
/// </summary>
/// <remarks>
/// 本实现为无状态服务（Singleton 生命周期），线程安全。<br/>
/// ShardCount 从配置读取（<c>ShardConfiguration:ShardCount</c>），默认 1（兼容单 Shard 模式）。
/// </remarks>
public sealed class ZoneBasedShardRouter : IShardRouter
{
    private readonly int _shardCount;

    /// <summary>
    /// 创建路由器实例。
    /// </summary>
    /// <param name="shardCount">Shard 总数（必须 >= 1）。</param>
    public ZoneBasedShardRouter(int shardCount = 1)
    {
        if (shardCount < 1)
            throw new ArgumentOutOfRangeException(nameof(shardCount), "ShardCount 必须 >= 1。");
        _shardCount = shardCount;
    }

    /// <inheritdoc />
    public int ShardCount => _shardCount;

    /// <inheritdoc />
    public long Resolve(long characterId)
    {
        if (_shardCount == 1) return 0;  // 单 Shard 快速路径
        // 取模路由：确保非负（characterId 可能为负数的边界情况）
        return ((characterId % _shardCount) + _shardCount) % _shardCount;
    }

    /// <inheritdoc />
    public long Resolve(long zoneId, long characterId)
    {
        if (_shardCount == 1) return zoneId;  // 单 Shard 时按 Zone 分配
        // 多 Zone + 多 Shard：zoneId 作为高位，characterId 取模作为低位
        var localShard = ((characterId % _shardCount) + _shardCount) % _shardCount;
        return zoneId * _shardCount + localShard;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P3.1 Shard 管理器 Grain 实现（全局单例）。<br/>
/// 动态 Shard 扩缩容、负载监控、自动分裂/合并。
/// </summary>
public sealed class ShardManagerGrain : Grain, IShardManagerGrain
{
    private readonly ILogger<ShardManagerGrain> _logger;

    /// <summary>Shard 注册表。</summary>
    private readonly Dictionary<long, ShardRuntimeState> _shards = new();

    /// <summary>Zone → Shard 映射。</summary>
    private readonly Dictionary<long, long> _zoneShardMap = new();

    /// <summary>Shard ID 自增计数器。</summary>
    private long _nextShardId = 1;

    // 扩缩容阈值
    private const float SplitThreshold = 0.85f; // 负载 > 85% 触发分裂
    private const float MergeThreshold = 0.20f; // 负载 < 20% 触发合并
    private const int MaxShards = 100;

    public ShardManagerGrain(ILogger<ShardManagerGrain> logger)
    {
        _logger = logger;
    }

    public Task RegisterShardAsync(long shardId, ShardInfo info)
    {
        _shards[shardId] = new ShardRuntimeState
        {
            Info = info,
            Health = ShardHealthStatus.Healthy,
            LastReportTime = DateTime.UtcNow,
        };

        _logger.LogInformation(
            "Shard 注册。ShardId={ShardId}, Silo={Silo}, Capacity={Capacity}",
            shardId, info.SiloAddress, info.MaxCapacity);

        return Task.CompletedTask;
    }

    public Task UnregisterShardAsync(long shardId)
    {
        if (_shards.Remove(shardId))
        {
            // 清理 Zone 映射
            var zonesToRemove = _zoneShardMap.Where(kv => kv.Value == shardId).Select(kv => kv.Key).ToList();
            foreach (var zoneId in zonesToRemove)
                _zoneShardMap.Remove(zoneId);

            _logger.LogInformation("Shard 下线。ShardId={ShardId}", shardId);
        }
        return Task.CompletedTask;
    }

    public Task ReportLoadAsync(long shardId, ShardLoadReport report)
    {
        if (!_shards.TryGetValue(shardId, out var state))
            return Task.CompletedTask;

        state.LastReport = report;
        state.LastReportTime = DateTime.UtcNow;

        // 计算负载百分比
        var capacity = state.Info.MaxCapacity;
        var load = capacity > 0 ? (float)report.ActivePlayerCount / capacity : 0f;
        state.LoadPercentage = load;

        // 更新健康状态
        state.Health = load switch
        {
            > SplitThreshold => ShardHealthStatus.Overloaded,
            > 0.6f => ShardHealthStatus.Elevated,
            < MergeThreshold => ShardHealthStatus.Idle,
            _ => ShardHealthStatus.Healthy,
        };

        // 自动分裂检测
        if (state.Health == ShardHealthStatus.Overloaded && _shards.Count < MaxShards)
        {
            _logger.LogWarning(
                "Shard 过载，建议分裂。ShardId={ShardId}, Load={Load:P0}, Players={Players}",
                shardId, load, report.ActivePlayerCount);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetShardForZoneAsync(long zoneId)
    {
        if (_zoneShardMap.TryGetValue(zoneId, out var shardId))
            return Task.FromResult(shardId);

        // 选择负载最低的 Shard
        var targetShard = _shards.Values
            .Where(s => s.Health != ShardHealthStatus.Offline)
            .OrderBy(s => s.LoadPercentage)
            .FirstOrDefault();

        var assignedShardId = targetShard?.Info.ShardId ?? 0;
        _zoneShardMap[zoneId] = assignedShardId;

        return Task.FromResult(assignedShardId);
    }

    public Task<ShardSplitResult> RequestSplitAsync(long shardId)
    {
        if (!_shards.ContainsKey(shardId))
            return Task.FromResult(new ShardSplitResult { Success = false, ErrorMessage = "Shard 不存在。" });

        if (_shards.Count >= MaxShards)
            return Task.FromResult(new ShardSplitResult { Success = false, ErrorMessage = "已达最大 Shard 数量限制。" });

        var newShardId = _nextShardId++;

        // 创建新 Shard 状态
        _shards[newShardId] = new ShardRuntimeState
        {
            Info = new ShardInfo
            {
                ShardId = newShardId,
                SiloAddress = _shards[shardId].Info.SiloAddress, // 同 Silo
                StartTime = DateTime.UtcNow,
                MaxCapacity = _shards[shardId].Info.MaxCapacity,
            },
            Health = ShardHealthStatus.Healthy,
            LastReportTime = DateTime.UtcNow,
        };

        // 迁移部分 Zone 到新 Shard
        var zonesToMigrate = _zoneShardMap
            .Where(kv => kv.Value == shardId)
            .Select(kv => kv.Key)
            .Take(_zoneShardMap.Count(kv => kv.Value == shardId) / 2)
            .ToList();

        foreach (var zoneId in zonesToMigrate)
            _zoneShardMap[zoneId] = newShardId;

        _logger.LogInformation(
            "Shard 分裂完成。SourceShard={Source}, NewShard={New}, MigratedZones={Zones}",
            shardId, newShardId, zonesToMigrate.Count);

        return Task.FromResult(new ShardSplitResult { Success = true, NewShardId = newShardId });
    }

    public Task<bool> RequestMergeAsync(long shardId1, long shardId2)
    {
        if (!_shards.TryGetValue(shardId1, out var state1) || !_shards.TryGetValue(shardId2, out var state2))
            return Task.FromResult(false);

        // 只有空闲 Shard 才能合并
        if (state1.Health != ShardHealthStatus.Idle && state2.Health != ShardHealthStatus.Idle)
            return Task.FromResult(false);

        // 合并 shardId2 到 shardId1
        var zonesToMigrate = _zoneShardMap.Where(kv => kv.Value == shardId2).Select(kv => kv.Key).ToList();
        foreach (var zoneId in zonesToMigrate)
            _zoneShardMap[zoneId] = shardId1;

        _shards.Remove(shardId2);

        _logger.LogInformation(
            "Shard 合并完成。Target={Target}, Source={Source}, MigratedZones={Zones}",
            shardId1, shardId2, zonesToMigrate.Count);

        return Task.FromResult(true);
    }

    public Task<ShardStatus[]> GetAllShardsAsync()
    {
        var statuses = _shards.Values.Select(s => new ShardStatus
        {
            ShardId = s.Info.ShardId,
            SiloAddress = s.Info.SiloAddress,
            Health = s.Health,
            EntityCount = s.LastReport?.EntityCount ?? 0,
            ActivePlayerCount = s.LastReport?.ActivePlayerCount ?? 0,
            LoadPercentage = s.LoadPercentage,
            TickLatencyMs = s.LastReport?.TickLatencyMs ?? 0,
            LastReportTime = s.LastReportTime,
        }).ToArray();

        return Task.FromResult(statuses);
    }

    public Task<ShardClusterStats> GetClusterStatsAsync()
    {
        var stats = new ShardClusterStats
        {
            TotalShards = _shards.Count,
            HealthyShards = _shards.Count(s => s.Value.Health == ShardHealthStatus.Healthy || s.Value.Health == ShardHealthStatus.Elevated),
            TotalEntities = _shards.Sum(s => s.Value.LastReport?.EntityCount ?? 0),
            TotalPlayers = _shards.Sum(s => s.Value.LastReport?.ActivePlayerCount ?? 0),
            AverageLoad = _shards.Count > 0 ? _shards.Average(s => s.Value.LoadPercentage) : 0,
            AverageTickLatencyMs = _shards.Count > 0 ? _shards.Average(s => s.Value.LastReport?.TickLatencyMs ?? 0) : 0,
        };

        return Task.FromResult(stats);
    }

    /// <summary>Shard 运行时状态。</summary>
    private sealed class ShardRuntimeState
    {
        public ShardInfo Info { get; init; } = null!;
        public ShardHealthStatus Health { get; set; }
        public ShardLoadReport? LastReport { get; set; }
        public DateTime LastReportTime { get; set; }
        public float LoadPercentage { get; set; }
    }
}

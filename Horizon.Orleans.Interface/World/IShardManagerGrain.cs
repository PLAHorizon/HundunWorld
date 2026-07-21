using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P3.1 Shard 管理器 Grain 契约（全局单例）。<br/>
/// 负责：动态 Shard 扩缩容、负载监控、Shard 分裂/合并、Zone-Shard 映射。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IShardManagerGrain : IGrainWithIntegerKey
{
    /// <summary>注册 Shard 上线。</summary>
    Task RegisterShardAsync(long shardId, ShardInfo info);

    /// <summary>Shard 下线。</summary>
    Task UnregisterShardAsync(long shardId);

    /// <summary>上报 Shard 负载。</summary>
    Task ReportLoadAsync(long shardId, ShardLoadReport report);

    /// <summary>获取 Zone 的 Shard 分配。</summary>
    Task<long> GetShardForZoneAsync(long zoneId);

    /// <summary>请求 Shard 分裂（负载过高）。</summary>
    Task<ShardSplitResult> RequestSplitAsync(long shardId);

    /// <summary>请求 Shard 合并（负载过低）。</summary>
    Task<bool> RequestMergeAsync(long shardId1, long shardId2);

    /// <summary>获取所有 Shard 状态。</summary>
    Task<ShardStatus[]> GetAllShardsAsync();

    /// <summary>获取集群统计。</summary>
    Task<ShardClusterStats> GetClusterStatsAsync();
}

/// <summary>Shard 信息。</summary>
[GenerateSerializer]
public sealed class ShardInfo
{
    [Id(0)] public long ShardId { get; set; }
    [Id(1)] public string SiloAddress { get; set; } = string.Empty;
    [Id(2)] public DateTime StartTime { get; set; }
    [Id(3)] public int MaxCapacity { get; set; } = 1000;
}

/// <summary>Shard 负载报告。</summary>
[GenerateSerializer]
public sealed class ShardLoadReport
{
    [Id(0)] public long ShardId { get; set; }
    [Id(1)] public int EntityCount { get; set; }
    [Id(2)] public int ActivePlayerCount { get; set; }
    [Id(3)] public float CpuUsage { get; set; }
    [Id(4)] public float MemoryUsageMb { get; set; }
    [Id(5)] public float TickLatencyMs { get; set; }
    [Id(6)] public DateTime ReportTime { get; set; }
}

/// <summary>Shard 分裂结果。</summary>
[GenerateSerializer]
public sealed class ShardSplitResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public long NewShardId { get; set; }
    [Id(2)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>Shard 状态。</summary>
[GenerateSerializer]
public sealed class ShardStatus
{
    [Id(0)] public long ShardId { get; set; }
    [Id(1)] public string SiloAddress { get; set; } = string.Empty;
    [Id(2)] public ShardHealthStatus Health { get; set; }
    [Id(3)] public int EntityCount { get; set; }
    [Id(4)] public int ActivePlayerCount { get; set; }
    [Id(5)] public float LoadPercentage { get; set; }
    [Id(6)] public float TickLatencyMs { get; set; }
    [Id(7)] public DateTime LastReportTime { get; set; }
}

/// <summary>Shard 集群统计。</summary>
[GenerateSerializer]
public sealed class ShardClusterStats
{
    [Id(0)] public int TotalShards { get; set; }
    [Id(1)] public int HealthyShards { get; set; }
    [Id(2)] public int TotalEntities { get; set; }
    [Id(3)] public int TotalPlayers { get; set; }
    [Id(4)] public float AverageLoad { get; set; }
    [Id(5)] public float AverageTickLatencyMs { get; set; }
}

/// <summary>Shard 健康状态。</summary>
[GenerateSerializer]
public enum ShardHealthStatus : byte
{
    /// <summary>健康。</summary>
    Healthy = 0,
    /// <summary>负载偏高。</summary>
    Elevated = 1,
    /// <summary>过载（需要分裂）。</summary>
    Overloaded = 2,
    /// <summary>空闲（可合并）。</summary>
    Idle = 3,
    /// <summary>离线。</summary>
    Offline = 4,
}

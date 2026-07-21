using System;
using System.Threading.Tasks;
using Orleans;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 副本管理器 Grain 契约（P2.2）。<br/>
/// Grain Primary Key = 0（全局单例）。<br/>
/// 负责：副本创建/销毁/超时回收、实例 ID 分配、活跃副本查询。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IInstanceManagerGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// 创建新副本实例。
    /// </summary>
    /// <param name="request">创建请求。</param>
    /// <returns>创建结果（含分配的 instanceId）。</returns>
    Task<CreateInstanceResult> CreateInstanceAsync(CreateInstanceRequest request);

    /// <summary>
    /// 销毁副本实例。
    /// </summary>
    Task DestroyInstanceAsync(long instanceId, InstanceCloseReason reason);

    /// <summary>
    /// 查询活跃副本列表。
    /// </summary>
    Task<ActiveInstanceInfo[]> GetActiveInstancesAsync();

    /// <summary>
    /// 查询指定副本状态。
    /// </summary>
    Task<InstanceState?> GetInstanceStateAsync(long instanceId);

    /// <summary>
    /// 获取统计信息。
    /// </summary>
    Task<InstanceManagerStats> GetStatsAsync();
}

/// <summary>
/// 创建副本请求。
/// </summary>
[GenerateSerializer]
public sealed class CreateInstanceRequest
{
    [Id(0)] public int TemplateId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public InstanceType Type { get; set; }
    [Id(3)] public int MaxPlayers { get; set; }
    [Id(4)] public long CreatorId { get; set; }
    [Id(5)] public long OriginZoneShardId { get; set; }
    [Id(6)] public int Difficulty { get; set; } = 1;
    [Id(7)] public float TimeoutSeconds { get; set; } = 3600f;
}

/// <summary>
/// 创建副本结果。
/// </summary>
[GenerateSerializer]
public sealed class CreateInstanceResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public long InstanceId { get; set; }
    [Id(2)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 活跃副本摘要信息。
/// </summary>
[GenerateSerializer]
public sealed class ActiveInstanceInfo
{
    [Id(0)] public long InstanceId { get; set; }
    [Id(1)] public int TemplateId { get; set; }
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public InstanceType Type { get; set; }
    [Id(4)] public int CurrentPlayers { get; set; }
    [Id(5)] public int MaxPlayers { get; set; }
    [Id(6)] public InstancePhase Phase { get; set; }
    [Id(7)] public DateTime CreateTime { get; set; }
}

/// <summary>
/// 副本管理器统计。
/// </summary>
[GenerateSerializer]
public sealed class InstanceManagerStats
{
    [Id(0)] public int ActiveInstanceCount { get; set; }
    [Id(1)] public long TotalCreatedCount { get; set; }
    [Id(2)] public long TotalDestroyedCount { get; set; }
    [Id(3)] public int TotalActivePlayers { get; set; }
}

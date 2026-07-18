using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// ZoneShard 负载指标快照（用于未来 sharding 路由决策）。
/// </summary>
[GenerateSerializer]
public sealed class ZoneShardLoadMetrics
{
    [Id(0)] public int EntityCount { get; set; }
    [Id(1)] public int SessionCount { get; set; }
    [Id(2)] public int ChunkCount { get; set; }
    [Id(3)] public long LastTickDurationMs { get; set; }
    [Id(4)] public int PendingInputsCount { get; set; }
    [Id(5)] public long TickCount { get; set; }
}

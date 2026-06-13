using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Core.Abstract;
using Horizon.Game.Core.World.ChunkCell;
using Horizon.Game.Message.World;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="IWorldChunkCellGrain"/> 的 SQL Server 持久化实现（P4-a）。<br/>
/// 通过 <c>[PersistentState("chunk", WorldSqlStore)]</c> 把 <see cref="ChunkCellPersistedState"/> 持久化到
/// Orleans 的 ADO.NET 存储提供者（见 <c>ConfigureOrleansStorage</c>）。
/// </summary>
/// <remarks>
/// 存储流程：
/// <list type="number">
///   <item>激活时加载 <see cref="ChunkCellPersistedState"/>，按 OpLog 回放重建内存状态。</item>
///   <item>每次写 op 后标记 dirty，由 <see cref="WritePendingAsync"/> 在 tick/去激活时写回。</item>
///   <item>保存前先 <see cref="ChunkCellState.CompactOpLog"/>，把 OpLog 压缩为"最小等价集合"。</item>
/// </list>
/// </remarks>
public class WorldChunkCellGrain : Grain, IWorldChunkCellGrain
{
    private readonly ILogger<WorldChunkCellGrain> _logger;
    private readonly IPersistentState<ChunkCellPersistedState> _persisted;
    private ChunkCellState? _state;
    private bool _dirty;

    public WorldChunkCellGrain(
        ILogger<WorldChunkCellGrain> logger,
        [PersistentState("chunk", OrleansConst.WorldSqlStore)] IPersistentState<ChunkCellPersistedState> persisted)
    {
        _logger = logger;
        _persisted = persisted;
    }

    public override Task OnActivateAsync(System.Threading.CancellationToken cancellationToken)
    {
        var mortonKey = (ulong)this.GetPrimaryKeyLong();
        _state = new ChunkCellState(mortonKey);
        if (_persisted.State.OpLog is { Length: > 0 } log)
        {
            _state.ApplyBatch(log);
            _logger.LogDebug("ChunkCell {Key}: 恢复 {Count} 条压缩 op（version={Version}）。",
                mortonKey, log.Length, _persisted.State.Version);
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, System.Threading.CancellationToken cancellationToken)
    {
        if (_dirty) await WritePendingAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private ChunkCellState State => _state ??= new ChunkCellState((ulong)this.GetPrimaryKeyLong());

    /// <inheritdoc />
    public async Task<int> ApplyOpsAsync(VoxelOp[] ops)
    {
        if (ops is null || ops.Length == 0) return 0;
        var applied = State.ApplyBatch(ops);
        if (applied > 0) _dirty = true;
        if (applied != ops.Length)
        {
            _logger.LogDebug("ChunkCell {Key}: 部分 op 被拒 ({Applied}/{Total})。",
                State.MortonKey, applied, ops.Length);
        }
        // 达到压缩阈值时 flush 一次，防止 persisted state 无限增长
        if (_dirty && State.OpLogSize >= 1024) await WritePendingAsync();
        return applied;
    }

    /// <inheritdoc />
    public Task<VoxelOp[]> ReadOpsSinceAsync(int sinceVersion)
    {
        var list = State.ReadOpsSince(sinceVersion < 0 ? 0 : sinceVersion);
        var arr = new VoxelOp[list.Count];
        for (int i = 0; i < list.Count; i++) arr[i] = list[i];
        return Task.FromResult(arr);
    }

    /// <inheritdoc />
    public Task<ChunkCellStats> GetStatsAsync()
        => Task.FromResult(new ChunkCellStats(State.Version, State.BlockCount, State.PrefabCount, State.OpLogSize));

    /// <inheritdoc />
    public async Task<int> CompactAsync()
    {
        var savings = State.CompactOpLog();
        await WritePendingAsync();
        return savings;
    }

    private async Task WritePendingAsync()
    {
        // 保存前压缩：把 runtime 的 op log 变成"最小等价 op 集合"再落盘
        State.CompactOpLog();
        var log = State.OpLog;
        var arr = new VoxelOp[log.Count];
        for (int i = 0; i < log.Count; i++) arr[i] = log[i];

        _persisted.State.MortonKey = State.MortonKey;
        _persisted.State.Version = State.Version;
        _persisted.State.OpLog = arr;
        await _persisted.WriteStateAsync();
        _dirty = false;
    }
}

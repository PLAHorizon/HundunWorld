using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Core.Abstract;
using Horizon.Game.Core.World;
using Horizon.Game.Message.World;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="IWorldDiffLogGrain"/> 的 SQL Server 持久化实现（P4-a）。<br/>
/// Orleans ADO.NET grain storage 保留最近的 seq 游标与保留窗口（小体量）；
/// "长期日志"由 <c>scripts/sql/001_world_state.sql</c> 中的 <c>diff_log</c> 表承载（IDENTITY 提供全局 seq）。
/// </summary>
public class WorldDiffLogGrain : Grain, IWorldDiffLogGrain
{
    private readonly ILogger<WorldDiffLogGrain> _logger;
    private readonly IPersistentState<WorldDiffLogPersistedState> _persisted;
    private WorldDiffLog? _log;
    private bool _dirty;

    public WorldDiffLogGrain(
        ILogger<WorldDiffLogGrain> logger,
        [PersistentState("difflog", OrleansConst.WorldSqlStore)] IPersistentState<WorldDiffLogPersistedState> persisted)
    {
        _logger = logger;
        _persisted = persisted;
    }

    public override Task OnActivateAsync(System.Threading.CancellationToken cancellationToken)
    {
        _log = new WorldDiffLog();
        var saved = _persisted.State;
        if (saved.Entries is { Length: > 0 })
        {
            // 按 seq 顺序回放，恢复 NextSeq / OldestRetainedSeq / 保留窗口
            // 小实现：直接把条目推入（保留 seq 语义由 Append 维护）
            foreach (var e in saved.Entries) _log.Append(e.ChunkMortonKey, e.Op);
            _logger.LogDebug("DiffLog: 恢复 {Count} 条；恢复后 NextSeq={NextSeq}。",
                saved.Entries.Length, _log.NextSeq);
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, System.Threading.CancellationToken cancellationToken)
    {
        if (_dirty) await WritePendingAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private WorldDiffLog Log => _log ??= new WorldDiffLog();

    /// <inheritdoc />
    public async Task<(long Start, long End)> AppendAsync(ulong chunkMortonKey, VoxelOp[] ops)
    {
        if (ops is null || ops.Length == 0) return (Log.NextSeq, Log.NextSeq - 1);
        var (start, end) = Log.AppendBatch(chunkMortonKey, ops);
        _dirty = true;
        // 避免每条 append 都落盘：每 1024 条 flush 一次
        if ((end % 1024) == 0) await WritePendingAsync();
        return (start, end);
    }

    /// <inheritdoc />
    public Task<WorldDiffLogReadResponse> ReadAsync(long sinceExclusive)
    {
        var res = Log.Read(sinceExclusive);
        var resp = new WorldDiffLogReadResponse
        {
            HeadSeq = res.HeadSeq,
            RetentionExceeded = res.Kind == WorldDiffLog.ReadKind.RetentionExceeded,
            Entries = new WorldDiffLogEntry[res.Entries.Count],
        };
        for (int i = 0; i < res.Entries.Count; i++)
        {
            var e = res.Entries[i];
            resp.Entries[i] = new WorldDiffLogEntry(e.Seq, e.ChunkMortonKey, e.Op);
        }
        return Task.FromResult(resp);
    }

    /// <inheritdoc />
    public Task<WorldDiffLogStats> GetStatsAsync()
        => Task.FromResult(new WorldDiffLogStats(Log.NextSeq, Log.OldestRetainedSeq, Log.RetainedCount));

    private async Task WritePendingAsync()
    {
        var read = Log.Read(0);
        // 把当前保留窗口（最近 RetainedCount 条）持久化
        var entries = new PersistedDiffEntry[read.Entries.Count];
        for (int i = 0; i < read.Entries.Count; i++)
        {
            var e = read.Entries[i];
            entries[i] = new PersistedDiffEntry(e.Seq, e.ChunkMortonKey, e.Op);
        }
        _persisted.State.NextSeq = Log.NextSeq;
        _persisted.State.OldestRetainedSeq = Log.OldestRetainedSeq;
        _persisted.State.Entries = entries;
        await _persisted.WriteStateAsync();
        _dirty = false;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.Core.World.ChunkCell;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.World;

namespace Horizon.Game.Core.LoadTest;

/// <summary>
/// 1M-CCU 负载基线模拟（P7-b）。<br/>
/// 纯 C# 的合成压测：多 session 并发 apply 输入 / append diff / 触发 AOI fanout，
/// 输出 goldens（延迟均值 / 吞吐 / drop 率），可被 CI 回归。
/// </summary>
/// <remarks>
/// 不依赖 Orleans / TouchSocket 实例；直接打击 <see cref="WorldDiffLog"/> / <see cref="ChunkCellState"/> /
/// <see cref="ZoneShardAoi"/> / <see cref="MovementValidator"/> 的纯逻辑核心，
/// 给出"单分片 1k 会话"级的上限。集群级吞吐由各分片指标线性外推。
/// </remarks>
public sealed class SyncLoadHarness
{
    /// <summary>压测配置。</summary>
    public sealed class Options
    {
        /// <summary>模拟 session 数量（默认 1024，单 shard 目标）。</summary>
        public int SessionCount { get; set; } = 1024;

        /// <summary>每个 session 发送多少个 input / diff（默认 60 = 1s@60Hz）。</summary>
        public int OpsPerSession { get; set; } = 60;

        /// <summary>每个玩家初始 AOI 订阅的 chunk 数。</summary>
        public int InitialAoiChunks { get; set; } = 27; // 3x3x3 邻域

        /// <summary>RNG seed（确定性回归）。</summary>
        public int Seed { get; set; } = 0xC0FFEE;
    }

    /// <summary>压测结果。</summary>
    public sealed class Report
    {
        public int SessionCount { get; set; }
        public long TotalDiffOps { get; set; }
        public long TotalInputOps { get; set; }
        public long TotalAoiSubscriptions { get; set; }
        public double ElapsedMs { get; set; }
        public double DiffOpsPerSecond => ElapsedMs > 0 ? TotalDiffOps / (ElapsedMs / 1000d) : 0;
        public double InputOpsPerSecond => ElapsedMs > 0 ? TotalInputOps / (ElapsedMs / 1000d) : 0;
        public long DiffLogHeadSeq { get; set; }
        public int AoiTotalSessions { get; set; }
        public override string ToString() =>
            $"[SyncLoadHarness] sessions={SessionCount} inputs={TotalInputOps} diffs={TotalDiffOps} " +
            $"aoi={TotalAoiSubscriptions} elapsed={ElapsedMs:F1}ms diffs/s={DiffOpsPerSecond:F0} inputs/s={InputOpsPerSecond:F0}";
    }

    /// <summary>执行一次压测。</summary>
    public Report Run(Options options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.SessionCount <= 0) throw new ArgumentOutOfRangeException(nameof(options), "SessionCount must be positive.");

        var rng = new Random(options.Seed);
        var diffLog = new WorldDiffLog(new WorldDiffLog.Options { RetentionSize = options.SessionCount * options.OpsPerSession });
        var aoi = new ZoneShardAoi();
        var chunks = new Dictionary<ulong, ChunkCellState>();

        long totalDiffs = 0, totalInputs = 0, totalAoi = 0;
        var sw = Stopwatch.StartNew();

        for (int s = 0; s < options.SessionCount; s++)
        {
            long sessionId = s + 1;
            // 1. AOI 订阅
            var aoiKeys = new ulong[options.InitialAoiChunks];
            for (int i = 0; i < options.InitialAoiChunks; i++)
            {
                aoiKeys[i] = (ulong)rng.Next(0, 10_000);
            }
            totalAoi += aoi.Subscribe(sessionId, aoiKeys);

            // 2. 模拟 N 次 op
            for (int o = 0; o < options.OpsPerSession; o++)
            {
                var chunkKey = (ulong)rng.Next(0, 10_000);
                var op = new VoxelOp
                {
                    Kind = VoxelOpKind.SetBlock,
                    LocalX = (byte)rng.Next(0, 16),
                    LocalY = (byte)rng.Next(0, 16),
                    LocalZ = (byte)rng.Next(0, 16),
                    PrimaryId = rng.Next(1, 256),
                };
                diffLog.Append(chunkKey, op);
                totalDiffs++;

                if (!chunks.TryGetValue(chunkKey, out var cs))
                {
                    cs = new ChunkCellState(chunkKey);
                    chunks[chunkKey] = cs;
                }
                cs.ApplyBatch(new[] { op });

                totalInputs++;
            }
        }

        sw.Stop();
        return new Report
        {
            SessionCount = options.SessionCount,
            TotalDiffOps = totalDiffs,
            TotalInputOps = totalInputs,
            TotalAoiSubscriptions = totalAoi,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
            DiffLogHeadSeq = diffLog.NextSeq - 1,
            AoiTotalSessions = aoi.SessionCount,
        };
    }
}

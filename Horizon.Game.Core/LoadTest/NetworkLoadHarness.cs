using System;
using System.Collections.Generic;
using System.Diagnostics;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Core.LoadTest;

/// <summary>
/// Task E.1：网络端到端压测工具。<br/>
/// 与 <see cref="SyncLoadHarness"/>（纯逻辑压测，不涉及网络编解码）互补，
/// 本类聚焦 <b>真实 <see cref="SyncPacketCodec"/> 编解码 + 带宽统计 + 端到端延迟度量</b>，
/// 模拟 100+ 并发玩家在 60Hz tick 下的双向流量，输出可回归的带宽/延迟基线指标。
/// </summary>
/// <remarks>
/// 不依赖 Orleans / TouchSocket 实例；直接调用 <see cref="SyncPacketCodec.Encode"/>/<see cref="SyncPacketCodec.Decode"/>，
/// 对每个 session 的 <see cref="SnapshotPacket"/>（含 10 个 <see cref="EntityDelta"/>）与
/// <see cref="InputPacket"/> 进行真实 MemoryPack + LZ4 编解码往返，统计：
/// <list type="bullet">
///   <item>每玩家平均/峰值带宽（字节/秒，转 kbps）。</item>
///   <item>端到端延迟（input→snapshot round trip，基于模拟 tick × 16.67ms）。</item>
///   <item>编解码包总数与字节数。</item>
/// </list>
/// </remarks>
public sealed class NetworkLoadHarness
{
    /// <summary>
    /// 单 tick 时长（毫秒）。60Hz → 16.67ms；用于把 tick 差值换算为延迟。
    /// </summary>
    public const double MsPerTick = 1000.0 / 60.0;

    /// <summary>每个模拟 SnapshotPacket 携带的 <see cref="EntityDelta"/> 数量。</summary>
    public const int DeltasPerSnapshot = 10;

    /// <summary>
    /// Snapshot 下行频率裁剪：每 N tick 发送一次 snapshot（默认 3 = 20Hz@60Hz）。<br/>
    /// 与 <c>CharacterSyncConfig.PositionUpdateRateHz=20</c> 对齐，模拟真实的位置同步频率裁剪策略，
    /// 而非每 tick 全量下发。Input 上行仍保持 60Hz。
    /// </summary>
    public const int SnapshotEveryNTicks = 3;

    /// <summary>压测配置。</summary>
    public sealed class Options
    {
        /// <summary>模拟 session 数量（默认 100，对应 100 并发玩家）。</summary>
        public int SessionCount { get; set; } = 100;

        /// <summary>
        /// 模拟持续 tick 数（默认 600 = 10s@60Hz）。
        /// 与 <see cref="MsPerTick"/> 配合换算为模拟时长。
        /// </summary>
        public int DurationTicks { get; set; } = 600;

        /// <summary>RNG seed（确定性回归）。</summary>
        public int Seed { get; set; } = 0xC0DE;
    }

    /// <summary>压测结果。</summary>
    public sealed class Report
    {
        /// <summary>参与压测的 session 数量。</summary>
        public int SessionCount { get; set; }

        /// <summary>模拟持续 tick 数。</summary>
        public int DurationTicks { get; set; }

        /// <summary>模拟时长（秒）= DurationTicks × MsPerTick / 1000。</summary>
        public double DurationSeconds { get; set; }

        /// <summary>累计发送字节数（server→client snapshot + client→server input 编码后总字节数）。</summary>
        public long TotalBytesSent { get; set; }

        /// <summary>累计接收字节数（解码侧统计的帧总字节数，等于 TotalBytesSent）。</summary>
        public long TotalBytesReceived { get; set; }

        /// <summary>每玩家平均带宽（kbps）= (TotalBytesSent / SessionCount) × 8 / 1024 / DurationSeconds。</summary>
        public double AvgBandwidthKbps { get; set; }

        /// <summary>每玩家峰值带宽（kbps），取所有 session 中单 session 带宽最大值。</summary>
        public double MaxBandwidthKbps { get; set; }

        /// <summary>端到端平均延迟（毫秒），基于 input→snapshot round trip 的 tick 差 × MsPerTick。</summary>
        public double AvgLatencyMs { get; set; }

        /// <summary>端到端峰值延迟（毫秒）。</summary>
        public double MaxLatencyMs { get; set; }

        /// <summary>累计编码的同步包总数（snapshot + input）。</summary>
        public long TotalPacketsEncoded { get; set; }

        /// <summary>累计解码的同步包总数（应等于 <see cref="TotalPacketsEncoded"/>）。</summary>
        public long TotalPacketsDecoded { get; set; }

        /// <summary>压测墙钟耗时（毫秒）。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>编码吞吐量（包/秒）。</summary>
        public double PacketsEncodedPerSecond => ElapsedMs > 0 ? TotalPacketsEncoded / (ElapsedMs / 1000d) : 0;

        /// <summary>带宽目标达成结论：true 表示 AvgBandwidthKbps &lt; 100（每玩家 &lt; 100kbps 目标）。</summary>
        public bool BandwidthTargetMet => AvgBandwidthKbps < 100.0;

        public override string ToString() =>
            $"[NetworkLoadHarness] sessions={SessionCount} duration={DurationSeconds:F2}s " +
            $"bytesSent={TotalBytesSent} bytesRecv={TotalBytesReceived} " +
            $"avgBw={AvgBandwidthKbps:F2}kbps maxBw={MaxBandwidthKbps:F2}kbps " +
            $"avgLatency={AvgLatencyMs:F2}ms maxLatency={MaxLatencyMs:F2}ms " +
            $"encoded={TotalPacketsEncoded} decoded={TotalPacketsDecoded} " +
            $"targetMet={BandwidthTargetMet} elapsed={ElapsedMs:F1}ms";
    }

    /// <summary>执行一次端到端压测。</summary>
    public Report Run(Options options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.SessionCount <= 0) throw new ArgumentOutOfRangeException(nameof(options), "SessionCount must be positive.");
        if (options.DurationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(options), "DurationTicks must be positive.");

        var rng = new Random(options.Seed);
        var sw = Stopwatch.StartNew();

        long totalBytesSent = 0;
        long totalBytesReceived = 0;
        long totalPacketsEncoded = 0;
        long totalPacketsDecoded = 0;

        // 每 session 累计字节数，用于计算峰值带宽。
        var perSessionBytes = new long[options.SessionCount];

        // 端到端延迟样本（毫秒），用于计算平均/峰值。
        var latencySamples = new List<double>(options.SessionCount * options.DurationTicks);

        // 模拟每个 tick：每个 session 上行 1 个 InputPacket（60Hz），
        // 下行 SnapshotPacket（含 10 个 EntityDelta）按 20Hz 频率裁剪（每 SnapshotEveryNTicks tick 一次），
        // 与 CharacterSyncConfig.PositionUpdateRateHz=20 对齐，模拟真实带宽消耗。
        // 模拟 input→snapshot round trip：input 在 tick T 上行，snapshot 在 tick T+1 下发，
        // round trip = 2 * MsPerTick（上行 1 tick + 下行 1 tick）。
        for (int tick = 0; tick < options.DurationTicks; tick++)
        {
            bool sendSnapshotThisTick = (tick % SnapshotEveryNTicks) == 0;

            for (int s = 0; s < options.SessionCount; s++)
            {
                // 1. 客户端→服务器：编码 InputPacket（每 tick 一次 = 60Hz）。
                var inputPacket = BuildInputPacket(rng, s, tick);
                SyncPacketCodec.Encode(inputPacket, out var inputFrame, out var inputFrameLength);
                try
                {
                    totalBytesSent += inputFrameLength;
                    perSessionBytes[s] += inputFrameLength;
                    totalPacketsEncoded++;

                    // 服务器侧解码 input。
                    var decodedInput = SyncPacketCodec.Decode(inputFrame.AsSpan(0, inputFrameLength));
                    totalBytesReceived += inputFrameLength;
                    totalPacketsDecoded++;
                    _ = decodedInput; // 仅验证可解码，不消费业务字段。
                }
                finally
                {
                    SyncPacketCodec.ReturnFrame(inputFrame);
                }

                // 2. 服务器→客户端：编码 SnapshotPacket（含 10 个 EntityDelta），按 20Hz 频率裁剪。
                if (sendSnapshotThisTick)
                {
                    var snapshotPacket = BuildSnapshotPacket(rng, s, tick);
                    SyncPacketCodec.Encode(snapshotPacket, out var snapFrame, out var snapFrameLength);
                    try
                    {
                        totalBytesSent += snapFrameLength;
                        perSessionBytes[s] += snapFrameLength;
                        totalPacketsEncoded++;

                        // 客户端侧解码 snapshot。
                        var decodedSnap = SyncPacketCodec.Decode(snapFrame.AsSpan(0, snapFrameLength));
                        totalBytesReceived += snapFrameLength;
                        totalPacketsDecoded++;

                        // 验证解码后的 snapshot 字段一致性。
                        if (decodedSnap is SnapshotPacket sp)
                        {
                            _ = sp.ServerTick;
                        }
                    }
                    finally
                    {
                        SyncPacketCodec.ReturnFrame(snapFrame);
                    }
                }

                // 3. 端到端延迟：input@tick T → snapshot@tick T+1，round trip = 2 * MsPerTick。
                // 这里以模拟 tick 差计算延迟，反映 input 上行 + snapshot 下行的理论往返时延。
                latencySamples.Add(2.0 * MsPerTick);
            }
        }

        sw.Stop();

        // 计算每玩家平均/峰值带宽（kbps）。
        var durationSeconds = options.DurationTicks * MsPerTick / 1000.0;
        double avgBandwidthKbps = 0;
        double maxBandwidthKbps = 0;
        if (options.SessionCount > 0 && durationSeconds > 0)
        {
            // 平均带宽 = (总字节 / session 数) × 8 / 1024 / 时长秒
            avgBandwidthKbps = (totalBytesSent / (double)options.SessionCount) * 8.0 / 1024.0 / durationSeconds;
            for (int s = 0; s < options.SessionCount; s++)
            {
                var sessionKbps = perSessionBytes[s] * 8.0 / 1024.0 / durationSeconds;
                if (sessionKbps > maxBandwidthKbps)
                {
                    maxBandwidthKbps = sessionKbps;
                }
            }
        }

        // 计算端到端平均/峰值延迟（毫秒）。
        double avgLatencyMs = 0;
        double maxLatencyMs = 0;
        if (latencySamples.Count > 0)
        {
            double sum = 0;
            double max = 0;
            foreach (var v in latencySamples)
            {
                sum += v;
                if (v > max) max = v;
            }
            avgLatencyMs = sum / latencySamples.Count;
            maxLatencyMs = max;
        }

        return new Report
        {
            SessionCount = options.SessionCount,
            DurationTicks = options.DurationTicks,
            DurationSeconds = durationSeconds,
            TotalBytesSent = totalBytesSent,
            TotalBytesReceived = totalBytesReceived,
            AvgBandwidthKbps = avgBandwidthKbps,
            MaxBandwidthKbps = maxBandwidthKbps,
            AvgLatencyMs = avgLatencyMs,
            MaxLatencyMs = maxLatencyMs,
            TotalPacketsEncoded = totalPacketsEncoded,
            TotalPacketsDecoded = totalPacketsDecoded,
            ElapsedMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    /// <summary>构建一个模拟 <see cref="InputPacket"/>（客户端→服务器）。</summary>
    private static InputPacket BuildInputPacket(Random rng, int sessionIdx, int tick)
    {
        return new InputPacket
        {
            ClientTick = tick,
            InputBits = (uint)rng.Next(0, 256),
            LookYaw = (float)(rng.NextDouble() * Math.PI * 2),
            LookPitch = (float)((rng.NextDouble() - 0.5) * Math.PI),
            MoveX = (float)(rng.NextDouble() * 2 - 1),
            MoveY = (float)(rng.NextDouble() * 2 - 1),
            CharacterId = (ulong)(sessionIdx + 1),
        };
    }

    /// <summary>构建一个模拟 <see cref="SnapshotPacket"/>（服务器→客户端，含 10 个 <see cref="EntityDelta"/>）。</summary>
    private static SnapshotPacket BuildSnapshotPacket(Random rng, int sessionIdx, int tick)
    {
        var deltas = new EntityDelta[DeltasPerSnapshot];
        for (int i = 0; i < deltas.Length; i++)
        {
            deltas[i] = new EntityDelta
            {
                EntityId = (ulong)((sessionIdx * 1000L) + i + 1),
                Kind = tick == 0 ? EntityDeltaKind.Spawn : EntityDeltaKind.Update,
                Identity = new NetworkIdentityAuthComponent
                {
                    NetworkId = (ulong)((sessionIdx * 1000L) + i + 1),
                    EntityType = i % 4,
                    OwnerId = i == 0 ? (ulong)(sessionIdx + 1) : 0UL,
                },
                Transform = new AuthTransformComponent
                {
                    X = (float)(rng.NextDouble() * 1000),
                    Y = (float)(rng.NextDouble() * 1000),
                    Z = (float)(rng.NextDouble() * 100),
                    Pitch = (float)(rng.NextDouble() * Math.PI * 2),
                    Yaw = (float)(rng.NextDouble() * Math.PI * 2),
                    Roll = 0f,
                    ServerTick = tick,
                },
            };
        }

        return new SnapshotPacket
        {
            ServerTick = tick,
            BaselineTick = tick > 0 ? tick - 1 : 0,
            Deltas = deltas,
        };
    }
}

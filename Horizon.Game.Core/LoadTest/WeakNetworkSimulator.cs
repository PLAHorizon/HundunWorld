using System;
using System.Collections.Generic;

namespace Horizon.Game.Core.LoadTest;

/// <summary>
/// Task E.2：弱网仿真器配置。<br/>
/// 描述注入到网络链路的延迟/丢包/抖动/中断参数，所有参数均可独立配置。
/// </summary>
public sealed class WeakNetworkOptions
{
    /// <summary>
    /// 单向延迟基准值（毫秒，默认 200）。<br/>
    /// 数据包从 ProcessOutbound 到可被 FlushReadyPackets 取出的最小间隔。
    /// </summary>
    public int LatencyMs { get; set; } = 200;

    /// <summary>
    /// 丢包率（0..1，默认 0.05 = 5%）。<br/>
    /// 每个 outbound/inbound 包按此概率被直接丢弃。
    /// </summary>
    public double PacketLossRate { get; set; } = 0.05;

    /// <summary>
    /// 抖动幅度（毫秒，默认 50）。<br/>
    /// 实际延迟 = LatencyMs + Uniform(-JitterMs, +JitterMs)，下限钳位到 0。
    /// </summary>
    public int JitterMs { get; set; } = 50;

    /// <summary>
    /// 中断周期（tick，默认 0 = 禁用）。<br/>
    /// 当 &gt; 0 时，每隔该 tick 数触发一次持续 <see cref="InterruptionDurationTicks"/> 的中断窗口，
    /// 中断窗口内所有 outbound/inbound 包被直接丢弃。
    /// </summary>
    public int InterruptionIntervalTicks { get; set; } = 0;

    /// <summary>
    /// 中断持续时长（tick，默认 10）。<br/>
    /// 仅当 <see cref="InterruptionIntervalTicks"/> &gt; 0 时生效。
    /// </summary>
    public int InterruptionDurationTicks { get; set; } = 10;

    /// <summary>
    /// 单 tick 时长（毫秒，默认 16.67 = 60Hz）。<br/>
    /// 用于把 <see cref="LatencyMs"/>/<see cref="JitterMs"/> 换算为 tick 偏移。
    /// </summary>
    public double MsPerTick { get; set; } = 1000.0 / 60.0;

    /// <summary>RNG seed（确定性回归）。</summary>
    public int Seed { get; set; } = 0xBEEF;
}

/// <summary>
/// Task E.2：弱网仿真器。<br/>
/// 在 <see cref="ProcessOutbound"/>/<see cref="ProcessInbound"/> 注入延迟/丢包/抖动/中断，
/// 数据包按计算出的投递 tick 入队 <see cref="_pendingPackets"/>，
/// 调用方在每个 tick 调用 <see cref="FlushReadyPackets"/> 取出本 tick 应投递的包。
/// </summary>
/// <remarks>
/// 典型用法（端到端弱网测试）：
/// <code>
/// var sim = new WeakNetworkSimulator(new WeakNetworkOptions { LatencyMs = 200, PacketLossRate = 0.05 });
/// for (long tick = 0; tick &lt; durationTicks; tick++) {
///     var outbound = sim.ProcessOutbound(rawFrame, tick);   // 返回 null 表示丢弃或延迟
///     var ready = sim.FlushReadyPackets(tick);               // 取出本 tick 应投递的包
///     foreach (var frame in ready) { /* 解码并应用 */ }
/// }
/// </code>
/// </remarks>
public sealed class WeakNetworkSimulator
{
    private readonly WeakNetworkOptions _options;
    private readonly Random _rng;
    private readonly Queue<PacketEntry> _pendingPackets = new();

    /// <summary>
    /// 待投递包队列条目：携带原始字节与投递 tick。
    /// </summary>
    private sealed class PacketEntry
    {
        public byte[] Packet { get; set; } = Array.Empty<byte>();
        public long DeliveryTick { get; set; }
    }

    /// <summary>累计送入仿真的包数（outbound + inbound）。</summary>
    public long PacketsSent { get; private set; }

    /// <summary>累计被丢弃的包数（含丢包率丢弃 + 中断窗口丢弃）。</summary>
    public long PacketsDropped { get; private set; }

    /// <summary>累计被延迟调度（入队等待投递）的包数。</summary>
    public long PacketsDelayed { get; private set; }

    /// <summary>累计实际投递的包数（通过 FlushReadyPackets 返回）。</summary>
    public long PacketsDelivered { get; private set; }

    /// <summary>实际丢包率 = PacketsDropped / PacketsSent。</summary>
    public double EffectiveLossRate => PacketsSent > 0 ? (double)PacketsDropped / PacketsSent : 0.0;

    public WeakNetworkSimulator(WeakNetworkOptions? options = null)
    {
        _options = options ?? new WeakNetworkOptions();
        _rng = new Random(_options.Seed);
    }

    /// <summary>
    /// 处理 outbound 包（client→server 或 server→client 上行链路）。<br/>
    /// 返回值语义：
    /// <list type="bullet">
    ///   <item><c>null</c>：包被丢弃（丢包率/中断）或被延迟入队（等待未来 tick 投递）。</item>
    ///   <item>非空：包立即投递（仅在 LatencyMs=0 且未被丢弃时出现）。</item>
    /// </list>
    /// </summary>
    /// <param name="packet">原始字节帧。</param>
    /// <param name="currentTick">当前模拟 tick。</param>
    public byte[]? ProcessOutbound(byte[] packet, long currentTick)
    {
        if (packet is null || packet.Length == 0) return null;
        PacketsSent++;
        return ProcessPacketInternal(packet, currentTick);
    }

    /// <summary>
    /// 处理 inbound 包（与 <see cref="ProcessOutbound"/> 对称，复用同一套延迟/丢包/抖动/中断规则）。
    /// </summary>
    public byte[]? ProcessInbound(byte[] packet, long currentTick)
    {
        if (packet is null || packet.Length == 0) return null;
        PacketsSent++;
        return ProcessPacketInternal(packet, currentTick);
    }

    /// <summary>
    /// 返回当前 tick 应投递的所有包（delivery tick ≤ <paramref name="currentTick"/>），并从队列移除。
    /// </summary>
    public List<byte[]> FlushReadyPackets(long currentTick)
    {
        var ready = new List<byte[]>();
        // Queue 是 FIFO，按投递 tick 递增顺序入队，可从队首连续取出已到期的包。
        while (_pendingPackets.Count > 0)
        {
            var head = _pendingPackets.Peek();
            if (head.DeliveryTick > currentTick)
            {
                break;
            }
            _pendingPackets.Dequeue();
            ready.Add(head.Packet);
            PacketsDelivered++;
        }
        return ready;
    }

    /// <summary>重置仿真器状态（清空队列 + 重置计数器 + 重置 RNG）。</summary>
    public void Reset()
    {
        _pendingPackets.Clear();
        PacketsSent = 0;
        PacketsDropped = 0;
        PacketsDelayed = 0;
        PacketsDelivered = 0;
    }

    // ── 内部实现 ──────────────────────────────────────────────────────────

    private byte[]? ProcessPacketInternal(byte[] packet, long currentTick)
    {
        // 1. 中断窗口检查：当前 tick 是否落在任一中断周期内。
        if (IsInInterruption(currentTick))
        {
            PacketsDropped++;
            return null;
        }

        // 2. 丢包率检查：按 PacketLossRate 概率丢弃。
        if (_options.PacketLossRate > 0 && _rng.NextDouble() < _options.PacketLossRate)
        {
            PacketsDropped++;
            return null;
        }

        // 3. 计算延迟（毫秒）→ tick 偏移。
        // 实际延迟 = LatencyMs + Uniform(-JitterMs, +JitterMs)，下限 0。
        var jitterSign = _rng.NextDouble() * 2.0 - 1.0; // [-1, 1]
        var latencyMs = _options.LatencyMs + jitterSign * _options.JitterMs;
        if (latencyMs < 0) latencyMs = 0;

        var msPerTick = _options.MsPerTick > 0 ? _options.MsPerTick : 1.0;
        var delayTicks = (long)Math.Round(latencyMs / msPerTick);
        if (delayTicks < 0) delayTicks = 0;

        // 4. 立即投递 vs 入队等待。
        if (delayTicks == 0)
        {
            // 无延迟：直接返回（仍计入已投递）。
            PacketsDelivered++;
            return packet;
        }

        // 入队等待 future tick 投递。
        var entry = new PacketEntry
        {
            Packet = packet,
            DeliveryTick = currentTick + delayTicks,
        };
        _pendingPackets.Enqueue(entry);
        PacketsDelayed++;
        return null;
    }

    /// <summary>
    /// 判断当前 tick 是否处于中断窗口。<br/>
    /// 中断周期 = <see cref="WeakNetworkOptions.InterruptionIntervalTicks"/>，
    /// 每 period 内最后 <see cref="WeakNetworkOptions.InterruptionDurationTicks"/> 个 tick 为中断窗口。
    /// </summary>
    private bool IsInInterruption(long currentTick)
    {
        if (_options.InterruptionIntervalTicks <= 0) return false;
        var period = _options.InterruptionIntervalTicks;
        var phase = currentTick % period;
        // 中断窗口：period 末尾的 InterruptionDurationTicks 个 tick
        return phase >= period - _options.InterruptionDurationTicks;
    }
}

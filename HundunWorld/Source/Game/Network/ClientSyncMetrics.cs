using System;
using System.Threading;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 客户端网络同步可观测性指标（Phase C2）。
    /// 静态类，线程安全（Interlocked 计数器 + volatile 浮点），供 UI/调试面板读取。
    /// </summary>
    public static class ClientSyncMetrics
    {
        // ─── RTT 估计（ms）：基于 InputAck.EchoClientTick 或 HeartbeatResponse ───

        /// <summary>估计 RTT（ms），指数加权移动平均。</summary>
        public static float EstimatedRttMs => Volatile.Read(ref _estimatedRttMs);

        /// <summary>RTT 抖动（ms），标准差估计。</summary>
        public static float RttJitterMs => Volatile.Read(ref _rttJitterMs);

        private static float _estimatedRttMs;
        private static float _rttJitterMs;
        private const float RttAlpha = 0.125f;  // EWMA 平滑因子（与 TCP RTT 估计一致）
        private const float JitterBeta = 0.25f;

        /// <summary>记录一次 RTT 样本（ms），更新 EWMA 和抖动。</summary>
        public static void RecordRtt(float rttMs)
        {
            if (rttMs < 0) return;
            var old = Volatile.Read(ref _estimatedRttMs);
            var newRtt = old == 0f ? rttMs : old + RttAlpha * (rttMs - old);
            Volatile.Write(ref _estimatedRttMs, newRtt);

            var deviation = Math.Abs(rttMs - newRtt);
            var oldJitter = Volatile.Read(ref _rttJitterMs);
            var newJitter = oldJitter == 0f ? deviation : oldJitter + JitterBeta * (deviation - oldJitter);
            Volatile.Write(ref _rttJitterMs, newJitter);
        }

        // ─── 快照接收 ───

        /// <summary>累计接收快照数。</summary>
        public static long SnapshotsReceived => Interlocked.Read(ref _snapshotsReceived);
        private static long _snapshotsReceived;

        /// <summary>最近快照间隔滑动平均（ms）。</summary>
        public static float SnapshotIntervalMs => Volatile.Read(ref _snapshotIntervalMs);
        private static float _snapshotIntervalMs;

        /// <summary>快照间隔标准差（ms）—— 即 jitter。</summary>
        public static float SnapshotJitterMs => Volatile.Read(ref _snapshotJitterMs);
        private static float _snapshotJitterMs;

        private static long _lastSnapshotReceiveTimestamp; // Stopwatch ticks
        private static readonly object _snapshotLock = new();

        /// <summary>记录一次快照到达，更新间隔统计。</summary>
        public static void RecordSnapshotReceived()
        {
            Interlocked.Increment(ref _snapshotsReceived);
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_snapshotLock)
            {
                if (_lastSnapshotReceiveTimestamp > 0)
                {
                    var intervalMs = (float)((now - _lastSnapshotReceiveTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    var oldAvg = _snapshotIntervalMs;
                    var newAvg = oldAvg == 0f ? intervalMs : oldAvg + 0.2f * (intervalMs - oldAvg);
                    _snapshotIntervalMs = newAvg;

                    var deviation = Math.Abs(intervalMs - newAvg);
                    var oldJitter = _snapshotJitterMs;
                    _snapshotJitterMs = oldJitter == 0f ? deviation : oldJitter + 0.25f * (deviation - oldJitter);
                }
                _lastSnapshotReceiveTimestamp = now;
            }
        }

        // ─── 预测与 Reconciliation ───

        /// <summary>预测误差滑动平均（米）。</summary>
        public static float PredictionErrorAvg => Volatile.Read(ref _predictionErrorAvg);
        private static float _predictionErrorAvg;

        /// <summary>记录一次预测误差样本（米）。</summary>
        public static void RecordPredictionError(float errorMeters)
        {
            var old = Volatile.Read(ref _predictionErrorAvg);
            var newVal = old == 0f ? errorMeters : old + 0.1f * (errorMeters - old);
            Volatile.Write(ref _predictionErrorAvg, newVal);
        }

        /// <summary>累计修正次数。</summary>
        public static long CorrectionsApplied => Interlocked.Read(ref _correctionsApplied);
        private static long _correctionsApplied;

        public static void RecordCorrection() => Interlocked.Increment(ref _correctionsApplied);

        /// <summary>累计 InputAck 接收数。</summary>
        public static long InputAcksReceived => Interlocked.Read(ref _inputAcksReceived);
        private static long _inputAcksReceived;

        public static void RecordInputAck() => Interlocked.Increment(ref _inputAcksReceived);

        // ─── 发送 ───

        /// <summary>累计输入包发送数。</summary>
        public static long InputPacketsSent => Interlocked.Read(ref _inputPacketsSent);
        private static long _inputPacketsSent;

        public static void RecordInputSent() => Interlocked.Increment(ref _inputPacketsSent);

        /// <summary>累计冗余重传数。</summary>
        public static long InputRetransmits => Interlocked.Read(ref _inputRetransmits);
        private static long _inputRetransmits;

        public static void RecordRetransmit() => Interlocked.Increment(ref _inputRetransmits);

        // ─── 连接 ───

        /// <summary>累计重连尝试次数。</summary>
        public static long ReconnectAttempts => Interlocked.Read(ref _reconnectAttempts);
        private static long _reconnectAttempts;

        public static void RecordReconnectAttempt() => Interlocked.Increment(ref _reconnectAttempts);

        /// <summary>累计重连成功次数。</summary>
        public static long ReconnectSuccesses => Interlocked.Read(ref _reconnectSuccesses);
        private static long _reconnectSuccesses;

        public static void RecordReconnectSuccess() => Interlocked.Increment(ref _reconnectSuccesses);

        /// <summary>距上次收到快照的时间（秒）。由外部每帧更新或按需计算。</summary>
        public static float TimeSinceLastSnapshotSeconds
        {
            get
            {
                var last = Volatile.Read(ref _lastSnapshotReceiveTimestamp);
                if (last == 0) return float.MaxValue;
                var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - last;
                return (float)(elapsed / (double)System.Diagnostics.Stopwatch.Frequency);
            }
        }

        // ─── 未知包 ───

        /// <summary>累计收到未知 Kind 的包数。</summary>
        public static long UnknownPackets => Interlocked.Read(ref _unknownPackets);
        private static long _unknownPackets;

        public static void RecordUnknownPacket() => Interlocked.Increment(ref _unknownPackets);

        // ─── 位置覆盖（替代 FlaxActorSyncSystem 诊断字典） ───

        /// <summary>累计位置覆盖检测次数。</summary>
        public static long PositionOverrideCount => Interlocked.Read(ref _positionOverrideCount);
        private static long _positionOverrideCount;

        public static void RecordPositionOverride() => Interlocked.Increment(ref _positionOverrideCount);

        // ─── 快照溢出（Phase C6） ───

        /// <summary>累计单帧快照消费溢出次数。</summary>
        public static long SnapshotOverflowCount => Interlocked.Read(ref _snapshotOverflowCount);
        private static long _snapshotOverflowCount;

        public static void RecordSnapshotOverflow() => Interlocked.Increment(ref _snapshotOverflowCount);

        // ─── 重置（断线重连时可选调用） ───

        /// <summary>重置所有指标（重连时调用）。</summary>
        public static void Reset()
        {
            Volatile.Write(ref _estimatedRttMs, 0f);
            Volatile.Write(ref _rttJitterMs, 0f);
            Interlocked.Exchange(ref _snapshotsReceived, 0);
            lock (_snapshotLock)
            {
                _snapshotIntervalMs = 0f;
                _snapshotJitterMs = 0f;
                _lastSnapshotReceiveTimestamp = 0;
            }
            Volatile.Write(ref _predictionErrorAvg, 0f);
            Interlocked.Exchange(ref _correctionsApplied, 0);
            Interlocked.Exchange(ref _inputAcksReceived, 0);
            Interlocked.Exchange(ref _inputPacketsSent, 0);
            Interlocked.Exchange(ref _inputRetransmits, 0);
            Interlocked.Exchange(ref _unknownPackets, 0);
            Interlocked.Exchange(ref _positionOverrideCount, 0);
            Interlocked.Exchange(ref _snapshotOverflowCount, 0);
        }
    }
}

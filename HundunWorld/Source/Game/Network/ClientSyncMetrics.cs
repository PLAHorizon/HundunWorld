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

        // ─── 插值延迟（远程角色移动平滑性可观测） ───

        /// <summary>当前自适应插值延迟（ms），由 ECSUpdateDriver 每帧从 SnapshotApplySystem 采集转发。</summary>
        public static float CurrentInterpolationDelayMs => Volatile.Read(ref _currentInterpolationDelayMs);
        private static float _currentInterpolationDelayMs;

        /// <summary>记录当前插值延迟（秒），内部转 ms 存储。</summary>
        public static void RecordInterpolationDelay(float delaySeconds)
        {
            Volatile.Write(ref _currentInterpolationDelayMs, delaySeconds * 1000f);
        }

        // ─── Stale 实体清理计数 ───

        /// <summary>累计因超时（90 秒未收到快照）被兜底清理的远程实体数。</summary>
        public static long StaleEntitiesCleaned => Interlocked.Read(ref _staleEntitiesCleaned);
        private static long _staleEntitiesCleaned;

        public static void RecordStaleEntityCleaned() => Interlocked.Increment(ref _staleEntitiesCleaned);

        // ─── 平滑度评分（60 帧滑动窗口：位移标准差 + 帧时间抖动综合评分） ───

        /// <summary>
        /// 远程角色移动平滑度评分（0..100，越大越平滑）。
        /// 基于 60 帧滑动窗口的位移 delta 标准差与帧时间标准差综合计算：
        /// 匀速移动（delta 稳定、帧时间稳定）→ 评分高；卡顿/跳跃（delta 波动大）→ 评分低。
        /// </summary>
        public static float SmoothnessScore => Volatile.Read(ref _smoothnessScore);
        private static float _smoothnessScore;
        private static readonly object _smoothnessLock = new();
        private static readonly float[] _smoothnessPositionDeltas = new float[60];
        private static readonly float[] _smoothnessFrameTimes = new float[60];
        private static int _smoothnessSampleIndex;
        private static int _smoothnessSampleCount;

        /// <summary>
        /// 记录一帧平滑度采样（位置 delta + 帧时间），维护 60 帧滑动窗口并更新综合评分。
        /// </summary>
        /// <param name="positionDeltaMeters">本帧远程角色平均渲染位置 delta（米）。</param>
        /// <param name="frameTimeSeconds">本帧帧时间（秒）。</param>
        public static void RecordSmoothnessSample(float positionDeltaMeters, float frameTimeSeconds)
        {
            lock (_smoothnessLock)
            {
                _smoothnessPositionDeltas[_smoothnessSampleIndex] = positionDeltaMeters;
                _smoothnessFrameTimes[_smoothnessSampleIndex] = frameTimeSeconds;
                _smoothnessSampleIndex = (_smoothnessSampleIndex + 1) % 60;
                if (_smoothnessSampleCount < 60) _smoothnessSampleCount++;

                if (_smoothnessSampleCount < 2)
                {
                    Volatile.Write(ref _smoothnessScore, 100f);
                    return;
                }

                // 计算位移 delta 的均值与标准差
                float sumDelta = 0f, sumFrame = 0f;
                for (int i = 0; i < _smoothnessSampleCount; i++)
                {
                    sumDelta += _smoothnessPositionDeltas[i];
                    sumFrame += _smoothnessFrameTimes[i];
                }
                var meanDelta = sumDelta / _smoothnessSampleCount;
                var meanFrame = sumFrame / _smoothnessSampleCount;

                float varDelta = 0f, varFrame = 0f;
                for (int i = 0; i < _smoothnessSampleCount; i++)
                {
                    var dd = _smoothnessPositionDeltas[i] - meanDelta;
                    var ff = _smoothnessFrameTimes[i] - meanFrame;
                    varDelta += dd * dd;
                    varFrame += ff * ff;
                }
                var stdDelta = MathF.Sqrt(varDelta / _smoothnessSampleCount);
                var stdFrame = MathF.Sqrt(varFrame / _smoothnessSampleCount);

                // 评分 = 100 / (1 + stdDelta * 5 + stdFrame * 200)
                // stdDelta 单位米（卡顿时位移跳变大使标准差大），stdFrame 单位秒（帧时间抖动）
                var score = 100f / (1f + stdDelta * 5f + stdFrame * 200f);
                Volatile.Write(ref _smoothnessScore, score);
            }
        }

        // ─── 当前策略组合（供运维查询对比不同网络环境下的方案表现） ───

        /// <summary>当前同步策略组合描述（如 "Active|Lerp+DeadReckoning|Medium|180ms|20Hz"）。</summary>
        public static string CurrentStrategyCombo => Volatile.Read(ref _currentStrategyCombo);
        private static string _currentStrategyCombo = string.Empty;

        /// <summary>设置当前策略组合描述。</summary>
        public static void SetCurrentStrategyCombo(string combo)
        {
            Volatile.Write(ref _currentStrategyCombo, combo ?? string.Empty);
        }

        // ─── 多角色同步指标（Phase C7：分级调度/降档/帧率降级可观测） ───

        /// <summary>当前远程角色渲染数量（FlaxActorSyncSystem 每帧写入）。</summary>
        public static int RemoteEntityCount => Volatile.Read(ref _remoteEntityCount);
        private static int _remoteEntityCount;

        /// <summary>本帧实际执行位置同步的实体数（分级调度后）。</summary>
        public static int PerFrameSyncedEntityCount => Volatile.Read(ref _perFrameSyncedEntityCount);
        private static int _perFrameSyncedEntityCount;

        /// <summary>分级调度降档累计次数（性能自适应触发）。</summary>
        public static long DegradeEventCount => Interlocked.Read(ref _degradeEventCount);
        private static long _degradeEventCount;

        /// <summary>帧率降级累计次数（远程角色 10+ 且帧率 &lt; 30FPS 时记录）。</summary>
        public static long FrameRateDropCount => Interlocked.Read(ref _frameRateDropCount);
        private static long _frameRateDropCount;

        /// <summary>远程角色数量硬上限触发累计次数（插值暂停）。</summary>
        public static long MaxEntityCapReachedCount => Interlocked.Read(ref _maxEntityCapReachedCount);
        private static long _maxEntityCapReachedCount;

        /// <summary>非法快照跳过累计次数（异常隔离）。</summary>
        public static long InvalidSnapshotSkippedCount => Interlocked.Read(ref _invalidSnapshotSkippedCount);
        private static long _invalidSnapshotSkippedCount;

        /// <summary>记录当前远程角色数量。</summary>
        public static void RecordRemoteEntityCount(int count) => Volatile.Write(ref _remoteEntityCount, count);

        /// <summary>记录本帧实际同步实体数。</summary>
        public static void RecordPerFrameSynced(int count) => Volatile.Write(ref _perFrameSyncedEntityCount, count);

        /// <summary>记录一次分级调度降档事件。</summary>
        public static void RecordDegradeEvent() => Interlocked.Increment(ref _degradeEventCount);

        /// <summary>记录一次帧率降级事件。</summary>
        public static void RecordFrameRateDrop() => Interlocked.Increment(ref _frameRateDropCount);

        /// <summary>记录一次远程角色数量硬上限触发事件。</summary>
        public static void RecordMaxEntityCapReached() => Interlocked.Increment(ref _maxEntityCapReachedCount);

        /// <summary>记录一次非法快照跳过事件。</summary>
        public static void RecordInvalidSnapshotSkipped() => Interlocked.Increment(ref _invalidSnapshotSkippedCount);

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
            Volatile.Write(ref _currentInterpolationDelayMs, 0f);
            Interlocked.Exchange(ref _staleEntitiesCleaned, 0);
            lock (_smoothnessLock)
            {
                _smoothnessSampleIndex = 0;
                _smoothnessSampleCount = 0;
                for (int i = 0; i < 60; i++)
                {
                    _smoothnessPositionDeltas[i] = 0f;
                    _smoothnessFrameTimes[i] = 0f;
                }
            }
            Volatile.Write(ref _smoothnessScore, 0f);
            Volatile.Write(ref _currentStrategyCombo, string.Empty);
            Volatile.Write(ref _remoteEntityCount, 0);
            Volatile.Write(ref _perFrameSyncedEntityCount, 0);
            Interlocked.Exchange(ref _degradeEventCount, 0);
            Interlocked.Exchange(ref _frameRateDropCount, 0);
            Interlocked.Exchange(ref _maxEntityCapReachedCount, 0);
            Interlocked.Exchange(ref _invalidSnapshotSkippedCount, 0);
        }
    }
}

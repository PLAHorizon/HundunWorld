using FlaxEngine;
using Horizon.Game.ECS.Arch.Diagnostics;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// <see cref="ISyncDiagnosticsSink"/> 的游戏层实现：将同步管线诊断事件转发到 Flax <see cref="Debug"/> 结构化日志。
    /// 输出含角色 ID 与触发数值，供运维排查远程角色移动异常。
    /// 所有异常被吞掉（try-catch 包裹），避免日志失败影响同步主逻辑。
    /// </summary>
    public sealed class SyncDiagnosticsSinkImpl : ISyncDiagnosticsSink
    {
        /// <inheritdoc />
        public void OnTeleportJump(ulong entityId, float distance, long serverTick)
        {
            try
            {
                Debug.Log($"[SyncDiag] TeleportJump Entity={entityId} Distance={distance:F2}m ServerTick={serverTick}");
            }
            catch { /* 吞掉异常，避免日志失败影响同步逻辑 */ }
        }

        /// <inheritdoc />
        public void OnCorrectionStormTriggered(ulong entityId, int recentCount, float windowSeconds)
        {
            try
            {
                Debug.LogWarning($"[SyncDiag] CorrectionStorm Entity={entityId} Count={recentCount} Window={windowSeconds:F1}s");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnStaleCorrectionSkipped(ulong entityId, long lastProcessedTick, long lastAckedTick)
        {
            try
            {
                Debug.Log($"[SyncDiag] StaleCorrectionSkipped Entity={entityId} ProcessedTick={lastProcessedTick} AckedTick={lastAckedTick}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnAdaptiveWindowAdjusted(float oldDelaySeconds, float newDelaySeconds, float rttSeconds, float jitterSeconds)
        {
            try
            {
                Debug.Log($"[SyncDiag] AdaptiveWindow {oldDelaySeconds * 1000:F0}ms->{newDelaySeconds * 1000:F0}ms RTT={rttSeconds * 1000:F0}ms Jitter={jitterSeconds * 1000:F0}ms");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnBaselineResyncRequested(long expectedBaselineTick, long receivedBaselineTick)
        {
            try
            {
                Debug.LogWarning($"[SyncDiag] BaselineResync Expected={expectedBaselineTick} Received={receivedBaselineTick}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnConfigInvalid(string fieldName, float configuredValue, float fallbackValue, bool isWarningOnly)
        {
            try
            {
                var suffix = isWarningOnly
                    ? "（配置未回退，可能仍表现为闪跳）"
                    : $"（已回退为 {fallbackValue}）";
                Debug.LogWarning($"[SyncDiag] ConfigInvalid Field={fieldName} Configured={configuredValue} Fallback={fallbackValue} WarningOnly={isWarningOnly} {suffix}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnInvalidSnapshotSkipped(ulong entityId, long serverTick)
        {
            try
            {
                Debug.LogWarning($"[SyncDiag] InvalidSnapshotSkipped Entity={entityId} ServerTick={serverTick}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnMultiEntityDegraded(int remoteEntityCount, string reason)
        {
            try
            {
                Debug.LogWarning($"[SyncDiag] MultiEntityDegraded Count={remoteEntityCount} Reason={reason}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnBandwidthThrottled(long sessionId, double kbps, int fromHz, int toHz)
        {
            try
            {
                Debug.LogWarning($"[SyncDiag] BandwidthThrottled Session={sessionId} {kbps:F1}kbps {fromHz}Hz->{toHz}Hz");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnBandwidthRecovered(long sessionId, double kbps, int fromHz, int toHz)
        {
            try
            {
                Debug.Log($"[SyncDiag] BandwidthRecovered Session={sessionId} {kbps:F1}kbps {fromHz}Hz->{toHz}Hz");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnScaleTierChanged(int entityCount, SyncScaleTier from, SyncScaleTier to)
        {
            try
            {
                Debug.Log($"[SyncDiag] ScaleTierChanged Count={entityCount} {from}->{to}");
            }
            catch { }
        }

        /// <inheritdoc />
        public void OnScaleDegrade(ulong entityId, float distanceMeters, string reason)
        {
            try
            {
                Debug.Log($"[SyncDiag] ScaleDegrade Entity={entityId} Dist={distanceMeters:F1}m Reason={reason}");
            }
            catch { }
        }
    }
}
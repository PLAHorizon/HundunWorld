using FlaxEngine;
using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;

namespace HundunWorld.Game
{
    /// <summary>
    /// ECS更新驱动脚本，负责在Flax主线程每帧更新ECS系统，
    /// 并确保 FlaxActorSyncSystem（ECS→Actor 视觉桥接）已挂载。
    /// 同时消费 InputSendQueue 中的输入包并通过网络发送到服务端。
    /// </summary>
    public class ECSUpdateDriver : Script
    {
        private FlaxActorSyncSystem _actorSyncSystem;
        private bool _actorSyncInitialized = false;

        private bool _firstUpdate = true;

        /// <summary>
        /// 上次输出"握手未完成"诊断日志的游戏时间（秒），用于限频。
        /// </summary>
        private float _lastHandshakeWaitLogTime = 0f;

        /// <summary>
    /// 是否已经输出过一次"握手未完成"诊断日志。
    /// 首次立即输出，后续按 5 秒间隔输出。
    /// </summary>
    private bool _handshakeWaitLogged = false;

    /// <summary>诊断：FlushInputSendQueue 帧计数器，用于限频日志输出。</summary>
    private long _diagSendFrameCount;

    /// <summary>[Phase C2] 上次采集的 InputSendSystem 重传计数，用于计算增量。</summary>
    private long _lastRetransmitCount;

    /// <summary>[Phase C2] 上次采集的 ReconciliationSystem 修正计数，用于计算增量。</summary>
    private int _lastCorrectionCount;

        /// <summary>[Phase C6] 上次采集的 SnapshotApplySystem 溢出计数。</summary>
        private long _lastOverflowCount;

        /// <summary>上次采集的 SnapshotApplySystem Stale 实体清理计数，用于增量转发。</summary>
        private long _lastStaleEntitiesCleaned;

        /// <summary>诊断 Sink 是否已注入到各同步系统（一次性注入标志）。</summary>
        private bool _diagnosticsInjected;

        /// <summary>
        /// 修正风暴检测窗口内允许的最大修正次数（配置外化注入）。
        /// 为 null 时使用 ReconciliationSystem 默认值 5。由外部配置加载逻辑在启动时设置。
        /// </summary>
        public int? ReconciliationStormThreshold { get; set; }

        /// <summary>
        /// 修正风暴检测窗口（秒，配置外化注入）。
        /// 为 null 时使用 ReconciliationSystem 默认值 2.0f。由外部配置加载逻辑在启动时设置。
        /// </summary>
        public float? ReconciliationStormWindowSeconds { get; set; }

        /// <summary>
        /// 修正风暴冷却时间（秒，配置外化注入）。
        /// 为 null 时使用 ReconciliationSystem 默认值 1.0f。由外部配置加载逻辑在启动时设置。
        /// </summary>
        public float? ReconciliationStormCooldownSeconds { get; set; }

        public override void OnStart()
        {
            Debug.Log("[ECSUpdateDriver] OnStart 被调用");

            // 确保 FlaxActorSyncSystem 已挂载到同一 Actor 上
            _actorSyncSystem = Actor.GetScript<FlaxActorSyncSystem>();
            if (_actorSyncSystem == null)
            {
                _actorSyncSystem = Actor.AddScript<FlaxActorSyncSystem>();
                Debug.Log("[ECSUpdateDriver] 已自动挂载 FlaxActorSyncSystem");
            }
            else
            {
                Debug.Log("[ECSUpdateDriver] FlaxActorSyncSystem 已存在，无需挂载");
            }
            _actorSyncInitialized = true;
        }

        public override void OnUpdate()
        {
            // 首帧诊断日志
            if (_firstUpdate)
            {
                _firstUpdate = false;
                var instance = HundunWorldGame.Instance;
                var plugin = HundunWorldGamePlugin.Instance;
                Debug.Log($"[ECSUpdateDriver] 首帧 OnUpdate: Instance={instance != null}, Plugin={plugin != null}, ECSManager={instance?.ECSManager != null}, ArchHost={instance?.ArchHost != null}");
            }

            // 确保游戏已初始化并启动
            if (HundunWorldGame.Instance != null &&
                HundunWorldGamePlugin.Instance != null &&
                HundunWorldGame.Instance.ECSManager != null)
            {
                try
                {
                    // 在Flax主线程上更新ECS系统
                    HundunWorldGame.Instance.ECSManager.Update(Time.DeltaTime);

                    // 驱动 ArchWorldHost（包含 SnapshotApplySystem、InterpolationSystem 等同步系统）
                    var archHost = HundunWorldGame.Instance.ArchHost;
                    if (archHost != null)
                    {
                        // 一次性注入诊断 Sink 到各同步系统（ISyncDiagnosticsSink → SyncDiagnosticsSinkImpl）
                        if (!_diagnosticsInjected)
                        {
                            var sink = new SyncDiagnosticsSinkImpl();
                            SnapshotApplySystem.Diagnostics = sink;
                            foreach (var sys in archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.Render))
                                if (sys is InterpolationSystem interp) interp.Diagnostics = sink;
                            foreach (var sys in archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate))
                            {
                                if (sys is ReconciliationSystem recon)
                                {
                                    recon.Diagnostics = sink;

                                    // 配置外化注入：风暴检测参数（由外部配置加载逻辑在启动时设置）
                                    if (ReconciliationStormThreshold.HasValue)
                                        recon.StormThreshold = ReconciliationStormThreshold.Value;
                                    if (ReconciliationStormWindowSeconds.HasValue)
                                        recon.StormWindowSeconds = ReconciliationStormWindowSeconds.Value;
                                    if (ReconciliationStormCooldownSeconds.HasValue)
                                        recon.StormCooldownSeconds = ReconciliationStormCooldownSeconds.Value;
                                }
                            }
                            _diagnosticsInjected = true;
                        }

                        archHost.Tick(TimeSpan.FromSeconds(Time.DeltaTime));

                        // 消费 InputSendQueue：将 ECS 管线产生的 InputPacket 发送到服务端
                        FlushInputSendQueue();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ECSUpdateDriver] ECS Tick 异常: {ex.Message}\n{ex.StackTrace}");
                }

                // 驱动 HundunWorldGame 自身更新（超时兜底等逻辑）
                HundunWorldGame.Instance.OnUpdate();
            }
        }

        /// <summary>
        /// 从 InputSendQueue 取出所有 InputPacket，编码为 SyncFrameMessage 并通过网络发送。
        /// </summary>
        private void FlushInputSendQueue()
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage())
                return;

            if (!networkManager.IsSyncHandshakeComplete)
            {
                // 限频日志：首次立即输出，后续每 5 秒最多一次
                var now = Time.GameTime;
                if (!_handshakeWaitLogged || (now - _lastHandshakeWaitLogTime) >= 5f)
                {
                    FlaxEngine.Debug.LogWarning($"[ECSUpdateDriver] InputPacket 被丢弃：同步握手未完成（已等待 {now:F1} 秒）");
                    _lastHandshakeWaitLogTime = now;
                    _handshakeWaitLogged = true;
                }

                // 触发握手重试：如果距上次发送已超过间隔，自动重发握手包
                _ = networkManager.TryEnsureSyncHandshakeAsync();
                return;
            }

            var pendingInputs = InputSendSystem.Instance?.GetPendingInputs();
            if (pendingInputs == null || pendingInputs.Count == 0)
                return;

            // 诊断：每 120 帧（≈2 秒）输出发送状态
            _diagSendFrameCount++;
            if (_diagSendFrameCount <= 3 || _diagSendFrameCount % 120 == 1)
            {
                FlaxEngine.Debug.Log($"[ECSUpdateDriver] FlushInputSendQueue#{_diagSendFrameCount}: SendingInputs={pendingInputs.Count}, HandshakeComplete={networkManager.IsSyncHandshakeComplete}");
            }

            foreach (var inputPacket in pendingInputs)
            {
                try
                {
                    SyncPacketCodec.Encode(inputPacket, out var frame, out var frameLength);
                    try
                    {
                        var payload = new byte[frameLength];
                        System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                        var syncFrame = new SyncFrameMessage
                        {
                            Frame = payload,
                            PacketKind = (byte)inputPacket.Kind,
                            ProtocolVersion = inputPacket.ProtocolVersion,
                        };

                        _ = networkManager.SendAsync(syncFrame);

                        // [Phase C2] 记录输入包发送
                        ClientSyncMetrics.RecordInputSent();
                    }
                    finally
                    {
                        SyncPacketCodec.ReturnFrame(frame);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ECSUpdateDriver] 发送输入包失败: {ex.Message}");
                }
            }

            // 上送 baseline 重传请求（SnapshotApplySystem 产生，delta 解码时 baseline 不匹配）
            FlushPendingResyncRequests();

            // [Phase C2] 转发 ECS 系统指标到 ClientSyncMetrics
            ForwardEcsMetrics();
        }

        /// <summary>
        /// 消费 SnapshotApplySystem 产生的 baseline 重传请求，编码为 SyncFrameMessage 发送服务端。
        /// 走可靠通道（SyncFrameMessage），服务端收到后强制下发全量快照。
        /// </summary>
        private void FlushPendingResyncRequests()
        {
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            if (networkManager == null || !networkManager.CanSendMessage() || !networkManager.IsSyncHandshakeComplete)
                return;

            BaselineResyncRequestPacket? req;
            while ((req = SnapshotApplySystem.TakePendingResyncRequest()) != null)
            {
                try
                {
                    SyncPacketCodec.Encode(req, out var frame, out var frameLength);
                    try
                    {
                        var payload = new byte[frameLength];
                        System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);
                        var syncFrame = new SyncFrameMessage
                        {
                            Frame = payload,
                            PacketKind = (byte)req.Kind,
                            ProtocolVersion = req.ProtocolVersion,
                        };
                        _ = networkManager.SendAsync(syncFrame);
                    }
                    finally
                    {
                        SyncPacketCodec.ReturnFrame(frame);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ECSUpdateDriver] 发送 baseline 重传请求失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// [Phase C2] 将 ECS 系统的内部计数器增量转发到 ClientSyncMetrics。
        /// </summary>
        private void ForwardEcsMetrics()
        {
            // 冗余重传增量
            var inputSend = InputSendSystem.Instance;
            if (inputSend != null)
            {
                long currentRetransmits = inputSend.TotalRetransmits;
                var delta = currentRetransmits - _lastRetransmitCount;
                for (long i = 0; i < delta; i++)
                    ClientSyncMetrics.RecordRetransmit();
                _lastRetransmitCount = currentRetransmits;
            }

            // 修正次数 + 预测误差 + 溢出计数
            var archHost = HundunWorldGame.Instance?.ArchHost;
            if (archHost != null)
            {
                var systems = archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate);
                foreach (var sys in systems)
                {
                    if (sys is ReconciliationSystem reconc)
                    {
                        int currentCorrections = reconc.TotalCorrectionsApplied;
                        var deltaCorr = currentCorrections - _lastCorrectionCount;
                        for (int i = 0; i < deltaCorr; i++)
                            ClientSyncMetrics.RecordCorrection();
                        _lastCorrectionCount = currentCorrections;

                        if (reconc.HasNewPredictionError)
                        {
                            ClientSyncMetrics.RecordPredictionError(reconc.LastPredictionError);
                            reconc.HasNewPredictionError = false;
                        }
                        break;
                    }
                }

                // [Phase C6] 转发 SnapshotApplySystem 溢出计数
                var netSystems = archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.NetworkReceive);
                foreach (var sys in netSystems)
                {
                    if (sys is SnapshotApplySystem snapshotSys)
                    {
                        long currentOverflow = snapshotSys.OverflowCount;
                        var deltaOverflow = currentOverflow - _lastOverflowCount;
                        for (long i = 0; i < deltaOverflow; i++)
                            ClientSyncMetrics.RecordSnapshotOverflow();
                        _lastOverflowCount = currentOverflow;

                        // 转发自适应插值延迟（秒→内部转 ms）
                        ClientSyncMetrics.RecordInterpolationDelay(SnapshotApplySystem.AdaptiveInterpolationDelaySeconds);

                        // 转发 Stale 实体清理计数增量（避免重复计数）
                        long currentStaleCleaned = snapshotSys.StaleEntitiesCleaned;
                        var deltaStale = currentStaleCleaned - _lastStaleEntitiesCleaned;
                        for (long i = 0; i < deltaStale; i++)
                            ClientSyncMetrics.RecordStaleEntityCleaned();
                        _lastStaleEntitiesCleaned = currentStaleCleaned;
                        break;
                    }
                }

                // 转发 InterpolationSystem 平滑度采样 + 拼接策略组合
                var renderSystems = archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.Render);
                foreach (var sys in renderSystems)
                {
                    if (sys is InterpolationSystem interpSys)
                    {
                        if (interpSys.HasNewSmoothnessSample)
                        {
                            ClientSyncMetrics.RecordSmoothnessSample(
                                interpSys.LastFrameSmoothnessPositionDeltaMeters,
                                interpSys.LastFrameSmoothnessFrameTimeSeconds);
                        }

                        // 拼接策略组合（插值延迟 + 网络质量等级 + Dead Reckoning 状态 + 平滑度采样），供运维查询对比不同网络环境表现
                        var delayMs = SnapshotApplySystem.AdaptiveInterpolationDelaySeconds * 1000f;
                        var quality = SnapshotApplySystem.CurrentNetworkQualityLevel;
                        var deadReckoning = quality != Horizon.Game.ECS.Arch.Diagnostics.NetworkQualityLevel.Strong ? "Lerp+DeadReckoning" : "Lerp";
                        ClientSyncMetrics.SetCurrentStrategyCombo(
                            $"Active|{deadReckoning}|{quality}|{delayMs:F0}ms|SmoothSample={(interpSys.HasNewSmoothnessSample ? "Y" : "N")}");
                        break;
                    }
                }
            }
        }
    }
}

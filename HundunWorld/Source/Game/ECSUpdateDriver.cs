using FlaxEngine;
using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Diagnostics;
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

    /// <summary>上次采集的 SnapshotApplySystem 非法快照跳过计数，用于增量转发。</summary>
    private long _lastInvalidSnapshotsSkipped;

    /// <summary>[RemoteVisibility] 可见性审计周期核对计时（秒）。</summary>
    private float _visibilityAuditTimer;

    /// <summary>[RemoteVisibility] 可见性审计核对间隔（秒，spec 5.4.1 规则 1：0.5~1s）。</summary>
    private const float VisibilityAuditIntervalSeconds = 0.8f;

    /// <summary>[RemoteVisibility] 上次可见性核对时状态机是否已恢复完成（用于恢复完成时触发全量核对）。</summary>
    private bool _lastRecoveryCompleteState;

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

        /// <summary>
        /// 远程角色同步阈值配置（配置外化注入通道）。
        /// 为 null 时使用默认构造（平滑区 100m / 硬跳 500m / 混合时长 0.2s）。
        /// 由外部配置加载逻辑在启动时设置，注入前经 <see cref="Horizon.Game.ECS.Arch.Configuration.RemoteSyncThresholdValidator"/>
        /// 校验，非法值回退默认并输出诊断。
        /// </summary>
        public Horizon.Game.ECS.Arch.Configuration.RemoteSyncThresholdOptions? RemoteSyncThresholdOptions { get; set; }

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

            // P-F8 场景切换自愈：登录后切换 GameWorld 时新实例的 OnStart 生命周期可能缺失
            // （实测日志：新 ECSUpdateDriver 在跑但无 OnStart/FlaxActorSyncSystem 挂载日志，
            // 导致远程角色 Actor 永不创建、位置永不同步）。在 OnUpdate 中持续自检，
            // 缺失时自动补挂载，保证 ECS→Actor 视觉桥接在任何场景过渡后都能恢复。
            if (_actorSyncSystem == null || _actorSyncSystem.Parent == null)
            {
                _actorSyncSystem = Actor.GetScript<FlaxActorSyncSystem>();
                if (_actorSyncSystem == null)
                {
                    _actorSyncSystem = Actor.AddScript<FlaxActorSyncSystem>();
                    Debug.LogWarning("[ECSUpdateDriver] P-F8 自愈：FlaxActorSyncSystem 缺失，已在 OnUpdate 重新挂载");
                }
                else
                {
                    Debug.LogWarning("[ECSUpdateDriver] P-F8 自愈：重新获取到已存在的 FlaxActorSyncSystem");
                }
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

                            // [远程同步防闪跳] 阈值配置加载与注入（DFX 4.4.2 阈值可配置）：
                            // 读取配置（缺省用默认构造），经 RemoteSyncThresholdValidator 校验（非法回退默认+诊断），
                            // 将校验后阈值注入所有 InterpolationSystem 与 FlaxActorSyncSystem。
                            // 诊断 Sink 已先行注入，保证 OnConfigInvalid 可正常输出。
                            var thresholdOptions = LoadRemoteSyncThresholdOptions();
                            var validatedOptions = Horizon.Game.ECS.Arch.Configuration.RemoteSyncThresholdValidator.Validate(
                                thresholdOptions, sink);
                            foreach (var sys in archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.Render))
                            {
                                if (sys is InterpolationSystem interp)
                                {
                                    interp.TeleportThresholdMeters = validatedOptions.SmoothThresholdMeters;
                                    interp.HardSnapThresholdMeters = validatedOptions.HardSnapThresholdMeters;
                                    interp.TeleportBlendDurationSeconds = validatedOptions.BlendDurationSeconds;
                                }
                            }
                            if (_actorSyncSystem != null)
                            {
                                _actorSyncSystem.NearDistanceMeters = validatedOptions.NearDistanceMeters;
                                _actorSyncSystem.MidDistanceMeters = validatedOptions.MidDistanceMeters;
                                _actorSyncSystem.PerformanceDegradeEntityCount = validatedOptions.PerformanceDegradeEntityCount;
                                _actorSyncSystem.MaxRemoteEntityCount = validatedOptions.MaxRemoteEntityCount;
                                _actorSyncSystem.UltraScaleEntityCap = validatedOptions.UltraScaleEntityCap;
                                _actorSyncSystem.Diagnostics = sink;
                            }

                            // [超大规模] 装配 SyncScaleController：注入档位阈值与诊断 Sink，
                            // 并赋给 FlaxActorSyncSystem 与 InterpolationSystem（最远优先降级 + 降级集合同步）。
                            var scaleController = new SyncScaleController
                            {
                                TierThresholds = validatedOptions.TierThresholds,
                                Diagnostics = sink,
                            };
                            if (_actorSyncSystem != null)
                            {
                                _actorSyncSystem.ScaleController = scaleController;
                            }
                            foreach (var sys in archHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.Render))
                            {
                                if (sys is InterpolationSystem interp)
                                {
                                    interp.Diagnostics = sink;
                                    interp.SetDegradedEntities(Array.Empty<ulong>());
                                }
                            }

                            _diagnosticsInjected = true;
                        }

                        archHost.Tick(TimeSpan.FromSeconds(Time.DeltaTime));

                        // 消费 InputSendQueue：将 ECS 管线产生的 InputPacket 发送到服务端
                        FlushInputSendQueue();

                        // [RemoteVisibility] 周期可见性审计核对（spec 5.4.1 规则 1：0.5~1s）。
                        TickVisibilityAudit(Time.DeltaTime);
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
        /// 周期可见性审计核对（spec 5.4.1 规则 1/2）：约 0.8s 一次，
        /// 恢复完成状态下也持续核对，确保"应见必见"收敛。
        /// </summary>
        private void TickVisibilityAudit(float deltaSeconds)
        {
            var instance = HundunWorldGame.Instance;
            if (instance?.RemoteVisibilityAudit == null)
            {
                return;
            }

            _visibilityAuditTimer += deltaSeconds;
            if (_visibilityAuditTimer < VisibilityAuditIntervalSeconds)
            {
                return;
            }

            _visibilityAuditTimer = 0f;

            try
            {
                var audit = instance.RemoteVisibilityAudit;
                audit.RunReconciliation();
                ClientSyncMetrics.RecordVisibilityCheck();

                // 恢复完成状态检测：首次进入时已由状态机驱动全量核对，此处持续核对兜底。
                var stateMachine = instance.ReconnectResumeStateMachine;
                var isComplete = stateMachine != null && stateMachine.IsRecoveryComplete;
                if (isComplete && !_lastRecoveryCompleteState)
                {
                    audit.RunReconciliation(); // 恢复完成瞬间额外核对一次
                }
                _lastRecoveryCompleteState = isComplete;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ECSUpdateDriver] 可见性审计核对异常被隔离: {ex.Message}");
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
                    // 发送出口统一经受控发送服务 GuardSyncSender（spec 5.4.1 规则 1、4.4.4 规则集中性）。
                    var guardSender = HundunWorldGame.Instance?.GuardSyncSender;
                    if (guardSender != null)
                    {
                        _ = guardSender.SendLocalAsync(inputPacket, inputPacket.CharacterId);
                    }
                    else
                    {
                        // 装配失败回退：直接编码发送（向后兼容，资格管控降级为仅连接/握手检查）。
                        SyncPacketCodec.Encode(inputPacket, out var fallbackFrame, out var fallbackLength);
                        try
                        {
                            var fallbackPayload = new byte[fallbackLength];
                            System.Buffer.BlockCopy(fallbackFrame, 0, fallbackPayload, 0, fallbackLength);
                            var fallbackSyncFrame = new SyncFrameMessage
                            {
                                Frame = fallbackPayload,
                                PacketKind = (byte)inputPacket.Kind,
                                ProtocolVersion = inputPacket.ProtocolVersion,
                            };
                            _ = networkManager.SendAsync(fallbackSyncFrame);
                        }
                        finally
                        {
                            SyncPacketCodec.ReturnFrame(fallbackFrame);
                        }
                    }

                    // [Phase C2] 记录输入包发送
                    ClientSyncMetrics.RecordInputSent();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ECSUpdateDriver] 发送输入包失败: {ex.Message}");
                }
            }

            // 上送 baseline 重传请求（SnapshotApplySystem 产生，delta 解码时 baseline 不匹配）
            FlushPendingResyncRequests();

            // P1.4：补齐战斗动作队列消费发送链路（打包后丢弃 → 授权发送）。
            FlushCombatSendQueue();

            // [Phase C2] 转发 ECS 系统指标到 ClientSyncMetrics
            ForwardEcsMetrics();
        }

        /// <summary>
        /// 消费战斗动作队列（P1.4 补齐"打包后丢弃"链路），出队发送前统一授权：
        /// 本地角色 → SendLocalAsync，绑定实体 → SendBoundEntityAsync（spec 5.2.1 规则 1）。
        /// </summary>
        private void FlushCombatSendQueue()
        {
            var guardSender = HundunWorldGame.Instance?.GuardSyncSender;
            if (guardSender == null)
            {
                return;
            }

            var combatQueue = InputSendQueue.Instance;
            while (combatQueue.TryDequeueCombat(out var combatPacket))
            {
                try
                {
                    // 以 AttackerId 识别发起实体：本地角色或深度绑定实体（AttackerId 承载其自身身份）。
                    var attackerId = combatPacket.AttackerId;
                    bool sent;
                    if (IsBoundEntityId(attackerId))
                    {
                        sent = guardSender.SendBoundEntityAsync(combatPacket, attackerId).GetAwaiter().GetResult();
                    }
                    else
                    {
                        sent = guardSender.SendLocalAsync(combatPacket, attackerId).GetAwaiter().GetResult();
                    }

                    if (sent)
                    {
                        ClientSyncMetrics.RecordCombatSent();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ECSUpdateDriver] 发送战斗动作包失败: {ex.Message}");
                }
            }
        }

        /// <summary>判断实体是否为深度绑定实体（本地角色识别委托，由装配方注入）。</summary>
        private bool IsBoundEntityId(ulong entityId)
        {
            var bindingRegistry = HundunWorldGame.Instance?.BindingRegistry;
            return bindingRegistry != null && bindingRegistry.TryGetValidBinding(entityId, out _);
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
        /// 加载远程角色同步阈值配置。优先使用外部注入的 <see cref="RemoteSyncThresholdOptions"/>，
        /// 缺失时使用默认构造（100/500/0.2/30/80/10/20）。
        /// </summary>
        private Horizon.Game.ECS.Arch.Configuration.RemoteSyncThresholdOptions LoadRemoteSyncThresholdOptions()
        {
            return RemoteSyncThresholdOptions ?? new Horizon.Game.ECS.Arch.Configuration.RemoteSyncThresholdOptions();
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

                        // 转发非法快照跳过计数增量（异常数据隔离可观测）
                        long currentInvalidSkipped = snapshotSys.InvalidSnapshotsSkipped;
                        var deltaInvalid = currentInvalidSkipped - _lastInvalidSnapshotsSkipped;
                        for (long i = 0; i < deltaInvalid; i++)
                            ClientSyncMetrics.RecordInvalidSnapshotSkipped();
                        _lastInvalidSnapshotsSkipped = currentInvalidSkipped;
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

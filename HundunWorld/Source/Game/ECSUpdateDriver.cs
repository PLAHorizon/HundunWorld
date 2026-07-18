using FlaxEngine;
using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Network;

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
        }
    }
}

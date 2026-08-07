using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    public class SyncPacketMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.SyncPacket };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<SnapshotPacket> SnapshotReceived;
        public event Action<InputAckPacket> InputAckReceived;
        public event Action<EventPacket> EventReceived;
        public event Action<WorldChunkDiffPacket> ChunkDiffReceived;

        /// <summary>P1.3：服务器拒绝握手（协议版本过低），客户端应触发强制更新流程。</summary>
        public event Action<HandshakeRejectPacket> HandshakeRejected;

        /// <summary>P1.4：收到伤害通知（服务端裁决结果）。</summary>
        public event Action<DamagePacket> DamageReceived;

        /// <summary>P1.4：收到死亡通知。</summary>
        public event Action<DeathPacket> DeathReceived;

        // 修复（静置断线后输入断流 — "无法移动 + 闪跳回原地"根因之一）：
        // 服务端 HandleReconnectAsync 对可恢复决策（ResumeIncremental/ResendFullChunks）返回 HandshakePacket
        // 作为握手确认。此前返回 WorldPatchManifestPacket，客户端在此 default 分支直接忽略，
        // IsSyncHandshakeComplete 永不为 true → ECSUpdateDriver 每帧丢弃 InputPacket → 移动请求不上行。
        public event Action<WorldPatchManifestPacket>? PatchManifestReceived;

        /// <summary>
        /// 重连恢复的握手确认事件（服务端对 ResumeIncremental/ResendFullChunks 的 HandshakePacket 确认）。
        /// 带缓存补分发语义（与 <see cref="HandshakeReceived"/> 一致）：缓存最近一次重连握手确认，
        /// 订阅时若已提前到达则立即补发，消除"重连确认在订阅之前到达"的竞态。
        /// </summary>
        private event Action<HandshakePacket>? _reconnectHandshakeReceived;
        private HandshakePacket? _lastReconnectHandshake;

        public event Action<HandshakePacket>? ReconnectHandshakeReceived
        {
            add
            {
                _reconnectHandshakeReceived += value;
                // 若重连握手确认已提前到达，立即分发给新订阅者（与 HandshakeReceived 的缓存分发模式一致）。
                if (_lastReconnectHandshake != null)
                {
                    value?.Invoke(_lastReconnectHandshake);
                }
            }
            remove => _reconnectHandshakeReceived -= value;
        }

        // 修复：将 HandshakeReceived 改为自定义事件，缓存最后一次握手响应。
        // 解决握手响应在 HundunWorldGame 订阅事件之前到达而导致“同步握手未完成”的竞态。
        private event Action<HandshakePacket> _handshakeReceivedInternal;
        private HandshakePacket _lastHandshake;

        public event Action<HandshakePacket> HandshakeReceived
        {
            add
            {
                _handshakeReceivedInternal += value;
                // 若握手响应已提前到达，立即分发给新订阅者，避免永远等待。
                if (_lastHandshake != null)
                {
                    value?.Invoke(_lastHandshake);
                }
            }
            remove => _handshakeReceivedInternal -= value;
        }

        public SyncPacketMessageHandler() : base(MessageType.SyncPacket)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message.Body is not SyncFrameMessage syncFrame)
            {
                FlaxEngine.Debug.LogError($"Invalid SyncPacket message body type: {message.Body?.GetType().Name ?? "null"}");
                await Task.CompletedTask;
                return;
            }

            SyncPacket packet;
            try
            {
                packet = SyncPacketCodec.Decode(syncFrame.Frame);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"Failed to decode SyncPacket frame: {ex.Message}");
                await Task.CompletedTask;
                return;
            }

            switch (packet)
            {
                case SnapshotPacket snapshot:
                    // [Phase C2] 记录快照接收时间戳和间隔统计
                    ClientSyncMetrics.RecordSnapshotReceived();
                    SnapshotReceived?.Invoke(snapshot);
                    break;
                case InputAckPacket inputAck:
                    // [Phase C2] 记录 InputAck 接收
                    ClientSyncMetrics.RecordInputAck();
                    InputAckReceived?.Invoke(inputAck);
                    break;
                case EventPacket eventPacket:
                    EventReceived?.Invoke(eventPacket);
                    break;
                case WorldChunkDiffPacket chunkDiff:
                    ChunkDiffReceived?.Invoke(chunkDiff);
                    break;
                case HandshakePacket handshake:
                    // 重连握手确认 vs 全量握手响应的区分：
                    // 服务端 HandleReconnectAsync 对可恢复决策（ResumeIncremental/ResendFullChunks）返回
                    // HandshakePacket 作为重连握手确认，特征为 InitialClientTick=0
                    // （BuildResumeHandshakeConfirmation 固定回显 0）；全量握手响应回显客户端发送的
                    // InitialClientTick（Unix 毫秒时间戳，恒非 0）。二者必须分开处理：
                    //   - 全量握手：缓存为 _lastHandshake + 触发 HandshakeReceived（驱动身份建立）。
                    //   - 重连确认：仅恢复输入门控（MarkSyncHandshakeComplete），不覆盖 _lastHandshake、
                    //     不触发 HandshakeReceived（避免身份建立流程被重复驱动，导致本地玩家实体被重置）。
                    if (handshake.InitialClientTick == 0 && handshake.LocalCharacterId != 0)
                    {
                        var lastReconnect = _lastReconnectHandshake;
                        // 去重：同一重连握手确认（LocalCharacterId 相同）只触发一次订阅者分发。
                        if (lastReconnect == null || lastReconnect.LocalCharacterId != handshake.LocalCharacterId)
                        {
                            _lastReconnectHandshake = handshake;
                            _reconnectHandshakeReceived?.Invoke(handshake);
                        }
                    }
                    else
                    {
                        // 缓存最后一次握手响应，使后续订阅者能立即收到。
                        _lastHandshake = handshake;
                        _handshakeReceivedInternal?.Invoke(handshake);
                    }
                    break;
                case WorldPatchManifestPacket patchManifest:
                    // 修复（静置断线后输入断流 — "无法移动 + 闪跳回原地"根因之一）：
                    // 重连恢复决策（ResumeIncremental）返回的 WorldPatchManifestPacket 此前落入 default 分支
                    // 被忽略。它同时是"服务端确认会话已恢复"的信号：恢复 IsSyncHandshakeComplete，
                    // 否则 ECSUpdateDriver.FlushInputSendQueue 每帧丢弃 InputPacket，移动请求永久不上行。
                    // 注意：仅恢复输入门控，不缓存为 _lastHandshake（非全量握手，不得驱动身份建立流程）。
                    PatchManifestReceived?.Invoke(patchManifest);
                    break;
                case HandshakeRejectPacket reject:
                    // P1.3：协议版本过低，服务器强制拒绝——触发客户端更新流程。
                    FlaxEngine.Debug.LogError($"[Sync] 握手被拒绝：{reject.Reason} (服务器版本={reject.ServerVersion}, 最低支持={reject.MinimumVersion})");
                    HandshakeRejected?.Invoke(reject);
                    break;
                case DamagePacket damage:
                    // P1.4：伤害通知，转发给 CombatEffectSystem。
                    DamageReceived?.Invoke(damage);
                    break;
                case DeathPacket death:
                    // P1.4：死亡通知，转发给 CombatEffectSystem。
                    DeathReceived?.Invoke(death);
                    break;
                default:
                    // [Phase C2] 记录未知 Kind 包计数
                    ClientSyncMetrics.RecordUnknownPacket();
                    FlaxEngine.Debug.LogWarning($"Unknown sync packet kind: {packet.Kind}");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}

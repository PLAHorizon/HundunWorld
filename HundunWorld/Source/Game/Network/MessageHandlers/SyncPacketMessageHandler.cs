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
                    // 缓存最后一次握手响应，使后续订阅者能立即收到。
                    _lastHandshake = handshake;
                    _handshakeReceivedInternal?.Invoke(handshake);
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

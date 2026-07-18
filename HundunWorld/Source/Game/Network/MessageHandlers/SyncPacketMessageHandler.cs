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
                    SnapshotReceived?.Invoke(snapshot);
                    break;
                case InputAckPacket inputAck:
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
                default:
                    FlaxEngine.Debug.LogWarning($"Unknown sync packet kind: {packet.Kind}");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}

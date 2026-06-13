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
        public event Action<HandshakePacket> HandshakeReceived;

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
                    HandshakeReceived?.Invoke(handshake);
                    break;
                default:
                    FlaxEngine.Debug.LogWarning($"Unknown sync packet kind: {packet.Kind}");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}

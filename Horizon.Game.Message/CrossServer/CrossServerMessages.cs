using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.CrossServer
{
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CrossServerTransferRequest : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.CrossServerTransferRequest;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.CrossServer;

        [MemoryPackOrder(0)]
        [Id(0)]
        public long CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string TargetSceneId { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class CrossServerTransferResponse : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.CrossServerTransferResponse;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.CrossServer;

        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public string NodeAddress { get; set; }
    }
}


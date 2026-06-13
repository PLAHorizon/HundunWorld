using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Arena
{
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaJoinRequest : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaJoinRequest;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public long CharacterId { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaJoinResponse : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaJoinResponse;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaLeaveRequest : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaLeaveRequest;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public long CharacterId { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaLeaveResponse : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaLeaveResponse;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaInfoRequest : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaInfoRequest;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public long CharacterId { get; set; }
    }

    [MemoryPackable]
    [GenerateSerializer]
    public partial class ArenaInfoResponse : MessageUnion, INetworkMessage
    {
        [MemoryPackIgnore]
        public MessageType Type => MessageType.ArenaInfoResponse;
        [MemoryPackIgnore]
        public ServiceType ServiceType => ServiceType.Arena;

        [MemoryPackOrder(0)]
        [Id(0)]
        public int MMR { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string RankName { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public int Wins { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int Losses { get; set; }
    }
}


using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using TouchSocket.Core;

namespace Horizon.Game.Message
{
    /// <summary>
    /// 网络消息接口
    /// </summary>
    public interface INetworkMessage
    {

        ServiceType ServiceType { get; }
        /// <summary>
        /// 消息类型
        /// </summary>
        MessageType Type { get; }

    }

    public interface IGameHeader
    {
        int GameId { get; }
        int ZoneId { get; }
        int ServerId { get; }
    }   
}


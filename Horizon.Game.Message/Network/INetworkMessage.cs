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

    /// <summary>
    /// 角色 ID 载体接口（Phase 5 协议层优化）。<br/>
    /// 实现此接口的消息类型可在 <c>HorizonMessageAdapter.ExtractCharacterId</c> 中
    /// 避免反射查找 CharacterId 属性，提升高频路径性能。<br/>
    /// 回退到反射（兼容未实现此接口的旧消息类型）。
    /// </summary>
    public interface ICharacterIdCarrier
    {
        /// <summary>
        /// 消息携带的角色 ID（0 表示未携带）。
        /// </summary>
        ulong CarrierCharacterId { get; }
    }
}


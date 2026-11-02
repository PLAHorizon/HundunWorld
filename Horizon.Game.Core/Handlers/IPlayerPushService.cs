using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 出站推送抽象。<see cref="ChatHandler"/> / <see cref="CharacterHandler"/> 运行在 Gateway 进程内，
    /// 把服务器主动发起的消息（聊天通知、外观变更通知等）经本接口写到目标角色的连接；
    /// 实现位于 Gateway（基于连接管理器 + 消息适配器），Core 层仅依赖接口，保持与具体传输解耦。
    /// </summary>
    public interface IPlayerPushService
    {
        /// <summary>当前在线角色ID集合（世界频道广播、外观变更广播用）。</summary>
        Task<IReadOnlyCollection<ulong>> GetOnlineCharacterIdsAsync();

        /// <summary>把一条消息推送给指定角色集合（离线/无连接者自动忽略）。</summary>
        Task PushAsync<T>(T message, IReadOnlyCollection<ulong> targetCharacterIds)
            where T : MessageUnion, INetworkMessage;
    }
}

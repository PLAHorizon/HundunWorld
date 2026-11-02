using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// <see cref="IPlayerPushService"/> 的 Gateway 实现：
    /// 通过连接管理器把服务器主动发起的消息写到目标角色的 TCP 连接（离线/无连接者自动跳过）。
    /// 复用共享的 <see cref="HorizonMessageAdapter"/> 打包线路帧（与 GengDi 复用路径一致）。
    /// <para>
    /// 注意：在线角色集合是本 Gateway 进程内的连接。多网关分片部署时，只有与发送者同网关的玩家能收到推送；
    /// 与聊天频道的现状一致，跨网关扇出需要另接 Orleans 流。
    /// </para>
    /// </summary>
    public sealed class PlayerPushService : IPlayerPushService
    {
        private readonly IConnectionManager _connections;
        private readonly HorizonMessageAdapter _adapter;
        private readonly ILogger<PlayerPushService> _logger;

        public PlayerPushService(
            IConnectionManager connections,
            HorizonMessageAdapter adapter,
            ILogger<PlayerPushService> logger)
        {
            _connections = connections;
            _adapter = adapter;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<IReadOnlyCollection<ulong>> GetOnlineCharacterIdsAsync()
        {
            var ids = _connections.GetAllCharacterIds().Select(id => (ulong)id).ToList();
            return Task.FromResult<IReadOnlyCollection<ulong>>(ids);
        }

        /// <inheritdoc />
        public async Task PushAsync<T>(T message, IReadOnlyCollection<ulong> targetCharacterIds)
            where T : MessageUnion, INetworkMessage
        {
            if (targetCharacterIds == null || targetCharacterIds.Count == 0)
                return;

            byte[] wire;
            try
            {
                wire = _adapter.PackMessage(message, message.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PlayerPushService] 打包推送消息失败 type={MessageType}", message.Type);
                return;
            }

            foreach (var id in targetCharacterIds)
            {
                var conn = _connections.GetConnectionByCharacterId((long)id);
                if (conn == null)
                    continue;

                try
                {
                    await conn.SendAsync(wire);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PlayerPushService] 推送消息给角色 {CharacterId} 失败 type={MessageType}", id, message.Type);
                }
            }
        }
    }
}

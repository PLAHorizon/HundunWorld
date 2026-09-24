using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// <see cref="IChatPushService"/> 的 Gateway 实现：
    /// 通过连接管理器把聊天通知写到目标角色的 TCP 连接（离线/无连接者自动跳过）。
    /// 复用共享的 <see cref="HorizonMessageAdapter"/> 打包线路帧（与 GengDi 复用路径一致）。
    /// </summary>
    public sealed class ChatPushService : IChatPushService
    {
        private readonly IConnectionManager _connections;
        private readonly HorizonMessageAdapter _adapter;
        private readonly ILogger<ChatPushService> _logger;

        public ChatPushService(
            IConnectionManager connections,
            HorizonMessageAdapter adapter,
            ILogger<ChatPushService> logger)
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
                _logger.LogError(ex, "[ChatPushService] 打包聊天消息失败");
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
                    _logger.LogWarning(ex, "[ChatPushService] 推送聊天给角色 {CharacterId} 失败", id);
                }
            }
        }
    }
}

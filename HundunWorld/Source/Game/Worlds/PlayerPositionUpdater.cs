using Arch.Core;
using FlaxEngine;
using HundunWorld.Game.Network;
using System;
using System.Threading.Tasks;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 玩家位置更新器，负责处理玩家位置的更新和同步
    /// </summary>
    public class PlayerPositionUpdater
    {
        private readonly NetworkManager _networkManager;
        private readonly WorldManager _worldManager;
        private readonly World _world;
        private ulong _playerId;
        private Vector3 _lastPosition;
        private DateTime _lastUpdate;
        private float _updateThreshold = 0.1f; // 位置更新阈值
        private TimeSpan _minUpdateInterval = TimeSpan.FromMilliseconds(50); // 最小更新间隔

        public PlayerPositionUpdater(NetworkManager networkManager, WorldManager worldManager, World world)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _worldManager = worldManager ?? throw new ArgumentNullException(nameof(worldManager));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// 设置玩家ID
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        public void SetPlayerId(ulong playerId)
        {
            _playerId = playerId;
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        /// <param name="newPosition">新位置</param>
        public async Task UpdatePlayerPositionAsync(Vector3 newPosition)
        {
            // 检查是否需要更新（基于距离阈值和时间间隔）
            if (ShouldUpdatePosition(newPosition))
            {
                // 更新本地世界状态
                await UpdateLocalPlayerPositionAsync(newPosition);
                
                // 发送位置更新到服务器
                await SendPositionUpdateToServerAsync(newPosition);
                
                // 更新最后位置和时间
                _lastPosition = newPosition;
                _lastUpdate = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 检查是否需要更新位置
        /// </summary>
        /// <param name="newPosition">新位置</param>
        /// <returns>是否需要更新</returns>
        private bool ShouldUpdatePosition(Vector3 newPosition)
        {
            // 检查距离阈值
            float distance = Vector3.Distance(_lastPosition, newPosition);
            if (distance < _updateThreshold)
                return false;

            // 检查时间间隔
            TimeSpan timeSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
            if (timeSinceLastUpdate < _minUpdateInterval)
                return false;

            return true;
        }

        /// <summary>
        /// 更新本地玩家位置
        /// </summary>
        /// <param name="newPosition">新位置</param>
        private async Task UpdateLocalPlayerPositionAsync(Vector3 newPosition)
        {
            // 更新世界管理器中的玩家实体状态
            await _worldManager.UpdateEntityPositionAsync(_playerId, newPosition);
        }

        /// <summary>
        /// 发送位置更新到服务器
        /// </summary>
        /// <param name="newPosition">新位置</param>
        private async Task SendPositionUpdateToServerAsync(Vector3 newPosition)
        {
            // 构造位置更新消息并发送到服务器
            // 由于缺少具体的消息定义，这里只是一个示例
            /*
            var message = new HorizonMessagePacket
            {
                ServiceType = ServiceType.World,
                Header = new MessageHeader
                {
                    MessageType = MessageType.PlayerPositionUpdate,
                    UserId = _playerId
                },
                Body = new MessageUnion { PlayerPositionUpdate = new PlayerPositionUpdateMessage { Position = newPosition } }
            };
            
            await _networkManager.SendMessageAsync(message);
            */
        }

        /// <summary>
        /// 处理来自服务器的玩家位置更新
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        /// <param name="position">位置</param>
        public async Task HandleRemotePlayerPositionUpdateAsync(ulong playerId, Vector3 position)
        {
            // 更新世界管理器中的其他玩家实体状态
            await _worldManager.UpdateEntityPositionAsync(playerId, position);
        }

        internal void Dispose()
        {
            // PlayerPositionUpdater does not own _networkManager, _worldManager, or _world,
            // so it must not dispose them. Cleanup is handled by HundunWorldGame.
        }
    }
}
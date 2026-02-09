using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 副本消息处理器
    /// 处理副本状态通知
    /// </summary>
    public class DungeonMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.DungeonStatusNotify
        };

        public override ServiceType ServiceType => ServiceType.Game;

        /// <summary>
        /// 副本状态更新事件
        /// </summary>
        public event Action<DungeonStatusNotifyMessage> DungeonStatusUpdated;

        public DungeonMessageHandler() : base(MessageType.DungeonStatusNotify)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[DungeonMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case DungeonStatusNotifyMessage dungeonStatus:
                    HandleDungeonStatus(dungeonStatus);
                    break;

                default:
                    Debug.LogWarning($"[DungeonMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleDungeonStatus(DungeonStatusNotifyMessage dungeonStatus)
        {
            Debug.Log($"[DungeonMessageHandler] 副本状态更新: 副本={dungeonStatus.DungeonName}, 状态={dungeonStatus.Status}, 玩家数={dungeonStatus.CurrentPlayers}/{dungeonStatus.MaxPlayers}, 剩余时间={dungeonStatus.RemainingSeconds}秒");
            DungeonStatusUpdated?.Invoke(dungeonStatus);
        }
    }
}

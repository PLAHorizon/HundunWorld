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
    /// 成就消息处理器
    /// 处理成就解锁通知和成就列表响应
    /// </summary>
    public class AchievementMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.AchievementUnlockNotify,
            MessageType.AchievementListResponse
        };

        public override ServiceType ServiceType => ServiceType.Game;

        /// <summary>
        /// 成就解锁事件
        /// </summary>
        public event Action<AchievementUnlockNotifyMessage> AchievementUnlocked;

        /// <summary>
        /// 成就列表更新事件
        /// </summary>
        public event Action<AchievementListResponseMessage> AchievementListReceived;

        public AchievementMessageHandler() : base(MessageType.AchievementUnlockNotify)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[AchievementMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case AchievementUnlockNotifyMessage unlockNotify:
                    HandleAchievementUnlock(unlockNotify);
                    break;

                case AchievementListResponseMessage achievementList:
                    HandleAchievementList(achievementList);
                    break;

                default:
                    Debug.LogWarning($"[AchievementMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleAchievementUnlock(AchievementUnlockNotifyMessage unlockNotify)
        {
            Debug.Log($"[AchievementMessageHandler] 成就解锁: {unlockNotify.AchievementName} (+{unlockNotify.Points}点)");
            AchievementUnlocked?.Invoke(unlockNotify);
        }

        private void HandleAchievementList(AchievementListResponseMessage achievementList)
        {
            Debug.Log($"[AchievementMessageHandler] 收到成就列表: 总数={achievementList.Achievements.Count}, 已解锁={achievementList.UnlockedCount}, 总点数={achievementList.TotalPoints}");
            AchievementListReceived?.Invoke(achievementList);
        }
    }
}

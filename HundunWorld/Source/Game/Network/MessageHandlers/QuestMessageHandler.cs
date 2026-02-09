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
    /// 任务消息处理器
    /// 处理任务列表响应和任务进度更新通知
    /// </summary>
    public class QuestMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.QuestListResponse,
            MessageType.QuestProgressNotify
        };

        public override ServiceType ServiceType => ServiceType.Quest;

        /// <summary>
        /// 收到任务列表事件
        /// </summary>
        public event Action<QuestListResponseMessage> QuestListReceived;

        /// <summary>
        /// 任务进度更新事件
        /// </summary>
        public event Action<QuestProgressNotifyMessage> QuestProgressUpdated;

        public QuestMessageHandler() : base(MessageType.QuestListResponse)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[QuestMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case QuestListResponseMessage questList:
                    HandleQuestList(questList);
                    break;

                case QuestProgressNotifyMessage questProgress:
                    HandleQuestProgress(questProgress);
                    break;

                default:
                    Debug.LogWarning($"[QuestMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleQuestList(QuestListResponseMessage questList)
        {
            Debug.Log($"[QuestMessageHandler] 收到任务列表: 任务数={questList.Quests.Count}");
            QuestListReceived?.Invoke(questList);
        }

        private void HandleQuestProgress(QuestProgressNotifyMessage questProgress)
        {
            Debug.Log($"[QuestMessageHandler] 任务进度更新: 任务={questProgress.QuestName}, 进度={questProgress.CurrentProgress}/{questProgress.TargetProgress}, 完成={questProgress.IsCompleted}");
            QuestProgressUpdated?.Invoke(questProgress);
        }
    }
}

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
            MessageType.QuestProgressNotify,
            // 接取与提交的响应带着请求同样的 MessageType 回来（网关用 CreateHorizonMessage 原样回包）。
            // 这两条以前没人声明，所以"接了任务"在服务端已经落库、客户端却一句话都收不到，
            // 界面只能靠本地猜测刷新。
            MessageType.AcceptQuest,
            MessageType.CompleteQuest,
            // 交互裁决按 1658 原路回（QuestInteractResultMessage）：服务端会明确给出成不成立、
            // 以及不成立的短码——客户端要靠它分派表现（太近/太远/对象不存在）。
            MessageType.QuestInteractRequest,
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

        /// <summary>接取任务响应事件（成功与否都在这里面）。</summary>
        public event Action<AcceptQuestResponse> AcceptResponded;

        /// <summary>提交任务响应事件（含服务端实际发放的奖励表）。</summary>
        public event Action<CompleteQuestResponse> CompleteResponded;

        /// <summary>与对象交互的服务端裁决回执（Ok=false 时 Reason 给短码）。</summary>
        public event Action<QuestInteractResultMessage> InteractResponded;

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

                case AcceptQuestResponse accept:
                    Debug.Log($"[QuestMessageHandler] 接取结果: Success={accept.Success} QuestId={accept.AcceptedQuest?.QuestId ?? 0} {accept.Message}");
                    AcceptResponded?.Invoke(accept);
                    break;

                case CompleteQuestResponse complete:
                    Debug.Log($"[QuestMessageHandler] 提交结果: Success={complete.Success} QuestId={complete.CompletedQuestId} 产出={complete.Rewards?.Count ?? 0} 项");
                    CompleteResponded?.Invoke(complete);
                    break;

                case QuestInteractResultMessage interact:
                    Debug.Log($"[QuestMessageHandler] 交互裁决: Ok={interact.Ok} Reason={interact.Reason} Entity={interact.EntityId} 推进={interact.AdvancedCount} 条目标");
                    InteractResponded?.Invoke(interact);
                    break;

                default:
                    Debug.LogWarning($"[QuestMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleQuestList(QuestListResponseMessage questList)
        {
            // 这行是"可接列表到底有没有到客户端"的唯一现场线索：只打印身上的任务数时，
            // "服务端没给 Offers" 与 "UI 没画" 两种故障在日志里长得一模一样，所以两个数都要打。
            Debug.Log($"[QuestMessageHandler] 收到任务列表: 任务数={questList.Quests.Count} 可接数={questList.Offers?.Count ?? 0} 目录版本={questList.CatalogVersion}");
            QuestListReceived?.Invoke(questList);
        }

        private void HandleQuestProgress(QuestProgressNotifyMessage questProgress)
        {
            Debug.Log($"[QuestMessageHandler] 任务进度更新: 任务={questProgress.QuestName}, 进度={questProgress.CurrentProgress}/{questProgress.TargetProgress}, 完成={questProgress.IsCompleted}");
            QuestProgressUpdated?.Invoke(questProgress);
        }
    }
}

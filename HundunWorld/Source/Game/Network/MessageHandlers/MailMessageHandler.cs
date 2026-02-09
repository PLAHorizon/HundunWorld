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
    /// 邮件消息处理器
    /// 处理邮件列表响应、邮件操作结果和新邮件通知
    /// </summary>
    public class MailMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.MailListResponse,
            MessageType.MailOperation,
            MessageType.MailNotify
        };

        public override ServiceType ServiceType => ServiceType.System;

        /// <summary>
        /// 收到邮件列表事件
        /// </summary>
        public event Action<MailListResponseMessage> MailListReceived;

        /// <summary>
        /// 邮件操作完成事件
        /// </summary>
        public event Action<MailOperationMessage> MailOperationCompleted;

        /// <summary>
        /// 新邮件通知事件
        /// </summary>
        public event Action<MailNotifyMessage> NewMailReceived;

        public MailMessageHandler() : base(MessageType.MailListResponse)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[MailMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case MailListResponseMessage mailList:
                    HandleMailList(mailList);
                    break;

                case MailOperationMessage mailOp:
                    HandleMailOperation(mailOp);
                    break;

                case MailNotifyMessage mailNotify:
                    HandleMailNotify(mailNotify);
                    break;

                default:
                    Debug.LogWarning($"[MailMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleMailList(MailListResponseMessage mailList)
        {
            Debug.Log($"[MailMessageHandler] 收到邮件列表: 邮件数={mailList.Mails.Count}, 未读数={mailList.UnreadCount}");
            MailListReceived?.Invoke(mailList);
        }

        private void HandleMailOperation(MailOperationMessage mailOp)
        {
            Debug.Log($"[MailMessageHandler] 邮件操作完成: 邮件ID={mailOp.MailId}, 操作={mailOp.OperationType}, 成功={mailOp.Success}");
            MailOperationCompleted?.Invoke(mailOp);
        }

        private void HandleMailNotify(MailNotifyMessage mailNotify)
        {
            Debug.Log($"[MailMessageHandler] 新邮件通知: 标题={mailNotify.Title}, 发件人={mailNotify.SenderName}, 未读数={mailNotify.UnreadCount}");
            NewMailReceived?.Invoke(mailNotify);
        }
    }
}

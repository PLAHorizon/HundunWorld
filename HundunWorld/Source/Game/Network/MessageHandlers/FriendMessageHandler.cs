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
    /// 好友消息处理器
    /// 处理好友列表、好友操作、好友状态更新和好友请求通知
    /// </summary>
    public class FriendMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.FriendList,
            MessageType.FriendOperation,
            MessageType.FriendStatusUpdate,
            MessageType.FriendRequestNotify
        };

        public override ServiceType ServiceType => ServiceType.Social;

        /// <summary>
        /// 好友列表更新事件
        /// </summary>
        public event Action<FriendListUpdateMessage> FriendListUpdated;

        /// <summary>
        /// 好友添加响应事件
        /// </summary>
        public event Action<AddFriendResponse> FriendAdded;

        /// <summary>
        /// 好友状态变更事件
        /// </summary>
        public event Action<FriendStatusUpdateMessage> FriendStatusChanged;

        /// <summary>
        /// 好友请求通知事件
        /// </summary>
        public event Action<FriendRequestNotifyMessage> FriendRequestReceived;

        public FriendMessageHandler() : base(MessageType.FriendList)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[FriendMessageHandler] 收到空消息体");
                return;
            }

            switch (message.Body)
            {
                case FriendListUpdateMessage friendList:
                    HandleFriendList(friendList);
                    break;

                case AddFriendResponse addFriendResponse:
                    HandleAddFriendResponse(addFriendResponse);
                    break;

                case FriendStatusUpdateMessage statusUpdate:
                    HandleFriendStatusUpdate(statusUpdate);
                    break;

                case FriendRequestNotifyMessage requestNotify:
                    HandleFriendRequestNotify(requestNotify);
                    break;

                default:
                    Debug.LogWarning($"[FriendMessageHandler] 未知的消息体类型: {message.Body.GetType().Name}");
                    break;
            }

            await Task.CompletedTask;
        }

        private void HandleFriendList(FriendListUpdateMessage friendList)
        {
            Debug.Log($"[FriendMessageHandler] 收到好友列表更新: 好友数量={friendList.Friends.Count}");
            FriendListUpdated?.Invoke(friendList);
        }

        private void HandleAddFriendResponse(AddFriendResponse response)
        {
            Debug.Log($"[FriendMessageHandler] 收到添加好友响应: 成功={response.Success}, 消息={response.Message}");
            FriendAdded?.Invoke(response);
        }

        private void HandleFriendStatusUpdate(FriendStatusUpdateMessage statusUpdate)
        {
            Debug.Log($"[FriendMessageHandler] 好友状态变更: {statusUpdate.FriendName} {(statusUpdate.IsOnline ? "上线" : "下线")}");
            FriendStatusChanged?.Invoke(statusUpdate);
        }

        private void HandleFriendRequestNotify(FriendRequestNotifyMessage requestNotify)
        {
            Debug.Log($"[FriendMessageHandler] 收到好友请求: 来自={requestNotify.RequesterName}(Lv.{requestNotify.RequesterLevel})");
            FriendRequestReceived?.Invoke(requestNotify);
        }
    }
}

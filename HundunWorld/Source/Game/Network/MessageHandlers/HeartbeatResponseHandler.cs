using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 心跳响应处理器
    /// </summary>
    public class HeartbeatResponseHandler : BaseMessageHandler
    {
        public HeartbeatResponseHandler() { }
        public HeartbeatResponseHandler(MessageType messageType) : base(messageType)
        {
        }

        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.Heartbeat, MessageType.HeartbeatResponse };



        public override ServiceType ServiceType => ServiceType.System;

        public new bool CanHandle(MessageType messageType)
        {
            return messageType == MessageType.HeartbeatResponse;
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body is HeartbeatResponse response)
            {
                var latency = response.Latency;
                FlaxEngine.Debug.Log($"心跳响应 - 延迟: {latency}ms");

                // 可以在这里更新UI显示网络状态
                // 例如：更新网络延迟显示
                 //NetworkUIManager.Instance?.UpdateLatency(latency);
            }

            await Task.CompletedTask;
        }


    }
}
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                // [Phase C2/C3] 基于 HeartbeatManager 记录的发送时间戳计算 RTT
                var sentTimestamp = HeartbeatManager.LastHeartbeatSentTimestamp;
                if (sentTimestamp > 0)
                {
                    var rttMs = (float)((Stopwatch.GetTimestamp() - sentTimestamp) * 1000.0 / Stopwatch.Frequency);
                    ClientSyncMetrics.RecordRtt(rttMs);
                    // [A2] 将 RTT 样本输入插值系统的自适应延迟：
                    // RTT 抬升（弱网）时自动加大插值窗口防快照缓冲抽干，
                    // 消除"周期性卡顿/瞬移"；RTT 低时窗口维持紧凑，降低延迟感。
                    Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem.RecordRttSample(rttMs);
#if DEBUG
                    FlaxEngine.Debug.Log($"心跳响应 - RTT: {rttMs:F1}ms (服务端报告延迟: {response.Latency}ms)");
#endif
                }
                else
                {
#if DEBUG
                    FlaxEngine.Debug.Log($"心跳响应 - 服务端延迟: {response.Latency}ms (无发送时间戳，无法计算 RTT)");
#endif
                }
            }

            await Task.CompletedTask;
        }


    }
}
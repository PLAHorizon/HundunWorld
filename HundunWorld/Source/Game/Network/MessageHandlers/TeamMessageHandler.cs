using FlaxEngine;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 组队消息处理器：组队 Create/Join/Leave/Kick/Transfer/Disband 统一走
    /// CreateTeamRequest/Response + TeamOperation（tag 空间已用尽）。
    /// </summary>
    public class TeamMessageHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType>
        {
            MessageType.Team,
        };

        public override ServiceType ServiceType => ServiceType.Social;

        /// <summary>所有组队操作回执（按 Operation 区分）。</summary>
        public event Action<CreateTeamResponse> TeamOperationResolved;
        public event Action<TeamInfo> TeamUpdated;

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (message?.Body == null)
            {
                Debug.LogWarning("[TeamMessageHandler] 收到空消息体");
                return Task.CompletedTask;
            }

            switch (message.Body)
            {
                case CreateTeamResponse response:
                    Debug.Log($"[TeamMessageHandler] 组队回执: Op={response.Operation}, Success={response.Success}, {response.Message}");
                    TeamOperationResolved?.Invoke(response);
                    if (response.Success && response.TeamInfo != null && response.TeamInfo.TeamId != 0UL)
                        TeamUpdated?.Invoke(response.TeamInfo);
                    break;

                case JoinTeamResponse joined:
                    Debug.Log($"[TeamMessageHandler] 加入队伍回执: Success={joined.Success}, {joined.Message}");
                    TeamOperationResolved?.Invoke(new CreateTeamResponse
                    {
                        Operation = TeamOperation.Join,
                        Success = joined.Success,
                        Message = joined.Message,
                        TeamInfo = joined.TeamInfo,
                    });
                    if (joined.Success) TeamUpdated?.Invoke(joined.TeamInfo);
                    break;

                case TeamInfo info:
                    TeamUpdated?.Invoke(info);
                    break;

                default:
                    Debug.LogWarning($"[TeamMessageHandler] 未知消息体: {message.Body.GetType().Name}");
                    break;
            }

            return Task.CompletedTask;
        }
    }
}

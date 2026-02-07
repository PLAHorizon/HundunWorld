using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    public class SocialHandler : MessageHandlerBase
    {
        public SocialHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {

        }


        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.Friend,
            MessageType.Team,
            MessageType.Guild,
            MessageType.SectInfo,
            MessageType.JoinSect,
            MessageType.SectSkill,
            MessageType.SectQuest,
            MessageType.Reputation,
            MessageType.ChivalryPoint,
            MessageType.Duel,
            MessageType.SwornBrother,
            MessageType.MasterApprentice
        };

        public override ServiceType ServiceType => ServiceType.Social;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.Friend:
                    return await HandleFriendAsync(message);
                case MessageType.Team:
                    return await HandleTeamAsync(message);
                case MessageType.Guild:
                    return await HandleGuildAsync(message);
                case MessageType.SectInfo:
                    return await HandleSectInfoAsync(message);
                case MessageType.JoinSect:
                    return await HandleJoinSectAsync(message);
                case MessageType.SectSkill:
                    return await HandleSectSkillAsync(message);
                case MessageType.SectQuest:
                    return await HandleSectQuestAsync(message);
                case MessageType.Reputation:
                    return await HandleReputationAsync(message);
                case MessageType.ChivalryPoint:
                    return await HandleChivalryPointAsync(message);
                case MessageType.Duel:
                    return await HandleDuelAsync(message);
                case MessageType.SwornBrother:
                    return await HandleSwornBrotherAsync(message);
                case MessageType.MasterApprentice:
                    return await HandleMasterApprenticeAsync(message);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleFriendAsync(HorizonMessagePacket message)
        {
            try
            {
                AddFriendRequest addFriendRequest = message.Body as AddFriendRequest;
                // 处理好友逻辑
                var response = new AddFriendResponse
                {
                    Success = true,
                    Message = "添加好友成功",
                    FriendInfo = new FriendInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理好友消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理好友消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleTeamAsync(HorizonMessagePacket message)
        {
            try
            {
                CreateTeamRequest createTeamRequest = message.Body as CreateTeamRequest;
                // 处理组队逻辑
                var response = new CreateTeamResponse
                {
                    Success = true,
                    Message = "创建队伍成功",
                    TeamInfo = new TeamInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理组队消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理组队消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleGuildAsync(HorizonMessagePacket message)
        {
            try
            {
                CreateGuildRequest createGuildRequest = message.Body as CreateGuildRequest;
                // 处理帮派逻辑
                var response = new CreateGuildResponse
                {
                    Success = true,
                    Message = "创建帮派成功",
                    GuildInfo = new GuildInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理帮派消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理帮派消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                SectInfoMessage sectInfoMessage = message.Body as SectInfoMessage;
                // 处理门派信息逻辑
                var response = new SectInfoMessage
                {
                    SectId = sectInfoMessage.SectId,
                    SectName = sectInfoMessage.SectName,
                    SectLeader = sectInfoMessage.SectLeader,
                    MemberCount = sectInfoMessage.MemberCount,
                    SectLevel = sectInfoMessage.SectLevel,
                    Reputation = sectInfoMessage.Reputation,
                    Resources = sectInfoMessage.Resources
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派信息消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派信息消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleJoinSectAsync(HorizonMessagePacket message)
        {
            try
            {
                JoinSectRequest joinSectRequest = message.Body as JoinSectRequest;
                // 处理加入门派逻辑
                var response = new JoinSectResponse
                {
                    Success = true,
                    Message = "成功加入门派",
                    SectId = joinSectRequest.SectId,
                    Position = "弟子"
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理加入门派消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理加入门派消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                SectSkillMessage sectSkillMessage = message.Body as SectSkillMessage;
                // 处理门派技能逻辑
                var response = new SectSkillMessage
                {
                    SectId = sectSkillMessage.SectId,
                    SkillId = sectSkillMessage.SkillId,
                    SkillName = sectSkillMessage.SkillName,
                    Description = sectSkillMessage.Description,
                    Level = sectSkillMessage.Level,
                    LearningConditions = sectSkillMessage.LearningConditions
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派技能消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派技能消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                SectQuestMessage sectQuestMessage = message.Body as SectQuestMessage;
                // 处理门派任务逻辑
                var response = new SectQuestMessage
                {
                    QuestId = sectQuestMessage.QuestId,
                    QuestName = sectQuestMessage.QuestName,
                    Description = sectQuestMessage.Description,
                    Rewards = sectQuestMessage.Rewards,
                    Requirements = sectQuestMessage.Requirements,
                    SectId = sectQuestMessage.SectId
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派任务消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleReputationAsync(HorizonMessagePacket message)
        {
            try
            {
                ReputationUpdateMessage reputationUpdateMessage = message.Body as ReputationUpdateMessage;
                // 处理声望更新逻辑
                var response = new ReputationUpdateMessage
                {
                    CharacterId = reputationUpdateMessage.CharacterId,
                    ReputationType = reputationUpdateMessage.ReputationType,
                    ChangeValue = reputationUpdateMessage.ChangeValue,
                    CurrentValue = reputationUpdateMessage.CurrentValue,
                    Reason = reputationUpdateMessage.Reason
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理声望更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理声望更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChivalryPointAsync(HorizonMessagePacket message)
        {
            try
            {
                ChivalryPointUpdateMessage chivalryPointUpdateMessage = message.Body as ChivalryPointUpdateMessage;
                // 处理侠义值更新逻辑
                var response = new ChivalryPointUpdateMessage
                {
                    CharacterId = chivalryPointUpdateMessage.CharacterId,
                    ChangeValue = chivalryPointUpdateMessage.ChangeValue,
                    CurrentValue = chivalryPointUpdateMessage.CurrentValue,
                    Reason = chivalryPointUpdateMessage.Reason
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理侠义值更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理侠义值更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDuelAsync(HorizonMessagePacket message)
        {
            try
            {
                DuelRequest duelRequest = message.Body as DuelRequest;
                // 处理比武切磋逻辑
                var response = new DuelResponse
                {
                    Accepted = true,
                    Message = "接受比武切磋",
                    DuelId = (ulong)new Random().Next(100000, 999999)
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理比武切磋消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理比武切磋消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSwornBrotherAsync(HorizonMessagePacket message)
        {
            try
            {
                SwornBrotherRequest swornBrotherRequest = message.Body as SwornBrotherRequest;
                // 处理结拜逻辑
                var response = new SwornBrotherResponse
                {
                    Agreed = true,
                    Message = "结拜成功",
                    BrotherhoodId = (ulong)new Random().Next(100000, 999999)
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理结拜消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理结拜消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleMasterApprenticeAsync(HorizonMessagePacket message)
        {
            try
            {
                MasterApprenticeRequest masterApprenticeRequest = message.Body as MasterApprenticeRequest;
                // 处理师徒关系逻辑
                var response = new MasterApprenticeResponse
                {
                    Agreed = true,
                    Message = "师徒关系建立成功",
                    RelationshipId = (ulong)new Random().Next(100000, 999999),
                    RelationshipLevel = 1
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理师徒关系消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理师徒关系消息失败" }));
            }
        }
    }
}
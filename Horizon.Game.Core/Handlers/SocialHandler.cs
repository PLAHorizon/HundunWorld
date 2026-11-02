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
                return message.Body switch
                {
                    CreateTeamRequest req => await HandleTeamOperationAsync(req),
                    JoinTeamRequest join => await HandleTeamOperationAsync(new CreateTeamRequest
                    {
                        Operation = TeamOperation.Join,
                        LeaderId = join.RequesterId,
                        TeamId = join.TeamId,
                    }),
                    _ => (true, CreateHorizonMessage(new ErrorMessage
                    {
                        ErrorCode = 400,
                        Message = "不支持的组队操作"
                    }))
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理组队消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理组队消息失败" }));
            }
        }

        /// <summary>
        /// 组队操作统一入口（Create/Join/Leave/Kick/Transfer/Disband）。
        /// 复用 CreateTeamRequest/Response 的 union tag：消息 tag 空间已用尽（0..255），
        /// 管理操作通过 <see cref="TeamOperation"/> 区分。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleTeamOperationAsync(CreateTeamRequest request)
        {
            var response = new CreateTeamResponse { Operation = request.Operation };

            if (request.Operation == TeamOperation.Create)
            {
                if (request.LeaderId == 0UL)
                    return FailTeam(response, "角色ID无效");
                if (string.IsNullOrWhiteSpace(request.TeamName))
                    return FailTeam(response, "队伍名称不能为空");

                var teamId = NewTeamId();
                var teamGrain = OrleansClient.GetGrain<ITeamGrain>(ToSocialKey(teamId));
                var ok = await teamGrain.CreateTeamAsync(ToSocialKey(request.LeaderId), request.TeamName, request.TeamGoal);
                if (!ok) return FailTeam(response, "创建队伍失败：队伍名无效、过长或队伍已存在");

                response.Success = true;
                response.Message = "创建队伍成功";
                response.TeamInfo = await teamGrain.GetTeamInfoAsync();
                return (true, CreateHorizonMessage(response));
            }

            if (request.TeamId == 0UL)
                return FailTeam(response, "队伍ID无效");

            var grain = OrleansClient.GetGrain<ITeamGrain>(ToSocialKey(request.TeamId));
            switch (request.Operation)
            {
                case TeamOperation.Join:
                {
                    if (request.LeaderId == 0UL) return FailTeam(response, "角色ID无效");
                    var ok = await grain.JoinTeamAsync(ToSocialKey(request.LeaderId));
                    if (!ok) return FailTeam(response, "加入队伍失败：队伍不存在、已满或已在队伍中");
                    response.Success = true;
                    response.Message = "加入队伍成功";
                    response.TeamInfo = await grain.GetTeamInfoAsync();
                    break;
                }

                case TeamOperation.Leave:
                {
                    if (request.LeaderId == 0UL) return FailTeam(response, "角色ID无效");
                    var ok = await grain.LeaveTeamAsync(ToSocialKey(request.LeaderId));
                    if (!ok) return FailTeam(response, "离开队伍失败：队伍不存在或不是队员");
                    response.Success = true;
                    response.Message = "已离开队伍";
                    break;
                }

                case TeamOperation.Kick:
                {
                    if (request.LeaderId == 0UL || request.TargetId == 0UL)
                        return FailTeam(response, "操作者/目标无效");
                    var ok = await grain.KickMemberAsync(ToSocialKey(request.LeaderId), ToSocialKey(request.TargetId));
                    if (!ok) return FailTeam(response, "踢人失败：只有队长可操作，或目标不在队伍中");
                    response.Success = true;
                    response.Message = "已踢出队员";
                    response.TeamInfo = await grain.GetTeamInfoAsync();
                    break;
                }

                case TeamOperation.Transfer:
                {
                    if (request.LeaderId == 0UL || request.TargetId == 0UL)
                        return FailTeam(response, "队长/新队长无效");
                    var ok = await grain.TransferLeaderAsync(ToSocialKey(request.LeaderId), ToSocialKey(request.TargetId));
                    if (!ok) return FailTeam(response, "转移队长失败：只有队长可操作，或目标不在队伍中");
                    response.Success = true;
                    response.Message = "已转移队长";
                    response.TeamInfo = await grain.GetTeamInfoAsync();
                    break;
                }

                case TeamOperation.Disband:
                {
                    if (request.LeaderId == 0UL) return FailTeam(response, "队长ID无效");
                    var ok = await grain.DisbandTeamAsync(ToSocialKey(request.LeaderId));
                    if (!ok) return FailTeam(response, "解散失败：只有队长可解散");
                    response.Success = true;
                    response.Message = "队伍已解散";
                    break;
                }

                default:
                    return FailTeam(response, "不支持的组队操作");
            }

            return (true, CreateHorizonMessage(response));
        }

        private (bool, HorizonMessagePacket) FailTeam(CreateTeamResponse response, string message)
        {
            response.Success = false;
            response.Message = message;
            return (true, CreateHorizonMessage(response));
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleGuildAsync(HorizonMessagePacket message)
        {
            try
            {
                return message.Body switch
                {
                    CreateGuildRequest create => await CreateGuildAsync(create),
                    JoinGuildRequest join => await JoinGuildAsync(join),
                    _ => (true, CreateHorizonMessage(new ErrorMessage
                    {
                        ErrorCode = 400,
                        Message = "不支持的帮派操作"
                    }))
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理帮派消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理帮派消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> CreateGuildAsync(CreateGuildRequest request)
        {
            var response = new CreateGuildResponse();
            if (request.CreatorId == 0UL)
            {
                response.Success = false;
                response.Message = "角色ID无效";
                return (true, CreateHorizonMessage(response));
            }

            // GuildInfo.GuildId 是 int；用"低 4 字节为 GuildId、其余为 0"的 Guid 作主键，
            // 保证 int -> Guid 可逆（GuildGrain 的 GuidToUInt64 只读前 8 字节）。
            var guildId = Random.Shared.Next(1, int.MaxValue);
            var guildKey = ToSocialKey((ulong)(uint)guildId);
            var guildGrain = OrleansClient.GetGrain<IGuildGrain>(guildKey);

            var ok = await guildGrain.CreateGuildAsync(request.GuildName, ToSocialKey(request.CreatorId));
            if (!ok)
            {
                response.Success = false;
                response.Message = "创建帮派失败：名称无效、过长或帮派已存在";
                return (true, CreateHorizonMessage(response));
            }

            var info = await guildGrain.GetGuildInfoAsync();
            info.GuildId = guildId;
            response.Success = true;
            response.Message = "创建帮派成功";
            response.GuildInfo = info;
            return (true, CreateHorizonMessage(response));
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> JoinGuildAsync(JoinGuildRequest request)
        {
            var response = new JoinGuildResponse();
            if (request.RequesterId == 0UL || request.GuildId <= 0)
            {
                response.Success = false;
                response.Message = "角色ID或帮派ID无效";
                return (true, CreateHorizonMessage(response));
            }

            var guildGrain = OrleansClient.GetGrain<IGuildGrain>(ToSocialKey((ulong)(uint)request.GuildId));
            var ok = await guildGrain.ApplyToJoinAsync(ToSocialKey(request.RequesterId), request.ApplicationReason);
            if (!ok)
            {
                response.Success = false;
                response.Message = "申请加入帮派失败：帮派不存在或已在帮派中";
                return (true, CreateHorizonMessage(response));
            }

            var info = await guildGrain.GetGuildInfoAsync();
            info.GuildId = request.GuildId;
            response.Success = true;
            response.Message = "入帮申请已提交，等待审批";
            response.GuildInfo = info;
            return (true, CreateHorizonMessage(response));
        }

        private static Guid ToSocialKey(ulong id)
        {
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, id);
            return new Guid(bytes);
        }

        private static ulong NewTeamId()
        {
            ulong id;
            do
            {
                id = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
            } while (id == 0UL);

            return id;
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
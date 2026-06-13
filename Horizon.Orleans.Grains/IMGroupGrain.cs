using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// IM群组Grain实现 - 管理群组元数据、成员、群聊消息
    /// </summary>
    public class IMGroupGrain : Grain, IIMGroupGrain
    {
        private const int MaxMessageContentLength = 5000;
        private const int MaxGroupNameLength = 50;
        private const int MaxAnnouncementLength = 500;
        /// <summary>邀请/申请过期时间：3天（毫秒）。</summary>
        private const long InviteExpirationMs = 3L * 24 * 60 * 60 * 1000;

        private readonly ILogger<IMGroupGrain> _logger;
        private readonly IPersistentState<IMGroupState> _groupState;
        private readonly SensitiveWordFilter _sensitiveWordFilter;
        private readonly MessageRateLimiter _rateLimiter;
        private bool _chatStateFlushPending;
        private bool _chatStateFlushInProgress;

        public IMGroupGrain(
            ILogger<IMGroupGrain> logger,
            [PersistentState("imGroup", "GameStore")] IPersistentState<IMGroupState> groupState)
        {
            _logger = logger;
            _groupState = groupState;
            _sensitiveWordFilter = new SensitiveWordFilter();
            _rateLimiter = new MessageRateLimiter(maxMessagesPerWindow: 20, windowSeconds: 60);
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("IMGroupGrain {GrainKey} 正在激活", this.GetPrimaryKey());

            var state = _groupState.State;
            state.Members ??= new Dictionary<ulong, IMGroupMemberEntry>();
            state.ChatHistory ??= new List<IMGroupChatRecord>();
            state.PendingJoinApplications ??= new Dictionary<ulong, IMGroupJoinApplicationEntry>();
            state.PendingInvites ??= new Dictionary<ulong, IMGroupPendingInviteEntry>();
            state.PendingInviteApprovals ??= new Dictionary<ulong, IMGroupPendingInviteApprovalEntry>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<IMGroupCreateResponse> CreateGroupAsync(IMGroupCreateRequest request)
        {
            try
            {
                _logger.LogInformation("创建群组: CreatorId={CreatorId}, GroupName={GroupName}",
                    request.CreatorId, request.GroupName);

                var response = new IMGroupCreateResponse();

                var state = _groupState.State;

                // 若此 Grain 已经存在一个活跃群组（有成员且未解散），拒绝重复创建。
                // 若原群组已解散，则清理旧状态允许在同一 Grain 上重建（防御性处理 UUID 复用边缘情况）。
                if (state.Members.Count > 0 && !state.IsDisbanded)
                {
                    response.Success = false;
                    response.Message = "群组已经在此 Grain ID 存在";
                    return response;
                }

                if (string.IsNullOrWhiteSpace(request.GroupName) || request.GroupName.Length > MaxGroupNameLength)
                {
                    response.Success = false;
                    response.Message = "群组名称无效";
                    return response;
                }

                var groupNameCheck = _sensitiveWordFilter.Check(request.GroupName);
                if (groupNameCheck.IsViolation)
                {
                    _logger.LogWarning("群组名称包含敏感词: GroupName={GroupName}, Categories={Categories}",
                        request.GroupName, string.Join(",", groupNameCheck.MatchedCategories));
                    response.Success = false;
                    response.Message = "群组名称包含违禁内容";
                    return response;
                }

                // 服务端防重名：通过群主的用户粒子原子性地检查并注册群名。
                // 同名不同ID被视为重名。
                // 因此如果是同一个用户发起群名为A的群组创建请求：如果此前他没有名为A的活跃群组，就会创建成功。
                var groupIdForCheck = GuidToUInt64(this.GetPrimaryKey());
                try
                {
                    var creatorGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(request.CreatorId));
                    var nameAvailable = await creatorGrain.CheckAndRegisterGroupNameAsync(request.GroupName, groupIdForCheck);
                    if (!nameAvailable)
                    {
                        var groupId = string.Empty;
                        // 若已存在，则在抛出错误之前也可以查一下此用户已有的同名群组ID以作提示或不作强制失败处理。
                        // 这一步若业务需要可以扩展。
                        
                        response.Success = false;
                        response.Message = $"已存在同名群组「{request.GroupName}」，请修改群名后重试；如需恢复或加入该群组请重新启动客户端或重试创建新名称。";
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "检查群名重复失败，继续创建: GroupName={GroupName}", request.GroupName);
                }

                if (!string.IsNullOrWhiteSpace(request.Announcement))
                {
                    var announcementCheck = _sensitiveWordFilter.Check(request.Announcement);
                    if (announcementCheck.IsViolation)
                    {
                        _logger.LogWarning("群公告包含敏感词: Categories={Categories}",
                            string.Join(",", announcementCheck.MatchedCategories));
                        response.Success = false;
                        response.Message = "群公告包含违禁内容";
                        return response;
                    }
                }

                // 所有校验通过后，再清理已解散群组的旧状态，防止校验失败时产生不一致的内存态。
                if (state.IsDisbanded)
                {
                    state.Members.Clear();
                    state.ChatHistory?.Clear();
                    state.PendingJoinApplications?.Clear();
                    state.PendingInvites?.Clear();
                    state.PendingInviteApprovals?.Clear();
                    state.IsDisbanded = false;
                }

                state.GroupId = this.GetPrimaryKey();
                state.GroupName = request.GroupName;
                state.OwnerId = request.CreatorId;
                state.Avatar = request.GroupAvatar ?? "";
                state.Announcement = request.Announcement ?? "";
                state.MaxMembers = request.MaxMembers > 0 ? request.MaxMembers : 200;
                state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state.IsDisbanded = false;

                state.Members[request.CreatorId] = new IMGroupMemberEntry
                {
                    UserId = request.CreatorId,
                    Nickname = "",
                    Role = IMGroupMemberRole.Owner,
                    JoinTime = state.CreateTime
                };

                if (request.InitialMemberIds != null)
                {
                    foreach (var memberId in request.InitialMemberIds)
                    {
                        if (memberId == request.CreatorId) continue;
                        if (state.Members.Count >= state.MaxMembers) break;

                        state.Members[memberId] = new IMGroupMemberEntry
                        {
                            UserId = memberId,
                            Role = IMGroupMemberRole.Member,
                            JoinTime = state.CreateTime
                        };
                    }
                }

                await _groupState.WriteStateAsync();

                response.Success = true;
                response.GroupInfo = new IMGroupInfo
                {
                    GroupId = GuidToUInt64(state.GroupId),
                    GroupName = state.GroupName,
                    OwnerId = state.OwnerId,
                    GroupAvatar = state.Avatar,
                    Announcement = state.Announcement,
                    MemberCount = state.Members.Count,
                    MaxMembers = state.MaxMembers,
                    CreateTime = state.CreateTime
                };

                _logger.LogInformation("群组创建成功: GroupId={GroupId}, MemberCount={MemberCount}",
                    state.GroupId, state.Members.Count);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建群组失败");
                throw;
            }
        }

        public async Task<IMGroupJoinResponse> JoinGroupAsync(IMGroupJoinRequest request)
        {
            try
            {
                _logger.LogInformation("加入群组请求: UserId={UserId}, GroupId={GroupId}",
                    request.UserId, request.GroupId);

                var response = new IMGroupJoinResponse
                {
                    GroupId = request.GroupId
                };

                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    response.Success = false;
                    response.Message = "群组已解散";
                    return response;
                }

                if (state.Members.ContainsKey(request.UserId))
                {
                    response.Success = false;
                    response.Message = "已是群成员";
                    return response;
                }

                if (state.Members.Count >= state.MaxMembers)
                {
                    response.Success = false;
                    response.Message = "群组人数已满";
                    return response;
                }

                // 需要管理员审核：创建待审核申请并通知管理员/群主
                if (state.JoinApprovalRequired)
                {
                    if (state.PendingJoinApplications.ContainsKey(request.UserId))
                    {
                        response.Success = false;
                        response.Message = "已提交申请，请等待审核";
                        return response;
                    }

                    state.PendingJoinApplications[request.UserId] = new IMGroupJoinApplicationEntry
                    {
                        ApplicantId = request.UserId,
                        // 申请人昵称由客户端发送，此处暂为空；后续可通过 IIMUserGrain 查询
                        ApplicantName = "",
                        Reason = request.Reason ?? "",
                        ApplyTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    await _groupState.WriteStateAsync();

                    // 通知群主和所有管理员
                    var notify = new IMGroupJoinApplyNotify
                    {
                        GroupId = request.GroupId,
                        GroupName = state.GroupName,
                        ApplicantId = request.UserId,
                        ApplicantName = "",
                        Reason = request.Reason ?? ""
                    };
                    await NotifyAdminsAsync(state, notify);

                    response.Success = true;
                    response.Message = "申请已提交，等待管理员审核";
                    _logger.LogInformation("加群申请已提交: UserId={UserId}", request.UserId);
                    return response;
                }

                // 无需审核：直接加入
                state.Members[request.UserId] = new IMGroupMemberEntry
                {
                    UserId = request.UserId,
                    Nickname = "",
                    Role = IMGroupMemberRole.Member,
                    JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await _groupState.WriteStateAsync();

                response.Success = true;
                _logger.LogInformation("加入群组成功: UserId={UserId}", request.UserId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加入群组失败");
                throw;
            }
        }

        public async Task<IMGroupJoinResponse> InviteUserAsync(IMGroupInviteRequest request)
        {
            try
            {
                _logger.LogInformation("邀请入群: InviterId={InviterId}, GroupId={GroupId}, Invitees={Count}",
                    request.InviterId, request.GroupId, request.InviteeIds?.Count ?? 0);

                var response = new IMGroupJoinResponse { GroupId = request.GroupId };
                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    response.Success = false;
                    response.Message = "群组已解散";
                    return response;
                }

                if (!state.Members.TryGetValue(request.InviterId, out var inviterEntry))
                {
                    response.Success = false;
                    response.Message = "邀请人不是群成员";
                    return response;
                }

                if (request.InviteeIds == null || request.InviteeIds.Count == 0)
                {
                    response.Success = false;
                    response.Message = "未指定被邀请用户";
                    return response;
                }

                // 清理过期的待确认邀请
                PurgeExpiredInvites(state);

                var isOwnerInviter = inviterEntry.Role == IMGroupMemberRole.Owner
                    || state.OwnerId == request.InviterId;
                // 当 MemberInviteRequiresApproval=false（默认）时，普通成员邀请与群主邀请行为一致，直接送达被邀请者
                var canDirectInvite = isOwnerInviter || !state.MemberInviteRequiresApproval;
                int delivered = 0;
                int pendingApprovalCount = 0;

                foreach (var inviteeId in request.InviteeIds)
                {
                    if (state.Members.ContainsKey(inviteeId))
                        continue;
                    if (state.Members.Count >= state.MaxMembers)
                        break;

                    if (!canDirectInvite)
                    {
                        // 非群主发起邀请且群主开启了审批要求：先挂起到待审批队列，通知群主审核，不通知被邀请者。
                        // 若该被邀请者已有未过期的待审批条目，则跳过以防止重复通知群主。
                        var nowForDedupe = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (state.PendingInviteApprovals.TryGetValue(inviteeId, out var existingApproval)
                            && existingApproval.RequestTime > 0
                            && (nowForDedupe - existingApproval.RequestTime) <= InviteExpirationMs)
                        {
                            // 同一被邀请者已有未过期审批请求，跳过以避免骚扰群主
                            pendingApprovalCount++;
                            continue;
                        }

                        var requestTs = nowForDedupe;
                        state.PendingInviteApprovals[inviteeId] = new IMGroupPendingInviteApprovalEntry
                        {
                            InviteeId = inviteeId,
                            InviterId = request.InviterId,
                            InviterName = inviterEntry.Nickname,
                            RequestTime = requestTs
                        };

                        var approvalNotify = new IMGroupInviteApprovalNotify
                        {
                            GroupId = request.GroupId,
                            GroupName = state.GroupName,
                            InviterId = request.InviterId,
                            InviterName = inviterEntry.Nickname,
                            InviteeId = inviteeId,
                            Timestamp = requestTs
                        };
                        await NotifyUserAsync(state.OwnerId, approvalNotify);
                        pendingApprovalCount++;
                    }
                    else
                    {
                        // 群主邀请，或普通成员邀请且未开启群主审批要求：邀请消息直达被邀请者
                        await DeliverInviteToInviteeAsync(state, request.GroupId, request.InviterId, inviterEntry.Nickname, inviteeId);
                        delivered++;
                    }
                }

                await _groupState.WriteStateAsync();

                response.Success = true;
                response.InviteConsentRequired = state.InviteConsentRequired;
                if (canDirectInvite)
                {
                    response.Message = state.InviteConsentRequired
                        ? $"邀请已发送，等待 {delivered} 位用户确认"
                        : $"已成功邀请 {delivered} 位用户入群";
                }
                else
                {
                    response.Message = pendingApprovalCount > 0
                        ? $"邀请已提交，等待群主审批（共 {pendingApprovalCount} 条）"
                        : "邀请已提交";
                }
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "邀请入群失败");
                throw;
            }
        }

        /// <summary>
        /// 将邀请消息实际送达被邀请者：若群组配置要求被邀请者同意则走待确认流程，否则直接加入。
        /// </summary>
        private async Task DeliverInviteToInviteeAsync(
            IMGroupState state,
            ulong groupId,
            ulong inviterId,
            string inviterName,
            ulong inviteeId)
        {
            if (state.InviteConsentRequired)
            {
                var inviteTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state.PendingInvites[inviteeId] = new IMGroupPendingInviteEntry
                {
                    InviteeId = inviteeId,
                    InviterId = inviterId,
                    InviteTime = inviteTs
                };

                var inviteNotify = new IMGroupInviteNotify
                {
                    GroupId = groupId,
                    GroupName = state.GroupName,
                    InviterId = inviterId,
                    InviterName = inviterName,
                    RequiresConsent = true
                };
                await NotifyUserAsync(inviteeId, inviteNotify);

                try
                {
                    var inviteeGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(inviteeId));
                    await inviteeGrain.AddPendingGroupInviteAsync(
                        groupId,
                        state.GroupName,
                        inviterId,
                        inviterName,
                        inviteTs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "同步入群邀请到用户粒子失败: InviteeId={InviteeId}, GroupId={GroupId}", inviteeId, groupId);
                }
            }
            else
            {
                state.Members[inviteeId] = new IMGroupMemberEntry
                {
                    UserId = inviteeId,
                    Nickname = "",
                    Role = IMGroupMemberRole.Member,
                    JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var inviteNotify = new IMGroupInviteNotify
                {
                    GroupId = groupId,
                    GroupName = state.GroupName,
                    InviterId = inviterId,
                    InviterName = inviterName,
                    RequiresConsent = false
                };
                await NotifyUserAsync(inviteeId, inviteNotify);

                // 即使被邀请者当前在线，也向其用户粒子写入一条直接加入通知（RequiresConsent=false），
                // 确保离线状态下重连后能通过 GetPendingGroupInvitesAsync 发现新群组成员关系。
                // 客户端收到该通知后仅刷新群组列表，无需用户确认，服务端在返回时自动清除该条目。
                try
                {
                    var inviteeGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(inviteeId));
                    await inviteeGrain.AddPendingGroupInviteAsync(
                        groupId, state.GroupName, inviterId, inviterName,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        requiresConsent: false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "同步直接加入通知到用户粒子失败: InviteeId={InviteeId}, GroupId={GroupId}", inviteeId, groupId);
                }
            }
        }

        public async Task<IMGroupJoinResponse> RespondToInviteAsync(IMGroupInviteResponse response)
        {
            try
            {
                _logger.LogInformation("响应入群邀请: UserId={UserId}, GroupId={GroupId}, Accept={Accept}",
                    response.UserId, response.GroupId, response.Accept);

                var result = new IMGroupJoinResponse { GroupId = response.GroupId };
                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    result.Success = false;
                    result.Message = "群组已解散";
                    return result;
                }

                if (!state.PendingInvites.ContainsKey(response.UserId))
                {
                    result.Success = false;
                    result.Message = "未找到待确认的邀请";
                    return result;
                }

                // 检查邀请是否已过期（3天）
                var invite = state.PendingInvites[response.UserId];
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (invite.InviteTime > 0 && (nowMs - invite.InviteTime) > InviteExpirationMs)
                {
                    state.PendingInvites.Remove(response.UserId);
                    await _groupState.WriteStateAsync();
                    result.Success = false;
                    result.Message = "邀请已过期（超过3天）";
                    return result;
                }

                state.PendingInvites.Remove(response.UserId);

                if (response.Accept)
                {
                    if (state.Members.ContainsKey(response.UserId))
                    {
                        result.Success = false;
                        result.Message = "已是群成员";
                        return result;
                    }

                    if (state.Members.Count >= state.MaxMembers)
                    {
                        result.Success = false;
                        result.Message = "群组人数已满";
                        return result;
                    }

                    state.Members[response.UserId] = new IMGroupMemberEntry
                    {
                        UserId = response.UserId,
                        Nickname = "",
                        Role = IMGroupMemberRole.Member,
                        JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    result.Success = true;
                    result.Message = "已成功加入群组";
                }
                else
                {
                    result.Success = true;
                    result.Message = "已拒绝入群邀请";
                }

                await _groupState.WriteStateAsync();

                // 从用户粒子移除待处理邀请（不影响主流程，失败仅记录警告）
                try
                {
                    var userGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(response.UserId));
                    await userGrain.RemovePendingGroupInviteAsync(response.GroupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "从用户粒子移除入群邀请失败: UserId={UserId}, GroupId={GroupId}", response.UserId, response.GroupId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "响应入群邀请失败");
                throw;
            }
        }

        public async Task<IMGroupJoinResponse> ReviewJoinApplicationAsync(IMGroupJoinApplyReview review)
        {
            try
            {
                _logger.LogInformation("审核加群申请: ReviewerId={ReviewerId}, ApplicantId={ApplicantId}, Approve={Approve}",
                    review.ReviewerId, review.ApplicantId, review.Approve);

                var result = new IMGroupJoinResponse { GroupId = review.GroupId };
                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    result.Success = false;
                    result.Message = "群组已解散";
                    return result;
                }

                // 只有群主或管理员可以审核
                if (!state.Members.TryGetValue(review.ReviewerId, out var reviewer) ||
                    (reviewer.Role != IMGroupMemberRole.Owner && reviewer.Role != IMGroupMemberRole.Admin))
                {
                    result.Success = false;
                    result.Message = "无权审核加群申请";
                    return result;
                }

                if (!state.PendingJoinApplications.TryGetValue(review.ApplicantId, out _))
                {
                    result.Success = false;
                    result.Message = "未找到该用户的加群申请";
                    return result;
                }

                state.PendingJoinApplications.Remove(review.ApplicantId);

                if (review.Approve)
                {
                    if (state.Members.ContainsKey(review.ApplicantId))
                    {
                        result.Success = false;
                        result.Message = "该用户已是群成员";
                        return result;
                    }

                    if (state.Members.Count >= state.MaxMembers)
                    {
                        result.Success = false;
                        result.Message = "群组人数已满，无法通过申请";
                        return result;
                    }

                    // 用户昵称由群成员列表同步时补全，此处先置空
                    state.Members[review.ApplicantId] = new IMGroupMemberEntry
                    {
                        UserId = review.ApplicantId,
                        Nickname = "",
                        Role = IMGroupMemberRole.Member,
                        JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    // 通知申请人已通过
                    var approveNotify = new IMGroupJoinResponse
                    {
                        GroupId = review.GroupId,
                        Success = true,
                        Message = $"您的加群申请已通过，已成功加入「{state.GroupName}」"
                    };
                    await NotifyUserAsync(review.ApplicantId, approveNotify);

                    result.Success = true;
                    result.Message = "已通过加群申请";
                }
                else
                {
                    // 通知申请人已拒绝
                    var rejectNotify = new IMGroupJoinResponse
                    {
                        GroupId = review.GroupId,
                        Success = false,
                        Message = $"您加入「{state.GroupName}」的申请已被拒绝"
                    };
                    await NotifyUserAsync(review.ApplicantId, rejectNotify);

                    result.Success = true;
                    result.Message = "已拒绝加群申请";
                }

                await _groupState.WriteStateAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核加群申请失败");
                throw;
            }
        }

        public async Task<IMGroupJoinResponse> ReviewInviteApprovalAsync(IMGroupInviteApprovalReview review)
        {
            try
            {
                _logger.LogInformation("审核入群邀请: ReviewerId={ReviewerId}, InviteeId={InviteeId}, Approve={Approve}",
                    review.ReviewerId, review.InviteeId, review.Approve);

                var result = new IMGroupJoinResponse { GroupId = review.GroupId };
                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    result.Success = false;
                    result.Message = "群组已解散";
                    return result;
                }

                // 只有群主可以审批由非群主成员发起的邀请
                if (state.OwnerId != review.ReviewerId)
                {
                    result.Success = false;
                    result.Message = "只有群主可以审批入群邀请";
                    return result;
                }

                if (!state.PendingInviteApprovals.TryGetValue(review.InviteeId, out var pending))
                {
                    result.Success = false;
                    result.Message = "未找到该用户的待审批邀请";
                    return result;
                }

                // 先检查过期，再验证成员状态，最后才移除审批条目
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (pending.RequestTime > 0 && (nowMs - pending.RequestTime) > InviteExpirationMs)
                {
                    state.PendingInviteApprovals.Remove(review.InviteeId);
                    await _groupState.WriteStateAsync();
                    result.Success = false;
                    result.Message = "该邀请审批已过期";
                    return result;
                }

                if (review.Approve)
                {
                    if (state.Members.ContainsKey(review.InviteeId))
                    {
                        state.PendingInviteApprovals.Remove(review.InviteeId);
                        await _groupState.WriteStateAsync();
                        result.Success = false;
                        result.Message = "该用户已是群成员";
                        return result;
                    }

                    if (state.Members.Count >= state.MaxMembers)
                    {
                        // 群已满时保留审批条目，允许群主之后再试（例如有人退群后）
                        result.Success = false;
                        result.Message = "群组人数已满";
                        return result;
                    }

                    state.PendingInviteApprovals.Remove(review.InviteeId);

                    // 审批通过后，邀请消息才送达被邀请者
                    await DeliverInviteToInviteeAsync(
                        state,
                        review.GroupId,
                        pending.InviterId,
                        pending.InviterName,
                        review.InviteeId);

                    result.Success = true;
                    result.Message = "已批准入群邀请";
                }
                else
                {
                    state.PendingInviteApprovals.Remove(review.InviteeId);
                    result.Success = true;
                    result.Message = "已拒绝入群邀请";
                }

                await _groupState.WriteStateAsync();

                // 向原邀请人推送审批结果通知
                var resultNotify = new IMGroupInviteResultNotify
                {
                    GroupId = review.GroupId,
                    GroupName = state.GroupName,
                    InviteeId = review.InviteeId,
                    Approved = review.Approve,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await NotifyUserAsync(pending.InviterId, resultNotify);

                _logger.LogInformation(
                    "入群邀请审批完成，已通知原邀请人: GroupId={GroupId}, InviterId={InviterId}, InviteeId={InviteeId}, Approve={Approve}",
                    review.GroupId,
                    pending.InviterId,
                    review.InviteeId,
                    review.Approve);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核入群邀请失败");
                throw;
            }
        }

        public Task<List<IMGroupInviteApprovalNotify>> GetPendingInviteApprovalsAsync()
        {
            var state = _groupState.State;
            PurgeExpiredInvites(state);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var groupIdUlong = GuidToUInt64(state.GroupId);
            var result = state.PendingInviteApprovals.Values
                .Where(e => e.RequestTime <= 0 || (nowMs - e.RequestTime) <= InviteExpirationMs)
                .Select(e => new IMGroupInviteApprovalNotify
                {
                    GroupId = groupIdUlong,
                    GroupName = state.GroupName,
                    InviterId = e.InviterId,
                    InviterName = e.InviterName,
                    InviteeId = e.InviteeId,
                    Timestamp = e.RequestTime
                })
                .ToList();

            return Task.FromResult(result);
        }

        public async Task<IMGroupLeaveResponse> LeaveGroupAsync(IMGroupLeaveRequest request)
        {
            try
            {
                _logger.LogInformation("退出群组: UserId={UserId}", request.UserId);

                var response = new IMGroupLeaveResponse
                {
                    GroupId = request.GroupId
                };

                var state = _groupState.State;

                if (!state.Members.ContainsKey(request.UserId))
                {
                    response.Success = false;
                    response.Message = "不是群成员";
                    return response;
                }

                if (state.OwnerId == request.UserId)
                {
                    response.Success = false;
                    response.Message = "群主不能直接退出群组，请先转让群主或解散群组";
                    return response;
                }

                state.Members.Remove(request.UserId);
                await _groupState.WriteStateAsync();

                response.Success = true;
                _logger.LogInformation("退出群组成功: UserId={UserId}", request.UserId);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退出群组失败");
                throw;
            }
        }

        public async Task<IMGroupDisbandResponse> DisbandGroupAsync(IMGroupDisbandRequest request)
        {
            try
            {
                _logger.LogInformation("解散群组: OwnerId={OwnerId}", request.OwnerId);

                var response = new IMGroupDisbandResponse
                {
                    GroupId = request.GroupId
                };

                var state = _groupState.State;

                if (state.OwnerId != request.OwnerId)
                {
                    response.Success = false;
                    response.Message = "只有群主可以解散群组";
                    return response;
                }

                if (state.IsDisbanded)
                {
                    response.Success = true;
                    response.Message = "群组已解散";
                    return response;
                }

                var disbandTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state.IsDisbanded = true;

                // 清理待确认邀请前，并发通知各被邀请者的用户Grain移除对应的待处理记录
                var inviteeIds = state.PendingInvites.Keys.ToArray();
                await Task.WhenAll(inviteeIds.Select(async inviteeId =>
                {
                    try
                    {
                        var inviteeGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(inviteeId));
                        await inviteeGrain.RemovePendingGroupInviteAsync(request.GroupId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "解散群组时清除被邀请者待处理邀请失败: InviteeId={InviteeId}", inviteeId);
                    }
                }));

                // 解散时从群主的用户粒子中注销群名注册，使群主可复用该群名创建新群组
                try
                {
                    var ownerGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(state.OwnerId));
                    await ownerGrain.UnregisterOwnedGroupNameAsync(request.GroupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "解散群组时注销群名失败: OwnerId={OwnerId}, GroupId={GroupId}", state.OwnerId, request.GroupId);
                }

                // 清理待处理队列以防二次投递；保留 Members 与 ChatHistory 以便客户端仍可查看本地缓存消息
                state.PendingInvites.Clear();
                state.PendingInviteApprovals.Clear();
                state.PendingJoinApplications.Clear();

                // 并发通知所有成员群组已解散（客户端据此将群标记为已解散但不删除本地缓存）
                var memberIds = state.Members.Values.Select(m => m.UserId).ToList();
                var disbandNotify = new IMGroupDisbandNotify
                {
                    GroupId = request.GroupId,
                    GroupName = state.GroupName,
                    Timestamp = disbandTimestamp
                };
                await Task.WhenAll(memberIds.Select(memberId => NotifyUserAsync(memberId, disbandNotify)));

                await _groupState.WriteStateAsync();

                response.Success = true;
                _logger.LogInformation("群组已解散: GroupId={GroupId}, NotifiedMembers={Count}", state.GroupId, memberIds.Count);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解散群组失败");
                throw;
            }
        }

        public async Task<string> SendGroupMessageAsync(IMGroupChatSendMessage message)
        {
            try
            {
                _logger.LogInformation("发送群聊消息: SenderId={SenderId}, GroupId={GroupId}",
                    message.SenderId, message.GroupId);

                var state = _groupState.State;

                if (state.IsDisbanded)
                {
                    _logger.LogWarning("群组已解散，无法发送消息");
                    return "";
                }

                if (!state.Members.ContainsKey(message.SenderId))
                {
                    _logger.LogWarning("发送者不是群成员: SenderId={SenderId}", message.SenderId);
                    return "";
                }

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("消息内容为空");
                    return "";
                }

                if (message.Content.Length > MaxMessageContentLength)
                {
                    _logger.LogWarning("消息内容过长: Length={Length}", message.Content.Length);
                    return "";
                }

                var senderId = (long)message.SenderId;
                if (_rateLimiter.IsRateLimited(senderId))
                {
                    _logger.LogWarning("消息发送频率超限: SenderId={SenderId}", message.SenderId);
                    return "";
                }

                var filteredContent = _sensitiveWordFilter.FilterText(message.Content);
                var serverMessageId = Guid.NewGuid().ToString("N");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var record = new IMGroupChatRecord
                {
                    ServerMessageId = serverMessageId,
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp,
                    MentionedUserIds = message.MentionedUserIds ?? new List<ulong>(),
                    MentionAll = message.MentionAll,
                    Status = IMMessageStatus.Sent
                };

                state.ChatHistory.Add(record);

                if (state.ChatHistory.Count > state.MaxChatHistory)
                    state.ChatHistory.RemoveRange(0, state.ChatHistory.Count - state.MaxChatHistory);

                _rateLimiter.RecordMessage(senderId);
                await PersistGroupChatRecordAsync(message.GroupId, state.GroupName, record);

                var notify = new IMGroupChatNotifyMessage
                {
                    ServerMessageId = serverMessageId,
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    SenderAvatar = message.SenderAvatar,
                    GroupId = message.GroupId,
                    GroupName = state.GroupName,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp,
                    MentionedUserIds = message.MentionedUserIds ?? new List<ulong>(),
                    MentionAll = message.MentionAll,
                    Attachments = message.Attachments ?? new List<string>()
                };

                var recipientIds = state.Members.Keys
                    .Where(memberId => memberId != message.SenderId)
                    .ToList();

                ObserveBackgroundTask(
                    DeliverGroupMessageAsync(recipientIds, notify),
                    $"投递群聊消息给成员失败: GroupId={message.GroupId}, ServerMessageId={serverMessageId}");

                _logger.LogInformation("群聊消息发送成功: ServerMessageId={ServerMessageId}", serverMessageId);
                return serverMessageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送群聊消息失败");
                throw;
            }
        }

        public Task<IMGroupInfo> GetGroupInfoAsync()
        {
            try
            {
                var state = _groupState.State;

                // 若 GroupId 为空，说明该 Grain 从未创建过群组，视为已解散/不存在。
                // 这是为了解决 OwnedGroupNames 中存在过期条目指向已失效 Grain 时的假阳性封堵问题。
                var isNeverCreated = state.GroupId == Guid.Empty;

                var info = new IMGroupInfo
                {
                    GroupId = GuidToUInt64(state.GroupId),
                    GroupName = state.GroupName,
                    OwnerId = state.OwnerId,
                    GroupAvatar = state.Avatar,
                    Announcement = state.Announcement,
                    MemberCount = state.Members.Count,
                    MaxMembers = state.MaxMembers,
                    CreateTime = state.CreateTime,
                    JoinApprovalRequired = state.JoinApprovalRequired,
                    InviteConsentRequired = state.InviteConsentRequired,
                    IsDisbanded = state.IsDisbanded || isNeverCreated,
                    MemberInviteRequiresApproval = state.MemberInviteRequiresApproval
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取群组信息失败");
                throw;
            }
        }

        public async Task<bool> UpdateGroupInfoAsync(IMGroupInfoUpdateMessage update)
        {
            try
            {
                _logger.LogInformation("更新群组信息: OperatorId={OperatorId}", update.OperatorId);

                var state = _groupState.State;

                if (!state.Members.TryGetValue(update.OperatorId, out var member))
                {
                    _logger.LogWarning("操作者不是群成员: OperatorId={OperatorId}", update.OperatorId);
                    return false;
                }

                if (member.Role == IMGroupMemberRole.Member)
                {
                    _logger.LogWarning("普通成员无权修改群信息");
                    return false;
                }

                if (update.GroupInfo != null)
                {
                    // 群名称在创建后不可修改（仅允许解散群组）。忽略请求中的群名称字段，
                    // 避免客户端意外覆盖；见需求“群名称不可修改但可以解散群”。
                    if (!string.IsNullOrEmpty(update.GroupInfo.GroupName)
                        && !string.Equals(update.GroupInfo.GroupName, state.GroupName, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("拒绝修改群名称: GroupId={GroupId}, Requested={Requested}, Current={Current}",
                            state.GroupId, update.GroupInfo.GroupName, state.GroupName);
                    }

                    if (update.GroupInfo.GroupAvatar != null)
                        state.Avatar = update.GroupInfo.GroupAvatar;

                    if (update.GroupInfo.Announcement != null && update.GroupInfo.Announcement.Length <= MaxAnnouncementLength)
                    {
                        var announcementCheck = _sensitiveWordFilter.Check(update.GroupInfo.Announcement);
                        if (announcementCheck.IsViolation)
                        {
                            _logger.LogWarning("群公告包含敏感词: Categories={Categories}",
                                string.Join(",", announcementCheck.MatchedCategories));
                            return false;
                        }

                        state.Announcement = update.GroupInfo.Announcement;
                    }

                    // 更新入群设置（只有群主可修改）
                    if (member.Role == IMGroupMemberRole.Owner)
                    {
                        state.JoinApprovalRequired = update.GroupInfo.JoinApprovalRequired;
                        state.InviteConsentRequired = update.GroupInfo.InviteConsentRequired;
                        state.MemberInviteRequiresApproval = update.GroupInfo.MemberInviteRequiresApproval;
                    }
                }

                await _groupState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新群组信息失败");
                throw;
            }
        }

        public Task<IMGroupMemberListResponse> GetMemberListAsync(IMGroupMemberListRequest request)
        {
            try
            {
                var state = _groupState.State;
                var limit = request.Limit > 0 ? request.Limit : 50;

                var members = state.Members.Values
                    .OrderByDescending(m => m.Role)
                    .ThenBy(m => m.JoinTime)
                    .Skip(request.Offset)
                    .Take(limit)
                    .Select(m => new IMGroupMemberInfo
                    {
                        UserId = m.UserId,
                        Nickname = m.Nickname,
                        Avatar = m.Avatar,
                        GroupNickname = m.GroupNickname,
                        Role = m.Role,
                        JoinTime = m.JoinTime
                    })
                    .ToList();

                var response = new IMGroupMemberListResponse
                {
                    GroupId = request.GroupId,
                    Members = members,
                    TotalCount = state.Members.Count,
                    HasMore = request.Offset + limit < state.Members.Count
                };

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取群成员列表失败");
                throw;
            }
        }

        public Task<List<IMGroupChatNotifyMessage>> GetGroupChatHistoryAsync(int count, long beforeTimestamp = 0)
        {
            try
            {
                if (count <= 0) count = 50;
                if (count > 200) count = 200;

                var state = _groupState.State;
                var query = state.ChatHistory.AsEnumerable();

                if (beforeTimestamp > 0)
                    query = query.Where(r => r.Timestamp < beforeTimestamp);

                var messages = query
                    .OrderByDescending(r => r.Timestamp)
                    .Take(count)
                    .OrderBy(r => r.Timestamp)
                    .Select(r => new IMGroupChatNotifyMessage
                    {
                        ServerMessageId = r.ServerMessageId,
                        SenderId = r.SenderId,
                        SenderName = r.SenderName,
                        GroupName = state.GroupName,
                        Content = r.Content,
                        ContentType = r.ContentType,
                        Timestamp = r.Timestamp,
                        MentionedUserIds = r.MentionedUserIds,
                        MentionAll = r.MentionAll
                    })
                    .ToList();

                return Task.FromResult(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取群聊历史失败");
                throw;
            }
        }

        private static Guid UInt64ToGuid(ulong value)
        {
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, value);
            return new Guid(bytes);
        }

        private async Task PersistGroupChatRecordAsync(ulong groupId, string groupName, IMGroupChatRecord record)
        {
            await IMChatRedisOutbox.TryAppendGroupChatRecordAsync(_logger, groupId, groupName, record);
            ScheduleChatStateFlush(
                $"异步持久化群聊状态失败: GroupId={groupId}, ServerMessageId={record.ServerMessageId}");
        }

        private void ScheduleChatStateFlush(string operation)
        {
            _chatStateFlushPending = true;
            if (_chatStateFlushInProgress)
            {
                return;
            }

            _chatStateFlushInProgress = true;
            ObserveBackgroundTask(FlushChatStateAsync(), operation);
        }

        private async Task FlushChatStateAsync()
        {
            try
            {
                while (true)
                {
                    _chatStateFlushPending = false;
                    await _groupState.WriteStateAsync();

                    if (!_chatStateFlushPending)
                    {
                        break;
                    }
                }
            }
            catch
            {
                _chatStateFlushPending = true;
                throw;
            }
            finally
            {
                _chatStateFlushInProgress = false;
            }
        }

        private async Task DeliverGroupMessageAsync(IReadOnlyCollection<ulong> recipientIds, IMGroupChatNotifyMessage notify)
        {
            foreach (var memberId in recipientIds)
            {
                try
                {
                    var memberGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(memberId));
                    await memberGrain.ReceiveGroupMessageAsync(notify);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "投递群聊消息给成员失败，消息已存储: GroupId={GroupId}, MemberId={MemberId}, ServerMessageId={ServerMessageId}",
                        notify.GroupId,
                        memberId,
                        notify.ServerMessageId);
                }
            }
        }

        private void ObserveBackgroundTask(Task task, string operation)
        {
            _ = task.ContinueWith(
                continuation => _logger.LogWarning(continuation.Exception, "{Operation}", operation),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async Task NotifyUserAsync(ulong userId, IMMessageUnion message)
        {
            try
            {
                var userGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(userId));
                await userGrain.ReceiveGroupSystemMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "推送群组系统通知给用户失败: UserId={UserId}, Type={Type}", userId, message?.Type);
            }
        }

        private async Task NotifyAdminsAsync(IMGroupState state, IMMessageUnion message)
        {
            var targets = state.Members.Values
                .Where(m => m.Role == IMGroupMemberRole.Owner || m.Role == IMGroupMemberRole.Admin)
                .Select(m => m.UserId)
                .ToList();

            foreach (var adminId in targets)
            {
                await NotifyUserAsync(adminId, message);
            }
        }

        /// <summary>
        /// 将Guid确定性转换为ulong（用于Grain键映射）
        /// </summary>
        private static ulong GuidToUInt64(Guid value)
        {
            return BitConverter.ToUInt64(value.ToByteArray(), 0);
        }

        /// <summary>
        /// 清理已过期的待确认邀请及待审批邀请（超过3天）。
        /// </summary>
        private static void PurgeExpiredInvites(IMGroupState state)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // 使用 Keys 集合的快照避免修改迭代异常，同时减少中间分配
            foreach (var key in state.PendingInvites.Keys.ToArray())
            {
                if (state.PendingInvites.TryGetValue(key, out var invite)
                    && invite.InviteTime > 0
                    && (nowMs - invite.InviteTime) > InviteExpirationMs)
                {
                    state.PendingInvites.Remove(key);
                }
            }

            // 同样清理过期的待群主审批邀请
            foreach (var key in state.PendingInviteApprovals.Keys.ToArray())
            {
                if (state.PendingInviteApprovals.TryGetValue(key, out var approval)
                    && approval.RequestTime > 0
                    && (nowMs - approval.RequestTime) > InviteExpirationMs)
                {
                    state.PendingInviteApprovals.Remove(key);
                }
            }
        }
    }
}

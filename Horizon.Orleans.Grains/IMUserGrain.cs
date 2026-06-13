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
    /// IM用户Grain实现 - 管理用户的私聊、陌生人聊天、联系人、会话
    /// </summary>
    public class IMUserGrain : Grain, IIMUserGrain
    {
        private const int MaxMessageContentLength = 5000;
        private const int MaxSearchResultCount = 50;
        private const int RecallTimeWindowMinutes = 2;
        /// <summary>好友请求过期时间：3天（毫秒）。</summary>
        private const long ContactRequestExpirationMs = 3L * 24 * 60 * 60 * 1000;

        private readonly ILogger<IMUserGrain> _logger;
        private readonly IPersistentState<IMUserState> _userState;
        private readonly SensitiveWordFilter _sensitiveWordFilter;
        private readonly MessageRateLimiter _rateLimiter;
        private readonly Dictionary<Guid, IIMGatewayObserver> _gatewayObservers = new();
        private bool _stateFlushPending;
        private bool _stateFlushInProgress;

        public IMUserGrain(
            ILogger<IMUserGrain> logger,
            [PersistentState("imUser", "GameStore")] IPersistentState<IMUserState> userState)
        {
            _logger = logger;
            _userState = userState;
            _sensitiveWordFilter = new SensitiveWordFilter();
            _rateLimiter = new MessageRateLimiter(maxMessagesPerWindow: 30, windowSeconds: 60);
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("IMUserGrain {GrainKey} 正在激活", this.GetPrimaryKey());

            var state = _userState.State;
            state.UserId = GuidToUInt64(this.GetPrimaryKey());
            state.Contacts ??= new Dictionary<ulong, IMContactEntry>();
            state.PendingContactRequests ??= new Dictionary<ulong, IMPendingContactRequest>();
            state.BlockedUsers ??= new HashSet<ulong>();
            state.Conversations ??= new Dictionary<string, IMConversationEntry>();
            state.PrivateChatHistory ??= new Dictionary<ulong, List<IMChatRecord>>();
            state.StrangerChatHistory ??= new Dictionary<ulong, List<IMChatRecord>>();
            state.PendingStrangerRequests ??= new Dictionary<ulong, IMStrangerChatRequestEntry>();
            state.ContactGroups ??= new Dictionary<string, int>();
            // 反序列化时 Dictionary 的自定义比较器不会被保留，需在激活时重建以保持大小写不敏感。
            // 若历史脏数据中存在仅大小写不同的重复键（comparer 丢失时可能写入），
            // 直接使用拷贝构造函数会抛出 ArgumentException 导致 Grain 激活失败。
            // 此处逐条插入并在冲突时保留先出现的条目并记录警告，避免因脏数据卡死用户粒子。
            var rebuiltGroupNames = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            if (state.OwnedGroupNames != null)
            {
                foreach (var kv in state.OwnedGroupNames)
                {
                    if (!rebuiltGroupNames.TryAdd(kv.Key, kv.Value))
                    {
                        _logger.LogWarning(
                            "OwnedGroupNames 存在大小写重复键，已丢弃后出现的条目: UserId={UserId}, Key={Key}, DiscardedGroupId={DiscardedGroupId}",
                            state.UserId, kv.Key, kv.Value);
                    }
                }
            }
            state.OwnedGroupNames = rebuiltGroupNames;

            await base.OnActivateAsync(cancellationToken);
        }

        #region 私聊/熟人聊天

        public async Task<string> SendPrivateMessageAsync(IMPrivateChatSendMessage message)
        {
            try
            {
                _logger.LogInformation("发送私聊消息: SenderId={SenderId}, ReceiverId={ReceiverId}",
                    message.SenderId, message.ReceiverId);

                if (!_userState.State.Contacts.ContainsKey(message.ReceiverId))
                {
                    _logger.LogWarning("目标用户不在联系人列表中: ReceiverId={ReceiverId}", message.ReceiverId);
                    return "";
                }

                if (_userState.State.BlockedUsers.Contains(message.ReceiverId))
                {
                    _logger.LogWarning("目标用户已被屏蔽: ReceiverId={ReceiverId}", message.ReceiverId);
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

                var record = new IMChatRecord
                {
                    ServerMessageId = serverMessageId,
                    ClientMessageId = message.ClientMessageId ?? "",
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    ReceiverId = message.ReceiverId,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp,
                    Status = IMMessageStatus.Sent
                };

                AddPrivateChatRecord(message.ReceiverId, record);

                _userState.State.Contacts.TryGetValue(message.ReceiverId, out var contact);
                UpdateConversation(
                    $"p_{message.ReceiverId}",
                    IMChatRelationType.Friend,
                    message.ReceiverId,
                    contact?.Nickname ?? "",
                    contact?.Avatar ?? "",
                    filteredContent,
                    timestamp,
                    incrementUnread: false);

                _rateLimiter.RecordMessage(senderId);
                await PersistUserChatRecordAsync(
                    IMChatRelationType.Friend,
                    message.ReceiverId,
                    $"p_{message.ReceiverId}",
                    record);

                // 投递消息给接收方
                var notify = new IMPrivateChatNotifyMessage
                {
                    ServerMessageId = serverMessageId,
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    SenderAvatar = message.SenderAvatar,
                    ReceiverId = message.ReceiverId,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp
                };

                try
                {
                    var receiverGuid = UInt64ToGuid(message.ReceiverId);
                    var receiverGrain = GrainFactory.GetGrain<IIMUserGrain>(receiverGuid);
                    ObserveBackgroundTask(
                        receiverGrain.ReceivePrivateMessageAsync(notify),
                        $"投递私聊消息给接收方失败: ReceiverId={message.ReceiverId}, ServerMessageId={serverMessageId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "投递私聊消息给接收方失败，消息已存储: ReceiverId={ReceiverId}", message.ReceiverId);
                }

                return serverMessageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送私聊消息失败");
                throw;
            }
        }

        public async Task<bool> ReceivePrivateMessageAsync(IMPrivateChatNotifyMessage notify)
        {
            try
            {
                _logger.LogInformation("接收私聊消息: ServerMessageId={ServerMessageId}, SenderId={SenderId}",
                    notify.ServerMessageId, notify.SenderId);

                var record = new IMChatRecord
                {
                    ServerMessageId = notify.ServerMessageId,
                    ClientMessageId = "",
                    SenderId = notify.SenderId,
                    SenderName = notify.SenderName,
                    ReceiverId = notify.ReceiverId,
                    Content = notify.Content,
                    ContentType = notify.ContentType,
                    Timestamp = notify.Timestamp,
                    Status = IMMessageStatus.Delivered
                };

                AddPrivateChatRecord(notify.SenderId, record);

                UpdateConversation(
                    $"p_{notify.SenderId}",
                    IMChatRelationType.Friend,
                    notify.SenderId,
                    notify.SenderName,
                    notify.SenderAvatar,
                    notify.Content,
                    notify.Timestamp,
                    incrementUnread: true);

                await PersistUserChatRecordAsync(
                    IMChatRelationType.Friend,
                    notify.SenderId,
                    $"p_{notify.SenderId}",
                    record);
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(notify),
                    $"推送私聊消息到网关订阅失败: ReceiverId={notify.ReceiverId}, ServerMessageId={notify.ServerMessageId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收私聊消息失败");
                throw;
            }
        }

        public async Task<bool> ReceiveGroupMessageAsync(IMGroupChatNotifyMessage notify)
        {
            try
            {
                _logger.LogInformation(
                    "接收群聊消息推送: ServerMessageId={ServerMessageId}, GroupId={GroupId}, SenderId={SenderId}",
                    notify.ServerMessageId,
                    notify.GroupId,
                    notify.SenderId);

                UpdateConversation(
                    $"g_{notify.GroupId}",
                    IMChatRelationType.Group,
                    notify.GroupId,
                    notify.GroupName,
                    string.Empty,
                    notify.Content,
                    notify.Timestamp,
                    incrementUnread: notify.SenderId != _userState.State.UserId);

                ScheduleStateFlush(
                    $"异步持久化群会话摘要失败: UserId={_userState.State.UserId}, GroupId={notify.GroupId}, ServerMessageId={notify.ServerMessageId}");
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(notify),
                    $"推送群聊消息到网关订阅失败: GroupId={notify.GroupId}, ServerMessageId={notify.ServerMessageId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收群聊消息推送失败");
                throw;
            }
        }

        public Task ReceiveGroupSystemMessageAsync(IMMessageUnion message)
        {
            try
            {
                _logger.LogInformation("接收群组系统通知: UserId={UserId}, Type={Type}",
                    _userState.State.UserId, message?.Type);
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(message),
                    $"推送群组系统通知失败: UserId={_userState.State.UserId}, Type={message?.Type}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收群组系统通知失败");
                throw;
            }
        }


        public async Task<bool> ProcessChatAckAsync(IMChatAckMessage ack)
        {
            try
            {
                _logger.LogInformation("处理消息回执: AckedMessageId={AckedMessageId}, Status={Status}",
                    ack.AckedMessageId, ack.Status);

                var updated = UpdateMessageStatus(_userState.State.PrivateChatHistory, ack.AckedMessageId, ack.Status);
                if (!updated)
                    updated = UpdateMessageStatus(_userState.State.StrangerChatHistory, ack.AckedMessageId, ack.Status);

                if (updated)
                    ScheduleStateFlush(
                        $"异步持久化消息回执失败: UserId={_userState.State.UserId}, AckedMessageId={ack.AckedMessageId}, Status={ack.Status}");

                return updated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息回执失败");
                throw;
            }
        }

        public async Task<bool> RecallMessageAsync(IMChatRecallMessage recall)
        {
            try
            {
                _logger.LogInformation("撤回消息: RecalledMessageId={RecalledMessageId}", recall.RecalledMessageId);

                var found = FindChatRecord(_userState.State.PrivateChatHistory, recall.RecalledMessageId);
                found ??= FindChatRecord(_userState.State.StrangerChatHistory, recall.RecalledMessageId);

                if (found == null)
                {
                    _logger.LogWarning("消息不存在: RecalledMessageId={RecalledMessageId}", recall.RecalledMessageId);
                    return false;
                }

                if (found.SenderId != recall.UserId)
                {
                    _logger.LogWarning("只能撤回自己发送的消息: UserId={UserId}, SenderId={SenderId}",
                        recall.UserId, found.SenderId);
                    return false;
                }

                var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - found.Timestamp;
                if (elapsed > RecallTimeWindowMinutes * 60 * 1000)
                {
                    _logger.LogWarning("超过撤回时间窗口: Elapsed={Elapsed}ms", elapsed);
                    return false;
                }

                found.Status = IMMessageStatus.Recalled;
                found.Content = "[消息已撤回]";
                ScheduleStateFlush(
                    $"异步持久化消息撤回失败: UserId={_userState.State.UserId}, RecalledMessageId={recall.RecalledMessageId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "撤回消息失败");
                throw;
            }
        }

        public async Task<bool> SendReadReceiptAsync(IMChatReadReceiptMessage receipt)
        {
            try
            {
                _logger.LogInformation("发送已读回执: PeerId={PeerId}", receipt.PeerId);

                if (_userState.State.PrivateChatHistory.TryGetValue(receipt.PeerId, out var history))
                {
                    foreach (var record in history.Where(r => r.SenderId == receipt.PeerId && r.Status < IMMessageStatus.Read))
                    {
                        record.Status = IMMessageStatus.Read;
                    }
                }

                var convId = $"p_{receipt.PeerId}";
                if (_userState.State.Conversations.TryGetValue(convId, out var conv))
                {
                    conv.UnreadCount = 0;
                }

                ScheduleStateFlush(
                    $"异步持久化已读回执失败: UserId={_userState.State.UserId}, PeerId={receipt.PeerId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送已读回执失败");
                throw;
            }
        }

        #endregion

        #region 陌生人聊天

        public async Task<IMStrangerChatResponse> RequestStrangerChatAsync(IMStrangerChatRequest request)
        {
            try
            {
                _logger.LogInformation("发起陌生人聊天请求: SenderId={SenderId}, TargetUserId={TargetUserId}",
                    request.SenderId, request.TargetUserId);

                var response = new IMStrangerChatResponse
                {
                    SenderId = request.SenderId,
                    TargetUserId = request.TargetUserId,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // 规则1：发送方必须已实名认证
                if (_userState.State.VerificationStatus == IdentityVerificationStatus.Unverified)
                {
                    _logger.LogWarning("未实名认证，不允许向陌生人发起聊天请求: SenderId={SenderId}", request.SenderId);
                    response.IsAllowed = false;
                    response.DeniedReason = StrangerChatDeniedReason.NotVerified;
                    return response;
                }

                // 规则2：发送方风险等级必须为Normal
                if (_userState.State.RiskLevel != UserRiskLevel.Normal)
                {
                    _logger.LogWarning("风险用户不允许发起陌生人聊天请求: SenderId={SenderId}, RiskLevel={RiskLevel}",
                        request.SenderId, _userState.State.RiskLevel);
                    response.IsAllowed = false;
                    response.DeniedReason = _userState.State.RiskLevel switch
                    {
                        UserRiskLevel.Dishonest => StrangerChatDeniedReason.SenderDishonest,
                        UserRiskLevel.FraudSuspect => StrangerChatDeniedReason.SenderFraudSuspect,
                        UserRiskLevel.Criminal => StrangerChatDeniedReason.SenderCriminal,
                        _ => StrangerChatDeniedReason.SenderDishonest
                    };
                    return response;
                }

                var senderId = (long)request.SenderId;
                if (_rateLimiter.IsRateLimited(senderId))
                {
                    _logger.LogWarning("陌生人聊天请求频率超限: SenderId={SenderId}", request.SenderId);
                    response.IsAllowed = false;
                    response.DeniedReason = StrangerChatDeniedReason.RateLimited;
                    return response;
                }

                var filteredGreetingMessage = string.IsNullOrWhiteSpace(request.GreetingMessage)
                    ? string.Empty
                    : _sensitiveWordFilter.FilterText(request.GreetingMessage) ?? string.Empty;

                var sanitizedRequest = new IMStrangerChatRequest
                {
                    SenderId = request.SenderId,
                    SenderName = request.SenderName,
                    SenderAvatar = request.SenderAvatar,
                    TargetUserId = request.TargetUserId,
                    SenderVerificationStatus = request.SenderVerificationStatus,
                    SenderRiskLevel = request.SenderRiskLevel,
                    GreetingMessage = filteredGreetingMessage,
                    Timestamp = request.Timestamp
                };

                // 检查目标用户是否允许陌生人消息
                try
                {
                    var targetGuid = UInt64ToGuid(request.TargetUserId);
                    var targetGrain = GrainFactory.GetGrain<IIMUserGrain>(targetGuid);

                    var targetAllowStranger = await targetGrain.GetAllowStrangerMessageAsync();
                    if (!targetAllowStranger)
                    {
                        response.IsAllowed = false;
                        response.DeniedReason = StrangerChatDeniedReason.ReceiverDisabledStranger;
                        return response;
                    }

                    await targetGrain.ReceiveStrangerChatRequestAsync(sanitizedRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "检查目标用户状态失败: TargetUserId={TargetUserId}", request.TargetUserId);
                    response.IsAllowed = false;
                    response.DeniedReason = StrangerChatDeniedReason.ReceiverNotFound;
                    return response;
                }

                _rateLimiter.RecordMessage(senderId);

                response.IsAllowed = true;
                response.SessionId = Guid.NewGuid().ToString("N");
                response.DeniedReason = StrangerChatDeniedReason.None;
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发起陌生人聊天请求失败");
                throw;
            }
        }

        public async Task<bool> ReceiveStrangerChatRequestAsync(IMStrangerChatRequest request)
        {
            try
            {
                _logger.LogInformation("接收陌生人聊天请求: SenderId={SenderId}", request.SenderId);

                if (_userState.State.BlockedUsers.Contains(request.SenderId))
                {
                    _logger.LogInformation("发送者已被屏蔽: SenderId={SenderId}", request.SenderId);
                    return false;
                }

                _userState.State.PendingStrangerRequests[request.SenderId] = new IMStrangerChatRequestEntry
                {
                    RequesterId = request.SenderId,
                    RequesterName = request.SenderName,
                    GreetingMessage = request.GreetingMessage ?? string.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Accepted = false
                };

                ScheduleStateFlush(
                    $"异步持久化陌生人请求失败: UserId={_userState.State.UserId}, RequesterId={request.SenderId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收陌生人聊天请求失败");
                throw;
            }
        }

        public async Task<string> SendStrangerMessageAsync(IMStrangerChatSendMessage message)
        {
            try
            {
                _logger.LogInformation("发送陌生人消息: SenderId={SenderId}, ReceiverId={ReceiverId}",
                    message.SenderId, message.ReceiverId);

                if (_userState.State.VerificationStatus == IdentityVerificationStatus.Unverified)
                {
                    _logger.LogWarning("未实名认证，不允许发送陌生人消息");
                    return "";
                }

                if (_userState.State.RiskLevel != UserRiskLevel.Normal)
                {
                    _logger.LogWarning("风险用户不允许发送陌生人消息: RiskLevel={RiskLevel}", _userState.State.RiskLevel);
                    return "";
                }

                // TODO(安全): 需要对 SessionId 进行完整的会话合法性验证（对照持久化会话表），
                //             当前仅做空值基本校验，防止完全无会话的匿名调用。
                if (string.IsNullOrEmpty(message.SessionId))
                {
                    _logger.LogWarning("陌生人消息缺少有效的 SessionId，拒绝发送: SenderId={SenderId}", message.SenderId);
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
                    _logger.LogWarning("消息发送频率超限");
                    return "";
                }

                var filteredContent = _sensitiveWordFilter.FilterText(message.Content);
                var serverMessageId = Guid.NewGuid().ToString("N");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var record = new IMChatRecord
                {
                    ServerMessageId = serverMessageId,
                    ClientMessageId = message.ClientMessageId ?? "",
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    ReceiverId = message.ReceiverId,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp,
                    Status = IMMessageStatus.Sent
                };

                AddStrangerChatRecord(message.ReceiverId, record);

                UpdateConversation(
                    $"s_{message.ReceiverId}",
                    IMChatRelationType.Stranger,
                    message.ReceiverId,
                    "",
                    "",
                    filteredContent,
                    timestamp,
                    incrementUnread: false);

                _rateLimiter.RecordMessage(senderId);
                await PersistUserChatRecordAsync(
                    IMChatRelationType.Stranger,
                    message.ReceiverId,
                    $"s_{message.ReceiverId}",
                    record);

                var notify = new IMStrangerChatNotifyMessage
                {
                    ServerMessageId = serverMessageId,
                    SenderId = message.SenderId,
                    SenderName = message.SenderName,
                    SenderAvatar = message.SenderAvatar,
                    ReceiverId = message.ReceiverId,
                    Content = filteredContent,
                    ContentType = message.ContentType,
                    Timestamp = timestamp
                };

                try
                {
                    var receiverGuid = UInt64ToGuid(message.ReceiverId);
                    var receiverGrain = GrainFactory.GetGrain<IIMUserGrain>(receiverGuid);
                    ObserveBackgroundTask(
                        receiverGrain.ReceiveStrangerMessageAsync(notify),
                        $"投递陌生人消息给接收方失败: ReceiverId={message.ReceiverId}, ServerMessageId={serverMessageId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "投递陌生人消息给接收方失败: ReceiverId={ReceiverId}", message.ReceiverId);
                }

                return serverMessageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送陌生人消息失败");
                throw;
            }
        }

        public async Task<bool> ReceiveStrangerMessageAsync(IMStrangerChatNotifyMessage notify)
        {
            try
            {
                _logger.LogInformation("接收陌生人消息: SenderId={SenderId}", notify.SenderId);

                if (_userState.State.BlockedUsers.Contains(notify.SenderId))
                    return false;

                var record = new IMChatRecord
                {
                    ServerMessageId = notify.ServerMessageId,
                    ClientMessageId = "",
                    SenderId = notify.SenderId,
                    SenderName = notify.SenderName,
                    ReceiverId = notify.ReceiverId,
                    Content = notify.Content,
                    ContentType = notify.ContentType,
                    Timestamp = notify.Timestamp,
                    Status = IMMessageStatus.Delivered
                };

                AddStrangerChatRecord(notify.SenderId, record);

                UpdateConversation(
                    $"s_{notify.SenderId}",
                    IMChatRelationType.Stranger,
                    notify.SenderId,
                    notify.SenderName,
                    notify.SenderAvatar,
                    notify.Content,
                    notify.Timestamp,
                    incrementUnread: true);

                await PersistUserChatRecordAsync(
                    IMChatRelationType.Stranger,
                    notify.SenderId,
                    $"s_{notify.SenderId}",
                    record);
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(notify),
                    $"推送陌生人消息到网关订阅失败: ReceiverId={notify.ReceiverId}, ServerMessageId={notify.ServerMessageId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收陌生人消息失败");
                throw;
            }
        }

        public Task<IdentityVerificationStatus> GetVerificationStatusAsync()
            => Task.FromResult(_userState.State.VerificationStatus);

        public async Task SetVerificationStatusAsync(IdentityVerificationStatus status)
        {
            _userState.State.VerificationStatus = status;
                ScheduleStateFlush(
                    $"异步持久化实名认证状态失败: UserId={_userState.State.UserId}, VerificationStatus={status}");
        }

        public Task<UserRiskLevel> GetRiskLevelAsync()
            => Task.FromResult(_userState.State.RiskLevel);

        public async Task SetRiskLevelAsync(UserRiskLevel level)
        {
            _userState.State.RiskLevel = level;
            ScheduleStateFlush(
                $"异步持久化风险等级失败: UserId={_userState.State.UserId}, RiskLevel={level}");
        }

        public Task<bool> GetAllowStrangerMessageAsync()
            => Task.FromResult(_userState.State.AllowStrangerMessage);

        public async Task SetAllowStrangerMessageAsync(bool allow)
        {
            _userState.State.AllowStrangerMessage = allow;
            ScheduleStateFlush(
                $"异步持久化陌生人消息开关失败: UserId={_userState.State.UserId}, Allow={allow}");
        }

        #endregion

        #region 联系人管理

        public async Task<IMContactAddResponse> AddContactAsync(IMContactAddRequest request)
        {
            try
            {
                _logger.LogInformation("添加联系人请求: UserId={UserId}, TargetUserId={TargetUserId}",
                    request.UserId, request.TargetUserId);

                var response = new IMContactAddResponse
                {
                    UserId = request.UserId,
                    TargetUserId = request.TargetUserId
                };

                var state = _userState.State;

                if (request.UserId == request.TargetUserId)
                {
                    response.Success = false;
                    response.Message = "不能添加自己为好友";
                    return response;
                }

                if (state.Contacts.ContainsKey(request.TargetUserId))
                {
                    response.Success = false;
                    response.Message = "该用户已是好友";
                    response.Relation = IMContactRelation.Friend;
                    return response;
                }

                if (state.Contacts.Count >= state.MaxContacts)
                {
                    response.Success = false;
                    response.Message = "联系人数量已达上限";
                    return response;
                }

                var filteredVerifyMessage = string.IsNullOrWhiteSpace(request.VerifyMessage)
                    ? string.Empty
                    : _sensitiveWordFilter.FilterText(request.VerifyMessage) ?? string.Empty;

                var sanitizedRequest = new IMContactAddRequest
                {
                    UserId = request.UserId,
                    TargetUserId = request.TargetUserId,
                    VerifyMessage = filteredVerifyMessage,
                    RemarkName = request.RemarkName,
                    Source = request.Source,
                    RequesterName = string.IsNullOrWhiteSpace(request.RequesterName)
                        ? string.IsNullOrWhiteSpace(state.Nickname) ? state.UserId.ToString() : state.Nickname
                        : request.RequesterName,
                    RequesterAvatar = string.IsNullOrWhiteSpace(request.RequesterAvatar)
                        ? state.Avatar ?? string.Empty
                        : request.RequesterAvatar
                };

                try
                {
                    var targetGuid = UInt64ToGuid(request.TargetUserId);
                    var targetGrain = GrainFactory.GetGrain<IIMUserGrain>(targetGuid);
                    await targetGrain.ReceiveContactRequestAsync(sanitizedRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "发送好友请求失败: TargetUserId={TargetUserId}", request.TargetUserId);
                    response.Success = false;
                    response.Message = "目标用户不存在";
                    return response;
                }

                response.Success = true;
                response.Relation = IMContactRelation.PendingRequest;
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加联系人失败");
                throw;
            }
        }

        public async Task<bool> ReceiveContactRequestAsync(IMContactAddRequest request)
        {
            try
            {
                _logger.LogInformation("接收好友请求: RequesterId={RequesterId}", request.UserId);

                var state = _userState.State;

                if (state.BlockedUsers.Contains(request.UserId))
                {
                    _logger.LogInformation("请求者已被屏蔽: RequesterId={RequesterId}", request.UserId);
                    return false;
                }

                if (state.Contacts.ContainsKey(request.UserId))
                {
                    _logger.LogInformation("请求者已是好友: RequesterId={RequesterId}", request.UserId);
                    return false;
                }

                state.PendingContactRequests[request.UserId] = new IMPendingContactRequest
                {
                    RequesterId = request.UserId,
                    RequesterName = string.IsNullOrWhiteSpace(request.RequesterName)
                        ? request.UserId.ToString()
                        : request.RequesterName,
                    Message = request.VerifyMessage ?? string.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                ScheduleStateFlush(
                    $"异步持久化待处理好友申请失败: UserId={state.UserId}, RequesterId={request.UserId}");
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(CreatePendingContactRequestNotification(
                        state.UserId,
                        state.PendingContactRequests[request.UserId].RequesterName,
                        state.PendingContactRequests[request.UserId].Message)),
                    $"推送好友申请通知失败: RequesterId={request.UserId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收好友请求失败");
                throw;
            }
        }

        public async Task<bool> HandleContactRequestAsync(ulong requesterId, bool accept)
        {
            try
            {
                _logger.LogInformation("处理好友请求: RequesterId={RequesterId}, Accept={Accept}", requesterId, accept);

                var state = _userState.State;

                if (!state.PendingContactRequests.TryGetValue(requesterId, out var request))
                {
                    _logger.LogWarning("好友请求不存在: RequesterId={RequesterId}", requesterId);
                    return false;
                }

                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (request.Timestamp > 0 && (nowMs - request.Timestamp) > ContactRequestExpirationMs)
                {
                    state.PendingContactRequests.Remove(requesterId);
                    ScheduleStateFlush(
                        $"异步持久化清理过期好友申请失败: UserId={state.UserId}, RequesterId={requesterId}");
                    _logger.LogInformation("好友请求已过期: RequesterId={RequesterId}", requesterId);
                    return false;
                }

                if (accept)
                {
                    if (state.Contacts.ContainsKey(requesterId))
                    {
                        state.PendingContactRequests.Remove(requesterId);
                        ScheduleStateFlush(
                            $"异步持久化处理好友申请失败: UserId={state.UserId}, RequesterId={requesterId}, Accept={accept}");
                        _logger.LogInformation("对方已是好友，跳过重复添加: RequesterId={RequesterId}", requesterId);
                        return true;
                    }

                    if (state.Contacts.Count >= state.MaxContacts)
                    {
                        _logger.LogWarning("联系人数量已达上限");
                        return false;
                    }

                    string requesterNickname = requesterId.ToString();
                    try
                    {
                        var requesterGuid = UInt64ToGuid(requesterId);
                        var requesterGrain = GrainFactory.GetGrain<IIMUserGrain>(requesterGuid);
                        var nickname = await requesterGrain.GetNicknameAsync();
                        if (!string.IsNullOrWhiteSpace(nickname))
                            requesterNickname = nickname;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "获取请求者昵称失败，使用回退值: RequesterId={RequesterId}", requesterId);
                    }

                    state.Contacts[requesterId] = new IMContactEntry
                    {
                        UserId = requesterId,
                        Nickname = requesterNickname,
                        Relation = IMContactRelation.Friend,
                        AddTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    state.PendingContactRequests.Remove(requesterId);

                    ScheduleStateFlush(
                        $"异步持久化处理好友申请失败: UserId={state.UserId}, RequesterId={requesterId}, Accept={accept}");

                    ObserveBackgroundTask(
                        NotifyGatewayObserversAsync(CreateRosterChangedNotification(
                            state.UserId,
                            "好友列表已更新",
                            $"{requesterNickname} 已加入你的好友列表",
                            priority: 1)),
                        $"推送接受者联系人变更通知失败: UserId={state.UserId}, RequesterId={requesterId}");

                    var currentDisplayName = string.IsNullOrWhiteSpace(state.Nickname)
                        ? state.UserId.ToString()
                        : state.Nickname;

                    try
                    {
                        var requesterGuid = UInt64ToGuid(requesterId);
                        var requesterGrain = GrainFactory.GetGrain<IIMUserGrain>(requesterGuid);
                        ObserveBackgroundTask(
                            requesterGrain.OnContactAddedAsync(state.UserId, currentDisplayName, state.OnlineStatus),
                            $"通知对方好友添加失败: UserId={state.UserId}, RequesterId={requesterId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "初始化对方好友通知失败: RequesterId={RequesterId}", requesterId);
                    }
                }
                else
                {
                    state.PendingContactRequests.Remove(requesterId);

                    ScheduleStateFlush(
                        $"异步持久化处理好友申请失败: UserId={state.UserId}, RequesterId={requesterId}, Accept={accept}");

                    var rejectDisplayName = string.IsNullOrWhiteSpace(state.Nickname)
                        ? state.UserId.ToString()
                        : state.Nickname;

                    try
                    {
                        var requesterGuid = UInt64ToGuid(requesterId);
                        var requesterGrain = GrainFactory.GetGrain<IIMUserGrain>(requesterGuid);
                        ObserveBackgroundTask(
                            requesterGrain.OnContactRequestRejectedAsync(state.UserId, rejectDisplayName),
                            $"通知对方好友申请被拒绝失败: UserId={state.UserId}, RequesterId={requesterId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "初始化对方拒绝通知失败: RequesterId={RequesterId}", requesterId);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理好友请求失败");
                throw;
            }
        }

        public async Task<IMContactRemoveResponse> RemoveContactAsync(IMContactRemoveRequest request)
        {
            try
            {
                _logger.LogInformation("删除联系人: UserId={UserId}, TargetUserId={TargetUserId}",
                    request.UserId, request.TargetUserId);

                var response = new IMContactRemoveResponse
                {
                    TargetUserId = request.TargetUserId
                };

                if (!_userState.State.Contacts.Remove(request.TargetUserId))
                {
                    response.Success = false;
                    response.Message = "联系人不存在";
                    return response;
                }

                ScheduleStateFlush(
                    $"异步持久化删除联系人失败: UserId={_userState.State.UserId}, TargetUserId={request.TargetUserId}");

                try
                {
                    var targetGuid = UInt64ToGuid(request.TargetUserId);
                    var targetGrain = GrainFactory.GetGrain<IIMUserGrain>(targetGuid);
                    ObserveBackgroundTask(
                        targetGrain.OnContactRemovedAsync(request.UserId),
                        $"通知对方联系人删除失败: TargetUserId={request.TargetUserId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "通知对方联系人删除失败: TargetUserId={TargetUserId}", request.TargetUserId);
                }

                response.Success = true;
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除联系人失败");
                throw;
            }
        }

        public async Task<IMContactBlockResponse> BlockContactAsync(IMContactBlockRequest request)
        {
            try
            {
                _logger.LogInformation("屏蔽联系人: UserId={UserId}, TargetUserId={TargetUserId}",
                    request.UserId, request.TargetUserId);

                var response = new IMContactBlockResponse
                {
                    TargetUserId = request.TargetUserId
                };

                var state = _userState.State;

                if (request.IsBlock)
                {
                    state.BlockedUsers.Add(request.TargetUserId);

                    if (state.Contacts.TryGetValue(request.TargetUserId, out var contact))
                        contact.Relation = IMContactRelation.Blocked;

                    response.Relation = IMContactRelation.Blocked;
                }
                else
                {
                    state.BlockedUsers.Remove(request.TargetUserId);

                    if (state.Contacts.TryGetValue(request.TargetUserId, out var contact))
                        contact.Relation = IMContactRelation.Friend;

                    response.Relation = IMContactRelation.Friend;
                }

                ScheduleStateFlush(
                    $"异步持久化屏蔽联系人失败: UserId={state.UserId}, TargetUserId={request.TargetUserId}, IsBlock={request.IsBlock}");
                response.Success = true;
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "屏蔽联系人失败");
                throw;
            }
        }

        public Task<IMPendingContactRequestListResponse> GetPendingContactRequestsAsync(IMPendingContactRequestListRequest request)
        {
            var limit = request.Limit > 0 ? request.Limit : 50;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 过滤掉已过期的好友请求（超过3天）
            var pending = _userState.State.PendingContactRequests.Values
                .Where(x => x.Timestamp <= 0 || (nowMs - x.Timestamp) <= ContactRequestExpirationMs)
                .OrderByDescending(x => x.Timestamp)
                .Skip(request.Offset)
                .Take(limit)
                .ToList();

            var response = new IMPendingContactRequestListResponse
            {
                PendingRequests = pending
            };

            return Task.FromResult(response);
        }

        public Task<IMGetPendingGroupInvitesResponse> GetPendingGroupInvitesAsync(IMGetPendingGroupInvitesRequest request)
        {
            var limit = request.Limit > 0 ? request.Limit : 50;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var state = _userState.State;
            state.PendingGroupInvites ??= new Dictionary<ulong, IMUserPendingGroupInviteEntry>();

            // 过滤无效时间戳和已过期邀请（超过3天），按时间倒序返回
            var pending = state.PendingGroupInvites.Values
                .Where(x => x.Timestamp > 0 && (nowMs - x.Timestamp) <= ContactRequestExpirationMs)
                .OrderByDescending(x => x.Timestamp)
                .Skip(request.Offset)
                .Take(limit)
                .ToList();

            // RequiresConsent=false 的条目属于一次性直接加入通知，返回给客户端后即可清除。
            // 客户端收到后刷新群组列表即可，无需用户主动确认，不必保留在待处理队列中。
            var directAddIds = pending
                .Where(x => !x.RequiresConsent)
                .Select(x => x.GroupId)
                .ToList();
            if (directAddIds.Count > 0)
            {
                foreach (var gid in directAddIds)
                {
                    state.PendingGroupInvites.Remove(gid);
                }
                ScheduleStateFlush(
                    $"异步持久化移除直接加入通知失败: UserId={state.UserId}");
            }

            return Task.FromResult(new IMGetPendingGroupInvitesResponse { PendingInvites = pending });
        }

        public Task AddPendingGroupInviteAsync(ulong groupId, string groupName, ulong inviterId, string inviterName, long timestamp, bool requiresConsent = true)
        {
            var state = _userState.State;
            state.PendingGroupInvites ??= new Dictionary<ulong, IMUserPendingGroupInviteEntry>();
            state.PendingGroupInvites[groupId] = new IMUserPendingGroupInviteEntry
            {
                GroupId = groupId,
                GroupName = groupName,
                InviterId = inviterId,
                InviterName = inviterName,
                Timestamp = timestamp,
                RequiresConsent = requiresConsent
            };
            ScheduleStateFlush(
                $"异步持久化待处理入群邀请失败: UserId={state.UserId}, GroupId={groupId}");
            return Task.CompletedTask;
        }

        public Task RemovePendingGroupInviteAsync(ulong groupId)
        {
            var state = _userState.State;
            if (state.PendingGroupInvites != null && state.PendingGroupInvites.Remove(groupId))
            {
                ScheduleStateFlush(
                    $"异步持久化移除入群邀请失败: UserId={state.UserId}, GroupId={groupId}");
            }
            return Task.CompletedTask;
        }

        public async Task<bool> CheckAndRegisterGroupNameAsync(string groupName, ulong groupId)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return false;
            }

            var state = _userState.State;
            // 不区分大小写：避免因大小写不同（如 "MyGroup" 与 "mygroup"）绕过重名检查
            state.OwnedGroupNames ??= new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

            // 若该用户已拥有同名群组（且 ID 不同），需进一步确认旧群是否仍活跃
            if (state.OwnedGroupNames.TryGetValue(groupName, out var existingId) && existingId != groupId)
            {
                // 向旧群 Grain 查询状态：若已解散则清理过期注册，允许以相同名称创建新群
                try
                {
                    var existingGroupGrain = GrainFactory.GetGrain<IIMGroupGrain>(UInt64ToGuid(existingId));
                    var groupInfo = await existingGroupGrain.GetGroupInfoAsync();
                    if (!groupInfo.IsDisbanded)
                    {
                        // 活跃群组存在同名，拒绝注册
                        return false;
                    }
                    // 已解散，移除过期注册，继续向下注册新群名
                    state.OwnedGroupNames.Remove(groupName);
                }
                catch (Exception ex)
                {
                    // 查询失败时保守拒绝，防止意外覆盖活跃群组
                    _logger.LogWarning(ex, "查询已注册同名群组状态失败，拒绝重名注册: UserId={UserId}, GroupName={GroupName}, ExistingGroupId={ExistingGroupId}",
                        state.UserId, groupName, existingId);
                    return false;
                }
            }

            state.OwnedGroupNames[groupName] = groupId;
            ScheduleStateFlush(
                $"异步持久化群名注册失败: UserId={state.UserId}, GroupName={groupName}, GroupId={groupId}");
            return true;
        }

        public Task UnregisterOwnedGroupNameAsync(ulong groupId)
        {
            var state = _userState.State;
            if (state.OwnedGroupNames == null)
            {
                return Task.CompletedTask;
            }

            // 查找并移除对应 groupId 的群名注册
            var key = state.OwnedGroupNames
                .FirstOrDefault(kv => kv.Value == groupId).Key;
            if (!string.IsNullOrEmpty(key))
            {
                state.OwnedGroupNames.Remove(key);
                ScheduleStateFlush(
                    $"异步持久化注销群名失败: UserId={state.UserId}, GroupId={groupId}");
            }

            return Task.CompletedTask;
        }

        public Task<IMContactListResponse> GetContactListAsync(IMContactListRequest request)
        {
            try
            {
                var state = _userState.State;
                var limit = request.Limit > 0 ? request.Limit : 50;

                var contacts = state.Contacts.Values
                    .Where(c => c.Relation == IMContactRelation.Friend)
                    .OrderByDescending(c => c.AddTime)
                    .Skip(request.Offset)
                    .Take(limit)
                    .Select(c => new IMContactInfo
                    {
                        UserId = c.UserId,
                        Nickname = c.Nickname,
                        Avatar = c.Avatar,
                        RemarkName = c.Remark,
                        Relation = c.Relation,
                        OnlineStatus = c.OnlineStatus,
                        Signature = c.Bio,
                        GroupName = c.GroupName
                    })
                    .ToList();

                var totalFriends = state.Contacts.Count(c => c.Value.Relation == IMContactRelation.Friend);

                var response = new IMContactListResponse
                {
                    Contacts = contacts,
                    TotalCount = totalFriends,
                    HasMore = request.Offset + limit < totalFriends
                };

                // 在后台实时刷新"在线"联系人的状态，修正持久化快照中可能存在的过时在线标记，
                // 并通过 Observer 推送纠正通知，确保客户端展示准确。
                var onlineContactIds = state.Contacts.Values
                    .Where(c => c.Relation == IMContactRelation.Friend
                        && c.OnlineStatus != IMOnlineStatus.Offline)
                    .Select(c => c.UserId)
                    .ToList();

                if (onlineContactIds.Count > 0)
                {
                    ObserveBackgroundTask(
                        RefreshAndPushContactsOnlineStatusAsync(onlineContactIds),
                        $"后台刷新联系人在线状态失败: UserId={state.UserId}");
                }

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取联系人列表失败");
                throw;
            }
        }

        public Task<IMContactInfo?> GetContactInfoAsync(ulong contactId)
        {
            var state = _userState.State;
            if (!state.Contacts.TryGetValue(contactId, out var entry))
            {
                return Task.FromResult<IMContactInfo?>(null);
            }

            var info = new IMContactInfo
            {
                UserId = entry.UserId,
                Nickname = entry.Nickname,
                Avatar = entry.Avatar,
                RemarkName = entry.Remark,
                Relation = entry.Relation,
                OnlineStatus = entry.OnlineStatus,
                Signature = entry.Bio,
                GroupName = entry.GroupName
            };

            return Task.FromResult<IMContactInfo?>(info);
        }

        public Task<IMContactGroupUpdateResponse> UpdateContactGroupAsync(IMContactGroupUpdateRequest request)
        {
            try
            {
                var state = _userState.State;
                var groups = state.ContactGroups ??= new Dictionary<string, int>();

                switch (request.Action)
                {
                    case "create":
                        if (string.IsNullOrWhiteSpace(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组名称不能为空。" });
                        if (groups.ContainsKey(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组名称已存在。" });
                        groups[request.GroupName] = groups.Count;
                        break;

                    case "rename":
                        if (string.IsNullOrWhiteSpace(request.GroupName) || string.IsNullOrWhiteSpace(request.NewGroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组名称不能为空。" });
                        if (!groups.ContainsKey(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "原分组不存在。" });
                        if (groups.ContainsKey(request.NewGroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "新分组名称已存在。" });
                        var sortIndex = groups[request.GroupName];
                        groups.Remove(request.GroupName);
                        groups[request.NewGroupName] = sortIndex;
                        foreach (var contact in state.Contacts.Values.Where(c => c.GroupName == request.GroupName))
                            contact.GroupName = request.NewGroupName;
                        break;

                    case "delete":
                        if (string.IsNullOrWhiteSpace(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组名称不能为空。" });
                        if (!groups.ContainsKey(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组不存在。" });
                        groups.Remove(request.GroupName);
                        foreach (var contact in state.Contacts.Values.Where(c => c.GroupName == request.GroupName))
                            contact.GroupName = string.Empty;
                        break;

                    case "reorder":
                        if (request.ContactUserIds == null || request.ContactUserIds.Count == 0)
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "无效的排序数据。" });
                        groups.Clear();
                        for (var i = 0; i < request.ContactUserIds.Count; i++)
                        {
                            var name = request.ContactUserIds[i].ToString();
                            if (!string.IsNullOrEmpty(name))
                                groups[name] = i;
                        }
                        break;

                    case "assign":
                        if (request.ContactUserIds == null)
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "缺少联系人ID列表。" });
                        if (!string.IsNullOrEmpty(request.GroupName) && !groups.ContainsKey(request.GroupName))
                            return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = "分组不存在。" });
                        foreach (var uid in request.ContactUserIds)
                        {
                            if (state.Contacts.TryGetValue(uid, out var contact))
                                contact.GroupName = request.GroupName ?? string.Empty;
                        }
                        break;

                    case "list":
                        break;

                    default:
                        return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = $"未知操作：{request.Action}" });
                }

                _userState.State.ContactGroups = groups;
                ScheduleStateFlush($"异步持久化联系人分组失败: UserId={state.UserId}, Action={request.Action}");
                return Task.FromResult(new IMContactGroupUpdateResponse
                {
                    Success = true,
                    ContactGroups = new Dictionary<string, int>(groups)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新联系人分组失败");
                return Task.FromResult(new IMContactGroupUpdateResponse { Success = false, Message = ex.Message });
            }
        }

        public Task<IMContactSearchResponse> SearchContactAsync(IMContactSearchRequest request)
        {
            try
            {
                var state = _userState.State;

                var results = state.Contacts.Values
                    .Where(c => c.Nickname.Contains(request.Keyword, StringComparison.OrdinalIgnoreCase)
                        || c.Remark.Contains(request.Keyword, StringComparison.OrdinalIgnoreCase)
                        || c.UserId.ToString().Contains(request.Keyword))
                    .Take(MaxSearchResultCount)
                    .Select(c => new IMContactInfo
                    {
                        UserId = c.UserId,
                        Nickname = c.Nickname,
                        Avatar = c.Avatar,
                        RemarkName = c.Remark,
                        Relation = c.Relation,
                        OnlineStatus = c.OnlineStatus
                    })
                    .ToList();

                var response = new IMContactSearchResponse
                {
                    Results = results,
                    HasMore = false
                };

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索联系人失败");
                throw;
            }
        }

        public async Task OnContactAddedAsync(ulong contactId, string contactName, IMOnlineStatus contactOnlineStatus)
        {
            _logger.LogInformation("联系人被添加回调: ContactId={ContactId}", contactId);

            var state = _userState.State;

            if (state.Contacts.ContainsKey(contactId))
                return;

            if (state.Contacts.Count >= state.MaxContacts)
            {
                _logger.LogWarning("联系人数量已达上限，无法同步: ContactId={ContactId}", contactId);
                return;
            }

            string contactNickname = contactName ?? contactId.ToString();
            try
            {
                var contactGuid = UInt64ToGuid(contactId);
                var contactGrain = GrainFactory.GetGrain<IIMUserGrain>(contactGuid);
                var nickname = await contactGrain.GetNicknameAsync();
                if (!string.IsNullOrWhiteSpace(nickname))
                    contactNickname = nickname;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取联系人昵称失败，使用回退值: ContactId={ContactId}", contactId);
            }

            state.Contacts[contactId] = new IMContactEntry
            {
                UserId = contactId,
                Nickname = contactNickname,
                Relation = IMContactRelation.Friend,
                OnlineStatus = contactOnlineStatus,
                AddTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            ScheduleStateFlush(
                $"异步持久化联系人添加回调失败: UserId={state.UserId}, ContactId={contactId}");
            ObserveBackgroundTask(
                NotifyGatewayObserversAsync(CreateRosterChangedNotification(
                    state.UserId,
                    "好友列表已更新",
                    $"{contactName} 已加入你的好友列表",
                    priority: 1)),
                $"推送联系人添加通知失败: UserId={state.UserId}, ContactId={contactId}");
        }

        public Task OnContactRequestRejectedAsync(ulong rejecterId, string rejecterName)
        {
            _logger.LogInformation("好友申请被拒绝回调: RejecterId={RejecterId}", rejecterId);

            var displayName = string.IsNullOrWhiteSpace(rejecterName)
                ? rejecterId.ToString()
                : rejecterName;

            ObserveBackgroundTask(
                NotifyGatewayObserversAsync(CreatePendingContactRequestNotification(
                    _userState.State.UserId,
                    displayName,
                    "已拒绝你的好友申请")),
                $"推送好友申请被拒绝通知失败: UserId={_userState.State.UserId}, RejecterId={rejecterId}");

            return Task.CompletedTask;
        }

        public async Task OnContactOnlineStatusChangedAsync(ulong contactId, IMOnlineStatus onlineStatus)
        {
            if (!_userState.State.Contacts.TryGetValue(contactId, out var contact))
            {
                return;
            }

            if (contact.OnlineStatus != onlineStatus)
            {
                contact.OnlineStatus = onlineStatus;
                ScheduleStateFlush(
                    $"异步持久化联系人在线状态失败: UserId={_userState.State.UserId}, ContactId={contactId}, OnlineStatus={onlineStatus}");
            }

            ObserveBackgroundTask(
                NotifyGatewayObserversAsync(CreateContactOnlineStatusNotification(contactId, onlineStatus)),
                $"推送联系人在线状态通知失败: ContactId={contactId}, OnlineStatus={onlineStatus}");
        }

        public Task OnContactProfileUpdatedAsync(ulong contactId, string nickname, string avatar, string bio)
        {
            if (!_userState.State.Contacts.TryGetValue(contactId, out var contact))
            {
                return Task.CompletedTask;
            }

            var normalizedBio = bio ?? string.Empty;
            var changed = false;
            if (!string.IsNullOrEmpty(nickname) && contact.Nickname != nickname)
            {
                contact.Nickname = nickname;
                changed = true;
            }

            // 使用 null 表示"未提供"，空字符串表示"清空头像"，两者均应响应
            if (avatar != null && contact.Avatar != avatar)
            {
                contact.Avatar = avatar;
                changed = true;
            }

            if (contact.Bio != normalizedBio)
            {
                contact.Bio = normalizedBio;
                changed = true;
            }

            if (changed)
            {
                ScheduleStateFlush(
                    $"异步持久化联系人资料更新失败: UserId={_userState.State.UserId}, ContactId={contactId}");
            }

            // 无论本地是否有变化都推送，确保客户端 UI 保持最新
            var profileUpdate = new IMContactProfileUpdateMessage
            {
                UserId = contactId,
                Nickname = nickname ?? string.Empty,
                Avatar = avatar ?? string.Empty,
                Bio = normalizedBio,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            ObserveBackgroundTask(
                NotifyGatewayObserversAsync(profileUpdate),
                $"推送联系人资料更新通知失败: ContactId={contactId}");

            return Task.CompletedTask;
        }

        public Task BroadcastProfileUpdateAsync(string nickname, string avatar, string bio)
        {
            var state = _userState.State;

            // 同步更新 Grain 自身缓存的昵称和头像，下次心跳时也保持一致
            if (!string.IsNullOrEmpty(nickname))
                state.Nickname = nickname;
            if (!string.IsNullOrEmpty(avatar))
                state.Avatar = avatar;

            ScheduleStateFlush(
                $"异步持久化资料广播状态失败: UserId={state.UserId}");

            ObserveBackgroundTask(
                NotifyContactsProfileUpdatedAsync(state.UserId, nickname, avatar, bio),
                $"向好友广播资料变更失败: UserId={state.UserId}");

            return Task.CompletedTask;
        }

        public async Task OnContactRemovedAsync(ulong contactId)
        {
            if (_userState.State.Contacts.Remove(contactId))
            {
                ScheduleStateFlush(
                    $"异步持久化联系人移除回调失败: UserId={_userState.State.UserId}, ContactId={contactId}");
                ObserveBackgroundTask(
                    NotifyGatewayObserversAsync(CreateRosterChangedNotification(
                        _userState.State.UserId,
                        "好友列表已更新",
                        "好友关系已移除，好友列表已刷新。",
                        priority: 1)),
                    $"推送好友删除通知失败: ContactId={contactId}");
            }
        }

        #endregion

        #region 会话管理

        public Task<IMConversationListResponse> GetConversationListAsync(IMConversationListRequest request)
        {
            try
            {
                var state = _userState.State;
                var limit = request.Limit > 0 ? request.Limit : 20;

                var conversations = state.Conversations.Values
                    .OrderByDescending(c => c.IsPinned)
                    .ThenByDescending(c => c.LastMessageTime)
                    .Skip(request.Offset)
                    .Take(limit)
                    .Select(c => new IMConversationInfo
                    {
                        ConversationId = c.ConversationId,
                        ChatRelationType = c.ChatType,
                        PeerId = c.TargetId,
                        DisplayName = c.TargetName,
                        Avatar = c.TargetAvatar,
                        LastMessageSummary = c.LastMessage,
                        LastMessageTime = c.LastMessageTime,
                        UnreadCount = c.UnreadCount,
                        IsPinned = c.IsPinned,
                        IsMuted = c.IsMuted
                    })
                    .ToList();

                var response = new IMConversationListResponse
                {
                    Conversations = conversations,
                    TotalCount = state.Conversations.Count,
                    HasMore = request.Offset + limit < state.Conversations.Count
                };

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取会话列表失败");
                throw;
            }
        }

        public async Task<bool> DeleteConversationAsync(IMConversationDeleteMessage message)
        {
            try
            {
                var removed = _userState.State.Conversations.Remove(message.ConversationId);
                if (removed)
                    ScheduleStateFlush(
                        $"异步持久化删除会话失败: UserId={_userState.State.UserId}, ConversationId={message.ConversationId}");
                return removed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除会话失败: ConversationId={ConversationId}", message.ConversationId);
                throw;
            }
        }

        public async Task<bool> PinConversationAsync(IMConversationPinMessage message)
        {
            try
            {
                if (_userState.State.Conversations.TryGetValue(message.ConversationId, out var conv))
                {
                    conv.IsPinned = message.IsPinned;
                    ScheduleStateFlush(
                        $"异步持久化置顶会话失败: UserId={_userState.State.UserId}, ConversationId={message.ConversationId}, IsPinned={message.IsPinned}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "置顶会话失败: ConversationId={ConversationId}", message.ConversationId);
                throw;
            }
        }

        public async Task<bool> MuteConversationAsync(IMConversationMuteMessage message)
        {
            try
            {
                if (_userState.State.Conversations.TryGetValue(message.ConversationId, out var conv))
                {
                    conv.IsMuted = message.IsMuted;
                    ScheduleStateFlush(
                        $"异步持久化会话免打扰失败: UserId={_userState.State.UserId}, ConversationId={message.ConversationId}, IsMuted={message.IsMuted}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置免打扰失败: ConversationId={ConversationId}", message.ConversationId);
                throw;
            }
        }

        #endregion

        #region 聊天记录

        public Task<IMChatHistoryQueryResponse> QueryChatHistoryAsync(IMChatHistoryQueryRequest request)
        {
            try
            {
                var response = new IMChatHistoryQueryResponse
                {
                    ConversationId = request.ConversationId,
                    ChatRelationType = request.ChatRelationType
                };

                List<IMChatRecord>? records = null;
                var chatType = request.ChatRelationType;

                if (chatType == IMChatRelationType.Friend)
                {
                    _userState.State.PrivateChatHistory.TryGetValue(request.PeerId, out records);
                }
                else if (chatType == IMChatRelationType.Stranger)
                {
                    _userState.State.StrangerChatHistory.TryGetValue(request.PeerId, out records);
                }
                else if (chatType == IMChatRelationType.Group)
                {
                    // 群聊历史记录存储在 IMGroupGrain 侧，用户 Grain 不持有，需通过群组 Grain 查询
                    throw new NotSupportedException("群聊历史记录查询尚未实现");
                }

                var count = request.Count > 0 ? request.Count : 20;
                // 多取一条用于判断是否还有更多记录
                var fetchCount = count + 1;

                if (records != null)
                {
                    var query = records.AsEnumerable();

                    if (request.EndTime > 0)
                        query = query.Where(r => r.Timestamp < request.EndTime);
                    if (request.StartTime > 0)
                        query = query.Where(r => r.Timestamp >= request.StartTime);

                    var candidateRecords = query
                        .OrderByDescending(r => r.Timestamp)
                        .Take(fetchCount)
                        .ToList();

                    // 利用多取的一条判断是否还有更多
                    response.HasMore = candidateRecords.Count > count;

                    var resultRecords = candidateRecords
                        .Take(count)
                        .OrderBy(r => r.Timestamp)
                        .ToList();

                    if (chatType == IMChatRelationType.Friend)
                    {
                        response.PrivateMessages = resultRecords.Select(r => new IMPrivateChatNotifyMessage
                        {
                            ServerMessageId = r.ServerMessageId,
                            SenderId = r.SenderId,
                            SenderName = r.SenderName,
                            ReceiverId = r.ReceiverId,
                            Content = r.Content,
                            ContentType = r.ContentType,
                            Timestamp = r.Timestamp
                        }).ToList();
                    }
                    else
                    {
                        response.StrangerMessages = resultRecords.Select(r => new IMStrangerChatNotifyMessage
                        {
                            ServerMessageId = r.ServerMessageId,
                            SenderId = r.SenderId,
                            SenderName = r.SenderName,
                            ReceiverId = r.ReceiverId,
                            Content = r.Content,
                            ContentType = r.ContentType,
                            Timestamp = r.Timestamp
                        }).ToList();
                    }
                }
                else
                {
                    response.HasMore = false;
                }

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询聊天记录失败");
                throw;
            }
        }

        public async Task<bool> ClearChatHistoryAsync(IMChatHistoryClearMessage message)
        {
            try
            {
                _logger.LogInformation("清空聊天记录: PeerId={PeerId}", message.PeerId);

                var cleared = false;

                if (message.ChatRelationType == IMChatRelationType.Friend)
                    cleared = _userState.State.PrivateChatHistory.Remove(message.PeerId);
                else if (message.ChatRelationType == IMChatRelationType.Stranger)
                    cleared = _userState.State.StrangerChatHistory.Remove(message.PeerId);

                if (cleared)
                    ScheduleStateFlush(
                        $"异步持久化清空聊天记录失败: UserId={_userState.State.UserId}, PeerId={message.PeerId}, ChatRelationType={message.ChatRelationType}");

                return cleared;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空聊天记录失败");
                throw;
            }
        }

        #endregion

        #region 在线状态

        public Task<IMOnlineStatus> GetOnlineStatusAsync()
            => Task.FromResult(_userState.State.OnlineStatus);

        public Task<string> GetNicknameAsync()
            => Task.FromResult(_userState.State.Nickname ?? string.Empty);

        public async Task SetOnlineStatusAsync(IMOnlineStatus status)
        {
            var previousStatus = _userState.State.OnlineStatus;
            _userState.State.OnlineStatus = status;
            ScheduleStateFlush(
                $"异步持久化在线状态失败: UserId={_userState.State.UserId}, OnlineStatus={status}");

            // 同 SyncSessionAsync：上线时总是广播，确保崩溃重连后好友能正确收到在线通知
            if (previousStatus != status || status == IMOnlineStatus.Online)
            {
                ObserveBackgroundTask(
                    NotifyContactsOnlineStatusChangedAsync(status),
                    $"同步联系人在线状态失败: UserId={_userState.State.UserId}, OnlineStatus={status}");
            }
        }

        public async Task SyncSessionAsync(string nickname, string avatar, IMOnlineStatus onlineStatus)
        {
            var previousStatus = _userState.State.OnlineStatus;
            _userState.State.UserId = GuidToUInt64(this.GetPrimaryKey());
            _userState.State.Nickname = nickname;
            _userState.State.Avatar = avatar;
            _userState.State.OnlineStatus = onlineStatus;

            ScheduleStateFlush(
                $"异步持久化会话同步失败: UserId={_userState.State.UserId}, OnlineStatus={onlineStatus}");

            // 状态发生变化，或用户从任意状态重新上线时（含崩溃重连场景），都必须广播，
            // 确保好友客户端能收到最新在线状态，不出现信息展示不准确的问题。
            if (previousStatus != onlineStatus || onlineStatus == IMOnlineStatus.Online)
            {
                ObserveBackgroundTask(
                    NotifyContactsOnlineStatusChangedAsync(onlineStatus),
                    $"同步会话在线状态失败: UserId={_userState.State.UserId}, OnlineStatus={onlineStatus}");
            }
        }

        public Task SubscribeGatewayAsync(Guid subscriptionId, IIMGatewayObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            _gatewayObservers[subscriptionId] = observer;
            return Task.CompletedTask;
        }

        public Task UnsubscribeGatewayAsync(Guid subscriptionId)
        {
            _gatewayObservers.Remove(subscriptionId);
            return Task.CompletedTask;
        }

        #endregion

        #region 辅助方法

        private void AddPrivateChatRecord(ulong targetUserId, IMChatRecord record)
        {
            var history = _userState.State.PrivateChatHistory;
            if (!history.TryGetValue(targetUserId, out var records))
            {
                records = new List<IMChatRecord>();
                history[targetUserId] = records;
            }

            records.Add(record);

            if (records.Count > _userState.State.MaxChatHistoryPerConversation)
                records.RemoveRange(0, records.Count - _userState.State.MaxChatHistoryPerConversation);
        }

        private void AddStrangerChatRecord(ulong targetUserId, IMChatRecord record)
        {
            var history = _userState.State.StrangerChatHistory;
            if (!history.TryGetValue(targetUserId, out var records))
            {
                records = new List<IMChatRecord>();
                history[targetUserId] = records;
            }

            records.Add(record);

            if (records.Count > _userState.State.MaxChatHistoryPerConversation)
                records.RemoveRange(0, records.Count - _userState.State.MaxChatHistoryPerConversation);
        }

        private void UpdateConversation(string convId, IMChatRelationType chatType, ulong targetId,
            string targetName, string targetAvatar, string lastMessage, long lastMessageTime,
            bool incrementUnread)
        {
            var state = _userState.State;

            if (!state.Conversations.TryGetValue(convId, out var conv))
            {
                if (state.Conversations.Count >= state.MaxConversations)
                {
                    var oldest = state.Conversations.Values
                        .Where(c => !c.IsPinned)
                        .OrderBy(c => c.LastMessageTime)
                        .FirstOrDefault();
                    if (oldest != null)
                        state.Conversations.Remove(oldest.ConversationId);
                }

                conv = new IMConversationEntry
                {
                    ConversationId = convId,
                    ChatType = chatType,
                    TargetId = targetId,
                    TargetName = targetName,
                    TargetAvatar = targetAvatar
                };
                state.Conversations[convId] = conv;
            }

            conv.LastMessage = lastMessage.Length > 100 ? lastMessage[..100] : lastMessage;
            conv.LastMessageTime = lastMessageTime;

            if (!string.IsNullOrEmpty(targetName))
                conv.TargetName = targetName;
            if (!string.IsNullOrEmpty(targetAvatar))
                conv.TargetAvatar = targetAvatar;

            if (incrementUnread)
                conv.UnreadCount++;
        }

        private static bool UpdateMessageStatus(Dictionary<ulong, List<IMChatRecord>> histories,
            string serverMessageId, IMMessageStatus newStatus)
        {
            foreach (var records in histories.Values)
            {
                var record = records.FirstOrDefault(r => r.ServerMessageId == serverMessageId);
                if (record != null)
                {
                    record.Status = newStatus;
                    return true;
                }
            }
            return false;
        }

        private static IMChatRecord? FindChatRecord(Dictionary<ulong, List<IMChatRecord>> histories,
            string serverMessageId)
        {
            foreach (var records in histories.Values)
            {
                var record = records.FirstOrDefault(r => r.ServerMessageId == serverMessageId);
                if (record != null)
                    return record;
            }
            return null;
        }

        private async Task PersistUserChatRecordAsync(
            IMChatRelationType relationType,
            ulong peerId,
            string conversationId,
            IMChatRecord record)
        {
            await IMChatRedisOutbox.TryAppendUserChatRecordAsync(
                _logger,
                _userState.State.UserId,
                relationType,
                peerId,
                conversationId,
                record);

            ScheduleStateFlush(
                $"异步持久化聊天状态失败: UserId={_userState.State.UserId}, RelationType={relationType}, PeerId={peerId}, ServerMessageId={record.ServerMessageId}");
        }

        private void ScheduleStateFlush(string operation)
        {
            _stateFlushPending = true;
            if (_stateFlushInProgress)
            {
                return;
            }

            _stateFlushInProgress = true;
            ObserveBackgroundTask(FlushStateAsync(), operation);
        }

        private async Task FlushStateAsync()
        {
            try
            {
                while (true)
                {
                    _stateFlushPending = false;
                    await _userState.WriteStateAsync();

                    if (!_stateFlushPending)
                    {
                        break;
                    }
                }
            }
            catch
            {
                _stateFlushPending = true;
                throw;
            }
            finally
            {
                _stateFlushInProgress = false;
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

        private async Task NotifyContactsOnlineStatusChangedAsync(IMOnlineStatus onlineStatus)
        {
            var contactIds = _userState.State.Contacts.Keys.ToList();
            if (contactIds.Count == 0)
            {
                return;
            }

            var currentUserId = _userState.State.UserId;
            foreach (var contactId in contactIds)
            {
                try
                {
                    var contactGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(contactId));
                    await contactGrain.OnContactOnlineStatusChangedAsync(currentUserId, onlineStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "同步联系人在线状态失败: UserId={UserId}, ContactId={ContactId}, OnlineStatus={OnlineStatus}", currentUserId, contactId, onlineStatus);
                }
            }
        }

        private async Task NotifyContactsProfileUpdatedAsync(ulong selfUserId, string nickname, string avatar, string bio)
        {
            var contactIds = _userState.State.Contacts.Keys.ToList();
            if (contactIds.Count == 0)
            {
                return;
            }

            // 使用信号量限制最大并发数，避免同时发起大量 Grain 调用引发广播风暴
            const int maxConcurrency = 20;
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var tasks = contactIds.Select(async contactId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var contactGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(contactId));
                    await contactGrain.OnContactProfileUpdatedAsync(selfUserId, nickname, avatar, bio);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "向好友推送资料变更失败: SelfUserId={SelfUserId}, ContactId={ContactId}", selfUserId, contactId);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 实时查询被标记为"在线"的联系人的真实状态，若与缓存不符则更新缓存并向客户端推送纠正通知。
        /// 此方法在 GetContactListAsync 返回响应后异步执行，不阻塞列表响应。
        /// </summary>
        private async Task RefreshAndPushContactsOnlineStatusAsync(List<ulong> candidateContactIds)
        {
            if (candidateContactIds.Count == 0)
            {
                return;
            }

            var state = _userState.State;
            var stateChanged = false;

            foreach (var contactId in candidateContactIds)
            {
                try
                {
                    var contactGrain = GrainFactory.GetGrain<IIMUserGrain>(UInt64ToGuid(contactId));
                    // Orleans Grain 内部不应使用 ConfigureAwait(false)，否则续体可能在 Orleans 调度器之外运行，导致并发状态访问
                    var realStatus = await contactGrain.GetOnlineStatusAsync();

                    if (!state.Contacts.TryGetValue(contactId, out var contact))
                    {
                        continue;
                    }

                    if (contact.OnlineStatus != realStatus)
                    {
                        contact.OnlineStatus = realStatus;
                        stateChanged = true;

                        // 推送纠正通知，让客户端静默更新该联系人的在线状态
                        ObserveBackgroundTask(
                            NotifyGatewayObserversAsync(CreateContactOnlineStatusNotification(contactId, realStatus)),
                            $"推送联系人在线状态纠正通知失败: ContactId={contactId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "实时查询联系人在线状态失败: ContactId={ContactId}", contactId);
                }
            }

            if (stateChanged)
            {
                ScheduleStateFlush($"异步持久化联系人在线状态刷新结果失败: UserId={state.UserId}");
            }
        }

        private async Task NotifyGatewayObserversAsync(IMMessageUnion message)
        {
            if (_gatewayObservers.Count == 0)
            {
                return;
            }

            var observers = _gatewayObservers.ToArray();
            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnMessageAsync(_userState.State.UserId, message);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "通知网关观察者失败: UserId={UserId}, SubscriptionId={SubscriptionId}", _userState.State.UserId, subscriptionId);
                }
            }
        }

        private static IMContactOnlineStatusMessage CreateContactOnlineStatusNotification(ulong contactId, IMOnlineStatus onlineStatus)
        {
            return new IMContactOnlineStatusMessage
            {
                UserId = contactId,
                OnlineStatus = onlineStatus,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private static IMSystemNotificationMessage CreateRosterChangedNotification(
            ulong targetUserId,
            string title,
            string content,
            byte priority)
        {
            return new IMSystemNotificationMessage
            {
                TargetUserId = targetUserId,
                Title = title,
                Content = content,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Priority = priority
            };
        }

        private static IMSystemNotificationMessage CreatePendingContactRequestNotification(
            ulong targetUserId,
            string requesterName,
            string verifyMessage)
        {
            var displayName = string.IsNullOrWhiteSpace(requesterName)
                ? "新的联系人"
                : requesterName;

            var content = string.IsNullOrWhiteSpace(verifyMessage)
                ? $"{displayName} 向你发送了好友申请。"
                : $"{displayName} 向你发送了好友申请：{verifyMessage}";

            return new IMSystemNotificationMessage
            {
                TargetUserId = targetUserId,
                Title = "新的好友申请",
                Content = content,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Priority = 2
            };
        }

        /// <summary>
        /// 将ulong确定性转换为Guid（用于Grain键映射）
        /// </summary>
        private static Guid UInt64ToGuid(ulong value)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(value).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        /// <summary>
        /// 将Guid确定性转换为ulong（用于Grain键映射）
        /// </summary>
        private static ulong GuidToUInt64(Guid value)
        {
            return BitConverter.ToUInt64(value.ToByteArray(), 0);
        }

        #endregion
    }
}

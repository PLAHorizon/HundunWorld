using System.Buffers;
using System.Collections.Concurrent;

using Horizon.Core.Security;
using Horizon.Game.GengDi.Enums;

using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

using K4os.Compression.LZ4;

using MemoryPack;

using TouchSocket.Core;
using TouchSocket.Sockets;

using TouchSocketTcpClient = TouchSocket.Sockets.TcpClient;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// IM 网关客户端。使用持久化共享连接和请求-响应关联取代旧的每请求新建连接模式，
    /// 从根本上消除"IM 网关请求超时"问题。
    /// </summary>
    internal sealed class ImGatewayContactClient : IDisposable, IAsyncDisposable
    {
        private const string DefaultHost = "192.168.1.78";
        private const int DefaultPort = 31000;
        private const int MaxRetryAttempts = 2;
        private const int ConnectTimeoutSeconds = 8;
        private const int RequestTimeoutSeconds = 15;
        private const int DefaultPendingListLimit = 50;

        private static readonly ImGatewayMessageAdapter s_messageAdapter = new();
        private static long s_sequenceSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly SemaphoreSlim _notificationLoopGate = new(1, 1);
        private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new(StringComparer.Ordinal);

        private ImGatewaySharedClient _sharedClient;
        private volatile bool _isSharedClientConnected;

        private ImGatewaySubscriptionClient _subscriptionClient;
        private CancellationTokenSource _notificationLoopCts;
        private Task _notificationLoopTask = Task.CompletedTask;
        private ulong _notificationUserId;
        private volatile bool _disposed;

        public event EventHandler<IMSystemNotificationMessage> SystemNotificationReceived;
        public event EventHandler<IMPrivateChatNotifyMessage> PrivateChatReceived;
        public event EventHandler<IMGroupChatNotifyMessage> GroupChatReceived;
        public event EventHandler<IMContactOnlineStatusMessage> ContactOnlineStatusReceived;
        public event EventHandler<IMContactProfileUpdateMessage> ContactProfileUpdateReceived;
        public event EventHandler<IMGroupInviteNotify> GroupInviteReceived;
        public event EventHandler<IMGroupJoinApplyNotify> GroupJoinApplyReceived;
        public event EventHandler<IMGroupInviteApprovalNotify> GroupInviteApprovalReceived;
        public event EventHandler<IMGroupInviteResultNotify> GroupInviteResultReceived;
        public event EventHandler<IMGroupDisbandNotify> GroupDisbandReceived;
        public event EventHandler<IMCallSignalMessage> CallSignalReceived;

        public Task<IMContactAddResponse> AddContactAsync(
            ulong userId,
            ulong targetUserId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMContactAddRequest
            {
                UserId = userId,
                TargetUserId = targetUserId,
                VerifyMessage = string.Empty,
                RemarkName = string.Empty,
                Source = "gengdi",
                RequesterName = ResolveCurrentNickname(userId),
                RequesterAvatar = ResolveCurrentAvatar()
            };

            return SendAsync<IMContactAddResponse>(request, userId, cancellationToken);
        }

        public Task<IMContactRemoveResponse> RemoveContactAsync(
            ulong userId,
            ulong targetUserId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMContactRemoveRequest
            {
                UserId = userId,
                TargetUserId = targetUserId
            };

            return SendAsync<IMContactRemoveResponse>(request, userId, cancellationToken);
        }

        public async Task<IReadOnlyList<IMContactInfo>> GetContactListAsync(
            ulong userId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMContactListRequest
            {
                UserId = userId,
                Offset = 0,
                Limit = 200,
                OnlineOnly = false
            };

            var response = await SendAsync<IMContactListResponse>(request, userId, cancellationToken).ConfigureAwait(false);
            return response.Contacts;
        }

        public async Task<IReadOnlyList<IMPendingContactRequest>> GetPendingContactRequestsAsync(
            ulong userId,
            int limit = DefaultPendingListLimit,
            CancellationToken cancellationToken = default)
        {
            var request = new IMPendingContactRequestListRequest
            {
                UserId = userId,
                Offset = 0,
                Limit = limit > 0 ? limit : DefaultPendingListLimit
            };

            var response = await SendAsync<IMPendingContactRequestListResponse>(request, userId, cancellationToken).ConfigureAwait(false);
            return response.PendingRequests;
        }

        public async Task<IReadOnlyList<IMUserPendingGroupInviteEntry>> GetPendingGroupInvitesAsync(
            ulong userId,
            int limit = DefaultPendingListLimit,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGetPendingGroupInvitesRequest
            {
                UserId = userId,
                Offset = 0,
                Limit = limit > 0 ? limit : DefaultPendingListLimit
            };

            var response = await SendAsync<IMGetPendingGroupInvitesResponse>(request, userId, cancellationToken).ConfigureAwait(false);
            return response.PendingInvites;
        }

        public Task<IMContactRequestHandleResponse> HandleContactRequestAsync(
            ulong userId,
            ulong requesterId,
            bool accept,
            CancellationToken cancellationToken = default)
        {
            var request = new IMContactRequestHandleRequest
            {
                UserId = userId,
                RequesterId = requesterId,
                Accept = accept
            };

            return SendAsync<IMContactRequestHandleResponse>(request, userId, cancellationToken);
        }

        public Task<IMContactGroupUpdateResponse> UpdateContactGroupAsync(
            IMContactGroupUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            return SendAsync<IMContactGroupUpdateResponse>(request, request.UserId, cancellationToken);
        }

        /// <summary>
        /// 在 IM 网关创建群组并获取服务端分配的数字群组 ID。
        /// </summary>
        public Task<IMGroupCreateResponse> CreateGroupAsync(
            ulong creatorId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupCreateRequest
            {
                CreatorId = creatorId,
                GroupName = groupName ?? string.Empty,
                GroupAvatar = string.Empty,
                Announcement = string.Empty,
                InitialMemberIds = new List<ulong> { creatorId },
                MaxMembers = 500
            };

            return SendAsync<IMGroupCreateResponse>(request, creatorId, cancellationToken);
        }

        /// <summary>
        /// 邀请好友入群。
        /// </summary>
        public Task<IMGroupJoinResponse> InviteToGroupAsync(
            ulong inviterId,
            ulong groupId,
            List<ulong> inviteeIds,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupInviteRequest
            {
                InviterId = inviterId,
                GroupId = groupId,
                InviteeIds = inviteeIds ?? new List<ulong>()
            };

            return SendAsync<IMGroupJoinResponse>(request, inviterId, cancellationToken);
        }

        /// <summary>
        /// 响应入群邀请（同意/拒绝）。
        /// </summary>
        public Task<IMGroupJoinResponse> RespondToGroupInviteAsync(
            ulong userId,
            ulong groupId,
            bool accept,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupInviteResponse
            {
                UserId = userId,
                GroupId = groupId,
                Accept = accept
            };

            return SendAsync<IMGroupJoinResponse>(request, userId, cancellationToken);
        }

        /// <summary>
        /// 群主审核由非群主成员发起的入群邀请（同意/拒绝）。
        /// </summary>
        public Task<IMGroupJoinResponse> ReviewGroupInviteApprovalAsync(
            ulong reviewerId,
            ulong groupId,
            ulong inviteeId,
            bool approve,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupInviteApprovalReview
            {
                ReviewerId = reviewerId,
                GroupId = groupId,
                InviteeId = inviteeId,
                Approve = approve
            };

            return SendAsync<IMGroupJoinResponse>(request, reviewerId, cancellationToken);
        }

        /// <summary>
        /// 群主拉取当前群组的待审批邀请列表（用于重连后恢复离线期间漏接的审批通知）。
        /// </summary>
        public async Task<IReadOnlyList<IMGroupInviteApprovalNotify>> GetPendingInviteApprovalsAsync(
            ulong ownerId,
            ulong groupId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGetPendingApprovalListRequest
            {
                OwnerId = ownerId,
                GroupId = groupId
            };

            var response = await SendAsync<IMGetPendingApprovalListResponse>(request, ownerId, cancellationToken)
                .ConfigureAwait(false);

            return response?.PendingApprovals ?? Array.Empty<IMGroupInviteApprovalNotify>().ToList();
        }

        /// <summary>
        /// 当前用户退出群组。
        /// </summary>
        public Task<IMGroupLeaveResponse> LeaveGroupAsync(
            ulong userId,
            ulong groupId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupLeaveRequest
            {
                UserId = userId,
                GroupId = groupId
            };

            return SendAsync<IMGroupLeaveResponse>(request, userId, cancellationToken);
        }

        /// <summary>
        /// 群主解散群组。
        /// </summary>
        public Task<IMGroupDisbandResponse> DisbandGroupAsync(
            ulong ownerId,
            ulong groupId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupDisbandRequest
            {
                OwnerId = ownerId,
                GroupId = groupId
            };

            return SendAsync<IMGroupDisbandResponse>(request, ownerId, cancellationToken);
        }

        public async Task<int> GetGroupMemberCountAsync(
            ulong userId,
            ulong groupId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupMemberListRequest
            {
                GroupId = groupId,
                UserId = userId,
                Offset = 0,
                Limit = 1
            };

            var response = await SendAsync<IMGroupMemberListResponse>(request, userId, cancellationToken).ConfigureAwait(false);
            return response?.TotalCount ?? 0;
        }

        public async Task<List<IMGroupMemberInfo>> GetGroupMemberListAsync(
            ulong userId,
            ulong groupId,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            var allMembers = new List<IMGroupMemberInfo>();
            var offset = 0;

            while (true)
            {
                var request = new IMGroupMemberListRequest
                {
                    GroupId = groupId,
                    UserId = userId,
                    Offset = offset,
                    Limit = Math.Min(limit, 50)
                };

                var response = await SendAsync<IMGroupMemberListResponse>(request, userId, cancellationToken).ConfigureAwait(false);
                if (response?.Members == null || response.Members.Count == 0)
                    break;

                allMembers.AddRange(response.Members);
                offset += response.Members.Count;

                if (!response.HasMore || allMembers.Count >= limit)
                    break;
            }

            return allMembers;
        }

        /// <summary>
        /// 向所有在线好友广播个人资料变更（昵称/头像/简介）。
        /// 此接口为单向广播，服务器不返回响应；建议在资料保存成功后延迟调用。
        /// </summary>
        public Task SendProfileUpdateBroadcastAsync(
            ulong userId,
            string nickname,
            string avatar,
            string bio,
            CancellationToken cancellationToken = default)
        {
            var request = new IMContactProfileBroadcastRequest
            {
                UserId = userId,
                Nickname = nickname,
                Avatar = avatar,
                Bio = bio
            };

            return SendFireAndForgetAsync(request, userId, cancellationToken);
        }

        /// <summary>
        /// 发送通话信令并等待服务端确认应答（发起/接听/拒绝/取消/挂断等状态性信令）。
        /// </summary>
        public Task<IMCallSignalAckMessage> SendCallSignalAsync(
            IMCallSignalMessage signal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(signal);
            signal.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return SendAsync<IMCallSignalAckMessage>(signal, signal.SenderId, cancellationToken);
        }

        /// <summary>
        /// 单向发送通话信令（保活/媒体状态同步等允许丢失的信令），失败仅记录日志不抛异常。
        /// </summary>
        public Task SendCallSignalFireAndForgetAsync(
            IMCallSignalMessage signal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(signal);
            signal.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return SendFireAndForgetAsync(signal, signal.SenderId, cancellationToken);
        }

        public Task<IMChatAckMessage> SendPrivateChatAsync(
            ulong senderId,
            ulong receiverId,
            string content,
            IMContentType contentType,
            CancellationToken cancellationToken = default)
        {
            var request = new IMPrivateChatSendMessage
            {
                SenderId = senderId,
                SenderName = ResolveCurrentNickname(senderId),
                SenderAvatar = ResolveCurrentAvatar(),
                ReceiverId = receiverId,
                Content = content ?? string.Empty,
                ContentType = contentType,
                ClientMessageId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            return SendAsync<IMChatAckMessage>(request, senderId, cancellationToken);
        }

        public Task<IMChatAckMessage> SendGroupChatAsync(
            ulong senderId,
            ulong groupId,
            string content,
            IMContentType contentType,
            CancellationToken cancellationToken = default)
        {
            var request = new IMGroupChatSendMessage
            {
                SenderId = senderId,
                SenderName = ResolveCurrentNickname(senderId),
                SenderAvatar = ResolveCurrentAvatar(),
                GroupId = groupId,
                Content = content ?? string.Empty,
                ContentType = contentType,
                ClientMessageId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            return SendAsync<IMChatAckMessage>(request, senderId, cancellationToken);
        }

        /// <summary>
        /// 获取服务端存储的会话列表（含服务端侧未读计数），用于同步离线期间积累的未读消息数量。
        /// 自动翻页直到 HasMore 为 false，最多拉取 10 页。
        /// </summary>
        public async Task<IReadOnlyList<IMConversationInfo>> GetConversationListAsync(
            ulong userId,
            CancellationToken cancellationToken = default)
        {
            const int pageLimit = 200;
            const int maxPages = 10;
            var allConversations = new System.Collections.Generic.List<IMConversationInfo>();

            for (var page = 0; page < maxPages; page++)
            {
                var request = new IMConversationListRequest
                {
                    UserId = userId,
                    Offset = page * pageLimit,
                    Limit = pageLimit
                };

                var response = await SendAsync<IMConversationListResponse>(request, userId, cancellationToken).ConfigureAwait(false);
                if (response.Conversations != null && response.Conversations.Count > 0)
                {
                    allConversations.AddRange(response.Conversations);
                }

                if (!response.HasMore)
                {
                    break;
                }
            }

            return allConversations.Count > 0 ? allConversations : System.Array.Empty<IMConversationInfo>();
        }

        /// <summary>
        /// 向服务端发送已读回执，重置指定会话的服务端未读计数。
        /// </summary>
        public Task SendReadReceiptAsync(
            ulong userId,
            ulong peerId,
            CancellationToken cancellationToken = default)
        {
            var request = new IMChatReadReceiptMessage
            {
                UserId = userId,
                PeerId = peerId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            return SendFireAndForgetAsync(request, userId, cancellationToken);
        }

        /// <summary>
        /// 通知服务端清理指定会话的聊天记录。
        /// 服务端收到此消息后不返回响应包（单向广播语义），使用 fire-and-forget 发送。
        /// </summary>
        public Task ClearChatHistoryAsync(
            ulong userId,
            ulong peerId,
            IMChatRelationType chatRelationType,
            CancellationToken cancellationToken = default)
        {
            var request = new IMChatHistoryClearMessage
            {
                UserId = userId,
                PeerId = peerId,
                ChatRelationType = chatRelationType,
                ConversationId = BuildConversationId(chatRelationType, peerId)
            };

            return SendFireAndForgetAsync(request, userId, cancellationToken);
        }

        /// <summary>
        /// 向服务端请求指定私聊会话的历史消息列表，用于在用户重新上线后补齐离线期间积累的消息。
        /// </summary>
        public Task<IMChatHistoryQueryResponse> GetPrivateChatHistoryAsync(
            ulong userId,
            ulong peerId,
            int count = 50,
            CancellationToken cancellationToken = default)
        {
            var request = new IMChatHistoryQueryRequest
            {
                UserId = userId,
                PeerId = peerId,
                ChatRelationType = IMChatRelationType.Friend,
                ConversationId = BuildConversationId(IMChatRelationType.Friend, peerId),
                Count = count > 0 ? count : 50
            };

            return SendAsync<IMChatHistoryQueryResponse>(request, userId, cancellationToken);
        }

        private static string BuildConversationId(IMChatRelationType chatRelationType, ulong peerId)
        {
            var prefix = chatRelationType switch
            {
                IMChatRelationType.Group => "g",
                IMChatRelationType.Stranger => "s",
                _ => "p"
            };
            return $"{prefix}_{peerId}";
        }

        public async Task StartRealtimeNotificationsAsync(ulong userId, CancellationToken cancellationToken = default)
        {
            if (userId == 0 || _disposed)
            {
                return;
            }

            try
            {
                await _notificationLoopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            try
            {
                if (_notificationUserId == userId && !_notificationLoopTask.IsCompleted)
                {
                    return;
                }

                await StopRealtimeNotificationsCoreAsync().ConfigureAwait(false);

                _notificationUserId = userId;
                _notificationLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _notificationLoopTask = RunNotificationLoopAsync(userId, _notificationLoopCts.Token);
            }
            finally
            {
                _notificationLoopGate.Release();
            }
        }

        public async Task StopRealtimeNotificationsAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _notificationLoopGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopRealtimeNotificationsCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _notificationLoopGate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 先取消通知循环，避免 RunNotificationLoopAsync 在后续访问已释放对象时抛异常
            var loopCts = _notificationLoopCts;
            _notificationLoopCts = null;
            loopCts?.Cancel();
            loopCts?.Dispose();

            // 在同步 Dispose 中限时等待后台任务退出，避免 DisposeCore 释放资源后后台任务仍在访问它们
            var loopTask = _notificationLoopTask;
            _notificationLoopTask = Task.CompletedTask;
            if (loopTask != null && !loopTask.IsCompleted)
            {
                try
                {
                    loopTask.Wait(TimeSpan.FromSeconds(3));
                }
                catch (AggregateException) { }
                catch (OperationCanceledException) { }
            }

            DisposeCore();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 取消通知循环并等待其结束，确保后台任务不再访问已释放资源
            var loopCts = _notificationLoopCts;
            _notificationLoopCts = null;
            loopCts?.Cancel();
            loopCts?.Dispose();

            var loopTask = _notificationLoopTask;
            _notificationLoopTask = Task.CompletedTask;

            if (loopTask != null && !loopTask.IsCompleted)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }

            DisposeCore();
        }

        /// <summary>
        /// Dispose/DisposeAsync 共用的资源释放核心逻辑
        /// </summary>
        private void DisposeCore()
        {
            FailAllPendingRequests("客户端已释放。");
            _sharedClient?.Dispose();
            _sharedClient = null;
            _isSharedClientConnected = false;
            _subscriptionClient?.Dispose();
            _subscriptionClient = null;
            _connectionGate.Dispose();
            _notificationLoopGate.Dispose();
        }

        /// <summary>
        /// 通过持久化共享连接发送请求并等待响应，支持自动重连和重试。
        /// 旧实现为每次请求新建 TCP 连接，连接建立开销是超时的主要原因。
        /// </summary>
        private async Task SendFireAndForgetAsync(
            IMMessageUnion message,
            ulong userId,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                var packet = CreatePacket(message, userId);
                var frame = PackPacket(packet);
                await client.SendAsync(frame).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单向广播消息，发送失败不影响主流程，仅记录调试日志
                System.Diagnostics.Debug.WriteLine($"[ImGatewayContactClient] 单向消息发送失败: {message.Type}, {ex.Message}");
            }
        }

        private async Task<TResponse> SendAsync<TResponse>(
            IMMessageUnion message,
            ulong userId,
            CancellationToken cancellationToken)
            where TResponse : IMMessageUnion
        {
            Exception lastException = null;

            for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    // 指数退避：attempt=1→500ms, attempt=2→1000ms
                    var backoffMs = Math.Min(500 * (1 << (attempt - 1)), 2000);
                    await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);
                }

                var packet = CreatePacket(message, userId);
                var messageId = packet.Header.MessageId;
                var pending = new PendingRequest();

                _pendingRequests[messageId] = pending;
                try
                {
                    var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                    var frame = PackPacket(packet);

                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestCts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

                    await client.SendAsync(frame).WaitAsync(requestCts.Token).ConfigureAwait(false);

                    var responsePacket = await pending.ResponseSource.Task.WaitAsync(requestCts.Token).ConfigureAwait(false);

                    if (responsePacket.Body is IMErrorMessage error)
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(error.Message) ? error.Details : error.Message);
                    }

                    if (responsePacket.Body is TResponse response)
                    {
                        return response;
                    }

                    throw new InvalidOperationException(
                        $"IM 网关返回了意外的响应类型：{responsePacket.Body?.GetType().Name ?? "unknown"}。");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                    InvalidateSharedClient();
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    InvalidateSharedClient();
                }
                finally
                {
                    _pendingRequests.TryRemove(messageId, out _);
                }
            }

            throw new InvalidOperationException(
                "IM 网关请求失败，已重试但仍无法完成。请检查网关服务状态和网络连接。",
                lastException);
        }

        /// <summary>
        /// 确保持久化共享连接可用。如果连接不存在或已断开，自动创建新连接。
        /// 使用信号量保证同一时刻只有一个线程创建连接。
        /// </summary>
        private async Task<ImGatewaySharedClient> EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ImGatewayContactClient));
            }

            var client = _sharedClient;
            if (client != null && _isSharedClientConnected)
            {
                return client;
            }

            try
            {
                await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException(nameof(ImGatewayContactClient));
            }
            try
            {
                client = _sharedClient;
                if (client != null && _isSharedClientConnected)
                {
                    return client;
                }

                // 主动重连前先清除旧连接的 Closed 回调，避免 Dispose 触发 Closed 事件后调用
                // FailAllPendingRequests 误判为网络断开，导致正在排队的新请求提前失败。
                var staleClient = _sharedClient;
                _sharedClient = null;
                _isSharedClientConnected = false;
                if (staleClient != null)
                {
                    staleClient.Closed = null;
                    staleClient.Dispose();
                }

                var newClient = new ImGatewaySharedClient();
                newClient.Received = (_, e) =>
                {
                    OnSharedClientDataReceived(e);
                    return Task.CompletedTask;
                };
                newClient.Closed = (_, e) =>
                {
                    _isSharedClientConnected = false;
                    FailAllPendingRequests(
                        string.IsNullOrWhiteSpace(e.Message) ? "IM 网关连接已断开。" : $"IM 网关连接已断开：{e.Message}");
                    return Task.CompletedTask;
                };

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

                try
                {
                    await newClient
                        .SetupAsync(new TouchSocketConfig()
                            .SetRemoteIPHost($"{ResolveHost()}:{ResolvePort()}")
                            .SetTcpDataHandlingAdapter(() => new ImGatewayMessageAdapter()))
                        .ConfigureAwait(false);

                    await newClient.ConnectAsync().WaitAsync(connectCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    newClient.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    newClient.Dispose();
                    throw new InvalidOperationException("IM 网关不可用，请确认 Horizon.IM.Gateway 已启动。", ex);
                }

                _sharedClient = newClient;
                _isSharedClientConnected = true;
                return newClient;
            }
            finally
            {
                try
                {
                    _connectionGate.Release();
                }
                catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// 处理共享连接收到的所有响应，通过 ResponseToMessageId 关联到对应的待处理请求。
        /// 未匹配到请求的推送消息（通知、聊天）也在此处分发。
        /// </summary>
        private void OnSharedClientDataReceived(ReceivedDataEventArgs e)
        {
            if (e.RequestInfo is not ImGatewayMessageInfo requestInfo || requestInfo.Packet == null)
            {
                return;
            }

            var packet = requestInfo.Packet;

            if (packet.Header?.IsResponse == true
                && !string.IsNullOrEmpty(packet.Header.ResponseToMessageId)
                && _pendingRequests.TryRemove(packet.Header.ResponseToMessageId, out var pending))
            {
                pending.ResponseSource.TrySetResult(packet);
                return;
            }

            DispatchPushMessage(packet);
        }

        private void DispatchPushMessage(IMMessagePacket packet)
        {
            try
            {
                if (packet.Body is IMSystemNotificationMessage notification)
                {
                    SystemNotificationReceived?.Invoke(this, notification);
                }
                else if (packet.Body is IMPrivateChatNotifyMessage privateChatNotification)
                {
                    PrivateChatReceived?.Invoke(this, privateChatNotification);
                }
                else if (packet.Body is IMGroupChatNotifyMessage groupChatNotification)
                {
                    GroupChatReceived?.Invoke(this, groupChatNotification);
                }
                else if (packet.Body is IMContactOnlineStatusMessage onlineStatusNotification)
                {
                    ContactOnlineStatusReceived?.Invoke(this, onlineStatusNotification);
                }
                else if (packet.Body is IMContactProfileUpdateMessage profileUpdateNotification)
                {
                    ContactProfileUpdateReceived?.Invoke(this, profileUpdateNotification);
                }
                else if (packet.Body is IMGroupInviteNotify groupInviteNotification)
                {
                    GroupInviteReceived?.Invoke(this, groupInviteNotification);
                }
                else if (packet.Body is IMGroupJoinApplyNotify groupJoinApplyNotification)
                {
                    GroupJoinApplyReceived?.Invoke(this, groupJoinApplyNotification);
                }
                else if (packet.Body is IMGroupInviteApprovalNotify groupInviteApprovalNotification)
                {
                    GroupInviteApprovalReceived?.Invoke(this, groupInviteApprovalNotification);
                }
                else if (packet.Body is IMGroupInviteResultNotify groupInviteResultNotification)
                {
                    GroupInviteResultReceived?.Invoke(this, groupInviteResultNotification);
                }
                else if (packet.Body is IMGroupDisbandNotify groupDisbandNotification)
                {
                    GroupDisbandReceived?.Invoke(this, groupDisbandNotification);
                }
                else if (packet.Body is IMCallSignalMessage callSignal)
                {
                    CallSignalReceived?.Invoke(this, callSignal);
                }
            }
            catch (Exception ex)
            {
                // 隔离订阅者异常，避免影响 TouchSocket 接收线程和连接稳定性
                System.Diagnostics.Debug.WriteLine($"[ImGatewayContactClient] 推送消息事件分发异常（{packet.Body?.GetType().Name ?? "unknown"}）：{ex.Message}");
            }
        }

        private void InvalidateSharedClient()
        {
            _isSharedClientConnected = false;
        }

        private void FailAllPendingRequests(string reason)
        {
            foreach (var kvp in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var pending))
                {
                    pending.ResponseSource.TrySetException(new InvalidOperationException(reason));
                }
            }
        }

        private async Task StopRealtimeNotificationsCoreAsync()
        {
            var loopCts = _notificationLoopCts;
            _notificationLoopCts = null;

            if (loopCts != null)
            {
                loopCts.Cancel();
                loopCts.Dispose();
            }

            var client = _subscriptionClient;
            _subscriptionClient = null;
            client?.Dispose();

            var loopTask = _notificationLoopTask;
            _notificationLoopTask = Task.CompletedTask;
            _notificationUserId = 0;

            if (loopTask == null || loopTask.IsCompleted)
            {
                return;
            }

            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunNotificationLoopAsync(ulong userId, CancellationToken cancellationToken)
        {
            var reconnectDelay = TimeSpan.FromSeconds(1);
            var maxReconnectDelay = TimeSpan.FromSeconds(15);

            while (!cancellationToken.IsCancellationRequested)
            {
                ImGatewaySubscriptionClient client = null;

                try
                {
                    client = CreateSubscriptionClient();
                    _subscriptionClient = client;

                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

                    await client
                        .SetupAsync(new TouchSocketConfig()
                            .SetRemoteIPHost($"{ResolveHost()}:{ResolvePort()}")
                            .SetTcpDataHandlingAdapter(() => new ImGatewayMessageAdapter()))
                        .ConfigureAwait(false);

                    await client.ConnectAsync().WaitAsync(connectCts.Token).ConfigureAwait(false);
                    await SendHeartbeatAsync(client, userId, cancellationToken).ConfigureAwait(false);

                    reconnectDelay = TimeSpan.FromSeconds(1);

                    using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
                    while (await heartbeatTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await SendHeartbeatAsync(client, userId, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                }
                finally
                {
                    if (ReferenceEquals(_subscriptionClient, client))
                    {
                        _subscriptionClient = null;
                    }

                    client?.Dispose();
                }

                try
                {
                    await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
                    reconnectDelay = TimeSpan.FromMilliseconds(
                        Math.Min(reconnectDelay.TotalMilliseconds * 2, maxReconnectDelay.TotalMilliseconds));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static IMMessagePacket CreatePacket(IMMessageUnion message, ulong userId)
        {
            ArgumentNullException.ThrowIfNull(message);

            var (messageType, serviceType) = ResolveMessageMetadata(message);

            var header = new IMMessageHeader
            {
                UserId = userId,
                MessageType = messageType,
                ServiceType = serviceType,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SequenceId = Interlocked.Increment(ref s_sequenceSeed),
                RequireResponse = true
            };

            AddSessionMetadata(header, userId);

            return new IMMessagePacket
            {
                Header = header,
                ServiceType = serviceType,
                Body = message
            };
        }

        private static (IMMessageType MessageType, IMServiceType ServiceType) ResolveMessageMetadata(IMMessageUnion message)
        {
            var messageType = message.Type;
            var serviceType = message.ServiceType;

            var runtimeType = message.GetType();
            if (runtimeType == typeof(IMMessageUnion))
            {
                return (messageType, serviceType);
            }

            var runtimeMessageType = runtimeType.GetProperty(nameof(IMMessageUnion.Type))?.GetValue(message);
            if (runtimeMessageType is IMMessageType typedMessageType)
            {
                messageType = typedMessageType;
            }

            var runtimeServiceType = runtimeType.GetProperty(nameof(IMMessageUnion.ServiceType))?.GetValue(message);
            if (runtimeServiceType is IMServiceType typedServiceType)
            {
                serviceType = typedServiceType;
            }

            return (messageType, serviceType);
        }

        private static void AddSessionMetadata(IMMessageHeader header, ulong userId)
        {
            var currentUser = App.CurrentUser;

            header.AuthToken = AccountService.GetImAuthToken();
            header.MachineId = MachineIdentifier.GetMachineGuid();
            header.ExtensionData[IMSessionHeaderKeys.Nickname] = ResolveCurrentNickname(userId);
            header.ExtensionData[IMSessionHeaderKeys.Avatar] = currentUser?.Avatar ?? string.Empty;
            header.ExtensionData[IMSessionHeaderKeys.OnlineStatus] = MapOnlineStatus(currentUser?.Status ?? UserStatus.Online).ToString();
        }

        private static string ResolveCurrentNickname(ulong userId)
        {
            var nickname = App.CurrentUser?.Username?.Trim();
            return string.IsNullOrWhiteSpace(nickname) ? userId.ToString() : nickname;
        }

        private static string ResolveCurrentAvatar()
        {
            return App.CurrentUser?.Avatar?.Trim() ?? string.Empty;
        }

        private static IMOnlineStatus MapOnlineStatus(UserStatus status)
        {
            return status switch
            {
                UserStatus.Online => IMOnlineStatus.Online,
                UserStatus.Away => IMOnlineStatus.Away,
                UserStatus.Busy => IMOnlineStatus.Busy,
                UserStatus.Invisible => IMOnlineStatus.Invisible,
                _ => IMOnlineStatus.Offline
            };
        }

        private static byte[] PackPacket(IMMessagePacket packet)
        {
            return s_messageAdapter.PackPacket(packet);
        }

        private ImGatewaySubscriptionClient CreateSubscriptionClient()
        {
            var client = new ImGatewaySubscriptionClient();
            client.Received = (_, e) =>
            {
                if (e.RequestInfo is not ImGatewayMessageInfo requestInfo || requestInfo.Packet?.Body == null)
                {
                    return Task.CompletedTask;
                }

                DispatchPushMessage(requestInfo.Packet);
                return Task.CompletedTask;
            };

            return client;
        }

        private static async Task SendHeartbeatAsync(
            ImGatewaySubscriptionClient client,
            ulong userId,
            CancellationToken cancellationToken)
        {
            var heartbeat = new IMHeartbeatMessage
            {
                UserId = userId,
                ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var frame = PackPacket(CreatePacket(heartbeat, userId));
            await client.SendAsync(frame).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string ResolveHost()
        {
            var discovered = GatewayDiscoveryService.ImGateway;
            if (discovered != null && !string.IsNullOrWhiteSpace(discovered.Host))
            {
                return discovered.Host;
            }
            return GatewayDiscoveryService.GetImGatewayHost();
        }

        private static int ResolvePort()
        {
            var discovered = GatewayDiscoveryService.ImGateway;
            if (discovered != null && discovered.Port > 0)
            {
                return discovered.Port;
            }
            return GatewayDiscoveryService.GetImGatewayPort();
        }

        private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
        {
            uint checksum = 0;
            foreach (var value in data)
            {
                checksum += value;
            }

            return (ushort)(checksum & 0xFFFF);
        }

        private static byte[] Pickle(byte[] input)
        {
            if (input.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var maxLength = LZ4Codec.MaximumOutputSize(input.Length);
            var output = new byte[maxLength + 4];
            BitConverter.GetBytes(input.Length).CopyTo(output, 0);

            var compressedLength = LZ4Codec.Encode(
                input,
                0,
                input.Length,
                output,
                4,
                output.Length - 4);

            if (compressedLength >= input.Length)
            {
                var raw = new byte[input.Length + 4];
                BitConverter.GetBytes(input.Length).CopyTo(raw, 0);
                Array.Copy(input, 0, raw, 4, input.Length);
                return raw;
            }

            var final = new byte[compressedLength + 4];
            Array.Copy(output, 0, final, 0, final.Length);
            return final;
        }

        private static byte[] Unpickle(ReadOnlySpan<byte> input)
        {
            if (input.Length < 4)
            {
                return null;
            }

            var raw = input.ToArray();
            var originalLength = BitConverter.ToInt32(raw, 0);
            if (originalLength <= 0)
            {
                return null;
            }

            var output = new byte[originalLength];
            var decompressedLength = LZ4Codec.Decode(raw, 4, raw.Length - 4, output, 0, output.Length);

            if (decompressedLength != originalLength)
            {
                if (raw.Length - 4 == originalLength)
                {
                    Array.Copy(raw, 4, output, 0, originalLength);
                    return output;
                }

                return null;
            }

            return output;
        }

        private sealed class PendingRequest
        {
            public TaskCompletionSource<IMMessagePacket> ResponseSource { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class ImGatewaySharedClient : TouchSocketTcpClient
        {
        }

        private sealed class ImGatewaySubscriptionClient : TouchSocketTcpClient
        {
        }

        private sealed class ImGatewayMessageAdapter : CustomFixedHeaderDataHandlingAdapter<ImGatewayMessageInfo>
        {
            private const int MaxPayloadSize = 1024 * 1024;

            public override int HeaderLength => 8;

            protected override ImGatewayMessageInfo GetInstance()
            {
                return new ImGatewayMessageInfo();
            }

            public byte[] PackPacket(IMMessagePacket packet, bool compress = true)
            {
                ArgumentNullException.ThrowIfNull(packet);

                var packetBytes = MemoryPackSerializer.Serialize(packet);
                byte[] payload = packetBytes;
                var compressed = false;

                if (compress && packetBytes.Length > 256)
                {
                    var compressedPayload = Pickle(packetBytes);
                    if (compressedPayload.Length < packetBytes.Length)
                    {
                        payload = compressedPayload;
                        compressed = true;
                    }
                }

                if (payload.Length > MaxPayloadSize)
                {
                    throw new InvalidOperationException(
                        $"消息大小（{payload.Length / 1024.0:F0} KB）超出允许上限（{MaxPayloadSize / 1024} KB）。请缩短消息内容后重试。");
                }

                var frame = new byte[HeaderLength + payload.Length];
                BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
                frame[4] = (byte)packet.Header.MessageType;
                frame[5] = compressed ? (byte)1 : (byte)0;
                BitConverter.GetBytes(CalculateChecksum(payload)).CopyTo(frame, 6);
                Array.Copy(payload, 0, frame, HeaderLength, payload.Length);
                return frame;
            }
        }

        private sealed class ImGatewayMessageInfo : IFixedHeaderRequestInfo
        {
            private bool _isCompressed;
            private ushort _expectedChecksum;

            public int BodyLength { get; set; }

            public byte[] Body { get; set; }

            public IMMessagePacket Packet { get; set; }

            public int MaxLength => 1024 * 1024;

            public bool TryBuild(ReadOnlySequence<byte> buffer, int length, out IRequestInfo requestInfo)
            {
                requestInfo = default!;

                try
                {
                    if (buffer.Length < 8)
                    {
                        return false;
                    }

                    var reader = new SequenceReader<byte>(buffer);
                    if (!reader.TryReadLittleEndian(out int payloadLength))
                    {
                        return false;
                    }

                    if (buffer.Length < 8 + payloadLength)
                    {
                        return false;
                    }

                    if (!reader.TryRead(out _))
                    {
                        return false;
                    }

                    if (!reader.TryRead(out byte compressedFlag))
                    {
                        return false;
                    }

                    _isCompressed = compressedFlag != 0;
                    if (!reader.TryReadLittleEndian(out short checksum))
                    {
                        return false;
                    }

                    _expectedChecksum = unchecked((ushort)checksum);
                    var payload = buffer.Slice(8, payloadLength).ToArray();
                    if (!TryDeserialize(payload, out var packet))
                    {
                        return false;
                    }

                    requestInfo = new ImGatewayMessageInfo
                    {
                        Body = payload,
                        BodyLength = payloadLength,
                        Packet = packet
                    };

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public bool TryBuild(ReadOnlySequence<byte> buffer, out IRequestInfo requestInfo)
            {
                return TryBuild(buffer, (int)buffer.Length, out requestInfo);
            }

            public bool OnParsingHeader(ReadOnlySpan<byte> header)
            {
                if (header.Length < 8)
                {
                    return false;
                }

                try
                {
                    var bodyLength = BitConverter.ToInt32(header);
                    if (bodyLength <= 0 || bodyLength > MaxLength)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ImGateway] 收到帧体长度 {bodyLength} 超出允许范围 [1, {MaxLength}]，已丢弃。");
                        return false;
                    }

                    BodyLength = bodyLength;
                    _isCompressed = header[5] != 0;
                    _expectedChecksum = BitConverter.ToUInt16(header.Slice(6, 2));
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImGateway] 解析帧头异常：{ex.Message}");
                    return false;
                }
            }

            public bool OnParsingBody(ReadOnlySpan<byte> body)
            {
                if (body.Length != BodyLength)
                {
                    return false;
                }

                try
                {
                    if (CalculateChecksum(body) != _expectedChecksum)
                    {
                        return false;
                    }

                    Body = body.ToArray();
                    if (!TryDeserialize(body, out var packet))
                    {
                        return false;
                    }

                    Packet = packet;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
            {
                if (Packet == null)
                {
                    return;
                }

                var body = MemoryPackSerializer.Serialize(Packet);
                byteBlock.Write(BitConverter.GetBytes(body.Length));
                byteBlock.Write(body);
            }

            private bool TryDeserialize(ReadOnlySpan<byte> payload, out IMMessagePacket packet)
            {
                packet = default!;
                var finalPayload = _isCompressed ? Unpickle(payload) : payload.ToArray();
                if (finalPayload == null)
                {
                    return false;
                }

                packet = MemoryPackSerializer.Deserialize<IMMessagePacket>(finalPayload)!;
                return packet != null;
            }
        }
    }
}
using Horizon.IM.Core;
using Horizon.IM.Core.Adapters;
using Horizon.IM.Gateway.Services;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Interface;

using Microsoft.Extensions.Logging.Abstractions;

using Orleans;
using Orleans.TestingHost;
using System.Diagnostics;

namespace Horizon.IM.Gateway.Tests;

public sealed class IMGatewayPushServiceTests : IAsyncLifetime
{
    private readonly FakeConnectionManager _connectionManager = new();
    private readonly IMMessageAdapter _adapter = new();

    private TestCluster? _cluster;
    private IMGatewayPushService? _pushService;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<IMGatewayTestSiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();

        var clusterClient = _cluster.GrainFactory as IClusterClient;
        if (clusterClient == null)
        {
            throw new InvalidOperationException("TestCluster GrainFactory 未实现 IClusterClient。");
        }

        _pushService = new IMGatewayPushService(
            NullLogger<IMGatewayPushService>.Instance,
            clusterClient,
            _connectionManager,
            _adapter);

        await _pushService.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_pushService != null)
        {
            await _pushService.StopAsync();
        }

        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task PrivateMessage_IsPushedToSubscribedReceiver()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(receiverId, "Receiver", string.Empty, IMOnlineStatus.Online);

        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "hello",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        Assert.Equal(IMContactRelation.PendingRequest, addResponse.Relation);

        var accepted = await receiverGrain.HandleContactRequestAsync(senderId, accept: true);
        Assert.True(accepted);

        _connectionManager.Clear();

        var serverMessageId = await senderGrain.SendPrivateMessageAsync(new IMPrivateChatSendMessage
        {
            SenderId = senderId,
            SenderName = "Sender",
            SenderAvatar = string.Empty,
            ReceiverId = receiverId,
            Content = "gateway push works",
            ContentType = IMContentType.Text,
            ClientMessageId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Assert.False(string.IsNullOrWhiteSpace(serverMessageId));

        var push = await _connectionManager.WaitForPushAsync(receiverId, TimeSpan.FromSeconds(5));
        Assert.NotNull(push);
        Assert.Equal(receiverId, push.UserId);

        var packet = ParsePacket(push.Payload);
        Assert.Equal(IMMessageType.PrivateChatNotify, packet.Header.MessageType);

        var notify = Assert.IsType<IMPrivateChatNotifyMessage>(packet.Body);
        Assert.Equal(senderId, notify.SenderId);
        Assert.Equal(receiverId, notify.ReceiverId);
        Assert.Equal("gateway push works", notify.Content);
        Assert.Equal(serverMessageId, notify.ServerMessageId);
    }

    [Fact]
    public async Task ContactOnlineStatus_IsPushedToSubscribedFriend()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(receiverId, "Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        _connectionManager.Clear();

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Busy);

        var packet = await WaitForPacketAsync(receiverId, IMMessageType.ContactOnlineStatus, TimeSpan.FromSeconds(5));
        Assert.Equal(IMMessageType.ContactOnlineStatus, packet.Header.MessageType);

        var notify = Assert.IsType<IMContactOnlineStatusMessage>(packet.Body);
        Assert.Equal(senderId, notify.UserId);
        Assert.Equal(IMOnlineStatus.Busy, notify.OnlineStatus);
    }

    [Fact]
    public async Task GroupMessage_IsPushedToSubscribedMember()
    {
        var (ownerId, memberId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var ownerGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(ownerId));
        var memberGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(memberId));

        await ownerGrain.SyncSessionAsync("Owner", string.Empty, IMOnlineStatus.Online);
        await memberGrain.SyncSessionAsync("Member", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(memberId, "Member", string.Empty, IMOnlineStatus.Online);

        var groupId = IMGrainKey.NewUInt64Id();
        var groupGrain = _cluster.GrainFactory.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(groupId));

        var createResponse = await groupGrain.CreateGroupAsync(new IMGroupCreateRequest
        {
            CreatorId = ownerId,
            GroupName = "gateway-push-group",
            MaxMembers = 20
        });

        Assert.True(createResponse.Success);

        var joinResponse = await groupGrain.JoinGroupAsync(new IMGroupJoinRequest
        {
            UserId = memberId,
            GroupId = groupId,
            Reason = "integration-test"
        });

        Assert.True(joinResponse.Success);

        _connectionManager.Clear();

        var serverMessageId = await groupGrain.SendGroupMessageAsync(new IMGroupChatSendMessage
        {
            SenderId = ownerId,
            SenderName = "Owner",
            SenderAvatar = string.Empty,
            GroupId = groupId,
            Content = "group push works",
            ContentType = IMContentType.Text,
            ClientMessageId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Assert.False(string.IsNullOrWhiteSpace(serverMessageId));

        var push = await _connectionManager.WaitForPushAsync(memberId, TimeSpan.FromSeconds(5));
        Assert.NotNull(push);
        Assert.Equal(memberId, push.UserId);

        var packet = ParsePacket(push.Payload);
        Assert.Equal(IMMessageType.GroupChatNotify, packet.Header.MessageType);

        var notify = Assert.IsType<IMGroupChatNotifyMessage>(packet.Body);
        Assert.Equal(groupId, notify.GroupId);
        Assert.Equal(ownerId, notify.SenderId);
        Assert.Equal("group push works", notify.Content);
        Assert.Equal(serverMessageId, notify.ServerMessageId);
    }

    [Fact]
    public async Task PrivateMessage_ReturnsQuickly_EvenWhenReceiverObserverIsSlow()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var clusterClient = Assert.IsAssignableFrom<IClusterClient>(_cluster!.GrainFactory);
        var senderGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        var observer = new DelayedGatewayObserver(TimeSpan.FromSeconds(3));
        var observerReference = clusterClient.CreateObjectReference<IIMGatewayObserver>(observer);
        var subscriptionId = Guid.NewGuid();

        try
        {
            await receiverGrain.SubscribeGatewayAsync(subscriptionId, observerReference);

            var stopwatch = Stopwatch.StartNew();
            var serverMessageId = await senderGrain.SendPrivateMessageAsync(new IMPrivateChatSendMessage
            {
                SenderId = senderId,
                SenderName = "Sender",
                SenderAvatar = string.Empty,
                ReceiverId = receiverId,
                Content = "slow observer should not block private ack",
                ContentType = IMContentType.Text,
                ClientMessageId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }).WaitAsync(TimeSpan.FromMilliseconds(750));
            stopwatch.Stop();

            Assert.False(string.IsNullOrWhiteSpace(serverMessageId));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750), $"私聊 ACK 返回过慢: {stopwatch.Elapsed}.");

            var delivered = await observer.WaitForMessageAsync(TimeSpan.FromSeconds(5));
            var notify = Assert.IsType<IMPrivateChatNotifyMessage>(delivered);
            Assert.Equal(serverMessageId, notify.ServerMessageId);
        }
        finally
        {
            await receiverGrain.UnsubscribeGatewayAsync(subscriptionId);
            clusterClient.DeleteObjectReference<IIMGatewayObserver>(observerReference);
        }
    }

    [Fact]
    public async Task GroupMessage_ReturnsQuickly_EvenWhenMemberObserverIsSlow()
    {
        var (ownerId, memberId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var clusterClient = Assert.IsAssignableFrom<IClusterClient>(_cluster!.GrainFactory);
        var ownerGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(ownerId));
        var memberGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(memberId));

        await ownerGrain.SyncSessionAsync("Owner", string.Empty, IMOnlineStatus.Online);
        await memberGrain.SyncSessionAsync("Member", string.Empty, IMOnlineStatus.Online);

        var groupId = IMGrainKey.NewUInt64Id();
        var groupGrain = _cluster.GrainFactory.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(groupId));

        var createResponse = await groupGrain.CreateGroupAsync(new IMGroupCreateRequest
        {
            CreatorId = ownerId,
            GroupName = "slow-observer-group",
            MaxMembers = 20
        });

        Assert.True(createResponse.Success);

        var joinResponse = await groupGrain.JoinGroupAsync(new IMGroupJoinRequest
        {
            UserId = memberId,
            GroupId = groupId,
            Reason = "integration-test"
        });

        Assert.True(joinResponse.Success);

        var observer = new DelayedGatewayObserver(TimeSpan.FromSeconds(3));
        var observerReference = clusterClient.CreateObjectReference<IIMGatewayObserver>(observer);
        var subscriptionId = Guid.NewGuid();

        try
        {
            await memberGrain.SubscribeGatewayAsync(subscriptionId, observerReference);

            var stopwatch = Stopwatch.StartNew();
            var serverMessageId = await groupGrain.SendGroupMessageAsync(new IMGroupChatSendMessage
            {
                SenderId = ownerId,
                SenderName = "Owner",
                SenderAvatar = string.Empty,
                GroupId = groupId,
                Content = "slow observer should not block group ack",
                ContentType = IMContentType.Text,
                ClientMessageId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }).WaitAsync(TimeSpan.FromMilliseconds(750));
            stopwatch.Stop();

            Assert.False(string.IsNullOrWhiteSpace(serverMessageId));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750), $"群聊 ACK 返回过慢: {stopwatch.Elapsed}.");

            var delivered = await observer.WaitForMessageAsync(TimeSpan.FromSeconds(5));
            var notify = Assert.IsType<IMGroupChatNotifyMessage>(delivered);
            Assert.Equal(serverMessageId, notify.ServerMessageId);
        }
        finally
        {
            await memberGrain.UnsubscribeGatewayAsync(subscriptionId);
            clusterClient.DeleteObjectReference<IIMGatewayObserver>(observerReference);
        }
    }

    [Fact]
    public async Task AddContact_ReturnsQuickly_EvenWhenReceiverObserverIsSlow()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var clusterClient = Assert.IsAssignableFrom<IClusterClient>(_cluster!.GrainFactory);
        var senderGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);

        var observer = new DelayedGatewayObserver(TimeSpan.FromSeconds(3));
        var observerReference = clusterClient.CreateObjectReference<IIMGatewayObserver>(observer);
        var subscriptionId = Guid.NewGuid();

        try
        {
            await receiverGrain.SubscribeGatewayAsync(subscriptionId, observerReference);

            var stopwatch = Stopwatch.StartNew();
            var response = await senderGrain.AddContactAsync(new IMContactAddRequest
            {
                UserId = senderId,
                TargetUserId = receiverId,
                VerifyMessage = "slow observer should not block contact request",
                RequesterName = "Sender",
                RequesterAvatar = string.Empty,
                Source = "integration-test"
            }).WaitAsync(TimeSpan.FromMilliseconds(750));
            stopwatch.Stop();

            Assert.True(response.Success);
            Assert.Equal(IMContactRelation.PendingRequest, response.Relation);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750), $"好友申请返回过慢: {stopwatch.Elapsed}.");

            var pendingResponse = await receiverGrain.GetPendingContactRequestsAsync(new IMPendingContactRequestListRequest
            {
                UserId = receiverId,
                Offset = 0,
                Limit = 20
            });

            Assert.Contains(pendingResponse.PendingRequests, request => request.RequesterId == senderId);

            var delivered = await observer.WaitForMessageAsync(TimeSpan.FromSeconds(5));
            var notify = Assert.IsType<IMSystemNotificationMessage>(delivered);
            Assert.Equal(receiverId, notify.TargetUserId);
        }
        finally
        {
            await receiverGrain.UnsubscribeGatewayAsync(subscriptionId);
            clusterClient.DeleteObjectReference<IIMGatewayObserver>(observerReference);
        }
    }

    [Fact]
    public async Task PendingContactRequests_CanBeQueriedAndClearedAfterAcceptance()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);

        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "please accept",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        Assert.Equal(IMContactRelation.PendingRequest, addResponse.Relation);

        var pendingResponse = await receiverGrain.GetPendingContactRequestsAsync(new IMPendingContactRequestListRequest
        {
            UserId = receiverId,
            Offset = 0,
            Limit = 20
        });

        var pendingRequest = Assert.Single(pendingResponse.PendingRequests);
        Assert.Equal(senderId, pendingRequest.RequesterId);
        Assert.Equal("Sender", pendingRequest.RequesterName);
        Assert.Equal("please accept", pendingRequest.Message);

        var accepted = await receiverGrain.HandleContactRequestAsync(senderId, accept: true);
        Assert.True(accepted);

        var clearedResponse = await receiverGrain.GetPendingContactRequestsAsync(new IMPendingContactRequestListRequest
        {
            UserId = receiverId,
            Offset = 0,
            Limit = 20
        });

        Assert.Empty(clearedResponse.PendingRequests);

        var receiverContacts = await receiverGrain.GetContactListAsync(new IMContactListRequest
        {
            UserId = receiverId,
            Offset = 0,
            Limit = 20,
            OnlineOnly = false
        });
        Assert.Contains(receiverContacts.Contacts, contact => contact.UserId == senderId);

        var senderContacts = await senderGrain.GetContactListAsync(new IMContactListRequest
        {
            UserId = senderId,
            Offset = 0,
            Limit = 20,
            OnlineOnly = false
        });
        Assert.Contains(senderContacts.Contacts, contact => contact.UserId == receiverId);
    }

    [Fact]
    public async Task ReceivingContactRequest_PushesPendingRequestRefreshToReceiver()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(receiverId, "Receiver", string.Empty, IMOnlineStatus.Online);

        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "please accept",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        Assert.Equal(IMContactRelation.PendingRequest, addResponse.Relation);

        var push = await _connectionManager.WaitForPushAsync(receiverId, TimeSpan.FromSeconds(5));
        Assert.NotNull(push);
        Assert.Equal(receiverId, push.UserId);

        var packet = ParsePacket(push.Payload);
        Assert.Equal(IMMessageType.SystemNotification, packet.Header.MessageType);

        var notify = Assert.IsType<IMSystemNotificationMessage>(packet.Body);
        Assert.Equal(receiverId, notify.TargetUserId);
        Assert.Equal("新的好友申请", notify.Title);
        Assert.Contains("Sender", notify.Content);
        Assert.Contains("please accept", notify.Content);
    }

    [Fact]
    public async Task AcceptingContactRequest_PushesRosterRefreshToRequester()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(senderId, "Sender", string.Empty, IMOnlineStatus.Online);
        await _pushService.EnsureUserSessionAsync(receiverId, "Receiver", string.Empty, IMOnlineStatus.Online);

        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "please accept",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        _connectionManager.Clear();

        var accepted = await receiverGrain.HandleContactRequestAsync(senderId, accept: true);
        Assert.True(accepted);

        var packet = await WaitForSystemNotificationAsync(senderId, "好友列表已更新", TimeSpan.FromSeconds(5));
        Assert.Equal(IMMessageType.SystemNotification, packet.Header.MessageType);

        var notify = Assert.IsType<IMSystemNotificationMessage>(packet.Body);
        Assert.Equal(senderId, notify.TargetUserId);
        Assert.Equal("好友列表已更新", notify.Title);
        Assert.Contains("已加入你的好友列表", notify.Content);
    }

    [Fact]
    public async Task RemovingContact_PushesRosterRefreshToRemovedFriend()
    {
        var (senderId, receiverId) = NewDistinctUserIds();

        Assert.NotNull(_cluster);
        Assert.NotNull(_pushService);

        var senderGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(senderId));
        var receiverGrain = _cluster.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(receiverId));

        await senderGrain.SyncSessionAsync("Sender", string.Empty, IMOnlineStatus.Online);
        await receiverGrain.SyncSessionAsync("Receiver", string.Empty, IMOnlineStatus.Online);
        await _pushService!.EnsureUserSessionAsync(receiverId, "Receiver", string.Empty, IMOnlineStatus.Online);
        await MakeFriendsAsync(senderGrain, receiverGrain, senderId, receiverId);

        _connectionManager.Clear();

        var response = await senderGrain.RemoveContactAsync(new IMContactRemoveRequest
        {
            UserId = senderId,
            TargetUserId = receiverId
        });

        Assert.True(response.Success);

        var packet = await WaitForSystemNotificationAsync(receiverId, "好友列表已更新", TimeSpan.FromSeconds(5));
        Assert.Equal(IMMessageType.SystemNotification, packet.Header.MessageType);

        var notify = Assert.IsType<IMSystemNotificationMessage>(packet.Body);
        Assert.Equal(receiverId, notify.TargetUserId);
        Assert.Equal("好友列表已更新", notify.Title);
        Assert.Contains("已移除", notify.Content);
    }

    private static async Task MakeFriendsAsync(
        IIMUserGrain senderGrain,
        IIMUserGrain receiverGrain,
        ulong senderId,
        ulong receiverId)
    {
        var addResponse = await senderGrain.AddContactAsync(new IMContactAddRequest
        {
            UserId = senderId,
            TargetUserId = receiverId,
            VerifyMessage = "hello",
            RequesterName = "Sender",
            RequesterAvatar = string.Empty,
            Source = "integration-test"
        });

        Assert.True(addResponse.Success);
        Assert.Equal(IMContactRelation.PendingRequest, addResponse.Relation);

        var accepted = await receiverGrain.HandleContactRequestAsync(senderId, accept: true);
        Assert.True(accepted);
    }

    private static (ulong first, ulong second) NewDistinctUserIds()
    {
        var first = IMGrainKey.NewUInt64Id();
        var second = IMGrainKey.NewUInt64Id();
        while (second == first)
        {
            second = IMGrainKey.NewUInt64Id();
        }

        return (first, second);
    }

    private static IMMessagePacket ParsePacket(byte[] frame)
    {
        Assert.True(frame.Length > IMProtocol.HeaderLength);

        var messageInfo = new IMMessageInfo();
        Assert.True(messageInfo.OnParsingHeader(frame.AsSpan(0, IMProtocol.HeaderLength)));
        Assert.True(messageInfo.OnParsingBody(frame.AsSpan(IMProtocol.HeaderLength)));

        return Assert.IsType<IMMessagePacket>(messageInfo.Packet);
    }

    private async Task<IMMessagePacket> WaitForPacketAsync(ulong userId, IMMessageType messageType, TimeSpan timeout)
    {
        var push = await _connectionManager.WaitForPushAsync(
            userId,
            candidate => ParsePacket(candidate.Payload).Header.MessageType == messageType,
            timeout);

        return ParsePacket(push.Payload);
    }

    private async Task<IMMessagePacket> WaitForSystemNotificationAsync(ulong userId, string title, TimeSpan timeout)
    {
        var push = await _connectionManager.WaitForPushAsync(
            userId,
            candidate =>
            {
                var packet = ParsePacket(candidate.Payload);
                return packet.Header.MessageType == IMMessageType.SystemNotification
                    && packet.Body is IMSystemNotificationMessage notify
                    && string.Equals(notify.Title, title, StringComparison.Ordinal);
            },
            timeout);

        return ParsePacket(push.Payload);
    }

    private sealed class FakeConnectionManager : IIMConnectionManager
    {
        private readonly object _syncRoot = new();
        private readonly List<SentPush> _sentPushes = new();
        private readonly Dictionary<ulong, TaskCompletionSource<SentPush>> _waiters = new();
        private long _sequence;

        public Task<bool> AddConnectionAsync(IMConnection connection)
        {
            return Task.FromResult(true);
        }

        public Task RemoveConnectionAsync(string connectionId)
        {
            return Task.CompletedTask;
        }

        public IMConnection? GetConnection(string connectionId)
        {
            return null;
        }

        public Task BindUserAsync(ulong userId, string connectionId)
        {
            return Task.CompletedTask;
        }

        public IMConnection? GetConnectionByUser(ulong userId)
        {
            return null;
        }

        public Task<bool> SendToUserAsync(ulong userId, byte[] message)
        {
            TaskCompletionSource<SentPush>? waiter = null;
            SentPush push;

            lock (_syncRoot)
            {
                push = new SentPush(++_sequence, userId, message);
                _sentPushes.Add(push);

                if (_waiters.TryGetValue(userId, out waiter))
                {
                    _waiters.Remove(userId);
                }
            }

            waiter?.TrySetResult(push);
            return Task.FromResult(true);
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _sentPushes.Clear();
            }
        }

        public async Task<SentPush> WaitForPushAsync(ulong userId, TimeSpan timeout)
            => await WaitForPushAsync(userId, _ => true, timeout);

        public async Task<SentPush> WaitForPushAsync(ulong userId, Func<SentPush, bool> predicate, TimeSpan timeout)
        {
            TaskCompletionSource<SentPush> waiter;
            var lastSeenSequence = 0L;
            var deadline = DateTime.UtcNow + timeout;

            while (true)
            {
                lock (_syncRoot)
                {
                    var existingPush = _sentPushes
                        .FirstOrDefault(push => push.Sequence > lastSeenSequence && push.UserId == userId && predicate(push));
                    if (existingPush != null)
                    {
                        return existingPush;
                    }

                    waiter = new TaskCompletionSource<SentPush>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters[userId] = waiter;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException();
                }

                var push = await waiter.Task.WaitAsync(remaining);
                lastSeenSequence = push.Sequence;
                if (push.UserId == userId && predicate(push))
                {
                    return push;
                }
            }
        }
    }

    private sealed class DelayedGatewayObserver : IIMGatewayObserver
    {
        private readonly TimeSpan _delay;
        private readonly TaskCompletionSource<IMMessageUnion> _messageSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayedGatewayObserver(TimeSpan delay)
        {
            _delay = delay;
        }

        public async Task OnMessageAsync(ulong userId, IMMessageUnion message)
        {
            _messageSource.TrySetResult(message);
            await Task.Delay(_delay);
        }

        public Task<IMMessageUnion> WaitForMessageAsync(TimeSpan timeout)
        {
            return _messageSource.Task.WaitAsync(timeout);
        }
    }

    private sealed record SentPush(long Sequence, ulong UserId, byte[] Payload);
}
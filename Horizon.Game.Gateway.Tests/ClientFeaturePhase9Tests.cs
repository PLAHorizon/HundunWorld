using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第九阶段
    /// 测试聊天与好友系统客户端集成消息类型和DTO
    /// </summary>
    public class ClientFeaturePhase9Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_ChatNotify_HasCorrectValue()
        {
            Assert.Equal(1368, (int)MessageType.ChatNotify);
        }

        [Fact]
        public void MessageType_FriendStatusUpdate_HasCorrectValue()
        {
            Assert.Equal(1369, (int)MessageType.FriendStatusUpdate);
        }

        [Fact]
        public void MessageType_FriendRequestNotify_HasCorrectValue()
        {
            Assert.Equal(1370, (int)MessageType.FriendRequestNotify);
        }

        [Fact]
        public void MessageType_ChatChannelJoin_HasCorrectValue()
        {
            Assert.Equal(1371, (int)MessageType.ChatChannelJoin);
        }

        [Fact]
        public void MessageType_ChatChannelLeave_HasCorrectValue()
        {
            Assert.Equal(1372, (int)MessageType.ChatChannelLeave);
        }

        [Fact]
        public void MessageType_Phase9Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.ChatNotify,
                (int)MessageType.FriendStatusUpdate,
                (int)MessageType.FriendRequestNotify,
                (int)MessageType.ChatChannelJoin,
                (int)MessageType.ChatChannelLeave
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase9Types_DoNotConflictWithPreviousPhases()
        {
            var phase8Max = (int)MessageType.CharacterAttributeRefresh; // 1367
            Assert.True((int)MessageType.ChatNotify > phase8Max);
            Assert.True((int)MessageType.FriendStatusUpdate > phase8Max);
            Assert.True((int)MessageType.FriendRequestNotify > phase8Max);
            Assert.True((int)MessageType.ChatChannelJoin > phase8Max);
            Assert.True((int)MessageType.ChatChannelLeave > phase8Max);
        }

        [Fact]
        public void MessageType_Phase9Types_AreSequential()
        {
            Assert.Equal((int)MessageType.ChatNotify + 1, (int)MessageType.FriendStatusUpdate);
            Assert.Equal((int)MessageType.FriendStatusUpdate + 1, (int)MessageType.FriendRequestNotify);
            Assert.Equal((int)MessageType.FriendRequestNotify + 1, (int)MessageType.ChatChannelJoin);
            Assert.Equal((int)MessageType.ChatChannelJoin + 1, (int)MessageType.ChatChannelLeave);
        }

        #endregion

        #region ChatNotifyMessage Tests

        [Fact]
        public void ChatNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new ChatNotifyMessage();
            Assert.Equal(MessageType.ChatNotify, msg.Type);
            Assert.Equal(ServiceType.Chat, msg.ServiceType);
            Assert.Equal(0UL, msg.SenderId);
            Assert.Equal("", msg.SenderName);
            Assert.Equal(ChatChannel.World, msg.Channel);
            Assert.Equal("", msg.Content);
            Assert.Equal(0L, msg.Timestamp);
            Assert.Equal("", msg.ChatMessageId);
            Assert.False(msg.IsSystemMessage);
        }

        [Fact]
        public void ChatNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new ChatNotifyMessage
            {
                SenderId = 100UL,
                SenderName = "玩家A",
                Channel = ChatChannel.Guild,
                Content = "你好世界",
                Timestamp = 1700000000L,
                ChatMessageId = "msg-001",
                IsSystemMessage = true
            };

            Assert.Equal(100UL, msg.SenderId);
            Assert.Equal("玩家A", msg.SenderName);
            Assert.Equal(ChatChannel.Guild, msg.Channel);
            Assert.Equal("你好世界", msg.Content);
            Assert.Equal(1700000000L, msg.Timestamp);
            Assert.Equal("msg-001", msg.ChatMessageId);
            Assert.True(msg.IsSystemMessage);
        }

        [Fact]
        public void ChatNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new ChatNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region FriendStatusUpdateMessage Tests

        [Fact]
        public void FriendStatusUpdateMessage_DefaultValues_AreCorrect()
        {
            var msg = new FriendStatusUpdateMessage();
            Assert.Equal(MessageType.FriendStatusUpdate, msg.Type);
            Assert.Equal(ServiceType.Social, msg.ServiceType);
            Assert.Equal(0UL, msg.FriendId);
            Assert.Equal("", msg.FriendName);
            Assert.False(msg.IsOnline);
            Assert.Equal(0L, msg.Timestamp);
        }

        [Fact]
        public void FriendStatusUpdateMessage_SetValues_RetainCorrectly()
        {
            var msg = new FriendStatusUpdateMessage
            {
                FriendId = 200UL,
                FriendName = "好友B",
                IsOnline = true,
                Timestamp = 1700000001L
            };

            Assert.Equal(200UL, msg.FriendId);
            Assert.Equal("好友B", msg.FriendName);
            Assert.True(msg.IsOnline);
            Assert.Equal(1700000001L, msg.Timestamp);
        }

        [Fact]
        public void FriendStatusUpdateMessage_ImplementsINetworkMessage()
        {
            var msg = new FriendStatusUpdateMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region FriendRequestNotifyMessage Tests

        [Fact]
        public void FriendRequestNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new FriendRequestNotifyMessage();
            Assert.Equal(MessageType.FriendRequestNotify, msg.Type);
            Assert.Equal(ServiceType.Social, msg.ServiceType);
            Assert.Equal(0UL, msg.RequesterId);
            Assert.Equal("", msg.RequesterName);
            Assert.Equal(0, msg.RequesterLevel);
            Assert.Equal("", msg.VerificationMessage);
            Assert.Equal(0L, msg.Timestamp);
        }

        [Fact]
        public void FriendRequestNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new FriendRequestNotifyMessage
            {
                RequesterId = 300UL,
                RequesterName = "侠客C",
                RequesterLevel = 50,
                VerificationMessage = "我想和你做朋友",
                Timestamp = 1700000002L
            };

            Assert.Equal(300UL, msg.RequesterId);
            Assert.Equal("侠客C", msg.RequesterName);
            Assert.Equal(50, msg.RequesterLevel);
            Assert.Equal("我想和你做朋友", msg.VerificationMessage);
            Assert.Equal(1700000002L, msg.Timestamp);
        }

        [Fact]
        public void FriendRequestNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new FriendRequestNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region ChatChannelJoinRequest Tests

        [Fact]
        public void ChatChannelJoinRequest_DefaultValues_AreCorrect()
        {
            var msg = new ChatChannelJoinRequest();
            Assert.Equal(MessageType.ChatChannelJoin, msg.Type);
            Assert.Equal(ServiceType.Chat, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(ChatChannel.World, msg.Channel);
        }

        [Fact]
        public void ChatChannelJoinRequest_SetValues_RetainCorrectly()
        {
            var msg = new ChatChannelJoinRequest
            {
                CharacterId = 400UL,
                Channel = ChatChannel.Team
            };

            Assert.Equal(400UL, msg.CharacterId);
            Assert.Equal(ChatChannel.Team, msg.Channel);
        }

        [Fact]
        public void ChatChannelJoinRequest_ImplementsINetworkMessage()
        {
            var msg = new ChatChannelJoinRequest();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region ChatChannelLeaveRequest Tests

        [Fact]
        public void ChatChannelLeaveRequest_DefaultValues_AreCorrect()
        {
            var msg = new ChatChannelLeaveRequest();
            Assert.Equal(MessageType.ChatChannelLeave, msg.Type);
            Assert.Equal(ServiceType.Chat, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(ChatChannel.World, msg.Channel);
        }

        [Fact]
        public void ChatChannelLeaveRequest_SetValues_RetainCorrectly()
        {
            var msg = new ChatChannelLeaveRequest
            {
                CharacterId = 500UL,
                Channel = ChatChannel.Guild
            };

            Assert.Equal(500UL, msg.CharacterId);
            Assert.Equal(ChatChannel.Guild, msg.Channel);
        }

        [Fact]
        public void ChatChannelLeaveRequest_ImplementsINetworkMessage()
        {
            var msg = new ChatChannelLeaveRequest();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region ChatChannel Enum Coverage

        [Fact]
        public void ChatChannel_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)ChatChannel.World);
            Assert.Equal(1, (int)ChatChannel.Nearby);
            Assert.Equal(2, (int)ChatChannel.Sect);
            Assert.Equal(3, (int)ChatChannel.Guild);
            Assert.Equal(4, (int)ChatChannel.Team);
            Assert.Equal(5, (int)ChatChannel.Private);
            Assert.Equal(6, (int)ChatChannel.System);
        }

        [Fact]
        public void ChatNotifyMessage_SupportsAllChannels()
        {
            foreach (ChatChannel channel in Enum.GetValues(typeof(ChatChannel)))
            {
                var msg = new ChatNotifyMessage { Channel = channel };
                Assert.Equal(channel, msg.Channel);
            }
        }

        #endregion

        #region Cross-Phase Compatibility Tests

        [Fact]
        public void ExistingChatMessage_StillWorks()
        {
            var msg = new ChatMessage
            {
                SenderId = 1UL,
                SenderName = "TestSender",
                Content = "Hello",
                ChannelType = ChatChannel.World
            };
            Assert.Equal(MessageType.Chat, msg.Type);
            Assert.Equal(ServiceType.Chat, msg.ServiceType);
        }

        [Fact]
        public void ExistingChatHistoryRequest_StillWorks()
        {
            var req = new ChatHistoryRequest
            {
                CharacterId = 1UL,
                ChannelType = ChatChannel.World,
                Count = 50
            };
            Assert.Equal(MessageType.Chat, req.Type);
        }

        [Fact]
        public void ExistingChatHistoryResponse_StillWorks()
        {
            var resp = new ChatHistoryResponse
            {
                HasMore = true
            };
            resp.Messages.Add(new ChatMessage { Content = "Test" });
            Assert.Single(resp.Messages);
            Assert.True(resp.HasMore);
        }

        [Fact]
        public void ExistingFriendInfo_StillWorks()
        {
            var info = new FriendInfo
            {
                FriendId = 1UL,
                FriendName = "TestFriend",
                Level = 10,
                IsOnline = true,
                Intimacy = 50
            };
            Assert.Equal(MessageType.Friend, info.Type);
            Assert.Equal(ServiceType.Social, info.ServiceType);
        }

        [Fact]
        public void ExistingAddFriendRequest_StillWorks()
        {
            var req = new AddFriendRequest
            {
                RequesterId = 1UL,
                TargetId = 2UL,
                VerificationMessage = "Add me"
            };
            Assert.Equal(MessageType.Friend, req.Type);
        }

        [Fact]
        public void ExistingFriendListUpdateMessage_StillWorks()
        {
            var msg = new FriendListUpdateMessage
            {
                CharacterId = 1UL
            };
            msg.Friends.Add(new FriendInfo { FriendId = 2UL });
            Assert.Single(msg.Friends);
        }

        #endregion

        #region MemoryPack Serialization Tests

        [Fact]
        public void ChatNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new ChatNotifyMessage
            {
                SenderId = 123UL,
                SenderName = "测试玩家",
                Channel = ChatChannel.Guild,
                Content = "这是一条测试消息",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ChatMessageId = Guid.NewGuid().ToString(),
                IsSystemMessage = false
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<ChatNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.SenderId, deserialized.SenderId);
            Assert.Equal(original.SenderName, deserialized.SenderName);
            Assert.Equal(original.Channel, deserialized.Channel);
            Assert.Equal(original.Content, deserialized.Content);
            Assert.Equal(original.Timestamp, deserialized.Timestamp);
            Assert.Equal(original.ChatMessageId, deserialized.ChatMessageId);
            Assert.Equal(original.IsSystemMessage, deserialized.IsSystemMessage);
        }

        [Fact]
        public void FriendStatusUpdateMessage_CanSerializeAndDeserialize()
        {
            var original = new FriendStatusUpdateMessage
            {
                FriendId = 456UL,
                FriendName = "好友测试",
                IsOnline = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<FriendStatusUpdateMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.FriendId, deserialized.FriendId);
            Assert.Equal(original.FriendName, deserialized.FriendName);
            Assert.Equal(original.IsOnline, deserialized.IsOnline);
            Assert.Equal(original.Timestamp, deserialized.Timestamp);
        }

        [Fact]
        public void FriendRequestNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new FriendRequestNotifyMessage
            {
                RequesterId = 789UL,
                RequesterName = "请求者",
                RequesterLevel = 30,
                VerificationMessage = "请求加好友",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<FriendRequestNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.RequesterId, deserialized.RequesterId);
            Assert.Equal(original.RequesterName, deserialized.RequesterName);
            Assert.Equal(original.RequesterLevel, deserialized.RequesterLevel);
            Assert.Equal(original.VerificationMessage, deserialized.VerificationMessage);
            Assert.Equal(original.Timestamp, deserialized.Timestamp);
        }

        [Fact]
        public void ChatChannelJoinRequest_CanSerializeAndDeserialize()
        {
            var original = new ChatChannelJoinRequest
            {
                CharacterId = 101UL,
                Channel = ChatChannel.Team
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<ChatChannelJoinRequest>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.CharacterId, deserialized.CharacterId);
            Assert.Equal(original.Channel, deserialized.Channel);
        }

        [Fact]
        public void ChatChannelLeaveRequest_CanSerializeAndDeserialize()
        {
            var original = new ChatChannelLeaveRequest
            {
                CharacterId = 102UL,
                Channel = ChatChannel.Guild
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<ChatChannelLeaveRequest>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.CharacterId, deserialized.CharacterId);
            Assert.Equal(original.Channel, deserialized.Channel);
        }

        #endregion
    }
}

using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// SocialState, GuildState, MessageChannelState 数据模型单元测试
    /// 测试社交系统、公会系统、消息频道系统的状态管理逻辑
    /// </summary>
    public class SocialSystemStateTests
    {
        #region SocialState Tests - 社交状态

        [Fact]
        public void SocialState_DefaultFriends_IsEmpty()
        {
            var state = new SocialState();
            Assert.NotNull(state.Friends);
            Assert.Empty(state.Friends);
        }

        [Fact]
        public void SocialState_DefaultFriendRequests_IsEmpty()
        {
            var state = new SocialState();
            Assert.NotNull(state.FriendRequests);
            Assert.Empty(state.FriendRequests);
        }

        [Fact]
        public void SocialState_DefaultChatHistory_IsEmpty()
        {
            var state = new SocialState();
            Assert.NotNull(state.ChatHistory);
            Assert.Empty(state.ChatHistory);
        }

        [Fact]
        public void SocialState_DefaultBlockedPlayers_IsEmpty()
        {
            var state = new SocialState();
            Assert.NotNull(state.BlockedPlayers);
            Assert.Empty(state.BlockedPlayers);
        }

        [Fact]
        public void SocialState_DefaultMaxFriends_Is100()
        {
            var state = new SocialState();
            Assert.Equal(100, state.MaxFriends);
        }

        [Fact]
        public void SocialState_DefaultMaxChatHistoryPerChannel_Is200()
        {
            var state = new SocialState();
            Assert.Equal(200, state.MaxChatHistoryPerChannel);
        }

        [Fact]
        public void SocialState_AddFriend_IncrementsCount()
        {
            var state = new SocialState();
            var friendId = Guid.NewGuid();
            state.Friends[friendId] = new FriendInfo { FriendId = 1001, FriendName = "TestFriend" };
            Assert.Single(state.Friends);
        }

        [Fact]
        public void SocialState_AddMultipleFriends_TracksAll()
        {
            var state = new SocialState();
            state.Friends[Guid.NewGuid()] = new FriendInfo { FriendId = 1001 };
            state.Friends[Guid.NewGuid()] = new FriendInfo { FriendId = 1002 };
            state.Friends[Guid.NewGuid()] = new FriendInfo { FriendId = 1003 };
            Assert.Equal(3, state.Friends.Count);
        }

        [Fact]
        public void SocialState_RemoveFriend_DecreasesCount()
        {
            var state = new SocialState();
            var friendId1 = Guid.NewGuid();
            var friendId2 = Guid.NewGuid();
            state.Friends[friendId1] = new FriendInfo { FriendId = 1001 };
            state.Friends[friendId2] = new FriendInfo { FriendId = 1002 };
            state.Friends.Remove(friendId1);
            Assert.Single(state.Friends);
        }

        [Fact]
        public void SocialState_FriendCapacityCheck_WorksCorrectly()
        {
            var state = new SocialState { MaxFriends = 2 };
            state.Friends[Guid.NewGuid()] = new FriendInfo { FriendId = 1 };
            state.Friends[Guid.NewGuid()] = new FriendInfo { FriendId = 2 };
            Assert.Equal(state.MaxFriends, state.Friends.Count);
        }

        [Fact]
        public void SocialState_AddFriendRequest_Tracked()
        {
            var state = new SocialState();
            var requestId = Guid.NewGuid();
            state.FriendRequests[requestId] = new FriendRequest
            {
                RequestId = requestId,
                RequesterId = Guid.NewGuid(),
                Message = "请加好友",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Single(state.FriendRequests);
        }

        [Fact]
        public void SocialState_HandleFriendRequest_RemovesFromList()
        {
            var state = new SocialState();
            var requestId = Guid.NewGuid();
            state.FriendRequests[requestId] = new FriendRequest { RequestId = requestId };
            state.FriendRequests.Remove(requestId);
            Assert.Empty(state.FriendRequests);
        }

        [Fact]
        public void SocialState_ChatHistory_AddMessage_WorksCorrectly()
        {
            var state = new SocialState();
            var channelKey = 0; // World channel
            state.ChatHistory[channelKey] = new List<ChatMessage>
            {
                new ChatMessage { SenderId = 1, Content = "你好", Timestamp = 1000 },
                new ChatMessage { SenderId = 2, Content = "你好世界", Timestamp = 1001 }
            };
            Assert.Equal(2, state.ChatHistory[channelKey].Count);
        }

        [Fact]
        public void SocialState_BlockPlayer_WorksCorrectly()
        {
            var state = new SocialState();
            var playerId = Guid.NewGuid();
            state.BlockedPlayers.Add(playerId);
            Assert.Contains(playerId, state.BlockedPlayers);
        }

        [Fact]
        public void SocialState_UnblockPlayer_WorksCorrectly()
        {
            var state = new SocialState();
            var playerId = Guid.NewGuid();
            state.BlockedPlayers.Add(playerId);
            state.BlockedPlayers.Remove(playerId);
            Assert.DoesNotContain(playerId, state.BlockedPlayers);
        }

        #endregion

        #region FriendRequest Tests - 好友申请

        [Fact]
        public void FriendRequest_DefaultValues_AreCorrect()
        {
            var request = new FriendRequest();
            Assert.Equal(Guid.Empty, request.RequestId);
            Assert.Equal(Guid.Empty, request.RequesterId);
            Assert.Equal(Guid.Empty, request.TargetId);
            Assert.Equal("", request.Message);
            Assert.Equal(0, request.Timestamp);
        }

        [Fact]
        public void FriendRequest_SetProperties_WorksCorrectly()
        {
            var requestId = Guid.NewGuid();
            var requesterId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var request = new FriendRequest
            {
                RequestId = requestId,
                RequesterId = requesterId,
                TargetId = targetId,
                Message = "请加好友",
                Timestamp = timestamp
            };

            Assert.Equal(requestId, request.RequestId);
            Assert.Equal(requesterId, request.RequesterId);
            Assert.Equal(targetId, request.TargetId);
            Assert.Equal("请加好友", request.Message);
            Assert.Equal(timestamp, request.Timestamp);
        }

        #endregion

        #region GuildState Tests - 公会状态

        [Fact]
        public void GuildState_DefaultValues_AreCorrect()
        {
            var state = new GuildState();
            Assert.Equal("", state.GuildName);
            Assert.Equal(Guid.Empty, state.LeaderId);
            Assert.False(state.IsCreated);
            Assert.Equal(1, state.Level);
            Assert.Equal(50, state.MaxMembers);
            Assert.Equal("", state.Declaration);
            Assert.NotNull(state.Members);
            Assert.Empty(state.Members);
            Assert.NotNull(state.Applications);
            Assert.Empty(state.Applications);
            Assert.NotNull(state.Resources);
            Assert.Empty(state.Resources);
        }

        [Fact]
        public void GuildState_CreateGuild_SetsProperties()
        {
            var state = new GuildState();
            var creatorId = Guid.NewGuid();

            state.GuildName = "风云帮";
            state.LeaderId = creatorId;
            state.IsCreated = true;
            state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.Equal("风云帮", state.GuildName);
            Assert.Equal(creatorId, state.LeaderId);
            Assert.True(state.IsCreated);
            Assert.True(state.CreateTime > 0);
        }

        [Fact]
        public void GuildState_AddMember_IncrementsCount()
        {
            var state = new GuildState();
            var memberId = Guid.NewGuid();
            state.Members[memberId] = new GuildMemberState
            {
                MemberId = memberId,
                Position = 4,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Single(state.Members);
        }

        [Fact]
        public void GuildState_AddMultipleMembers_TracksAll()
        {
            var state = new GuildState();
            for (int i = 0; i < 5; i++)
            {
                var memberId = Guid.NewGuid();
                state.Members[memberId] = new GuildMemberState { MemberId = memberId, Position = 4 };
            }
            Assert.Equal(5, state.Members.Count);
        }

        [Fact]
        public void GuildState_RemoveMember_DecreasesCount()
        {
            var state = new GuildState();
            var memberId1 = Guid.NewGuid();
            var memberId2 = Guid.NewGuid();
            state.Members[memberId1] = new GuildMemberState { MemberId = memberId1 };
            state.Members[memberId2] = new GuildMemberState { MemberId = memberId2 };
            state.Members.Remove(memberId1);
            Assert.Single(state.Members);
        }

        [Fact]
        public void GuildState_MemberCapacityCheck_WorksCorrectly()
        {
            var state = new GuildState { MaxMembers = 3 };
            state.Members[Guid.NewGuid()] = new GuildMemberState();
            state.Members[Guid.NewGuid()] = new GuildMemberState();
            state.Members[Guid.NewGuid()] = new GuildMemberState();
            Assert.Equal(state.MaxMembers, state.Members.Count);
        }

        [Fact]
        public void GuildState_AddApplication_Tracked()
        {
            var state = new GuildState();
            var appId = Guid.NewGuid();
            state.Applications[appId] = new GuildApplication
            {
                ApplicationId = appId,
                PlayerId = Guid.NewGuid(),
                Message = "请收留",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Single(state.Applications);
        }

        [Fact]
        public void GuildState_ProcessApplication_RemovesFromList()
        {
            var state = new GuildState();
            var appId = Guid.NewGuid();
            state.Applications[appId] = new GuildApplication { ApplicationId = appId };
            state.Applications.Remove(appId);
            Assert.Empty(state.Applications);
        }

        [Fact]
        public void GuildState_Resources_CanBeManaged()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;
            state.Resources["wood"] = 500;
            Assert.Equal(2, state.Resources.Count);
            Assert.Equal(1000, state.Resources["gold"]);
        }

        #endregion

        #region GuildMemberState Tests - 公会成员状态

        [Fact]
        public void GuildMemberState_DefaultValues_AreCorrect()
        {
            var member = new GuildMemberState();
            Assert.Equal(Guid.Empty, member.MemberId);
            Assert.Equal(4, member.Position); // 默认普通成员
            Assert.Equal(0, member.Contribution);
            Assert.Equal(0, member.JoinTime);
        }

        [Fact]
        public void GuildMemberState_SetProperties_WorksCorrectly()
        {
            var memberId = Guid.NewGuid();
            var member = new GuildMemberState
            {
                MemberId = memberId,
                Position = 0, // 帮主
                Contribution = 1000,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(memberId, member.MemberId);
            Assert.Equal(0, member.Position);
            Assert.Equal(1000, member.Contribution);
            Assert.True(member.JoinTime > 0);
        }

        [Fact]
        public void GuildMemberState_PositionHierarchy_ValidRange()
        {
            // 0=帮主, 1=副帮主, 2=长老, 3=精英, 4=普通成员
            var leader = new GuildMemberState { Position = 0 };
            var viceLeader = new GuildMemberState { Position = 1 };
            var elder = new GuildMemberState { Position = 2 };
            var elite = new GuildMemberState { Position = 3 };
            var member = new GuildMemberState { Position = 4 };

            Assert.True(leader.Position < viceLeader.Position);
            Assert.True(viceLeader.Position < elder.Position);
            Assert.True(elder.Position < elite.Position);
            Assert.True(elite.Position < member.Position);
        }

        #endregion

        #region GuildApplication Tests - 公会申请

        [Fact]
        public void GuildApplication_DefaultValues_AreCorrect()
        {
            var app = new GuildApplication();
            Assert.Equal(Guid.Empty, app.ApplicationId);
            Assert.Equal(Guid.Empty, app.PlayerId);
            Assert.Equal("", app.Message);
            Assert.Equal(0, app.Timestamp);
        }

        [Fact]
        public void GuildApplication_SetProperties_WorksCorrectly()
        {
            var appId = Guid.NewGuid();
            var playerId = Guid.NewGuid();
            var app = new GuildApplication
            {
                ApplicationId = appId,
                PlayerId = playerId,
                Message = "申请加入",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(appId, app.ApplicationId);
            Assert.Equal(playerId, app.PlayerId);
            Assert.Equal("申请加入", app.Message);
            Assert.True(app.Timestamp > 0);
        }

        #endregion

        #region MessageChannelState Tests - 消息频道状态

        [Fact]
        public void MessageChannelState_DefaultValues_AreCorrect()
        {
            var state = new MessageChannelState();
            Assert.NotNull(state.Subscribers);
            Assert.Empty(state.Subscribers);
            Assert.NotNull(state.RecentMessages);
            Assert.Empty(state.RecentMessages);
            Assert.Equal(100, state.MaxCachedMessages);
            Assert.Equal(0, state.TotalMessageCount);
        }

        [Fact]
        public void MessageChannelState_Subscribe_AddsPlayer()
        {
            var state = new MessageChannelState();
            state.Subscribers.Add(1001);
            state.Subscribers.Add(1002);
            Assert.Equal(2, state.Subscribers.Count);
        }

        [Fact]
        public void MessageChannelState_DuplicateSubscribe_Ignored()
        {
            var state = new MessageChannelState();
            state.Subscribers.Add(1001);
            state.Subscribers.Add(1001); // duplicate
            Assert.Single(state.Subscribers);
        }

        [Fact]
        public void MessageChannelState_Unsubscribe_RemovesPlayer()
        {
            var state = new MessageChannelState();
            state.Subscribers.Add(1001);
            state.Subscribers.Remove(1001);
            Assert.Empty(state.Subscribers);
        }

        [Fact]
        public void MessageChannelState_AddMessage_CachesCorrectly()
        {
            var state = new MessageChannelState();
            state.RecentMessages.Add(new ChatMessage { Content = "消息1", Timestamp = 1000 });
            state.RecentMessages.Add(new ChatMessage { Content = "消息2", Timestamp = 1001 });
            Assert.Equal(2, state.RecentMessages.Count);
        }

        [Fact]
        public void MessageChannelState_MessageCountTracked()
        {
            var state = new MessageChannelState();
            state.TotalMessageCount++;
            state.TotalMessageCount++;
            Assert.Equal(2, state.TotalMessageCount);
        }

        #endregion

        #region GroupChannelState Tests - 群组频道状态

        [Fact]
        public void GroupChannelState_DefaultValues_AreCorrect()
        {
            var state = new GroupChannelState();
            Assert.NotNull(state.Members);
            Assert.Empty(state.Members);
            Assert.NotNull(state.RecentMessages);
            Assert.Empty(state.RecentMessages);
            Assert.Equal(100, state.MaxCachedMessages);
        }

        [Fact]
        public void GroupChannelState_AddMember_WorksCorrectly()
        {
            var state = new GroupChannelState();
            state.Members.Add(1001);
            state.Members.Add(1002);
            Assert.Equal(2, state.Members.Count);
        }

        [Fact]
        public void GroupChannelState_RemoveMember_WorksCorrectly()
        {
            var state = new GroupChannelState();
            state.Members.Add(1001);
            state.Members.Add(1002);
            state.Members.Remove(1001);
            Assert.Single(state.Members);
            Assert.Contains(1002, state.Members);
        }

        [Fact]
        public void GroupChannelState_MessageCache_WorksCorrectly()
        {
            var state = new GroupChannelState();
            for (int i = 0; i < 5; i++)
            {
                state.RecentMessages.Add(new ChatMessage { Content = $"消息{i}", Timestamp = i });
            }
            Assert.Equal(5, state.RecentMessages.Count);
        }

        #endregion

        #region SystemChannelState Tests - 系统频道状态

        [Fact]
        public void SystemChannelState_DefaultValues_AreCorrect()
        {
            var state = new SystemChannelState();
            Assert.NotNull(state.Subscribers);
            Assert.Empty(state.Subscribers);
            Assert.NotNull(state.SystemMessages);
            Assert.Empty(state.SystemMessages);
            Assert.Equal(50, state.MaxCachedMessages);
        }

        [Fact]
        public void SystemChannelState_Subscribe_WorksCorrectly()
        {
            var state = new SystemChannelState();
            state.Subscribers.Add(1001);
            Assert.Single(state.Subscribers);
        }

        [Fact]
        public void SystemChannelState_AddSystemMessage_WorksCorrectly()
        {
            var state = new SystemChannelState();
            state.SystemMessages.Add(new ChatMessage
            {
                Content = "服务器维护公告",
                IsSystemMessage = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            Assert.Single(state.SystemMessages);
            Assert.True(state.SystemMessages[0].IsSystemMessage);
        }

        #endregion

        #region MessageRouterState Tests - 消息路由器状态

        [Fact]
        public void MessageRouterState_DefaultValues_AreCorrect()
        {
            var state = new MessageRouterState();
            Assert.Equal(0, state.TotalRoutedMessages);
            Assert.Equal(0, state.FailedRoutedMessages);
        }

        [Fact]
        public void MessageRouterState_TrackRouting_WorksCorrectly()
        {
            var state = new MessageRouterState();
            state.TotalRoutedMessages = 100;
            state.FailedRoutedMessages = 5;
            Assert.Equal(100, state.TotalRoutedMessages);
            Assert.Equal(5, state.FailedRoutedMessages);
        }

        #endregion

        #region SocialSystemMonitorState Tests - 社交系统监控状态

        [Fact]
        public void SocialSystemMonitorState_DefaultValues_AreCorrect()
        {
            var state = new SocialSystemMonitorState();
            Assert.Equal(0, state.TotalMessagesRouted);
            Assert.Equal(0, state.TotalChannels);
            Assert.Equal(0, state.ActiveUsers);
            Assert.Equal(0, state.LastResetTime);
        }

        [Fact]
        public void SocialSystemMonitorState_SetValues_WorksCorrectly()
        {
            var state = new SocialSystemMonitorState
            {
                TotalMessagesRouted = 5000,
                TotalChannels = 10,
                ActiveUsers = 200,
                LastResetTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Equal(5000, state.TotalMessagesRouted);
            Assert.Equal(10, state.TotalChannels);
            Assert.Equal(200, state.ActiveUsers);
            Assert.True(state.LastResetTime > 0);
        }

        [Fact]
        public void SocialSystemMonitorState_ResetSimulation_ClearsStats()
        {
            var state = new SocialSystemMonitorState
            {
                TotalMessagesRouted = 1000,
                TotalChannels = 5,
                ActiveUsers = 100,
                LastResetTime = 0
            };

            // Simulate reset
            state.TotalMessagesRouted = 0;
            state.TotalChannels = 0;
            state.ActiveUsers = 0;
            state.LastResetTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.Equal(0, state.TotalMessagesRouted);
            Assert.Equal(0, state.TotalChannels);
            Assert.Equal(0, state.ActiveUsers);
            Assert.True(state.LastResetTime > 0);
        }

        #endregion

        #region Friend Callback State Tests - 好友回调状态管理

        [Fact]
        public void SocialState_AddFriendCallback_AddsFriendToList()
        {
            var state = new SocialState();
            var friendGuid = Guid.NewGuid();

            var friendInfo = new FriendInfo
            {
                FriendId = BitConverter.ToUInt64(friendGuid.ToByteArray(), 0),
                IsOnline = false,
                Intimacy = 0,
                LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            state.Friends[friendGuid] = friendInfo;

            Assert.Single(state.Friends);
            Assert.True(state.Friends.ContainsKey(friendGuid));
        }

        [Fact]
        public void SocialState_RemoveFriendCallback_RemovesFriendFromList()
        {
            var state = new SocialState();
            var friendGuid = Guid.NewGuid();

            state.Friends[friendGuid] = new FriendInfo
            {
                FriendId = BitConverter.ToUInt64(friendGuid.ToByteArray(), 0),
                IsOnline = true,
                Intimacy = 50,
                LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Single(state.Friends);

            state.Friends.Remove(friendGuid);

            Assert.Empty(state.Friends);
        }

        [Fact]
        public void SocialState_FriendRequestHandled_RemovesRequest()
        {
            var state = new SocialState();
            var requestId = Guid.NewGuid();

            state.FriendRequests[requestId] = new FriendRequest
            {
                RequestId = requestId,
                RequesterId = Guid.NewGuid(),
                TargetId = Guid.NewGuid(),
                Message = "请加我好友",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Single(state.FriendRequests);

            state.FriendRequests.Remove(requestId);

            Assert.Empty(state.FriendRequests);
        }

        [Fact]
        public void SocialState_DuplicateFriendAdd_DoesNotDuplicate()
        {
            var state = new SocialState();
            var friendGuid = Guid.NewGuid();

            var friendInfo1 = new FriendInfo
            {
                FriendId = BitConverter.ToUInt64(friendGuid.ToByteArray(), 0),
                IsOnline = false,
                Intimacy = 0,
                LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // ContainsKey check should prevent duplicate
            if (!state.Friends.ContainsKey(friendGuid))
            {
                state.Friends[friendGuid] = friendInfo1;
            }

            // Try to add again - should not add
            if (!state.Friends.ContainsKey(friendGuid))
            {
                state.Friends[friendGuid] = friendInfo1;
            }

            Assert.Single(state.Friends);
        }

        [Fact]
        public void SocialState_MaxFriends_PreventsCallbackAdd()
        {
            var state = new SocialState { MaxFriends = 2 };

            // Add 2 friends to fill the list
            for (int i = 0; i < 2; i++)
            {
                var guid = Guid.NewGuid();
                state.Friends[guid] = new FriendInfo
                {
                    FriendId = (ulong)i,
                    IsOnline = false,
                    Intimacy = 0,
                    LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }

            Assert.Equal(2, state.Friends.Count);

            // Callback add should be blocked by capacity check
            var newFriendGuid = Guid.NewGuid();
            if (state.Friends.Count < state.MaxFriends)
            {
                state.Friends[newFriendGuid] = new FriendInfo();
            }

            Assert.Equal(2, state.Friends.Count);
            Assert.DoesNotContain(newFriendGuid, state.Friends.Keys);
        }

        [Fact]
        public void SocialState_RemoveNonexistentFriend_ReturnsFalse()
        {
            var state = new SocialState();
            var nonExistentGuid = Guid.NewGuid();

            var removed = state.Friends.Remove(nonExistentGuid);

            Assert.False(removed);
        }

        #endregion
    }
}

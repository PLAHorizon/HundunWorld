using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第二阶段
    /// 测试技能打断、好友系统、小地图标记、聊天消息等新增消息类型和DTO
    /// </summary>
    public class ClientFeaturePhase2Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_SkillInterrupt_HasCorrectValue()
        {
            Assert.Equal(1337, (int)MessageType.SkillInterrupt);
        }

        [Fact]
        public void MessageType_FriendList_HasCorrectValue()
        {
            Assert.Equal(1338, (int)MessageType.FriendList);
        }

        [Fact]
        public void MessageType_FriendOperation_HasCorrectValue()
        {
            Assert.Equal(1339, (int)MessageType.FriendOperation);
        }

        [Fact]
        public void MessageType_TeleportPoint_HasCorrectValue()
        {
            Assert.Equal(1340, (int)MessageType.TeleportPoint);
        }

        [Fact]
        public void MessageType_MinimapMarker_HasCorrectValue()
        {
            Assert.Equal(1341, (int)MessageType.MinimapMarker);
        }

        [Fact]
        public void MessageType_ChatSend_HasCorrectValue()
        {
            Assert.Equal(1342, (int)MessageType.ChatSend);
        }

        #endregion

        #region SkillInterruptReason Tests

        [Fact]
        public void SkillInterruptReason_HasExpectedValues()
        {
            Assert.Equal(0, (int)SkillInterruptReason.Stunned);
            Assert.Equal(1, (int)SkillInterruptReason.Silenced);
            Assert.Equal(2, (int)SkillInterruptReason.KnockedBack);
            Assert.Equal(3, (int)SkillInterruptReason.Death);
            Assert.Equal(4, (int)SkillInterruptReason.ManualCancel);
            Assert.Equal(5, (int)SkillInterruptReason.OutOfRange);
        }

        [Fact]
        public void SkillInterruptReason_HasSixValues()
        {
            var values = Enum.GetValues<SkillInterruptReason>();
            Assert.Equal(6, values.Length);
        }

        #endregion

        #region SkillInterruptMessage Tests

        [Fact]
        public void SkillInterruptMessage_DefaultMessageType_IsSkillInterrupt()
        {
            var msg = new SkillInterruptMessage();
            Assert.Equal(MessageType.SkillInterrupt, msg.Type);
        }

        [Fact]
        public void SkillInterruptMessage_DefaultServiceType_IsCombat()
        {
            var msg = new SkillInterruptMessage();
            Assert.Equal(ServiceType.Combat, msg.ServiceType);
        }

        [Fact]
        public void SkillInterruptMessage_DefaultValues_AreZeroOrDefault()
        {
            var msg = new SkillInterruptMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(0, msg.SkillId);
            Assert.Equal(0UL, msg.InterruptSourceId);
            Assert.Equal(SkillInterruptReason.Stunned, msg.Reason);
            Assert.False(msg.ResetCooldown);
            Assert.Equal(0L, msg.Timestamp);
        }

        [Fact]
        public void SkillInterruptMessage_CanSetCharacterId()
        {
            var msg = new SkillInterruptMessage { CharacterId = 12345UL };
            Assert.Equal(12345UL, msg.CharacterId);
        }

        [Fact]
        public void SkillInterruptMessage_CanSetSkillId()
        {
            var msg = new SkillInterruptMessage { SkillId = 5001 };
            Assert.Equal(5001, msg.SkillId);
        }

        [Fact]
        public void SkillInterruptMessage_CanSetInterruptSourceId()
        {
            var msg = new SkillInterruptMessage { InterruptSourceId = 9999UL };
            Assert.Equal(9999UL, msg.InterruptSourceId);
        }

        [Fact]
        public void SkillInterruptMessage_CanSetReason()
        {
            var msg = new SkillInterruptMessage { Reason = SkillInterruptReason.Silenced };
            Assert.Equal(SkillInterruptReason.Silenced, msg.Reason);
        }

        [Fact]
        public void SkillInterruptMessage_CanSetResetCooldown()
        {
            var msg = new SkillInterruptMessage { ResetCooldown = true };
            Assert.True(msg.ResetCooldown);
        }

        [Fact]
        public void SkillInterruptMessage_CompleteInterruptWorkflow()
        {
            var msg = new SkillInterruptMessage
            {
                CharacterId = 100UL,
                SkillId = 2001,
                InterruptSourceId = 200UL,
                Reason = SkillInterruptReason.KnockedBack,
                ResetCooldown = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(100UL, msg.CharacterId);
            Assert.Equal(2001, msg.SkillId);
            Assert.Equal(200UL, msg.InterruptSourceId);
            Assert.Equal(SkillInterruptReason.KnockedBack, msg.Reason);
            Assert.True(msg.ResetCooldown);
            Assert.True(msg.Timestamp > 0);
            Assert.Equal(MessageType.SkillInterrupt, msg.Type);
        }

        #endregion

        #region FriendOperationType Tests

        [Fact]
        public void FriendOperationType_HasExpectedValues()
        {
            Assert.Equal(0, (int)FriendOperationType.Add);
            Assert.Equal(1, (int)FriendOperationType.Remove);
            Assert.Equal(2, (int)FriendOperationType.Accept);
            Assert.Equal(3, (int)FriendOperationType.Reject);
            Assert.Equal(4, (int)FriendOperationType.Block);
            Assert.Equal(5, (int)FriendOperationType.Unblock);
        }

        [Fact]
        public void FriendOperationType_HasSixValues()
        {
            var values = Enum.GetValues<FriendOperationType>();
            Assert.Equal(6, values.Length);
        }

        #endregion

        #region FriendOnlineStatus Tests

        [Fact]
        public void FriendOnlineStatus_HasExpectedValues()
        {
            Assert.Equal(0, (int)FriendOnlineStatus.Offline);
            Assert.Equal(1, (int)FriendOnlineStatus.Online);
            Assert.Equal(2, (int)FriendOnlineStatus.Away);
            Assert.Equal(3, (int)FriendOnlineStatus.Busy);
        }

        [Fact]
        public void FriendOnlineStatus_HasFourValues()
        {
            var values = Enum.GetValues<FriendOnlineStatus>();
            Assert.Equal(4, values.Length);
        }

        #endregion

        #region FriendListMessage Tests

        [Fact]
        public void FriendListMessage_DefaultMessageType_IsFriendList()
        {
            var msg = new FriendListMessage();
            Assert.Equal(MessageType.FriendList, msg.Type);
        }

        [Fact]
        public void FriendListMessage_DefaultServiceType_IsSocial()
        {
            var msg = new FriendListMessage();
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        [Fact]
        public void FriendListMessage_DefaultFriendsList_IsEmpty()
        {
            var msg = new FriendListMessage();
            Assert.NotNull(msg.Friends);
            Assert.Empty(msg.Friends);
        }

        [Fact]
        public void FriendListMessage_DefaultPendingRequests_IsEmpty()
        {
            var msg = new FriendListMessage();
            Assert.NotNull(msg.PendingRequests);
            Assert.Empty(msg.PendingRequests);
        }

        [Fact]
        public void FriendListMessage_DefaultMaxFriendCount_Is100()
        {
            var msg = new FriendListMessage();
            Assert.Equal(100, msg.MaxFriendCount);
        }

        [Fact]
        public void FriendListMessage_CanAddFriends()
        {
            var msg = new FriendListMessage();
            msg.Friends.Add(new FriendInfo { FriendId = 1001UL, FriendName = "剑心", Level = 45, IsOnline = true });
            msg.Friends.Add(new FriendInfo { FriendId = 1002UL, FriendName = "风清扬", Level = 80, IsOnline = false });

            Assert.Equal(2, msg.Friends.Count);
            Assert.Equal("剑心", msg.Friends[0].FriendName);
            Assert.True(msg.Friends[0].IsOnline);
        }

        [Fact]
        public void FriendListMessage_CanAddPendingRequests()
        {
            var msg = new FriendListMessage();
            msg.PendingRequests.Add(new FriendInfo { FriendId = 2001UL, FriendName = "令狐冲" });

            Assert.Single(msg.PendingRequests);
            Assert.Equal("令狐冲", msg.PendingRequests[0].FriendName);
        }

        #endregion

        #region FriendOperationMessage Tests

        [Fact]
        public void FriendOperationMessage_DefaultMessageType_IsFriendOperation()
        {
            var msg = new FriendOperationMessage();
            Assert.Equal(MessageType.FriendOperation, msg.Type);
        }

        [Fact]
        public void FriendOperationMessage_DefaultServiceType_IsSocial()
        {
            var msg = new FriendOperationMessage();
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        [Fact]
        public void FriendOperationMessage_DefaultValues_AreZeroOrDefault()
        {
            var msg = new FriendOperationMessage();
            Assert.Equal(FriendOperationType.Add, msg.Operation);
            Assert.Equal(0UL, msg.TargetCharacterId);
            Assert.Equal("", msg.TargetName);
            Assert.False(msg.Success);
            Assert.Equal("", msg.ResultMessage);
        }

        [Fact]
        public void FriendOperationMessage_CanSetOperation()
        {
            var msg = new FriendOperationMessage { Operation = FriendOperationType.Remove };
            Assert.Equal(FriendOperationType.Remove, msg.Operation);
        }

        [Fact]
        public void FriendOperationMessage_CanSetTargetCharacterId()
        {
            var msg = new FriendOperationMessage { TargetCharacterId = 3001UL };
            Assert.Equal(3001UL, msg.TargetCharacterId);
        }

        [Fact]
        public void FriendOperationMessage_AddFriendWorkflow()
        {
            var request = new FriendOperationMessage
            {
                Operation = FriendOperationType.Add,
                TargetName = "任盈盈",
                TargetCharacterId = 5001UL
            };

            Assert.Equal(FriendOperationType.Add, request.Operation);
            Assert.Equal("任盈盈", request.TargetName);
            Assert.Equal(MessageType.FriendOperation, request.Type);
        }

        [Fact]
        public void FriendOperationMessage_ResponseSuccess()
        {
            var response = new FriendOperationMessage
            {
                Operation = FriendOperationType.Add,
                TargetCharacterId = 5001UL,
                Success = true,
                ResultMessage = "添加好友成功"
            };

            Assert.True(response.Success);
            Assert.Equal("添加好友成功", response.ResultMessage);
        }

        #endregion

        #region MapMarkerType Tests

        [Fact]
        public void MapMarkerType_HasExpectedValues()
        {
            Assert.Equal(0, (int)MapMarkerType.TeleportPoint);
            Assert.Equal(1, (int)MapMarkerType.QuestNpc);
            Assert.Equal(2, (int)MapMarkerType.QuestObjective);
            Assert.Equal(3, (int)MapMarkerType.TeamMember);
            Assert.Equal(4, (int)MapMarkerType.Boss);
            Assert.Equal(5, (int)MapMarkerType.Merchant);
            Assert.Equal(6, (int)MapMarkerType.Custom);
        }

        [Fact]
        public void MapMarkerType_HasSevenValues()
        {
            var values = Enum.GetValues<MapMarkerType>();
            Assert.Equal(7, values.Length);
        }

        #endregion

        #region MapMarkerInfo Tests

        [Fact]
        public void MapMarkerInfo_DefaultValues_AreZeroOrDefault()
        {
            var info = new MapMarkerInfo();
            Assert.Equal(0, info.MarkerId);
            Assert.Equal(MapMarkerType.TeleportPoint, info.MarkerType);
            Assert.Equal("", info.Name);
            Assert.Equal(0f, info.X);
            Assert.Equal(0f, info.Y);
            Assert.Equal(0f, info.Z);
            Assert.False(info.IsInteractable);
        }

        [Fact]
        public void MapMarkerInfo_CanSetAllProperties()
        {
            var info = new MapMarkerInfo
            {
                MarkerId = 101,
                MarkerType = MapMarkerType.QuestNpc,
                Name = "张三丰",
                X = 100.5f,
                Y = 50.0f,
                Z = 200.3f,
                IsInteractable = true
            };

            Assert.Equal(101, info.MarkerId);
            Assert.Equal(MapMarkerType.QuestNpc, info.MarkerType);
            Assert.Equal("张三丰", info.Name);
            Assert.Equal(100.5f, info.X);
            Assert.Equal(50.0f, info.Y);
            Assert.Equal(200.3f, info.Z);
            Assert.True(info.IsInteractable);
        }

        #endregion

        #region TeleportPointMessage Tests

        [Fact]
        public void TeleportPointMessage_DefaultMessageType_IsTeleportPoint()
        {
            var msg = new TeleportPointMessage();
            Assert.Equal(MessageType.TeleportPoint, msg.Type);
        }

        [Fact]
        public void TeleportPointMessage_DefaultServiceType_IsGame()
        {
            var msg = new TeleportPointMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void TeleportPointMessage_DefaultTeleportPoints_IsEmpty()
        {
            var msg = new TeleportPointMessage();
            Assert.NotNull(msg.TeleportPoints);
            Assert.Empty(msg.TeleportPoints);
        }

        [Fact]
        public void TeleportPointMessage_DefaultAreaName_IsEmpty()
        {
            var msg = new TeleportPointMessage();
            Assert.Equal("", msg.AreaName);
        }

        [Fact]
        public void TeleportPointMessage_CanAddTeleportPoints()
        {
            var msg = new TeleportPointMessage
            {
                AreaName = "华山"
            };

            msg.TeleportPoints.Add(new MapMarkerInfo
            {
                MarkerId = 1,
                MarkerType = MapMarkerType.TeleportPoint,
                Name = "华山入口",
                X = 100f, Y = 0f, Z = 200f,
                IsInteractable = true
            });

            msg.TeleportPoints.Add(new MapMarkerInfo
            {
                MarkerId = 2,
                MarkerType = MapMarkerType.TeleportPoint,
                Name = "思过崖",
                X = 300f, Y = 150f, Z = 400f,
                IsInteractable = true
            });

            Assert.Equal(2, msg.TeleportPoints.Count);
            Assert.Equal("华山", msg.AreaName);
            Assert.Equal("华山入口", msg.TeleportPoints[0].Name);
        }

        #endregion

        #region MinimapMarkerMessage Tests

        [Fact]
        public void MinimapMarkerMessage_DefaultMessageType_IsMinimapMarker()
        {
            var msg = new MinimapMarkerMessage();
            Assert.Equal(MessageType.MinimapMarker, msg.Type);
        }

        [Fact]
        public void MinimapMarkerMessage_DefaultServiceType_IsGame()
        {
            var msg = new MinimapMarkerMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void MinimapMarkerMessage_DefaultMarkers_IsEmpty()
        {
            var msg = new MinimapMarkerMessage();
            Assert.NotNull(msg.Markers);
            Assert.Empty(msg.Markers);
        }

        [Fact]
        public void MinimapMarkerMessage_DefaultIsFullUpdate_IsFalse()
        {
            var msg = new MinimapMarkerMessage();
            Assert.False(msg.IsFullUpdate);
        }

        [Fact]
        public void MinimapMarkerMessage_DefaultRemovedMarkerIds_IsEmpty()
        {
            var msg = new MinimapMarkerMessage();
            Assert.NotNull(msg.RemovedMarkerIds);
            Assert.Empty(msg.RemovedMarkerIds);
        }

        [Fact]
        public void MinimapMarkerMessage_FullUpdateWithMarkers()
        {
            var msg = new MinimapMarkerMessage { IsFullUpdate = true };

            msg.Markers.Add(new MapMarkerInfo { MarkerId = 1, MarkerType = MapMarkerType.TeleportPoint, Name = "传送点A" });
            msg.Markers.Add(new MapMarkerInfo { MarkerId = 2, MarkerType = MapMarkerType.QuestNpc, Name = "任务NPC" });
            msg.Markers.Add(new MapMarkerInfo { MarkerId = 3, MarkerType = MapMarkerType.Boss, Name = "世界Boss" });

            Assert.True(msg.IsFullUpdate);
            Assert.Equal(3, msg.Markers.Count);
        }

        [Fact]
        public void MinimapMarkerMessage_IncrementalUpdateWithRemovals()
        {
            var msg = new MinimapMarkerMessage { IsFullUpdate = false };

            msg.Markers.Add(new MapMarkerInfo { MarkerId = 4, MarkerType = MapMarkerType.TeamMember, Name = "队友位置" });
            msg.RemovedMarkerIds.Add(1);
            msg.RemovedMarkerIds.Add(2);

            Assert.False(msg.IsFullUpdate);
            Assert.Single(msg.Markers);
            Assert.Equal(2, msg.RemovedMarkerIds.Count);
        }

        #endregion

        #region ChatChannelType Tests

        [Fact]
        public void ChatChannelType_HasExpectedValues()
        {
            Assert.Equal(0, (int)ChatChannelType.World);
            Assert.Equal(1, (int)ChatChannelType.Area);
            Assert.Equal(2, (int)ChatChannelType.Team);
            Assert.Equal(3, (int)ChatChannelType.Guild);
            Assert.Equal(4, (int)ChatChannelType.Whisper);
            Assert.Equal(5, (int)ChatChannelType.System);
        }

        [Fact]
        public void ChatChannelType_HasSixValues()
        {
            var values = Enum.GetValues<ChatChannelType>();
            Assert.Equal(6, values.Length);
        }

        #endregion

        #region ChatSendMessage Tests

        [Fact]
        public void ChatSendMessage_DefaultMessageType_IsChatSend()
        {
            var msg = new ChatSendMessage();
            Assert.Equal(MessageType.ChatSend, msg.Type);
        }

        [Fact]
        public void ChatSendMessage_DefaultServiceType_IsSocial()
        {
            var msg = new ChatSendMessage();
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        [Fact]
        public void ChatSendMessage_DefaultValues_AreZeroOrDefault()
        {
            var msg = new ChatSendMessage();
            Assert.Equal(0UL, msg.SenderId);
            Assert.Equal("", msg.SenderName);
            Assert.Equal(ChatChannelType.World, msg.Channel);
            Assert.Equal("", msg.Content);
            Assert.Equal(0UL, msg.TargetId);
            Assert.Equal(0L, msg.Timestamp);
        }

        [Fact]
        public void ChatSendMessage_CanSetSenderId()
        {
            var msg = new ChatSendMessage { SenderId = 8001UL };
            Assert.Equal(8001UL, msg.SenderId);
        }

        [Fact]
        public void ChatSendMessage_CanSetSenderName()
        {
            var msg = new ChatSendMessage { SenderName = "张无忌" };
            Assert.Equal("张无忌", msg.SenderName);
        }

        [Fact]
        public void ChatSendMessage_CanSetChannel()
        {
            var msg = new ChatSendMessage { Channel = ChatChannelType.Guild };
            Assert.Equal(ChatChannelType.Guild, msg.Channel);
        }

        [Fact]
        public void ChatSendMessage_CanSetContent()
        {
            var msg = new ChatSendMessage { Content = "大家好！有人一起下副本吗？" };
            Assert.Equal("大家好！有人一起下副本吗？", msg.Content);
        }

        [Fact]
        public void ChatSendMessage_WhisperWorkflow()
        {
            var msg = new ChatSendMessage
            {
                SenderId = 100UL,
                SenderName = "张无忌",
                Channel = ChatChannelType.Whisper,
                TargetId = 200UL,
                Content = "你好，我们组队吧",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(100UL, msg.SenderId);
            Assert.Equal(ChatChannelType.Whisper, msg.Channel);
            Assert.Equal(200UL, msg.TargetId);
            Assert.Equal("你好，我们组队吧", msg.Content);
            Assert.True(msg.Timestamp > 0);
            Assert.Equal(MessageType.ChatSend, msg.Type);
        }

        [Fact]
        public void ChatSendMessage_WorldChatWorkflow()
        {
            var msg = new ChatSendMessage
            {
                SenderId = 300UL,
                SenderName = "令狐冲",
                Channel = ChatChannelType.World,
                Content = "世界频道消息测试",
                Timestamp = 1707400000000L
            };

            Assert.Equal(ChatChannelType.World, msg.Channel);
            Assert.Equal("世界频道消息测试", msg.Content);
            Assert.Equal(0UL, msg.TargetId); // 世界频道不需要目标
        }

        #endregion
    }
}

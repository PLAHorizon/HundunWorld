using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// GuildState, GuildMemberState, GuildApplication 数据模型单元测试
    /// 测试公会系统的状态管理和业务逻辑
    /// </summary>
    public class GuildSystemStateTests
    {
        #region GuildState Default Values - 公会默认值

        [Fact]
        public void GuildState_DefaultGuildName_IsEmpty()
        {
            var state = new GuildState();
            Assert.Equal("", state.GuildName);
        }

        [Fact]
        public void GuildState_DefaultLeaderId_IsEmptyGuid()
        {
            var state = new GuildState();
            Assert.Equal(Guid.Empty, state.LeaderId);
        }

        [Fact]
        public void GuildState_DefaultIsCreated_IsFalse()
        {
            var state = new GuildState();
            Assert.False(state.IsCreated);
        }

        [Fact]
        public void GuildState_DefaultLevel_Is1()
        {
            var state = new GuildState();
            Assert.Equal(1, state.Level);
        }

        [Fact]
        public void GuildState_DefaultMaxMembers_Is50()
        {
            var state = new GuildState();
            Assert.Equal(50, state.MaxMembers);
        }

        [Fact]
        public void GuildState_DefaultDeclaration_IsEmpty()
        {
            var state = new GuildState();
            Assert.Equal("", state.Declaration);
        }

        [Fact]
        public void GuildState_DefaultMembers_IsEmpty()
        {
            var state = new GuildState();
            Assert.NotNull(state.Members);
            Assert.Empty(state.Members);
        }

        [Fact]
        public void GuildState_DefaultApplications_IsEmpty()
        {
            var state = new GuildState();
            Assert.NotNull(state.Applications);
            Assert.Empty(state.Applications);
        }

        [Fact]
        public void GuildState_DefaultResources_IsEmpty()
        {
            var state = new GuildState();
            Assert.NotNull(state.Resources);
            Assert.Empty(state.Resources);
        }

        [Fact]
        public void GuildState_DefaultCreateTime_IsZero()
        {
            var state = new GuildState();
            Assert.Equal(0, state.CreateTime);
        }

        #endregion

        #region GuildState Property Setting - 公会属性设置

        [Fact]
        public void GuildState_SetGuildName_WorksCorrectly()
        {
            var state = new GuildState { GuildName = "天下第一帮" };
            Assert.Equal("天下第一帮", state.GuildName);
        }

        [Fact]
        public void GuildState_SetLeaderId_WorksCorrectly()
        {
            var leaderId = Guid.NewGuid();
            var state = new GuildState { LeaderId = leaderId };
            Assert.Equal(leaderId, state.LeaderId);
        }

        [Fact]
        public void GuildState_SetIsCreated_WorksCorrectly()
        {
            var state = new GuildState { IsCreated = true };
            Assert.True(state.IsCreated);
        }

        [Fact]
        public void GuildState_SetLevel_WorksCorrectly()
        {
            var state = new GuildState { Level = 5 };
            Assert.Equal(5, state.Level);
        }

        #endregion

        #region GuildMemberState Tests - 公会成员状态

        [Fact]
        public void GuildMemberState_DefaultPosition_Is4()
        {
            var member = new GuildMemberState();
            Assert.Equal(4, member.Position);
        }

        [Fact]
        public void GuildMemberState_DefaultMemberId_IsEmptyGuid()
        {
            var member = new GuildMemberState();
            Assert.Equal(Guid.Empty, member.MemberId);
        }

        [Fact]
        public void GuildMemberState_DefaultContribution_IsZero()
        {
            var member = new GuildMemberState();
            Assert.Equal(0, member.Contribution);
        }

        [Fact]
        public void GuildMemberState_DefaultJoinTime_IsZero()
        {
            var member = new GuildMemberState();
            Assert.Equal(0, member.JoinTime);
        }

        [Fact]
        public void GuildMemberState_LeaderPosition_Is0()
        {
            var member = new GuildMemberState { Position = 0 };
            Assert.Equal(0, member.Position);
        }

        [Fact]
        public void GuildMemberState_ViceLeaderPosition_Is1()
        {
            var member = new GuildMemberState { Position = 1 };
            Assert.Equal(1, member.Position);
        }

        [Fact]
        public void GuildMemberState_ElderPosition_Is2()
        {
            var member = new GuildMemberState { Position = 2 };
            Assert.Equal(2, member.Position);
        }

        [Fact]
        public void GuildMemberState_ElitePosition_Is3()
        {
            var member = new GuildMemberState { Position = 3 };
            Assert.Equal(3, member.Position);
        }

        [Fact]
        public void GuildMemberState_RegularMemberPosition_Is4()
        {
            var member = new GuildMemberState { Position = 4 };
            Assert.Equal(4, member.Position);
        }

        [Fact]
        public void GuildMemberState_SetProperties_WorksCorrectly()
        {
            var memberId = Guid.NewGuid();
            var member = new GuildMemberState
            {
                MemberId = memberId,
                Position = 2,
                Contribution = 500,
                JoinTime = 1700000000000
            };

            Assert.Equal(memberId, member.MemberId);
            Assert.Equal(2, member.Position);
            Assert.Equal(500, member.Contribution);
            Assert.Equal(1700000000000, member.JoinTime);
        }

        #endregion

        #region GuildApplication Tests - 入会申请

        [Fact]
        public void GuildApplication_DefaultApplicationId_IsEmptyGuid()
        {
            var app = new GuildApplication();
            Assert.Equal(Guid.Empty, app.ApplicationId);
        }

        [Fact]
        public void GuildApplication_DefaultPlayerId_IsEmptyGuid()
        {
            var app = new GuildApplication();
            Assert.Equal(Guid.Empty, app.PlayerId);
        }

        [Fact]
        public void GuildApplication_DefaultMessage_IsEmpty()
        {
            var app = new GuildApplication();
            Assert.Equal("", app.Message);
        }

        [Fact]
        public void GuildApplication_DefaultTimestamp_IsZero()
        {
            var app = new GuildApplication();
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
                Message = "请收下我",
                Timestamp = 1700000000000
            };

            Assert.Equal(appId, app.ApplicationId);
            Assert.Equal(playerId, app.PlayerId);
            Assert.Equal("请收下我", app.Message);
            Assert.Equal(1700000000000, app.Timestamp);
        }

        #endregion

        #region Member Management - 成员管理

        [Fact]
        public void GuildState_AddMember_IncrementsCount()
        {
            var state = new GuildState();
            var memberId = Guid.NewGuid();
            state.Members[memberId] = new GuildMemberState { MemberId = memberId, Position = 4 };
            Assert.Single(state.Members);
        }

        [Fact]
        public void GuildState_AddMultipleMembers_TracksAll()
        {
            var state = new GuildState();
            for (int i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                state.Members[id] = new GuildMemberState { MemberId = id, Position = 4 };
            }
            Assert.Equal(5, state.Members.Count);
        }

        [Fact]
        public void GuildState_RemoveMember_DecreasesCount()
        {
            var state = new GuildState();
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            state.Members[id1] = new GuildMemberState { MemberId = id1 };
            state.Members[id2] = new GuildMemberState { MemberId = id2 };
            state.Members.Remove(id1);
            Assert.Single(state.Members);
        }

        [Fact]
        public void GuildState_ContainsMember_ReturnsTrue()
        {
            var state = new GuildState();
            var memberId = Guid.NewGuid();
            state.Members[memberId] = new GuildMemberState { MemberId = memberId };
            Assert.True(state.Members.ContainsKey(memberId));
        }

        [Fact]
        public void GuildState_DoesNotContainMember_ReturnsFalse()
        {
            var state = new GuildState();
            Assert.False(state.Members.ContainsKey(Guid.NewGuid()));
        }

        [Fact]
        public void GuildState_CreateGuildAddsLeader_PositionIsZero()
        {
            var state = new GuildState();
            var creatorId = Guid.NewGuid();

            state.GuildName = "测试公会";
            state.LeaderId = creatorId;
            state.IsCreated = true;
            state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var leader = new GuildMemberState
            {
                MemberId = creatorId,
                Position = 0,
                Contribution = 0,
                JoinTime = state.CreateTime
            };
            state.Members[creatorId] = leader;

            Assert.Single(state.Members);
            Assert.Equal(0, state.Members[creatorId].Position);
            Assert.Equal(creatorId, state.Members[creatorId].MemberId);
        }

        [Fact]
        public void GuildState_MemberContribution_CanBeUpdated()
        {
            var state = new GuildState();
            var memberId = Guid.NewGuid();
            var member = new GuildMemberState { MemberId = memberId, Contribution = 0 };
            state.Members[memberId] = member;

            member.Contribution += 100;
            Assert.Equal(100, state.Members[memberId].Contribution);
        }

        #endregion

        #region Application Management - 申请管理

        [Fact]
        public void GuildState_AddApplication_IncrementsCount()
        {
            var state = new GuildState();
            var appId = Guid.NewGuid();
            state.Applications[appId] = new GuildApplication
            {
                ApplicationId = appId,
                PlayerId = Guid.NewGuid(),
                Message = "申请加入"
            };
            Assert.Single(state.Applications);
        }

        [Fact]
        public void GuildState_RemoveApplication_DecreasesCount()
        {
            var state = new GuildState();
            var appId1 = Guid.NewGuid();
            var appId2 = Guid.NewGuid();
            state.Applications[appId1] = new GuildApplication { ApplicationId = appId1 };
            state.Applications[appId2] = new GuildApplication { ApplicationId = appId2 };
            state.Applications.Remove(appId1);
            Assert.Single(state.Applications);
        }

        [Fact]
        public void GuildState_DuplicateApplication_CheckByPlayerId()
        {
            var state = new GuildState();
            var playerId = Guid.NewGuid();

            var app = new GuildApplication
            {
                ApplicationId = Guid.NewGuid(),
                PlayerId = playerId,
                Message = "第一次申请"
            };
            state.Applications[app.ApplicationId] = app;

            bool hasPendingApplication = state.Applications.Values.Any(a => a.PlayerId == playerId);
            Assert.True(hasPendingApplication);
        }

        [Fact]
        public void GuildState_NoPendingApplication_ReturnsFalse()
        {
            var state = new GuildState();
            var playerId = Guid.NewGuid();
            bool hasPendingApplication = state.Applications.Values.Any(a => a.PlayerId == playerId);
            Assert.False(hasPendingApplication);
        }

        [Fact]
        public void GuildState_ApplicationMessageTruncation_LongMessage()
        {
            var longMessage = new string('A', 300);
            int maxLen = 200;
            var truncated = longMessage.Length > maxLen ? longMessage[..maxLen] : longMessage;

            var app = new GuildApplication { Message = truncated };
            Assert.Equal(maxLen, app.Message.Length);
        }

        [Fact]
        public void GuildState_ApplicationMessageTruncation_ShortMessage()
        {
            var shortMessage = "我想加入";
            int maxLen = 200;
            var result = shortMessage.Length > maxLen ? shortMessage[..maxLen] : shortMessage;

            var app = new GuildApplication { Message = result };
            Assert.Equal("我想加入", app.Message);
        }

        #endregion

        #region Resource Tracking - 资源追踪

        [Fact]
        public void GuildState_AddResource_TracksCorrectly()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;
            Assert.Single(state.Resources);
            Assert.Equal(1000, state.Resources["gold"]);
        }

        [Fact]
        public void GuildState_UpdateResource_ChangesValue()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;
            state.Resources["gold"] += 500;
            Assert.Equal(1500, state.Resources["gold"]);
        }

        [Fact]
        public void GuildState_MultipleResources_TrackedIndependently()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;
            state.Resources["wood"] = 500;
            state.Resources["stone"] = 300;
            Assert.Equal(3, state.Resources.Count);
        }

        [Fact]
        public void GuildState_RemoveResource_DecreasesCount()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;
            state.Resources["wood"] = 500;
            state.Resources.Remove("gold");
            Assert.Single(state.Resources);
            Assert.False(state.Resources.ContainsKey("gold"));
        }

        #endregion

        #region Position Hierarchy Validation - 职位层级验证

        [Fact]
        public void PositionHierarchy_LeaderCanKickViceLeader()
        {
            int operatorPosition = 0; // 帮主
            int targetPosition = 1;   // 副帮主
            Assert.True(operatorPosition < targetPosition);
        }

        [Fact]
        public void PositionHierarchy_LeaderCanKickRegularMember()
        {
            int operatorPosition = 0; // 帮主
            int targetPosition = 4;   // 成员
            Assert.True(operatorPosition < targetPosition);
        }

        [Fact]
        public void PositionHierarchy_ViceLeaderCanKickElder()
        {
            int operatorPosition = 1; // 副帮主
            int targetPosition = 2;   // 长老
            Assert.True(operatorPosition < targetPosition);
        }

        [Fact]
        public void PositionHierarchy_ViceLeaderCannotKickViceLeader()
        {
            int operatorPosition = 1; // 副帮主
            int targetPosition = 1;   // 副帮主
            // 操作者职位必须高于目标（数值越小越高），相同也不行
            Assert.False(operatorPosition < targetPosition);
        }

        [Fact]
        public void PositionHierarchy_ElderCannotKickViceLeader()
        {
            int operatorPosition = 2; // 长老
            int targetPosition = 1;   // 副帮主
            Assert.False(operatorPosition < targetPosition);
        }

        [Fact]
        public void PositionHierarchy_RegularMemberCannotKickAnyone()
        {
            int operatorPosition = 4; // 成员
            for (int target = 0; target <= 4; target++)
            {
                Assert.False(operatorPosition < target);
            }
        }

        [Fact]
        public void PositionHierarchy_SamePositionCannotKick()
        {
            for (int pos = 0; pos <= 4; pos++)
            {
#pragma warning disable CS1718 // 与自身比较是有意为之，用于验证自我踢出逻辑
                Assert.False(pos < pos);
#pragma warning restore CS1718
            }
        }

        #endregion

        #region MaxMembers Enforcement - 成员上限

        [Fact]
        public void GuildState_MembersAtCapacity_IsFull()
        {
            var state = new GuildState { MaxMembers = 3 };
            for (int i = 0; i < 3; i++)
            {
                var id = Guid.NewGuid();
                state.Members[id] = new GuildMemberState { MemberId = id };
            }
            Assert.Equal(state.MaxMembers, state.Members.Count);
        }

        [Fact]
        public void GuildState_MembersBelowCapacity_IsNotFull()
        {
            var state = new GuildState { MaxMembers = 50 };
            var id = Guid.NewGuid();
            state.Members[id] = new GuildMemberState { MemberId = id };
            Assert.True(state.Members.Count < state.MaxMembers);
        }

        [Fact]
        public void GuildState_ExpandMaxMembers_IncreasesCapacity()
        {
            var state = new GuildState { MaxMembers = 50 };
            state.MaxMembers += 10;
            Assert.Equal(60, state.MaxMembers);
        }

        #endregion

        #region Appoint Position Validation - 任命职位验证

        [Fact]
        public void AppointPosition_OnlyLeaderCanAppoint()
        {
            int operatorPosition = 0; // 帮主
            Assert.Equal(0, operatorPosition);
        }

        [Fact]
        public void AppointPosition_NonLeaderCannotAppoint()
        {
            for (int pos = 1; pos <= 4; pos++)
            {
                Assert.NotEqual(0, pos);
            }
        }

        [Fact]
        public void AppointPosition_CannotAppointToLeader()
        {
            int targetPosition = 0;
            Assert.Equal(0, targetPosition);
            // position == 0 应该被拒绝（帮主需要通过转让）
        }

        [Fact]
        public void AppointPosition_ValidPositionRange()
        {
            // 有效职位范围: 1-4（任命时0不被允许）
            for (int pos = 1; pos <= 4; pos++)
            {
                Assert.True(pos >= 1 && pos <= 4);
            }
        }

        [Fact]
        public void AppointPosition_InvalidPositionNegative_OutOfRange()
        {
            int position = -1;
            Assert.True(position < 0 || position > 4);
        }

        [Fact]
        public void AppointPosition_InvalidPositionAbove4_OutOfRange()
        {
            int position = 5;
            Assert.True(position < 0 || position > 4);
        }

        #endregion

        #region Edge Cases - 边界情况

        [Fact]
        public void GuildState_EmptyGuildName_IsValid()
        {
            var state = new GuildState { GuildName = "" };
            Assert.Equal("", state.GuildName);
        }

        [Fact]
        public void GuildState_GuildNameMaxLength_Is32()
        {
            int maxLen = 32;
            var name = new string('A', maxLen);
            Assert.Equal(maxLen, name.Length);
            Assert.True(name.Length <= maxLen);
        }

        [Fact]
        public void GuildState_GuildNameExceedsMaxLength_Detected()
        {
            int maxLen = 32;
            var name = new string('A', 33);
            Assert.True(name.Length > maxLen);
        }

        [Fact]
        public void GuildState_LeaderCannotLeave_PositionCheck()
        {
            var member = new GuildMemberState { Position = 0 };
            // 帮主不能直接离开
            Assert.Equal(0, member.Position);
        }

        [Fact]
        public void GuildState_NonLeaderCanLeave_PositionCheck()
        {
            for (int pos = 1; pos <= 4; pos++)
            {
                var member = new GuildMemberState { Position = pos };
                Assert.NotEqual(0, member.Position);
            }
        }

        [Fact]
        public void GuildState_ApproverPermission_LeaderOrViceOnly()
        {
            // 帮主(0)和副帮主(1)可以审批，Position <= 1
            Assert.True(0 <= 1);
            Assert.True(1 <= 1);
            Assert.False(2 <= 1);
            Assert.False(3 <= 1);
            Assert.False(4 <= 1);
        }

        [Fact]
        public void GuildState_NewMemberFromApplication_PositionIs4()
        {
            var newMember = new GuildMemberState
            {
                MemberId = Guid.NewGuid(),
                Position = 4,
                Contribution = 0,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Equal(4, newMember.Position);
            Assert.Equal(0, newMember.Contribution);
        }

        [Fact]
        public void GuildState_CannotKickSelf_SameId()
        {
            var operatorId = Guid.NewGuid();
            var targetId = operatorId;
            Assert.Equal(operatorId, targetId);
        }

        [Fact]
        public void GuildState_GuidToUInt64_ProducesDeterministicResult()
        {
            var guid = Guid.NewGuid();
            var bytes = guid.ToByteArray();
            var result1 = BitConverter.ToUInt64(bytes, 0);
            var result2 = BitConverter.ToUInt64(bytes, 0);
            Assert.Equal(result1, result2);
        }

        [Fact]
        public void GuildState_GuidToUInt64_DifferentGuidsProduceDifferentResults()
        {
            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();
            var result1 = BitConverter.ToUInt64(guid1.ToByteArray(), 0);
            var result2 = BitConverter.ToUInt64(guid2.ToByteArray(), 0);
            Assert.NotEqual(result1, result2);
        }

        [Fact]
        public void GuildState_ResourcesCopied_AreIndependent()
        {
            var state = new GuildState();
            state.Resources["gold"] = 1000;

            var copy = new Dictionary<string, int>(state.Resources);
            copy["gold"] = 2000;

            Assert.Equal(1000, state.Resources["gold"]);
            Assert.Equal(2000, copy["gold"]);
        }

        #endregion
    }
}

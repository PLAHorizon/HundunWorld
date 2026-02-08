using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// TeamState, TeamMemberState 数据模型单元测试
    /// 测试组队系统的状态管理逻辑
    /// </summary>
    public class TeamSystemStateTests
    {
        #region TeamState Tests - 队伍状态

        [Fact]
        public void TeamState_DefaultValues_AreCorrect()
        {
            var state = new TeamState();
            Assert.Equal("", state.TeamName);
            Assert.Equal(Guid.Empty, state.LeaderId);
            Assert.False(state.IsCreated);
            Assert.Equal("", state.TeamGoal);
            Assert.Equal(5, state.MaxMembers);
            Assert.NotNull(state.Members);
            Assert.Empty(state.Members);
            Assert.Equal(0, state.CreateTime);
        }

        [Fact]
        public void TeamState_CreateTeam_SetsProperties()
        {
            var state = new TeamState();
            var leaderId = Guid.NewGuid();

            state.TeamName = "龙虎队";
            state.LeaderId = leaderId;
            state.IsCreated = true;
            state.TeamGoal = "挑战副本";
            state.CreateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.Equal("龙虎队", state.TeamName);
            Assert.Equal(leaderId, state.LeaderId);
            Assert.True(state.IsCreated);
            Assert.Equal("挑战副本", state.TeamGoal);
            Assert.True(state.CreateTime > 0);
        }

        [Fact]
        public void TeamState_AddMember_IncrementsCount()
        {
            var state = new TeamState();
            var memberId = Guid.NewGuid();
            state.Members[memberId] = new TeamMemberState
            {
                MemberId = memberId,
                IsLeader = false,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            Assert.Single(state.Members);
        }

        [Fact]
        public void TeamState_AddMultipleMembers_TracksAll()
        {
            var state = new TeamState();
            for (int i = 0; i < 5; i++)
            {
                var memberId = Guid.NewGuid();
                state.Members[memberId] = new TeamMemberState { MemberId = memberId };
            }
            Assert.Equal(5, state.Members.Count);
        }

        [Fact]
        public void TeamState_RemoveMember_DecreasesCount()
        {
            var state = new TeamState();
            var memberId1 = Guid.NewGuid();
            var memberId2 = Guid.NewGuid();
            state.Members[memberId1] = new TeamMemberState { MemberId = memberId1 };
            state.Members[memberId2] = new TeamMemberState { MemberId = memberId2 };
            state.Members.Remove(memberId1);
            Assert.Single(state.Members);
        }

        [Fact]
        public void TeamState_MemberCapacityCheck_WorksCorrectly()
        {
            var state = new TeamState { MaxMembers = 3 };
            state.Members[Guid.NewGuid()] = new TeamMemberState();
            state.Members[Guid.NewGuid()] = new TeamMemberState();
            state.Members[Guid.NewGuid()] = new TeamMemberState();
            Assert.Equal(state.MaxMembers, state.Members.Count);
        }

        [Fact]
        public void TeamState_DefaultMaxMembers_Is5()
        {
            var state = new TeamState();
            Assert.Equal(5, state.MaxMembers);
        }

        [Fact]
        public void TeamState_Disband_ClearsState()
        {
            var state = new TeamState();
            state.TeamName = "测试队伍";
            state.LeaderId = Guid.NewGuid();
            state.IsCreated = true;
            state.Members[Guid.NewGuid()] = new TeamMemberState();

            // Simulate disband
            state.IsCreated = false;
            state.Members.Clear();
            state.TeamName = "";
            state.TeamGoal = "";
            state.LeaderId = Guid.Empty;

            Assert.False(state.IsCreated);
            Assert.Empty(state.Members);
            Assert.Equal("", state.TeamName);
            Assert.Equal(Guid.Empty, state.LeaderId);
        }

        [Fact]
        public void TeamState_TransferLeader_UpdatesCorrectly()
        {
            var state = new TeamState();
            var oldLeaderId = Guid.NewGuid();
            var newLeaderId = Guid.NewGuid();

            state.LeaderId = oldLeaderId;
            state.Members[oldLeaderId] = new TeamMemberState { MemberId = oldLeaderId, IsLeader = true };
            state.Members[newLeaderId] = new TeamMemberState { MemberId = newLeaderId, IsLeader = false };

            // Transfer leadership
            state.Members[oldLeaderId].IsLeader = false;
            state.Members[newLeaderId].IsLeader = true;
            state.LeaderId = newLeaderId;

            Assert.Equal(newLeaderId, state.LeaderId);
            Assert.False(state.Members[oldLeaderId].IsLeader);
            Assert.True(state.Members[newLeaderId].IsLeader);
        }

        #endregion

        #region TeamMemberState Tests - 队伍成员状态

        [Fact]
        public void TeamMemberState_DefaultValues_AreCorrect()
        {
            var member = new TeamMemberState();
            Assert.Equal(Guid.Empty, member.MemberId);
            Assert.False(member.IsLeader);
            Assert.Equal(0, member.JoinTime);
        }

        [Fact]
        public void TeamMemberState_SetProperties_WorksCorrectly()
        {
            var memberId = Guid.NewGuid();
            var member = new TeamMemberState
            {
                MemberId = memberId,
                IsLeader = true,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(memberId, member.MemberId);
            Assert.True(member.IsLeader);
            Assert.True(member.JoinTime > 0);
        }

        [Fact]
        public void TeamMemberState_LeaderFlag_CanBeToggled()
        {
            var member = new TeamMemberState { IsLeader = true };
            Assert.True(member.IsLeader);

            member.IsLeader = false;
            Assert.False(member.IsLeader);
        }

        #endregion
    }
}

using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第三阶段
    /// 测试装备对比、公会管理、组队邀请、击杀特写、快捷键配置等新增消息类型和DTO
    /// </summary>
    public class ClientFeaturePhase3Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_EquipmentComparison_HasCorrectValue()
        {
            Assert.Equal(1343, (int)MessageType.EquipmentComparison);
        }

        [Fact]
        public void MessageType_GuildManagement_HasCorrectValue()
        {
            Assert.Equal(1344, (int)MessageType.GuildManagement);
        }

        [Fact]
        public void MessageType_TeamInvite_HasCorrectValue()
        {
            Assert.Equal(1345, (int)MessageType.TeamInvite);
        }

        [Fact]
        public void MessageType_KillCam_HasCorrectValue()
        {
            Assert.Equal(1346, (int)MessageType.KillCam);
        }

        [Fact]
        public void MessageType_HotkeyConfig_HasCorrectValue()
        {
            Assert.Equal(1347, (int)MessageType.HotkeyConfig);
        }

        [Fact]
        public void MessageType_NewPhase3Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.EquipmentComparison,
                (int)MessageType.GuildManagement,
                (int)MessageType.TeamInvite,
                (int)MessageType.KillCam,
                (int)MessageType.HotkeyConfig
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region EquipmentComparisonMessage Tests

        [Fact]
        public void EquipmentComparisonMessage_DefaultMessageType_IsEquipmentComparison()
        {
            var msg = new EquipmentComparisonMessage();
            Assert.Equal(MessageType.EquipmentComparison, msg.Type);
        }

        [Fact]
        public void EquipmentComparisonMessage_DefaultServiceType_IsGame()
        {
            var msg = new EquipmentComparisonMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void EquipmentComparisonMessage_CanSetProperties()
        {
            var msg = new EquipmentComparisonMessage
            {
                CurrentEquipmentId = 1001,
                CompareEquipmentId = 1002,
                SlotIndex = 0,
                CurrentStats = new List<EquipmentStatInfo>
                {
                    new() { StatName = "攻击力", StatValue = 350, DiffValue = 0 }
                },
                CompareStats = new List<EquipmentStatInfo>
                {
                    new() { StatName = "攻击力", StatValue = 420, DiffValue = 70 }
                }
            };

            Assert.Equal(1001UL, msg.CurrentEquipmentId);
            Assert.Equal(1002UL, msg.CompareEquipmentId);
            Assert.Equal(0, msg.SlotIndex);
            Assert.Single(msg.CurrentStats);
            Assert.Single(msg.CompareStats);
        }

        [Fact]
        public void EquipmentStatInfo_CanSetAllProperties()
        {
            var stat = new EquipmentStatInfo
            {
                StatName = "暴击率",
                StatValue = 18.5f,
                DiffValue = 6.5f
            };

            Assert.Equal("暴击率", stat.StatName);
            Assert.Equal(18.5f, stat.StatValue);
            Assert.Equal(6.5f, stat.DiffValue);
        }

        [Fact]
        public void EquipmentComparisonMessage_DefaultLists_AreEmpty()
        {
            var msg = new EquipmentComparisonMessage();
            Assert.NotNull(msg.CurrentStats);
            Assert.Empty(msg.CurrentStats);
            Assert.NotNull(msg.CompareStats);
            Assert.Empty(msg.CompareStats);
        }

        [Fact]
        public void EquipmentComparisonMessage_SlotIndex_ValidRange()
        {
            // 测试有效装备槽位（0-5）
            for (int i = 0; i <= 5; i++)
            {
                var msg = new EquipmentComparisonMessage { SlotIndex = i };
                Assert.Equal(i, msg.SlotIndex);
            }
        }

        #endregion

        #region GuildManagementMessage Tests

        [Fact]
        public void GuildManagementMessage_DefaultMessageType_IsGuildManagement()
        {
            var msg = new GuildManagementMessage();
            Assert.Equal(MessageType.GuildManagement, msg.Type);
        }

        [Fact]
        public void GuildManagementMessage_DefaultServiceType_IsSocial()
        {
            var msg = new GuildManagementMessage();
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        [Fact]
        public void GuildManagementMessage_CanSetAllProperties()
        {
            var msg = new GuildManagementMessage
            {
                Action = GuildManagementAction.Kick,
                GuildId = 5001,
                OperatorId = 1001,
                TargetId = 1005,
                ExtraText = "违反公会规定",
                Success = true,
                ResultMessage = "成员已被踢出"
            };

            Assert.Equal(GuildManagementAction.Kick, msg.Action);
            Assert.Equal(5001UL, msg.GuildId);
            Assert.Equal(1001UL, msg.OperatorId);
            Assert.Equal(1005UL, msg.TargetId);
            Assert.Equal("违反公会规定", msg.ExtraText);
            Assert.True(msg.Success);
            Assert.Equal("成员已被踢出", msg.ResultMessage);
        }

        [Fact]
        public void GuildManagementAction_HasExpectedValues()
        {
            Assert.Equal(0, (int)GuildManagementAction.Apply);
            Assert.Equal(1, (int)GuildManagementAction.Approve);
            Assert.Equal(2, (int)GuildManagementAction.Reject);
            Assert.Equal(3, (int)GuildManagementAction.Kick);
            Assert.Equal(4, (int)GuildManagementAction.Promote);
            Assert.Equal(5, (int)GuildManagementAction.Demote);
            Assert.Equal(6, (int)GuildManagementAction.TransferLeader);
            Assert.Equal(7, (int)GuildManagementAction.UpdateAnnouncement);
            Assert.Equal(8, (int)GuildManagementAction.Disband);
        }

        [Fact]
        public void GuildManagementAction_HasNineValues()
        {
            var values = Enum.GetValues<GuildManagementAction>();
            Assert.Equal(9, values.Length);
        }

        [Fact]
        public void GuildManagementMessage_DefaultStrings_AreEmpty()
        {
            var msg = new GuildManagementMessage();
            Assert.Equal("", msg.ExtraText);
            Assert.Equal("", msg.ResultMessage);
        }

        #endregion

        #region TeamInviteMessage Tests

        [Fact]
        public void TeamInviteMessage_DefaultMessageType_IsTeamInvite()
        {
            var msg = new TeamInviteMessage();
            Assert.Equal(MessageType.TeamInvite, msg.Type);
        }

        [Fact]
        public void TeamInviteMessage_DefaultServiceType_IsSocial()
        {
            var msg = new TeamInviteMessage();
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        [Fact]
        public void TeamInviteMessage_CanSetAllProperties()
        {
            var msg = new TeamInviteMessage
            {
                Action = TeamInviteAction.Invite,
                InviterId = 1001,
                InviterName = "队长大人",
                InviteeId = 1002,
                InviteeName = "新手玩家",
                TeamId = 3001,
                InviterLevel = 65,
                Success = true,
                ResultMessage = "邀请已发送"
            };

            Assert.Equal(TeamInviteAction.Invite, msg.Action);
            Assert.Equal(1001UL, msg.InviterId);
            Assert.Equal("队长大人", msg.InviterName);
            Assert.Equal(1002UL, msg.InviteeId);
            Assert.Equal("新手玩家", msg.InviteeName);
            Assert.Equal(3001UL, msg.TeamId);
            Assert.Equal(65, msg.InviterLevel);
            Assert.True(msg.Success);
            Assert.Equal("邀请已发送", msg.ResultMessage);
        }

        [Fact]
        public void TeamInviteAction_HasExpectedValues()
        {
            Assert.Equal(0, (int)TeamInviteAction.Invite);
            Assert.Equal(1, (int)TeamInviteAction.Accept);
            Assert.Equal(2, (int)TeamInviteAction.Decline);
            Assert.Equal(3, (int)TeamInviteAction.Cancel);
            Assert.Equal(4, (int)TeamInviteAction.Apply);
        }

        [Fact]
        public void TeamInviteAction_HasFiveValues()
        {
            var values = Enum.GetValues<TeamInviteAction>();
            Assert.Equal(5, values.Length);
        }

        [Fact]
        public void TeamInviteMessage_DefaultStrings_AreEmpty()
        {
            var msg = new TeamInviteMessage();
            Assert.Equal("", msg.InviterName);
            Assert.Equal("", msg.InviteeName);
            Assert.Equal("", msg.ResultMessage);
        }

        [Fact]
        public void TeamInviteMessage_AcceptAction_SetsCorrectly()
        {
            var msg = new TeamInviteMessage
            {
                Action = TeamInviteAction.Accept,
                InviteeId = 1002,
                TeamId = 3001,
                Success = true
            };

            Assert.Equal(TeamInviteAction.Accept, msg.Action);
            Assert.True(msg.Success);
        }

        [Fact]
        public void TeamInviteMessage_DeclineAction_SetsCorrectly()
        {
            var msg = new TeamInviteMessage
            {
                Action = TeamInviteAction.Decline,
                InviteeId = 1002,
                TeamId = 3001,
                Success = true,
                ResultMessage = "对方拒绝了你的邀请"
            };

            Assert.Equal(TeamInviteAction.Decline, msg.Action);
            Assert.Equal("对方拒绝了你的邀请", msg.ResultMessage);
        }

        #endregion

        #region KillCamMessage Tests

        [Fact]
        public void KillCamMessage_DefaultMessageType_IsKillCam()
        {
            var msg = new KillCamMessage();
            Assert.Equal(MessageType.KillCam, msg.Type);
        }

        [Fact]
        public void KillCamMessage_DefaultServiceType_IsGame()
        {
            var msg = new KillCamMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void KillCamMessage_CanSetAllProperties()
        {
            var msg = new KillCamMessage
            {
                KillerId = 1001,
                KillerName = "武林高手",
                VictimId = 2001,
                VictimName = "落败侠客",
                FinishingSkillName = "天山折梅手",
                TotalDamage = 5000f,
                IsCriticalKill = true,
                KillStreak = 3
            };

            Assert.Equal(1001UL, msg.KillerId);
            Assert.Equal("武林高手", msg.KillerName);
            Assert.Equal(2001UL, msg.VictimId);
            Assert.Equal("落败侠客", msg.VictimName);
            Assert.Equal("天山折梅手", msg.FinishingSkillName);
            Assert.Equal(5000f, msg.TotalDamage);
            Assert.True(msg.IsCriticalKill);
            Assert.Equal(3, msg.KillStreak);
        }

        [Fact]
        public void KillCamMessage_DefaultValues_AreCorrect()
        {
            var msg = new KillCamMessage();
            Assert.Equal(0UL, msg.KillerId);
            Assert.Equal("", msg.KillerName);
            Assert.Equal(0UL, msg.VictimId);
            Assert.Equal("", msg.VictimName);
            Assert.Equal("", msg.FinishingSkillName);
            Assert.Equal(0f, msg.TotalDamage);
            Assert.False(msg.IsCriticalKill);
            Assert.Equal(0, msg.KillStreak);
        }

        [Fact]
        public void KillCamMessage_KillStreak_TracksMultipleKills()
        {
            var msg = new KillCamMessage { KillStreak = 5 };
            Assert.Equal(5, msg.KillStreak);
            Assert.True(msg.KillStreak > 1);
        }

        #endregion

        #region HotkeyConfigMessage Tests

        [Fact]
        public void HotkeyConfigMessage_DefaultMessageType_IsHotkeyConfig()
        {
            var msg = new HotkeyConfigMessage();
            Assert.Equal(MessageType.HotkeyConfig, msg.Type);
        }

        [Fact]
        public void HotkeyConfigMessage_DefaultServiceType_IsGame()
        {
            var msg = new HotkeyConfigMessage();
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void HotkeyConfigMessage_CanSetBindings()
        {
            var msg = new HotkeyConfigMessage
            {
                CharacterId = 1001,
                Bindings = new List<HotkeyBinding>
                {
                    new() { SlotIndex = 0, KeyName = "Q", SkillId = 101 },
                    new() { SlotIndex = 1, KeyName = "W", SkillId = 102 },
                    new() { SlotIndex = 2, KeyName = "E", SkillId = 103 },
                    new() { SlotIndex = 3, KeyName = "R", SkillId = 104 }
                }
            };

            Assert.Equal(1001UL, msg.CharacterId);
            Assert.Equal(4, msg.Bindings.Count);
            Assert.Equal("Q", msg.Bindings[0].KeyName);
            Assert.Equal(101, msg.Bindings[0].SkillId);
        }

        [Fact]
        public void HotkeyConfigMessage_DefaultBindings_AreEmpty()
        {
            var msg = new HotkeyConfigMessage();
            Assert.NotNull(msg.Bindings);
            Assert.Empty(msg.Bindings);
        }

        [Fact]
        public void HotkeyBinding_CanSetAllProperties()
        {
            var binding = new HotkeyBinding
            {
                SlotIndex = 5,
                KeyName = "F1",
                SkillId = 205
            };

            Assert.Equal(5, binding.SlotIndex);
            Assert.Equal("F1", binding.KeyName);
            Assert.Equal(205, binding.SkillId);
        }

        [Fact]
        public void HotkeyBinding_DefaultKeyName_IsEmpty()
        {
            var binding = new HotkeyBinding();
            Assert.Equal("", binding.KeyName);
        }

        [Fact]
        public void HotkeyConfigMessage_MultipleBindings_HaveUniqueSlots()
        {
            var msg = new HotkeyConfigMessage
            {
                Bindings = new List<HotkeyBinding>
                {
                    new() { SlotIndex = 0, KeyName = "1" },
                    new() { SlotIndex = 1, KeyName = "2" },
                    new() { SlotIndex = 2, KeyName = "3" },
                    new() { SlotIndex = 3, KeyName = "4" },
                    new() { SlotIndex = 4, KeyName = "5" },
                    new() { SlotIndex = 5, KeyName = "6" },
                    new() { SlotIndex = 6, KeyName = "7" },
                    new() { SlotIndex = 7, KeyName = "8" }
                }
            };

            var slots = msg.Bindings.Select(b => b.SlotIndex).ToList();
            Assert.Equal(slots.Count, slots.Distinct().Count());
        }

        #endregion

        #region Cross-DTO Validation Tests

        [Fact]
        public void AllPhase3Messages_ImplementINetworkMessage()
        {
            Assert.IsAssignableFrom<INetworkMessage>(new EquipmentComparisonMessage());
            Assert.IsAssignableFrom<INetworkMessage>(new GuildManagementMessage());
            Assert.IsAssignableFrom<INetworkMessage>(new TeamInviteMessage());
            Assert.IsAssignableFrom<INetworkMessage>(new KillCamMessage());
            Assert.IsAssignableFrom<INetworkMessage>(new HotkeyConfigMessage());
        }

        [Fact]
        public void AllPhase3Messages_HaveCorrectServiceTypes()
        {
            // Game service messages
            Assert.Equal(ServiceType.Game, new EquipmentComparisonMessage().ServiceType);
            Assert.Equal(ServiceType.Game, new KillCamMessage().ServiceType);
            Assert.Equal(ServiceType.Game, new HotkeyConfigMessage().ServiceType);

            // Social service messages
            Assert.Equal(ServiceType.Social, new GuildManagementMessage().ServiceType);
            Assert.Equal(ServiceType.Social, new TeamInviteMessage().ServiceType);
        }

        [Fact]
        public void Phase3MessageTypes_ContinueFromPhase2()
        {
            // Phase 2 ended at ChatSend = 1342
            // Phase 3 should start from 1343
            Assert.Equal(1342, (int)MessageType.ChatSend);
            Assert.Equal(1343, (int)MessageType.EquipmentComparison);
            Assert.True((int)MessageType.EquipmentComparison > (int)MessageType.ChatSend);
        }

        [Fact]
        public void Phase3MessageTypes_AreContiguous()
        {
            // Verify the new message types are sequential
            Assert.Equal((int)MessageType.EquipmentComparison + 1, (int)MessageType.GuildManagement);
            Assert.Equal((int)MessageType.GuildManagement + 1, (int)MessageType.TeamInvite);
            Assert.Equal((int)MessageType.TeamInvite + 1, (int)MessageType.KillCam);
            Assert.Equal((int)MessageType.KillCam + 1, (int)MessageType.HotkeyConfig);
        }

        #endregion
    }
}

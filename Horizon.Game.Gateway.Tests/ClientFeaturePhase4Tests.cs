using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第四阶段
    /// 测试音频播放消息、Buff显示消息、新增MessageType枚举和DTO
    /// </summary>
    public class ClientFeaturePhase4Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_AudioPlayback_HasCorrectValue()
        {
            Assert.Equal(1348, (int)MessageType.AudioPlayback);
        }

        [Fact]
        public void MessageType_BuffDisplay_HasCorrectValue()
        {
            Assert.Equal(1349, (int)MessageType.BuffDisplay);
        }

        [Fact]
        public void MessageType_NewPhase4Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.AudioPlayback,
                (int)MessageType.BuffDisplay
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase4Types_DoNotConflictWithPreviousPhases()
        {
            var phase3Max = (int)MessageType.HotkeyConfig; // 1347
            Assert.True((int)MessageType.AudioPlayback > phase3Max);
            Assert.True((int)MessageType.BuffDisplay > phase3Max);
        }

        [Fact]
        public void MessageType_Phase4Types_AreSequential()
        {
            Assert.Equal((int)MessageType.AudioPlayback + 1, (int)MessageType.BuffDisplay);
        }

        #endregion

        #region AudioPlaybackMessage Tests

        [Fact]
        public void AudioPlaybackMessage_DefaultValues_AreCorrect()
        {
            var msg = new AudioPlaybackMessage();
            Assert.Equal("", msg.SoundPath);
            Assert.Equal(GameAudioCategory.Skill, msg.Category);
            Assert.Equal(1.0f, msg.Volume);
            Assert.Equal(0f, msg.PositionX);
            Assert.Equal(0f, msg.PositionY);
            Assert.Equal(0f, msg.PositionZ);
            Assert.False(msg.Is3D);
            Assert.Equal(0, msg.SkillId);
            Assert.Equal(MessageType.AudioPlayback, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void AudioPlaybackMessage_SetProperties_WorkCorrectly()
        {
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/Skills/Skill_101",
                Category = GameAudioCategory.Attack,
                Volume = 0.8f,
                PositionX = 10.5f,
                PositionY = 5.0f,
                PositionZ = -3.2f,
                Is3D = true,
                SkillId = 101
            };

            Assert.Equal("/Game/Audio/Skills/Skill_101", msg.SoundPath);
            Assert.Equal(GameAudioCategory.Attack, msg.Category);
            Assert.Equal(0.8f, msg.Volume);
            Assert.Equal(10.5f, msg.PositionX);
            Assert.Equal(5.0f, msg.PositionY);
            Assert.Equal(-3.2f, msg.PositionZ);
            Assert.True(msg.Is3D);
            Assert.Equal(101, msg.SkillId);
        }

        [Fact]
        public void AudioPlaybackMessage_ImplementsINetworkMessage()
        {
            var msg = new AudioPlaybackMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void AudioPlaybackMessage_2DSound_HasIs3DFalse()
        {
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/UI/Click",
                Category = GameAudioCategory.UI,
                Is3D = false
            };

            Assert.False(msg.Is3D);
        }

        [Fact]
        public void AudioPlaybackMessage_3DSound_HasPositionAndIs3DTrue()
        {
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/Effects/Death_Sound",
                Category = GameAudioCategory.Death,
                Is3D = true,
                PositionX = 100f,
                PositionY = 0f,
                PositionZ = 200f
            };

            Assert.True(msg.Is3D);
            Assert.Equal(100f, msg.PositionX);
            Assert.Equal(200f, msg.PositionZ);
        }

        #endregion

        #region GameAudioCategory Tests

        [Fact]
        public void GameAudioCategory_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)GameAudioCategory.Skill);
            Assert.Equal(1, (int)GameAudioCategory.Attack);
            Assert.Equal(2, (int)GameAudioCategory.Hit);
            Assert.Equal(3, (int)GameAudioCategory.Death);
            Assert.Equal(4, (int)GameAudioCategory.Resurrect);
            Assert.Equal(5, (int)GameAudioCategory.Environment);
            Assert.Equal(6, (int)GameAudioCategory.UI);
        }

        [Fact]
        public void GameAudioCategory_HasSevenValues()
        {
            var values = Enum.GetValues(typeof(GameAudioCategory));
            Assert.Equal(7, values.Length);
        }

        [Fact]
        public void GameAudioCategory_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(GameAudioCategory)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region BuffDisplayMessage Tests

        [Fact]
        public void BuffDisplayMessage_DefaultValues_AreCorrect()
        {
            var msg = new BuffDisplayMessage();
            Assert.Equal(0UL, msg.TargetId);
            Assert.Equal(0, msg.EffectId);
            Assert.Equal("", msg.EffectName);
            Assert.Equal("", msg.IconPath);
            Assert.Equal(0f, msg.Duration);
            Assert.Equal(1, msg.StackCount);
            Assert.True(msg.IsBuff);
            Assert.Equal(BuffOperation.Add, msg.Operation);
            Assert.Equal(MessageType.BuffDisplay, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void BuffDisplayMessage_SetProperties_WorkCorrectly()
        {
            var msg = new BuffDisplayMessage
            {
                TargetId = 12345,
                EffectId = 501,
                EffectName = "金钟罩",
                IconPath = "/Game/UI/Icons/Buff_Shield",
                Duration = 30.0f,
                StackCount = 3,
                IsBuff = true,
                Operation = BuffOperation.Stack
            };

            Assert.Equal(12345UL, msg.TargetId);
            Assert.Equal(501, msg.EffectId);
            Assert.Equal("金钟罩", msg.EffectName);
            Assert.Equal("/Game/UI/Icons/Buff_Shield", msg.IconPath);
            Assert.Equal(30.0f, msg.Duration);
            Assert.Equal(3, msg.StackCount);
            Assert.True(msg.IsBuff);
            Assert.Equal(BuffOperation.Stack, msg.Operation);
        }

        [Fact]
        public void BuffDisplayMessage_ImplementsINetworkMessage()
        {
            var msg = new BuffDisplayMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void BuffDisplayMessage_Debuff_HasIsBuffFalse()
        {
            var msg = new BuffDisplayMessage
            {
                EffectName = "燃烧",
                IsBuff = false,
                Duration = 10.0f
            };

            Assert.False(msg.IsBuff);
        }

        [Fact]
        public void BuffDisplayMessage_RemoveOperation_WorksCorrectly()
        {
            var msg = new BuffDisplayMessage
            {
                TargetId = 99999,
                EffectId = 201,
                Operation = BuffOperation.Remove
            };

            Assert.Equal(BuffOperation.Remove, msg.Operation);
        }

        [Fact]
        public void BuffDisplayMessage_RefreshOperation_UpdatesDuration()
        {
            var msg = new BuffDisplayMessage
            {
                EffectId = 301,
                Duration = 15.0f,
                Operation = BuffOperation.Refresh
            };

            Assert.Equal(BuffOperation.Refresh, msg.Operation);
            Assert.Equal(15.0f, msg.Duration);
        }

        #endregion

        #region BuffOperation Tests

        [Fact]
        public void BuffOperation_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)BuffOperation.Add);
            Assert.Equal(1, (int)BuffOperation.Refresh);
            Assert.Equal(2, (int)BuffOperation.Remove);
            Assert.Equal(3, (int)BuffOperation.Stack);
        }

        [Fact]
        public void BuffOperation_HasFourValues()
        {
            var values = Enum.GetValues(typeof(BuffOperation));
            Assert.Equal(4, values.Length);
        }

        [Fact]
        public void BuffOperation_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(BuffOperation)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region AudioPlaybackMessage Scenario Tests

        [Fact]
        public void AudioPlaybackMessage_SkillSound_ScenarioTest()
        {
            // 模拟技能施放时的音效消息
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/Skills/Skill_1001",
                Category = GameAudioCategory.Skill,
                Volume = 1.0f,
                Is3D = true,
                PositionX = 50.0f,
                PositionY = 1.0f,
                PositionZ = 30.0f,
                SkillId = 1001
            };

            Assert.Equal(MessageType.AudioPlayback, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.True(msg.Is3D);
            Assert.Equal(1001, msg.SkillId);
        }

        [Fact]
        public void AudioPlaybackMessage_UISound_ScenarioTest()
        {
            // 模拟UI点击音效消息
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/UI/ButtonClick",
                Category = GameAudioCategory.UI,
                Volume = 0.5f,
                Is3D = false
            };

            Assert.Equal(GameAudioCategory.UI, msg.Category);
            Assert.False(msg.Is3D);
            Assert.Equal(0.5f, msg.Volume);
        }

        [Fact]
        public void AudioPlaybackMessage_DeathSound_ScenarioTest()
        {
            // 模拟死亡音效消息
            var msg = new AudioPlaybackMessage
            {
                SoundPath = "/Game/Audio/Effects/Death_Sound",
                Category = GameAudioCategory.Death,
                Volume = 1.0f,
                Is3D = true,
                PositionX = 120f,
                PositionY = 0f,
                PositionZ = -80f
            };

            Assert.Equal(GameAudioCategory.Death, msg.Category);
            Assert.True(msg.Is3D);
        }

        #endregion

        #region BuffDisplayMessage Scenario Tests

        [Fact]
        public void BuffDisplayMessage_AddBuff_ScenarioTest()
        {
            // 模拟添加防御Buff
            var msg = new BuffDisplayMessage
            {
                TargetId = 10001,
                EffectId = 2001,
                EffectName = "岩甲防御",
                IconPath = "/Game/UI/Icons/Buff_RockArmor",
                Duration = 30.0f,
                StackCount = 1,
                IsBuff = true,
                Operation = BuffOperation.Add
            };

            Assert.Equal(BuffOperation.Add, msg.Operation);
            Assert.True(msg.IsBuff);
            Assert.Equal(30.0f, msg.Duration);
        }

        [Fact]
        public void BuffDisplayMessage_AddDebuff_ScenarioTest()
        {
            // 模拟添加燃烧Debuff
            var msg = new BuffDisplayMessage
            {
                TargetId = 10002,
                EffectId = 3001,
                EffectName = "燃烧",
                IconPath = "/Game/UI/Icons/Debuff_Burning",
                Duration = 10.0f,
                StackCount = 1,
                IsBuff = false,
                Operation = BuffOperation.Add
            };

            Assert.False(msg.IsBuff);
            Assert.Equal(10.0f, msg.Duration);
        }

        [Fact]
        public void BuffDisplayMessage_StackBuff_ScenarioTest()
        {
            // 模拟Buff叠加
            var msg = new BuffDisplayMessage
            {
                TargetId = 10003,
                EffectId = 2002,
                EffectName = "连击强化",
                StackCount = 5,
                IsBuff = true,
                Operation = BuffOperation.Stack
            };

            Assert.Equal(5, msg.StackCount);
            Assert.Equal(BuffOperation.Stack, msg.Operation);
        }

        [Fact]
        public void BuffDisplayMessage_RemoveBuff_ScenarioTest()
        {
            // 模拟移除Buff
            var msg = new BuffDisplayMessage
            {
                TargetId = 10004,
                EffectId = 2001,
                Operation = BuffOperation.Remove
            };

            Assert.Equal(BuffOperation.Remove, msg.Operation);
        }

        [Fact]
        public void BuffDisplayMessage_RefreshBuff_ScenarioTest()
        {
            // 模拟刷新Buff持续时间
            var msg = new BuffDisplayMessage
            {
                TargetId = 10005,
                EffectId = 2003,
                EffectName = "急速",
                Duration = 20.0f,
                Operation = BuffOperation.Refresh
            };

            Assert.Equal(BuffOperation.Refresh, msg.Operation);
            Assert.Equal(20.0f, msg.Duration);
        }

        #endregion

        #region Integration Tests - MessageType Continuity

        [Fact]
        public void MessageType_Phase4_FollowsPhase3Sequence()
        {
            // 确保Phase 4消息类型紧跟Phase 3
            Assert.Equal((int)MessageType.HotkeyConfig + 1, (int)MessageType.AudioPlayback);
            Assert.Equal((int)MessageType.HotkeyConfig + 2, (int)MessageType.BuffDisplay);
        }

        [Fact]
        public void MessageType_AllPhase4Types_HaveGameServiceType()
        {
            var audioMsg = new AudioPlaybackMessage();
            var buffMsg = new BuffDisplayMessage();

            Assert.Equal(ServiceType.Game, audioMsg.ServiceType);
            Assert.Equal(ServiceType.Game, buffMsg.ServiceType);
        }

        [Theory]
        [InlineData(GameAudioCategory.Skill)]
        [InlineData(GameAudioCategory.Attack)]
        [InlineData(GameAudioCategory.Hit)]
        [InlineData(GameAudioCategory.Death)]
        [InlineData(GameAudioCategory.Resurrect)]
        [InlineData(GameAudioCategory.Environment)]
        [InlineData(GameAudioCategory.UI)]
        public void AudioPlaybackMessage_CanSetAllCategories(GameAudioCategory category)
        {
            var msg = new AudioPlaybackMessage { Category = category };
            Assert.Equal(category, msg.Category);
        }

        [Theory]
        [InlineData(BuffOperation.Add)]
        [InlineData(BuffOperation.Refresh)]
        [InlineData(BuffOperation.Remove)]
        [InlineData(BuffOperation.Stack)]
        public void BuffDisplayMessage_CanSetAllOperations(BuffOperation operation)
        {
            var msg = new BuffDisplayMessage { Operation = operation };
            Assert.Equal(operation, msg.Operation);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void AudioPlaybackMessage_EmptySoundPath_DefaultsToEmpty()
        {
            var msg = new AudioPlaybackMessage();
            Assert.Equal("", msg.SoundPath);
            Assert.NotNull(msg.SoundPath);
        }

        [Fact]
        public void BuffDisplayMessage_ZeroDuration_IsValid()
        {
            var msg = new BuffDisplayMessage
            {
                Duration = 0f,
                Operation = BuffOperation.Add
            };
            Assert.Equal(0f, msg.Duration);
        }

        [Fact]
        public void BuffDisplayMessage_DefaultStackCount_IsOne()
        {
            var msg = new BuffDisplayMessage();
            Assert.Equal(1, msg.StackCount);
        }

        [Fact]
        public void AudioPlaybackMessage_VolumeRange_AcceptsValid()
        {
            var msg = new AudioPlaybackMessage { Volume = 0.0f };
            Assert.Equal(0.0f, msg.Volume);

            msg.Volume = 1.0f;
            Assert.Equal(1.0f, msg.Volume);

            msg.Volume = 0.5f;
            Assert.Equal(0.5f, msg.Volume);
        }

        [Fact]
        public void BuffDisplayMessage_LargeTargetId_WorksCorrectly()
        {
            var msg = new BuffDisplayMessage { TargetId = ulong.MaxValue };
            Assert.Equal(ulong.MaxValue, msg.TargetId);
        }

        [Fact]
        public void AudioPlaybackMessage_NegativePosition_WorksCorrectly()
        {
            var msg = new AudioPlaybackMessage
            {
                PositionX = -100f,
                PositionY = -50f,
                PositionZ = -200f
            };

            Assert.Equal(-100f, msg.PositionX);
            Assert.Equal(-50f, msg.PositionY);
            Assert.Equal(-200f, msg.PositionZ);
        }

        #endregion
    }
}

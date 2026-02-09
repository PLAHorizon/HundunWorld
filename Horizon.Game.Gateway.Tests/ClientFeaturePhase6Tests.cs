using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第六阶段
    /// 测试动画状态同步、性能报告、断线重连、LOD配置、粒子预算、消息压缩配置消息
    /// </summary>
    public class ClientFeaturePhase6Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_AnimationSync_HasCorrectValue()
        {
            Assert.Equal(1352, (int)MessageType.AnimationSync);
        }

        [Fact]
        public void MessageType_PerformanceReport_HasCorrectValue()
        {
            Assert.Equal(1353, (int)MessageType.PerformanceReport);
        }

        [Fact]
        public void MessageType_Reconnection_HasCorrectValue()
        {
            Assert.Equal(1354, (int)MessageType.Reconnection);
        }

        [Fact]
        public void MessageType_LODConfig_HasCorrectValue()
        {
            Assert.Equal(1355, (int)MessageType.LODConfig);
        }

        [Fact]
        public void MessageType_ParticleBudget_HasCorrectValue()
        {
            Assert.Equal(1356, (int)MessageType.ParticleBudget);
        }

        [Fact]
        public void MessageType_MessageCompressionConfig_HasCorrectValue()
        {
            Assert.Equal(1357, (int)MessageType.MessageCompressionConfig);
        }

        [Fact]
        public void MessageType_NewPhase6Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.AnimationSync,
                (int)MessageType.PerformanceReport,
                (int)MessageType.Reconnection,
                (int)MessageType.LODConfig,
                (int)MessageType.ParticleBudget,
                (int)MessageType.MessageCompressionConfig
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase6Types_DoNotConflictWithPreviousPhases()
        {
            var phase5Max = (int)MessageType.InputConfigSync; // 1351
            Assert.True((int)MessageType.AnimationSync > phase5Max);
            Assert.True((int)MessageType.PerformanceReport > phase5Max);
            Assert.True((int)MessageType.Reconnection > phase5Max);
            Assert.True((int)MessageType.LODConfig > phase5Max);
            Assert.True((int)MessageType.ParticleBudget > phase5Max);
            Assert.True((int)MessageType.MessageCompressionConfig > phase5Max);
        }

        [Fact]
        public void MessageType_Phase6Types_AreSequential()
        {
            Assert.Equal((int)MessageType.AnimationSync + 1, (int)MessageType.PerformanceReport);
            Assert.Equal((int)MessageType.PerformanceReport + 1, (int)MessageType.Reconnection);
            Assert.Equal((int)MessageType.Reconnection + 1, (int)MessageType.LODConfig);
            Assert.Equal((int)MessageType.LODConfig + 1, (int)MessageType.ParticleBudget);
            Assert.Equal((int)MessageType.ParticleBudget + 1, (int)MessageType.MessageCompressionConfig);
        }

        [Fact]
        public void MessageType_Phase6_FollowsPhase5Sequence()
        {
            Assert.Equal((int)MessageType.InputConfigSync + 1, (int)MessageType.AnimationSync);
            Assert.Equal((int)MessageType.InputConfigSync + 6, (int)MessageType.MessageCompressionConfig);
        }

        #endregion

        #region AnimationSyncMessage Tests

        [Fact]
        public void AnimationSyncMessage_DefaultValues_AreCorrect()
        {
            var msg = new AnimationSyncMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(AnimationStateType.Idle, msg.AnimationState);
            Assert.Equal("", msg.AnimationName);
            Assert.Equal(1.0f, msg.PlaybackSpeed);
            Assert.Equal(0f, msg.Progress);
            Assert.Equal(0, msg.SkillId);
            Assert.Equal(0L, msg.Timestamp);
            Assert.Equal(MessageType.AnimationSync, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void AnimationSyncMessage_SetProperties_WorkCorrectly()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 10001,
                AnimationState = AnimationStateType.AttackActive,
                AnimationName = "SwordSlash",
                PlaybackSpeed = 1.5f,
                Progress = 0.75f,
                SkillId = 2001,
                Timestamp = 1234567890L
            };

            Assert.Equal(10001UL, msg.CharacterId);
            Assert.Equal(AnimationStateType.AttackActive, msg.AnimationState);
            Assert.Equal("SwordSlash", msg.AnimationName);
            Assert.Equal(1.5f, msg.PlaybackSpeed);
            Assert.Equal(0.75f, msg.Progress);
            Assert.Equal(2001, msg.SkillId);
            Assert.Equal(1234567890L, msg.Timestamp);
        }

        [Fact]
        public void AnimationSyncMessage_ImplementsINetworkMessage()
        {
            var msg = new AnimationSyncMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void AnimationSyncMessage_AttackScenario()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 50001,
                AnimationState = AnimationStateType.AttackStartup,
                AnimationName = "HeavyStrike",
                PlaybackSpeed = 1.0f,
                Progress = 0f,
                SkillId = 1001
            };

            Assert.Equal(AnimationStateType.AttackStartup, msg.AnimationState);
            Assert.Equal("HeavyStrike", msg.AnimationName);
        }

        [Fact]
        public void AnimationSyncMessage_CastScenario()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 50002,
                AnimationState = AnimationStateType.CastActive,
                AnimationName = "FireBolt",
                PlaybackSpeed = 0.8f,
                Progress = 0.5f,
                SkillId = 3001
            };

            Assert.Equal(AnimationStateType.CastActive, msg.AnimationState);
        }

        [Fact]
        public void AnimationSyncMessage_DeathScenario()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 50003,
                AnimationState = AnimationStateType.Death,
                AnimationName = "Death_Fall",
                PlaybackSpeed = 1.0f,
                Progress = 0f
            };

            Assert.Equal(AnimationStateType.Death, msg.AnimationState);
        }

        [Fact]
        public void AnimationSyncMessage_HitScenario()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 50004,
                AnimationState = AnimationStateType.Hit,
                AnimationName = "Hit_Front",
                PlaybackSpeed = 1.0f,
                Progress = 0.3f
            };

            Assert.Equal(AnimationStateType.Hit, msg.AnimationState);
        }

        [Fact]
        public void AnimationSyncMessage_ChargingScenario()
        {
            var msg = new AnimationSyncMessage
            {
                CharacterId = 50005,
                AnimationState = AnimationStateType.Charging,
                AnimationName = "PowerCharge",
                PlaybackSpeed = 1.0f,
                Progress = 0.6f,
                SkillId = 4001
            };

            Assert.Equal(AnimationStateType.Charging, msg.AnimationState);
            Assert.Equal(0.6f, msg.Progress);
        }

        #endregion

        #region AnimationStateType Tests

        [Fact]
        public void AnimationStateType_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)AnimationStateType.Idle);
            Assert.Equal(1, (int)AnimationStateType.Moving);
            Assert.Equal(2, (int)AnimationStateType.AttackStartup);
            Assert.Equal(3, (int)AnimationStateType.AttackActive);
            Assert.Equal(4, (int)AnimationStateType.AttackRecovery);
            Assert.Equal(5, (int)AnimationStateType.CastStartup);
            Assert.Equal(6, (int)AnimationStateType.CastActive);
            Assert.Equal(7, (int)AnimationStateType.CastRecovery);
            Assert.Equal(8, (int)AnimationStateType.Hit);
            Assert.Equal(9, (int)AnimationStateType.Death);
            Assert.Equal(10, (int)AnimationStateType.Charging);
            Assert.Equal(11, (int)AnimationStateType.Channeling);
        }

        [Fact]
        public void AnimationStateType_HasTwelveValues()
        {
            var values = Enum.GetValues(typeof(AnimationStateType));
            Assert.Equal(12, values.Length);
        }

        [Fact]
        public void AnimationStateType_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(AnimationStateType)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region PerformanceReportMessage Tests

        [Fact]
        public void PerformanceReportMessage_DefaultValues_AreCorrect()
        {
            var msg = new PerformanceReportMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(0f, msg.CurrentFPS);
            Assert.Equal(0f, msg.AverageFPS);
            Assert.Equal(0, msg.NetworkLatencyMs);
            Assert.Equal(0f, msg.MemoryUsageMB);
            Assert.Equal(0, msg.OptimizationLevel);
            Assert.Equal(0L, msg.Timestamp);
            Assert.Equal(MessageType.PerformanceReport, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void PerformanceReportMessage_SetProperties_WorkCorrectly()
        {
            var msg = new PerformanceReportMessage
            {
                CharacterId = 20001,
                CurrentFPS = 58.5f,
                AverageFPS = 60.2f,
                NetworkLatencyMs = 45,
                MemoryUsageMB = 512.3f,
                OptimizationLevel = 2,
                Timestamp = 9876543210L
            };

            Assert.Equal(20001UL, msg.CharacterId);
            Assert.Equal(58.5f, msg.CurrentFPS);
            Assert.Equal(60.2f, msg.AverageFPS);
            Assert.Equal(45, msg.NetworkLatencyMs);
            Assert.Equal(512.3f, msg.MemoryUsageMB);
            Assert.Equal(2, msg.OptimizationLevel);
        }

        [Fact]
        public void PerformanceReportMessage_ImplementsINetworkMessage()
        {
            var msg = new PerformanceReportMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void PerformanceReportMessage_HighPerformance_Scenario()
        {
            var msg = new PerformanceReportMessage
            {
                CurrentFPS = 120f,
                AverageFPS = 118f,
                NetworkLatencyMs = 10,
                MemoryUsageMB = 256f,
                OptimizationLevel = 3
            };

            Assert.True(msg.CurrentFPS > 60);
            Assert.True(msg.NetworkLatencyMs < 50);
        }

        [Fact]
        public void PerformanceReportMessage_LowPerformance_Scenario()
        {
            var msg = new PerformanceReportMessage
            {
                CurrentFPS = 15f,
                AverageFPS = 20f,
                NetworkLatencyMs = 350,
                MemoryUsageMB = 2048f,
                OptimizationLevel = 0
            };

            Assert.True(msg.CurrentFPS < 30);
            Assert.True(msg.NetworkLatencyMs > 200);
        }

        #endregion

        #region ReconnectionMessage Tests

        [Fact]
        public void ReconnectionMessage_DefaultValues_AreCorrect()
        {
            var msg = new ReconnectionMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal("", msg.SessionToken);
            Assert.Equal(ReconnectionState.Reconnecting, msg.State);
            Assert.Equal(0, msg.AttemptCount);
            Assert.Equal(0L, msg.DisconnectTimestamp);
            Assert.Equal(0L, msg.LastAcknowledgedSequence);
            Assert.Equal(MessageType.Reconnection, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void ReconnectionMessage_SetProperties_WorkCorrectly()
        {
            var msg = new ReconnectionMessage
            {
                CharacterId = 30001,
                SessionToken = "abc123-session-token",
                State = ReconnectionState.Reconnected,
                AttemptCount = 3,
                DisconnectTimestamp = 1234567890L,
                LastAcknowledgedSequence = 5000
            };

            Assert.Equal(30001UL, msg.CharacterId);
            Assert.Equal("abc123-session-token", msg.SessionToken);
            Assert.Equal(ReconnectionState.Reconnected, msg.State);
            Assert.Equal(3, msg.AttemptCount);
            Assert.Equal(1234567890L, msg.DisconnectTimestamp);
            Assert.Equal(5000L, msg.LastAcknowledgedSequence);
        }

        [Fact]
        public void ReconnectionMessage_ImplementsINetworkMessage()
        {
            var msg = new ReconnectionMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void ReconnectionMessage_ReconnectingScenario()
        {
            var msg = new ReconnectionMessage
            {
                CharacterId = 40001,
                SessionToken = "session-reconnect",
                State = ReconnectionState.Reconnecting,
                AttemptCount = 1,
                DisconnectTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LastAcknowledgedSequence = 12345
            };

            Assert.Equal(ReconnectionState.Reconnecting, msg.State);
            Assert.Equal(1, msg.AttemptCount);
        }

        [Fact]
        public void ReconnectionMessage_FailedScenario()
        {
            var msg = new ReconnectionMessage
            {
                CharacterId = 40002,
                State = ReconnectionState.Failed,
                AttemptCount = 10
            };

            Assert.Equal(ReconnectionState.Failed, msg.State);
            Assert.Equal(10, msg.AttemptCount);
        }

        [Fact]
        public void ReconnectionMessage_RequireReauthScenario()
        {
            var msg = new ReconnectionMessage
            {
                CharacterId = 40003,
                State = ReconnectionState.RequireReauth,
                SessionToken = ""
            };

            Assert.Equal(ReconnectionState.RequireReauth, msg.State);
        }

        #endregion

        #region ReconnectionState Tests

        [Fact]
        public void ReconnectionState_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)ReconnectionState.Reconnecting);
            Assert.Equal(1, (int)ReconnectionState.Reconnected);
            Assert.Equal(2, (int)ReconnectionState.Failed);
            Assert.Equal(3, (int)ReconnectionState.RequireReauth);
        }

        [Fact]
        public void ReconnectionState_HasFourValues()
        {
            var values = Enum.GetValues(typeof(ReconnectionState));
            Assert.Equal(4, values.Length);
        }

        [Fact]
        public void ReconnectionState_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(ReconnectionState)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region LODConfigMessage Tests

        [Fact]
        public void LODConfigMessage_DefaultValues_AreCorrect()
        {
            var msg = new LODConfigMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(4, msg.LODLevelCount);
            Assert.NotNull(msg.LODDistances);
            Assert.Empty(msg.LODDistances);
            Assert.True(msg.EnableOcclusionCulling);
            Assert.Equal(500.0f, msg.MaxViewDistance);
            Assert.True(msg.EnableMaterialBatching);
            Assert.Equal(MessageType.LODConfig, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void LODConfigMessage_SetProperties_WorkCorrectly()
        {
            var msg = new LODConfigMessage
            {
                CharacterId = 50001,
                LODLevelCount = 3,
                LODDistances = new List<float> { 30f, 80f, 200f },
                EnableOcclusionCulling = false,
                MaxViewDistance = 300f,
                EnableMaterialBatching = false
            };

            Assert.Equal(50001UL, msg.CharacterId);
            Assert.Equal(3, msg.LODLevelCount);
            Assert.Equal(3, msg.LODDistances.Count);
            Assert.Equal(30f, msg.LODDistances[0]);
            Assert.Equal(80f, msg.LODDistances[1]);
            Assert.Equal(200f, msg.LODDistances[2]);
            Assert.False(msg.EnableOcclusionCulling);
            Assert.Equal(300f, msg.MaxViewDistance);
            Assert.False(msg.EnableMaterialBatching);
        }

        [Fact]
        public void LODConfigMessage_ImplementsINetworkMessage()
        {
            var msg = new LODConfigMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void LODConfigMessage_HighQuality_Scenario()
        {
            var msg = new LODConfigMessage
            {
                LODLevelCount = 5,
                LODDistances = new List<float> { 50f, 120f, 250f, 400f, 600f },
                EnableOcclusionCulling = true,
                MaxViewDistance = 800f,
                EnableMaterialBatching = true
            };

            Assert.Equal(5, msg.LODDistances.Count);
            Assert.Equal(800f, msg.MaxViewDistance);
        }

        [Fact]
        public void LODConfigMessage_LowQuality_Scenario()
        {
            var msg = new LODConfigMessage
            {
                LODLevelCount = 2,
                LODDistances = new List<float> { 20f, 50f },
                EnableOcclusionCulling = false,
                MaxViewDistance = 100f,
                EnableMaterialBatching = false
            };

            Assert.Equal(2, msg.LODDistances.Count);
            Assert.Equal(100f, msg.MaxViewDistance);
        }

        #endregion

        #region ParticleBudgetMessage Tests

        [Fact]
        public void ParticleBudgetMessage_DefaultValues_AreCorrect()
        {
            var msg = new ParticleBudgetMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(10000, msg.MaxParticleCount);
            Assert.Equal(50, msg.MaxEmitterCount);
            Assert.Equal(2, msg.QualityLevel);
            Assert.True(msg.EnableGPUParticles);
            Assert.Equal(200.0f, msg.ParticleViewDistance);
            Assert.Equal(MessageType.ParticleBudget, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void ParticleBudgetMessage_SetProperties_WorkCorrectly()
        {
            var msg = new ParticleBudgetMessage
            {
                CharacterId = 60001,
                MaxParticleCount = 5000,
                MaxEmitterCount = 25,
                QualityLevel = 1,
                EnableGPUParticles = false,
                ParticleViewDistance = 100f
            };

            Assert.Equal(60001UL, msg.CharacterId);
            Assert.Equal(5000, msg.MaxParticleCount);
            Assert.Equal(25, msg.MaxEmitterCount);
            Assert.Equal(1, msg.QualityLevel);
            Assert.False(msg.EnableGPUParticles);
            Assert.Equal(100f, msg.ParticleViewDistance);
        }

        [Fact]
        public void ParticleBudgetMessage_ImplementsINetworkMessage()
        {
            var msg = new ParticleBudgetMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void ParticleBudgetMessage_HighQuality_Scenario()
        {
            var msg = new ParticleBudgetMessage
            {
                MaxParticleCount = 20000,
                MaxEmitterCount = 100,
                QualityLevel = 3,
                EnableGPUParticles = true,
                ParticleViewDistance = 400f
            };

            Assert.Equal(20000, msg.MaxParticleCount);
            Assert.Equal(3, msg.QualityLevel);
        }

        [Fact]
        public void ParticleBudgetMessage_MinimalQuality_Scenario()
        {
            var msg = new ParticleBudgetMessage
            {
                MaxParticleCount = 1000,
                MaxEmitterCount = 5,
                QualityLevel = 0,
                EnableGPUParticles = false,
                ParticleViewDistance = 30f
            };

            Assert.Equal(1000, msg.MaxParticleCount);
            Assert.Equal(0, msg.QualityLevel);
            Assert.False(msg.EnableGPUParticles);
        }

        #endregion

        #region MessageCompressionConfigMessage Tests

        [Fact]
        public void MessageCompressionConfigMessage_DefaultValues_AreCorrect()
        {
            var msg = new MessageCompressionConfigMessage();
            Assert.Equal(0UL, msg.CharacterId);
            Assert.True(msg.EnableCompression);
            Assert.Equal(CompressionAlgorithm.GZip, msg.Algorithm);
            Assert.Equal(256, msg.MinCompressionSize);
            Assert.Equal(10, msg.BatchSizeThreshold);
            Assert.Equal(50, msg.BatchTimeThresholdMs);
            Assert.Equal(MessageType.MessageCompressionConfig, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
        }

        [Fact]
        public void MessageCompressionConfigMessage_SetProperties_WorkCorrectly()
        {
            var msg = new MessageCompressionConfigMessage
            {
                CharacterId = 70001,
                EnableCompression = false,
                Algorithm = CompressionAlgorithm.LZ4,
                MinCompressionSize = 512,
                BatchSizeThreshold = 20,
                BatchTimeThresholdMs = 100
            };

            Assert.Equal(70001UL, msg.CharacterId);
            Assert.False(msg.EnableCompression);
            Assert.Equal(CompressionAlgorithm.LZ4, msg.Algorithm);
            Assert.Equal(512, msg.MinCompressionSize);
            Assert.Equal(20, msg.BatchSizeThreshold);
            Assert.Equal(100, msg.BatchTimeThresholdMs);
        }

        [Fact]
        public void MessageCompressionConfigMessage_ImplementsINetworkMessage()
        {
            var msg = new MessageCompressionConfigMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        [Fact]
        public void MessageCompressionConfigMessage_NoCompression_Scenario()
        {
            var msg = new MessageCompressionConfigMessage
            {
                EnableCompression = false,
                Algorithm = CompressionAlgorithm.None
            };

            Assert.False(msg.EnableCompression);
            Assert.Equal(CompressionAlgorithm.None, msg.Algorithm);
        }

        [Fact]
        public void MessageCompressionConfigMessage_AggressiveBatching_Scenario()
        {
            var msg = new MessageCompressionConfigMessage
            {
                EnableCompression = true,
                Algorithm = CompressionAlgorithm.LZ4,
                MinCompressionSize = 64,
                BatchSizeThreshold = 50,
                BatchTimeThresholdMs = 20
            };

            Assert.Equal(50, msg.BatchSizeThreshold);
            Assert.Equal(20, msg.BatchTimeThresholdMs);
        }

        #endregion

        #region CompressionAlgorithm Tests

        [Fact]
        public void CompressionAlgorithm_AllValues_AreDefined()
        {
            Assert.Equal(0, (int)CompressionAlgorithm.None);
            Assert.Equal(1, (int)CompressionAlgorithm.GZip);
            Assert.Equal(2, (int)CompressionAlgorithm.Deflate);
            Assert.Equal(3, (int)CompressionAlgorithm.LZ4);
        }

        [Fact]
        public void CompressionAlgorithm_HasFourValues()
        {
            var values = Enum.GetValues(typeof(CompressionAlgorithm));
            Assert.Equal(4, values.Length);
        }

        [Fact]
        public void CompressionAlgorithm_AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(CompressionAlgorithm)).Cast<int>().ToArray();
            Assert.Equal(values.Length, values.Distinct().Count());
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void AllPhase6Messages_HaveGameServiceType()
        {
            var messages = new INetworkMessage[]
            {
                new AnimationSyncMessage(),
                new PerformanceReportMessage(),
                new ReconnectionMessage(),
                new LODConfigMessage(),
                new ParticleBudgetMessage(),
                new MessageCompressionConfigMessage()
            };

            foreach (var msg in messages)
            {
                Assert.Equal(ServiceType.Game, msg.ServiceType);
            }
        }

        [Fact]
        public void AllPhase6Messages_ImplementINetworkMessage()
        {
            var messages = new INetworkMessage[]
            {
                new AnimationSyncMessage(),
                new PerformanceReportMessage(),
                new ReconnectionMessage(),
                new LODConfigMessage(),
                new ParticleBudgetMessage(),
                new MessageCompressionConfigMessage()
            };

            foreach (var msg in messages)
            {
                Assert.IsAssignableFrom<INetworkMessage>(msg);
            }
        }

        [Theory]
        [InlineData(AnimationStateType.Idle)]
        [InlineData(AnimationStateType.Moving)]
        [InlineData(AnimationStateType.AttackStartup)]
        [InlineData(AnimationStateType.AttackActive)]
        [InlineData(AnimationStateType.AttackRecovery)]
        [InlineData(AnimationStateType.CastStartup)]
        [InlineData(AnimationStateType.CastActive)]
        [InlineData(AnimationStateType.CastRecovery)]
        [InlineData(AnimationStateType.Hit)]
        [InlineData(AnimationStateType.Death)]
        [InlineData(AnimationStateType.Charging)]
        [InlineData(AnimationStateType.Channeling)]
        public void AnimationSyncMessage_CanSetAllStates(AnimationStateType state)
        {
            var msg = new AnimationSyncMessage { AnimationState = state };
            Assert.Equal(state, msg.AnimationState);
        }

        [Theory]
        [InlineData(ReconnectionState.Reconnecting)]
        [InlineData(ReconnectionState.Reconnected)]
        [InlineData(ReconnectionState.Failed)]
        [InlineData(ReconnectionState.RequireReauth)]
        public void ReconnectionMessage_CanSetAllStates(ReconnectionState state)
        {
            var msg = new ReconnectionMessage { State = state };
            Assert.Equal(state, msg.State);
        }

        [Theory]
        [InlineData(CompressionAlgorithm.None)]
        [InlineData(CompressionAlgorithm.GZip)]
        [InlineData(CompressionAlgorithm.Deflate)]
        [InlineData(CompressionAlgorithm.LZ4)]
        public void MessageCompressionConfigMessage_CanSetAllAlgorithms(CompressionAlgorithm algo)
        {
            var msg = new MessageCompressionConfigMessage { Algorithm = algo };
            Assert.Equal(algo, msg.Algorithm);
        }

        [Fact]
        public void AnimationSyncMessage_LargeCharacterId_WorksCorrectly()
        {
            var msg = new AnimationSyncMessage { CharacterId = ulong.MaxValue };
            Assert.Equal(ulong.MaxValue, msg.CharacterId);
        }

        [Fact]
        public void LODConfigMessage_EmptyDistances_IsValid()
        {
            var msg = new LODConfigMessage
            {
                LODDistances = new List<float>()
            };

            Assert.Empty(msg.LODDistances);
        }

        [Fact]
        public void ParticleBudgetMessage_ZeroBudget_AcceptsValue()
        {
            var msg = new ParticleBudgetMessage
            {
                MaxParticleCount = 0,
                MaxEmitterCount = 0
            };

            Assert.Equal(0, msg.MaxParticleCount);
            Assert.Equal(0, msg.MaxEmitterCount);
        }

        #endregion

        #region Cross-Phase Integration Tests

        [Fact]
        public void MessageType_AllPhasesContiguous_Phase5ToPhase6()
        {
            var allTypes = new[]
            {
                (int)MessageType.InventoryDragDrop,       // 1350 Phase 5
                (int)MessageType.InputConfigSync,         // 1351
                (int)MessageType.AnimationSync,           // 1352 Phase 6
                (int)MessageType.PerformanceReport,       // 1353
                (int)MessageType.Reconnection,            // 1354
                (int)MessageType.LODConfig,               // 1355
                (int)MessageType.ParticleBudget,          // 1356
                (int)MessageType.MessageCompressionConfig  // 1357
            };

            for (int i = 1; i < allTypes.Length; i++)
            {
                Assert.Equal(allTypes[i - 1] + 1, allTypes[i]);
            }
        }

        [Fact]
        public void MessageType_AllPhases_Contiguous()
        {
            // 验证从Phase 3到Phase 6所有消息类型连续
            var allTypes = new[]
            {
                (int)MessageType.EquipmentComparison,      // 1343 Phase 3
                (int)MessageType.GuildManagement,          // 1344
                (int)MessageType.TeamInvite,               // 1345
                (int)MessageType.KillCam,                  // 1346
                (int)MessageType.HotkeyConfig,             // 1347
                (int)MessageType.AudioPlayback,            // 1348 Phase 4
                (int)MessageType.BuffDisplay,              // 1349
                (int)MessageType.InventoryDragDrop,        // 1350 Phase 5
                (int)MessageType.InputConfigSync,          // 1351
                (int)MessageType.AnimationSync,            // 1352 Phase 6
                (int)MessageType.PerformanceReport,        // 1353
                (int)MessageType.Reconnection,             // 1354
                (int)MessageType.LODConfig,                // 1355
                (int)MessageType.ParticleBudget,           // 1356
                (int)MessageType.MessageCompressionConfig   // 1357
            };

            for (int i = 1; i < allTypes.Length; i++)
            {
                Assert.Equal(allTypes[i - 1] + 1, allTypes[i]);
            }
        }

        [Fact]
        public void MessageType_Phase6HasSixNewTypes()
        {
            int phase5Max = (int)MessageType.InputConfigSync; // 1351
            int phase6Min = (int)MessageType.AnimationSync;   // 1352
            int phase6Max = (int)MessageType.MessageCompressionConfig; // 1357

            Assert.Equal(phase5Max + 1, phase6Min);
            Assert.Equal(6, phase6Max - phase6Min + 1);
        }

        #endregion
    }
}

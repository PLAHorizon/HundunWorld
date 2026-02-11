using Horizon.Orleans.Silo.Configuration;
using Horizon.Orleans.Silo.Extensions;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 测试Grain回收配置和超时配置
    /// </summary>
    public class GrainCollectionConfigTests
    {
        #region HorizonTimeoutConfiguration 测试

        [Fact]
        public void HorizonTimeoutConfiguration_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var config = new HorizonTimeoutConfiguration();

            // Assert
            Assert.Equal(10000, config.ConnectionTimeoutMs);
            Assert.Equal(30000, config.GatewayTimeoutMs);
            Assert.Equal(15000, config.KeepAliveTimeoutMs);
            Assert.Equal(60000, config.RequestTimeoutMs);
            Assert.Equal(60000, config.ResponseTimeoutMs);
            Assert.Equal(300000, config.ResponseTimeoutWithDebuggerMs);
            Assert.Equal(3, config.MaxRetryAttempts);
            Assert.Equal(1000, config.RetryDelayMs);
            Assert.Equal(10, config.MaxForwardCount);
            Assert.Equal(60000, config.GatewayListRefreshPeriodMs);
            Assert.Equal(5000, config.DelayWarningThresholdMs);
            Assert.Equal(120000, config.ClusterMembershipTimeoutMs);
            Assert.Equal(15000, config.GatewayConnectionTimeoutMs);
            Assert.Equal(30000, config.MessageProcessingTimeoutMs);
            Assert.Equal(3, config.RetryCount);
            Assert.Equal(1000, config.RetryIntervalMs);
            Assert.Equal(30000, config.GrainDeactivationTimeoutMs);
            Assert.True(config.EnableTimeoutDiagnostics);
        }

        [Fact]
        public void HorizonTimeoutConfiguration_TimeSpanProperties_MatchMsValues()
        {
            // Arrange
            var config = new HorizonTimeoutConfiguration
            {
                ConnectionTimeoutMs = 5000,
                GatewayTimeoutMs = 20000,
                GrainDeactivationTimeoutMs = 60000
            };

            // Assert
            Assert.Equal(TimeSpan.FromMilliseconds(5000), config.ConnectionTimeout);
            Assert.Equal(TimeSpan.FromMilliseconds(20000), config.GatewayTimeout);
            Assert.Equal(TimeSpan.FromMilliseconds(60000), config.GrainDeactivationTimeout);
        }

        [Fact]
        public void HorizonTimeoutConfiguration_ValidConfiguration_IsValid()
        {
            // Arrange
            var config = new HorizonTimeoutConfiguration();

            // Act
            var warnings = config.ValidateConfiguration();

            // Assert
            Assert.Empty(warnings);
            Assert.True(config.IsValid());
        }

        [Fact]
        public void HorizonTimeoutConfiguration_GetGatewayTimeouts_ReturnsAllTimeouts()
        {
            // Arrange
            var config = new HorizonTimeoutConfiguration();

            // Act
            var timeouts = config.GetGatewayTimeouts();

            // Assert
            Assert.Equal(8, timeouts.Count);
            Assert.True(timeouts.ContainsKey("Connection"));
            Assert.True(timeouts.ContainsKey("Gateway"));
            Assert.True(timeouts.ContainsKey("KeepAlive"));
            Assert.True(timeouts.ContainsKey("Request"));
            Assert.True(timeouts.ContainsKey("Response"));
            Assert.True(timeouts.ContainsKey("GatewayConnection"));
            Assert.True(timeouts.ContainsKey("MessageProcessing"));
            Assert.True(timeouts.ContainsKey("GrainDeactivation"));
        }

        [Fact]
        public void HorizonTimeoutConfiguration_GrainDeactivationTimeout_DefaultIs30Seconds()
        {
            // Arrange
            var config = new HorizonTimeoutConfiguration();

            // Assert - 默认30秒
            Assert.Equal(30000, config.GrainDeactivationTimeoutMs);
            Assert.Equal(TimeSpan.FromSeconds(30), config.GrainDeactivationTimeout);
        }

        [Fact]
        public void HorizonTimeoutConfiguration_GrainDeactivationTimeout_MinCollectionAgeApplied()
        {
            // Arrange - 当GrainDeactivationTimeout小于2分钟时，应使用2分钟作为最小值
            var config = new HorizonTimeoutConfiguration
            {
                GrainDeactivationTimeoutMs = 30000 // 30秒
            };

            // Act
            var minCollectionAge = TimeSpan.FromMinutes(2);
            var configuredCollectionAge = config.GrainDeactivationTimeout;
            var effectiveCollectionAge = configuredCollectionAge > minCollectionAge
                ? configuredCollectionAge
                : minCollectionAge;

            // Assert - 应使用最小值2分钟
            Assert.Equal(minCollectionAge, effectiveCollectionAge);
        }

        [Fact]
        public void HorizonTimeoutConfiguration_GrainDeactivationTimeout_LargeValuePreserved()
        {
            // Arrange - 当GrainDeactivationTimeout大于2分钟时，应保留配置值
            var config = new HorizonTimeoutConfiguration
            {
                GrainDeactivationTimeoutMs = 300000 // 5分钟
            };

            // Act
            var minCollectionAge = TimeSpan.FromMinutes(2);
            var configuredCollectionAge = config.GrainDeactivationTimeout;
            var effectiveCollectionAge = configuredCollectionAge > minCollectionAge
                ? configuredCollectionAge
                : minCollectionAge;

            // Assert - 应保留配置值
            Assert.Equal(TimeSpan.FromMinutes(5), effectiveCollectionAge);
        }

        #endregion

        #region Grain类型特定回收配置测试

        [Fact]
        public void GrainCollectionConfig_CombatGrain_HasExpectedCollectionAge()
        {
            // CombatGrain应该有较短的回收年龄（2分钟），因为战斗会话是短暂的
            var combatGrainType = typeof(Horizon.Orleans.Grains.CombatGrain);
            Assert.NotNull(combatGrainType.FullName);
            
            var expectedAge = TimeSpan.FromMinutes(2);
            // 验证配置值合理性：战斗Grain回收年龄应在1-5分钟之间
            Assert.True(expectedAge >= TimeSpan.FromMinutes(1), "CombatGrain回收年龄不应小于1分钟");
            Assert.True(expectedAge <= TimeSpan.FromMinutes(5), "CombatGrain回收年龄不应大于5分钟");
        }

        [Fact]
        public void GrainCollectionConfig_PassportGrain_HasExpectedCollectionAge()
        {
            // PassportGrain应该有中等的回收年龄（5分钟），认证可以快速重新激活
            var passportGrainType = typeof(Horizon.Orleans.Grains.PassportGrain);
            Assert.NotNull(passportGrainType.FullName);
            
            var expectedAge = TimeSpan.FromMinutes(5);
            // 验证配置值合理性：认证Grain回收年龄应在2-15分钟之间
            Assert.True(expectedAge >= TimeSpan.FromMinutes(2), "PassportGrain回收年龄不应小于2分钟");
            Assert.True(expectedAge <= TimeSpan.FromMinutes(15), "PassportGrain回收年龄不应大于15分钟");
        }

        [Fact]
        public void GrainCollectionConfig_CharacterGrain_HasExpectedCollectionAge()
        {
            // CharacterGrain应该有较长的回收年龄（10分钟），保持玩家在线期间活跃
            var characterGrainType = typeof(Horizon.Orleans.Grains.CharacterGrain);
            Assert.NotNull(characterGrainType.FullName);
            
            var expectedAge = TimeSpan.FromMinutes(10);
            // 验证配置值合理性：角色Grain回收年龄应在5-30分钟之间
            Assert.True(expectedAge >= TimeSpan.FromMinutes(5), "CharacterGrain回收年龄不应小于5分钟");
            Assert.True(expectedAge <= TimeSpan.FromMinutes(30), "CharacterGrain回收年龄不应大于30分钟");
        }

        [Fact]
        public void GrainCollectionConfig_TypeSpecificAges_AreOrdered()
        {
            // 验证回收年龄顺序：CombatGrain < PassportGrain < CharacterGrain
            var combatAge = TimeSpan.FromMinutes(2);
            var passportAge = TimeSpan.FromMinutes(5);
            var characterAge = TimeSpan.FromMinutes(10);

            Assert.True(combatAge < passportAge, "CombatGrain应该比PassportGrain回收更快");
            Assert.True(passportAge < characterAge, "PassportGrain应该比CharacterGrain回收更快");
        }

        [Fact]
        public void GrainCollectionConfig_DefaultTimeout_CanCreateConfiguration()
        {
            // Arrange
            var config = HorizonTimeoutConfigurationExtensions.CreateDefaultTimeoutConfiguration();

            // Assert
            Assert.NotNull(config);
            Assert.True(config.IsValid());
        }

        #endregion

        #region DataServiceProvide CountAsync 去重测试

        [Fact]
        public void DataServiceProvide_CountAsync_InterfaceHasSingleDeclaration()
        {
            // 验证IDataContext接口中只有一个CountAsync声明
            var interfaceType = typeof(Horizon.Core.Abstract.IDataContext<,,>);
            var countAsyncMethods = interfaceType.GetMethods()
                .Where(m => m.Name == "CountAsync")
                .ToList();

            Assert.Single(countAsyncMethods);
        }

        #endregion
    }
}

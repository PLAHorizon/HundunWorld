using Horizon.Orleans.Grains;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// GameServerState 数据模型单元测试
    /// 测试游戏服务器状态管理逻辑
    /// </summary>
    public class GameServerStateTests
    {
        #region GameServerState Default Tests

        [Fact]
        public void GameServerState_DefaultValues_AreCorrect()
        {
            var state = new GameServerState();
            Assert.Equal("", state.ServerName);
            Assert.False(state.IsInitialized);
            Assert.Equal((int)ServerStatus.Normal, state.Status);
            Assert.Equal(0, state.OnlineCount);
            Assert.Equal(5000, state.MaxOnlineCount);
            Assert.Equal(0f, state.CpuUsage);
            Assert.Equal(0f, state.MemoryUsage);
            Assert.Equal(0, state.NetworkLatency);
            Assert.Equal("", state.MaintenanceReason);
            Assert.Equal(0, state.LastUpdateTime);
            Assert.NotNull(state.OnlinePlayers);
            Assert.Empty(state.OnlinePlayers);
        }

        [Fact]
        public void GameServerState_DefaultMaxOnlineCount_Is5000()
        {
            var state = new GameServerState();
            Assert.Equal(5000, state.MaxOnlineCount);
        }

        #endregion

        #region Server Initialization Tests

        [Fact]
        public void GameServerState_Initialize_SetsProperties()
        {
            var state = new GameServerState();

            state.ServerName = "风云一区";
            state.MaxOnlineCount = 3000;
            state.IsInitialized = true;
            state.Status = (int)ServerStatus.Normal;
            state.LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.Equal("风云一区", state.ServerName);
            Assert.Equal(3000, state.MaxOnlineCount);
            Assert.True(state.IsInitialized);
            Assert.True(state.LastUpdateTime > 0);
        }

        #endregion

        #region Online Player Management Tests

        [Fact]
        public void GameServerState_PlayerOnline_IncrementsCount()
        {
            var state = new GameServerState();
            state.OnlinePlayers.Add(Guid.NewGuid());
            Assert.Single(state.OnlinePlayers);
        }

        [Fact]
        public void GameServerState_MultiplePlayersOnline_TracksAll()
        {
            var state = new GameServerState();
            for (int i = 0; i < 10; i++)
            {
                state.OnlinePlayers.Add(Guid.NewGuid());
            }
            Assert.Equal(10, state.OnlinePlayers.Count);
        }

        [Fact]
        public void GameServerState_DuplicatePlayerOnline_Ignored()
        {
            var state = new GameServerState();
            var playerId = Guid.NewGuid();
            state.OnlinePlayers.Add(playerId);
            state.OnlinePlayers.Add(playerId); // duplicate
            Assert.Single(state.OnlinePlayers);
        }

        [Fact]
        public void GameServerState_PlayerOffline_DecreasesCount()
        {
            var state = new GameServerState();
            var player1 = Guid.NewGuid();
            var player2 = Guid.NewGuid();
            state.OnlinePlayers.Add(player1);
            state.OnlinePlayers.Add(player2);
            state.OnlinePlayers.Remove(player1);
            Assert.Single(state.OnlinePlayers);
            Assert.Contains(player2, state.OnlinePlayers);
        }

        [Fact]
        public void GameServerState_PlayerCapacityCheck_WorksCorrectly()
        {
            var state = new GameServerState { MaxOnlineCount = 3 };
            state.OnlinePlayers.Add(Guid.NewGuid());
            state.OnlinePlayers.Add(Guid.NewGuid());
            state.OnlinePlayers.Add(Guid.NewGuid());
            Assert.Equal(state.MaxOnlineCount, state.OnlinePlayers.Count);
        }

        #endregion

        #region Server Status Tests

        [Fact]
        public void GameServerState_SetMaintenance_UpdatesStatus()
        {
            var state = new GameServerState();
            state.Status = (int)ServerStatus.Maintenance;
            state.MaintenanceReason = "定期维护";
            Assert.Equal((int)ServerStatus.Maintenance, state.Status);
            Assert.Equal("定期维护", state.MaintenanceReason);
        }

        [Fact]
        public void GameServerState_ExitMaintenance_ResetsStatus()
        {
            var state = new GameServerState();
            state.Status = (int)ServerStatus.Maintenance;
            state.MaintenanceReason = "定期维护";

            state.Status = (int)ServerStatus.Normal;
            state.MaintenanceReason = "";

            Assert.Equal((int)ServerStatus.Normal, state.Status);
            Assert.Equal("", state.MaintenanceReason);
        }

        [Fact]
        public void GameServerState_AllStatusValues_AreValid()
        {
            var state = new GameServerState();

            state.Status = (int)ServerStatus.Normal;
            Assert.Equal(1, state.Status);

            state.Status = (int)ServerStatus.Busy;
            Assert.Equal(2, state.Status);

            state.Status = (int)ServerStatus.Maintenance;
            Assert.Equal(3, state.Status);

            state.Status = (int)ServerStatus.Fault;
            Assert.Equal(4, state.Status);

            state.Status = (int)ServerStatus.Full;
            Assert.Equal(5, state.Status);
        }

        #endregion

        #region Server Load Tests

        [Fact]
        public void GameServerState_UpdateLoad_SetsValues()
        {
            var state = new GameServerState();
            state.CpuUsage = 45.5f;
            state.MemoryUsage = 60.3f;
            state.NetworkLatency = 25;

            Assert.Equal(45.5f, state.CpuUsage);
            Assert.Equal(60.3f, state.MemoryUsage);
            Assert.Equal(25, state.NetworkLatency);
        }

        [Fact]
        public void GameServerState_HighLoad_DetectedCorrectly()
        {
            var state = new GameServerState();
            state.CpuUsage = 85f;
            state.MemoryUsage = 90f;

            // Business logic: high CPU > 80 or memory > 85 should trigger Busy status
            bool isHighLoad = state.CpuUsage > 80 || state.MemoryUsage > 85;
            Assert.True(isHighLoad);
        }

        [Fact]
        public void GameServerState_NormalLoad_DetectedCorrectly()
        {
            var state = new GameServerState();
            state.CpuUsage = 30f;
            state.MemoryUsage = 45f;

            bool isHighLoad = state.CpuUsage > 80 || state.MemoryUsage > 85;
            Assert.False(isHighLoad);
        }

        [Fact]
        public void GameServerState_LastUpdateTime_TracksChanges()
        {
            var state = new GameServerState();
            var time1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            state.LastUpdateTime = time1;
            Assert.Equal(time1, state.LastUpdateTime);

            var time2 = time1 + 1000;
            state.LastUpdateTime = time2;
            Assert.Equal(time2, state.LastUpdateTime);
            Assert.True(state.LastUpdateTime > time1);
        }

        #endregion
    }
}

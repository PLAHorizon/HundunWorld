using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 针对 GameServerGrain.PlayerOnlineAsync/PlayerOfflineAsync 的回归测试。
    /// 覆盖：BUG 修复核心点（持久化在线列表维护）、防御性校验、幂等性、维护/容量限制。
    /// 这些方法仅依赖 IPersistentState 与 ILogger，不依赖 Orleans runtime，
    /// 因此可使用 Moq 直接 new GameServerGrain 实例进行测试，无需 TestCluster。
    /// </summary>
    public class GameServerGrainPlayerOnlineOfflineTests
    {
        private readonly Mock<IPersistentState<GameServerState>> _stateMock;
        private readonly GameServerState _state;
        private readonly GameServerGrain _grain;

        public GameServerGrainPlayerOnlineOfflineTests()
        {
            _state = new GameServerState
            {
                ServerName = "测试服",
                IsInitialized = true,
                Status = (int)ServerStatus.Normal,
                MaxOnlineCount = 3,
                OnlinePlayers = new HashSet<long>()
            };
            _stateMock = new Mock<IPersistentState<GameServerState>>();
            _stateMock.SetupGet(s => s.State).Returns(_state);

            _grain = new GameServerGrain(NullLogger<GameServerGrain>.Instance, _stateMock.Object);
        }

        // ============================================================
        // PlayerOnlineAsync 防御性校验
        // ============================================================

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(long.MinValue)]
        public async Task PlayerOnlineAsync_RejectsInvalidCharacterId(long characterId)
        {
            var result = await _grain.PlayerOnlineAsync(characterId);

            Assert.False(result);
            Assert.Empty(_state.OnlinePlayers);
            // 不应触发持久化写入
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Never);
        }

        // ============================================================
        // PlayerOnlineAsync 正常路径
        // ============================================================

        [Fact]
        public async Task PlayerOnlineAsync_AddsPlayerAndPersists()
        {
            var result = await _grain.PlayerOnlineAsync(1001L);

            Assert.True(result);
            Assert.Contains(1001L, _state.OnlinePlayers);
            Assert.Single(_state.OnlinePlayers);
            // 核心修复点：必须立即落盘
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Once);
        }

        [Fact]
        public async Task PlayerOnlineAsync_UpdatesLastUpdateTime()
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _grain.PlayerOnlineAsync(1001L);

            Assert.True(_state.LastUpdateTime >= before);
        }

        // ============================================================
        // PlayerOnlineAsync 幂等性
        // ============================================================

        [Fact]
        public async Task PlayerOnlineAsync_Idempotent_ReturnsTrueAndDoesNotDuplicateWrite()
        {
            // 第一次上线：触发一次 WriteStateAsync
            var first = await _grain.PlayerOnlineAsync(1001L);
            Assert.True(first);
            var writesAfterFirst = _stateMock.Invocations.Count(i => i.Method.Name == nameof(IPersistentState<GameServerState>.WriteStateAsync));

            // 第二次上线（重复）：应返回 true，且不再触发持久化写入
            var second = await _grain.PlayerOnlineAsync(1001L);

            Assert.True(second);
            Assert.Single(_state.OnlinePlayers);
            var writesAfterSecond = _stateMock.Invocations.Count(i => i.Method.Name == nameof(IPersistentState<GameServerState>.WriteStateAsync));
            Assert.Equal(writesAfterFirst, writesAfterSecond);
        }

        // ============================================================
        // PlayerOnlineAsync 维护状态拒绝
        // ============================================================

        [Fact]
        public async Task PlayerOnlineAsync_RejectsDuringMaintenance()
        {
            _state.Status = (int)ServerStatus.Maintenance;
            _state.MaintenanceReason = "定期维护";

            var result = await _grain.PlayerOnlineAsync(1001L);

            Assert.False(result);
            Assert.Empty(_state.OnlinePlayers);
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Never);
        }

        // ============================================================
        // PlayerOnlineAsync 容量限制
        // ============================================================

        [Fact]
        public async Task PlayerOnlineAsync_RejectsWhenServerFull()
        {
            // MaxOnlineCount = 3，先填满
            await _grain.PlayerOnlineAsync(1001L);
            await _grain.PlayerOnlineAsync(1002L);
            await _grain.PlayerOnlineAsync(1003L);
            _stateMock.Invocations.Clear();

            var result = await _grain.PlayerOnlineAsync(1004L);

            Assert.False(result);
            Assert.DoesNotContain(1004L, _state.OnlinePlayers);
            Assert.Equal(3, _state.OnlinePlayers.Count);
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Never);
        }

        // ============================================================
        // PlayerOfflineAsync 防御性校验
        // ============================================================

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(long.MinValue)]
        public async Task PlayerOfflineAsync_RejectsInvalidCharacterId(long characterId)
        {
            var result = await _grain.PlayerOfflineAsync(characterId);

            Assert.False(result);
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Never);
        }

        // ============================================================
        // PlayerOfflineAsync 正常路径
        // ============================================================

        [Fact]
        public async Task PlayerOfflineAsync_RemovesPlayerAndPersists()
        {
            _state.OnlinePlayers.Add(1001L);
            _state.OnlinePlayers.Add(1002L);

            var result = await _grain.PlayerOfflineAsync(1001L);

            Assert.True(result);
            Assert.DoesNotContain(1001L, _state.OnlinePlayers);
            Assert.Single(_state.OnlinePlayers);
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Once);
        }

        // ============================================================
        // PlayerOfflineAsync 幂等性（BUG 修复核心场景）
        //
        // 场景：GoOfflineAsync 内部已调用 PlayerOfflineAsync，
        // 随后 PlayerDespawnScheduler 兜底再次调用。
        // 期望：第二次调用返回 true（最终状态一致），不抛异常，不重复写持久化。
        // ============================================================

        [Fact]
        public async Task PlayerOfflineAsync_Idempotent_ReturnsTrueWhenAlreadyOffline()
        {
            _state.OnlinePlayers.Add(1001L);

            // 第一次下线：成功移除，触发一次 WriteStateAsync
            var first = await _grain.PlayerOfflineAsync(1001L);
            Assert.True(first);
            var writesAfterFirst = _stateMock.Invocations.Count(i => i.Method.Name == nameof(IPersistentState<GameServerState>.WriteStateAsync));

            // 第二次下线（兜底重复调用）：应返回 true（幂等），且不再触发持久化写入
            var second = await _grain.PlayerOfflineAsync(1001L);

            Assert.True(second);
            Assert.Empty(_state.OnlinePlayers);
            var writesAfterSecond = _stateMock.Invocations.Count(i => i.Method.Name == nameof(IPersistentState<GameServerState>.WriteStateAsync));
            Assert.Equal(writesAfterFirst, writesAfterSecond);
        }

        // ============================================================
        // PlayerOfflineAsync 幂等性（角色从未上线）
        // ============================================================

        [Fact]
        public async Task PlayerOfflineAsync_Idempotent_ReturnsTrueWhenNeverOnline()
        {
            // 角色从未上线，直接调用下线（兜底场景）
            var result = await _grain.PlayerOfflineAsync(9999L);

            // 应返回 true（最终状态一致），避免兜底调用方误判失败
            Assert.True(result);
            Assert.Empty(_state.OnlinePlayers);
            _stateMock.Verify(s => s.WriteStateAsync(), Times.Never);
        }

        // ============================================================
        // 端到端：上线 → 下线 → 再次上线
        // ============================================================

        [Fact]
        public async Task PlayerOnlineThenOfflineThenOnline_RoundtripWorks()
        {
            // 上线
            Assert.True(await _grain.PlayerOnlineAsync(1001L));
            Assert.Single(_state.OnlinePlayers);

            // 下线
            Assert.True(await _grain.PlayerOfflineAsync(1001L));
            Assert.Empty(_state.OnlinePlayers);

            // 再次上线（验证不会因之前的下线状态而失败）
            Assert.True(await _grain.PlayerOnlineAsync(1001L));
            Assert.Single(_state.OnlinePlayers);
        }

        // ============================================================
        // 多角色并发场景（验证集合行为）
        // ============================================================

        [Fact]
        public async Task PlayerOnlineAsync_MultiplePlayers_AllTracked()
        {
            await _grain.PlayerOnlineAsync(1001L);
            await _grain.PlayerOnlineAsync(1002L);
            await _grain.PlayerOnlineAsync(1003L);

            Assert.Equal(3, _state.OnlinePlayers.Count);
            Assert.Contains(1001L, _state.OnlinePlayers);
            Assert.Contains(1002L, _state.OnlinePlayers);
            Assert.Contains(1003L, _state.OnlinePlayers);

            // 下线中间一个，其余两个应保留
            Assert.True(await _grain.PlayerOfflineAsync(1002L));
            Assert.Equal(2, _state.OnlinePlayers.Count);
            Assert.DoesNotContain(1002L, _state.OnlinePlayers);
            Assert.Contains(1001L, _state.OnlinePlayers);
            Assert.Contains(1003L, _state.OnlinePlayers);
        }

        // ============================================================
        // GetOnlinePlayerCountAsync 反映 OnlinePlayers.Count
        // ============================================================

        [Fact]
        public async Task GetOnlinePlayerCountAsync_ReflectsOnlinePlayersCount()
        {
            Assert.Equal(0, await _grain.GetOnlinePlayerCountAsync());

            await _grain.PlayerOnlineAsync(1001L);
            await _grain.PlayerOnlineAsync(1002L);
            Assert.Equal(2, await _grain.GetOnlinePlayerCountAsync());

            await _grain.PlayerOfflineAsync(1001L);
            Assert.Equal(1, await _grain.GetOnlinePlayerCountAsync());
        }
    }
}

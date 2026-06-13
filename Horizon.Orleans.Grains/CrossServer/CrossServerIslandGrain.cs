using Horizon.Orleans.Interface.CrossServer;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.CrossServer
{
    public class CrossServerIslandGrain : Grain, ICrossServerIslandGrain
    {
        private readonly ILogger<CrossServerIslandGrain> _logger;
        private string _matchId;
        private int _maxCapacity = 1000;
        private HashSet<long> _players = new HashSet<long>();

        public CrossServerIslandGrain(ILogger<CrossServerIslandGrain> logger)
        {
            _logger = logger;
        }

        public Task InitializeIslandAsync(string matchId, int maxCapacity)
        {
            _matchId = matchId;
            _maxCapacity = maxCapacity;
            _logger.LogInformation($"岛屿 {this.GetPrimaryKeyLong()} 已为比赛 {matchId} 初始化，容量: {maxCapacity}");
            return Task.CompletedTask;
        }

        public Task<bool> PlayerEnterAsync(long characterId, int sourceServerId)
        {
            if (_players.Count >= _maxCapacity) return Task.FromResult(false);
            
            _players.Add(characterId);
            _logger.LogInformation($"玩家 {characterId} 来自服务器 {sourceServerId} 已进入岛屿 {this.GetPrimaryKeyLong()}");
            return Task.FromResult(true);
        }

        public Task PlayerLeaveAsync(long characterId)
        {
            _players.Remove(characterId);
            _logger.LogInformation($"玩家 {characterId} 已离开岛屿 {this.GetPrimaryKeyLong()}");
            return Task.CompletedTask;
        }

        public Task<int> GetPlayerCountAsync()
        {
            return Task.FromResult(_players.Count);
        }

        public Task BroadcastGlobalMessageAsync(string message)
        {
            _logger.LogInformation($"[Island {this.GetPrimaryKeyLong()} Broadcast] {message}");
            // Integration with chat services would exist here
            return Task.CompletedTask;
        }

        public Task ReportKillAsync(long killerId, long victimId)
        {
            _logger.LogInformation($"[Island {this.GetPrimaryKeyLong()}] Player {killerId} killed {victimId}");
            return Task.CompletedTask;
        }
    }
}
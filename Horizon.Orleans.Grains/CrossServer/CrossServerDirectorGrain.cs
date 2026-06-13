using Horizon.Orleans.Interface.CrossServer;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.CrossServer
{
    public class CrossServerDirectorGrain : Grain, ICrossServerDirectorGrain
    {
        private readonly ILogger<CrossServerDirectorGrain> _logger;
        private List<string> _activeMatches = new List<string>();

        public CrossServerDirectorGrain(ILogger<CrossServerDirectorGrain> logger)
        {
            _logger = logger;
        }

        public Task<string> CreateCrossServerMatchAsync(int battleType, List<int> serverIds)
        {
            string matchId = Guid.NewGuid().ToString();
            _activeMatches.Add(matchId);
            _logger.LogInformation($"已创建跨服比赛 {matchId}，类型: {battleType}，参与服务器: {string.Join(",", serverIds)}");
            return Task.FromResult(matchId);
        }

        public Task<bool> EndMatchAsync(string matchId, int winnerServerId)
        {
            if (_activeMatches.Contains(matchId))
            {
                _activeMatches.Remove(matchId);
                _logger.LogInformation($"跨服比赛 {matchId} 已结束。获胜服务器: {winnerServerId}");
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<List<string>> GetActiveMatchesAsync()
        {
            return Task.FromResult(_activeMatches);
        }
    }
}
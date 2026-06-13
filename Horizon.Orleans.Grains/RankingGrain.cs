using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 排行榜系统Grain实现 - 负责排行榜管理
    /// </summary>
    public class RankingGrain : Grain, IRankingGrain
    {
        private readonly ILogger<RankingGrain> _logger;
        private readonly IPersistentState<RankingState> _rankingState;

        public RankingGrain(
            ILogger<RankingGrain> logger,
            [PersistentState("ranking", "GameStore")] IPersistentState<RankingState> rankingState)
        {
            _logger = logger;
            _rankingState = rankingState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RankingGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_rankingState.State.Entries == null)
                _rankingState.State.Entries = new Dictionary<Guid, RankingEntry>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> InitializeAsync(int rankingType, string rankingName, int maxEntries = 100)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rankingName))
                {
                    _logger.LogWarning("排行榜名称无效");
                    return false;
                }

                if (maxEntries <= 0 || maxEntries > 1000)
                {
                    _logger.LogWarning("排行榜最大条目数无效: MaxEntries={MaxEntries}", maxEntries);
                    return false;
                }

                var state = _rankingState.State;
                state.RankingType = rankingType;
                state.RankingName = rankingName.Trim();
                state.MaxEntries = maxEntries;
                state.LastUpdateTime = DateTime.Now;

                await _rankingState.WriteStateAsync();

                _logger.LogInformation("初始化排行榜: Type={RankingType}, Name={RankingName}, MaxEntries={MaxEntries}",
                    rankingType, rankingName, maxEntries);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化排行榜失败: Type={RankingType}", rankingType);
                throw;
            }
        }

        public async Task<int> UpdateScoreAsync(Guid playerId, string playerName, long score)
        {
            try
            {
                if (playerId == Guid.Empty)
                {
                    _logger.LogWarning("玩家ID无效");
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(playerName))
                {
                    _logger.LogWarning("玩家名称无效");
                    return -1;
                }

                var state = _rankingState.State;
                var now = DateTime.Now;

                if (state.Entries.TryGetValue(playerId, out var existingEntry))
                {
                    existingEntry.Score = score;
                    existingEntry.PlayerName = playerName.Trim();
                    existingEntry.UpdateTime = now;
                }
                else
                {
                    state.Entries[playerId] = new RankingEntry
                    {
                        PlayerId = playerId,
                        PlayerName = playerName.Trim(),
                        Score = score,
                        UpdateTime = now
                    };
                }

                // Recalculate ranks (sorted by score descending)
                var sorted = state.Entries.Values
                    .OrderByDescending(e => e.Score)
                    .ThenBy(e => e.UpdateTime)
                    .ToList();

                // Trim to max entries
                if (sorted.Count > state.MaxEntries)
                {
                    var toRemove = sorted.Skip(state.MaxEntries).ToList();
                    foreach (var entry in toRemove)
                    {
                        state.Entries.Remove(entry.PlayerId);
                    }
                    sorted = sorted.Take(state.MaxEntries).ToList();
                }

                // Assign ranks
                int playerRank = -1;
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Rank = i + 1;
                    if (sorted[i].PlayerId == playerId)
                    {
                        playerRank = i + 1;
                    }
                }

                state.LastUpdateTime = now;
                await _rankingState.WriteStateAsync();

                _logger.LogInformation("更新排行榜分数: PlayerId={PlayerId}, Score={Score}, Rank={Rank}",
                    playerId, score, playerRank);
                return playerRank;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新排行榜分数失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<List<RankingEntry>> GetTopEntriesAsync(int count)
        {
            try
            {
                if (count <= 0)
                    count = 10;

                var entries = _rankingState.State.Entries.Values
                    .OrderBy(e => e.Rank)
                    .Take(count)
                    .ToList();

                return Task.FromResult(entries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取排行榜前N名失败");
                throw;
            }
        }

        public Task<int> GetPlayerRankAsync(Guid playerId)
        {
            try
            {
                if (_rankingState.State.Entries.TryGetValue(playerId, out var entry))
                {
                    return Task.FromResult(entry.Rank);
                }

                return Task.FromResult(-1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取玩家排名失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<RankingEntry?> GetPlayerEntryAsync(Guid playerId)
        {
            try
            {
                if (_rankingState.State.Entries.TryGetValue(playerId, out var entry))
                {
                    return Task.FromResult<RankingEntry?>(entry);
                }

                return Task.FromResult<RankingEntry?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取玩家排名条目失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<RankingInfo> GetRankingInfoAsync()
        {
            try
            {
                var state = _rankingState.State;
                var info = new RankingInfo
                {
                    RankingType = state.RankingType,
                    RankingName = state.RankingName,
                    MaxEntries = state.MaxEntries,
                    LastUpdateTime = state.LastUpdateTime,
                    Entries = state.Entries.Values
                        .OrderBy(e => e.Rank)
                        .ToList()
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取排行榜信息失败");
                throw;
            }
        }

        public async Task<bool> RemovePlayerAsync(Guid playerId)
        {
            try
            {
                var state = _rankingState.State;

                if (!state.Entries.Remove(playerId))
                {
                    _logger.LogWarning("玩家不在排行榜中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                // Recalculate ranks
                var sorted = state.Entries.Values
                    .OrderByDescending(e => e.Score)
                    .ThenBy(e => e.UpdateTime)
                    .ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Rank = i + 1;
                }

                state.LastUpdateTime = DateTime.Now;
                await _rankingState.WriteStateAsync();

                _logger.LogInformation("移除玩家排名: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除玩家排名失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> ResetRankingAsync()
        {
            try
            {
                var state = _rankingState.State;
                state.Entries.Clear();
                state.LastUpdateTime = DateTime.Now;

                await _rankingState.WriteStateAsync();

                _logger.LogInformation("重置排行榜: Type={RankingType}, Name={RankingName}",
                    state.RankingType, state.RankingName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置排行榜失败");
                throw;
            }
        }
    }
}

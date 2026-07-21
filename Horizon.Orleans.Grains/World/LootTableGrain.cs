using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.2 掉落表 Grain 实现。<br/>
/// 概率掉落 + 保底机制（连续 N 次未出稀有则必出）。
/// </summary>
public sealed class LootTableGrain : Grain, ILootTableGrain
{
    private readonly ILogger<LootTableGrain> _logger;
    private LootTableConfig _config = null!;

    /// <summary>保底计数器（killerId → 连续未出稀有的次数）。</summary>
    private readonly Dictionary<long, int> _pityCounters = new();

    private static readonly Random _random = new();

    public LootTableGrain(ILogger<LootTableGrain> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(LootTableConfig config)
    {
        _config = config;
        _logger.LogInformation(
            "掉落表初始化。TableId={TableId}, Name={Name}, Entries={Count}, PityThreshold={Pity}",
            config.TableId, config.Name, config.Entries.Length, config.PityThreshold);
        return Task.CompletedTask;
    }

    public Task<LootDropResult[]> RollLootAsync(long killerId, float luckBonus = 0f)
    {
        if (_config == null || _config.Entries.Length == 0)
            return Task.FromResult(Array.Empty<LootDropResult>());

        var results = new List<LootDropResult>();
        var pityCounter = _pityCounters.GetValueOrDefault(killerId, 0);
        var triggeredPity = false;

        // 检查保底
        if (pityCounter >= _config.PityThreshold)
        {
            // 保底触发：必出一个符合稀有度阈值的物品
            var pityEligible = _config.Entries
                .Where(e => e.IsPityEligible && e.Rarity >= _config.PityRarityThreshold)
                .ToArray();

            if (pityEligible.Length > 0)
            {
                var pityEntry = pityEligible[_random.Next(pityEligible.Length)];
                results.Add(new LootDropResult
                {
                    ItemId = pityEntry.ItemId,
                    ItemName = pityEntry.ItemName,
                    Count = RollCount(pityEntry),
                    Rarity = pityEntry.Rarity,
                    IsPityDrop = true,
                });
                triggeredPity = true;
                _pityCounters[killerId] = 0;

                _logger.LogInformation(
                    "保底触发。TableId={TableId}, Killer={Killer}, Item={ItemId}, Rarity={Rarity}",
                    _config.TableId, killerId, pityEntry.ItemId, pityEntry.Rarity);
            }
        }

        // 常规掉落
        var gotRare = false;
        foreach (var entry in _config.Entries)
        {
            var adjustedRate = Math.Clamp(entry.DropRate * (1f + luckBonus), 0f, 1f);
            if (_random.NextSingle() < adjustedRate)
            {
                results.Add(new LootDropResult
                {
                    ItemId = entry.ItemId,
                    ItemName = entry.ItemName,
                    Count = RollCount(entry),
                    Rarity = entry.Rarity,
                    IsPityDrop = false,
                });

                if (entry.Rarity >= _config.PityRarityThreshold)
                    gotRare = true;
            }
        }

        // 更新保底计数
        if (!triggeredPity)
        {
            _pityCounters[killerId] = gotRare ? 0 : pityCounter + 1;
        }

        return Task.FromResult(results.ToArray());
    }

    public Task<LootTableConfig> GetConfigAsync() => Task.FromResult(_config);

    private static int RollCount(LootEntry entry)
    {
        if (entry.MinCount >= entry.MaxCount)
            return entry.MinCount;
        return _random.Next(entry.MinCount, entry.MaxCount + 1);
    }
}

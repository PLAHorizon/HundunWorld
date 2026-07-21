using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P2.2 掉落表 Grain 契约。<br/>
/// Grain Primary Key = lootTableId（对应配置表）。<br/>
/// 负责：概率掉落计算、保底机制、掉落组管理。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface ILootTableGrain : IGrainWithIntegerKey
{
    /// <summary>初始化掉落表配置。</summary>
    Task InitializeAsync(LootTableConfig config);

    /// <summary>
    /// 执行一次掉落计算。
    /// </summary>
    /// <param name="killerId">击杀者 ID（用于保底计数）。</param>
    /// <param name="luckBonus">幸运加成（0-1，影响概率）。</param>
    /// <returns>掉落结果列表。</returns>
    Task<LootDropResult[]> RollLootAsync(long killerId, float luckBonus = 0f);

    /// <summary>获取掉落表配置。</summary>
    Task<LootTableConfig> GetConfigAsync();
}

/// <summary>掉落表配置。</summary>
[GenerateSerializer]
public sealed class LootTableConfig
{
    [Id(0)] public int TableId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public LootEntry[] Entries { get; set; } = Array.Empty<LootEntry>();
    /// <summary>保底次数：连续 N 次未出稀有则必出。</summary>
    [Id(3)] public int PityThreshold { get; set; } = 50;
    /// <summary>保底稀有度阈值。</summary>
    [Id(4)] public int PityRarityThreshold { get; set; } = 4;
}

/// <summary>掉落条目。</summary>
[GenerateSerializer]
public sealed class LootEntry
{
    [Id(0)] public int ItemId { get; set; }
    [Id(1)] public string ItemName { get; set; } = string.Empty;
    /// <summary>掉落概率（0-1）。</summary>
    [Id(2)] public float DropRate { get; set; }
    /// <summary>最小数量。</summary>
    [Id(3)] public int MinCount { get; set; } = 1;
    /// <summary>最大数量。</summary>
    [Id(4)] public int MaxCount { get; set; } = 1;
    /// <summary>稀有度（1-5，5 最稀有）。</summary>
    [Id(5)] public int Rarity { get; set; } = 1;
    /// <summary>是否受保底机制影响。</summary>
    [Id(6)] public bool IsPityEligible { get; set; }
}

/// <summary>掉落结果。</summary>
[GenerateSerializer]
public sealed class LootDropResult
{
    [Id(0)] public int ItemId { get; set; }
    [Id(1)] public string ItemName { get; set; } = string.Empty;
    [Id(2)] public int Count { get; set; }
    [Id(3)] public int Rarity { get; set; }
    [Id(4)] public bool IsPityDrop { get; set; }
}

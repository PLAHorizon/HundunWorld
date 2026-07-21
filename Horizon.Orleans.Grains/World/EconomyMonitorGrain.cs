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
/// P2.3 经济监控 Grain 实现（全局单例）。<br/>
/// 异步追踪货币流向，检测异常交易行为。
/// </summary>
public sealed class EconomyMonitorGrain : Grain, IEconomyMonitorGrain
{
    private readonly ILogger<EconomyMonitorGrain> _logger;

    // 统计周期
    private DateTime _periodStart = DateTime.UtcNow;
    private long _totalMinted;
    private long _totalBurned;
    private long _totalTradeVolume;
    private int _tradeCount;
    private int _anomalyCount;

    // 玩家行为追踪（用于异常检测）
    private readonly Dictionary<long, PlayerEconomyProfile> _playerProfiles = new();

    // 异常阈值配置
    private const long GoldFarmingThresholdPerHour = 100_000; // 每小时产出超过 10 万金币视为刷金
    private const int TradeFrequencyThreshold = 50; // 每小时交易超过 50 次视为洗钱
    private const long LargeTradeThreshold = 50_000; // 单笔大额交易阈值

    public EconomyMonitorGrain(ILogger<EconomyMonitorGrain> logger)
    {
        _logger = logger;
    }

    public Task RecordCurrencyMintAsync(long amount, string source, long characterId)
    {
        _totalMinted += amount;

        var profile = GetOrCreateProfile(characterId);
        profile.TotalMinted += amount;
        profile.MintCount++;
        profile.LastActivityTime = DateTime.UtcNow;

        // 实时刷金检测
        var hourlyMint = profile.GetHourlyMintRate();
        if (hourlyMint > GoldFarmingThresholdPerHour)
        {
            _anomalyCount++;
            _logger.LogWarning(
                "经济异常：疑似刷金。CharacterId={CharacterId}, HourlyMint={HourlyMint}, Source={Source}",
                characterId, hourlyMint, source);
        }

        return Task.CompletedTask;
    }

    public Task RecordCurrencyBurnAsync(long amount, string sink, long characterId)
    {
        _totalBurned += amount;

        var profile = GetOrCreateProfile(characterId);
        profile.TotalBurned += amount;
        profile.BurnCount++;

        return Task.CompletedTask;
    }

    public Task RecordTradeAsync(long fromId, long toId, long amount, string itemType, int itemCount)
    {
        _totalTradeVolume += amount;
        _tradeCount++;

        var fromProfile = GetOrCreateProfile(fromId);
        var toProfile = GetOrCreateProfile(toId);

        fromProfile.TotalTradedOut += amount;
        fromProfile.TradeCount++;
        fromProfile.LastActivityTime = DateTime.UtcNow;

        toProfile.TotalTradedIn += amount;
        toProfile.TradeCount++;
        toProfile.LastActivityTime = DateTime.UtcNow;

        // 大额交易记录
        if (amount >= LargeTradeThreshold)
        {
            _logger.LogInformation(
                "大额交易。From={From}, To={To}, Amount={Amount}, Item={Item}x{Count}",
                fromId, toId, amount, itemType, itemCount);
        }

        // 洗钱检测：频繁小额交易
        if (fromProfile.TradeCount > TradeFrequencyThreshold)
        {
            var avgAmount = fromProfile.TotalTradedOut / fromProfile.TradeCount;
            if (avgAmount < LargeTradeThreshold / 10) // 平均金额很小
            {
                _anomalyCount++;
                _logger.LogWarning(
                    "经济异常：疑似洗钱。CharacterId={CharacterId}, TradeCount={TradeCount}, AvgAmount={Avg}",
                    fromId, fromProfile.TradeCount, avgAmount);
            }
        }

        return Task.CompletedTask;
    }

    public Task<EconomyReport> GetReportAsync()
    {
        var netFlow = _totalMinted - _totalBurned;
        var inflationRate = _totalMinted > 0 ? (float)netFlow / _totalMinted : 0f;

        return Task.FromResult(new EconomyReport
        {
            PeriodStart = _periodStart,
            TotalMinted = _totalMinted,
            TotalBurned = _totalBurned,
            NetFlow = netFlow,
            TotalTradeVolume = _totalTradeVolume,
            TradeCount = _tradeCount,
            InflationRate = inflationRate,
            AnomalyCount = _anomalyCount,
        });
    }

    public Task<AnomalyDetectionResult> DetectAnomaliesAsync(long characterId)
    {
        if (!_playerProfiles.TryGetValue(characterId, out var profile))
            return Task.FromResult(new AnomalyDetectionResult { HasAnomaly = false });

        // 刷金检测
        var hourlyMint = profile.GetHourlyMintRate();
        if (hourlyMint > GoldFarmingThresholdPerHour)
        {
            return Task.FromResult(new AnomalyDetectionResult
            {
                HasAnomaly = true,
                Type = AnomalyType.GoldFarming,
                Description = $"每小时金币产出 {hourlyMint:N0} 超过阈值 {GoldFarmingThresholdPerHour:N0}",
                SuspiciousAmount = hourlyMint,
            });
        }

        // 洗钱检测
        if (profile.TradeCount > TradeFrequencyThreshold)
        {
            var avgAmount = profile.TotalTradedOut / Math.Max(1, profile.TradeCount);
            if (avgAmount < LargeTradeThreshold / 10)
            {
                return Task.FromResult(new AnomalyDetectionResult
                {
                    HasAnomaly = true,
                    Type = AnomalyType.MoneyLaundering,
                    Description = $"交易频率 {profile.TradeCount} 次/小时，平均金额 {avgAmount:N0}",
                    SuspiciousAmount = profile.TotalTradedOut,
                });
            }
        }

        return Task.FromResult(new AnomalyDetectionResult { HasAnomaly = false });
    }

    private PlayerEconomyProfile GetOrCreateProfile(long characterId)
    {
        if (!_playerProfiles.TryGetValue(characterId, out var profile))
        {
            profile = new PlayerEconomyProfile { CharacterId = characterId, FirstSeenTime = DateTime.UtcNow };
            _playerProfiles[characterId] = profile;
        }
        return profile;
    }

    /// <summary>玩家经济行为画像。</summary>
    private sealed class PlayerEconomyProfile
    {
        public long CharacterId { get; init; }
        public DateTime FirstSeenTime { get; init; }
        public DateTime LastActivityTime { get; set; }
        public long TotalMinted { get; set; }
        public long TotalBurned { get; set; }
        public int MintCount { get; set; }
        public int BurnCount { get; set; }
        public long TotalTradedIn { get; set; }
        public long TotalTradedOut { get; set; }
        public int TradeCount { get; set; }

        public long GetHourlyMintRate()
        {
            var hours = Math.Max(1, (DateTime.UtcNow - FirstSeenTime).TotalHours);
            return (long)(TotalMinted / hours);
        }
    }
}

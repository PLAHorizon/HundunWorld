using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P2.3 经济监控 Grain 契约（全局单例）。<br/>
/// 负责：货币总量追踪、通胀率计算、异常交易检测、经济报告生成。<br/>
/// 不侵入交易逻辑，异步分析。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IEconomyMonitorGrain : IGrainWithIntegerKey
{
    /// <summary>记录货币产出（怪物掉落/任务奖励/系统发放）。</summary>
    Task RecordCurrencyMintAsync(long amount, string source, long characterId);

    /// <summary>记录货币消耗（修理/强化/交易税/系统回收）。</summary>
    Task RecordCurrencyBurnAsync(long amount, string sink, long characterId);

    /// <summary>记录交易（玩家间金币转移）。</summary>
    Task RecordTradeAsync(long fromId, long toId, long amount, string itemType, int itemCount);

    /// <summary>获取经济报告。</summary>
    Task<EconomyReport> GetReportAsync();

    /// <summary>检测异常交易（刷金/复制/洗钱）。</summary>
    Task<AnomalyDetectionResult> DetectAnomaliesAsync(long characterId);
}

/// <summary>经济报告。</summary>
[GenerateSerializer]
public sealed class EconomyReport
{
    /// <summary>统计周期开始时间。</summary>
    [Id(0)] public DateTime PeriodStart { get; set; }
    /// <summary>总产出（金币）。</summary>
    [Id(1)] public long TotalMinted { get; set; }
    /// <summary>总消耗（金币）。</summary>
    [Id(2)] public long TotalBurned { get; set; }
    /// <summary>净流入（产出-消耗）。</summary>
    [Id(3)] public long NetFlow { get; set; }
    /// <summary>交易总额。</summary>
    [Id(4)] public long TotalTradeVolume { get; set; }
    /// <summary>交易笔数。</summary>
    [Id(5)] public int TradeCount { get; set; }
    /// <summary>估算通胀率（净流入/总产出）。</summary>
    [Id(6)] public float InflationRate { get; set; }
    /// <summary>异常交易数。</summary>
    [Id(7)] public int AnomalyCount { get; set; }
}

/// <summary>异常检测结果。</summary>
[GenerateSerializer]
public sealed class AnomalyDetectionResult
{
    [Id(0)] public bool HasAnomaly { get; set; }
    [Id(1)] public AnomalyType Type { get; set; }
    [Id(2)] public string Description { get; set; } = string.Empty;
    [Id(3)] public long SuspiciousAmount { get; set; }
}

/// <summary>异常类型。</summary>
[GenerateSerializer]
public enum AnomalyType : byte
{
    None = 0,
    /// <summary>刷金（短时间大量产出）。</summary>
    GoldFarming = 1,
    /// <summary>复制（同一物品多次交易）。</summary>
    Duplication = 2,
    /// <summary>洗钱（频繁小额转移）。</summary>
    MoneyLaundering = 3,
    /// <summary>价格操纵（拍卖行异常出价）。</summary>
    PriceManipulation = 4,
}

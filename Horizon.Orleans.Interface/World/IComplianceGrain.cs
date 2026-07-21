using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P3.5 合规服务 Grain 契约（全局单例）。<br/>
/// 负责：实名认证、防沉迷限制、未成年人保护。<br/>
/// 符合国内游戏合规要求。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IComplianceGrain : IGrainWithIntegerKey
{
    /// <summary>验证实名信息。</summary>
    Task<RealNameVerifyResult> VerifyRealNameAsync(long characterId, RealNameInfo info);

    /// <summary>获取防沉迷状态。</summary>
    Task<AntiAddictionStatus> GetAntiAddictionStatusAsync(long characterId);

    /// <summary>检查是否允许登录。</summary>
    Task<LoginCheckResult> CheckLoginAllowedAsync(long characterId);

    /// <summary>记录在线时长。</summary>
    Task RecordOnlineTimeAsync(long characterId, int minutes);

    /// <summary>检查消费限制。</summary>
    Task<PurchaseCheckResult> CheckPurchaseAllowedAsync(long characterId, long amount);

    /// <summary>获取合规统计。</summary>
    Task<ComplianceStats> GetStatsAsync();
}

/// <summary>实名信息。</summary>
[GenerateSerializer]
public sealed class RealNameInfo
{
    [Id(0)] public string RealName { get; set; } = string.Empty;
    [Id(1)] public string IdCardNumber { get; set; } = string.Empty;
}

/// <summary>实名验证结果。</summary>
[GenerateSerializer]
public sealed class RealNameVerifyResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(2)] public AgeGroup AgeGroup { get; set; }
    [Id(3)] public bool IsVerified { get; set; }
}

/// <summary>防沉迷状态。</summary>
[GenerateSerializer]
public sealed class AntiAddictionStatus
{
    [Id(0)] public long CharacterId { get; set; }
    [Id(1)] public AgeGroup AgeGroup { get; set; }
    [Id(2)] public int TodayOnlineMinutes { get; set; }
    [Id(3)] public int RemainingMinutes { get; set; }
    [Id(4)] public bool IsRestricted { get; set; }
    [Id(5)] public string RestrictionReason { get; set; } = string.Empty;
    [Id(6)] public long TodaySpentAmount { get; set; }
    [Id(7)] public long MonthlySpentAmount { get; set; }
}

/// <summary>登录检查结果。</summary>
[GenerateSerializer]
public sealed class LoginCheckResult
{
    [Id(0)] public bool Allowed { get; set; }
    [Id(1)] public string Reason { get; set; } = string.Empty;
    [Id(2)] public DateTime? AllowedAfter { get; set; }
}

/// <summary>消费检查结果。</summary>
[GenerateSerializer]
public sealed class PurchaseCheckResult
{
    [Id(0)] public bool Allowed { get; set; }
    [Id(1)] public string Reason { get; set; } = string.Empty;
    [Id(2)] public long RemainingQuota { get; set; }
}

/// <summary>合规统计。</summary>
[GenerateSerializer]
public sealed class ComplianceStats
{
    [Id(0)] public int TotalVerifiedUsers { get; set; }
    [Id(1)] public int MinorUsers { get; set; }
    [Id(2)] public int AdultUsers { get; set; }
    [Id(3)] public int CurrentlyRestricted { get; set; }
    [Id(4)] public long TotalPurchaseBlocked { get; set; }
}

/// <summary>年龄分组。</summary>
[GenerateSerializer]
public enum AgeGroup : byte
{
    /// <summary>未验证。</summary>
    Unverified = 0,
    /// <summary>未成年人（<18岁）。</summary>
    Minor = 1,
    /// <summary>成年人（>=18岁）。</summary>
    Adult = 2,
}

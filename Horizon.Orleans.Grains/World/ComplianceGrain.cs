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
/// P3.5 合规服务 Grain 实现（全局单例）。<br/>
/// 实名认证、防沉迷限制、未成年人消费限制。
/// </summary>
public sealed class ComplianceGrain : Grain, IComplianceGrain
{
    private readonly ILogger<ComplianceGrain> _logger;

    /// <summary>用户合规记录（characterId → record）。</summary>
    private readonly Dictionary<long, UserComplianceRecord> _records = new();

    // 防沉迷限制配置（符合国内规定）
    private const int MinorDailyLimitMinutes = 90; // 未成年人每日限 1.5 小时
    private const int MinorHolidayLimitMinutes = 180; // 节假日限 3 小时
    private const long MinorSinglePurchaseLimit = 5000; // 单次消费限额（分）= 50 元
    private const long MinorMonthlyPurchaseLimit = 20000; // 月消费限额（分）= 200 元

    // 统计
    private int _totalVerified;
    private int _minorUsers;
    private int _adultUsers;
    private long _totalPurchaseBlocked;

    public ComplianceGrain(ILogger<ComplianceGrain> logger)
    {
        _logger = logger;
    }

    public Task<RealNameVerifyResult> VerifyRealNameAsync(long characterId, RealNameInfo info)
    {
        // 简单校验（生产环境应调用公安实名认证 API）
        if (string.IsNullOrWhiteSpace(info.RealName) || string.IsNullOrWhiteSpace(info.IdCardNumber))
        {
            return Task.FromResult(new RealNameVerifyResult
            {
                Success = false,
                ErrorMessage = "实名信息不完整。",
            });
        }

        if (info.IdCardNumber.Length != 18)
        {
            return Task.FromResult(new RealNameVerifyResult
            {
                Success = false,
                ErrorMessage = "身份证号格式错误。",
            });
        }

        // 从身份证号解析出生日期（简化版）
        var ageGroup = ParseAgeGroupFromIdCard(info.IdCardNumber);

        var record = GetOrCreateRecord(characterId);
        record.RealName = info.RealName;
        record.IdCardNumber = MaskIdCard(info.IdCardNumber);
        record.AgeGroup = ageGroup;
        record.IsVerified = true;
        record.VerifyTime = DateTime.UtcNow;

        _totalVerified++;
        if (ageGroup == AgeGroup.Minor)
            _minorUsers++;
        else
            _adultUsers++;

        _logger.LogInformation(
            "实名认证通过。CharacterId={CharacterId}, AgeGroup={AgeGroup}",
            characterId, ageGroup);

        return Task.FromResult(new RealNameVerifyResult
        {
            Success = true,
            AgeGroup = ageGroup,
            IsVerified = true,
        });
    }

    public Task<AntiAddictionStatus> GetAntiAddictionStatusAsync(long characterId)
    {
        var record = GetOrCreateRecord(characterId);
        var isHoliday = IsHoliday(DateTime.UtcNow);
        var limit = record.AgeGroup == AgeGroup.Minor
            ? (isHoliday ? MinorHolidayLimitMinutes : MinorDailyLimitMinutes)
            : int.MaxValue;

        var remaining = Math.Max(0, limit - record.TodayOnlineMinutes);
        var isRestricted = record.AgeGroup == AgeGroup.Minor && remaining <= 0;

        // 未成年人宵禁（22:00-08:00 禁止登录）
        if (record.AgeGroup == AgeGroup.Minor && IsCurfewTime(DateTime.UtcNow))
        {
            isRestricted = true;
        }

        return Task.FromResult(new AntiAddictionStatus
        {
            CharacterId = characterId,
            AgeGroup = record.AgeGroup,
            TodayOnlineMinutes = record.TodayOnlineMinutes,
            RemainingMinutes = remaining,
            IsRestricted = isRestricted,
            RestrictionReason = isRestricted ? GetRestrictionReason(record, remaining) : string.Empty,
            TodaySpentAmount = record.TodaySpentAmount,
            MonthlySpentAmount = record.MonthlySpentAmount,
        });
    }

    public Task<LoginCheckResult> CheckLoginAllowedAsync(long characterId)
    {
        var record = GetOrCreateRecord(characterId);

        // 未实名认证：允许登录但限制游戏时间（1 小时）
        if (!record.IsVerified)
        {
            if (record.TodayOnlineMinutes >= 60)
            {
                return Task.FromResult(new LoginCheckResult
                {
                    Allowed = false,
                    Reason = "未实名认证，每日限玩 1 小时。请完成实名认证。",
                });
            }
            return Task.FromResult(new LoginCheckResult { Allowed = true });
        }

        // 成年人：无限制
        if (record.AgeGroup == AgeGroup.Adult)
            return Task.FromResult(new LoginCheckResult { Allowed = true });

        // 未成年人：宵禁检查
        if (IsCurfewTime(DateTime.UtcNow))
        {
            return Task.FromResult(new LoginCheckResult
            {
                Allowed = false,
                Reason = "未成年人宵禁时间（22:00-08:00）禁止登录。",
                AllowedAfter = GetNextAllowedTime(),
            });
        }

        // 未成年人：时长检查
        var isHoliday = IsHoliday(DateTime.UtcNow);
        var limit = isHoliday ? MinorHolidayLimitMinutes : MinorDailyLimitMinutes;
        if (record.TodayOnlineMinutes >= limit)
        {
            return Task.FromResult(new LoginCheckResult
            {
                Allowed = false,
                Reason = $"未成年人每日游戏时长已达上限（{limit} 分钟）。",
                AllowedAfter = DateTime.UtcNow.Date.AddDays(1),
            });
        }

        return Task.FromResult(new LoginCheckResult { Allowed = true });
    }

    public Task RecordOnlineTimeAsync(long characterId, int minutes)
    {
        var record = GetOrCreateRecord(characterId);
        record.TodayOnlineMinutes += minutes;
        record.LastOnlineTime = DateTime.UtcNow;

        // 每日重置检查
        if (record.LastResetDate < DateTime.UtcNow.Date)
        {
            record.TodayOnlineMinutes = minutes;
            record.TodaySpentAmount = 0;
            record.LastResetDate = DateTime.UtcNow.Date;
        }

        return Task.CompletedTask;
    }

    public Task<PurchaseCheckResult> CheckPurchaseAllowedAsync(long characterId, long amount)
    {
        var record = GetOrCreateRecord(characterId);

        // 成年人：无限制
        if (record.AgeGroup == AgeGroup.Adult || !record.IsVerified)
        {
            return Task.FromResult(new PurchaseCheckResult { Allowed = true, RemainingQuota = long.MaxValue });
        }

        // 未成年人：单次限额
        if (amount > MinorSinglePurchaseLimit)
        {
            _totalPurchaseBlocked++;
            return Task.FromResult(new PurchaseCheckResult
            {
                Allowed = false,
                Reason = $"未成年人单次消费不能超过 {MinorSinglePurchaseLimit / 100} 元。",
                RemainingQuota = MinorSinglePurchaseLimit,
            });
        }

        // 未成年人：月限额
        var remaining = MinorMonthlyPurchaseLimit - record.MonthlySpentAmount;
        if (amount > remaining)
        {
            _totalPurchaseBlocked++;
            return Task.FromResult(new PurchaseCheckResult
            {
                Allowed = false,
                Reason = $"未成年人月消费已达上限（{MinorMonthlyPurchaseLimit / 100} 元）。",
                RemainingQuota = Math.Max(0, remaining),
            });
        }

        record.TodaySpentAmount += amount;
        record.MonthlySpentAmount += amount;

        return Task.FromResult(new PurchaseCheckResult
        {
            Allowed = true,
            RemainingQuota = remaining - amount,
        });
    }

    public Task<ComplianceStats> GetStatsAsync()
    {
        return Task.FromResult(new ComplianceStats
        {
            TotalVerifiedUsers = _totalVerified,
            MinorUsers = _minorUsers,
            AdultUsers = _adultUsers,
            CurrentlyRestricted = _records.Count(r => r.Value.AgeGroup == AgeGroup.Minor && r.Value.TodayOnlineMinutes >= MinorDailyLimitMinutes),
            TotalPurchaseBlocked = _totalPurchaseBlocked,
        });
    }

    // --- 辅助方法 ---

    private UserComplianceRecord GetOrCreateRecord(long characterId)
    {
        if (!_records.TryGetValue(characterId, out var record))
        {
            record = new UserComplianceRecord { CharacterId = characterId };
            _records[characterId] = record;
        }
        return record;
    }

    private static AgeGroup ParseAgeGroupFromIdCard(string idCard)
    {
        // 身份证号第 7-14 位为出生日期（YYYYMMDD）
        if (idCard.Length >= 14 && int.TryParse(idCard.Substring(6, 8), out var birthDate))
        {
            var year = birthDate / 10000;
            var age = DateTime.UtcNow.Year - year;
            return age < 18 ? AgeGroup.Minor : AgeGroup.Adult;
        }
        return AgeGroup.Unverified;
    }

    private static string MaskIdCard(string idCard)
    {
        if (idCard.Length < 18) return idCard;
        return idCard.Substring(0, 6) + "********" + idCard.Substring(14);
    }

    private static bool IsCurfewTime(DateTime time)
    {
        var hour = time.Hour;
        return hour >= 22 || hour < 8;
    }

    private static bool IsHoliday(DateTime date)
    {
        // 简化版：周末视为节假日
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }

    private static DateTime GetNextAllowedTime()
    {
        var now = DateTime.UtcNow;
        if (now.Hour >= 22)
            return now.Date.AddDays(1).AddHours(8);
        return now.Date.AddHours(8);
    }

    private static string GetRestrictionReason(UserComplianceRecord record, int remaining)
    {
        if (IsCurfewTime(DateTime.UtcNow))
            return "未成年人宵禁时间（22:00-08:00）。";
        if (remaining <= 0)
            return "今日游戏时长已达上限。";
        return string.Empty;
    }

    /// <summary>用户合规记录。</summary>
    private sealed class UserComplianceRecord
    {
        public long CharacterId { get; init; }
        public string RealName { get; set; } = string.Empty;
        public string IdCardNumber { get; set; } = string.Empty;
        public AgeGroup AgeGroup { get; set; } = AgeGroup.Unverified;
        public bool IsVerified { get; set; }
        public DateTime VerifyTime { get; set; }
        public int TodayOnlineMinutes { get; set; }
        public long TodaySpentAmount { get; set; }
        public long MonthlySpentAmount { get; set; }
        public DateTime LastOnlineTime { get; set; }
        public DateTime LastResetDate { get; set; } = DateTime.UtcNow.Date;
    }
}

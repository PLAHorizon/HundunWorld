using Horizon.Core.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerAICostControlService
    {
        private readonly ILogger<FlowerAICostControlService> _logger;
        private readonly ICache _cache;
        private readonly ConcurrentDictionary<Guid, UserRateLimit> _rateLimits = new();
        private readonly HashSet<string> _sensitiveWords;
        private const int DailyCallLimit = 50;
        private const int CacheTtlMinutes = 60;

        private static readonly string[] DefaultSensitiveWords = {
            "暴力", "色情", "赌博", "毒品", "枪支", "爆炸", "自杀", "杀人"
        };

        public FlowerAICostControlService(ILogger<FlowerAICostControlService> logger, ICache cache)
        {
            _logger = logger;
            _cache = cache;
            _sensitiveWords = new HashSet<string>(DefaultSensitiveWords, StringComparer.OrdinalIgnoreCase);
        }

        public bool CheckRateLimit(Guid userId)
        {
            var today = DateTime.Now.Date;
            var limit = _rateLimits.GetOrAdd(userId, _ => new UserRateLimit());

            if (limit.ResetDate != today)
            {
                limit.ResetDate = today;
                limit.CallCount = 0;
            }

            if (limit.CallCount >= DailyCallLimit)
            {
                _logger.LogWarning("用户 {UserId} 达到每日调用上限: {Count}/{Limit}", userId, limit.CallCount, DailyCallLimit);
                return false;
            }

            limit.CallCount++;
            return true;
        }

        public int GetRemainingCalls(Guid userId)
        {
            if (_rateLimits.TryGetValue(userId, out var limit) && limit.ResetDate == DateTime.Now.Date)
                return Math.Max(0, DailyCallLimit - limit.CallCount);
            return DailyCallLimit;
        }

        public bool ContainsSensitiveContent(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var word in _sensitiveWords)
            {
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("检测到敏感词: {Word}", word);
                    return true;
                }
            }
            return false;
        }

        public string FilterSensitiveContent(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var filtered = text;
            foreach (var word in _sensitiveWords)
            {
                if (filtered.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    filtered = filtered.Replace(word, new string('*', word.Length), StringComparison.OrdinalIgnoreCase);
                }
            }
            return filtered;
        }

        public async Task<string?> GetCachedAnswerAsync(string question)
        {
            try
            {
                var cacheKey = $"FLOWER_AI_QA_CACHE_{ComputeHash(question)}";
                return await _cache.GetAsync<string>(cacheKey).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async Task CacheAnswerAsync(string question, string answer)
        {
            try
            {
                var cacheKey = $"FLOWER_AI_QA_CACHE_{ComputeHash(question)}";
                await _cache.InsertAsync(cacheKey, answer, CacheTtlMinutes * 60).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "缓存AI问答失败");
            }
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes)[..16];
        }

        private class UserRateLimit
        {
            public DateTime ResetDate { get; set; }
            public int CallCount { get; set; }
        }
    }
}

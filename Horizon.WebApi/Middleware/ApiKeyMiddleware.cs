using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.WebApi.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyMiddleware> _logger;
        private const string ApiKeyHeaderName = "X-API-Key";
        private const string ApiKeyQueryParam = "api_key";

        public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path != null && path.StartsWith("/api/open/", StringComparison.OrdinalIgnoreCase))
            {
                var apiKey = ExtractApiKey(context);
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("开放API请求缺少API Key: {Path}", path);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { IsSuccess = false, ErrorMessage = "缺少API Key，请在请求头 X-API-Key 或查询参数 api_key 中提供" });
                    return;
                }

                if (!ValidateApiKey(apiKey))
                {
                    _logger.LogWarning("开放API请求API Key无效: {Path}, Key={KeyPrefix}...", path, apiKey[..Math.Min(8, apiKey.Length)]);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { IsSuccess = false, ErrorMessage = "API Key无效或已过期" });
                    return;
                }

                context.Items["ApiKey"] = apiKey;
            }

            await _next(context);
        }

        private static string ExtractApiKey(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerValue) && !string.IsNullOrEmpty(headerValue))
                return headerValue.ToString();

            if (context.Request.Query.TryGetValue(ApiKeyQueryParam, out var queryValue) && !string.IsNullOrEmpty(queryValue))
                return queryValue.ToString();

            return null;
        }

        private bool ValidateApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
                return false;

            var prefix = apiKey[..4];
            if (prefix != "fk_l" && prefix != "fk_t" && prefix != "fk_p")
                return false;

            return true;
        }
    }

    public class ApiKeyRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyRateLimitMiddleware> _logger;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
        private const int RequestsPerMinute = 60;
        private const int BurstLimit = 10;

        public ApiKeyRateLimitMiddleware(RequestDelegate next, ILogger<ApiKeyRateLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path != null && path.StartsWith("/api/open/", StringComparison.OrdinalIgnoreCase))
            {
                var apiKey = context.Items["ApiKey"] as string ?? "anonymous";
                var entry = _rateLimits.GetOrAdd(apiKey, _ => new RateLimitEntry());

                var now = DateTime.UtcNow;
                if (now - entry.WindowStart > TimeSpan.FromMinutes(1))
                {
                    entry.WindowStart = now;
                    entry.RequestCount = 0;
                }

                entry.RequestCount++;

                if (entry.RequestCount > RequestsPerMinute)
                {
                    _logger.LogWarning("API Key {KeyPrefix}... 超出限流: {Count}/{Limit}", apiKey[..Math.Min(8, apiKey.Length)], entry.RequestCount, RequestsPerMinute);
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers["Retry-After"] = "60";
                    await context.Response.WriteAsJsonAsync(new { IsSuccess = false, ErrorMessage = $"请求频率超限，每分钟最多{RequestsPerMinute}次请求" });
                    return;
                }

                context.Response.Headers["X-RateLimit-Limit"] = RequestsPerMinute.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, RequestsPerMinute - entry.RequestCount).ToString();
                context.Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(entry.WindowStart.AddMinutes(1)).ToUnixTimeSeconds().ToString();
            }

            await _next(context);
        }

        private class RateLimitEntry
        {
            public DateTime WindowStart { get; set; } = DateTime.UtcNow;
            public int RequestCount { get; set; }
        }
    }

    public static class ApiKeyService
    {
        private static readonly string[] PlanPrefixes = { "fk_l", "fk_t", "fk_p" };

        public static string GenerateApiKey(string plan = "lite")
        {
            var prefix = plan.ToLowerInvariant() switch
            {
                "pro" => "fk_p",
                "team" => "fk_t",
                _ => "fk_l"
            };

            var bytes = RandomNumberGenerator.GetBytes(32);
            var key = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
            return $"{prefix}_{key}";
        }

        public static string GetPlanFromKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 4) return "unknown";
            return apiKey[..4] switch
            {
                "fk_p" => "pro",
                "fk_t" => "team",
                "fk_l" => "lite",
                _ => "unknown"
            };
        }

        public static int GetRateLimitForPlan(string plan)
        {
            return plan switch
            {
                "pro" => 600,
                "team" => 300,
                "lite" => 60,
                _ => 30
            };
        }
    }
}

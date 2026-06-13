using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.WebApi.Middleware
{
    public sealed class OrleansStartupConnectionRetryFilter : IClientConnectionRetryFilter
    {
        private readonly ILogger<OrleansStartupConnectionRetryFilter> _logger;
        private int _attempts;
        private const int MaxRetryCount = 0;
        private const int RetryIntervalMs = 3000;

        public OrleansStartupConnectionRetryFilter(
            ILogger<OrleansStartupConnectionRetryFilter> logger)
        {
            _logger = logger;
        }

        public async Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);

            if (MaxRetryCount > 0 && attempt > MaxRetryCount)
            {
                _logger.LogError(exception,
                    "Orleans 客户端启动失败，已超过最大重试次数 {RetryCount} 次。",
                    MaxRetryCount);
                return false;
            }

            var maxRetryText = MaxRetryCount > 0 ? MaxRetryCount.ToString() : "无限";

            _logger.LogWarning(exception,
                "Orleans 集群暂不可用，第 {Attempt}/{MaxRetry} 次重试将在 {DelayMs}ms 后进行。",
                attempt,
                maxRetryText,
                RetryIntervalMs);

            try
            {
                await Task.Delay(RetryIntervalMs, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// Orleans 客户端启动期连接重试过滤器。
    /// 当 Silo 或其 Gateway 端口尚未就绪时，避免宿主进程直接退出。
    /// </summary>
    public sealed class OrleansStartupConnectionRetryFilter : IClientConnectionRetryFilter
    {
        private readonly ILogger<OrleansStartupConnectionRetryFilter> _logger;
        private readonly IOptionsMonitor<OrleansOptions> _optionsMonitor;
        private int _attempts;

        public OrleansStartupConnectionRetryFilter(
            ILogger<OrleansStartupConnectionRetryFilter> logger,
            IOptionsMonitor<OrleansOptions> optionsMonitor)
        {
            _logger = logger;
            _optionsMonitor = optionsMonitor;
        }

        public async Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken)
        {
            var options = _optionsMonitor.CurrentValue;
            var attempt = Interlocked.Increment(ref _attempts);
            var retryCount = options.RetryCount;

            if (retryCount > 0 && attempt > retryCount)
            {
                _logger.LogError(
                    exception,
                    "Orleans 客户端启动失败，已超过最大重试次数 {RetryCount} 次。",
                    retryCount);
                return false;
            }

            var delayMs = Math.Max(500, options.RetryInterval);
            var maxRetryText = retryCount > 0 ? retryCount.ToString() : "无限";

            _logger.LogWarning(
                exception,
                "Orleans 集群暂不可用，第 {Attempt}/{MaxRetry} 次重试将在 {DelayMs}ms 后进行。",
                attempt,
                maxRetryText,
                delayMs);

            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
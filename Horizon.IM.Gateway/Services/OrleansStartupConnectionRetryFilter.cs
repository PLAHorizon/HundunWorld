using Horizon.IM.Gateway.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Orleans;

using System.Threading;

namespace Horizon.IM.Gateway.Services;

/// <summary>
/// Orleans 客户端启动期连接重试过滤器，避免 Silo Gateway 尚未就绪时 IM 网关直接退出。
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
            _logger.LogError(exception, "IM Gateway 连接 Orleans 超过最大重试次数 {RetryCount} 次。", retryCount);
            return false;
        }

        var delayMs = Math.Max(500, options.RetryIntervalMilliseconds);
        var maxRetryText = retryCount > 0 ? retryCount.ToString() : "无限";

        _logger.LogWarning(
            exception,
            "IM Gateway 所需 Orleans 集群暂不可用，第 {Attempt}/{MaxRetry} 次重试将在 {DelayMs}ms 后进行。",
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
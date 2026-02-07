using Microsoft.Extensions.Logging;
using Horizon.Orleans.Silo.Configuration;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;

namespace Horizon.Orleans.Silo.Diagnostics;

/// <summary>
/// Horizon网关诊断工具，用于检测Orleans Silo和Gateway的连接状态和性能
/// 提供全面的网络诊断功能，包括端口可达性检测、连接时间测量和性能基准测试
/// </summary>
public class HorizonGatewayDiagnostic
{
    private readonly ILogger<HorizonGatewayDiagnostic> _logger;
    private readonly HorizonTimeoutConfiguration _timeoutConfig;

    public HorizonGatewayDiagnostic(
        ILogger<HorizonGatewayDiagnostic> logger,
        HorizonTimeoutConfiguration timeoutConfig)
    {
        _logger = logger;
        _timeoutConfig = timeoutConfig;
    }

    /// <summary>
    /// 运行完整的网关诊断检查
    /// </summary>
    public async Task<GatewayDiagnosticResult> RunCompleteDiagnosticAsync(
        string siloHost = "localhost",
        int siloPort = 11111,
        int gatewayPort = 30000)
    {
        var result = new GatewayDiagnosticResult();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("开始运行 Horizon Orleans 网关诊断检查...");
        _logger.LogInformation("目标配置 - Silo: {Host}:{SiloPort}, Gateway: {Host}:{GatewayPort}",
            siloHost, siloPort, siloHost, gatewayPort);

        try
        {
            // 1. 检查基础网络连通性（Ping测试）
            result.NetworkConnectivity = await CheckNetworkConnectivityAsync(siloHost);

            // 2. 检查端口可达性
            result.SiloPortReachable = await CheckPortReachabilityAsync(siloHost, siloPort);
            result.GatewayPortReachable = await CheckPortReachabilityAsync(siloHost, gatewayPort);

            // 3. 测量TCP连接时间
            result.SiloConnectionTime = await MeasureConnectionTimeAsync(siloHost, siloPort);
            result.GatewayConnectionTime = await MeasureConnectionTimeAsync(siloHost, gatewayPort);

            // 4. 测量DNS解析时间
            result.DnsResolutionTime = await MeasureDnsResolutionTimeAsync(siloHost);

            // 5. 验证超时配置
            result.TimeoutConfigurationWarnings = _timeoutConfig.ValidateConfiguration();

            // 6. 运行性能基准测试
            result.PerformanceMetrics = await RunPerformanceBenchmarkAsync(siloHost, gatewayPort);

            result.IsHealthy = DetermineOverallHealth(result);
            result.TotalDiagnosticTime = stopwatch.Elapsed;

            LogDiagnosticResults(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "诊断过程中发生错误");
            result.DiagnosticError = ex.Message;
            result.IsHealthy = false;
            result.TotalDiagnosticTime = stopwatch.Elapsed;
            return result;
        }
    }

    /// <summary>
    /// 检查网络连通性（使用Ping测试）
    /// </summary>
    private async Task<bool> CheckNetworkConnectivityAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, (int)_timeoutConfig.ConnectionTimeout.TotalMilliseconds);

            var isReachable = reply.Status == IPStatus.Success;
            _logger.LogInformation("网络连通性检查 {Host}: {Status} (往返时间: {RoundTripTime}ms)",
                host, isReachable ? "成功" : "失败", reply.RoundtripTime);

            return isReachable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("网络连通性检查失败 {Host}: {Error}", host, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 检查指定端口的可达性（TCP连接测试）
    /// </summary>
    private async Task<bool> CheckPortReachabilityAsync(string host, int port)
    {
        try
        {
            using var tcpClient = new TcpClient();

            // 设置较短的超时时间以快速检测端口状态
            var timeoutMs = Math.Min(3000, (int)_timeoutConfig.GatewayConnectionTimeout.TotalMilliseconds);
            var connectTask = tcpClient.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutMs);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == connectTask && !connectTask.IsFaulted && tcpClient.Connected)
            {
                _logger.LogInformation("端口可达性检查 {Host}:{Port}: 成功", host, port);
                return true;
            }
            else
            {
                _logger.LogInformation("端口可达性检查 {Host}:{Port}: 失败（连接超时或被拒绝）", host, port);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("端口检查 {Host}:{Port} 发生异常: {Error} （这可能表示端口不可达）", host, port, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 测量TCP连接时间
    /// </summary>
    private async Task<TimeSpan> MeasureConnectionTimeAsync(string host, int port)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var tcpClient = new TcpClient();

            // 设置较短的超时时间以快速检测连接性能
            var timeoutMs = Math.Min(2000, (int)_timeoutConfig.GatewayConnectionTimeout.TotalMilliseconds);
            var connectTask = tcpClient.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutMs);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            stopwatch.Stop();

            if (completedTask == connectTask && !connectTask.IsFaulted && tcpClient.Connected)
            {
                var connectionTime = stopwatch.Elapsed;
                _logger.LogInformation("TCP连接时间测量 {Host}:{Port}: {Time}ms",
                    host, port, connectionTime.TotalMilliseconds);
                return connectionTime;
            }
            else
            {
                _logger.LogInformation("TCP连接时间测量 {Host}:{Port}: 连接失败（超时）", host, port);
                return TimeSpan.FromMilliseconds(-1); // 表示连接失败
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogInformation("TCP连接时间测量异常 {Host}:{Port}: {Error} （连接失败）", host, port, ex.Message);
            return TimeSpan.FromMilliseconds(-1); // 表示连接失败
        }
    }

    /// <summary>
    /// 测量DNS解析时间
    /// </summary>
    private async Task<TimeSpan> MeasureDnsResolutionTimeAsync(string host)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Dns.GetHostAddressesAsync(host);
            stopwatch.Stop();

            var resolutionTime = stopwatch.Elapsed;
            _logger.LogInformation("DNS解析时间 {Host}: {Time}ms", host, resolutionTime.TotalMilliseconds);

            return resolutionTime;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("DNS解析失败 {Host}: {Error}", host, ex.Message);
            return TimeSpan.FromMilliseconds(-1);
        }
    }

    /// <summary>
    /// 运行性能基准测试
    /// </summary>
    private async Task<PerformanceMetrics> RunPerformanceBenchmarkAsync(string host, int port)
    {
        var metrics = new PerformanceMetrics();
        var connectionTimes = new List<TimeSpan>();

        _logger.LogInformation("开始性能基准测试 (连续5次连接测试)...");

        // 首先确保至少能建立一次连接
        var firstConnectionTime = await MeasureConnectionTimeAsync(host, port);
        if (firstConnectionTime.TotalMilliseconds <= 0)
        {
            _logger.LogInformation("性能测试跳过：无法建立基础连接");
            metrics.TotalConnections = 1;
            metrics.SuccessfulConnections = 0;
            metrics.ConnectionSuccessRate = 0.0;
            return metrics;
        }

        connectionTimes.Add(firstConnectionTime);

        // 进行额外的4次连接测试
        for (int i = 1; i < 5; i++)
        {
            var connectionTime = await MeasureConnectionTimeAsync(host, port);
            if (connectionTime.TotalMilliseconds > 0)
            {
                connectionTimes.Add(connectionTime);
            }

            await Task.Delay(50); // 等待50ms
        }

        if (connectionTimes.Any())
        {
            metrics.AverageConnectionTime = TimeSpan.FromMilliseconds(
                connectionTimes.Average(t => t.TotalMilliseconds));
            metrics.MinConnectionTime = connectionTimes.Min();
            metrics.MaxConnectionTime = connectionTimes.Max();
            metrics.SuccessfulConnections = connectionTimes.Count;
        }

        metrics.TotalConnections = 5;
        metrics.ConnectionSuccessRate = (double)metrics.SuccessfulConnections / metrics.TotalConnections * 100;

        _logger.LogInformation("性能基准测试完成");
        _logger.LogInformation("   平均连接时间: {AvgTime}ms", metrics.AverageConnectionTime.TotalMilliseconds);
        _logger.LogInformation("   连接成功率: {Rate}%", metrics.ConnectionSuccessRate);

        return metrics;
    }

    /// <summary>
    /// 判断整体健康状态
    /// </summary>
    private bool DetermineOverallHealth(GatewayDiagnosticResult result)
    {
        var healthChecks = new[]
        {
            result.NetworkConnectivity,
            result.SiloPortReachable,
            // Gateway端口可达性检查是可选的，因为在某些环境中可能无法直接访问
            // result.GatewayPortReachable,
            result.SiloConnectionTime.TotalMilliseconds > 0 &&
                result.SiloConnectionTime < _timeoutConfig.GatewayConnectionTimeout,
            // Gateway连接时间检查是可选的，主要关注Silo连接
            // result.GatewayConnectionTime.TotalMilliseconds > 0 && 
            //     result.GatewayConnectionTime < _timeoutConfig.GatewayConnectionTimeout,
            result.DnsResolutionTime.TotalMilliseconds > 0 && result.DnsResolutionTime < TimeSpan.FromSeconds(5)
        };

        var healthyCount = healthChecks.Count(check => check);
        var healthPercentage = (double)healthyCount / healthChecks.Length * 100;

        return healthPercentage >= 75; // 75%以上的检查通过则认为整体健康
    }

    /// <summary>
    /// 记录诊断结果
    /// </summary>
    private void LogDiagnosticResults(GatewayDiagnosticResult result)
    {
        _logger.LogInformation("诊断完成 (总耗时: {Time}ms)", result.TotalDiagnosticTime.TotalMilliseconds);
        _logger.LogInformation("诊断结果摘要:");
        _logger.LogInformation("   整体健康状态: {Status}", result.IsHealthy ? "健康" : "不健康");
        _logger.LogInformation("   网络连通性: {Status}", result.NetworkConnectivity ? "正常" : "异常");
        _logger.LogInformation("   Silo端口可达: {Status}", result.SiloPortReachable ? "正常" : "异常");
        _logger.LogInformation("   Gateway端口可达: {Status}", result.GatewayPortReachable ? "正常" : "异常");

        if (result.TimeoutConfigurationWarnings.Any())
        {
            _logger.LogWarning("配置警告:");
            foreach (var warning in result.TimeoutConfigurationWarnings)
            {
                _logger.LogWarning("   - {Warning}", warning);
            }
        }
    }
}

/// <summary>
/// 网关诊断结果
/// </summary>
public class GatewayDiagnosticResult
{
    public bool IsHealthy { get; set; }
    public bool NetworkConnectivity { get; set; }
    public bool SiloPortReachable { get; set; }
    public bool GatewayPortReachable { get; set; }
    public TimeSpan SiloConnectionTime { get; set; }
    public TimeSpan GatewayConnectionTime { get; set; }
    public TimeSpan DnsResolutionTime { get; set; }
    public PerformanceMetrics PerformanceMetrics { get; set; } = new();
    public List<string> TimeoutConfigurationWarnings { get; set; } = new();
    public TimeSpan TotalDiagnosticTime { get; set; }
    public string? DiagnosticError { get; set; }
}

/// <summary>
/// 性能指标
/// </summary>
public class PerformanceMetrics
{
    public TimeSpan AverageConnectionTime { get; set; }
    public TimeSpan MinConnectionTime { get; set; }
    public TimeSpan MaxConnectionTime { get; set; }
    public int SuccessfulConnections { get; set; }
    public int TotalConnections { get; set; }
    public double ConnectionSuccessRate { get; set; }
}
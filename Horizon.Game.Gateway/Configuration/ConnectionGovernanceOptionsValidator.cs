using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Configuration;

/// <summary>
/// 连接治理配置校验器：非法值回退默认并输出配置异常诊断（连接精简治理，spec 4.4.4）。
/// 在启动阶段调用一次，保证运行时治理参数恒合法。
/// </summary>
public static class ConnectionGovernanceOptionsValidator
{
    /// <summary>
    /// 校验并修正配置。
    /// </summary>
    /// <param name="options">待校验配置（可能为 null 或非法值）。</param>
    /// <param name="logger">诊断日志（可为 null）。</param>
    /// <returns>合法配置；非法字段回退默认值（5/30/10000/4/1/15）。</returns>
    /// <remarks>
    /// 回退规则：
    /// <list type="bullet">
    /// <item><c>options</c> 为 null → 返回全新默认配置。</item>
    /// <item><c>FirstPacketTimeoutSeconds &lt;= 0</c> → 回退 5。</item>
    /// <item><c>IdleTimeoutSeconds &lt;= 0</c> → 回退 30。</item>
    /// <item><c>MaxConnections &lt;= 0</c> → 回退 10000。</item>
    /// <item><c>MaxConnectionsPerUser &lt;= 0</c> → 回退 1。</item>
    /// <item><c>MaxConnectionsPerIp &lt;= 0</c> → 回退 4。</item>
    /// <item><c>MaxConnectionsPerIp &lt; MaxConnectionsPerUser</c> → 回退 4/1（保证每 IP ≥ 每用户）。</item>
    /// <item><c>DespawnGracePeriodSeconds &lt;= 0</c> → 回退 15。</item>
    /// </list>
    /// </remarks>
    public static ConnectionGovernanceOptions Validate(ConnectionGovernanceOptions? options, ILogger? logger = null)
    {
        var result = options ?? new ConnectionGovernanceOptions();

        if (result.FirstPacketTimeoutSeconds <= 0)
        {
            Notify(logger, "FirstPacketTimeoutSeconds", result.FirstPacketTimeoutSeconds, 5);
            result.FirstPacketTimeoutSeconds = 5;
        }

        if (result.IdleTimeoutSeconds <= 0)
        {
            Notify(logger, "IdleTimeoutSeconds", result.IdleTimeoutSeconds, 30);
            result.IdleTimeoutSeconds = 30;
        }

        if (result.MaxConnections <= 0)
        {
            Notify(logger, "MaxConnections", result.MaxConnections, 10000);
            result.MaxConnections = 10000;
        }

        if (result.MaxConnectionsPerUser <= 0)
        {
            Notify(logger, "MaxConnectionsPerUser", result.MaxConnectionsPerUser, 1);
            result.MaxConnectionsPerUser = 1;
        }

        if (result.MaxConnectionsPerIp <= 0)
        {
            Notify(logger, "MaxConnectionsPerIp", result.MaxConnectionsPerIp, 4);
            result.MaxConnectionsPerIp = 4;
        }

        // 每 IP ≥ 每用户：非法组合回退默认（spec 4.4.4）。
        if (result.MaxConnectionsPerIp < result.MaxConnectionsPerUser)
        {
            Notify(logger, "MaxConnectionsPerIp/MaxConnectionsPerUser", $"{result.MaxConnectionsPerIp}/{result.MaxConnectionsPerUser}", "4/1");
            result.MaxConnectionsPerIp = 4;
            result.MaxConnectionsPerUser = 1;
        }

        if (result.DespawnGracePeriodSeconds <= 0)
        {
            Notify(logger, "DespawnGracePeriodSeconds", result.DespawnGracePeriodSeconds, 15);
            result.DespawnGracePeriodSeconds = 15;
        }

        return result;
    }

    private static void Notify(ILogger? logger, string fieldName, object configuredValue, object fallbackValue)
    {
        logger?.LogWarning(
            "[ConnectionGovernance] 配置非法回退：Field={Field} Configured={Configured} Fallback={Fallback}",
            fieldName, configuredValue, fallbackValue);
    }
}
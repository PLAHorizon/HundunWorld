using System.ComponentModel.DataAnnotations;

namespace Horizon.Orleans.Silo.Configuration
{
    /// <summary>
    /// Horizon Gateway timeout configuration
    /// </summary>
    public class HorizonTimeoutConfiguration
    {
        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        [Range(1000, 60000)]
        public int ConnectionTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// Gateway timeout in milliseconds
        /// </summary>
        [Range(1000, 300000)]
        public int GatewayTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Keep alive timeout in milliseconds
        /// </summary>
        [Range(1000, 60000)]
        public int KeepAliveTimeoutMs { get; set; } = 15000;

        /// <summary>
        /// Request timeout in milliseconds
        /// </summary>
        [Range(1000, 120000)]
        public int RequestTimeoutMs { get; set; } = 60000;

        /// <summary>
        /// Response timeout in milliseconds
        /// </summary>
        [Range(1000, 120000)]
        public int ResponseTimeoutMs { get; set; } = 60000;

        /// <summary>
        /// Response timeout with debugger in milliseconds
        /// </summary>
        [Range(1000, 600000)]
        public int ResponseTimeoutWithDebuggerMs { get; set; } = 300000;

        /// <summary>
        /// Maximum retry attempts
        /// </summary>
        [Range(0, 10)]
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in milliseconds
        /// </summary>
        [Range(100, 10000)]
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum forward count
        /// </summary>
        [Range(1, 100)]
        public int MaxForwardCount { get; set; } = 10;

        /// <summary>
        /// Gateway list refresh period in milliseconds
        /// </summary>
        [Range(1000, 300000)]
        public int GatewayListRefreshPeriodMs { get; set; } = 60000;

        /// <summary>
        /// Delay warning threshold in milliseconds
        /// </summary>
        [Range(100, 30000)]
        public int DelayWarningThresholdMs { get; set; } = 5000;

        /// <summary>
        /// Cluster membership timeout in milliseconds
        /// </summary>
        [Range(30000, 600000)]
        public int ClusterMembershipTimeoutMs { get; set; } = 120000;

        /// <summary>
        /// Gateway connection timeout in milliseconds
        /// </summary>
        [Range(1000, 60000)]
        public int GatewayConnectionTimeoutMs { get; set; } = 15000;

        /// <summary>
        /// Message processing timeout in milliseconds
        /// </summary>
        [Range(1000, 120000)]
        public int MessageProcessingTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Retry count for operations
        /// </summary>
        [Range(0, 10)]
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// Retry interval in milliseconds
        /// </summary>
        [Range(100, 10000)]
        public int RetryIntervalMs { get; set; } = 1000;

        /// <summary>
        /// Grain deactivation timeout in milliseconds
        /// </summary>
        [Range(1000, 300000)]
        public int GrainDeactivationTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Whether to enable timeout diagnostics
        /// </summary>
        public bool EnableTimeoutDiagnostics { get; set; } = true;

        /// <summary>
        /// Validate configuration settings
        /// </summary>
        public List<string> ValidateConfiguration()
        {
            var warnings = new List<string>();

            if (ConnectionTimeoutMs <= 0)
                warnings.Add("Connection timeout must be greater than 0");

            if (GatewayTimeoutMs <= 0)
                warnings.Add("Gateway timeout must be greater than 0");

            if (RequestTimeoutMs <= 0)
                warnings.Add("Request timeout must be greater than 0");

            if (ResponseTimeoutMs <= 0)
                warnings.Add("Response timeout must be greater than 0");

            if (MaxRetryAttempts < 0)
                warnings.Add("Max retry attempts cannot be negative");

            if (RetryDelayMs <= 0)
                warnings.Add("Retry delay must be greater than 0");

            if (MaxForwardCount <= 0)
                warnings.Add("Max forward count must be greater than 0");

            return warnings;
        }

        /// <summary>
        /// Validate configuration settings (alternative method)
        /// </summary>
        public bool IsValid()
        {
            return ValidateConfiguration().Count == 0;
        }

        /// <summary>
        /// Get gateway timeouts as dictionary
        /// </summary>
        public Dictionary<string, TimeSpan> GetGatewayTimeouts()
        {
            return new Dictionary<string, TimeSpan>
            {
                { "Connection", ConnectionTimeout },
                { "Gateway", GatewayTimeout },
                { "KeepAlive", KeepAliveTimeout },
                { "Request", RequestTimeout },
                { "Response", ResponseTimeout },
                { "GatewayConnection", GatewayConnectionTimeout },
                { "MessageProcessing", MessageProcessingTimeout },
                { "GrainDeactivation", GrainDeactivationTimeout }
            };
        }

        /// <summary>
        /// Get connection timeout as TimeSpan
        /// </summary>
        public TimeSpan ConnectionTimeout => TimeSpan.FromMilliseconds(ConnectionTimeoutMs);

        /// <summary>
        /// Get gateway timeout as TimeSpan
        /// </summary>
        public TimeSpan GatewayTimeout => TimeSpan.FromMilliseconds(GatewayTimeoutMs);

        /// <summary>
        /// Get keep alive timeout as TimeSpan
        /// </summary>
        public TimeSpan KeepAliveTimeout => TimeSpan.FromMilliseconds(KeepAliveTimeoutMs);

        /// <summary>
        /// Get request timeout as TimeSpan
        /// </summary>
        public TimeSpan RequestTimeout => TimeSpan.FromMilliseconds(RequestTimeoutMs);

        /// <summary>
        /// Get response timeout as TimeSpan
        /// </summary>
        public TimeSpan ResponseTimeout => TimeSpan.FromMilliseconds(ResponseTimeoutMs);

        /// <summary>
        /// Get response timeout with debugger as TimeSpan
        /// </summary>
        public TimeSpan ResponseTimeoutWithDebugger => TimeSpan.FromMilliseconds(ResponseTimeoutWithDebuggerMs);

        /// <summary>
        /// Get retry delay as TimeSpan
        /// </summary>
        public TimeSpan RetryDelay => TimeSpan.FromMilliseconds(RetryDelayMs);

        /// <summary>
        /// Get gateway list refresh period as TimeSpan
        /// </summary>
        public TimeSpan GatewayListRefreshPeriod => TimeSpan.FromMilliseconds(GatewayListRefreshPeriodMs);

        /// <summary>
        /// Get delay warning threshold as TimeSpan
        /// </summary>
        public TimeSpan DelayWarningThreshold => TimeSpan.FromMilliseconds(DelayWarningThresholdMs);

        /// <summary>
        /// Get cluster membership timeout as TimeSpan
        /// </summary>
        public TimeSpan ClusterMembershipTimeout => TimeSpan.FromMilliseconds(ClusterMembershipTimeoutMs);

        /// <summary>
        /// Get gateway connection timeout as TimeSpan
        /// </summary>
        public TimeSpan GatewayConnectionTimeout => TimeSpan.FromMilliseconds(GatewayConnectionTimeoutMs);

        /// <summary>
        /// Get message processing timeout as TimeSpan
        /// </summary>
        public TimeSpan MessageProcessingTimeout => TimeSpan.FromMilliseconds(MessageProcessingTimeoutMs);

        /// <summary>
        /// Get retry interval as TimeSpan
        /// </summary>
        public TimeSpan RetryInterval => TimeSpan.FromMilliseconds(RetryIntervalMs);

        /// <summary>
        /// Get grain deactivation timeout as TimeSpan
        /// </summary>
        public TimeSpan GrainDeactivationTimeout => TimeSpan.FromMilliseconds(GrainDeactivationTimeoutMs);
    }
}

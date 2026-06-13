namespace Horizon.IM.Gateway.Configuration;

public class OrleansOptions
{
    public string ClusterId { get; set; } = "dev";

    public string ServiceId { get; set; } = "BaseService";

    public int ResponseTimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 0;

    public int RetryIntervalMilliseconds { get; set; } = 1000;
}
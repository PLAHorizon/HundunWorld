namespace Horizon.IM.Gateway.Configuration;

public class NetworkOptions
{
    public string IpAddress { get; set; } = "0.0.0.0";

    public int TcpPort { get; set; } = 31000;

    public bool AllowPortFallback { get; set; } = true;

    public int PortFallbackRange { get; set; } = 20;

    public bool NoDelay { get; set; } = true;
}
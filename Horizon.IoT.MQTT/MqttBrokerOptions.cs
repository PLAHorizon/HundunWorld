namespace Horizon.IoT.MQTT
{
    public class MqttBrokerOptions
    {
        public const string SectionName = "MqttBroker";
        public int TcpPort { get; set; } = 1883;
        public int WebSocketPort { get; set; } = 8083;
        public bool EnableTls { get; set; } = false;
        public string TlsCertificatePath { get; set; } = "";
        public string TlsCertificatePassword { get; set; } = "";
        public int MaxPendingMessagesPerClient { get; set; } = 1000;
        public int MaxMessageSize { get; set; } = 65536;
        public string BridgeClientId { get; set; } = "Horizon-Server-Bridge";
        public string BridgeApiKey { get; set; } = "";
    }
}

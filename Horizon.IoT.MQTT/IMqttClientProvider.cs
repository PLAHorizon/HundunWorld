using MQTTnet.Client;

namespace Horizon.IoT.MQTT
{
    public interface IMqttClientProvider
    {
        Task<IMqttClient> GetClientAsync();
        MqttBrokerOptions Options { get; }
    }
}

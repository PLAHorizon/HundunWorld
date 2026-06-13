using System.Text.Json;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Orleans;

namespace Horizon.IoT.MQTT
{
    public class MqttBridgeHostedService : BackgroundService
    {
        private readonly IMqttClientProvider _clientProvider;
        private readonly IClusterClient _clusterClient;
        private readonly ILogger<MqttBridgeHostedService> _logger;

        public MqttBridgeHostedService(
            IMqttClientProvider clientProvider,
            IClusterClient clusterClient,
            ILogger<MqttBridgeHostedService> logger)
        {
            _clientProvider = clientProvider;
            _clusterClient = clusterClient;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            var client = await _clientProvider.GetClientAsync();

            client.ApplicationMessageReceivedAsync += OnMessageReceived;

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter("flower/iot/+/+/sensor/data", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter("flower/iot/+/+/status/heartbeat", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter("flower/iot/+/+/status/online", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter("flower/iot/+/+/status/offline", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter("flower/iot/+/+/status/config", MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopicFilter("flower/iot/+/+/response/+", MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.SubscribeAsync(subscribeOptions, stoppingToken);
            _logger.LogInformation("MQTT bridge subscribed to IoT topics");
        }

        private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                var topic = args.ApplicationMessage.Topic;
                var payload = args.ApplicationMessage.ConvertPayloadToString();

                var greenhouseId = MqttTopicHelper.ParseGreenhouseId(topic);
                var deviceCode = MqttTopicHelper.ParseDeviceCode(topic);
                var messageType = MqttTopicHelper.ParseMessageType(topic);

                if (string.IsNullOrEmpty(greenhouseId) || string.IsNullOrEmpty(deviceCode))
                {
                    _logger.LogWarning("Invalid MQTT topic format: {Topic}", topic);
                    return;
                }

                switch (messageType)
                {
                    case "sensor":
                        await HandleSensorDataAsync(deviceCode, payload);
                        break;
                    case "status":
                        await HandleStatusAsync(greenhouseId, deviceCode, topic, payload);
                        break;
                    case "response":
                        await HandleResponseAsync(deviceCode, topic, payload);
                        break;
                    default:
                        _logger.LogWarning("Unhandled MQTT message type: {MessageType} on topic {Topic}", messageType, topic);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MQTT message on topic {Topic}", args.ApplicationMessage.Topic);
            }
        }

        private async Task HandleSensorDataAsync(string deviceCode, string payload)
        {
            var reading = JsonSerializer.Deserialize<SensorReading>(payload);
            if (reading == null)
            {
                _logger.LogWarning("Failed to deserialize SensorReading from device {DeviceCode}", deviceCode);
                return;
            }

            reading.DeviceId = deviceCode;
            var grain = _clusterClient.GetGrain<IIoTDeviceGrain>(deviceCode);
            await grain.UpdateReadingAsync(reading);
            _logger.LogDebug("Sensor data updated for device {DeviceCode}", deviceCode);
        }

        private async Task HandleStatusAsync(string greenhouseId, string deviceCode, string topic, string payload)
        {
            var statusType = topic.Split('/').LastOrDefault() ?? "";

            switch (statusType)
            {
                case "heartbeat":
                    var managementGrain = _clusterClient.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                    await managementGrain.UpdateHeartbeatAsync(deviceCode);
                    _logger.LogDebug("Heartbeat updated for device {DeviceCode}", deviceCode);
                    break;

                case "online":
                    var onlineGrain = _clusterClient.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                    await onlineGrain.UpdateOnlineStatusAsync(deviceCode, "Online");
                    _logger.LogInformation("Device {DeviceCode} came online", deviceCode);
                    break;

                case "offline":
                    var offlineGrain = _clusterClient.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                    await offlineGrain.UpdateOnlineStatusAsync(deviceCode, "Offline");
                    _logger.LogInformation("Device {DeviceCode} went offline", deviceCode);
                    break;

                case "config":
                    var deviceGrain = _clusterClient.GetGrain<IIoTDeviceGrain>(deviceCode);
                    await deviceGrain.UpdateReportedPropertyAsync("Config", payload);
                    _logger.LogDebug("Config reported for device {DeviceCode}", deviceCode);
                    break;

                default:
                    _logger.LogWarning("Unhandled status type: {StatusType} for device {DeviceCode}", statusType, deviceCode);
                    break;
            }
        }

        private async Task HandleResponseAsync(string deviceCode, string topic, string payload)
        {
            try
            {
                var response = JsonSerializer.Deserialize<DeviceCommandResponse>(payload);
                if (response == null || string.IsNullOrEmpty(response.CommandId))
                {
                    _logger.LogWarning("Failed to deserialize DeviceCommandResponse from device {DeviceCode}", deviceCode);
                    return;
                }

                var deviceGrain = _clusterClient.GetGrain<IIoTDeviceGrain>(deviceCode);
                await deviceGrain.CompleteCommandAsync(response.CommandId, payload);
                _logger.LogDebug("Command response received for device {DeviceCode} command {CommandId}", deviceCode, response.CommandId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling command response from device {DeviceCode}", deviceCode);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace Horizon.Game.GengDi.Core.Services
{
    public class SensorDataEventArgs : EventArgs
    {
        public SensorReading Reading { get; set; }
        public string GreenhouseId { get; set; }
        public string DeviceCode { get; set; }
    }

    public class CommandResponseEventArgs : EventArgs
    {
        public DeviceCommandResponse Response { get; set; }
        public string GreenhouseId { get; set; }
        public string DeviceCode { get; set; }
    }

    public class FlowerMqttClientService : IAsyncDisposable
    {
        private static readonly Lazy<FlowerMqttClientService> _instance = new(() => new FlowerMqttClientService());

        public static FlowerMqttClientService Instance => _instance.Value;

        private IManagedMqttClient _client;
        private bool _isConnected;
        private string _brokerHost;
        private int _webSocketPort;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public event EventHandler<SensorDataEventArgs> SensorDataReceived;
        public event EventHandler<CommandResponseEventArgs> CommandResponseReceived;
        public event EventHandler<bool> ConnectionStateChanged;

        public bool IsConnected => _isConnected;

        private FlowerMqttClientService()
        {
            var factory = new MqttFactory();
            _client = factory.CreateManagedMqttClient();

            _client.ConnectedAsync += e =>
            {
                _isConnected = true;
                ConnectionStateChanged?.Invoke(this, true);
                return Task.CompletedTask;
            };

            _client.DisconnectedAsync += e =>
            {
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
                return Task.CompletedTask;
            };

            _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
        }

        public async Task ConnectAsync(string brokerHost, int webSocketPort)
        {
            _brokerHost = brokerHost;
            _webSocketPort = webSocketPort;

            var tlsOptions = new MqttClientTlsOptions
            {
                UseTls = false
            };

            var options = new MqttClientOptions
            {
                ClientId = $"GengDi_{Environment.MachineName}_{Guid.NewGuid():N}",
                ProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion.V311,
                KeepAlivePeriod = TimeSpan.FromSeconds(30),
                CleanSession = true,
                ChannelOptions = new MqttClientWebSocketOptions
                {
                    Uri = $"ws://{brokerHost}:{webSocketPort}/mqtt",
                    TlsOptions = tlsOptions
                }
            };

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(options)
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithMaxPendingMessages(100)
                .Build();

            await _client.StartAsync(managedOptions).ConfigureAwait(false);
        }

        public async Task DisconnectAsync()
        {
            await _client.StopAsync().ConfigureAwait(false);
            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, false);
        }

        public async Task SubscribeSensorDataAsync(string greenhouseId)
        {
            var topic = $"flower/iot/{greenhouseId}/+/sensor/data";
            var filter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.SubscribeAsync(new List<MqttTopicFilter> { filter }).ConfigureAwait(false);
        }

        public async Task UnsubscribeSensorDataAsync(string greenhouseId)
        {
            var topic = $"flower/iot/{greenhouseId}/+/sensor/data";
            await _client.UnsubscribeAsync(topic).ConfigureAwait(false);
        }

        public async Task PublishCommandAsync(string greenhouseId, string deviceCode, string action, string payload)
        {
            var topic = $"flower/iot/{greenhouseId}/{deviceCode}/command/{action}";
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _client.EnqueueAsync(message).ConfigureAwait(false);
        }

        public async Task SubscribeCommandResponseAsync(string greenhouseId, string deviceCode)
        {
            var topic = $"flower/iot/{greenhouseId}/{deviceCode}/response/+";
            var filter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.SubscribeAsync(new List<MqttTopicFilter> { filter }).ConfigureAwait(false);
        }

        private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                var segments = topic.Split('/');
                var payload = e.ApplicationMessage.ConvertPayloadToString();

                if (segments.Length >= 5 && segments[0] == "flower" && segments[1] == "iot")
                {
                    var greenhouseId = segments[2];
                    var deviceCode = segments[3];

                    if (segments[4] == "sensor" && segments.Length >= 6 && segments[5] == "data")
                    {
                        var reading = JsonSerializer.Deserialize<SensorReading>(payload, _jsonOptions);
                        if (reading != null)
                        {
                            SensorDataReceived?.Invoke(this, new SensorDataEventArgs
                            {
                                Reading = reading,
                                GreenhouseId = greenhouseId,
                                DeviceCode = deviceCode
                            });
                        }
                    }
                    else if (segments[4] == "response" && segments.Length >= 6)
                    {
                        var response = JsonSerializer.Deserialize<DeviceCommandResponse>(payload, _jsonOptions);
                        if (response != null)
                        {
                            CommandResponseReceived?.Invoke(this, new CommandResponseEventArgs
                            {
                                Response = response,
                                GreenhouseId = greenhouseId,
                                DeviceCode = deviceCode
                            });
                        }
                    }
                }
            }
            catch
            {
            }

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync().ConfigureAwait(false);
            _client?.Dispose();
            _client = null;
        }
    }
}

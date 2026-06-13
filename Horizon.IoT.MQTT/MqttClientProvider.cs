using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace Horizon.IoT.MQTT
{
    public class MqttClientProvider : IMqttClientProvider, IDisposable
    {
        private readonly MqttBrokerOptions _options;
        private readonly ILogger<MqttClientProvider> _logger;
        private readonly MqttFactory _mqttFactory;
        private IMqttClient? _client;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _disposed;

        public MqttBrokerOptions Options => _options;

        public MqttClientProvider(IOptions<MqttBrokerOptions> options, ILogger<MqttClientProvider> logger)
        {
            _options = options.Value;
            _logger = logger;
            _mqttFactory = new MqttFactory();
        }

        public async Task<IMqttClient> GetClientAsync()
        {
            if (_client?.IsConnected == true)
                return _client;

            await _semaphore.WaitAsync();
            try
            {
                if (_client?.IsConnected == true)
                    return _client;

                if (_client == null)
                {
                    _client = _mqttFactory.CreateMqttClient();
                    _client.DisconnectedAsync += OnDisconnected;
                }

                var clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost", _options.TcpPort)
                    .WithClientId(_options.BridgeClientId)
                    .WithCredentials(_options.BridgeClientId, _options.BridgeApiKey)
                    .WithCleanSession(false)
                    .Build();

                await _client.ConnectAsync(clientOptions);
                _logger.LogInformation("MQTT bridge client connected to localhost:{Port}", _options.TcpPort);

                return _client;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task OnDisconnected(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT bridge client disconnected. Attempting reconnect...");

            if (_client == null || _disposed)
                return;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));

                var clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost", _options.TcpPort)
                    .WithClientId(_options.BridgeClientId)
                    .WithCredentials(_options.BridgeClientId, _options.BridgeApiKey)
                    .WithCleanSession(false)
                    .Build();

                await _client.ConnectAsync(clientOptions);
                _logger.LogInformation("MQTT bridge client reconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT bridge client reconnect failed");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _client?.Dispose();
            _semaphore.Dispose();
        }
    }
}

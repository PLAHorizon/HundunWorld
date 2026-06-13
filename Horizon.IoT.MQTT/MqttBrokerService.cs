using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Server;

namespace Horizon.IoT.MQTT
{
    public class MqttBrokerService : IHostedService
    {
        private readonly MqttBrokerOptions _options;
        private readonly MqttConnectionValidator _connectionValidator;
        private readonly MqttTopicAuthorizer _topicAuthorizer;
        private readonly ILogger<MqttBrokerService> _logger;
        private MqttServer? _mqttServer;

        public MqttBrokerService(
            IOptions<MqttBrokerOptions> options,
            MqttConnectionValidator connectionValidator,
            MqttTopicAuthorizer topicAuthorizer,
            ILogger<MqttBrokerService> logger)
        {
            _options = options.Value;
            _connectionValidator = connectionValidator;
            _topicAuthorizer = topicAuthorizer;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var mqttFactory = new MqttFactory();

            var serverOptions = new MqttServerOptionsBuilder()
                .WithDefaultEndpointPort(_options.TcpPort)
                .WithDefaultEndpoint()
                .WithMaxPendingMessagesPerClient(_options.MaxPendingMessagesPerClient);

            if (_options.EnableTls && !string.IsNullOrEmpty(_options.TlsCertificatePath))
            {
                var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    _options.TlsCertificatePath, _options.TlsCertificatePassword);
                serverOptions.WithEncryptionCertificate(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
            }

            _mqttServer = mqttFactory.CreateMqttServer(serverOptions.Build());

            _mqttServer.ValidatingConnectionAsync += _connectionValidator.ValidateAsync;
            _mqttServer.InterceptingPublishAsync += _topicAuthorizer.ValidatePublishAsync;
            _mqttServer.InterceptingSubscriptionAsync += _topicAuthorizer.ValidateSubscriptionAsync;

            _mqttServer.ClientConnectedAsync += e =>
            {
                var deviceCode = e.SessionItems?["DeviceCode"] as string;
                _logger.LogInformation("MQTT client connected: {DeviceCode}", deviceCode);
                return Task.CompletedTask;
            };

            _mqttServer.ClientDisconnectedAsync += e =>
            {
                var deviceCode = e.SessionItems?["DeviceCode"] as string;
                _logger.LogInformation("MQTT client disconnected: {DeviceCode}", deviceCode);
                return Task.CompletedTask;
            };

            await _mqttServer.StartAsync();

            _logger.LogInformation("MQTT broker started on TCP port {TcpPort}", _options.TcpPort);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttServer != null)
            {
                await _mqttServer.StopAsync();
                _logger.LogInformation("MQTT broker stopped");
            }
        }
    }
}

using System.Text;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.Flower;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Horizon.IoT.MQTT
{
    public class MqttConnectionValidator
    {
        private readonly IDataContext<FlowerEntityContext, FlowerIoTDevice, long> _deviceContext;
        private readonly MqttBrokerOptions _brokerOptions;
        private readonly ILogger<MqttConnectionValidator> _logger;

        public MqttConnectionValidator(
            IDataContext<FlowerEntityContext, FlowerIoTDevice, long> deviceContext,
            IOptions<MqttBrokerOptions> brokerOptions,
            ILogger<MqttConnectionValidator> logger)
        {
            _deviceContext = deviceContext;
            _brokerOptions = brokerOptions.Value;
            _logger = logger;
        }

        public async Task ValidateAsync(ValidatingConnectionEventArgs args)
        {
            var deviceCode = args.ClientId;
            var apiKey = args.Password;

            if (deviceCode == _brokerOptions.BridgeClientId)
            {
                if (!string.IsNullOrEmpty(_brokerOptions.BridgeApiKey) && apiKey == _brokerOptions.BridgeApiKey)
                {
                    args.SessionItems["DeviceCode"] = _brokerOptions.BridgeClientId;
                    args.SessionItems["GreenhouseId"] = "bridge";
                    args.ReasonCode = MqttConnectReasonCode.Success;
                    _logger.LogInformation("MQTT bridge client authenticated: {BridgeClientId}", _brokerOptions.BridgeClientId);
                }
                else
                {
                    args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    _logger.LogWarning("MQTT bridge connection rejected: invalid or missing BridgeApiKey");
                }
                return;
            }

            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(apiKey))
            {
                args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                _logger.LogWarning("MQTT connection rejected: missing DeviceCode or ApiKey");
                return;
            }

            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);

                if (device == null)
                {
                    args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    _logger.LogWarning("MQTT connection rejected: device not found - {DeviceCode}", deviceCode);
                    return;
                }

                if (!device.IsEnabled)
                {
                    args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    _logger.LogWarning("MQTT connection rejected: device disabled - {DeviceCode}", deviceCode);
                    return;
                }

                if (device.ApiKey != apiKey)
                {
                    args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    _logger.LogWarning("MQTT connection rejected: invalid ApiKey - {DeviceCode}", deviceCode);
                    return;
                }

                args.SessionItems["DeviceCode"] = device.DeviceCode;
                args.SessionItems["GreenhouseId"] = device.GreenhouseId;
                args.ReasonCode = MqttConnectReasonCode.Success;

                _logger.LogInformation("MQTT device authenticated: {DeviceCode} (Greenhouse: {GreenhouseId})", device.DeviceCode, device.GreenhouseId);
            }
            catch (Exception ex)
            {
                args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                _logger.LogError(ex, "MQTT connection validation error for device {DeviceCode}", deviceCode);
            }
        }
    }
}

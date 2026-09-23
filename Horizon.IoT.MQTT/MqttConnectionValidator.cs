using System.Text;
using System.Security.Cryptography;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.Flower;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Horizon.IoT.MQTT
{
    /// <summary>
    /// MQTT 连接校验器（设备码 + ApiKey 恒定时间比对）。
    /// <para>
    /// <b>为什么注入 <see cref="IServiceScopeFactory"/> 而不是 <c>IDataContext</c> 本身</b>：
    /// 本类被注册为**单例**（由 <c>MqttBrokerService</c> 宿主服务持有），而 <c>IDataContext</c>
    /// 是**作用域**服务。原先直接把作用域服务注入单例有两个后果：
    /// <list type="number">
    ///   <item><b>启动即失败</b>——<c>ValidateScopes</c> 会拒绝这种 captive dependency。
    ///         实测症状：Silo 在 <c>Development</c> 环境下启动直接抛
    ///         "Cannot consume scoped service 'IDataContext&lt;...&gt;' from singleton 'MqttConnectionValidator'"，
    ///         只有把环境切成 Production（默认不做作用域校验）才能起来；</item>
    ///   <item><b>并发正确性受损</b>——即使跳过校验，单例持有的那个 DbContext 会被**所有并发 MQTT 连接共享**，
    ///         这是比启动失败更隐蔽的问题。</item>
    /// </list>
    /// 改为每次校验时开一个作用域并按需解析，两个问题一并消除。
    /// </para>
    /// </summary>
    public class MqttConnectionValidator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MqttBrokerOptions _brokerOptions;
        private readonly ILogger<MqttConnectionValidator> _logger;

        public MqttConnectionValidator(
            IServiceScopeFactory scopeFactory,
            IOptions<MqttBrokerOptions> brokerOptions,
            ILogger<MqttConnectionValidator> logger)
        {
            _scopeFactory = scopeFactory;
            _brokerOptions = brokerOptions.Value;
            _logger = logger;
        }

        public async Task ValidateAsync(ValidatingConnectionEventArgs args)
        {
            var deviceCode = args.ClientId;
            var apiKey = args.Password;

            if (deviceCode == _brokerOptions.BridgeClientId)
            {
                if (!string.IsNullOrEmpty(_brokerOptions.BridgeApiKey) && FixedTimeEqualsString(apiKey, _brokerOptions.BridgeApiKey))
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
                // 每次校验独立作用域：短生命周期、随用随弃，天然避免跨连接共享 DbContext。
                using var scope = _scopeFactory.CreateScope();
                var deviceContext = scope.ServiceProvider
                    .GetRequiredService<IDataContext<FlowerEntityContext, FlowerIoTDevice, long>>();

                var device = await deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);

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

                // 修复（安全）：API key 比较改为恒定时间，避免字符串 == 的时间侧信道泄露密钥前缀。
                if (!FixedTimeEqualsString(device.ApiKey, apiKey))
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

        /// <summary>恒定时间比较两个字符串（UTF-8 字节），避免时序侧信道。长度不等时直接返回 false。</summary>
        private static bool FixedTimeEqualsString(string? a, string? b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a ?? string.Empty);
            var bBytes = Encoding.UTF8.GetBytes(b ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}

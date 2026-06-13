using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Horizon.IoT.MQTT
{
    public class MqttTopicAuthorizer
    {
        private readonly ILogger<MqttTopicAuthorizer> _logger;

        public MqttTopicAuthorizer(ILogger<MqttTopicAuthorizer> logger)
        {
            _logger = logger;
        }

        public Task ValidatePublishAsync(InterceptingPublishEventArgs args)
        {
            var deviceCode = args.SessionItems?["DeviceCode"] as string;
            var greenhouseId = args.SessionItems?["GreenhouseId"] as string;

            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(greenhouseId))
            {
                args.ProcessPublish = false;
                _logger.LogWarning("MQTT publish rejected: unauthenticated client");
                return Task.CompletedTask;
            }

            var topic = args.ApplicationMessage.Topic;
            var expectedPrefix = $"flower/iot/{greenhouseId}/{deviceCode}/";

            if (!topic.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                args.ProcessPublish = false;
                _logger.LogWarning("MQTT publish rejected: topic {Topic} not allowed for device {DeviceCode}", topic, deviceCode);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        public Task ValidateSubscriptionAsync(InterceptingSubscriptionEventArgs args)
        {
            var deviceCode = args.SessionItems?["DeviceCode"] as string;
            var greenhouseId = args.SessionItems?["GreenhouseId"] as string;

            if (string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(greenhouseId))
            {
                args.ProcessSubscription = false;
                _logger.LogWarning("MQTT subscription rejected: unauthenticated client");
                return Task.CompletedTask;
            }

            var topic = args.TopicFilter.Topic;
            var commandTopic = $"flower/iot/{greenhouseId}/{deviceCode}/command/+";
            var configTopic = $"flower/iot/{greenhouseId}/{deviceCode}/config/+";

            if (!IsTopicMatch(topic, commandTopic) &&
                !IsTopicMatch(topic, configTopic))
            {
                args.ProcessSubscription = false;
                _logger.LogWarning("MQTT subscription rejected: topic {Topic} not allowed for device {DeviceCode}", topic, deviceCode);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        private static bool IsTopicMatch(string actualTopic, string filterPattern)
        {
            var filterParts = filterPattern.Split('/');
            var topicParts = actualTopic.Split('/');

            if (filterParts.Length != topicParts.Length)
                return false;

            for (var i = 0; i < filterParts.Length; i++)
            {
                if (filterParts[i] == "+")
                    continue;

                if (!string.Equals(filterParts[i], topicParts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }
}

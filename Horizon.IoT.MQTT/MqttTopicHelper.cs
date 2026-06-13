namespace Horizon.IoT.MQTT
{
    public static class MqttTopicHelper
    {
        public static string ParseGreenhouseId(string topic)
        {
            var segments = topic.Split('/');
            if (segments.Length > 2)
                return segments[2];
            return string.Empty;
        }

        public static string ParseDeviceCode(string topic)
        {
            var segments = topic.Split('/');
            if (segments.Length > 3)
                return segments[3];
            return string.Empty;
        }

        public static string ParseMessageType(string topic)
        {
            var segments = topic.Split('/');
            if (segments.Length > 4)
                return segments[4];
            return string.Empty;
        }

        public static string BuildSensorDataTopic(string greenhouseId, string deviceCode)
        {
            return $"flower/iot/{greenhouseId}/{deviceCode}/sensor/data";
        }

        public static string BuildCommandTopic(string greenhouseId, string deviceCode, string action)
        {
            return $"flower/iot/{greenhouseId}/{deviceCode}/command/{action}";
        }

        public static string BuildConfigTopic(string greenhouseId, string deviceCode, string configType)
        {
            return $"flower/iot/{greenhouseId}/{deviceCode}/config/{configType}";
        }

        public static string BuildStatusTopic(string greenhouseId, string deviceCode, string statusType)
        {
            return $"flower/iot/{greenhouseId}/{deviceCode}/status/{statusType}";
        }
    }
}

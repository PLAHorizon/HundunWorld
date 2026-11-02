using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 设备（大棚传感器）下行指令的执行回执，经 MQTT 以 JSON 回传。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class DeviceCommandResponse
    {
        [Id(0)] public string CommandId { get; set; } = "";
        [Id(1)] public string Action { get; set; } = "";
        [Id(2)] public bool Success { get; set; }
        [Id(3)] public string Result { get; set; } = "";
        [Id(4)] public DateTime Timestamp { get; set; }
    }
}

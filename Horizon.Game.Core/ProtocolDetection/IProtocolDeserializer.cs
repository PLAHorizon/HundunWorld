using System;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Core.ProtocolDetection
{
    /// <summary>
    /// 协议反序列化器接口 - 策略模式实现
    /// </summary>
    public interface IProtocolDeserializer
    {
        /// <summary>
        /// 协议版本标识
        /// </summary>
        string ProtocolVersion { get; }

        /// <summary>
        /// 优先级（数字越小优先级越高）
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 检查是否能够处理指定的数据
        /// </summary>
        /// <param name="data">待检测的数据</param>
        /// <returns>是否能够处理</returns>
        bool CanHandle(ReadOnlySpan<byte> data);

        /// <summary>
        /// 尝试反序列化数据
        /// </summary>
        /// <param name="data">待反序列化的数据</param>
        /// <param name="encryptionKey">加密密钥</param>
        /// <returns>反序列化结果，失败时返回null</returns>
        HorizonMessagePacket? TryDeserialize(ReadOnlySpan<byte> data, byte[]? encryptionKey);
    }
}

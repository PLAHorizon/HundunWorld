using Horizon.IM.Message.Enums;

namespace Horizon.IM.Message.Network
{
    /// <summary>
    /// IM网络消息接口
    /// </summary>
    public interface IIMNetworkMessage
    {
        /// <summary>
        /// IM服务类型
        /// </summary>
        IMServiceType ServiceType { get; }

        /// <summary>
        /// IM消息类型
        /// </summary>
        IMMessageType Type { get; }
    }
}

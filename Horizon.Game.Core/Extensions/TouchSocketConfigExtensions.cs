using Horizon.Game.Core.Adapters;
using Horizon.Game.Message.Network;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Extensions
{
    /// <summary>
    /// TouchSocket 配置扩展方法，用于集成 Horizon 消息适配器
    /// </summary>
    public static class TouchSocketConfigExtensions
    {
        /// <summary>
        /// 使用 Horizon 消息适配器（简化版本）
        /// </summary>
        /// <param name="config">TouchSocket 配置</param>
        /// <returns>配置对象</returns>
        public static TouchSocketConfig UseHorizonMessageAdapter(this TouchSocketConfig config)
        {
            // 每次调用都创建新的适配器实例，避免重用问题
            return config.SetTcpDataHandlingAdapter(() => new HorizonMessageAdapter());
        }
    }
}

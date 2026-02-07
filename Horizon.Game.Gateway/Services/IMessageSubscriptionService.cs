using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 消息订阅服务接口
    /// </summary>
    public interface IMessageSubscriptionService
    {
        /// <summary>
        /// 订阅广播消息
        /// </summary>
        Task SubscribeToBroadcastMessagesAsync();
        
        /// <summary>
        /// 处理广播消息
        /// </summary>
        Task HandleBroadcastMessageAsync(BroadcastMessage message);
        
        /// <summary>
        /// 启动服务
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 停止服务
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 集群协调服务接口
    /// </summary>
    public interface IClusterCoordinationService
    {
        /// <summary>
        /// 注册网关实例
        /// </summary>
        Task RegisterGatewayInstanceAsync(GatewayInstanceInfo instanceInfo);
        
        /// <summary>
        /// 获取所有网关实例
        /// </summary>
        Task<List<GatewayInstanceInfo>> GetAllGatewayInstancesAsync();
        
        /// <summary>
        /// 更新网关实例状态
        /// </summary>
        Task UpdateGatewayInstanceStateAsync(string instanceId, GatewayInstanceState state);
        
        /// <summary>
        /// 获取连接分布信息
        /// </summary>
        Task<ConnectionDistributionInfo> GetConnectionDistributionAsync();
        
        /// <summary>
        /// 保存集群状态快照（用于容灾恢复）
        /// </summary>
        Task SaveClusterSnapshotAsync();
        
        /// <summary>
        /// 恢复集群状态快照
        /// </summary>
        Task<ClusterState> RestoreClusterSnapshotAsync();
        
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
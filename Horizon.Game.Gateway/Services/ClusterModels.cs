using System;
using System.Collections.Generic;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 网关实例信息
    /// </summary>
    public class GatewayInstanceInfo
    {
        /// <summary>
        /// 实例ID
        /// </summary>
        public string InstanceId { get; set; }
        
        /// <summary>
        /// 实例地址
        /// </summary>
        public string Address { get; set; }
        
        /// <summary>
        /// 实例端口
        /// </summary>
        public int Port { get; set; }
        
        /// <summary>
        /// 连接数
        /// </summary>
        public int ConnectionCount { get; set; }
        
        /// <summary>
        /// 实例状态
        /// </summary>
        public GatewayInstanceState State { get; set; }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// 网关实例状态枚举
    /// </summary>
    public enum GatewayInstanceState
    {
        /// <summary>
        /// 正常运行
        /// </summary>
        Running,
        
        /// <summary>
        /// 维护中
        /// </summary>
        Maintenance,
        
        /// <summary>
        /// 故障
        /// </summary>
        Faulted
    }

    /// <summary>
    /// 连接分布信息
    /// </summary>
    public class ConnectionDistributionInfo
    {
        /// <summary>
        /// 总连接数
        /// </summary>
        public int TotalConnections { get; set; }
        
        /// <summary>
        /// 各实例连接分布
        /// </summary>
        public Dictionary<string, int> InstanceConnections { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// 集群状态信息
    /// </summary>
    public class ClusterState
    {
        /// <summary>
        /// 集群ID
        /// </summary>
        public string ClusterId { get; set; }
        
        /// <summary>
        /// 网关实例列表
        /// </summary>
        public List<GatewayInstanceInfo> Instances { get; set; } = new List<GatewayInstanceInfo>();
        
        /// <summary>
        /// 总连接数
        /// </summary>
        public int TotalConnections { get; set; }
        
        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdate { get; set; }
    }
}
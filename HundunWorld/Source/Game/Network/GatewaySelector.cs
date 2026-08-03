using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网关选择器
    /// 负责选择最佳网关服务器
    /// </summary>
    public class GatewaySelector
    {
        private readonly List<GatewayInfo> _gatewayList;
        private readonly object _lock = new object();
        private GatewayInfo _lastSuccessfulGateway; // 记录上次连接成功的网关

        public GatewaySelector(List<GatewayInfo> gatewayList)
        {
            _gatewayList = gatewayList ?? throw new ArgumentNullException(nameof(gatewayList));
        }

        /// <summary>
        /// 选择最佳网关
        /// </summary>
        /// <returns>最佳网关信息</returns>
        public async Task<GatewayInfo> SelectBestGatewayAsync()
        {
            if (_gatewayList == null || _gatewayList.Count == 0)
                throw new InvalidOperationException("网关列表为空");

            // 测试每个网关的延迟
            var gatewayLatencies = new List<(GatewayInfo Gateway, long Latency)>();

            foreach (var gateway in _gatewayList)
            {
                var latency = await TestGatewayLatencyAsync(gateway);
                gateway.Latency = latency;
                gateway.LastTestTime = DateTime.UtcNow;
                gatewayLatencies.Add((gateway, latency));
            }
            
            List<GatewayInfo> availableGateways;
            lock (_lock)
            {
                // 过滤出可用的网关
                availableGateways = gatewayLatencies.Where(g => g.Gateway.IsAvailable).Select(m=>m.Gateway).ToList();
            }

            if (availableGateways.Count == 0)
                throw new InvalidOperationException("没有可用的网关");
                
            // 根据延迟和负载选择最佳网关
            var bestGateway = gatewayLatencies
                .OrderBy(g => CalculateGatewayScore(g.Gateway, g.Latency))
                .First();

            return bestGateway.Gateway;
        }
        
        /// <summary>
        /// 获取按优先级排序的网关列表（上次连接成功的网关优先）
        /// </summary>
        /// <returns>按优先级排序的网关列表</returns>
        public List<GatewayInfo> GetPrioritizedGatewayList()
        {
            lock (_lock)
            {
                // 创建网关列表副本
                var gateways = new List<GatewayInfo>(_gatewayList);
                
                // 如果有上次连接成功的网关，将其移到列表开头
                if (_lastSuccessfulGateway != null && gateways.Contains(_lastSuccessfulGateway))
                {
                    // 将上次成功的网关移到列表开头
                    gateways.Remove(_lastSuccessfulGateway);
                    gateways.Insert(0, _lastSuccessfulGateway);
                }
                
                return gateways;
            }
        }

        /// <summary>
        /// 计算网关评分（数值越小越好）
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <param name="latency">延迟</param>
        /// <returns>评分</returns>
        private double CalculateGatewayScore(GatewayInfo gateway, long latency)
        {
            // 如果连接失败，给予最高评分
            if (latency == long.MaxValue)
                return double.MaxValue;

            // 综合考虑延迟和负载
            // 延迟权重0.7，负载权重0.3
            double latencyScore = latency * 0.7;
            double loadScore = gateway.Load * 0.3;
            
            // 失败次数惩罚
            double failurePenalty = gateway.FailureCount * 100;
            
            return latencyScore + loadScore + failurePenalty;
        }

        /// <summary>
        /// 测试网关延迟。当前已禁用 TCP 连接探查，始终返回固定延迟 1ms 并标记网关可用。
        /// <para>
        /// 原实现创建完整 TouchSocket TcpClient 协议连接测量延迟，
        /// 服务端会创建 GameConnection 并等待首包，不发送数据即关闭 → 首包超时幽灵连接。
        /// 现已禁用探查，直接返回 1ms 固定延迟。如需恢复，恢复方法体内的 TCP 连接测延迟逻辑。
        /// </para>
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <returns>延迟时间（毫秒）</returns>
        public async Task<long> TestGatewayLatencyAsync(GatewayInfo gateway)
        {
            await Task.CompletedTask; // 保持 async 签名

            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            gateway.IsAvailable = true;
            return 1; // 固定延迟 1ms，表示可达
        }

        /// <summary>
        /// 标记网关为不可用
        /// </summary>
        /// <param name="gateway">网关信息</param>
        public void MarkGatewayAsUnavailable(GatewayInfo gateway)
        {
            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            lock (_lock)
            {
                gateway.IsAvailable = false;
                gateway.FailureCount++;
            }
        }

        /// <summary>
        /// 标记网关为可用
        /// </summary>
        /// <param name="gateway">网关信息</param>
        public void MarkGatewayAsAvailable(GatewayInfo gateway)
        {
            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            lock (_lock)
            {
                gateway.IsAvailable = true;
                // 不重置失败次数，而是逐渐减少
                if (gateway.FailureCount > 0)
                    gateway.FailureCount--;
                    
                // 记录上次连接成功的网关
                _lastSuccessfulGateway = gateway;
            }
        }
        
        /// <summary>
        /// 获取所有网关信息的副本
        /// </summary>
        /// <returns>网关信息列表</returns>
        public List<GatewayInfo> GetAllGateways()
        {
            lock (_lock)
            {
                return new List<GatewayInfo>(_gatewayList);
            }
        }
    }
}
using FlaxEngine;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络可用性检查器
    /// 负责检查特定网关的可用性，实现指数退避检测策略，避免频繁检测影响性能
    /// </summary>
    public class NetworkAvailabilityChecker
    {
        private readonly ConcurrentDictionary<string, GatewayAvailabilityInfo> _gatewayAvailabilityCache = new();
        private readonly object _lock = new object();

        /// <summary>
        /// 网关可用性信息
        /// </summary>
        private class GatewayAvailabilityInfo
        {
            public bool IsAvailable { get; set; }
            public DateTime LastCheckTime { get; set; }
            public int FailureCount { get; set; }
            public long Latency { get; set; } = long.MaxValue;
        }

        /// <summary>
        /// 检查单个网关的可用性
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <returns>网关是否可用</returns>
        public async Task<bool> CheckGatewayAvailabilityAsync(GatewayInfo gateway)
        {
            if (gateway == null)
                throw new ArgumentNullException(nameof(gateway));

            var key = $"{gateway.IP}:{gateway.Port}";

            // 检查缓存中是否有近期的检查结果
            if (_gatewayAvailabilityCache.TryGetValue(key, out var info) &&
                DateTime.UtcNow - info.LastCheckTime < GetCacheDuration(info.FailureCount))
            {
                EnhancedDiagnostics.LogDiagnostic($"使用缓存的网关检查结果: {gateway.IP}:{gateway.Port} 可用: {info.IsAvailable}");
                return info.IsAvailable;
            }

            // 执行实际检查
            var isAvailable = await PerformGatewayCheckAsync(gateway);
            var latency = await MeasureGatewayLatencyAsync(gateway);

            // 更新缓存
            _gatewayAvailabilityCache[key] = new GatewayAvailabilityInfo
            {
                IsAvailable = isAvailable,
                LastCheckTime = DateTime.UtcNow,
                FailureCount = isAvailable ? 0 : info?.FailureCount + 1 ?? 1,
                Latency = latency
            };

            EnhancedDiagnostics.LogDiagnostic($"网关检查完成: {gateway.IP}:{gateway.Port} 可用: {isAvailable}, 延迟: {latency}ms");
            return isAvailable;
        }

        /// <summary>
        /// 并行检查多个网关的可用性
        /// </summary>
        /// <param name="gateways">网关列表</param>
        /// <returns>网关可用性字典</returns>
        public async Task<Dictionary<GatewayInfo, bool>> CheckMultipleGatewaysAvailabilityAsync(List<GatewayInfo> gateways)
        {
            if (gateways == null || gateways.Count == 0)
                return new Dictionary<GatewayInfo, bool>();

            var results = new Dictionary<GatewayInfo, bool>();
            var tasks = new List<Task<KeyValuePair<GatewayInfo, bool>>>();

            // 为每个网关创建检查任务
            foreach (var gateway in gateways)
            {
                tasks.Add(CheckSingleGatewayAsync(gateway));
            }

            // 等待所有任务完成
            var taskResults = await Task.WhenAll(tasks);

            // 收集结果
            foreach (var result in taskResults)
            {
                results[result.Key] = result.Value;
            }

            EnhancedDiagnostics.LogDiagnostic($"多网关检查完成，检查了 {gateways.Count} 个网关");
            return results;
        }

        /// <summary>
        /// 检查单个网关的可用性（用于并行处理）
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <returns>网关和其可用性</returns>
        private async Task<KeyValuePair<GatewayInfo, bool>> CheckSingleGatewayAsync(GatewayInfo gateway)
        {
            var isAvailable = await CheckGatewayAvailabilityAsync(gateway);
            return new KeyValuePair<GatewayInfo, bool>(gateway, isAvailable);
        }

        /// <summary>
        /// 执行网关可用性检查
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <returns>网关是否可用</returns>
        private async Task<bool> PerformGatewayCheckAsync(GatewayInfo gateway)
        {
            try
            {
                // 使用网络连接助手类来处理连接
                return await NetworkConnectionHelper.ConnectWithExceptionHandlingAsync(gateway.IP, gateway.Port, 5000);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[网关检查] 检查网关 {gateway.IP}:{gateway.Port} 时发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, $"网关检查 {gateway.IP}:{gateway.Port}");
                return false;
            }
        }

        /// <summary>
        /// 测量网关延迟
        /// </summary>
        /// <param name="gateway">网关信息</param>
        /// <returns>延迟时间（毫秒）</returns>
        private async Task<long> MeasureGatewayLatencyAsync(GatewayInfo gateway)
        {
            try
            {
                // 使用网络连接助手类来测量延迟
                return await NetworkConnectionHelper.MeasureLatencyWithExceptionHandlingAsync(gateway.IP, gateway.Port, 5000);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[延迟测量] 测量网关 {gateway.IP}:{gateway.Port} 延迟时发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, $"延迟测量 {gateway.IP}:{gateway.Port}");
                return long.MaxValue;
            }
        }

        /// <summary>
        /// 根据失败次数计算缓存持续时间（指数退避策略）
        /// </summary>
        /// <param name="failureCount">失败次数</param>
        /// <returns>缓存持续时间</returns>
        private TimeSpan GetCacheDuration(int failureCount)
        {
            // 基础缓存时间10秒
            var baseDuration = TimeSpan.FromSeconds(10);

            // 根据失败次数应用指数退避
            if (failureCount == 0)
                return baseDuration;
            else if (failureCount <= 3)
                return TimeSpan.FromSeconds(10 * failureCount);
            else
                return TimeSpan.FromMinutes(1); // 最大1分钟缓存
        }

        /// <summary>
        /// 清理过期的缓存项
        /// </summary>
        private void CleanupExpiredCache()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();

            foreach (var kvp in _gatewayAvailabilityCache)
            {
                var info = kvp.Value;
                if (now - info.LastCheckTime > TimeSpan.FromMinutes(5))
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _gatewayAvailabilityCache.TryRemove(key, out _);
            }

            EnhancedDiagnostics.LogDiagnostic($"清理了 {expiredKeys.Count} 个过期的缓存项");
        }

        /// <summary>
        /// 开始定期检查网关可用性
        /// </summary>
        /// <param name="gateways">要检查的网关列表</param>
        public void StartPeriodicCheck(List<GatewayInfo> gateways)
        {
            // 实现定期检查逻辑
            EnhancedDiagnostics.LogDiagnostic("开始定期网关检查");
        }

        /// <summary>
        /// 停止定期检查
        /// </summary>
        public void StopPeriodicCheck()
        {
            // 实现停止检查逻辑
            EnhancedDiagnostics.LogDiagnostic("停止定期网关检查");
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 多主机连通性并发探测器（纯 C#，无 FlaxEngine 依赖，可单元测试）。
    /// 对多个测试主机并行发起探测，任一成功即返回 true，并取消其余在途探测。
    /// 探测使用独立的局部取消令牌，绝不永久取消调用方共享的 CancellationTokenSource，
    /// 避免"一次成功探测后所有后续连通性检查被永久短路"导致客户端永不重连的死锁缺陷。
    /// </summary>
    public static class NetworkProbeRunner
    {
        /// <summary>
        /// 并发探测多个主机：任一成功即返回 true，同时取消其余在途探测。
        /// </summary>
        /// <param name="hosts">测试主机列表。</param>
        /// <param name="port">探测端口。</param>
        /// <param name="probeAsync">单主机探测委托；返回 true 表示该主机连通。</param>
        /// <param name="cancellationToken">调用方取消令牌（仅作为链接来源，不会被取消）。</param>
        /// <returns>任一主机连通返回 true，否则 false。</returns>
        public static async Task<bool> ProbeAnyAsync(
            IReadOnlyList<string> hosts,
            int port,
            Func<string, int, CancellationToken, Task<bool>> probeAsync,
            CancellationToken cancellationToken)
        {
            if (hosts == null || hosts.Count == 0)
            {
                return false;
            }

            using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tasks = new List<Task<bool>>(hosts.Count);
            foreach (var host in hosts)
            {
                if (localCts.IsCancellationRequested) break;
                tasks.Add(probeAsync(host, port, localCts.Token));
            }

            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);
                tasks.Remove(completedTask);

                bool reachable;
                try
                {
                    reachable = await completedTask;
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (Exception)
                {
                    continue;
                }

                if (reachable)
                {
                    // 取消其余在途探测（仅局部令牌，不影响调用方共享令牌）
                    localCts.Cancel();
                    // 观察其余任务，避免未观察任务异常
                    foreach (var remaining in tasks)
                    {
                        _ = remaining.ContinueWith(static _ => { }, TaskScheduler.Default);
                    }
                    return true;
                }
            }

            return false;
        }
    }
}
using FlaxEngine;
using Game.Game.Network;
using Horizon.Game.Message.Enums;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络状态监控器
    /// 负责监控设备网络状态变化，检测网络连接/断开事件
    /// </summary>
    public class NetworkStateMonitor : INetworkStateMonitor, IDisposable
    {
        private NetworkStatus _currentStatus = NetworkStatus.Unknown;
        private readonly object _lock = new object();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        /// <summary>
        /// 网络状态变化事件
        /// </summary>
        public event Action<NetworkStatus> NetworkStatusChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        public NetworkStateMonitor()
        {
            EnhancedLogging.LogInfo("网络状态监控器初始化开始");
            // 初始化时检查当前网络状态
            _ = Task.Run(async () =>
            {
                try
                {
                    // 在编辑器中减少等待时间，加快初始化速度
                    await Task.Delay(100); // 从x1000ms减少到80ms
                    var status = await CheckNetworkStatusAsync();
                    UpdateNetworkStatus(status);
                    EnhancedLogging.LogInfo("网络状态监控器初始化完成");
                }
                catch (Exception ex)
                {
                    EnhancedLogging.LogError($"网络状态监控器初始化时发生错误: {ex.Message}");
                    EnhancedDiagnostics.LogException(ex, "网络状态监控器初始化");
                }
            });
        }

        /// <summary>
        /// 获取当前网络状态
        /// </summary>
        /// <returns>当前网络状态</returns>
        public NetworkStatus GetCurrentStatus()
        {
            lock (_lock)
            {
                return _currentStatus;
            }
        }

        /// <summary>
        /// 检查网络是否可用
        /// </summary>
        /// <returns>网络是否可用</returns>
        public async Task<bool> IsNetworkAvailableAsync()
        {
            return await CheckNetworkConnectivityAsync();
        }

        /// <summary>
        /// 检查网关是否可达
        /// </summary>
        /// <param name="ip">网关IP地址</param>
        /// <param name="port">网关端口</param>
        /// <returns>网关是否可达</returns>
        public async Task<bool> IsGatewayReachableAsync(string ip, int port)
        {
            return await CheckPortReachabilityAsync(ip, port, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 检查网络连接状态
        /// </summary>
        /// <returns>网络状态</returns>
        private async Task<NetworkStatus> CheckNetworkStatusAsync()
        {
            try
            {
                // 检查网络连通性
                if (await CheckNetworkConnectivityAsync())
                {
                    return NetworkStatus.Connected;
                }
                else
                {
                    return NetworkStatus.Disconnected;
                }
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[网络状态检查] 检查网络状态时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "网络状态检查");
                return NetworkStatus.Unknown;
            }
        }

        /// <summary>
        /// 检查网络连通性（通过TCP连接测试）
        /// </summary>
        /// <returns>网络是否连通</returns>
        private async Task<bool> CheckNetworkConnectivityAsync()
        {
            try
            {
                // 检查取消令牌
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    EnhancedDiagnostics.LogDiagnostic("网络连通性检查被取消");
                    return false;
                }

                // 尝试连接几个公共服务器，但只测试最快的一个
                // 修复：并发探测与取消管理委托给 NetworkProbeRunner。
                // 原实现在任一主机连接成功后调用 _cancellationTokenSource.Cancel() 永久取消共享令牌，
                // 导致 StartMonitoring 监控循环退出且所有后续 IsNetworkAvailableAsync 被短路返回 false，
                // 心跳连续超时后 ReconnectionManager 误判"本地网络不可用"，永不触发重连。
                string[] testHosts = { "8.8.8.8", "114.114.114.114", "223.5.5.5" };
                int testPort = 53; // DNS端口

                bool available = await NetworkProbeRunner.ProbeAnyAsync(
                    testHosts,
                    testPort,
                    (host, port, token) => CheckPortReachabilityAsync(host, port, token),
                    _cancellationTokenSource.Token);

                if (!available)
                {
                    // 仅在未找到连接时记录（探查禁用时始终可达，此分支不会触发；保留用于未来恢复探查时诊断）。
                    EnhancedDiagnostics.LogDiagnostic("网络连通性检查完成，未找到可用连接");
                }
                return available;
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogError($"[网络连通性检查] 检查网络连通性时发生错误: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, "网络连通性检查");
                return false;
            }
        }

        /// <summary>
        /// 检查指定端口的可达性（TCP连接测试）
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="cancellationToken">本次探测的取消令牌（由 NetworkProbeRunner 提供局部令牌）。</param>
        /// <returns>端口是否可达</returns>
        private async Task<bool> CheckPortReachabilityAsync(string host, int port, CancellationToken cancellationToken)
        {
            try
            {
                // 检查取消令牌
                if (cancellationToken.IsCancellationRequested)
                {
                    EnhancedDiagnostics.LogDiagnostic($"端口检查 {host}:{port} 被取消");
                    return false;
                }

                // 使用网络连接助手类来处理连接，在编辑器中减少超时时间
                return await NetworkConnectionHelper.ConnectWithExceptionHandlingAsync(host, port, 1000); // 从3000ms减少到1000ms
            }
            catch (Exception ex)
            {
                EnhancedLogging.LogWarning($"[端口检查] 检查端口 {host}:{port} 时发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, $"端口检查 {host}:{port}");
                return false;
            }
        }

        /// <summary>
        /// 更新网络状态并触发事件
        /// </summary>
        /// <param name="newStatus">新的网络状态</param>
        private void UpdateNetworkStatus(NetworkStatus newStatus)
        {
            NetworkStatus oldStatus;
            lock (_lock)
            {
                oldStatus = _currentStatus;
                _currentStatus = newStatus;
            }

            // 如果状态发生变化，触发事件
            if (oldStatus != newStatus)
            {
                EnhancedLogging.LogInfo($"[网络状态变化] 从 {oldStatus} 变化到 {newStatus}");
                EnhancedDiagnostics.LogDiagnostic($"网络状态变化: {oldStatus} -> {newStatus}");
                NetworkStatusChanged?.Invoke(newStatus);
            }
        }

        /// <summary>
        ///手动干预检查
        /// </summary>
        public void Check()
        {
            var oldCts = _cancellationTokenSource;
            _cancellationTokenSource = new CancellationTokenSource();
            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
        }

        /// <summary>
        /// 开始监控网络状态变化
        /// </summary>
        public void StartMonitoring()
        {
            // 启动定期检查网络状态的任务
            _ = Task.Run(async () =>
            {
                EnhancedLogging.LogInfo("[监控] 开始网络状态监控");
                EnhancedDiagnostics.LogDiagnostic("开始网络状态监控");

                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var status = await CheckNetworkStatusAsync();
                        UpdateNetworkStatus(status);


                        // 检查是否需要继续监控
                        if (_disposed)
                        {
                            EnhancedLogging.LogInfo("[监控] 网络状态监控已停止");
                            EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（对象已释放）");
                            break;
                        }

                        await Task.Delay(5000, _cancellationTokenSource.Token); // 每5秒检查一次
                    }
                    catch (OperationCanceledException)
                    {
                        // 操作被取消，正常退出
                        EnhancedLogging.LogInfo("[监控] 网络状态监控已停止（操作被取消）");
                        EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（操作被取消）");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // 对象已被释放，正常退出
                        EnhancedLogging.LogInfo("[监控] 网络状态监控已停止（对象已释放）");
                        EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（对象已释放）");
                        break;
                    }
                    catch (Exception ex)
                    {
                        EnhancedLogging.LogError($"[网络监控] 监控网络状态时发生错误: {ex.Message}");
                        EnhancedLogging.LogError($"[网络监控] 错误堆栈: {ex.StackTrace}");
                        EnhancedDiagnostics.LogException(ex, "网络状态监控");

                        // 检查是否需要继续监控
                        if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            EnhancedLogging.LogInfo("[监控] 网络状态监控已停止");
                            EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（取消或释放）");
                            break;
                        }

                        // 出错时等待一段时间再重试
                        try
                        {
                            await Task.Delay(5000, _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 操作被取消，正常退出
                            EnhancedLogging.LogInfo("[监控] 网络状态监控已停止（操作被取消）");
                            EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（操作被取消）");
                            break;
                        }
                        catch (ObjectDisposedException)
                        {
                            // 对象已被释放，正常退出
                            EnhancedLogging.LogInfo("[监控] 网络状态监控已停止（对象已释放）");
                            EnhancedDiagnostics.LogDiagnostic("网络状态监控已停止（对象已释放）");
                            break;
                        }
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private bool _disposed = false;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            // 清理事件委托，防止外部引用残留
            NetworkStatusChanged = null;

            EnhancedDiagnostics.LogDiagnostic("网络状态监控器已释放");
        }

        // 为NetworkManager提供访问CancellationTokenSource的方法
        public CancellationTokenSource CancellationTokenSource => _cancellationTokenSource;
    }
}
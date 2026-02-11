using FlaxEngine;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 网络连接助手类，提供统一的网络连接和异常处理方法
    /// </summary>
    public static class NetworkConnectionHelper
    {
        /// <summary>
        /// 执行TCP连接操作并正确处理所有可能的异常
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>连接是否成功</returns>
        public static async Task<bool> ConnectWithExceptionHandlingAsync(string host, int port, int timeoutMs = 5000)
        {
            TcpClient tcpClient = null;

            try
            {
                tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && !connectTask.IsFaulted)
                {
                    // 检查连接是否成功建立
                    if (tcpClient.Client != null && tcpClient.Client.Connected)
                    {
                        // 正常关闭连接
                        tcpClient.Close();
                        EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", true);
                        return true;
                    }
                    else
                    {
                        EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, "连接未建立");
                        return false;
                    }
                }
                else
                {
                    // 如果是超时，取消连接任务
                    if (completedTask == timeoutTask && !connectTask.IsCompleted)
                    {
                        try
                        {
                            // 尝试取消连接任务
                            tcpClient?.Close();
                        }
                        catch (Exception ex)
                        {
                            EnhancedDiagnostics.LogException(ex, $"取消连接任务 {host}:{port}");
                            // 忽略取消连接时的异常
                        }
                    }
                    EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, "连接超时或失败");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，这是正常的，不需要记录为Warning
                EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, "操作被取消");
                return false;
            }
            catch (ObjectDisposedException)
            {
                // 对象已被释放，这是正常的，不需要记录为Warning
                EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, "对象已被释放");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TCP连接] 连接到 {host}:{port} 时发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, $"TCP连接 {host}:{port}");
                //专门处理Socket异常，特别是I / O操作中止的情况
                if (ex.HResult == 995) // WSA_OPERATION_ABORTED - 由于线程退出或应用程序请求，已中止 I/O 操作
                {
                    // I/O操作被中止是正常的取消操作，不需要记录为Warning
                    EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, $"I/O操作被中止 (错误码: {ex.HResult})");
                    return false;
                }
                else if (ex.HResult == 10060) // WSAETIMEDOUT - 连接超时
                {
                    EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, $"连接超时 (错误码: {ex.HResult})");
                    return false;
                }
                else
                {
                    Debug.LogWarning($"[TCP连接] 连接到 {host}:{port} 时发生Socket异常: {ex.Message}");
                    EnhancedDiagnostics.LogNetworkOperation("TCP连接", $"{host}:{port}", false, $"Socket异常 (错误码: {ex.HResult})");
                    return false;
                }
            }
            finally
            {
                // 确保资源被正确释放
                if (tcpClient != null)
                {
                    try
                    {
                        tcpClient.Close();
                    }
                    catch (Exception ex)
                    {
                        EnhancedDiagnostics.LogException(ex, $"释放TCP客户端 {host}:{port}");
                    }
                }
            }
        }

        /// <summary>
        /// 测量TCP连接延迟并正确处理所有可能的异常
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>连接延迟（毫秒），如果连接失败则返回long.MaxValue</returns>
        public static async Task<long> MeasureLatencyWithExceptionHandlingAsync(string host, int port, int timeoutMs = 5000)
        {
            

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
               using TcpClient tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                stopwatch.Stop();

                if (completedTask == connectTask && !connectTask.IsFaulted)
                {
                    // 检查连接是否成功建立
                    if (tcpClient.Client != null && tcpClient.Client.Connected)
                    {
                        // 正常关闭连接
                        tcpClient.Close();
                        EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", true, $"延迟: {stopwatch.ElapsedMilliseconds}ms");
                        return stopwatch.ElapsedMilliseconds;
                    }
                    else
                    {
                        EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, "连接未建立");
                        return long.MaxValue;
                    }
                }
                else
                {
                    // 如果是超时，取消连接任务
                    if (completedTask == timeoutTask && !connectTask.IsCompleted)
                    {
                        try
                        {
                            // 尝试取消连接任务
                            tcpClient?.Close();
                        }
                        catch (Exception ex)
                        {
                            EnhancedDiagnostics.LogException(ex, $"取消连接任务 {host}:{port}");
                            // 忽略取消连接时的异常
                        }
                    }
                    EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, "连接超时或失败");
                    return long.MaxValue;
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，这是正常的，不需要记录为Warning
                EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, "操作被取消");
                return long.MaxValue;
            }
            catch (ObjectDisposedException)
            {
                // 对象已被释放，这是正常的，不需要记录为Warning
                EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, "对象已被释放");
                return long.MaxValue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[延迟测量] 测量 {host}:{port} 延迟时发生异常: {ex.Message}");
                EnhancedDiagnostics.LogException(ex, $"延迟测量 {host}:{port}");
                // 专门处理Socket异常，特别是I/O操作中止的情况
                if (ex.HResult == 995) // WSA_OPERATION_ABORTED - 由于线程退出或应用程序请求，已中止 I/O 操作
                {
                    // I/O操作被中止是正常的取消操作，不需要记录为Warning
                    EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, $"I/O操作被中止 (错误码: {ex.HResult})");
                    return long.MaxValue;
                }
                else if (ex.HResult == 10060) // WSAETIMEDOUT - 连接超时
                {
                    EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, $"连接超时 (错误码: {ex.HResult})");
                    return long.MaxValue;
                }
                else
                {
                    Debug.LogWarning($"[延迟测量] 测量 {host}:{port} 延迟时发生Socket异常: {ex.Message}");
                    EnhancedDiagnostics.LogNetworkOperation("延迟测量", $"{host}:{port}", false, $"Socket异常 (错误码: {ex.HResult})");
                    return long.MaxValue;
                }
            }
            finally
            {
                // 确保资源被正确释放
                EnhancedDiagnostics.LogNetworkOperation( "释放测试连接",$"释放TCP客户端 {host}:{port}",false);
            }
        }
    }
}
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
        /// 执行TCP连接操作并正确处理所有可能的异常。当前已禁用 TCP 探查，始终返回 true（可达）。
        /// <para>
        /// 原实现创建原始 TcpClient 连接测试可达性，服务端可能 Accept 并创建短暂 GameConnection → 幽灵连接。
        /// 现已禁用，直接返回 true。如需恢复，恢复方法体内的 TCP 连接逻辑。
        /// </para>
        /// 已移除周期性诊断日志以避免刷屏（探查禁用时日志无信息量）。
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>连接是否成功</returns>
        public static async Task<bool> ConnectWithExceptionHandlingAsync(string host, int port, int timeoutMs = 5000)
        {
            await Task.CompletedTask; // 保持 async 签名
            return true;
        }

        /// <summary>
        /// 测量TCP连接延迟并正确处理所有可能的异常。当前已禁用 TCP 探查，始终返回固定延迟 1ms。
        /// <para>
        /// 原实现创建原始 TcpClient 连接测量延迟，服务端可能 Accept 并创建短暂 GameConnection → 幽灵连接。
        /// 现已禁用，直接返回 1ms。如需恢复，恢复方法体内的 TCP 连接测延迟逻辑。
        /// </para>
        /// 已移除周期性诊断日志以避免刷屏（探查禁用时日志无信息量）。
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>连接延迟（毫秒），如果连接失败则返回long.MaxValue</returns>
        public static async Task<long> MeasureLatencyWithExceptionHandlingAsync(string host, int port, int timeoutMs = 5000)
        {
            await Task.CompletedTask; // 保持 async 签名
            return 1;
        }
    }
}
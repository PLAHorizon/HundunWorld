using System;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using Game.Game.Network;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 客户端单连接编排协调器实现（连接精简治理，spec 5.1.1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 职责：
    /// </para>
    /// <list type="bullet">
    ///   <item><b>互斥夺锁</b>：<see cref="RequestConnectAsync"/> 在发起任何 TCP 动作前先原子置位"连接中"标志，
    ///         已置位则返回 false 让并发请求复用/等待，保证同一时刻仅一条建连路径在途。</item>
    ///   <item><b>首包契约</b>：实际执行建连的路径在 TCP 连接建立后立即由业务调用方发送首包
    ///         （进游戏 EnterGameRequest / 重连 ReconnectResumePacket / 登录认证请求），协调器记录时延。</item>
    ///   <item><b>首包时延观测</b>：记录"TCP 连接建立 → 首包发出"时延到 <see cref="LastFirstPacketLatencyMs"/>，
    ///         超过 1 秒输出诊断日志（spec 5.1.1.2），不阻塞建连流程。</item>
    /// </list>
    /// <para>
    /// 与 <see cref="NetworkManager"/> 组合 1:1（构造注入）。<see cref="NetworkManager.ConnectAsync"/> 内部已强化
    /// 原子夺锁语义（spec 5.1.1.3），本协调器在更高层编排三类业务建连请求（登录/进游戏/重连）的互斥与首包契约。
    /// </para>
    /// </remarks>
    public sealed class ClientConnectionCoordinator : IClientConnectionCoordinator
    {
        private readonly NetworkManager _networkManager;

        private int _connectingFlag;             // 互斥标志位（Interlocked）
        private int _lastFirstPacketLatencyMs;   // 首包时延（毫秒，Volatile）
        private long _connectStartedAtMs;

        /// <summary>首包时延观测值（毫秒）：最近一次"TCP 连接建立 → 首包发出"时延。</summary>
        public int LastFirstPacketLatencyMs => Volatile.Read(ref _lastFirstPacketLatencyMs);

        /// <summary>是否有建连流程在途（互斥状态）。</summary>
        public bool IsConnectingInProgress => Volatile.Read(ref _connectingFlag) != 0;

        /// <summary>
        /// 创建单连接编排协调器。
        /// </summary>
        /// <param name="networkManager">组合的 NetworkManager 实例（不可为 null）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> 为 null。</exception>
        public ClientConnectionCoordinator(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        /// <inheritdoc />
        public async Task<bool> RequestConnectAsync(ClientConnectionRequestKind kind)
        {
            // 快速路径：连接已在线，直接复用（不发起任何 TCP 动作）。
            if (_networkManager.GetConnectionStatus() == ConnectionStatus.Connected)
            {
                EnhancedLogging.LogInfo($"[ClientConnectionCoordinator] 连接已在线（{kind}），复用现有连接");
                return false;
            }

            // 原子夺锁：仅一条路径能成功置位"连接中"标志，其余路径返回 false（复用/等待）。
            if (Interlocked.CompareExchange(ref _connectingFlag, 1, 0) != 0)
            {
                EnhancedLogging.LogInfo($"[ClientConnectionCoordinator] 已有建连流程在途（{kind}），本次请求复用/等待");
                return false;
            }

            try
            {
                _connectStartedAtMs = Environment.TickCount64;
                EnhancedLogging.LogInfo($"[ClientConnectionCoordinator] 请求建连（{kind}）开始");

                var gateway = _networkManager.GetCurrentGateway();
                if (gateway == null)
                {
                    // 修复 BUG（登录建连报"无可用网关"）：首次建连时 _currentGateway 尚未设置
                    //（仅在 ConnectAsync 内部赋值）。从 NetworkManager 已加载的网关列表中解析一个网关
                    // 作为当前网关，闭合"先有鸡还是先有蛋"缺口。
                    if (!_networkManager.TryResolveGateway())
                    {
                        EnhancedLogging.LogWarning($"[ClientConnectionCoordinator] 无可用网关（{kind}），建连失败");
                        return false;
                    }
                    gateway = _networkManager.GetCurrentGateway();
                }

                var connected = await _networkManager.ConnectAsync(gateway.IP, gateway.Port);
                if (!connected)
                {
                    EnhancedLogging.LogWarning($"[ClientConnectionCoordinator] 建连失败（{kind}），NetworkManager 已回收客户端");
                    return false;
                }

                EnhancedLogging.LogInfo($"[ClientConnectionCoordinator] 建连成功（{kind}），等待调用方发送首包");
                return true;
            }
            catch (Exception ex)
            {
                EnhancedDiagnostics.LogException(ex, "ClientConnectionCoordinator.RequestConnectAsync");
                return false;
            }
            finally
            {
                // 释放互斥标志（无论成败，下次建连可再次夺锁）。
                Volatile.Write(ref _connectingFlag, 0);
            }
        }

        /// <summary>
        /// 标记首包已发出：由业务调用方在 TCP 建连成功并发出首包后调用，
        /// 记录"连接建立 → 首包发出"时延并输出诊断日志（spec 5.1.1.2）。
        /// </summary>
        public void MarkFirstPacketSent()
        {
            var latencyMs = (int)(Environment.TickCount64 - _connectStartedAtMs);
            Volatile.Write(ref _lastFirstPacketLatencyMs, Math.Max(0, latencyMs));

            if (latencyMs > 1000)
            {
                Debug.LogWarning($"[ClientConnectionCoordinator] 首包时延超标: {latencyMs}ms > 1000ms");
            }
            else
            {
                EnhancedLogging.LogInfo($"[ClientConnectionCoordinator] 首包已发出，时延 {latencyMs}ms");
            }
        }
    }
}

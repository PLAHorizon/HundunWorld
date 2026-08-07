using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 心跳包管理器
    /// </summary>
    public class HeartbeatManager
    {
        private readonly NetworkManager _networkManager;
        private CancellationTokenSource _heartbeatCts;
        private bool _isRunning = false;
        private const int HeartbeatInterval = 15000; // 15秒间隔（优化：从20秒降低到15秒，与服务端IdleTimeout=45秒配合，容差30秒覆盖网络抖动和GC暂停）

        /// <summary>
        /// [Phase C3] 最近一次心跳发送的 Stopwatch 时间戳，供 HeartbeatResponseHandler 计算 RTT。
        /// 跨线程读写使用 Volatile 保证可见性。
        /// </summary>
        private static long _lastHeartbeatSentTimestamp;

        /// <summary>获取最近一次心跳发送的 Stopwatch 时间戳（0 表示尚未发送）。</summary>
        public static long LastHeartbeatSentTimestamp => Volatile.Read(ref _lastHeartbeatSentTimestamp);

        public HeartbeatManager(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        /// <summary>
        /// 启动心跳包发送
        /// </summary>
        public void StartHeartbeat()
        {
            // 如果已经在运行，先停止
            if (_isRunning)
            {
                StopHeartbeat();
            }
            
            _isRunning = true;
            _heartbeatCts?.Dispose();
            _heartbeatCts = new CancellationTokenSource();
            
            _ = Task.Run(async () =>
            {
                Debug.Log("[心跳管理器] 开始发送心跳包");
                EnhancedDiagnostics.LogDiagnostic("开始发送心跳包");
                
                while (!(_heartbeatCts?.Token.IsCancellationRequested??true))
                {
                    try
                    {
                        // 检查是否可以发送消息（需要已认证用户）
                        var heartbeatSuccess = false; // 声明在 if-else 之外，确保后续 delayMs 逻辑可访问
                        if (_networkManager.CanSendMessage() && !string.IsNullOrEmpty(_networkManager.AuthToken))
                        {
                            // 发送心跳消息
                            var heartbeatMessage = new HeartbeatMessage
                            {
                                ServiceType = ServiceType.System,
                                Type = MessageType.Heartbeat,
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            };
                            
                            var heartbeatPacket = new HorizonMessagePacket
                            {
                                Header = new MessageHeader
                                {
                                    MessageId = Guid.NewGuid().ToString(),
                                    MessageType = MessageType.Heartbeat,
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    GameId = _networkManager.GameId,
                                    ZoneId = _networkManager.ZoneId,
                                    ServerId = _networkManager.ServerId,
                                    UserId = _networkManager.UserId,
                                    AuthToken = _networkManager.AuthToken,
                                    MachineId = MachineIdentifier.GetMachineGuid()
                                },
                                ServiceType = ServiceType.System,
                                Body = heartbeatMessage
                            };

                            heartbeatSuccess = await _networkManager.SendMessageAsync(heartbeatPacket);

                            // [Phase C3] 记录心跳发送时间戳，供 RTT 计算
                            if (heartbeatSuccess)
                            {
                                Volatile.Write(ref _lastHeartbeatSentTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
                            }
                            
                            if (heartbeatSuccess)
                            {
                                Debug.Log($"[心跳管理器] 心跳包发送成功 ({DateTime.Now:HH:mm:ss})");
                                EnhancedDiagnostics.LogNetworkOperation("心跳包发送", "服务器", true);
                            }
                            else
                            {
                                Debug.LogWarning($"[心跳管理器] 心跳包发送失败 ({DateTime.Now:HH:mm:ss})");
                                EnhancedDiagnostics.LogNetworkOperation("心跳包发送", "服务器", false);
                            }
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(_networkManager.AuthToken))
                                Debug.Log($"[心跳管理器] 用户未认证，跳过心跳包发送 ({DateTime.Now:HH:mm:ss})");
                            else
                                Debug.Log($"[心跳管理器] 网络未连接，跳过心跳包发送 ({DateTime.Now:HH:mm:ss})");
                            EnhancedDiagnostics.LogDiagnostic("网络未连接或未认证，跳过心跳包发送");
                        }

                        // 等待下次发送（优化：发送失败或未发送时缩短等待到5秒，快速重试避免空闲超时）
                        var delayMs = heartbeatSuccess ? HeartbeatInterval : 5000;
                        await Task.Delay(delayMs, _heartbeatCts?.Token?? CancellationToken.None);
                    }
                    catch (OperationCanceledException)
                    {
                        // 任务被取消，正常退出
                        Debug.Log("[心跳管理器] 心跳包发送任务已取消");
                        EnhancedDiagnostics.LogDiagnostic("心跳包发送任务已取消");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[心跳管理器] 发送心跳包时发生错误: {ex.Message}");
                        Debug.LogError($"[心跳管理器] 错误堆栈: {ex.StackTrace}");
                        EnhancedDiagnostics.LogException(ex, "发送心跳包");
                        
                        // 出错时等待一段时间后重试
                        try
                        {
                            if (_heartbeatCts?.Token.IsCancellationRequested??true)
                            {
                                break;
                            }
                            await Task.Delay(5000, _heartbeatCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                
                _isRunning = false;
                Debug.Log("[心跳管理器] 心跳包发送任务已停止");
                EnhancedDiagnostics.LogDiagnostic("心跳包发送任务已停止");
            }, _heartbeatCts.Token);
        }

        /// <summary>
        /// 停止心跳包发送
        /// </summary>
        public void StopHeartbeat()
        {
            if (!_isRunning) return;
            
            _isRunning = false;
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
            Debug.Log("[心跳管理器] 请求停止心跳包发送");
            EnhancedDiagnostics.LogDiagnostic("请求停止心跳包发送");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            StopHeartbeat();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
            EnhancedDiagnostics.LogDiagnostic("心跳包管理器资源已释放");
        }
    }
}
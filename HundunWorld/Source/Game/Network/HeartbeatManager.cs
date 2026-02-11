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
        private const int HeartbeatInterval = 20000; // 20秒间隔

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
                        // 检查是否可以发送消息
                        if (_networkManager.CanSendMessage())
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
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                },
                                ServiceType = ServiceType.System,
                                Body = heartbeatMessage
                            };

                            var heartbeatSuccess = await _networkManager.SendMessageAsync(heartbeatPacket);
                            
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
                            Debug.Log($"[心跳管理器] 网络未连接，跳过心跳包发送 ({DateTime.Now:HH:mm:ss})");
                            EnhancedDiagnostics.LogDiagnostic("网络未连接，跳过心跳包发送");
                        }
                        
                        // 等待下次发送
                        await Task.Delay(HeartbeatInterval, _heartbeatCts?.Token?? CancellationToken.None);
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
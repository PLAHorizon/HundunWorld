using FlaxEngine;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game
{
    /// <summary>
    /// 网络诊断脚本，用于诊断网络连接问题
    /// </summary>
    public class NetworkDiagnosticsScript : Script
    {
        private NetworkManager _networkManager;
        
        public override void OnStart()
        {
            Debug.Log("网络诊断脚本初始化开始");
            
            // 延迟执行测试，确保系统完全初始化
            Scripting.InvokeOnUpdate(StartDiagnostics);
        }
        
        private async void StartDiagnostics()
        {
            Debug.Log("=== 网络连接诊断开始 ===");
            
            try
            {
                // 1. 检查配置文件
                DiagnoseConfig();
                
                // 2. 初始化网络管理器
                InitializeNetworkManager();
                
                // 3. 检查网络管理器状态
                DiagnoseNetworkManager();
                
                // 4. 尝试连接
                await AttemptConnection();
                
                // 5. 最终状态检查
                FinalStatusCheck();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"诊断过程中发生异常: {ex.Message}");
                Debug.LogError($"异常堆栈: {ex.StackTrace}");
            }
            
            Debug.Log("=== 网络连接诊断结束 ===");
        }
        
        private void DiagnoseConfig()
        {
            Debug.Log("--- 1. 配置文件诊断 ---");
            
            try
            {
                var config = NetworkConfigManager.LoadConfig();
                Debug.Log($"配置加载成功: AutoConnect={config.AutoConnect}, ReconnectInterval={config.ReconnectInterval}");
                Debug.Log($"网关数量: {config.GatewayList.Count}");
                
                foreach (var gateway in config.GatewayList)
                {
                    Debug.Log($"网关: {gateway.IP}:{gateway.Port} ({gateway.Region})");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"配置文件诊断失败: {ex.Message}");
            }
        }
        
        private void InitializeNetworkManager()
        {
            Debug.Log("--- 2. 网络管理器初始化 ---");
            
            try
            {
                var config = NetworkConfigManager.LoadConfig();
                var gatewayList = NetworkConfigManager.ConvertToGatewayInfo(config.GatewayList);
                
                _networkManager = new NetworkManager(gatewayList);
                
                // 订阅所有事件
                _networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
                _networkManager.ConnectionError += OnConnectionError;
               // _networkManager.MessageReceived += OnMessageReceived;
                
                Debug.Log("网络管理器初始化成功");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"网络管理器初始化失败: {ex.Message}");
            }
        }
        
        private void DiagnoseNetworkManager()
        {
            Debug.Log("--- 3. 网络管理器状态诊断 ---");
            
            if (_networkManager == null)
            {
                Debug.LogError("网络管理器未初始化");
                return;
            }
            
            try
            {
                var status = _networkManager.GetConnectionStatus();
                Debug.Log($"当前连接状态: {status}");
                
                var gateway = _networkManager.GetCurrentGateway();
                if (gateway != null)
                {
                    Debug.Log($"当前网关: {gateway.IP}:{gateway.Port}");
                }
                else
                {
                    Debug.Log("当前没有设置网关");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"网络管理器状态诊断失败: {ex.Message}");
            }
        }
        
        private async Task AttemptConnection()
        {
            Debug.Log("--- 4. 连接尝试 ---");
            
            if (_networkManager == null)
            {
                Debug.LogError("网络管理器未初始化，无法尝试连接");
                return;
            }
            
            try
            {
                var config = NetworkConfigManager.LoadConfig();
                if (config.GatewayList.Count > 0)
                {
                    var firstGateway = config.GatewayList[0];
                    Debug.Log($"尝试连接到网关: {firstGateway.IP}:{firstGateway.Port}");
                    
                    // 记录开始时间
                    var startTime = Time.TimeSinceStartup;
                    
                    var result = await _networkManager.ConnectAsync(firstGateway.IP, firstGateway.Port);
                    
                    var endTime = Time.TimeSinceStartup;
                    var duration = endTime - startTime;
                    
                    Debug.Log($"连接方法返回: {result}, 耗时: {duration:F2}秒");
                    
                    // 等待一小段时间查看异步连接结果
                    await Task.Delay(1000);
                    
                    var status = _networkManager.GetConnectionStatus();
                    Debug.Log($"连接后状态: {status}");
                }
                else
                {
                    Debug.LogWarning("没有配置网关");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"连接尝试失败: {ex.Message}");
                Debug.LogError($"异常堆栈: {ex.StackTrace}");
            }
        }
        
        private void FinalStatusCheck()
        {
            Debug.Log("--- 5. 最终状态检查 ---");
            
            if (_networkManager == null)
            {
                Debug.LogError("网络管理器未初始化");
                return;
            }
            
            try
            {
                var status = _networkManager.GetConnectionStatus();
                Debug.Log($"最终连接状态: {status}");
                
                var gateway = _networkManager.GetCurrentGateway();
                if (gateway != null)
                {
                    Debug.Log($"最终网关信息: {gateway.IP}:{gateway.Port}");
                }
                else
                {
                    Debug.Log("最终没有网关信息");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"最终状态检查失败: {ex.Message}");
            }
        }
        
        private void OnConnectionStatusChanged(ConnectionStatus status)
        {
            Debug.Log($"[事件] 连接状态变化: {status}");
        }
        
        private void OnConnectionError(string error)
        {
            Debug.LogError($"[事件] 连接错误: {error}");
        }
        
        private void OnMessageReceived(Horizon.Game.Message.Network.HorizonMessagePacket message)
        {
            Debug.Log($"[事件] 收到消息: {message.Header.MessageId}");
        }
    }
}

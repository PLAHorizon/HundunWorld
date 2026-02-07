using FlaxEngine;
using Game.Performance;
using HundunWorld.Game.ECS;
using HundunWorld.Game.Modules;
using HundunWorld.Game.Network;
using HundunWorld.Game.Worlds;
using System;
using System.Threading.Tasks;
using Arch.Core;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using TouchSocket.Core;

namespace HundunWorld.Game
{
    /// <summary>
    /// 游戏主类
    /// </summary>
    public class HundunWorldGame
    {
        private bool _isRunning = false;
        private readonly ECSManager _ecsManager;
        private readonly ModuleManager _moduleManager;
        private readonly WorldManager _worldManager;
        private readonly PlayerPositionUpdater _playerPositionUpdater;
        private readonly EventBroadcaster _eventBroadcaster;
        private readonly WorldDataManager _worldDataManager;
        private readonly World _archWorld;
        private NetworkManager _networkManager;
        private ulong _playerId = 0;
        private static HundunWorldGame _instance;
        public static HundunWorldGame Instance
        {
            get
            {
                if (_instance == null)
                {
                    Debug.Log("HundunWorldGame 没有初始化，正在手动初始化！");
                    _instance = new HundunWorldGame();
                    _instance.StartAsync().ConfigureFalseAwait();
                }
                return _instance;
            }
        }
        public HundunWorldGame()
        {
            Debug.Log("HundunWorldGame 构造函数开始");

            // 初始化网络管理器
            InitializeNetworkManager();

            {
                // 初始化Arch ECS世界
                _archWorld = World.Create();

                // 初始化各个系统组件
                _ecsManager = new ECSManager();
                _moduleManager = new ModuleManager();
                _worldManager = new WorldManager(_networkManager, _archWorld);
                _playerPositionUpdater = new PlayerPositionUpdater(_networkManager, _worldManager, _archWorld);
                _eventBroadcaster = new EventBroadcaster(_networkManager);
                _worldDataManager = new WorldDataManager("Data/World");
            }
            Engine.RequestingExit += () =>
            {
                Dispose();
            };
            Debug.Log("HundunWorldGame 构造函数完成");
        }

        /// <summary>
        /// 初始化网络管理器
        /// </summary>
        private void InitializeNetworkManager()
        {
            Debug.Log("初始化网络管理器开始");

            try
            {
                // 从配置文件加载网关列表
                var config = NetworkConfigManager.LoadConfig();
                var gatewayList = NetworkConfigManager.ConvertToGatewayInfo(config.GatewayList);

                // 创建网络管理器
                _networkManager = new NetworkManager(gatewayList);

                // 订阅网络事件
                _networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
               //_networkManager.MessageReceived += OnMessageReceived;
                _networkManager.ConnectionError += OnConnectionError;

                Debug.Log("网络管理器初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化网络管理器时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 连接状态变化事件处理
        /// </summary>
        private void OnConnectionStatusChanged(ConnectionStatus status)
        {
            Debug.Log($"网络连接状态变化: {status}");
        }

        /// <summary>
        /// 消息接收事件处理
        /// </summary>
        private void OnMessageReceived(HorizonMessagePacket message)
        {
            // 处理接收到的消息
            Debug.Log($"收到消息: {message.Header.MessageId}");
        }

        /// <summary>
        /// 连接错误事件处理
        /// </summary>
        private void OnConnectionError(string error)
        {
            Debug.LogError($"网络连接错误: {error}");
        }

        /// <summary>
        /// 启动游戏
        /// </summary>
        public async Task StartAsync()
        {
            Debug.Log("启动游戏开始");

            if (_isRunning)
                return;

            // 清除旧的缓存数据以解决序列化版本兼容问题
            try
            {
                Debug.Log("正在清除旧缓存数据...");
                Horizon.Game.Core.Database.DatabaseManager.ClearAllCacheData();
                Debug.Log("旧缓存数据已清除");
            }
            catch (Exception ex)
            {
                Debug.LogError($"清除缓存数据失败: {ex.Message}");
            }

            // 启动ECS系统
            _ecsManager.Start();

            // 启动世界同步
            _worldManager.StartSynchronization();

            // 启动游戏循环
            _isRunning = true;

            // 在后台运行游戏主循环
            _ = Task.Run(async () =>
            {
                await GameLoopAsync().ConfigureAwait(false);
            });
            if (_instance == null)
                _instance = this;
            Debug.Log("游戏启动完成");
        }

        /// <summary>
        /// 停止游戏
        /// </summary>
        public async Task StopAsync()
        {
            Debug.Log("停止游戏开始");

            if (!_isRunning)
                return;

            _isRunning = false;

            // 停止世界同步
            _worldManager.StopSynchronization();

            // 停止ECS系统
            _ecsManager.Stop();

            // 断开网关连接
            _networkManager?.Dispose();

            await Task.CompletedTask;

            Debug.Log("游戏停止完成");
        }

        /// <summary>
        /// 游戏主循环
        /// </summary>
        private async Task GameLoopAsync()
        {
            Debug.Log("游戏主循环开始");

            const int targetFPS = 60;
            const int frameDelay = 1000 / targetFPS;

            DateTime lastFrameTime = DateTime.UtcNow;

            while (_isRunning)
            {
                DateTime currentFrameTime = DateTime.UtcNow;
                float deltaTime = (float)(currentFrameTime - lastFrameTime).TotalSeconds;
                lastFrameTime = currentFrameTime;

                try
                {
                    // 更新ECS系统
                    _ecsManager.Update(deltaTime);

                    // 更新模块
                    // 这里应该更新所有已加载的模块

                    // 保持帧率
                    int elapsedMs = (int)(DateTime.UtcNow - currentFrameTime).TotalMilliseconds;
                    int sleepTime = frameDelay - elapsedMs;

                    if (sleepTime > 0)
                    {
                        await Task.Delay(sleepTime);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"游戏循环异常: {ex.Message}");
                }
            }

            Debug.Log("游戏主循环结束");
        }

        /// <summary>
        /// 设置玩家ID
        /// </summary>
        /// <param name="playerId">玩家ID</param>
        public void SetPlayerId(ulong playerId)
        {
            _playerId = playerId;
            _playerPositionUpdater.SetPlayerId(playerId);
        }

        /// <summary>
        /// 加载游戏模块
        /// </summary>
        /// <param name="modulePath">模块文件路径</param>
        public bool LoadModule(string modulePath)
        {
            return _moduleManager.LoadModule(modulePath);
        }

        /// <summary>
        /// 卸载游戏模块
        /// </summary>
        /// <param name="moduleName">模块名称</param>
        public bool UnloadModule(string moduleName)
        {
            return _moduleManager.UnloadModule(moduleName);
        }

        /// <summary>
        /// 获取模块管理器
        /// </summary>
        public ModuleManager ModuleManager => _moduleManager;

        /// <summary>
        /// 获取ECS管理器
        /// </summary>
        public ECSManager ECSManager => _ecsManager;

        /// <summary>
        /// 获取Arch ECS世界
        /// </summary>
        public World ArchWorld => _archWorld;

        /// <summary>
        /// 获取网络管理器
        /// </summary>
        public NetworkManager NetworkManager => _networkManager;

        /// <summary>
        /// 获取世界管理器
        /// </summary>
        public WorldManager WorldManager => _worldManager;

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            Debug.Log("HundunWorldGame 开始释放资源");
            try
            {
                // 停止游戏
                _ = StopAsync();

                // 释放各个系统组件
                _eventBroadcaster?.Dispose();
                _playerPositionUpdater?.Dispose();
                _worldManager?.Dispose();
                _moduleManager?.DisposeAllModules();
                _ecsManager?.Dispose();
                _networkManager?.Dispose();
                _worldDataManager?.Dispose();

                // 销毁Arch ECS世界
                if (_archWorld != null)
                {
                    World.Destroy(_archWorld);
                }

            }
            catch (Exception ex)
            {
                Debug.LogError($"性能报告异常: {ex.Message}");
            }
            finally
            {
                _instance = null;
            }

            Debug.Log("HundunWorldGame 资源释放完成");
        }
    }
}

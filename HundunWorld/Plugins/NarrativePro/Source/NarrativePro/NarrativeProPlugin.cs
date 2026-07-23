using System;
using System.Reflection;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales;

namespace NarrativePro
{
    public class NarrativeProPlugin : GamePlugin
    {
        private static NarrativeProPlugin _instance;

        public static NarrativeProPlugin Instance => _instance;

        public NarrativeSettings NarrativeSettings { get; private set; }

        public TalesComponent ActiveTalesComponent { get; set; }

        /// <summary>
        /// NarrativeProNetworkAdapter 实例引用（通过反射获取）
        /// </summary>
        private object _networkAdapterRef;

        /// <summary>
        /// NarrativeSyncManager 单例实例（延迟创建，当 ActiveTalesComponent 可用时自动初始化）
        /// </summary>
        private NarrativePro.Network.NarrativeSyncManager _syncManager;

        /// <summary>
        /// 获取当前 NarrativeSyncManager 实例（可能为 null，当 ActiveTalesComponent 未设置时）
        /// </summary>
        public NarrativePro.Network.NarrativeSyncManager SyncManager => GetNarrativeSyncManager();

        public NarrativeProPlugin()
        {
            _description = new PluginDescription
            {
                Name = "NarrativePro",
                Category = "Gameplay",
                Author = "成阳",
                Description = "叙事系统插件 - 任务/对话/事件/存档",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        public override void Initialize()
        {
            _instance = this;
            NarrativeSettings = new NarrativeSettings();
            Debug.Log("[NarrativePro] 叙事系统插件初始化完成");
            base.Initialize();

            // 延迟接线，等待 NetworkManager 初始化完成
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                TryWireNetworkAdapter();
            });
        }

        /// <summary>
        /// 通过反射桥接 NarrativeProNetworkAdapter（Game 模块）与本插件的 NarrativeSyncManager
        /// </summary>
        private void TryWireNetworkAdapter()
        {
            try
            {
                // 1. 通过反射获取 HundunWorldGame.Instance.NetworkManager
                var gameType = Type.GetType("HundunWorld.Game.HundunWorldGame, Game.CSharp");
                if (gameType == null)
                {
                    Debug.LogWarning("[NarrativePro] HundunWorldGame 类型未找到，网络接线跳过");
                    return;
                }

                var instanceProp = gameType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                var gameInstance = instanceProp?.GetValue(null);
                if (gameInstance == null) return;

                var nmProp = gameType.GetProperty("NetworkManager", BindingFlags.Instance | BindingFlags.Public);
                var networkManager = nmProp?.GetValue(gameInstance);
                if (networkManager == null) return;

                // 2. 通过反射获取 NarrativeProNetworkAdapter 类型并查询实例
                var adapterType = Type.GetType("HundunWorld.Game.Network.Adapters.NarrativeProNetworkAdapter, Game.CSharp");
                if (adapterType == null)
                {
                    Debug.LogWarning("[NarrativePro] NarrativeProNetworkAdapter 类型未找到");
                    return;
                }

                var getHandlerMethod = networkManager.GetType().GetMethod("GetHandler", new[] { typeof(Type) });
                if (getHandlerMethod == null)
                {
                    Debug.LogWarning("[NarrativePro] NetworkManager.GetHandler(Type) 方法未找到");
                    return;
                }

                _networkAdapterRef = getHandlerMethod.Invoke(networkManager, new object[] { adapterType });
                if (_networkAdapterRef == null)
                {
                    Debug.LogWarning("[NarrativePro] NarrativeProNetworkAdapter 实例未注册，接线跳过");
                    return;
                }

                // 3. 获取 SendNarrativeMessageAsync 和 IsConnected 用于 SendCallback
                var sendMethod = adapterType.GetMethod("SendNarrativeMessageAsync");
                var isConnectedProp = adapterType.GetProperty("IsConnected");

                // 4. 设置 _onMessageReceived 回调，指向 NarrativeSyncManager.OnNarrativeMessageReceived
                var setCallbackMethod = adapterType.GetMethod("SetMessageReceivedCallback");
                if (setCallbackMethod != null)
                {
                    // 创建一个 Action<string> 委托，调用时转发给 NarrativeSyncManager
                    Action<string> onMessage = (json) =>
                    {
                        // 延迟获取 NarrativeSyncManager，因为它可能尚未创建
                        var syncManager = GetNarrativeSyncManager();
                        syncManager?.OnNarrativeMessageReceived(json);
                    };
                    setCallbackMethod.Invoke(_networkAdapterRef, new object[] { onMessage });
                }

                Debug.Log("[NarrativePro] 网络适配器接线完成");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativePro] 网络适配器接线失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前 NarrativeSyncManager 实例（延迟创建，当 ActiveTalesComponent 可用时自动初始化）
        /// </summary>
        private NarrativePro.Network.NarrativeSyncManager GetNarrativeSyncManager()
        {
            if (ActiveTalesComponent == null) return null;

            // 延迟初始化：当 TalesComponent 可用时创建 SyncManager 并接线
            if (_syncManager == null)
            {
                _syncManager = new NarrativePro.Network.NarrativeSyncManager(ActiveTalesComponent);

                // 接入网络发送回调
                var sendCb = CreateSendCallback();
                if (sendCb != null)
                {
                    _syncManager.SendCallback = sendCb;
                }

                // 接入网络连接状态检查
                var connectedCb = CreateIsConnectedCallback();
                if (connectedCb != null)
                {
                    _syncManager.IsConnectedCallback = connectedCb;
                }

                Debug.Log("[NarrativePro] NarrativeSyncManager 实例化完成并已接入网络回调");
            }

            return _syncManager;
        }

        /// <summary>
        /// 获取网络发送委托（供 NarrativeSyncManager.SendCallback 使用）
        /// </summary>
        public Func<string, int, System.Threading.Tasks.Task<bool>> CreateSendCallback()
        {
            if (_networkAdapterRef == null) return null;

            var adapterType = _networkAdapterRef.GetType();
            var sendMethod = adapterType.GetMethod("SendNarrativeMessageAsync");
            var isConnectedProp = adapterType.GetProperty("IsConnected");

            return async (json, updateType) =>
            {
                if (_networkAdapterRef == null) return false;
                var task = sendMethod.Invoke(_networkAdapterRef, new object[] { json, updateType });
                return await (System.Threading.Tasks.Task<bool>)task;
            };
        }

        /// <summary>
        /// 获取网络连接状态检查委托
        /// </summary>
        public Func<bool> CreateIsConnectedCallback()
        {
            if (_networkAdapterRef == null) return null;

            var isConnectedProp = _networkAdapterRef.GetType().GetProperty("IsConnected");
            return () =>
            {
                if (_networkAdapterRef == null) return false;
                return (bool)isConnectedProp.GetValue(_networkAdapterRef);
            };
        }

        public override void Deinitialize()
        {
            _syncManager = null;
            _networkAdapterRef = null;
            _instance = null;
            Debug.Log("[NarrativePro] 叙事系统插件已卸载");
            base.Deinitialize();
        }
    }
}

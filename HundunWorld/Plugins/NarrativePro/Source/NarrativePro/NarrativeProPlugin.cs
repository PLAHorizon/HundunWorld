using System;
using System.Reflection;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.SaveSystem;
using NarrativePro.SaveSystem.Subsystems;
using NarrativePro.Tales;

namespace NarrativePro
{
    public class NarrativeProPlugin : GamePlugin
    {
        private static NarrativeProPlugin _instance;

        public static NarrativeProPlugin Instance => _instance;

        public NarrativeSettings NarrativeSettings { get; private set; }

        /// <summary>
        /// 当前激活的叙事组件（由 <see cref="SetActiveTalesComponent"/> 登记，供网络同步与全局查询使用）。
        /// 对应 UE5 中 GameMode/GameInstance 持有的活跃 Narrative 组件。
        /// </summary>
        public TalesComponent ActiveTalesComponent { get; private set; }

        /// <summary>
        /// 登记当前激活的叙事组件（TalesComponent.OnEnable 调用）。
        /// 重复登记同一组件不重建同步管理器；切换组件时把已有的 NarrativeSyncManager 指向新组件，
        /// 以免待发送的更新仍作用在已禁用的旧组件上。
        /// </summary>
        /// <param name="component">要设为激活的叙事组件；null 视为清除。</param>
        public void SetActiveTalesComponent(TalesComponent component)
        {
            if (component == null)
            {
                ClearActiveTalesComponent(null);
                return;
            }

            if (ReferenceEquals(ActiveTalesComponent, component)) return;

            ActiveTalesComponent = component;
            _syncManager?.AttachTalesComponent(component);
        }

        /// <summary>
        /// 注销激活的叙事组件（TalesComponent.OnDisable 调用）。
        /// 仅当传入的正是当前激活组件时才清除，避免后启用的组件被先禁用的组件误覆盖（UE5 注册表同理）。
        /// </summary>
        /// <param name="component">被禁用的叙事组件。</param>
        public void ClearActiveTalesComponent(TalesComponent component)
        {
            if (component != null && !ReferenceEquals(ActiveTalesComponent, component)) return;

            ActiveTalesComponent = null;
            _syncManager?.AttachTalesComponent(null);
        }

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
            // 数据侧预热：任务/对话/物品/foley/设置的 JSON 路径解析与注册表预热（缺数据时大声报错）
            NarrativePro.Data.NarrativeDataLoader.InitializeAll();
            Debug.Log("[NarrativePro] 叙事系统插件初始化完成");
            base.Initialize();

            // 延迟接线，等待 NetworkManager 初始化完成。
            // 真正的重试在 NarrativeRuntimePump 里（NarrativeRuntimeBootstrap.TickNetworkAdapterRetry，
            // 1 秒一次 × 120 次）：场景脚本创建 NetworkManager 一定晚于插件 Initialize，单次抢跑必然落空。
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                TryWireNetworkAdapter();
            });

            // 存档系统生命周期接线（对应 UE5 ANarrativeGameMode::InitGame → InitializeSaveSystem）
            NarrativeSaveBootstrap.Install();

            // 运行期装配接线（对应 UE5 类默认子对象 + GameMode 生成 HUD/控制器）：
            // 创建 NarrativeCharacterSubsystem / NarrativeCommonUISubsystem / NarrativeCommonHUD /
            // NarrativeGameplayHUD / 对话·任务面板，注册 UI 层，并给本地玩家角色补挂 PlayerController。
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.Install();
        }

        /// <summary>
        /// 打开角色创建器（对游戏侧的公开入口，文档口径：调用本方法即“可达入口”，
        /// 内部转发到 <c>NarrativeRuntimeBootstrap.OpenCreator</c>，后者按需创建
        /// <c>CharacterCreatorPlayerController</c> 会话与真实选项控件容器）。
        /// </summary>
        /// <param name="configJsonPath">创建器配置 JSON；为空时在 Content/NarrativePro/CharacterCreator 下搜索。</param>
        /// <param name="formTag">表单标签；无效时取 CharacterCreator_Form_Male。</param>
        public static bool OpenCreator(string configJsonPath = null, NarrativePro.Items.GameplayTag formTag = default)
        {
            return NarrativePro.CommonUI.NarrativeRuntimeBootstrap.OpenCreator(configJsonPath, formTag);
        }

        /// <summary>关闭角色创建器并回收其 UI 容器。</summary>
        public static void CloseCreator()
        {
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.CloseCreator();
        }

        /// <summary>
        /// 在指定 UI 层打开菜单（对应 UE5 UNarrativeGameplayHUD::OpenMenu）。
        /// 层容器由运行期引导注册，因此本方法在场景装配完成后即可用。
        /// </summary>
        public static NarrativePro.Widgets.NarrativeMenu OpenMenu(string menuClassName, NarrativePro.Items.GameplayTag layerTag = default)
        {
            return NarrativePro.CommonUI.NarrativeRuntimeBootstrap.OpenMenu(menuClassName, layerTag);
        }

        /// <summary>显示一条 HUD 小提示（对应 UE5 ShowNotification）。</summary>
        public static void ShowNotification(string text, float duration = 5f)
        {
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.ShowNotification(text, duration);
        }

        /// <summary>
        /// 供游戏侧（GameMode/主菜单）在启动流程中显式初始化存档系统。
        /// 对应 UE5 NarrativeGameMode::InitGame 中传入的 OptionsString，
        /// 形如 "LevelName?SaveGameName=Slot&LevelTransition=1"。
        /// </summary>
        public static void InitializeSaveSystem(string optionString = null)
        {
            if (!string.IsNullOrEmpty(optionString))
            {
                NarrativeSaveBootstrap.SetOptionString(optionString);
            }

            NarrativeSaveBootstrap.Install();

            var subsystem = NarrativeSaveBootstrap.Subsystem ?? NarrativeSaveSubsystem.Instance;
            if (subsystem != null && !string.IsNullOrEmpty(optionString))
            {
                subsystem.SetOptionString(optionString);
            }
        }

        /// <summary>当前存档子系统实例（可能为空，场景未初始化时为 null）。</summary>
        public NarrativeSaveSubsystem SaveSubsystem => NarrativeSaveSubsystem.Instance;

        /// <summary>立即保存当前世界（主菜单“保存游戏”按钮可直接调用）。</summary>
        public static bool SaveGame(string saveName = null, int slot = 0)
        {
            return NarrativeSaveBootstrap.SaveCurrentGame(saveName, slot);
        }

        /// <summary>立即读取存档（主菜单“读取游戏”按钮可直接调用）。</summary>
        public static bool LoadGame(string saveName = null, int slot = 0)
        {
            return NarrativeSaveBootstrap.LoadCurrentGame(saveName, slot);
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
            // 回收运行期装配（HUD/面板/宿主 Actor）
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.Uninstall();

            // 卸载存档系统钩子（内部会先把当前世界状态落盘）
            NarrativeSaveBootstrap.Uninstall();

            _syncManager = null;
            _networkAdapterRef = null;
            _instance = null;
            Debug.Log("[NarrativePro] 叙事系统插件已卸载");
            base.Deinitialize();
        }
    }
}

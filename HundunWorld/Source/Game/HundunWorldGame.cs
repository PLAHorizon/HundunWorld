using FlaxEngine;
using Game.Performance;
using HundunWorld.Game.ECS;
using HundunWorld.Game.Modules;
using HundunWorld.Game.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.Character;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.Worlds;
using System;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Sync;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.Message.Sync.Components;
using TouchSocket.Core;
using Game.Character.Attributes;
using Game.Database;
using System.Threading;
using System.Collections.Generic;

namespace HundunWorld.Game
{
    /// <summary>
    /// 游戏主类
    /// </summary>
    public class HundunWorldGame
    {
        private bool _isRunning = false;
        private bool _disposed = false;
        private readonly ECSManager _ecsManager;
        private readonly ArchWorldHost _archWorldHost;
        private readonly ModuleManager _moduleManager;
        private readonly WorldManager _worldManager;
        private readonly PlayerPositionUpdater _playerPositionUpdater;
        private readonly EventBroadcaster _eventBroadcaster;
        private readonly WorldDataManager _worldDataManager;
        private readonly World _archWorld;
        private NetworkManager _networkManager;
        private ClientConnectionCoordinator _connectionCoordinator;

        // ─── 上行发送资格管控（SyncGuard，spec 5.4.1 规则集中性） ───
        private Horizon.Game.ECS.Arch.SyncGuard.Contracts.IBindingRelationshipRegistry _bindingRegistry;
        private HundunWorld.Game.SyncGuard.LocalSendEligibilityState _eligibilityState;
        private HundunWorld.Game.SyncGuard.SendViolationMonitor _violationMonitor;
        private HundunWorld.Game.SyncGuard.OutboundSyncAuthorizer _outboundSyncAuthorizer;
        private HundunWorld.Game.SyncGuard.GuardSyncSender _guardSyncSender;

        /// <summary>本地玩家的翻越驱动（GASP Traversal → 服务端权威）。裁决回执从同步包转发到它。</summary>
        private HundunWorld.Game.Gasp.GaspTraversalDriver _localTraversalDriver;

        // ─── 远程角色观测链路修复（RemoteVisibility，spec 5.6） ───
        private HundunWorld.Game.RemoteVisibility.ReconnectResumeSender _reconnectResumeSender;
        private HundunWorld.Game.RemoteVisibility.ReconnectResumeStateMachine _reconnectResumeStateMachine;
        private HundunWorld.Game.RemoteVisibility.RemoteVisibilityAudit _remoteVisibilityAudit;
        private HundunWorld.Game.RemoteVisibility.RemoteObservationCoordinator _remoteObservationCoordinator;
        private HundunWorld.Game.RemoteVisibility.ReconnectFlowGuard _reconnectFlowGuard;
        private bool _remoteVisibilityInitialized;
        // 使用 Volatile.Read/Write 保证跨线程可见性（ulong 不能使用 volatile 关键字）。
        // SetPlayerId 在 UI 线程（Scripting.InvokeOnUpdate）与网络线程（OnHandshakeReceived 事件）被并发写入，
        // PlayerId 属性可能在其他线程读取。
        private ulong _playerId = 0;

        // 客户端 AOI 动态订阅状态：记录本地玩家当前已订阅的 chunk MortonKey 集合。
        // 由 LocalSimulationSystem.PlayerChunkChanged 事件驱动，跨 chunk 边界时计算 added/removed
        // 并通过 NetworkManager.SendSubscriptionUpdateAsync 上行到服务端。
        // 之前仅服务端在握手时订阅单个 chunk（16m），玩家移动出该 chunk 后即收不到任何 delta，
        // 导致远程角色“静止不动”。现改为客户端动态订阅 11x11x11 视野范围（176m×176m×176m）。
        private HashSet<ulong>? _localPlayerSubscribedChunks = null;
        // 标记是否已订阅 PlayerChunkChanged 事件，避免 SetPlayerId 多次调用时重复订阅。
        private bool _isPlayerChunkChangedSubscribed = false;
        
        // AOI 订阅滞后防抖：记录上次订阅中心的 chunk 坐标。
        // 只有当玩家移动超过 HysteresisChunks 个 chunk 时才重新计算订阅，
        // 避免每跨 16m chunk 边界就触发 185193 chunk（radius=28）的 diff 计算和网络上行。
        private int _lastSubCenterCX = int.MinValue;
        private int _lastSubCenterCY = int.MinValue;
        private int _lastSubCenterCZ = int.MinValue;
        /// <summary>滞后阈值（chunk 数）：玩家移动超过此距离才重新订阅。radius=28 时，8 chunks=128m 移动才触发一次更新（与 radius 成比例放大，避免大范围订阅频繁重算）。</summary>
        private const int AoiHysteresisChunks = 8;

        /// <summary>
        /// 本地玩家角色ID（由握手响应或登录响应设置）
        /// </summary>
        public ulong PlayerId => Volatile.Read(ref _playerId);
        private Actor _localPlayerActor;
        // 待创建的本地玩家 Actor 请求（场景切换到 GameWorld 完成后执行创建）
        private bool _hasPendingLocalPlayerActorRequest;
        private float _pendingRequestTime;
        private ulong _pendingLocalPlayerCharacterId;
        private float _pendingLocalPlayerX, _pendingLocalPlayerY, _pendingLocalPlayerZ;

        // 待回填的本地玩家属性（来自 EnterGameResponse.CharacterInfo）。
        // RequestCreateLocalPlayerActor 可能为异步（等待场景切换），LocalPlayerActor 此时未创建，
        // 故需先缓存属性，待 CreateLocalPlayerActor 完成后再回填到 CharacterAttributesComponent。
        private bool _hasPendingLocalPlayerAttributes;
        private string _pendingLocalPlayerNickname;
        private int _pendingLocalPlayerLevel;
        private CharacterStage _pendingLocalPlayerStage;
        private static HundunWorldGame _instance;
        private System.Action _requestingExitHandler;
        /// <summary>同步包 handler 订阅后台重试循环的取消令牌（Dispose 时取消，防止 PIE 退出后循环残留）。</summary>
        private System.Threading.CancellationTokenSource _syncSubscribeCts;
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

        /// <summary>
        /// 安全释放游戏实例：仅当实例已创建时执行 Dispose。
        /// 供 PIE 退出/场景清理等外部路径调用，避免通过 <see cref="Instance"/> getter
        /// 触发延迟创建导致"清理路径反而复活游戏实例"（PIE 退出后残留网络任务）。
        /// Dispose 内部幂等，重复调用安全。
        /// </summary>
        public static void TryDisposeIfCreated()
        {
            var instance = _instance;
            if (instance != null)
            {
                Debug.Log("HundunWorldGame.TryDisposeIfCreated: 释放已创建的游戏实例");
                instance.Dispose();
            }
        }
        public HundunWorldGame()
        {
            Debug.Log("HundunWorldGame 构造函数开始");

            try
            {
                // 初始化网络管理器
                InitializeNetworkManager();
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化网络管理器失败（构造函数级别）: {ex.Message}\n{ex.StackTrace}");
                // 网络初始化失败不应阻止游戏启动，用户仍可进入离线模式
            }

            try
            {
                // 初始化Arch ECS世界
                _archWorld = World.Create();

                // 初始化 ArchWorldHost 并注册同步系统（SnapshotApplySystem、InterpolationSystem 等）
                _archWorldHost = new ArchWorldHost(_archWorld);
                var archAssembly = typeof(Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem).Assembly;
                Debug.Log($"[HundunWorldGame] Arch ECS 程序集: {archAssembly.FullName}, Location: {archAssembly.Location}");
                var registeredSystems = SystemRegistry.RegisterFromAssembly(_archWorldHost, archAssembly);
                Debug.Log($"[HundunWorldGame] 已注册 {registeredSystems.Count} 个 Arch ECS 同步系统");

                // 初始化各个系统组件
                _ecsManager = new ECSManager();
                _moduleManager = new ModuleManager();
                _worldManager = new WorldManager(_networkManager, _archWorld);
                _playerPositionUpdater = new PlayerPositionUpdater(_networkManager, _worldManager, _archWorld);
                _eventBroadcaster = new EventBroadcaster(_networkManager);
                _worldDataManager = new WorldDataManager("Data/World");
            }
            catch (System.Reflection.ReflectionTypeLoadException rtle)
            {
                // 详细记录类型加载失败信息
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"初始化 ECS/世界系统失败 (ReflectionTypeLoadException): {rtle.Message}");
                if (rtle.LoaderExceptions != null)
                {
                    sb.AppendLine($"LoaderExceptions 数量: {rtle.LoaderExceptions.Length}");
                    for (int i = 0; i < rtle.LoaderExceptions.Length; i++)
                    {
                        var le = rtle.LoaderExceptions[i];
                        sb.AppendLine($"  [{i}] {le?.GetType().Name}: {le?.Message}");
                    }
                }
                Debug.LogError(sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化 ECS/世界系统失败: {ex.Message}\n{ex.StackTrace}");
                // ECS 初始化失败是严重问题，但不应导致闪退，至少让游戏窗口显示出来
            }

            // ─── 装配上行发送资格管控组件（SyncGuard）：需在 ECS 世界与系统注册完成后执行 ───
            InitializeSyncGuard();

            // ─── 装配远程角色观测链路修复组件（RemoteVisibility） ───
            InitializeRemoteVisibility();

            _requestingExitHandler = () =>
            {
                Dispose();
            };
            Engine.RequestingExit += _requestingExitHandler;
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

                var iniConfig = HorizonGameIniReader.TryRead();
                if (iniConfig != null && iniConfig.GameGateway != null
                    && !string.IsNullOrWhiteSpace(iniConfig.GameGateway.Host)
                    && iniConfig.GameGateway.Port > 0)
                {
                    Debug.Log($"[HundunWorldGame] 使用 HorizonGame.ini 中的网关地址: {iniConfig.GameGateway.Host}:{iniConfig.GameGateway.Port}");
                    gatewayList.Insert(0, new GatewayInfo
                    {
                        IP = iniConfig.GameGateway.Host,
                        Port = iniConfig.GameGateway.Port,
                        Region = "HorizonGame"
                    });
                }

                _networkManager = new NetworkManager(gatewayList);

                // [连接精简治理 spec 5.1.1] 装配客户端单连接编排协调器：
                // 登录/进游戏/重连三类建连请求统一经此互斥编排，任意客户端进程任一时刻仅一条 TCP 连接在途。
                _connectionCoordinator = new ClientConnectionCoordinator(_networkManager);

                // 订阅网络事件
                _networkManager.ConnectionStatusChanged += OnConnectionStatusChanged;
               //_networkManager.MessageReceived += OnMessageReceived;
                _networkManager.ConnectionError += OnConnectionError;
                _networkManager.DisconnectTimedOut += OnDisconnectTimedOut;


                // 订阅同步包事件：将服务端快照桥接到 SnapshotReceiveBuffer
                // 这是同步链路的关键桥接：SyncPacketMessageHandler -> SnapshotReceiveBuffer -> SnapshotApplySystem
                // 注意：AddAllMessageHandlers 在 NetworkManager 构造函数中已被调用
                // 此处延迟获取 handler 并订阅事件
                SubscribeSyncHandlerEvents();

                Debug.Log("网络管理器初始化完成");

                // 说明：连接精简治理（spec 5.1）移除了此处的"启动预连接"行为。
                // 原行为：启动时后台 Task.Run 调用 ConnectAsync 建立"只建连不发首包"的连接，
                // 直接导致服务端首包超时幽灵连接（客户端需等待 EnterGame 流程才发首包）。
                // 治理后建连只允许在登录/进游戏等业务意图触发点发起（经 ClientConnectionCoordinator 编排），
                // 任意客户端进程任一时刻仅持有一条必要游戏连接。
            }
            catch (Exception ex)
            {
                Debug.LogError($"初始化网络管理器时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 装配上行发送资格管控组件（SyncGuard）并完成事件订阅（spec 5.4.1 规则集中性）。
        /// </summary>
        private void InitializeSyncGuard()
        {
            try
            {
                _bindingRegistry = new HundunWorld.Game.SyncGuard.BindingRelationshipRegistry();
                _eligibilityState = new HundunWorld.Game.SyncGuard.LocalSendEligibilityState(_bindingRegistry);
                _violationMonitor = new HundunWorld.Game.SyncGuard.SendViolationMonitor();
                _outboundSyncAuthorizer = new HundunWorld.Game.SyncGuard.OutboundSyncAuthorizer(
                    bindingRegistry: _bindingRegistry,
                    eligibilityState: _eligibilityState,
                    violationReporter: _violationMonitor,
                    isLocalPlayerEntity: IsLocalPlayerEntity,
                    getLocalPlayerEntityId: () => _playerId,
                    getLocalPlayerEntityCount: CountLocalPlayerEntities);
                // 统一使用同一个兜底守卫实例：GuardSyncSender 标记授权放行，NetworkManager 识别同一实例。
                var outboundGuard = new HundunWorld.Game.SyncGuard.OutboundSyncGuardImpl(_outboundSyncAuthorizer, _violationMonitor);
                _guardSyncSender = new HundunWorld.Game.SyncGuard.GuardSyncSender(
                    _outboundSyncAuthorizer, _violationMonitor,
                    syncFrame => _networkManager.SendAsync(syncFrame),
                    outboundGuard);

                // 注入 NetworkManager 兜底守卫：拦截绕过受控发送入口的旁路发送。
                if (_networkManager != null)
                {
                    _networkManager.OutboundSyncGuard = outboundGuard;
                }

                // 订阅连接/握手状态事件，驱动本地资格状态机迁移。
                if (_networkManager != null)
                {
                    _networkManager.ConnectedStateChanged += OnConnectedStateChanged;
                    _networkManager.SyncHandshakeStateChanged += OnSyncHandshakeStateChanged;
                }

                // 为 ECS 侧 SendAuthorizationSystem 注入集中式授权器。
                InjectAuthorizerToSendAuthorizationSystem();

                Debug.Log("[HundunWorldGame] SyncGuard 资格管控组件装配完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 装配 SyncGuard 失败: {ex.Message}");
            }
        }

        /// <summary>查询实体是否为本地玩家（供授权器 O(1) 判定）。</summary>
        private bool IsLocalPlayerEntity(ulong entityId)
        {
            if (entityId == 0 || _archWorld == null)
            {
                return false;
            }

            try
            {
                var query = new Arch.Core.QueryDescription()
                    .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent>();
                bool found = false;
                _archWorld.Query(in query, (Entity entity, ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent netId) =>
                {
                    if (netId.EntityId == entityId)
                    {
                        found = netId.IsLocalPlayer;
                    }
                });
                return found;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>统计场景内本地玩家实体数量（供唯一性校验）。</summary>
        private int CountLocalPlayerEntities()
        {
            if (_archWorld == null)
            {
                return 0;
            }

            int count = 0;
            try
            {
                var query = new Arch.Core.QueryDescription()
                    .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent>();
                _archWorld.Query(in query, (ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent netId) =>
                {
                    if (netId.IsLocalPlayer)
                    {
                        count++;
                    }
                });
            }
            catch
            {
                // 忽略查询异常
            }
            return count;
        }

        /// <summary>连接状态变化 → 驱动资格状态机。</summary>
        private void OnConnectedStateChanged(bool isConnected)
        {
            try
            {
                _eligibilityState?.OnConnectionChanged(isConnected);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 连接状态驱动资格状态机失败: {ex.Message}");
            }
        }

        /// <summary>握手状态变化 → 驱动资格状态机。</summary>
        private void OnSyncHandshakeStateChanged(bool isComplete)
        {
            try
            {
                _eligibilityState?.OnHandshakeChanged(isComplete);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 握手状态驱动资格状态机失败: {ex.Message}");
            }
        }

        /// <summary>为 ECS 侧 SendAuthorizationSystem 注入集中式授权器。</summary>
        private void InjectAuthorizerToSendAuthorizationSystem()
        {
            try
            {
                if (_archWorldHost == null)
                {
                    return;
                }

                var systems = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.NetworkSend);
                foreach (var system in systems)
                {
                    if (system is Horizon.Game.ECS.Arch.Systems.SendAuthorizationSystem authSystem)
                    {
                        authSystem.SetAuthorizer(_outboundSyncAuthorizer);
                        authSystem.SetLocalPlayerIdProvider(() => _playerId);
                        Debug.Log("[HundunWorldGame] 已为 SendAuthorizationSystem 注入 IOutboundSyncAuthorizer");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 注入 SendAuthorizationSystem 授权器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 连接状态变化事件处理
        /// </summary>
        /// <summary>跟踪是否处于重连流程中（区分首次连接和重连）。</summary>
        private bool _wasReconnecting;

        private void OnConnectionStatusChanged(ConnectionStatus status)
        {
            Debug.Log($"网络连接状态变化: {status}");

            // 重连中：不暂停插值和 Actor 同步。
            // 修复（远程角色同时不动+同时离线）：
            // 原实现在 Reconnecting 状态暂停 InterpolationSystem 和 FlaxActorSyncSystem，
            // 导致断线期间所有远程角色完全不动。即使快照队列中有积压的快照也无法处理。
            // 正确做法：不暂停任何系统。InterpolationSystem 继续用最后已知 Target 插值
            // （通常已到达目标，位置不变），FlaxActorSyncSystem 继续同步位置/朝向/动画。
            // 恢复连接后新快照更新 Target，无缝衔接。
            // 只有真正断线（30 秒无任何消息）时 HeartbeatTimeout 才触发，此时快照队列已空，
            // InterpolationSystem 自然保持当前位置（Target 已到达），无需暂停。
            if (status == ConnectionStatus.Reconnecting)
            {
                _wasReconnecting = true;
                // 不暂停任何系统，让插值和同步继续运行
            }

            // 重连成功：恢复插值系统，让服务端全量快照更新现有实体。
            //
            // 修复（远程角色闪退 — Actor 反复销毁重建）：
            // 原实现调用 ClearLocalEntitiesOnDisconnect() 销毁所有 Actor 后等待服务端重建，
            // 导致 Actor 销毁→重建的视觉闪退。该方案在连接频繁抖动时尤为严重。
            //
            // 正确做法：不清理现有 Actor/ECS 实体，仅恢复暂停的系统。
            // 服务端重连后发送的全量快照会更新已有实体的位置数据。
            // SnapshotApplySystem 处理已存在的 Spawn 为 Update（不销毁重建），
            // FlaxActorSyncSystem 处理已存在的 EntityId 跳过（不复用重建）。
            // 在断线期间被服务端 Despawn 的实体，通过 Despawn delta 或 StaleEntityTimeout 清理。
            if (status == ConnectionStatus.Connected && _wasReconnecting)
            {
                _wasReconnecting = false;
                Debug.Log("[HundunWorldGame] 重连成功，恢复同步（不清除 Actor，让全量快照更新现有实体）");

                // 修复（静置断线后无法移动 + 闪跳回原地）：
                // 重连成功后补发一次携带"当前预测位置"的完整同步握手。
                // 必要性：
                //   1) 服务端 resume 路径（HandleReconnectAsync）不调用 EnterWorldAsync，
                //      若实体已因空闲超时+Despawn 宽限被清理，AOI 订阅已移除，
                //      客户端重连后收不到任何快照 → 远程角色看不到移动。
                //      补发握手触发 EnterWorldAsync（重连分支）重建 AOI 订阅并强制全量快照。
                //   2) 服务端 EnterWorldAsync 重连分支现会以握手坐标对齐实体权威位置——
                //      必须携带客户端当前预测位置（而非首次进入的初始位置），
                //      否则服务端被拉回静置/初始坐标，客户端再次被快照拉回（闪跳）。
                _ = SendReconnectFullHandshakeWithLatestPositionAsync();

                // 恢复 FlaxActorSyncSystem
                var flaxSync = FlaxActorSyncSystem.Instance;
                if (flaxSync != null) flaxSync.IsPaused = false;

                // 恢复 InterpolationSystem
                if (_archWorldHost != null)
                {
                    var interpSys = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.Render)
                        .OfType<Horizon.Game.ECS.Arch.Systems.InterpolationSystem>()
                        .FirstOrDefault();
                    if (interpSys != null) interpSys.IsPaused = false;
                }
            }

            // 断线时不清除远程角色 Actor 和 ECS 实体，也不切换场景。
            //
            // 修复（远程角色闪退 — Actor 反复销毁重建）：
            // 原实现立即切换场景（CharacterSelection），导致 GameWorld 场景被卸载，
            // 所有远程 Actor 和 ECS 实体被销毁。当 ReconnectionManager 快速重连成功后，
            // 同步系统已不存在（场景已卸载），无法恢复，导致以下循环：
            //   1) 断线 → 场景切换 → Actor 销毁
            //   2) 重连成功 → 同步系统已不存，无法恢复
            //   3) 用户重新进入游戏 → 新 Actor 创建
            //   4) 再次断线 → 场景切换 → Actor 再次销毁
            //   每 ~35 秒循环一次，表现为"闪退"。
            //
            // 正确做法：断线时不切换场景，让 ReconnectionManager 在后台处理重连。
            // - 断线后等待 60 秒再拉起网关探查，避免网络抖动时频繁探查
            // - ReconnectionManager 在后台以指数退避策略重试（最多 10 次）
            // - 重连成功 → Reconnecting 状态已暂停的同步系统被恢复 → 游戏继续
            // - 重连失败（探查超过10次）→ Failed 状态触发场景切换 → 回到登录界面
            if (status == ConnectionStatus.Disconnected)
            {
                Debug.Log("[HundunWorldGame] 网关离线，ReconnectionManager 正在后台重连...");
                // 不切换场景，不销毁 Actor，让 ReconnectionManager 处理重连。
                // Reconnecting 状态会暂停同步系统，Connected 状态会恢复同步系统。
                // Failed 状态（探查超过10次）会触发场景切换到登录界面。
            }

            // 重连失败：探查超过 MaxReconnectAttempts 次仍连接不上，或断线超过阈值时间，
            // 退出回角色选择场景（而非登录场景），等待用户主动选择进入游戏时再按需拉起连接。
            if (status == ConnectionStatus.Failed)
            {
                _wasReconnecting = false;

                var gameState = HundunWorld.Game.Core.GameStateManager.Instance;
                // 暂停态（Esc 系统菜单盖着）也算"还在游戏世界里"：只看 IsInGame 会让断线兜底
                // 在菜单开着时静默跳过，玩家就被留在一个连不上服务器的菜单里。
                if (gameState != null && (gameState.IsInGame || gameState.IsPaused))
                {
                    Debug.Log("[HundunWorldGame] 网关离线超过阈值时间，退出游戏世界，返回角色选择界面");
                    Scripting.InvokeOnUpdate(() =>
                    {
                        var sceneManager = HundunWorld.Game.UI.GameSceneManager.Instance;
                        if (sceneManager != null)
                        {
                            sceneManager.TransitionTo(Horizon.Game.Message.Enums.SceneType.CharacterSelection);
                        }
                        gameState.ChangeState(HundunWorld.Game.Core.GameState.CharacterSelect);
                    });
                }
            }
        }

        /// <summary>
        /// 断线超时事件处理：网关离线超过阈值时间（60s），ReconnectionManager 停止所有自动重连后触发。
        /// 退回角色选择界面，等待用户主动选择进入游戏时再按需拉起连接。
        /// </summary>
        private void OnDisconnectTimedOut()
        {
            Debug.LogWarning("[HundunWorldGame] 断线超时，网关长时间不可达，退回角色选择界面");
            _wasReconnecting = false;

            Scripting.InvokeOnUpdate(() =>
            {
                var gameState = HundunWorld.Game.Core.GameStateManager.Instance;
                var sceneManager = HundunWorld.Game.UI.GameSceneManager.Instance;

                if (sceneManager != null)
                {
                    sceneManager.TransitionTo(Horizon.Game.Message.Enums.SceneType.CharacterSelection);
                }
                if (gameState != null)
                {
                    gameState.ChangeState(HundunWorld.Game.Core.GameState.CharacterSelect);
                }
            });
        }

        /// <summary>
        /// 断线时清理本地所有远程角色实体和 Actor。
        /// </summary>
        private void ClearLocalEntitiesOnDisconnect()
        {
            Debug.Log("[HundunWorldGame] 断线清理：开始清理本地远程角色实体和 Actor");

            // 1. 清理 FlaxActorSyncSystem 的所有远程 Actor
            var flaxActorSync = FlaxActorSyncSystem.Instance;
            if (flaxActorSync != null)
            {
                flaxActorSync.ClearAllActors();
            }

            // 2. 清理 SnapshotApplySystem 的实体映射
            if (_archWorldHost != null)
            {
                var snapshotSystem = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.NetworkReceive)
                    .OfType<Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem>()
                    .FirstOrDefault();
                snapshotSystem?.ClearAllEntityMappings();
            }

            // 2.5 销毁 Arch World 中所有远程实体（带 InterpolatedTransformComponent 的非本地玩家实体），
            // 避免孤儿实体在重连后被 ReconcileMissingActors 补创建为幽灵 Actor。
            if (_archWorld != null)
            {
                var remoteQuery = new Arch.Core.QueryDescription()
                    .WithAll<Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent, Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent>();
                var toDestroy = new List<Arch.Core.Entity>();
                _archWorld.Query(in remoteQuery, (Arch.Core.Entity e, ref Horizon.Game.ECS.Arch.Components.InterpolatedTransformComponent _, ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent nid) =>
                {
                    if (!nid.IsLocalPlayer)
                        toDestroy.Add(e);
                });
                foreach (var e in toDestroy)
                {
                    if (_archWorld.IsAlive(e))
                        _archWorld.Destroy(e);
                }
                if (toDestroy.Count > 0)
                    Debug.Log($"[HundunWorldGame] 断线清理：销毁了 {toDestroy.Count} 个远程 ECS 实体");
            }

            // 3. 清理快照接收缓冲区，避免断线期间积压的旧快照在重连后污染状态
            Horizon.Game.ECS.Arch.Network.SnapshotReceiveBuffer.Instance.ClearQueue();

            // 3.5 清空所有网络同步缓冲区，避免旧会话残留数据在重连后干扰新会话
            Horizon.Game.ECS.Arch.Network.InputHistoryBuffer.Instance.Clear();
            Horizon.Game.ECS.Arch.Network.InputSendQueue.Instance.ClearQueue();
            Horizon.Game.ECS.Arch.Network.EventReceiveBuffer.Instance.ClearQueue();
            Horizon.Game.ECS.Arch.Network.CorrectionReceiveBuffer.Instance.Clear();
            Horizon.Game.ECS.Arch.Network.InputAckReceiveBuffer.Instance.Clear();

            // 3.7 清空下行去重指纹环形缓冲区，避免跨会话误判（新会话的同指纹 diff 被误判为重复）
            Array.Clear(_chunkDiffDedupFingerprints, 0, _chunkDiffDedupFingerprints.Length);
            _chunkDiffDedupIndex = 0;

            // 3.6 重置 ReconciliationSystem 状态（风暴检测/冷却/统计），避免旧会话历史影响新会话
            if (_archWorldHost != null)
            {
                var reconciliationSys = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate)
                    .OfType<Horizon.Game.ECS.Arch.Systems.ReconciliationSystem>()
                    .FirstOrDefault();
                reconciliationSys?.ResetState();
            }

            // 4. 重置快照基线缓存，确保重连后首个全量快照重建 baseline
            // 阶段 6.4 起 ResetLastAppliedSnapshot 为实例方法（静态竞态修复）：取当前系统实例调用。
            GetSnapshotApplySystemInstance()?.ResetLastAppliedSnapshot();

            // 5. 重置 AOI 订阅状态，确保重连后重新全量订阅（而非被滞后防抖跳过）
            _localPlayerSubscribedChunks = null;
            _lastSubCenterCX = int.MinValue;
            _lastSubCenterCY = int.MinValue;
            _lastSubCenterCZ = int.MinValue;

            Debug.Log("[HundunWorldGame] 断线清理完成");
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
        /// 服务端快照包接收处理
        /// 将 SnapshotPacket 放入 SnapshotReceiveBuffer，供 SnapshotApplySystem 消费
        /// </summary>
        private void OnSnapshotReceived(SnapshotPacket snapshot)
        {
            SnapshotReceiveBuffer.Instance.Enqueue(snapshot);

            // [Phase C5] 更新最近已应用的服务器 Tick，供重连时 ReconnectResumePacket 使用
            _networkManager?.UpdateLastAppliedServerTick(snapshot.ServerTick);

            // [Phase C7] 网络线程热路径日志治理（DFX 4.1.2）：原实现对每个含 Deltas 的快照执行
            // Debug.Log（字符串插值 + 日志 I/O），多角色时快照 Deltas 增多，高频日志阻塞网络接收线程，
            // 引发心跳/输入延迟 → 同步"瘫痪"观感。改为限频输出（前 5 次 + 每 120 次），
            // 与 OnChunkDiffReceived 既有限频模式一致；其余路径仅保留零分配操作。
            if (snapshot.Deltas != null && snapshot.Deltas.Length > 0)
            {
                var recvCount = Interlocked.Increment(ref _snapshotRecvLogCount);
                if (recvCount <= 5 || recvCount % 120 == 1)
                {
                    Debug.Log($"[HundunWorldGame] 收到服务端快照: ServerTick={snapshot.ServerTick}, Deltas={snapshot.Deltas.Length}");
                }
            }
        }

        /// <summary>
        /// 输入确认包接收处理
        /// </summary>
        private void OnInputAckReceived(InputAckPacket inputAck)
        {
            // P-F4 双链路重复确认幂等防护：服务端直连响应 ACK 与广播安全网 ACK 可能对同一
            // ClientTick 各送达一次（客户端实测每个 tick 收到两条相同 ACK）。对重复 tick 的
            // 确认直接丢弃，避免重复写入 InputAckReceiveBuffer/重复推进 OnInputAck/重复日志。
            var ackTick = inputAck.LastProcessedClientTick;
            if (ackTick == _lastInputAckTick)
            {
                return;
            }
            _lastInputAckTick = ackTick;

            // 桥接到 ECS 缓冲区，供 ReconciliationSystem 消费
            Horizon.Game.ECS.Arch.Network.InputAckReceiveBuffer.Instance.Latest = inputAck;

            // 推进 InputSendSystem 的已确认 tick，清理冗余重传环形缓冲中的已确认输入。
            // 若不调用此方法，_lastAckedClientTick 永远为 0，导致每次 Update 都触发冗余重传，
            // 大量旧 InputPacket 被发送到服务端后被去重拒绝，浪费带宽并污染日志。
            Horizon.Game.ECS.Arch.Systems.InputSendSystem.Instance?.OnInputAck(ackTick);

            // P-F4 限频日志：ACK 为 30-60Hz 高频包，原每包一条 Debug.Log 在多角色场景下
            // 字符串插值 + 日志 IO 开销显著；保留前 5 次 + 每 120 次采样输出。
            var ackLogCount = Interlocked.Increment(ref _inputAckLogCount);
            if (ackLogCount <= 5 || ackLogCount % 120 == 1)
            {
                Debug.Log($"[HundunWorldGame] 收到输入确认: LastProcessedClientTick={ackTick}（累计 {ackLogCount} 条，采样输出）");
            }
        }

        /// <summary>
        /// 握手响应包接收处理
        /// </summary>
        private void OnHandshakeReceived(HandshakePacket handshake)
        {
            // 重连场景下重置快照基线缓存：ECS World 销毁重建后，上一会话应用的 baseline 会残留，
            // 导致增量快照重建基于过期 baseline。
            // 每次握手都重置是安全的：首个全量快照（BaselineTick=0）会重新建立 baseline。
            // 阶段 6.4 起 ResetLastAppliedSnapshot 为实例方法（静态竞态修复）：取当前系统实例调用。
            GetSnapshotApplySystemInstance()?.ResetLastAppliedSnapshot();
            Debug.Log("[HundunWorldGame] 已重置快照基线缓存（重连/握手场景）");

            // 确保本地玩家 ID 已设置
            if (handshake.LocalCharacterId != 0 && _playerId == 0)
            {
                SetPlayerId(handshake.LocalCharacterId);
                Debug.Log($"[HundunWorldGame] 握手响应设置本地玩家ID: {handshake.LocalCharacterId}");
            }

            // 收到服务端握手确认，标记同步握手完成
            _networkManager?.MarkSyncHandshakeComplete();

            Debug.Log($"[HundunWorldGame] 收到握手响应: CharacterId={handshake.LocalCharacterId}");
        }

        /// <summary>
        /// 重连恢复握手确认处理（服务端对 ResumeIncremental/ResendFullChunks 的 HandshakePacket 确认）。
        /// 仅恢复同步握手门控（IsSyncHandshakeComplete），使 ECSUpdateDriver 恢复上行 InputPacket；
        /// 不做身份建立/基线重置——重连不销毁本地实体（OnConnectionStatusChanged 保持既有策略），
        /// 身份与基线由随后的全量快照（BaselineTick=0）自然恢复。
        /// </summary>
        private void OnReconnectHandshakeReceived(HandshakePacket handshake)
        {
            _networkManager?.MarkSyncHandshakeComplete();
            Debug.Log($"[HundunWorldGame] 收到重连握手确认（恢复输入门控）: CharacterId={handshake.LocalCharacterId}");
        }

        /// <summary>
        /// 兼容路径：服务端重连恢复决策返回的 WorldPatchManifestPacket（旧服务端/低版本场景）。
        /// 同 <see cref="OnReconnectHandshakeReceived"/>：仅恢复同步握手门控。
        /// </summary>
        private void OnPatchManifestReceived(WorldPatchManifestPacket manifest)
        {
            _networkManager?.MarkSyncHandshakeComplete();
            Debug.Log($"[HundunWorldGame] 收到 WorldPatchManifest（恢复输入门控）: PatchCutoverDiffSeq={manifest.PatchCutoverDiffSeq}");
        }

        /// <summary>同步事件包日志限频（见 OnEventReceived）。</summary>
        private float _eventPacketLogTime = -100f;

        /// <summary>
        /// DS 权威伤害包 → 立刻落到受击 Actor 的血量上（不等下一份快照）。
        /// <para>为什么必须有：怪物血量只有 DS 那份是权威的，而快照按"变化触发"，
        /// 连打几刀时血条可能整段不刷新；玩家看到的就是"打中了但血条纹丝不动"。</para>
        /// </summary>
        private void OnDsDamageReceived(Horizon.Game.Message.Sync.DamagePacket damage)
        {
            var actor = FindWorldActorByEntityId(damage.TargetId);
            if (actor == null) return;

            var attrs = actor.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>();
            if (attrs != null)
            {
                if (damage.MaxHp > 0) attrs.MaxHealth = damage.MaxHp;
                attrs.CurrentHealth = damage.RemainingHp;
            }
        }

        /// <summary>
        /// v17 翻越裁决回执 → 本地翻越驱动。受理时本地预测已经在跑（无需动作）；
        /// 拒绝时驱动会撤销预测并结束 GASP 动画，位置回正交给随后的权威快照/修正。
        /// </summary>
        private void OnTraversalResultReceived(Horizon.Game.Message.Sync.TraversalResultPacket result)
            => _localTraversalDriver?.OnAuthorityResult(result);

        /// <summary>
        /// DS 权威死亡包 → 客户端立刻进入"尸体"表现：不可再选中、收掉读条。
        /// 尸体交互位（<c>CorpseInteractable</c>）仍由快照下发，G 键那条链路不变。
        /// </summary>
        private void OnDsDeathReceived(Horizon.Game.Message.Sync.DeathPacket death)
        {
            var actor = FindWorldActorByEntityId(death.EntityId);
            if (actor == null) return;

            var attrs = actor.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>();
            if (attrs != null)
            {
                attrs.CurrentHealth = 0f;
                attrs.IsDead = true;
            }
            var tag = actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
            if (tag != null)
            {
                tag.Selectable = false;
                tag.EndCast();
            }
        }

        /// <summary>按服务端实体 id 找客户端 Actor（怪物/NPC 都在选中标记表里；数量级很小，线性扫足够）。</summary>
        private static Actor FindWorldActorByEntityId(ulong entityId)
        {
            if (entityId == 0UL) return null;
            foreach (var t in HundunWorld.Game.Selection.SelectableEntityTag.Active)
            {
                if (t != null && t.EntityId == entityId && t.Actor != null && t.Actor.IsActive)
                    return t.Actor;
            }
            return null;
        }

        /// <summary>
        /// 同步事件包接收处理
        /// 将 EventPacket 放入 EventReceiveBuffer，供 EventApplySystem 消费
        /// </summary>
        private void OnEventReceived(EventPacket eventPacket)
        {
            Horizon.Game.ECS.Arch.Network.EventReceiveBuffer.Instance.Enqueue(eventPacket);
            // CORPSE-LOOT-PATCH：拾取窗口关了但还有东西没拿 ⇒ 弹一条提示（服务端每人只发一条，这里只负责显示）。
            if (eventPacket.Events != null)
            {
                for (var i = 0; i < eventPacket.Events.Length; i++)
                {
                    var e = eventPacket.Events[i];
                    if (e.Kind == SyncEventKind.CorpseLootExpired && e.IntValue > 0)
                        HundunWorld.Game.Selection.WorldSelectionService.NotifyCorpseLootExpired(e.IntValue);
                }
            }
            // 这条 Info 原先每个同步包一行，实测每服务器 tick 两行、约 120 行/秒，
            // 4 分钟就把磁盘日志顶到 2MB 上限并打出 "Trimming log file."——此后 Logs/Log_*.txt 不再记录任何东西，
            // 地面/相机这类需要跨分钟观察的问题只能靠编辑器面板肉眼读，本次排查被它挡了两次。
            // 消费逻辑不变，仅把日志降为每 5 秒一条（足够证明事件链在跑）。
            if (eventPacket.Events != null && eventPacket.Events.Length > 0
                && Time.UnscaledGameTime - _eventPacketLogTime >= 5f)
            {
                _eventPacketLogTime = Time.UnscaledGameTime;
                Debug.Log($"[HundunWorldGame] 收到同步事件: ServerTick={eventPacket.ServerTick}, Events={eventPacket.Events.Length}");
            }
        }

        /// <summary>
        /// WorldChunkDiffPacket 接收处理（服务端通过 fanout 推送的快照/事件）
        /// 按 PayloadType 路由到对应缓冲区：
        /// - EntityDelta: 反序列化为 EntityDelta[] → SnapshotPacket → SnapshotReceiveBuffer
        /// - Event: 反序列化为 EventPacket → EventReceiveBuffer（含 CorrectionPacket）
        /// - InteractionSync: 反序列化为 InteractionSyncPacket → EventReceiveBuffer
        /// - SceneObjectSync: 反序列化为 SceneObjectSyncPacket → EventReceiveBuffer
        /// </summary>
        // 诊断：OnChunkDiffReceived 调用计数，用于限频日志（前 5 次无条件输出，后续每 60 次输出一次）
        private long _chunkDiffReceivedCount;

        // 诊断：OnSnapshotReceived 含 Deltas 快照日志计数，用于限频日志（前 5 次无条件输出，后续每 120 次输出一次）
        private long _snapshotRecvLogCount;

        // P-F4：InputAck 幂等去重（双链路重复确认）+ 日志采样计数器。
        private long _lastInputAckTick = long.MinValue;
        private long _inputAckLogCount;

        // 下行去重（P-F 兜底）：最近收到的 (ChunkMortonKey, DiffSeqEnd, PayloadType, Payload.Length) 指纹，
        // 用于丢弃服务端/网关重复下发的字节级相同 diff。有界环形去重（最近 N 条）。
        // 方案4（plan.md §5）：窗口从 16 提升到 64，覆盖 1 秒内 ~20Hz × 3 chunk = 60 个指纹，留余量，
        // 避免多 chunk 场景下旧指纹被覆盖导致漏去重（根因 #4）。
        private const int ChunkDiffDedupWindow = 64;
        private readonly long[] _chunkDiffDedupFingerprints = new long[ChunkDiffDedupWindow];
        private int _chunkDiffDedupIndex;
        private long _chunkDiffDedupDroppedCount;

        private void OnChunkDiffReceived(WorldChunkDiffPacket diff)
        {
            if (diff?.Payload == null || diff.Payload.Length == 0)
            {
                Debug.LogWarning("[HundunWorldGame] OnChunkDiffReceived: Payload 为空，已丢弃");
                return;
            }

            // 修复（远程角色"前冲/回拉"——Event 包被去重误丢）：
            // 实测日志显示 PayloadType=Event 的包被下行去重丢弃 55/s。Event 通道承载
            // InputAck（每 3 tick 一条/人）与 Correction——同一 chunk 内多个玩家的 InputAck
            // 序列化后长度相同、(ChunkKey, DiffSeqEnd=tick, PayloadType, Payload.Length) 指纹完全相同
            // → 被去重误判为"重复"丢弃。
            // 后果：客户端收不到 InputAck → _lastAckedClientTick 不推进 → 预测-权威误差持续累积
            // → 移动摇动/回拉。Event 是离散事件（低频、去重收益低、误丢代价极高），跳过指纹去重；
            // EntityDelta（高频快照，重复下发是真实问题）保留去重。
            if (diff.PayloadType == WorldChunkDiffPayloadType.EntityDelta)
            {
                // 有界最近去重：同一 (ChunkMortonKey, DiffSeqEnd, PayloadType, Payload.Length) 在最近窗口内出现即为重复（字节级相同），丢弃。
                // 指纹混合：ChunkMortonKey(ulong) 高位异或后与 DiffSeqEnd(tick) 混入，再叠加 PayloadType 与 Payload.Length，降低碰撞概率。
                // 方案4（plan.md §5）：指纹混入 Payload.Length，区分同 tick 同 chunk 不同内容的包（如全量快照 vs 增量），降低误碰撞（根因 #4）。
                var fingerprint = ((long)diff.ChunkMortonKey ^ (diff.DiffSeqEnd << 8)) * 31 + (int)diff.PayloadType * 7 + diff.Payload.Length;
                var dedupIdx = _chunkDiffDedupIndex;
                for (int i = 0; i < ChunkDiffDedupWindow; i++)
                {
                    if (_chunkDiffDedupFingerprints[(dedupIdx + ChunkDiffDedupWindow - i) % ChunkDiffDedupWindow] == fingerprint)
                    {
                        _chunkDiffDedupDroppedCount++;
                        if (_chunkDiffDedupDroppedCount % 60 == 1)
                            Debug.LogWarning($"[HundunWorldGame] 下行去重：丢弃重复 ChunkDiff（ChunkKey=0x{diff.ChunkMortonKey:X16}, DiffSeqEnd={diff.DiffSeqEnd}, PayloadType={diff.PayloadType}, 累计丢弃={_chunkDiffDedupDroppedCount}）");
                        return;
                    }
                }
                _chunkDiffDedupFingerprints[dedupIdx] = fingerprint;
                _chunkDiffDedupIndex = (dedupIdx + 1) % ChunkDiffDedupWindow;
            }

            try
            {
                var recvCount = Interlocked.Increment(ref _chunkDiffReceivedCount);
                switch (diff.PayloadType)
                {
                    case WorldChunkDiffPayloadType.EntityDelta:
                        {
                            // 方案1评估（plan.md §5）：Deserialize 得到的 EntityDelta[] 被 SnapshotReceiveBuffer 异步消费，池化生命周期不安全，保持现状。
                            var deltas = MemoryPack.MemoryPackSerializer.Deserialize<EntityDelta[]>(diff.Payload);
                            if (deltas == null || deltas.Length == 0)
                            {
                                Debug.LogWarning($"[HundunWorldGame] OnChunkDiffReceived: EntityDelta 反序列化为空！PayloadSize={diff.Payload.Length}, ChunkKey=0x{diff.ChunkMortonKey:X16}");
                                break;
                            }
                            // 诊断日志：前 5 次无条件输出，后续每 60 次输出一次
                            if (recvCount <= 5 || recvCount % 60 == 1)
                            {
                                var entityIds = string.Join(",", deltas.Take(5).Select(d => d.EntityId));
                                var kinds = string.Join(",", deltas.Take(5).Select(d => d.Kind));
                                var firstTransform = deltas[0].Transform.HasValue
                                    ? $"X={deltas[0].Transform.Value.X:F2},Y={deltas[0].Transform.Value.Y:F2},Z={deltas[0].Transform.Value.Z:F2},Yaw={deltas[0].Transform.Value.Yaw:F2}"
                                    : "null";
                                Debug.Log($"[HundunWorldGame] OnChunkDiffReceived#{recvCount} EntityDelta: DeltaCount={deltas.Length}, ChunkKey=0x{diff.ChunkMortonKey:X16}, EntityIds=[{entityIds}], Kinds=[{kinds}], FirstTransform={firstTransform}");
                            }
                            // 方案1评估（plan.md §5）：SnapshotPacket 被 SnapshotReceiveBuffer 异步消费，池化生命周期不安全，保持现状。
                            var snapshot = new SnapshotPacket
                            {
                                ServerTick = diff.DiffSeqEnd,
                                BaselineTick = 0,
                                Deltas = deltas,
                            };
                            SnapshotReceiveBuffer.Instance.Enqueue(snapshot);
                            break;
                        }
                    case WorldChunkDiffPayloadType.Event:
                        {
                            var eventPacket = MemoryPack.MemoryPackSerializer.Deserialize<EventPacket>(diff.Payload);
                            if (eventPacket != null)
                            {
                                // 表现型脚本事件（/event）走 Vfx 分支直接落到世界文字；
                                // 生成回执走 ScriptedSpawnResult 分支贴回聊天。
                                // 两者都留在游戏层做：EventApplySystem 属共享 ECS 层、不认 UI。
                                TryShowScriptedEffect(eventPacket);
                                TryApplyScriptedSpawnResult(eventPacket);
                                TryApplyMonsterCast(eventPacket);
                                TryApplyMonsterSay(eventPacket);
                                Horizon.Game.ECS.Arch.Network.EventReceiveBuffer.Instance.Enqueue(eventPacket);
                            }
                            break;
                        }
                    case WorldChunkDiffPayloadType.InteractionSync:
                        {
                            // 交互同步包通过 EventPacket 包装后走 EventReceiveBuffer，
                            // 由 EventApplySystem 消费。
                            var interaction = MemoryPack.MemoryPackSerializer.Deserialize<InteractionSyncPacket>(diff.Payload);
                            if (interaction != null)
                            {
                                var wrappedEvent = new EventPacket
                                {
                                    ServerTick = diff.DiffSeqEnd,
                                    Events = new[]
                                    {
                                        new SyncEvent
                                        {
                                            Kind = interaction.StateBits switch
                                            {
                                                var b when (b & 0x01) != 0 => SyncEventKind.InteractStart,
                                                var b when (b & 0x02) != 0 => SyncEventKind.InteractEnd,
                                                var b when (b & 0x04) != 0 => SyncEventKind.InteractStolen,
                                                _ => SyncEventKind.Unknown,
                                            },
                                            SourceEntityId = (ulong)interaction.InteractorId,
                                            TargetEntityId = (ulong)interaction.InteractableId,
                                            IntValue = interaction.SlotIdx,
                                            Payload = MemoryPack.MemoryPackSerializer.Serialize(interaction),
                                        },
                                    },
                                };
                                Horizon.Game.ECS.Arch.Network.EventReceiveBuffer.Instance.Enqueue(wrappedEvent);
                            }
                            break;
                        }
                    case WorldChunkDiffPayloadType.SceneObjectSync:
                        {
                            // 场景对象同步包暂不走 EventReceiveBuffer，直接日志输出。
                            // 后续如有 InteractionApplySystem 等消费者可扩展路由。
                            var sceneObject = MemoryPack.MemoryPackSerializer.Deserialize<SceneObjectSyncPacket>(diff.Payload);
                            if (sceneObject != null)
                            {
                                Debug.Log($"[HundunWorldGame] 收到场景对象同步: ObjectId={sceneObject.ObjectId}, StateBits=0x{sceneObject.StateBits:X2}");
                            }
                            break;
                        }
                    default:
                        Debug.LogWarning($"[HundunWorldGame] ChunkDiff 未知的 PayloadType={diff.PayloadType}, ChunkKey=0x{diff.ChunkMortonKey:X16}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HundunWorldGame] ChunkDiff 反序列化失败: {ex.Message}, PayloadType={diff.PayloadType}");
            }
        }

        /// <summary>
        /// 订阅 SyncPacketMessageHandler 事件
        /// 由于 AddAllMessageHandlers 在 NetworkManager 构造函数的异步任务中执行，
        /// handler 可能尚未注册，因此需要：
        /// 1. 订阅 NetworkManager.HandlersRegistered 事件，在处理器注册完成后立即订阅
        /// 2. 保留延迟重试作为兜底（减少重试次数和间隔）
        /// </summary>
        private void SubscribeSyncHandlerEvents()
        {
            // [修复] 订阅 HandlersRegistered 事件，确保 handler 注册完成后能立即订阅，不遗漏
            _networkManager.HandlersRegistered += OnHandlersRegistered;

            // 尝试立即订阅（如果 handler 已经注册完成）
            if (TrySubscribeSyncHandler())
            {
                return;
            }

            // 兜底：延迟重试（失效点 #5 修复：扩展重试窗口到 30×200ms=6s，
            // 并在彻底失败后启动后台周期恢复，避免 handler 注册延迟超过兜底窗口时永久丢失订阅）。
            // 修复：重试循环必须携带取消令牌，PIE 退出/Dispose 时终止循环，
            // 否则退出 PIE 后该无界循环仍每 2 秒访问已释放的 NetworkManager。
            _syncSubscribeCts?.Dispose();
            _syncSubscribeCts = new System.Threading.CancellationTokenSource();
            var subscribeToken = _syncSubscribeCts.Token;
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 30 && !subscribeToken.IsCancellationRequested; i++)
                {
                    await Task.Delay(200, subscribeToken);
                    if (TrySubscribeSyncHandler())
                    {
                        return;
                    }
                }

                if (subscribeToken.IsCancellationRequested)
                {
                    return;
                }

                Debug.LogWarning("[HundunWorldGame] 6 秒内未找到 SyncPacketMessageHandler，启动后台周期重试...");

                // 后台周期恢复：每 2 秒重试一次，直到订阅成功。
                // 防止 NetworkManager 异步 handler 注册延迟导致永久订阅失败，
                // 进而让快照/ChunkDiff/握手事件完全无法桥接到 ECS。
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (!subscribeToken.IsCancellationRequested && await timer.WaitForNextTickAsync(subscribeToken))
                {
                    if (TrySubscribeSyncHandler())
                    {
                        Debug.Log("[HundunWorldGame] 后台周期重试订阅成功。");
                        return;
                    }
                }
            }, subscribeToken);
        }

        /// <summary>
        /// HandlersRegistered 事件回调：消息处理器注册完成后立即尝试订阅同步事件
        /// </summary>
        private void OnHandlersRegistered()
        {
            TrySubscribeSyncHandler();
        }

        /// <summary>
        /// 尝试订阅 SyncPacketMessageHandler 事件，成功返回 true
        /// </summary>
        private bool TrySubscribeSyncHandler()
        {
            var syncHandler = _networkManager?.GetHandler<ManagedHundunWorld.Network.Handlers.SyncPacketMessageHandler>();
            if (syncHandler != null)
            {
                syncHandler.SnapshotReceived += OnSnapshotReceived;
                syncHandler.InputAckReceived += OnInputAckReceived;
                syncHandler.HandshakeReceived += OnHandshakeReceived;
                syncHandler.EventReceived += OnEventReceived;
                // DS 权威的伤害/死亡包：快照是 1Hz/变化触发，血条与"已死亡"不该等下一份快照；
                // 这两条事件原先**没有任何订阅者**（CombatEffectSystem 的 Enqueue 也零调用方），
                // 表现就是"服务端已经打死了，客户端血条一动不动、尸体也出不来"。
                syncHandler.DamageReceived += OnDsDamageReceived;
                syncHandler.DeathReceived += OnDsDeathReceived;
                // CORPSE-LOOT-PATCH：拾取回执与"到期没拿完"都交给选中层在主线程弹提示。
                syncHandler.CorpseLootResultReceived += p =>
                    HundunWorld.Game.Selection.WorldSelectionService.NotifyCorpseLootResult(p);
                syncHandler.ChunkDiffReceived += OnChunkDiffReceived;
                // v17：翻越裁决回执 → 本地翻越驱动（拒绝时撤销预测并结束动画）。
                syncHandler.TraversalResultReceived += OnTraversalResultReceived;
                // 修复（静置断线后输入断流 — "无法移动 + 闪跳回原地"根因之一）：
                // 重连恢复确认（服务端对 ResumeIncremental/ResendFullChunks 的 HandshakePacket 确认，
                // 或兼容路径的 WorldPatchManifestPacket）到达时恢复同步握手门控 IsSyncHandshakeComplete。
                // 否则 ECSUpdateDriver.FlushInputSendQueue 每帧丢弃 InputPacket，移动请求永久不上行。
                syncHandler.ReconnectHandshakeReceived += OnReconnectHandshakeReceived;
                syncHandler.PatchManifestReceived += OnPatchManifestReceived;
                Debug.Log("[HundunWorldGame] 已订阅同步包事件（快照桥接已建立，含 ChunkDiff/重连握手确认）");

                // 订阅成功后移除 HandlersRegistered 事件监听，避免重复订阅
                _networkManager.HandlersRegistered -= OnHandlersRegistered;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 启动游戏
        /// </summary>
        public async Task StartAsync()
        {
            Debug.Log("启动游戏开始");

            if (_isRunning)
                return;

            try
            {
                Debug.Log("正在清除旧缓存数据...");
                DatabaseManager.ClearAllCacheData();
                Debug.Log("旧缓存数据已清除");
            }
            catch (Exception ex)
            {
                Debug.LogError($"清除缓存数据失败: {ex.Message}");
            }

            // 物品内容表按需重载：编辑器"保存→导出"后，新开一局/PIE 就能看到新定义。
            // 静态层自己不跨 PIE 重置，所以每次启动都显式 Reload 一次。
            HundunWorld.Game.Inventory.ItemCatalogOverlay.Reload();
            HundunWorld.Game.Inventory.ItemCatalog.InvalidateCache();
            Debug.Log("[ItemCatalogOverlay] " + HundunWorld.Game.Inventory.ItemCatalogOverlay.Source);

            _ecsManager.Start();
            _worldManager.StartSynchronization();

            _isRunning = true;

            if (_instance == null)
                _instance = this;
            Debug.Log("游戏启动完成（ECS更新由ECSUpdateDriver在主线程驱动）");
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

            await Task.CompletedTask;

            Debug.Log("游戏停止完成");
        }

        public void SetPlayerId(ulong playerId)
        {
            Volatile.Write(ref _playerId, playerId);
            _playerPositionUpdater.SetPlayerId(playerId);

            // 驱动本地资格状态机：身份确立/丢失事件（spec 5.1.3、6.1 时序）。
            _eligibilityState?.OnLocalIdentityChanged(playerId != 0);

            // 设置 NetworkManager.CharacterId，使后续所有上行同步包的 MessageHeader.CharacterId
            // 都携带角色 ID（服务端 HandleSubscriptionUpdateAsync 等依赖此字段路由）。
            if (_networkManager != null)
            {
                _networkManager.CharacterId = playerId;
            }

            // 设置 SnapshotApplySystem 的本地玩家ID，以便区分本地玩家和远程玩家
            if (_archWorldHost != null)
            {
                var snapshotSystem = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.NetworkReceive)
                    .OfType<Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem>()
                    .FirstOrDefault();
                if (snapshotSystem != null)
                {
                    snapshotSystem.LocalPlayerOwnerId = playerId;
                    Debug.Log($"[HundunWorldGame] SnapshotApplySystem.LocalPlayerOwnerId = {playerId}");
                }

                // 订阅 LocalSimulationSystem.PlayerChunkChanged 事件以实现客户端 AOI 动态订阅。
                // 仅订阅一次（_isPlayerChunkChangedSubscribed 防止重复订阅）。
                // 事件在 FixedUpdate 阶段（主线程）触发，回调中可直接读写 ECS 组件。
                if (!_isPlayerChunkChangedSubscribed)
                {
                    var localSimSystem = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate)
                        .OfType<Horizon.Game.ECS.Arch.Systems.LocalSimulationSystem>()
                        .FirstOrDefault();
                    if (localSimSystem != null)
                    {
                        localSimSystem.PlayerChunkChanged += OnPlayerChunkChanged;
                        _isPlayerChunkChangedSubscribed = true;
                        Debug.Log("[HundunWorldGame] 已订阅 LocalSimulationSystem.PlayerChunkChanged 事件");

                        // 注入地面高度采样器：ECS 层不能依赖 FlaxEngine.Physics，
                        // 由本类（Flax 端）提供一个 RayCast 回调，让 LocalSimulationSystem
                        // 在每帧预测后采样 (x, y) 处的 Terrain 高度约束 pred.Z，防止穿透地面。
                        // 坐标转换：ECS 为 Z-up（x=左右, y=前后, z=高度），
                        // Flax 为 Y-up → Flax 世界 (x, 0, y) 处射线，命中点的 Flax.Y 即 ECS.Z。
                        localSimSystem.GroundHeightSampler = SampleGroundHeightEcs;
                        Debug.Log("[HundunWorldGame] 已注入 LocalSimulationSystem.GroundHeightSampler");

                        // 同时注入 ReconciliationSystem.GroundHeightSampler：
                        // 回滚重播时也需要应用地面约束，否则从服务端权威位置重放的预测位置
                        // 会穿透 Terrain，导致下一次 correction 触发循环。
                        var reconciliationSystem = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate)
                            .OfType<Horizon.Game.ECS.Arch.Systems.ReconciliationSystem>()
                            .FirstOrDefault();
                        if (reconciliationSystem != null)
                        {
                            reconciliationSystem.GroundHeightSampler = SampleGroundHeightEcs;
                            Debug.Log("[HundunWorldGame] 已注入 ReconciliationSystem.GroundHeightSampler");
                        }
                        else
                        {
                            Debug.LogWarning("[HundunWorldGame] ReconciliationSystem 未找到，回滚重播时无地面约束");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[HundunWorldGame] LocalSimulationSystem 未找到，无法订阅 PlayerChunkChanged 事件");
                    }
                }
            }
        }

        /// <summary>
        /// 地面高度采样器：供 <see cref="Horizon.Game.ECS.Arch.Systems.LocalSimulationSystem.GroundHeightSampler"/> 使用。
        /// 给定 ECS 水平坐标 (x, y)（Z-up 坐标系，x=左右, y=前后），
        /// 通过 <see cref="Physics.RayCast"/> 从角色上方向下射线检测 Terrain/碰撞体，
        /// 返回该位置的地面 ECS.Z 高度（世界单位，本工程为 cm）；未命中任何碰撞体时返回 <see cref="float.NaN"/>。
        /// <para>
        /// 坐标转换：ECS Z-up ↔ Flax Y-up
        /// <list type="bullet">
        ///   <item>Flax.X = ECS.X（左右）</item>
        ///   <item>Flax.Y = ECS.Z（上下，高度）</item>
        ///   <item>Flax.Z = ECS.Y（前后）</item>
        /// </list>
        /// 因此在 Flax 世界 (x, 角色上方某高度, y) 处向下射线，命中点的 Flax.Y 即为地面 ECS.Z。
        /// </para>
        /// <para>
        /// 起点高度取 max(角色高度 + 5m, +2000m)，射到 -2000m：既稳定高于地表（含山地），
        /// 也能把已经坠到地面以下的角色捞回。注意世界单位是 cm——写死的 1000 只有 10m，
        /// 山上会从地表以下起射而漏检（历史缺陷）。
        /// 使用 <see cref="LayersMask.Default"/> 与 ThirdPersonCamera.GroundLayers 默认值一致。
        /// </para>
        /// </summary>
        /// <param name="ecsX">ECS 世界坐标 X（左右，cm）</param>
        /// <param name="ecsY">ECS 世界坐标 Y（前后，cm）</param>
        /// <returns>该位置的地面 ECS.Z 高度（cm）；无地面返回 <see cref="float.NaN"/></returns>
        /// <summary>地面采样诊断节流（临时，可连同 [GroundProbe] 标记一起删除）。</summary>
        private float _groundProbeLogTime = -100f;

        private float SampleGroundHeightEcs(float ecsX, float ecsY)
        {
            // 诊断（临时，可整段删除）：ASCII 标记 + 2s 限频。中文关键词在磁盘日志里会因 NUL 填充
            // 计数为 0，所以标记必须全 ASCII；用来判断采样器是否命中、地面到底在哪。
            bool bLog = Time.UnscaledGameTime - _groundProbeLogTime >= 2f;
            if (bLog) _groundProbeLogTime = Time.UnscaledGameTime;

            // 射线起点必须相对**角色当前高度**取，不能写死绝对高度。
            // 世界单位是 cm（引擎 DefaultGravity=-981、角色 MoveSpeed=500cm/s、场景里 Cube 中心 Y=50
            // 即 0.5m 坐在 Y=0 地面上）；原实现起点 Y=1000 是"1km 高空"的米制假设，在 cm 世界只有 10m。
            // 本图有庆云山这类山体，站在高于 10m 的地形上时射线起点落在地表**以下**，RayCastAll 打不到东西
            // → 返回 NaN → LocalSimulationSystem 据此跳过地面约束 → 保护期(120 帧=2s)一过角色就按重力持续下沉，
            // 服务端再不断 correction，表现就是"停留几秒后相机跟着沉进地面并反复抖动"。
            // 起点必须高于**地表**，而不是"角色上方几米"：角色一旦已经坠到地面以下
            // （本工程曾在无地面区域一路下坠，实测相机到过 -999m），"角色上方 5m"就整段在地面以下，
            // 射线再也打不到地面 → 永远拉不回来。故取"角色上方 5m"与 +2000m 中的较高者，
            // 并一直射到 -2000m：既覆盖山地高度，也能把已经坠落的角色捞回地面。
            const float probeTopY = 200000f;      // +2000m
            const float probeBottomY = -200000f;  // -2000m
            var standY = _localPlayerActor != null ? _localPlayerActor.Position.Y : 0f;
            var rayStart = new Vector3(ecsX, Mathf.Max(standY + 500f, probeTopY), ecsY);
            float rayLength = rayStart.Y - probeBottomY;
            var rayDir = Vector3.Down;

            // hitTriggers=false：必须跳过触发器。POI 的 Discover 球形触发器半径达 700~1200m，
            // 射线起点常落在球体内部，PhysX 会在距离 0 处返回退化命中（Point == 射线起点），
            // 导致采样"地面"恒等于射线起点高度，把角色钉死在半空（进入游戏后悬于半空的根因）。
            // 必须跳过**所有角色身体**的碰撞体（远程角色 + 本机玩家自己）：CharacterRoot.prefab 的
            // BodyCollider 是给点选用的，不是地形。本工程没有地面碰撞体，若把角色胶囊当成交点，
            // 本函数会从"无地面→NaN（跳过地面约束）"悄悄变成"地面高度=头顶 1.8 米"，
            // 且每 tick 重采样一次就再抬一个身位，角色一路悬空到射线起点。
            if (Physics.RayCastAll(rayStart, rayDir, out RayCastHit[] hits, rayLength, LayersMask.Default, false))
            {
                System.Array.Sort(hits, (a, b) => a.Distance.CompareTo(b.Distance));

                for (var i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    var owner = hit.Collider != null ? hit.Collider.Parent : null;
                    if (HundunWorld.Game.Selection.WorldSelectionService.IsCharacterBodyCollider(owner, _localPlayerActor))
                    {
                        if (bLog)
                        {
                            Debug.Log($"[GroundProbe] SKIP-CHAR x={ecsX:F0} z={ecsY:F0} owner={owner?.Name} " +
                                      $"type={owner?.GetType().Name}");
                        }

                        continue;
                    }

                    // 2026-10-23 实机根因：NPC 的 CombatCollider 是个朝上的盒子（nY=1.00），
                    // 仰角射击时它比角色高 1.8m+，被当成"地面"逐 tick 把角色抬上去
                    // （GroundProbe HIT owner=NPC_… groundY=4181 standY=4000，Y 直冲 7986）。
                    // 规则：高于脚下"一步"的面不是地面；只有 Terrain / 运行期地面在角色
                    // 明显陷到其下方时才作为救援接受（保留原来的"从地下捞回"能力）。
                    const float MaxStepUp = 80f;
                    if (hit.Point.Y > standY + MaxStepUp)
                    {
                        // 运行期地面的 Collider 是挂在场景根下的独立 Actor，Parent 读出来是 Scene，
                        // 所以还要看 Collider 自己的名字（实测日志 owner=Scene type=Scene groundY=0）。
                        string colliderName = hit.Collider != null ? hit.Collider.Name : null;
                        bool terrainLike = owner is Terrain
                                           || colliderName == RuntimeGroundName
                                           || (owner != null && owner.Name == RuntimeGroundName);
                        bool rescue = terrainLike && standY < hit.Point.Y - 200f;
                        if (!rescue)
                        {
                            if (bLog)
                            {
                                Debug.Log($"[GroundProbe] SKIP-OVERHEAD x={ecsX:F0} z={ecsY:F0} owner={owner?.Name} " +
                                          $"type={owner?.GetType().Name} groundY={hit.Point.Y:F0} standY={standY:F0}");
                            }

                            continue;
                        }
                    }

                    // 只认"朝上的可站立面"：法线朝下的天花板、竖直侧面（墙/柱）都不是地面。
                    // 这一条与"跳过角色碰撞体"互为兜底：任何被误判为地面的头顶物件法线通常不朝上，
                    // 而真正的 Terrain/地面盒法线朝上。
                    if (hit.Normal.Y < 0.5f)
                    {
                        if (bLog)
                        {
                            Debug.Log($"[GroundProbe] SKIP-NORMAL x={ecsX:F0} z={ecsY:F0} owner={owner?.Name} " +
                                      $"n=({hit.Normal.X:F2},{hit.Normal.Y:F2},{hit.Normal.Z:F2}) y={hit.Point.Y:F0}");
                        }

                        continue;
                    }

                    // 命中点的 Flax.Y 即对应 ECS.Z（地面高度）
                    _hasSinkRail = false;
                    if (bLog)
                    {
                        Debug.Log($"[GroundProbe] HIT x={ecsX:F0} z={ecsY:F0} hits={hits.Length} " +
                                  $"groundY={hit.Point.Y:F0} standY={standY:F0} top={rayStart.Y:F0} " +
                                  $"owner={owner?.Name} type={owner?.GetType().Name} nY={hit.Normal.Y:F2}");
                    }
                    return hit.Point.Y;
                }
            }

            // 未命中任何碰撞体。旧实现在这里返回 NaN，让上层"跳过地面约束"，于是角色以 TerminalVelocity
            // 无界下坠（实测 3 分钟坠到 -91m，相机穿地后每几帧被 correction 拽回，就是"沉入地面后反复抖动"）。
            // 现在做两件事：补铺运行期地面，并把下坠深度限制在"开始漏采时高度 -5m"以内。
            // 参照高度取"角色当前高度"与 0 的较高者：让从服务端持久化的深坑高度出生时
            // （实测曾为 -176m）也能被抬回，而不是在坑底继续数深度。
            if (!_hasSinkRail)
            {
                _sinkRailY = System.Math.Max(standY, 0f);
                _hasSinkRail = true;
            }

            // 先补铺地面再判深度：否则一旦越过限深线就会提前 return，永远不再有铺地面的入口。
            TryLayRuntimeGround();

            var standTooDeep = _sinkRailY - standY > SinkRailDepth;
            if (standTooDeep)
            {
                if (bLog)
                {
                    Debug.Log($"[GroundProbe] RAIL x={ecsX:F0} z={ecsY:F0} standY={standY:F0} rail={_sinkRailY:F0}");
                }
                return _sinkRailY - SinkRailDepth;
            }

            if (bLog)
            {
                Debug.Log($"[GroundProbe] MISS x={ecsX:F0} z={ecsY:F0} hits={(hits != null ? hits.Length : 0)} " +
                          $"standY={standY:F0} top={rayStart.Y:F0} len={rayLength:F0}");
            }
            return float.NaN;
        }

        /// <summary>运行期补铺的地面碰撞体名称（用于去重，勿改动）。</summary>
        private const string RuntimeGroundName = "Ground_Runtime";

        /// <summary>无地面时下坠的最大深度（cm）：相对"开始漏采时的高度"，防止相机无限沉入地下。</summary>
        private const float SinkRailDepth = 500f;

        private float _sinkRailY;
        private bool _hasSinkRail;
        private float _nextRuntimeGroundTime = -100f;

        /// <summary>
        /// 玩法区地面在运行期必须存在。地图文件里的 Ground_PlayArea 只有编辑器**重新从磁盘加载**过
        /// World.scene 才会带上：编辑器会缓存场景，任何一次保存都会用内存里的旧内容覆盖手写内容，
        /// 实测同一台编辑器里两次 PIE 一次有地面（角色稳定停在 Y=0 达 4 分钟）、一次全程打不到（坠到 -91m）。
        /// 所以地面由代码来铺，铺不铺由"这一帧射线打没打到"决定，天然去重且能在场景重载后自愈。
        /// </summary>
        private void TryLayRuntimeGround()
        {
            if (Time.UnscaledGameTime < _nextRuntimeGroundTime) return;
            _nextRuntimeGroundTime = Time.UnscaledGameTime + 5f;

            var scene = FindGameWorldScene();
            if (scene == null) return;

            // 已有地面（地图里的或上一次铺的）就不再重复铺设。
            // FindActor(type,name) 按名字递归查子树，被销毁的 Actor 不在树里，因此能正确反映"地面又没了"。
            if (scene.FindActor<Collider>(RuntimeGroundName) != null) return;

            var ground = new BoxCollider
            {
                Name = RuntimeGroundName,
                Position = new Vector3(0f, -50f, 0f),
                Size = new Vector3(100000f, 100f, 100000f)
            };
            // 上表面正好在 Y=0；覆盖 ±500m，真实地形恢复后应按地图高度图重做或撤掉这块板。
            SpawnActorToGameplayScene(ground, RuntimeGroundName);
            Debug.LogWarning($"[HundunWorldGame] 玩法区无地面碰撞体，已补铺 {RuntimeGroundName}（顶面 Y=0，覆盖 ±500m）");
        }

        /// <summary>
        /// 修复「进入游戏后在天上飞」：在 GameWorld 场景加载完成后重新采样地面高度，
        /// 并修正本地玩家 ECS 实体的 PredictedTransformComponent.Z（ECS 上下轴）。
        /// <para>
        /// 根因：CreateLocalPlayerEntity 在角色选择场景中被调用，此时 Physics.RayCast 无法命中
        /// GameWorld 的 Terrain（未加载），SampleGroundHeightEcs 返回 NaN，
        /// pred.Z 使用了服务端 initialY（可能为 0 或上次保存值，与实际地形高度不一致）。
        /// 场景加载完成后重新采样可获取正确地面高度，立即修正 pred.Z 防止角色悬空或穿透。
        /// </para>
        /// </summary>
        /// <param name="characterId">本地玩家角色 ID（用于查找 ECS 实体）</param>
        /// <param name="flaxX">Flax 世界 X 坐标（左右）</param>
        /// <param name="flaxZ">Flax 世界 Z 坐标（前后）</param>
        private void RealignLocalPlayerToGround(ulong characterId, float flaxX, float flaxZ)
        {
            if (_archWorld == null) return;

            // 采样地面高度（ECS 坐标系：传入 FlaxX=左右, FlaxZ=前后）
            var groundEcsZ = SampleGroundHeightEcs(flaxX, flaxZ);
            if (float.IsNaN(groundEcsZ))
            {
                Debug.LogWarning($"[HundunWorldGame] RealignLocalPlayerToGround: 地面采样失败（NaN），保持当前高度。Pos=({flaxX:F2}, {flaxZ:F2})");
                ScheduleGroundRealignRetry(characterId, flaxX, flaxZ);
                return;
            }

            // 查找本地玩家 ECS 实体
            var query = new Arch.Core.QueryDescription()
                .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent, Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>();

            bool found = false;
            float predEcsX = flaxX, predEcsY = flaxZ, predYaw = 0f;
            _archWorld.Query(in query, (Arch.Core.Entity e, ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent nid, ref Horizon.Game.ECS.Arch.Components.PredictedTransformComponent pred) =>
            {
                if (!nid.IsLocalPlayer || nid.EntityId != characterId) return;
                if (found) return;
                found = true;
                predEcsX = pred.X;
                predEcsY = pred.Y;
                predYaw = pred.Yaw;

                // 仅当当前高度与地面偏差超过 0.5m 时修正（避免每帧微小抖动）
                if (MathF.Abs(pred.Z - groundEcsZ) > 0.5f)
                {
                    Debug.Log($"[HundunWorldGame] RealignLocalPlayerToGround: 修正本地玩家高度 pred.Z={pred.Z:F3} → groundZ={groundEcsZ:F3} (FlaxX={flaxX:F2}, FlaxZ={flaxZ:F2})");
                    pred.Z = groundEcsZ;
                    pred.Vz = 0f; // 清零垂直速度，防止残留重力加速度
                }
            });

            if (!found)
            {
                Debug.LogWarning($"[HundunWorldGame] RealignLocalPlayerToGround: 未找到本地玩家 ECS 实体 (CharacterId={characterId})，稍后重试");
                ScheduleGroundRealignRetry(characterId, flaxX, flaxZ);
                return;
            }

            _hasPendingGroundRealign = false;

            // ★ 修复「进入游戏后悬于半空」的服务端半边：贴地只改了本地 pred.Z，
            // 服务端权威 Z 仍停在出生半空值或 Redis 持久化的旧悬空值（EnterWorldAsync 优先用后者，
            // 会覆盖握手里已贴地的 InitialY）。玩家一移动 drift 即超阈值 → Correction 风暴
            // 把角色反复拉回半空。复用快速旅行通道上报权威贴地（理由见 OnFastTravelRequested 注释）。
            // 坐标：Flax X=ECS X（左右）、Flax Y=ECS Z（上下=groundEcsZ）、Flax Z=ECS Y（前后）。
            SendFastTravelRequest(null, predEcsX, groundEcsZ, predEcsY, predYaw);
        }

        // 地面重对齐重试状态：Actor 创建瞬间地形碰撞体可能未就绪（流式 chunk 未加载），
        // 采样返回 NaN 会让角色滞留在半空出生高度，每秒重试一次，最多 30 秒。
        private bool _hasPendingGroundRealign;
        private ulong _pendingGroundRealignCharacterId;
        private float _pendingGroundRealignX;
        private float _pendingGroundRealignZ;
        private float _pendingGroundRealignNextTime;
        private float _pendingGroundRealignDeadline;

        private void ScheduleGroundRealignRetry(ulong characterId, float flaxX, float flaxZ)
        {
            if (_hasPendingGroundRealign && _pendingGroundRealignCharacterId == characterId)
            {
                // 已有针对同一角色的重试在排队：保留原超时截止，仅刷新坐标
                _pendingGroundRealignX = flaxX;
                _pendingGroundRealignZ = flaxZ;
                return;
            }

            _hasPendingGroundRealign = true;
            _pendingGroundRealignCharacterId = characterId;
            _pendingGroundRealignX = flaxX;
            _pendingGroundRealignZ = flaxZ;
            _pendingGroundRealignNextTime = Time.GameTime + 1f;
            _pendingGroundRealignDeadline = Time.GameTime + 30f;
        }

        /// <summary>
        /// 处理 NarrativePro 的快速旅行请求：把目标位置写入 ECS 预测变换（本地玩家位置的单一事实源），
        /// 并同步 Actor 位置以获得即时视觉反馈。
        /// <para>
        /// 轴映射与 <see cref="LocalPlayerActorSyncSystem"/> 保持一致：
        /// Flax.X → ECS.X（左右）、Flax.Y → ECS.Z（上下）、Flax.Z → ECS.Y（前后）。
        /// 之所以必须写 ECS 而不是只改 Actor：同步系统每帧用 ECS 值回写 <c>Actor.Position</c>，
        /// 只改 Actor 会在下一帧被打回原处。
        /// </para>
        /// </summary>
        private void OnFastTravelRequested(NarrativePro.Navigation.POIData poi, Transform destination)
        {
            var player = _localPlayerActor;
            if (player == null)
            {
                Debug.LogWarning($"[FastTravel] 找不到本地玩家 Actor，取消传送到 {poi?.POITag}");
                return;
            }

            if (_archWorld == null)
            {
                Debug.LogWarning("[FastTravel] ECS World 未就绪，取消传送");
                return;
            }

            float flaxX = destination.Translation.X;
            float flaxZ = destination.Translation.Z;

            // 高度优先贴合地面；采样失败（NaN，例如该点无地形）时退回 POI 配置高度
            float sampledEcsZ = SampleGroundHeightEcs(flaxX, flaxZ);
            bool grounded = !float.IsNaN(sampledEcsZ);
            float flaxY = grounded ? sampledEcsZ : destination.Translation.Y;

            float yawRadians = destination.Orientation.EulerAngles.Y * Mathf.DegreesToRadians;

            bool applied = false;
            var query = new Arch.Core.QueryDescription()
                .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent,
                         Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>();

            _archWorld.Query(in query,
                (Arch.Core.Entity e,
                 ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent nid,
                 ref Horizon.Game.ECS.Arch.Components.PredictedTransformComponent pred) =>
                {
                    if (!nid.IsLocalPlayer || applied) return;

                    pred.X = flaxX;
                    pred.Y = flaxZ;
                    pred.Z = flaxY;
                    pred.Yaw = yawRadians;
                    pred.Vz = 0f; // 清零垂直速度，避免残留重力把玩家拽离落点
                    applied = true;
                });

            if (!applied)
            {
                Debug.LogWarning("[FastTravel] 未找到本地玩家 ECS 实体，仅同步了客户端 Actor（可能被同步系统回写）");
            }

            player.Position = new Vector3(flaxX, flaxY, flaxZ);

            // 上报服务端做权威应用：只改客户端预测会被 MovementValidator 判为瞬移（位移 > 100m）
            // 并下发纠正把玩家拉回，因此必须让服务端也把权威位置对齐到目标点。
            SendFastTravelRequest(poi, flaxX, flaxY, flaxZ, yawRadians);

            Debug.Log($"[FastTravel] 已传送至 {poi?.POITag}（{poi?.POIDisplayName}）" +
                      $"Flax=({flaxX:F1},{flaxY:F1},{flaxZ:F1})" +
                      (grounded ? "，高度已贴地" : "，地面采样失败→使用配置高度"));
        }

        /// <summary>
        /// 向服务端上报快速旅行请求（fire-and-forget；失败只告警，不影响本地预测表现）。
        /// </summary>
        private void SendFastTravelRequest(NarrativePro.Navigation.POIData poi, float flaxX, float flaxY, float flaxZ, float yawRadians)
        {
            if (_networkManager == null)
            {
                Debug.LogWarning("[FastTravel] NetworkManager 不可用，跳过服务端上报（位置可能被服务端纠正回滚）");
                return;
            }

            var entityId = PlayerId;
            if (entityId == 0)
            {
                Debug.LogWarning("[FastTravel] 本地玩家实体 Id 未知（握手未完成？），跳过服务端上报");
                return;
            }

            var request = new Horizon.Game.Message.Network.FastTravelRequestMessage
            {
                EntityId = entityId,
                PoiTag = poi?.POITag.TagName ?? "",
                X = flaxX,
                Y = flaxY,
                Z = flaxZ,
                Yaw = yawRadians,
            };

            _ = SendFastTravelRequestAsync(request);
        }

        private async System.Threading.Tasks.Task SendFastTravelRequestAsync(
            Horizon.Game.Message.Network.FastTravelRequestMessage request)
        {
            try
            {
                var ok = await _networkManager.SendMessageAsync(request);
                if (!ok)
                {
                    Debug.LogWarning($"[FastTravel] 服务端上报返回失败（{request.PoiTag}），位置可能被纠正回滚");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FastTravel] 服务端上报异常（{request.PoiTag}）: {ex.Message}");
            }
        }

        /// <summary>
        /// 重连成功后补发携带"当前预测位置"的完整同步握手。
        /// <para>
        /// 修复（静置断线后无法移动 + 闪跳回原地）：
        /// <list type="number">
        ///   <item>服务端 resume 路径（HandleReconnectAsync）不调用 EnterWorldAsync；若实体已因
        ///         空闲超时+Despawn 宽限被清理，AOI 订阅已移除，客户端重连后收不到任何快照。
        ///         补发握手触发服务端 EnterWorldAsync（重连分支：实体已存在时对齐位置 + 重建 AOI 订阅；
        ///         实体不存在时走 P-F9 式重新注册），恢复快照流与 AOI。</item>
        ///   <item>服务端 EnterWorldAsync 重连分支现会以握手坐标对齐实体权威位置——必须携带客户端
        ///         当前预测位置（而非首次进入的初始位置），否则服务端被拉回静置/初始坐标，
        ///         客户端随后被全量快照拉回（闪跳回原地）。</item>
        /// </list>
        /// 坐标转换：PredictedTransformComponent 为 ECS Z-up（X=左右, Y=前后, Z=上下）；
        /// 握手 InitialX/Y/Z 为 Flax Y-up（X=左右, Y=上下, Z=前后），故 flaxX=ecsX, flaxY=ecsZ, flaxZ=ecsY。
        /// </para>
        /// </summary>
        private async System.Threading.Tasks.Task SendReconnectFullHandshakeWithLatestPositionAsync()
        {
            if (_networkManager == null || _archWorld == null)
            {
                return;
            }

            var characterId = PlayerId;
            if (characterId == 0)
            {
                Debug.LogWarning("[HundunWorldGame] 重连补发握手跳过：本地玩家 ID 未确立");
                return;
            }

            // 从 ECS 读取本地玩家当前预测位置（ECS Z-up）。
            float ecsX = 0f, ecsY = 0f, ecsZ = 0f;
            bool found = false;
            try
            {
                var query = new Arch.Core.QueryDescription()
                    .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent, Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>();
                _archWorld.Query(in query, (Arch.Core.Entity e, ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent nid,
                    ref Horizon.Game.ECS.Arch.Components.PredictedTransformComponent pred) =>
                {
                    if (!nid.IsLocalPlayer || nid.EntityId != characterId || found) return;
                    found = true;
                    ecsX = pred.X;
                    ecsY = pred.Y;
                    ecsZ = pred.Z;
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HundunWorldGame] 重连补发握手：读取预测位置异常被隔离: {ex.Message}");
            }

            if (!found)
            {
                // 实体未找到（可能被清理）：回退到缓存的首握坐标，仍触发 EnterWorldAsync 重建实体/订阅。
                Debug.LogWarning("[HundunWorldGame] 重连补发握手：未找到本地玩家 ECS 实体，回退使用缓存握手坐标");
                await _networkManager.SendSyncHandshakeAsync(
                    characterId, _networkManager.LastHandshakePositionX,
                    _networkManager.LastHandshakePositionY, _networkManager.LastHandshakePositionZ);
                return;
            }

            // ECS Z-up → Flax Y-up：flaxX=ecsX(左右), flaxY=ecsZ(上下), flaxZ=ecsY(前后)。
            await _networkManager.SendSyncHandshakeAsync(characterId, ecsX, ecsZ, ecsY);
            Debug.Log($"[HundunWorldGame] 重连补发完整同步握手（携带当前预测位置）: CharacterId={characterId}, Flax=({ecsX:F2},{ecsZ:F2},{ecsY:F2})");
        }

        /// <summary>
        /// 本地玩家跨越 chunk 边界时触发：计算新视野范围与旧订阅范围的 diff，
        /// 通过 NetworkManager.SendSubscriptionUpdateAsync 上行 SubscriptionUpdatePacket。
        /// <para>
        /// 视野半径 radius=5 表示 11x11x11=1331 个 chunk（覆盖 176m×176m×176m，视距约 80m）。
        /// 与服务端 HandleHandshakeAsync 中的初始订阅半径保持一致。
        /// </para>
        /// <para>
        /// 滞后防抖：只有当玩家移动超过 <see cref="AoiHysteresisChunks"/> 个 chunk（48m）才重新计算订阅，
        /// 避免每 16m 就触发 1331 chunk 的 diff 计算和网络包上行。
        /// </para>
        /// </summary>
        /// <param name="x">本地玩家当前世界 X 坐标（米）</param>
        /// <param name="y">本地玩家当前世界 Y 坐标（米）</param>
        /// <param name="z">本地玩家当前世界 Z 坐标（米）</param>
        private void OnPlayerChunkChanged(float x, float y, float z)
        {
            try
            {
                const int ViewRadiusChunks = 28;
                const float MetresPerChunkCell = 16f;

                // 滞后防抖：计算当前 chunk 坐标，与上次订阅中心比较。
                int curCX = (int)MathF.Floor(x / MetresPerChunkCell);
                int curCY = (int)MathF.Floor(y / MetresPerChunkCell);
                int curCZ = (int)MathF.Floor(z / MetresPerChunkCell);

                // 滞后检查：首次订阅或移动超过阈值时才触发更新
                if (_localPlayerSubscribedChunks != null && _localPlayerSubscribedChunks.Count > 0)
                {
                    int dx = Math.Abs(curCX - _lastSubCenterCX);
                    int dy = Math.Abs(curCY - _lastSubCenterCY);
                    int dz = Math.Abs(curCZ - _lastSubCenterCZ);
                    if (dx < AoiHysteresisChunks && dy < AoiHysteresisChunks && dz < AoiHysteresisChunks)
                    {
                        return; // 未超过滞后阈值，跳过本次订阅更新
                    }
                }

                // ── 位置驱动订阅优化 ──
                // 原实现在主线程调用 ComputeChunksInView(R=28) 创建 185,193 个条目的 HashSet，
                // 每次分配 ~3-4MB → GC 暂停 → 卡顿。
                // 优化：位置驱动订阅下（hasPosition=true），服务端 grain 侧自动计算 AOI 差异，
                // 客户端无需计算完整 chunk 视图，只发送位置（24 字节）即可。
                // 本地仅更新 chunk 坐标用于滞后判断，不分配任何集合。
                _lastSubCenterCX = curCX;
                _lastSubCenterCY = curCY;
                _lastSubCenterCZ = curCZ;

                // 标记为"已订阅"（用单元素集合代替 185,193 元素的 HashSet，仅用于滞后检查的 Count > 0 判断）
                if (_localPlayerSubscribedChunks == null || _localPlayerSubscribedChunks.Count == 0)
                {
                    _localPlayerSubscribedChunks = new HashSet<ulong> { 0 }; // 占位元素
                    Debug.Log($"[HundunWorldGame] 初始位置驱动 AOI 订阅: radius={ViewRadiusChunks}, Pos=({x:F1},{y:F1},{z:F1})");
                }

                // 异步发送位置驱动订阅更新（非阻塞，避免影响 FixedUpdate 节奏）
                // 只传玩家位置（24 字节），不传 chunk key 数组（~416KB），零主线程分配
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _networkManager.SendSubscriptionUpdateAsync(
                            Array.Empty<ulong>(), Array.Empty<ulong>(),
                            x, y, z, hasPosition: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[HundunWorldGame] SendSubscriptionUpdateAsync 失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] OnPlayerChunkChanged 异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 计算以世界坐标 (worldX, worldY, worldZ) 为中心、半径 radiusChunks 内的所有 chunk 的 MortonKey 集合。
        /// <para>
        /// 与服务端 <c>WorldChunkAoi.GetChunksInView</c> + <c>MortonCodec.Encode3D</c> 完全等价。
        /// 此处内联实现以避免 Flax 客户端引用的预编译 Horizon.Game.Core.dll 可能过时
        /// 导致 <c>WorldCoord</c> 类型不可见（CS0234）。
        /// </para>
        /// <para>
        /// 常量：<c>MetresPerChunkCell = 16</c>；Morton 偏移 <c>AxisBias = 2^20</c>。
        /// </para>
        /// </summary>
        private static HashSet<ulong> ComputeChunksInView(float worldX, float worldY, float worldZ, int radiusChunks)
        {
            const float MetresPerChunkCell = 16f;
            const int AxisBias = 1 << 20; // 2^20，与 MortonCodec.AxisBias 一致

            // 1. 世界坐标 → chunk 整数坐标（Floor 语义）
            int cx = (int)MathF.Floor(worldX / MetresPerChunkCell);
            int cy = (int)MathF.Floor(worldY / MetresPerChunkCell);
            int cz = (int)MathF.Floor(worldZ / MetresPerChunkCell);

            if (radiusChunks < 0) radiusChunks = 0;

            var chunks = new HashSet<ulong>();
            for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
            {
                for (int dy = -radiusChunks; dy <= radiusChunks; dy++)
                {
                    for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
                    {
                        chunks.Add(MortonEncode3D(cx + dx, cy + dy, cz + dz));
                    }
                }
            }
            return chunks;
        }

        /// <summary>
        /// 3D Morton 编码（与 MortonCodec.Encode3D 等价）。
        /// 把 (x, y, z) 三个 21 位有符号块坐标打包到一个 ulong 的低 63 位中。
        /// </summary>
        private static ulong MortonEncode3D(int x, int y, int z)
        {
            const int AxisBias = 1 << 20; // 2^20
            var ux = (ulong)(x + AxisBias);
            var uy = (ulong)(y + AxisBias);
            var uz = (ulong)(z + AxisBias);
            return Part1By2(ux) | (Part1By2(uy) << 1) | (Part1By2(uz) << 2);
        }

        /// <summary>在 21 位输入 n 的每个位中间插入 2 个 0（n 的位 i → 输出的位 3i）。</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong Part1By2(ulong n)
        {
            n &= 0x1FFFFFUL;                                  // 保留低 21 位
            n = (n | (n << 32)) & 0x1F00000000FFFFUL;
            n = (n | (n << 16)) & 0x1F0000FF0000FFUL;
            n = (n | (n << 8))  & 0x100F00F00F00F00FUL;
            n = (n | (n << 4))  & 0x10C30C30C30C30C3UL;
            n = (n | (n << 2))  & 0x1249249249249249UL;
            return n;
        }

        /// <summary>
        /// 创建本地玩家 Arch ECS 实体。
        /// 注意：服务端 BroadcastEntityLifecycleAsync step 2 会补发含自身的 Spawn delta，
        /// SnapshotApplySystem.HandleSpawn 会检测并收养此实体（注册到字典），而非重复创建。
        /// 参照 <see cref="Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem.HandleSpawn"/> 的本地玩家分支：
        /// 添加 NetworkIdentityComponent(IsLocalPlayer=true) + AuthTransformComponent +
        /// PlayerInputComponent + PredictedTransformComponent，不添加 InterpolatedTransformComponent
        /// （本地玩家走预测管线，不插值）。
        /// </summary>
        /// <returns>地面对齐后的 Flax Y（上下）坐标，供握手包使用，确保服务端初始位置与客户端一致。</returns>
        public float CreateLocalPlayerEntity(ulong characterId, float initialX, float initialY, float initialZ)
        {
            if (_archWorld == null)
            {
                Debug.LogError("[HundunWorldGame] CreateLocalPlayerEntity 失败：Arch World 未初始化");
                return initialY;
            }

            // 修复（角色无法移动 — 重复本地玩家实体）：
            // 若 Arch World 中已存在同 EntityId 的本地玩家实体（由 HandleSpawn 收养逻辑创建），
            // 不重复创建，避免 InputSendSystem 每帧多发全零输入包导致服务端零输入覆盖正确输入。
            var checkQuery = new Arch.Core.QueryDescription()
                .WithAll<NetworkIdentityComponent, PlayerInputComponent, Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>();
            bool alreadyExists = false;
            _archWorld.Query(in checkQuery, (Arch.Core.Entity e, ref NetworkIdentityComponent nid) =>
            {
                if (nid.EntityId == characterId && nid.IsLocalPlayer)
                    alreadyExists = true;
            });
            if (alreadyExists)
            {
                Debug.Log($"[HundunWorldGame] CreateLocalPlayerEntity 跳过：CharacterId={characterId} 的本地玩家实体已存在");
                return initialY;
            }

            // 初始位置对齐地面：防止开局 pred.Z 低于 Terrain，第一帧重力下落就穿透。
            // 坐标转换：initialX/Y/Z 为 Flax Y-up（X=左右, Y=上下, Z=前后），
            // SampleGroundHeightEcs 接受 ECS Z-up（x=左右, y=前后），返回 ECS.Z（= Flax.Y 上下）。
            // 因此传入 (initialX, initialZ)，返回值 groundZ 是 ECS 上下高度。
            var groundEcsZ = SampleGroundHeightEcs(initialX, initialZ);
            // 若采样失败（NaN），兜底用 initialY（Flax 上下）作为初始高度
            var finalEcsZ = float.IsNaN(groundEcsZ) ? initialY : groundEcsZ;
            if (!float.IsNaN(groundEcsZ) && MathF.Abs(groundEcsZ - initialY) > 0.01f)
            {
                Debug.Log($"[HundunWorldGame] 初始位置对齐地面: initialY={initialY:F3} → groundZ={groundEcsZ:F3} (ECS.Z)");
            }

            var entity = _archWorld.Create();

            var netId = new NetworkIdentityComponent
            {
                EntityId = characterId,
                IsLocalPlayer = true,
            };
            _archWorld.Add(entity, netId);

            var authTransform = new Horizon.Game.Message.Sync.Components.AuthTransformComponent
            {
                X = initialX,
                Y = finalEcsZ,
                Z = initialZ,
                Pitch = 0f,
                Yaw = 0f,
                Roll = 0f,
                ServerTick = 0,
            };
            _archWorld.Add(entity, authTransform);

            var input = new PlayerInputComponent
            {
                MoveX = 0f,
                MoveY = 0f,
                LookYaw = 0f,
                LookPitch = 0f,
                JumpPressed = false,
                InputBits = 0,
            };
            _archWorld.Add(entity, input);

            var predicted = new Horizon.Game.ECS.Arch.Components.PredictedTransformComponent
            {
                // initialX/Y/Z 来自握手（Flax Y-up：X=左右, Y=上下, Z=前后），与 AuthTransformComponent 同坐标系；
                // PredictedTransformComponent 供 MovementFormula 使用，必须为 ECS Z-up（X=左右, Y=前后, Z=上下）。
                // 因此 Y/Z 交换：Y(前后) ← initialZ, Z(上下) ← initialY，
                // 与 SnapshotApplySystem.HandleSpawn 的本地玩家分支保持一致。
                // 修复：Z 用 finalEcsZ（已对齐地面），防止开局穿透。
                X = initialX,
                Y = initialZ,
                Z = finalEcsZ,
                Vz = 0f,
                Yaw = 0f,
                Pitch = 0f,
                ClientTick = 0,
                NeedsReconciliation = false,
            };
            _archWorld.Add(entity, predicted);

            Debug.Log($"[HundunWorldGame] 已创建本地玩家 ECS 实体: CharacterId={characterId}, Pos=({initialX},{initialY},{initialZ}), GroundAlignedZ={finalEcsZ:F3}, Entity={entity}");

            // 修复（角色无法真正移动）：返回地面对齐后的 Flax Y（上下）坐标。
            // 握手包必须使用此值（而非原始 CharacterInfo.Position.Y），
            // 否则服务端 RegisterEntityAsync 的初始 ECS.Z 与客户端 PredictedTransformComponent.Z 不一致，
            // 导致首帧 drift 超阈值触发 Correction 风暴，角色被拉回服务端初始高度。
            return finalEcsZ; // finalEcsZ 是 ECS.Z（= Flax.Y 上下）
        }

        /// <summary>
        /// 请求创建本地玩家 Flax Actor。
        /// 若当前已在 GameWorld 场景，立即创建；否则缓存请求，待场景切换到 GameWorld 完成后创建。
        /// 这样可避免 Actor 被旧场景卸载时销毁。
        /// </summary>
        public void RequestCreateLocalPlayerActor(ulong characterId, float x, float y, float z)
        {
            // 新会话开始：清掉上一会话遗留的远程角色 Actor。
            // FlaxActorSyncSystem 挂在常驻 RootScene 上，退世界不会触发其 OnDestroy，
            // 旧 Actor 若不在此处主动清理，会跨会话堆积成"离线/不存在角色"的幽灵。
            // （本地玩家 Actor 有下方的显式销毁，远程角色此前无人负责。）
            var flaxSyncForPurge = FlaxActorSyncSystem.Instance;
            if (flaxSyncForPurge != null)
            {
                flaxSyncForPurge.ClearAllActors();
                Debug.Log("[HundunWorldGame] 新会话开始，已清理上一会话遗留的远程角色 Actor");
            }

            // 若已存在本地玩家 Actor，先销毁
            if (_localPlayerActor != null)
            {
                DestroyActorRecursive(_localPlayerActor);
                _localPlayerActor = null;
            }

            // 检查当前是否已在 GameWorld 场景
            var sceneManager = UI.GameSceneManager.Instance;
            bool inGameWorld = sceneManager != null && sceneManager.CurrentSceneType == SceneType.GameWorld;

            // 额外检查：即使 GameSceneManager 认为不在 GameWorld，也检查 Level 中是否已有 World 场景
            // 这可以处理 GameSceneManager 状态更新滞后的情况
            if (!inGameWorld)
            {
                for (int i = 0; i < Level.ScenesCount; i++)
                {
                    var scene = Level.GetScene(i);
                    if (scene != null && scene.Name == "World")
                    {
                        inGameWorld = true;
                        Debug.Log($"[HundunWorldGame] 检测到 World 场景已加载，视为已在 GameWorld");
                        break;
                    }
                }
            }

            if (inGameWorld)
            {
                Debug.Log("[HundunWorldGame] 当前已在 GameWorld，立即创建本地玩家 Actor");
                CreateLocalPlayerActor(characterId, x, y, z);
                return;
            }

            // 若 GameSceneManager 不可用，直接立即创建（兜底，避免永远不创建）
            if (sceneManager == null)
            {
                Debug.LogWarning("[HundunWorldGame] GameSceneManager.Instance 为空，立即创建本地玩家 Actor（可能不在 GameWorld 场景）");
                CreateLocalPlayerActor(characterId, x, y, z);
                return;
            }

            // 缓存请求，等待场景切换完成
            _hasPendingLocalPlayerActorRequest = true;
            _pendingLocalPlayerCharacterId = characterId;
            _pendingLocalPlayerX = x;
            _pendingLocalPlayerY = y;
            _pendingLocalPlayerZ = z;
            _pendingRequestTime = Time.GameTime;
            Debug.Log($"[HundunWorldGame] 已缓存本地玩家 Actor 创建请求: CharacterId={characterId}, 等待场景切换到 GameWorld");

            // 订阅场景切换完成事件（若尚未订阅）
            EnsureSceneTransitionSubscription();

            // 防御性检查：如果场景切换已经在等待完成（_isTransitioning 为 false 且 CurrentSceneType 已更新），
            // 直接执行创建，避免事件已触发过导致永远等待
            if (!sceneManager.IsTransitioning && sceneManager.CurrentSceneType == SceneType.GameWorld)
            {
                Debug.Log("[HundunWorldGame] 订阅后发现场景切换已完成，立即创建本地玩家 Actor");
                _hasPendingLocalPlayerActorRequest = false;
                CreateLocalPlayerActor(characterId, x, y, z);
            }
        }

        /// <summary>
        /// 应用本地玩家角色属性到 CharacterAttributesComponent。
        /// 由 EnterGameHandler 在收到 EnterGameResponse 后调用，用于回填服务端返回的角色名/等级/成长阶段。
        /// 处理两种时序：
        /// 1) 若 LocalPlayerActor 已创建（同步路径），立即回填到 CharacterAttributesComponent；
        /// 2) 若 LocalPlayerActor 未创建（异步路径，等待场景切换），先缓存属性，
        ///    待 CreateLocalPlayerActor 完成创建并挂载 CharacterAttributesComponent 后自动应用。
        /// </summary>
        /// <param name="nickname">角色昵称（来自服务端 CharacterInfo.CharacterName，可为 null）</param>
        /// <param name="level">角色等级（来自服务端 CharacterInfo.Level）</param>
        /// <param name="stage">成长阶段（由客户端按 Level 推算）</param>
        public void ApplyLocalPlayerAttributes(string nickname, int level, CharacterStage stage)
        {
            if (_localPlayerActor != null)
            {
                var attrComp = _localPlayerActor.GetScript<CharacterAttributesComponent>();
                if (attrComp != null)
                {
                    attrComp.Nickname = nickname ?? attrComp.Nickname;
                    attrComp.Level = level;
                    attrComp.CurrentStage = stage;
                    Debug.Log($"[HundunWorldGame] 已回填本地玩家属性: Nickname={attrComp.Nickname}, Level={attrComp.Level}, Stage={attrComp.CurrentStage}");
                }
                else
                {
                    Debug.LogWarning("[HundunWorldGame] LocalPlayerActor 已创建但未挂载 CharacterAttributesComponent，回填失败");
                }
                _hasPendingLocalPlayerAttributes = false;
            }
            else
            {
                // LocalPlayerActor 尚未创建（RequestCreateLocalPlayerActor 走了异步路径），缓存待应用
                _hasPendingLocalPlayerAttributes = true;
                _pendingLocalPlayerNickname = nickname;
                _pendingLocalPlayerLevel = level;
                _pendingLocalPlayerStage = stage;
                Debug.Log($"[HundunWorldGame] LocalPlayerActor 尚未创建，缓存待应用属性: Nickname={nickname}, Level={level}, Stage={stage}");
            }
        }

        /// <summary>
        /// 确保已订阅 GameSceneManager.TransitionCompleted 事件
        /// </summary>
        private void EnsureSceneTransitionSubscription()
        {
            var sceneManager = UI.GameSceneManager.Instance;
            if (sceneManager == null)
            {
                Debug.LogWarning("[HundunWorldGame] GameSceneManager.Instance 为空，无法订阅场景切换事件");
                return;
            }

            // 避免重复订阅：先移除再添加
            sceneManager.TransitionCompleted -= OnSceneTransitionCompleted;
            sceneManager.TransitionCompleted += OnSceneTransitionCompleted;
        }

        /// <summary>
        /// 场景切换完成回调：若切换到 GameWorld 且有待创建的本地玩家 Actor 请求，则执行创建
        /// </summary>
        private void OnSceneTransitionCompleted(SceneType previousScene, SceneType targetScene)
        {
            if (targetScene != SceneType.GameWorld)
                return;

            if (!_hasPendingLocalPlayerActorRequest)
                return;

            // 取消订阅（一次性）
            var sceneManager = UI.GameSceneManager.Instance;
            if (sceneManager != null)
            {
                sceneManager.TransitionCompleted -= OnSceneTransitionCompleted;
            }

            // 执行创建
            _hasPendingLocalPlayerActorRequest = false;
            var characterId = _pendingLocalPlayerCharacterId;
            var x = _pendingLocalPlayerX;
            var y = _pendingLocalPlayerY;
            var z = _pendingLocalPlayerZ;

            Debug.Log($"[HundunWorldGame] 场景已切换到 GameWorld，开始创建本地玩家 Actor: CharacterId={characterId}");
            CreateLocalPlayerActor(characterId, x, y, z);
        }

        /// <summary>
        /// 每帧更新（由外部脚本驱动）。包含待创建本地玩家 Actor 请求的超时兜底逻辑。
        /// </summary>
        public void OnUpdate()
        {
            // 外观补传的重试泵：网络在角色生成之后才就绪时，靠这里把挂起的提交补上。
            try
            {
                HundunWorld.Game.Services.MetaHumanAppearanceService.Instance.TickPendingHeals();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HundunWorldGame] 外观补传重试失败：{ex.Message}");
            }

            // 超时兜底：如果等待场景切换超过 10 秒仍未生成角色，主动触发
            if (_hasPendingLocalPlayerActorRequest && _pendingRequestTime > 0)
            {
                float elapsed = Time.GameTime - _pendingRequestTime;
                if (elapsed >= 10.0f)
                {
                    Debug.LogWarning($"[HundunWorldGame] 等待场景切换超时 ({elapsed:F1}s)，触发兜底生成本地玩家 Actor");
                    _hasPendingLocalPlayerActorRequest = false;
                    var characterId = _pendingLocalPlayerCharacterId;
                    var x = _pendingLocalPlayerX;
                    var y = _pendingLocalPlayerY;
                    var z = _pendingLocalPlayerZ;
                    CreateLocalPlayerActor(characterId, x, y, z);
                    _pendingRequestTime = 0;
                }
            }

            // 地面重对齐重试：地形碰撞体就绪后立即把角色从半空出生高度贴地并上报服务端
            if (_hasPendingGroundRealign)
            {
                float now = Time.GameTime;
                if (now >= _pendingGroundRealignDeadline)
                {
                    _hasPendingGroundRealign = false;
                    Debug.LogWarning("[HundunWorldGame] 地面重对齐重试超时（30s），角色可能仍悬空");
                }
                else if (now >= _pendingGroundRealignNextTime)
                {
                    _pendingGroundRealignNextTime = now + 1f;
                    RealignLocalPlayerToGround(_pendingGroundRealignCharacterId, _pendingGroundRealignX, _pendingGroundRealignZ);
                }
            }


            // 技能效果计时：SkillEffectSystem 自己不驱动，唯一入口是 CombatSystemManager.Update，
            // 而它此前没有任何调用方（原先的转发者 CombatSystemCoordinator 本身是零引用死链），
            // 于是 DoT/HoT/增益既不跳伤也不到期。挂在这个每帧点上，效果才会真正走完生命周期。
            Combat.CombatSystemManager.Instance.Update(Time.DeltaTime);

            // POI 发现：玩家进入 POI 的发现半径即标记已发现（内部自行限频）
            Navigation.MapDiscoveryDriver.Tick(Time.DeltaTime, _localPlayerActor);

            // 设置的 5s 自动保存（GameStateManager 内部节流）：此前没有任何调用方，
            // 自动保存写了却永远不会触发；挂在唯一每帧点上，改完音量不点"保存"也不会丢。
            HundunWorld.Game.Core.GameStateManager.Instance.Update(Time.DeltaTime);

            // 控制状态倒计时：本地技能效果（3s 眩晕等）与权威状态 TTL 都靠它到期回收，
            // 此前同样零调用方——状态一旦施加就永远挂着。
            HundunWorld.Game.Character.ControlStateManager.Instance.Update(Time.DeltaTime);
        }

        /// <summary>
        /// 本地玩家角色 Prefab 路径（CharacterRoot 包含 AnimatedModel、PlayerController 等组件）
        /// 注意：Flax Engine 打包后资源路径不带扩展名，带扩展名的路径在 GetAssetInfo 中查找可能失败
        /// </summary>
        // 修复：必须带 .prefab 扩展名，否则 Flax Content API 会把无扩展名路径解析为绝对路径导致找不到文件
        private const string LocalPlayerPrefabPath = "Content/Prefabs/Character/CharacterRoot.prefab";

        /// <summary>
        /// 本地玩家角色 Prefab 的 GUID（运行时通过 Content.GetAssetInfo 从路径动态获取真实 GUID，避免 .NET Guid 字节序与 Flax C++ GUID 不一致问题）
        /// </summary>
        private static Guid LocalPlayerPrefabGuid = Guid.Empty;

        /// <summary>
        /// 加载 Content 资源。优先同步加载，然后通过 GetAssetInfo 转换路径后加载，
        /// 最后回退到异步加载并等待完成。
        /// 修复：
        /// 1) 同步加载返回的 Asset 也需检查 IsLoaded，确保资源数据就绪；
        /// 2) GetAssetInfo 对带扩展名路径在打包后可能失败，增加去掉扩展名的回退尝试；
        /// 3) LoadAsync 返回的 Asset 必须等待 IsLoaded 后再返回，避免调用方使用未就绪的资源。
        /// </summary>
        public static T LoadContentWithFallback<T>(string primaryPath) where T : Asset
        {
            // 1) 直接用路径同步加载
            var syncAsset = Content.Load<T>(primaryPath);
            if (syncAsset != null && syncAsset.IsLoaded)
            {
                return syncAsset;
            }

            // 2) 通过 GetAssetInfo 将路径转为 GUID，再同步加载
            //    打包后 GetAssetInfo 对带扩展名路径可能失败，尝试去掉扩展名再查找
            if (TryGetAssetGuidFromPath(primaryPath, out var assetGuid))
            {
                var asset = Content.Load<T>(assetGuid);
                if (asset != null && asset.IsLoaded)
                {
                    return asset;
                }
            }

            // 3) 最后尝试异步加载 + 等待加载完成
            var asyncAsset = Content.LoadAsync<T>(primaryPath);
            if (asyncAsset != null)
            {
                // 等待资源加载完成（最多30秒）
                if (asyncAsset.WaitForLoaded(30000.0))
                {
                    return asyncAsset;
                }
                Debug.LogWarning($"[ContentLoad] 异步资源加载超时: {primaryPath}");
                return null;
            }

            // 4) 若 primaryPath 带扩展名，尝试无扩展名路径异步加载
            var pathWithoutExt = RemoveFileExtension(primaryPath);
            if (pathWithoutExt != primaryPath)
            {
                asyncAsset = Content.LoadAsync<T>(pathWithoutExt);
                if (asyncAsset != null)
                {
                    if (asyncAsset.WaitForLoaded(30000.0))
                    {
                        return asyncAsset;
                    }
                    Debug.LogWarning($"[ContentLoad] 异步资源加载超时（无扩展名路径）: {pathWithoutExt}");
                    return null;
                }
            }

            Debug.LogWarning($"[ContentLoad] 所有加载方式均失败: {primaryPath}");
            return null;
        }

        /// <summary>
        /// 尝试从路径获取资源 GUID。先尝试原始路径，若失败且路径带扩展名则去掉扩展名再试。
        /// 打包后 GetAssetInfo 对带扩展名路径可能失败，需要去掉扩展名重试。
        /// </summary>
        public static bool TryGetAssetGuidFromPath(string path, out Guid guid)
        {
            // 先用原始路径查找
            if (Content.GetAssetInfo(path, out var assetInfo))
            {
                guid = assetInfo.ID;
                return true;
            }

            // 去掉扩展名再试
            var pathWithoutExt = RemoveFileExtension(path);
            if (pathWithoutExt != path && Content.GetAssetInfo(pathWithoutExt, out assetInfo))
            {
                guid = assetInfo.ID;
                return true;
            }

            guid = Guid.Empty;
            return false;
        }

        /// <summary>
        /// 去掉文件路径的扩展名。例如 "Content/Foo/Bar.prefab" → "Content/Foo/Bar"
        /// </summary>
        private static string RemoveFileExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var lastDot = path.LastIndexOf('.');
            if (lastDot < 0) return path;
            // 确保点号在路径最后一段（不是目录中的点）
            var lastSlash = path.LastIndexOf('/');
            var lastBackslash = path.LastIndexOf('\\');
            var lastSeparator = Math.Max(lastSlash, lastBackslash);
            if (lastDot < lastSeparator) return path;
            return path.Substring(0, lastDot);
        }

        /// <summary>
        /// 通过 GUID 同步加载 Content 资源，并等待加载完成。
        /// GUID 在打包后始终有效，不受路径格式差异影响。
        /// 修复：Content.Load 只返回引用，必须 WaitForLoaded 确保资产数据已就绪。
        /// </summary>
        public static T LoadContentByGuid<T>(Guid assetGuid) where T : Asset
        {
            if (assetGuid == Guid.Empty)
            {
                Debug.LogWarning("[ContentLoad] GUID 为空");
                return null;
            }

            T asset = null;
            try
            {
                asset = Content.Load<T>(assetGuid);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ContentLoad] GUID 加载异常: {assetGuid}, {ex.Message}");
            }

            if (asset == null)
            {
                Debug.LogWarning($"[ContentLoad] GUID 加载失败: {assetGuid}");
                return null;
            }

            // 关键修复：等待资产数据加载完成
            if (!asset.IsLoaded)
            {
                Debug.Log($"[ContentLoad] GUID 资产引用已获取，等待数据加载: {assetGuid}");
                if (!asset.WaitForLoaded(30000.0))
                {
                    Debug.LogWarning($"[ContentLoad] GUID 资产加载超时: {assetGuid}");
                    // 仍返回引用，让调用方决定是否使用
                }
            }

            return asset;
        }

        /// <summary>
        /// 统一资产完全加载辅助方法。优先通过 GUID 同步加载，失败后按路径同步、路径异步、
        /// 无扩展名路径异步的顺序尝试，仅当资产 <see cref="Asset.IsLoaded"/> 为 true 时才返回。
        /// 用于修复打包构建中资产未加载就被使用而触发引擎断言的问题。
        /// </summary>
        /// <summary>按项目内路径加载资产：统一转绝对路径再交给 Content（内容库按绝对路径登记，见 MetaHumanAssetUtil.ResolveProjectPath）。</summary>
        public static T LoadAssetAt<T>(string path, float timeoutSeconds = 10000.0f) where T : Asset
        {
            return Content.Load<T>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path), timeoutSeconds);
        }

        public static T LoadAssetFullyLoaded<T>(Guid guid, string path) where T : Asset
        {
            // 统一成绝对路径：本工程内容库按绝对路径登记，相对写法会另起一条重复条目并触发 ID 改写（#77）。
            path = MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path);

            // 0. 诊断：检查 GUID 对应的资产是否存在但类型不匹配（例如把 Model 当 SkinnedModel 用）
            bool typeMismatch = false;
            if (guid != Guid.Empty)
            {
                var rawAsset = Content.Load<Asset>(guid);
                if (rawAsset != null && !(rawAsset is T))
                {
                    typeMismatch = true;
                    Debug.LogError($"[LoadAssetFullyLoaded][CRITICAL] 资产类型不匹配：GUID={guid} 期望 {typeof(T).Name}，实际是 {rawAsset.GetType().Name}。请检查 Prefab 引用或在 Flax Editor 中重新导入源文件为正确的资产类型。");

                    // 类型不匹配时，尝试从 Content 中自动查找一个可用的同类型资产作为兜底
                    var fallbackByType = FindFirstAssetByType<T>();
                    if (fallbackByType != null)
                    {
                        Debug.LogWarning($"[LoadAssetFullyLoaded] 类型不匹配，自动使用项目中的 {typeof(T).Name} 兜底: {fallbackByType.Path}");
                        return fallbackByType;
                    }
                }
            }

            // 1. 优先通过 GUID 加载。GUID 在编辑器和打包构建中都最可靠。
            if (guid != Guid.Empty)
            {
                var asset = Content.Load<T>(guid);
                if (asset != null)
                {
                    if (WaitForAssetLoaded(asset, 30000.0, $"GUID:{guid}"))
                    {
                        Debug.Log($"[LoadAssetFullyLoaded] GUID 资产加载完成: {guid}, Path={asset.Path}");
                        return asset;
                    }
                    Debug.LogWarning($"[LoadAssetFullyLoaded] GUID 资产未在超时内加载完成: {guid}");
                }
            }

            // 2. GUID 失败或未加载，尝试路径同步加载
            if (!string.IsNullOrEmpty(path))
            {
                var asset = Content.Load<T>(path);
                if (asset != null && WaitForAssetLoaded(asset, 30000.0, $"Path:{path}"))
                {
                    Debug.Log($"[LoadAssetFullyLoaded] 路径资产加载完成: {path}");
                    return asset;
                }

                // 3. 同步路径失败，尝试异步路径加载
                asset = Content.LoadAsync<T>(path);
                if (asset != null && WaitForAssetLoaded(asset, 30000.0, $"AsyncPath:{path}"))
                {
                    Debug.Log($"[LoadAssetFullyLoaded] 异步路径资产加载完成: {path}");
                    return asset;
                }

                // 4. 尝试添加 .flax 后缀（如果路径没有后缀）
                // FlaxEngine 的 Content.Load 需要带 .flax 后缀才能正确加载资产
                var pathWithExt = System.IO.Path.HasExtension(path)
                    ? path
                    : path + ".flax";
                if (!string.IsNullOrEmpty(pathWithExt) && pathWithExt != path)
                {
                    asset = Content.LoadAsync<T>(pathWithExt);
                    if (asset != null && WaitForAssetLoaded(asset, 30000.0, $"AsyncPathWithExt:{pathWithExt}"))
                    {
                        Debug.Log($"[LoadAssetFullyLoaded] 带后缀异步路径资产加载完成: {pathWithExt}");
                        return asset;
                    }
                }
            }

            // 5. 最后兜底：枚举 Content 中所有该类型资产，返回第一个已加载的
            var fallbackAsset = FindFirstAssetByType<T>();
            if (fallbackAsset != null)
            {
                Debug.LogWarning($"[LoadAssetFullyLoaded] 通过类型枚举找到兜底资产: {fallbackAsset.Path}");
                return fallbackAsset;
            }

            Debug.LogError($"[LoadAssetFullyLoaded] 无法加载资产: GUID={guid}, Path={path}");
            return null;
        }

        /// <summary>
        /// 等待资产加载完成，捕获异常并返回是否成功。
        /// </summary>
        private static bool WaitForAssetLoaded(Asset asset, double timeoutMs, string context)
        {
            if (asset == null) return false;
            if (asset.IsLoaded) return true;

            try
            {
                Debug.Log($"[LoadAssetFullyLoaded] 等待资产加载: {context}, Path={asset.Path}");
                if (asset.WaitForLoaded(timeoutMs))
                {
                    return asset.IsLoaded;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoadAssetFullyLoaded] 等待资产加载异常: {context}, {ex.Message}");
            }

            return asset.IsLoaded;
        }

        /// <summary>
        /// 创建本地玩家 Flax Actor（基于 CharacterRoot.prefab）。
        /// PlayerController 等 Script 挂在角色 Actor 上，需要 Actor 先存在。
        /// 与 <see cref="CreateLocalPlayerEntity"/> 配合：ECS 实体用于网络同步，Actor 用于本地表现与控制。
        /// 修复要点：
        /// 1) 严格预加载 SkinnedModel（及 AnimationGraph），只有资产 IsLoaded 才继续；
        /// 2) SkinnedModel 加载失败时，不生成引用未加载资源的 CharacterRoot.prefab，改用空 Actor 兜底；
        /// 3) 生成后校验 AnimatedModel 状态，只把已加载的资产赋给它，未加载时禁用组件防止断言。
        /// </summary>
        public Actor CreateLocalPlayerActor(ulong characterId, float x, float y, float z)
        {
            // 若已存在本地玩家 Actor，先销毁以避免重复创建
            if (_localPlayerActor != null)
            {
                Debug.LogWarning($"[HundunWorldGame] 本地玩家 Actor 已存在，将销毁旧 Actor 后重建: CharacterId={characterId}");
                DestroyActorRecursive(_localPlayerActor);
                _localPlayerActor = null;
            }

            var position = new Vector3(x, y, z);

            // ── 尝试预加载 SkinnedModel 和 AnimationGraph（作为回退备用）──
            // 关键变更：不再要求预加载成功才生成 Prefab。Prefab 自身已引用这些资产，
            // 打包构建中 Prefab 引用通常比硬编码 GUID/路径更可靠。预加载仅作为降级替补。
            Debug.Log("[HundunWorldGame] 开始预加载角色资产（备用）...");
            // 世界身体优先用 Ada（完整 MetaHuman 蒙皮 + 已绑好的 ALS 重定向）；
            // 加载失败再退回 Character_1，保证进世界不因资产问题失败。
            var preloadedSkinnedModel = LoadAssetFullyLoaded<SkinnedModel>(Guid.Empty, EquipmentDatabase.TemplateSkinnedBodyModelPath)
                                        ?? LoadAssetFullyLoaded<SkinnedModel>(SkinnedModelGuid, EquipmentDatabase.MaleBodyModelPath);
            var preloadedAnimGraph = LoadAssetFullyLoaded<AnimationGraph>(AnimationGraphGuid, "Content/Character/Models/Animation Graph.flax");

            Debug.Log($"[HundunWorldGame] 预加载 SkinnedModel: {(preloadedSkinnedModel != null ? preloadedSkinnedModel.Path : "FAILED")}, IsLoaded={(preloadedSkinnedModel?.IsLoaded.ToString() ?? "N/A")}");
            Debug.Log($"[HundunWorldGame] 预加载 AnimationGraph: {(preloadedAnimGraph != null ? preloadedAnimGraph.Path : "FAILED")}, IsLoaded={(preloadedAnimGraph?.IsLoaded.ToString() ?? "N/A")}");

            Actor actor = null;
            AnimatedModel animatedModel = null;

            // 修复：优先从路径动态获取真实 GUID，绕过 .NET Guid 字节序与 Flax C++ GUID 不一致问题
            // .NET 的 new Guid(string) 内部按小端序存储前 3 段，传给 Flax C++ 后被按 4 字节块反转解释，导致 GUID 完全不同
            if (LocalPlayerPrefabGuid == Guid.Empty)
            {
                if (TryGetAssetGuidFromPath(LocalPlayerPrefabPath, out var realGuid))
                {
                    LocalPlayerPrefabGuid = realGuid;
                    Debug.Log($"[HundunWorldGame] 从路径动态获取到 Prefab 真实 GUID: {realGuid}");
                }
                else
                {
                    Debug.LogWarning($"[HundunWorldGame] 无法从路径获取 Prefab GUID，将使用路径加载: {LocalPlayerPrefabPath}");
                }
            }

            // 加载 Prefab。使用 LoadAssetFullyLoaded 统一处理 GUID + 路径 fallback + 类型兜底
            var prefab = LoadAssetFullyLoaded<Prefab>(LocalPlayerPrefabGuid, LocalPlayerPrefabPath);

            // Prefab 资产本身也必须加载完成才能生成
            if (prefab != null && !prefab.IsLoaded)
            {
                Debug.Log("[HundunWorldGame] Prefab 尚未加载完成，等待加载...");
                if (!WaitForAssetLoaded(prefab, 30000.0, $"Prefab:{LocalPlayerPrefabGuid}"))
                {
                    Debug.LogError("[HundunWorldGame] Prefab 等待加载超时");
                    prefab = null;
                }
            }

            if (prefab == null)
            {
                Debug.LogError($"[HundunWorldGame] 无法加载 Prefab (GUID={LocalPlayerPrefabGuid}, Path={LocalPlayerPrefabPath})，所有加载方式均失败，使用兜底角色");
                actor = CreateFallbackCharacterActor(position, preloadedSkinnedModel, preloadedAnimGraph);
            }
            else
            {
                // 生成到 World 场景
                var targetScene = FindGameWorldScene();
                if (targetScene != null)
                {
                    actor = PrefabManager.SpawnPrefab(prefab, position, Quaternion.Identity);
                    if (actor != null && actor.Scene != targetScene)
                    {
                        Debug.Log($"[HundunWorldGame] Actor 当前在场景: {actor.Scene?.Name ?? "null"}，移动到 World 场景");
                        Level.SpawnActor(actor, targetScene);
                    }
                }
                else
                {
                    Debug.LogError("[HundunWorldGame] 未找到玩法场景，本地玩家将落入 RootScene（切场景不卸载，请检查 World 场景是否已加载）");
                    actor = PrefabManager.SpawnPrefab(prefab, position, Quaternion.Identity);
                }

                if (actor == null)
                {
                    Debug.LogError("[HundunWorldGame] PrefabManager.SpawnPrefab 返回 null，使用兜底角色");
                    actor = CreateFallbackCharacterActor(position, preloadedSkinnedModel, preloadedAnimGraph);
                }
                else
                {
                    actor.Position = position;
                    actor.Orientation = Quaternion.Identity;

                    // 获取 Prefab 中的身体 AnimatedModel；如果找不到则新建一个（头部是独立 Actor，不能选它）
                    animatedModel = FindBodyAnimatedModel(actor);
                    if (animatedModel == null)
                    {
                        Debug.LogWarning($"[HundunWorldGame] Prefab 中缺少 AnimatedModel，新建一个: CharacterId={characterId}");
                        animatedModel = actor.AddChild<AnimatedModel>();
                        animatedModel.Name = "AnimatedModel";
                    }

                    // 安全初始化：优先等待 Prefab 自带引用加载完成，失败时才使用预加载资产替换
                    AssignAnimatedModelResources(animatedModel, actor, preloadedSkinnedModel, preloadedAnimGraph);

                    // 本地玩家与远端统一用 Ada 承载 MetaHuman 身体：占位身体（Character_1）挂同一张图时，
                    // 它的 retarget 条目指向已失效骨架 ⇒ 图求值不出姿态（T-pose）。这里换成 Ada；
                    // 内部会先停用旧模型再换，避免运行中直接换 SkinnedModel 的引擎断言（2026-10-25）。
                    EnsureMetaHumanBody(actor);
                    animatedModel = FindBodyAnimatedModel(actor) ?? animatedModel;

                    // 程序化待机：空动画图（deps=0）下让身体有呼吸/微摆；检测到真实动画会自己让位。
                    if (animatedModel.GetScript<HundunWorld.Game.Character.ProceduralIdleDriver>() == null)
                    {
                        animatedModel.AddScript<HundunWorld.Game.Character.ProceduralIdleDriver>();
                    }
                }
            }

            actor.Name = $"LocalPlayer_{characterId}";
            actor.Position = position;
            _localPlayerActor = actor;

            // 挂上动画控制器：CharacterRoot.prefab 本身没带这个脚本（日志里 AnimationController=False 即此因），
            // 而两个同步系统都是用 GetScript<CharacterAnimationController>() 去找它来驱动状态的。
            if (actor.GetScript<CharacterAnimationController>() == null)
            {
                var animationController = actor.AddScript<CharacterAnimationController>();
                if (animationController != null)
                    animationController.AnimatedModel = animatedModel;
            }

            // Idle/Walk/Run 混合：按根 Actor 位移速度写两个 α 参数（本地与远端同一套图）。
            EnsureLocomotionDriver(actor);

            // GASP（Motion Matching）：第一版只挂本地玩家；数据缺失/初始化失败时它自己保持停用，回落到上面的图驱动。
            EnsureGaspAnimator(actor);

            // 翻越驱动：检测 → 本地预测登记 → 上行计划 → 服务端权威裁决（F 键触发）。
            _localTraversalDriver = actor.GetScript<HundunWorld.Game.Gasp.GaspTraversalDriver>()
                ?? actor.AddScript<HundunWorld.Game.Gasp.GaspTraversalDriver>();

            // Foley：本地玩家挂脚步消费者（无音频资产时进入事件模式，仅计数/日志）。
            if (actor.GetScript<HundunWorld.Game.Gasp.GaspFoley>() == null)
                actor.AddScript<HundunWorld.Game.Gasp.GaspFoley>();
            if (HundunWorld.Game.Gasp.GaspDebugDraw.DebugMarkerPresent()
                && actor.GetScript<HundunWorld.Game.Gasp.GaspDebugDraw>() == null)
                actor.AddScript<HundunWorld.Game.Gasp.GaspDebugDraw>();

            // 自测夹具：标记存在时自动挂到本地角色上（此前依赖手工挂脚本的测试场景，场景已删）。
            if (actor.GetScript<HundunWorld.Game.Gasp.GaspTestFixture>() == null)
            {
                try
                {
                    var marker = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "HundunWorld", "gasp_fixture.on");
                    if (System.IO.File.Exists(marker))
                        actor.AddScript<HundunWorld.Game.Gasp.GaspTestFixture>();
                }
                catch (Exception)
                {
                    // 标记读取失败不影响正常进世界。
                }
            }

            // 自动添加并初始化 CharacterEquipmentManager，应用已保存的外观数据
            var equipmentManager = actor.AddScript<CharacterEquipmentManager>();            if (equipmentManager != null)
            {
                var appearance = CharacterPersistenceService.Instance.LoadAppearanceAsync(characterId).GetAwaiter().GetResult();
                if (appearance == null)
                {
                    appearance = CharacterPersistenceService.AppearanceData.GetDefaultAppearance();
                }

                equipmentManager.Initialize(
                    appearance.BodyEquipmentId,
                    appearance.AccessoryIds,
                    appearance.WeaponIds,
                    appearance.AppearanceEquipmentId);

                // 本地缓存只用于"进场景瞬间有得看"；随后向服务端要权威装备清单，
                // 回包由 RoleEquipmentSyncHandler 应用（失败则保持本地渲染不动）。
                _ = Network.Handlers.RoleEquipmentSyncHandler.RequestAsync((ulong)characterId);

                // 背包同理：拉服务端权威清单；若服务端为空且本地有货，处理器会一次性迁移上去。
                _ = Network.MessageHandlers.RoleInventorySyncHandler.RequestAsync((ulong)characterId);

                Debug.Log($"[HundunWorldGame] 已初始化 CharacterEquipmentManager: CharacterId={characterId}, Body={appearance.BodyEquipmentId}, Appearance={appearance.AppearanceEquipmentId}, Accessories=[{string.Join(",", appearance.AccessoryIds)}], Weapons=[{string.Join(",", appearance.WeaponIds)}]");
            }
            else
            {
                Debug.LogError($"[HundunWorldGame] 无法为角色添加 CharacterEquipmentManager: CharacterId={characterId}");
            }

            // 最终状态校验与兜底：如果 AnimatedModel 仍未获得已加载的 SkinnedModel，禁用它防止断言
            // 注意：不设置 SkinnedModel = null，避免竞态中 Job 调用 SetupSkinningData 触发断言。
            // 只设置 UpdateMode = Never 和 IsActive = false，让 CanUpdateModel 自然返回 false。
            animatedModel = FindBodyAnimatedModel(actor);
            var finalSkinnedModel = animatedModel?.SkinnedModel;
            if (animatedModel != null && (finalSkinnedModel == null || !finalSkinnedModel.IsLoaded))
            {
                Debug.LogError($"[HundunWorldGame][PACKAGE] 最终校验失败：AnimatedModel 的 SkinnedModel 未加载，禁用 AnimatedModel 防止断言");
                animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                animatedModel.IsActive = false;
            }

            // Ada 是厘米模型（Character_1 是米、靠 100 缩放）：确认换成 Ada 后把缩放归 1。
            AnimatedModel worldHead = null;
            if (animatedModel?.SkinnedModel != null)
            {
                bool isAda = animatedModel.SkinnedModel.Path.IndexOf("Ada_BodyPreview", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isAda)
                    animatedModel.Scale = Vector3.One;

                try
                {
                    var headModel = LoadAssetAt<SkinnedModel>(EquipmentDatabase.HeadModelPath);
                    if (headModel != null && headModel.IsLoaded)
                    {
                        // 头是独立 Actor；WorldHeadMasterPose 在 OnStart 调一次 SetMasterPoseModel，
                        // 与 Ada 同骨架按骨名跟随（不在运行中换模型，避免动画竞态断言）。
                        worldHead = actor.AddChild<AnimatedModel>();
                        worldHead.Name = $"LocalPlayerHead_{characterId}";
                        worldHead.SkinnedModel = headModel;
                        worldHead.Scale = Vector3.One;
                        worldHead.LocalPosition = Vector3.Zero;
                        // 490 骨头资产同理：关掉逐骨运动模糊，避免首次蒙皮时撞 AnimatedModel.cpp:82（#76）。
                        worldHead.PerBoneMotionBlur = false;
                        worldHead.AddScript<HundunWorld.Game.Character.WorldHeadMasterPose>();
                        Debug.Log($"[HundunWorldGame] 世界头已创建（master pose）: {headModel.Path}");
                    }
                    else
                    {
                        Debug.LogWarning($"[HundunWorldGame] 世界头部模型未就绪: {EquipmentDatabase.HeadModelPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HundunWorldGame] 世界头部创建失败（CharacterId={characterId}）：{ex}");
                }
            }

            // 世界角色头发：毛发卡片与头资产同空间，挂在头 Actor 下随头移动（静态网格，不参与蒙皮）。
            if (worldHead != null)
            {
                try
                {
                    var hairModel = Content.Load<Model>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(EquipmentDatabase.HairCardsModelPath), 10000.0);
                    if (hairModel != null && hairModel.IsLoaded)
                    {
                        var hair = worldHead.AddChild<StaticModel>();
                        hair.Name = $"LocalPlayerHair_{characterId}";
                        hair.Model = hairModel;
                        hair.LocalPosition = Vector3.Zero;
                        hair.LocalScale = Vector3.One;

                        // 发色：创角色板写的是离散下标（AppearanceInfo.HairColor），世界侧用同一张调色板还原；
                        // 写到该 Actor 自己的材质实例上，避免改到共享材质。
                        // 两个角色缓存并存（服务层与创角 UI 层），任一命中即可；都没有才回落 0。
                        var characterInfo = CharacterService.Instance?.GetCharacterFromCache(characterId);
                        if (characterInfo == null)
                        {
                            var uiManager = global::Game.UI.Character.CharacterManager.Instance;
                            characterInfo = uiManager?.CharacterList?.FirstOrDefault(c => c.CharacterId == characterId)
                                            ?? uiManager?.SelectedCharacter;
                        }
                        var appearance = characterInfo?.Appearance;
                        int hairColorIndex = appearance?.HairColor ?? 0;
                        var palette = HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcFacePresets.HairColors;
                        FlaxEngine.Color hairColor = palette.Length > 0
                            ? palette[Mathf.Clamp(hairColorIndex, 0, palette.Length - 1)]
                            : new FlaxEngine.Color(0.10f, 0.06f, 0.035f);
                        var baseMaterial = hair.GetMaterial(0);
                        if (baseMaterial != null)
                        {
                            if (baseMaterial.CreateVirtualInstance() is MaterialInstance hairInstance)
                            {
                                hairInstance.SetParameterValue("HairColor", hairColor);
                                hair.SetMaterial(0, hairInstance);
                            }
                        }
                        // 发型：模板参数 hair.style → HairStyleCatalog；绑定器在模型层换卡，并保留发色实例。
                        hair.AddScript<HundunWorld.Game.Character.MetaHumanHairBinder>();
                        Debug.Log($"[HundunWorldGame] 世界头发已创建: {hairModel.Path} 发色下标={hairColorIndex} RGB=({hairColor.R:F2},{hairColor.G:F2},{hairColor.B:F2})");

                        // 眉毛卡片：与头发同空间、同抠图材质，颜色跟随发色。
                        HundunWorld.Game.Character.MetaHumanEyebrowBinder.Attach(
                            worldHead, hairColor, $"LocalPlayerBrows_{characterId}", out _);
                    }
                    else
                    {
                        Debug.LogWarning($"[HundunWorldGame] 世界头发模型未就绪: {EquipmentDatabase.HairCardsModelPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HundunWorldGame] 世界头发创建失败（CharacterId={characterId}）：{ex}");
                }
            }

            // 世界角色应用玩家外观：按持久化角色 id 读回已发布外观，写回目标为本角色的 AnimatedModel。
            // 读不到外观/目标未命中都会留在 MetaHumanCharacter 的诊断里，不静默退化成"看起来没生效"。
            if (animatedModel != null && animatedModel.SkinnedModel != null)
            {
                try
                {
                    var mh = actor.GetScript<MetaHuman.Runtime.MetaHumanCharacter>()
                             ?? actor.AddScript<MetaHuman.Runtime.MetaHumanCharacter>();
                    if (mh != null)
                    {
                        mh.PersistenceCharacterId = characterId.ToString();
                        // Load() 以 TemplateIds 为准建会话；不给就永远 loaded=false（已实机复现），
                        // 与创角面板用同一套基础模板，保证发布后的外观能在世界角色上落地。
                        mh.TemplateIds = new[] { "metahuman.face_archetype", "metahuman.body_base" };
                        mh.TargetModels = worldHead != null
                            ? new AnimatedModel[] { animatedModel, worldHead }
                            : new AnimatedModel[] { animatedModel };
                        mh.EnableBoneScaleChannel = true;
                        mh.ApplyOnStart = true;
                        if (!mh.IsLoaded)
                        {
                            mh.Load();
                        }

                        Debug.Log($"[HundunWorldGame] 已挂 MetaHumanCharacter：CharacterId={characterId}, "
                                  + $"loaded={mh.IsLoaded}, targets={(worldHead != null ? 2 : 1)}, diag={mh.AppliersDiag()}");

                        // 世界角色也要有面部空闲动画（UE 的 Face Animator 对齐）：默认只眨眼，
                        // 不跑周期性张嘴 demo（会盖住外观静帧对比）。挂在头 Actor 上，Owner 显式指认。
                        if (worldHead != null)
                        {
                            var face = worldHead.GetScript<MetaHuman.Runtime.MetaHumanFaceAnimator>()
                                       ?? worldHead.AddScript<MetaHuman.Runtime.MetaHumanFaceAnimator>();
                            if (face != null)
                            {
                                face.Owner = mh;
                                face.AutoBlink = true;
                                face.AutoPlayDemo = false;
                                face.LinkMirrors = true;
                            }

                            // 外部追踪入口（Live Link 等价）：默认不监听，开发/演出时再打开。
                            var tracking = worldHead.GetScript<MetaHuman.Runtime.MetaHumanFaceTrackingReceiver>()
                                           ?? worldHead.AddScript<MetaHuman.Runtime.MetaHumanFaceTrackingReceiver>();
                            if (tracking != null)
                            {
                                tracking.Animator = face;
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"[HundunWorldGame] 无法为本地玩家添加 MetaHumanCharacter: CharacterId={characterId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HundunWorldGame] 挂 MetaHumanCharacter 失败（CharacterId={characterId}）：{ex}");
                }
            }

            // 服务端副本自愈：本机有捏脸快照、服务端没有时补传一次（跨设备恢复靠这条）。
            // Ink 登录流程不走 CharacterStateManager.AdoptFromServer，所以要在这里显式补。
            try
            {
                HundunWorld.Game.Services.MetaHumanAppearanceService.Instance
                    .HealServerFromLocal((long)characterId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HundunWorldGame] 外观服务端自愈调用失败：{ex.Message}");
            }

            Debug.Log($"[HundunWorldGame] 已创建本地玩家 Actor: CharacterId={characterId}, Pos=({x},{y},{z}), " +
                $"AnimatedModel={(animatedModel != null ? animatedModel.Name : "null")}, " +
                $"SkinnedModelPath={finalSkinnedModel?.Path ?? "MISSING"}, " +
                $"SkinnedModelLoaded={finalSkinnedModel?.IsLoaded.ToString() ?? "N/A"}, " +
                $"AnimGraphPath={animatedModel?.AnimationGraph?.Path ?? "MISSING"}, " +
                $"AnimGraphLoaded={animatedModel?.AnimationGraph?.IsLoaded.ToString() ?? "N/A"}, " +
                $"IsActive={animatedModel?.IsActive.ToString() ?? "N/A"}");

            // 确保 LocalPlayerActor 必挂 CharacterAttributesComponent（供 UI 数据绑定）
            var attrComp = actor.GetScript<CharacterAttributesComponent>();
            if (attrComp == null)
            {
                attrComp = actor.AddScript<CharacterAttributesComponent>();
                Debug.Log($"[HundunWorldGame] LocalPlayerActor 缺少 CharacterAttributesComponent，已自动添加");
            }
            Debug.Log($"[HundunWorldGame] 已确保 LocalPlayerActor 挂载 CharacterAttributesComponent: Level={attrComp.Level}, Nickname={attrComp.Nickname}, Stage={attrComp.CurrentStage}");

            // 装备快照可能比这个组件先到（进场景瞬间），那次重算会因组件为 null 而整体落空。
            // 组件一就位就按已缓存的权威快照补算一次，不必等下一次穿戴回包。
            HundunWorld.Game.Character.EquipmentAttributeBridge.RefreshFromEquipped();

            // 五行三行的权威值只有服务端读得到（属性表 + 物品模板 + 亲和兜底），
            // 所以组件一就位就取一次画像；请求排在组件之后发，回包不会落到没有组件的 Actor 上。
            _ = HundunWorld.Game.Network.MessageHandlers.CharacterAttributeRefreshHandler
                .RequestAsync((ulong)characterId,
                    HundunWorld.Game.Network.MessageHandlers.CharacterAttributeRefreshHandler.ReasonLogin);

            // 技能模板表同样以服务端为准（客户端那份硬编码只作下行到达前的兜底）。
            _ = HundunWorld.Game.Network.MessageHandlers.SkillCatalogSyncHandler.RequestAsync((ulong)characterId);

            // 应用缓存的本地玩家属性（来自 EnterGameResponse，由 ApplyLocalPlayerAttributes 在异步路径下缓存）。
            // 同步路径下 ApplyLocalPlayerAttributes 已直接回填，此处仅处理异步路径。
            if (_hasPendingLocalPlayerAttributes)
            {
                attrComp.Nickname = _pendingLocalPlayerNickname ?? attrComp.Nickname;
                attrComp.Level = _pendingLocalPlayerLevel;
                attrComp.CurrentStage = _pendingLocalPlayerStage;
                _hasPendingLocalPlayerAttributes = false;
                Debug.Log($"[HundunWorldGame] 已应用缓存的本地玩家属性: Nickname={attrComp.Nickname}, Level={attrComp.Level}, Stage={attrComp.CurrentStage}");
            }

            // 挂载 LocalPlayerActorSyncSystem：从 ECS PredictedTransformComponent 读取位置/朝向应用到 Actor。
            // [重构背景] PlayerController 不再直接修改 Actor.Position/Orientation，
            // 改由本系统从 ECS 同步，确保客户端显示位置 = 本地预测位置 = 服务端校验输入三者一致。
            // 执行顺序：Flax 中同一 Actor 上的 Script 按 AddScript 顺序执行 OnUpdate，
            // LocalPlayerActorSyncSystem 在 PlayerController 之后挂载，OnUpdate 在其后执行；
            // 而 ECSUpdateDriver 挂在不同 Actor 上，按场景树顺序先于 LocalPlayerActor 执行，
            // 保证本系统读取的 PredictedTransformComponent 是本帧最新值。
            var localPlayerSyncSystem = actor.GetScript<LocalPlayerActorSyncSystem>();
            if (localPlayerSyncSystem == null)
            {
                localPlayerSyncSystem = actor.AddScript<LocalPlayerActorSyncSystem>();
                Debug.Log($"[HundunWorldGame] 已挂载 LocalPlayerActorSyncSystem: CharacterId={characterId}");
            }

            // 头顶名牌组件（血条/名称/等级/称号）：挂在位置同步系统之后，投影时读到的是本帧最新位置。
            if (actor.GetScript<HundunWorld.Game.UI.GameMain.NameplateUI>() == null)
            {
                actor.AddScript<HundunWorld.Game.UI.GameMain.NameplateUI>();
            }

            // 导航组件：承载 POI 发现状态与快速旅行（对应 UE5 挂在 PlayerController 上的 UNarrativeNavigationComponent）。
            // 只挂脚本、不加碰撞体，因此不改变玩家物理与交互。
            var navigationComponent = actor.GetScript<NarrativePro.Navigation.NarrativeNavigationComponent>();
            if (navigationComponent == null)
            {
                navigationComponent = actor.AddScript<NarrativePro.Navigation.NarrativeNavigationComponent>();
                Debug.Log($"[HundunWorldGame] 已挂载 NarrativeNavigationComponent: CharacterId={characterId}");
            }

            // 注入玩家 Actor 并接管快速旅行：
            // 本项目的玩家位置由 ECS PredictedTransformComponent 单点驱动（LocalPlayerActorSyncSystem 每帧回写），
            // 插件直接改 Actor.Position 会被覆盖，因此必须由本类写 ECS 组件。
            navigationComponent.SetLocalPlayerActor(actor);
            navigationComponent.OnFastTravelRequested -= OnFastTravelRequested;
            navigationComponent.OnFastTravelRequested += OnFastTravelRequested;

            // ★ 修复「进入游戏后在天上飞」：重新对齐地面高度。
            // CreateLocalPlayerEntity 在角色选择场景中被调用（GameWorld 未加载），
            // 此时 SampleGroundHeightEcs 的 Physics.RayCast 无法命中 Terrain → 返回 NaN，
            // pred.Z 使用了服务端 initialY（可能为 0 或上次保存值，与实际地形高度不一致）。
            // 此刻 GameWorld 场景已加载完毕，Physics.RayCast 可正常工作，重新采样并修正 ECS 实体高度。
            RealignLocalPlayerToGround(characterId, x, z);

            return actor;
        }

        // 角色资产 GUID（来自 CharacterRoot.prefab）
        // 重要：身体资产必须是 FlaxEngine.SkinnedModel 类型。
        // 如果实际是 FlaxEngine.Model，Content.Load<SkinnedModel> 会失败，导致角色没有骨骼网格体。
        // 出现此类问题时，请在 Flax Editor 中重新导入源 FBX 并选择 Type = SkinnedModel。
        // 2026-09-27：身体资产已由 skm_uefn_mannequin 换成 Character_1/2（提交 5287c6c），路径以 EquipmentDatabase 为准。
        // 2026-10-16：**这里不再填 GUID 常量**。资产的 GUID 会随重导入变掉（当天实测：Character_1.flax 的头
        // 从 528b2144-… 变成 216f8857-…，模板 body.flax 从 69b4df20-… 变成 bea3fa49-…），而
        // LoadAssetFullyLoaded 是"GUID 优先、失败才回退路径"，于是每次重导都要白等一次 30s 超时 +
        // 满屏 Cannot find asset 报错，严重时角色直接没身体。路径才是这一层唯一可靠的键。
        private static readonly Guid SkinnedModelGuid = Guid.Empty;
        // 2026-09 修正：原 ceded67f4bb2623f40b4dcb493b0d419 为**错误 GUID**（字节顺序被写乱）。
        // Content/Character/Models/Animation Graph.flax 头部实际 ID = ceded67f623f4bb2b4dcb44019d4b093，
        // 与日志中的 "Invalid or missing asset (ceded67f623f4bb2b4dcb44019d4b093, FlaxEngine.AnimationGraph)" 一致。
        private static readonly Guid AnimationGraphGuid = new Guid("ceded67f623f4bb2b4dcb44019d4b093");

        /// <summary>
        /// 安全初始化 AnimatedModel 的资源引用。
        /// 策略：优先保留并等待 Prefab 自带的 SkinnedModel/AnimationGraph 引用加载完成；
        /// 只有当自带引用无法加载时，才使用预加载资产替换。
        /// 所有资源切换都遵循竞态安全协议：UpdateMode=Never、IsActive=false、先清空 AnimationGraph 再改 SkinnedModel。
        /// </summary>
        /// <summary>
        /// 确保角色 Actor 上有一个带身体网格的 <see cref="AnimatedModel"/>，没有就补挂一个。
        /// <para>
        /// 为什么需要：远程玩家用的 <c>Content/Prefabs/Character/CharacterRoot.prefab</c>
        /// 至今是个占位资产——根节点只有一个连 Model 引用都没有的 <c>StaticModel "Capsule"</c>，
        /// 所以远程角色在场景里其实**看不见**（日志表现为
        /// "未找到 AnimatedModel，无法绑定 RootMotionTarget"）。本地玩家走的是同一套兜底
        /// （见 <c>Prefab 中缺少 AnimatedModel，新建一个</c> 分支），这里把那条路开放给远程复用。
        /// </para>
        /// <para>
        /// 性别：网络同步层的实体里没有 Gender 字段（只有位置/朝向/身份），
        /// 因此默认男性身体；等同步协议带上性别后由调用方传入即可。
        /// </para>
        /// </summary>
        /// <param name="root">角色根 Actor</param>
        /// <param name="gender">0 = 男，1 = 女</param>
        /// <returns>已有或新建的 AnimatedModel；root 为空时返回 null</returns>
        public static AnimatedModel EnsureBodyModel(Actor root, int gender = 0)
        {
            if (root == null)
                return null;

            var model = FindBodyAnimatedModel(root);
            if (model == null)
            {
                model = root.AddChild<AnimatedModel>();
                model.Name = "AnimatedModel";
            }

            AssignAnimatedModelResources(model, root, EquipmentDatabase.GetBodyModel(gender), null);
            return model;
        }

        /// <summary>
        /// 远端玩家身体：与本地玩家同一套 Ada 承载模型 + 重定向动画图，否则头部形状没有脸骨可写。
        /// 已有非 Ada 的 AnimatedModel 时先停用再加新的（运行中直接换 SkinnedModel 会踩引擎断言）。
        /// </summary>
        /// <summary>
        /// Ada 挂 locomotion 图：剪辑里的 MetaHuman 通道按 UE Z-up 烘、Ada 骨架是 Y-up，
        /// 直接播会让骨架整体多转 −90°X、网格撕成碎片（2026-10-25 实测：pelvis 局部平移 (0,2.94,−95.74)）。
        /// 现在由 <see cref="HundunWorld.Game.Character.MetaHumanClipFrameFix"/> 在动画更新后把姿态转正，
        /// 目视与骨骼读数均已成立（直立、8 根关键骨 0.9s 内 0.9–3.1cm 位移、无碎片）。
        /// </summary>
        private const bool AttachLocomotionGraphToAda = true;

        /// <summary>
        /// 从角色 Actor 下挑「身体」AnimatedModel：头部是独立 Actor（LocalPlayerHead_* / conformhead 资产），
        /// 用 FindActor&lt;AnimatedModel&gt;() 会按遍历顺序撞到它，把 Ada 身体和动画图挂到头上
        /// （2026-10-25 实测：日志出现 model=Ada_FaceMesh_conformhead4 + graph，身体仍是 Character_1）。
        /// 规则：排除名字或模型路径像头的；优先名字就是 "AnimatedModel" 的那个。
        /// </summary>
        private static AnimatedModel FindBodyAnimatedModel(Actor root)
        {
            if (root == null)
            {
                return null;
            }
            var all = new List<AnimatedModel>();
            CollectAnimatedModels(root, all);
            AnimatedModel named = null;
            AnimatedModel first = null;
            string dump = "";
            for (int i = 0; i < all.Count; i++)
            {
                AnimatedModel m = all[i];
                if (m == null)
                {
                    continue;
                }
                bool head = IsHeadModel(m);
                dump += (i > 0 ? " | " : "") + "name=" + m.Name
                        + ",id=" + m.ID
                        + ",active=" + m.IsActive
                        + ",model=" + (m.SkinnedModel != null ? m.SkinnedModel.Path : "null")
                        + ",head=" + head;
                if (head)
                {
                    continue;
                }
                if (first == null)
                {
                    first = m;
                }
                if (named == null && string.Equals(m.Name, "AnimatedModel", StringComparison.OrdinalIgnoreCase))
                {
                    named = m;
                }
            }
            AnimatedModel chosen = named ?? first;
            Debug.Log("[HundunWorldGame] 身体候选(" + all.Count + "): " + dump
                      + " ⇒ 选中=" + (chosen != null ? chosen.Name + "/" + chosen.ID : "null"));
            return chosen;
        }

        private static bool IsHeadModel(AnimatedModel model)
        {
            if (model.Name != null && model.Name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            string path = model.SkinnedModel != null ? model.SkinnedModel.Path : null;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return path.IndexOf("conformhead", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("head_mh", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CollectAnimatedModels(Actor root, List<AnimatedModel> result)
        {
            if (root == null)
            {
                return;
            }
            if (root is AnimatedModel model)
            {
                result.Add(model);
            }
            var children = root.Children;
            if (children == null)
            {
                return;
            }
            for (int i = 0; i < children.Length; i++)
            {
                CollectAnimatedModels(children[i], result);
            }
        }

        public static AnimatedModel EnsureMetaHumanBody(Actor root)
        {
            if (root == null)
            {
                return null;
            }

            AnimatedModel existing = FindBodyAnimatedModel(root);
            if (existing != null && existing.SkinnedModel != null
                && existing.SkinnedModel.Path.IndexOf("Ada_BodyPreview", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!AttachLocomotionGraphToAda && existing.AnimationGraph != null)
                {
                    existing.AnimationGraph = null;
                }
                if (existing.AnimationGraph != null)
                {
                    HundunWorld.Game.Character.MetaHumanClipFrameFix.Attach(existing);
                }
                EnsureLocomotionDriver(root);
                return existing;
            }

            if (existing != null)
            {
                // 只停用不清引用的话，AssignAnimatedModelResources 会继续优先用"自带引用"（占位身体），
                // Ada 永远换不上来（2026-10-25 实测：本地玩家身体一直是 Character_1）。
                existing.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                existing.IsActive = false;
                existing.SkinnedModel = null;
            }

            var ada = LoadAssetFullyLoaded<SkinnedModel>(Guid.Empty, EquipmentDatabase.TemplateSkinnedBodyModelPath);
            if (ada == null || !ada.IsLoaded)
            {
                Debug.LogWarning("[HundunWorldGame] MetaHuman 身体未就绪，远端回退到人形占位身体：" + EquipmentDatabase.TemplateSkinnedBodyModelPath);
                return EnsureBodyModel(root);
            }

            var model = FindBodyAnimatedModel(root);
            if (model == null)
            {
                model = root.AddChild<AnimatedModel>();
                model.Name = "AnimatedModel";
            }

            // 图与 Ada 的坐标口径差（剪辑 Z-up / 骨架 Y-up）由 MetaHumanClipFrameFix 在动画更新后转正，
            // 见 AttachLocomotionGraphToAda 的说明与任务 #94 的实测读数。
            var graph = AttachLocomotionGraphToAda
                ? LoadAssetFullyLoaded<AnimationGraph>(Guid.Empty, "Content/Character/Models/Animation Graph.flax")
                : null;
            AssignAnimatedModelResources(model, root, ada, graph);
            if (!AttachLocomotionGraphToAda)
            {
                // Assign 在 target 图为 null 时会恢复原图（原图可能是先前设上的同一张），这里再清一次确保真的不挂。
                model.AnimationGraph = null;
            }
            model.IsActive = true;
            model.UpdateMode = AnimatedModel.AnimationUpdateMode.Auto;
            if (model.AnimationGraph != null)
            {
                HundunWorld.Game.Character.MetaHumanClipFrameFix.Attach(model);
            }
            Debug.Log("[HundunWorldGame] MetaHuman 身体已就位: model=" + (model.SkinnedModel != null ? model.SkinnedModel.Path : "null")
                      + " graph=" + (model.AnimationGraph != null ? model.AnimationGraph.Path : "null"));

            // Ada 是厘米模型；与本地玩家同一口径。
            model.Scale = Vector3.One;
            EnsureLocomotionDriver(root);
            return model;
        }

        /// <summary>
        /// 挂 locomotion（Idle/Walk/Run 混合）驱动。本地与远端共用，重复调用无副作用。
        /// </summary>
        public static void EnsureLocomotionDriver(Actor actor)
        {
            if (actor == null)
                return;
            if (actor.GetScript<LocomotionGraphDriver>() == null)
                actor.AddScript<LocomotionGraphDriver>();
        }

        /// <summary>
        /// 挂 GASP Motion Matching 运行时。重复调用无副作用；无数据文件时组件会保持未就绪、不接管骨骼。
        /// </summary>
        public static void EnsureGaspAnimator(Actor actor)
        {
            EnsureGaspAnimator(actor, false);
        }

        /// <summary>
        /// 挂 GASP Motion Matching 运行时。remote=true 时尊重网络 LOD、关闭脚部射线。
        /// </summary>
        public static void EnsureGaspAnimator(Actor actor, bool remote)
        {
            if (actor == null)
                return;
            var gasp = actor.GetScript<HundunWorld.Game.Gasp.GaspAnimator>();
            if (gasp == null)
                gasp = actor.AddScript<HundunWorld.Game.Gasp.GaspAnimator>();
            if (gasp != null && remote)
            {
                gasp.RemoteMode = true;
                gasp.EnableFootPlacement = false;
            }
        }

        public static void AssignAnimatedModelResources(AnimatedModel animatedModel, Actor rootActor,
            SkinnedModel preloadedSkinnedModel, AnimationGraph preloadedAnimGraph)
        {
            if (animatedModel == null) return;

            // ── 步骤 1：检查并等待 Prefab 自带的 SkinnedModel 引用 ──
            var currentSkinnedModel = animatedModel.SkinnedModel;
            if (currentSkinnedModel != null && !currentSkinnedModel.IsLoaded)
            {
                Debug.Log($"[HundunWorldGame] Prefab 自带 SkinnedModel 未加载，等待中: {currentSkinnedModel.Path}");
                currentSkinnedModel.WaitForLoaded(30000.0);
            }

            // ── 步骤 2：选择目标 SkinnedModel ──
            // 优先级：已加载的自带引用 > 已加载的预加载资产 > 预加载资产（可能未加载）
            SkinnedModel targetSkinnedModel = null;
            if (currentSkinnedModel != null && currentSkinnedModel.IsLoaded)
            {
                targetSkinnedModel = currentSkinnedModel;
                Debug.Log($"[HundunWorldGame] 使用 Prefab 自带 SkinnedModel: {targetSkinnedModel.Path}");
            }
            else if (preloadedSkinnedModel != null && preloadedSkinnedModel.IsLoaded)
            {
                targetSkinnedModel = preloadedSkinnedModel;
                Debug.Log($"[HundunWorldGame] 使用预加载 SkinnedModel 替换未加载的自带引用: {targetSkinnedModel.Path}");
            }
            else if (preloadedSkinnedModel != null)
            {
                preloadedSkinnedModel.WaitForLoaded(30000.0);
                if (preloadedSkinnedModel.IsLoaded)
                {
                    targetSkinnedModel = preloadedSkinnedModel;
                    Debug.Log($"[HundunWorldGame] 预加载 SkinnedModel 等待后可用: {targetSkinnedModel.Path}");
                }
            }

            // ── 步骤 3：检查并等待 Prefab 自带的 AnimationGraph 引用 ──
            var currentAnimGraph = animatedModel.AnimationGraph;
            if (currentAnimGraph != null && !currentAnimGraph.IsLoaded)
            {
                Debug.Log($"[HundunWorldGame] Prefab 自带 AnimationGraph 未加载，等待中: {currentAnimGraph.Path}");
                currentAnimGraph.WaitForLoaded(30000.0);
            }

            // ── 步骤 4：选择目标 AnimationGraph ──
            AnimationGraph targetAnimGraph = null;
            if (currentAnimGraph != null && currentAnimGraph.IsLoaded)
            {
                targetAnimGraph = currentAnimGraph;
            }
            else if (preloadedAnimGraph != null && preloadedAnimGraph.IsLoaded)
            {
                targetAnimGraph = preloadedAnimGraph;
            }
            else if (preloadedAnimGraph != null)
            {
                preloadedAnimGraph.WaitForLoaded(30000.0);
                if (preloadedAnimGraph.IsLoaded)
                    targetAnimGraph = preloadedAnimGraph;
            }

            // ── 步骤 5：如果没有任何可用的 SkinnedModel，安全降级 ──
            if (targetSkinnedModel == null)
            {
                animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                animatedModel.IsActive = false;
                Debug.LogError("[HundunWorldGame][PACKAGE] AssignAnimatedModelResources: 没有已加载的 SkinnedModel（自带引用和预加载均失败），禁用 AnimatedModel");
                return;
            }

            // ── 步骤 6：竞态安全切换 ──
            var originalUpdateMode = animatedModel.UpdateMode;
            bool wasActive = animatedModel.IsActive;
            var originalAnimGraph = animatedModel.AnimationGraph;

            animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
            animatedModel.IsActive = false;
            animatedModel.AnimationGraph = null;

            // 只有目标与当前引用不同才修改，避免不必要的 OnSkinnedModelChanged 回调
            if (animatedModel.SkinnedModel != targetSkinnedModel)
            {
                animatedModel.SkinnedModel = targetSkinnedModel;
            }

            // 恢复 AnimationGraph（可能和 original 相同，也可能来自预加载）
            animatedModel.AnimationGraph = targetAnimGraph ?? originalAnimGraph;

            // 设置 RootMotionTarget
            if (animatedModel.RootMotionTarget == null && rootActor != null)
                animatedModel.RootMotionTarget = rootActor;

            // 490 骨模型首次蒙皮时，引擎按“骨数×48×(PerBoneMotionBlur?2:1)”申请骨骼缓冲；
            // 冷缓冲 16KB 只翻倍一次到 32KB，而 490 骨 ×48×2 ≈ 47KB 会触发 AnimatedModel.cpp:82 断言（#76）。
            // 逐骨运动模糊在这里没有收益，关掉后需求减半（≈23KB），运行时不再撞断言。
            animatedModel.PerBoneMotionBlur = false;

            // 手动刷新
            if (animatedModel.SkinnedModel != null && animatedModel.SkinnedModel.IsLoaded)
            {
                try
                {
                    animatedModel.SetupSkinningData();
                    animatedModel.ResetAnimation();
                    animatedModel.UpdateAnimation();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HundunWorldGame] 刷新 AnimatedModel 失败: {ex.Message}");
                }
            }

            // 恢复激活状态和更新模式
            animatedModel.IsActive = wasActive;
            animatedModel.UpdateMode = originalUpdateMode;

            Debug.Log($"[HundunWorldGame] AnimatedModel 资源已赋值并刷新: SkinnedModel={animatedModel.SkinnedModel?.Path ?? "null"}, " +
                $"AnimationGraph={(animatedModel.AnimationGraph?.Path ?? "null")}, IsActive={animatedModel.IsActive}");
        }

        /// <summary>
        /// 在 Content 中查找指定类型的第一个已加载资产（兜底策略）。
        /// </summary>
        private static T FindFirstAssetByType<T>() where T : Asset
        {
            try
            {
                // 获取 Content 中所有该类型资产
                var assets = Content.GetAssets(typeof(T));
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        if (asset != null)
                        {
                            if (!asset.IsLoaded)
                            {
                                asset.WaitForLoaded(10000.0);
                            }
                            if (asset.IsLoaded)
                            {
                                Debug.Log($"[ContentLoad] 通过枚举找到 {typeof(T).Name}: {asset.Path}");
                                return asset as T;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ContentLoad] 枚举 {typeof(T).Name} 资产失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 查找 GameWorld 场景（World.scene），确保 Actor 生成在正确的场景中。
        /// 不依赖场景名称（可能为空或不匹配），改为查找包含 Terrain 或 WorldSceneInitializer 的场景。
        /// </summary>
        public static FlaxEngine.Scene FindGameWorldScene()
        {
            // 方式1：按场景资产路径精确匹配 ".../World.scene"。
            // 必须排在最前：Login.scene 也带 Terrain，旧的「含 Terrain 即玩法场景」会把 Login 当成世界，
            // 本地玩家因此被生成进登录场景（2026-10-25 实测：LocalPlayer_44 落在 Login，World 里另有相机/地形）。
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && IsWorldScenePath(scene))
                    return scene;
            }

            // 方式2：查找包含 WorldSceneInitializer 脚本的场景（排除登录/常驻场景）
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene == null) continue;
                if (scene.GetScript<WorldSceneInitializer>() != null && !IsNonGameplayScenePath(scene))
                    return scene;
            }

            // 方式3：查找包含 Terrain 的场景（World 场景有 Terrain），同样排除登录/常驻场景
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene == null) continue;
                if (scene.FindActor<Terrain>() != null && !IsNonGameplayScenePath(scene))
                    return scene;
            }

            // 调试：打印所有场景信息
            Debug.LogWarning($"[FindGameWorldScene] 未找到 World 场景，当前场景数: {Level.ScenesCount}");
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                Debug.LogWarning($"[FindGameWorldScene] Scene[{i}]: Name={scene?.Name ?? "null"}, HasTerrain={scene?.FindActor<Terrain>() != null}");
            }

            return null;
        }

        /// <summary>场景资产路径是否命中 "/World.scene"（Scene.Name 是根对象名，不能拿来判）。</summary>
        private static bool IsWorldScenePath(FlaxEngine.Scene scene)
        {
            return SceneAssetPathContains(scene, "/World.scene");
        }

        /// <summary>登录/常驻/过渡这类非玩法场景：它们的资产路径命中就排除。</summary>
        private static bool IsNonGameplayScenePath(FlaxEngine.Scene scene)
        {
            return SceneAssetPathContains(scene, "/Login.scene")
                   || SceneAssetPathContains(scene, "/RootScene.scene")
                   || SceneAssetPathContains(scene, "/TransitionScene.scene");
        }

        private static bool SceneAssetPathContains(FlaxEngine.Scene scene, string fragment)
        {
            if (scene == null || scene.ID == Guid.Empty)
                return false;

            try
            {
                if (FlaxEngine.Content.GetAssetInfo(scene.ID, out var info) && info.Path != null)
                {
                    string path = info.Path.Replace('\\', '/');
                    return path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                // 资产信息解析失败按“不匹配”处理，交给后面的兜底继续找
            }

            return false;
        }

        /// <summary>
        /// 将 Actor 生成到当前玩法（World）场景。
        /// Flax 无父级版 <see cref="Level.SpawnActor(Actor)"/> 固定落入 Scenes[0]（常驻 RootScene，
        /// 切场景不卸载），世界对象必须显式指定场景，否则会在 RootScene 里跨会话堆积。
        /// 解析不到玩法场景时大声报错并仍按默认落点生成（可见性优先）。
        /// 返回 true 表示生成失败（Flax 约定）。
        /// </summary>
        public static bool SpawnActorToGameplayScene(Actor actor, string what = null)
        {
            var target = FindGameWorldScene();
            if (target == null)
            {
                Debug.LogError($"[SpawnRouting] 未找到玩法场景，{what ?? actor?.Name ?? "Actor"} 将落入 RootScene（RootScene 只允许全局共享宿主，请检查 World 场景是否已加载）");
                return Level.SpawnActor(actor);
            }
            return Level.SpawnActor(actor, target);
        }

        /// <summary>
        /// 把已通过 PrefabManager.SpawnPrefab 生成的 Actor 搬进玩法场景（SpawnPrefab 同样默认落 Scenes[0]）。
        /// </summary>
        public static void MoveActorToGameplayScene(Actor actor, string what = null)
        {
            if (actor == null) return;
            var target = FindGameWorldScene();
            if (target == null)
            {
                Debug.LogError($"[SpawnRouting] 未找到玩法场景，{what ?? actor.Name ?? "Actor"} 滞留在 {actor.Scene?.Name ?? "未知场景"}（RootScene 只允许全局共享宿主）");
                return;
            }
            if (actor.Scene != target)
            {
                Level.SpawnActor(actor, target);
            }
        }

        /// <summary>
        /// 创建可见的占位角色 Actor，用于在 Prefab 加载/生成失败或 SkinnedModel 缺失时仍能看见本地玩家位置。
        /// 使用预加载的 SkinnedModel 和 AnimationGraph 资产，确保角色有骨骼网格体和动画；
        /// 若 SkinnedModel 仍不可用，则附加一个可见方块作为兜底，避免角色完全不可见。
        /// </summary>
        private static Actor CreateFallbackCharacterActor(Vector3 position,
            SkinnedModel preloadedSkinnedModel, AnimationGraph preloadedAnimGraph)
        {
            var root = new EmptyActor
            {
                Name = "LocalPlayer_Fallback",
                Position = position
            };

            // 生成到玩法场景；解析失败时大声报错并退回默认落点（可见性优先）
            SpawnActorToGameplayScene(root, "LocalPlayer_Fallback");

            // 创建 AnimatedModel 并加载骨骼网格体和动画图
            var animatedModel = root.AddChild<AnimatedModel>();
            animatedModel.Name = "FallbackModel";
            animatedModel.Position = Vector3.Zero;

            // 使用预加载资产；若未提供则再次尝试完全加载
            var skinnedModel = preloadedSkinnedModel ?? LoadAssetFullyLoaded<SkinnedModel>(SkinnedModelGuid, EquipmentDatabase.MaleBodyModelPath);
            var animGraph = preloadedAnimGraph ?? LoadAssetFullyLoaded<AnimationGraph>(AnimationGraphGuid, "Content/Character/Models/Animation Graph.flax");
            AssignAnimatedModelResources(animatedModel, root, skinnedModel, animGraph);

            // 如果 SkinnedModel 仍然不可用，添加一个可见的占位方块，避免角色完全不可见
            if (animatedModel.SkinnedModel == null || !animatedModel.SkinnedModel.IsLoaded)
            {
                Debug.LogWarning("[HundunWorldGame] 兜底角色缺少 SkinnedModel，附加可见占位方块");
                AddFallbackVisualCube(root);
            }

            // 添加一个简单的碰撞体让角色能站在地面上
            var collider = root.AddChild<BoxCollider>();
            collider.Name = "FallbackCollider";
            collider.Size = new Vector3(0.6f, 1.8f, 0.6f);
            collider.Center = new Vector3(0, 0.9f, 0);

            Debug.LogWarning("[HundunWorldGame] 已生成占位角色 Actor（带骨骼网格体+动画图+碰撞体），说明 Prefab 资源加载存在问题");
            return root;
        }

        /// <summary>
        /// 为兜底角色添加一个可见的引擎方块模型，使其在缺少 SkinnedModel 时仍能被看到。
        /// </summary>
        private static void AddFallbackVisualCube(Actor root)
        {
            if (root == null) return;

            var staticModel = root.AddChild<StaticModel>();
            staticModel.Name = "FallbackVisualCube";
            staticModel.LocalPosition = new Vector3(0, 0.9f, 0);
            staticModel.LocalScale = new Vector3(0.6f, 1.8f, 0.6f);

            var boxModel = Content.Load<Model>("Engine/Models/Box");
            if (boxModel != null && boxModel.IsLoaded)
            {
                staticModel.Model = boxModel;
            }
            else
            {
                // 若引擎方块未加载，尝试异步加载并让它自行完成
                boxModel = Content.LoadAsync<Model>("Engine/Models/Box");
                if (boxModel != null)
                {
                    boxModel.WaitForLoaded(5000.0);
                    staticModel.Model = boxModel;
                }
            }

            var material = Content.Load<MaterialBase>("Engine/WhiteMaterial");
            if (material != null && material.IsLoaded)
            {
                staticModel.SetMaterial(0, material);
            }

            Debug.Log($"[HundunWorldGame] 兜底可见方块状态: Model={(staticModel.Model?.Path ?? "null")}, Material={(staticModel.GetMaterial(0)?.Path ?? "null")}");
        }

        /// <summary>
        /// 获取本地玩家 Flax Actor（由 <see cref="CreateLocalPlayerActor"/> 创建）
        /// </summary>
        public Actor LocalPlayerActor => _localPlayerActor;

        /// <summary>
        /// 销毁指定 Actor（含其所有子层级）。Flax 的 Actor.Destroy 会递归销毁子 Actor。
        /// </summary>
        private static void DestroyActorRecursive(Actor actor)
        {
            if (actor == null) return;
            Actor.Destroy(actor);
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
        /// 获取 ArchWorldHost（驱动 SnapshotApplySystem 等同步系统）
        /// </summary>
        public ArchWorldHost ArchHost => _archWorldHost;

        /// <summary>
        /// 获取网络管理器
        /// </summary>
        public NetworkManager NetworkManager => _networkManager;

        /// <summary>
        /// 获取受控发送服务（实体同步类上行的唯一合法发送入口）。
        /// </summary>
        public HundunWorld.Game.SyncGuard.GuardSyncSender GuardSyncSender => _guardSyncSender;

        /// <summary>
        /// 获取集中式资格判定组件（统一规则源）。
        /// </summary>
        public Horizon.Game.ECS.Arch.SyncGuard.Contracts.IOutboundSyncAuthorizer OutboundSyncAuthorizer => _outboundSyncAuthorizer;

        /// <summary>
        /// 获取绑定关系注册表（供召唤/宠物管理业务登记绑定）。
        /// </summary>
        public Horizon.Game.ECS.Arch.SyncGuard.Contracts.IBindingRelationshipRegistry BindingRegistry => _bindingRegistry;

        /// <summary>
        /// 获取本地发送资格状态机。
        /// </summary>
        public HundunWorld.Game.SyncGuard.LocalSendEligibilityState EligibilityState => _eligibilityState;

        /// <summary>
        /// 获取重连恢复协商发送器（RemoteVisibility）。
        /// </summary>
        public HundunWorld.Game.RemoteVisibility.ReconnectResumeSender ReconnectResumeSender => _reconnectResumeSender;

        /// <summary>
        /// 获取重连恢复状态机（RemoteVisibility）。
        /// </summary>
        public HundunWorld.Game.RemoteVisibility.ReconnectResumeStateMachine ReconnectResumeStateMachine => _reconnectResumeStateMachine;

        /// <summary>
        /// 获取可见性审计（RemoteVisibility）。
        /// </summary>
        public HundunWorld.Game.RemoteVisibility.RemoteVisibilityAudit RemoteVisibilityAudit => _remoteVisibilityAudit;

        /// <summary>
        /// 装配远程角色观测链路修复组件（RemoteVisibility）并完成事件接线（spec 5.6.1 规则 1/3、design 2.1.2）。
        /// 需在 ECS 世界与系统注册、SyncGuard 装配完成后执行。
        /// </summary>
        private void InitializeRemoteVisibility()
        {
            if (_remoteVisibilityInitialized || _networkManager == null)
            {
                return;
            }

            try
            {
                // 1. 重连恢复状态机。
                _reconnectResumeStateMachine = new HundunWorld.Game.RemoteVisibility.ReconnectResumeStateMachine();

                // 2. 重连协商发送器（真实送达依据，spec 5.6.1 规则 1）。
                _reconnectResumeSender = new HundunWorld.Game.RemoteVisibility.ReconnectResumeSender(_networkManager);

                // 3. 可见性审计：注入应可见/实际呈现只读查询与补建回调。
                _remoteVisibilityAudit = new HundunWorld.Game.RemoteVisibility.RemoteVisibilityAudit(
                    getExpectedVisible: () => GetSnapshotApplySystemInstance()?.GetRemoteEntityIds(_archWorld) ?? Array.Empty<ulong>(),
                    getPresented: () => FlaxActorSyncSystem.Instance?.GetPresentedEntityIds() ?? Array.Empty<ulong>(),
                    reconcileCallback: () => FlaxActorSyncSystem.Instance?.ReconcileMissingActors(),
                    diagnosticsProvider: null);

                // 4. 观测编排：注入 FlaxActorSyncSystem 暂停开关与插值恢复动作。
                _remoteObservationCoordinator = new HundunWorld.Game.RemoteVisibility.RemoteObservationCoordinator(
                    _reconnectResumeStateMachine, _remoteVisibilityAudit, _networkManager)
                    .WithActorPauseControl(paused =>
                    {
                        if (FlaxActorSyncSystem.Instance != null)
                        {
                            FlaxActorSyncSystem.Instance.IsPaused = paused;
                        }
                    });

                // 5. 重连流程异常隔离包装。
                _reconnectFlowGuard = new HundunWorld.Game.RemoteVisibility.ReconnectFlowGuard();

                // 6. 事件接线（spec 6.2）：
                //   a) NetworkManager 重连成功路径经 ReconnectResumeSender 受控发送（结果回传状态机）。
                _networkManager.ReconnectResumeSendHook = (characterId, lastTick) =>
                    SendResumeAndFeedStateMachineAsync(characterId, lastTick);

                //   b) 重连成功事件链经 ReconnectFlowGuard 安全包装（替换 NetworkManager 直接订阅）。
                _reconnectFlowGuard.RegisterGuardedHandler("OnReconnectionSucceeded", () => _networkManager.RaiseReconnectionSucceededSafely());
                _networkManager.SubscribeReconnectFlowGuard(_reconnectFlowGuard);

                //   c) 状态机相位变化驱动观测编排（RecoveryComplete → 恢复 + 全量核对补建）。
                _reconnectResumeStateMachine.PhaseChanged += OnReconnectPhaseChanged;

                //   d) SnapshotApplySystem 基线应用通知 → 状态机推进 BaselineRebuilding。
                var snapshotSystem = GetSnapshotApplySystemInstance();
                if (snapshotSystem != null)
                {
                    snapshotSystem.BaselineApplied += OnBaselineSnapshotApplied;
                }

                // 7. 启动观测编排（订阅连接状态事件）。
                _remoteObservationCoordinator.Start();

                _remoteVisibilityInitialized = true;
                Debug.Log("[HundunWorldGame] RemoteVisibility 观测链路修复组件装配完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 装配 RemoteVisibility 失败: {ex.Message}");
            }
        }

        /// <summary>获取 SnapshotApplySystem 实例。</summary>
        private Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem? GetSnapshotApplySystemInstance()
        {
            if (_archWorldHost == null)
            {
                return null;
            }

            return _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.NetworkReceive)
                .OfType<Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem>()
                .FirstOrDefault();
        }

        /// <summary>受控发送协商消息并回传结果到恢复状态机。</summary>
        private async System.Threading.Tasks.Task<bool> SendResumeAndFeedStateMachineAsync(ulong characterId, long lastTick)
        {
            try
            {
                var result = await _reconnectResumeSender.SendResumeAsync(characterId, lastTick);
                if (result.Delivered)
                {
                    _reconnectResumeStateMachine.OnNegotiationDelivered();
                    return true;
                }

                _reconnectResumeStateMachine.OnNegotiationFailed(result.Reason ?? HundunWorld.Game.RemoteVisibility.Contracts.ResumeFailReason.ConnectionNotReady);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HundunWorldGame] 受控协商发送异常被隔离: {ex.Message}");
                _reconnectResumeStateMachine.OnNegotiationFailed(HundunWorld.Game.RemoteVisibility.Contracts.ResumeFailReason.ChannelError);
                return false;
            }
        }

        /// <summary>重连恢复相位变化 → 驱动观测编排（spec 6.2）。</summary>
        private void OnReconnectPhaseChanged(HundunWorld.Game.RemoteVisibility.Contracts.ReconnectResumePhaseSnapshot snapshot)
        {
            try
            {
                switch (snapshot.Phase)
                {
                    case HundunWorld.Game.RemoteVisibility.Contracts.ReconnectResumePhase.RecoveryComplete:
                        _remoteObservationCoordinator?.OnRecoveryCompleted();
                        // 资格恢复收敛信号（spec 5.5.1 规则 7/8）。
                        _eligibilityState?.OnReconnectRecoveryComplete();
                        Debug.Log("[HundunWorldGame] 重连恢复完成：观测与资格链路已收敛");
                        break;

                    case HundunWorld.Game.RemoteVisibility.Contracts.ReconnectResumePhase.Reconnecting:
                        _remoteObservationCoordinator?.OnReconnecting();
                        break;

                    case HundunWorld.Game.RemoteVisibility.Contracts.ReconnectResumePhase.RecoveryFailed:
                        // 回退全量握手重建基线（spec 4.2.9、6.4 规则 5）。
                        _remoteObservationCoordinator?.OnReconnecting();
                        Debug.LogWarning("[HundunWorldGame] 重连恢复失败，回退全量握手重建基线");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 重连相位编排异常被隔离: {ex.Message}");
            }
        }

        /// <summary>基线/续连快照应用 → 状态机推进 BaselineRebuilding（spec 6.4 规则 2）。</summary>
        private void OnBaselineSnapshotApplied(long serverTick)
        {
            try
            {
                _reconnectResumeStateMachine?.OnBaselineSnapshotApplied();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] 基线应用通知异常被隔离: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取客户端单连接编排协调器（连接精简治理，spec 5.1.1）。
        /// 登录/进游戏/重连三类建连请求统一经此互斥编排，保证任意时刻仅一条 TCP 连接在途。
        /// </summary>
        public ClientConnectionCoordinator ConnectionCoordinator => _connectionCoordinator;

        /// <summary>
        /// 获取世界管理器
        /// </summary>
        public WorldManager WorldManager => _worldManager;

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Debug.Log("HundunWorldGame 开始释放资源");

            // [修复] 取消订阅 Engine.RequestingExit，防止释放后再次触发 Dispose
            if (_requestingExitHandler != null)
            {
                Engine.RequestingExit -= _requestingExitHandler;
                _requestingExitHandler = null;
            }

            try
            {
                // 停止游戏
                _ = StopAsync();

                // 先取消订阅网络事件，防止释放过程中触发回调
                if (_networkManager != null)
                {
                    _networkManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _networkManager.ConnectionError -= OnConnectionError;
                    _networkManager.DisconnectTimedOut -= OnDisconnectTimedOut;
                    _networkManager.ConnectedStateChanged -= OnConnectedStateChanged;
                    _networkManager.SyncHandshakeStateChanged -= OnSyncHandshakeStateChanged;
                }

                // 取消同步包 handler 订阅后台重试循环，防止退出 PIE 后无界循环残留
                if (_syncSubscribeCts != null)
                {
                    try { _syncSubscribeCts.Cancel(); } catch { }
                    _syncSubscribeCts.Dispose();
                    _syncSubscribeCts = null;
                }

                // 释放各个系统组件
                _eventBroadcaster?.Dispose();
                _playerPositionUpdater?.Dispose();
                _worldManager?.Dispose();
                _moduleManager?.DisposeAllModules();
                _ecsManager?.Dispose();
                _archWorldHost?.Dispose();
                _networkManager?.Dispose();
                _worldDataManager?.Dispose();

                // 释放单例服务资源（仅在已创建的情况下）
                try
                {
                    CharacterPersistenceService.DisposeIfCreated();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"释放角色持久化服务时发生错误: {ex.Message}");
                }

                // 销毁Arch ECS世界（由 ArchWorldHost.Dispose 管理）
                // 注意：_archWorldHost.Dispose() 已经会销毁底层 World

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

        /// <summary>
        /// 把服务端下发的表现型脚本事件（<see cref="SyncEventKind.Vfx"/>）渲染成世界文字。
        /// <para>
        /// 坐标系：服务端 Z-up、Flax Y-up，故 (x,y,z) -> (x,z,y)——与远程角色位置同一换轴口径，
        /// 漏一次就会出现"事件飘在天边"。
        /// </para>
        /// </summary>
        /// <summary>
        /// 处理 DS 发回的脚本生成回执：把真实实体 ID 贴回聊天，让 <c>/despawn &lt;id&gt;</c> 可用。
        /// <para>定向包（受众只有操作者），因此不会污染同屏其他人的聊天。</para>
        /// </summary>
        private static void TryApplyScriptedSpawnResult(EventPacket packet)
        {
            if (packet?.Events == null) return;

            for (var i = 0; i < packet.Events.Length; i++)
            {
                var e = packet.Events[i];
                if (e.Kind != SyncEventKind.ScriptedSpawnResult || e.Payload == null || e.Payload.Length == 0)
                    continue;

                try
                {
                    var result = MemoryPack.MemoryPackSerializer
                        .Deserialize<Horizon.Game.Message.Sync.ScriptedSpawnResultPayload>(e.Payload);
                    if (result == null) continue;

                    global::HundunWorld.Game.Chat.WorldScriptClient.Instance
                        .ApplySpawnResult(result.RequestId, result.EntityIds, result.Removed);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ScriptedEvent] 生成回执解析失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 把服务端的怪物起手/收条事件贴到对应 Actor 的选中标记上（头顶名牌据此画施法条）。
        /// <para>
        /// <b>只写字段、不创建任何东西</b>：这条通路不保证在主线程上，而"条子晚一帧出现"完全无害；
        /// 为每个起手排一次主线程队列反而会把延迟固定成一帧以上。真要动 Actor（生成/销毁）就必须排队，
        /// 这也是 <see cref="TryShowScriptedEffect"/> 与本方法分开放的原因。
        /// </para>
        /// <para>找不到 Actor（还没进 AOI / 已经 Despawn）是正常情况，静默丢弃：
        /// 为看不见的主播条子报错只会淹没日志。</para>
        /// </summary>
        private static void TryApplyMonsterCast(EventPacket packet)
        {
            if (packet?.Events == null) return;
            var sync = FlaxActorSyncSystem.Instance;
            if (sync == null) return;

            for (var i = 0; i < packet.Events.Length; i++)
            {
                var e = packet.Events[i];
                var isStart = e.Kind == SyncEventKind.MonsterCastStart;
                if (!isStart && e.Kind != SyncEventKind.MonsterCastStop) continue;

                if (!sync.TryGetEntityActor(e.SourceEntityId, out var actor) || actor == null) continue;
                var tag = actor.GetScript<global::HundunWorld.Game.Selection.SelectableEntityTag>();
                if (tag == null) continue;

                if (!isStart)
                {
                    // 三个出口（完成/打断/取消）在这里待遇相同：客户端的状态位就是"清干净"，
                    // 区别只体现在表现层要不要闪一下——那由 EndCast 之后的下一帧自然消失。
                    tag.EndCast();
                    continue;
                }

                var schoolMask = 0;
                var interruptible = true;
                try
                {
                    if (e.Payload != null && e.Payload.Length > 0)
                    {
                        var payload = MemoryPack.MemoryPackSerializer
                            .Deserialize<Horizon.Game.Message.Sync.MonsterCastPayload>(e.Payload);
                        if (payload != null)
                        {
                            schoolMask = payload.SchoolMask;
                            interruptible = payload.Interruptible;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MonsterCast] 起手载荷解析失败（技能 {e.IntValue}）：{ex.Message}");
                }

                tag.BeginCast(e.IntValue, schoolMask, interruptible, e.FloatValue);
            }
        }

        /// <summary>
        /// 进战喊话：服务端只发文本号，文案按 <c>(ModelKey, sayId)</c> 在客户端文本表里查
        /// （<c>Data/Monsters/client_texts.json</c>，与外观表同一口径）。
        /// <para>查不到必须念出来：那正是"服务端认得有这一号、客户端查不到文案"的半截状态，
        /// 而它在屏幕上的表现与"这只怪没配台词"完全同形（都是沉默）。</para>
        /// </summary>
        private static void TryApplyMonsterSay(EventPacket packet)
        {
            if (packet?.Events == null) return;
            var sync = FlaxActorSyncSystem.Instance;
            if (sync == null) return;

            for (var i = 0; i < packet.Events.Length; i++)
            {
                var e = packet.Events[i];
                if (e.Kind != SyncEventKind.MonsterSay) continue;

                if (!sync.TryGetEntityActor(e.SourceEntityId, out var actor) || actor == null) continue;
                var tag = actor.GetScript<global::HundunWorld.Game.Selection.SelectableEntityTag>();
                if (tag == null) continue;

                if (!global::Game.Selection.MonsterTextCatalog
                        .TryResolve(tag.ModelKey, e.IntValue, out var text, out _))
                {
                    Debug.LogWarning($"[MonsterSay] 文本号 {e.IntValue} 在客户端文本表里查不到" +
                                     $"（模型键 {tag.ModelKey}）：跑一次 MonsterCli export-texts");
                    continue;
                }
                tag.ShowShout(text);
            }
        }

        private static void TryShowScriptedEffect(EventPacket packet)
        {
            if (packet?.Events == null) return;

            for (var i = 0; i < packet.Events.Length; i++)
            {
                var e = packet.Events[i];
                if (e.Kind != SyncEventKind.Vfx || e.Payload == null || e.Payload.Length == 0) continue;

                try
                {
                    var effect = MemoryPack.MemoryPackSerializer
                        .Deserialize<Horizon.Game.Message.Sync.ScriptedEffectPayload>(e.Payload);
                    if (string.IsNullOrEmpty(effect?.Text)) continue;

                    var position = new Vector3(effect.X, effect.Z, effect.Y);
                    var duration = effect.DurationSeconds > 0f ? effect.DurationSeconds : 5f;

                    var numbers = global::Game.Combat.Effects.DamageNumberSystem.Instance;
                    if (numbers != null)
                    {
                        numbers.ShowText(effect.Text, position, new Color(1f, 0.85f, 0.35f, 1f), duration);
                    }

                    // 世界文字只在视锥内可见；聊天里再留一条，保证操作者能确认事件已被服务端受理。
                    global::HundunWorld.Game.Chat.ChatClient.Instance.AddLocalSystem($"[世界事件] {effect.Text}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ScriptedEvent] Vfx 载荷解析失败：{ex.Message}");
                }
            }
        }
    }
}

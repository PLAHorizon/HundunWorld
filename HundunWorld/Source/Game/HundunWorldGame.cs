using FlaxEngine;
using Game.Performance;
using HundunWorld.Game.ECS;
using HundunWorld.Game.Modules;
using HundunWorld.Game.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.Character;
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
        // 避免每跨 16m chunk 边界就触发 1331 chunk 的 diff 计算和网络上行。
        private int _lastSubCenterCX = int.MinValue;
        private int _lastSubCenterCY = int.MinValue;
        private int _lastSubCenterCZ = int.MinValue;
        /// <summary>滞后阈值（chunk 数）：玩家移动超过此距离才重新订阅。radius=5 时，3 chunks=48m 移动才触发一次更新。</summary>
        private const int AoiHysteresisChunks = 3;

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
                if (gameState != null && gameState.IsInGame)
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

            // 3.6 重置 ReconciliationSystem 状态（风暴检测/冷却/统计），避免旧会话历史影响新会话
            if (_archWorldHost != null)
            {
                var reconciliationSys = _archWorldHost.GetSystems(Horizon.Game.ECS.Arch.Core.SystemGroup.FixedUpdate)
                    .OfType<Horizon.Game.ECS.Arch.Systems.ReconciliationSystem>()
                    .FirstOrDefault();
                reconciliationSys?.ResetState();
            }

            // 4. 重置快照基线缓存，确保重连后首个全量快照重建 baseline
            Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem.ResetLastAppliedSnapshot();

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
            // 桥接到 ECS 缓冲区，供 ReconciliationSystem 消费
            Horizon.Game.ECS.Arch.Network.InputAckReceiveBuffer.Instance.Latest = inputAck;

            // 推进 InputSendSystem 的已确认 tick，清理冗余重传环形缓冲中的已确认输入。
            // 若不调用此方法，_lastAckedClientTick 永远为 0，导致每次 Update 都触发冗余重传，
            // 大量旧 InputPacket 被发送到服务端后被去重拒绝，浪费带宽并污染日志。
            Horizon.Game.ECS.Arch.Systems.InputSendSystem.Instance?.OnInputAck(inputAck.LastProcessedClientTick);

            Debug.Log($"[HundunWorldGame] 收到输入确认: LastProcessedClientTick={inputAck.LastProcessedClientTick}");
        }

        /// <summary>
        /// 握手响应包接收处理
        /// </summary>
        private void OnHandshakeReceived(HandshakePacket handshake)
        {
            // 重连场景下重置快照基线缓存：ECS World 销毁重建后，SnapshotApplySystem._lastAppliedSnapshot
            // （static 字段）会残留旧 baseline，导致增量快照重建基于过期 baseline。
            // 每次握手都重置是安全的：首个全量快照（BaselineTick=0）会重新建立 baseline。
            Horizon.Game.ECS.Arch.Systems.SnapshotApplySystem.ResetLastAppliedSnapshot();
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
        /// 同步事件包接收处理
        /// 将 EventPacket 放入 EventReceiveBuffer，供 EventApplySystem 消费
        /// </summary>
        private void OnEventReceived(EventPacket eventPacket)
        {
            Horizon.Game.ECS.Arch.Network.EventReceiveBuffer.Instance.Enqueue(eventPacket);
            if (eventPacket.Events != null && eventPacket.Events.Length > 0)
            {
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

        private void OnChunkDiffReceived(WorldChunkDiffPacket diff)
        {
            if (diff?.Payload == null || diff.Payload.Length == 0)
            {
                Debug.LogWarning("[HundunWorldGame] OnChunkDiffReceived: Payload 为空，已丢弃");
                return;
            }
            try
            {
                var recvCount = Interlocked.Increment(ref _chunkDiffReceivedCount);
                switch (diff.PayloadType)
                {
                    case WorldChunkDiffPayloadType.EntityDelta:
                        {
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
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(200);
                    if (TrySubscribeSyncHandler())
                    {
                        return;
                    }
                }
                Debug.LogWarning("[HundunWorldGame] 6 秒内未找到 SyncPacketMessageHandler，启动后台周期重试...");

                // 后台周期恢复：每 2 秒重试一次，直到订阅成功。
                // 防止 NetworkManager 异步 handler 注册延迟导致永久订阅失败，
                // 进而让快照/ChunkDiff/握手事件完全无法桥接到 ECS。
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (await timer.WaitForNextTickAsync())
                {
                    if (TrySubscribeSyncHandler())
                    {
                        Debug.Log("[HundunWorldGame] 后台周期重试订阅成功。");
                        return;
                    }
                }
            });
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
                syncHandler.ChunkDiffReceived += OnChunkDiffReceived;
                Debug.Log("[HundunWorldGame] 已订阅同步包事件（快照桥接已建立，含 ChunkDiff）");

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
        /// 通过 <see cref="Physics.RayCast"/> 从高空向下射线检测 Terrain/碰撞体，
        /// 返回该位置的地面 ECS.Z 高度（米）；未命中任何碰撞体时返回 <see cref="float.NaN"/>。
        /// <para>
        /// 坐标转换：ECS Z-up ↔ Flax Y-up
        /// <list type="bullet">
        ///   <item>Flax.X = ECS.X（左右）</item>
        ///   <item>Flax.Y = ECS.Z（上下，高度）</item>
        ///   <item>Flax.Z = ECS.Y（前后）</item>
        /// </list>
        /// 因此在 Flax 世界 (x, 1000, y) 处从高空向下射线，命中点的 Flax.Y 即为地面 ECS.Z。
        /// </para>
        /// <para>
        /// 射线起点的 Y=1000 假定游戏世界地形最高点不超过 1000 米；若场景超高可调大。
        /// 射线长度 2000 覆盖从 +1000 到 -1000 的范围。
        /// 使用 <see cref="LayersMask.Default"/> 与 ThirdPersonCamera.GroundLayers 默认值一致。
        /// </para>
        /// </summary>
        /// <param name="ecsX">ECS 世界坐标 X（左右，米）</param>
        /// <param name="ecsY">ECS 世界坐标 Y（前后，米）</param>
        /// <returns>该位置的地面 ECS.Z 高度（米）；无地面返回 <see cref="float.NaN"/></returns>
        private float SampleGroundHeightEcs(float ecsX, float ecsY)
        {
            // ECS (x, y) → Flax 世界坐标 (x, 高空起点, y)
            var rayStart = new Vector3(ecsX, 1000f, ecsY);
            var rayDir = Vector3.Down;

            if (Physics.RayCast(rayStart, rayDir, out RayCastHit hit, 2000f, LayersMask.Default))
            {
                // 命中点的 Flax.Y 即对应 ECS.Z（地面高度）
                return hit.Point.Y;
            }

            // 未命中任何碰撞体（例如角色走到 Terrain 边界外或场景未配置地面）
            // 返回 NaN 让 LocalSimulationSystem 跳过地面约束，避免错误地把角色拉到 0 高度
            return float.NaN;
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
                return;
            }

            // 查找本地玩家 ECS 实体
            var query = new Arch.Core.QueryDescription()
                .WithAll<Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent, Horizon.Game.ECS.Arch.Components.PredictedTransformComponent>();

            bool found = false;
            _archWorld.Query(in query, (Arch.Core.Entity e, ref Horizon.Game.ECS.Arch.Components.NetworkIdentityComponent nid, ref Horizon.Game.ECS.Arch.Components.PredictedTransformComponent pred) =>
            {
                if (!nid.IsLocalPlayer || nid.EntityId != characterId) return;
                if (found) return;
                found = true;

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
                Debug.LogWarning($"[HundunWorldGame] RealignLocalPlayerToGround: 未找到本地玩家 ECS 实体 (CharacterId={characterId})");
            }
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
                const int ViewRadiusChunks = 10;
                const float MetresPerChunkCell = 16f;

                // 滞后防抖：计算当前 chunk 坐标，与上次订阅中心比较。
                int curCX = (int)MathF.Floor(x / MetresPerChunkCell);
                int curCY = (int)MathF.Floor(y / MetresPerChunkCell);
                int curCZ = (int)MathF.Floor(z / MetresPerChunkCell);

                if (_localPlayerSubscribedChunks != null && _localPlayerSubscribedChunks.Count > 0)
                {
                    // 已有订阅：检查是否移动超过滞后阈值
                    int dx = Math.Abs(curCX - _lastSubCenterCX);
                    int dy = Math.Abs(curCY - _lastSubCenterCY);
                    int dz = Math.Abs(curCZ - _lastSubCenterCZ);
                    if (dx < AoiHysteresisChunks && dy < AoiHysteresisChunks && dz < AoiHysteresisChunks)
                    {
                        return; // 未超过滞后阈值，跳过本次订阅更新
                    }
                }

                var newView = ComputeChunksInView(x, y, z, ViewRadiusChunks);

                var added = new List<ulong>();
                var removed = new List<ulong>();

                if (_localPlayerSubscribedChunks == null || _localPlayerSubscribedChunks.Count == 0)
                {
                    // 首次订阅：全部为 added
                    added.AddRange(newView);
                    Debug.Log($"[HundunWorldGame] 初始 AOI 订阅: {added.Count} chunks (radius={ViewRadiusChunks}), Pos=({x:F1},{y:F1},{z:F1})");
                }
                else
                {
                    // 增量 diff
                    foreach (var key in newView)
                    {
                        if (!_localPlayerSubscribedChunks.Contains(key))
                            added.Add(key);
                    }
                    foreach (var key in _localPlayerSubscribedChunks)
                    {
                        if (!newView.Contains(key))
                            removed.Add(key);
                    }

                    if (added.Count > 0 || removed.Count > 0)
                    {
                        Debug.Log($"[HundunWorldGame] AOI 订阅更新: Added={added.Count}, Removed={removed.Count}, Pos=({x:F1},{y:F1},{z:F1})");
                    }
                }

                // 更新本地订阅状态（乐观更新：立即反映，不等服务端确认）
                _localPlayerSubscribedChunks = newView;
                _lastSubCenterCX = curCX;
                _lastSubCenterCY = curCY;
                _lastSubCenterCZ = curCZ;

                // 异步发送订阅更新包（非阻塞，避免影响 FixedUpdate 节奏）
                if (added.Count > 0 || removed.Count > 0)
                {
                    var addedArr = added.ToArray();
                    var removedArr = removed.ToArray();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _networkManager.SendSubscriptionUpdateAsync(addedArr, removedArr);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[HundunWorldGame] SendSubscriptionUpdateAsync 失败: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HundunWorldGame] OnPlayerChunkChanged 异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 计算以世界坐标 (worldX, worldY, worldZ) 为中心、半径 radiusChunks 内的所有 chunk 的 MortonKey 集合。
        /// <para>
        /// 与服务端 <c>WorldCoord.GetChunksInView</c> + <c>MortonCodec.Encode3D</c> 完全等价。
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
        public static T LoadAssetFullyLoaded<T>(Guid guid, string path) where T : Asset
        {
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
            var preloadedSkinnedModel = LoadAssetFullyLoaded<SkinnedModel>(SkinnedModelGuid, "Content/Character/Models/skm_uefn_mannequin.flax");
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

                    // 获取 Prefab 中的 AnimatedModel；如果找不到则新建一个
                    animatedModel = actor.FindActor<AnimatedModel>() ?? actor.GetChild<AnimatedModel>();
                    if (animatedModel == null)
                    {
                        Debug.LogWarning($"[HundunWorldGame] Prefab 中缺少 AnimatedModel，新建一个: CharacterId={characterId}");
                        animatedModel = actor.AddChild<AnimatedModel>();
                        animatedModel.Name = "AnimatedModel";
                    }

                    // 安全初始化：优先等待 Prefab 自带引用加载完成，失败时才使用预加载资产替换
                    AssignAnimatedModelResources(animatedModel, actor, preloadedSkinnedModel, preloadedAnimGraph);
                }
            }

            actor.Name = $"LocalPlayer_{characterId}";
            actor.Position = position;
            _localPlayerActor = actor;

            // 自动添加并初始化 CharacterEquipmentManager，应用已保存的外观数据
            var equipmentManager = actor.AddScript<CharacterEquipmentManager>();
            if (equipmentManager != null)
            {
                var appearance = CharacterPersistenceService.Instance.LoadAppearanceAsync(characterId).GetAwaiter().GetResult();
                if (appearance == null)
                {
                    appearance = CharacterPersistenceService.AppearanceData.GetDefaultAppearance();
                }

                equipmentManager.Initialize(
                    appearance.BodyEquipmentId,
                    appearance.AccessoryIds,
                    appearance.WeaponIds);

                Debug.Log($"[HundunWorldGame] 已初始化 CharacterEquipmentManager: CharacterId={characterId}, Body={appearance.BodyEquipmentId}, Accessories=[{string.Join(",", appearance.AccessoryIds)}], Weapons=[{string.Join(",", appearance.WeaponIds)}]");
            }
            else
            {
                Debug.LogError($"[HundunWorldGame] 无法为角色添加 CharacterEquipmentManager: CharacterId={characterId}");
            }

            // 最终状态校验与兜底：如果 AnimatedModel 仍未获得已加载的 SkinnedModel，禁用它防止断言
            // 注意：不设置 SkinnedModel = null，避免竞态中 Job 调用 SetupSkinningData 触发断言。
            // 只设置 UpdateMode = Never 和 IsActive = false，让 CanUpdateModel 自然返回 false。
            animatedModel = actor.FindActor<AnimatedModel>();
            var finalSkinnedModel = animatedModel?.SkinnedModel;
            if (animatedModel != null && (finalSkinnedModel == null || !finalSkinnedModel.IsLoaded))
            {
                Debug.LogError($"[HundunWorldGame][PACKAGE] 最终校验失败：AnimatedModel 的 SkinnedModel 未加载，禁用 AnimatedModel 防止断言");
                animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
                animatedModel.IsActive = false;
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

            // ★ 修复「进入游戏后在天上飞」：重新对齐地面高度。
            // CreateLocalPlayerEntity 在角色选择场景中被调用（GameWorld 未加载），
            // 此时 SampleGroundHeightEcs 的 Physics.RayCast 无法命中 Terrain → 返回 NaN，
            // pred.Z 使用了服务端 initialY（可能为 0 或上次保存值，与实际地形高度不一致）。
            // 此刻 GameWorld 场景已加载完毕，Physics.RayCast 可正常工作，重新采样并修正 ECS 实体高度。
            RealignLocalPlayerToGround(characterId, x, z);

            return actor;
        }

        // 角色资产 GUID（来自 CharacterRoot.prefab）
        // 重要：SkinnedModelGuid 对应的资产必须是 FlaxEngine.SkinnedModel 类型。
        // 如果实际是 FlaxEngine.Model，Content.Load<SkinnedModel> 会失败，导致角色没有骨骼网格体。
        // 出现此类问题时，请在 Flax Editor 中重新导入源 FBX 并选择 Type = SkinnedModel。
        private static readonly Guid SkinnedModelGuid = new Guid("c7c70820409088e4d96db396a43c410f");
        private static readonly Guid AnimationGraphGuid = new Guid("ceded67f4bb2623f40b4dcb493b0d419");

        /// <summary>
        /// 安全初始化 AnimatedModel 的资源引用。
        /// 策略：优先保留并等待 Prefab 自带的 SkinnedModel/AnimationGraph 引用加载完成；
        /// 只有当自带引用无法加载时，才使用预加载资产替换。
        /// 所有资源切换都遵循竞态安全协议：UpdateMode=Never、IsActive=false、先清空 AnimationGraph 再改 SkinnedModel。
        /// </summary>
        private static void AssignAnimatedModelResources(AnimatedModel animatedModel, Actor rootActor,
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
        private static FlaxEngine.Scene FindGameWorldScene()
        {
            // 方式1：查找包含 WorldSceneInitializer 脚本的场景
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene == null) continue;
                if (scene.GetScript<WorldSceneInitializer>() != null)
                    return scene;
            }

            // 方式2：查找包含 Terrain 的场景（World 场景有 Terrain）
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene == null) continue;
                if (scene.FindActor<Terrain>() != null)
                    return scene;
            }

            // 方式3：按名称查找（兜底）
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && (scene.Name == "World" || scene.Name == "WorldScene"))
                {
                    return scene;
                }
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

            // 查找目标场景并确保 Actor 生成在正确的场景中
            var targetScene = FindGameWorldScene();
            if (targetScene != null)
            {
                Level.SpawnActor(root, targetScene);
            }
            else
            {
                Level.SpawnActor(root);
            }

            // 创建 AnimatedModel 并加载骨骼网格体和动画图
            var animatedModel = root.AddChild<AnimatedModel>();
            animatedModel.Name = "FallbackModel";
            animatedModel.Position = Vector3.Zero;

            // 使用预加载资产；若未提供则再次尝试完全加载
            var skinnedModel = preloadedSkinnedModel ?? LoadAssetFullyLoaded<SkinnedModel>(SkinnedModelGuid, "Content/Character/Models/skm_uefn_mannequin.flax");
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
    }
}

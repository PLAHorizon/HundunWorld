using System;
using System.Collections.Generic;
using Arch.Core;
using FlaxEngine;
using Game.Combat;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Diagnostics;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync.Components;
using Game.Character.Attributes;
using HundunWorld.Game.Character;
using HundunWorld.Game.Network;
using HundunWorld.Game.Selection;

namespace HundunWorld.Game
{
    /// <summary>
    /// [Phase C7] 远程实体渲染同步的分级更新优先级（由 FlaxActorSyncSystem 按距离分级 + 数量降档计算）。
    /// </summary>
    public enum RemoteEntityUpdatePriority
    {
        /// <summary>每帧更新位置 + 朝向 + 动画（Near 档，≤ NearDistanceMeters）。</summary>
        EveryFrame = 0,

        /// <summary>每帧更新位置 + 朝向，动画隔帧更新（Mid 档）。</summary>
        AnimationEveryOtherFrame = 1,

        /// <summary>位置 + 朝向 + 动画全部隔帧更新（Far 档）。</summary>
        PositionOnlyEveryOtherFrame = 2,
    }

    /// <summary>
    /// ECS→Flax Actor 视觉桥接系统：将 Arch ECS 中远程实体的插值位置同步到 Flax Engine Actor，
    /// 使远程角色在屏幕上可见。
    ///
    /// 运行在 Flax 主线程（作为 Script），在 ECSUpdateDriver 之后执行。
    /// 订阅 SnapshotApplySystem 的 Spawn/Despawn 事件来创建/销毁 Flax Actor。
    /// 每帧从 Arch World 查询 InterpolatedTransformComponent 来同步位置。
    /// </summary>
    public class FlaxActorSyncSystem : Script
    {
        /// <summary>单例引用（OnEnable 设置，OnDisable 清空），供断线清理时快速访问。</summary>
        public static FlaxActorSyncSystem? Instance { get; private set; }

        /// <summary>EntityId → Flax Actor 的映射表。</summary>
        private readonly Dictionary<ulong, Actor> _entityIdToActor = new();

        /// <summary>
        /// 已经替哪些 NPC 拉过任务清单（每个实体只问一次；Despawn 时移除，防实体 id 复用时问不到）。
        /// </summary>
        private readonly HashSet<ulong> _questOffersRequested = new();

        /// <summary>
        /// 按服务器实体 ID 找 Actor（供游戏层把离散事件贴到"那一个"实体上，例如怪物起手事件）。
        /// <para>找不到是正常情况：实体可能还没进 AOI、或已经 Despawn。调用方必须把"找不到"当成
        /// 一次静默丢弃而不是错误——为一条看不见的主播条子报错只会淹没日志。</para>
        /// </summary>
        public bool TryGetEntityActor(ulong entityId, out Actor? actor)
        {
            actor = null;
            if (entityId == 0UL) return false;
            return _entityIdToActor.TryGetValue(entityId, out actor);
        }

        /// <summary>[Phase C5] 断线期间暂停位置更新（不 Destroy Actor，重连后恢复）。</summary>
        public bool IsPaused { get; set; } = false;

        /// <summary>[Phase C6] 远程实体数超过此阈值时启用 LOD 降频（非最近实体每 2 帧更新一次）。</summary>
        public int LodEntityThreshold { get; set; } = 50;

        /// <summary>[Phase C7] 远程角色分级更新距离：Near 档上限（米），默认 30，由 ECSUpdateDriver 注入。</summary>
        public float NearDistanceMeters { get; set; } = 30f;

        /// <summary>[Phase C7] 远程角色分级更新距离：Mid 档上限（米），默认 80，由 ECSUpdateDriver 注入。</summary>
        public float MidDistanceMeters { get; set; } = 80f;

        /// <summary>[Phase C7] 远程角色性能降档阈值（个），超此数量整体降档，默认 10，由 ECSUpdateDriver 注入。</summary>
        public int PerformanceDegradeEntityCount { get; set; } = 10;

        /// <summary>[Phase C7] 远程角色数量硬上限（个），超限暂停最远角色插值推进，默认 20，由 ECSUpdateDriver 注入。</summary>
        public int MaxRemoteEntityCount { get; set; } = 20;

        /// <summary>[超大规模] 超规模实体数上限（个），默认 5000；同屏实体数超过该值进入 OverLimit 档位最远优先降级。</summary>
        public int UltraScaleEntityCap { get; set; } = 5000;

        /// <summary>[超大规模] 客户端规模档位控制器（spec 5.5.1.3），由 ECSUpdateDriver 装配注入。</summary>
        public SyncScaleController? ScaleController { get; set; }

        /// <summary>[Phase C7] 远程实体分级更新优先级（距离分级 + 数量降档后的帧预算）。</summary>
        private readonly Dictionary<ulong, RemoteEntityUpdatePriority> _entityIdToUpdatePriority = new();

        /// <summary>[Phase C7] 上次触发"数量降档"诊断的远程角色数（去重限频）。</summary>
        private int _lastDegradeDiagCount;

        /// <summary>[Phase C7] 上次触发"数量硬上限"诊断的远程角色数（去重限频）。</summary>
        private int _lastMaxCapDiagCount;

        /// <summary>[Phase C7] 诊断事件汇（可选）。由 ECSUpdateDriver 注入，null 时不输出诊断日志。</summary>
        public Horizon.Game.ECS.Arch.Diagnostics.ISyncDiagnosticsSink? Diagnostics { get; set; }

        /// <summary>[Phase C6] LOD 降频帧计数（奇偶帧交替更新）。</summary>
        private long _lodFrameCount;

        /// <summary>EntityId → 上次同步位置（用于检测是否需要更新）。</summary>
        private readonly Dictionary<ulong, Vector3> _entityIdToLastPosition = new();

        /// <summary>Arch World 引用。</summary>
        private World _archWorld;

        /// <summary>是否已成功订阅事件。</summary>
        private bool _eventsSubscribed = false;

        /// <summary>远程角色复用的 Prefab 路径（与本地玩家 CharacterRoot 一致，含完整外观+动画）。
        /// 修复：必须带 .prefab 扩展名，否则 Flax Content API 解析为绝对路径导致找不到文件。</summary>
        [Tooltip("远程角色复用的 Prefab 路径（与本地玩家一致，含完整外观与动画）")]
        public string RemotePlayerPrefabPath { get; set; } = "Content/Prefabs/Character/CharacterRoot.prefab";

        /// <summary>CharacterRoot Prefab 的 GUID。
        /// 修复：初始化为 Guid.Empty，运行时通过 Content.GetAssetInfo 从路径动态获取真实 GUID，
        /// 避免 .NET Guid 字节序与 Flax C++ GUID 不一致问题。
        /// 与 HundunWorldGame.LocalPlayerPrefabGuid 保持一致策略。</summary>
        [Tooltip("远程角色 Prefab 的 GUID（与本地玩家 CharacterRoot 一致）")]
        public Guid RemotePlayerPrefabGuid { get; set; } = Guid.Empty;

        /// <summary>远程角色默认模型缩放。</summary>
        [Tooltip("远程角色默认模型缩放")]
        public float DefaultModelScale { get; set; } = 1.0f;

        /// <summary>
        /// 怪物 Prefab 路径。留空时怪物沿用远程角色的占位 Prefab（工程内目前没有任何怪物美术资源，
        /// <c>Content/Models/Monsters</c> 下只有材质），此时靠头顶名牌与选中高亮环区分敌我。
        /// 补上怪物 Prefab 后无需改代码即生效。
        /// </summary>
        [Tooltip("怪物 Prefab 路径（留空则用占位角色 Prefab）")]
        public string MonsterPrefabPath { get; set; } = string.Empty;

        /// <summary>怪物 Prefab 的 GUID；首次使用时按 <see cref="MonsterPrefabPath"/> 解析并缓存。</summary>
        public Guid MonsterPrefabGuid { get; set; } = Guid.Empty;

        /// <summary>是否使用简单的胶囊体代替模型（调试用）。
        /// 默认 false，让远程角色使用 Prefab 中的完整 SkinnedModel 资源。</summary>
        [Tooltip("使用简单胶囊体代替模型（调试用，默认关闭）")]
        public bool UseDebugCapsule { get; set; } = false;

        public override void OnStart()
        {
            Instance = this;
            _archWorld = HundunWorldGame.Instance?.ArchWorld;
            if (_archWorld == null)
            {
                Debug.LogWarning("[FlaxActorSyncSystem] Arch World 未就绪，将在首次 Update 时重试");
            }

            // 订阅 SnapshotApplySystem 的 Spawn/Despawn 事件
            SubscribeToSnapshotEvents();

            Debug.Log("[FlaxActorSyncSystem] 初始化完成");
        }

        private bool _firstUpdateDiag = true;
        // 诊断：OnUpdate 帧计数，用于限频日志（每 120 帧 ≈ 2 秒输出一次）
        private long _onUpdateFrameCount;

        public override void OnUpdate()
        {
            // 确保 Arch World 已获取
            if (_archWorld == null)
            {
                _archWorld = HundunWorldGame.Instance?.ArchWorld;
            }

            // 每帧检查事件订阅状态，未订阅时重试
            if (!_eventsSubscribed && _archWorld != null)
            {
                SubscribeToSnapshotEvents();
            }

            // 首帧诊断日志
            if (_firstUpdateDiag)
            {
                _firstUpdateDiag = false;
                var archHost = HundunWorldGame.Instance?.ArchHost;
                Debug.Log($"[FlaxActorSyncSystem] 首帧诊断: ArchWorld={_archWorld != null}, EventsSubscribed={_eventsSubscribed}, ArchHost={archHost != null}, RemoteActorCount={_entityIdToActor.Count}");
            }

            // 帧计数（所有构建配置均递增，供 ReconcileMissingActors 限频使用）
            var frameCount = System.Threading.Interlocked.Increment(ref _onUpdateFrameCount);

#if DEBUG
            // 限频诊断：每 120 帧（≈2 秒）输出系统运行状态 + Actor/实体计数（仅 Debug 构建）
            if (frameCount <= 3 || frameCount % 120 == 1)
            {
                int interpEntityCount = 0;
                if (_archWorld != null)
                {
                    var countQuery = new QueryDescription().WithAll<InterpolatedTransformComponent>();
                    _archWorld.Query(in countQuery, (Entity e, ref InterpolatedTransformComponent _) => interpEntityCount++);
                }
                Debug.Log($"[FlaxActorSyncSystem] OnUpdate#{frameCount}: ArchWorld={_archWorld != null}, EventsSubscribed={_eventsSubscribed}, ActorCount={_entityIdToActor.Count}, InterpEntityCount={interpEntityCount}");
            }
#endif

            // 周期性补偿检查：确保所有远程实体都有对应的 Flax Actor。
            // 覆盖场景：事件订阅时序竞态、网络抖动导致 Spawn 事件丢失、重连后实体重建。
            // 修复（进入游戏时观测不到远程角色）：原间隔 120 帧（≈2秒）过长，
            // 新玩家进入游戏后首批 Spawn 事件若被错过，需等待 2 秒才能补创建 Actor，
            // 表现为"进入场景后看不到其他玩家"。缩短到 30 帧（≈0.5秒），大幅缩短不可见窗口。
            if (_eventsSubscribed && _archWorld != null && frameCount % 30 == 15)
            {
                ReconcileMissingActors();
            }

            // 同步远程实体的插值位置到 Flax Actor
            SyncInterpolatedPositions();
        }

        public override void OnDestroy()
        {
            // 取消订阅事件
            UnsubscribeFromSnapshotEvents();

            // 销毁所有已创建的 Actor
            ClearAllActors();

            Instance = null;

            Debug.Log("[FlaxActorSyncSystem] 已销毁，清理了所有远程角色 Actor");
        }

        /// <summary>
        /// 订阅 SnapshotApplySystem 的 Spawn/Despawn 事件。
        /// 订阅成功后立即扫描已有远程实体并补创建 Actor（修复时序竞态：首批快照在订阅前处理导致 Actor 缺失）。
        /// </summary>
        private void SubscribeToSnapshotEvents()
        {
            var archHost = HundunWorldGame.Instance?.ArchHost;
            if (archHost == null) return;

            var snapshotSystem = GetSnapshotApplySystem(archHost);
            if (snapshotSystem == null) return;

            snapshotSystem.EntitySpawned += OnEntitySpawned;
            snapshotSystem.EntityDespawned += OnEntityDespawned;
            _eventsSubscribed = true;
            Debug.Log("[FlaxActorSyncSystem] 已订阅 SnapshotApplySystem 事件");

            // 修复 BUG：订阅前已 Spawn 的远程实体不会触发 EntitySpawned 事件，
            // 导致 Flax Actor 永远不被创建（玩家间不可见）。
            // 订阅成功后立即扫描已有实体，补创建缺失的 Actor。
            CreateActorsForExistingEntities();
        }

        /// <summary>
        /// 扫描 Arch World 中已存在的远程实体，为缺少 Flax Actor 的实体补创建。
        /// 解决事件订阅时序竞态：若 SnapshotApplySystem 在 FlaxActorSyncSystem 订阅前已处理了 Spawn，
        /// 则 EntitySpawned 事件被错过，远程角色 Actor 不会被创建。
        /// </summary>
        private void CreateActorsForExistingEntities()
        {
            if (_archWorld == null) return;

            int created = 0;
            var query = new QueryDescription()
                .WithAll<InterpolatedTransformComponent, NetworkIdentityComponent, AuthTransformComponent>();

            _archWorld.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp, ref NetworkIdentityComponent netId, ref AuthTransformComponent auth) =>
            {
                // 跳过本地玩家
                if (netId.IsLocalPlayer) return;

                // 已有 Actor 的实体跳过
                if (_entityIdToActor.ContainsKey(netId.EntityId)) return;

                // 补创建远程实体 Actor
                Actor remoteActor = CreateRemoteEntityActor(netId.EntityId, interp.X, interp.Y, interp.Z,
                    netId.EntityType, netId.Nickname, netId.Level, netId);
                if (remoteActor != null)
                {
                    _entityIdToActor[netId.EntityId] = remoteActor;
                    _entityIdToLastPosition[netId.EntityId] = new Vector3(interp.X, interp.Y, interp.Z);
                    created++;
                }
            });

            if (created > 0)
            {
                Debug.Log($"[FlaxActorSyncSystem] 补创建了 {created} 个已有远程实体的 Actor（修复订阅时序竞态）");
            }
        }

        /// <summary>
        /// 周期性补偿：检查是否有远程实体缺少对应 Actor，若有则补创建。
        /// 轻量级检查：先比较计数，不匹配时才遍历。
        /// 公开化：供可见性审计与恢复编排显式触发（spec 5.4.1 规则 2、design 2.1.3(4)）。
        /// </summary>
        public void ReconcileMissingActors()
        {
            if (_archWorld == null) return;

            // 快速计数检查：如果远程实体数 == Actor 数，无需遍历
            int remoteEntityCount = 0;
            var countQuery = new QueryDescription()
                .WithAll<InterpolatedTransformComponent, NetworkIdentityComponent>();
            _archWorld.Query(in countQuery, (Entity e, ref InterpolatedTransformComponent _, ref NetworkIdentityComponent nid) =>
            {
                if (!nid.IsLocalPlayer) remoteEntityCount++;
            });

            if (remoteEntityCount <= _entityIdToActor.Count) return;

            // 存在缺失 Actor 的实体，补创建
            CreateActorsForExistingEntities();
        }

        /// <summary>
        /// 导出实际呈现的远程实体 ID 集合（供可见性审计核对，design 2.1.3(4)）。
        /// 复用 <see cref="_entityIdToActor"/> 键集合，不新增映射。
        /// </summary>
        public System.Collections.Generic.IReadOnlyCollection<ulong> GetPresentedEntityIds()
        {
            try
            {
                lock (_entityIdToActor)
                {
                    return new List<ulong>(_entityIdToActor.Keys);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlaxActorSyncSystem] GetPresentedEntityIds 异常: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        /// <summary>
        /// 取消订阅 SnapshotApplySystem 事件。
        /// </summary>
        private void UnsubscribeFromSnapshotEvents()
        {
            var archHost = HundunWorldGame.Instance?.ArchHost;
            if (archHost == null) return;

            var snapshotSystem = GetSnapshotApplySystem(archHost);
            if (snapshotSystem == null) return;

            snapshotSystem.EntitySpawned -= OnEntitySpawned;
            snapshotSystem.EntityDespawned -= OnEntityDespawned;
        }

        /// <summary>
        /// 获取 SnapshotApplySystem 实例。
        /// </summary>
        private SnapshotApplySystem GetSnapshotApplySystem(ArchWorldHost archHost)
        {
            var systems = archHost.GetSystems(SystemGroup.NetworkReceive);
            foreach (var sys in systems)
            {
                if (sys is SnapshotApplySystem snapshotSys)
                    return snapshotSys;
            }
            return null;
        }

        /// <summary>
        /// 获取 InterpolationSystem 实例（用于同步规模档位降级集合）。
        /// </summary>
        private InterpolationSystem GetInterpolationSystem(ArchWorldHost archHost)
        {
            var systems = archHost.GetSystems(SystemGroup.Render);
            foreach (var sys in systems)
            {
                if (sys is InterpolationSystem interpSys)
                    return interpSys;
            }
            return null;
        }

        /// <summary>
        /// 实体 Spawn 事件处理：创建对应的 Flax Actor。
        /// </summary>
        private void OnEntitySpawned(SnapshotApplySystem.EntitySpawnedEventArgs args)
        {
            if (_entityIdToActor.ContainsKey(args.EntityId))
            {
                // 已存在，跳过
                return;
            }

            // 本地玩家不需要创建远程 Actor
            if (args.IsLocalPlayer)
            {
                Debug.Log($"[FlaxActorSyncSystem] 本地玩家实体 Spawn，跳过创建远程 Actor: EntityId={args.EntityId}");
                return;
            }

            // 创建远程实体 Actor（类型由服务端下发的 EntityType 决定）。
            // 外观字段是 spawn-only 的 Identity 内容，这里从同一帧的 ECS 组件取（事件已带 Arch 实体句柄）。
            var spawnIdentity = default(NetworkIdentityComponent);
            if (_archWorld != null && args.ArchEntity != default && _archWorld.IsAlive(args.ArchEntity))
            {
                try
                {
                    spawnIdentity = _archWorld.Get<NetworkIdentityComponent>(args.ArchEntity);
                }
                catch
                {
                    // 取不到就当没下发外观：回退占位 Prefab，绝不因此放弃创建 Actor。
                }
            }
            Actor remoteActor = CreateRemoteEntityActor(args.EntityId, args.X, args.Y, args.Z,
                args.EntityType, args.Nickname, args.Level, spawnIdentity);
            if (remoteActor != null)
            {
                _entityIdToActor[args.EntityId] = remoteActor;
                _entityIdToLastPosition[args.EntityId] = new Vector3(args.X, args.Y, args.Z);
                Debug.Log($"[FlaxActorSyncSystem] 远程 Actor 已创建: EntityId={args.EntityId}, Type={args.EntityType}, Pos=({args.X:F2}, {args.Y:F2}, {args.Z:F2})");
            }
        }

        /// <summary>
        /// 实体 Despawn 事件处理：销毁对应的 Flax Actor。
        /// </summary>
        private void OnEntityDespawned(SnapshotApplySystem.EntityDespawnedEventArgs args)
        {
            if (_entityIdToActor.TryGetValue(args.EntityId, out var actor))
            {
                if (actor != null)
                {
                    Destroy(actor);
                }
                // 清理所有相关字典，避免引用残留
                _entityIdToActor.Remove(args.EntityId);
                _entityIdToLastPosition.Remove(args.EntityId);
                _entityIdToLastYaw.Remove(args.EntityId);
                _entityIdToAnimatedModel.Remove(args.EntityId);
                _entityIdToIsWalkingParam.Remove(args.EntityId);
                _entityIdIsWalkingMissing.Remove(args.EntityId);
                _entityIdToAnimationController.Remove(args.EntityId);
                _entityIdToUpdatePriority.Remove(args.EntityId);
                _questOffersRequested.Remove(args.EntityId);
                Debug.Log($"[FlaxActorSyncSystem] 远程角色 Actor 已销毁: EntityId={args.EntityId}");
            }
        }

        /// <summary>
        /// 清理所有远程角色 Actor 并重置内部映射（断线/重连场景使用）。
        /// 在客户端断线时调用，避免离线期间远程角色 Actor 残留。
        /// </summary>
        public void ClearAllActors()
        {
            var count = _entityIdToActor.Count;
            foreach (var kv in _entityIdToActor)
            {
                if (kv.Value != null)
                {
                    Destroy(kv.Value);
                }
            }
            _entityIdToActor.Clear();
            _entityIdToLastPosition.Clear();
            _entityIdToLastYaw.Clear();
            _entityIdToAnimatedModel.Clear();
            _entityIdToIsWalkingParam.Clear();
            _entityIdIsWalkingMissing.Clear();
            _entityIdToAnimationController.Clear();
            _entityIdToUpdatePriority.Clear();
            _questOffersRequested.Clear();

            if (count > 0)
            {
                Debug.Log($"[FlaxActorSyncSystem] 已清理所有远程角色 Actor: {count} 个");
            }
        }

        /// <summary>
        /// 创建远程实体的 Flax Actor（玩家 / 怪物按服务端下发的 <paramref name="wireEntityType"/> 分流）。
        /// 失效点 #3 修复：改用 PrefabManager.SpawnPrefab 复用 CharacterRoot Prefab，
        /// 与本地玩家共享完整外观（SkinnedModel + AnimationGraph + MaterialController*）。
        /// Spawn 后必须禁用 prefab 自带的本机控制脚本（PlayerController 等），
        /// 否则远程实例会读取本机输入并抢写本地玩家 ECS 实体的 PlayerInputComponent。
        /// <para>
        /// <c>wireEntityType</c> 取服务端 Identity.EntityType（对齐共享 NetworkEntityType 取值），
        /// <c>displayName</c> 是头顶信息卡显示名（玩家昵称 / 怪物模板名，可空），
        /// <c>level</c> 为 0 表示服务端未下发等级。
        /// </para>
        /// </summary>
        private Actor CreateRemoteEntityActor(ulong entityId, float x, float y, float z, int wireEntityType, string displayName, int level,
            NetworkIdentityComponent identity = default)
        {
            var kind = KindFromWireType(wireEntityType);

            try
            {
                var position = new Vector3(x, y, z);

                // 1) 加载 Prefab：
                //    怪物按服务端下发的 model_key 查外观目录（编辑器配的那份）；
                //    查不到再退 MonsterPrefabPath，最后才退角色占位 Prefab。
                //    三级回退的意义：漏配表现为"这只怪还是占位人形 + 一条警告"，而不是随机长成新样子。
                var useMonsterPrefab = kind == SelectableKind.Monster && !string.IsNullOrEmpty(MonsterPrefabPath);
                var prefabPath = useMonsterPrefab ? MonsterPrefabPath : RemotePlayerPrefabPath;
                var prefabGuid = useMonsterPrefab ? MonsterPrefabGuid : RemotePlayerPrefabGuid;

                var monsterScale = 0f;
                var monsterStand = -1;
                var fromVisualCatalog = false;
                if (kind == SelectableKind.Monster)
                {
                    var catalogPath = string.Empty;
                    var catalogScale = 0f;
                    var catalogStand = -1;
                    var hit = MonsterVisualCatalog.TryResolve(identity.ModelKey, out catalogPath, out catalogScale, out catalogStand);
                    if (hit && !string.IsNullOrEmpty(catalogPath))
                    {
                        prefabPath = catalogPath;
                        prefabGuid = Guid.Empty;      // 按新路径重解 GUID（GUID 与路径必须同源自洽）
                        useMonsterPrefab = true;
                        fromVisualCatalog = true;
                        monsterScale = catalogScale;
                        monsterStand = catalogStand;
                    }
                    else if (!string.IsNullOrEmpty(identity.ModelKey))
                    {
                        // 只在"下发了模型键但目录里没有"时警告一次：漏配必须显形，
                        // 但已经回退到占位外观，不能因为警告而让实体不可见。
                        Debug.LogWarning($"[FlaxActorSyncSystem] 模型键 \"{identity.ModelKey}\"（怪物 {entityId}）不在外观目录" +
                                         $"（来源 {MonsterVisualCatalog.Source}），回退占位 Prefab");
                    }
                    // 下发值优先于目录里的通用值（编辑器逐只配的缩放才是权威）
                    if (identity.ModelScale > 0f) monsterScale = identity.ModelScale;
                    if (identity.StandState >= 0) monsterStand = identity.StandState;
                }

                // NPC 的逐只美术：立场表的 `prefab`（编号为准、模型键兜底）。
                // 空串/解析失败都回退占位人形——一条坏数据不该让 NPC 从屏幕上消失。
                var useNpcPrefab = false;
                if (kind == SelectableKind.Npc)
                {
                    if (NpcStanceCatalog.TryGetPrefab(identity.TemplateId, identity.ModelKey, out var npcPrefab)
                        && !string.IsNullOrEmpty(npcPrefab)
                        && Guid.TryParse(npcPrefab, out var npcPrefabGuid)
                        && npcPrefabGuid != Guid.Empty)
                    {
                        prefabGuid = npcPrefabGuid;
                        prefabPath = string.Empty;   // GUID 是唯一身份：再带路径只会多一种拼写
                        useNpcPrefab = true;
                    }

                    // 体型缩放对 NPC 同样生效：以前只有怪物吃这一格，编辑器里把铁匠调大调小都没反应。
                    if (identity.ModelScale > 0f) monsterScale = identity.ModelScale;
                }


                if (prefabGuid == Guid.Empty && !string.IsNullOrEmpty(prefabPath))
                {
                    if (HundunWorldGame.TryGetAssetGuidFromPath(prefabPath, out var realGuid))
                    {
                        // 只有"回退用的那两条固定路径"才配进字段缓存。
                        // 目录命中时 prefabPath 是**这一只怪**的 prefab，写进 MonsterPrefabGuid
                        // 会让下一只查不到目录的怪拿"上一只怪的 GUID + 自己的回退路径"去加载——
                        // GUID 优先于路径，结果就是它静默长成了别的怪的样子（正是本文件承诺绝不发生的事）。
                        if (!fromVisualCatalog)
                        {
                            if (useMonsterPrefab) MonsterPrefabGuid = realGuid;
                            else RemotePlayerPrefabGuid = realGuid;
                        }
                        prefabGuid = realGuid;
                        Debug.Log($"[FlaxActorSyncSystem] 从路径动态获取到 Prefab 真实 GUID: {realGuid}");
                    }
                    else
                    {
                        Debug.LogWarning($"[FlaxActorSyncSystem] 无法从路径获取 Prefab GUID，将使用路径加载: {prefabPath}");
                    }
                }

                // NPC 配的 GUID 可能是错的（贴错/资产被删）。加载失败必须回退占位人形——
                // 否则一条坏配置让这只 NPC 直接不可见，而日志只在宿主里，玩家只看到"人没了"。
                var npcFallbackPath = useNpcPrefab ? RemotePlayerPrefabPath : string.Empty;
                var npcFallbackGuid = useNpcPrefab ? RemotePlayerPrefabGuid : Guid.Empty;

                var prefab = HundunWorldGame.LoadAssetFullyLoaded<Prefab>(prefabGuid, prefabPath);
                if (prefab == null && useNpcPrefab)
                {
                    Debug.LogWarning($"[FlaxActorSyncSystem] NPC {entityId} 的预制体加载失败（GUID={prefabGuid}），回退占位人形");
                    useNpcPrefab = false;
                    prefabPath = npcFallbackPath;
                    prefabGuid = npcFallbackGuid;
                    prefab = HundunWorldGame.LoadAssetFullyLoaded<Prefab>(prefabGuid, prefabPath);
                }

                if (prefab == null)
                {
                    Debug.LogError($"[FlaxActorSyncSystem] 无法加载远程实体 Prefab (GUID={prefabGuid}, Path={prefabPath})，实体 {entityId} 将不可见");
                    return null;
                }

                // 2) Spawn Prefab 到目标场景（与本地玩家一致）
                // 修复（幽灵角色堆积根因之一）：原实现算出 targetScene 却两个分支走同样的
                // PrefabManager.SpawnPrefab —— 远程角色实际总落在常驻 RootScene（切场景不卸载）。
                // 本系统挂在 RootScene 上，退世界时 OnDestroy/ClearAllActors 不会触发，
                // 落在 RootScene 的旧 Actor 因此跨会话堆积成"离线/不存在角色"的幽灵。
                // 与本地玩家对齐：生成后若不在目标场景则移入 World 场景，随场景卸载自然销毁。
                var targetScene = HundunWorldGame.FindGameWorldScene();
                Actor actor = PrefabManager.SpawnPrefab(prefab, position, Quaternion.Identity);
                if (actor != null && targetScene != null && actor.Scene != targetScene)
                {
                    Level.SpawnActor(actor, targetScene);
                }
                else if (actor != null && targetScene == null)
                {
                    Debug.LogError($"[FlaxActorSyncSystem] 未找到玩法场景，实体 {entityId} 滞留在 RootScene，切场景不会卸载、将堆积成幽灵");
                }

                if (actor == null)
                {
                    Debug.LogError($"[FlaxActorSyncSystem] PrefabManager.SpawnPrefab 返回 null，远程玩家 {entityId} 创建失败");
                    return null;
                }

                actor.Name = kind switch
                {
                    SelectableKind.Monster => $"Monster_{entityId}",
                    SelectableKind.Npc => $"Npc_{entityId}",
                    _ => $"RemotePlayer_{entityId}",
                };
                actor.Position = position;

                // 应用模型缩放：怪物与 NPC 都用服务端/外观目录给的那一只的缩放
                // （精英同模型放大、NPC 体型微调就是这条）；未下发（<=0）时才用全局默认值。
                // 拿 0 当默认会把实体压成一个点。
                var appliedScale = (kind == SelectableKind.Monster || kind == SelectableKind.Npc) && monsterScale > 0f
                    ? monsterScale
                    : DefaultModelScale;
                if (Math.Abs(appliedScale - 1.0f) > 0.0001f)
                {
                    actor.Scale = new Vector3(appliedScale, appliedScale, appliedScale);
                }

                // 3) ★ 关键：禁用 prefab 自带的本机控制/编辑器/demo 脚本。
                // 远程实例不应采集本机输入、不应跑技能状态机/demo 逻辑，否则会干扰本机玩家。
                DisableLocalControlScripts(actor, entityId);

                // 4) 补挂身体模型。CharacterRoot.prefab 目前只是占位胶囊（没有 AnimatedModel），
                // 不补挂远程玩家就是一只看不见的胶囊；走本地玩家同一套兜底。
                // 怪物/NPC 走了专用 Prefab 时自带外观，不能再套人形身体兜底。
                if (!useMonsterPrefab && !useNpcPrefab)
                {
                    // 玩家远端与本地同一套 Ada 承载模型（头部形状要脸骨）；怪物仍走人形占位身体。
                    if (kind == SelectableKind.Player)
                    {
                        HundunWorldGame.EnsureMetaHumanBody(actor);
                        HundunWorldGame.EnsureGaspAnimator(actor, true);
                    }
                    else
                        HundunWorldGame.EnsureBodyModel(actor);
                }

                // 5) ★★★ 关键修复：禁用 AnimatedModel 的 RootMotionTarget。
                // CharacterRoot.prefab 中 AnimatedModel.RootMotionTarget 指向 CharacterRoot 自身，
                // 如果不禁用，动画系统每帧会在 FlaxActorSyncSystem 设置位置之后，
                // 用根运动位移覆盖 Actor.Position，导致远程角色位置由动画根运动驱动而非网络同步驱动，
                // 表现为"看不到彼此移动"或"位置被动画拉回/漂移"。
                // 远程角色位置的唯一事实源是 ECS InterpolatedTransformComponent（由网络快照驱动）。
                DisableRootMotionOnRemoteActor(actor, entityId);

                // 6) 挂载 RemotePlayerActor 脚本（用于 EntityId 标识与动画参数 setter，零冲突）
                // 若 prefab 已自带同名脚本则复用，否则新增。
                var remotePlayerScript = actor.GetScript<RemotePlayerActor>();
                if (remotePlayerScript == null)
                {
                    remotePlayerScript = actor.AddScript<RemotePlayerActor>();
                }
                if (remotePlayerScript != null)
                {
                    remotePlayerScript.EntityId = entityId;
                    // 占位名：服务端未下发名字时头顶名牌会退回到它（属性桥优先用网络 Nickname）。
                    remotePlayerScript.PlayerName = string.IsNullOrEmpty(displayName)
                        ? (kind == SelectableKind.Monster ? $"Monster_{entityId}" : $"Player_{entityId}")
                        : displayName;
                }

                // 6a) 远端外观：玩家实体 Id 就是角色 Id，按它向服务端拉一次已经落库的捏脸 blob。
                // 回包可能比 Actor 先到，所以这里也尝试套一次缓存（两处都是幂等的）。
                if (kind == SelectableKind.Player)
                {
                    var appearance = HundunWorld.Game.Services.MetaHumanAppearanceService.Instance;
                    appearance.TryApplyRemoteLook(entityId);
                    appearance.RequestRemoteAppearance(entityId);
                }

                // 6b) 可选中实体标记：选中框架（射线命中后反查）与头顶信息卡都读这一处。
                var selectTag = actor.GetScript<SelectableEntityTag>();
                if (selectTag == null)
                {
                    selectTag = actor.AddScript<SelectableEntityTag>();
                }
                if (selectTag != null)
                {
                    selectTag.EntityId = entityId;
                    selectTag.Kind = kind;
                    selectTag.DisplayName = displayName ?? string.Empty;
                    // 怪物身份与外观四件套落到标记上：头顶名牌（让分色/骷髅"??"）、
                    // 选中信息卡与图鉴都从这里读，不再各自去猜。
                    selectTag.TemplateId = identity.TemplateId;
                    selectTag.ModelKey = identity.ModelKey ?? string.Empty;
                    selectTag.Rank = identity.Rank;
                    selectTag.StandState = monsterStand;

                    // 晚进场者的那根条子：Spawn 补发时带上"这只怪正在读什么、读到哪了"，
                    // 否则新人看到的是空条，直到它下一次起手才恢复（起手事件只发给当时看得见的人）。
                    if (kind == SelectableKind.Monster && identity.CastTotalSeconds > 0f)
                    {
                        selectTag.BeginCast(identity.CastSkillId, identity.CastSchoolMask,
                            identity.CastInterruptible != 0, identity.CastTotalSeconds, identity.CastElapsedSeconds);
                    }

                    selectTag.Level = level;
                }

                // 6. 挂载头顶名牌组件：每帧把自身头顶投影到屏幕，在共享宿主内绘制血条/名称/等级/称号。
                if (actor.GetScript<HundunWorld.Game.UI.GameMain.NameplateUI>() == null)
                {
                    actor.AddScript<HundunWorld.Game.UI.GameMain.NameplateUI>();
                }

                return actor;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FlaxActorSyncSystem] 创建远程角色 Actor 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 把服务端 Identity.EntityType 映射为选中层的种类。
        /// <para>
        /// 未知/缺失（0）一律按远程玩家处理：这是本系统改型之前的既有表现，旧服务端（不发真类型）
        /// 接上客户端也不会突然"所有人消失"。
        /// </para>
        /// </summary>
        private static SelectableKind KindFromWireType(int wireEntityType)
        {
            var wire = (HundunWorld.Game.ECS.Components.NetworkEntityType)wireEntityType;
            switch (wire)
            {
                case HundunWorld.Game.ECS.Components.NetworkEntityType.Monster: return SelectableKind.Monster;
                case HundunWorld.Game.ECS.Components.NetworkEntityType.Item: return SelectableKind.Item;
                case HundunWorld.Game.ECS.Components.NetworkEntityType.Npc: return SelectableKind.Npc;
                default: return SelectableKind.Player;
            }
        }

        /// <summary>
        /// 禁用 prefab 自带的本机控制/编辑器/demo 脚本，避免远程实例采集本机输入或抢写本地玩家 ECS。
        /// PlayerController 是核心必须禁用项：它 OnUpdate 会读键盘鼠标并 WriteInputToEcs（命中本地玩家实体）。
        /// LocalPlayerActorSyncSystem 必须禁用：它 OnUpdate 会读取本地玩家 PredictedTransformComponent 并设置 Actor.Position，
        /// 如果远程 Actor 上仍启用此脚本，会每帧将远程 Actor 位置覆盖为本地玩家位置，导致远程角色"卡在本地玩家位置不动"。
        /// SkillAnimation* / AppearanceEditor / PreviewController 是 demo/编辑器脚本，禁用更安全。
        /// 保留：AnimationGraphController、MaterialController*（驱动动画与外观，不读输入）。
        /// </summary>
        private void DisableLocalControlScripts(Actor actor, ulong entityId)
        {
            // PlayerController：必须禁用（会采集本机输入）
            var playerController = actor.GetScript<PlayerController>();
            if (playerController != null)
            {
                playerController.Enabled = false;
                Debug.Log($"[FlaxActorSyncSystem] 远程玩家 {entityId} 已禁用 PlayerController");
            }

            // LocalPlayerActorSyncSystem：必须禁用（会覆盖远程 Actor 位置为本地玩家位置）
            // 这是"看不到彼此移动"BUG 的关键根因：如果 CharacterRoot.prefab 包含此脚本，
            // 远程 Actor 每帧都会被设置为本地玩家的预测位置，完全覆盖 FlaxActorSyncSystem 的插值位置。
            DisableScriptIfExists<LocalPlayerActorSyncSystem>(actor, entityId, "LocalPlayerActorSyncSystem");

            // ECSUpdateDriver：必须禁用（会重复驱动 ArchWorldHost.Tick，导致并发异常或状态错乱）
            DisableScriptIfExists<ECSUpdateDriver>(actor, entityId, "ECSUpdateDriver");

            // SkillAnimationDemo：demo 脚本，禁用
            DisableScriptIfExists<SkillAnimationDemo>(actor, entityId, "SkillAnimationDemo");

            // SkillAnimationSystemInitializer：技能系统初始化（本机场景级），禁用
            DisableScriptIfExists<SkillAnimationSystemInitializer>(actor, entityId, "SkillAnimationSystemInitializer");

            // CharacterAppearanceEditor：编辑器/预览脚本，禁用
            // （CharacterAppearancePreviewController 已随旧预览栈删除，不再需要防御性禁用）
            DisableScriptIfExists<Rendering.CharacterAppearanceEditor>(actor, entityId, "CharacterAppearanceEditor");
        }

        /// <summary>若 Actor 上存在指定类型脚本则禁用并记录日志。</summary>
        private void DisableScriptIfExists<T>(Actor actor, ulong entityId, string displayName) where T : Script
        {
            var script = actor.GetScript<T>();
            if (script != null)
            {
                script.Enabled = false;
                Debug.Log($"[FlaxActorSyncSystem] 远程玩家 {entityId} 已禁用 {displayName}");
            }
        }

        /// <summary>
        /// 禁用远程角色 AnimatedModel 的 RootMotionTarget，防止动画根运动覆盖网络同步位置。
        /// CharacterRoot.prefab 的 AnimatedModel 默认设置 RootMotionTarget=CharacterRoot，
        /// 用于本地玩家的动画驱动移动。但远程角色位置由网络快照（InterpolatedTransformComponent）驱动，
        /// 如果不禁用根运动，动画系统会在 FlaxActorSyncSystem 设置位置后用根运动位移覆盖 Actor.Position，
        /// 导致远程角色"看不到移动"或位置漂移。
        /// 递归查找 Actor 层级中所有 AnimatedModel 并禁用 RootMotionTarget（角色可能有多个 AnimatedModel）。
        /// </summary>
        private void DisableRootMotionOnRemoteActor(Actor actor, ulong entityId)
        {
            int disabledCount = 0;
            DisableRootMotionRecursive(actor, entityId, ref disabledCount);
            if (disabledCount > 0)
            {
                Debug.Log($"[FlaxActorSyncSystem] 远程玩家 {entityId} 已禁用 {disabledCount} 个 AnimatedModel 的 RootMotionTarget（防止根运动覆盖网络位置）");
            }
            else
            {
                Debug.LogWarning($"[FlaxActorSyncSystem] 远程玩家 {entityId} 未找到 AnimatedModel，无法禁用 RootMotionTarget。角色可能不可见或位置同步异常。");
            }
        }

        private void DisableRootMotionRecursive(Actor actor, ulong entityId, ref int disabledCount)
        {
            if (actor is AnimatedModel am)
            {
                if (am.RootMotionTarget != null)
                {
                    am.RootMotionTarget = null;
                    disabledCount++;
                }
            }

            for (int i = 0; i < actor.ChildrenCount; i++)
            {
                var child = actor.GetChild(i);
                DisableRootMotionRecursive(child, entityId, ref disabledCount);
            }
        }

        /// <summary>EntityId → 上次同步朝向 Yaw（用于检测是否需要更新）。</summary>
        private readonly Dictionary<ulong, float> _entityIdToLastYaw = new();

        /// <summary>EntityId → 远程角色 AnimatedModel（用于设置动画参数，回退方案）。</summary>
        private readonly Dictionary<ulong, AnimatedModel> _entityIdToAnimatedModel = new();

        /// <summary>EntityId → 远程角色的 IsWalking 动画参数句柄（回退方案）。</summary>
        private readonly Dictionary<ulong, AnimGraphParameter> _entityIdToIsWalkingParam = new();

        /// <summary>
        /// 当前动画图没有 IsWalking 的实体：查一次记下来，不再逐帧调用 GetParameter
        /// （引擎在缺参数时每次调用都会打一条 "missing parameter" 警告；实测一次世界会话刷了 13k 行）。
        /// </summary>
        private readonly HashSet<ulong> _entityIdIsWalkingMissing = new();
        private bool _warnedIsWalkingMissing;

        /// <summary>EntityId → 远程角色的 CharacterAnimationController（优先使用）。</summary>
        private readonly Dictionary<ulong, CharacterAnimationController> _entityIdToAnimationController = new();

        /// <summary>是否已输出首帧同步诊断日志。</summary>
        private bool _firstSyncDiag = true;

        /// <summary>诊断：SyncInterpolatedPositions 帧计数器。</summary>
        private long _syncPosFrameCount;

        // [Phase C1] 已移除 _diagInterpSnapshot/_diagLastWrittenPos/_diagPosOverrideWarnCount 诊断字典，
        // 位置覆盖检测改为通过 ClientSyncMetrics.PositionOverrideCount 计数器记录（Phase C2）。

        /// <summary>
        /// 每帧同步远程实体的插值位置、朝向、动画到 Flax Actor。
        /// </summary>
        private void SyncInterpolatedPositions()
        {
            if (_archWorld == null)
            {
                return;
            }

            // [Phase C5] 断线期间冻结位置更新，避免无新数据时 Actor 位置漂移
            if (IsPaused)
            {
                return;
            }

            // 诊断：每 120 帧（≈2 秒）输出详细同步状态
            var diagFrame = System.Threading.Interlocked.Increment(ref _syncPosFrameCount);
            var isDiagFrame = diagFrame <= 5 || diagFrame % 120 == 1;

            if (_entityIdToActor.Count == 0)
            {
                // 只在 GameWorld 告警：登录/选角/创角场景里远程角色 Actor 恒为 0，
                // 这条"Actor 未创建、位置无法同步"是假故障，每 2 秒刷一次会淹没真信号。
                if (isDiagFrame && HundunWorld.Game.UI.GameSceneManager.IsInGameWorld)
                {
                    // 关键诊断：有 InterpolatedTransformComponent 实体但没有 Actor
                    int interpCount = 0;
                    var countQuery = new QueryDescription().WithAll<InterpolatedTransformComponent>();
                    _archWorld.Query(in countQuery, (Entity e, ref InterpolatedTransformComponent _) => interpCount++);
                    Debug.LogWarning($"[FlaxActorSyncSystem] SyncInterpolatedPositions#{diagFrame}: ActorCount=0 但 InterpEntityCount={interpCount}！远程角色 Actor 未创建，位置无法同步。EventsSubscribed={_eventsSubscribed}");
                }
                return;
            }

            // 收集需要清理的已销毁 Actor
            var destroyedEntityIds = new List<ulong>();

            // 诊断：本帧位置变更统计
            int diagTotalEntities = 0;
            int diagActorsFound = 0;
            int diagPositionsChanged = 0;

            // [Phase C7] 分级更新调度：按距离分级 + 数量降档计算每实体的帧预算。
            // 替代原 [Phase C6] 单一 LOD 奇偶帧逻辑（LodEntityThreshold=50 过高且无距离分级）。
            var lodFrame = System.Threading.Interlocked.Increment(ref _lodFrameCount);
            var localPos = GetLocalPlayerPosition();

            // 数量降档：远程角色数超过性能阈值时整体降档（Near→Mid、Mid→Far、Far→隔2帧）
            var degraded = _entityIdToActor.Count > PerformanceDegradeEntityCount;
            if (degraded && _lastDegradeDiagCount != _entityIdToActor.Count)
            {
                _lastDegradeDiagCount = _entityIdToActor.Count;
                Diagnostics?.OnMultiEntityDegraded(_entityIdToActor.Count, "PerformanceDegrade");
                ClientSyncMetrics.RecordDegradeEvent();
            }

            // 数量硬上限：超过上限时按距离对最远角色暂停插值推进（保持最后合法位置，不创建新 Actor）
            var maxCapReached = _entityIdToActor.Count >= MaxRemoteEntityCount;
            if (maxCapReached && _lastMaxCapDiagCount != _entityIdToActor.Count)
            {
                _lastMaxCapDiagCount = _entityIdToActor.Count;
                Diagnostics?.OnMultiEntityDegraded(_entityIdToActor.Count, "MaxEntityCap");
                ClientSyncMetrics.RecordMaxEntityCapReached();
            }

            // [超大规模] 规模档位编排：向 SyncScaleController 汇报同屏远程实体数并应用最远优先降级。
            // 降级实体暂停插值推进但不得从订阅集无声丢失（spec 5.5.1.3）；档位回落时恢复全帧平滑呈现。
            if (ScaleController != null)
            {
                ScaleController.OnRemoteEntityCountChanged(_entityIdToActor.Count);
                if (_entityIdToActor.Count > ScaleController.TierThresholds[ScaleController.TierThresholds.Length - 1])
                {
                    // 计算各远程实体距本地玩家的距离，最远优先降级。
                    var farEntities = new List<(ulong EntityId, float Distance)>(_entityIdToActor.Count);
                    foreach (var kv in _entityIdToActor)
                    {
                        var dist = EstimateDistanceToLocal(kv.Key, localPos);
                        farEntities.Add((kv.Key, dist));
                    }
                    ScaleController.ApplyDegradeTo(farEntities);
                }
                else if (ScaleController.DegradedEntityIds.Count > 0)
                {
                    ScaleController.ClearDegraded();
                }
            }

            // 查询所有带 InterpolatedTransformComponent + NetworkIdentityComponent + AuthTransformComponent 的实体
            var query = new QueryDescription()
                .WithAll<InterpolatedTransformComponent, NetworkIdentityComponent, AuthTransformComponent>();

            _archWorld.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp, ref NetworkIdentityComponent netId, ref AuthTransformComponent auth) =>
            {
                diagTotalEntities++;

                // [Phase C7] 分级计算：按与本地玩家距离分 Near/Mid/Far 档，缓存到 _entityIdToUpdatePriority
                var entityId = netId.EntityId;
                var priority = ComputeUpdatePriority(entityId, interp, localPos, degraded);
                _entityIdToUpdatePriority[entityId] = priority;

                // [Phase C7] 数量硬上限：超限时按距离对最远角色暂停插值推进（不创建/不更新渲染位置）
                if (maxCapReached)
                {
                    if (priority == RemoteEntityUpdatePriority.PositionOnlyEveryOtherFrame
                        && IsFarFromLocal(interp, localPos))
                    {
                        return; // 最远角色本帧暂停推进，保持最后合法渲染位置
                    }
                }

                // [Phase C7] 帧预算执行：按优先级决定本帧是否跳过位置/朝向/动画更新
                var skipPosition = priority == RemoteEntityUpdatePriority.PositionOnlyEveryOtherFrame
                    && (lodFrame & 1) != 0;
                if (skipPosition)
                {
                    return; // 本帧跳过此实体（Far 档隔帧），下帧更新
                }

                // [Phase C7] 整体降档时动画停止（Mid/Far 均不更新动画，Far 位置也隔帧）
                var skipAnimation = degraded
                    || priority != RemoteEntityUpdatePriority.EveryFrame
                    && (lodFrame & 1) != 0;

                if (_entityIdToActor.TryGetValue(entityId, out var actor))
                {
                    if (actor == null)
                    {
                        destroyedEntityIds.Add(entityId);
                        return;
                    }

                    diagActorsFound++;

                    // 1) 同步插值位置
                    var newPos = new Vector3(interp.X, interp.Y, interp.Z);
                    bool positionUpdated = false;
                    if (_entityIdToLastPosition.TryGetValue(entityId, out var lastPos))
                    {
                        float distSquared = (newPos - lastPos).LengthSquared;
                        // 修复（远程角色移动不明显 — 位置更新阈值过严）：
                        // 原值 0.0001（1cm²）在 Lerp 接近目标时每帧移动 < 1cm，Actor 不更新，
                        // 视觉上角色"卡住不动"。降到 0.000001（0.01cm² = 0.1mm²），
                        // 让微小追赶也能反映到 Actor 位置。
                        if (distSquared > 0.000001f)
                        {
                            // [Phase C7] 渲染端数值兜底（三道防线第三道）：
                            // 任一轴非有限值则保持上一帧渲染位置并跳过写入，不 throw、不影响其他角色。
                            if (float.IsFinite(newPos.X) && float.IsFinite(newPos.Y) && float.IsFinite(newPos.Z))
                            {
                                actor.Position = newPos;
                                _entityIdToLastPosition[entityId] = newPos;
                                positionUpdated = true;
                                diagPositionsChanged++;
                            }
                        }
                    }
                    else
                    {
                        if (float.IsFinite(newPos.X) && float.IsFinite(newPos.Y) && float.IsFinite(newPos.Z))
                        {
                            actor.Position = newPos;
                            _entityIdToLastPosition[entityId] = newPos;
                            positionUpdated = true;
                            diagPositionsChanged++;
                        }
                    }

#if DEBUG
                    // 诊断：每 120 帧输出每个远程实体的详细位置信息（仅 Debug 构建）
                    if (isDiagFrame)
                    {
                        var actorPos = actor.Position;
                        Debug.Log($"[FlaxActorSyncSystem] SyncPos#{diagFrame} EntityId={entityId}: InterpPos=({interp.X:F2},{interp.Y:F2},{interp.Z:F2}), ActorPos=({actorPos.X:F2},{actorPos.Y:F2},{actorPos.Z:F2}), PosUpdated={positionUpdated}, Alpha={interp.Alpha:F2}, Target=({interp.TargetX:F2},{interp.TargetY:F2},{interp.TargetZ:F2}), AuthYaw={auth.Yaw:F2}rad");
                    }
#endif

                    // 2) 同步朝向（从 InterpolatedTransformComponent.Yaw，已经由 InterpolationSystem 平滑插值）
                    // 修复（远程角色闪移）：原实现从 auth.Yaw 读取（每帧瞬移到服务端值），
                    // 导致远程角色朝向突变，视觉上表现为“闪移”。
                    // InterpolationSystem 已对 Yaw 做最短路径插值（处理±180°环绕），
                    // 直接读取 interp.Yaw 即可获得平滑朝向。
                    float yawDeg = interp.Yaw * (180.0f / (float)Math.PI);
                    if (_entityIdToLastYaw.TryGetValue(entityId, out var lastYaw))
                    {
                        if (Math.Abs(yawDeg - lastYaw) > 0.1f)
                        {
                            actor.Orientation = Quaternion.Euler(0, yawDeg, 0);
                            _entityIdToLastYaw[entityId] = yawDeg;
                        }
                    }
                    else
                    {
                        actor.Orientation = Quaternion.Euler(0, yawDeg, 0);
                        _entityIdToLastYaw[entityId] = yawDeg;
                    }

                    // 2b) 同步昵称/称号 + 血量/等级到 Actor，供头顶名牌（NameplateUI）读取真实值：
                    //   - 昵称/称号：随 Identity 于 Spawn 下发，SnapshotApplySystem 已写入 NetworkIdentityComponent，此处直读。
                    //   - 血量/等级：EntityStateAuthComponent 由 SnapshotApplySystem 从网络写入 ECS（1Hz 心跳 + 变化触发）。
                    var attrs = actor.GetScript<CharacterAttributesComponent>();
                    if (attrs == null)
                    {
                        attrs = actor.AddScript<CharacterAttributesComponent>();
                    }
                    if (attrs != null)
                    {
                        // 服务端未下发昵称时（旧服务端/档案查询失败）才退回 Actor 侧占位名。
                        if (!string.IsNullOrEmpty(netId.Nickname))
                        {
                            if (attrs.Nickname != netId.Nickname) attrs.Nickname = netId.Nickname;
                        }
                        else
                        {
                            var rpa = actor.GetScript<RemotePlayerActor>();
                            if (rpa != null && !string.IsNullOrEmpty(rpa.PlayerName) && attrs.Nickname != rpa.PlayerName)
                                attrs.Nickname = rpa.PlayerName;
                        }

                        if (attrs.Title != netId.Title) attrs.Title = netId.Title ?? "";

                        if (_archWorld.Has<EntityStateAuthComponent>(entity))
                        {
                            ref var stateAuth = ref _archWorld.Get<EntityStateAuthComponent>(entity);
                            if (stateAuth.MaxHealth > 0)
                                attrs.MaxHealth = stateAuth.MaxHealth;
                            attrs.CurrentHealth = stateAuth.Health;
                            if (stateAuth.Level > 0)
                                attrs.Level = stateAuth.Level;
                            if (stateAuth.Exp > 0)
                                attrs.Exp = stateAuth.Exp;

                            // 服务端宣告"这只怪死了"：尸体要留在屏幕上（服务端 corpse_seconds 窗口内
                            // 它还会继续出现在快照里），但不能再被选中，头顶也不该挂着活人的名牌。
                            // 判据用状态位而不是"血量<=0"：血量归零是数值，死亡是状态，
                            // 用数值推状态在任何一个"0 血但没死"的下发场景里就会认错尸体。
                            //
                            // "正在回位、此刻打不到"（服务端状态位，判据主人只有一处：
                            // MonsterAiGate.BlocksIncomingDamage，与拒伤同一条式子，所以客户端的说法
                            // 不可能和服务端的判定各说各话）与死亡是<b>两件事</b>：尸体不能选，
                            // 而回位中的怪看得见、选得中、就是打不动——所以另开一个字段，不拿 Selectable 兼。
                            //
                            // 两个旗标合成一次取标记组件：这条通路每个同步实体每帧都走，而 GetScript
                            // 是遍历该 Actor 的脚本列表，没变化时不该付这次遍历。
                            bool deadChanged = attrs.IsDead != stateAuth.IsDead;
                            bool evadeChanged = attrs.Unattackable != stateAuth.IsEvading;

                            // 身份增量（姿态/服务位）：作息换档时姿态会变，服务位是配置改动的结果。
                            // 只有真的变了才去取标记组件并写——这段每实体每帧都走，
                            // GetScript 要遍历脚本列表，不能无条件付这笔开销。
                            var stateTag = (HundunWorld.Game.Selection.SelectableEntityTag)null;
                            if (stateAuth.NpcStandState >= 0 || stateAuth.NpcServiceFlags >= 0)
                                stateTag = actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
                            bool standChanged = stateTag != null && stateAuth.NpcStandState >= 0
                                                && stateTag.StandState != stateAuth.NpcStandState;
                            bool serviceChanged = stateTag != null && stateAuth.NpcServiceFlags >= 0
                                                  && stateTag.NpcServiceFlags != stateAuth.NpcServiceFlags;

                            if (deadChanged || evadeChanged)
                            {
                                attrs.IsDead = stateAuth.IsDead;
                                attrs.Unattackable = stateAuth.IsEvading;
                                stateTag ??= actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
                                if (stateTag != null)
                                {
                                    if (deadChanged)
                                    {
                                        // Selectable 的既有语义就是"死亡/被采集后由持有系统置 false"，
                                        // 选中框架的每条拾取通路都已经认它——这里只写，不新加判据。
                                        stateTag.Selectable = !stateAuth.IsDead;
                                        if (stateAuth.IsDead) stateTag.EndCast(); // 尸体不会还在读条
                                    }
                                    if (evadeChanged) stateTag.Unattackable = stateAuth.IsEvading;
                                }
                            }

                            if (stateTag != null)
                            {
                                if (standChanged) stateTag.StandState = stateAuth.NpcStandState;
                                if (serviceChanged) stateTag.NpcServiceFlags = stateAuth.NpcServiceFlags;
                                stateTag.CorpseInteractable = stateAuth.IsCorpseInteractable;   // CORPSE-LOOT-PATCH

                                // 头顶 !/? 要求"每只任务 NPC 都有一份清单"：服务位带 QuestGiver 的
                                // 第一时间各拉一份（每个实体只问一次；Despawn 时清掉记录，
                                // 免得实体 id 被另一只 NPC 复用时永远问不到）。
                                // 只在 RequestList 真发出去之后才记账：还没连上时它返回 false，
                                // 先记上就再也不会重试了。
                                if ((stateTag.NpcServiceFlags & (int)HundunWorld.Game.Selection.NpcStanceCatalog.ServiceQuestGiver) != 0
                                    && !_questOffersRequested.Contains(entityId)
                                    && HundunWorld.Game.Network.QuestClientService.Instance.RequestList(entityId))
                                {
                                    _questOffersRequested.Add(entityId);
                                }
                            }
                        }
                    }

                    // 3) 同步动画：根据 MovementStateAuthComponent 控制动画状态
                    // 优先查找 CharacterAnimationController
                    if (!_entityIdToAnimationController.TryGetValue(entityId, out var animationController) || animationController == null)
                    {
                        animationController = actor.GetScript<CharacterAnimationController>();
                        if (animationController != null)
                        {
                            _entityIdToAnimationController[entityId] = animationController;
                        }
                    }

                    // 回退：查找 AnimatedModel
                    if (!_entityIdToAnimatedModel.TryGetValue(entityId, out var animatedModel) || animatedModel == null)
                    {
                        animatedModel = FindAnimatedModel(actor);
                        if (animatedModel != null)
                        {
                            _entityIdToAnimatedModel[entityId] = animatedModel;
                        }
                    }

                    // 首帧同步诊断
                    if (_firstSyncDiag)
                    {
                        _firstSyncDiag = false;
                        Debug.Log($"[FlaxActorSyncSystem] 首帧同步诊断: EntityId={entityId}, AuthYaw={auth.Yaw}rad({yawDeg}deg), AnimatedModel={animatedModel != null}, AnimationController={animationController != null}, ActorName={actor.Name}");
                    }

                    // [Phase C7] 动画更新纳入分级调度：Near 档每帧、Mid 档隔帧、Far 档与整体降档不执行，
                    // 降低多角色时动画刷新的 CPU 开销（design.md 1.1.2 差异说明）。
                    //
                    // 尸体先短路：服务端在 corpse_seconds 窗口内还会继续下发这只怪（速度 0、MovementMode=Walk），
                    // 照下面的分支走就会给一具尸体播 Idle——那是"站着的死怪"，比直接消失更刺眼。
                    // IsDead 参数由动画图自己决定怎么躺；没有这个参数的图（当前多数怪物图）等于没动作，
                    // 但绝不会再被写回 Idle/Walk。
                    if (attrs != null && attrs.IsDead)
                    {
                        if (animationController != null) animationController.SetIsDead(true);
                    }
                    else if (!skipAnimation && _archWorld.Has<MovementStateAuthComponent>(entity))
                    {
                        ref var movement = ref _archWorld.Get<MovementStateAuthComponent>(entity);

                        // 使用 CharacterAnimationController 设置精细动画状态
                        if (animationController != null)
                        {
                            // 设置移动速度参数（用于动画混合）- 每帧更新，不受变化检测限制
                            if (_archWorld.Has<PlayerInputComponent>(entity))
                            {
                                ref var input = ref _archWorld.Get<PlayerInputComponent>(entity);
                                animationController.SetMoveSpeed(input.MaxSpeed);
                            }

                            // 设置动画状态
                            var animState = movement.MovementMode switch
                            {
                                MovementMode.Walk => CharacterAnimationState.Walk,
                                MovementMode.Run => CharacterAnimationState.Run,
                                MovementMode.Crouch => CharacterAnimationState.Crouch,
                                MovementMode.Jump => CharacterAnimationState.Jump,
                                MovementMode.Fall => CharacterAnimationState.Fall,
                                _ => CharacterAnimationState.Idle
                            };

                            animationController.SetAnimationState(animState);
                        }
                        else if (animatedModel != null)
                        {
                            // 回退：使用 IsWalking 参数；缺参数只查一次，别每帧刷引擎警告
                            AnimGraphParameter isWalkingParam = null;
                            if (!_entityIdIsWalkingMissing.Contains(entityId)
                                && (!_entityIdToIsWalkingParam.TryGetValue(entityId, out isWalkingParam) || isWalkingParam == null))
                            {
                                isWalkingParam = animatedModel.GetParameter("IsWalking");
                                if (isWalkingParam != null)
                                {
                                    _entityIdToIsWalkingParam[entityId] = isWalkingParam;
                                }
                                else
                                {
                                    _entityIdIsWalkingMissing.Add(entityId);
                                    if (!_warnedIsWalkingMissing)
                                    {
                                        _warnedIsWalkingMissing = true;
                                        Debug.LogWarning("[FlaxActorSyncSystem] 当前动画图没有 IsWalking 参数（速度混合由 LocomotionGraphDriver 负责），已停止逐帧查询");
                                    }
                                }
                            }

                            if (isWalkingParam != null)
                            {
                                bool isMoving = movement.MovementMode == MovementMode.Walk
                                            || movement.MovementMode == MovementMode.Run
                                            || movement.MovementMode == MovementMode.Crouch;
                                isWalkingParam.Value = isMoving;
                            }
                        }
                    }
                    else if (!skipAnimation && animatedModel != null)
                    {
                        // 回退：用 Target/Start 差值判断移动意图；缺参数只查一次
                        AnimGraphParameter isWalkingParam = null;
                        if (!_entityIdIsWalkingMissing.Contains(entityId)
                            && (!_entityIdToIsWalkingParam.TryGetValue(entityId, out isWalkingParam) || isWalkingParam == null))
                        {
                            isWalkingParam = animatedModel.GetParameter("IsWalking");
                            if (isWalkingParam != null)
                            {
                                _entityIdToIsWalkingParam[entityId] = isWalkingParam;
                            }
                            else
                            {
                                _entityIdIsWalkingMissing.Add(entityId);
                                if (!_warnedIsWalkingMissing)
                                {
                                    _warnedIsWalkingMissing = true;
                                    Debug.LogWarning("[FlaxActorSyncSystem] 当前动画图没有 IsWalking 参数（速度混合由 LocomotionGraphDriver 负责），已停止逐帧查询");
                                }
                            }
                        }

                        if (isWalkingParam != null)
                        {
                            float moveDelta = (new Vector3(interp.TargetX, interp.TargetY, interp.TargetZ)
                                              - new Vector3(interp.StartX, interp.StartY, interp.StartZ)).LengthSquared;
                            isWalkingParam.Value = moveDelta > 0.0001f;
                        }
                    }
                }
            });

            // 清理已销毁的 Actor 映射
            foreach (var entityId in destroyedEntityIds)
            {
                _entityIdToActor.Remove(entityId);
                _entityIdToLastPosition.Remove(entityId);
                _entityIdToLastYaw.Remove(entityId);
                _entityIdToAnimatedModel.Remove(entityId);
                _entityIdToIsWalkingParam.Remove(entityId);
                _entityIdToAnimationController.Remove(entityId);
                _entityIdToUpdatePriority.Remove(entityId);
            }

            // [超大规模] 将规模档位降级集合同步到 InterpolationSystem（暂停插值推进但保留 Target，恢复无闪跳）。
            if (ScaleController != null && HundunWorldGame.Instance?.ArchHost != null)
            {
                var interpSystem = GetInterpolationSystem(HundunWorldGame.Instance.ArchHost);
                if (interpSystem != null)
                {
                    interpSystem.SetDegradedEntities(ScaleController.DegradedEntityIds);
                }
            }

            // [Phase C7] 多角色指标：当前远程角色数量 + 本帧实际同步实体数
            ClientSyncMetrics.RecordRemoteEntityCount(_entityIdToActor.Count);
            ClientSyncMetrics.RecordPerFrameSynced(diagPositionsChanged);

#if DEBUG
            // 诊断：每 120 帧输出汇总统计（仅 Debug 构建）
            if (isDiagFrame)
            {
                Debug.Log($"[FlaxActorSyncSystem] SyncPos#{diagFrame} 汇总: TotalEntities={diagTotalEntities}, ActorsFound={diagActorsFound}, PositionsChanged={diagPositionsChanged}, ActorCount={_entityIdToActor.Count}");
            }
#endif
        }

        /// <summary>
        /// 获取指定实体 ID 对应的 Flax Actor（调试用）。
        /// </summary>
        public Actor GetActorForEntity(ulong entityId)
        {
            _entityIdToActor.TryGetValue(entityId, out var actor);
            return actor;
        }

        /// <summary>
        /// 获取当前远程角色 Actor 数量（调试用）。
        /// </summary>
        public int GetRemoteActorCount() => _entityIdToActor.Count;

        /// <summary>
        /// [Phase C7] 获取本地玩家 Flax Actor 的位置（分级计算的距离基准）。
        /// 本地玩家 Actor 缺失时回退为原点，此时所有远程角色均按远档处理（保守降频）。
        /// </summary>
        private static Vector3 GetLocalPlayerPosition()
        {
            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            return local != null ? local.Position : Vector3.Zero;
        }

        /// <summary>
        /// [Phase C7] 按距离分级 + 数量降档计算实体本帧的更新优先级。
        /// </summary>
        /// <param name="entityId">远程实体 ID。</param>
        /// <param name="interp">插值组件（读取当前插值位置）。</param>
        /// <param name="localPos">本地玩家位置。</param>
        /// <param name="degraded">是否整体降档（远程角色数超性能阈值）。</param>
        /// <returns>该实体本帧的更新优先级。</returns>
        private RemoteEntityUpdatePriority ComputeUpdatePriority(
            ulong entityId, in InterpolatedTransformComponent interp, Vector3 localPos, bool degraded)
        {
            // 距离分级（与本地玩家的平面距离）
            var dx = interp.X - localPos.X;
            var dz = interp.Z - localPos.Z;
            var distSq = dx * dx + dz * dz;

            var nearSq = NearDistanceMeters * NearDistanceMeters;
            var midSq = MidDistanceMeters * MidDistanceMeters;

            RemoteEntityUpdatePriority priority;
            if (distSq <= nearSq)
                priority = RemoteEntityUpdatePriority.EveryFrame;
            else if (distSq <= midSq)
                priority = RemoteEntityUpdatePriority.AnimationEveryOtherFrame;
            else
                priority = RemoteEntityUpdatePriority.PositionOnlyEveryOtherFrame;

            // 数量降档：Near→Mid 预算、Mid→Far 预算、Far→隔 2 帧（仍为 PositionOnlyEveryOtherFrame，
            // 由 skipPosition 的奇偶逻辑天然实现隔帧；Far 已在隔帧基础上不再额外处理）
            if (degraded && priority == RemoteEntityUpdatePriority.EveryFrame)
                priority = RemoteEntityUpdatePriority.AnimationEveryOtherFrame;
            else if (degraded && priority == RemoteEntityUpdatePriority.AnimationEveryOtherFrame)
                priority = RemoteEntityUpdatePriority.PositionOnlyEveryOtherFrame;

            return priority;
        }

        /// <summary>
        /// [Phase C7] 判断远程实体是否处于"远档"（超过 Mid 距离），用于数量硬上限时选择最远角色暂停推进。
        /// </summary>
        private bool IsFarFromLocal(in InterpolatedTransformComponent interp, Vector3 localPos)
        {
            var dx = interp.X - localPos.X;
            var dz = interp.Z - localPos.Z;
            var distSq = dx * dx + dz * dz;
            var midSq = MidDistanceMeters * MidDistanceMeters;
            return distSq > midSq;
        }

        /// <summary>
        /// [超大规模] 估算远程实体距本地玩家的距离（供最远优先降级排序）。
        /// 实体位置从 Arch World 的 InterpolatedTransformComponent 读取；实体缺失或未就绪时按 0 处理（不参与降级候选排序靠前）。
        /// </summary>
        private float EstimateDistanceToLocal(ulong entityId, Vector3 localPos)
        {
            if (_archWorld == null) return 0f;
            // 通过查询定位实体较繁琐，直接使用上次同步位置缓存（含插值位置）估算。
            if (_entityIdToLastPosition.TryGetValue(entityId, out var pos))
            {
                var dx = pos.X - localPos.X;
                var dz = pos.Z - localPos.Z;
                return MathF.Sqrt(dx * dx + dz * dz);
            }
            return 0f;
        }

        /// <summary>
        /// 递归查找 Actor 层级中的 AnimatedModel。
        /// </summary>
        private static AnimatedModel FindAnimatedModel(Actor actor)
        {
            if (actor is AnimatedModel am) return am;
            for (int i = 0; i < actor.ChildrenCount; i++)
            {
                var child = actor.GetChild(i);
                var result = FindAnimatedModel(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}

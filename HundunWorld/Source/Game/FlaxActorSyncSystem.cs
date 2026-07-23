using System;
using System.Collections.Generic;
using Arch.Core;
using FlaxEngine;
using Game.Combat;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Sync.Components;
using HundunWorld.Game.Character;

namespace HundunWorld.Game
{
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

            // 限频诊断：每 120 帧（≈2 秒）输出系统运行状态 + Actor/实体计数，
            // 确认 FlaxActorSyncSystem 是否在运行、是否有远程实体需要同步。
            var frameCount = System.Threading.Interlocked.Increment(ref _onUpdateFrameCount);
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

            // 创建远程角色 Actor
            Actor remoteActor = CreateRemotePlayerActor(args.EntityId, args.X, args.Y, args.Z);
            if (remoteActor != null)
            {
                _entityIdToActor[args.EntityId] = remoteActor;
                _entityIdToLastPosition[args.EntityId] = new Vector3(args.X, args.Y, args.Z);
                Debug.Log($"[FlaxActorSyncSystem] 远程角色 Actor 已创建: EntityId={args.EntityId}, Pos=({args.X:F2}, {args.Y:F2}, {args.Z:F2})");
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
                _entityIdToAnimationController.Remove(args.EntityId);
                _diagLastWrittenPos.Remove(args.EntityId);
                _diagPosOverrideWarnCount.Remove(args.EntityId);
                _diagInterpSnapshot.Remove(args.EntityId);
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
            _entityIdToAnimationController.Clear();
            _diagLastWrittenPos.Clear();
            _diagPosOverrideWarnCount.Clear();
            _diagInterpSnapshot.Clear();

            if (count > 0)
            {
                Debug.Log($"[FlaxActorSyncSystem] 已清理所有远程角色 Actor: {count} 个");
            }
        }

        /// <summary>
        /// 创建远程角色 Flax Actor。
        /// 失效点 #3 修复：改用 PrefabManager.SpawnPrefab 复用 CharacterRoot Prefab，
        /// 与本地玩家共享完整外观（SkinnedModel + AnimationGraph + MaterialController*）。
        /// Spawn 后必须禁用 prefab 自带的本机控制脚本（PlayerController 等），
        /// 否则远程实例会读取本机输入并抢写本地玩家 ECS 实体的 PlayerInputComponent。
        /// </summary>
        private Actor CreateRemotePlayerActor(ulong entityId, float x, float y, float z)
        {
            try
            {
                var position = new Vector3(x, y, z);

                // 1) 加载 CharacterRoot Prefab
                // 修复：优先从路径动态获取真实 GUID，绕过 .NET Guid 字节序与 Flax C++ GUID 不一致问题
                // 使用 LoadAssetFullyLoaded 统一处理 GUID + 路径 fallback + 类型兜底
                if (RemotePlayerPrefabGuid == Guid.Empty && !string.IsNullOrEmpty(RemotePlayerPrefabPath))
                {
                    if (HundunWorldGame.TryGetAssetGuidFromPath(RemotePlayerPrefabPath, out var realGuid))
                    {
                        RemotePlayerPrefabGuid = realGuid;
                        Debug.Log($"[FlaxActorSyncSystem] 从路径动态获取到 Prefab 真实 GUID: {realGuid}");
                    }
                    else
                    {
                        Debug.LogWarning($"[FlaxActorSyncSystem] 无法从路径获取 Prefab GUID，将使用路径加载: {RemotePlayerPrefabPath}");
                    }
                }

                var prefab = HundunWorldGame.LoadAssetFullyLoaded<Prefab>(RemotePlayerPrefabGuid, RemotePlayerPrefabPath);

                if (prefab == null)
                {
                    Debug.LogError($"[FlaxActorSyncSystem] 无法加载远程角色 Prefab (GUID={RemotePlayerPrefabGuid}, Path={RemotePlayerPrefabPath})，远程玩家 {entityId} 将不可见");
                    return null;
                }

                // 2) Spawn Prefab 到目标场景（与本地玩家一致）
                var targetScene = FindGameWorldScene();
                Actor actor;
                if (targetScene != null)
                {
                    // SpawnPrefab 本身会注册到主场景；这里仅用于日志校验
                    actor = PrefabManager.SpawnPrefab(prefab, position, Quaternion.Identity);
                }
                else
                {
                    actor = PrefabManager.SpawnPrefab(prefab, position, Quaternion.Identity);
                }

                if (actor == null)
                {
                    Debug.LogError($"[FlaxActorSyncSystem] PrefabManager.SpawnPrefab 返回 null，远程玩家 {entityId} 创建失败");
                    return null;
                }

                actor.Name = $"RemotePlayer_{entityId}";
                actor.Position = position;

                // 应用模型缩放
                if (DefaultModelScale != 1.0f)
                {
                    actor.Scale = new Vector3(DefaultModelScale, DefaultModelScale, DefaultModelScale);
                }

                // 3) ★ 关键：禁用 prefab 自带的本机控制/编辑器/demo 脚本。
                // 远程实例不应采集本机输入、不应跑技能状态机/demo 逻辑，否则会干扰本机玩家。
                DisableLocalControlScripts(actor, entityId);

                // 4) ★★★ 关键修复：禁用 AnimatedModel 的 RootMotionTarget。
                // CharacterRoot.prefab 中 AnimatedModel.RootMotionTarget 指向 CharacterRoot 自身，
                // 如果不禁用，动画系统每帧会在 FlaxActorSyncSystem 设置位置之后，
                // 用根运动位移覆盖 Actor.Position，导致远程角色位置由动画根运动驱动而非网络同步驱动，
                // 表现为"看不到彼此移动"或"位置被动画拉回/漂移"。
                // 远程角色位置的唯一事实源是 ECS InterpolatedTransformComponent（由网络快照驱动）。
                DisableRootMotionOnRemoteActor(actor, entityId);

                // 5) 挂载 RemotePlayerActor 脚本（用于 EntityId 标识与动画参数 setter，零冲突）
                // 若 prefab 已自带同名脚本则复用，否则新增。
                var remotePlayerScript = actor.GetScript<RemotePlayerActor>();
                if (remotePlayerScript == null)
                {
                    remotePlayerScript = actor.AddScript<RemotePlayerActor>();
                }
                if (remotePlayerScript != null)
                {
                    remotePlayerScript.EntityId = entityId;
                    remotePlayerScript.PlayerName = $"Player_{entityId}";
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

            // CharacterAppearanceEditor / CharacterAppearancePreviewController：编辑器/预览脚本，禁用
            DisableScriptIfExists<Rendering.CharacterAppearanceEditor>(actor, entityId, "CharacterAppearanceEditor");
            DisableScriptIfExists<Rendering.CharacterAppearancePreviewController>(actor, entityId, "CharacterAppearancePreviewController");
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

        /// <summary>EntityId → 远程角色的 CharacterAnimationController（优先使用）。</summary>
        private readonly Dictionary<ulong, CharacterAnimationController> _entityIdToAnimationController = new();

        /// <summary>是否已输出首帧同步诊断日志。</summary>
        private bool _firstSyncDiag = true;

        /// <summary>诊断：SyncInterpolatedPositions 帧计数器。</summary>
        private long _syncPosFrameCount;

        /// <summary>诊断：每帧记录远程实体的插值位置快照（用于检测位置是否在变化）。</summary>
        private readonly Dictionary<ulong, Vector3> _diagInterpSnapshot = new();

        /// <summary>
        /// 诊断：上一帧 FlaxActorSyncSystem 实际写入 Actor 的位置（与下一帧读取的 Actor.Position 对比，
        /// 检测是否有其他系统在覆盖位置，如 Root Motion / RigidBody / 其他 Script）。
        /// key: entityId, value: 上一帧通过 actor.Position = 写入的值。
        /// </summary>
        private readonly Dictionary<ulong, Vector3> _diagLastWrittenPos = new();

        /// <summary>诊断：位置被外部覆盖的累计告警次数（限频用）。</summary>
        private readonly Dictionary<ulong, long> _diagPosOverrideWarnCount = new();

        /// <summary>
        /// 每帧同步远程实体的插值位置、朝向、动画到 Flax Actor。
        /// </summary>
        private void SyncInterpolatedPositions()
        {
            if (_archWorld == null)
            {
                return;
            }

            // 诊断：每 120 帧（≈2 秒）输出详细同步状态
            var diagFrame = System.Threading.Interlocked.Increment(ref _syncPosFrameCount);
            var isDiagFrame = diagFrame <= 5 || diagFrame % 120 == 1;

            if (_entityIdToActor.Count == 0)
            {
                if (isDiagFrame)
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

            // 查询所有带 InterpolatedTransformComponent + NetworkIdentityComponent + AuthTransformComponent 的实体
            var query = new QueryDescription()
                .WithAll<InterpolatedTransformComponent, NetworkIdentityComponent, AuthTransformComponent>();

            _archWorld.Query(in query, (Entity entity, ref InterpolatedTransformComponent interp, ref NetworkIdentityComponent netId, ref AuthTransformComponent auth) =>
            {
                diagTotalEntities++;

                if (_entityIdToActor.TryGetValue(netId.EntityId, out var actor))
                {
                    if (actor == null)
                    {
                        destroyedEntityIds.Add(netId.EntityId);
                        return;
                    }

                    diagActorsFound++;
                    var entityId = netId.EntityId;

                    // ★★★ 位置覆盖检测：对比上一帧我们写入的位置与当前 Actor 实际位置。
                    // 如果两者差异显著，说明有其他系统（Root Motion / RigidBody / 其他 Script）
                    // 在 FlaxActorSyncSystem 写入后覆盖了 Actor.Position，这是"看不到移动"的关键线索。
                    if (_diagLastWrittenPos.TryGetValue(entityId, out var lastWrittenPos))
                    {
                        var currentActorPos = actor.Position;
                        float overrideDistSq = (currentActorPos - lastWrittenPos).LengthSquared;
                        // 阈值 0.01 平方米（10cm 位移）— 排除浮点抖动
                        if (overrideDistSq > 0.01f)
                        {
                            _diagPosOverrideWarnCount.TryGetValue(entityId, out var warnCount);
                            warnCount++;
                            _diagPosOverrideWarnCount[entityId] = warnCount;
                            // 限频：每个实体前 3 次无条件输出，后续每 300 次（≈5 秒）输出一次
                            if (warnCount <= 3 || warnCount % 300 == 1)
                            {
                                Debug.LogWarning($"[FlaxActorSyncSystem] ⚠️ 位置被外部覆盖！EntityId={entityId}, 上帧写入=({lastWrittenPos.X:F2},{lastWrittenPos.Y:F2},{lastWrittenPos.Z:F2}), 当前读取=({currentActorPos.X:F2},{currentActorPos.Y:F2},{currentActorPos.Z:F2}), 偏移={Mathf.Sqrt(overrideDistSq):F3}m。可能原因：Root Motion 未禁用 / RigidBody 物理 / 其他 Script 覆盖。WarnCount={warnCount}");
                            }
                        }
                    }

                    // 1) 同步插值位置
                    var newPos = new Vector3(interp.X, interp.Y, interp.Z);
                    bool positionUpdated = false;
                    if (_entityIdToLastPosition.TryGetValue(entityId, out var lastPos))
                    {
                        float distSquared = (newPos - lastPos).LengthSquared;
                        if (distSquared > 0.0001f)
                        {
                            actor.Position = newPos;
                            _entityIdToLastPosition[entityId] = newPos;
                            _diagLastWrittenPos[entityId] = newPos;
                            positionUpdated = true;
                            diagPositionsChanged++;
                        }
                    }
                    else
                    {
                        actor.Position = newPos;
                        _entityIdToLastPosition[entityId] = newPos;
                        _diagLastWrittenPos[entityId] = newPos;
                        positionUpdated = true;
                        diagPositionsChanged++;
                    }

                    // 诊断：每 120 帧输出每个远程实体的详细位置信息
                    if (isDiagFrame)
                    {
                        var actorPos = actor.Position;
                        bool interpChanged = !_diagInterpSnapshot.TryGetValue(entityId, out var prevInterp)
                            || (newPos - prevInterp).LengthSquared > 0.0001f;
                        _diagInterpSnapshot[entityId] = newPos;

                        Debug.Log($"[FlaxActorSyncSystem] SyncPos#{diagFrame} EntityId={entityId}: InterpPos=({interp.X:F2},{interp.Y:F2},{interp.Z:F2}), ActorPos=({actorPos.X:F2},{actorPos.Y:F2},{actorPos.Z:F2}), LastPos=({lastPos.X:F2},{lastPos.Y:F2},{lastPos.Z:F2}), PosUpdated={positionUpdated}, InterpChanged={interpChanged}, Alpha={interp.Alpha:F2}, Target=({interp.TargetX:F2},{interp.TargetY:F2},{interp.TargetZ:F2}), AuthYaw={auth.Yaw:F2}rad");
                    }

                    // 2) 同步朝向（从 AuthTransformComponent.Yaw，弧度）
                    // 服务端 Yaw 为弧度，Flax 旋转用 Yaw/Pitch/Roll（度）
                    float yawDeg = auth.Yaw * (180.0f / (float)Math.PI);
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

                    if (_archWorld.Has<MovementStateAuthComponent>(entity))
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
                            // 回退：使用 IsWalking 参数
                            if (!_entityIdToIsWalkingParam.TryGetValue(entityId, out var isWalkingParam) || isWalkingParam == null)
                            {
                                isWalkingParam = animatedModel.GetParameter("IsWalking");
                                if (isWalkingParam != null)
                                {
                                    _entityIdToIsWalkingParam[entityId] = isWalkingParam;
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
                    else if (animatedModel != null)
                    {
                        // 回退：用 Target/Start 差值判断移动意图
                        if (!_entityIdToIsWalkingParam.TryGetValue(entityId, out var isWalkingParam) || isWalkingParam == null)
                        {
                            isWalkingParam = animatedModel.GetParameter("IsWalking");
                            if (isWalkingParam != null)
                            {
                                _entityIdToIsWalkingParam[entityId] = isWalkingParam;
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
            }

            // 诊断：每 120 帧输出汇总统计，定位"有实体但位置不变"问题
            if (isDiagFrame)
            {
                Debug.Log($"[FlaxActorSyncSystem] SyncPos#{diagFrame} 汇总: TotalEntities={diagTotalEntities}, ActorsFound={diagActorsFound}, PositionsChanged={diagPositionsChanged}, ActorCount={_entityIdToActor.Count}。若 PositionsChanged=0 且 TotalEntities>0，说明插值位置未变化（HandleUpdate 未更新 Target 或 InterpolationSystem 未推进 Alpha）");
            }
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
        /// 查找 GameWorld 场景（World.scene），确保 Actor 生成在正确的场景中。
        /// </summary>
        private static FlaxEngine.Scene FindGameWorldScene()
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && (scene.Name == "World" || scene.Name == "WorldScene"))
                {
                    return scene;
                }
            }
            return null;
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

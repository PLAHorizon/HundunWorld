// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系：
//   - Save.NarrativeSaveManager 仅做业务级 JSON 文件存储，不涉及 Actor/组件状态；
//   - 本子系统做“世界级 Actor 快照”：遍历实现 INarrativeSavableActor 的 Actor，
//     生成 NarrativeActorRecord（含变换 + 字节归档），并写入磁盘 JSON。
//   - 两者互不替代；如需合并，可将 Save.NarrativeSaveData 嵌入本子系统的 PlayerData 或自定义记录中。
//
// 生命周期（对应 UE5）：NarrativeGameMode::InitGame → InitializeSaveSystem；
// NarrativePlayerCharacter::OnCharacterVisualInitialized → IsNewGame ? 新建角色 : LoadPlayerData。
// Flax 侧由 NarrativeSaveBootstrap（插件 Initialize 时安装）驱动，见 NarrativeSaveBootstrap.cs。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem.Subsystems
{
    /// <summary>
    /// 存档阶段变化事件。对应 UE5 FOnSavePhaseChanged。
    /// </summary>
    public delegate void OnSavePhaseChanged();

    /// <summary>
    /// 可选接口：供 Actor / 组件提供自定义字节状态。
    /// 由于 Flax 无 UE5 的 UPROPERTY(SaveGame) 反射归档机制，
    /// 实现此接口的 Actor / 组件可将自己的状态序列化为 byte[]，
    /// 存档子系统会将其写入 NarrativeActorRecord.ByteData / NarrativeSaveComponent.ByteData，
    /// 读档时回调 LoadSaveStateBytes。
    /// 未实现此接口的对象由 NarrativeSaveStateArchive 自动反射归档（等价 UE5 Actor->Serialize(Ar)），
    /// 也可以直接挂载 NarrativeSaveStateProviderComponent 获得完整归档能力。
    /// </summary>
    public interface INarrativeSaveStateProvider
    {
        /// <summary>获取当前状态字节（通常为 JSON 序列化结果）。</summary>
        byte[] GetSaveStateBytes();

        /// <summary>从字节恢复状态。</summary>
        void LoadSaveStateBytes(byte[] data);
    }

    /// <summary>
    /// 存档子系统。对应 UE5 UNarrativeSaveSubsystem（继承 UWorldSubsystem）。
    /// 负责保存场景中所有实现 INarrativeSavableActor 接口的 Actor 及其 INarrativeSavableComponent 组件。
    /// 默认会记录 Actor 的 Transform，以及通过 INarrativeSaveStateProvider / 反射归档得到的字节状态。
    /// Flax 无 WorldSubsystem 等价物，改为挂在宿主 Actor 上的 Script 单例（Instance）。
    /// </summary>
    public class NarrativeSaveSubsystem : Script
    {
        /// <summary>玩家数据延迟加载的等待上限（秒）。对应 UE5 定时器 DeferredLoadPlayerData。</summary>
        private const float PlayerDataLoadTimeoutSeconds = 30f;

        /// <summary>玩家数据延迟加载的重试间隔（秒），避免每帧全场景扫描。</summary>
        private const float PlayerDataRetryIntervalSeconds = 0.25f;

        // ===== 单例 =====

        private static NarrativeSaveSubsystem _instance;

        /// <summary>当前场景的存档子系统实例（可能为空）。</summary>
        public static NarrativeSaveSubsystem Instance => _instance;

        // ===== 事件 =====

        /// <summary>开始加载时触发。对应 UE5 OnBeginLoad。</summary>
        public event OnSavePhaseChanged OnBeginLoad;

        /// <summary>加载完成时触发。对应 UE5 OnFinishedLoad。</summary>
        public event OnSavePhaseChanged OnFinishedLoad;

        /// <summary>开始保存时触发。对应 UE5 OnBeginSave。</summary>
        public event OnSavePhaseChanged OnBeginSave;

        /// <summary>保存完成时触发。对应 UE5 OnFinishedSave。</summary>
        public event OnSavePhaseChanged OnFinishedSave;

        /// <summary>稳定 Actor 生成时触发。对应 UE5 OnStableActorSpawned。</summary>
        public event Action<Actor, Guid> OnStableActorSpawned;

        /// <summary>稳定 Actor 销毁时触发。对应 UE5 OnStableActorDestroyed。</summary>
        public event Action<Actor, Guid> OnStableActorDestroyed;

        // ===== 内部状态 =====

        private NarrativeSave _narrativeSaveGame;
        private string _optionString = string.Empty;
        private bool _bIsCurrentlyLoading;
        private int _currentSaveSlot = -1;
        private string _currentSaveName = string.Empty;
        private bool _bSavingDisabled;
        private bool _bIsLevelTransition;
        private bool _bInitialized;

        /// <summary>GUID → 运行时 Actor 引用的快速查找表。对应 UE5 QuickLookupMap。</summary>
        private readonly Dictionary<Guid, WeakReference<Actor>> _quickLookupMap = new Dictionary<Guid, WeakReference<Actor>>();

        /// <summary>关卡内置 Actor 集合（场景初始化快照）。对应 UE5 net startup 语义。键为 Flax Actor.ID。</summary>
        private readonly HashSet<Guid> _netStartupActorIds = new HashSet<Guid>();

        /// <summary>运行期动态生成的 Actor 集合。键为 Flax Actor.ID。</summary>
        private readonly HashSet<Guid> _dynamicSpawnedActorIds = new HashSet<Guid>();

        /// <summary>Flax Actor.ID → 稳定存档 GUID 映射。Actor 被外部销毁后据此把记录标 bDestroyed。</summary>
        private readonly Dictionary<Guid, Guid> _actorIdToStableGuid = new Dictionary<Guid, Guid>();

        /// <summary>首轮巡检发现、次轮仍未进入任何集合的候选（两轮确认，防 additive 场景加载竞态误判）。</summary>
        private readonly HashSet<Guid> _pendingDynamicCandidateIds = new HashSet<Guid>();

        private float _actorReconcileTimer;

        /// <summary>Actor 增删巡检间隔（秒）。Flax 1.12 无全局 ActorDestroy 事件（已在 FlaxEngine.CSharp.xml 核实），
        /// 用轮询对账实现 UE5 PostInitialize 绑定 ActorDestroy 的等价效果（NarrativeSaveSubsystem.cpp:645-669）。</summary>
        private const float ActorReconcileIntervalSeconds = 1.0f;

        private bool _bNetStartupSnapshotTaken;

        private Actor _hostActor;
        private Scene _saveContextScene;
        private float _autosaveTimer;
        private bool _bPendingPlayerDataLoad;
        private bool _bPlayerDataLoaded;
        private double _playerDataLoadDeadline;
        private float _playerDataRetryTimer;
        private bool _bSaveSystemInitialized;

        private static readonly JsonSerializerOptions _jsonOptions = NarrativeSaveStateArchive.CreateSaveFileOptions(false);

        /// <summary>构造。</summary>
        public NarrativeSaveSubsystem()
        {
        }

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;
            // 缓存当前场景中所有稳定 Actor
            RebuildLookupMap();

            // 对应 UE5 PostInitialize 绑定关卡增删事件（NarrativeSaveSubsystem.cpp:645-669）。
            // Flax 1.12 的全局事件为 Level.SceneLoaded / Level.SceneUnloading（已在 NarrativeSaveBootstrap
            // 的既有使用处核实签名 (Scene, Guid)）；Actor 级没有全局 Destroy 事件，用 TickActorReconcile 轮询对账。
            Level.SceneUnloading += OnLevelSceneUnloading;
        }

        public override void OnDisable()
        {
            Level.SceneUnloading -= OnLevelSceneUnloading;
            _quickLookupMap.Clear();
            _netStartupActorIds.Clear();
            _dynamicSpawnedActorIds.Clear();
            _actorIdToStableGuid.Clear();
            _pendingDynamicCandidateIds.Clear();
            _bNetStartupSnapshotTaken = false;
            if (_instance == this) _instance = null;
            base.OnDisable();
        }

        /// <summary>场景正在卸载：其中 Actor 即将消失，延后一拍做销毁对账（等价 UE5 LevelRemoveFromWorld 绑定）。</summary>
        private void OnLevelSceneUnloading(Scene scene, Guid sceneId)
        {
            if (scene == null) return;
            Scripting.InvokeOnUpdate(() =>
            {
                if (_instance != this) return;
                ReconcileDestroyedActors();
            });
        }

        /// <summary>
        /// 定时自动存档与玩家数据延迟加载的驱动。
        /// 对应 UE5 FTimerManager 上的 TimerHandle_DeferredLoadPlayerData 与关卡切换前的静默保存。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();
            TickPlayerDataLoad();
            TickAutosave();
            TickActorReconcile();
        }

        /// <summary>
        /// 周期性 Actor 对账（缺陷 2/3 的 SaveSystem 侧统一钩子）：
        /// 1) 把快照之后出现、且未走 NotifyActorDynamicallySpawned 的可存档 Actor 确定性登记为动态生成
        ///    （两轮观察确认，替代 NarrativeDynamicSpawnSourceResolver 的名称匹配兜底路径）；
        /// 2) 把被外部销毁、从未回调 OnActorDestroyed 的 net startup Actor 的记录标 bDestroyed。
        /// 对应 UE5 PostInitialize 绑定的 ActorDestroy/LevelAdd/LevelRemove 委托。
        /// </summary>
        private void TickActorReconcile()
        {
            if (!_bNetStartupSnapshotTaken || _bIsCurrentlyLoading) return;

            _actorReconcileTimer += Time.DeltaTime;
            if (_actorReconcileTimer < ActorReconcileIntervalSeconds) return;
            _actorReconcileTimer = 0f;

            SweepDynamicallySpawnedActors();
            ReconcileDestroyedActors();
        }

        /// <summary>把「快照后出现且连续两轮存在」的可存档 Actor 确定性登记为动态生成来源。</summary>
        private void SweepDynamicallySpawnedActors()
        {
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == null || IsArchivableExempt(actor)) continue;

                if (_netStartupActorIds.Contains(actor.ID) || _dynamicSpawnedActorIds.Contains(actor.ID))
                {
                    _pendingDynamicCandidateIds.Remove(actor.ID);
                    continue;
                }

                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding == null) continue;
                Guid stableGuid = binding.GetActorGUID();
                if (stableGuid == Guid.Empty) continue;

                // HashSet.Add 返回 false 表示上一轮已见过：两轮确认后才登记。
                if (!_pendingDynamicCandidateIds.Add(actor.ID))
                {
                    // 确定性登记生成身份；Prefab 路径能解析到就一并持久登记，
                    // 后续保存不再依赖生成器反查/名称匹配。
                    string prefabPath = NarrativeDynamicSpawnSourceResolver.Resolve(actor);
                    NotifyActorDynamicallySpawned(actor, prefabPath);
                    NarrativeLog.Log($"[SaveSystem] 自动登记动态生成 Actor：{actor.Name}（路径：{(string.IsNullOrEmpty(prefabPath) ? "待解析链兜底" : prefabPath)}）");
                }
            }
        }

        /// <summary>
        /// 对账已被外部销毁的 net startup Actor：其记录标 bDestroyed，
        /// 无记录时补建「已销毁」占位记录，保证读档侧语义与 UE5 ActorDestroy 绑定一致。
        /// </summary>
        private void ReconcileDestroyedActors()
        {
            if (_narrativeSaveGame == null || _netStartupActorIds.Count == 0) return;

            var liveIds = new HashSet<Guid>();
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor != null) liveIds.Add(actor.ID);
            }

            List<Guid> deadIds = null;
            foreach (var actorId in _netStartupActorIds)
            {
                if (liveIds.Contains(actorId)) continue;
                (deadIds ??= new List<Guid>()).Add(actorId);
            }
            if (deadIds == null) return;

            foreach (var actorId in deadIds)
            {
                _netStartupActorIds.Remove(actorId);
                _pendingDynamicCandidateIds.Remove(actorId);

                if (!_actorIdToStableGuid.TryGetValue(actorId, out var stableGuid) || stableGuid == Guid.Empty)
                {
                    _actorIdToStableGuid.Remove(actorId);
                    continue;
                }
                _actorIdToStableGuid.Remove(actorId);

                _quickLookupMap.Remove(stableGuid);

                if (_narrativeSaveGame.RecordMap.TryGetValue(stableGuid, out var record) && record != null)
                {
                    if (!record.bDestroyed)
                    {
                        record.bDestroyed = true;
                        NarrativeLog.Log($"[SaveSystem] 对账标记 net startup Actor 已销毁：{record.ActorName}，GUID={stableGuid}");
                    }
                }
                else
                {
                    _narrativeSaveGame.RecordMap[stableGuid] = new NarrativeActorRecord
                    {
                        ActorGUID = stableGuid,
                        ActorName = $"Destroyed_{stableGuid:N}",
                        bNetStartup = true,
                        bDestroyed = true,
                    };
                    NarrativeLog.Log($"[SaveSystem] 对账补建已销毁记录：GUID={stableGuid}");
                }
            }
        }

        /// <summary>由 NarrativeSaveBootstrap 设置宿主 Actor，避免子系统把自己写进存档。</summary>
        public void SetHostActor(Actor host)
        {
            _hostActor = host;
        }

        /// <summary>
        /// 采集/对账/还原三侧的统一豁免判定：子系统自身宿主 + 运行期引导容器宿主
        /// （HUD / CommonUI / 面板挂在上面，每次场景加载重建，跨会话无身份）。
        /// </summary>
        private bool IsArchivableExempt(Actor actor)
        {
            return actor == _hostActor || NarrativeSaveContainerFilter.IsRuntimeBootstrapHost(actor);
        }

        /// <summary>
        /// 设置当前存档上下文场景（写入 NarrativeSave.LevelName 的关卡名来源，主菜单据此决定加载哪个关卡）。
        /// 对应 UE5 UGameplayStatics::GetCurrentLevelName。
        /// </summary>
        public void SetSaveContextScene(Scene scene)
        {
            if (scene == null) return;
            _saveContextScene = scene;
            if (_narrativeSaveGame != null)
            {
                _narrativeSaveGame.LevelName = scene.Name ?? string.Empty;
            }
        }

        /// <summary>当前存档上下文场景（为空时取 Level.GetScene(0)）。</summary>
        private Scene ResolveContextScene()
        {
            if (_saveContextScene != null) return _saveContextScene;
            return Level.GetScene(0);
        }

        /// <summary>场景退出时清理（由 NarrativeSaveBootstrap 调用）。</summary>
        public void OnWorldTeardown()
        {
            _bPendingPlayerDataLoad = false;
            _bPlayerDataLoaded = false;
            _bInitialized = false;
            _bSaveSystemInitialized = false;
            NarrativePlayerDataRegistry.ClearLocalPlayer();
            NarrativeDynamicSpawnSourceResolver.Reset();
            _actorIdToStableGuid.Clear();
            _pendingDynamicCandidateIds.Clear();
            _bNetStartupSnapshotTaken = false;
        }

        /// <summary>重建 GUID → Actor 查找表。</summary>
        private void RebuildLookupMap()
        {
            _quickLookupMap.Clear();
            foreach (var actor in Level.GetActors<Actor>())
            {
                RegisterActorInLookup(actor);
            }
        }

        /// <summary>注册一个 Actor 到查找表（若其实现 INarrativeStableActor 或具备影子实现）。</summary>
        private void RegisterActorInLookup(Actor actor)
        {
            if (actor == null) return;

            Guid guid = Guid.Empty;
            if (actor is INarrativeStableActor stable)
            {
                guid = stable.GetActorGUID();
            }
            else
            {
                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding != null) guid = binding.GetActorGUID();
            }

            if (guid != Guid.Empty)
            {
                _quickLookupMap[guid] = new WeakReference<Actor>(actor);
                // 记录运行时 ID → 稳定 GUID：Actor 被外部销毁后，对账路径仍可找到其记录
                _actorIdToStableGuid[actor.ID] = guid;
            }
        }

        /// <summary>从查找表移除一个 Actor。</summary>
        private void UnregisterActorFromLookup(Actor actor)
        {
            if (actor == null) return;

            Guid guid = Guid.Empty;
            if (actor is INarrativeStableActor stable)
            {
                guid = stable.GetActorGUID();
            }
            else
            {
                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding != null) guid = binding.GetActorGUID();
            }

            if (guid != Guid.Empty)
            {
                _quickLookupMap.Remove(guid);
            }
        }

        /// <summary>
        /// 通知子系统：该 Actor 由运行期代码生成（非关卡内置）。
        /// 外部生成器（Spawner / NPC 子系统 / 掉落物）一行接入即可保证它走动态重生分支。
        /// <param name="actor">生成的 Actor。</param>
        /// <param name="prefabPath">生成所用的 Prefab 路径（可空；提供后动态重生一定成功）。</param>
        /// </summary>
        public void NotifyActorDynamicallySpawned(Actor actor, string prefabPath = null)
        {
            if (actor == null) return;
            _dynamicSpawnedActorIds.Add(actor.ID);
            _netStartupActorIds.Remove(actor.ID);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                NarrativeDynamicSpawnSourceResolver.RegisterSpawnSource(actor, prefabPath);
            }

            RegisterActorInLookup(actor);
        }

        /// <summary>当前场景内已存在的 Actor 视为“关卡内置”（对应 UE5 net startup）。</summary>
        private void TakeNetStartupSnapshot()
        {
            _netStartupActorIds.Clear();
            _dynamicSpawnedActorIds.Clear();

            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == null || IsArchivableExempt(actor)) continue;
                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding == null) continue;
                _netStartupActorIds.Add(actor.ID);
                RememberStableGuid(actor, binding);
            }

            _bNetStartupSnapshotTaken = true;
        }

        /// <summary>
        /// 重新采集“关卡内置”Actor 快照。additive 场景加载完成后调用：
        /// 随场景文件一起出现的 Actor 属于 net startup，不应被当作运行期动态生成对象。
        /// </summary>
        public void RefreshNetStartupSnapshot()
        {
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == null || IsArchivableExempt(actor)) continue;
                if (_dynamicSpawnedActorIds.Contains(actor.ID)) continue;
                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding == null) continue;
                _netStartupActorIds.Add(actor.ID);
                RememberStableGuid(actor, binding);
            }

            _bNetStartupSnapshotTaken = true;
        }

        /// <summary>登记 Flax 运行时 Actor.ID → 稳定存档 GUID，供销毁对账在 Actor 消失后定位记录。</summary>
        private void RememberStableGuid(Actor actor, NarrativeSavableActorBinding binding)
        {
            if (actor == null || binding == null) return;
            Guid guid = binding.GetActorGUID();
            if (guid != Guid.Empty)
            {
                _actorIdToStableGuid[actor.ID] = guid;
            }
        }

        private void EnsureNetStartupSnapshot()
        {
            if (!_bNetStartupSnapshotTaken)
            {
                TakeNetStartupSnapshot();
            }
        }

        // ===== 存档对象更新 =====

        /// <summary>
        /// 创建/更新存档对象，将世界状态写入其中（不写入磁盘）。
        /// 对应 UE5 UpdateSaveObject。
        /// </summary>
        /// <param name="bSkipRecordCreation">是否跳过记录生成（用于新游戏初始化）。</param>
        public virtual bool UpdateSaveObject(bool bSkipRecordCreation = false)
        {
            if (_narrativeSaveGame == null)
            {
                _narrativeSaveGame = CreateSaveObject();
            }

            if (bSkipRecordCreation)
            {
                return true;
            }

            if (_narrativeSaveGame == null) return false;

            EnsureNetStartupSnapshot();

            // 保存前再对账一次销毁状态：外部（引擎/其它系统）销毁的 net startup Actor
            // 没有机会回调 OnActorDestroyed，此处按场景存活集合补标 bDestroyed。
            ReconcileDestroyedActors();

            // 主菜单根据此字段记忆上一次加载的关卡
            Scene scene = ResolveContextScene();
            _narrativeSaveGame.LevelName = scene != null ? scene.Name : string.Empty;

            // 角色创建器外观随存档持久化（对应 UE5 UNarrativeSaveWithCreatorData 的用法）。
            // 不做这一步的话读档只会按随机种子重掷外观（缺陷：appearance re-rolls every session）。
            NarrativePro.CharacterCreator.NarrativeCreatorPersistence.CaptureIntoSave(_narrativeSaveGame);

            // 玩家单独保存：把控制器 / 玩家状态 / Pawn 折进一份 PlayerData（对应 UE5 cpp:47-83）
            var skippedActorIds = CapturePlayerData();

            // 遍历场景中所有 Actor，为实现了 INarrativeStableActor 的 Actor 生成记录
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == null || IsArchivableExempt(actor)) continue;

                var binding = NarrativeSavableActorResolver.Resolve(actor);
                if (binding == null) continue;

                Guid actorGuid = binding.GetActorGUID();
                if (actorGuid == Guid.Empty) continue;

                if (skippedActorIds.Contains(actor.ID)) continue;

                var record = new NarrativeActorRecord();
                if (CreateActorRecord(actor, record))
                {
                    _narrativeSaveGame.RecordMap[actorGuid] = record;
                }
            }

            // 维护按关卡镜像分片（SavedLevels 的真实写入方，对应 UE5 FNarrativeSavedLevel 按关卡存储）
            _narrativeSaveGame.SyncSavedLevel(_narrativeSaveGame.LevelName);

            return true;
        }

        /// <summary>
        /// 捕获玩家数据到 NarrativeSaveGame.PlayerData。对应 UE5 UpdateSaveObject 中的 PlayerController 遍历块。
        /// </summary>
        /// <returns>需要从世界记录中排除的 Actor 运行时 ID（Flax Actor.ID）集合。</returns>
        private HashSet<Guid> CapturePlayerData()
        {
            var skipped = new HashSet<Guid>();

            var snapshot = NarrativePlayerDataRegistry.Resolve();
            if (snapshot == null || !snapshot.IsValidForSave()) return skipped;

            var playerData = new NarrativeSavePlayer();

            // Pawn（保留 Transform：读档时据此还原玩家位置）
            if (snapshot.Pawn != null)
            {
                skipped.Add(snapshot.Pawn.ID);
                CreateActorRecord(snapshot.Pawn, playerData.PawnData);
            }

            // PlayerState
            var playerStateActor = ResolveActorFor(snapshot.PlayerState);
            if (playerStateActor != null)
            {
                skipped.Add(playerStateActor.ID);
                if (playerStateActor == snapshot.Pawn)
                {
                    // 同一个 Actor 兼职 PlayerState：复制一份记录，避免与 PawnData 共享实例
                    playerData.PlayerStateData = playerData.PawnData.Clone();
                }
                else
                {
                    CreateActorRecord(playerStateActor, playerData.PlayerStateData);
                }
            }

            // PlayerController
            var controllerActor = ResolveActorFor(snapshot.Controller);
            if (controllerActor != null)
            {
                skipped.Add(controllerActor.ID);
                if (controllerActor == snapshot.Pawn)
                {
                    playerData.ControllerData = playerData.PawnData.Clone();
                }
                else if (controllerActor == playerStateActor)
                {
                    playerData.ControllerData = playerData.PlayerStateData.Clone();
                }
                else
                {
                    CreateActorRecord(controllerActor, playerData.ControllerData);
                }
            }

            // 不保存控制器与玩家状态的变换（UE5：没必要，最坏情况会引发 Bug）
            playerData.PlayerStateData.Transform = Transform.Identity;
            playerData.ControllerData.Transform = Transform.Identity;

            _narrativeSaveGame.PlayerData = playerData;

            if (playerData.IsValid())
            {
                NarrativeLog.Log($"[SaveSystem] 玩家数据已捕获：Pawn={snapshot.Pawn?.Name}");
            }

            return skipped;
        }

        private static Actor ResolveActorFor(System.Object candidate)
        {
            if (candidate is Actor actor) return actor;
            if (candidate is Script script) return script.Actor;
            return null;
        }

        // ===== 存读档 =====

        /// <summary>
        /// 将记录写入存档文件并提交到磁盘。对应 UE5 Save。
        /// </summary>
        /// <param name="saveName">存档名（默认取开发者设置的 DefaultSaveName）。</param>
        /// <param name="slot">存档槽（默认取开发者设置的 DefaultSaveSlot）。</param>
        public virtual bool Save(string saveName = "NarrativeSave", int slot = 0)
        {
            if (_bSavingDisabled) return false;

            ApplyDefaultSlot(ref saveName, ref slot);

            OnBeginSave?.Invoke();

            UpdateSaveObject();

            bool bSaved = false;
            if (_narrativeSaveGame != null)
            {
                bSaved = SaveGameToSlot(_narrativeSaveGame, saveName, slot);
                if (bSaved)
                {
                    _currentSaveName = saveName;
                    _currentSaveSlot = slot;

                    // 世界档落盘成功后同步落业务档（任务/对话进度），
                    // 让 NarrativeSaveManager.SaveToFile 在自动存档/退出/手动保存三条路径上都有真实调用方。
                    PersistNarrativeBusinessSave();
                }
                else
                {
                    _currentSaveName = string.Empty;
                    _currentSaveSlot = -1;
                }
            }

            OnFinishedSave?.Invoke();
            return bSaved;
        }

        /// <summary>
        /// 加载存档文件。对应 UE5 Load。
        /// 会更新场景中已存在的 INarrativeSavableActor 状态，并尝试重新生成缺失的动态 Actor。
        /// </summary>
        public virtual bool Load(string saveName = "NarrativeSave", int slot = 0)
        {
            ApplyDefaultSlot(ref saveName, ref slot);

            if (!DoesSaveGameExist(saveName, slot))
            {
                return false;
            }

            OnBeginLoad?.Invoke();
            _bIsCurrentlyLoading = true;
            _bInitialized = true;

            _narrativeSaveGame = LoadGameFromSlot(saveName, slot);

            if (_narrativeSaveGame != null)
            {
                _currentSaveName = saveName;
                _currentSaveSlot = slot;

                // 并入当前关卡的分片记录（SavedLevels 的真实读取方）：
                // 合并 RecordMap 缺失的 GUID 由分片补回，不覆盖已有记录。
                Scene loadScene = ResolveContextScene();
                int levelMerged = _narrativeSaveGame.MergeSavedLevel(loadScene != null ? loadScene.Name : null);
                if (levelMerged > 0)
                {
                    NarrativeLog.Log($"[SaveSystem] 从关卡分片补回 {levelMerged} 条记录（{loadScene?.Name}）");
                }

                var foundRecords = new HashSet<Guid>();
                var pendingDestroy = new List<Actor>();

                // 遍历场景中已有 Actor，按 GUID 还原状态
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null || IsArchivableExempt(actor)) continue;

                    var binding = NarrativeSavableActorResolver.Resolve(actor);
                    if (binding == null) continue;

                    Guid actorGuid = binding.GetActorGUID();
                    if (actorGuid == Guid.Empty) continue;

                    if (_narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
                    {
                        // net startup 且标记销毁的 Actor 会被 LoadActorFromRecord 销毁；
                        // 这里统一延后到遍历结束后执行，避免在 Level.GetActors 快照销毁期间访问已失效对象。
                        if (record != null && record.bNetStartup && record.bDestroyed)
                        {
                            pendingDestroy.Add(actor);
                            foundRecords.Add(actorGuid);
                            continue;
                        }

                        LoadActorFromRecord(actor, record);
                        foundRecords.Add(actorGuid);
                    }
                }

                foreach (var actor in pendingDestroy)
                {
                    DestroyActor(actor);
                }

                // 收集需要动态生成的 Actor（只收非 net startup 的动态记录，UE5 语义）
                var dynamicActors = new List<NarrativeActorRecord>();
                foreach (var kvp in _narrativeSaveGame.RecordMap)
                {
                    var record = kvp.Value;
                    if (!foundRecords.Contains(record.ActorGUID))
                    {
                        if (!record.bNetStartup && record.bNeedsDynamicSpawn)
                        {
                            dynamicActors.Add(record);
                        }
                    }
                }

                foreach (var record in dynamicActors)
                {
                    LoadDynamicRecord(record);
                }

                if (dynamicActors.Count > 0)
                {
                    NarrativeLog.Log($"[SaveSystem] 动态重生 Actor {dynamicActors.Count} 个");
                }

                // 玩家数据等 Pawn/控制器就绪后再还原（对应 UE5 角色视觉初始化完成时的延迟加载）
                if (_narrativeSaveGame.PlayerData != null && _narrativeSaveGame.PlayerData.IsValid())
                {
                    RequestDeferredPlayerDataLoad();
                }
            }
            else
            {
                _currentSaveName = string.Empty;
                _currentSaveSlot = -1;
            }

            _bIsCurrentlyLoading = false;
            OnFinishedLoad?.Invoke();
            return true;
        }

        /// <summary>删除磁盘上的存档。对应 UE5 DeleteSave。</summary>
        public virtual bool DeleteSave(string saveName = "NarrativeSave", int slot = 0)
        {
            ApplyDefaultSlot(ref saveName, ref slot);

            string filePath = GetSaveFilePath(saveName, slot);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    if (saveName == _currentSaveName)
                    {
                        _currentSaveName = string.Empty;
                        _currentSaveSlot = -1;
                        _narrativeSaveGame = null;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 删除存档失败 {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载玩家数据。对应 UE5 LoadPlayerData（按 Pawn → PlayerState → Controller 顺序还原）。
        /// </summary>
        public virtual void LoadPlayerData()
        {
            if (_narrativeSaveGame == null) return;
            if (_narrativeSaveGame.PlayerData == null || !_narrativeSaveGame.PlayerData.IsValid()) return;

            var playerSave = _narrativeSaveGame.PlayerData;

            var snapshot = NarrativePlayerDataRegistry.Resolve();
            if (snapshot == null)
            {
                NarrativeLog.LogWarning("[SaveSystem] LoadPlayerData：本地玩家尚未就绪，稍后重试。");
                return;
            }

            _bIsCurrentlyLoading = true;
            try
            {
                // 1) Pawn：关卡切换时保留新关卡给出的位置
                if (snapshot.Pawn != null && playerSave.PawnData != null && playerSave.PawnData.IsValid())
                {
                    Transform oldPawnTransform = snapshot.Pawn.Transform;
                    LoadActorFromRecord(snapshot.Pawn, playerSave.PawnData);

                    if (_bIsLevelTransition)
                    {
                        snapshot.Pawn.Transform = oldPawnTransform;
                    }
                }
                NarrativePlayerDataRegistry.NotifyReceivers(NarrativePlayerDataRole.Pawn, playerSave.PawnData);

                // 2) PlayerState：物品/阵营等，需要 Pawn 先就绪
                var playerStateActor = ResolveActorFor(snapshot.PlayerState);
                if (playerStateActor != null && playerSave.PlayerStateData != null && playerSave.PlayerStateData.IsValid()
                    && !ReferenceEquals(playerStateActor, snapshot.Pawn))
                {
                    LoadActorFromRecord(playerStateActor, playerSave.PlayerStateData);
                }
                NarrativePlayerDataRegistry.NotifyReceivers(NarrativePlayerDataRole.PlayerState, playerSave.PlayerStateData);

                // 3) Controller：任务路点等，需要一切就绪
                var controllerActor = ResolveActorFor(snapshot.Controller);
                if (controllerActor != null && playerSave.ControllerData != null && playerSave.ControllerData.IsValid()
                    && !ReferenceEquals(controllerActor, snapshot.Pawn)
                    && !ReferenceEquals(controllerActor, playerStateActor))
                {
                    LoadActorFromRecord(controllerActor, playerSave.ControllerData);
                }
                NarrativePlayerDataRegistry.NotifyReceivers(NarrativePlayerDataRole.Controller, playerSave.ControllerData);

                _bPlayerDataLoaded = true;
                _bPendingPlayerDataLoad = false;
                NarrativeLog.Log($"[SaveSystem] 玩家数据已还原（Pawn={snapshot.Pawn?.Name}）");
            }
            finally
            {
                _bIsCurrentlyLoading = false;
            }
        }

        /// <summary>
        /// 延迟加载玩家数据。对应 UE5 DeferredLoadPlayerData（由角色视觉初始化完成后触发的定时器回调）。
        /// 玩家尚未就绪时会在 OnUpdate 中重试，直到超时。
        /// </summary>
        public void DeferredLoadPlayerData()
        {
            RequestDeferredPlayerDataLoad();
        }

        /// <summary>请求一次延迟玩家数据加载（可重复请求）。</summary>
        public void RequestDeferredPlayerDataLoad()
        {
            if (_bPlayerDataLoaded) return;
            _bPendingPlayerDataLoad = true;
            _playerDataLoadDeadline = Time.UnscaledGameTime + PlayerDataLoadTimeoutSeconds;
        }

        /// <summary>玩家数据是否已还原。</summary>
        public bool IsPlayerDataLoaded => _bPlayerDataLoaded;

        private void TickPlayerDataLoad()
        {
            if (!_bPendingPlayerDataLoad || _bPlayerDataLoaded) return;
            if (_bIsCurrentlyLoading) return;

            _playerDataRetryTimer += Time.DeltaTime;
            if (_playerDataRetryTimer < PlayerDataRetryIntervalSeconds) return;

            if (Time.UnscaledGameTime > _playerDataLoadDeadline)
            {
                _bPendingPlayerDataLoad = false;
                NarrativeLog.LogWarning("[SaveSystem] 玩家数据延迟加载超时（本地玩家始终未就绪），已放弃本次还原。");
                return;
            }

            try
            {
                LoadPlayerData();
            }
            catch (Exception ex)
            {
                _bPendingPlayerDataLoad = false;
                NarrativeLog.LogError($"[SaveSystem] 玩家数据延迟加载异常: {ex.Message}");
            }
        }

        private void TickAutosave()
        {
            var settings = SaveSystemDeveloperSettings.Instance;
            float interval = settings != null ? settings.AutoSaveIntervalSeconds : 0f;
            if (interval <= 0f) return;
            if (_bSavingDisabled) return;
            if (!_bInitialized) return;

            _autosaveTimer += Time.DeltaTime;
            if (_autosaveTimer < interval) return;
            _autosaveTimer = 0f;

            if (!HasSaveableState()) return;

            string saveName = settings.DefaultSaveName ?? "NarrativeSave";
            int slot = settings.DefaultSaveSlot;
            Save(saveName, slot);
        }

        /// <summary>场景退出时的静默保存。对应 UE5 关卡切换前保存。</summary>
        public void SaveOnSceneExit()
        {
            if (_bSavingDisabled) return;

            // 业务档（任务/对话进度）先落盘：即使世界档无记录/玩家数据（纯单机剧情推进）也要保住进度
            PersistNarrativeBusinessSave();

            if (!_bInitialized && _narrativeSaveGame == null) return;
            if (!HasSaveableState()) return;

            var settings = SaveSystemDeveloperSettings.Instance;
            string saveName = _currentSaveName;
            int slot = _currentSaveSlot;

            if (string.IsNullOrEmpty(saveName))
            {
                saveName = settings != null ? settings.DefaultSaveName : "NarrativeSave";
                slot = settings != null ? settings.DefaultSaveSlot : 0;
            }

            try
            {
                Save(saveName, slot);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 场景退出保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 把激活 TalesComponent 的任务/对话业务进度经 NarrativeSaveManager 落 JSON 文件。
        /// 由 Save()/SaveOnSceneExit() 驱动（自动存档、退出、手动保存三条路径共用）。
        /// </summary>
        private static void PersistNarrativeBusinessSave()
        {
            try
            {
                var tales = NarrativeProPlugin.Instance?.ActiveTalesComponent;
                if (tales != null)
                {
                    tales.SaveNarrativeStateToJson();
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 业务存档落盘失败: {ex.Message}");
            }
        }

        /// <summary>当前是否有值得落盘的状态（避免登录/空场景写空档）。</summary>
        private bool HasSaveableState()
        {
            if (_narrativeSaveGame == null) return false;
            if (_narrativeSaveGame.RecordMap != null && _narrativeSaveGame.RecordMap.Count > 0) return true;
            return _narrativeSaveGame.PlayerData != null && _narrativeSaveGame.PlayerData.IsValid();
        }

        // ===== 单 Actor 操作 =====

        /// <summary>加载单个 Actor 的状态。对应 UE5 LoadSingleActor。</summary>
        public virtual bool LoadSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding == null) return false;

            Guid actorGuid = binding.GetActorGUID();
            if (actorGuid != Guid.Empty && _narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
            {
                LoadActorFromRecord(actor, record);
                return true;
            }

            return false;
        }

        /// <summary>保存单个 Actor 的状态。对应 UE5 SaveSingleActor。</summary>
        public virtual bool SaveSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding == null) return false;

            Guid actorGuid = binding.GetActorGUID();
            if (actorGuid == Guid.Empty) return false;

            EnsureNetStartupSnapshot();

            var newRecord = new NarrativeActorRecord();
            if (CreateActorRecord(actor, newRecord))
            {
                _narrativeSaveGame.RecordMap[newRecord.ActorGUID] = newRecord;
            }
            return true;
        }

        /// <summary>从存档移除单个 Actor 的记录。对应 UE5 RemoveSingleActor。</summary>
        public virtual bool RemoveSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding == null) return false;

            Guid actorGuid = binding.GetActorGUID();
            bool bNetStartup = IsNetStartupActor(actor);

            // net startup Actor 若无记录，先创建一份以便标记销毁
            if (bNetStartup && !_narrativeSaveGame.RecordMap.ContainsKey(actorGuid))
            {
                SaveSingleActor(actor);
            }

            if (actorGuid != Guid.Empty && _narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
            {
                if (record.bNetStartup != bNetStartup)
                {
                    NarrativeLog.LogWarning($"[SaveSystem] 记录与 Actor 的 net startup 状态不一致: {actor.Name}");
                }

                if (record.bNetStartup)
                {
                    if (!record.bDestroyed)
                    {
                        record.bDestroyed = true;
                        NarrativeLog.Log($"[SaveSystem] 标记 net startup Actor {actor.Name} 为已销毁。");
                    }
                }
                else
                {
                    _narrativeSaveGame.RecordMap.Remove(actorGuid);
                    if (binding.NeedsPermanentRecord() && !record.bDestroyed)
                    {
                        // 需要永久记录的动态 Actor：销毁前把当前状态写回
                        var permanentRecord = new NarrativeActorRecord();
                        if (CreateActorRecord(actor, permanentRecord))
                        {
                            _narrativeSaveGame.RecordMap[permanentRecord.ActorGUID] = permanentRecord;
                        }
                    }
                }
                return true;
            }

            return false;
        }

        /// <summary>Actor 销毁时的登记清理。对应 UE5 OnActorDestroyed。</summary>
        public void OnActorDestroyed(Actor destroyedActor)
        {
            if (destroyedActor == null) return;

            var binding = NarrativeSavableActorResolver.Resolve(destroyedActor);
            if (binding != null)
            {
                Guid guid = binding.GetActorGUID();
                if (guid != Guid.Empty)
                {
                    _quickLookupMap.Remove(guid);
                    OnStableActorDestroyed?.Invoke(destroyedActor, guid);
                }
            }

            _dynamicSpawnedActorIds.Remove(destroyedActor.ID);
            _netStartupActorIds.Remove(destroyedActor.ID);
            _pendingDynamicCandidateIds.Remove(destroyedActor.ID);
            _actorIdToStableGuid.Remove(destroyedActor.ID);
        }

        /// <summary>检查指定 GUID 的记录是否存在。对应 UE5 DoesRecordExist。</summary>
        public bool DoesRecordExist(Guid recordGUID)
        {
            return _narrativeSaveGame != null && _narrativeSaveGame.RecordMap.ContainsKey(recordGUID);
        }

        /// <summary>按 GUID 加载动态记录。对应 UE5 LoadDynamicRecord(GUID)。</summary>
        public virtual bool LoadDynamicRecord(Guid recordGUID)
        {
            if (_narrativeSaveGame != null && _narrativeSaveGame.RecordMap.TryGetValue(recordGUID, out var record))
            {
                return LoadDynamicRecord(record);
            }
            return false;
        }

        /// <summary>从动态记录生成 Actor。对应 UE5 LoadDynamicRecord(record)。</summary>
        public virtual bool LoadDynamicRecord(NarrativeActorRecord record)
        {
            if (record == null || !record.IsValid() || !record.bNeedsDynamicSpawn) return false;

            string prefabPath = record.ActorSoftClassPath;

            // ActorSoftClassPath 必须是 Prefab 资产路径；类型全名一律拒绝（曾经的缺陷）
            if (!NarrativeDynamicSpawnSourceResolver.IsPlausibleAssetPath(prefabPath))
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 失败：ActorSoftClassPath 不是 Prefab 资产路径（{prefabPath}），GUID={record.ActorGUID}");
                return false;
            }

            if (!NarrativeDynamicSpawnSourceResolver.ValidatePrefabPath(prefabPath))
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 失败：Prefab 资产不存在（{prefabPath}），GUID={record.ActorGUID}");
                return false;
            }

            // 通过 Flax 的 Content.LoadAsync 加载预制体，再由 PrefabManager 在记录的 Transform 处生成。
            Prefab prefab = Content.LoadAsync<Prefab>(prefabPath);
            if (prefab == null)
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 加载预制体失败：路径={prefabPath}，GUID={record.ActorGUID}");
                return false;
            }

            Actor spawnedActor = PrefabManager.SpawnPrefab(prefab, record.Transform.Translation, record.Transform.Orientation);
            if (spawnedActor == null)
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 生成 Actor 失败：路径={prefabPath}，GUID={record.ActorGUID}");
                return false;
            }
            // SpawnPrefab 默认落 Scenes[0]（RootScene），恢复出的动态对象属玩法世界对象，搬入玩法场景
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.MoveToGameplayScene(spawnedActor, record.ActorName ?? prefabPath);

            spawnedActor.Transform = record.Transform;
            if (!string.IsNullOrEmpty(record.ActorName))
            {
                spawnedActor.Name = record.ActorName;
            }

            // 重生成的 Actor 属于动态 Actor，并记住其来源路径以便再次保存
            NotifyActorDynamicallySpawned(spawnedActor, prefabPath);

            var binding = NarrativeSavableActorResolver.Resolve(spawnedActor);
            if (binding != null)
            {
                binding.SetActorGUID(record.ActorGUID);
            }

            LoadActorFromRecord(spawnedActor, record);

            if (record.ActorGUID != Guid.Empty)
            {
                OnStableActorSpawned?.Invoke(spawnedActor, record.ActorGUID);
            }

            NarrativeLog.Log($"[SaveSystem] LoadDynamicRecord 生成 Actor {spawnedActor.Name} 成功，GUID={record.ActorGUID}");
            return true;
        }

        /// <summary>启用/禁用保存。对应 UE5 SetSavingDisabled。</summary>
        public void SetSavingDisabled(bool bShouldDisable)
        {
            _bSavingDisabled = bShouldDisable;
        }

        /// <summary>保存是否被禁用。对应 UE5 IsSavingDisabled。</summary>
        public bool IsSavingDisabled => _bSavingDisabled;

        /// <summary>是否为新游戏（尚未保存过）。对应 UE5 IsNewGame。</summary>
        public bool IsNewGame()
        {
            if (_narrativeSaveGame != null)
            {
                return _narrativeSaveGame.PlayerData == null || !_narrativeSaveGame.PlayerData.IsValid();
            }
            return true;
        }

        /// <summary>是否正在加载（含等待延迟还原玩家数据）。对应 UE5 IsLoading。</summary>
        public bool IsLoading()
        {
            return _bIsCurrentlyLoading || _bPendingPlayerDataLoad;
        }

        /// <summary>获取当前存档对象。对应 UE5 GetSaveObject。</summary>
        public NarrativeSave GetSaveObject()
        {
            return _narrativeSaveGame;
        }

        /// <summary>按 GUID 查找场景中的 Actor。对应 UE5 LookupActorByGUID。</summary>
        public Actor LookupActorByGUID(Guid searchGUID)
        {
            if (searchGUID == Guid.Empty) return null;
            if (_quickLookupMap.TryGetValue(searchGUID, out var weakRef))
            {
                if (weakRef.TryGetTarget(out var actor) && actor != null)
                {
                    return actor;
                }
                _quickLookupMap.Remove(searchGUID);
            }
            return null;
        }

        // ===== Actor 记录创建/还原 =====

        /// <summary>从 Actor 创建存档记录。对应 UE5 CreateActorRecord。</summary>
        public bool CreateActorRecord(Actor actor, NarrativeActorRecord actorRecord)
        {
            if (actor == null || actorRecord == null) return false;

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding == null)
            {
                // UE5：未实现接口的 Actor 不记录（玩家三元组之外的对象都需接口）
                return false;
            }

            actorRecord.ActorGUID = binding.GetActorGUID();
            if (actorRecord.ActorGUID == Guid.Empty)
            {
                return false;
            }
            actorRecord.bNeedsDynamicSpawn = binding.ShouldRespawn();

            NarrativeLog.Log($"[SaveSystem] 为 Actor {actor.Name} 创建记录，GUID={actorRecord.ActorGUID}");

            actorRecord.ActorName = actor.Name;

            // 仅可移动 Actor 保存 Transform（避免覆盖设计师调整）
            // Flax 中 Actor 的变换可在加载侧再判定，这里始终记录
            actorRecord.Transform = actor.Transform;

            // 动态重生需要 Prefab 资产路径（绝不写类型全名）。
            // 运行期注册表（NarrativeDynamicSpawnSourceResolver）是进程内存，全新启动时为空：
            // 重存解析不到路径时保留记录里已有的有效路径，绝不把已写好的路径抹空。
            string spawnPath = ResolveDynamicSpawnPath(actor, binding, actorRecord.bNeedsDynamicSpawn);
            if (string.IsNullOrEmpty(spawnPath) && !string.IsNullOrEmpty(actorRecord.ActorSoftClassPath))
            {
                spawnPath = actorRecord.ActorSoftClassPath;
            }
            actorRecord.ActorSoftClassPath = spawnPath;
            actorRecord.bNetStartup = IsNetStartupActor(actor);

            // 通知 Actor 与组件即将被保存，先让 PrepareForSave 产出数据，再统一归档
            // （顺序约束：Actor 先于组件，对应 UE5 NarrativeSaveSubsystem.cpp:714-741）
            binding.PrepareForSave();

            var capturedByActorState = new HashSet<System.Object>();
            var componentEntries = NarrativeSavableActorResolver.CollectSavableComponents(
                actor, binding, capturedByActorState, ShouldArchiveAllScripts());

            foreach (var entry in componentEntries)
            {
                if (entry.Value is INarrativeSavableComponent savableComp)
                {
                    savableComp.PrepareForSave();
                }
                else
                {
                    NarrativeReflect.TryInvokeVoid(entry.Value, "PrepareForSave");
                }
            }

            // Actor 自身状态：宿主对象 + 实现了 INarrativeSaveStateProvider 的对象
            actorRecord.ByteData = CaptureActorState(actor, binding, capturedByActorState);

            // 组件状态
            actorRecord.SavedComponents.Clear();
            foreach (var entry in componentEntries)
            {
                // 稳定身份：ComponentName 沿用旧键语义（老档仍能按名字定位）；
                // ComponentId 是确定性 slot id（与键同形，写读两侧同规则重算，跨进程重启成立）；
                // ComponentFingerprint 是载荷指纹，读档比对不符时拒绝应用字节（见 LoadActorFromRecord）。
                string slotId = NarrativeComponentKeyRegistry.GetOrCreateSlotId(entry.Value, entry.Key);

                // 组件无可归档状态时仍然登记，保证读档侧会调用其 Load() 钩子
                actorRecord.SavedComponents.Add(new NarrativeSaveComponent
                {
                    ComponentName = entry.Key,
                    ComponentId = slotId,
                    ComponentFingerprint = NarrativeSaveStateArchive.ComputeFingerprintForObject(entry.Value),
                    ByteData = NarrativeSaveStateArchive.CaptureSingle(entry.Key, entry.Value),
                });
            }

            return true;
        }

        /// <summary>
        /// 解析动态重生所需的 Prefab 资产路径。
        /// 非动态 Actor 允许为空；动态 Actor 缺失路径时记录告警（读档侧会明确报错而不是静默丢 Actor）。
        /// </summary>
        private string ResolveDynamicSpawnPath(Actor actor, NarrativeSavableActorBinding binding, bool bNeedsDynamicSpawn)
        {
            string path = NarrativeDynamicSpawnSourceResolver.Resolve(actor);

            if (string.IsNullOrEmpty(path) && bNeedsDynamicSpawn && !IsNetStartupActor(actor))
            {
                NarrativeLog.LogWarning(
                    $"[SaveSystem] Actor {actor.Name} 需要动态重生，但未找到 Prefab 资产路径。" +
                    $"可为其脚本添加 public string SavePrefabPath = \"Content/...\"; 或实现 INarrativeDynamicSpawnSource，" +
                    $"或调用 NarrativeDynamicSpawnSourceResolver.RegisterSpawnSource(actor, prefabPath)。");
            }

            return path ?? string.Empty;
        }

        /// <summary>
        /// Actor 状态字节：宿主对象（提供 GUID 的 Actor/Script）+ 其余实现 INarrativeSaveStateProvider 的脚本 + Actor 自身。
        /// 归档键优先取显式稳定键（[NarrativeSaveKey] / SaveComponentKey），未声明时沿用
        /// 按 GetScripts 顺序计数的 "类型全名" / "类型全名#1"，与读档侧
        /// NarrativeSaveStateArchive.ResolveKeyInActor 的计数方式严格一致，避免同类多实例串位。
        /// 注意：每条目标同时写入确定性 slot id（"i"，与键同形、跨重启可重算）与载荷指纹（"f"）。
        /// 读档侧按 id/键定位后用指纹校验，不一致拒绝应用 —— 脚本增删导致的序号挪位不再串档。
        /// </summary>
        private byte[] CaptureActorState(Actor actor, NarrativeSavableActorBinding binding, HashSet<System.Object> consumed)
        {
            var targets = new List<NarrativeStateTarget>();
            var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            if (actor is INarrativeSaveStateProvider)
            {
                targets.Add(new NarrativeStateTarget(NarrativeSaveStateArchive.GetKeyForObject(actor), actor));
                consumed?.Add(actor);
            }

            foreach (var script in NarrativeSavableActorResolver.GetScripts(actor))
            {
                if (script == null) continue;

                // 位置化键计数收敛在 NarrativeSaveComponentKeys.NextKey —— 与读档侧
                // NarrativeSaveStateArchive.ResolveKeyInActor 的按序号选中是同一份实现。
                string baseKey = NarrativeSaveStateArchive.GetKeyForObject(script);
                string positionalKey = NarrativeSaveComponentKeys.NextKey(baseKey, keyCounts);

                bool isBindingTarget = binding != null && ReferenceEquals(script, binding.Target);
                if (!isBindingTarget)
                {
                    // 组件段已捕获的对象不重复写入 Actor 状态段
                    if (consumed != null && consumed.Contains(script)) continue;
                    if (!(script is INarrativeSaveStateProvider)) continue;
                }

                targets.Add(new NarrativeStateTarget(positionalKey, script));
                consumed?.Add(script);
            }

            return NarrativeSaveStateArchive.Capture(targets);
        }

        private static bool ShouldArchiveAllScripts()
        {
            var settings = SaveSystemDeveloperSettings.Instance;
            return settings != null && settings.bArchiveAllSavableScripts;
        }

        /// <summary>从存档记录还原 Actor 状态。对应 UE5 LoadActorFromRecord。</summary>
        public void LoadActorFromRecord(Actor actor, NarrativeActorRecord actorRecord)
        {
            if (actor == null || actorRecord == null) return;

            // net startup Actor 且记录标记销毁，则销毁当前 Actor
            if (actorRecord.bNetStartup && actorRecord.bDestroyed)
            {
                NarrativeLog.Log($"[SaveSystem] 从记录销毁 net startup Actor {actor.Name}，GUID={actorRecord.ActorGUID}");
                DestroyActor(actor);
                return;
            }

            // 应用 Transform（非 Identity 时）
            if (!IsIdentityTransform(actorRecord.Transform))
            {
                actor.Transform = actorRecord.Transform;
            }

            NarrativeLog.Log($"[SaveSystem] 从记录加载 Actor {actor.Name}，GUID={actorRecord.ActorGUID}");

            var resolver = NarrativeSaveStateArchive.MakeActorTargetResolver(actor);
            var scopeValidator = new Func<System.Object, bool>(obj => NarrativeSaveStateArchive.IsInActorScopeOf(obj, actor));

            // 还原 Actor 状态（provider 与非 provider 都能还原）
            NarrativeSaveStateArchive.Apply(actorRecord.ByteData, resolver, scopeValidator);

            var binding = NarrativeSavableActorResolver.Resolve(actor);
            if (binding != null)
            {
                if (actorRecord.ActorGUID != Guid.Empty)
                {
                    binding.SetActorGUID(actorRecord.ActorGUID);
                }
            }

            // 通知 Actor 已加载 —— 但必须在其组件之后：见下方 UE5 顺序约束。

            // ===== 顺序约束（对应 UE5 NarrativeSaveSubsystem.cpp:790-803）=====
            // 应用顺序：Actor 状态段（上面已应用 ByteData）→ 逐组件状态段；
            // 通知顺序：逐组件 Load() → Actor 的 Load()。
            // 之前这里是「先 binding.Load() 再还原组件」，Actor 在自己的组件仍是默认值时就被通知“已加载”，
            // 任何依赖组件已还原状态的逻辑都会静默拿到旧值。
            var restoredComponents = new List<System.Object>();
            var claimedComponents = new List<System.Object>();

            if (actorRecord.SavedComponents != null)
            {
                foreach (var savedComp in actorRecord.SavedComponents)
                {
                    if (savedComp == null) continue;
                    if (string.IsNullOrEmpty(savedComp.ComponentName) && string.IsNullOrEmpty(savedComp.ComponentId)) continue;

                    var component = ResolveSavedComponent(savedComp, actor, resolver, claimedComponents);
                    if (component == null)
                    {
                        NarrativeLog.LogWarning(
                            $"[SaveSystem] 未在 {actor.Name} 上找到组件 {savedComp.ComponentName}（id={savedComp.ComponentId}），跳过。");
                        continue;
                    }

                    // 载荷指纹闸门：新档带指纹且与定位到的组件不符 → 拒绝应用字节与该组件的 Load() 通知，
                    // 告警点名组件并保留其默认值（指纹为空的老档照常还原，下次保存补写）。
                    if (!NarrativeSaveStateArchive.FingerprintMatches(component, savedComp.ComponentFingerprint))
                    {
                        NarrativeLog.LogWarning(
                            $"[SaveSystem] 组件 {actor.Name}/{savedComp.ComponentName}（id={savedComp.ComponentId}）" +
                            "载荷指纹与定位对象不一致，拒绝应用存档字节，保留组件默认值");
                        continue;
                    }

                    claimedComponents.Add(component);

                    if (savedComp.ByteData != null && savedComp.ByteData.Length > 0)
                    {
                        // 组件记录内可能引用同一 Actor 作用域内的其它对象（例如状态提供者组件聚合的载荷）
                        NarrativeSaveStateArchive.Apply(savedComp.ByteData, key =>
                        {
                            if (string.Equals(key, savedComp.ComponentName, StringComparison.Ordinal)) return component;
                            if (!string.IsNullOrEmpty(savedComp.ComponentId)
                                && string.Equals(key, savedComp.ComponentId, StringComparison.Ordinal)) return component;
                            var resolved = resolver(key);
                            return resolved;
                        }, scopeValidator);
                    }

                    restoredComponents.Add(component);
                }
            }

            // 组件先于 Actor 收到 Load()
            foreach (var component in restoredComponents)
            {
                if (component is INarrativeSavableComponent savableComp)
                {
                    savableComp.Load();
                }
                else
                {
                    NarrativeReflect.TryInvokeVoid(component, "Load");
                }
            }

            binding?.Load();
        }

        /// <summary>
        /// 按记录定位运行期组件：slot id 优先（会话内注册表命中时，脚本中途重排也找回原对象），
        /// 名字键兜底。id 为确定性键（与名字同形）时，全新进程下兜底的名字定位本身就是确定性还原，
        /// 身份不再依赖进程内存；旧版 GUID 形态的 id 在新会话解析不到时同样走该通路。
        /// 用名字定位成功时把记录里的 id 绑到该对象，此后该会话的保存不再受枚举顺序影响。
        /// 同一条 Actor 记录内的多个组件不允许定位到同一个对象（那正是串档的表现），已被占用的候选会被放弃。
        /// 定位结果还要过 LoadActorFromRecord 处的载荷指纹闸门，不匹配则拒绝应用。
        /// </summary>
        private static System.Object ResolveSavedComponent(
            NarrativeSaveComponent savedComp,
            Actor actor,
            Func<string, System.Object> resolver,
            List<System.Object> claimed)
        {
            System.Object byName = null;
            if (!string.IsNullOrEmpty(savedComp.ComponentName))
            {
                byName = resolver(savedComp.ComponentName);
                if (byName != null && IsAlreadyClaimed(claimed, byName)) byName = null;
            }

            if (string.IsNullOrEmpty(savedComp.ComponentId)) return byName;

            // 注册表是进程全局的：确定性 id 在不同 Actor 的同型脚本间可能同形，
            // 命中对象必须属于本 Actor 作用域，否则视为 miss（回到确定性键定位）。
            var byId = NarrativeComponentKeyRegistry.Resolve(savedComp.ComponentId);
            if (byId != null && !NarrativeSaveStateArchive.IsInActorScopeOf(byId, actor)) byId = null;
            if (byId != null && IsAlreadyClaimed(claimed, byId)) byId = null;

            if (byId != null)
            {
                if (byName != null && !ReferenceEquals(byName, byId))
                {
                    NarrativeLog.LogWarning(
                        $"[SaveSystem] 组件记录 {savedComp.ComponentName} 的名字定位与 slot id 定位不一致，按 id 还原（脚本顺序变化已被阻止）");
                }
                return byId;
            }

            if (byName != null)
            {
                NarrativeComponentKeyRegistry.Bind(byName, savedComp.ComponentId);
            }
            return byName;
        }

        private static bool IsAlreadyClaimed(List<System.Object> claimed, System.Object candidate)
        {
            if (claimed == null || candidate == null) return false;
            for (int i = 0; i < claimed.Count; i++)
            {
                if (ReferenceEquals(claimed[i], candidate)) return true;
            }
            return false;
        }

        // ===== 初始化 =====

        /// <summary>初始化存档系统。对应 UE5 InitializeSaveSystem（由 GameMode::InitGame 调用）。</summary>
        public void InitializeSaveSystem()
        {
            if (_bSaveSystemInitialized) return;
            _bSaveSystemInitialized = true;

            // 重建查找表，并把此刻场景中已存在的 Actor 记为 net startup
            RebuildLookupMap();
            TakeNetStartupSnapshot();

            _bPlayerDataLoaded = false;
            _bPendingPlayerDataLoad = false;
            _autosaveTimer = 0f;

            var settings = SaveSystemDeveloperSettings.Instance;
            string slotString = string.Empty;
            int slot = 0;

            if (!string.IsNullOrEmpty(_optionString))
            {
                slotString = ParseOption(_optionString, "SaveGameName");
                _bIsLevelTransition = ParseOption(_optionString, "LevelTransition").Length > 0;
            }

            if (string.IsNullOrEmpty(slotString) && settings != null && settings.bAutoLoadDefaultSlot)
            {
                // 没有 GameMode URL 时，仅当存档里的 LevelName 与当前关卡一致才续读默认槽。
                // 对应 UE5 SaveSystemDeveloperSettings::bAutoLoadFirstSaveInEditor 的 LevelName 比对
                // （NarrativeSaveSubsystem.cpp:854-872），避免登录/菜单场景把玩法 Actor 重生进来。
                string candidateName = settings.DefaultSaveName;
                int candidateSlot = settings.DefaultSaveSlot;

                Scene currentScene = ResolveContextScene();
                string currentLevelName = currentScene != null ? currentScene.Name : string.Empty;

                if (!string.IsNullOrEmpty(candidateName)
                    && DoesSaveGameExist(candidateName, candidateSlot)
                    && string.Equals(PeekSavedLevelName(candidateName, candidateSlot), currentLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    slotString = candidateName;
                    slot = candidateSlot;
                }
            }

            if (!string.IsNullOrEmpty(slotString) && DoesSaveGameExist(slotString, slot))
            {
                // 关卡切换时加载，但角色 Transform 由新关卡决定
                Load(slotString, slot);
            }
            else
            {
                // 新游戏：仅创建空存档对象
                _bIsLevelTransition = false;
                UpdateSaveObject(true);
            }

            _bInitialized = true;
        }

        /// <summary>重置初始化标记（外部要求重新走一遍初始化流程时调用）。</summary>
        public void ResetInitialization()
        {
            _bSaveSystemInitialized = false;
        }

        /// <summary>
        /// 只读取存档文件中的 LevelName，用于判断存档是否属于当前关卡（不反序列化整个存档对象）。
        /// </summary>
        private static string PeekSavedLevelName(string saveName, int slot)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName, slot);
                if (!File.Exists(filePath)) return string.Empty;

                using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("LevelName", out var levelNameEl)
                    && levelNameEl.ValueKind == JsonValueKind.String)
                {
                    return levelNameEl.GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 读取存档关卡名失败 {saveName}_{slot}: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>设置 GameMode 选项字符串。对应 UE5 中读取 GM->OptionsString。</summary>
        public void SetOptionString(string optionString)
        {
            _optionString = optionString ?? string.Empty;

            if (!string.IsNullOrEmpty(_optionString))
            {
                _bIsLevelTransition = ParseOption(_optionString, "LevelTransition").Length > 0;
            }
        }

        // ===== 内部辅助 =====

        /// <summary>创建存档对象。对应 UE5 GetSaveGameClass() + CreateSaveGameObject。</summary>
        private NarrativeSave CreateSaveObject()
        {
            var saveClass = SaveSystemDeveloperSettings.ResolveSaveGameClass();
            if (saveClass == null || saveClass == typeof(NarrativeSave))
            {
                return new NarrativeSave();
            }

            try
            {
                if (Activator.CreateInstance(saveClass) is NarrativeSave customSave)
                {
                    NarrativeLog.Log($"[SaveSystem] 使用配置的存档类型 {saveClass.FullName}");
                    return customSave;
                }
                NarrativeLog.LogWarning($"[SaveSystem] 存档类型 {saveClass.FullName} 实例化结果不可用，回退 NarrativeSave");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 存档类型 {saveClass.FullName} 实例化失败: {ex.Message}");
            }

            return new NarrativeSave();
        }

        /// <summary>
        /// 判断 Actor 是否为 net startup（关卡内置，随场景文件一起加载）。
        /// UE5 语义：AActor::IsNetStartupActor 表示该 Actor 由关卡（地图）在启动时创建；
        /// 运行期由代码 Spawn 出来的 Actor 为 false。
        /// Flax 等价：以存档系统初始化时的场景快照为准，快照内存在即内置；
        /// 快照之后出现的 Actor（Prefab 实例化、AddScript 生成、本子系统重生的动态 Actor）一律为动态。
        /// </summary>
        private bool IsNetStartupActor(Actor actor)
        {
            if (actor == null) return false;
            if (actor == _hostActor) return false;

            EnsureNetStartupSnapshot();

            if (_dynamicSpawnedActorIds.Contains(actor.ID)) return false;
            if (_netStartupActorIds.Contains(actor.ID)) return true;

            // 快照建立之后才出现的 Actor：运行期生成
            return false;
        }

        /// <summary>判断 Transform 是否为 Identity（近似）。</summary>
        private bool IsIdentityTransform(Transform t)
        {
            const float eps = 1e-5f;
            // Quaternion.Angle 在 Flax 中是属性，使用点积计算角度差
            float dot = (float)Math.Clamp(Quaternion.Dot(t.Orientation, Quaternion.Identity), -1f, 1f);
            float angleRad = (float)Math.Acos(dot);
            return Vector3.DistanceSquared(t.Translation, Vector3.Zero) < eps
                && angleRad < eps
                && Math.Abs(t.Scale.X - 1f) < eps
                && Math.Abs(t.Scale.Y - 1f) < eps
                && Math.Abs(t.Scale.Z - 1f) < eps;
        }

        /// <summary>销毁 Actor。Flax 中通过静态方法 Actor.Destroy(actor) 销毁。</summary>
        private void DestroyActor(Actor actor)
        {
            if (actor != null)
            {
                OnActorDestroyed(actor);
                NarrativeDynamicSpawnSourceResolver.UnregisterSpawnSource(actor);
                Actor.Destroy(actor);
            }
        }

        /// <summary>从选项字符串解析键值。对应 UE5 UGameplayStatics::ParseOption。</summary>
        private static string ParseOption(string options, string key)
        {
            if (string.IsNullOrEmpty(options) || string.IsNullOrEmpty(key)) return string.Empty;
            var parts = options.Split('?');
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    string k = part.Substring(0, eq);
                    string v = part.Substring(eq + 1);
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return v;
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>未显式指定存档名/槽时，套用开发者设置里的默认值。</summary>
        private static void ApplyDefaultSlot(ref string saveName, ref int slot)
        {
            var settings = SaveSystemDeveloperSettings.Instance;
            if (settings == null) return;

            if (string.IsNullOrEmpty(saveName) || string.Equals(saveName, "NarrativeSave", StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(settings.DefaultSaveName)) saveName = settings.DefaultSaveName;
                if (slot == 0) slot = settings.DefaultSaveSlot;
            }
        }

        // ===== 磁盘 IO（替代 UE5 UGameplayStatics::SaveGameToSlot 等） =====

        /// <summary>存档目录。由 SaveSystemDeveloperSettings 决定（可被 SaveDirectoryOverride 覆盖）。</summary>
        private static string SaveDirectory => SaveSystemDeveloperSettings.SaveDirectory();

        /// <summary>获取存档文件完整路径。</summary>
        private static string GetSaveFilePath(string saveName, int slot)
        {
            string dir = SaveDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string safeName = string.Empty;
            foreach (var ch in (saveName ?? "NarrativeSave"))
            {
                safeName += char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
            }
            return Path.Combine(dir, $"{safeName}_{slot}.json");
        }

        /// <summary>判断存档文件是否存在。对应 UE5 UGameplayStatics::DoesSaveGameExist。</summary>
        private static bool DoesSaveGameExist(string saveName, int slot)
        {
            if (string.IsNullOrEmpty(saveName)) return false;
            return File.Exists(GetSaveFilePath(saveName, slot));
        }

        /// <summary>写入存档到磁盘。对应 UE5 UGameplayStatics::SaveGameToSlot。</summary>
        private static bool SaveGameToSlot(NarrativeSave save, string saveName, int slot)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName, slot);
                // 以 object 声明类型序列化，保证开发者设置指定的存档子类字段也能落盘
                string json = JsonSerializer.Serialize((System.Object)save, _jsonOptions);
                File.WriteAllText(filePath, json);
                NarrativeLog.Log($"[SaveSystem] 存档已写入 {filePath}（记录 {save.RecordMap?.Count ?? 0} 条）");
                return true;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 写入存档失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>从磁盘加载存档。对应 UE5 UGameplayStatics::LoadGameFromSlot。</summary>
        private static NarrativeSave LoadGameFromSlot(string saveName, int slot)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName, slot);
                if (!File.Exists(filePath)) return null;
                string json = File.ReadAllText(filePath);

                var saveClass = SaveSystemDeveloperSettings.ResolveSaveGameClass();
                var save = JsonSerializer.Deserialize(json, saveClass, _jsonOptions) as NarrativeSave;
                if (save != null)
                {
                    save.OnSerialize(true);
                }
                NarrativeLog.Log($"[SaveSystem] 存档已读取 {filePath}（记录 {save?.RecordMap?.Count ?? 0} 条）");
                return save;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 读取存档失败: {ex.Message}");
                return null;
            }
        }
    }
}

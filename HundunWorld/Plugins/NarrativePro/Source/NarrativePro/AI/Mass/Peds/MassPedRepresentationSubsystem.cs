using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人表现子系统。对应 UE5 <c>UMassPedRepresentationSubsystem</c>
    /// （<c>Private/AI/Mass/Peds/MassPedRepresentationSubsystem.cpp:11-19</c>）。
    /// <para>
    /// UE5 只做两件事：Initialize 里先初始化 <c>UMassPedSpawnerSubsystem</c> 并把 Actor 生成子系统指向它；
    /// 其余由基类 <c>UMassRepresentationSubsystem</c> 提供——即「可视化描述句柄表」+「按 LOD 复用 Actor」。
    /// Flax 没有 Mass，所以本类同时承担基类那部分职责：
    /// <see cref="FindOrAddStaticMeshDesc"/> 的句柄表、LOD 档位与 Actor 池、以及生成请求的派发。
    /// </para>
    /// </summary>
    public class MassPedRepresentationSubsystem
    {
        private static MassPedRepresentationSubsystem _instance;

        /// <summary>单例。对应 UE5 <c>UWorld::GetSubsystem&lt;UMassPedRepresentationSubsystem&gt;</c>。</summary>
        public static MassPedRepresentationSubsystem Get()
        {
            if (_instance == null)
            {
                _instance = new MassPedRepresentationSubsystem();
                _instance.Initialize();
            }
            return _instance;
        }

        /// <summary>已登记的可视化描述表。对应 UE5 <c>MeshDescriptions</c>。</summary>
        private readonly List<StaticMeshInstanceVisualizationDesc> _meshDescriptions = new List<StaticMeshInstanceVisualizationDesc>();

        /// <summary>描述签名 → 句柄，避免同一网格重复注册。</summary>
        private readonly Dictionary<string, int> _descIndexBySignature = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>实体 → 表现 Actor。对应 UE5 <c>FMassActorFragment</c> 的托管侧账本。</summary>
        private readonly Dictionary<MassEntityHandle, Actor> _entityActors = new Dictionary<MassEntityHandle, Actor>();

        /// <summary>可复用的空闲 Actor 池（按定义路径分组）。对应 UE5 的 representation pool。</summary>
        private readonly Dictionary<string, List<Actor>> _actorPool = new Dictionary<string, List<Actor>>(StringComparer.Ordinal);

        /// <summary>Actor 生成子系统。对应 UE5 <c>ActorSpawnerSubsystem</c>。</summary>
        public MassPedSpawnerSubsystem ActorSpawnerSubsystem { get; private set; }

        /// <summary>LOD 成本控制参数。对应 <c>EntityLOD.h</c> 档位之上的配额约束。</summary>
        public EntityLODCostControl LodCostControl = new EntityLODCostControl();

        /// <summary>当前高精度 Actor 数。</summary>
        public int CurrentHighLodCount { get; private set; }

        /// <summary>当前中精度 Actor 数。</summary>
        public int CurrentMediumLodCount { get; private set; }

        /// <summary>
        /// 初始化。对应 UE5 <c>Initialize(FSubsystemCollectionBase&amp;)</c>：
        /// 先初始化依赖（Actor 生成子系统），再把 <c>ActorSpawnerSubsystem</c> 指向它。
        /// </summary>
        public virtual void Initialize()
        {
            ActorSpawnerSubsystem = MassPedSpawnerSubsystem.Get();
        }

        /// <summary>释放单例（切场景时用）。</summary>
        public static void Shutdown()
        {
            _instance?.ReleaseAll();
            _instance = null;
        }

        /// <summary>
        /// 登记可视化描述并返回句柄；同签名复用。
        /// 对应 UE5 <c>UMassRepresentationSubsystem::FindOrAddStaticMeshDesc</c>
        /// （<c>NarrativeVisualizationTrait.cpp:73</c> 调用点）。
        /// </summary>
        public virtual int FindOrAddStaticMeshDesc(StaticMeshInstanceVisualizationDesc desc)
        {
            if (desc == null) return -1;

            string signature = desc.GetSignature();
            if (_descIndexBySignature.TryGetValue(signature, out int existing))
            {
                return existing;
            }

            int handle = _meshDescriptions.Count;
            _meshDescriptions.Add(desc);
            _descIndexBySignature[signature] = handle;
            return handle;
        }

        /// <summary>按句柄取可视化描述。</summary>
        public StaticMeshInstanceVisualizationDesc GetStaticMeshDesc(int handle)
        {
            if (handle < 0 || handle >= _meshDescriptions.Count) return null;
            return _meshDescriptions[handle];
        }

        /// <summary>已注册的描述数量。</summary>
        public int MeshDescriptionCount => _meshDescriptions.Count;

        /// <summary>
        /// 被管理的实体：更新 LOD 档位并按档位申请/释放 Actor。
        /// 对应 UE5 <c>UMassRepresentationSubsystem</c> 的 LOD 更新 + Actor 复用行为。
        /// </summary>
        public virtual void UpdateEntityRepresentation(NarrativePedEntityManager entityManager, MassEntityHandle entity, Vector3 observerLocation)
        {
            if (entityManager == null) return;
            if (!entityManager.TryGetFragmentData<PedRepresentationFragment>(entity, out var representation)) return;
            if (!entityManager.TryGetFragmentData<PedTransformFragment>(entity, out var transform)) return;

            float distance = Vector3.Distance(transform.Location, observerLocation);
            EEntityLOD desired = LodCostControl.GetLodForDistance(distance);
            desired = LodCostControl.ApplyBudget(desired, CurrentHighLodCount, CurrentMediumLodCount);

            representation.PreviousLOD = representation.CurrentLOD;
            representation.CurrentLOD = desired;

            if (desired == EEntityLOD.High || desired == EEntityLOD.Medium)
            {
                EnsureActor(entityManager, entity, transform, representation);
            }
            else
            {
                // Low/Off：释放 Actor。对应 UE5 Low 档 + AutoDestroy 标签时的回收
                ReleaseActor(entityManager, entity, representation);
            }
        }

        /// <summary>取实体当前的表现 Actor。对应 UE5 <c>FMassActorFragment::GetMutable</c>。</summary>
        public Actor GetActorForEntity(MassEntityHandle entity)
        {
            return _entityActors.TryGetValue(entity, out var actor) ? actor : null;
        }

        private void EnsureActor(NarrativePedEntityManager entityManager, MassEntityHandle entity,
            PedTransformFragment transform, PedRepresentationFragment representation)
        {
            _entityActors.TryGetValue(entity, out var existing);

            string definitionPath = ResolveDefinitionPath(entityManager, entity);
            if (string.IsNullOrEmpty(definitionPath)) return;

            if (existing != null)
            {
                // 已有 Actor：只搬位置并恢复启用（LOD 在 Off→High 之间切换时会被停用）
                existing.Position = transform.Location;
                existing.Orientation = transform.Rotation;
                ActorManagement().SetActorEnabled(EMassActorEnabledType.Fully, existing, entity.Index, entityManager);
                return;
            }

            var request = new PedActorSpawnRequest
            {
                MassAgent = entity,
                Transform = transform.GetTransform(),
                Guid = StableGuidFor(entity)
            };

            var spawnSubsystem = ActorSpawnerSubsystem ?? MassPedSpawnerSubsystem.Get();
            var spawned = spawnSubsystem.RequestActor(entityManager, request);
            if (spawned == null) return;

            _entityActors[entity] = spawned;
            representation.bHasActor = true;
            CountLod(representation.CurrentLOD, 1);

            // 对应 UE5 OnPostActorSpawn：把种子与定义回写到角色
            ActorManagement().OnPostActorSpawn(request, entityManager);
        }

        private void ReleaseActor(NarrativePedEntityManager entityManager, MassEntityHandle entity, PedRepresentationFragment representation)
        {
            if (!_entityActors.TryGetValue(entity, out var actor) || actor == null)
            {
                representation.bHasActor = false;
                return;
            }

            CountLod(representation.CurrentLOD, -1);
            _entityActors.Remove(entity);
            representation.bHasActor = false;

            if (representation.bAutoDestroy)
            {
                // 对应 UE5 AutoDestroyProcessor：低档且带自动销毁标记的实体其 Actor 直接销毁
                DestroyActorCompletely(actor);
            }
            else
            {
                // 否则停用并回收进池子等待复用
                ActorManagement().SetActorEnabled(EMassActorEnabledType.Disabled, actor, entity.Index, entityManager);
                string path = ActorPathOf(actor);
                if (!string.IsNullOrEmpty(path))
                {
                    if (!_actorPool.TryGetValue(path, out var list))
                    {
                        list = new List<Actor>();
                        _actorPool[path] = list;
                    }
                    if (list.Count < MaxPooledPerDefinition)
                    {
                        list.Add(actor);
                    }
                    else
                    {
                        DestroyActorCompletely(actor);
                    }
                }
                else
                {
                    DestroyActorCompletely(actor);
                }
            }
        }

        /// <summary>每个定义最多缓存的空闲 Actor 数。</summary>
        public int MaxPooledPerDefinition = 8;

        private Actor AcquirePooledActor(string definitionPath, Transform spawnTransform)
        {
            if (!_actorPool.TryGetValue(definitionPath, out var list) || list.Count == 0) return null;
            var actor = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            if (actor == null) return null;
            actor.Position = spawnTransform.Translation;
            actor.Orientation = spawnTransform.Orientation;
            return actor;
        }

        internal Actor TryAcquirePooled(string definitionPath, Transform spawnTransform) => AcquirePooledActor(definitionPath, spawnTransform);

        internal void RegisterActorPath(Actor actor, string definitionPath)
        {
            if (actor == null || string.IsNullOrEmpty(definitionPath)) return;
            ActorPathMap[actor] = definitionPath;
        }

        private readonly Dictionary<Actor, string> ActorPathMap = new Dictionary<Actor, string>();

        private string ActorPathOf(Actor actor)
        {
            return actor != null && ActorPathMap.TryGetValue(actor, out var path) ? path : null;
        }

        private static void DestroyActorCompletely(Actor actor)
        {
            if (actor == null) return;
            try
            {
                FlaxEngine.Object.Destroy(actor);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"销毁行人表现 Actor 失败：{ex.Message}");
            }
        }

        private string ResolveDefinitionPath(NarrativePedEntityManager entityManager, MassEntityHandle entity)
        {
            var properties = entityManager.GetConstSharedFragmentData<NarrativePedProperties>(entity);
            var pedFragment = entityManager.GetFragmentData<NarrativePedFragment>(entity);
            if (properties == null || pedFragment == null) return null;
            if (pedFragment.NarrativePedIndex < 0 || pedFragment.NarrativePedIndex >= properties.NarrativePedPaths.Count) return null;
            return properties.NarrativePedPaths[pedFragment.NarrativePedIndex];
        }

        /// <summary>
        /// 由实体句柄派生稳定 GUID。对应 UE5 用 <c>FSpawnRequest.Guid</c> 复用同名 Actor 的做法
        /// （<c>MassPedSpawnerSubsystem.cpp:22-36</c>）。
        /// </summary>
        private static Guid StableGuidFor(MassEntityHandle entity)
        {
            unchecked
            {
                byte[] bytes = new byte[16];
                int a = entity.Index;
                int b = entity.SerialNumber;
                for (int i = 0; i < 4; i++)
                {
                    bytes[i] = (byte)(a >> (i * 8));
                    bytes[4 + i] = (byte)(b >> (i * 8));
                    bytes[8 + i] = (byte)(0x50 << i);
                    bytes[12 + i] = (byte)(0x42 << i);
                }
                return new Guid(bytes);
            }
        }

        private MassNarrativePedRepresentationActorManagement ActorManagement()
        {
            return _actorManagement ??= new MassNarrativePedRepresentationActorManagement();
        }

        private MassNarrativePedRepresentationActorManagement _actorManagement;

        private void CountLod(EEntityLOD lod, int delta)
        {
            if (lod == EEntityLOD.High)
            {
                CurrentHighLodCount = Mathf.Max(0, CurrentHighLodCount + delta);
            }
            else if (lod == EEntityLOD.Medium)
            {
                CurrentMediumLodCount = Mathf.Max(0, CurrentMediumLodCount + delta);
            }
        }

        /// <summary>清空全部表现 Actor 与池。对应 UE5 子系统销毁。</summary>
        public virtual void ReleaseAll()
        {
            foreach (var kvp in _entityActors)
            {
                if (kvp.Value != null) DestroyActorCompletely(kvp.Value);
            }
            _entityActors.Clear();

            foreach (var list in _actorPool.Values)
            {
                foreach (var actor in list)
                {
                    if (actor != null) DestroyActorCompletely(actor);
                }
            }
            _actorPool.Clear();
            ActorPathMap.Clear();
            CurrentHighLodCount = 0;
            CurrentMediumLodCount = 0;
        }

        /// <summary>当前持有 Actor 的实体数（诊断用）。</summary>
        public int ManagedActorCount => _entityActors.Count;
    }
}

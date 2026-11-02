using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass
{
    /// <summary>
    /// 实体 LOD。对应 UE5 <c>EEntityLOD</c>（<c>Public/AI/Mass/EntityLOD.h:8</c>）。
    /// <para>
    /// UE5 由 Mass 的 LOD 管理器给出每个实体的 LOD 档位，<c>ArsenalStatics.cpp:1254 GetEntityLOD</c>
    /// 把它读回给 Agent 组件；Low 档 + AutoDestroy 标签的实体会被回收
    /// （<c>Vehicles/Mass/AutoDestroyProcessor</c> 的同一语义）。
    /// </para>
    /// </summary>
    public enum EEntityLOD : byte
    {
        /// <summary>高精度：生成完整 Actor。</summary>
        High = 0,
        /// <summary>中精度：Actor 降级（关闭碰撞/子 Actor）。</summary>
        Medium = 1,
        /// <summary>低精度：不保留 Actor，可被自动销毁。</summary>
        Low = 2,
        /// <summary>不表现。</summary>
        Off = 3,
        /// <summary>档位数量。</summary>
        Max = 4
    }

    /// <summary>
    /// LOD 代价控制器。对应 UE5 由 <c>UMassRepresentationSubsystem</c> / LOD 管理器承担的
    /// 「同屏高精度实体数量上限」职责（EntityLOD.h 的档位在这里落地为可执行的成本约束）。
    /// </summary>
    [Serializable]
    public class EntityLODCostControl
    {
        /// <summary>允许同时存在的高精度 Actor 数。</summary>
        public int MaxHighLodEntities = 24;

        /// <summary>允许同时存在的中精度 Actor 数。</summary>
        public int MaxMediumLodEntities = 64;

        /// <summary>高精度半径（世界单位）。</summary>
        public float HighLodRadius = 3000f;

        /// <summary>中精度半径。</summary>
        public float MediumLodRadius = 8000f;

        /// <summary>不表现半径；超出即 Off/Low，Actor 回收。</summary>
        public float CullRadius = 14000f;

        /// <summary>
        /// 按距离给出候选档位。对应 UE5 LOD 管理器依据距离分档的行为。
        /// </summary>
        public EEntityLOD GetLodForDistance(float distance)
        {
            if (distance <= HighLodRadius) return EEntityLOD.High;
            if (distance <= MediumLodRadius) return EEntityLOD.Medium;
            if (distance <= CullRadius) return EEntityLOD.Low;
            return EEntityLOD.Off;
        }

        /// <summary>
        /// 在预算内把候选档位降级：高/中档 Actor 数量已达上限时退到下一档。
        /// 对应「LOD cost control」——距离优先排序后按配额放行。
        /// </summary>
        public EEntityLOD ApplyBudget(EEntityLOD desired, int currentHighCount, int currentMediumCount)
        {
            if (desired == EEntityLOD.High && currentHighCount >= MaxHighLodEntities)
            {
                desired = EEntityLOD.Medium;
            }
            if (desired == EEntityLOD.Medium && currentMediumCount >= MaxMediumLodEntities)
            {
                desired = EEntityLOD.Low;
            }
            return desired;
        }
    }

    /// <summary>
    /// Mass 片段基类。对应 UE5 <c>FMassFragment</c>。
    /// Flax 无 Mass，本移植以「每实体一个片段字典」的托管实现代替 SoA 存储，
    /// 保持片段即数据、处理器即系统的可观测语义。
    /// </summary>
    [Serializable]
    public abstract class PedFragment { }

    /// <summary>Mass 常量共享片段基类。对应 UE5 <c>FMassConstSharedFragment</c>。</summary>
    [Serializable]
    public abstract class PedConstSharedFragment { }

    /// <summary>Mass 标签基类（零数据，仅作存在性判定）。对应 UE5 <c>FMassTag</c>。</summary>
    [Serializable]
    public abstract class PedTag { }

    /// <summary>
    /// 实体变换片段。对应 UE5 <c>FTransformFragment</c>。
    /// </summary>
    [Serializable]
    public class PedTransformFragment : PedFragment
    {
        public Vector3 Location = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Identity;

        public Vector3 GetForwardVector() => Vector3.Transform(Vector3.Forward, Rotation);

        public Transform GetTransform() => new Transform(Location, Rotation);

        public void SetTransform(Transform value)
        {
            Location = value.Translation;
            Rotation = value.Orientation;
        }
    }

    /// <summary>实体速度片段。对应 UE5 <c>FMassVelocityFragment</c>。</summary>
    [Serializable]
    public class PedVelocityFragment : PedFragment
    {
        public Vector3 Value = Vector3.Zero;

        public float Speed => Value.Length;
    }

    /// <summary>Agent 半径片段。对应 UE5 <c>FAgentRadiusFragment</c>。</summary>
    [Serializable]
    public class AgentRadiusFragment : PedFragment
    {
        public float Radius = 40f;
    }

    /// <summary>
    /// 实体 Actor 片段。对应 UE5 <c>FMassActorFragment</c>：持有该实体当前的表现 Actor。
    /// 只有拥有此片段且有效的实体才会参与「车内乘客忽略」一类的 Actor 级判定。
    /// </summary>
    [Serializable]
    public class PedActorFragment : PedFragment
    {
        [NonSerialized]
        public Actor SpawnedActor;

        public bool IsValid() => SpawnedActor != null;
    }

    /// <summary>
    /// 实体生成请求。对应 UE5 <c>FMassActorSpawnRequest</c> 的可观测子集。
    /// </summary>
    public class PedActorSpawnRequest
    {
        /// <summary>请求生成实体的模板名。对应 UE5 Template。</summary>
        public string TemplateName = "";

        /// <summary>请求实体。</summary>
        public MassEntityHandle MassAgent;

        /// <summary>用于命名/复用的稳定标识。对应 UE5 Guid。</summary>
        public Guid Guid = Guid.NewGuid();

        /// <summary>生成变换。</summary>
        public Transform Transform;

        /// <summary>生成结果 Actor。对应 UE5 SpawnedActor。</summary>
        public Actor SpawnedActor;

        /// <summary>是否已命名（对应 UE5 `SpawnedActorMap` 命中路径）。</summary>
        public bool bFoundExisting = false;

        public PedActorSpawnRequest() { }
    }

    /// <summary>生成请求状态。对应 UE5 <c>ESpawnRequestStatus</c>。</summary>
    public enum ESpawnRequestStatus
    {
        Succeeded = 0,
        Failed = 1,
        TryAgain = 2
    }

    /// <summary>
    /// 实体模板构建上下文。对应 UE5 <c>FMassEntityTemplateBuildContext</c>。
    /// 特质（<see cref="PedEntityTraitBase"/>）通过它往实体上挂片段/共享片段/标签。
    /// </summary>
    public class PedEntityTemplateBuildContext
    {
        /// <summary>模板名（用于 Actor 命名与诊断）。对应 UE5 Template->GetName()。</summary>
        public string TemplateName = "PedTemplate";

        /// <summary>片段类型 → 工厂。</summary>
        internal readonly Dictionary<Type, Func<PedFragment>> FragmentFactories = new Dictionary<Type, Func<PedFragment>>();

        /// <summary>片段类型 → 常量共享实例。对应 UE5 GetOrCreateConstSharedFragment。</summary>
        internal readonly Dictionary<Type, PedConstSharedFragment> ConstSharedFragments = new Dictionary<Type, PedConstSharedFragment>();

        /// <summary>标签集合。对应 UE5 AddTag。</summary>
        internal readonly HashSet<Type> Tags = new HashSet<Type>();

        /// <summary>是否在编辑期检视数据。对应 UE5 <c>IsInspectingData()</c>。</summary>
        public bool bInspectingData = false;

        /// <summary>添加片段。对应 UE5 <c>AddFragment&lt;T&gt;()</c>。</summary>
        public void AddFragment<T>(Func<PedFragment> factory = null) where T : PedFragment
        {
            FragmentFactories[typeof(T)] = factory ?? (() => Activator.CreateInstance<T>());
        }

        /// <summary>添加常量共享片段。对应 UE5 <c>AddConstSharedFragment</c>。</summary>
        public void AddConstSharedFragment<T>(T fragment) where T : PedConstSharedFragment
        {
            ConstSharedFragments[typeof(T)] = fragment;
        }

        /// <summary>添加标签。对应 UE5 <c>AddTag&lt;T&gt;()</c>。</summary>
        public void AddTag<T>() where T : PedTag
        {
            Tags.Add(typeof(T));
        }

        /// <summary>是否已含某片段。供特质之间避免重复挂载。</summary>
        public bool HasFragment<T>() where T : PedFragment => FragmentFactories.ContainsKey(typeof(T));
    }

    /// <summary>
    /// 实体特质基类。对应 UE5 <c>UMassEntityTraitBase</c>。
    /// </summary>
    [Serializable]
    public abstract class PedEntityTraitBase
    {
        /// <summary>构建实体模板。对应 UE5 <c>BuildTemplate</c>。</summary>
        public abstract void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world);
    }

    /// <summary>
    /// 处理器基类。对应 UE5 <c>UMassProcessor</c>。
    /// </summary>
    public abstract class PedProcessor
    {
        /// <summary>执行分组。对应 UE5 <c>ExecutionOrder.ExecuteInGroup</c>。</summary>
        public string ExecuteInGroup = "";

        /// <summary>相对顺序。对应 UE5 ExecuteBefore/After 的线性化结果。</summary>
        public int ExecutionPriority = 0;

        protected NarrativePedEntityManager EntityManager;

        public void Initialize(NarrativePedEntityManager entityManager)
        {
            EntityManager = entityManager;
            ConfigureQueries();
        }

        /// <summary>配置查询。对应 UE5 <c>ConfigureQueries</c>。</summary>
        public virtual void ConfigureQueries() { }

        /// <summary>执行。对应 UE5 <c>Execute(FMassEntityManager&amp;, FMassExecutionContext&amp;)</c>。</summary>
        public abstract void Execute(float deltaTime);
    }

    /// <summary>观察器处理器基类。对应 UE5 <c>UMassObserverProcessor</c>。</summary>
    public abstract class PedObserverProcessor : PedProcessor
    {
        /// <summary>被观察的片段类型。对应 UE5 <c>ObservedTypes</c>。</summary>
        public List<Type> ObservedTypes = new List<Type>();

        /// <summary>被观察的操作。对应 UE5 <c>ObservedOperations</c>。</summary>
        public PedObserverOperation ObservedOperations = PedObserverOperation.Add;

        /// <summary>片段被加入实体时调用。对应 UE5 observer 的 Execute 时机。</summary>
        public abstract void OnFragmentAdded(MassEntityHandle entity);

        public override void Execute(float deltaTime)
        {
            // 观察器由 EntityManager 在增删片段时直接派发，不参与常规 tick
        }
    }

    /// <summary>观察操作。对应 UE5 <c>EMassObservedOperationFlags</c>。</summary>
    [Flags]
    public enum PedObserverOperation
    {
        None = 0,
        Add = 1,
        Remove = 2
    }

    /// <summary>
    /// 二维空间哈希网格。对应 UE5 <c>FVehicleObstacleHashGrid</c>
    /// （Flax 侧 <c>Vehicles/Mass/VehicleFragments.cs</c> 仅保留类型占位，这里给出真实实现）。
    /// </summary>
    [Serializable]
    public class PedHashGrid
    {
        /// <summary>单元边长（世界单位）。</summary>
        public float CellSize = 1000f;

        private readonly Dictionary<Long2, List<MassEntityHandle>> _cells = new Dictionary<Long2, List<MassEntityHandle>>();
        private readonly Dictionary<MassEntityHandle, List<Long2>> _entityCells = new Dictionary<MassEntityHandle, List<Long2>>();

        private struct Long2
        {
            public long X;
            public long Y;
            public Long2(long x, long y) { X = x; Y = y; }
            public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
            public override bool Equals(object obj) => obj is Long2 other && other.X == X && other.Y == Y;
        }

        private static long ToCell(float value, float cellSize)
        {
            return (long)Math.Floor(value / (cellSize <= 0f ? 1f : cellSize));
        }

        /// <summary>插入实体（以其位置与半径占据若干单元）。</summary>
        public void Insert(MassEntityHandle entity, Vector3 location, float radius)
        {
            Remove(entity);
            if (CellSize <= 0f) return;

            float extent = radius > 0f ? radius : CellSize;
            long minX = ToCell(location.X - extent, CellSize);
            long maxX = ToCell(location.X + extent, CellSize);
            long minZ = ToCell(location.Z - extent, CellSize);
            long maxZ = ToCell(location.Z + extent, CellSize);

            var occupied = new List<Long2>();
            for (long x = minX; x <= maxX; x++)
            {
                for (long z = minZ; z <= maxZ; z++)
                {
                    var cell = new Long2(x, z);
                    if (!_cells.TryGetValue(cell, out var list))
                    {
                        list = new List<MassEntityHandle>();
                        _cells[cell] = list;
                    }
                    list.Add(entity);
                    occupied.Add(cell);
                }
            }
            _entityCells[entity] = occupied;
        }

        /// <summary>移除实体。</summary>
        public void Remove(MassEntityHandle entity)
        {
            if (!_entityCells.TryGetValue(entity, out var occupied)) return;
            foreach (var cell in occupied)
            {
                if (_cells.TryGetValue(cell, out var list))
                {
                    list.Remove(entity);
                    if (list.Count == 0) _cells.Remove(cell);
                }
            }
            _entityCells.Remove(entity);
        }

        /// <summary>查询 AABB 范围内的实体（去重）。对应 UE5 <c>HashGrid.Query(Box, Out)</c>。</summary>
        public void Query(Vector3 center, float extent, List<MassEntityHandle> outEntities)
        {
            if (outEntities == null || CellSize <= 0f) return;

            long minX = ToCell(center.X - extent, CellSize);
            long maxX = ToCell(center.X + extent, CellSize);
            long minZ = ToCell(center.Z - extent, CellSize);
            long maxZ = ToCell(center.Z + extent, CellSize);

            for (long x = minX; x <= maxX; x++)
            {
                for (long z = minZ; z <= maxZ; z++)
                {
                    if (!_cells.TryGetValue(new Long2(x, z), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!outEntities.Contains(list[i]))
                        {
                            outEntities.Add(list[i]);
                        }
                    }
                }
            }
        }

        /// <summary>清空。</summary>
        public void Clear()
        {
            _cells.Clear();
            _entityCells.Clear();
        }
    }

    /// <summary>
    /// 行人 Mass 实体管理器。对应 UE5 <c>FMassEntityManager</c> + <c>UMassSimulationSubsystem</c> 的可观测子集。
    /// <para>
    /// Flax 没有 Mass，本类是手写的托管等价物：实体 = 句柄、组件 = 按类型索引的片段字典、
    /// 系统 = <see cref="PedProcessor"/> 队列、结构性修改走延迟命令缓冲。
    /// 之所以可行：<c>Vehicles/Mass</c> 已经证明本仓库可以自建 Mass 式模拟，而行人只需要
    /// 「实体 + 片段 + 处理器 + 表现 Actor 复用」这四件事。
    /// </para>
    /// </summary>
    public class NarrativePedEntityManager
    {
        private class EntityRecord
        {
            public int SerialNumber;
            public bool bAlive;
            public readonly Dictionary<Type, PedFragment> Fragments = new Dictionary<Type, PedFragment>();
            public readonly HashSet<Type> Tags = new HashSet<Type>();
            public readonly HashSet<Type> ConstSharedTypes = new HashSet<Type>();
        }

        private readonly List<EntityRecord> _entities = new List<EntityRecord>();
        private readonly Stack<int> _freeIndices = new Stack<int>();
        private readonly Dictionary<Type, PedConstSharedFragment> _sharedFragments = new Dictionary<Type, PedConstSharedFragment>();
        private readonly List<PedProcessor> _processors = new List<PedProcessor>();
        private readonly List<PedObserverProcessor> _observers = new List<PedObserverProcessor>();
        private readonly List<Action> _deferredCommands = new List<Action>();

        /// <summary>模板登记表。对应 UE5 实体模板。</summary>
        public readonly Dictionary<string, PedEntityTemplateBuildContext> Templates = new Dictionary<string, PedEntityTemplateBuildContext>();

        /// <summary>当前活动实体数。</summary>
        public int ActiveEntityCount { get; private set; }

        /// <summary>把片段实例登记为某类型的常量共享数据。对应 UE5 <c>GetOrCreateConstSharedFragment</c>。</summary>
        public T GetOrCreateConstSharedFragment<T>(T fragment) where T : PedConstSharedFragment
        {
            if (_sharedFragments.TryGetValue(typeof(T), out var existing) && existing is T typed)
            {
                return typed;
            }
            _sharedFragments[typeof(T)] = fragment;
            return fragment;
        }

        /// <summary>注册模板。</summary>
        public void RegisterTemplate(string name, PedEntityTemplateBuildContext context)
        {
            context.TemplateName = name;
            Templates[name] = context;
        }

        /// <summary>注册处理器（按 ExecutionPriority 排序后执行）。</summary>
        public void AddProcessor(PedProcessor processor)
        {
            processor.Initialize(this);
            _processors.Add(processor);
            _processors.Sort((a, b) => a.ExecutionPriority.CompareTo(b.ExecutionPriority));
        }

        public void RemoveProcessor<T>() where T : PedProcessor
        {
            _processors.RemoveAll(p => p is T);
        }

        /// <summary>注册观察器（片段增删时派发）。对应 UE5 <c>UMassObserverProcessor</c> 注册。</summary>
        public void AddObserver(PedObserverProcessor observer)
        {
            observer.Initialize(this);
            _observers.Add(observer);
        }

        /// <summary>创建实体并套用模板。对应 UE5 <c>BatchCreateEntities</c>。</summary>
        public MassEntityHandle CreateEntity(PedEntityTemplateBuildContext template)
        {
            int index;
            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Pop();
                _entities[index] = new EntityRecord { SerialNumber = _entities[index] != null ? _entities[index].SerialNumber + 1 : 1, bAlive = true };
            }
            else
            {
                index = _entities.Count;
                _entities.Add(new EntityRecord { SerialNumber = 1, bAlive = true });
            }

            var record = _entities[index];
            foreach (var kvp in template.FragmentFactories)
            {
                record.Fragments[kvp.Key] = kvp.Value();
            }
            foreach (var tag in template.Tags)
            {
                record.Tags.Add(tag);
            }
            foreach (var kvp in template.ConstSharedFragments)
            {
                _sharedFragments[kvp.Key] = kvp.Value;
                record.ConstSharedTypes.Add(kvp.Key);
            }

            ActiveEntityCount++;
            DispatchObserver(record.Tags, template, MassEntityHandleFrom(index, record.SerialNumber), PedObserverOperation.Add);
            return MassEntityHandleFrom(index, record.SerialNumber);
        }

        private void DispatchObserver(HashSet<Type> tags, PedEntityTemplateBuildContext template, MassEntityHandle handle, PedObserverOperation operation)
        {
            foreach (var observer in _observers)
            {
                if (!observer.ObservedOperations.HasFlag(operation)) continue;
                foreach (var observed in observer.ObservedTypes)
                {
                    if (template != null && template.FragmentFactories.ContainsKey(observed))
                    {
                        observer.OnFragmentAdded(handle);
                        break;
                    }
                    if (tags != null && tags.Contains(observed))
                    {
                        observer.OnFragmentAdded(handle);
                        break;
                    }
                }
            }
        }

        /// <summary>销毁实体。对应 UE5 <c>DestroyEntity</c>。</summary>
        public bool DestroyEntity(MassEntityHandle entity)
        {
            var record = GetRecord(entity);
            if (record == null) return false;
            record.bAlive = false;
            record.Fragments.Clear();
            record.Tags.Clear();
            record.ConstSharedTypes.Clear();
            _freeIndices.Push(entity.Index);
            ActiveEntityCount--;
            return true;
        }

        public bool IsEntityValid(MassEntityHandle entity) => GetRecord(entity) != null;

        public bool IsEntityActive(MassEntityHandle entity)
        {
            var record = GetRecord(entity);
            return record != null && record.bAlive;
        }

        private EntityRecord GetRecord(MassEntityHandle entity)
        {
            if (entity.Index < 0 || entity.Index >= _entities.Count) return null;
            var record = _entities[entity.Index];
            if (record == null || !record.bAlive) return null;
            if (record.SerialNumber != entity.SerialNumber) return null;
            return record;
        }

        private static MassEntityHandle MassEntityHandleFrom(int index, int serial)
        {
            // UE5 的句柄 0/0 表示无效，因此索引与序列号都要偏移 1
            return new MassEntityHandle { Index = index + 1, SerialNumber = serial };
        }

        /// <summary>取片段数据（可写）。对应 UE5 <c>GetFragmentDataChecked</c>。</summary>
        public T GetFragmentData<T>(MassEntityHandle entity) where T : PedFragment
        {
            return TryGetFragmentData<T>(entity, out var value) ? value : null;
        }

        public bool TryGetFragmentData<T>(MassEntityHandle entity, out T value) where T : PedFragment
        {
            value = null;
            var record = GetRecord(entity);
            if (record == null) return false;
            if (!record.Fragments.TryGetValue(typeof(T), out var fragment)) return false;
            value = fragment as T;
            return value != null;
        }

        /// <summary>给实体加片段。对应 UE5 <c>AddFragmentToEntity</c>。</summary>
        public T AddFragment<T>(MassEntityHandle entity) where T : PedFragment, new()
        {
            var record = GetRecord(entity);
            if (record == null) return null;
            if (record.Fragments.TryGetValue(typeof(T), out var existing) && existing is T typed)
            {
                return typed;
            }
            var created = new T();
            record.Fragments[typeof(T)] = created;
            DispatchObserver(record.Tags, null, entity, PedObserverOperation.Add);
            return created;
        }

        public bool RemoveFragment<T>(MassEntityHandle entity) where T : PedFragment
        {
            var record = GetRecord(entity);
            if (record == null) return false;
            return record.Fragments.Remove(typeof(T));
        }

        /// <summary>取常量共享片段。对应 UE5 <c>GetConstSharedFragmentDataChecked</c>。</summary>
        public T GetConstSharedFragmentData<T>(MassEntityHandle entity) where T : PedConstSharedFragment
        {
            var record = GetRecord(entity);
            if (record == null) return null;
            if (!record.ConstSharedTypes.Contains(typeof(T))) return null;
            return _sharedFragments.TryGetValue(typeof(T), out var value) ? value as T : null;
        }

        public void AddTag<T>(MassEntityHandle entity) where T : PedTag
        {
            GetRecord(entity)?.Tags.Add(typeof(T));
        }

        public void RemoveTag<T>(MassEntityHandle entity) where T : PedTag
        {
            GetRecord(entity)?.Tags.Remove(typeof(T));
        }

        public bool HasTag<T>(MassEntityHandle entity) where T : PedTag
        {
            return GetRecord(entity)?.Tags.Contains(typeof(T)) == true;
        }

        /// <summary>
        /// 遍历同时拥有全部给定片段类型的实体。对应 UE5 <c>FMassEntityQuery::ForEachEntityChunk</c>。
        /// </summary>
        public void ForEachEntity(IEnumerable<Type> requiredFragmentTypes, Action<MassEntityHandle> action)
        {
            if (action == null) return;
            var required = new List<Type>(requiredFragmentTypes ?? Array.Empty<Type>());
            for (int i = 0; i < _entities.Count; i++)
            {
                var record = _entities[i];
                if (record == null || !record.bAlive) continue;

                bool ok = true;
                foreach (var type in required)
                {
                    if (!record.Fragments.ContainsKey(type)) { ok = false; break; }
                }
                if (!ok) continue;

                action(new MassEntityHandle { Index = i + 1, SerialNumber = record.SerialNumber });
            }
        }

        /// <summary>便捷版：按片段类型组合遍历。</summary>
        public void ForEachEntity(Action<MassEntityHandle> action, params Type[] requiredFragmentTypes)
        {
            ForEachEntity((IEnumerable<Type>)requiredFragmentTypes ?? Array.Empty<Type>(), action);
        }

        /// <summary>当前所有活动实体（快照）。对应 UE5 <c>FSpawnedEntities::SpawnedEntities</c>。</summary>
        public List<MassEntityHandle> GetActiveEntities()
        {
            var result = new List<MassEntityHandle>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var record = _entities[i];
                if (record == null || !record.bAlive) continue;
                result.Add(new MassEntityHandle { Index = i + 1, SerialNumber = record.SerialNumber });
            }
            return result;
        }

        /// <summary>推入延迟命令。对应 UE5 <c>FMassCommandBuffer::PushCommand&lt;FMassDeferredSetCommand&gt;</c>。</summary>
        public void Defer(Action command)
        {
            if (command == null) return;
            _deferredCommands.Add(command);
        }

        /// <summary>执行一轮所有处理器并冲刷延迟命令。对应 UE5 <c>FMassSimulationTick</c>。</summary>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _processors.Count; i++)
            {
                try
                {
                    _processors[i].Execute(deltaTime);
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Mass 处理器 {_processors[i].GetType().Name} 执行失败: {ex.Message}");
                }
            }

            FlushDeferredCommands();
        }

        /// <summary>冲刷延迟命令。结构性修改必须等处理器跑完再做，避免遍历中改动集合。</summary>
        public void FlushDeferredCommands()
        {
            if (_deferredCommands.Count == 0) return;
            for (int i = 0; i < _deferredCommands.Count; i++)
            {
                try
                {
                    _deferredCommands[i].Invoke();
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Mass 延迟命令执行失败: {ex.Message}");
                }
            }
            _deferredCommands.Clear();
        }

        /// <summary>清空所有实体与处理器状态（切场景时用）。</summary>
        public void Reset()
        {
            _entities.Clear();
            _freeIndices.Clear();
            ActiveEntityCount = 0;
            _deferredCommands.Clear();
        }
    }
}

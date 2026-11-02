using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    // =========================================================================
    // 车道 / 障碍索引：UE5 由 Mass + ZoneGraph 提供，Flax 两者皆无
    // （已核对 FlaxEngine.CSharp.xml：ZoneGraph / Mass / Chaos Vehicle 均 0 命中），
    // 因此这里是等价的可移植实现：句柄 = (数据索引, 车道索引)，存储 = JSON 构建的折线车道。
    // =========================================================================

    /// <summary>Mass 实体句柄。对应 UE5 FMassEntityHandle（Index + SerialNumber）。</summary>
    [Serializable]
    public struct MassEntityHandle : IEquatable<MassEntityHandle>
    {
        public int Index;
        public int SerialNumber;
        public bool IsValid => Index != 0 || SerialNumber != 0;
        public static readonly MassEntityHandle Invalid = default;

        public bool Equals(MassEntityHandle other) => Index == other.Index && SerialNumber == other.SerialNumber;
        public override bool Equals(object obj) => obj is MassEntityHandle other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ SerialNumber;
        public override string ToString() => $"Entity({Index},{SerialNumber})";

        public static bool operator ==(MassEntityHandle a, MassEntityHandle b) => a.Equals(b);
        public static bool operator !=(MassEntityHandle a, MassEntityHandle b) => !a.Equals(b);
    }

    /// <summary>ZoneGraph 车道句柄。对应 UE5 FZoneGraphLaneHandle（DataHandle + Index）。</summary>
    [Serializable]
    public struct ZoneGraphLaneHandle : IEquatable<ZoneGraphLaneHandle>
    {
        /// <summary>所属 ZoneGraph 数据。对应 UE5 FZoneGraphLaneHandle::DataHandle。</summary>
        public ZoneGraphDataHandle DataHandle;

        /// <summary>车道在存储内的索引，-1 表示无效。</summary>
        public int Index;

        public bool IsValid => Index >= 0;
        public static readonly ZoneGraphLaneHandle Invalid = new ZoneGraphLaneHandle { Index = -1 };

        public bool Equals(ZoneGraphLaneHandle other) => Index == other.Index && DataHandle.Index == other.DataHandle.Index;
        public override bool Equals(object obj) => obj is ZoneGraphLaneHandle other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ DataHandle.Index;
        public override string ToString() => $"Lane({DataHandle.Index},{Index})";

        public static bool operator ==(ZoneGraphLaneHandle a, ZoneGraphLaneHandle b) => a.Equals(b);
        public static bool operator !=(ZoneGraphLaneHandle a, ZoneGraphLaneHandle b) => !a.Equals(b);
    }

    /// <summary>ZoneGraph 数据句柄。对应 UE5 FZoneGraphDataHandle。</summary>
    [Serializable]
    public struct ZoneGraphDataHandle : IEquatable<ZoneGraphDataHandle>
    {
        public int Index;
        public bool IsValid => Index >= 0;
        public static readonly ZoneGraphDataHandle Invalid = new ZoneGraphDataHandle { Index = -1 };

        public bool Equals(ZoneGraphDataHandle other) => Index == other.Index;
        public override bool Equals(object obj) => obj is ZoneGraphDataHandle other && Equals(other);
        public override int GetHashCode() => Index;
    }

    /// <summary>ZoneGraph 标签。对应 UE5 FZoneGraphTag。</summary>
    [Serializable]
    public struct ZoneGraphTag
    {
        public string Name;

        public ZoneGraphTag(string name)
        {
            Name = name;
        }

        public bool IsValid => !string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// ZoneGraph 标签掩码。对应 UE5 FZoneGraphTagMask —— Flax 无标签位图，用标签名集合等价。
    /// </summary>
    [Serializable]
    public class ZoneGraphTagMask
    {
        public List<string> TagNames = new List<string>();

        /// <summary>空掩码（每次返回新实例，避免共享可变列表）。</summary>
        public static ZoneGraphTagMask None => new ZoneGraphTagMask();

        public bool IsEmpty => TagNames == null || TagNames.Count == 0;

        public void Add(string tag)
        {
            if (string.IsNullOrEmpty(tag) || TagNames.Contains(tag)) return;
            TagNames.Add(tag);
        }

        public bool Contains(string tag) => TagNames != null && TagNames.Contains(tag);
    }

    /// <summary>
    /// ZoneGraph 标签过滤器。对应 UE5 FZoneGraphTagFilter（Any/All/Not）。
    /// <see cref="Pass"/> 与 UE5 语义一致：Not 命中即拒绝；All 需全部命中；Any 非空时至少命中一个。
    /// </summary>
    [Serializable]
    public class ZoneGraphTagFilter
    {
        public ZoneGraphTagMask AnyTags = ZoneGraphTagMask.None;
        public ZoneGraphTagMask AllTags = ZoneGraphTagMask.None;
        public ZoneGraphTagMask NotTags = ZoneGraphTagMask.None;

        public bool Pass(IList<string> tags)
        {
            if (NotTags != null)
            {
                foreach (var not in NotTags.TagNames)
                {
                    if (Contains(tags, not)) return false;
                }
            }

            if (AllTags != null)
            {
                foreach (var all in AllTags.TagNames)
                {
                    if (!Contains(tags, all)) return false;
                }
            }

            if (AnyTags != null && AnyTags.TagNames.Count > 0)
            {
                foreach (var any in AnyTags.TagNames)
                {
                    if (Contains(tags, any)) return true;
                }
                return false;
            }

            return true;
        }

        private static bool Contains(IList<string> tags, string tag)
        {
            if (tags == null || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 载具障碍物哈希网格。对应 UE5 <c>FVehicleObstacleHashGrid</c>（THierarchicalHashGrid2D）。
    /// Flax 无该模板，这里实现单层均匀网格（UE5 的分层在本作 2-4 车量级下无收益，记录为简化点）：
    /// <see cref="Add"/> / <see cref="Move"/> / <see cref="Remove"/> / <see cref="Query"/> 与 UE5 调用点一一对应。
    /// </summary>
    [Serializable]
    public class VehicleObstacleHashGrid
    {
        /// <summary>网格单元位置。对应 UE5 FVehicleObstacleHashGrid::FCellLocation。</summary>
        [Serializable]
        public struct FCellLocation
        {
            public int X;
            public int Y;
            public int Layer;

            public bool IsValid => Layer >= 0;
            public override string ToString() => $"Cell({X},{Y},L{Layer})";
        }

        /// <summary>单元边长（世界单位）。UE5 侧由 THierarchicalHashGrid2D 配置，此处取 900（≈9m）。</summary>
        public float CellSize = 900f;

        private readonly Dictionary<long, List<VehicleObstacleRecord>> _cells = new Dictionary<long, List<VehicleObstacleRecord>>();
        private readonly Dictionary<MassEntityHandle, VehicleObstacleRecord> _byHandle = new Dictionary<MassEntityHandle, VehicleObstacleRecord>();
        private readonly List<VehicleObstacleRecord> _scratch = new List<VehicleObstacleRecord>();

        /// <summary>当前登记的障碍物数量。</summary>
        public int Count => _byHandle.Count;

        private static FCellLocation CellFor(Vector3 location, float cellSize)
        {
            // Mathf 无 Ceiling（Flax 坑），用 Floor + 1 等价上取整；此处只需 Floor。
            int x = (int)Mathf.Floor(location.X / cellSize);
            int y = (int)Mathf.Floor(location.Z / cellSize);
            return new FCellLocation { X = x, Y = y, Layer = 0 };
        }

        private static long CellKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>按句柄取记录（未登记返回 null）。</summary>
        public bool TryGet(MassEntityHandle handle, out VehicleObstacleRecord record)
        {
            return _byHandle.TryGetValue(handle, out record);
        }

        /// <summary>登记障碍物。对应 UE5 <c>VehicleObstacles.Add(entity, extent)</c>。</summary
        public FCellLocation Add(MassEntityHandle handle, Vector3 location, float radius)
        {
            if (_byHandle.TryGetValue(handle, out var existing))
            {
                return Move(handle, existing.CellLocation, location, radius);
            }

            var record = new VehicleObstacleRecord
            {
                Handle = handle,
                Location = location,
                Radius = radius,
                CellLocation = CellFor(location, CellSize),
                bRegistered = true,
            };
            _byHandle[handle] = record;
            Bucket(record.CellLocation).Add(record);
            return record.CellLocation;
        }

        /// <summary>移动障碍物。对应 UE5 <c>VehicleObstacles.Move(entity, prevCell, extent)</c>。</summary>
        public FCellLocation Move(MassEntityHandle handle, FCellLocation previousCell, Vector3 location, float radius)
        {
            if (!_byHandle.TryGetValue(handle, out var record))
            {
                return Add(handle, location, radius);
            }

            record.Location = location;
            record.Radius = radius;
            var nextCell = CellFor(location, CellSize);
            if (nextCell.X != previousCell.X || nextCell.Y != previousCell.Y)
            {
                var bucket = Bucket(previousCell);
                bucket.Remove(record);
                if (bucket.Count == 0) _cells.Remove(CellKey(previousCell.X, previousCell.Y));
                Bucket(nextCell).Add(record);
                record.CellLocation = nextCell;
            }
            else
            {
                record.CellLocation = nextCell;
            }
            return record.CellLocation;
        }

        /// <summary>移除障碍物。对应 UE5 <c>VehicleObstacles.Remove(entity, cell)</c>。</summary>
        public void Remove(MassEntityHandle handle, FCellLocation cell)
        {
            if (!_byHandle.TryGetValue(handle, out var record)) return;
            _byHandle.Remove(handle);
            var bucket = Bucket(record.CellLocation);
            bucket.Remove(record);
            if (bucket.Count == 0) _cells.Remove(CellKey(record.CellLocation.X, record.CellLocation.Y));
            record.bRegistered = false;
        }

        /// <summary>清空网格。</summary>
        public void Clear()
        {
            _cells.Clear();
            _byHandle.Clear();
        }

        /// <summary>
        /// 盒范围查询。对应 UE5 <c>VehicleObstacles.Query(FBox, out TArray)</c>。
        /// 返回的列表是内部暂存视图，调用方只可即时读取。
        /// </summary>
        public List<VehicleObstacleRecord> Query(Vector3 center, Vector3 extent)
        {
            _scratch.Clear();
            if (CellSize <= 1e-3f) return _scratch;

            var minCell = CellFor(center - extent, CellSize);
            var maxCell = CellFor(center + extent, CellSize);
            for (int x = minCell.X; x <= maxCell.X; x++)
            {
                for (int y = minCell.Y; y <= maxCell.Y; y++)
                {
                    if (!_cells.TryGetValue(CellKey(x, y), out var bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var record = bucket[i];
                        if (!_scratch.Contains(record)) _scratch.Add(record);
                    }
                }
            }
            return _scratch;
        }

        private List<VehicleObstacleRecord> Bucket(FCellLocation cell)
        {
            long key = CellKey(cell.X, cell.Y);
            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = new List<VehicleObstacleRecord>();
                _cells[key] = bucket;
            }
            return bucket;
        }
    }

    /// <summary>
    /// 交叉口侧哈希网格。对应 UE5 <c>FIntersectionSideHashGrid</c>。
    /// UE5 用它让 <c>ATrafficLight</c> 按位置找回自己管辖的交叉口侧
    /// （<c>TrafficLight.cpp:CacheIntersectionSide</c> 的 <c>IntersectionSidesGrid.Query</c>）。
    /// 交叉口侧数量与关卡路口数同阶（个位数），这里用线性最近查询实现同一语义。
    /// </summary>
    [Serializable]
    public class IntersectionSideHashGrid
    {
        private struct Entry
        {
            public TrafficIntersectionSideHandle Handle;
            public Vector3 Location;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>已登记的交叉口侧数量。</summary>
        public int Count => _entries.Count;

        /// <summary>登记交叉口侧。对应 UE5 <c>IntersectionSidesGrid.Add(Container, bounds)</c>。</summary>
        public void Add(TrafficIntersectionSideHandle handle, Vector3 location)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Handle.IntersectionIndex == handle.IntersectionIndex
                    && _entries[i].Handle.SideIndex == handle.SideIndex
                    && _entries[i].Handle.ZoneGraphDataHandle.Index == handle.ZoneGraphDataHandle.Index)
                {
                    _entries[i] = new Entry { Handle = handle, Location = location };
                    return;
                }
            }
            _entries.Add(new Entry { Handle = handle, Location = location });
        }

        /// <summary>清空。对应 UE5 ZoneGraph 数据移除。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// 范围查询。对应 UE5 <c>IntersectionSidesGrid.Query(Extent, out Sides)</c>。
        /// 结果按到查询点的 2D 距离升序（UE5 在 TrafficLight.cpp 里也是按 DistSquared2D 排序取最近）。
        /// </summary>
        public List<TrafficIntersectionSideHandle> Query(Vector3 center, Vector3 extent)
        {
            var result = new List<TrafficIntersectionSideHandle>();
            var distances = new List<float>();

            foreach (var entry in _entries)
            {
                Vector3 delta = entry.Location - center;
                bool inside = Mathf.Abs(delta.X) <= Mathf.Abs(extent.X)
                              && Mathf.Abs(delta.Z) <= Mathf.Abs(extent.Z)
                              && Mathf.Abs(delta.Y) <= Mathf.Abs(extent.Y);
                if (!inside) continue;
                result.Add(entry.Handle);
                distances.Add(delta.X * delta.X + delta.Z * delta.Z);
            }

            // 选择排序（数量极小），保持 handle 与距离同步
            for (int i = 0; i < result.Count; i++)
            {
                int best = i;
                for (int j = i + 1; j < result.Count; j++)
                {
                    if (distances[j] < distances[best]) best = j;
                }
                if (best != i)
                {
                    (result[i], result[best]) = (result[best], result[i]);
                    (distances[i], distances[best]) = (distances[best], distances[i]);
                }
            }
            return result;
        }

        /// <summary>按句柄取交叉口侧位置（供调试/查询）。</summary>
        public bool TryGetLocation(TrafficIntersectionSideHandle handle, out Vector3 location)
        {
            foreach (var entry in _entries)
            {
                if (entry.Handle.IntersectionIndex == handle.IntersectionIndex
                    && entry.Handle.SideIndex == handle.SideIndex
                    && entry.Handle.ZoneGraphDataHandle.Index == handle.ZoneGraphDataHandle.Index)
                {
                    location = entry.Location;
                    return true;
                }
            }
            location = Vector3.Zero;
            return false;
        }
    }

    /// <summary>
    /// ZoneGraph 存储。对应 UE5 <c>FZoneGraphStorage</c>（Lanes / LanePoints / LaneTangentVectors + 查询 API）。
    /// 由 <see cref="NarrativePro.Vehicles.VehicleTrafficSimulation"/> 从 JSON 构建，
    /// 供移动/车道/交通灯三个子系统共享读取。
    /// </summary>
    [Serializable]
    public class ZoneGraphStorage
    {
        /// <summary>本存储的数据句柄。</summary>
        public ZoneGraphDataHandle DataHandle = new ZoneGraphDataHandle { Index = 0 };

        /// <summary>车道列表。对应 UE5 Storage.Lanes。</summary>
        public List<TrafficLaneData> Lanes = new List<TrafficLaneData>();

        /// <summary>展平的车道点。对应 UE5 Storage.LanePoints（保留字段以对齐调试输出）。</summary>
        public List<Vector3> LanePoints = new List<Vector3>();

        /// <summary>展平的车道切线。对应 UE5 Storage.LaneTangentVectors。</summary>
        public List<Vector3> LaneTangentVectors = new List<Vector3>();

        /// <summary>添加车道并回填 Index。对应 UE5 构建存储。</summary>
        public TrafficLaneData AddLane(TrafficLaneData lane)
        {
            lane.Index = Lanes.Count;
            Lanes.Add(lane);
            return lane;
        }

        /// <summary>构建累计里程/切线并展平点集。对应 UE5 ZoneGraph 烘焙。</summary>
        public void Build()
        {
            LanePoints.Clear();
            LaneTangentVectors.Clear();
            for (int i = 0; i < Lanes.Count; i++)
            {
                var lane = Lanes[i];
                lane.Index = i;
                lane.Build();
                for (int p = 0; p < lane.Points.Count; p++)
                {
                    LanePoints.Add(lane.Points[p]);
                    LaneTangentVectors.Add(p < lane.Tangents.Count ? lane.Tangents[p] : Vector3.Forward);
                }
            }
        }

        /// <summary>车道序号 → 句柄。</summary>
        public ZoneGraphLaneHandle MakeHandle(int laneIndex)
        {
            return new ZoneGraphLaneHandle { DataHandle = DataHandle, Index = laneIndex };
        }

        /// <summary>取车道数据（无效返回 null）。对应 UE5 <c>Storage.GetLaneOrDefault</c>。</summary>
        public TrafficLaneData GetLane(ZoneGraphLaneHandle handle)
        {
            if (!handle.IsValid || handle.DataHandle.Index != DataHandle.Index) return null;
            if (handle.Index < 0 || handle.Index >= Lanes.Count) return null;
            return Lanes[handle.Index];
        }

        /// <summary>车道长度。对应 UE5 <c>UE::ZoneGraph::Query::GetLaneLength</c>。</summary>
        public bool GetLaneLength(ZoneGraphLaneHandle handle, out float length)
        {
            var lane = GetLane(handle);
            length = lane != null ? lane.Length : 0f;
            return lane != null;
        }

        /// <summary>沿车道取位置。对应 UE5 <c>ZoneGraph.CalculateLocationAlongLane</c>。</summary>
        public bool CalculateLocationAlongLane(ZoneGraphLaneHandle handle, float distanceAlongLane, out TrafficLaneLocation location)
        {
            var lane = GetLane(handle);
            location = default;
            if (lane == null) return false;
            lane.GetLocationAt(distanceAlongLane, out var position, out var tangent);
            location.LaneHandle = handle;
            location.Position = position;
            location.Tangent = tangent;
            location.DistanceAlongLane = distanceAlongLane;
            location.LaneLength = lane.Length;
            return true;
        }

        /// <summary>把点投影到车道。对应 UE5 <c>ZoneGraph.FindNearestLocationOnLane</c>。</summary>
        public bool FindNearestLocationOnLane(ZoneGraphLaneHandle handle, Vector3 point, float searchExtent, out TrafficLaneLocation location)
        {
            var lane = GetLane(handle);
            location = default;
            if (lane == null) return false;
            lane.FindNearest(point, out var nearest, out var distance, out var tangent);
            location.LaneHandle = handle;
            location.Position = nearest;
            location.Tangent = tangent;
            location.DistanceAlongLane = distance;
            location.LaneLength = lane.Length;
            return true;
        }

        /// <summary>在半径内找最近车道。对应 UE5 <c>UE::ZoneGraph::Query::FindClosestLane</c>。</summary>
        public bool FindClosestLane(Vector3 point, float radius, out TrafficLaneLocation location)
        {
            location = default;
            float bestDistanceSquared = radius > 0f ? radius * radius : float.MaxValue;
            bool bFound = false;

            for (int i = 0; i < Lanes.Count; i++)
            {
                var lane = Lanes[i];
                if (lane.Points == null || lane.Points.Count < 2) continue;
                lane.FindNearest(point, out var nearest, out var distance, out var tangent);
                float distanceSquared = (nearest - point).LengthSquared;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    location.LaneHandle = MakeHandle(i);
                    location.Position = nearest;
                    location.Tangent = tangent;
                    location.DistanceAlongLane = distance;
                    location.LaneLength = lane.Length;
                    bFound = true;
                }
            }
            return bFound;
        }

        /// <summary>出向链接车道。对应 UE5 <c>UE::ZoneGraph::Query::GetLinkedLanes(Outgoing)</c>。</summary>
        public void GetLinkedLanes(ZoneGraphLaneHandle handle, bool bOutgoing, List<ZoneGraphLaneHandle> outLanes)
        {
            outLanes.Clear();
            var lane = GetLane(handle);
            if (lane == null) return;
            var links = bOutgoing ? lane.NextLanes : lane.PreviousLanes;
            if (links == null) return;
            foreach (int index in links)
            {
                if (index < 0 || index >= Lanes.Count) continue;
                outLanes.Add(MakeHandle(index));
            }
        }

        /// <summary>第一条出向链接。对应 UE5 <c>ZoneGraph.GetFirstLinkedLane</c>。</summary>
        public bool GetFirstLinkedLane(ZoneGraphLaneHandle handle, out ZoneGraphLaneHandle linkedLane)
        {
            var buffer = new List<ZoneGraphLaneHandle>();
            GetLinkedLanes(handle, true, buffer);
            linkedLane = buffer.Count > 0 ? buffer[0] : ZoneGraphLaneHandle.Invalid;
            return buffer.Count > 0;
        }
    }

    /// <summary>Mass 生成实体类型。对应 UE5 FMassSpawnedEntityType（此处只剩数量语义）。</summary>
    [Serializable]
    public class MassSpawnedEntityType
    {
        public int Amount;

        /// <summary>模板名。对应 UE5 FMassSpawnedEntityType::EntityAsset 的角色。</summary>
        public string TemplateName = "TrafficVehicle";
    }

    // =========================================================================
    // 片段基类占位：对应 UE5 FMassFragment/FMassConstSharedFragment/FMassTag/FObjectWrapperFragment
    // =========================================================================

    /// <summary>Mass 片段基类占位。对应 UE5 FMassFragment。</summary>
    [Serializable]
    public abstract class MassFragment { }

    /// <summary>Mass 共享片段基类占位。对应 UE5 FMassConstSharedFragment。</summary>
    [Serializable]
    public abstract class MassConstSharedFragment { }

    /// <summary>Mass 标签基类占位。对应 UE5 FMassTag。</summary>
    [Serializable]
    public abstract class MassTag : MassFragment { }

    /// <summary>对象包装片段基类占位。对应 UE5 FObjectWrapperFragment。</summary>
    [Serializable]
    public abstract class ObjectWrapperFragment : MassFragment { }

    /// <summary>Agent 半径片段占位。对应 UE5 FAgentRadiusFragment。</summary>
    [Serializable]
    public class AgentRadiusFragment : MassFragment
    {
        public float Radius = 0f;
    }

    // =========================================================================
    // VehicleFragments.h 中的结构
    // =========================================================================

    /// <summary>
    /// 载具移动片段。对应 UE5 FVehicleLocomotionFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。Flax 无 Mass，改为 [Serializable] class 占位。
    /// </summary>
    [Serializable]
    public class VehicleLocomotionFragment : MassFragment
    {
        public float DesiredSpeed = 0f;

        /// <summary>高 LOD PID 控制器。</summary>
        public VehiclePIDController ThrottlePIDController = new VehiclePIDController();
        public VehiclePIDController SteeringPIDController = new VehiclePIDController();

        /// <summary>油门，范围 -1 到 1，&lt; 0 为刹车。</summary>
        public float Throttle = 0f;

        public float Steering = 0f;

        public float DistanceToNextVehicle = float.MaxValue;

        public MassEntityHandle NextVehicleHandle = MassEntityHandle.Invalid;

        public ZoneGraphLaneHandle NextLane = ZoneGraphLaneHandle.Invalid;

        public float Color = 0f;
    }

    /// <summary>
    /// PID 设置。对应 UE5 FPIDSettings（VehicleFragments.h）。
    /// </summary>
    [Serializable]
    public class PIDSettings
    {
        public float ProportionalGain = 0f;
        public float IntegralGain = 0f;
        public float DerivativeGain = 0f;
    }

    /// <summary>
    /// 载具 PID 控制器片段。对应 UE5 FVehiclePIDControllerFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。
    /// </summary>
    [Serializable]
    public class VehiclePIDControllerFragment : MassConstSharedFragment
    {
        public PIDSettings SteeringSettings = new PIDSettings();
        public PIDSettings ThrottleSettings = new PIDSettings();

        /// <summary>当前车道前方计算转向的距离。</summary>
        public float LookAheadDistance = 500f;
    }

    /// <summary>
    /// 载具设置片段。对应 UE5 FVehicleSettingsFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。
    /// </summary>
    [Serializable]
    public class VehicleSettingsFragment : MassConstSharedFragment
    {
        /// <summary>希望与障碍物保持的最小距离。</summary>
        public float MinimumDistanceToObstacle = 300f;

        /// <summary>有障碍物接近时开始刹车的距离。</summary>
        public float BrakingDistanceFromObstacle = 500f;

        /// <summary>障碍物接近时刹车力度。</summary>
        public float ObstacleAvoidanceBrakingPower = 2f;

        /// <summary>搜索周围障碍物的半径。</summary>
        public float ObstacleSearchRadius = 1500f;

        /// <summary>与关闭车道的最小距离。</summary>
        public float ClosedLaneMinDistance = 100f;

        /// <summary>关闭车道接近时开始刹车的距离。</summary>
        public float ClosedLaneBrakingDistance = 300f;

        /// <summary>关闭车道接近时刹车力度。</summary>
        public float ClosedLaneBrakingPower = 2f;

        /// <summary>与下一辆车保持的最小距离。</summary>
        public float MinimumDistanceToNext = 300f;

        /// <summary>下一辆车接近时开始刹车的距离。</summary>
        public float BrakingDistanceFromNext = 500f;

        /// <summary>下一辆车接近时刹车力度。</summary>
        public float NextVehicleAvoidanceBrakingPower = 2f;

        /// <summary>载具期望的最大速度。</summary>
        public float VehicleMaxSpeed = 400f;

        /// <summary>搜索附近 zonegraph 车道的范围。</summary>
        public float LaneSearchRadius = 500f;

        /// <summary>在 zonegraph 中选择车道时使用的过滤器。</summary>
        public ZoneGraphTagFilter VehicleLaneFilter = new ZoneGraphTagFilter();
    }

    /// <summary>
    /// 载具障碍物片段。对应 UE5 FVehicleObstacleFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。
    /// </summary>
    [Serializable]
    public class VehicleObstacleFragment : MassFragment
    {
        /// <summary>此障碍物存储的位置。</summary>
        public VehicleObstacleHashGrid.FCellLocation CellLocation;
    }

    /// <summary>
    /// 载具组件包装片段。对应 UE5 FVehicleComponentWrapperFragment（VehicleFragments.h）。
    /// 继承 FObjectWrapperFragment。UE5 包装的是 <c>UChaosWheeledVehicleMovementComponent</c>；
    /// Flax 1.12 无 Chaos Vehicle System（已核对 FlaxEngine.CSharp.xml，0 命中 Vehicle/WheeledVehicle），
    /// 因此这里包装 Flax 侧的载具脚本 Actor <see cref="TrafficVehicleActor"/>，
    /// 由 <see cref="VehicleRepresentationSubsystem"/> 在高 LOD 时写入、移动处理器据此做 Actor↔车道同步。
    /// </summary>
    [Serializable]
    public class VehicleComponentWrapperFragment : ObjectWrapperFragment
    {
        /// <summary>包装的载具 Actor 脚本。对应 UE5 TWeakObjectPtr&lt;UChaosWheeledVehicleMovementComponent&gt;。</summary>
        [NonSerialized]
        public TrafficVehicleActor Vehicle;

        /// <summary>Actor 侧的速度读值（UE5 由 Chaos 组件给出，此处由运动学模拟写入）。</summary>
        public float Speed;
    }

    /// <summary>
    /// 种子片段。对应 UE5 FSeedFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。存储确定性随机值的种子。
    /// </summary>
    [Serializable]
    public class SeedFragment : MassFragment
    {
        public SeedFragment() { }

        public SeedFragment(int inSeed)
        {
            Seed = inSeed;
        }

        public int Seed = 0;

        /// <summary>随机流。对应 UE5 FRandomStream。Flax 简化为 System.Random。</summary>
        [NonSerialized]
        public System.Random Stream;
    }

    /// <summary>
    /// 乘客加权概率。对应 UE5 FPassengerWeightedProbability（VehicleFragments.h）。
    /// 存储加权概率与乘客数量。
    /// </summary>
    [Serializable]
    public class PassengerWeightedProbability
    {
        public PassengerWeightedProbability() { }

        public PassengerWeightedProbability(float inWeightedProb, int inNumPassengers)
        {
            WeightedProbability = inWeightedProb;
            NumPassengers = inNumPassengers;
        }

        public float WeightedProbability = 1f;
        public int NumPassengers = 1;
    }

    /// <summary>
    /// 乘客设置片段。对应 UE5 FPassengerSettingsFragment（VehicleFragments.h）。
    /// 继承 FMassConstSharedFragment。定义可进入载具的乘客。
    /// </summary>
    [Serializable]
    public class PassengerSettingsFragment : MassConstSharedFragment
    {
        public PassengerSettingsFragment() { }

        /// <summary>可进入载具的乘客定义。对应 UE5 TArray&lt;UNPCDefinition*&gt;。</summary>
        public List<NPCDefinition> PassengerDefinitions = new List<NPCDefinition>();

        /// <summary>获得 x 名乘客的加权概率。</summary>
        public List<PassengerWeightedProbability> PassengerCountProbability =
            new List<PassengerWeightedProbability> { new PassengerWeightedProbability(1f, 1) };

        public bool IsValid()
        {
            return PassengerDefinitions != null && PassengerDefinitions.Count > 0
                && PassengerCountProbability != null && PassengerCountProbability.Count > 0;
        }
    }

    /// <summary>
    /// 载具乘客片段。对应 UE5 FVehiclePassengersFragment（VehicleFragments.h）。
    /// 继承 FMassFragment。存储载具内乘客的实体特定数据。
    /// </summary>
    [Serializable]
    public class VehiclePassengersFragment : MassFragment
    {
        public VehiclePassengersFragment() { }

        /// <summary>键为座位索引，值为乘客定义。对应 UE5 TArray&lt;TPair&lt;int, UNPCDefinition*&gt;&gt;。</summary>
        public List<KeyValuePair<int, NPCDefinition>> Passengers = new List<KeyValuePair<int, NPCDefinition>>();
    }

    /// <summary>
    /// 载具乘客加权采样器。对应 UE5 FVehiclePassengerWeightedSampler（VehicleFragments.h）。
    /// 继承 FWeightedRandomSampler。用于获得载具内乘客数量的加权概率。
    /// </summary>
    [Serializable]
    public class VehiclePassengerWeightedSampler
    {
        public List<PassengerWeightedProbability> PassengerCountProbability;

        public VehiclePassengerWeightedSampler(List<PassengerWeightedProbability> probability)
        {
            PassengerCountProbability = probability ?? new List<PassengerWeightedProbability>();
        }

        /// <summary>获取权重列表。对应 UE5 GetWeights。</summary>
        public virtual float GetWeights(List<float> outWeights)
        {
            // 对应 UE5 FWeightedRandomSampler::GetWeights：输出各档权重并返回权重总和
            float total = 0f;
            if (outWeights == null) return 0f;
            outWeights.Clear();
            foreach (var p in PassengerCountProbability)
            {
                outWeights.Add(p.WeightedProbability);
                total += p.WeightedProbability;
            }
            return total;
        }
    }

    /// <summary>
    /// 自动销毁标签。对应 UE5 FAutoDestroyTag（VehicleFragments.h）。
    /// 继承 FMassTag。当实体处于 Low LOD 且带此标签时自动销毁。
    /// </summary>
    [Serializable]
    public class AutoDestroyTag : MassTag
    {
    }
}

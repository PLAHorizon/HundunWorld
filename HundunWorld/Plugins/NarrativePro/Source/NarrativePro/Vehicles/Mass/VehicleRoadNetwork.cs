using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 车道几何（Flax 侧 ZoneGraph 车道的等价物）。
    /// <para>
    /// UE5 侧对应 <c>FZoneLaneData</c> + <c>FZoneGraphStorage</c> 的车道段：折线点、切线、累计距离、
    /// 出/入向链接、标签集合（<c>Intersection</c> / <c>Closed</c> 等由交通灯注解写入）。
    /// Flax 无 ZoneGraph 资产（已核对 <c>FlaxEngine.CSharp.xml</c>：0 命中 ZoneGraph），
    /// 因此车道由 <c>Content/NarrativePro/Vehicles/*.json</c> 描述、运行期构建 —— 这是可移植的等价实现，
    /// 而不是把 UE5 的 ZoneGraph 当成「不可移植」留着不实现。
    /// </para>
    /// </summary>
    [Serializable]
    public class TrafficLaneData
    {
        /// <summary>在本 <see cref="ZoneGraphStorage"/> 内的车道序号。对应 UE5 FZoneGraphLaneHandle.Index。</summary>
        public int Index = -1;

        /// <summary>所属路口/路段分区号。对应 UE5 FZoneLaneData.ZoneIndex。</summary>
        public int ZoneIndex = -1;

        /// <summary>车道名（JSON 引用与日志用）。</summary>
        public string Name = "";

        /// <summary>车道折线点（世界坐标）。对应 UE5 Storage.LanePoints 的一段。</summary>
        public List<Vector3> Points = new List<Vector3>();

        /// <summary>逐点切线（单位向量）。对应 UE5 Storage.LaneTangentVectors。</summary>
        public List<Vector3> Tangents = new List<Vector3>();

        /// <summary>逐点累计里程（与 Points 等长，首点为 0）。</summary>
        public List<float> CumulativeDistances = new List<float>();

        /// <summary>出向链接（下一车道 Index 列表）。对应 UE5 GetLinkedLanes(Outgoing)。</summary>
        public List<int> NextLanes = new List<int>();

        /// <summary>入向链接（上一车道 Index 列表）。对应 UE5 GetLinkedLanes(Incoming)。</summary>
        public List<int> PreviousLanes = new List<int>();

        /// <summary>车道标签。对应 UE5 FZoneLaneData.Tags。</summary>
        public List<string> Tags = new List<string>();

        /// <summary>车道总长。对应 UE5 GetLaneLength。</summary>
        public float Length;

        /// <summary>是否在终点回连到起点（环形道路，便于演示持续车流）。</summary>
        public bool bLooping;

        /// <summary>是否含有指定标签。</summary>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || Tags == null) return false;
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>添加标签（幂等）。对应 UE5 注解系统写 Tags。</summary>
        public void AddTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || Tags == null) return;
            Tags.Add(tag);
        }

        /// <summary>移除标签。对应 UE5 注解系统移除 Tags。</summary>
        public void RemoveTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || Tags == null) return;
            Tags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>累计里程 / 切线。对应 UE5 构建 ZoneGraph 时预算的 LanePoints/LaneTangentVectors。</summary>
        public void Build()
        {
            CumulativeDistances.Clear();
            Tangents.Clear();
            Length = 0f;
            if (Points == null || Points.Count == 0) return;

            CumulativeDistances.Add(0f);
            for (int i = 1; i < Points.Count; i++)
            {
                Length += Vector3.Distance(Points[i - 1], Points[i]);
                CumulativeDistances.Add(Length);
            }

            // 环道：末点回连起点
            if (bLooping && Points.Count > 1)
            {
                Length += Vector3.Distance(Points[Points.Count - 1], Points[0]);
                CumulativeDistances.Add(Length);
            }

            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 previous = i == 0
                    ? (bLooping && Points.Count > 2 ? Points[Points.Count - 2] : Points[0])
                    : Points[i - 1];
                Vector3 next = i >= Points.Count - 1
                    ? (bLooping && Points.Count > 2 ? Points[1] : Points[Points.Count - 1])
                    : Points[i + 1];
                Vector3 tangent = next - previous;
                Tangents.Add(tangent.LengthSquared > 1e-6f ? Vector3.Normalize(tangent) : Vector3.Forward);
            }
        }

        private int SegmentFor(float distanceAlongLane)
        {
            if (CumulativeDistances.Count < 2) return 0;
            float d = Mathf.Clamp(distanceAlongLane, 0f, Length);
            for (int i = 1; i < CumulativeDistances.Count; i++)
            {
                if (d <= CumulativeDistances[i]) return i - 1;
            }
            return CumulativeDistances.Count - 2;
        }

        /// <summary>
        /// 取车道上某里程处的位置与切线。对应 UE5 <c>UE::ZoneGraph::Query::CalculateLocationAlongLane</c>。
        /// </summary>
        public void GetLocationAt(float distanceAlongLane, out Vector3 location, out Vector3 tangent)
        {
            if (Points == null || Points.Count == 0)
            {
                location = Vector3.Zero;
                tangent = Vector3.Forward;
                return;
            }
            if (Points.Count == 1)
            {
                location = Points[0];
                tangent = Tangents.Count > 0 ? Tangents[0] : Vector3.Forward;
                return;
            }

            int segment = SegmentFor(distanceAlongLane);
            float startDistance = CumulativeDistances[segment];
            float endDistance = CumulativeDistances[segment + 1];
            float span = endDistance - startDistance;
            float alpha = span > 1e-4f ? Mathf.Clamp((distanceAlongLane - startDistance) / span, 0f, 1f) : 0f;

            Vector3 a = Points[segment];
            Vector3 b = segment + 1 < Points.Count ? Points[segment + 1] : Points[0];
            location = Vector3.Lerp(a, b, alpha);
            tangent = segment < Tangents.Count ? Tangents[segment] : Vector3.Forward;
            if (tangent.LengthSquared < 1e-6f) tangent = Vector3.Forward;
        }

        /// <summary>
        /// 把世界坐标投影到车道上，返回最近里程与位置。
        /// 对应 UE5 <c>ZoneGraph.FindNearestLocationOnLane</c>（High LOD 用「Actor 实际位置」反推里程）。
        /// </summary>
        public void FindNearest(Vector3 worldLocation, out Vector3 nearestLocation, out float distanceAlongLane, out Vector3 tangent)
        {
            nearestLocation = Points != null && Points.Count > 0 ? Points[0] : Vector3.Zero;
            distanceAlongLane = 0f;
            tangent = Tangents != null && Tangents.Count > 0 ? Tangents[0] : Vector3.Forward;
            if (Points == null || Points.Count < 2) return;

            float bestDistanceSquared = float.MaxValue;
            for (int i = 0; i < Points.Count - 1; i++)
            {
                Vector3 a = Points[i];
                Vector3 b = Points[i + 1];
                Vector3 ab = b - a;
                float squaredLength = ab.LengthSquared;
                float t = squaredLength > 1e-6f
                    ? Mathf.Clamp(Vector3.Dot(worldLocation - a, ab) / squaredLength, 0f, 1f)
                    : 0f;
                Vector3 projected = a + ab * t;
                float distanceSquared = (projected - worldLocation).LengthSquared;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    nearestLocation = projected;
                    distanceAlongLane = CumulativeDistances[i] + Mathf.Sqrt(squaredLength) * t;
                    tangent = i < Tangents.Count ? Tangents[i] : Vector3.Forward;
                }
            }

            distanceAlongLane = Mathf.Clamp(distanceAlongLane, 0f, Length);
        }
    }

    /// <summary>
    /// 车道上的位置快照。对应 UE5 <c>FZoneGraphLaneLocation</c>。
    /// </summary>
    [Serializable]
    public struct TrafficLaneLocation
    {
        public ZoneGraphLaneHandle LaneHandle;
        public Vector3 Position;
        public Vector3 Tangent;
        public float DistanceAlongLane;
        public float LaneLength;
    }

    /// <summary>
    /// 障碍物记录。对应 UE5 进入 <c>FVehicleObstacleHashGrid</c> 的一个可避让对象
    /// （载具本体 + 由 <see cref="VehicleObstacleProcessor"/> 周期登记的车外对象，如行人 Actor）。
    /// </summary>
    public class VehicleObstacleRecord
    {
        public MassEntityHandle Handle;
        public Actor OwnerActor;
        public Vector3 Location;
        public Vector3 Velocity;
        public float Radius = 40f;
        public bool bIsVehicle;
        public ZoneGraphLaneHandle Lane = ZoneGraphLaneHandle.Invalid;
        public float DistanceAlongLane;
        public VehicleObstacleHashGrid.FCellLocation CellLocation;
        public bool bRegistered;
    }
}

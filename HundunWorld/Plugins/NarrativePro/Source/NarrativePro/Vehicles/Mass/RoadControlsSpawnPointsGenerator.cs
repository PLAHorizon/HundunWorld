using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 道路控制生成点生成器（Flax 侧手写实现）。对应 UE5 URoadControlsSpawnPointsGenerator
    /// （RoadControlsSpawnPointsGenerator.h/.cpp:20-164）。
    /// <para>
    /// UE5 的 Generate 流程：向 ZoneGraph 子系统取全部已注册数据 → 在车道点上铺候选位置
    /// → 若存在激活的 <c>AQuestRoadControls</c>，用它的 BoxComponent 过滤位置
    /// → 随机洗牌 → 截断/回压到请求数量（不足时打 Error 日志并夹紧）→ 分发给实体类型。
    /// Flax 无 ZoneGraph/BoxComponent：候选位置改为沿 <see cref="TrafficLaneData"/> 折线按里程采样，
    /// 过滤改用 <see cref="QuestRoadControls"/> 上的显式盒（中心 + 半边长），其余步骤逐一对应。
    /// </para>
    /// </summary>
    [Serializable]
    public class RoadControlsSpawnPointsGenerator
    {
        /// <summary>随机选择种子。对应 UE5 <c>GetRandomSelectionSeed()</c>。</summary>
        public int RandomSelectionSeed = 0;

        /// <summary>载具子系统（提供车道存储）。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>每条车道上采样的候选点数量。UE5 用车道全部点，这里按里程等分。</summary>
        public int SamplesPerLane = 2;

        /// <summary>距车道起终点的最小保留距离（世界单位），避免正好压在切道点上。</summary>
        public float LaneEndMargin = 150f;

        /// <summary>
        /// 一个候选生成点。对应 UE5 <c>FMassTransformsSpawnData</c> 里的一条变换（附带车道信息，
        /// 因为 Flax 侧必须知道落点车道才能直接写车道位置片段）。
        /// </summary>
        public struct SpawnPoint
        {
            public ZoneGraphLaneHandle Lane;
            public float DistanceAlongLane;
            public Vector3 Location;
        }

        public RoadControlsSpawnPointsGenerator(MassVehicleSubsystem vehicleSubsystem)
        {
            VehicleSubsystem = vehicleSubsystem;
        }

        /// <summary>最近一次生成的候选点数（诊断自证）。</summary>
        public int LastCandidateCount { get; private set; }

        /// <summary>最近一次被盒过滤掉的点数。</summary>
        public int LastFilteredCount { get; private set; }

        /// <summary>
        /// 生成生成点。对应 UE5 URoadControlsSpawnPointsGenerator::Generate（cpp:20-164）。
        /// </summary>
        /// <param name="count">需要的生成点数量。</param>
        /// <param name="entityTypes">请求的实体类型（取其 Amount 之和作为兜底数量）。</param>
        public List<SpawnPoint> Generate(int count, List<MassSpawnedEntityType> entityTypes = null)
        {
            var results = new List<SpawnPoint>();
            LastCandidateCount = 0;
            LastFilteredCount = 0;

            if (count <= 0 && entityTypes != null)
            {
                foreach (var type in entityTypes) count += type != null ? Mathf.Max(0, type.Amount) : 0;
            }
            if (count <= 0) return results;

            var storage = VehicleSubsystem != null ? VehicleSubsystem.Storage : null;
            if (storage == null || storage.Lanes.Count == 0)
            {
                MassVehicleLog.LogError("RoadControlsSpawnPointsGenerator：没有可用的车道存储，无法生成载具。");
                return results;
            }

            var settings = VehicleSubsystem.TrafficLightSettings ?? new TrafficLightSettings();
            string closedTag = settings.ClosedTag.Name;
            string intersectionTag = settings.IntersectionTag.Name;

            // ① 铺候选点（对应 UE5 GeneratePointsForZoneGraphData）
            foreach (var lane in storage.Lanes)
            {
                if (lane == null || lane.Points == null || lane.Points.Count < 2 || lane.Length <= 1f) continue;
                // 交叉口内车道与关闭车道不能作为出生点
                if (lane.HasTag(intersectionTag) || lane.HasTag(closedTag)) continue;

                int samples = Mathf.Max(1, SamplesPerLane);
                for (int s = 0; s < samples; s++)
                {
                    float t = (s + 1f) / (samples + 1f);
                    float distance = Mathf.Clamp(lane.Length * t, LaneEndMargin, lane.Length - LaneEndMargin);
                    if (lane.Length <= LaneEndMargin * 2f) distance = lane.Length * 0.5f;

                    lane.GetLocationAt(distance, out var position, out _);
                    results.Add(new SpawnPoint
                    {
                        Lane = storage.MakeHandle(lane.Index),
                        DistanceAlongLane = distance,
                        Location = position,
                    });
                }
            }

            LastCandidateCount = results.Count;

            // ② 用激活的道路控制器盒过滤（对应 UE5 cpp:59-82 的 IsPointInBoxWithTransform）
            var activeControls = QuestRoadControls.ActiveControls;
            if (activeControls != null && activeControls.Count > 0)
            {
                var filtered = new List<SpawnPoint>(results.Count);
                foreach (var point in results)
                {
                    if (IsInAnyActiveBox(point.Location, activeControls)) filtered.Add(point);
                }
                LastFilteredCount = results.Count - filtered.Count;
                results = filtered;
            }

            if (results.Count == 0)
            {
                MassVehicleLog.LogError("RoadControlsSpawnPointsGenerator：车道上没有落在道路控制范围内的生成点。");
                return results;
            }

            // ③ 洗牌（对应 UE5 RandomStream.RandHelper + Swap）
            var random = new Random(RandomSelectionSeed != 0 ? RandomSelectionSeed : Environment.TickCount);
            for (int i = 0; i < results.Count; i++)
            {
                int j = random.Next(results.Count);
                (results[i], results[j]) = (results[j], results[i]);
            }

            // ④ 数量回压（对应 UE5 cpp:103-113：多则截断，少则夹紧并打 Error）
            if (results.Count > count)
            {
                results.RemoveRange(count, results.Count - count);
            }
            else if (count > results.Count)
            {
                MassVehicleLog.LogWarning(
                    $"RoadControlsSpawnPointsGenerator：生成点不足，请求 {count} 个夹紧为 {results.Count} 个。");
            }

            MassVehicleLog.Log($"RoadControlsSpawnPointsGenerator：候选 {LastCandidateCount} 点，" +
                               $"过滤后 {LastCandidateCount - LastFilteredCount}，交付 {results.Count}。");
            return results;
        }

        /// <summary>点是否落在任一激活的道路控制器盒内。</summary>
        private static bool IsInAnyActiveBox(Vector3 location, List<QuestRoadControls> controls)
        {
            foreach (var control in controls)
            {
                if (control == null || !control.IsActive()) continue;
                if (control.ContainsPoint(location)) return true;
            }
            return false;
        }
    }
}

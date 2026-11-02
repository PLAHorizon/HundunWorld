using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具移动处理器（Flax 侧手写实现）。对应 UE5 UVehicleMovementProcessor
    /// （VehicleMovementProcessor.h/.cpp:24-407）。
    /// <para>
    /// UE5 用四个 Mass query 分阶段处理全部载具实体，本类保持同样的阶段与顺序：
    /// ① <see cref="CalculateNextVehicle"/> ← NextVehicleQuery（cpp:69-124，用障碍哈希找同车道/下一车道前车距离）；
    /// ② <see cref="CalculateSpeed"/> ← CalculateSpeedQuery（cpp:126-207，前车/来车/关闭车道/交叉口余量四段刹车）；
    /// ③ <see cref="CalculateLane"/> ← CalculateLaneQuery（cpp:209-327，车道末端按权重选下一车道、声明交叉口出口车位、切道）；
    /// ④ <see cref="MoveVehicles"/> ← MoveVehicleQuery（cpp:329-397，沿车道推进并写回变换/速度）。
    /// </para>
    /// <para>
    /// 与 UE5 的差值（Chaos 缺失，已核对 FlaxEngine.CSharp.xml 无 Vehicle/WheeledVehicle API）：
    /// UE5 在高 LOD 让 Chaos 载具自己按 Throttle/Steering 驱动物理、反向更新车道里程（cpp:386-395）；
    /// Flax 侧没有物理载具，因此 Throttle/Steering 两个 PID 改为驱动运动学积分——
    /// Throttle 驱动沿车道的标量速度，Steering 驱动朝车道切线的转向插值速率。
    /// 车道里程仍是唯一真相，故仍然严格贴着车道行驶。
    /// </para>
    /// </summary>
    public class VehicleMovementProcessor
    {
        /// <summary>载具子系统（车道占用、障碍网格、存储、标签设置）。对应 UE5 的 SubsystemRequirement。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>本处理器使用的载具特质（共享片段来源）。对应 UE5 的 FVehicleSettingsFragment 常量共享片段。</summary>
        public VehicleTrait Trait;

        private readonly List<ZoneGraphLaneHandle> _linkedBuffer = new List<ZoneGraphLaneHandle>();
        private readonly List<TrafficLaneLocation> _laneLocationBuffer = new List<TrafficLaneLocation>();

        public VehicleMovementProcessor(MassVehicleSubsystem vehicleSubsystem, VehicleTrait trait)
        {
            VehicleSubsystem = vehicleSubsystem;
            Trait = trait ?? new VehicleTrait();
        }

        /// <summary>本帧是否至少执行过一次完整的四阶段更新（诊断自证）。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>
        /// 每帧更新全部载具。对应 UE5 Execute（cpp:65-398）的四段 ForEachEntityChunk。
        /// </summary>
        public void UpdateTraffic(IReadOnlyList<TrafficVehicle> vehicles, float deltaTime)
        {
            if (VehicleSubsystem == null || vehicles == null || vehicles.Count == 0) return;
            if (deltaTime <= 0f) return;

            CalculateNextVehicle(vehicles);
            CalculateSpeed(vehicles);
            CalculateLane(vehicles, deltaTime);
            MoveVehicles(vehicles, deltaTime);
            UpdateCount++;
        }

        // =====================================================================
        // ① 前车距离（NextVehicleQuery）
        // =====================================================================

        /// <summary>
        /// 在障碍哈希里找本车前方最近的一辆载具（同车道或已选定的下一车道）。
        /// 对应 UE5 cpp:69-124。
        /// </summary>
        public void CalculateNextVehicle(IReadOnlyList<TrafficVehicle> vehicles)
        {
            var grid = VehicleSubsystem.VehicleObstacles;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;
                var settings = SettingsFor(vehicle);

                vehicle.Locomotion.DistanceToNextVehicle = float.MaxValue;
                vehicle.Locomotion.NextVehicleHandle = MassEntityHandle.Invalid;

                var records = grid.Query(vehicle.Location, new Vector3(settings.ObstacleSearchRadius));
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    if (record.Handle == vehicle.Handle) continue;
                    if (!record.bIsVehicle) continue;
                    if (!record.Lane.IsValid) continue;
                    if (!VehicleSubsystem.TryGetVehicle(record.Handle, out var obstacle)) continue;

                    bool bSameLane = vehicle.LaneHandle == record.Lane;
                    bool bAheadOfVehicle = vehicle.DistanceAlongLane < obstacle.DistanceAlongLane;
                    float distanceToVehicle = obstacle.DistanceAlongLane - vehicle.DistanceAlongLane;

                    // 在下一车道上的车也要算：它的里程要加上「本车剩余车道长度」
                    bool bIsInNextLane = vehicle.Locomotion.NextLane == record.Lane;
                    float distanceToNext = obstacle.DistanceAlongLane + (vehicle.LaneLength - vehicle.DistanceAlongLane);

                    if (bSameLane && bAheadOfVehicle && distanceToVehicle < vehicle.Locomotion.DistanceToNextVehicle)
                    {
                        vehicle.Locomotion.DistanceToNextVehicle = distanceToVehicle;
                        vehicle.Locomotion.NextVehicleHandle = record.Handle;
                    }
                    else if (bIsInNextLane && distanceToNext < vehicle.Locomotion.DistanceToNextVehicle)
                    {
                        vehicle.Locomotion.DistanceToNextVehicle = distanceToNext;
                        vehicle.Locomotion.NextVehicleHandle = record.Handle;
                    }
                }
            }
        }

        // =====================================================================
        // ② 期望速度（CalculateSpeedQuery）
        // =====================================================================

        /// <summary>
        /// 逐车算出 DesiredSpeed：前车 → 来车/行人 → 关闭车道（含交叉口余量不足）。
        /// 对应 UE5 cpp:126-207。
        /// </summary>
        public void CalculateSpeed(IReadOnlyList<TrafficVehicle> vehicles)
        {
            var lightSettings = VehicleSubsystem.TrafficLightSettings ?? new TrafficLightSettings();
            string closedTag = lightSettings.ClosedTag.Name;
            string intersectionTag = lightSettings.IntersectionTag.Name;
            var storage = VehicleSubsystem.Storage;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;
                var settings = SettingsFor(vehicle);

                float speed = settings.VehicleMaxSpeed;

                // cpp:147-149 —— 同/下一车道前车
                CalculateSpeedFromObstacle(vehicle.Locomotion.DistanceToNextVehicle, settings.MinimumDistanceToNext,
                    settings.BrakingDistanceFromNext, settings.NextVehicleAvoidanceBrakingPower, ref speed);

                // cpp:151-156 —— 最近非前车障碍（UE5 用 FIncomingCollisionFragment，Flax 侧从同一张障碍网格现算）
                float incomingDistance = FindIncomingObstacleDistance(vehicle, settings.ObstacleSearchRadius);
                if (incomingDistance < float.MaxValue)
                {
                    CalculateSpeedFromObstacle(incomingDistance, settings.MinimumDistanceToObstacle,
                        settings.BrakingDistanceFromObstacle, settings.ObstacleAvoidanceBrakingPower, ref speed);
                }

                // cpp:158-202 —— 下一车道关闭 / 交叉口无余量：在车道入口前刹停
                if (vehicle.Locomotion.NextLane.IsValid)
                {
                    var nextLane = storage.GetLane(vehicle.Locomotion.NextLane);
                    if (nextLane != null)
                    {
                        bool bStopAtLaneExit = nextLane.HasTag(closedTag) || !settings.VehicleLaneFilter.Pass(nextLane.Tags);

                        if (!bStopAtLaneExit && nextLane.HasTag(intersectionTag))
                        {
                            // 交叉口后的第一条车道若太挤或标签不合，就不进交叉口（防路口锁死）
                            if (storage.GetFirstLinkedLane(vehicle.Locomotion.NextLane, out var laneAfterIntersection))
                            {
                                float space = VehicleSubsystem.GetAvailableSpace(laneAfterIntersection, false);
                                if (space < lightSettings.MinimumLaneSpaceToEnter)
                                {
                                    bStopAtLaneExit = true;
                                }

                                var afterData = storage.GetLane(laneAfterIntersection);
                                if (afterData != null && !settings.VehicleLaneFilter.Pass(afterData.Tags))
                                {
                                    bStopAtLaneExit = true;
                                }
                            }
                        }

                        if (bStopAtLaneExit)
                        {
                            if (storage.CalculateLocationAlongLane(vehicle.Locomotion.NextLane, 0f, out var laneEntrance))
                            {
                                float distanceToEntrance = Vector3.Distance(laneEntrance.Position, vehicle.Location);
                                CalculateSpeedFromObstacle(distanceToEntrance, settings.ClosedLaneMinDistance,
                                    settings.ClosedLaneBrakingDistance, settings.ClosedLaneBrakingPower, ref speed);
                            }
                        }
                    }
                }

                vehicle.Locomotion.DesiredSpeed = Mathf.Clamp(speed, 0f, settings.VehicleMaxSpeed);
            }
        }

        /// <summary>
        /// 车头前方最近障碍（含行人等非载具对象）的纵向距离。
        /// 对应 UE5 的 FIncomingCollisionFragment::DistanceToObstacle 消费点（cpp:152-155）。
        /// </summary>
        private float FindIncomingObstacleDistance(TrafficVehicle vehicle, float searchRadius)
        {
            if (searchRadius <= 0f) return float.MaxValue;

            Vector3 forward = vehicle.Velocity.LengthSquared > 1e-4f
                ? Vector3.Normalize(vehicle.Velocity)
                : ForwardOf(vehicle.Rotation);

            float best = float.MaxValue;
            var records = VehicleSubsystem.VehicleObstacles.Query(vehicle.Location, new Vector3(searchRadius));
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.Handle == vehicle.Handle) continue;

                Vector3 toRecord = record.Location - vehicle.Location;
                toRecord.Y = 0f;
                float length = toRecord.Length;
                if (length < 1e-3f || length > searchRadius) continue;

                // 只看车头 60° 锥内（UE5 的 incoming collision 同样只关心前方来车）
                float alignment = Vector3.Dot(Vector3.Normalize(toRecord), forward);
                if (alignment < 0.5f) continue;

                float longitudinal = length - record.Radius;
                if (longitudinal < best) best = Mathf.Max(0f, longitudinal);
            }
            return best;
        }

        /// <summary>
        /// 根据障碍物距离压制速度。对应 UE5 CalculateSpeedFromObstacle（cpp:400-408）：
        /// <c>factor = clamp(GetRangePct(min, braking, distance),0,1)^brakingPower; speed = min(speed, speed*factor)</c>。
        /// </summary>
        public static void CalculateSpeedFromObstacle(float distanceToObstacle, float minDistanceToObstacle,
            float breakingDistanceToObstacle, float brakingPower, ref float speed)
        {
            float span = breakingDistanceToObstacle - minDistanceToObstacle;
            if (span <= 1e-4f)
            {
                // UE5 的 GetRangePct 在分母为 0 时返回未定义值；这里按「已过最小距离就全速」处理
                if (distanceToObstacle <= minDistanceToObstacle) speed = 0f;
                return;
            }

            // GetRangePct(Min, Max, Value) = clamp((Value - Min) / (Max - Min), 0, 1)
            float factor = Mathf.Clamp((distanceToObstacle - minDistanceToObstacle) / span, 0f, 1f);
            factor = Mathf.Pow(factor, Mathf.Max(1f, brakingPower));

            float maxAvoidanceSpeed = speed * factor;
            speed = Mathf.Min(speed, maxAvoidanceSpeed);
        }

        // =====================================================================
        // ③ 车道选择与切换（CalculateLaneQuery）
        // =====================================================================

        /// <summary>
        /// 接近车道末端时按权重选下一车道（含交叉口出口车位声明），真正到末端时切道。
        /// 对应 UE5 cpp:209-327。
        /// </summary>
        public void CalculateLane(IReadOnlyList<TrafficVehicle> vehicles, float deltaTime)
        {
            var storage = VehicleSubsystem.Storage;
            var lightSettings = VehicleSubsystem.TrafficLightSettings ?? new TrafficLightSettings();
            string intersectionTag = lightSettings.IntersectionTag.Name;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;
                var settings = SettingsFor(vehicle);

                // cpp:225-300 —— 快到末端且尚未选定下一车道：加权挑选
                if (!vehicle.Locomotion.NextLane.IsValid
                    && vehicle.DistanceAlongLane >= vehicle.LaneLength - settings.ClosedLaneBrakingDistance)
                {
                    storage.GetLinkedLanes(vehicle.LaneHandle, true, _linkedBuffer);
                    if (_linkedBuffer.Count > 0)
                    {
                        var weights = new List<float>(_linkedBuffer.Count);
                        var candidates = new List<ZoneGraphLaneHandle>(_linkedBuffer.Count);
                        float bestScore = 0f;

                        foreach (var candidate in _linkedBuffer)
                        {
                            var candidateData = storage.GetLane(candidate);
                            if (candidateData == null) continue;
                            if (!settings.VehicleLaneFilter.Pass(candidateData.Tags)) continue;

                            float weight = 0f;
                            storage.GetLinkedLanes(candidate, true, _laneHandleScratch);
                            foreach (var nextLinked in _laneHandleScratch)
                            {
                                var nextData = storage.GetLane(nextLinked);
                                bool bIsValidNextLane = nextData != null && settings.VehicleLaneFilter.Pass(nextData.Tags);
                                bool bHasEnoughSpace = VehicleSubsystem.GetAvailableSpace(nextLinked, true)
                                                       >= lightSettings.MinimumLaneSpaceToEnter;
                                if (bIsValidNextLane) weight += 1f;
                                if (bHasEnoughSpace) weight += 0.5f;
                            }

                            bestScore = Mathf.Max(bestScore, weight);
                            candidates.Add(candidate);
                            weights.Add(weight);
                        }

                        // 取所有最高分候选，随机一个（对应 UE5 FilterByPredicate(>=BestScore) + RandRange）
                        var bestIndexes = new List<int>();
                        for (int i = 0; i < weights.Count; i++)
                        {
                            if (weights[i] >= bestScore) bestIndexes.Add(i);
                        }

                        if (bestIndexes.Count > 0)
                        {
                            int pick = bestIndexes[vehicle.Seed.Stream != null
                                ? vehicle.Seed.Stream.Next(bestIndexes.Count)
                                : 0];
                            var chosen = candidates[pick];

                            // cpp:287-295 —— 进入交叉口前先声明出口车位，避免路口过满
                            var chosenData = storage.GetLane(chosen);
                            if (chosenData != null && chosenData.HasTag(intersectionTag))
                            {
                                if (storage.GetFirstLinkedLane(chosen, out var laneAfterIntersection) && laneAfterIntersection.IsValid)
                                {
                                    VehicleSubsystem.ClaimLaneSpot(laneAfterIntersection);
                                }
                            }

                            vehicle.Locomotion.NextLane = chosen;
                        }
                    }
                }

                // cpp:302-325 —— 真正到达末端：换道
                if (vehicle.Locomotion.NextLane.IsValid && vehicle.DistanceAlongLane >= vehicle.LaneLength)
                {
                    var currentLaneData = storage.GetLane(vehicle.LaneHandle);
                    if (currentLaneData != null && currentLaneData.HasTag(intersectionTag))
                    {
                        VehicleSubsystem.UnclaimLaneSpot(vehicle.Locomotion.NextLane);
                    }

                    VehicleSubsystem.VehicleLeaveLane(vehicle.LaneHandle, vehicle.Handle);

                    vehicle.LaneHandle = vehicle.Locomotion.NextLane;
                    vehicle.DistanceAlongLane = 0f;
                    storage.GetLaneLength(vehicle.LaneHandle, out vehicle.LaneLength);
                    vehicle.Locomotion.NextLane = ZoneGraphLaneHandle.Invalid;

                    VehicleSubsystem.VehicleEnterLane(vehicle.LaneHandle, vehicle.Handle);
                }
            }
        }

        private readonly List<ZoneGraphLaneHandle> _laneHandleScratch = new List<ZoneGraphLaneHandle>();

        // =====================================================================
        // ④ 沿车道推进（MoveVehicleQuery）
        // =====================================================================

        /// <summary>
        /// PID 驱动的运动学推进：油门把标量速度推向 DesiredSpeed，转向把朝向插值到前瞻切线。
        /// 对应 UE5 cpp:329-397（其中 Chaos 部分见类注释的差值说明）。
        /// </summary>
        public void MoveVehicles(IReadOnlyList<TrafficVehicle> vehicles, float deltaTime)
        {
            var storage = VehicleSubsystem.Storage;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;
                var trait = TraitFor(vehicle);
                var settings = trait.VehicleSettingsFragment;
                var pidSettings = trait.PIDControllerSettings;
                vehicle.AliveTime += deltaTime;

                if (!vehicle.LaneHandle.IsValid)
                {
                    // 对应 UE5 cpp:355-360 的错误分支：不在任何车道上就无法移动
                    if (storage.FindClosestLane(vehicle.Location, settings.LaneSearchRadius, out var recovered))
                    {
                        vehicle.LaneHandle = recovered.LaneHandle;
                        vehicle.LaneLength = recovered.LaneLength;
                        vehicle.DistanceAlongLane = recovered.DistanceAlongLane;
                        VehicleSubsystem.VehicleEnterLane(vehicle.LaneHandle, vehicle.Handle);
                        vehicle.bLaneLossWarned = false; // 重新入道后允许下次丢失再报一次
                    }
                    else
                    {
                        // 缺陷③：原 (int)AliveTime % 5 门控让每辆丢道车每 5 秒刷一条告警；
                        // 改为每辆车只报一次（重新入道时复位），告警信息量不变、噪声归零。
                        if (vehicle.AliveTime > 1f && !vehicle.bLaneLossWarned)
                        {
                            vehicle.bLaneLossWarned = true;
                            MassVehicleLog.LogWarning($"载具 {vehicle.Handle} 找不到附近车道，" +
                                                      "请确认车道已构建且生成点落在车道附近。");
                        }
                        vehicle.Speed = 0f;
                        vehicle.Velocity = Vector3.Zero;
                        continue;
                    }
                }

                // 油门：goal=期望速度, actual=当前速度。对应 UE5 高 LOD 的 Throttle PID → Chaos 输入
                float throttle = vehicle.Locomotion.ThrottlePIDController.Tick(
                    vehicle.Locomotion.DesiredSpeed, vehicle.Speed, pidSettings.ThrottleSettings);
                vehicle.Locomotion.Throttle = throttle;

                // 唯一的积分点：运动学移动模型（UE5 里这一步是 Chaos 受力积分）
                vehicle.Movement.Throttle = Mathf.Clamp(throttle, -1f, 1f);
                vehicle.Speed = vehicle.Movement.Integrate(vehicle.Locomotion.DesiredSpeed, throttle,
                    trait.ThrottleAuthority, settings.VehicleMaxSpeed, deltaTime);

                // 里程推进（UE5 用 DesiredSpeed 直接推进，这里用 PID 收敛后的实际速度）
                vehicle.DistanceAlongLane = Mathf.Min(vehicle.LaneLength, vehicle.DistanceAlongLane + vehicle.Speed * deltaTime);

                if (!storage.CalculateLocationAlongLane(vehicle.LaneHandle, vehicle.DistanceAlongLane, out var laneLocation))
                {
                    continue;
                }

                // 前瞻点切线作为期望朝向。对应 UE5 LookAheadDistance 的用法
                Vector3 desiredTangent = laneLocation.Tangent;
                float lookAheadDistance = Mathf.Max(1f, pidSettings.LookAheadDistance);
                if (storage.CalculateLocationAlongLane(vehicle.LaneHandle,
                        Mathf.Min(vehicle.LaneLength, vehicle.DistanceAlongLane + lookAheadDistance), out var lookAhead))
                {
                    desiredTangent = lookAhead.Tangent;
                }

                Quaternion targetRotation = VehicleTrait.RotationFromTangent(desiredTangent);

                // 转向：把角度误差交给 Steering PID，其输出决定本帧允许的姿态收敛速率
                // Flax 的 Quaternion.Angle 是属性不是方法，这里按 UE5 口径用前向向量夹角（度）
                Vector3 currentForward = Vector3.Transform(Vector3.Forward, vehicle.Rotation);
                Vector3 targetForward = Vector3.Transform(Vector3.Forward, targetRotation);
                float facingDot = Mathf.Clamp(Vector3.Dot(currentForward, targetForward), -1f, 1f);
                float angleError = (float)(System.Math.Acos(facingDot) * (180.0 / System.Math.PI));
                float steering = vehicle.Locomotion.SteeringPIDController.Tick(angleError, 0f, pidSettings.SteeringSettings);
                vehicle.Locomotion.Steering = steering;

                float interpSpeed = Mathf.Clamp(Mathf.Abs(steering), 1f, trait.MaxSteerRateDegrees);
                float alpha = Mathf.Clamp(interpSpeed * deltaTime / Mathf.Max(1e-3f, angleError), 0f, 1f);
                vehicle.Rotation = Quaternion.Slerp(vehicle.Rotation, targetRotation, alpha);

                // 位置插值。对应 UE5 VInterpTo(current, lanePosition, dt, 1) + 高度取车道值
                float locationAlpha = Mathf.Clamp(deltaTime, 0f, 1f);
                Vector3 interpolated = vehicle.Location + (laneLocation.Position - vehicle.Location) * locationAlpha;
                interpolated.Y = laneLocation.Position.Y;
                vehicle.Location = interpolated;

                vehicle.Velocity = desiredTangent * vehicle.Speed;
                vehicle.Movement.Velocity = vehicle.Velocity;
                vehicle.Movement.Steering = Mathf.Clamp(steering / Mathf.Max(1f, trait.MaxSteerRateDegrees), -1f, 1f);

                // 高 LOD 由 Actor 承载：写回 Actor（UE5 是反向：Chaos 驱 Actor，再由 Actor 反推里程）
                vehicle.ComponentWrapper.Speed = vehicle.Speed;
                vehicle.ApplyToActor();

                // 环道回到起点
                var laneData = storage.GetLane(vehicle.LaneHandle);
                if (laneData != null && laneData.bLooping && vehicle.DistanceAlongLane >= vehicle.LaneLength - 1f)
                {
                    vehicle.DistanceAlongLane = 0f;
                }
            }
        }

        /// <summary>实体特质（缺失时回落到处理器默认）。对应 UE5 的 ConstSharedFragment 读取。</summary>
        private VehicleTrait TraitFor(TrafficVehicle vehicle)
        {
            return vehicle != null && vehicle.Trait != null ? vehicle.Trait : Trait;
        }

        /// <summary>实体的载具设置片段。对应 UE5 <c>GetConstSharedFragment&lt;FVehicleSettingsFragment&gt;()</c>。</summary>
        private VehicleSettingsFragment SettingsFor(TrafficVehicle vehicle)
        {
            return TraitFor(vehicle).VehicleSettingsFragment;
        }

        /// <summary>取四元数的前向（Flax 前向为 +Z，地面投影到 XZ）。</summary>
        private static Vector3 ForwardOf(Quaternion rotation)
        {
            Vector3 forward = Vector3.Transform(Vector3.Forward, rotation);
            forward.Y = 0f;
            if (forward.LengthSquared < 1e-6f) return Vector3.Forward;
            return Vector3.Normalize(forward);
        }
    }
}

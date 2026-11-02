using FlaxEngine;

namespace NarrativePro.GAS.AbilityTasks
{
    /// <summary>
    /// 移动到位置并等待到达的能力任务。对应 UE5 UAbilityTask_MoveToLocationAndWait。
    /// <para>
    /// UE5 走 <c>UActorMovementComponent::RequestMoveTo</c>（内部是 NavMesh 寻路 + 到达回调）。
    /// Flax 对应物是 <see cref="NarrativePro.Navigation.NavigatorStatics"/> 的 <c>TryFindPath</c>：本实现优先沿 NavMesh 折点移动，
    /// 场景没有可用导航网格（或寻路失败）时退化为直线移动，保证行为仍可用。
    /// </para>
    /// - 到达阈值 StopDistance 决定完成时机；
    /// - 移动由 Actor.Position 直接推进（与 UE5 的 MovementComponent 驱动等价）。
    /// </summary>
    public class AbilityTask_MoveToLocationAndWait : AbilityTask
    {
        /// <summary>目标位置。</summary>
        public Vector3 Location = Vector3.Zero;

        /// <summary>移动速度。</summary>
        public float MoveSpeed = 500f;

        /// <summary>是否在到达后停止。</summary>
        public bool bStopAtLocation = true;

        /// <summary>是否朝向目标位置。</summary>
        public bool bFaceLocation = false;

        /// <summary>停止距离阈值（cm）。</summary>
        public float StopDistance = 10f;

        /// <summary>到达后等待时间（秒）。</summary>
        public float WaitTime = 0f;

        /// <summary>是否允许使用 NavMesh 寻路（关掉就永远走直线，便于对照调试）。</summary>
        public bool bUseNavMesh = true;

        private float _waitElapsed = 0f;
        private bool _bWaiting = false;

        /// <summary>NavMesh 折点队列（当前正在朝其移动的下标）。</summary>
        private Vector3[] _pathCorners;
        private int _pathIndex = 0;

        /// <summary>创建任务实例。</summary>
        public static AbilityTask_MoveToLocationAndWait Create(NarrativeGameplayAbility ability, Vector3 location, float speed = 500f, float stopDistance = 10f)
        {
            var task = new AbilityTask_MoveToLocationAndWait
            {
                OwningAbility = ability,
                Location = location,
                MoveSpeed = speed,
                StopDistance = stopDistance
            };
            return task;
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!bIsActive || bIsComplete) return;
            if (OwningAbility?.Actor == null) { Complete(); return; }

            if (!_bWaiting)
            {
                // 移动阶段
                var actor = OwningAbility.Actor;

                // 本帧目标：有导航路径时是下一个折点，否则是终点
                Vector3 moveTarget = ResolveMoveTarget(actor.Position);
                Vector3 toTarget = moveTarget - actor.Position;
                float distSq = toTarget.LengthSquared;
                bool bAtFinalDestination = Vector3.DistanceSquared(actor.Position, Location) <= StopDistance * StopDistance;

                if (distSq <= StopDistance * StopDistance)
                {
                    // 到达本段目标：推进折点游标
                    if (_pathCorners != null && _pathIndex < _pathCorners.Length - 1)
                    {
                        _pathIndex++;
                        return;
                    }

                    if (!bAtFinalDestination && _pathCorners != null)
                    {
                        // 路径走完了但还没到终点（NavMesh 只给出部分路径）：退回直线
                        _pathCorners = null;
                        _pathIndex = 0;
                        return;
                    }

                    // 到达
                    if (bStopAtLocation)
                    {
                        actor.Position = Location;
                    }

                    if (WaitTime > 0f)
                    {
                        _bWaiting = true;
                        _waitElapsed = 0f;
                    }
                    else
                    {
                        Complete();
                    }
                    return;
                }

                // 朝向：沿本段方向转，最后一段自然朝向终点
                Vector3 forward = distSq > 0.0001f ? toTarget / (float)System.Math.Sqrt(distSq) : Vector3.Forward;
                if (bFaceLocation)
                {
                    Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.Up);
                    actor.Orientation = Quaternion.Slerp(actor.Orientation, lookRot, Mathf.Clamp(deltaTime * 10f, 0f, 1f));
                }

                // 按速度推进
                float step = MoveSpeed * deltaTime;
                if (step * step >= distSq)
                {
                    actor.Position = moveTarget;
                }
                else
                {
                    actor.Position = actor.Position + forward * step;
                }
            }
            else
            {
                // 等待阶段
                _waitElapsed += deltaTime;
                if (_waitElapsed >= WaitTime)
                {
                    Complete();
                }
            }
        }

        /// <summary>
        /// 取本帧应移动到的位置：导航折点优先，没有可用路径则直接是终点。
        /// </summary>
        private Vector3 ResolveMoveTarget(Vector3 from)
        {
            if (!bUseNavMesh) return Location;

            if (_pathCorners == null)
            {
                _pathCorners = TryBuildNavPath(from, Location);
                _pathIndex = 0;
            }

            if (_pathCorners == null || _pathCorners.Length == 0) return Location;
            if (_pathIndex >= _pathCorners.Length) return Location;

            return _pathCorners[_pathIndex];
        }

        /// <summary>
        /// 请求 NavMesh 路径。无导航网格/寻路失败时返回 null，调用方按直线处理。
        /// </summary>
        private static Vector3[] TryBuildNavPath(Vector3 start, Vector3 end)
        {
            if (NarrativePro.Navigation.NavigatorStatics.TryFindPath(start, end, out Vector3[] corners) && corners != null && corners.Length > 0)
            {
                return corners;
            }
            return null;
        }

        /// <summary>本任务当前是否在沿 NavMesh 路径移动。</summary>
        public bool IsUsingNavMeshPath => _pathCorners != null && _pathCorners.Length > 0;
    }
}

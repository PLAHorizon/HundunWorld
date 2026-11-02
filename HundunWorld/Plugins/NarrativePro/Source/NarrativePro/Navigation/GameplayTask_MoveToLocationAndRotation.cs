using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 目标到达模式。适配 UE5 EGoalReachMode。
    /// </summary>
    public enum EGoalReachMode
    {
        /// <summary>仅使用 AcceptanceRadius</summary>
        ExactLocation = 0,
        /// <summary>使用 AcceptanceRadius + 修正后的 agent 半径</summary>
        OverlapAgent = 1,
        /// <summary>使用 AcceptanceRadius + 目标 actor 半径</summary>
        OverlapGoal = 2,
        /// <summary>使用 AcceptanceRadius + agent 半径 + 目标 actor 半径</summary>
        OverlapAgentAndGoal = 3
    }

    /// <summary>
    /// 路径跟随结果。适配 UE5 EPathFollowingResult。
    /// </summary>
    public enum EPathFollowingResult
    {
        Invalid = 0,
        Success = 1,
        Blocked = 2,
        OffPath = 3,
        Aborted = 4
    }

    /// <summary>
    /// 移动到指定位置并旋转的任务。适配 UE5 UGameplayTask_MoveToLocationAndRotation。
    /// <para>
    /// UE5 通过 <c>UArsenalStatics::MoveToLocationExtended</c> 交给 PathFollowingComponent 驱动；
    /// Flax 没有 PathFollowingComponent，因此这里直接取 <see cref="NavigatorStatics.TryFindPath"/>
    /// 的导航网格拐点并逐拐点转向推进，等价于路径跟随。导航未就绪时退化为直接推进，
    /// 与 <c>NavigatorStatics.TestPath</c> 的「未就绪按未知处理」降级语义一致。
    /// </para>
    /// </summary>
    public class MoveToLocationAndRotationTask
    {
        /// <summary>任务拥有者</summary>
        public Actor TaskOwner { get; private set; }

        /// <summary>目标位置</summary>
        public Vector3 DesiredLocation { get; private set; }

        /// <summary>目标旋转</summary>
        public Quaternion DesiredRotation { get; private set; }

        /// <summary>旋转插值速度（度/秒，对应 UE5 RInterpConstantTo 的 MaxSpeed）</summary>
        public float RotationInterpSpeed { get; private set; }

        /// <summary>旋转插值模式：true=恒速（对应 UE5 RInterpConstantTo），false=平滑（对应 RInterpTo）</summary>
        public bool bConstantRotationInterp = true;

        /// <summary>到达判定模式</summary>
        public EGoalReachMode GoalReachMode { get; private set; }

        /// <summary>移动完成后是否结束任务</summary>
        public bool bEndWhenFinishedMove { get; private set; }

        /// <summary>到达容差半径</summary>
        public float AcceptanceRadius { get; set; } = 50f;

        /// <summary>Agent 半径（OverlapAgent 系列模式下叠加到 AcceptanceRadius）</summary>
        public float AgentRadius { get; set; } = 35f;

        /// <summary>目标 Actor 半径（OverlapGoal 系列模式下叠加到 AcceptanceRadius）</summary>
        public float GoalActorRadius { get; set; } = 0f;

        /// <summary>移动速度（世界单位/秒）</summary>
        public float MoveSpeed { get; set; } = 600f;

        /// <summary>旋转完成判定容差（度）</summary>
        public float RotationTolerance { get; set; } = 1f;

        /// <summary>是否使用导航网格路径。关闭后为直接推进。</summary>
        public bool bUseNavMeshPath = true;

        // 状态
        public bool bStartedMoving { get; private set; } = false;
        public bool bFinishedMoving { get; private set; } = false;
        public bool bFinishedRotating { get; private set; } = false;

        /// <summary>任务是否仍在运行</summary>
        public bool IsRunning { get; private set; } = false;

        /// <summary>当前正在跟随的导航拐点（供调试与面包屑绘制）。</summary>
        public IReadOnlyList<Vector3> CurrentPath => _path;

        // 事件
        public event Action<EPathFollowingResult> OnCompleted;
        public event Action<EPathFollowingResult> OnFailed;

        // 内部状态
        private readonly List<Vector3> _path = new List<Vector3>();
        private int _pathCursor;
        private bool _bTaskEnded;

        public MoveToLocationAndRotationTask(Actor taskOwner, Vector3 targetLocation, Quaternion targetRotation,
            float interpSpeed, EGoalReachMode goalReachMode = EGoalReachMode.ExactLocation, bool bEndWhenFinishedMove = true)
        {
            TaskOwner = taskOwner;
            DesiredLocation = targetLocation;
            DesiredRotation = targetRotation;
            RotationInterpSpeed = interpSpeed;
            GoalReachMode = goalReachMode;
            this.bEndWhenFinishedMove = bEndWhenFinishedMove;
        }

        /// <summary>
        /// 激活任务。对应 UE5 Activate：解析路径，无法开始时立即失败。
        /// </summary>
        public void Activate()
        {
            if (TaskOwner == null)
            {
                Fail(EPathFollowingResult.Invalid);
                return;
            }

            IsRunning = true;
            bStartedMoving = true;
            bFinishedMoving = false;
            bFinishedRotating = false;
            _bTaskEnded = false;
            _path.Clear();
            _pathCursor = 0;

            Vector3 start = TaskOwner.Position;

            // 已经在目标点：直接判定移动完成（UE5 中 MoveRequestID 为 Invalid 时的特例）
            if (Vector3.Distance(start, DesiredLocation) <= GetEffectiveAcceptanceRadius())
            {
                FinishMove(EPathFollowingResult.Success);
                if (bEndWhenFinishedMove)
                {
                    FinishedRotating();
                }
                return;
            }

            if (bUseNavMeshPath && NavigatorStatics.IsNavigationReady())
            {
                if (NavigatorStatics.TryFindPath(start, DesiredLocation, out var corners) && corners != null && corners.Length > 0)
                {
                    _path.AddRange(corners);
                    // 首个拐点通常是起点自身，跳过它
                    if (_path.Count > 1 && Vector3.Distance(_path[0], start) < 1f) _pathCursor = 1;
                }
                else
                {
                    // 导航就绪却求不出路径：目标不可达（对应 UE5 的 Blocked/Failed 回调）
                    Fail(EPathFollowingResult.Blocked);
                    return;
                }
            }
        }

        /// <summary>每帧推进任务。需要由外部（通常是 Script.OnUpdate）调用。</summary>
        public void TickTask(float deltaTime)
        {
            if (!IsRunning || _bTaskEnded || TaskOwner == null) return;

            if (!bFinishedMoving)
            {
                AdvanceMovement(deltaTime);

                float reachRadius = GetEffectiveAcceptanceRadius();
                if (Vector3.Distance(TaskOwner.Position, DesiredLocation) <= reachRadius)
                {
                    bFinishedMoving = true;
                    FinishMove(EPathFollowingResult.Success);

                    if (bEndWhenFinishedMove)
                    {
                        FinishedRotating();
                        return;
                    }
                }
            }

            if (bFinishedMoving && !bFinishedRotating)
            {
                AdvanceRotation(deltaTime);
            }
        }

        /// <summary>沿路径（或直指目标）推进一步。</summary>
        private void AdvanceMovement(float deltaTime)
        {
            Vector3 currentPos = TaskOwner.Position;

            // 选取当前跟随点：路径模式下取下一个尚未到达的拐点
            Vector3 waypoint = DesiredLocation;
            bool bIsFinalWaypoint = true;

            while (_pathCursor < _path.Count)
            {
                Vector3 candidate = _path[_pathCursor];
                // 拐点吸附（略高于到达容差）后才推进游标，避免在拐点附近抖动
                float snapRadius = Mathf.Max(AcceptanceRadius * 0.5f, 1f);
                if (Vector3.DistanceSquared(currentPos, candidate) <= snapRadius * snapRadius)
                {
                    _pathCursor++;
                    continue;
                }
                waypoint = candidate;
                bIsFinalWaypoint = _pathCursor == _path.Count - 1 && Vector3.DistanceSquared(candidate, DesiredLocation) < 1f;
                break;
            }

            Vector3 toTarget = waypoint - currentPos;
            toTarget.Y = 0f; // XZ 平面推进，高度由角色的地面对齐负责
            float distance = toTarget.Length;
            if (distance <= 0.0001f) return;

            Vector3 moveDir = toTarget / distance;
            float step = MoveSpeed * deltaTime;
            if (step >= distance)
            {
                if (bIsFinalWaypoint || _pathCursor >= _path.Count)
                {
                    // 直接把 Actor 放到目标点，避免剩余步长丢失
                    Vector3 arrived = waypoint;
                    arrived.Y = DesiredLocation.Y;
                    TaskOwner.Position = arrived;
                }
                else
                {
                    TaskOwner.Position = waypoint;
                }
            }
            else
            {
                TaskOwner.Position = currentPos + moveDir * step;
            }

            // 移动中朝前进方向转身（除非移动已完成、正等待目标旋转）
            if (!bIsFinalWaypoint && moveDir.LengthSquared > 0.001f)
            {
                Quaternion lookRot = Quaternion.LookRotation(moveDir, Vector3.Up);
                TaskOwner.Orientation = RotateToward(TaskOwner.Orientation, lookRot, deltaTime, smooth: true);
            }
        }

        /// <summary>移动到位后把朝向插值到目标旋转。对应 UE5 TickTask 的旋转分支。</summary>
        private void AdvanceRotation(float deltaTime)
        {
            Quaternion currentRot = TaskOwner.Orientation;

            // 只改 yaw，保留 pitch/roll（对应 UE5 的 DesiredRotation.Pitch/Roll = CharRot.Pitch/Roll）
            float currentYaw = ExtractYaw(currentRot);
            float desiredYaw = ExtractYaw(DesiredRotation);
            float newYaw = RotateYawConstantToward(currentYaw, desiredYaw, RotationInterpSpeed, deltaTime);

            Quaternion target = Quaternion.Lerp(
                Quaternion.Euler(0f, currentYaw, 0f),
                Quaternion.Euler(0f, desiredYaw, 0f),
                1f);
            TaskOwner.Orientation = MultiplyYawOnto(currentRot, target);

            if (Mathf.Abs(NormalizeYaw(desiredYaw - currentYaw)) <= RotationTolerance)
            {
                bFinishedRotating = true;
                FinishedRotating();
            }
        }

        /// <summary>把 rot 上的 yaw 差叠加到 base 上（保持 base 的 pitch/roll）。</summary>
        private static Quaternion MultiplyYawOnto(Quaternion baseRot, Quaternion yawOnly)
        {
            Vector3 e = baseRot.EulerAngles;
            Vector3 y = yawOnly.EulerAngles;
            return Quaternion.Euler(e.X, y.Y, e.Z);
        }

        private static float ExtractYaw(Quaternion rotation)
        {
            return rotation.EulerAngles.Y;
        }

        private static float NormalizeYaw(float yaw)
        {
            while (yaw > 180f) yaw -= 360f;
            while (yaw < -180f) yaw += 360f;
            return yaw;
        }

        private static float RotateYawConstantToward(float current, float target, float maxSpeedDegPerSec, float deltaTime)
        {
            float diff = NormalizeYaw(target - current);
            if (maxSpeedDegPerSec <= 0f) return target;
            float maxStep = maxSpeedDegPerSec * deltaTime;
            if (Mathf.Abs(diff) <= maxStep) return target;
            return current + Math.Sign(diff) * maxStep;
        }

        private Quaternion RotateToward(Quaternion current, Quaternion target, float deltaTime, bool smooth)
        {
            if (smooth || RotationInterpSpeed <= 0f)
            {
                float alpha = smooth ? Mathf.Clamp(RotationInterpSpeed * deltaTime * 0.1f, 0f, 1f) : 1f;
                return Quaternion.Lerp(current, target, alpha);
            }
            return current;
        }

        /// <summary>按到达模式折算实际容差半径。</summary>
        public float GetEffectiveAcceptanceRadius()
        {
            switch (GoalReachMode)
            {
                case EGoalReachMode.OverlapAgent: return AcceptanceRadius + AgentRadius;
                case EGoalReachMode.OverlapGoal: return AcceptanceRadius + GoalActorRadius;
                case EGoalReachMode.OverlapAgentAndGoal: return AcceptanceRadius + AgentRadius + GoalActorRadius;
                default: return AcceptanceRadius;
            }
        }

        /// <summary>是否已完成移动。对应 UE5 HasFinishedMove（UE5 实际返回 bFinishedRotating）。</summary>
        public bool HasFinishedMove() => bFinishedRotating;

        /// <summary>完成移动。对应 UE5 FinishMove：只接受本任务的路径请求结果。</summary>
        protected virtual void FinishMove(EPathFollowingResult result)
        {
            if (result != EPathFollowingResult.Success)
            {
                Fail(result);
                return;
            }
            bFinishedMoving = true;
        }

        /// <summary>完成旋转。</summary>
        protected virtual void FinishedRotating()
        {
            bFinishedRotating = true;
            if (_bTaskEnded) return;

            OnCompleted?.Invoke(EPathFollowingResult.Success);
            IsRunning = false;

            // bEndWhenFinishedMove 为 false 时任务保活，由外部 Destroy 结束（对应 UE5 不 EndTask）
            if (bEndWhenFinishedMove)
            {
                _bTaskEnded = true;
            }
        }

        /// <summary>失败并终止任务。</summary>
        protected void Fail(EPathFollowingResult result)
        {
            if (_bTaskEnded) return;
            _bTaskEnded = true;
            IsRunning = false;
            bStartedMoving = false;
            OnFailed?.Invoke(result);
        }

        /// <summary>销毁任务（对应 UE5 EndTask / OnDestroy）。</summary>
        public void Destroy()
        {
            _path.Clear();
            _pathCursor = 0;
            _bTaskEnded = true;
            IsRunning = false;
        }

        /// <summary>静态工厂：创建移动并旋转任务。</summary>
        public static MoveToLocationAndRotationTask MoveToLocationAndRotation(
            Actor taskOwner, Vector3 targetLocation, Quaternion targetRotation,
            float interpSpeed, EGoalReachMode goalReachMode = EGoalReachMode.ExactLocation,
            bool bEndWhenFinishedMove = true)
        {
            return new MoveToLocationAndRotationTask(taskOwner, targetLocation, targetRotation, interpSpeed, goalReachMode, bEndWhenFinishedMove);
        }
    }
}

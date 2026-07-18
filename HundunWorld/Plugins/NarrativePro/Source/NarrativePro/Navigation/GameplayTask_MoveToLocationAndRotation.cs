using System;
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
        Success = 0,
        Blocked = 1,
        OffPath = 2,
        Aborted = 3
    }

    /// <summary>
    /// 移动到指定位置并旋转的任务。适配 UE5 UGameplayTask_MoveToLocationAndRotation。
    /// Flax 中作为 Script 协程任务，简化为：先调用 NavMesh 路径移动，再插值旋转。
    /// 移动完成后触发 OnCompleted，失败触发 OnFailed。
    /// </summary>
    public class MoveToLocationAndRotationTask
    {
        /// <summary>任务拥有者</summary>
        public Actor TaskOwner { get; private set; }

        /// <summary>目标位置</summary>
        public Vector3 DesiredLocation { get; private set; }

        /// <summary>目标旋转</summary>
        public Quaternion DesiredRotation { get; private set; }

        /// <summary>旋转插值速度</summary>
        public float RotationInterpSpeed { get; private set; }

        /// <summary>到达判定模式</summary>
        public EGoalReachMode GoalReachMode { get; private set; }

        /// <summary>移动完成后是否结束任务</summary>
        public bool bEndWhenFinishedMove { get; private set; }

        /// <summary>到达容差半径</summary>
        public float AcceptanceRadius { get; set; } = 50f;

        // 状态
        public bool bStartedMoving { get; private set; } = false;
        public bool bFinishedMoving { get; private set; } = false;
        public bool bFinishedRotating { get; private set; } = false;

        /// <summary>任务是否仍在运行</summary>
        public bool IsRunning { get; private set; } = false;

        // 事件
        public event Action<EPathFollowingResult> OnCompleted;
        public event Action<EPathFollowingResult> OnFailed;

        // 内部状态
        private float _currentMoveSpeed = 600f; // 默认移动速度
        private float _rotationTolerance = 1f; // 旋转容差（度）

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

        /// <summary>激活任务。</summary>
        public void Activate()
        {
            if (TaskOwner == null)
            {
                OnFailed?.Invoke(EPathFollowingResult.Aborted);
                return;
            }
            IsRunning = true;
            bStartedMoving = true;

            // 注意：实际移动逻辑需要由外部的 Update 调用每帧推进
            // 此处仅标记任务激活
        }

        /// <summary>每帧推进任务。需要由外部（通常是 Script.OnUpdate）调用。</summary>
        public void TickTask(float deltaTime)
        {
            if (!IsRunning || TaskOwner == null) return;

            // 移动逻辑
            if (!bFinishedMoving)
            {
                Vector3 currentPos = TaskOwner.Position;
                Vector3 toTarget = DesiredLocation - currentPos;
                float distance = toTarget.Length;

                if (distance <= AcceptanceRadius)
                {
                    bFinishedMoving = true;
                    FinishMove(EPathFollowingResult.Success);
                    if (bEndWhenFinishedMove)
                    {
                        // 立即结束任务
                        bFinishedRotating = true;
                        FinishedRotating();
                        return;
                    }
                }
                else
                {
                    // 简单直线移动
                    Vector3 moveDir = Vector3.Normalize(toTarget);
                    Vector3 moveStep = moveDir * _currentMoveSpeed * deltaTime;
                    if (moveStep.Length > distance)
                    {
                        TaskOwner.Position = DesiredLocation;
                    }
                    else
                    {
                        TaskOwner.Position = currentPos + moveStep;
                    }

                    // 朝向移动方向（如果不需要最终旋转，或还未到达）
                    if (moveDir.LengthSquared > 0.001f)
                    {
                        Quaternion lookRot = Quaternion.LookRotation(moveDir, Vector3.Up);
                        TaskOwner.Orientation = Quaternion.Slerp(TaskOwner.Orientation, lookRot, deltaTime * RotationInterpSpeed);
                    }
                }
            }

            // 旋转逻辑
            if (bFinishedMoving && !bFinishedRotating)
            {
                Quaternion currentRot = TaskOwner.Orientation;
                // Quaternion.Angle 是属性，使用点积计算角度差
                float dot = (float)Math.Clamp(Quaternion.Dot(currentRot, DesiredRotation), -1f, 1f);
                float angleDiff = (float)Math.Acos(Math.Abs(dot)) * 2f * Mathf.RadiansToDegrees;

                if (angleDiff <= _rotationTolerance)
                {
                    TaskOwner.Orientation = DesiredRotation;
                    bFinishedRotating = true;
                    FinishedRotating();
                }
                else
                {
                    TaskOwner.Orientation = Quaternion.Slerp(currentRot, DesiredRotation, deltaTime * RotationInterpSpeed);
                }
            }
        }

        /// <summary>是否已完成移动。</summary>
        public bool HasFinishedMove() => bFinishedMoving;

        /// <summary>完成移动。</summary>
        protected virtual void FinishMove(EPathFollowingResult result)
        {
            // 默认实现：触发完成事件
        }

        /// <summary>完成旋转。</summary>
        protected virtual void FinishedRotating()
        {
            IsRunning = false;
            OnCompleted?.Invoke(EPathFollowingResult.Success);
        }

        /// <summary>销毁任务。</summary>
        public void Destroy()
        {
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

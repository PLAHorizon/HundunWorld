using FlaxEngine;

namespace NarrativePro.GAS.AbilityTasks
{
    /// <summary>
    /// 移动到位置并等待到达的能力任务。对应 UE5 UAbilityTask_MoveToLocationAndWait。
    /// 简化点：
    /// - 不依赖 NavMesh，使用直线移动（Flax-不兼容: 当前未接入 Flax NavMesh，保留直线移动。原文 TODO: 接入 Flax NavMesh）
    /// - 移动由 Actor.Position 直接插值
    /// - 到达阈值 StopDistance 决定完成时机
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

        private float _waitElapsed = 0f;
        private bool _bWaiting = false;

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
                Vector3 current = actor.Position;
                Vector3 toTarget = Location - current;
                float distSq = toTarget.LengthSquared;

                if (distSq <= StopDistance * StopDistance)
                {
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

                // 朝向目标
                if (bFaceLocation)
                {
                    Vector3 forward = toTarget.Normalized;
                    // 使用 Quaternion.LookRotation 计算朝向旋转并平滑插值
                    Quaternion lookRot = Quaternion.LookRotation(forward, Vector3.Up);
                    actor.Orientation = Quaternion.Slerp(actor.Orientation, lookRot, Mathf.Clamp(deltaTime * 10f, 0f, 1f));
                }

                // 直线移动
                float step = MoveSpeed * deltaTime;
                if (step * step >= distSq)
                {
                    actor.Position = Location;
                }
                else
                {
                    actor.Position = current + toTarget.Normalized * step;
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
    }
}

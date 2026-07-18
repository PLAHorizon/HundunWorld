using FlaxEngine;

namespace NarrativePro.GAS.AbilityTasks
{
    /// <summary>
    /// 旋转 Actor 朝向目标的能力任务。对应 UE5 UAbilityTask_RotateActor。
    /// 简化点：
    /// - 仅修改 Yaw 旋转
    /// - 旋转由插值实现（Quaternion.Slerp）
    /// </summary>
    public class AbilityTask_RotateActor : AbilityTask
    {
        /// <summary>目标位置（朝向此位置）。</summary>
        public Vector3 TargetLocation = Vector3.Zero;

        /// <summary>旋转速度（度/秒）。</summary>
        public float RotationSpeed = 360f;

        /// <summary>是否朝向目标。</summary>
        public bool bFaceTarget = true;

        /// <summary>旋转完成后等待时间（秒）。</summary>
        public float WaitTime = 0f;

        private float _waitElapsed = 0f;
        private bool _bWaiting = false;
        private Quaternion _targetRotation = Quaternion.Identity;

        /// <summary>创建任务实例。</summary>
        public static AbilityTask_RotateActor Create(NarrativeGameplayAbility ability, Vector3 targetLocation, float rotationSpeed = 360f)
        {
            var task = new AbilityTask_RotateActor
            {
                OwningAbility = ability,
                TargetLocation = targetLocation,
                RotationSpeed = rotationSpeed
            };
            return task;
        }

        public override void Activate()
        {
            base.Activate();
            if (OwningAbility?.Actor == null) { Complete(); return; }

            // 计算目标旋转（朝向 TargetLocation，仅 Yaw）
            Vector3 current = OwningAbility.Actor.Position;
            Vector3 toTarget = TargetLocation - current;
            if (toTarget.LengthSquared < 0.01f)
            {
                // 已在目标位置
                if (WaitTime > 0f) { _bWaiting = true; _waitElapsed = 0f; }
                else Complete();
                return;
            }

            float yaw = Mathf.Atan2(toTarget.X, toTarget.Z) * Mathf.RadiansToDegrees;
            _targetRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!bIsActive || bIsComplete) return;
            if (OwningAbility?.Actor == null) { Complete(); return; }

            if (!_bWaiting)
            {
                // 旋转阶段
                var actor = OwningAbility.Actor;
                Quaternion current = actor.Orientation;
                // 计算两个 quaternion 之间的角度差：dot→acos→×2
                float dot = Mathf.Clamp(Quaternion.Dot(current, _targetRotation), -1f, 1f);
                float angleDiff = Mathf.Acos(Mathf.Abs(dot)) * 2f * Mathf.RadiansToDegrees;

                if (angleDiff < 1f)
                {
                    actor.Orientation = _targetRotation;
                    if (WaitTime > 0f) { _bWaiting = true; _waitElapsed = 0f; }
                    else Complete();
                    return;
                }

                float maxStep = RotationSpeed * deltaTime;
                if (maxStep >= angleDiff)
                {
                    actor.Orientation = _targetRotation;
                }
                else
                {
                    float t = maxStep / angleDiff;
                    actor.Orientation = Quaternion.Slerp(current, _targetRotation, t);
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

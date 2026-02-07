using System;
using FlaxEngine;

namespace Game.Combat.Effects
{
    /// <summary>
    /// 相机震动系统
    /// 用于战斗打击反馈和环境特效的相机震动效果
    /// </summary>
    public class CameraShakeSystem : Script
    {
        /// <summary>
        /// 震动强度预设
        /// </summary>
        public enum ShakeIntensity
        {
            None = 0,
            VeryLight = 1,      // 极轻微 - 轻击命中
            Light = 2,          // 轻微 - 普通攻击
            Medium = 3,         // 中等 - 重击
            Strong = 4,         // 强烈 - 大招
            VeryStrong = 5,     // 极强 - Boss技能
            Extreme = 6         // 极致 - 终结技/爆炸
        }

        [Header("相机引用")]
        [Tooltip("要应用震动的相机")]
        public Camera TargetCamera;

        [Header("震动参数")]
        [Tooltip("是否启用相机震动")]
        public bool EnableShake = true;

        [Tooltip("震动强度乘数（0-2，用于全局调整震动强度）")]
        [Limit(0, 2)]
        public float ShakeMultiplier = 1.0f;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 当前震动状态
        private bool isShaking = false;
        private float shakeTimer = 0.0f;
        private float shakeDuration = 0.0f;
        private float shakeIntensity = 0.0f;
        private float shakeFrequency = 15.0f; // 震动频率（每秒震动次数）

        // 震动偏移量
        private Vector3 shakeOffset = Vector3.Zero;
        private Vector3 shakeRotation = Vector3.Zero;

        // 原始相机位置和旋转（用于恢复）
        private Vector3 originalPosition;
        private Quaternion originalRotation;

        // 震动曲线（衰减）
        private float DecayFactor => Mathf.Max(0, 1.0f - (shakeTimer / shakeDuration));

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            if (TargetCamera == null)
            {
                TargetCamera = Camera.MainCamera;
            }

            if (TargetCamera != null)
            {
                originalPosition = TargetCamera.Position;
                originalRotation = TargetCamera.Orientation;
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            if (!EnableShake || TargetCamera == null)
                return;

            if (isShaking)
            {
                UpdateShake();
            }

            if (ShowDebug)
            {
                DebugDraw.DrawText($"Camera Shake: {isShaking}, Intensity: {shakeIntensity:F2}, Time: {shakeTimer:F2}/{shakeDuration:F2}", 
                    new Vector3(100, 100, 0), Color.White);
            }
        }

        /// <summary>
        /// 触发相机震动（预设强度）
        /// </summary>
        /// <param name="intensity">震动强度预设</param>
        /// <param name="customDuration">自定义持续时间（0则使用默认）</param>
        public void TriggerShake(ShakeIntensity intensity, float customDuration = 0)
        {
            if (!EnableShake)
                return;

            switch (intensity)
            {
                case ShakeIntensity.VeryLight:
                    StartShake(0.05f, customDuration > 0 ? customDuration : 0.15f, 20.0f);
                    break;
                case ShakeIntensity.Light:
                    StartShake(0.1f, customDuration > 0 ? customDuration : 0.2f, 15.0f);
                    break;
                case ShakeIntensity.Medium:
                    StartShake(0.2f, customDuration > 0 ? customDuration : 0.3f, 15.0f);
                    break;
                case ShakeIntensity.Strong:
                    StartShake(0.4f, customDuration > 0 ? customDuration : 0.5f, 12.0f);
                    break;
                case ShakeIntensity.VeryStrong:
                    StartShake(0.6f, customDuration > 0 ? customDuration : 0.7f, 10.0f);
                    break;
                case ShakeIntensity.Extreme:
                    StartShake(1.0f, customDuration > 0 ? customDuration : 1.0f, 8.0f);
                    break;
            }
        }

        /// <summary>
        /// 触发相机震动（自定义参数）
        /// </summary>
        /// <param name="intensity">震动强度（0-1）</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="frequency">震动频率（每秒震动次数）</param>
        public void StartShake(float intensity, float duration, float frequency = 15.0f)
        {
            if (!EnableShake || TargetCamera == null)
                return;

            // 如果正在震动，叠加震动强度
            if (isShaking)
            {
                shakeIntensity = Mathf.Max(shakeIntensity, intensity * ShakeMultiplier);
                shakeDuration = Mathf.Max(shakeDuration, duration);
                shakeTimer = 0; // 重置计时器
            }
            else
            {
                isShaking = true;
                shakeIntensity = intensity * ShakeMultiplier;
                shakeDuration = duration;
                shakeFrequency = frequency;
                shakeTimer = 0;

                // 记录原始位置和旋转
                originalPosition = TargetCamera.Position;
                originalRotation = TargetCamera.Orientation;
            }

            if (ShowDebug)
            {
                Debug.Log($"Camera Shake Started: Intensity={shakeIntensity:F2}, Duration={shakeDuration:F2}s, Frequency={shakeFrequency:F1}Hz");
            }
        }

        /// <summary>
        /// 停止震动
        /// </summary>
        public void StopShake()
        {
            if (!isShaking)
                return;

            isShaking = false;
            shakeTimer = 0;
            shakeOffset = Vector3.Zero;
            shakeRotation = Vector3.Zero;

            // 恢复原始位置和旋转
            if (TargetCamera != null)
            {
                TargetCamera.Position = originalPosition;
                TargetCamera.Orientation = originalRotation;
            }
        }

        /// <summary>
        /// 更新震动效果
        /// </summary>
        private void UpdateShake()
        {
            shakeTimer += Time.DeltaTime;

            // 震动结束
            if (shakeTimer >= shakeDuration)
            {
                StopShake();
                return;
            }

            // 计算衰减系数
            float decay = DecayFactor;

            // 使用随机数生成震动偏移（替代PerlinNoise）
            float time = shakeTimer * shakeFrequency;
            
            // 位置偏移（XYZ三个方向）
            // 使用简单的三角函数模拟噪声效果
            float offsetX = Mathf.Sin(time * 2.1f) * Mathf.Cos(time * 1.3f) * shakeIntensity * decay;
            float offsetY = Mathf.Cos(time * 1.7f) * Mathf.Sin(time * 2.5f) * shakeIntensity * decay;
            float offsetZ = Mathf.Sin(time * 1.9f) * Mathf.Cos(time * 2.3f) * shakeIntensity * decay;
            shakeOffset = new Vector3(offsetX, offsetY, offsetZ);

            // 旋转偏移（俯仰、偏航、翻滚，单位：度）
            float rotX = Mathf.Sin(time * 1.5f + 1.0f) * 2.5f * shakeIntensity * decay;
            float rotY = Mathf.Cos(time * 1.8f + 2.0f) * 2.5f * shakeIntensity * decay;
            float rotZ = Mathf.Sin(time * 2.0f + 3.0f) * 1.5f * shakeIntensity * decay;
            shakeRotation = new Vector3(rotX, rotY, rotZ);

            // 应用震动到相机
            ApplyShake();
        }

        /// <summary>
        /// 应用震动到相机
        /// </summary>
        private void ApplyShake()
        {
            if (TargetCamera == null)
                return;

            // 应用位置偏移
            TargetCamera.Position = originalPosition + shakeOffset;

            // 应用旋转偏移
            Quaternion shakeQuat = Quaternion.Euler(shakeRotation.X, shakeRotation.Y, shakeRotation.Z);
            TargetCamera.Orientation = originalRotation * shakeQuat;
        }

        /// <summary>
        /// 技能震动快捷方法
        /// </summary>
        /// <param name="skillPower">技能威力（0-100）</param>
        public void ShakeForSkill(float skillPower)
        {
            if (skillPower < 10)
                TriggerShake(ShakeIntensity.VeryLight);
            else if (skillPower < 30)
                TriggerShake(ShakeIntensity.Light);
            else if (skillPower < 50)
                TriggerShake(ShakeIntensity.Medium);
            else if (skillPower < 70)
                TriggerShake(ShakeIntensity.Strong);
            else if (skillPower < 90)
                TriggerShake(ShakeIntensity.VeryStrong);
            else
                TriggerShake(ShakeIntensity.Extreme);
        }

        /// <summary>
        /// 命中震动快捷方法
        /// </summary>
        /// <param name="damage">伤害值</param>
        /// <param name="maxHealth">最大生命值</param>
        public void ShakeForHit(float damage, float maxHealth)
        {
            float damagePercent = (damage / maxHealth) * 100;
            ShakeForSkill(damagePercent);
        }

        /// <summary>
        /// 爆炸震动快捷方法
        /// </summary>
        /// <param name="distance">距离爆炸中心的距离</param>
        /// <param name="maxDistance">最大影响距离</param>
        public void ShakeForExplosion(float distance, float maxDistance = 50.0f)
        {
            if (distance > maxDistance)
                return;

            // 距离越近，震动越强
            float distanceFactor = 1.0f - (distance / maxDistance);
            float intensity = distanceFactor * 0.8f;
            float duration = 0.5f + distanceFactor * 0.5f;

            StartShake(intensity, duration, 12.0f);
        }

        /// <summary>
        /// 步行震动（持续轻微震动）
        /// </summary>
        public void StartWalkingShake()
        {
            if (!isShaking)
            {
                StartShake(0.02f, 999.0f, 2.0f); // 持续震动，直到停止
            }
        }

        /// <summary>
        /// 停止步行震动
        /// </summary>
        public void StopWalkingShake()
        {
            StopShake();
        }
    }
}

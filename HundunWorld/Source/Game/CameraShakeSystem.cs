using FlaxEngine;
using FlaxEngine.Utilities;
using Game;
using System.Collections.Generic;

namespace HundunWorld.Game
{
    /// <summary>
    /// 相机震动系统，提供丰富的震动效果
    /// </summary>
    public class CameraShakeSystem : Script
    {
        #region 震动参数

        /// <summary>
        /// 震动强度倍数
        /// </summary>
        [Tooltip("震动强度倍数")]
        public float ShakeIntensityMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// 是否启用震动
        /// </summary>
        [Tooltip("是否启用震动")]
        public bool EnableShake { get; set; } = true;

        /// <summary>
        /// 最大震动强度
        /// </summary>
        [Tooltip("最大震动强度")]
        public float MaxShakeIntensity { get; set; } = 5.0f;

        /// <summary>
        /// 震动衰减速度
        /// </summary>
        [Tooltip("震动衰减速度")]
        public float ShakeDecaySpeed { get; set; } = 2.0f;

        #endregion

        #region 震动队列

        /// <summary>
        /// 活跃的震动效果列表
        /// </summary>
        private List<ShakeEffect> _activeShakes = new List<ShakeEffect>();

        /// <summary>
        /// 相机引用
        /// </summary>
        private ThirdPersonCamera _thirdPersonCamera;

        /// <summary>
        /// 当前总震动偏移
        /// </summary>
        private Vector3 _totalShakeOffset = Vector3.Zero;

        #endregion

        #region 预设震动效果

        /// <summary>
        /// 轻微震动预设
        /// </summary>
        public static readonly ShakePreset LightShake = new ShakePreset
        {
            Intensity = 0.1f,
            Duration = 0.2f,
            Frequency = 20.0f,
            Type = ShakeType.Random
        };

        /// <summary>
        /// 中等震动预设
        /// </summary>
        public static readonly ShakePreset MediumShake = new ShakePreset
        {
            Intensity = 0.3f,
            Duration = 0.5f,
            Frequency = 15.0f,
            Type = ShakeType.Random
        };

        /// <summary>
        /// 强烈震动预设
        /// </summary>
        public static readonly ShakePreset HeavyShake = new ShakePreset
        {
            Intensity = 0.8f,
            Duration = 1.0f,
            Frequency = 10.0f,
            Type = ShakeType.Random
        };

        /// <summary>
        /// 爆炸震动预设
        /// </summary>
        public static readonly ShakePreset ExplosionShake = new ShakePreset
        {
            Intensity = 1.5f,
            Duration = 1.5f,
            Frequency = 8.0f,
            Type = ShakeType.Impulse
        };

        /// <summary>
        /// 冲击震动预设
        /// </summary>
        public static readonly ShakePreset ImpactShake = new ShakePreset
        {
            Intensity = 0.5f,
            Duration = 0.3f,
            Frequency = 25.0f,
            Type = ShakeType.Impulse
        };

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取第三人称相机引用
            _thirdPersonCamera = Actor.GetScript<ThirdPersonCamera>();
            if (_thirdPersonCamera == null)
            {
                // 尝试从父对象获取
                _thirdPersonCamera = Actor.Parent?.GetScript<ThirdPersonCamera>();
            }
        }

        public override void OnUpdate()
        {
            if (!EnableShake)
                return;

            // 更新所有震动效果
            UpdateShakeEffects();

            // 计算总震动偏移
            CalculateTotalShake();

            // 应用震动到相机
            ApplyShakeToCamera();
        }

        #endregion

        #region 震动效果管理

        /// <summary>
        /// 更新震动效果
        /// </summary>
        private void UpdateShakeEffects()
        {
            for (int i = _activeShakes.Count - 1; i >= 0; i--)
            {
                var shake = _activeShakes[i];
                shake.Update(Time.DeltaTime);

                // 移除已完成的震动
                if (shake.IsCompleted)
                {
                    _activeShakes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 计算总震动偏移
        /// </summary>
        private void CalculateTotalShake()
        {
            _totalShakeOffset = Vector3.Zero;

            foreach (var shake in _activeShakes)
            {
                _totalShakeOffset += shake.GetCurrentOffset();
            }

            // 限制最大震动强度
            if (_totalShakeOffset.Length > MaxShakeIntensity)
            {
                _totalShakeOffset = _totalShakeOffset.Normalized * MaxShakeIntensity;
            }

            // 应用强度倍数
            _totalShakeOffset *= ShakeIntensityMultiplier;
        }

        /// <summary>
        /// 应用震动到相机
        /// </summary>
        private void ApplyShakeToCamera()
        {
            if (_thirdPersonCamera != null)
            {
                // 应用震动偏移
                Actor.Position += _totalShakeOffset;
            }
        }

        #endregion

        #region 公共震动接口

        /// <summary>
        /// 触发震动效果
        /// </summary>
        /// <param name="intensity">震动强度</param>
        /// <param name="duration">震动持续时间</param>
        /// <param name="frequency">震动频率</param>
        /// <param name="shakeType">震动类型</param>
        public void TriggerShake(float intensity, float duration, float frequency = 20.0f, ShakeType shakeType = ShakeType.Random)
        {
            if (!EnableShake)
                return;

            var shake = new ShakeEffect(intensity, duration, frequency, shakeType);
            _activeShakes.Add(shake);
        }

        /// <summary>
        /// 使用预设触发震动
        /// </summary>
        /// <param name="preset">震动预设</param>
        public void TriggerShake(ShakePreset preset)
        {
            TriggerShake(preset.Intensity, preset.Duration, preset.Frequency, preset.Type);
        }

        /// <summary>
        /// 触发方向性震动
        /// </summary>
        /// <param name="direction">震动方向</param>
        /// <param name="intensity">震动强度</param>
        /// <param name="duration">震动持续时间</param>
        public void TriggerDirectionalShake(Vector3 direction, float intensity, float duration)
        {
            if (!EnableShake)
                return;

            var shake = new ShakeEffect(intensity, duration, 20.0f, ShakeType.Directional);
            shake.SetDirection(direction.Normalized);
            _activeShakes.Add(shake);
        }

        /// <summary>
        /// 触发距离衰减震动
        /// </summary>
        /// <param name="sourcePosition">震动源位置</param>
        /// <param name="intensity">基础强度</param>
        /// <param name="duration">持续时间</param>
        /// <param name="maxDistance">最大影响距离</param>
        public void TriggerDistanceBasedShake(Vector3 sourcePosition, float intensity, float duration, float maxDistance)
        {
            if (!EnableShake)
                return;

            float distance = Vector3.Distance(Actor.Position, sourcePosition);
            if (distance > maxDistance)
                return;

            // 根据距离衰减强度
            float attenuatedIntensity = intensity * (1.0f - distance / maxDistance);
            TriggerShake(attenuatedIntensity, duration);
        }

        /// <summary>
        /// 停止所有震动
        /// </summary>
        public void StopAllShakes()
        {
            _activeShakes.Clear();
            _totalShakeOffset = Vector3.Zero;
        }

        /// <summary>
        /// 停止指定类型的震动
        /// </summary>
        /// <param name="shakeType">震动类型</param>
        public void StopShakesByType(ShakeType shakeType)
        {
            for (int i = _activeShakes.Count - 1; i >= 0; i--)
            {
                if (_activeShakes[i].Type == shakeType)
                {
                    _activeShakes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 获取当前震动强度
        /// </summary>
        /// <returns>当前震动强度</returns>
        public float GetCurrentShakeIntensity()
        {
            return _totalShakeOffset.Length;
        }

        /// <summary>
        /// 检查是否有活跃的震动
        /// </summary>
        /// <returns>是否有震动</returns>
        public bool HasActiveShakes()
        {
            return _activeShakes.Count > 0;
        }

        #endregion
    }

    #region 震动相关结构和枚举

    /// <summary>
    /// 震动类型
    /// </summary>
    public enum ShakeType
    {
        /// <summary>
        /// 随机震动
        /// </summary>
        Random,
        
        /// <summary>
        /// 冲击震动
        /// </summary>
        Impulse,
        
        /// <summary>
        /// 方向性震动
        /// </summary>
        Directional,
        
        /// <summary>
        /// 正弦波震动
        /// </summary>
        Sine
    }

    /// <summary>
    /// 震动预设
    /// </summary>
    public struct ShakePreset
    {
        public float Intensity;
        public float Duration;
        public float Frequency;
        public ShakeType Type;
    }

    /// <summary>
    /// 震动效果类
    /// </summary>
    public class ShakeEffect
    {
        public float Intensity { get; private set; }
        public float Duration { get; private set; }
        public float Frequency { get; private set; }
        public ShakeType Type { get; private set; }
        
        private float _elapsedTime = 0f;
        private Vector3 _direction = Vector3.Up;
        private float _phase = 0f;
        
        public bool IsCompleted => _elapsedTime >= Duration;

        public ShakeEffect(float intensity, float duration, float frequency, ShakeType type)
        {
            Intensity = intensity;
            Duration = duration;
            Frequency = frequency;
            Type = type;
            _phase = RandomUtil.Random.NextSingle() * Mathf.Pi * 2.0f;
        }

        public void SetDirection(Vector3 direction)
        {
            _direction = direction;
        }

        public void Update(float deltaTime)
        {
            _elapsedTime += deltaTime;
        }

        public Vector3 GetCurrentOffset()
        {
            if (IsCompleted)
                return Vector3.Zero;

            float normalizedTime = _elapsedTime / Duration;
            float envelope = GetEnvelope(normalizedTime);
            
            switch (Type)
            {
                case ShakeType.Random:
                    return GetRandomOffset() * Intensity * envelope;
                    
                case ShakeType.Impulse:
                    return GetImpulseOffset(normalizedTime) * Intensity * envelope;
                    
                case ShakeType.Directional:
                    return _direction * Intensity * envelope * GetNoiseValue();
                    
                case ShakeType.Sine:
                    return GetSineOffset() * Intensity * envelope;
                    
                default:
                    return Vector3.Zero;
            }
        }

        private float GetEnvelope(float normalizedTime)
        {
            // 使用指数衰减包络
            return Mathf.Exp(-normalizedTime * 3.0f);
        }

        private Vector3 GetRandomOffset()
        {
            return new Vector3(
                RandomUtil.Random.NextFloat(-1f, 1f),
                RandomUtil.Random.NextFloat(-1f, 1f),
                RandomUtil.Random.NextFloat(-1f, 1f)
            );
        }

        private Vector3 GetImpulseOffset(float normalizedTime)
        {
            float impulse = Mathf.Exp(-normalizedTime * 8.0f);
            return GetRandomOffset() * impulse;
        }

        private Vector3 GetSineOffset()
        {
            float time = _elapsedTime * Frequency + _phase;
            return new Vector3(
                Mathf.Sin(time),
                Mathf.Sin(time * 1.1f),
                Mathf.Sin(time * 0.9f)
            );
        }

        private float GetNoiseValue()
        {
            return RandomUtil.Random.NextFloat(-1f, 1f);
        }
    }

    #endregion
}
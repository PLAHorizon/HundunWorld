using FlaxEngine;
using System;

namespace Game.Effects
{
    /// <summary>
    /// Tween 循环模式
    /// </summary>
    public enum TweenLoopMode
    {
        /// <summary>
        /// 执行一次后停止
        /// </summary>
        Once,

        /// <summary>
        /// 循环播放（结束后重新开始）
        /// </summary>
        Loop,

        /// <summary>
        /// 来回播放（正向结束后反向播放）
        /// </summary>
        PingPong
    }

    /// <summary>
    /// 缓动函数类型
    /// </summary>
    public enum EaseType
    {
        /// <summary>
        /// 线性插值
        /// </summary>
        Linear,

        /// <summary>
        /// 正弦缓入缓出
        /// </summary>
        EaseInOutSine,

        /// <summary>
        /// 立方缓出
        /// </summary>
        EaseOutCubic
    }

    /// <summary>
    /// 缓动函数实现
    /// </summary>
    public static class EasingFunctions
    {
        /// <summary>
        /// 根据指定的缓动类型求值（输入和输出范围 0-1）
        /// </summary>
        /// <param name="type">缓动类型</param>
        /// <param name="t">归一化时间（0-1）</param>
        /// <returns>缓动后的值</returns>
        public static float Evaluate(EaseType type, float t)
        {
            switch (type)
            {
                case EaseType.Linear:
                    return t;
                case EaseType.EaseInOutSine:
                    // 正弦缓入缓出: f(t) = -(cos(π * t) - 1) / 2
                    return -(Mathf.Cos(Mathf.Pi * t) - 1f) / 2f;
                case EaseType.EaseOutCubic:
                    // 立方缓出: f(t) = 1 - (1 - t)^3
                    return 1f - Mathf.Pow(1f - t, 3f);
                default:
                    return t;
            }
        }
    }

    /// <summary>
    /// 通用 Tween 抽象基类
    /// </summary>
    /// <typeparam name="T">插值类型</typeparam>
    public abstract class Tween<T>
    {
        /// <summary>
        /// 起始值
        /// </summary>
        public T From;

        /// <summary>
        /// 目标值
        /// </summary>
        public T To;

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        public float Duration;

        /// <summary>
        /// 已经过的时间（秒）
        /// </summary>
        public float Elapsed;

        /// <summary>
        /// 缓动类型
        /// </summary>
        public EaseType Ease;

        /// <summary>
        /// 循环模式
        /// </summary>
        public TweenLoopMode LoopMode;

        /// <summary>
        /// 当前播放方向（用于 PingPong 模式）
        /// true = 正向, false = 反向
        /// </summary>
        protected bool IsPlayingForward = true;

        /// <summary>
        /// 是否已完成
        /// </summary>
        public bool IsCompleted { get; protected set; }

        /// <summary>
        /// 当前插值结果
        /// </summary>
        public T Value { get; protected set; }

        /// <summary>
        /// 当前插值结果（兼容别名）
        /// </summary>
        public T CurrentValue => Value;

        /// <summary>
        /// 抽象插值方法，由子类实现
        /// </summary>
        /// <param name="a">起始值</param>
        /// <param name="b">目标值</param>
        /// <param name="t">插值因子（0-1）</param>
        /// <returns>插值结果</returns>
        protected abstract T Lerp(T a, T b, float t);

        /// <summary>
        /// 无参构造函数（支持对象初始化器语法）
        /// </summary>
        protected Tween()
        {
            LoopMode = TweenLoopMode.Once;
            Ease = EaseType.Linear;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="from">起始值</param>
        /// <param name="to">目标值</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="easeType">缓动类型</param>
        /// <param name="loopMode">循环模式</param>
        protected Tween(T from, T to, float duration, EaseType easeType = EaseType.Linear, TweenLoopMode loopMode = TweenLoopMode.Once)
        {
            From = from;
            To = to;
            Duration = duration;
            Ease = easeType;
            LoopMode = loopMode;
            Elapsed = 0f;
            IsCompleted = false;
            IsPlayingForward = true;
            Value = from;
        }

        /// <summary>
        /// 每帧推进 Tween
        /// </summary>
        /// <param name="deltaTime">帧间隔（秒）</param>
        public void Update(float deltaTime)
        {
            if (IsCompleted)
            {
                return;
            }

            // 累加时间
            if (IsPlayingForward)
            {
                Elapsed += deltaTime;
            }
            else
            {
                Elapsed -= deltaTime;
            }

            // 计算归一化进度
            float normalizedProgress = Mathf.Clamp(Elapsed / Duration, 0f, 1f);

            // 应用缓动函数
            float easedProgress = EasingFunctions.Evaluate(Ease, normalizedProgress);

            // 计算当前值
            Value = Lerp(From, To, easedProgress);

            // 处理循环模式
            HandleLoopMode();
        }

        /// <summary>
        /// 处理循环模式逻辑
        /// </summary>
        private void HandleLoopMode()
        {
            switch (LoopMode)
            {
                case TweenLoopMode.Once:
                    if (Elapsed >= Duration)
                    {
                        Elapsed = Duration;
                        Value = Lerp(From, To, 1f);
                        IsCompleted = true;
                    }
                    break;

                case TweenLoopMode.Loop:
                    if (Elapsed >= Duration)
                    {
                        Elapsed -= Duration;
                        float normalizedProgress = Mathf.Clamp(Elapsed / Duration, 0f, 1f);
                        float easedProgress = EasingFunctions.Evaluate(Ease, normalizedProgress);
                        Value = Lerp(From, To, easedProgress);
                    }
                    break;

                case TweenLoopMode.PingPong:
                    if (IsPlayingForward && Elapsed >= Duration)
                    {
                        // 正向播放结束，切换到反向
                        IsPlayingForward = false;
                        Elapsed = Duration - (Elapsed - Duration);
                    }
                    else if (!IsPlayingForward && Elapsed <= 0f)
                    {
                        // 反向播放结束，切换到正向
                        IsPlayingForward = true;
                        Elapsed = -Elapsed;
                    }
                    break;
            }
        }

        /// <summary>
        /// 重置 Tween 到初始状态
        /// </summary>
        public void Reset()
        {
            Elapsed = 0f;
            IsCompleted = false;
            IsPlayingForward = true;
            Value = From;
        }

        /// <summary>
        /// 立即跳转到完成状态
        /// </summary>
        public void Complete()
        {
            Elapsed = Duration;
            Value = Lerp(From, To, 1f);
            IsCompleted = true;
        }
    }

    /// <summary>
    /// float 类型 Tween
    /// </summary>
    public class FloatTween : Tween<float>
    {
        /// <summary>
        /// 无参构造函数（支持对象初始化器语法）
        /// </summary>
        public FloatTween()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="from">起始值</param>
        /// <param name="to">目标值</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="easeType">缓动类型</param>
        /// <param name="loopMode">循环模式</param>
        public FloatTween(float from, float to, float duration, EaseType easeType = EaseType.Linear, TweenLoopMode loopMode = TweenLoopMode.Once)
            : base(from, to, duration, easeType, loopMode)
        {
        }

        /// <inheritdoc/>
        protected override float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }

    /// <summary>
    /// Float2 类型 Tween（二维向量插值）
    /// </summary>
    public class Float2Tween : Tween<Float2>
    {
        /// <summary>
        /// 无参构造函数（支持对象初始化器语法）
        /// </summary>
        public Float2Tween()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="from">起始值</param>
        /// <param name="to">目标值</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="easeType">缓动类型</param>
        /// <param name="loopMode">循环模式</param>
        public Float2Tween(Float2 from, Float2 to, float duration, EaseType easeType = EaseType.Linear, TweenLoopMode loopMode = TweenLoopMode.Once)
            : base(from, to, duration, easeType, loopMode)
        {
        }

        /// <inheritdoc/>
        protected override Float2 Lerp(Float2 a, Float2 b, float t)
        {
            return Float2.Lerp(a, b, t);
        }
    }

    /// <summary>
    /// Color 类型 Tween（颜色插值）
    /// </summary>
    public class ColorTween : Tween<Color>
    {
        /// <summary>
        /// 无参构造函数（支持对象初始化器语法）
        /// </summary>
        public ColorTween()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="from">起始值</param>
        /// <param name="to">目标值</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="easeType">缓动类型</param>
        /// <param name="loopMode">循环模式</param>
        public ColorTween(Color from, Color to, float duration, EaseType easeType = EaseType.Linear, TweenLoopMode loopMode = TweenLoopMode.Once)
            : base(from, to, duration, easeType, loopMode)
        {
        }

        /// <inheritdoc/>
        protected override Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, t);
        }
    }

    /// <summary>
    /// Quaternion 类型 Tween（四元数旋转插值）
    /// </summary>
    public class QuaternionTween : Tween<Quaternion>
    {
        /// <summary>
        /// 无参构造函数（支持对象初始化器语法）
        /// </summary>
        public QuaternionTween()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="from">起始值</param>
        /// <param name="to">目标值</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="easeType">缓动类型</param>
        /// <param name="loopMode">循环模式</param>
        public QuaternionTween(Quaternion from, Quaternion to, float duration, EaseType easeType = EaseType.Linear, TweenLoopMode loopMode = TweenLoopMode.Once)
            : base(from, to, duration, easeType, loopMode)
        {
        }

        /// <inheritdoc/>
        protected override Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Slerp(a, b, t);
        }
    }
}

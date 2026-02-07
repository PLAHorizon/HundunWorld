using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.UI.Animation
{
    /// <summary>
    /// UI动画信息类
    /// 包含动画的所有必要信息和状态
    /// </summary>
    public class UIAnimation
    {
        public Control Target { get; set; }
        public AnimationType Type { get; set; }
        public float Duration { get; set; }
        public EasingType Easing { get; set; }
        public Action OnComplete { get; set; }
        public Float2 Position { get; set; } = Float2.Zero;
        // 动画状态
        public float ElapsedTime { get; set; }
        public bool IsComplete { get; set; }
        public bool IsValid { get; set; } = true;
        public string AnimationId { get; set; }

        // 动画起始和目标值
        public object StartValue { get; set; }
        public object TargetValue { get; set; }

        public UIAnimation(Control target, AnimationType type, float duration, EasingType easing = EasingType.EaseInOut)
        {
            Target = target;
            Type = type;
            Duration = duration;
            Easing = easing;
            ElapsedTime = 0f;
            IsComplete = false;
            AnimationId = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// 验证动画是否仍然有效
        /// </summary>
        public bool ValidateTarget()
        {
            try
            {
                if (Target == null || Target.IsDisposing)
                {
                    IsValid = false;
                    return false;
                }

                // 检查目标控件的父容器是否有效
                var parent = Target.Parent;
                if (parent != null && parent.IsDisposing)
                {
                    IsValid = false;
                    return false;
                }

                return true;
            }
            catch
            {
                IsValid = false;
                return false;
            }
        }
    }

    /// <summary>
    /// UI动画管理器
    /// 负责管理和执行UI动画，采用线程安全的单例模式
    /// </summary>
    public class UIAnimationManager : Script
    {
        private static UIAnimationManager _instance;
        private static readonly object _lockObject = new object();
        private static volatile bool _isDisposed = false;

        // 使用线程安全的集合
        private readonly ConcurrentDictionary<string, UIAnimation> _activeAnimations = new ConcurrentDictionary<string, UIAnimation>();
        private readonly List<UIAnimation> _pendingRemovals = new List<UIAnimation>();
        private readonly object _updateLock = new object();

        // 性能监控
        private int _animationCount = 0;
        private float _lastCleanupTime = 0f;
        private const float CLEANUP_INTERVAL = 5f;
        private const int MAX_ANIMATIONS = 200;

        // 错误处理
        private bool _isInSafeMode = false;
        private int _errorCount = 0;
        private const int MAX_ERRORS_PER_SECOND = 10;
        private float _lastErrorTime = 0f;

        public static UIAnimationManager Instance
        {
            get
            {
                if (_instance == null && !_isDisposed)
                {
                    lock (_lockObject)
                    {
                        if (_instance == null && !_isDisposed)
                        {
                            CreateInstance();
                        }
                    }
                }
                return _instance;
            }
        }

        private static void CreateInstance()
        {
            try
            {
                Actor gameObject = null;

                // 尝试找到现有的管理器对象
                if (Level.Scenes != null)
                {
                    gameObject = Level.FindActor("UIAnimationManager");
                }

                // 如果没有找到，创建新的
                if (gameObject == null)
                {
                    gameObject = new EmptyActor();
                    gameObject.Name = "UIAnimationManager";

                    // 确保对象不会被销毁
                    // 使对象在场景切换时不被销毁
                    gameObject.HideFlags = HideFlags.DontSave;
                }

                // 获取或添加脚本组件
                _instance = gameObject.GetScript<UIAnimationManager>();
                if (_instance == null)
                {
                    _instance = gameObject.AddScript<UIAnimationManager>();
                }

                FlaxEngine.Debug.Log("UIAnimationManager实例已创建");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建UIAnimationManager实例失败: {ex.Message}");
                throw;
            }
        }

        public override void OnAwake()
        {
            // 线程安全的实例管理
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    _instance = this;
                    InitializeManager();
                }
                else if (_instance != this)
                {
                    // 销毁重复的实例
                    FlaxEngine.Debug.LogWarning("检测到重复的UIAnimationManager实例，销毁当前实例");
                    Actor.Destroy(this);
                    return;
                }
            }
        }

        private void InitializeManager()
        {
            _lastCleanupTime = Time.GameTime;
            _isInSafeMode = false;
            _errorCount = 0;
            _lastErrorTime = 0f;

            FlaxEngine.Debug.Log("UIAnimationManager初始化完成");
        }

        public override void OnUpdate()
        {
            if (_isInSafeMode)
            {
                return;
            }

            try
            {
                lock (_updateLock)
                {
                    UpdateAnimations();
                    PerformCleanup();
                }
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        private void UpdateAnimations()
        {
            _pendingRemovals.Clear();

            foreach (var kvp in _activeAnimations)
            {
                var animation = kvp.Value;

                // 验证动画目标是否仍然有效
                if (!animation.ValidateTarget())
                {
                    _pendingRemovals.Add(animation);
                    continue;
                }

                try
                {
                    UpdateAnimation(animation);

                    if (animation.IsComplete)
                    {
                        // 安全地执行完成回调
                        try
                        {
                            animation.OnComplete?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogWarning($"动画完成回调执行失败: {ex.Message}");
                        }

                        _pendingRemovals.Add(animation);
                    }
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning($"更新动画失败: {ex.Message}");
                    _pendingRemovals.Add(animation);
                }
            }

            // 安全地移除完成的动画
            foreach (var animation in _pendingRemovals)
            {
                _activeAnimations.TryRemove(animation.AnimationId, out _);
            }

            _animationCount = _activeAnimations.Count;
        }

        private void PerformCleanup()
        {
            if (Time.GameTime - _lastCleanupTime > CLEANUP_INTERVAL)
            {
                // 清理无效的动画
                var invalidAnimations = new List<string>();

                foreach (var kvp in _activeAnimations)
                {
                    if (!kvp.Value.ValidateTarget())
                    {
                        invalidAnimations.Add(kvp.Key);
                    }
                }

                foreach (var id in invalidAnimations)
                {
                    _activeAnimations.TryRemove(id, out _);
                }

                _lastCleanupTime = Time.GameTime;

                if (invalidAnimations.Count > 0)
                {
                    FlaxEngine.Debug.Log($"清理了 {invalidAnimations.Count} 个无效动画");
                }
            }
        }

        private void HandleError(Exception ex)
        {
            _errorCount++;
            var currentTime = Time.GameTime;

            if (currentTime - _lastErrorTime > 1f)
            {
                _errorCount = 1;
                _lastErrorTime = currentTime;
            }

            FlaxEngine.Debug.LogError($"UIAnimationManager错误: {ex.Message}");

            // 暂时禁用安全模式，直到我们找到错误根源
            // if (_errorCount > MAX_ERRORS_PER_SECOND)
            // {
            //     EnterSafeMode();
            // }
        }

        private void EnterSafeMode()
        {
            _isInSafeMode = true;
            StopAllAnimations();
            FlaxEngine.Debug.LogWarning("UIAnimationManager进入安全模式，所有动画已停止");
        }

        public void ExitSafeMode()
        {
            _isInSafeMode = false;
            _errorCount = 0;
            _lastErrorTime = 0f;
            FlaxEngine.Debug.Log("UIAnimationManager退出安全模式");
        }

        #region 动画更新核心方法

        private void UpdateAnimation(UIAnimation animation)
        {
            animation.ElapsedTime += Time.DeltaTime;
            float progress = Math.Min(animation.ElapsedTime / animation.Duration, 1f);

            // 应用缓动函数
            float easedProgress = ApplyEasing(progress, animation.Easing);

            // 根据动画类型更新控件
            ApplyAnimationType(animation, easedProgress);

            if (progress >= 1f)
            {
                animation.IsComplete = true;
            }
        }

        private float ApplyEasing(float t, EasingType easing)
        {
            switch (easing)
            {
                case EasingType.Linear:
                    return t;
                case EasingType.EaseIn:
                    return t * t;
                case EasingType.EaseOut:
                    return 1f - (1f - t) * (1f - t);
                case EasingType.EaseInOut:
                    return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                case EasingType.Bounce:
                    return BounceEase(t);
                case EasingType.Elastic:
                    return ElasticEase(t);
                default:
                    return t;
            }
        }

        private float BounceEase(float t)
        {
            if (t < 1f / 2.75f)
                return 7.5625f * t * t;
            else if (t < 2f / 2.75f)
                return 7.5625f * (t -= 1.5f / 2.75f) * t + 0.75f;
            else if (t < 2.5f / 2.75f)
                return 7.5625f * (t -= 2.25f / 2.75f) * t + 0.9375f;
            else
                return 7.5625f * (t -= 2.625f / 2.75f) * t + 0.984375f;
        }

        private float ElasticEase(float t)
        {
            if (t == 0f || t == 1f) return t;
            float p = 0.3f;
            float s = p / 4f;
            return -(float)(Math.Pow(2, 10 * (t -= 1)) * Math.Sin((t - s) * (2 * Math.PI) / p));
        }

        private void ApplyAnimationType(UIAnimation animation, float progress)
        {
            try
            {
                switch (animation.Type)
                {
                    case AnimationType.FadeIn:
                        ApplyFadeAnimation(animation, progress, true);
                        break;
                    case AnimationType.FadeOut:
                        ApplyFadeAnimation(animation, progress, false);
                        break;
                    case AnimationType.SlideIn:
                        ApplySlideAnimation(animation, progress, true);
                        break;
                    case AnimationType.SlideOut:
                        ApplySlideAnimation(animation, progress, false);
                        break;
                    case AnimationType.ScaleIn:
                        ApplyScaleAnimation(animation, progress, true);
                        break;
                    case AnimationType.ScaleOut:
                        ApplyScaleAnimation(animation, progress, false);
                        break;
                    case AnimationType.Shake:
                        ApplyShakeAnimation(animation, progress);
                        break;
                    case AnimationType.Bounce:
                        ApplyBounceAnimation(animation, progress);
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"应用动画类型失败: {ex.Message}");
                animation.IsComplete = true;
            }
        }

        private void ApplyFadeAnimation(UIAnimation animation, float progress, bool fadeIn)
        {
            if (animation.Target is Panel panel)
            {
                float startAlpha = fadeIn ? 0f : 1f;
                float targetAlpha = fadeIn ? 1f : 0f;

                if (animation.StartValue == null)
                {
                    animation.StartValue = startAlpha;
                    animation.TargetValue = targetAlpha;
                }

                float currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, progress);
                var color = panel.BackgroundColor;
                panel.BackgroundColor = new Color(color.R, color.G, color.B, currentAlpha);
            }
        }

        private void ApplySlideAnimation(UIAnimation animation, float progress, bool slideIn)
        {
            if (animation.StartValue == null)
            {
                var startPos = animation.Target.Location;
                var targetPos = !slideIn ? new Float2(startPos.X + animation.Position.X, startPos.Y+ animation.Position.Y) : new Float2(startPos.X - animation.Position.X, startPos.Y - animation.Position.Y);

                //if (slideIn)
                //{
                //    startPos = new Float2(startPos.X , startPos.Y);
                //    animation.Target.Location = startPos;
                //}

                animation.StartValue = startPos;
                animation.TargetValue = targetPos;
            }

            var start = (Float2)animation.StartValue;
            var target = (Float2)animation.TargetValue;
            animation.Target.Location = Float2.Lerp(start, target, progress);
        }

        private void ApplyScaleAnimation(UIAnimation animation, float progress, bool scaleIn)
        {
            if (animation.StartValue == null)
            {
                var originalSize = animation.Target.Size;
                var startSize = scaleIn ? Float2.Zero : originalSize;
                var targetSize = scaleIn ? originalSize : Float2.Zero;

                animation.StartValue = startSize;
                animation.TargetValue = targetSize;
            }

            var start = (Float2)animation.StartValue;
            var target = (Float2)animation.TargetValue;
            animation.Target.Size = Float2.Lerp(start, target, progress);
        }

        private void ApplyShakeAnimation(UIAnimation animation, float progress)
        {
            if (animation.StartValue == null)
            {
                animation.StartValue = animation.Target.Location;
            }

            var originalPos = (Float2)animation.StartValue;
            float intensity = 10f * (1f - progress); // 震动强度逐渐减小

            float offsetX = (float)(Math.Sin(progress * Math.PI * 20) * intensity);
            float offsetY = (float)(Math.Cos(progress * Math.PI * 15) * intensity);

            animation.Target.Location = new Float2(originalPos.X + offsetX, originalPos.Y + offsetY);

            if (progress >= 1f)
            {
                animation.Target.Location = originalPos; // 恢复原位置
            }
        }

        private void ApplyBounceAnimation(UIAnimation animation, float progress)
        {
            if (animation.StartValue == null)
            {
                animation.StartValue = animation.Target.Size;
            }

            var originalSize = (Float2)animation.StartValue;
            float bounceScale = 1f + 0.2f * BounceEase(progress);

            animation.Target.Size = new Float2(originalSize.X * bounceScale, originalSize.Y * bounceScale);
        }

        #endregion

        #region 公共动画接口

        /// <summary>
        /// 淡入动画
        /// </summary>
        public string FadeIn(Control control, float duration = 0.3f, EasingType easing = EasingType.EaseOut, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("FadeIn: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.FadeIn, duration, easing)
            {
               
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 淡出动画
        /// </summary>
        public string FadeOut(Control control, float duration = 0.3f, EasingType easing = EasingType.EaseOut, Action onComplete = null)
        {
            FlaxEngine.Debug.Log($"[UIAnimationManager] 开始FadeOut动画: 控件={control?.GetType().Name}, 时长={duration}秒");
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("FadeOut: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.FadeOut, duration, easing)
            {
                OnComplete = () =>
                {
                    FlaxEngine.Debug.Log($"[UIAnimationManager] FadeOut动画完成: 控件={control?.GetType().Name}");
                    onComplete?.Invoke();
                }
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 滑入动画
        /// </summary>
        public string SlideIn(Control control,Float2 position, float duration = 0.5f, EasingType easing = EasingType.EaseOut, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("SlideIn: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.SlideIn, duration, easing)
            {
                Position = position,
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 滑出动画
        /// </summary>
        public string SlideOut(Control control, Float2  position, float duration = 0.5f, EasingType easing = EasingType.EaseIn, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("SlideOut: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.SlideOut, duration, easing)
            {
                Position = position,
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 缩放进入动画
        /// </summary>
        public string ScaleIn(Control control, float duration = 0.3f, EasingType easing = EasingType.Bounce, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("ScaleIn: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.ScaleIn, duration, easing)
            {
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 震动动画
        /// </summary>
        public string Shake(Control control, float duration = 0.5f, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("Shake: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.Shake, duration, EasingType.Linear)
            {
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 弹跳动画
        /// </summary>
        public string Bounce(Control control, float duration = 0.6f, Action onComplete = null)
        {
            if (control == null || control.IsDisposing)
            {
                FlaxEngine.Debug.LogWarning("Bounce: 目标控件无效");
                return null;
            }

            if (_animationCount >= MAX_ANIMATIONS)
            {
                FlaxEngine.Debug.LogWarning("动画数量已达上限，无法创建新动画");
                return null;
            }

            var animation = new UIAnimation(control, AnimationType.Bounce, duration, EasingType.Bounce)
            {
                OnComplete = onComplete
            };

            _activeAnimations.TryAdd(animation.AnimationId, animation);
            return animation.AnimationId;
        }

        /// <summary>
        /// 停止指定控件的所有动画
        /// </summary>
        public void StopAnimations(Control control)
        {
            if (control == null) return;

            var animationsToRemove = new List<string>();

            foreach (var kvp in _activeAnimations)
            {
                if (kvp.Value.Target == control)
                {
                    animationsToRemove.Add(kvp.Key);
                }
            }

            foreach (var id in animationsToRemove)
            {
                _activeAnimations.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// 停止指定ID的动画
        /// </summary>
        public void StopAnimation(string animationId)
        {
            if (string.IsNullOrEmpty(animationId)) return;
            _activeAnimations.TryRemove(animationId, out _);
        }

        /// <summary>
        /// 停止所有动画
        /// </summary>
        public void StopAllAnimations()
        {
            _activeAnimations.Clear();
        }

        /// <summary>
        /// 获取当前活跃动画数量
        /// </summary>
        public int GetActiveAnimationCount()
        {
            return _animationCount;
        }

        /// <summary>
        /// 检查是否在安全模式
        /// </summary>
        public bool IsInSafeMode()
        {
            return _isInSafeMode;
        }

        #endregion

        public override void OnDestroy()
        {
            _isDisposed = true;
            StopAllAnimations();

            lock (_lockObject)
            {
                if (_instance == this)
                {
                    _instance = null;
                }
            }

            FlaxEngine.Debug.Log("UIAnimationManager已销毁");
        }
    }
}

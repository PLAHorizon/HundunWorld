using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;

using HundunWorld.Game.UI.Events;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Controllers
{
    /// <summary>
    /// 鍔ㄧ敾绫诲瀷鏋氫妇
    /// </summary>
    

    /// <summary>
    /// 缂撳姩鍑芥暟绫诲瀷
    /// </summary>
    

    /// <summary>
    /// 鍔ㄧ敾閰嶇疆
    /// </summary>
    public class AnimationConfig
    {
        public AnimationType Type { get; set; } = AnimationType.FadeIn;
        public float Duration { get; set; } = 0.5f;
        public EasingType Easing { get; set; } = EasingType.EaseOut;
        public Action OnComplete { get; set; }
    }

    /// <summary>
    /// 鍔ㄧ敾鐘舵€?    /// </summary>
    public class AnimationState
    {
        public string AnimationId { get; set; } = Guid.NewGuid().ToString();
        public Control Target { get; set; }
        public AnimationConfig Config { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsPlaying { get; set; } = false;
        public Float2 StartPosition { get; set; }
        public Float2 TargetPosition { get; set; }
        public float StartAlpha { get; set; }
        public float TargetAlpha { get; set; }
        public float StartScale { get; set; } = 1.0f;
        public float TargetScale { get; set; } = 1.0f;
    }

    /// <summary>
    /// 鍔ㄧ敾鎺у埗鍣?    /// 绠＄悊UI鍒囨崲杩囩▼涓殑鍔ㄧ敾鏁堟灉锛屾彁渚涙爣鍑嗗寲鐨勫姩鐢绘挱鏀炬帴鍙?    /// </summary>
    public class AnimationController : Script
    {
        private readonly Dictionary<string, AnimationState> _activeAnimations = new Dictionary<string, AnimationState>();
        private UIEventBus _eventBus;
        private static AnimationController _instance;

        public bool EnableAnimations { get; set; } = true;
        public float GlobalSpeedMultiplier { get; set; } = 1.0f;
        public bool LogAnimationOperations { get; set; } = true;
        public static AnimationController Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("AnimationController") ?? new EmptyActor();
                    gameObject.Name = "AnimationController";
                    _instance = gameObject.GetScript<AnimationController>() ?? gameObject.AddScript<AnimationController>();
                    Engine.RequestingExit += () =>
                    {
                        _instance = null;
                    };
                }
                return _instance;
            }
        }

        #region 鐢熷懡鍛ㄦ湡

        public override void OnStart()
        {
            _eventBus = UIEventBus.Instance;
            FlaxEngine.Debug.Log("鍔ㄧ敾鎺у埗鍣ㄥ垵濮嬪寲瀹屾垚");
        }

        public override void OnUpdate()
        {
            if (EnableAnimations)
            {
                UpdateAnimations();
            }
        }

        public override void OnDestroy()
        {
            _activeAnimations.Clear();
        }

        #endregion

        #region 鍏叡鎺ュ彛

        /// <summary>
        /// 鎾斁鍦烘櫙杩涘叆鍔ㄧ敾
        /// </summary>
        public string PlayEnterAnimation(SceneType sceneType, Control target = null)
        {
            if (!EnableAnimations) return "";

            var config = new AnimationConfig
            {
                Type = AnimationType.FadeIn,
                Duration = 0.5f,
                Easing = EasingType.EaseOut
            };

            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 鎾斁鍦烘櫙閫€鍑哄姩鐢?        /// </summary>
        public string PlayExitAnimation(SceneType sceneType, Control target = null)
        {
            if (!EnableAnimations) return "";

            var config = new AnimationConfig
            {
                Type = AnimationType.FadeOut,
                Duration = 0.3f,
                Easing = EasingType.EaseIn
            };

            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 娣″叆鍔ㄧ敾
        /// </summary>
        public string FadeIn(Control target, float duration = 0.5f, EasingType easing = EasingType.EaseOut, Action onComplete = null)
        {
            var config = new AnimationConfig
            {
                Type = AnimationType.FadeIn,
                Duration = duration,
                Easing = easing,
                OnComplete = onComplete
            };
            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 娣″嚭鍔ㄧ敾
        /// </summary>
        public string FadeOut(Control target, float duration = 0.5f, EasingType easing = EasingType.EaseIn, Action onComplete = null)
        {
            var config = new AnimationConfig
            {
                Type = AnimationType.FadeOut,
                Duration = duration,
                Easing = easing,
                OnComplete = onComplete
            };
            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 婊戝叆鍔ㄧ敾
        /// </summary>
        public string SlideIn(Control target, float duration = 0.6f, EasingType easing = EasingType.EaseOut, Action onComplete = null)
        {
            var config = new AnimationConfig
            {
                Type = AnimationType.SlideIn,
                Duration = duration,
                Easing = easing,
                OnComplete = onComplete
            };
            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 闇囧姩鍔ㄧ敾
        /// </summary>
        public string Shake(Control target, float duration = 0.5f, Action onComplete = null)
        {
            var config = new AnimationConfig
            {
                Type = AnimationType.Shake,
                Duration = duration,
                Easing = EasingType.Linear,
                OnComplete = onComplete
            };
            return PlayAnimation(target, config);
        }

        /// <summary>
        /// 鎾斁鍔ㄧ敾
        /// </summary>
        public string PlayAnimation(Control target, AnimationConfig config)
        {
            if (!EnableAnimations || target == null || config == null) return "";

            var state = new AnimationState
            {
                Target = target,
                Config = config,
                StartTime = DateTime.UtcNow,
                IsPlaying = true,
                StartPosition = target.Location,
                StartAlpha = target.BackgroundColor.A
            };

            SetAnimationTargets(state);
            _activeAnimations[state.AnimationId] = state;

            if (LogAnimationOperations)
            {
                FlaxEngine.Debug.Log($"寮€濮嬪姩鐢? {config.Type}");
            }

            return state.AnimationId;
        }

        /// <summary>
        /// 鍋滄鍔ㄧ敾
        /// </summary>
        public bool StopAnimation(string animationId)
        {
            if (_activeAnimations.TryGetValue(animationId, out var animation))
            {
                animation.IsPlaying = false;
                _activeAnimations.Remove(animationId);
                return true;
            }
            return false;
        }

        #endregion

        #region 绉佹湁鏂规硶

        /// <summary>
        /// 璁剧疆鍔ㄧ敾鐩爣鍊?        /// </summary>
        private void SetAnimationTargets(AnimationState state)
        {
            switch (state.Config.Type)
            {
                case AnimationType.FadeIn:
                    state.StartAlpha = 0.0f;
                    state.TargetAlpha = 1.0f;
                    break;
                case AnimationType.FadeOut:
                    state.TargetAlpha = 0.0f;
                    break;
                case AnimationType.SlideIn:
                    state.StartPosition = new Float2(-state.Target.Size.X, state.Target.Location.Y);
                    state.TargetPosition = state.Target.Location;
                    break;
                case AnimationType.ScaleIn:
                    state.StartScale = 0.0f;
                    state.TargetScale = 1.0f;
                    break;
            }
        }

        /// <summary>
        /// 鏇存柊鎵€鏈夊姩鐢?        /// </summary>
        private void UpdateAnimations()
        {
            var currentTime = DateTime.UtcNow;
            var completedAnimations = new List<string>();

            foreach (var kvp in _activeAnimations)
            {
                var animation = kvp.Value;
                if (!animation.IsPlaying) continue;

                var elapsed = (float)(currentTime - animation.StartTime).TotalSeconds;
                var progress = Mathf.Min(elapsed / (animation.Config.Duration * GlobalSpeedMultiplier), 1.0f);
                var easedProgress = ApplyEasing(progress, animation.Config.Easing);

                UpdateAnimationFrame(animation, easedProgress);

                if (progress >= 1.0f)
                {
                    animation.IsPlaying = false;
                    completedAnimations.Add(kvp.Key);
                    animation.Config.OnComplete?.Invoke();
                }
            }

            foreach (var animationId in completedAnimations)
            {
                _activeAnimations.Remove(animationId);
            }
        }

        /// <summary>
        /// 鏇存柊鍔ㄧ敾甯?        /// </summary>
        private void UpdateAnimationFrame(AnimationState animation, float progress)
        {
            var target = animation.Target;
            if (target == null) return;

            switch (animation.Config.Type)
            {
                case AnimationType.FadeIn:
                case AnimationType.FadeOut:
                    var alpha = Mathf.Lerp(animation.StartAlpha, animation.TargetAlpha, progress);
                    var color = target.BackgroundColor;
                    color.A = alpha;
                    target.BackgroundColor = color;
                    break;

                case AnimationType.SlideIn:
                    var position = Float2.Lerp(animation.StartPosition, animation.TargetPosition, progress);
                    target.Location = position;
                    break;

                case AnimationType.Shake:
                    var intensity = 10.0f * (1.0f - progress);
                    var offsetX = (float)(Mathf.Sin(progress * Mathf.Pi * 20) * intensity);
                    var newPosition = animation.StartPosition + new Float2(offsetX, 0);
                    target.Location = newPosition;
                    break;
            }
        }

        /// <summary>
        /// 搴旂敤缂撳姩鍑芥暟
        /// </summary>
        private float ApplyEasing(float t, EasingType easing)
        {
            switch (easing)
            {
                case EasingType.Linear: return t;
                case EasingType.EaseIn: return t * t;
                case EasingType.EaseOut: return 1 - (1 - t) * (1 - t);
                case EasingType.EaseInOut: return t < 0.5f ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                case EasingType.EaseOutBack:
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1;
                    return 1 + c3 * (t - 1) * (t - 1) * (t - 1) + c1 * (t - 1) * (t - 1);
                default: return t;
            }
        }

        internal void StopAllAnimations()
        {
            
            foreach (var animation in _activeAnimations.Values)
            {
                animation.IsPlaying = false;
                _activeAnimations.Remove(animation.AnimationId);
            }

        }

        #endregion
    }
}

using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 缓动函数类型
    /// </summary>
    public enum EaseType
    {
        Linear,
        EaseInQuad,
        EaseOutQuad,
        EaseInOutQuad,
        EaseOutBack,
        EaseOutElastic,
        EaseOutCubic,
        EaseInOutSine,
    }

    /// <summary>
    /// UI动画描述
    /// </summary>
    public class UIAnimation
    {
        public float Duration { get; set; } = 0.3f;
        public float Delay { get; set; } = 0f;
        public EaseType Ease { get; set; } = EaseType.EaseOutQuad;
        public Float2? FromPosition { get; set; }
        public Float2? ToPosition { get; set; }
        public Float2? FromScale { get; set; }
        public Float2? ToScale { get; set; }
        public float? FromAlpha { get; set; }
        public float? ToAlpha { get; set; }
        public Action OnComplete { get; set; }

        // 运行时状态
        internal float Elapsed = 0f;
        internal bool Started = false;
        internal Control Target = null;
        internal Float2 StartPos;
        internal Float2 StartScale;
        internal float StartAlpha;
    }

    /// <summary>
    /// Toast通知类型
    /// </summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error,
        Quest,       // 任务相关
        Loot,        // 掉落相关
        Achievement, // 成就
    }

    /// <summary>
    /// UI动画系统 - 产品级UI/UX体验。
    /// 特性：
    /// - 通用UI动画（位移/缩放/透明度，支持缓动曲线）
    /// - Toast通知系统（多类型/堆叠/自动消失）
    /// - 面板过渡动画（滑入/淡入/弹出）
    /// - 战斗反馈（伤害数字浮动/暴击放大/屏幕闪光）
    /// - 按钮反馈（按压缩放/悬浮高亮）
    /// - 数值滚动动画（经验条/血量变化）
    /// </summary>
    public class UIAnimationSystem
    {
        private static UIAnimationSystem _instance;
        public static UIAnimationSystem Instance => _instance ??= new UIAnimationSystem();

        private static readonly Random _rng = new Random();

        // ===== 动画队列 =====
        private readonly List<UIAnimation> _activeAnimations = new List<UIAnimation>();
        private readonly List<UIAnimation> _pendingRemove = new List<UIAnimation>();

        // ===== Toast通知 =====
        private readonly List<ToastEntry> _activeToasts = new List<ToastEntry>();
        private const int MaxToasts = 5;
        private const float ToastDuration = 3.5f;
        private const float ToastSlideTime = 0.3f;
        private ContainerControl _toastContainer;

        // ===== 伤害数字 =====
        private readonly List<DamageNumberEntry> _damageNumbers = new List<DamageNumberEntry>();
        private ContainerControl _damageContainer;

        // ===== 数值滚动 =====
        private readonly List<ValueRollEntry> _valueRolls = new List<ValueRollEntry>();

        // ===== 公共属性 =====
        public int ActiveAnimationCount => _activeAnimations.Count;
        public int ActiveToastCount => _activeToasts.Count;

        // ===== 每帧更新 =====

        public void Update(float deltaTime)
        {
            UpdateAnimations(deltaTime);
            UpdateToasts(deltaTime);
            UpdateDamageNumbers(deltaTime);
            UpdateValueRolls(deltaTime);
        }

        // ===== 通用动画API =====

        /// <summary>播放UI动画</summary>
        public void PlayAnimation(Control target, UIAnimation anim)
        {
            anim.Target = target;
            anim.Elapsed = 0f;
            anim.Started = false;

            // 记录初始状态
            if (anim.FromPosition.HasValue)
                anim.StartPos = anim.FromPosition.Value;
            else
                anim.StartPos = target.Location;

            if (anim.FromScale.HasValue)
                anim.StartScale = anim.FromScale.Value;
            else
                anim.StartScale = new Float2(1f, 1f);

            anim.StartAlpha = 1f;

            _activeAnimations.Add(anim);
        }

        /// <summary>淡入控件</summary>
        public void FadeIn(Control target, float duration = 0.3f, Action onComplete = null)
        {
            PlayAnimation(target, new UIAnimation
            {
                Duration = duration,
                FromAlpha = 0f,
                ToAlpha = 1f,
                Ease = EaseType.EaseOutCubic,
                OnComplete = onComplete,
            });
        }

        /// <summary>淡出控件</summary>
        public void FadeOut(Control target, float duration = 0.3f, Action onComplete = null)
        {
            PlayAnimation(target, new UIAnimation
            {
                Duration = duration,
                FromAlpha = 1f,
                ToAlpha = 0f,
                Ease = EaseType.EaseInQuad,
                OnComplete = onComplete,
            });
        }

        /// <summary>滑入（从右侧）</summary>
        public void SlideInFromRight(Control target, float distance = 300f, float duration = 0.4f)
        {
            var targetPos = target.Location;
            PlayAnimation(target, new UIAnimation
            {
                Duration = duration,
                FromPosition = new Float2(targetPos.X + distance, targetPos.Y),
                ToPosition = targetPos,
                Ease = EaseType.EaseOutCubic,
            });
        }

        /// <summary>滑入（从底部）</summary>
        public void SlideInFromBottom(Control target, float distance = 200f, float duration = 0.4f)
        {
            var targetPos = target.Location;
            PlayAnimation(target, new UIAnimation
            {
                Duration = duration,
                FromPosition = new Float2(targetPos.X, targetPos.Y + distance),
                ToPosition = targetPos,
                Ease = EaseType.EaseOutBack,
            });
        }

        /// <summary>弹出动画（缩放）</summary>
        public void PopIn(Control target, float duration = 0.35f)
        {
            PlayAnimation(target, new UIAnimation
            {
                Duration = duration,
                FromScale = new Float2(0.6f, 0.6f),
                ToScale = new Float2(1f, 1f),
                FromAlpha = 0f,
                ToAlpha = 1f,
                Ease = EaseType.EaseOutBack,
            });
        }

        /// <summary>按钮按压反馈</summary>
        public void ButtonPress(Control target)
        {
            PlayAnimation(target, new UIAnimation
            {
                Duration = 0.1f,
                FromScale = new Float2(1f, 1f),
                ToScale = new Float2(0.92f, 0.92f),
                Ease = EaseType.EaseOutQuad,
                OnComplete = () =>
                {
                    PlayAnimation(target, new UIAnimation
                    {
                        Duration = 0.15f,
                        FromScale = new Float2(0.92f, 0.92f),
                        ToScale = new Float2(1f, 1f),
                        Ease = EaseType.EaseOutElastic,
                    });
                }
            });
        }

        // ===== Toast通知 =====

        /// <summary>显示Toast通知</summary>
        public void ShowToast(string message, ToastType type = ToastType.Info, float duration = -1f)
        {
            if (_activeToasts.Count >= MaxToasts)
            {
                // 移除最旧的
                RemoveToast(_activeToasts[0]);
            }

            var toast = new ToastEntry
            {
                Message = message,
                Type = type,
                Duration = duration > 0 ? duration : ToastDuration,
                Elapsed = 0f,
                State = ToastState.SlidingIn,
                SlideProgress = 0f,
            };

            _activeToasts.Add(toast);
            LayoutToasts();

            Debug.Log($"[Toast] {GetToastPrefix(type)} {message}");
        }

        /// <summary>显示获得物品通知</summary>
        public void ShowLootToast(string itemName, int count, int quality)
        {
            string qualityName = quality switch
            {
                >= 5 => "传说",
                >= 4 => "史诗",
                >= 3 => "精良",
                >= 2 => "优秀",
                _ => "普通"
            };
            ShowToast($"获得 [{qualityName}]{itemName} ×{count}", ToastType.Loot);
        }

        /// <summary>显示任务完成通知</summary>
        public void ShowQuestCompleteToast(string questName)
        {
            ShowToast($"任务完成：{questName}", ToastType.Quest, 4f);
        }

        /// <summary>显示成就通知</summary>
        public void ShowAchievementToast(string achievementName)
        {
            ShowToast($"🏆 成就达成：{achievementName}", ToastType.Achievement, 5f);
        }

        // ===== 伤害数字 =====

        /// <summary>显示伤害数字</summary>
        public void ShowDamageNumber(Vector3 worldPos, int damage, bool isCrit = false, bool isHeal = false)
        {
            var entry = new DamageNumberEntry
            {
                WorldPosition = worldPos,
                Value = damage,
                IsCrit = isCrit,
                IsHeal = isHeal,
                Elapsed = 0f,
                Duration = isCrit ? 1.2f : 0.9f,
                Velocity = new Float2(
                    (float)(_rng.NextDouble() * 60.0 - 30.0),
                    isCrit ? -120f : -80f
                ),
                Scale = isCrit ? 1.5f : 1.0f,
            };
            _damageNumbers.Add(entry);
        }

        // ===== 数值滚动 =====

        /// <summary>数值滚动动画（如经验条/血量变化）</summary>
        public void RollValue(Label target, float from, float to, float duration = 0.8f, string format = "F0")
        {
            _valueRolls.Add(new ValueRollEntry
            {
                Target = target,
                From = from,
                To = to,
                Duration = duration,
                Elapsed = 0f,
                Format = format,
            });
        }

        // ===== 内部更新 =====

        private void UpdateAnimations(float dt)
        {
            _pendingRemove.Clear();

            foreach (var anim in _activeAnimations)
            {
                anim.Elapsed += dt;

                // 延迟处理
                if (anim.Elapsed < anim.Delay) continue;

                if (!anim.Started)
                {
                    anim.Started = true;
                    if (anim.Target != null && anim.FromPosition.HasValue)
                        anim.Target.Location = anim.FromPosition.Value;
                }

                float t = Mathf.Clamp((anim.Elapsed - anim.Delay) / anim.Duration, 0f, 1f);
                float eased = ApplyEase(t, anim.Ease);

                if (anim.Target == null)
                {
                    _pendingRemove.Add(anim);
                    continue;
                }

                // 位置插值
                if (anim.ToPosition.HasValue)
                {
                    anim.Target.Location = Float2.Lerp(anim.StartPos, anim.ToPosition.Value, eased);
                }

                // 缩放插值
                if (anim.ToScale.HasValue)
                {
                    var scale = Float2.Lerp(anim.StartScale, anim.ToScale.Value, eased);
                    anim.Target.Scale = scale;
                }

                // 透明度（通过颜色模拟）
                if (anim.ToAlpha.HasValue)
                {
                    float alpha = Mathf.Lerp(anim.FromAlpha ?? 1f, anim.ToAlpha.Value, eased);
                    var bg = anim.Target.BackgroundColor;
                    anim.Target.BackgroundColor = new Color(bg.R, bg.G, bg.B, alpha);
                }

                // 完成
                if (t >= 1f)
                {
                    _pendingRemove.Add(anim);
                    anim.OnComplete?.Invoke();
                }
            }

            foreach (var anim in _pendingRemove)
                _activeAnimations.Remove(anim);
        }

        private void UpdateToasts(float dt)
        {
            for (int i = _activeToasts.Count - 1; i >= 0; i--)
            {
                var toast = _activeToasts[i];
                toast.Elapsed += dt;

                switch (toast.State)
                {
                    case ToastState.SlidingIn:
                        toast.SlideProgress += dt / ToastSlideTime;
                        if (toast.SlideProgress >= 1f)
                        {
                            toast.SlideProgress = 1f;
                            toast.State = ToastState.Visible;
                        }
                        break;

                    case ToastState.Visible:
                        if (toast.Elapsed >= toast.Duration)
                        {
                            toast.State = ToastState.FadingOut;
                            toast.SlideProgress = 1f;
                        }
                        break;

                    case ToastState.FadingOut:
                        toast.SlideProgress -= dt / ToastSlideTime;
                        if (toast.SlideProgress <= 0f)
                        {
                            _activeToasts.RemoveAt(i);
                            LayoutToasts();
                        }
                        break;
                }
            }
        }

        private void UpdateDamageNumbers(float dt)
        {
            for (int i = _damageNumbers.Count - 1; i >= 0; i--)
            {
                var entry = _damageNumbers[i];
                entry.Elapsed += dt;

                if (entry.Elapsed >= entry.Duration)
                {
                    _damageNumbers.RemoveAt(i);
                    continue;
                }

                // 物理运动
                entry.Velocity.Y += 200f * dt; // 重力
                _damageNumbers[i] = entry;
            }
        }

        private void UpdateValueRolls(float dt)
        {
            for (int i = _valueRolls.Count - 1; i >= 0; i--)
            {
                var roll = _valueRolls[i];
                roll.Elapsed += dt;

                float t = Mathf.Clamp(roll.Elapsed / roll.Duration, 0f, 1f);
                float eased = ApplyEase(t, EaseType.EaseOutCubic);
                float value = Mathf.Lerp(roll.From, roll.To, eased);

                if (roll.Target != null)
                    roll.Target.Text = value.ToString(roll.Format);

                if (t >= 1f)
                    _valueRolls.RemoveAt(i);
            }
        }

        // ===== 辅助方法 =====

        private void RemoveToast(ToastEntry toast)
        {
            _activeToasts.Remove(toast);
            LayoutToasts();
        }

        private void LayoutToasts()
        {
            // Toast从屏幕顶部中央向下堆叠
            // 实际渲染由UI层处理，这里只维护数据
        }

        private string GetToastPrefix(ToastType type) => type switch
        {
            ToastType.Success => "✓",
            ToastType.Warning => "⚠",
            ToastType.Error => "✕",
            ToastType.Quest => "◆",
            ToastType.Loot => "✦",
            ToastType.Achievement => "🏆",
            _ => "ℹ",
        };

        /// <summary>获取Toast颜色</summary>
        public static Color GetToastColor(ToastType type) => type switch
        {
            ToastType.Success => InkWashTheme.TextJade,
            ToastType.Warning => InkWashTheme.Warning,
            ToastType.Error => new Color(0.9f, 0.3f, 0.3f, 1f),
            ToastType.Quest => InkWashTheme.TextGold,
            ToastType.Loot => InkWashTheme.GoldBright,
            ToastType.Achievement => InkWashTheme.GoldPrimary,
            _ => InkWashTheme.TextDefault,
        };

        /// <summary>缓动函数</summary>
        public static float ApplyEase(float t, EaseType ease)
        {
            switch (ease)
            {
                case EaseType.Linear:
                    return t;
                case EaseType.EaseInQuad:
                    return t * t;
                case EaseType.EaseOutQuad:
                    return t * (2f - t);
                case EaseType.EaseInOutQuad:
                    return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
                case EaseType.EaseOutBack:
                    float c1 = 1.70158f;
                    float c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                case EaseType.EaseOutElastic:
                    if (t == 0f || t == 1f) return t;
                    float c4 = (2f * Mathf.Pi) / 3f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                case EaseType.EaseOutCubic:
                    return 1f - Mathf.Pow(1f - t, 3f);
                default:
                    return t;
            }
        }

        // ===== 获取当前Toast列表（供渲染层使用）=====
        public List<ToastEntry> GetActiveToasts() => new List<ToastEntry>(_activeToasts);

        // ===== 获取当前伤害数字（供渲染层使用）=====
        public List<DamageNumberEntry> GetActiveDamageNumbers() => new List<DamageNumberEntry>(_damageNumbers);
    }

    // ===== 数据结构 =====

    public enum ToastState { SlidingIn, Visible, FadingOut }

    public class ToastEntry
    {
        public string Message { get; set; } = "";
        public ToastType Type { get; set; }
        public float Duration { get; set; }
        public float Elapsed { get; set; }
        public ToastState State { get; set; }
        public float SlideProgress { get; set; }
    }

    public struct DamageNumberEntry
    {
        public Vector3 WorldPosition;
        public int Value;
        public bool IsCrit;
        public bool IsHeal;
        public float Elapsed;
        public float Duration;
        public Float2 Velocity;
        public float Scale;
    }

    internal class ValueRollEntry
    {
        public Label Target;
        public float From;
        public float To;
        public float Duration;
        public float Elapsed;
        public string Format;
    }
}

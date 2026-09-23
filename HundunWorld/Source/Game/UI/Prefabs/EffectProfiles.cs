using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.Components;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>动画触发时机。</summary>
    public enum UIAnimationTrigger
    {
        /// <summary>OnStart 时自动播放</summary>
        OnLoad = 0,

        /// <summary>目标控件 Clicked 事件触发时播放</summary>
        OnClick = 1,

        /// <summary>仅由代码 / <see cref="UIAnimationProfile.Play"/> 手动触发</summary>
        Manual = 2
    }

    /// <summary>可 Prefab 化的动画种类（映射到 <see cref="UIAnimationManager"/> 公开方法）。</summary>
    public enum UIPrefabAnimationKind
    {
        /// <summary>淡入</summary>
        FadeIn = 0,
        /// <summary>淡出（结束后隐藏）</summary>
        FadeOut = 1,
        /// <summary>滑入（按偏移量）</summary>
        SlideIn = 2,
        /// <summary>滑出（按偏移量）</summary>
        SlideOut = 3,
        /// <summary>缩放弹入</summary>
        ScaleIn = 4,
        /// <summary>抖动</summary>
        Shake = 5,
        /// <summary>弹跳</summary>
        Bounce = 6
    }

    /// <summary>
    /// 波纹效果 Prefab 参数档案（对应 <see cref="RippleEffect"/>）。
    /// 通常作为按钮 Prefab 的子控件叠加使用。
    /// </summary>
    [ExecuteInEditMode]
    public class RippleEffectProfile : UIComponentProfile
    {
        /// <summary>波纹时长（秒）</summary>
        [EditorOrder(100), EditorDisplay("波纹")]
        public float Duration = 0.3f;

        /// <summary>最大半径覆写，&lt;=0 表示按控件尺寸自动计算</summary>
        [EditorOrder(101), EditorDisplay("波纹"), Tooltip("最大半径覆写，<=0 按控件尺寸自动计算")]
        public float MaxRadius = -1f;

        /// <summary>波纹颜色令牌名</summary>
        [EditorOrder(102), EditorDisplay("波纹"), Tooltip("波纹颜色令牌名（如 GoldPrimary），留空使用控件默认")]
        public string ColorToken = "GoldPrimary";

        /// <summary>宿主按钮 Clicked 时自动发波（中心=宿主控件中心）</summary>
        [EditorOrder(103), EditorDisplay("波纹"), Tooltip("订阅宿主 UIControl 按钮 Clicked 自动播放波纹")]
        public bool TriggerOnHostClick = true;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new RippleEffect();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is RippleEffect ripple)
            {
                ripple.Duration = Duration;
                if (UIPrefabUtils.TryResolveColorToken(ColorToken, out var color))
                    ripple.RippleColor = color;
            }
        }

        /// <inheritdoc />
        public override void OnStart()
        {
            base.OnStart();
            if (!TriggerOnHostClick || BoundControl is not RippleEffect ripple)
                return;

            var host = UIPrefabUtils.FindHostUIControl(Actor);
            var button = host != null && host.HasControl ? host.Control : null;
            if (button == null && Actor.Parent != null)
            {
                // 常见组织：波纹与按钮同为某容器 UIControl 的子级
                var parentHost = UIPrefabUtils.FindHostUIControl(Actor.Parent);
                button = parentHost != null && parentHost.HasControl ? parentHost.Control : null;
            }
            if (button == null)
                return;

            var eventInfo = button.GetType().GetEvent("Clicked",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            if (eventInfo == null)
                return;

            if (eventInfo.EventHandlerType == typeof(Action))
                eventInfo.AddEventHandler(button, new Action(() => Play(Float2.Zero)));
            else
                Play(Float2.Zero);
        }

        /// <summary>在指定位置（控件本地坐标，空=中心）播放一次波纹。</summary>
        public void Play(Float2 center)
        {
            if (BoundControl is not RippleEffect ripple)
                return;

            var size = ripple.Size;
            var radius = MaxRadius > 0f
                ? MaxRadius
                : MathF.Sqrt(size.X * size.X + size.Y * size.Y) * 0.5f;
            if (center == Float2.Zero)
                center = new Float2(size.X * 0.5f, size.Y * 0.5f);
            ripple.StartRipple(center, radius);
        }
    }

    /// <summary>
    /// 通用 UI 动画 Prefab 参数档案（数据驱动 <see cref="UIAnimationManager"/>）。
    /// 挂在与目标控件同一宿主（或父链有 UICanvas）的 Actor 上，
    /// 通过 <see cref="TargetControlName"/>（注册名）解析动画目标。
    /// </summary>
    public class UIAnimationProfile : Script
    {
        /// <summary>动画目标控件登记名（留空 = 本 Actor 上 UIControl 的控件）</summary>
        [EditorOrder(0), EditorDisplay("动画"), Tooltip("目标控件登记名（UIComponentProfile.ControlName）；留空使用本 Actor 上 UIControl 的控件")]
        public string TargetControlName = string.Empty;

        /// <summary>动画种类</summary>
        [EditorOrder(1), EditorDisplay("动画")]
        public UIPrefabAnimationKind Kind = UIPrefabAnimationKind.FadeIn;

        /// <summary>时长（秒）</summary>
        [EditorOrder(2), EditorDisplay("动画")]
        public float Duration = 0.3f;

        /// <summary>缓动曲线</summary>
        [EditorOrder(3), EditorDisplay("动画")]
        public EasingType Easing = EasingType.EaseOut;

        /// <summary>触发时机</summary>
        [EditorOrder(4), EditorDisplay("动画")]
        public UIAnimationTrigger Trigger = UIAnimationTrigger.OnLoad;

        /// <summary>循环间隔（秒），0 表示不循环</summary>
        [EditorOrder(5), EditorDisplay("动画"), Tooltip("循环间隔（秒），0 表示不循环")]
        public float LoopInterval = 0f;

        /// <summary>SlideIn/SlideOut 的偏移量</summary>
        [EditorOrder(6), EditorDisplay("动画")]
        public Float2 SlideOffset = new Float2(0f, 40f);

        /// <summary>动画完成回调（代码侧订阅）。</summary>
        public event Action AnimationCompleted;

        private Control _target;
        private float _loopTimer;
        private bool _playing;

        /// <inheritdoc />
        public override void OnStart()
        {
            _target = ResolveTarget();

            if (Trigger == UIAnimationTrigger.OnClick && _target != null)
            {
                var eventInfo = _target.GetType().GetEvent("Clicked",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                if (eventInfo != null)
                {
                    if (eventInfo.EventHandlerType == typeof(Action))
                        eventInfo.AddEventHandler(_target, new Action(Play));
                    else if (eventInfo.EventHandlerType.IsGenericType)
                    {
                        var handler = (Delegate)System.Activator.CreateInstance(
                            typeof(Action<>).MakeGenericType(eventInfo.EventHandlerType.GetGenericArguments()[0]),
                            (object)(Action)(() => Play()));
                        eventInfo.AddEventHandler(_target, handler);
                    }
                }
            }
            else if (Trigger == UIAnimationTrigger.OnLoad)
            {
                Play();
            }
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (LoopInterval <= 0f || _playing)
                return;
            _loopTimer += Time.DeltaTime;
            if (_loopTimer >= LoopInterval)
            {
                _loopTimer = 0f;
                Play();
            }
        }

        /// <summary>播放一次动画。</summary>
        public void Play()
        {
            var target = _target != null && !_target.IsDisposing ? _target : ResolveTarget();
            if (target == null)
            {
                Debug.LogWarning($"[UIAnimationProfile] 找不到动画目标控件: \"{TargetControlName}\"（Actor={Actor?.Name}）");
                return;
            }
            _target = target;
            _playing = true;
            if (LoopInterval > 0f)
                _loopTimer = 0f;

            var manager = UIAnimationManager.Instance;
            if (manager == null)
                return;

            Action onComplete = () =>
            {
                _playing = false;
                AnimationCompleted?.Invoke();
            };

            switch (Kind)
            {
                case UIPrefabAnimationKind.FadeIn:
                    manager.FadeIn(target, Duration, Easing, onComplete);
                    break;
                case UIPrefabAnimationKind.FadeOut:
                    manager.FadeOut(target, Duration, Easing, onComplete);
                    break;
                case UIPrefabAnimationKind.SlideIn:
                    manager.SlideIn(target, target.Location + SlideOffset, Duration, Easing, onComplete);
                    break;
                case UIPrefabAnimationKind.SlideOut:
                    manager.SlideOut(target, target.Location + SlideOffset, Duration, Easing, onComplete);
                    break;
                case UIPrefabAnimationKind.ScaleIn:
                    manager.ScaleIn(target, Duration, Easing, onComplete);
                    break;
                case UIPrefabAnimationKind.Shake:
                    manager.Shake(target, Duration, onComplete);
                    break;
                case UIPrefabAnimationKind.Bounce:
                    manager.Bounce(target, Duration, onComplete);
                    break;
            }
        }

        private Control ResolveTarget()
        {
            if (!string.IsNullOrEmpty(TargetControlName))
            {
                var registered = UIPrefabRegistry.Resolve(TargetControlName);
                if (registered != null)
                    return registered;
            }

            var host = UIPrefabUtils.FindHostUIControl(Actor);
            if (host != null && host.HasControl)
                return host.Control;
            return null;
        }
    }
}

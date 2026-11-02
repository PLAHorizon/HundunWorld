using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// 模拟滑块的拖拽方式。对应 UE5 <c>EAnalogDragType</c>。
    /// </summary>
    public enum NarrativeAnalogDragType
    {
        /// <summary>绝对拖拽：按下位置即值（鼠标直接点/拖，UE5 默认滑块行为）。</summary>
        Absolute = 0,

        /// <summary>相对拖拽：按鼠标/摇杆的位移增量推 DragPercentage（对应 UE5 用手柄摇杆驱动的 AnalogSlider）。</summary>
        Relative = 1
    }

    /// <summary>
    /// Narrative 模拟滑块。对应 UE5 <c>UNarrativeAnalogSlider</c>（继承 UAnalogSlider）。
    /// <para>
    /// UE5 里这个子类只做一件事：<c>RebuildWidget</c> 时把 <c>UIPrimaryColor</c> 从开发者设置里
    /// 拉出来覆盖设计器设置的 <c>SliderHandleColor</c>。Flax 侧用真正的
    /// <see cref="Slider"/> 派生实现同样的行为（<see cref="ApplyTheme"/>），
    /// 并把 AnalogSlider 的核心概念 —— 模拟拖拽百分比 —— 落在 Flax 可表达的行为上：
    /// Absolute 模式沿用 Flax 的鼠标拖拽，Relative 模式用鼠标位移增量推 DragPercentage
    /// （UE5 由摇杆提供的能力，Flax 1.12 没有面向脚本的摇杆轴事件，见 <see cref="UpdateRelativeDrag"/>）。
    /// </para>
    /// </summary>
    public class NarrativeAnalogSlider : Slider
    {
        /// <summary>拖拽方式。对应 UE5 AnalogSlider 的 EAnalogDragType。</summary>
        public NarrativeAnalogDragType DragType = NarrativeAnalogDragType.Absolute;

        /// <summary>
        /// 模拟拖拽百分比（0-1）。对应 UE5 <c>SAnalogSlider::GetDragPercentage</c>
        /// —— 摇杆/相对拖拽下"按下点相对滑块的偏移比例"。
        /// </summary>
        public float DragPercentage { get; private set; }

        /// <summary>Relative 模式下推满量程所需的鼠标位移（像素）。越小越灵敏。</summary>
        public float RelativeDragDistance = 240f;

        /// <summary>值变化通知阈值（0-1 归一化量程内）。对应 UE5 AnalogSlider 的 TickThreshold 语义。</summary>
        public float ValueChangeThreshold = 0.01f;

        /// <summary>是否把滑块手柄涂成主题主色（UE5 子类唯一的行为，默认开启）。</summary>
        public bool bUseThemeHandleColor = true;

        /// <summary>是否把填充轨道涂成主题反色强调。</summary>
        public bool bUseThemeFillTrack = true;

        private bool _bRelativeDragging;
        private float _lastNotifiedValue;

        /// <summary>值超过 <see cref="ValueChangeThreshold"/> 变化时触发。</summary>
        public event Action<NarrativeAnalogSlider> AnalogValueChanged;

        /// <summary>拖拽结束（鼠标释放 / 相对拖拽捕获结束）时触发。</summary>
        public event Action<NarrativeAnalogSlider> SlidingCommitted;

        public NarrativeAnalogSlider()
        {
            // Flax GUI 约定：构造期非 TopLeft 锚会按父尺寸 0 重算位置，统一用 TopLeft，尺寸交给宿主布局。
            AnchorPreset = AnchorPresets.TopLeft;

            _lastNotifiedValue = Value;
            ValueChanged += OnFlaxValueChanged;
            SlidingEnd += () => SlidingCommitted?.Invoke(this);

            ApplyTheme();
        }

        /// <summary>
        /// 重建控件外观。对应 UE5 <c>RebuildWidget</c>：
        /// 从 <see cref="NarrativeUIDeveloperSettings"/> 取主色覆盖设计器里设的手柄颜色。
        /// </summary>
        public virtual void RebuildWidget()
        {
            ApplyTheme();
        }

        /// <summary>把开发者设置的主题色落到 Flax Slider 的真实属性上。</summary>
        public void ApplyTheme()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            if (bUseThemeHandleColor)
            {
                // UE5: SetSliderHandleColor(Settings->UIPrimaryColor)
                ThumbColor = settings.UIPrimaryColor;
                ThumbColorHighlighted = Color.Lerp(settings.UIPrimaryColor, Color.White, 0.25f);
                ThumbColorSelected = Color.Lerp(settings.UIPrimaryColor, Color.White, 0.45f);
                ThumbBrush = new SolidColorBrush(settings.UIPrimaryColor);
            }

            if (bUseThemeFillTrack && FillTrack)
            {
                TrackFillLineColor = settings.UIInvertAccentColor;
                FillTrackBrush = new SolidColorBrush(settings.UIInvertAccentColor);
                TrackLineColor = settings.UIInvertColor;
            }
        }

        /// <summary>设置模拟拖拽百分比（0-1），等价 UE5 由摇杆驱动的输入口。</summary>
        public void SetDragPercentage(float percentage, bool bCommit = true)
        {
            DragPercentage = Math.Max(0f, Math.Min(1f, percentage));

            float range = Maximum - Minimum;
            float newValue = Minimum + DragPercentage * range;

            if (bCommit)
            {
                Value = newValue;
                NotifyIfChanged(newValue);
            }
        }

        /// <summary>按当前 Value 反推 DragPercentage。</summary>
        public float GetDragPercentage()
        {
            float range = Maximum - Minimum;
            if (range <= 0f) return 0f;
            DragPercentage = Math.Max(0f, Math.Min(1f, (Value - Minimum) / range));
            return DragPercentage;
        }

        // ===== 输入处理 =====

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (DragType == NarrativeAnalogDragType.Relative && button == MouseButton.Left)
            {
                // 相对模式不走基类的"按下点即值"，改为位移增量驱动
                Focus();
                _bRelativeDragging = true;
                StartMouseCapture(false);
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        public override void OnMouseMoveRelative(Float2 motion)
        {
            base.OnMouseMoveRelative(motion);

            if (_bRelativeDragging)
            {
                UpdateRelativeDrag(motion.X);
            }
        }

        public override void OnEndMouseCapture()
        {
            base.OnEndMouseCapture();

            if (_bRelativeDragging)
            {
                _bRelativeDragging = false;
                SlidingCommitted?.Invoke(this);
            }
        }

        /// <summary>
        /// 用一帧的横向位移推进 DragPercentage。
        /// UE5 由 SAnalogSlider 的摇杆输入提供，Flax 侧对应鼠标/触摸的相对位移
        /// （手柄输入可通过 <see cref="SetDragPercentage"/> 由外部摇杆轮询直接驱动）。
        /// </summary>
        public void UpdateRelativeDrag(float deltaPixels)
        {
            float distance = RelativeDragDistance > 0f ? RelativeDragDistance : 240f;
            SetDragPercentage(GetDragPercentage() + deltaPixels / distance);
        }

        private void OnFlaxValueChanged()
        {
            GetDragPercentage();
            NotifyIfChanged(Value);
        }

        private void NotifyIfChanged(float newValue)
        {
            float range = Maximum - Minimum;
            float normalizedDelta = range > 0f ? Math.Abs(newValue - _lastNotifiedValue) / range : 0f;

            if (normalizedDelta >= (ValueChangeThreshold > 0f ? ValueChangeThreshold : 0.01f))
            {
                _lastNotifiedValue = newValue;
                AnalogValueChanged?.Invoke(this);
            }
        }
    }
}

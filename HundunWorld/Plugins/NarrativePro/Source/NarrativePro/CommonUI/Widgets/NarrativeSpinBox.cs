using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative SpinBox。对应 UE5 <c>UNarrativeSpinBox</c>（继承 USpinBox）。
    /// <para>
    /// 真正的 Flax 组合控件：<see cref="ValueLabel"/>（<see cref="NarrativeCommonTextBlock"/>）显示数值，
    /// <see cref="DecreaseButton"/> / <see cref="IncreaseButton"/>（<see cref="Button"/>）按步长加减，
    /// 底部用 <see cref="Render2D"/> 画一条归一化填充条（对应 UE5 FSpinBoxStyle 的 FillBrush），
    /// 颜色全部从 <see cref="NarrativeUIDeveloperSettings"/> 的 UIPrimary / UIInvert 取（与 UE5 行为一致）。
    /// </para>
    /// <para>
    /// 数值语义对应 UE5 USpinBox：<see cref="MinValue"/> / <see cref="MaxValue"/> 钳制、
    /// <see cref="Step"/> 步长、按住按钮连发（对应 UE5 的按键重复）、
    /// <see cref="OnValueChange"/> / <see cref="OnValueCommitted"/> 两个事件（对应 ValueChange / ValueCommitted）。
    /// </para>
    /// </summary>
    public class NarrativeSpinBox : ContainerControl
    {
        /// <summary>前景色（对应 UE5 SetForegroundColor）。</summary>
        public Color ForegroundColor = Color.White;

        /// <summary>背景画刷颜色（对应 UE5 BackgroundBrush.TintColor）。</summary>
        public new Color BackgroundColor { get; set; } = Color.Black;

        /// <summary>激活时背景画刷颜色（对应 UE5 ActiveBackgroundBrush.TintColor）。</summary>
        public Color ActiveBackgroundColor = Color.Black;

        /// <summary>悬停时背景画刷颜色（对应 UE5 HoveredBackgroundBrush.TintColor）。</summary>
        public Color HoveredBackgroundColor = Color.Black;

        /// <summary>激活时填充画刷颜色（对应 UE5 ActiveFillBrush.TintColor）。</summary>
        public Color ActiveFillColor = Color.Black;

        /// <summary>悬停时填充画刷颜色（对应 UE5 HoveredFillBrush.TintColor）。</summary>
        public Color HoveredFillColor = Color.Black;

        /// <summary>非激活时填充画刷颜色（对应 UE5 InactiveFillBrush.TintColor）。</summary>
        public Color InactiveFillColor = Color.Black;

        /// <summary>数值显示格式。对应 UE5 SpinBox 的 ValuePrefix/DecimalCount 组合语义。</summary>
        public string ValueFormat = "0.###";

        /// <summary>步长（对应 UE5 StepSize / MinSliderDelta）。</summary>
        public float Step = 1f;

        /// <summary>连发触发前的按住时长（秒，对应 UE5 的按键重复延迟）。</summary>
        public float RepeatDelay = 0.4f;

        /// <summary>连发间隔（秒）。</summary>
        public float RepeatInterval = 0.12f;

        private float _value;
        private float _minValue;
        private float _maxValue = 100f;
        private float _repeatTimer;
        private float _repeatAccumulator;
        private int _heldDirection;
        private bool _bColorsFromTheme;

        /// <summary>数值显示（真实 Flax 文本控件）。</summary>
        public NarrativeCommonTextBlock ValueLabel { get; private set; }

        /// <summary>减量按钮。</summary>
        public Button DecreaseButton { get; private set; }

        /// <summary>增量按钮。</summary>
        public Button IncreaseButton { get; private set; }

        /// <summary>填充条高度（像素）。</summary>
        public float FillBarHeight = 3f;

        /// <summary>值变化事件（每次有效改动都派发）。对应 UE5 OnValueChange。</summary>
        public event Action<float> OnValueChange;

        /// <summary>值提交事件（用户交互结束时派发）。对应 UE5 OnValueCommitted。</summary>
        public event Action<float> OnValueCommitted;

        /// <summary>当前数值（写入即按 [MinValue, MaxValue] 钳制）。对应 UE5 USpinBox 的 Value。</summary>
        public float Value
        {
            get => _value;
            set => SetValue(value);
        }

        /// <summary>最小值。对应 UE5 USpinBox 的 MinValue。</summary>
        public float MinValue
        {
            get => _minValue;
            set
            {
                _minValue = value;
                if (_maxValue < _minValue) _maxValue = _minValue;
                ClampValue();
            }
        }

        /// <summary>最大值。对应 UE5 USpinBox 的 MaxValue。</summary>
        public float MaxValue
        {
            get => _maxValue;
            set
            {
                _maxValue = value;
                if (_minValue > _maxValue) _minValue = _maxValue;
                ClampValue();
            }
        }

        /// <summary>构造函数。建立真实子控件并落地默认主题。</summary>
        public NarrativeSpinBox()
        {
            // 构造期只用 TopLeft 锚 + 显式尺寸（非 TopLeft 锚会在父尺寸为 0 时算错位置）
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(200f, 26f);

            ValueLabel = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(30f, 0f),
                Size = new Float2(Size.X - 60f, Size.Y),
                Text = _value.ToString(ValueFormat),
                Style = "Button",
            };
            ValueLabel.SetJustification(ETextJustify.Center);
            AddChild(ValueLabel);

            DecreaseButton = CreateStepButton("−", -1);
            IncreaseButton = CreateStepButton("+", 1);

            ArrangeChildren();
            ApplyThemeColors();
        }

        private Button CreateStepButton(string glyph, int direction)
        {
            var button = new Button
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(26f, Size.Y),
                Text = glyph,
                TextColor = ForegroundColor,
                HasBorder = true,
                BorderThickness = 1f,
            };
            button.Clicked += () => StepBy(direction, true);
            AddChild(button);
            return button;
        }

        /// <summary>
        /// 重建控件。对应 UE5 RebuildWidget：从 NarrativeUIDeveloperSettings 拉主色与反色，
        /// 应用到 FSpinBoxStyle 的各个画刷（这里是前景/背景/填充色 + 子控件外观）。
        /// </summary>
        public virtual void RebuildWidget()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings != null)
            {
                SetForegroundColor(settings.UIPrimaryColor);

                // 应用反色到背景与填充画刷（与 UE5 行为一致）。
                BackgroundColor = settings.UIInvertColor;
                ActiveBackgroundColor = settings.UIInvertAccentColor;
                HoveredBackgroundColor = settings.UIInvertAccentColor;
                ActiveFillColor = settings.UIInvertAccentColor;
                HoveredFillColor = settings.UIPrimaryColor;
                InactiveFillColor = settings.UIInvertColor;
                _bColorsFromTheme = true;
            }

            ApplyThemeColors();

            if (ValueLabel != null)
            {
                ValueLabel.SetText(_value.ToString(ValueFormat));
            }

            ArrangeChildren();
        }

        /// <summary>设置前景色。对应 UE5 SetForegroundColor。</summary>
        public void SetForegroundColor(Color color)
        {
            ForegroundColor = color;

            if (ValueLabel != null) ValueLabel.TextColor = color;
            if (DecreaseButton != null) DecreaseButton.TextColor = color;
            if (IncreaseButton != null) IncreaseButton.TextColor = color;
        }

        /// <summary>把主题色落到背景/按钮画刷。</summary>
        public void ApplyThemeColors()
        {
            base.BackgroundColor = BackgroundColor;

            if (DecreaseButton != null)
            {
                DecreaseButton.BackgroundColor = HoveredBackgroundColor;
                DecreaseButton.BackgroundColorHighlighted = ActiveBackgroundColor;
                DecreaseButton.BorderColor = ForegroundColor;
            }

            if (IncreaseButton != null)
            {
                IncreaseButton.BackgroundColor = HoveredBackgroundColor;
                IncreaseButton.BackgroundColorHighlighted = ActiveBackgroundColor;
                IncreaseButton.BorderColor = ForegroundColor;
            }
        }

        /// <summary>设置当前数值（钳制到 [MinValue, MaxValue]）。</summary>
        public void SetValue(float newValue)
        {
            ApplyValue(newValue, false);
        }

        /// <summary>按步长增减。对应 UE5 USpinBox 的 OnIncrement/OnDecrement。</summary>
        public void AddValue(float delta)
        {
            ApplyValue(_value + delta, true);
        }

        /// <summary>设置最小值 / 最大值（对应 UE5 SetMinValue / SetMaxValue）。</summary>
        public void SetMinValue(float minValue) => MinValue = minValue;

        /// <summary>设置最大值。</summary>
        public void SetMaxValue(float maxValue) => MaxValue = maxValue;

        /// <summary>设置步长（对应 UE5 SetStepSize）。</summary>
        public void SetStep(float step) => Step = System.Math.Abs(step);

        /// <summary>读回当前步长。</summary>
        public float GetStep() => Step;

        private void StepBy(int direction, bool bCommit)
        {
            ApplyValue(_value + direction * Step, bCommit);
        }

        private void ApplyValue(float candidate, bool bFromInteraction)
        {
            float clamped = Clamp(candidate);
            bool changed = !IsNearlyEqual(clamped, _value);
            _value = clamped;

            if (ValueLabel != null)
            {
                ValueLabel.SetText(_value.ToString(ValueFormat));
            }

            if (changed)
            {
                OnValueChange?.Invoke(_value);
                if (bFromInteraction) OnValueCommitted?.Invoke(_value);
            }

        }

        private void ClampValue()
        {
            float clamped = Clamp(_value);
            if (IsNearlyEqual(clamped, _value)) return;
            _value = clamped;
            ValueLabel?.SetText(_value.ToString(ValueFormat));
            OnValueChange?.Invoke(_value);
        }

        private float Clamp(float v)
        {
            return System.Math.Max(_minValue, System.Math.Min(_maxValue, v));
        }

        private static bool IsNearlyEqual(float a, float b)
        {
            return System.Math.Abs(a - b) < 0.0001f;
        }

        /// <summary>显式重排子控件（TopLeft 锚 + 显式 Location，跟随本控件尺寸）。</summary>
        public void ArrangeChildren()
        {
            float width = System.Math.Max(60f, Size.X);
            float height = System.Math.Max(18f, Size.Y);

            if (DecreaseButton != null)
            {
                DecreaseButton.Location = Float2.Zero;
                DecreaseButton.Size = new Float2(26f, height);
            }

            if (IncreaseButton != null)
            {
                IncreaseButton.Location = new Float2(width - 26f, 0f);
                IncreaseButton.Size = new Float2(26f, height);
            }

            if (ValueLabel != null)
            {
                ValueLabel.Location = new Float2(26f, 0f);
                ValueLabel.Size = new Float2(width - 52f, height - FillBarHeight);
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            ArrangeChildren();
        }

        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            ApplyThemeColors();
            ArrangeChildren();
        }

        /// <summary>绘制归一化填充条（对应 UE5 FSpinBoxStyle 的 FillBrush）。</summary>
        public override void Draw()
        {
            base.Draw();

            if (!Visible || Size.X <= 0f || Size.Y <= FillBarHeight) return;

            float span = _maxValue - _minValue;
            float ratio = span > 0f ? (_value - _minValue) / span : 0f;
            if (ratio < 0f) ratio = 0f;
            else if (ratio > 1f) ratio = 1f;

            Color fillColor = _bColorsFromTheme ? ActiveFillColor : InactiveFillColor;
            if (IncreaseButton != null && IncreaseButton.IsMouseOver ||
                DecreaseButton != null && DecreaseButton.IsMouseOver)
            {
                fillColor = HoveredFillColor;
            }

            float barWidth = System.Math.Max(0f, (Size.X - 4f) * ratio);
            Render2D.FillRectangle(
                new Rectangle(2f, Size.Y - FillBarHeight, barWidth, FillBarHeight),
                fillColor);
        }

        /// <summary>按住加/减按钮时连发（对应 UE5 SpinBox 的按键重复）。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            int direction = 0;
            if (DecreaseButton != null && DecreaseButton.IsPressed) direction = -1;
            else if (IncreaseButton != null && IncreaseButton.IsPressed) direction = 1;

            if (direction == 0)
            {
                _heldDirection = 0;
                _repeatTimer = 0f;
                _repeatAccumulator = 0f;
                return;
            }

            if (direction != _heldDirection)
            {
                _heldDirection = direction;
                _repeatTimer = 0f;
                _repeatAccumulator = 0f;
                return;
            }

            _repeatTimer += deltaTime;
            if (_repeatTimer < RepeatDelay) return;

            _repeatAccumulator += deltaTime;
            if (_repeatAccumulator < RepeatInterval) return;

            _repeatAccumulator = 0f;
            StepBy(direction, false);
        }

        /// <summary>键盘微调（上下/左右方向键）。对应 UE5 SpinBox 的键盘交互。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            switch (key)
            {
                case KeyboardKeys.ArrowUp:
                    StepBy(1, true);
                    return true;
                case KeyboardKeys.ArrowDown:
                    StepBy(-1, true);
                    return true;
                case KeyboardKeys.ArrowRight:
                    StepBy(1, true);
                    return true;
                case KeyboardKeys.ArrowLeft:
                    StepBy(-1, true);
                    return true;
            }

            return false;
        }
    }
}

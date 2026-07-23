using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Effects;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 参数滑块组组件 - 可折叠的三级参数结构（维度 > 滑块）
    /// 用于角色创建界面的捏脸参数调节
    /// </summary>
    public class ParameterSliderGroup : ContainerControl
    {
        // Events
        public event Action<string, float> OnParameterChanged; // (parameterName, value)

        // Style constants
        private static readonly Color DimensionHeaderColor = new Color(0.12f, 0.12f, 0.15f, 0.9f);
        private static readonly Color SliderBackgroundColor = new Color(0.08f, 0.08f, 0.10f, 0.8f);
        private static readonly Color SliderFillColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1.0f); // Gold
        private static readonly Color LabelColor = new Color(0.7f, 0.7f, 0.75f);
        private static readonly Color ValueColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1.0f);
        // 标准金色 RGB(212,175,55) - 维度标题、轨道、滑块、值标签统一使用
        private static readonly Color GoldColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1.0f);
        private static readonly float RowHeight = 28f;
        private static readonly float DimensionHeaderHeight = 32f;
        private static readonly float Padding = 8f;

        // Data
        private Dictionary<string, bool> _dimensionExpanded = new Dictionary<string, bool>();
        private Dictionary<string, Slider> _sliders = new Dictionary<string, Slider>();
        private Dictionary<string, Label> _valueLabels = new Dictionary<string, Label>();
        private float _currentY = 0;

        // 参数值插值缓动:每次 slider 值变化时启动一个 0.15s 的 tween,逐帧推送平滑值
        private Dictionary<string, FloatTween> _paramTweens = new Dictionary<string, FloatTween>();
        private Dictionary<string, float> _paramLastEmitted = new Dictionary<string, float>();
        private const float ParamTweenDuration = 0.15f;

        public ParameterSliderGroup()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
        }

        /// <summary>
        /// 添加一个参数维度（如"移动"、"缩放"、"角度"）
        /// </summary>
        public void AddDimension(string dimensionName, string[] parameterNames, float[] defaultValues = null)
        {
            _dimensionExpanded[dimensionName] = true;

            // 使用实际宽度或父控件宽度，避免构造时 Width=0 导致按钮不可见
            float headerWidth = Width > 0 ? Width : (Parent != null ? Parent.Width - 16f : 220f);

            // Dimension header button
            var headerBtn = new Button
            {
                Parent = this,
                Y = _currentY,
                Width = headerWidth,
                Height = DimensionHeaderHeight,
                Text = $"  \u25BC {dimensionName}",
                TextColor = GoldColor,
                BackgroundColor = DimensionHeaderColor,
                Font = UIHelper.SetFont(size: 12)
            };
            headerBtn.Clicked += () => ToggleDimension(dimensionName);
            _currentY += DimensionHeaderHeight;

            // Parameter sliders under this dimension
            for (int i = 0; i < parameterNames.Length; i++)
            {
                float defaultVal = defaultValues != null && i < defaultValues.Length ? defaultValues[i] : 0.5f;
                CreateParameterRow(parameterNames[i], defaultVal, dimensionName);
            }
        }

        private void CreateParameterRow(string paramName, float defaultValue, string dimensionName)
        {
            var row = new ContainerControl
            {
                Parent = this,
                Y = _currentY,
                Height = RowHeight,
                BackgroundColor = Color.Transparent
            };
            // Tag the row with its dimension for show/hide
            row.Tag = dimensionName;

            var nameLabel = new Label
            {
                Parent = row,
                X = Padding,
                Y = 0,
                Width = 60,
                Height = RowHeight,
                Text = paramName,
                TextColor = LabelColor,
                Font = UIHelper.SetFont(size: 11),
                VerticalAlignment = TextAlignment.Center
            };

            var slider = new Slider
            {
                Parent = row,
                X = 70,
                Y = 5,                       // 居中于 28px 行高：(28 - 18) / 2 = 5,thumb 18px 上下对齐
                Width = 120,
                Height = 18,                 // thumb 18px,与下方 BackgroundColor 视觉轨道 6px 配合
                Value = defaultValue,
                Minimum = 0f,
                Maximum = 1f,
                BackgroundColor = SliderFillColor  // 整个轨道为金色,符合金色一致性
            };
            // 若 FlaxEngine 版本暴露 ThumbSize 属性,使用编译期反射设置;否则保持默认 thumb 尺寸。
            var sliderType = slider.GetType();
            var thumbSizeProp = sliderType.GetProperty("ThumbSize");
            if (thumbSizeProp != null && thumbSizeProp.CanWrite)
            {
                try { thumbSizeProp.SetValue(slider, 18f); }
                catch { /* 忽略:thumb 大小不可设置时保持引擎默认 */ }
            }
            _sliders[paramName] = slider;

            var valueLabel = new Label
            {
                Parent = row,
                X = 200,
                Y = 0,
                Width = 40,
                Height = RowHeight,
                Text = defaultValue.ToString("F2"),
                TextColor = ValueColor,
                Font = UIHelper.SetFont(size: 11),
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Center
            };
            _valueLabels[paramName] = valueLabel;

            var capturedName = paramName;
            slider.ValueChanged += () =>
            {
                float newValue = slider.Value;
                valueLabel.Text = newValue.ToString("F2");
                StartParameterTween(capturedName, newValue);
            };

            _currentY += RowHeight;
        }

        /// <summary>
        /// 启动(或替换)某个参数的插值 tween
        /// </summary>
        private void StartParameterTween(string paramName, float newValue)
        {
            // From: 优先取进行中 tween 的当前插值,否则取上次发射值
            float fromValue;
            if (_paramTweens.TryGetValue(paramName, out var existing))
            {
                fromValue = existing.CurrentValue;
            }
            else if (_paramLastEmitted.TryGetValue(paramName, out var last))
            {
                fromValue = last;
            }
            else
            {
                fromValue = newValue;
            }

            _paramTweens[paramName] = new FloatTween
            {
                From = fromValue,
                To = newValue,
                Duration = ParamTweenDuration,
                Elapsed = 0f,
                Ease = global::Game.Effects.EaseType.EaseOutCubic
            };
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_paramTweens.Count == 0) return;

            // 临时记录要移除的已完成 tween,避免在遍历时修改字典
            List<string> completed = null;
            foreach (var kvp in _paramTweens)
            {
                var tween = kvp.Value;
                tween.Update(deltaTime);

                float currentValue = tween.CurrentValue;
                _paramLastEmitted[kvp.Key] = currentValue;
                OnParameterChanged?.Invoke(kvp.Key, currentValue);

                if (tween.IsCompleted)
                {
                    if (completed == null) completed = new List<string>();
                    completed.Add(kvp.Key);
                }
            }

            if (completed != null)
            {
                for (int i = 0; i < completed.Count; i++)
                {
                    _paramTweens.Remove(completed[i]);
                }
            }
        }

        private void ToggleDimension(string dimensionName)
        {
            _dimensionExpanded[dimensionName] = !_dimensionExpanded[dimensionName];
            bool expanded = _dimensionExpanded[dimensionName];

            // Show/hide rows tagged with this dimension
            foreach (var child in Children)
            {
                if (child is ContainerControl container && container.Tag as string == dimensionName)
                {
                    container.Visible = expanded;
                }
            }
        }

        /// <summary>
        /// 获取参数值
        /// </summary>
        public float GetParameterValue(string paramName)
        {
            if (_sliders.TryGetValue(paramName, out var slider))
                return slider.Value;
            return 0.5f;
        }

        /// <summary>
        /// 设置参数值
        /// </summary>
        public void SetParameterValue(string paramName, float value)
        {
            if (_sliders.TryGetValue(paramName, out var slider))
                slider.Value = value;
        }

        /// <summary>
        /// 清除所有参数
        /// </summary>
        public void ClearAll()
        {
            RemoveChildren();
            _sliders.Clear();
            _valueLabels.Clear();
            _dimensionExpanded.Clear();
            _paramTweens.Clear();
            _paramLastEmitted.Clear();
            _currentY = 0;
        }
    }
}

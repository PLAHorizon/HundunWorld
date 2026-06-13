using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 底部操作按钮栏 - 燕云十六声风格
    /// 深色半透明背景，金色边框按钮
    /// </summary>
    public class BottomActionBar : ContainerControl
    {
        public event Action<string> OnButtonClicked; // button name

        private static readonly Color BarBackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        private static readonly float BarHeight = 64f;
        private static readonly float ButtonWidth = 150f;
        private static readonly float ButtonHeight = 46f;
        private static readonly float ButtonSpacing = 24f;

        private List<Button> _buttons = new List<Button>();
        private string[] _buttonNames;
        private ButtonStyle[] _buttonStyles;

        public BottomActionBar()
        {
            Height = BarHeight;
            BackgroundColor = BarBackgroundColor;
            // Margin 参数顺序: (Left, Top, Right, Bottom)
            // 对于 HorizontalStretchBottom 锚点: anchorMin.Y = anchorMax.Y = 1
            // 高度 = -Top - Bottom，需要 Top = -BarHeight 才能让高度 = BarHeight
            Offsets = new Margin(0, -BarHeight, 0, 0);
        }

        /// <summary>
        /// 设置按钮列表
        /// </summary>
        public void SetButtons(string[] buttonNames, ButtonStyle[] styles = null)
        {
            RemoveChildren();
            _buttons.Clear();

            _buttonNames = buttonNames;
            _buttonStyles = styles;

            for (int i = 0; i < buttonNames.Length; i++)
            {
                var style = styles != null && i < styles.Length ? styles[i] : ButtonStyle.Default;
                var btn = CreateStyledButton(buttonNames[i], style);
                btn.Parent = this;
                btn.Y = (BarHeight - ButtonHeight) / 2f;
                btn.Width = ButtonWidth;
                btn.Height = ButtonHeight;

                string name = buttonNames[i];
                btn.Clicked += () => OnButtonClicked?.Invoke(name);
                _buttons.Add(btn);
            }

            LayoutButtons();
        }

        /// <summary>
        /// 当控件大小改变时重新布局按钮
        /// </summary>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutButtons();
        }

        /// <summary>
        /// 布局按钮 - 居中排列
        /// </summary>
        private void LayoutButtons()
        {
            if (_buttons.Count == 0) return;

            float totalWidth = _buttons.Count * ButtonWidth + (_buttons.Count - 1) * ButtonSpacing;
            float startX = (Width - totalWidth) / 2f;

            if (Width <= 0 || startX < 10)
            {
                startX = 10;
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].X = startX + i * (ButtonWidth + ButtonSpacing);
            }
        }

        // 金色外发光颜色 (RGB 212,175,55)
        private static readonly Color AccentGlowColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.3f);

        private Button CreateStyledButton(string text, ButtonStyle style)
        {
            Color bgColor, textColor, borderColor;

            switch (style)
            {
                case ButtonStyle.Accent:
                    bgColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.9f);
                    textColor = new Color(1.0f, 0.95f, 0.8f);
                    borderColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.8f);
                    break;
                case ButtonStyle.Ghost:
                    bgColor = new Color(0.1f, 0.1f, 0.12f, 0.6f);
                    textColor = new Color(0.7f, 0.7f, 0.75f);
                    borderColor = new Color(0.4f, 0.4f, 0.45f, 0.5f);
                    break;
                default:
                    bgColor = new Color(0.12f, 0.12f, 0.15f, 0.8f);
                    textColor = new Color(0.85f, 0.85f, 0.9f);
                    borderColor = new Color(0.5f, 0.5f, 0.55f, 0.5f);
                    break;
            }

            var btn = new HoverableButton
            {
                Text = text,
                BackgroundColor = bgColor,
                TextColor = textColor,
                BorderColor = borderColor,
                BorderThickness = 1f,
                Font = new FontReference { Size = 18 }
            };
            btn.SetOriginalColor(bgColor);
            if (style == ButtonStyle.Accent)
            {
                btn.SetAccentGlow();
            }
            return btn;
        }

        public enum ButtonStyle
        {
            Default,
            Accent,
            Ghost
        }

        /// <summary>
        /// 支持 hover 反馈的按钮控件
        /// 鼠标悬停时背景色变亮 20%，离开时恢复
        /// </summary>
        public class HoverableButton : Button
        {
            private Color _originalColor;
            private bool _wasHover;
            private RippleEffect _ripple;
            private Panel _glowPanel;

            public HoverableButton()
            {
                // 提前创建波纹效果子控件(覆盖整个按钮区域)
                _ripple = new RippleEffect
                {
                    Parent = this,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    BackgroundColor = Color.Transparent
                };
            }

            /// <summary>
            /// 设置按钮的原始背景色（必须在创建后立即调用）
            /// </summary>
            public void SetOriginalColor(Color color)
            {
                _originalColor = color;
                BackgroundColor = color;
            }

            /// <summary>
            /// 为 Accent 风格按钮添加金色外发光层（hover 时显示）
            /// </summary>
            public void SetAccentGlow()
            {
                if (_glowPanel != null) return;
                _glowPanel = new Panel
                {
                    Parent = this,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = new Margin(-4, -4, -4, -4),
                    BackgroundColor = AccentGlowColor,
                    Visible = false
                };
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _ripple != null)
                {
                    _ripple.StartRipple(location, Width * 1.5f);
                }
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                bool isHover = IsMouseOver;
                if (isHover != _wasHover)
                {
                    _wasHover = isHover;
                    if (isHover)
                    {
                        BackgroundColor = new Color(
                            _originalColor.R * 1.2f,
                            _originalColor.G * 1.2f,
                            _originalColor.B * 1.2f,
                            _originalColor.A
                        );
                        if (_glowPanel != null) _glowPanel.Visible = true;
                    }
                    else
                    {
                        BackgroundColor = _originalColor;
                        if (_glowPanel != null) _glowPanel.Visible = false;
                    }
                }
            }
        }
    }
}
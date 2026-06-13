using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 统一的"Space 下一步"按钮，用于角色创建多步流程
    /// </summary>
    public class NextStepButton : ContainerControl
    {
        private Panel _background;
        private Panel _glowPanel;
        private RippleEffect _ripple;
        private Label _spaceKeyLabel;
        private Label _nextLabel;
        private bool _isEnabled = true;
        private bool _uiInitialized = false;
        private bool _isHover = false;
        private bool _isPressed = false;

        // 背景色（按交互状态）— 暗金棕黄色调，匹配参考图
        private static readonly Color NormalColor = new Color(180f / 255f, 150f / 255f, 60f / 255f, 0.9f);
        private static readonly Color HoverColor = new Color(200f / 255f, 170f / 255f, 80f / 255f, 0.95f);
        private static readonly Color PressColor = new Color(150f / 255f, 125f / 255f, 40f / 255f, 1.0f);
        private static readonly Color DisabledColor = new Color(80f / 255f, 80f / 255f, 80f / 255f, 0.5f);
        // 金色外发光颜色 (暗金棕黄)
        private static readonly Color GlowColor = new Color(180f / 255f, 150f / 255f, 60f / 255f, 0.25f);

        /// <summary>
        /// 鼠标进入按钮时触发
        /// </summary>
        public event Action OnMouseEnterHandler;

        /// <summary>
        /// 鼠标离开按钮时触发
        /// </summary>
        public event Action OnMouseLeaveHandler;

        /// <summary>
        /// 按钮被按下时触发
        /// </summary>
        public event Action OnPressHandler;

        /// <summary>
        /// 按钮被释放时触发
        /// </summary>
        public event Action OnReleaseHandler;

        /// <summary>
        /// 点击事件（鼠标左键点击或按空格键触发）
        /// </summary>
        public event Action OnClicked;

        /// <summary>
        /// 鼠标是否悬停在按钮上
        /// </summary>
        public bool IsHover => _isHover;

        /// <summary>
        /// 按钮是否处于按下状态
        /// </summary>
        public bool IsPressed => _isPressed;

        public NextStepButton()
        {
            // ★ 关键修复：不在构造函数中设置 AnchorPreset
            // Flax 中构造函数设置 AnchorPreset 时父容器尺寸可能为 0，导致布局覆盖
            // 改用绝对定位：在 CreateUI 中根据 Parent 实际尺寸计算位置
            Size = new Float2(220, 54);
            BackgroundColor = Color.Transparent;
            Cursor = CursorType.Hand;

            // 延迟创建UI，等到父控件有正确尺寸后再创建
            _uiInitialized = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_uiInitialized && Parent != null && Parent.Width > 0 && Parent.Height > 0)
            {
                _uiInitialized = true;
                // ★ 绝对定位：根据父容器实际尺寸计算右下角位置
                Location = new Float2(Parent.Width - Size.X - 40, Parent.Height - Size.Y - 40);
                CreateUI();
                Debug.Log($"[NextStepButton] 已创建, Parent.Size=({Parent.Width:F0}x{Parent.Height:F0}), Location=({Location.X:F0},{Location.Y:F0}), Size=({Size.X:F0}x{Size.Y:F0})");
            }

            // 检测鼠标悬停状态变化（OnMouseEnter 在此 Flax 版本不可覆盖）
            bool isHover = IsMouseOver;
            if (isHover != _isHover)
            {
                _isHover = isHover;
                if (isHover)
                {
                    OnMouseEnterHandler?.Invoke();
                    if (_glowPanel != null) _glowPanel.Visible = true;
                    // Hover 时轻微上浮效果
                    if (_background != null)
                    {
                        _background.Y = -2f;
                    }
                }
                else
                {
                    _isPressed = false;
                    OnMouseLeaveHandler?.Invoke();
                    if (_glowPanel != null) _glowPanel.Visible = false;
                    // 恢复位置
                    if (_background != null)
                    {
                        _background.Y = 0f;
                    }
                }
            }

            // 根据交互状态调整背景色
            if (_background != null)
            {
                if (!_isEnabled)
                {
                    _background.BackgroundColor = DisabledColor;
                }
                else if (_isPressed)
                {
                    _background.BackgroundColor = PressColor;
                }
                else if (_isHover)
                {
                    _background.BackgroundColor = HoverColor;
                }
                else
                {
                    _background.BackgroundColor = NormalColor;
                }
            }

            // 根据按下状态调整按钮缩放，提供按压反馈
            Scale = _isPressed ? new Float2(0.95f, 0.95f) : new Float2(1.0f, 1.0f);
        }

        private void CreateUI()
        {
            // 底层金色外发光（hover 时显示）- 绝对定位避免锚点问题
            _glowPanel = new Panel
            {
                Parent = this,
                Location = new Float2(-4, -4),
                Size = new Float2(Size.X + 8, Size.Y + 8),
                BackgroundColor = GlowColor,
                Visible = false
            };

            // 按钮背景 - 使用标准 Panel
            _background = new Panel
            {
                Size = new Float2(220, 54),
                BackgroundColor = NormalColor
            };
            AddChild(_background);

            // Space 键标签（左侧）
            _spaceKeyLabel = new Label
            {
                Text = "Space",
                Size = new Float2(55, 28),
                Location = new Float2(12, 11),
                BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f),
                TextColor = Color.White,
                Font = UIHelper.SetFont(size: 12),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            _background.AddChild(_spaceKeyLabel);

            // 下一步标签（右侧）
            _nextLabel = new Label
            {
                Text = "下一步",
                Size = new Float2(90, 28),
                Location = new Float2(95, 11),
                TextColor = Color.Black,
                Font = UIHelper.SetFont(size: 16),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            _background.AddChild(_nextLabel);

            // 点击波纹效果 - 绝对定位避免锚点问题
            _ripple = new RippleEffect
            {
                Parent = this,
                Location = Float2.Zero,
                Size = Size,
                BackgroundColor = Color.Transparent
            };
        }

        /// <summary>
        /// 鼠标按下事件
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left && _isEnabled)
            {
                _isPressed = true;
                if (_ripple != null)
                {
                    _ripple.StartRipple(location, Width * 1.5f);
                }
                OnPressHandler?.Invoke();
                OnClicked?.Invoke();
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        /// <summary>
        /// 鼠标释放事件
        /// </summary>
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left && _isPressed)
            {
                _isPressed = false;
                OnReleaseHandler?.Invoke();
            }
            return base.OnMouseUp(location, button);
        }

        /// <summary>
        /// 检测键盘按键（由父控件在OnKeyDown中调用）
        /// </summary>
        public bool HandleKeyDown(KeyboardKeys key)
        {
            if (_isEnabled && Visible && key == KeyboardKeys.Spacebar)
            {
                OnClicked?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 显示按钮
        /// </summary>
        public void Show()
        {
            Visible = true;
        }

        /// <summary>
        /// 隐藏按钮
        /// </summary>
        public void Hide()
        {
            Visible = false;
        }

        /// <summary>
        /// 设置按钮启用/禁用状态
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;

            if (_spaceKeyLabel != null && _nextLabel != null)
            {
                float alpha = enabled ? 1.0f : 0.4f;
                _spaceKeyLabel.TextColor = new Color(Color.White.R, Color.White.G, Color.White.B, alpha);
                _nextLabel.TextColor = new Color(Color.Black.R, Color.Black.G, Color.Black.B, alpha);
            }
        }
    }
}

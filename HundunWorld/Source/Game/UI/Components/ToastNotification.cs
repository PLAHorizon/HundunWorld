using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Components
{
    public class ToastNotification : Panel
    {
        private Label _messageLabel;
        private Panel _iconPanel;
        private ToastType _type;
        private float _showTime;
        private float _duration;
        private bool _isShowing;
        
        public ToastNotification() : base()
        {
            SetupComponents();
            Visible = false;
        }
        
        private void SetupComponents()
        {
            Size = new Float2(350, 80);
            BackgroundColor = new Color(0.2f, 0.2f, 0.25f, 0.95f);
            
            // 图标面板
            _iconPanel = new Panel
            {
                Size = new Float2(40, 40),
                Location = new Float2(20, 20),
                BackgroundColor = Color.Transparent
            };
            AddChild(_iconPanel);
            
            // 消息标签
            _messageLabel = new Label
            {
                Location = new Float2(80, 20),
                Size = new Float2(250, 40),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            AddChild(_messageLabel);
        }
        
        public void Show(string message, ToastType type = ToastType.Info, float duration = 3f)
        {
            _type = type;
            _duration = duration;
            _showTime = 0f;
            _isShowing = true;
            
            _messageLabel.Text = message;
            SetupIcon(type);
            SetupColors(type);
            
            Visible = true;
            
            // 播放显示动画
            PlayShowAnimation();
        }
        
        private void SetupIcon(ToastType type)
        {
            // 清除现有图标
            _iconPanel.Children.Clear();
            
            Color iconColor;
            string iconText;
            
            switch (type)
            {
                case ToastType.Success:
                    iconColor = Color.Green;
                    iconText = "✓";
                    break;
                case ToastType.Warning:
                    iconColor = Color.Orange;
                    iconText = "⚠";
                    break;
                case ToastType.Error:
                    iconColor = Color.Red;
                    iconText = "✗";
                    break;
                default:
                    iconColor = Color.Blue;
                    iconText = "ℹ";
                    break;
            }
            
            var iconLabel = new Label
            {
                Text = iconText,
                Size = new Float2(40, 40),
                TextColor = iconColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            _iconPanel.AddChild(iconLabel);
        }
        
        private void SetupColors(ToastType type)
        {
            Color borderColor;
            
            switch (type)
            {
                case ToastType.Success:
                    borderColor = Color.Green;
                    break;
                case ToastType.Warning:
                    borderColor = Color.Orange;
                    break;
                case ToastType.Error:
                    borderColor = Color.Red;
                    break;
                default:
                    borderColor = Color.Blue;
                    break;
            }
            
            // 如果支持边框，在这里设置
            // BorderColor = borderColor;
        }
        
        private void PlayShowAnimation()
        {
            // 简单的淡入动画
            Color originalColor = BackgroundColor;
            BackgroundColor = originalColor.AlphaMultiplied(0f);
            
            // 这里可以添加更复杂的动画逻辑
            BackgroundColor = originalColor;
        }
        
        private void PlayHideAnimation()
        {
            // 简单的淡出动画
            Color originalColor = BackgroundColor;
            BackgroundColor = originalColor.AlphaMultiplied(0f);
            
            Visible = false;
        }
        
        public override void Update(float de)
        {
            base.Update(de);
            
            if (_isShowing)
            {
                _showTime += Time.DeltaTime;
                
                if (_showTime >= _duration)
                {
                    _isShowing = false;
                    PlayHideAnimation();
                }
            }
        }
        
        public void Hide()
        {
            _isShowing = false;
            PlayHideAnimation();
        }
    }
    
    /// <summary>
    /// Toast管理器
    /// 管理多个Toast消息的显示
    /// </summary>
    public class ToastManager : ContainerControl
    {
        private const int MAX_TOASTS = 5;
        private const float TOAST_SPACING = 90f;
        
        public ToastManager() : base()
        {
            AnchorPreset = AnchorPresets.TopRight;
            Size = new Float2(370, 500);
            BackgroundColor = Color.Transparent;
        }
        
        public void ShowToast(string message, ToastType type = ToastType.Info, float duration = 3f)
        {
            // 移除过多的Toast
            while (Children.Count >= MAX_TOASTS)
            {
                var oldestToast = Children[0];
                RemoveChild(oldestToast);
                oldestToast.Dispose();
            }
            
            // 创建新的Toast
            var toast = new ToastNotification();
            toast.Location = new Float2(10, Children.Count * TOAST_SPACING + 10);
            AddChild(toast);
            
            toast.Show(message, type, duration);
            
            // 重新排列现有Toast
            ReorganizeToasts();
        }
        
        private void ReorganizeToasts()
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var toast = Children[i];
                toast.Location = new Float2(10, i * TOAST_SPACING + 10);
            }
        }
        
        public void ShowInfo(string message, float duration = 3f)
        {
            ShowToast(message, ToastType.Info, duration);
        }
        
        public void ShowSuccess(string message, float duration = 3f)
        {
            ShowToast(message, ToastType.Success, duration);
        }
        
        public void ShowWarning(string message, float duration = 3f)
        {
            ShowToast(message, ToastType.Warning, duration);
        }
        
        public void ShowError(string message, float duration = 3f)
        {
            ShowToast(message, ToastType.Error, duration);
        }
    }
}
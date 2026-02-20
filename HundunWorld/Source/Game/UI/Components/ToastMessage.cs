using System;
using FlaxEngine;
using FlaxEngine.GUI;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// Toast消息类
    /// 表示单个Toast消息的UI元素
    /// </summary>
    public class ToastMessage
    {
        private static ToastManager _sharedManager;
        private static UICanvas _toastCanvas;
        private static readonly object _lock = new object();
        
        private const float DEFAULT_WIDTH = 220f;
        private const float DEFAULT_HEIGHT = 45f;
        
        public Panel Panel { get; }
        public string Message { get; }
        public Color Color { get; }
        public string Icon { get; }
        public float Duration { get; }

        public ToastMessage(string message, Color color, string icon, float duration)
        {
            Message = message;
            Color = color;
            Icon = icon;
            Duration = duration;

            Panel = new Panel
            {
                Size = new Float2(DEFAULT_WIDTH, DEFAULT_HEIGHT),
                BackgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.95f),
                ScrollbarThumbSelectedColor = color,
                ScrollMargin = new Margin(2)
            };

            // 图标标签
            var iconLabel = new Label
            {
                Text = icon,
                Location = new Float2(8, 5),
                Size = new Float2(24, DEFAULT_HEIGHT - 10),
                TextColor = color,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 12)
            };
            Panel.AddChild(iconLabel);

            // 消息标签
            float messageWidth = DEFAULT_WIDTH - 40;
            var messageLabel = new Label
            {
                Text = message,
                Location = new Float2(35, 3),
                Size = new Float2(messageWidth, DEFAULT_HEIGHT - 6),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Wrapping = TextWrapping.WrapWords,
                Font = UIHelper.SetFont(size: 10)
            };
            Panel.AddChild(messageLabel);
        }
        
        private static ToastManager GetSharedManager()
        {
            if (_sharedManager == null)
            {
                lock (_lock)
                {
                    if (_sharedManager == null)
                    {
                        // 创建专用的 UICanvas
                        _toastCanvas = new UICanvas
                        {
                            Name = "ToastCanvas"
                        };
                        
                        const float TOAST_WIDTH = 240f;
                        const float MARGIN = 10f;
                        _sharedManager = new ToastManager
                        {
                            AnchorPreset = AnchorPresets.TopRight,
                            Size = new Float2(TOAST_WIDTH + MARGIN * 2, 400),
                            Location = new Float2(MARGIN, MARGIN)
                        };
                        
                        _toastCanvas.GUI.AddChild(_sharedManager);
                        
                        FlaxEngine.Debug.Log("[ToastMessage] ToastManager 已创建并添加到 Canvas");
                    }
                }
            }
            return _sharedManager;
        }
        
        /// <summary>
        /// 显示信息提示
        /// </summary>
        public static void ShowInfo(string message, float duration = 3f)
        {
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                GetSharedManager().ShowToast(message, ToastType.Info, duration);
            });
        }
        
        /// <summary>
        /// 显示成功提示
        /// </summary>
        public static void ShowSuccess(string message, float duration = 3f)
        {
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                GetSharedManager().ShowToast(message, ToastType.Success, duration);
            });
        }
        
        /// <summary>
        /// 显示警告提示
        /// </summary>
        public static void ShowWarning(string message, float duration = 3f)
        {
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                GetSharedManager().ShowToast(message, ToastType.Warning, duration);
            });
        }
        
        /// <summary>
        /// 显示错误提示
        /// </summary>
        public static void ShowError(string message, float duration = 4f)
        {
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                GetSharedManager().ShowToast(message, ToastType.Error, duration);
            });
        }
    }
}
using FlaxEngine;
using FlaxEngine.GUI;
using System;
using System.Collections.Generic;

namespace Game.UI
{
    /// <summary>
    /// 全局HUD通知系统
    /// 提供统一的消息显示接口，支持队列化通知和淡入淡出效果
    /// </summary>
    public static class HUD
    {
        private static readonly Queue<NotificationEntry> _pendingQueue = new Queue<NotificationEntry>();
        private static readonly List<NotificationEntry> _activeNotifications = new List<NotificationEntry>();
        private static ContainerControl _notificationContainer;
        private static bool _initialized;
        private const int MaxVisibleNotifications = 5;
        private const float FadeInDuration = 0.3f;
        private const float FadeOutDuration = 0.5f;
        private const float NotificationHeight = 36f;
        private const float NotificationSpacing = 4f;

        /// <summary>
        /// 初始化通知系统的UI容器
        /// </summary>
        /// <param name="parentContainer">父容器（通常是RootControl）</param>
        public static void Initialize(ContainerControl parentContainer)
        {
            if (_initialized || parentContainer == null)
                return;

            _notificationContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Offsets = new Margin(-200, 400, 10, 250),
            };
            parentContainer.AddChild(_notificationContainer);
            _initialized = true;
            FlaxEngine.Debug.Log("[HUD] 通知系统已初始化");
        }

        /// <summary>
        /// 显示通知消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <param name="duration">显示时长（秒）</param>
        /// <param name="color">消息颜色</param>
        public static void ShowNotification(string message, float duration = 3.0f, Color? color = null)
        {
            try
            {
                FlaxEngine.Debug.Log($"[HUD] {message}");

                var entry = new NotificationEntry
                {
                    Message = message,
                    Duration = duration,
                    DisplayColor = color ?? Color.White,
                    CreatedTime = Time.GameTime,
                    State = NotificationState.Pending
                };

                if (_initialized && _activeNotifications.Count < MaxVisibleNotifications)
                {
                    ShowNotificationImmediate(entry);
                }
                else if (!_initialized)
                {
                    FlaxEngine.Debug.LogWarning("[HUD] 通知系统尚未初始化，消息已加入队列等待显示");
                    _pendingQueue.Enqueue(entry);
                }
                else
                {
                    _pendingQueue.Enqueue(entry);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"HUD显示通知失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新通知系统（应在游戏主循环中调用）
        /// </summary>
        public static void Update()
        {
            if (!_initialized)
                return;

            try
            {
                var currentTime = Time.GameTime;

                for (int i = _activeNotifications.Count - 1; i >= 0; i--)
                {
                    var entry = _activeNotifications[i];
                    var elapsed = currentTime - entry.ShownTime;

                    switch (entry.State)
                    {
                        case NotificationState.FadingIn:
                            var fadeInProgress = Mathf.Clamp(elapsed / FadeInDuration, 0f, 1f);
                            if (entry.Panel != null)
                                entry.Panel.Opacity = fadeInProgress;
                            if (fadeInProgress >= 1f)
                                entry.State = NotificationState.Visible;
                            break;

                        case NotificationState.Visible:
                            if (elapsed >= entry.Duration)
                            {
                                entry.State = NotificationState.FadingOut;
                                entry.FadeOutStartTime = currentTime;
                            }
                            break;

                        case NotificationState.FadingOut:
                            var fadeOutElapsed = currentTime - entry.FadeOutStartTime;
                            var fadeOutProgress = Mathf.Clamp(fadeOutElapsed / FadeOutDuration, 0f, 1f);
                            if (entry.Panel != null)
                                entry.Panel.Opacity = 1f - fadeOutProgress;
                            if (fadeOutProgress >= 1f)
                            {
                                RemoveNotificationAt(i);
                            }
                            break;
                    }
                }

                // 显示队列中等待的通知
                while (_pendingQueue.Count > 0 && _activeNotifications.Count < MaxVisibleNotifications)
                {
                    var next = _pendingQueue.Dequeue();
                    ShowNotificationImmediate(next);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"HUD更新失败: {ex.Message}");
            }
        }

        private static void ShowNotificationImmediate(NotificationEntry entry)
        {
            if (_notificationContainer == null)
                return;

            var yOffset = _activeNotifications.Count * (NotificationHeight + NotificationSpacing);

            var panel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(0, 400, yOffset, NotificationHeight),
                BackgroundColor = new Color(0, 0, 0, 0.75f),
                Opacity = 0f
            };

            var label = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(8, -8, 2, -2),
                Text = entry.Message,
                TextColor = entry.DisplayColor,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };

            panel.AddChild(label);
            _notificationContainer.AddChild(panel);

            entry.Panel = panel;
            entry.Label = label;
            entry.ShownTime = Time.GameTime;
            entry.State = NotificationState.FadingIn;

            _activeNotifications.Add(entry);
        }

        private static void RemoveNotificationAt(int index)
        {
            var entry = _activeNotifications[index];
            if (entry.Panel != null && _notificationContainer != null)
            {
                _notificationContainer.RemoveChild(entry.Panel);
                entry.Panel.Dispose();
            }
            _activeNotifications.RemoveAt(index);

            // 重新排列剩余通知位置
            RearrangeNotifications();
        }

        private static void RemoveNotification(NotificationEntry entry)
        {
            if (entry.Panel != null && _notificationContainer != null)
            {
                _notificationContainer.RemoveChild(entry.Panel);
                entry.Panel.Dispose();
            }
            _activeNotifications.Remove(entry);

            RearrangeNotifications();
        }

        private static void RearrangeNotifications()
        {
            for (int i = 0; i < _activeNotifications.Count; i++)
            {
                var notification = _activeNotifications[i];
                if (notification.Panel != null)
                {
                    var yOffset = i * (NotificationHeight + NotificationSpacing);
                    notification.Panel.Offsets = new Margin(0, 400, yOffset, NotificationHeight);
                }
            }
        }

        /// <summary>
        /// 清除所有通知
        /// </summary>
        public static void ClearAll()
        {
            foreach (var entry in _activeNotifications)
            {
                if (entry.Panel != null && _notificationContainer != null)
                {
                    _notificationContainer.RemoveChild(entry.Panel);
                    entry.Panel.Dispose();
                }
            }
            _activeNotifications.Clear();
            _pendingQueue.Clear();
        }

        /// <summary>
        /// 显示成功消息
        /// </summary>
        public static void ShowSuccess(string message, float duration = 3.0f)
        {
            ShowNotification(message, duration, Color.Green);
        }

        /// <summary>
        /// 显示警告消息
        /// </summary>
        public static void ShowWarning(string message, float duration = 3.0f)
        {
            ShowNotification(message, duration, Color.Yellow);
        }

        /// <summary>
        /// 显示错误消息
        /// </summary>
        public static void ShowError(string message, float duration = 3.0f)
        {
            ShowNotification(message, duration, Color.Red);
        }

        /// <summary>
        /// 显示信息消息
        /// </summary>
        public static void ShowInfo(string message, float duration = 3.0f)
        {
            ShowNotification(message, duration, Color.Blue);
        }

        /// <summary>
        /// 通知状态
        /// </summary>
        private enum NotificationState
        {
            Pending,
            FadingIn,
            Visible,
            FadingOut
        }

        /// <summary>
        /// 通知条目
        /// </summary>
        private class NotificationEntry
        {
            public string Message { get; set; }
            public float Duration { get; set; }
            public Color DisplayColor { get; set; }
            public float CreatedTime { get; set; }
            public float ShownTime { get; set; }
            public float FadeOutStartTime { get; set; }
            public NotificationState State { get; set; }
            public Panel Panel { get; set; }
            public Label Label { get; set; }
        }
    }
}
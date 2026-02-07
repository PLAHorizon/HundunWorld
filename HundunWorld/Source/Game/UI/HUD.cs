using FlaxEngine;
using System;

namespace Game.UI
{
    /// <summary>
    /// 全局HUD通知系统
    /// 提供统一的消息显示接口
    /// </summary>
    public static class HUD
    {
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
                // 记录到调试日志
                FlaxEngine.Debug.Log($"[HUD] {message}");
                
                // TODO: 实现真正的HUD通知系统
                // 可以集成到ToastNotification或其他UI系统中
                
                // 临时解决方案：使用调试输出
                if (color.HasValue)
                {
                    FlaxEngine.Debug.Log($"[HUD {color.Value}] {message}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"HUD显示通知失败: {ex.Message}");
            }
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
    }
}
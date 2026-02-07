using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// UI事件类型枚举
    /// </summary>
    public enum UIEventType
    {
        /// <summary>
        /// 按钮点击
        /// </summary>
        ButtonClick,
        
        /// <summary>
        /// 按钮悬停
        /// </summary>
        ButtonHover,
        
        /// <summary>
        /// 面板打开
        /// </summary>
        PanelOpen,
        
        /// <summary>
        /// 面板关闭
        /// </summary>
        PanelClose,
        
        /// <summary>
        /// 通知
        /// </summary>
        Notification,
        
        /// <summary>
        /// 错误
        /// </summary>
        Error,
        
        /// <summary>
        /// 成功
        /// </summary>
        Success,
        
        /// <summary>
        /// 物品选择
        /// </summary>
        ItemSelect,
        
        /// <summary>
        /// 物品拖拽
        /// </summary>
        ItemDrag,
        
        /// <summary>
        /// 窗口打开
        /// </summary>
        WindowOpen,
        
        /// <summary>
        /// 窗口关闭
        /// </summary>
        WindowClose
    }
}
using System;

namespace Horizon.Game.Message.Enums
{
   

    /// <summary>
    /// 动画状态枚举
    /// </summary>
    public enum AnimationState
    {
        /// <summary>
        /// 空闲
        /// </summary>
        Idle,
        
        /// <summary>
        /// 播放中
        /// </summary>
        Playing,
        
        /// <summary>
        /// 暂停
        /// </summary>
        Paused,
        
        /// <summary>
        /// 完成
        /// </summary>
        Completed,
        
        /// <summary>
        /// 停止
        /// </summary>
        Stopped
    }

    /// <summary>
    /// 动画方向枚举
    /// </summary>
    public enum AnimationDirection
    {
        /// <summary>
        /// 正向
        /// </summary>
        Forward,
        
        /// <summary>
        /// 反向
        /// </summary>
        Reverse,
        
        /// <summary>
        /// 交替
        /// </summary>
        Alternate,
        
        /// <summary>
        /// 交替反向
        /// </summary>
        AlternateReverse
    }
}
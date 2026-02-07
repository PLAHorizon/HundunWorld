using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 错误类型枚举
    /// </summary>
    public enum ErrorType
    {
        /// <summary>
        /// 网络错误
        /// </summary>
        Network,

        /// <summary>
        /// 认证错误
        /// </summary>
        Authentication,

        /// <summary>
        /// 授权错误
        /// </summary>
        Authorization,

        /// <summary>
        /// 数据错误
        /// </summary>
        Data,

        /// <summary>
        /// 逻辑错误
        /// </summary>
        Logic,

        /// <summary>
        /// 系统错误
        /// </summary>
        System,
        /// <summary>
        /// 
        /// </summary>
        Server,
        /// <summary>
        /// 
        /// </summary>
        Validation,
        /// <summary>
        /// 
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 错误严重级别枚举
    /// </summary>
    public enum ErrorSeverity
    {
        /// <summary>
        /// 信息级别
        /// </summary>
        Info,

        /// <summary>
        /// 警告级别
        /// </summary>
        Warning,

        /// <summary>
        /// 错误级别
        /// </summary>
        Error,

        /// <summary>
        /// 严重级别
        /// </summary>
        Critical
    }
}
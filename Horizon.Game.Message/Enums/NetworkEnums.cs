using MemoryPack;
using System;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 重连原因枚举
    /// </summary>
    
    public enum ReconnectReason
    {
        /// <summary>
        /// 网络错误
        /// </summary>
        NetworkError,
        
        /// <summary>
        /// 服务器不可用
        /// </summary>
        ServerUnavailable,
        
        /// <summary>
        /// 连接超时
        /// </summary>
        ConnectionTimeout,
        
        /// <summary>
        /// 心跳超时
        /// </summary>
        HeartbeatTimeout,
        
        /// <summary>
        /// 手动重连
        /// </summary>
        Manual,
        
        /// <summary>
        /// 网关切换
        /// </summary>
        GatewaySwitching,
        NetworkTimeout,
        AuthenticationFailed,
        ProtocolError,
        UnexpectedDisconnection,
        NetworkChanged,


    }
   
    /// <summary>
    /// 重连策略枚举
    /// </summary>
    
    public enum ReconnectStrategy
    {
        /// <summary>
        /// 固定间隔
        /// </summary>
        FixedInterval,
        
        /// <summary>
        /// 指数退避
        /// </summary>
        ExponentialBackoff,
        
        /// <summary>
        /// 线性增长
        /// </summary>
        LinearBackoff,
        
        /// <summary>
        /// 自适应
        /// </summary>
        Adaptive,
        
        /// <summary>
        /// 标准策略
        /// </summary>
        Standard,
        
        /// <summary>
        /// 快速重连
        /// </summary>
        Quick,
        
        /// <summary>
        /// 保守策略
        /// </summary>
        Conservative,
        
        /// <summary>
        /// 最小尝试
        /// </summary>
        Minimal,
        
        /// <summary>
        /// 激进策略
        /// </summary>
        Aggressive,
        
        /// <summary>
        /// 网络自适应
        /// </summary>
        NetworkAdaptive
    }
    
    /// <summary>
    /// 网络质量等级
    /// </summary>
    
    public enum NetworkQuality
    {
        /// <summary>
        /// 优秀
        /// </summary>
        Excellent,
        
        /// <summary>
        /// 良好
        /// </summary>
        Good,
        
        /// <summary>
        /// 一般
        /// </summary>
        Fair,
        
        /// <summary>
        /// 较差
        /// </summary>
        Poor,
        
        /// <summary>
        /// 很差
        /// </summary>
        VeryPoor,
        
        /// <summary>
        /// 未知
        /// </summary>
        Unknown
    }
    
    /// <summary>
    /// 连接状态枚举
    /// </summary>
    
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        GatewaySwitching,// 网关切换中
        Error,           // 错误状态
        Failed,
        Unknown
    }
    
    /// <summary>
    /// 压缩级别枚举
    /// </summary>
    
    public enum CompressionLevel
    {
        None,
        Fast,
        Optimal,
        Maximum
    }

    public enum NetworkStatus
    {
        Connected,
        Limited,
        Disconnected,
        Unknown,
    }
}
using MemoryPack;
using Orleans;
using Orleans.CodeGeneration;
using System;
using System.ComponentModel;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 验证码用途枚举
    /// </summary>
    public enum VerificationPurpose
    {
        /// <summary>
        /// 注册
        /// </summary>
        [Description("注册")]
        Register = 0,

        /// <summary>
        /// 登录
        /// </summary>
        [Description("登录")]
        Login = 1,

        /// <summary>
        /// 找回密码
        /// </summary>
        [Description("找回密码")]
        ForgotPassword = 2,

        /// <summary>
        /// 修改绑定手机
        /// </summary>
        [Description("修改绑定手机")]
        ChangePhone = 3,

        /// <summary>
        /// 修改绑定邮箱
        /// </summary>
        [Description("修改绑定邮箱")]
        ChangeEmail = 4,

        /// <summary>
        /// 实名认证
        /// </summary>
        [Description("实名认证")]
        RealNameAuth = 5
    }

    /// <summary>
    /// 验证码请求消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class VerificationCodeRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 邮箱
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Email { get; set; } = "";

        /// <summary>
        /// 手机号
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string PhoneNumber { get; set; } = "";

        /// <summary>
        /// 验证码用途
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public VerificationPurpose Purpose { get; set; } = VerificationPurpose.Register;

        /// <summary>
        /// 客户端IP地址
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string ClientIP { get; set; } = "";

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string ClientVersion { get; set; } = "";

        [MemoryPackOrder(5)]
        [Id(5)]
        [MemoryPackAllowSerialize]
        public MessageType Type { get; set; } = MessageType.VerificationCodeRequest;
        
        [MemoryPackOrder(6)]
        [Id(6)]
        [MemoryPackAllowSerialize]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }

    /// <summary>
    /// 验证码响应消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class VerificationCodeResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 验证码ID（用于后续验证）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string VerificationId { get; set; } = "";

        /// <summary>
        /// 过期时间（Unix时间戳）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long ExpireTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        [MemoryPackAllowSerialize]
        public MessageType Type { get; set; } = MessageType.VerificationCodeResponse;
        
        [MemoryPackOrder(5)]
        [Id(5)]
        [MemoryPackAllowSerialize]
        public ServiceType ServiceType { get; set; } = ServiceType.Account;
    }
}
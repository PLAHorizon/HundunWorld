using Orleans;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 实名认证状态
    /// </summary>
    [Flags]
    [Serializable]
    [GenerateSerializer]
    public enum RealNameAuthStatus
    {
        /// <summary>
        /// 空认证
        /// </summary>
        [Description("空认证"), Id(0)]
        Default = 0,
        /// <summary>
        /// 手机号认证
        /// </summary>
        [Description("手机号认证"), Id(1)]
        Phone = 1,
        /// <summary>
        /// 邮箱认证
        /// </summary>
        [Description("邮箱认证"), Id(2)]
        Email = Phone << 1,
        /// <summary>
        /// 双重认证
        /// </summary>
        [Description("双重认证"), Id(3)]
        Normal = Phone << 2,
        /// <summary>
        /// 人脸认证
        /// </summary>
        [Description("人脸认证"), Id(4)]
        FaceId = Phone << 3,
        /// <summary>
        /// 身份证认证
        /// </summary>
        [Description("身份证认证"), Id(5)]
        IDCard = Phone << 4,
        /// <summary>
        /// 全证认证
        /// </summary>
        [Description("全认证"), Id(6)]
        All = Phone << 5,

    }
}

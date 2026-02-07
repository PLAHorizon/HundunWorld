using Horizon.Core.Abstract;
using Orleans;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 修改用户信息
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ChangeUserInfo
    {
        [Id(0)] public string PassportId { get; set; }
        [Id(1)] public long AppId { get; set; }
        [Id(2)] public AppType AppType { get; set; }
        /// <summary>
        /// 修改类型
        /// </summary>
        [Id(3)] public UserInfoType Type { get; set; }
        /// <summary>
        /// 变更值 
        /// </summary>
        [Id(4)] public string Value { get; set; }
    }
    /// <summary>
    /// 用户信息数据类型
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public enum UserInfoType
    {
        /// <summary>
        /// 身份证号
        /// </summary>
        [Description("身份证号"), Id(0)]

        IdCard = 0,
        /// <summary>
        /// 手机号
        /// </summary>
        [Description("手机号"), Id(1)]
        Phone = 1,
        /// <summary>
        /// 邮箱
        /// </summary>
        [Description("邮箱"), Id(2)]
        Email = 2,
        /// <summary>
        /// 头像
        /// </summary>  
        [Description("头像"), Id(3)]
        Avatar = 3,

    }
}

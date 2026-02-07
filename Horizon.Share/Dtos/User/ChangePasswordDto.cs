using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Orleans;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 修改密码
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ChangePasswordDto
    {
        [Id(0)] public string PassportId { get; set; }
        [Id(1)] public string OldPassword { get; set; }
        [Id(2)] public string NewPassword { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        [Id(3)] public long AppId { get; set; }
        /// <summary>
        /// 应用类型
        /// </summary>
        [Id(4)] public AppType AppType { get; set; }
        /// <summary>
        /// 通行证类型
        /// </summary>
        [Id(5)] public PassportType PassportType { get; set; }
    }
}

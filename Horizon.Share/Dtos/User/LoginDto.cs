using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Orleans;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 登录数据模型类
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class LoginDto
    {
        /// <summary>
        /// 通行证号
        /// </summary>
        [Id(0)] public string? PassportId { get; set; }
        /// <summary>
        /// 密码
        /// </summary>
        [Id(1)] public string? Password { get; set; }
        /// <summary>
        /// 验证码
        /// </summary>
        [Id(2)] public string? VerifyCode { get; set; }
        /// <summary>
        /// 手机号
        /// </summary>
        [Id(3)] public string? Phone { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [Id(4)]
        public string? Email { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        [Id(5)] public long AppId { get; set; }
        /// <summary>
        /// 应用类型
        /// </summary>
        [Id(6)] public AppType AppType { get; set; }
        /// <summary>
        /// 通行证类型
        /// </summary>
        [Id(7)] public PassportType PassportType { get; set; }

        /// <summary>
        /// 游戏上下文信息
        /// </summary>
        [Id(8)] public GameLoginContextDto? GameContext { get; set; }

        /// <summary>
        /// 客户端机器唯一标识符（由客户端通过 MachineIdentifier.GetMachineGuid() 获取后上传，用于令牌绑定）
        /// </summary>
        [Id(9)] public string? MachineId { get; set; }
    }

    /// <summary>
    /// 登录时的游戏上下文信息
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameLoginContextDto
    {
        /// <summary>
        /// 客户端IP
        /// </summary>
        [Id(0)] public string? Ip { get; set; }
        /// <summary>
        /// 平台Id
        /// </summary>
        [Id(1)] public string? PlatformId { get; set; }
        /// <summary>
        /// 设备Id
        /// </summary>
        [Id(2)] public string? DeviceId { get; set; }
    }

}

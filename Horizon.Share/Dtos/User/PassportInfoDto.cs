using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Orleans;
using Orleans.CodeGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.User
{
    [Serializable]
    [GenerateSerializer]
    public class PassportInfoDto
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Id(0)]
        public string PassportId { get; set; }
        /// <summary>
        /// 姓名
        /// </summary>
        [Id(1)] public string Name { get; set; }
        /// <summary>
        /// 头像
        /// </summary>
        [Id(2)] public string Avatar { get; set; }
        /// <summary>
        /// 手机号
        /// </summary>
        [Id(3)] public string Phone { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [Id(4)] public string Email { get; set; }
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
        /// 机构Id
        /// </summary>
        [Id(8)] public long OrganizationId { get; set; }

        /// <summary>
        /// 用户Id
        /// </summary>
        [Id(9)] public long UserId { get; set; }
        
        /// <summary>
        /// 会话令牌
        /// </summary>
        [Id(10)] public string SessionToken { get; set; }
        [Id(11)]
        public string UserName { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class GameUserInfoDto
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Id(0)]
        public string PassportId { get; set; }
        /// <summary>
        /// 姓名
        /// </summary>
        [Id(1)] public string Name { get; set; }
        /// <summary>
        /// 头像
        /// </summary>
        [Id(2)] public string Avatar { get; set; }
        /// <summary>
        /// 手机号
        /// </summary>
        [Id(3)] public string Phone { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [Id(4)] public string Email { get; set; }

        /// <summary>
        /// 游戏内用户Id
        /// </summary>
        [Id(5)] public long GameUserId { get; set; }
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(6)] public int GameId { get; set; }
    }
}

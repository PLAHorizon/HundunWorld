using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Orleans;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 注册通行证Dto
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class RegisterDto
    {
        /// <summary>
        /// 密码
        /// </summary>
        [Id(0)] public string Password { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        [Id(1)] public string Phone { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [Id(2)] public string Email { get; set; }
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
        /// <summary>
        /// 昵称
        /// </summary>
        [Id(6)] public string NickName { get; set; }
        [Id(7)]
        public GameRegisterDto GameContext { get; set; }

        [Id(8)]
        public string RealName { get; set; }

        [Id(9)]
        public string ID { get; set; }

    }

    /// <summary>
    /// 在游戏内注册通信证
    /// 同时提供游戏内用户
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameRegisterDto 
    {
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(0)]
        public int GameId { get; set; }
        [Id(1)]
        public int ServerId { get; set; }
       [Id(2)]
       public int AreaId { get; set; }
       [Id(3)] public string Ip { get; set; }
      [Id(4)]  public string PlatformId { get; set; }
    }
}

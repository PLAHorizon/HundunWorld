using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Share.Commones;
using Orleans;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    [Serializable]
    [GenerateSerializer]
    public class OpenUserDto
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Id(0)]
        public string PassportId { get; set; }


        /// <summary>
        /// 昵称
        /// </summary>
        [Id(1)]
        public string NickName { get; set; }
        /// <summary>
        /// 头像
        /// </summary>
        [Id(2)]
        public string Avatar { get; set; }
        /// <summary>
        /// 手机号
        /// </summary>
        [Id(3)]
        public string Phone { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        [Id(4)]
        public string Email { get; set; }
        /// <summary>
        /// 实名状态
        /// </summary>
        [Id(5)] public RealNameAuthStatus RealNameAuthStatus { get; set; }

    }
}

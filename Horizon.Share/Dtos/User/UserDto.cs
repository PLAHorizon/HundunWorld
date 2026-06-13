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
    public class UserDto
    {
        /// <summary>
        /// 用户Id
        /// </summary>
        [Id(12)]
        public Guid UserId { get; set; }

        /// <summary>
        /// 通行证
        /// </summary>
        [Id(0)]
        public string PassportId { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        [Id(1)] public long AppId { get; set; }
        /// <summary>
        /// 应用类型
        /// </summary>
        [Id(2)] public AppType AppType { get; set; }        /// <summary>
                                                            /// 通行证类型
                                                            /// </summary>
        [Id(4)] public PassportType PassportType { get; set; }
        /// <summary>
        /// 机构Id
        /// </summary>
        [Id(3)] public long? OrganizationId { get; set; }        /// <summary>
                                                                 /// 组织机构名
                                                                 /// </summary>
        [Id(5)] public string Organization { get; set; }        /// <summary>
                                                                /// 姓名
                                                                /// </summary>
        [Id(6)]
        public string Name { get; set; }        /// <summary>
                                                /// 昵称
                                                /// </summary>
        [Id(7)]
        public string NickName { get; set; }        /// <summary>
                                                    /// 头像
                                                    /// </summary>
        [Id(8)]
        public string Avatar { get; set; }        /// <summary>
                                                  /// 手机号
                                                  /// </summary>
        [Id(9)]
        public string Phone { get; set; }        /// <summary>
                                                 /// 邮箱
                                                 /// </summary>
        [Id(10)]
        public string Email { get; set; }        /// <summary>
                                                 /// 实名状态
                                                 /// </summary>
        [Id(11)] public RealNameAuthStatus RealNameAuthStatus { get; set; }

    }


    /// <summary>
    /// 查询用户信息 Dto
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class UserQueryDto : PageQuery
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Id(3)] public string PassportId { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        [Id(4)] public long AppId { get; set; }
        /// <summary>
        /// 应用类型
        /// </summary>
        [Id(5)] public AppType AppType { get; set; }
        /// <summary>
        /// 通行证类型
        /// </summary>
        [Id(6)] public PassportType PassportType { get; set; }
        /// <summary>
        /// 机构Id
        /// </summary>
        [Id(7)]
        public long? OrganizationId { get; set; }
    }
}

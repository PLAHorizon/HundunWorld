using Horizon.Share.Dtos.User;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.Verification
{
    /// <summary>
    /// 验证数据Dto
    /// </summary>
    public class VerificationUserDataDto
    {
        public VerificationUserDataDto(string token)
        {
            Token = token;
        }
        /// <summary>
        /// 通行证
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 用户数据类型
        /// </summary>
        public UserInfoType Type { get; set; }
        /// <summary>
        /// 待验证数据
        /// </summary>
        public string Value { get; set; }
        /// <summary>
        /// 服务端验证值
        /// </summary>
        public string Token { get; }
    }
}

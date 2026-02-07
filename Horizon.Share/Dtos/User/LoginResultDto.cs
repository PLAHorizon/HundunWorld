using Orleans;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 登录授权返回Dto
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class LoginResultDto
    {
        /// <summary>
        /// 访问令牌
        /// </summary>
        [Id(0)] public string AccessToken { get; set; }

        /// <summary>
        /// 刷新令牌
        /// </summary>
        [Id(1)] public string RefreshToken { get; set; }

        /// <summary>
        /// 有效时间（秒）
        /// </summary>
        [Id(2)] public long ExpiresIn { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        [Id(3)] public DateTime ExpiresTime { get; set; }

    }
}

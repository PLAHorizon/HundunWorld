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

        /// <summary>
        /// IM网关鉴权令牌（AES-256-CBC加密，用于IM Gateway身份验证）
        /// </summary>
        [Id(4)] public string ImAuthToken { get; set; }

        /// <summary>
        /// 用户ID（用于角色列表请求等后续操作）
        /// </summary>
        [Id(5)] public ulong UserId { get; set; }

    }
}

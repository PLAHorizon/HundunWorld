using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 身份认证服务接口
    /// </summary>
    public interface IIdentityServer
    {
        /// <summary>
        /// 获取身份授权令牌
        /// </summary>
        /// <typeparam name="T">携带身份信息的数据类型</typeparam>
        /// <param name="loginDto">携带身份信息的数据是列</param>
        /// <returns></returns>
        Task<IdentityTokenData> AccessTokenAsync<T>(T loginDto);
        /// <summary>
        /// 更新授权令牌
        /// </summary>
        /// <param name="refreshToken">更新令牌的口令</param>
        /// <returns></returns>
        Task<IdentityTokenData> RefreshTokenAsync(string refreshToken);
    }
}

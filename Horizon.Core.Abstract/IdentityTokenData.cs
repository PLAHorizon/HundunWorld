using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{

    /// <summary>
    /// 身份授权令牌
    /// </summary>
    public class IdentityTokenData
    {
        /// <summary>
        /// 身份授权是否成功
        /// </summary>
        public bool IsSuccess { get; set; }
        /// <summary>
        /// 错误码
        /// </summary>
        public string ErrorCode { get; set; }
        /// <summary>
        /// 授权的令牌字符串
        /// </summary>
        public string AccessToken { get; private set; }

        /// <summary>
        /// 身份令牌
        /// </summary>
        public string IdentityToken { get; private set; }

        /// <summary>
        /// 身份授权环境
        /// </summary>
        public string Scope { get; private set; }

        /// <summary>
        /// 已颁发的令牌类型
        /// </summary>
        public string IssuedTokenType { get; private set; }

        /// <summary>
        /// 令牌类型
        /// </summary>
        public string TokenType { get; private set; }

        /// <summary>
        /// 替换/更新令牌口令
        /// </summary>
        public string RefreshToken { get; private set; }

        /// <summary>
        /// 身份授权错误描述
        /// </summary>
        public string ErrorDescription { get; private set; }


        /// <summary>
        /// 令牌过期时间
        /// </summary>
        public int ExpiresIn { get; private set; }
    }
}

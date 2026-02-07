using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 错误码
    /// </summary>
    public class ErrorCodes
    {
        /// <summary>
        /// 无效用户
        /// </summary>
        public const string INVALID_USER = "401";
        /// <summary>
        /// 受限用户
        /// </summary>
        public const string LIMITED_USER = "301";
        /// <summary>
        /// 时限受限用户
        /// </summary>
        public const string TIMELIMITED_USER = "302";
    }
}

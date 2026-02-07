using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 设置密码接口
    /// </summary>
    public interface ISetPassword
    {
        /// <summary>
        /// 设置密码
        /// </summary>
        /// <param name="password">明文密码</param>
        /// <param name="salt">佐料</param>
        /// <returns></returns>
        string SetPassword(string password, string salt);
    }
}

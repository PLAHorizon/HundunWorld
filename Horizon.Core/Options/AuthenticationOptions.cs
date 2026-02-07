using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// 认证选项
    /// </summary>
    public class AuthenticationOptions
    {
        public string CheckSessionCookieDomain { get; set; }
        public string CheckSessionCookieName { get; set; }
    }
}

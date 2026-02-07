using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// 授权选项
    /// </summary>
    public class AuthorizationOptions
    {
        public string Authority { get; set; }
        public string Audience { get; set; }
    }
}

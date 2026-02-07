using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 邮件标题枚举
    /// </summary>
    public enum EmailTitleEnum
    {
        /// <summary>
        /// 用户服务请求提醒邮件
        /// </summary>
        [Description("用户服务请求提醒邮件")]
        RequestService = 0,
    }
}

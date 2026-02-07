using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 消息枚举
    /// </summary>
    public enum MessageEnum
    {
        /// <summary>
        /// 邮件
        /// </summary>
        [Description("邮件")]
        Email = 0,
        /// <summary>
        /// 短信
        /// </summary>
        [Description("短信")]
        Phone = 1,
    }
}

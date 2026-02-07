using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 性别类型
    /// </summary>
    public enum GenderType
    {
        /// <summary>
        /// 女
        /// </summary>
        [Description("女")]
        Female = 0,
        /// <summary>
        /// 男
        /// </summary>
        [Description("男")]
        Male = 1,
        /// <summary>
        /// 未知
        /// </summary>
        [Description("未知")]
        Other = -1
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 性别
    /// </summary>
    public enum Gender
    {

        /// <summary>
        /// 男
        /// </summary>
        [Description("男")]
        Male = 1,
        /// <summary>
        /// 女
        /// </summary>
        [Description("女")]
        Female = 0,
    }
}

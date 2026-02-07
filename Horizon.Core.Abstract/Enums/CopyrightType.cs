using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 版权类型
    /// </summary>
    public enum CopyrightType
    {
        /// <summary>
        /// 自有版权
        /// </summary>
        [Description("自有版权")]
        Default = 0,
        /// <summary>
        /// 自由版权
        /// </summary>
        [Description("自由版权")]
        Open = 1
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 分成比率状态
    /// </summary>
    public enum SplitRateStatusEnum
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 优先
        /// </summary>
        [Description("优先")]
        First = 1,
        /// <summary>
        /// 冻结
        /// </summary>
        [Description("冻结")]
        Frozen = -1,
        /// <summary>
        /// 已失效
        /// </summary>
        [Description("已失效")]
        Invalid = -2,

    }
}

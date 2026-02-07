using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 阅读收费模式
    /// </summary>
    public enum ReadingChargeMode
    {
        /// <summary>
        /// 免费
        /// </summary>
        [Description("免费")]
        Free = 0,
        /// <summary>
        /// 时间区间收费
        /// </summary>
        [Description("时间区间收费")]
        Time = 1,
        /// <summary>
        /// 章节付费
        /// </summary>
        [Description("章节付费")]
        Chapters = 2
    }
}

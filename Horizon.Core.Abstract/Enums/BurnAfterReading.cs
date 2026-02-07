using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    ///阅后即焚类型
    /// </summary>
    public enum BurnAfterReading
    {
        /// <summary>
        /// 10秒
        /// </summary>
        [Description("10秒")]
        TenS = 0,
        /// <summary>
        /// 30秒
        /// </summary>
        [Description("30秒")]
        ThirtyS = 1,
        /// <summary>
        /// 1分钟
        /// </summary>
        [Description("1分钟")]
        OneM = 2,
        /// <summary>
        /// 30分钟
        /// </summary>
        [Description("30分钟")]
        ThirtyM = 3,
        /// <summary>
        /// 长效
        /// </summary>
        [Description("长效")]
        LongTerm = 4,
        /// <summary>
        /// 自定义
        /// </summary>
        [Description("长效")]
        Other = 5,
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 评价类型
    /// </summary>
    public enum CommentType
    {
        /// <summary>
        /// 门店评价
        /// </summary>
        [Description("门店")]
        Branch = 0,
        /// <summary>
        /// 验光师评价
        /// </summary>
        [Description("验光师")] Optometry = 1,
        /// <summary>
        /// 流程评价
        /// </summary>
        [Description("流程")] Event = 2,

        /// <summary>
        /// 回复门店评价
        /// </summary>
        [Description("门店回复")]
        ReplyBranch = 10,
        /// <summary>
        /// 回复验光师评价
        /// </summary>
        [Description("验光师回复")] ReplyOptometry = 11,
        /// <summary>
        /// 回复流程评价
        /// </summary>
        [Description("回复流程")] ReplyEvent = 12,
    }
}

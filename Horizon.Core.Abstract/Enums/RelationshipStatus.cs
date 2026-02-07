using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 好友关系状态
    /// </summary>
    public enum RelationshipStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 未确认
        /// </summary>
        [Description("未确认")]
        UnKnow = 1,
        /// <summary>
        /// 拒绝
        /// </summary>
        [Description("拒绝")]
        Refuse = 2,
        /// <summary>
        /// 已删除（对方）
        /// </summary>
        [Description("已删除")]
        SelfDelete = 3,
        /// <summary>
        /// 已删除（对方删除自己）
        /// </summary>
        [Description("已删除")]
        UnSelfDelete = 4,
    }
}

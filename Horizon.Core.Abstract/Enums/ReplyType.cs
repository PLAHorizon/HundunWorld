using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 微信回复类型
    /// </summary>
    public enum WXReplyType
    {
        /// <summary>
        /// 关键词自动回复
        /// </summary>
        [Description("关键词自动回复")]
        Keyword = 0,

        /// <summary>
        /// 关注后自动回复
        /// </summary>
        [Description("关注后自动回复")]
        Follow = 1,

        /// <summary>
        /// 消息自动回复
        /// </summary>
        [Description("消息自动回复")]
        Msg = 2,
    }
}

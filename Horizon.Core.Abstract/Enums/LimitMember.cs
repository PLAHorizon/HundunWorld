using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 聊天群组/话题/运营号 成员上限
    /// </summary>
    public enum LimitMember
    {
        /// <summary>
        /// 500
        /// </summary>
        [Description("500")]
        Five = 500,
        /// <summary>
        /// 1000
        /// </summary>
        [Description("1000")]
        OneThousand = 1000,
        /// <summary>
        /// 5000
        /// </summary>
        [Description("5000")]
        FiveThousand = 5000,
    }
}

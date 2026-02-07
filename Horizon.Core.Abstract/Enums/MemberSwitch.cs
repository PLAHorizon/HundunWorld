using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 会员特定身份切换
    /// </summary>
    public enum MemberSwitch
    {
        /// <summary>
        /// 顾客
        /// </summary>
        [Description("顾客")]
        Member = 0,
        /// <summary>
        /// 工作Style
        /// </summary>
        [Description("工作Style")]
        Executor = 1,
        /// <summary>
        /// 门店管理
        /// </summary>
        [Description("门店管理")]
        Branch = 2,
        /// <summary>
        /// 企业管理
        /// </summary>
        [Description("企业管理")]
        Enterprise = 3,
        /// <summary>
        /// 系统管理
        /// </summary>
        [Description("系统管理")]
        System = 4
    }
}

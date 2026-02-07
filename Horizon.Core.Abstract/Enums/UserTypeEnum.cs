
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 用户类型
    /// </summary>
    public enum UserTypeEnum
    {
        /// <summary>
        /// 系统用户
        /// </summary>
        [Description("系统用户")]
        SysManager = -9,
        /// <summary>
        /// 管理员用户
        /// </summary>
        [Description("应用管理用户")]
        Admin = 0,
        /// <summary>
        /// 执行者
        /// </summary>
        [Description("应用操作用户")]
        Executor = 1,
        /// <summary>
        /// 会员
        /// </summary>
        [Description("应用使用用户")]
        Member = 2,
        /// <summary>
        /// 游客
        /// </summary>
        [Description("临时用户")]
        Guset = 3,
        /// <summary>
        /// 其它用户
        /// </summary>
        [Description("未知用户")]
        Other = -1
    }
}

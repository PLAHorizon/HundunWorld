using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 角色类型
    /// </summary>
    public enum RoleType
    {
        /// <summary>
        /// 能设置角色、权限的类型
        /// </summary>
        [Description("可设置")]
        Setting = 0,
        /// <summary>
        /// 不能操作角色和权限的类型
        /// </summary>
        [Description("不可设置")]
        UnSetting = -1
    }
}

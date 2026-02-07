using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 权限Action的类型
    /// </summary>
    public enum ActionType
    {
        /// <summary>
        /// 菜单
        /// </summary>
        [Description("菜单")]
        Menu = 0,
        /// <summary>
        /// 按钮
        /// </summary>
        [Description("按钮")]
        Button = 1,
        /// <summary>
        /// POST分视图
        /// </summary>
        [Description("POST分视图")]
        POST = 2,
        /// <summary>
        /// GET分视图
        /// </summary>
        [Description("GET分视图")]
        GET = 3,
    }
}

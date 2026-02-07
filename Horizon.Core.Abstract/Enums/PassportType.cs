using Orleans;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 通行证类型
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public enum PassportType
    {
        /// <summary>
        /// 系统用户
        /// </summary>
        [Description("系统用户"), Id(0)]
        System = 999,
        /// <summary>
        /// 普通用户
        /// </summary>
        [Description("普通用户"), Id(1)]
        Normal = 0,
        /// <summary>
        /// 会员
        /// </summary>
        [Description("会员"), Id(2)]
        Member = 1 << 1,

        /// <summary>
        /// 管理员用户
        /// </summary>
        [Description("应用管理用户"), Id(3)]
        Admin = 1 << 2,
        /// <summary>
        /// 执行者
        /// </summary>
        [Description("应用操作用户"), Id(4)]
        Executor = 1 << 3,
    }
}

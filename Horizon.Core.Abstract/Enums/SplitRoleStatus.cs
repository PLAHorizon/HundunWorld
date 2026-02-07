using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 会员分裂角色状态
    /// </summary>
    public enum SplitRoleStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 禁用
        /// </summary>
        [Description("禁用")]
        Disable = -1
    }

    /// <summary>
    /// 会员分裂状态
    /// </summary>
    public enum SplitStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 忙碌
        /// </summary>
        [Description("忙碌")]
        Busye = -1,
        /// <summary>
        /// 休息
        /// </summary>
        [Description("休息")]
        Rest = 2,
        /// <summary>
        /// 请假
        /// </summary>
        [Description("请假")]
        Leave = 3,
    }

    /// <summary>
    /// 会员分裂级别
    /// </summary>
    public enum SplitLevel
    {

        /// <summary>
        /// 初级
        /// </summary>
        [Description("初级")]
        Start = 0,
        /// <summary>
        /// 中级
        /// </summary>
        [Description("中级")]
        Middle = -1,
        /// <summary>
        /// 高级
        /// </summary>
        [Description("高级")]
        High = 2,
        /// <summary>
        /// 特级  
        /// </summary>
        [Description("特级")]
        Supe = 3,
    }
}

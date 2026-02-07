using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 门店状态
    /// </summary>
    public enum BranchUpdateStatus
    {

        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 冻结
        /// </summary>
        [Description("冻结")]
        Frozen = 1,
        /// <summary>
        /// 禁用
        /// </summary>
        [Description("禁用")]
        Disable = 2,
        /// <summary>
        /// 初创
        /// </summary>
        [Description("初创")]
        InitialCreate = 3,
        /// <summary>
        /// 待激活
        /// </summary>
        [Description("待激活")]
        Activation = 4,
    }
}

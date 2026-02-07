using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 企业许可授信类型
    /// </summary>
    public enum EnterpriseAuthType
    {
        /// <summary>
        /// 免费版
        /// </summary>
        [Description("免费版")]
        Free = 0,
        /// <summary>
        /// 标准版
        /// </summary>
        [Description("标准版")]
        Standard = 1,
        /// <summary>
        /// 旗舰版
        /// </summary>
        [Description("旗舰版")]
        Flagship = 2,
    }

    /// <summary>
    /// 企业授权状态
    /// </summary>
    public enum EnterpriseAuthStatus
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
        /// 审核中
        /// </summary>
        [Description("审核中")]
        Autio = 4,
        /// <summary>
        /// 审核第一步
        /// </summary>
        [Description("审核第一步")]
        Autio1 = 5,
        /// <summary>
        /// 审核第二步
        /// </summary>
        [Description("审核第二步")]
        Autio2 = 6,
        /// <summary>
        /// 审核第三步
        /// </summary>
        [Description("审核第三步")]
        Autio3 = 7,
        /// <summary>
        /// 驳回
        /// </summary>
        [Description("驳回")]
        Reject = -1,
        /// <summary>
        /// 重新审核
        /// </summary>
        [Description("重新审核")]
        AgainAutio = 9,
    }
}

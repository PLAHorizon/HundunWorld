using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 代理商提现状态
    /// </summary>
    public enum CashWithdrawalStatusEnum
    {
        /// <summary>
        /// 请求体现
        /// </summary>
        [Description("请求体现")]
        Request = 0,
        /// <summary>
        /// 处理中
        /// </summary>
        [Description("处理中")]
        Process = 1,
        /// <summary>
        /// 提现完成
        /// </summary>
        [Description("体现完成")]
        Complete = 2,
        /// <summary>
        /// 拒绝提现，永不可在提现
        /// </summary>
        [Description("拒绝提现")]
        Refuse = -1,
        /// <summary>
        /// 提现失败
        /// </summary>
        [Description("提现失败")]
        Fail = -2,
        /// <summary>
        /// 节假日顺延
        /// </summary>
        [Description("节假日顺延")]
        HolidaysPostponed = 3,
        /// <summary>
        /// 提现被冻结，解冻后可以再次提现
        /// </summary>
        [Description("提现被冻结")]
        Frozen = 4
    }
    /// <summary>
    /// 代理商提现来源
    /// </summary>
    public enum CashWithdrawalSourceEnum
    {
        /// <summary>
        /// 商城
        /// </summary>
        [Description("商城")]
        Mall = 0,
        /// <summary>
        /// ERP
        /// </summary>
        [Description("ERP")]
        ERP = 1,
    }
}

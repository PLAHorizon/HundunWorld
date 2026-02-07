using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 服务状态枚举
    /// </summary>
    public enum ServiceStatusEnum
    {
        /// <summary>
        /// 请求服务
        /// </summary>
        [Description("请求服务")]
        RequestService = 0,
        /// <summary>
        /// 等待服务中
        /// </summary>
        [Description("等待服务中")]
        Waiting = 1,
        /// <summary>
        /// 服务进行中
        /// </summary>
        [Description("服务进行中")]
        InProcess = 2,
        /// <summary>
        /// 完成
        /// </summary>
        [Description("完成")]
        Complete = 3,
        /// <summary>
        /// 排队服务中
        /// </summary>
        [Description("排队服务中")]
        Queue = 4,
    }
}

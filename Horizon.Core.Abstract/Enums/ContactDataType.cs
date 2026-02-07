using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 社交资料类型
    /// </summary>
    public enum ContactDataType
    {
        /// <summary>
        /// 行业
        /// </summary>
        [Description("行业")]
        Industry = 0,
        /// <summary>
        /// 工作/职位
        /// </summary>
        [Description("工作/职位")]
        Job = 1,
        /// <summary>
        /// 公司
        /// </summary>
        [Description("公司")]
        Company = 2,
        /// <summary>
        /// 来自
        /// </summary>
        [Description("来自")]
        Province = 3,
        /// <summary>
        /// 出没地
        /// </summary>
        [Description("出没地")]
        ActiveCity = 4

    }
}

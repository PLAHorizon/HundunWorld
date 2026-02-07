using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums.Enums
{
    /// <summary>
    /// 签名类型
    /// </summary>
    public enum SiginType
    {
        /// <summary>
        /// 签入
        /// </summary>
        [Description("签入")]
        In = 0,
        /// <summary>
        /// 签出
        /// </summary>
        [Description("签入")]
        Out = 1
    }
}

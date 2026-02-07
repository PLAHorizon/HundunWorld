using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 视力类型
    /// </summary>
    public enum SeeingType
    {
        /// <summary>
        /// 近视
        /// </summary>
        [Description("近视")]
        Myopia = 0,
        /// <summary>
        /// 远视
        /// </summary>
        [Description("远视")]
        Hyperopia = 1

    }
}

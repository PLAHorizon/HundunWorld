using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    public interface ISupport
    {
        /// <summary>
        /// 支持数
        /// </summary>
        int SupportCount { get; set; }
        /// <summary>
        /// 反对数
        /// </summary>
        int UnSupportCount { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 实体类聚集根
    /// </summary>
    public interface IAggregateRoot
    {
        /// <summary>
        /// 创建人通行证
        /// </summary>
        string Passport { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        DateTime CreateTime { get; set; }
        /// <summary>
        /// 修改人通行证
        /// </summary>
        string ModifyPassport { get; set; }
        /// <summary>
        /// 修改时间
        /// </summary>
        DateTime? ModifyTime { get; set; }
    }
}

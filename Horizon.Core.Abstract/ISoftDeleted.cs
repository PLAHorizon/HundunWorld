using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 数据软删除接口
    /// </summary>
    public interface ISoftDeleted
    {
        /// <summary>
        /// 是否已删除，true : 已删除，false : 未删除
        /// </summary>        
        bool IsDeleted { get; set; }
    }
}

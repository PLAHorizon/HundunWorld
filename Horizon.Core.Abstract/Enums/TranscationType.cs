using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 事务类型
    /// </summary>
    public enum TranscationType
    {
        /// <summary>
        /// 数据库事务
        /// </summary>
        [Description("数据库事务")]
        Db = 1001,
        /// <summary>
        /// 业务事务
        /// </summary>
        [Description("业务事务")]
        Buisness = Db << 1
    }
}

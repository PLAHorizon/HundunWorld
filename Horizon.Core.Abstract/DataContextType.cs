using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 数据上下文类型
    /// </summary>
    public enum DataContextType
    {
        SqlServer = 0,
        Oracle = 1,
        Mysql = 2,
        Npgsql = 5
    }
}

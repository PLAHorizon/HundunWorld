using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 数据操作类型
    /// </summary>

    public enum DataOptions
    {
        [EnumMember]
        Add = 0,
        [EnumMember]
        Update = 1,
        [EnumMember]
        Select = 2,
        [EnumMember]
        Delete = 3
    }
}

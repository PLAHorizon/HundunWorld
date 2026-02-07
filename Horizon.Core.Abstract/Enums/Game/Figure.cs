using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 游戏人物体形
    /// </summary>

    public enum Figure
    {
        /// <summary>
        /// 正常体形
        /// </summary>
        [EnumMember]
        Normal = 0,
        /// <summary>
        /// 萝莉/正太
        /// </summary>
        [EnumMember]
        Small = 1,
        /// <summary>
        /// 高大/威武
        /// </summary>
        [EnumMember]
        Big = 2
    }
}

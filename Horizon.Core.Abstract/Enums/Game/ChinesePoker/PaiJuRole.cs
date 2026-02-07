using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 牌局中的角色
    /// </summary>

    public enum PaiJuRole
    {
        /// <summary>
        /// 正常玩家
        /// </summary>
        [EnumMember, Description("正常玩家")]
        Normal = 0,
        /// <summary>
        /// 旁观玩家
        /// </summary>
        [EnumMember, Description("旁观玩家")]
        Spectator = 1,
    }
}

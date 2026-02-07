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
    /// 斗地主中物品类型
    /// </summary>

    public enum PokerItemType
    {
        /// <summary>
        /// 欢乐豆
        /// </summary>
        [EnumMember, Description("欢乐豆")]
        HappyDou = 0,
    }
}

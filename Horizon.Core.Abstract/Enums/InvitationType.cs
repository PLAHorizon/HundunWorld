using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 邀请/挑战 类型
    /// </summary>
    public enum InvitationType
    {
        /// <summary>
        /// 添加好友
        /// </summary>
        [Description("添加好友")]
        Relationship = 0,
        /// <summary>
        /// 颜值PK
        /// </summary>
        [Description("颜值PK")]
        FaceScorePK = 1,
        /// <summary>
        /// 限时挑战
        /// </summary>
        [Description("限时挑战")]
        TimedChallenge = 2,
    }
}

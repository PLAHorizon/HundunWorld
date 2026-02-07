using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 消息范围
    /// </summary>

    public enum MessageRange
    {
        /// <summary>
        /// 世界消息
        /// </summary>
        [EnumMember]
        World = 0,
        /// <summary>
        /// 本地消息
        /// </summary>
        [EnumMember]
        Local = 1,
        /// <summary>
        /// 团队消息
        /// </summary>
        [EnumMember]
        Group = 2,
        /// <summary>
        /// 队伍消息
        /// </summary>
        [EnumMember]
        Team = 3,
        /// <summary>
        /// 密聊消息
        /// </summary>
        [EnumMember]
        Private = 4,
        /// <summary>
        /// 贸易消息
        /// </summary>
        [EnumMember]
        Trade = 5,
        /// <summary>
        /// 战斗日志消息
        /// </summary>
        [EnumMember]
        CombatLog = 6
    }
}

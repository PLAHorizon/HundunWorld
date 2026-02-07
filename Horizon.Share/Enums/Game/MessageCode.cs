using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Enums.Game
{
    /// <summary>
    /// 消息码
    /// </summary>
    public enum GameMessageCode : ushort
    {
        /// <summary>
        /// 建立连接
        /// </summary>
        [Description("建立连接")]
        Connection = 0,
        /// <summary>
        /// 位置
        /// </summary>
        [Description("位置")] Position = 1,
        /// <summary>
        /// 朝向
        /// </summary>
        [Description("朝向")] Rotation = 2,
        /// <summary>
        /// 动画
        /// </summary>
        [Description("动画")] Animation = 3,
        /// <summary>
        /// 装备
        /// </summary>
        [Description("装备")] Equipment = 4,
        /// <summary>
        /// 聊天
        /// </summary>
        [Description("聊天")] Chat = 5,
        /// <summary>
        /// 断开连接
        /// </summary>
        [Description("断开连接")] DisConnection = 40400,
    }
}

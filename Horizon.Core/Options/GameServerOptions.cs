using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Options
{
    /// <summary>
    /// 游戏服务器标识项
    /// </summary>
    public class GameServerOptions
    {
        /// <summary>
        /// 游戏Id
        /// </summary>
        public int GameId { get; set; }
        /// <summary>
        /// 游戏区Id
        /// </summary>
        public int AreaId { get; set; }
        /// <summary>
        /// 游戏服务器Id
        /// </summary>
        public int ServerId { get; set; }
    }
}

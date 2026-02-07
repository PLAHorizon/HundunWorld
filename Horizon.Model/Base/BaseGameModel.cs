using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Model.Base
{
    /// <summary>
    /// 游戏基础类
    /// </summary>
    public class BaseGameModel<T> : BaseIdentityModel<T>
    {
        /// <summary>
        /// 游戏id
        /// </summary>
        public int GameId { get; set; }
        
        /// <summary>
        /// 游戏区
        /// </summary>
        public int AreaId { get; set; }
        
        /// <summary>
        /// 游戏服
        /// </summary>
        public int ServerId { get; set; }

        /// <summary>
        /// 游戏本地用户Id
        /// </summary>
        public long GameUserId { get; set; }
    }
}

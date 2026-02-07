using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏玩家信息
    /// </summary>
    public record PlayerDto
    {
        /// <summary>
        /// 网络Id
        /// </summary>
        public ushort NetworkIdentity { get; set; }
        /// <summary>
        /// 游戏角色Id
        /// </summary>
        public ulong UserRoleId { get; set; }
        /// <summary>
        /// 玩家X坐标
        /// </summary>
        public double PostionX { get; set; }
        /// <summary>
        /// 玩家Y坐标
        /// </summary>
        public double PostionY { get; set; }
        /// <summary>
        /// 玩家Z坐标
        /// </summary>
        public double PostionZ { get; set; }
        /// <summary>
        /// 玩家W坐标
        /// </summary>
        public double PostionW { get; set; }
        /// <summary>
        /// 玩家角度
        /// </summary>
        public double Rotation { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 角色位置信息
    /// </summary>
    [Serializable]
    public class PositionAndRotateMessage : IMessage
    {
        /// <summary>
        /// 网络Id
        /// </summary>

        public required string NetworkId { get; set; } = "0";
        /// <summary>
        /// 用户角色Id
        /// </summary>
        public long UserRoleId { get; set; }
        /// <summary>
        /// 位置
        /// </summary>
        public Vector3 Position { get; set; }
        /// <summary>
        /// 转向
        /// </summary>
        public Quaternion Rotate { get; set; }
    }
}

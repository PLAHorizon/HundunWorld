using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 角色出生信息
    /// </summary>
    public record CharacterSpawnerDto
    {
        /// <summary>
        /// 世界Id
        /// </summary>
        public uint WorldId { get; set; }
        /// <summary>
        /// 场景Id
        /// </summary>
        public uint SceneId { get; set; }
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

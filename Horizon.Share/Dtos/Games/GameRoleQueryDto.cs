using Horizon.Share.Enums.Game;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏角色查询
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameRoleQueryDto
    {
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(0)] public int Id { get; set; }
        /// <summary>
        /// 角色类型
        /// </summary>
        [Id(1)] public GameRoleKind Kind { get; set; }
        /// <summary>
        /// 游戏角色Id
        /// </summary>
        [Id(2)] public long? RoleNormalId { get; set; }
        /// <summary>
        /// 用户游戏角色Id
        /// </summary>
        [Id(3)] public long? UserRoleId { get; set; }
    }
}

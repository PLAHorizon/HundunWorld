using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏角色在游戏世界中的地理信息
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameRoleWorldInfoDto : GameRoleDto
    {
        [Id(8)] public decimal X { get; set; }
        [Id(9)] public decimal Y { get; set; }
        [Id(10)] public decimal Z { get; set; }
        [Id(11)] public decimal W { get; set; }
        /// <summary>
        /// 是否活着
        /// </summary>
        [Id(12)] public bool IsLive { get; set; }
    }
}

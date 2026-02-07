using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    [Serializable]
    [GenerateSerializer]
    public class GameQueryDto
    {
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(1)] public int GameId { get; set; }
        /// <summary>
        /// 游戏分区Id
        /// </summary>
        [Id(2)] public int AreaId { get; set; }
        /// <summary>
        /// 游戏服务器Id
        /// </summary>
        [Id(3)] public int ServerId { get; set; }
        /// <summary>
        /// 游戏用户Id
        /// </summary>
        [Id(4)] public long GameUserId { get; set; }
        /// <summary>
        /// 角色Id
        /// </summary>
        [Id(5)] public long CharacterId { get; set; }

    }
}

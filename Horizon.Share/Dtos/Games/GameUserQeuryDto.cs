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
    public class GameUserQeuryDto
    {
        /// <summary>
        /// 通行证Id
        /// </summary>
        [Id(0)] public string PassportId { get; set; }
        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(1)] public int GameId { get; set; }
    }
}

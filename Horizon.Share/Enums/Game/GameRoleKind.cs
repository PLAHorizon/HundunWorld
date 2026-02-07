using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Enums.Game
{
    /// <summary>
    /// 游戏角色类型
    /// </summary>
    public enum GameRoleKind
    {
        /// <summary>
        /// 玩家
        /// </summary>
        Player = 0,
        /// <summary>
        /// 游戏NPC
        /// </summary>
        NPC = 1,
        /// <summary>
        /// 中立生物
        /// </summary>
        Neutrality = 2,
        /// <summary>
        /// 怪物
        /// </summary>
        Monster = 3,
        /// <summary>
        /// 敌人
        /// </summary>
        Enemy = 4
    }
}

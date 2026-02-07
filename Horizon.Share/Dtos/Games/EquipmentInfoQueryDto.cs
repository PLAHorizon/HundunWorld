using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 查询装备信息
    /// </summary>
    public class EquipmentInfoQueryDto
    {

        /// <summary>
        /// 游戏Id
        /// </summary>
        [Id(0)] public int GameId { get; set; }
        /// <summary>
        /// 游戏分区Id
        /// </summary>
        [Id(1)] public int AreaId { get; set; }
        /// <summary>
        /// 游戏服务器Id
        /// </summary>
        [Id(2)] public int ServerId { get; set; }


    }
    /// <summary>
    /// 查询角色装备信息
    /// </summary>
    public class RoleEquipmentInfoQueryDto : EquipmentInfoQueryDto
    {
        /// <summary>
        /// 游戏角色Id
        /// </summary>
        [Id(4)] public long UserRoleId { get; set; }
        /// <summary>
        /// 角色序号
        /// </summary>
        [Id(5)] public int Index { get; set; } = 0;
    }

    /// <summary>
    ///通过Id 查询装备信息
    /// </summary>
    public class EquipmentIdQueryDto : EquipmentInfoQueryDto
    {
        /// <summary>
        /// 游戏角色Id
        /// </summary>
        [Id(4)] public long EquipmentId { get; set; }

    }
}

using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏内用户游戏角色
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameRoleDto
    {
        /// <summary>
        /// 角色序号
        /// </summary>
        [Id(0)] public int Index { get; set; }
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
        /// 游戏角色Id
        /// </summary>
        [Id(5)] public long UserRoleId { get; set; }

        /// <summary>
        /// 游戏角色名称
        /// </summary>
        [Id(6)] public string RoleNickName { get; set; }
        /// <summary>
        /// 游戏角色基础信息Id
        /// </summary>

        [Id(7)] public long RoleNormalId { get; set; }
        /// <summary>
        /// 预留自定义角色数据信息
        /// </summary>
        [Id(8)] public byte[] CustomRoleData { get; set; }



        /// <summary>
        /// 角色头像
        /// </summary>
        [Id(9)] public ushort UserRoleAvatar { get; set; }

        /// <summary>
        /// 角色当前装备
        /// </summary>
        [Id(10)] public RoleEquipmentDto RoleEquipments { get; set; }

    }
}

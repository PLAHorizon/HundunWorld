using Orleans;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 游戏用户角色概要信息
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameUserRoleInfoDto
    {
        [Id(0)]
        public long UserRoleId { get; set; }

        [Id(1)] public string SceneName { get; set; }
        [Id(2)] public bool IsLive { get; set; }
        [Id(3)] public RoleEquipmentDto[] RoleEquipments { get; set; }
    }
    /// <summary>
    /// 游戏角色装备
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class RoleEquipmentDto
    {
        [Id(0)]
        public long UserRoleId { get; set; }
        /// <summary>
        /// 装备在角色身上的第几套索引
        /// </summary>
        [Id(1)]
        public long EquipmentIndex { get; set; }

        //-------------以下是角色的装备栏位,值为装备的相应编号------------

        /// <summary>
        /// 头部
        /// </summary>
        [Id(2)]
        public long Head { get; set; }
        /// <summary>
        /// 面部
        /// </summary>
        [Id(3)] public long Face { get; set; }
        /// <summary>
        /// 颈链
        /// </summary>
        [Id(4)] public long Necklace { get; set; }

        /// <summary>
        /// 衣服
        /// </summary>
        [Id(5)] public long Clothes { get; set; }

        /// <summary>
        /// 腰带
        /// </summary>
        [Id(6)] public long Belt { get; set; }
        /// <summary>
        /// 裤子
        /// </summary>
        [Id(7)] public long Trousers { get; set; }

        /// <summary>
        /// 鞋子
        /// </summary>
        [Id(8)] public long Shoes { get; set; }

        /// <summary>
        /// 左手
        /// </summary>
        [Id(9)] public long LeftHand { get; set; }
        /// <summary>
        /// 右手
        /// </summary>
        [Id(10)] public long RightHand { get; set; }

        /// <summary>
        /// 左戒指
        /// </summary>
        [Id(11)] public long LeftRing { get; set; }
        /// <summary>
        /// 右戒指
        /// </summary>
        [Id(12)] public long RightRing { get; set; }

        /// <summary>
        /// 腰坠
        /// </summary>
        [Id(13)] public long BeltOrnament { get; set; }

        /// <summary>
        /// 武器
        /// </summary>
        [Id(14)] public long Weapon { get; set; }

        /// <summary>
        /// 副手武器
        /// </summary>
        [Id(15)] public long SecondaryWeapon { get; set; }
        /// <summary>
        /// 副手武器
        /// </summary>

        [Id(16)] public long OtherWeapon { get; set; }
    }
}
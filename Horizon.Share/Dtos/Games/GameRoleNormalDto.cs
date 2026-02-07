using Horizon.Core.Abstract.Enums;
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
    /// 游戏基础角色
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class GameRoleNormalDto
    {
        [Id(0)]
        public int GameId { get; set; }
        /// <summary>
        /// 角色的默认编号，游戏中创建角色、游戏进行的基础，以：100001 为起始，同类型角色编号增加位数
        /// 如：100001 是男性的同一职业角色，而1000011是女性角色，1000012是萌娃角色
        /// </summary>

        [Id(1)]
        public int RoleNormalId { get; set; }
        /// <summary>
        /// 角色名称
        /// </summary>

        [Id(2)] public string Name { get; set; }
        /// <summary>
        /// 门派Id
        /// </summary>

        [Id(3)] public int SchoolId { get; set; }
        /// <summary>
        /// 角色性别
        /// </summary>

        [Id(4)] public bool Gender { get; set; }
        /// <summary>
        /// 角色体形
        /// </summary>

        [Id(5)] public Figure Figure { get; set; }

        //-------------以下是角色的装备栏位,值为装备的相应编号------------

        /// <summary>
        /// 头部
        /// </summary>

        [Id(6)] public long Head { get; set; }        /// <summary>
                                                      /// 面部
                                                      /// </summary>

        [Id(8)] public long Face { get; set; }
        /// <summary>
        /// 项链
        /// </summary>

        [Id(7)] public long Necklace { get; set; }        /// <summary>
                                                          /// 衣服
                                                          /// </summary>

        [Id(11)] public long Clothes { get; set; }

        /// <summary>
        /// 腰带
        /// </summary>

        [Id(9)] public long Belt { get; set; }
        /// <summary>
        /// 裤子
        /// </summary>

        [Id(10)] public long Trousers { get; set; }        /// <summary>
                                                           /// 鞋子
                                                           /// </summary>

        [Id(12)] public long Shoes { get; set; }        /// <summary>
                                                        /// 左手
                                                        /// </summary>

        [Id(13)] public long LeftHand { get; set; }        /// <summary>
                                                           /// 右手
                                                           /// </summary>

        [Id(14)] public long RightHand { get; set; }

        /// <summary>
        /// 左戒指
        /// </summary>

        [Id(15)] public long LeftRing { get; set; }
        /// <summary>
        /// 右戒指
        /// </summary>

        [Id(16)] public long RightRing { get; set; }

        /// <summary>
        /// 腰坠
        /// </summary>

        [Id(17)] public long BeltOrnament { get; set; }

        /// <summary>
        /// 武器
        /// </summary>

        [Id(18)] public long Weapon { get; set; }

        /// <summary>
        /// 副手武器
        /// </summary>

        [Id(19)] public long SecondaryWeapon { get; set; }
        /// <summary>
        /// 副手武器
        /// </summary>

        [Id(20)] public long OtherWeapon { get; set; }

        /// <summary>
        /// 默认技能列表，技能编号集合
        /// </summary>

        [Id(21)] public string NormalSkills { get; set; }
    }
}

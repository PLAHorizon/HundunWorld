using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 角色实体
    /// </summary>
    [Table("Game_HunduShijie_Character"), TableDescription(Name = "Game_HunduShijie_Character", Order = "HunduShijie_003", Description = "角色信息")]
    [Comment("角色信息表")]
    [EntityStorage("Game")]
    public class CharacterEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [Key]
        [Column("character_id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "character_id", Order = "1", Description = "角色ID")]
        [Comment("角色ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 用户ID
        /// </summary>
        [Column("user_id", TypeName = "bigint", Order = 2), TableDescription(TypeName = "bigint", Name = "user_id", Order = "2", Description = "用户ID")]
        [Comment("用户ID")]
        public long UserId { get; set; }
        
        /// <summary>
        /// 角色名
        /// </summary>
        [Required]
        [Column("character_name", TypeName = "nvarchar(20)", Order = 3), TableDescription(TypeName = "nvarchar(20)", Name = "character_name", Order = "3", Description = "角色名")]
        [Comment("角色名")]
        public string CharacterName { get; set; }
        
        /// <summary>
        /// 等级
        /// </summary>
        [Column("level", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "level", Order = "4", Description = "等级")]
        [Comment("等级")]
        public int Level { get; set; }
        
        /// <summary>
        /// 经验值
        /// </summary>
        [Column("experience", TypeName = "bigint", Order = 5), TableDescription(TypeName = "bigint", Name = "experience", Order = "5", Description = "经验值")]
        [Comment("经验值")]
        public long Experience { get; set; }
        
        /// <summary>
        /// 职业
        /// </summary>
        [Column("profession", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "profession", Order = "6", Description = "职业")]
        [Comment("职业 0-剑客 1-刀客 2-枪客 等")]
        public int Profession { get; set; }
        
        /// <summary>
        /// 性别
        /// </summary>
        [Column("gender", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "gender", Order = "7", Description = "性别")]
        [Comment("性别 0-男 1-女")]
        public int Gender { get; set; }
        
        /// <summary>
        /// 门派
        /// </summary>
        [Column("sect", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "sect", Order = "8", Description = "门派")]
        [Comment("门派 0-无门派 1-少林寺 2-武当派 等")]
        public int Sect { get; set; }
        
        /// <summary>
        /// 阵营
        /// </summary>
        [Column("faction", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "faction", Order = "9", Description = "阵营")]
        [Comment("阵营 0-中立 1-正派 2-邪派 3-隐世")]
        public int Faction { get; set; }
        
        /// <summary>
        /// 境界等级
        /// </summary>
        [Column("realm_level", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "realm_level", Order = "10", Description = "境界等级")]
        [Comment("境界等级")]
        public int RealmLevel { get; set; }
        
        /// <summary>
        /// 战斗力
        /// </summary>
        [Column("combat_power", TypeName = "bigint", Order = 11), TableDescription(TypeName = "bigint", Name = "combat_power", Order = "11", Description = "战斗力")]
        [Comment("战斗力")]
        public long CombatPower { get; set; }
        
        /// <summary>
        /// 当前血量
        /// </summary>
        [Column("health", TypeName = "float", Order = 12), TableDescription(TypeName = "float", Name = "health", Order = "12", Description = "当前血量")]
        [Comment("当前血量")]
        public float Health { get; set; }
        
        /// <summary>
        /// 最大血量
        /// </summary>
        [Column("max_health", TypeName = "float", Order = 13), TableDescription(TypeName = "float", Name = "max_health", Order = "13", Description = "最大血量")]
        [Comment("最大血量")]
        public float MaxHealth { get; set; }
        
        /// <summary>
        /// 攻击力
        /// </summary>
        [Column("attack_power", TypeName = "float", Order = 14), TableDescription(TypeName = "float", Name = "attack_power", Order = "14", Description = "攻击力")]
        [Comment("攻击力")]
        public float AttackPower { get; set; }
        
        /// <summary>
        /// 防御力
        /// </summary>
        [Column("defense", TypeName = "float", Order = 15), TableDescription(TypeName = "float", Name = "defense", Order = "15", Description = "防御力")]
        [Comment("防御力")]
        public float Defense { get; set; }
        
        /// <summary>
        /// 五行元素类型
        /// </summary>
        [Column("wuxing_element", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "wuxing_element", Order = "16", Description = "五行元素类型")]
        [Comment("五行元素类型 0=无 1=金 2=木 3=水 4=火 5=土")]
        public int WuxingElement { get; set; }
        
        /// <summary>
        /// 当前地图ID
        /// </summary>
        [Column("map_id", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "map_id", Order = "17", Description = "当前地图ID")]
        [Comment("当前地图ID")]
        public int MapId { get; set; }
        
        /// <summary>
        /// X坐标
        /// </summary>
        [Column("position_x", TypeName = "float", Order = 18), TableDescription(TypeName = "float", Name = "position_x", Order = "18", Description = "X坐标")]
        [Comment("X坐标")]
        public float PositionX { get; set; }
        
        /// <summary>
        /// Y坐标
        /// </summary>
        [Column("position_y", TypeName = "float", Order = 19), TableDescription(TypeName = "float", Name = "position_y", Order = "19", Description = "Y坐标")]
        [Comment("Y坐标")]
        public float PositionY { get; set; }
        
        /// <summary>
        /// Z坐标
        /// </summary>
        [Column("position_z", TypeName = "float", Order = 20), TableDescription(TypeName = "float", Name = "position_z", Order = "20", Description = "Z坐标")]
        [Comment("Z坐标")]
        public float PositionZ { get; set; }
        
        /// <summary>
        /// 朝向
        /// </summary>
        [Column("rotation", TypeName = "float", Order = 21), TableDescription(TypeName = "float", Name = "rotation", Order = "21", Description = "朝向")]
        [Comment("朝向")]
        public float Rotation { get; set; }
        
        /// <summary>
        /// 当前称号ID
        /// </summary>
        [Column("current_title_id", TypeName = "int", Order = 22), TableDescription(TypeName = "int", Name = "current_title_id", Order = "22", Description = "当前称号ID")]
        [Comment("当前称号ID")]
        public int? CurrentTitleId { get; set; }
        
        /// <summary>
        /// 侠义值
        /// </summary>
        [Column("chivalry_points", TypeName = "int", Order = 23), TableDescription(TypeName = "int", Name = "chivalry_points", Order = "23", Description = "侠义值")]
        [Comment("侠义值")]
        public int ChivalryPoints { get; set; }
        
        /// <summary>
        /// 恶名值
        /// </summary>
        [Column("evil_points", TypeName = "int", Order = 24), TableDescription(TypeName = "int", Name = "evil_points", Order = "24", Description = "恶名值")]
        [Comment("恶名值")]
        public int EvilPoints { get; set; }
        
        /// <summary>
        /// 声望值
        /// </summary>
        [Column("reputation", TypeName = "int", Order = 25), TableDescription(TypeName = "int", Name = "reputation", Order = "25", Description = "声望值")]
        [Comment("声望值")]
        public int Reputation { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 26), TableDescription(TypeName = "datetime", Name = "create_time", Order = "26", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 最后登录时间
        /// </summary>
        [Column("last_login_time", TypeName = "datetime", Order = 27), TableDescription(TypeName = "datetime", Name = "last_login_time", Order = "27", Description = "最后登录时间")]
        [Comment("最后登录时间")]
        public DateTime? LastLoginTime { get; set; }
        
        /// <summary>
        /// 是否删除
        /// </summary>
        [Column("is_deleted", TypeName = "bit", Order = 28), TableDescription(TypeName = "bit", Name = "is_deleted", Order = "28", Description = "是否删除")]
        [Comment("是否删除")]
        public bool IsDeleted { get; set; }
        
        /// <summary>
        /// 删除时间
        /// </summary>
        [Column("delete_time", TypeName = "datetime", Order = 29), TableDescription(TypeName = "datetime", Name = "delete_time", Order = "29", Description = "删除时间")]
        [Comment("删除时间")]
        public DateTime? DeleteTime { get; set; }
        
        /// <summary>
        /// 头发模型
        /// </summary>
        [Column("hair_model", TypeName = "int", Order = 30), TableDescription(TypeName = "int", Name = "hair_model", Order = "30", Description = "头发模型")]
        [Comment("头发模型")]
        public int HairModel { get; set; }
        
        /// <summary>
        /// 头发颜色
        /// </summary>
        [Column("hair_color", TypeName = "int", Order = 31), TableDescription(TypeName = "int", Name = "hair_color", Order = "31", Description = "头发颜色")]
        [Comment("头发颜色")]
        public int HairColor { get; set; }
        
        /// <summary>
        /// 脸型模型
        /// </summary>
        [Column("face_model", TypeName = "int", Order = 32), TableDescription(TypeName = "int", Name = "face_model", Order = "32", Description = "脸型模型")]
        [Comment("脸型模型")]
        public int FaceModel { get; set; }
        
        /// <summary>
        /// 皮肤颜色
        /// </summary>
        [Column("skin_color", TypeName = "int", Order = 33), TableDescription(TypeName = "int", Name = "skin_color", Order = "33", Description = "皮肤颜色")]
        [Comment("皮肤颜色")]
        public int SkinColor { get; set; }
        
        /// <summary>
        /// 眼睛颜色
        /// </summary>
        [Column("eye_color", TypeName = "int", Order = 34), TableDescription(TypeName = "int", Name = "eye_color", Order = "34", Description = "眼睛颜色")]
        [Comment("眼睛颜色")]
        public int EyeColor { get; set; }
        
        /// <summary>
        /// 游戏用户ID
        /// </summary>
        [Column("game_user_id", TypeName = "bigint", Order = 35), TableDescription(TypeName = "bigint", Name = "game_user_id", Order = "35", Description = "游戏用户ID")]
        [Comment("游戏用户ID")]
        public long GameUserId { get; set; }
    }
}

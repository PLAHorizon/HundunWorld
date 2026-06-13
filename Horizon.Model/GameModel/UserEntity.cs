using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 用户账号实体
    /// </summary>
    [Table("Game_HunduShijie_User"), TableDescription(Name = "Game_HunduShijie_User", Order = "HunduShijie_002", Description = "用户账号信息")]
    [Comment("用户账号表")]
    [EntityStorage("Game")]
    public class UserEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [Key]
        [Column("user_id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "user_id", Order = "1", Description = "用户ID")]
        [Comment("用户ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 账号名
        /// </summary>
        
        [Column("account_name", TypeName = "varchar(50)", Order = 2), TableDescription(TypeName = "varchar(50)", Name = "account_name", Order = "2", Description = "账号名")]
        [Comment("账号名")]
        [Required]
        public string AccountName { get; set; }
        
        /// <summary>
        /// 密码哈希
        /// </summary>
        
        [Column("password_hash", TypeName = "varchar(256)", Order = 3), TableDescription(TypeName = "varchar(256)", Name = "password_hash", Order = "3", Description = "密码哈希")]
        [Comment("密码哈希")]
        [Required]
        public string PasswordHash { get; set; }
        
        /// <summary>
        /// 密码盐
        /// </summary>
        
        [Column("password_salt", TypeName = "varchar(128)", Order = 4), TableDescription(TypeName = "varchar(128)", Name = "password_salt", Order = "4", Description = "密码盐")]
        [Comment("密码盐")]
        [Required]
        public string PasswordSalt { get; set; }
        
        /// <summary>
        /// 账号状态 0-正常 1-冻结 2-封禁
        /// </summary>
        [Column("status", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "status", Order = "5", Description = "账号状态")]
        [Comment("账号状态 0-正常 1-冻结 2-封禁")]
        public int Status { get; set; }
        
        /// <summary>
        /// 活跃等级
        /// </summary>
        [Column("activity_level", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "activity_level", Order = "6", Description = "活跃等级")]
        [Comment("活跃等级")]
        public int ActivityLevel { get; set; }
        
        /// <summary>
        /// 活跃度积分
        /// </summary>
        [Column("activity_points", TypeName = "bigint", Order = 7), TableDescription(TypeName = "bigint", Name = "activity_points", Order = "7", Description = "活跃度积分")]
        [Comment("活跃度积分")]
        public long ActivityPoints { get; set; }
        
        /// <summary>
        /// 累计在线时长（分钟）
        /// </summary>
        [Column("total_online_minutes", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "total_online_minutes", Order = "8", Description = "累计在线时长")]
        [Comment("累计在线时长（分钟）")]
        public int TotalOnlineMinutes { get; set; }
        
        /// <summary>
        /// 连续登录天数
        /// </summary>
        [Column("consecutive_login_days", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "consecutive_login_days", Order = "9", Description = "连续登录天数")]
        [Comment("连续登录天数")]
        public int ConsecutiveLoginDays { get; set; }
        
        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("create_time", TypeName = "datetime", Order = 10), TableDescription(TypeName = "datetime", Name = "create_time", Order = "10", Description = "创建时间")]
        [Comment("创建时间")]
        public DateTime CreateTime { get; set; }
        
        /// <summary>
        /// 最后登录时间
        /// </summary>
        [Column("last_login_time", TypeName = "datetime", Order = 11), TableDescription(TypeName = "datetime", Name = "last_login_time", Order = "11", Description = "最后登录时间")]
        [Comment("最后登录时间")]
        public DateTime? LastLoginTime { get; set; }
        
        /// <summary>
        /// 最后登录IP
        /// </summary>
        [Column("last_login_ip", TypeName = "varchar(50)", Order = 12), TableDescription(TypeName = "varchar(50)", Name = "last_login_ip", Order = "12", Description = "最后登录IP")]
        [Comment("最后登录IP")]
        public string LastLoginIp { get; set; }
        
        /// <summary>
        /// 服务器ID
        /// </summary>
        [Column("server_id", TypeName = "int", Order = 13), TableDescription(TypeName = "int", Name = "server_id", Order = "13", Description = "服务器ID")]
        [Comment("服务器ID")]
        public int ServerId { get; set; }
        
        /// <summary>
        /// 平台ID
        /// </summary>
        [Column("platform_id", TypeName = "varchar(50)", Order = 14), TableDescription(TypeName = "varchar(50)", Name = "platform_id", Order = "14", Description = "平台ID")]
        [Comment("平台ID")]
        public string PlatformId { get; set; }
        
        /// <summary>
        /// 设备ID
        /// </summary>
        [Column("device_id", TypeName = "varchar(128)", Order = 15), TableDescription(TypeName = "varchar(128)", Name = "device_id", Order = "15", Description = "设备ID")]
        [Comment("设备ID")]
        public string DeviceId { get; set; }
        
        /// <summary>
        /// 邮箱
        /// </summary>
        [Column("email", TypeName = "varchar(100)", Order = 16), TableDescription(TypeName = "varchar(100)", Name = "email", Order = "16", Description = "邮箱")]
        [Comment("邮箱")]
        public string Email { get; set; }
        
        /// <summary>
        /// 手机号
        /// </summary>
        [Column("phone", TypeName = "varchar(100)", Order = 17), TableDescription(TypeName = "varchar(100)", Name = "phone", Order = "17", Description = "手机号")]
        [Comment("手机号")]
        public string Phone { get; set; }
        
        /// <summary>
        /// 是否已删除
        /// </summary>
        [Column("is_deleted", TypeName = "bit", Order = 18), TableDescription(TypeName = "bit", Name = "is_deleted", Order = "18", Description = "是否已删除")]
        [Comment("是否已删除")]
        public bool IsDeleted { get; set; }
    }
}

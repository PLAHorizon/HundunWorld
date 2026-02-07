
using Horizon.Model;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using System.Runtime.Serialization;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model
{
    /// <summary>
    /// 用户状态
    /// </summary>
    public enum UserStatsEnum
    {
        /// <summary>
        /// 正常在线
        /// </summary>
        Normal = 0,
        /// <summary>
        /// 签入
        /// </summary>
        Signin = 1,
        /// <summary>
        /// 签出
        /// </summary>
        SignOut = 2,
        /// <summary>
        /// 冻结
        /// </summary>
        Frozen = -1,
        /// <summary>
        /// 废弃，弃用，不再使用
        /// </summary>
        Abandoned = -4
    }
    /// <summary>
    /// 通用用户基类
    /// </summary>   
    public abstract class UserModel<T> : BaseNoneModel<T>
    {
        /// <summary>
        /// 当前用户通行证类型
        /// </summary>
        [Comment("当前用户通行证类型")]
        public PassportType PassportType { get; set; } = PassportType.Member;
        [Comment("用户头像")]
        public string? Avatar { get; set; }
        [Comment("用户简介")]
        public string? Description { get; set; }

        public GenderType? Gender { get; set; }
        /// <summary>
        /// 实际用户的Id
        /// </summary>
        [StringLength(60)]
        public string PassportId { get; set; }
        public long AppId { get; set; }
        public AppType AppType { get; set; }
        /// <summary>
        /// 组织机构Id
        /// </summary>
        public long? OrganizationId { get; set; }
        [StringLength(50)]
        [Comment("真实姓名")]
        public string? Name { get; set; }
        [StringLength(100)]
        [Comment("昵称")]
        public string? NickName { get; set; }
        /// <summary>
        /// 身份证号
        /// </summary>
        [StringLength(50)]
        [Comment("身份Id号")]
        public string? IdCard { get; set; }
        public DateTime? Birthday { get; set; }
        /// <summary>
        /// 星座
        /// </summary>
        public Constellation? Constellation { get; set; }
        /// <summary>
        /// 职业
        /// </summary>
        public string? Occupation { get; set; }
        /// <summary>
        /// 电话
        /// </summary>
        [StringLength(100)]
        [Comment("电话")]
        public string? Phone { get; set; }
        [StringLength(100)]
        [Comment("邮箱")]
        public string? Email { get; set; }
        /// <summary>
        /// 地区标识
        /// </summary>
        public string? RegionPath { get; set; }
        [StringLength(200)]
        [Comment("地址")]
        public string? Address { get; set; }

        [Comment("创建时间")]
        public DateTime? CreateDate { get; set; }
        [Comment("最后登录时间"), ConcurrencyCheck]
        public DateTime? LastLoginDate { get; set; }
        [Comment("冻结时间")]
        public DateTime? FrozenDate { get; set; }
        public new UserStatsEnum Status { get; set; }
        [Comment("登录次数"), ConcurrencyCheck]
        public long LoginNumber { get; set; }
        [Comment("使用地址")]
        public string? IP { get; set; }
    }
}

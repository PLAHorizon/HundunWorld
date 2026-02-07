using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model.Xinggaung
{
    /// <summary>
    /// 用户关注集合
    /// </summary>
    [Table("Xingguang_Follows"), TableDescription(Name = "Xingguang_Follows", Order = "Xingguang_101", Description = "用户关注集合")]
    [Comment("用户关注集合")]
    [EntityStorage("Xingguang")]
    public class Follows : BaseNoneAggregateRootModel<Guid>, ISoftDeleted
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
        public string Passport { get; set; }
        /// <summary>
        /// 关注对象通行证
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 3), TableDescription(TypeName = "varchar(32)", Name = "FollowPassport", Order = "3", Description = "关注对象通行证")]
        [Comment("关注对象通行证")]
        public string FollowPassport { get; set; }
        /// <summary>
        /// 关注对象头像
        /// </summary>
        [Column(TypeName = "varchar(256)", Order = 4), TableDescription(TypeName = "varchar(256)", Name = "FollowAvatar", Order = "4", Description = "关注对象头像")]
        [Comment("关注对象头像")]
        public string FollowAvatar { get; set; }
        /// <summary>
        /// 关注对象昵称
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 5), TableDescription(TypeName = "varchar(32)", Name = "FollowNickName", Order = "5", Description = "关注对象昵称")]
        [Comment("关注对象昵称")]
        public string FollowNickName { get; set; }
        /// <summary>
        /// 关注对象简介
        /// </summary>
        [Column(TypeName = "varchar(256)", Order = 6), TableDescription(TypeName = "varchar(256)", Name = "FollowDescription", Order = "6", Description = "关注对象简介")]
        [Comment("关注对象简介")]
        public string FollowDescription { get; set; }
        /// <summary>
        /// 关注日期
        /// </summary>
        [Column(TypeName = "datetime", Order = 7), TableDescription(TypeName = "datetime", Name = "FollowDate", Order = "7", Description = "关注日期")]
        [Comment("关注日期")]
        public DateTime FollowDate { get; set; }
        /// <summary>
        /// 是否互关
        /// </summary>
        [Column(TypeName = "bool", Order = 8), TableDescription(TypeName = "bool", Name = "IsTwoWay", Order = "8", Description = "是否互关")]
        [Comment("是否互关")]
        public bool IsTwoWay { get; set; }
        /// <summary>
        /// 是否已删除
        /// </summary>
        [Column(TypeName = "bool", Order = 9), TableDescription(TypeName = "bool", Name = "IsDeleted", Order = "9", Description = "是否已删除")]
        [Comment("是否已删除")]
        public bool IsDeleted { get; set; }
    }
}

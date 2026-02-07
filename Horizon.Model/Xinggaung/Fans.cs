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
    /// 粉丝集合
    /// </summary>
    [Table("Xingguang_Fans"), TableDescription(Name = "Xingguang_Fans", Order = "Xingguang_102", Description = "粉丝集合")]
    [Comment("粉丝集合")]
    [EntityStorage("Xingguang")]
    public class Fans : BaseNoneAggregateRootModel<Guid>, ISoftDeleted
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
        public string Passport { get; set; }
        /// <summary>
        /// 粉丝通行证
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 3), TableDescription(TypeName = "varchar(32)", Name = "FanPassport", Order = "3", Description = "粉丝通行证")]
        [Comment("粉丝通行证")]
        public string FanPassport { get; set; }
        /// <summary>
        /// 粉丝头像
        /// </summary>
        [Column(TypeName = "varchar(256)", Order = 4), TableDescription(TypeName = "varchar(256)", Name = "FanAvatar", Order = "4", Description = "粉丝头像")]
        [Comment("粉丝头像")]
        public string FanAvatar { get; set; }
        /// <summary>
        /// 粉丝昵称
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 5), TableDescription(TypeName = "varchar(32)", Name = "FanNickName", Order = "5", Description = "粉丝昵称")]
        [Comment("粉丝昵称")]
        public string FanNickName { get; set; }
        /// <summary>
        /// 粉丝简介
        /// </summary>
        [Column(TypeName = "varchar(256)", Order = 6), TableDescription(TypeName = "varchar(256)", Name = "FanDescription", Order = "6", Description = "粉丝简介")]
        [Comment("粉丝简介")]
        public string FanDescription { get; set; }
        /// <summary>
        /// 关注日期
        /// </summary>
        [Column(TypeName = "datetime", Order = 7), TableDescription(TypeName = "datetime", Name = "FanDate", Order = "7", Description = "关注日期")]
        [Comment("关注日期")]
        public DateTime FanDate { get; set; }
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

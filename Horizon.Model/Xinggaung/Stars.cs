using Horizon.Core.Abstract;
using Horizon.Share.Enums;
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
    /// 星光
    /// </summary>
    [Table("Xingguang_Stars"), TableDescription(Name = "Xingguang_Stars", Order = "Xingguang_102", Description = "星光")]
    [Comment("星光")]
    [EntityStorage("Xingguang")]
    public class Stars : BaseNoneAggregateRootModel<Guid>, ISoftDeleted
    {
        /// <summary>
        /// 通行证
        /// </summary>
        [Column(TypeName = "varchar(32)", Order = 2), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "2", Description = "通行证")]
        [Comment("通行证")]
        public string Passport { get; set; }
        /// <summary>
        /// 本次星光获得类型
        /// </summary>
        [Column(TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "StarsType", Order = "3", Description = "本次星光获得类型")]
        [Comment("本次星光获得类型")]
        public StarsType StarsType { get; set; }
        /// <summary>
        /// 本次星光获得值，正值获得，负值扣除
        /// </summary>
        [Column(TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "Starlight", Order = "4", Description = "本次星光获得值，正值获得，负值扣除")]
        [Comment("本次星光获得值，正值获得，负值扣除")]
        public int Starlight { get; set; }

        /// <summary>
        /// 当前星光值
        /// </summary>
        [Column(TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "CurrentStarlight", Order = "5", Description = "当前星光值")]
        [Comment("当前星光值")]
        public int CurrentStarlight { get; set; }

        /// <summary>
        /// 之前星光值
        /// </summary>
        [Column(TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "PreviousStarlight", Order = "6", Description = "之前星光值")]
        [Comment("当前星光值")]
        public int PreviousStarlight { get; set; }

        /// <summary>
        /// 星光更新日期
        /// </summary>
        [Column(TypeName = "datetime", Order = 7), TableDescription(TypeName = "datetime", Name = "FanDate", Order = "7", Description = "关注日期")]
        [Comment("星光更新日期")]
        public DateTime Date { get; set; }
        /// <summary>
        /// 是否已删除
        /// </summary>
        [Column(TypeName = "bool", Order = 9), TableDescription(TypeName = "bool", Name = "IsDeleted", Order = "9", Description = "是否已删除")]
        [Comment("是否已删除")]
        public bool IsDeleted { get; set; }

    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

namespace Horizon.Model
{
    /// <summary>
    /// 添加好友临时表
    /// </summary>
    [Table("IM_Log_AddRelationship")]
    public class AddRelationshipLog : BaseNoneModel<Guid>
    {
        private Guid _id;
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]

        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }

        /// <summary>
        /// 添加请求会员Id
        /// </summary>
        public string SourceId { get; set; }
        /// <summary>
        /// 被添加会员Id
        /// </summary>
        public string TargetId { get; set; }
        /// <summary>
        /// 添加好友的途径来源
        /// </summary>
        public IMSourceType SourceType { get; set; }
        /// <summary>
        /// 附加消息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// 红包金额
        /// </summary>
        public decimal RedMoney { get; set; }
        /// <summary>
        /// 礼物Id
        /// </summary>
        public long IMGiftId { get; set; }
        /// <summary>
        /// 礼物数量
        /// </summary>
        public long GiftCount { get; set; }
        /// <summary>
        /// 是否已接受
        /// </summary>
        public bool? IsAccpet { get; set; }
    }
}

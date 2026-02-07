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
    /// 聊天投诉
    /// </summary>
    [Table("IM_ChatComplaint")]
    public class ChatComplaint : BaseNoneModel<Guid>
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
        /// 投诉会员
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 被投诉对象
        /// </summary>
        public string TargetId { get; set; }
        /// <summary>
        /// 投诉信息
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 投诉类型
        /// </summary>
        public ChatComplaintType ChatComplaintType { get; set; }
        /// <summary>
        /// 投诉时间
        /// </summary>
        public DateTime Date { get; set; }
    }
}

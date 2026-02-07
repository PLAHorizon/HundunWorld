using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Model
{
    /// <summary>
    /// 受邀 邀请/挑战 列表
    /// </summary>
    [Table("IM_Log_Invitation")]
    public class InvitationLog : BaseNoneModel<Guid>
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
        ///  邀请/挑战 Id
        /// </summary>
        public long InvitationId { get; set; }
        /// <summary>
        /// 发起者Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 受邀者Id
        /// </summary>
        public long BeInvitedId { get; set; }
        /// <summary>
        /// 受邀时间
        /// </summary>
        public DateTime BeInvitedDate { get; set; }
        /// <summary>
        /// 令牌，区分同一项目不同组别
        /// </summary>
        public string Token { get; set; }
    }
}

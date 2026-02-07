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
    /// 聊天群组
    /// </summary>
    [Table("IM_ChatGroup")]
    public class ChatGroup : BaseIdentityModel<long>
    {
        private long _id;
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]

        public new long Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 群组名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 成员上限
        /// </summary>
        public LimitMember LimitMember { get; set; }
        /// <summary>
        /// 是否正常
        /// </summary>
        public bool IsNormal { get; set; }

        public virtual ICollection<M2GChatMessage> M2GChatMessages { get; set; }
    }
}

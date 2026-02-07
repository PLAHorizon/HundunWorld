using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{
    /// <summary>
    /// 会员间聊天信息
    /// </summary>
    [Table("IM_M2MChatMessage"), DataContract]
    public class M2MChatMessage : BaseChatMessage
    {
        /// <summary>
        /// 发送消息会员Id
        /// </summary>
        public string SourceId { get; set; }
        /// <summary>
        /// 接收消息会员Id
        /// </summary>
        public string TargetId { get; set; }
        /// <summary>
        /// 发送消息会员头像
        /// </summary>
        [DataMember, NotMapped] public string SourceIcon { get; set; }
        /// <summary>
        /// 接收消息会员头像
        /// </summary>
        [DataMember, NotMapped] public string TargetIcon { get; set; }
        /// <summary>
        /// 是否已读
        /// </summary>
        public bool? IsRead { get; set; }

    }
}

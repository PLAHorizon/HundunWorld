using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{
    /// <summary>
    /// 会员在群组中的聊天信息
    /// </summary>
    [Table("IM_M2GChatMessage")]
    public class M2GChatMessage : BaseChatMessage
    {
        /// <summary>
        /// 发送消息会员Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 接收消息群组Id
        /// </summary>
        public long GroupId { get; set; }


    }
}

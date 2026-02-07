using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{
    /// <summary>
    /// 会员与机器人之间的聊天信息
    /// </summary>
    [Table("IM_M2BChatMessage")]
    public class M2BChatMessage : BaseChatMessage
    {
        /// <summary>
        /// 发送消息会员Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 接收消息聊天机器人Id
        /// </summary>
        public long ChatBotId { get; set; }


    }
}

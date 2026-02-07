
using Horizon.Share.Enums.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.Games
{
    /// <summary>
    /// 
    /// </summary>
    public record ChatMessageDto
    {
        /// <summary>
        /// 通信客户端网络Id
        /// </summary>
        public string NetworkIdentity { get; set; }
        /// <summary>
        /// 用户游戏角色Id
        /// </summary>
        public ulong UserRoleId { get; set; }
        /// <summary>
        /// 角色名称
        /// </summary>
        public string UserRoleNickName { get; set; }
        /// <summary>
        /// 聊天类型,默认公频聊天
        /// </summary>
        public ChatKind Kind { get; set; } = ChatKind.ChatWorld;
        /// <summary>
        /// 聊天文本内容
        /// </summary>
        public string Message { get; set; }

    }
}

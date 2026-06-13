using System.ComponentModel;

namespace Horizon.IM.Message.Enums
{
    /// <summary>
    /// IM服务类型枚举
    /// </summary>
    public enum IMServiceType : byte
    {
        /// <summary>
        /// IM网关服务
        /// </summary>
        [Description("IM网关服务")]
        Gateway = 1,

        /// <summary>
        /// 聊天服务
        /// </summary>
        [Description("聊天服务")]
        Chat = 2,

        /// <summary>
        /// 联系人服务
        /// </summary>
        [Description("联系人服务")]
        Contact = 3,

        /// <summary>
        /// 通知服务
        /// </summary>
        [Description("通知服务")]
        Notification = 4,

        /// <summary>
        /// 群组服务
        /// </summary>
        [Description("群组服务")]
        Group = 5,

        /// <summary>
        /// 内容审核服务
        /// </summary>
        [Description("内容审核服务")]
        Moderation = 6
    }
}

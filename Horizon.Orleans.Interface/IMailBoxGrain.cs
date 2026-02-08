using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 邮箱系统Grain接口 - 负责邮件收发管理
    /// Key格式: 玩家ID (Guid)
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IMailBoxGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 发送邮件
        /// </summary>
        /// <param name="senderId">发件人ID</param>
        /// <param name="senderName">发件人名称</param>
        /// <param name="title">邮件标题</param>
        /// <param name="content">邮件内容</param>
        /// <param name="mailType">邮件类型</param>
        /// <param name="attachments">附件物品（模板ID -> 数量）</param>
        /// <param name="attachedCurrency">附件货币</param>
        /// <returns>发送结果</returns>
        Task<SendMailResult> SendMailAsync(Guid senderId, string senderName, string title, string content, int mailType,
            Dictionary<int, int>? attachments = null, long attachedCurrency = 0);

        /// <summary>
        /// 获取所有邮件
        /// </summary>
        /// <returns>邮件列表</returns>
        Task<List<MailData>> GetAllMailsAsync();

        /// <summary>
        /// 获取未读邮件
        /// </summary>
        /// <returns>未读邮件列表</returns>
        Task<List<MailData>> GetUnreadMailsAsync();

        /// <summary>
        /// 阅读邮件（标记为已读）
        /// </summary>
        /// <param name="mailId">邮件ID</param>
        /// <returns>邮件数据</returns>
        Task<MailData?> ReadMailAsync(long mailId);

        /// <summary>
        /// 领取邮件附件
        /// </summary>
        /// <param name="mailId">邮件ID</param>
        /// <returns>是否成功</returns>
        Task<bool> ClaimAttachmentAsync(long mailId);

        /// <summary>
        /// 删除邮件
        /// </summary>
        /// <param name="mailId">邮件ID</param>
        /// <returns>是否成功</returns>
        Task<bool> DeleteMailAsync(long mailId);

        /// <summary>
        /// 获取未读邮件数量
        /// </summary>
        /// <returns>未读数量</returns>
        Task<int> GetUnreadCountAsync();

        /// <summary>
        /// 清理过期邮件
        /// </summary>
        /// <returns>清理的邮件数量</returns>
        Task<int> CleanExpiredMailsAsync();
    }
}

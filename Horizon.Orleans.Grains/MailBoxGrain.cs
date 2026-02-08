using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 邮箱系统Grain实现 - 负责邮件收发管理
    /// </summary>
    public class MailBoxGrain : Grain, IMailBoxGrain
    {
        private readonly ILogger<MailBoxGrain> _logger;
        private readonly IPersistentState<MailBoxState> _mailBoxState;

        /// <summary>
        /// 邮件默认过期时间（30天）
        /// </summary>
        private static readonly TimeSpan MailExpiration = TimeSpan.FromDays(30);

        public MailBoxGrain(
            ILogger<MailBoxGrain> logger,
            [PersistentState("mailbox", "GameStore")] IPersistentState<MailBoxState> mailBoxState)
        {
            _logger = logger;
            _mailBoxState = mailBoxState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MailBoxGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_mailBoxState.State.Mails == null)
                _mailBoxState.State.Mails = new Dictionary<long, MailData>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<SendMailResult> SendMailAsync(Guid senderId, string senderName, string title, string content, int mailType,
            Dictionary<int, int>? attachments = null, long attachedCurrency = 0)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    return new SendMailResult
                    {
                        Success = false,
                        Message = "邮件标题不能为空"
                    };
                }

                if (title.Length > 50)
                {
                    return new SendMailResult
                    {
                        Success = false,
                        Message = "邮件标题过长（最多50字符）"
                    };
                }

                if (content != null && content.Length > 500)
                {
                    return new SendMailResult
                    {
                        Success = false,
                        Message = "邮件内容过长（最多500字符）"
                    };
                }

                var state = _mailBoxState.State;

                // Check mailbox capacity (exclude deleted mails)
                var activeMails = state.Mails.Values.Count(m => m.Status != (int)MailStatus.Deleted);
                if (activeMails >= state.MaxMails)
                {
                    return new SendMailResult
                    {
                        Success = false,
                        Message = "邮箱已满"
                    };
                }

                var now = DateTime.UtcNow;
                var mailId = state.NextMailId++;

                var mail = new MailData
                {
                    MailId = mailId,
                    SenderId = senderId,
                    SenderName = senderName ?? "",
                    Title = title.Trim(),
                    Content = content ?? "",
                    MailType = mailType,
                    Status = (int)MailStatus.Unread,
                    Attachments = attachments ?? new Dictionary<int, int>(),
                    AttachedCurrency = attachedCurrency,
                    SendTime = now,
                    ExpireTime = now.Add(MailExpiration)
                };

                state.Mails[mailId] = mail;
                state.UnreadCount++;

                await _mailBoxState.WriteStateAsync();

                _logger.LogInformation("发送邮件: MailId={MailId}, From={SenderId}, Title={Title}",
                    mailId, senderId, title);

                return new SendMailResult
                {
                    Success = true,
                    Message = "发送成功",
                    MailId = mailId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送邮件失败: SenderId={SenderId}", senderId);
                throw;
            }
        }

        public Task<List<MailData>> GetAllMailsAsync()
        {
            try
            {
                var mails = _mailBoxState.State.Mails.Values
                    .Where(m => m.Status != (int)MailStatus.Deleted)
                    .OrderByDescending(m => m.SendTime)
                    .ToList();

                return Task.FromResult(mails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取邮件列表失败");
                throw;
            }
        }

        public Task<List<MailData>> GetUnreadMailsAsync()
        {
            try
            {
                var mails = _mailBoxState.State.Mails.Values
                    .Where(m => m.Status == (int)MailStatus.Unread)
                    .OrderByDescending(m => m.SendTime)
                    .ToList();

                return Task.FromResult(mails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取未读邮件失败");
                throw;
            }
        }

        public async Task<MailData?> ReadMailAsync(long mailId)
        {
            try
            {
                var state = _mailBoxState.State;

                if (!state.Mails.TryGetValue(mailId, out var mail))
                {
                    _logger.LogWarning("邮件不存在: MailId={MailId}", mailId);
                    return null;
                }

                if (mail.Status == (int)MailStatus.Deleted)
                {
                    _logger.LogWarning("邮件已删除: MailId={MailId}", mailId);
                    return null;
                }

                if (mail.Status == (int)MailStatus.Unread)
                {
                    mail.Status = (int)MailStatus.Read;
                    state.UnreadCount = Math.Max(0, state.UnreadCount - 1);
                    await _mailBoxState.WriteStateAsync();
                }

                return mail;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "阅读邮件失败: MailId={MailId}", mailId);
                throw;
            }
        }

        public async Task<bool> ClaimAttachmentAsync(long mailId)
        {
            try
            {
                var state = _mailBoxState.State;

                if (!state.Mails.TryGetValue(mailId, out var mail))
                {
                    _logger.LogWarning("邮件不存在: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Status == (int)MailStatus.Deleted)
                {
                    _logger.LogWarning("邮件已删除: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Status == (int)MailStatus.Claimed)
                {
                    _logger.LogWarning("附件已领取: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Attachments.Count == 0 && mail.AttachedCurrency <= 0)
                {
                    _logger.LogWarning("邮件无附件: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Status == (int)MailStatus.Unread)
                {
                    state.UnreadCount = Math.Max(0, state.UnreadCount - 1);
                }

                mail.Status = (int)MailStatus.Claimed;

                await _mailBoxState.WriteStateAsync();

                _logger.LogInformation("领取邮件附件: MailId={MailId}, Items={ItemCount}, Currency={Currency}",
                    mailId, mail.Attachments.Count, mail.AttachedCurrency);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "领取邮件附件失败: MailId={MailId}", mailId);
                throw;
            }
        }

        public async Task<bool> DeleteMailAsync(long mailId)
        {
            try
            {
                var state = _mailBoxState.State;

                if (!state.Mails.TryGetValue(mailId, out var mail))
                {
                    _logger.LogWarning("邮件不存在: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Status == (int)MailStatus.Deleted)
                {
                    _logger.LogWarning("邮件已删除: MailId={MailId}", mailId);
                    return false;
                }

                // Cannot delete if attachments haven't been claimed
                bool hasAttachments = mail.Attachments.Count > 0 || mail.AttachedCurrency > 0;
                if (hasAttachments && mail.Status != (int)MailStatus.Claimed)
                {
                    _logger.LogWarning("邮件包含未领取的附件，无法删除: MailId={MailId}", mailId);
                    return false;
                }

                if (mail.Status == (int)MailStatus.Unread)
                {
                    state.UnreadCount = Math.Max(0, state.UnreadCount - 1);
                }

                mail.Status = (int)MailStatus.Deleted;
                await _mailBoxState.WriteStateAsync();

                _logger.LogInformation("删除邮件: MailId={MailId}", mailId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除邮件失败: MailId={MailId}", mailId);
                throw;
            }
        }

        public Task<int> GetUnreadCountAsync()
        {
            return Task.FromResult(_mailBoxState.State.UnreadCount);
        }

        public async Task<int> CleanExpiredMailsAsync()
        {
            try
            {
                var state = _mailBoxState.State;
                var now = DateTime.UtcNow;
                var expiredIds = new List<long>();

                foreach (var kvp in state.Mails)
                {
                    if (kvp.Value.Status != (int)MailStatus.Deleted && kvp.Value.ExpireTime < now)
                    {
                        expiredIds.Add(kvp.Key);
                    }
                }

                foreach (var id in expiredIds)
                {
                    var mail = state.Mails[id];
                    if (mail.Status == (int)MailStatus.Unread)
                    {
                        state.UnreadCount = Math.Max(0, state.UnreadCount - 1);
                    }
                    mail.Status = (int)MailStatus.Deleted;
                }

                if (expiredIds.Count > 0)
                {
                    await _mailBoxState.WriteStateAsync();
                    _logger.LogInformation("清理过期邮件: Count={Count}", expiredIds.Count);
                }

                return expiredIds.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期邮件失败");
                throw;
            }
        }
    }
}

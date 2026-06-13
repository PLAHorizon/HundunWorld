using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// MailBoxState, MailData, SendMailResult 数据模型及业务逻辑单元测试
    /// 测试邮件系统的状态管理和邮件收发逻辑
    /// </summary>
    public class MailSystemTests
    {
        #region MailBoxState Tests - 邮箱状态默认值

        [Fact]
        public void MailBoxState_DefaultValues_AreCorrect()
        {
            var state = new MailBoxState();
            Assert.NotNull(state.Mails);
            Assert.Empty(state.Mails);
            Assert.Equal(1, state.NextMailId);
            Assert.Equal(100, state.MaxMails);
            Assert.Equal(0, state.UnreadCount);
        }

        [Fact]
        public void MailBoxState_SetMaxMails_Works()
        {
            var state = new MailBoxState { MaxMails = 200 };
            Assert.Equal(200, state.MaxMails);
        }

        [Fact]
        public void MailBoxState_SetNextMailId_Works()
        {
            var state = new MailBoxState { NextMailId = 50 };
            Assert.Equal(50, state.NextMailId);
        }

        [Fact]
        public void MailBoxState_SetUnreadCount_Works()
        {
            var state = new MailBoxState { UnreadCount = 5 };
            Assert.Equal(5, state.UnreadCount);
        }

        #endregion

        #region MailData Tests - 邮件数据模型

        [Fact]
        public void MailData_DefaultValues_AreCorrect()
        {
            var mail = new MailData();
            Assert.Equal(0, mail.MailId);
            Assert.Equal(Guid.Empty, mail.SenderId);
            Assert.Equal("", mail.SenderName);
            Assert.Equal("", mail.Title);
            Assert.Equal("", mail.Content);
            Assert.Equal(0, mail.MailType);
            Assert.Equal((int)MailStatus.Unread, mail.Status);
            Assert.NotNull(mail.Attachments);
            Assert.Empty(mail.Attachments);
            Assert.Equal(0, mail.AttachedCurrency);
        }

        [Fact]
        public void MailData_SetProperties_Works()
        {
            var senderId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var mail = new MailData
            {
                MailId = 1,
                SenderId = senderId,
                SenderName = "系统",
                Title = "欢迎邮件",
                Content = "欢迎来到混沌世界！",
                MailType = (int)MailType.System,
                Status = (int)MailStatus.Unread,
                SendTime = now,
                ExpireTime = now.AddDays(30),
                Attachments = new Dictionary<int, int> { { 1001, 10 } },
                AttachedCurrency = 1000
            };

            Assert.Equal(1, mail.MailId);
            Assert.Equal(senderId, mail.SenderId);
            Assert.Equal("系统", mail.SenderName);
            Assert.Equal("欢迎邮件", mail.Title);
            Assert.Equal("欢迎来到混沌世界！", mail.Content);
            Assert.Equal((int)MailType.System, mail.MailType);
            Assert.Equal((int)MailStatus.Unread, mail.Status);
            Assert.Equal(now, mail.SendTime);
            Assert.Single(mail.Attachments);
            Assert.Equal(10, mail.Attachments[1001]);
            Assert.Equal(1000, mail.AttachedCurrency);
        }

        [Fact]
        public void MailData_MultipleAttachments_Works()
        {
            var mail = new MailData
            {
                Attachments = new Dictionary<int, int>
                {
                    { 1001, 5 },
                    { 1002, 10 },
                    { 1003, 1 }
                }
            };

            Assert.Equal(3, mail.Attachments.Count);
            Assert.Equal(5, mail.Attachments[1001]);
            Assert.Equal(10, mail.Attachments[1002]);
            Assert.Equal(1, mail.Attachments[1003]);
        }

        #endregion

        #region SendMailResult Tests - 发送邮件结果

        [Fact]
        public void SendMailResult_DefaultValues_AreCorrect()
        {
            var result = new SendMailResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(0, result.MailId);
        }

        [Fact]
        public void SendMailResult_SuccessResult_Works()
        {
            var result = new SendMailResult
            {
                Success = true,
                Message = "发送成功",
                MailId = 42
            };

            Assert.True(result.Success);
            Assert.Equal("发送成功", result.Message);
            Assert.Equal(42, result.MailId);
        }

        [Fact]
        public void SendMailResult_FailureResult_Works()
        {
            var result = new SendMailResult
            {
                Success = false,
                Message = "邮箱已满"
            };

            Assert.False(result.Success);
            Assert.Equal("邮箱已满", result.Message);
        }

        #endregion

        #region MailStatus Enum Tests - 邮件状态枚举

        [Fact]
        public void MailStatus_HasExpectedValues()
        {
            Assert.Equal(0, (int)MailStatus.Unread);
            Assert.Equal(1, (int)MailStatus.Read);
            Assert.Equal(2, (int)MailStatus.Replied);
            Assert.Equal(3, (int)MailStatus.Claimed);
            Assert.Equal(4, (int)MailStatus.Deleted);
        }

        [Fact]
        public void MailStatus_EnumCount_IsCorrect()
        {
            var values = Enum.GetValues<MailStatus>();
            Assert.Equal(5, values.Length);
        }

        #endregion

        #region MailType Enum Tests - 邮件类型枚举

        [Fact]
        public void MailType_HasExpectedValues()
        {
            Assert.Equal(0, (int)MailType.System);
            Assert.Equal(1, (int)MailType.Player);
            Assert.Equal(2, (int)MailType.Guild);
            Assert.Equal(3, (int)MailType.ActivityReward);
        }

        [Fact]
        public void MailType_EnumCount_IsCorrect()
        {
            var values = Enum.GetValues<MailType>();
            Assert.Equal(4, values.Length);
        }

        #endregion

        #region MailBox State Logic Tests - 邮箱状态业务逻辑

        [Fact]
        public void MailBoxState_AddMail_Works()
        {
            var state = new MailBoxState();
            var mailId = state.NextMailId++;

            state.Mails[mailId] = new MailData
            {
                MailId = mailId,
                Title = "测试邮件",
                Content = "测试内容",
                SendTime = DateTime.UtcNow,
                ExpireTime = DateTime.UtcNow.AddDays(30)
            };
            state.UnreadCount++;

            Assert.Single(state.Mails);
            Assert.Equal(1, state.UnreadCount);
            Assert.Equal(2, state.NextMailId);
        }

        [Fact]
        public void MailBoxState_ReadMail_UpdatesStatus()
        {
            var state = new MailBoxState();
            var mailId = state.NextMailId++;

            state.Mails[mailId] = new MailData
            {
                MailId = mailId,
                Title = "未读邮件",
                Status = (int)MailStatus.Unread
            };
            state.UnreadCount = 1;

            // Read the mail
            var mail = state.Mails[mailId];
            mail.Status = (int)MailStatus.Read;
            state.UnreadCount--;

            Assert.Equal((int)MailStatus.Read, mail.Status);
            Assert.Equal(0, state.UnreadCount);
        }

        [Fact]
        public void MailBoxState_ClaimAttachment_UpdatesStatus()
        {
            var state = new MailBoxState();
            var mailId = state.NextMailId++;

            state.Mails[mailId] = new MailData
            {
                MailId = mailId,
                Title = "奖励邮件",
                Status = (int)MailStatus.Read,
                Attachments = new Dictionary<int, int> { { 1001, 5 } },
                AttachedCurrency = 500
            };

            var mail = state.Mails[mailId];
            mail.Status = (int)MailStatus.Claimed;

            Assert.Equal((int)MailStatus.Claimed, mail.Status);
        }

        [Fact]
        public void MailBoxState_DeleteMail_UpdatesStatus()
        {
            var state = new MailBoxState();
            var mailId = state.NextMailId++;

            state.Mails[mailId] = new MailData
            {
                MailId = mailId,
                Title = "旧邮件",
                Status = (int)MailStatus.Read
            };

            var mail = state.Mails[mailId];
            mail.Status = (int)MailStatus.Deleted;

            Assert.Equal((int)MailStatus.Deleted, mail.Status);
        }

        [Fact]
        public void MailBoxState_FilterUnreadMails_Works()
        {
            var state = new MailBoxState();

            for (int i = 1; i <= 5; i++)
            {
                state.Mails[i] = new MailData
                {
                    MailId = i,
                    Title = $"邮件{i}",
                    Status = i <= 3 ? (int)MailStatus.Unread : (int)MailStatus.Read
                };
            }

            var unread = state.Mails.Values.Where(m => m.Status == (int)MailStatus.Unread).ToList();
            Assert.Equal(3, unread.Count);
        }

        [Fact]
        public void MailBoxState_FilterActiveMails_ExcludesDeleted()
        {
            var state = new MailBoxState();

            state.Mails[1] = new MailData { MailId = 1, Status = (int)MailStatus.Unread };
            state.Mails[2] = new MailData { MailId = 2, Status = (int)MailStatus.Read };
            state.Mails[3] = new MailData { MailId = 3, Status = (int)MailStatus.Deleted };

            var active = state.Mails.Values.Where(m => m.Status != (int)MailStatus.Deleted).ToList();
            Assert.Equal(2, active.Count);
        }

        [Fact]
        public void MailBoxState_MailCapacityCheck_Works()
        {
            var state = new MailBoxState { MaxMails = 3 };

            for (int i = 1; i <= 3; i++)
            {
                state.Mails[i] = new MailData { MailId = i, Status = (int)MailStatus.Read };
            }

            var activeMails = state.Mails.Values.Count(m => m.Status != (int)MailStatus.Deleted);
            Assert.Equal(3, activeMails);
            Assert.True(activeMails >= state.MaxMails);
        }

        [Fact]
        public void MailBoxState_ExpiredMailCleanup_Works()
        {
            var state = new MailBoxState();
            var now = DateTime.UtcNow;

            state.Mails[1] = new MailData
            {
                MailId = 1,
                Title = "过期邮件",
                Status = (int)MailStatus.Read,
                ExpireTime = now.AddDays(-1)
            };
            state.Mails[2] = new MailData
            {
                MailId = 2,
                Title = "未过期邮件",
                Status = (int)MailStatus.Read,
                ExpireTime = now.AddDays(29)
            };

            var expired = state.Mails.Values
                .Where(m => m.Status != (int)MailStatus.Deleted && m.ExpireTime < now)
                .ToList();

            Assert.Single(expired);
            Assert.Equal(1, expired[0].MailId);
        }

        [Fact]
        public void MailBoxState_SortByTime_Descending()
        {
            var state = new MailBoxState();
            var now = DateTime.UtcNow;

            state.Mails[1] = new MailData { MailId = 1, Title = "旧邮件", SendTime = now.AddHours(-2) };
            state.Mails[2] = new MailData { MailId = 2, Title = "新邮件", SendTime = now };
            state.Mails[3] = new MailData { MailId = 3, Title = "中间邮件", SendTime = now.AddHours(-1) };

            var sorted = state.Mails.Values.OrderByDescending(m => m.SendTime).ToList();

            Assert.Equal("新邮件", sorted[0].Title);
            Assert.Equal("中间邮件", sorted[1].Title);
            Assert.Equal("旧邮件", sorted[2].Title);
        }

        [Fact]
        public void MailData_HasAttachments_Check()
        {
            var mailNoAttach = new MailData { Attachments = new Dictionary<int, int>(), AttachedCurrency = 0 };
            var mailWithItems = new MailData { Attachments = new Dictionary<int, int> { { 1, 1 } }, AttachedCurrency = 0 };
            var mailWithCurrency = new MailData { Attachments = new Dictionary<int, int>(), AttachedCurrency = 100 };

            bool hasAttach1 = mailNoAttach.Attachments.Count > 0 || mailNoAttach.AttachedCurrency > 0;
            bool hasAttach2 = mailWithItems.Attachments.Count > 0 || mailWithItems.AttachedCurrency > 0;
            bool hasAttach3 = mailWithCurrency.Attachments.Count > 0 || mailWithCurrency.AttachedCurrency > 0;

            Assert.False(hasAttach1);
            Assert.True(hasAttach2);
            Assert.True(hasAttach3);
        }

        #endregion

        #region GameEventType Mail Events Tests

        [Fact]
        public void GameEventType_MailEvents_HaveExpectedValues()
        {
            Assert.Equal(600, (int)GameEventType.MailSent);
            Assert.Equal(601, (int)GameEventType.MailReceived);
        }

        #endregion
    }
}

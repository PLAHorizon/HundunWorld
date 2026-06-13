using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Enums;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    internal class MessageService
    {
        private readonly MessageRepository _messageRepository;
        private readonly ImGatewayContactClient _imGatewayContactClient;
        private readonly SocialLinkPreviewService _previewService;
        private readonly ConcurrentDictionary<string, User> _gatewayUsersById;

        public MessageService(
            MessageRepository messageRepository,
            ImGatewayContactClient imGatewayContactClient,
            SocialLinkPreviewService previewService)
        {
            _messageRepository = messageRepository;
            _imGatewayContactClient = imGatewayContactClient;
            _previewService = previewService;
            _gatewayUsersById = new ConcurrentDictionary<string, User>(StringComparer.Ordinal);
        }

        public Horizon.Game.GengDi.Models.IMMessage SendMessage(
            string senderId,
            string receiverId,
            string content,
            MessageType type,
            bool isGroupConversation = false,
            string messageIdOverride = null,
            DateTime? timestampOverride = null,
            bool? isReadOverride = null)
        {
            var message = new Horizon.Game.GengDi.Models.IMMessage
            {
                Id = string.IsNullOrWhiteSpace(messageIdOverride) ? Guid.NewGuid().ToString() : messageIdOverride,
                SenderId = senderId,
                ReceiverId = receiverId,
                IsGroupConversation = isGroupConversation,
                Content = content,
                Timestamp = timestampOverride ?? DateTime.Now,
                IsRead = isReadOverride ?? false,
                Type = type
            };

            var existingMessage = _messageRepository.GetById(message.Id);
            if (existingMessage != null)
            {
                existingMessage.SenderId = message.SenderId;
                existingMessage.ReceiverId = message.ReceiverId;
                existingMessage.IsGroupConversation = message.IsGroupConversation;
                existingMessage.Content = message.Content;
                existingMessage.Timestamp = message.Timestamp;
                existingMessage.IsRead = existingMessage.IsRead || message.IsRead;
                existingMessage.Type = message.Type;
                _messageRepository.Update(existingMessage);
                return existingMessage;
            }

            _messageRepository.Add(message);
            return message;
        }

        public Task<Horizon.Game.GengDi.Models.IMMessage> SendMessageAsync(
            string senderId,
            string receiverId,
            string content,
            MessageType type,
            bool isGroupConversation = false,
            string messageIdOverride = null,
            DateTime? timestampOverride = null,
            bool? isReadOverride = null)
        {
            return ExecuteRepositoryAsync(() => SendMessage(
                senderId,
                receiverId,
                content,
                type,
                isGroupConversation,
                messageIdOverride,
                timestampOverride,
                isReadOverride));
        }

        /// <summary>
        /// 转发消息：使用已序列化的消息内容和原始消息类型，直接发送，不重新解析URL。
        /// </summary>
        public async Task<Horizon.Game.GengDi.Models.IMMessage> SendForwardedMessageAsync(
            string senderId,
            string receiverId,
            string serializedContent,
            MessageType contentType,
            bool isGroupConversation = false)
        {
            var useGroupTransport = ShouldUseGroupTransport(receiverId, isGroupConversation);
            if (!useGroupTransport && ShouldUseGatewayDirectChat(senderId, receiverId))
            {
                return await SendDirectMessageViaGatewayAsync(senderId, receiverId, serializedContent, contentType).ConfigureAwait(false);
            }

            if (useGroupTransport && ShouldUseGatewayGroupChat(senderId, receiverId))
            {
                return await SendGroupMessageViaGatewayAsync(senderId, receiverId, serializedContent, contentType).ConfigureAwait(false);
            }

            return await SendMessageAsync(senderId, receiverId, serializedContent, contentType, useGroupTransport).ConfigureAwait(false);
        }

        public async Task<Horizon.Game.GengDi.Models.IMMessage> SendComposedMessageAsync(
            string senderId,
            string receiverId,
            string draftText,
            string attachmentSource,
            MediaAttachmentType attachmentType,
            bool isGroupConversation = false)
        {
            RichMessageContent content;

            if (!string.IsNullOrWhiteSpace(attachmentSource) && attachmentType == MediaAttachmentType.Image)
            {
                var storedAsset = await Task.Run(() => LocalMediaStore.PersistAttachment(attachmentSource, attachmentType)).ConfigureAwait(false);
                _ = PreviewImageService.Instance.LoadAsync(storedAsset.PreviewPath);
                content = _previewService.CreateImageFromLocalPath(storedAsset.MediaPath, draftText, storedAsset.PreviewPath);
            }
            else if (!string.IsNullOrWhiteSpace(attachmentSource) && attachmentType == MediaAttachmentType.Video)
            {
                var storedAsset = await Task.Run(() => LocalMediaStore.PersistAttachment(attachmentSource, attachmentType)).ConfigureAwait(false);
                _ = PreviewImageService.Instance.LoadAsync(storedAsset.PreviewPath);
                content = _previewService.CreateVideoFromLocalPath(storedAsset.MediaPath, draftText, storedAsset.PreviewPath);
            }
            else if (!string.IsNullOrWhiteSpace(attachmentSource) && attachmentType == MediaAttachmentType.File)
            {
                var fileName = System.IO.Path.GetFileName(attachmentSource);
                var fileText = string.IsNullOrWhiteSpace(draftText)
                    ? $"[文件] {fileName}"
                    : $"[文件] {fileName} {draftText}";
                content = RichMessageContent.CreateText(fileText);
            }
            else if (SocialLinkParser.TryExtractFirstUrl(draftText, out var url, out var caption))
            {
                content = await _previewService.CreateFromUrlAsync(url, caption).ConfigureAwait(false);
            }
            else
            {
                content = RichMessageContent.CreateText(draftText ?? string.Empty);
            }

            var serializedContent = RichMessageContentSerializer.Serialize(content);
            var useGroupTransport = ShouldUseGroupTransport(receiverId, isGroupConversation);
            if (!useGroupTransport && ShouldUseGatewayDirectChat(senderId, receiverId))
            {
                return await SendDirectMessageViaGatewayAsync(senderId, receiverId, serializedContent, content.Type).ConfigureAwait(false);
            }

            if (useGroupTransport && ShouldUseGatewayGroupChat(senderId, receiverId))
            {
                return await SendGroupMessageViaGatewayAsync(senderId, receiverId, serializedContent, content.Type).ConfigureAwait(false);
            }

            return await SendMessageAsync(senderId, receiverId, serializedContent, content.Type, useGroupTransport).ConfigureAwait(false);
        }

        public List<Horizon.Game.GengDi.Models.IMMessage> GetMessages(string userId, string otherId, int limit = 50)
        {
            return _messageRepository.GetMessagesBetweenUsers(userId, otherId, limit);
        }

        public Task<List<Horizon.Game.GengDi.Models.IMMessage>> GetMessagesAsync(string userId, string otherId, int limit = 50)
        {
            return ExecuteRepositoryAsync(() => GetMessages(userId, otherId, limit));
        }

        public List<Horizon.Game.GengDi.Models.IMMessage> GetGroupMessages(string groupId, int limit = 50)
        {
            return _messageRepository.GetGroupMessages(groupId, limit);
        }

        public Task<List<Horizon.Game.GengDi.Models.IMMessage>> GetGroupMessagesAsync(string groupId, int limit = 50)
        {
            return ExecuteRepositoryAsync(() => GetGroupMessages(groupId, limit));
        }

        public Task<Horizon.Game.GengDi.Models.IMMessage> SaveIncomingGatewayPrivateMessageAsync(
            string currentUserId,
            IMPrivateChatNotifyMessage notify,
            bool markAsRead = false)
        {
            return ExecuteRepositoryAsync(() => SaveIncomingGatewayPrivateMessage(currentUserId, notify, markAsRead));
        }

        public Task<Horizon.Game.GengDi.Models.IMMessage> SaveIncomingGatewayGroupMessageAsync(
            string currentUserId,
            IMGroupChatNotifyMessage notify,
            bool markAsRead = false)
        {
            return ExecuteRepositoryAsync(() => SaveIncomingGatewayGroupMessage(currentUserId, notify, markAsRead));
        }

        public void MarkMessageAsRead(string messageId)
        {
            var message = _messageRepository.GetById(messageId);
            if (message != null)
            {
                message.IsRead = true;
                _messageRepository.Update(message);
            }
        }

        public Task MarkMessageAsReadAsync(string messageId)
        {
            return ExecuteRepositoryAsync(() => MarkMessageAsRead(messageId));
        }

        public void ClearConversation(string currentUserId, string peerId, bool isGroupConversation)
        {
            if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(peerId))
            {
                return;
            }

            var allMessages = _messageRepository.GetAll();
            var toDelete = isGroupConversation
                ? allMessages.Where(m => m.IsGroupConversation && string.Equals(m.ReceiverId, peerId, StringComparison.Ordinal)).ToList()
                : allMessages.Where(m => !m.IsGroupConversation &&
                    ((string.Equals(m.SenderId, currentUserId, StringComparison.Ordinal) && string.Equals(m.ReceiverId, peerId, StringComparison.Ordinal))
                    || (string.Equals(m.SenderId, peerId, StringComparison.Ordinal) && string.Equals(m.ReceiverId, currentUserId, StringComparison.Ordinal)))).ToList();

            foreach (var message in toDelete)
            {
                _messageRepository.Delete(message.Id);
            }

            Task.Run(() => CleanupLocalMediaForMessages(toDelete));
        }

        public Task ClearConversationAsync(string currentUserId, string peerId, bool isGroupConversation)
        {
            return ExecuteRepositoryAsync(() => ClearConversation(currentUserId, peerId, isGroupConversation));
        }

        public async Task ClearConversationWithArchiveAsync(string currentUserId, string peerId, bool isGroupConversation)
        {
            if (ShouldUseGatewayContacts(currentUserId))
            {
                await SendClearHistoryToGatewayAsync(currentUserId, peerId, isGroupConversation).ConfigureAwait(false);
            }

            await ClearConversationAsync(currentUserId, peerId, isGroupConversation).ConfigureAwait(false);
        }

        /// <summary>
        /// 从 IM 网关拉取指定私聊会话的服务端聊天记录，将本地缺失的消息持久化到 LiteDB，
        /// 并返回经过去重合并后的最终消息列表（按时间升序）。
        /// </summary>
        public async Task<List<Horizon.Game.GengDi.Models.IMMessage>> FetchAndPersistOfflineMessagesAsync(
            string currentUserId,
            string friendId,
            int fetchCount = 50)
        {
            if (!ImIdentity.TryResolveUserId(currentUserId, out var userId)
                || !ImIdentity.TryResolveUserId(friendId, out var peerUserId))
            {
                return await GetMessagesAsync(currentUserId, friendId).ConfigureAwait(false);
            }

            List<IMPrivateChatNotifyMessage> serverMessages = null;

            try
            {
                var response = await _imGatewayContactClient
                    .GetPrivateChatHistoryAsync(userId, peerUserId, fetchCount)
                    .ConfigureAwait(false);

                serverMessages = response?.PrivateMessages;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MessageService] 拉取服务端离线消息失败（将使用本地缓存）：{ex.Message}");
            }

            if (serverMessages != null && serverMessages.Count > 0)
            {
                foreach (var notify in serverMessages)
                {
                    if (notify.ReceiverId != userId)
                    {
                        continue;
                    }

                    try
                    {
                        await SaveIncomingGatewayPrivateMessageAsync(currentUserId, notify, markAsRead: false)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[MessageService] 持久化离线消息失败（ServerMessageId={notify.ServerMessageId}）：{ex.Message}");
                    }
                }
            }

            return await GetMessagesAsync(currentUserId, friendId).ConfigureAwait(false);
        }

        private Horizon.Game.GengDi.Models.IMMessage SaveIncomingGatewayPrivateMessage(
            string currentUserId,
            IMPrivateChatNotifyMessage notify,
            bool markAsRead)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);
            ArgumentNullException.ThrowIfNull(notify);

            var senderId = notify.SenderId.ToString();
            var messageId = string.IsNullOrWhiteSpace(notify.ServerMessageId)
                ? Guid.NewGuid().ToString("N")
                : notify.ServerMessageId;

            CacheGatewayPrivateSender(notify);

            var existingMessage = _messageRepository.GetById(messageId);
            if (existingMessage != null)
            {
                if (markAsRead && !existingMessage.IsRead)
                {
                    existingMessage.IsRead = true;
                    _messageRepository.Update(existingMessage);
                }

                return existingMessage;
            }

            var message = new Horizon.Game.GengDi.Models.IMMessage
            {
                Id = messageId,
                SenderId = senderId,
                ReceiverId = currentUserId,
                Content = notify.Content ?? string.Empty,
                Timestamp = notify.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(notify.Timestamp).LocalDateTime
                    : DateTime.Now,
                IsRead = markAsRead,
                Type = InferGatewayMessageType(notify.ContentType, notify.Content)
            };

            _messageRepository.Add(message);
            return message;
        }

        private Horizon.Game.GengDi.Models.IMMessage SaveIncomingGatewayGroupMessage(
            string currentUserId,
            IMGroupChatNotifyMessage notify,
            bool markAsRead)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);
            ArgumentNullException.ThrowIfNull(notify);

            var groupId = notify.GroupId.ToString();
            var messageId = string.IsNullOrWhiteSpace(notify.ServerMessageId)
                ? Guid.NewGuid().ToString("N")
                : notify.ServerMessageId;
            var existingMessage = _messageRepository.GetById(messageId);
            if (existingMessage != null)
            {
                if (markAsRead && !existingMessage.IsRead)
                {
                    existingMessage.IsRead = true;
                    _messageRepository.Update(existingMessage);
                }

                return existingMessage;
            }

            var message = new Models.IMMessage
            {
                Id = messageId,
                SenderId = notify.SenderId.ToString(),
                ReceiverId = groupId,
                IsGroupConversation = true,
                Content = notify.Content ?? string.Empty,
                Timestamp = notify.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(notify.Timestamp).LocalDateTime
                    : DateTime.Now,
                IsRead = markAsRead,
                Type = InferGatewayMessageType(notify.ContentType, notify.Content)
            };

            _messageRepository.Add(message);
            return message;
        }

        private async Task SendClearHistoryToGatewayAsync(string currentUserId, string peerId, bool isGroupConversation)
        {
            if (!ImIdentity.TryResolveUserId(currentUserId, out var currentGatewayId))
            {
                return;
            }

            if (!ulong.TryParse(peerId, out var peerGatewayId) || peerGatewayId == 0)
            {
                return;
            }

            try
            {
                await _imGatewayContactClient.ClearChatHistoryAsync(
                    currentGatewayId,
                    peerGatewayId,
                    isGroupConversation ? IMChatRelationType.Group : IMChatRelationType.Friend).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 服务端归档失败不阻止客户端本地清理
            }
        }

        private async Task<Horizon.Game.GengDi.Models.IMMessage> SendDirectMessageViaGatewayAsync(
            string senderId,
            string receiverId,
            string serializedContent,
            MessageType messageType)
        {
            if (!ImIdentity.TryResolveUserId(senderId, out var senderUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ImIdentity.TryResolveUserId(receiverId, out var receiverUserId))
            {
                throw new InvalidOperationException("当前好友没有有效通行证号，无法通过 IM 网关发送消息。");
            }

            var ack = await _imGatewayContactClient
                .SendPrivateChatAsync(senderUserId, receiverUserId, serializedContent, MapLocalMessageContentType(messageType))
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(ack.AckedMessageId))
            {
                throw new InvalidOperationException("IM 网关未返回有效消息确认。请稍后重试。");
            }

            return await SendMessageAsync(
                senderId,
                receiverId,
                serializedContent,
                messageType,
                isGroupConversation: false,
                messageIdOverride: ack.AckedMessageId,
                timestampOverride: ack.Timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ack.Timestamp).LocalDateTime : DateTime.Now,
                isReadOverride: true).ConfigureAwait(false);
        }

        private async Task<Horizon.Game.GengDi.Models.IMMessage> SendGroupMessageViaGatewayAsync(
            string senderId,
            string groupId,
            string serializedContent,
            MessageType messageType)
        {
            if (!ImIdentity.TryResolveUserId(senderId, out var senderUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ulong.TryParse(groupId, out var gatewayGroupId) || gatewayGroupId == 0)
            {
                throw new InvalidOperationException("当前群组没有有效网关群号，无法通过 IM 网关发送消息。");
            }

            var ack = await _imGatewayContactClient
                .SendGroupChatAsync(senderUserId, gatewayGroupId, serializedContent, MapLocalMessageContentType(messageType))
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(ack.AckedMessageId))
            {
                throw new InvalidOperationException("IM 网关未返回有效群消息确认。请稍后重试。");
            }

            return await SendMessageAsync(
                senderId,
                groupId,
                serializedContent,
                messageType,
                isGroupConversation: true,
                messageIdOverride: ack.AckedMessageId,
                timestampOverride: ack.Timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ack.Timestamp).LocalDateTime : DateTime.Now,
                isReadOverride: true).ConfigureAwait(false);
        }

        private void CacheGatewayPrivateSender(IMPrivateChatNotifyMessage notify)
        {
            var senderId = notify.SenderId.ToString();
            if (string.IsNullOrWhiteSpace(senderId))
            {
                return;
            }

            _gatewayUsersById.TryGetValue(senderId, out var existingUser);
            _gatewayUsersById[senderId] = new User
            {
                Id = senderId,
                PassportId = senderId,
                Username = string.IsNullOrWhiteSpace(notify.SenderName)
                    ? existingUser?.Username ?? senderId
                    : notify.SenderName,
                Avatar = string.IsNullOrWhiteSpace(notify.SenderAvatar)
                    ? existingUser?.Avatar ?? string.Empty
                    : notify.SenderAvatar,
                Bio = existingUser?.Bio ?? "已通过 IM 网关同步",
                Status = existingUser?.Status ?? UserStatus.Offline
            };
        }

        private static bool ShouldUseGroupTransport(string receiverId, bool isGroupConversation)
        {
            return isGroupConversation;
        }

        private bool ShouldUseGatewayDirectChat(string senderId, string receiverId)
        {
            return ImIdentity.TryResolveUserId(senderId, out _)
                && ImIdentity.TryResolveUserId(receiverId, out _);
        }

        private bool ShouldUseGatewayGroupChat(string senderId, string groupId)
        {
            return ImIdentity.TryResolveUserId(senderId, out _)
                && ulong.TryParse(groupId, out var parsedGroupId)
                && parsedGroupId > 0;
        }

        private bool ShouldUseGatewayContacts(string userId)
        {
            return ImIdentity.TryResolveUserId(userId, out _);
        }

        private static IMContentType MapLocalMessageContentType(MessageType messageType)
        {
            return messageType switch
            {
                MessageType.Image => IMContentType.Image,
                MessageType.Video => IMContentType.Video,
                MessageType.File => IMContentType.File,
                MessageType.System => IMContentType.System,
                MessageType.Emoji => IMContentType.Emoji,
                _ => IMContentType.Text
            };
        }

        private static MessageType InferGatewayMessageType(IMContentType contentType, string rawContent)
        {
            var inferredContent = RichMessageContentSerializer.Deserialize(MessageType.Text, rawContent);
            if (inferredContent.Type != MessageType.Text)
            {
                return inferredContent.Type;
            }

            return contentType switch
            {
                IMContentType.Image => MessageType.Image,
                IMContentType.Video => MessageType.Video,
                IMContentType.File => MessageType.File,
                IMContentType.System => MessageType.System,
                IMContentType.Emoji => MessageType.Emoji,
                _ => MessageType.Text
            };
        }

        private static void CleanupLocalMediaForMessages(IReadOnlyList<Horizon.Game.GengDi.Models.IMMessage> messages)
        {
            foreach (var message in messages)
            {
                if (message.Type != MessageType.Image && message.Type != MessageType.Video)
                {
                    continue;
                }

                var content = RichMessageContentSerializer.Deserialize(message);
                TryDeleteLocalFile(content.MediaUrl);
                TryDeleteLocalFile(content.PreviewImageUrl);

                foreach (var attachment in content.Attachments)
                {
                    TryDeleteLocalFile(attachment.MediaUrl);
                    TryDeleteLocalFile(attachment.PreviewImageUrl);
                }
            }
        }

        private static void TryDeleteLocalFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 无法删除本地文件不影响功能
            }
        }

        private static async Task ExecuteRepositoryAsync(Action action)
        {
            await ClientAsyncDispatcher.RunLiteDbAsync(action).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteRepositoryAsync<T>(Func<T> action)
        {
            return await ClientAsyncDispatcher.RunLiteDbAsync(action).ConfigureAwait(false);
        }
    }
}

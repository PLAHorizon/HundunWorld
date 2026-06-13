using Horizon.Core;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    internal static class IMChatRedisOutbox
    {
        private const string UserOutboxPrefix = "im:chat:outbox:user";
        private const string GroupOutboxPrefix = "im:chat:outbox:group";
        private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

        public static Task TryAppendUserChatRecordAsync(
            ILogger logger,
            ulong userId,
            IMChatRelationType relationType,
            ulong peerId,
            string conversationId,
            IMChatRecord record)
        {
            var envelope = new IMChatPersistenceEnvelope
            {
                Scope = relationType == IMChatRelationType.Stranger ? "user-stranger" : "user-friend",
                OwnerId = userId,
                PeerId = peerId,
                ConversationId = conversationId,
                ServerMessageId = record.ServerMessageId,
                ClientMessageId = record.ClientMessageId,
                SenderId = record.SenderId,
                SenderName = record.SenderName,
                ReceiverId = record.ReceiverId,
                Content = record.Content,
                ContentType = record.ContentType,
                Timestamp = record.Timestamp,
                Status = record.Status,
                PersistedAtUtc = DateTime.Now
            };

            var key = $"{UserOutboxPrefix}:{userId}:{relationType.ToString().ToLowerInvariant()}";
            return TryAppendAsync(logger, key, envelope);
        }

        public static Task TryAppendGroupChatRecordAsync(
            ILogger logger,
            ulong groupId,
            string groupName,
            IMGroupChatRecord record)
        {
            var envelope = new IMChatPersistenceEnvelope
            {
                Scope = "group",
                OwnerId = groupId,
                GroupId = groupId,
                GroupName = groupName,
                ConversationId = $"g_{groupId}",
                ServerMessageId = record.ServerMessageId,
                SenderId = record.SenderId,
                SenderName = record.SenderName,
                Content = record.Content,
                ContentType = record.ContentType,
                Timestamp = record.Timestamp,
                Status = record.Status,
                MentionedUserIds = record.MentionedUserIds ?? new List<ulong>(),
                MentionAll = record.MentionAll,
                PersistedAtUtc = DateTime.Now
            };

            var key = $"{GroupOutboxPrefix}:{groupId}";
            return TryAppendAsync(logger, key, envelope);
        }

        private static async Task TryAppendAsync(ILogger logger, string key, IMChatPersistenceEnvelope envelope)
        {
            var cache = Cache.Current;
            if (cache == null)
            {
                return;
            }

            try
            {
                await cache.EnqueueItemOnListAsync(key, envelope);
                await cache.ExpireEntryInAsync(key, DefaultRetention);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "写入 IM 聊天 Redis 队列失败: Key={Key}, Scope={Scope}, ServerMessageId={ServerMessageId}",
                    key,
                    envelope.Scope,
                    envelope.ServerMessageId);
            }
        }

        internal sealed class IMChatPersistenceEnvelope
        {
            public string Scope { get; set; } = string.Empty;

            public ulong OwnerId { get; set; }

            public ulong PeerId { get; set; }

            public ulong GroupId { get; set; }

            public string GroupName { get; set; } = string.Empty;

            public string ConversationId { get; set; } = string.Empty;

            public string ServerMessageId { get; set; } = string.Empty;

            public string ClientMessageId { get; set; } = string.Empty;

            public ulong SenderId { get; set; }

            public string SenderName { get; set; } = string.Empty;

            public ulong ReceiverId { get; set; }

            public string Content { get; set; } = string.Empty;

            public IMContentType ContentType { get; set; }

            public long Timestamp { get; set; }

            public IMMessageStatus Status { get; set; }

            public List<ulong> MentionedUserIds { get; set; } = new();

            public bool MentionAll { get; set; }

            public DateTime PersistedAtUtc { get; set; }
        }
    }
}
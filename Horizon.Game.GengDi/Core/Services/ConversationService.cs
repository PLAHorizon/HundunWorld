using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    internal class ConversationService
    {
        private readonly MessageRepository _messageRepository;
        private readonly ImGatewayContactClient _imGatewayContactClient;

        public ConversationService(
            MessageRepository messageRepository,
            ImGatewayContactClient imGatewayContactClient)
        {
            _messageRepository = messageRepository;
            _imGatewayContactClient = imGatewayContactClient;
        }

        public Task MarkConversationAsReadAsync(string currentUserId, string friendId)
        {
            return ExecuteRepositoryAsync(() => MarkConversationAsRead(currentUserId, friendId));
        }

        /// <summary>
        /// 向 IM 网关查询服务端存储的会话列表（含服务端侧未读计数），用于同步离线期间积累的未读消息数量。
        /// </summary>
        public async Task<IReadOnlyList<IMConversationInfo>> GetServerConversationListAsync(
            string currentUserId)
        {
            if (!ImIdentity.TryResolveUserId(currentUserId, out var userId))
            {
                return System.Array.Empty<IMConversationInfo>();
            }

            try
            {
                return await _imGatewayContactClient
                    .GetConversationListAsync(userId)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ConversationService] 获取服务端会话列表失败（将使用本地缓存）：{ex.Message}");
                return System.Array.Empty<IMConversationInfo>();
            }
        }

        /// <summary>
        /// 向 IM 网关发送私聊已读回执，重置当前用户与指定对端私聊会话在服务端侧的未读计数。
        /// </summary>
        public async Task SendReadReceiptToServerAsync(string currentUserId, string peerId)
        {
            if (!ImIdentity.TryResolveUserId(currentUserId, out var userId)
                || !ImIdentity.TryResolveUserId(peerId, out var peerUserId))
            {
                return;
            }

            try
            {
                await _imGatewayContactClient
                    .SendReadReceiptAsync(userId, peerUserId)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ConversationService] 发送已读回执失败：{ex.Message}");
            }
        }

        public Task<Dictionary<string, DirectConversationState>> GetDirectConversationStatesAsync(
            string currentUserId,
            IEnumerable<string> friendIds)
        {
            return ExecuteRepositoryAsync(() => GetDirectConversationStates(currentUserId, friendIds));
        }

        public Task MarkGroupConversationAsReadAsync(string currentUserId, string groupId)
        {
            return ExecuteRepositoryAsync(() => MarkGroupConversationAsRead(currentUserId, groupId));
        }

        public Task<Dictionary<string, GroupConversationState>> GetGroupConversationStatesAsync(IEnumerable<string> groupIds, string currentUserId)
        {
            return ExecuteRepositoryAsync(() => GetGroupConversationStates(groupIds, currentUserId));
        }

        private void MarkConversationAsRead(string currentUserId, string friendId)
        {
            if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(friendId))
            {
                return;
            }

            var unreadMessages = _messageRepository.GetAll()
                .Where(message => string.Equals(message.SenderId, friendId, StringComparison.Ordinal)
                    && string.Equals(message.ReceiverId, currentUserId, StringComparison.Ordinal)
                    && !message.IsRead)
                .ToList();

            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
                _messageRepository.Update(message);
            }
        }

        private void MarkGroupConversationAsRead(string currentUserId, string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var unreadMessages = _messageRepository.GetAll()
                .Where(message => message.IsGroupConversation
                    && string.Equals(message.ReceiverId, groupId, StringComparison.Ordinal)
                    && !string.Equals(message.SenderId, currentUserId, StringComparison.Ordinal)
                    && !message.IsRead)
                .ToList();

            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
                _messageRepository.Update(message);
            }
        }

        private Dictionary<string, DirectConversationState> GetDirectConversationStates(
            string currentUserId,
            IEnumerable<string> friendIds)
        {
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return new Dictionary<string, DirectConversationState>(StringComparer.Ordinal);
            }

            var friendIdSet = new HashSet<string>(
                friendIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            if (friendIdSet.Count == 0)
            {
                return new Dictionary<string, DirectConversationState>(StringComparer.Ordinal);
            }

            return _messageRepository.GetAll()
                .Where(message => message != null)
                .Where(message =>
                    (string.Equals(message.SenderId, currentUserId, StringComparison.Ordinal)
                        && friendIdSet.Contains(message.ReceiverId))
                    || (string.Equals(message.ReceiverId, currentUserId, StringComparison.Ordinal)
                        && friendIdSet.Contains(message.SenderId)))
                .GroupBy(
                    message => string.Equals(message.SenderId, currentUserId, StringComparison.Ordinal)
                        ? message.ReceiverId
                        : message.SenderId,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => new DirectConversationState
                    {
                        LatestMessage = group.OrderByDescending(message => message.Timestamp).First(),
                        UnreadCount = group.Count(message =>
                            string.Equals(message.ReceiverId, currentUserId, StringComparison.Ordinal)
                            && !message.IsRead)
                    },
                    StringComparer.Ordinal);
        }

        private Dictionary<string, GroupConversationState> GetGroupConversationStates(IEnumerable<string> groupIds, string currentUserId)
        {
            var groupIdSet = new HashSet<string>(
                groupIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            if (groupIdSet.Count == 0)
            {
                return new Dictionary<string, GroupConversationState>(StringComparer.Ordinal);
            }

            return _messageRepository.GetAll()
                .Where(message => message.IsGroupConversation && groupIdSet.Contains(message.ReceiverId))
                .GroupBy(message => message.ReceiverId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => new GroupConversationState
                    {
                        LatestMessage = group.OrderByDescending(message => message.Timestamp).First(),
                        UnreadCount = group.Count(message =>
                            !string.Equals(message.SenderId, currentUserId, StringComparison.Ordinal)
                            && !message.IsRead)
                    },
                    StringComparer.Ordinal);
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

    public sealed class DirectConversationState
    {
        public Horizon.Game.GengDi.Models.IMMessage LatestMessage { get; set; }

        public int UnreadCount { get; set; }
    }

    public sealed class GroupConversationState
    {
        public Horizon.Game.GengDi.Models.IMMessage LatestMessage { get; set; }

        public int UnreadCount { get; set; }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Enums;
using Horizon.IM.Message;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    public class SocialService
    {
        private readonly UserRepository _userRepository;
        private readonly MessageRepository _messageRepository;
        private readonly GroupRepository _groupRepository;
        private readonly SocialLinkPreviewService _previewService;
        private readonly ImGatewayContactClient _imGatewayContactClient;
        private readonly ConcurrentDictionary<string, User> _gatewayUsersById;

        private static readonly DemoContactSeed[] DemoContacts =
        {
            new("苍穹领航员", "navigator@horizon.local", "保持编队，优先共享战术视频。", UserStatus.Online),
            new("夜港侦查员", "scout@horizon.local", "我会把抖音和 B 站情报先丢到频道里。", UserStatus.Away),
            new("轨道分析师", "analyst@horizon.local", "负责整理链接卡片、战报和资源图集。", UserStatus.Busy),
            new("星门后勤官", "logistics@horizon.local", "补给、下载包和安装资源都走我这里。", UserStatus.Online),
            new("远征新闻台", "newsdesk@horizon.local", "主流视频站的新热视频会自动整理成卡片。", UserStatus.Online)
        };

        public SocialService()
        {
            _userRepository = new UserRepository();
            _messageRepository = new MessageRepository();
            _groupRepository = new GroupRepository();
            _previewService = new SocialLinkPreviewService();
            _imGatewayContactClient = new ImGatewayContactClient();
            _gatewayUsersById = new ConcurrentDictionary<string, User>(StringComparer.Ordinal);
        }

        internal ImGatewayContactClient GatewayClient => _imGatewayContactClient;

        public Task EnsureDemoSocialGraphAsync(string userId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return Task.CompletedTask;
            }

            return ExecuteRepositoryAsync(() => EnsureDemoSocialGraph(userId));
        }

        public void EnsureDemoSocialGraph(string userId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var currentUser = GetLocalUser(userId);
            if (currentUser == null)
            {
                return;
            }

            var demoUsers = EnsureDemoContacts();
            var hasExistingSocialState = currentUser.Friends.Count > 0
                || _groupRepository.GetAll().Any(group => group.Members.Contains(currentUser.Id))
                || _messageRepository.GetAll().Any(message =>
                    string.Equals(message.SenderId, currentUser.Id, StringComparison.Ordinal) ||
                    string.Equals(message.ReceiverId, currentUser.Id, StringComparison.Ordinal));

            if (hasExistingSocialState)
            {
                return;
            }

            foreach (var demoUser in demoUsers.Take(4))
            {
                AcceptFriendRequest(currentUser.Id, demoUser.Id);
            }

            EnsureDemoGroup(currentUser, demoUsers.Take(3).ToList());
        }

        // 好友管理
        public bool SendFriendRequest(string senderId, string receiverUsername)
        {
            var receiver = _userRepository.GetByUsername(receiverUsername?.Trim());
            receiver ??= GetLocalUser(receiverUsername?.Trim());
            if (receiver == null)
            {
                return false;
            }

            var sender = GetLocalUser(senderId);
            if (sender == null || MatchesUserKey(receiver, senderId))
            {
                return false;
            }

            if (sender.Friends.Contains(receiver.Id))
            {
                return false;
            }

            return AcceptFriendRequest(senderId, receiver.Id);
        }

        public Task<bool> SendFriendRequestAsync(string senderId, string receiverUsername)
        {
            if (ShouldUseGatewayContacts(senderId))
            {
                return SendFriendRequestViaGatewayAsync(senderId, receiverUsername);
            }

            return ExecuteRepositoryAsync(() => SendFriendRequest(senderId, receiverUsername));
        }

        public bool AcceptFriendRequest(string userId, string friendId)
        {
            var user = GetLocalUser(userId);
            var friend = GetLocalUser(friendId);

            if (user == null || friend == null)
            {
                return false;
            }

            var userFriends = user.Friends;
            if (!userFriends.Contains(friend.Id))
            {
                userFriends.Add(friend.Id);
                user.Friends = userFriends;
                _userRepository.Update(user);
            }

            var friendFriends = friend.Friends;
            if (!friendFriends.Contains(user.Id))
            {
                friendFriends.Add(user.Id);
                friend.Friends = friendFriends;
                _userRepository.Update(friend);
            }

            return true;
        }

        public bool RejectFriendRequest(string userId, string requesterId)
        {
            return true;
        }

        public Task<bool> AcceptFriendRequestAsync(string userId, string friendId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return HandleFriendRequestViaGatewayAsync(userId, friendId, accept: true);
            }

            return ExecuteRepositoryAsync(() => AcceptFriendRequest(userId, friendId));
        }

        public Task<bool> RejectFriendRequestAsync(string userId, string requesterId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return HandleFriendRequestViaGatewayAsync(userId, requesterId, accept: false);
            }

            return ExecuteRepositoryAsync(() => RejectFriendRequest(userId, requesterId));
        }

        public bool RemoveFriend(string userId, string friendId)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return false;
            }

            var friend = GetLocalUser(friendId);
            var resolvedFriendId = friend?.Id ?? friendId;

            var userFriends = user.Friends;
            if (userFriends.Contains(resolvedFriendId))
            {
                userFriends.Remove(resolvedFriendId);
                user.Friends = userFriends;
                _userRepository.Update(user);
            }

            if (friend != null)
            {
                var friendFriends = friend.Friends;
                if (friendFriends.Contains(user.Id))
                {
                    friendFriends.Remove(user.Id);
                    friend.Friends = friendFriends;
                    _userRepository.Update(friend);
                }
            }

            return true;
        }

        public Task<bool> RemoveFriendAsync(string userId, string friendId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return RemoveFriendViaGatewayAsync(userId, friendId);
            }

            return ExecuteRepositoryAsync(() => RemoveFriend(userId, friendId));
        }

        /// <summary>
        /// 邀请好友入群。
        /// </summary>
        public async Task<IMGroupJoinResponse> InviteToGroupAsync(string userId, string groupId, List<string> friendIds)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ulong.TryParse(groupId, out var groupIdValue))
            {
                throw new InvalidOperationException("无效的群组 ID。");
            }

            var inviteeIds = new List<ulong>();
            foreach (var fid in friendIds)
            {
                if (ImIdentity.TryResolveUserId(fid, out var uid))
                {
                    inviteeIds.Add(uid);
                }
            }

            if (inviteeIds.Count == 0)
            {
                throw new InvalidOperationException("没有有效的被邀请用户。");
            }

            var response = await _imGatewayContactClient
                .InviteToGroupAsync(currentUserId, groupIdValue, inviteeIds)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Message) ? "邀请入群失败。" : response.Message);
            }

            return response;
        }

        /// <summary>
        /// 响应入群邀请。
        /// </summary>
        public async Task RespondToGroupInviteAsync(string userId, ulong groupId, bool accept)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            var response = await _imGatewayContactClient
                .RespondToGroupInviteAsync(currentUserId, groupId, accept)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Message) ? "处理入群邀请失败。" : response.Message);
            }
        }

        /// <summary>
        /// 从服务端拉取当前用户待处理的入群邀请列表（支持离线恢复）。
        /// </summary>
        public async Task<IReadOnlyList<IMUserPendingGroupInviteEntry>> GetPendingGroupInvitesAsync(string userId)
        {
            if (!ShouldUseGatewayContacts(userId))
            {
                return Array.Empty<IMUserPendingGroupInviteEntry>();
            }

            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                return Array.Empty<IMUserPendingGroupInviteEntry>();
            }

            return await _imGatewayContactClient
                .GetPendingGroupInvitesAsync(currentUserId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 群主审批由非群主成员发起的入群邀请。
        /// </summary>
        public async Task ReviewGroupInviteApprovalAsync(string reviewerId, ulong groupId, ulong inviteeId, bool approve)
        {
            if (!ImIdentity.TryResolveUserId(reviewerId, out var reviewerUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            var response = await _imGatewayContactClient
                .ReviewGroupInviteApprovalAsync(reviewerUserId, groupId, inviteeId, approve)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Message) ? "审批入群邀请失败。" : response.Message);
            }
        }

        /// <summary>
        /// 群主拉取指定群组的待审批邀请列表（重连后恢复离线漏接的审批通知）。
        /// </summary>
        public async Task<IReadOnlyList<IMGroupInviteApprovalNotify>> GetPendingInviteApprovalsAsync(
            string ownerId,
            ulong groupId)
        {
            if (!ShouldUseGatewayContacts(ownerId))
                return Array.Empty<IMGroupInviteApprovalNotify>();

            if (!ImIdentity.TryResolveUserId(ownerId, out var ownerUserId))
                return Array.Empty<IMGroupInviteApprovalNotify>();

            return await _imGatewayContactClient
                .GetPendingInviteApprovalsAsync(ownerUserId, groupId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 当前用户退出群组（非群主）。服务端从群成员列表中移除该用户，
        /// 本地也同步移除该用户的群成员身份。
        /// </summary>
        public async Task LeaveGroupAsync(string userId, string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("群组 ID 不能为空。", nameof(groupId));
            }

            if (ImIdentity.TryResolveUserId(userId, out var currentUserId)
                && ulong.TryParse(groupId, out var groupIdValue))
            {
                var response = await _imGatewayContactClient
                    .LeaveGroupAsync(currentUserId, groupIdValue)
                    .ConfigureAwait(false);

                if (!response.Success)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(response.Message) ? "退出群组失败。" : response.Message);
                }
            }

            await ExecuteRepositoryAsync(() =>
            {
                RemoveMemberFromGroup(groupId, userId);
                return true;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 群主解散群组。服务端将群置为已解散并通知其他成员；本地将群标记为已解散，
        /// 但保留成员列表与缓存消息，以便用户后续仍可查看历史消息。
        /// </summary>
        public async Task DisbandGroupAsync(string ownerId, string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                throw new ArgumentException("群组 ID 不能为空。", nameof(groupId));
            }

            if (ImIdentity.TryResolveUserId(ownerId, out var ownerUserId)
                && ulong.TryParse(groupId, out var groupIdValue))
            {
                var response = await _imGatewayContactClient
                    .DisbandGroupAsync(ownerUserId, groupIdValue)
                    .ConfigureAwait(false);

                if (!response.Success)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(response.Message) ? "解散群组失败。" : response.Message);
                }
            }

            await ExecuteRepositoryAsync(() =>
            {
                MarkGroupDisbandedLocalCore(groupId);
                return true;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 将本地群组标记为已解散（保留成员与消息缓存）。
        /// </summary>
        public Task MarkGroupDisbandedLocalAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return Task.CompletedTask;
            }

            return ExecuteRepositoryAsync(() =>
            {
                MarkGroupDisbandedLocalCore(groupId);
                return true;
            });
        }

        private void MarkGroupDisbandedLocalCore(string groupId)
        {
            var group = _groupRepository.GetById(groupId);
            if (group == null || group.IsDisbanded)
            {
                return;
            }

            group.IsDisbanded = true;
            _groupRepository.Update(group);
        }

        /// <summary>
        /// 彻底删除本地群组记录（用于已解散群的清理）。
        /// 删除后群组名称释放，数据不可恢复。
        /// </summary>
        public Task DeleteGroupAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return Task.CompletedTask;
            }

            return ExecuteRepositoryAsync(() =>
            {
                var group = _groupRepository.GetById(groupId);
                if (group == null)
                {
                    return true;
                }

                if (!group.IsDisbanded)
                {
                    throw new InvalidOperationException("只能删除已解散的群组。");
                }

                _groupRepository.Delete(groupId);
                return true;
            });
        }

        public List<User> GetFriends(string userId)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return new List<User>();
            }

            return user.Friends
                .Select(friendId => _userRepository.GetById(friendId))
                .Where(friend => friend != null)
                .OrderByDescending(friend => friend.IsAvailable)
                .ThenBy(friend => friend.Username)
                .ToList();
        }

        public Task<List<User>> GetFriendsAsync(string userId)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return GetFriendsViaGatewayAsync(userId);
            }

            return ExecuteRepositoryAsync(() => GetFriends(userId));
        }

        public List<User> GetSuggestedFriends(string userId, int limit = 5)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return new List<User>();
            }

            var excludedIds = new HashSet<string>(user.Friends, StringComparer.Ordinal)
            {
                userId
            };

            return FilterSuggestedFriends(_userRepository.GetAll(), userId, excludedIds)
                .OrderByDescending(candidate => candidate.IsAvailable)
                .ThenBy(candidate => candidate.Username)
                .Take(limit)
                .ToList();
        }

        public static IEnumerable<User> FilterSuggestedFriends(
            IEnumerable<User> allUsers,
            string currentUserId,
            IEnumerable<string> existingFriendIds)
        {
            var excludedIds = new HashSet<string>(existingFriendIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal)
            {
                currentUserId ?? string.Empty
            };

            return (allUsers ?? Enumerable.Empty<User>())
                .Where(candidate => candidate != null)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Id) && !excludedIds.Contains(candidate.Id))
                .Where(candidate => !LooksLikeTemporaryTestUser(candidate));
        }

        public Task<List<User>> GetSuggestedFriendsAsync(string userId, int limit = 5)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return GetPendingFriendRequestsViaGatewayAsync(userId, limit);
            }

            return ExecuteRepositoryAsync(() => GetSuggestedFriends(userId, limit));
        }

        public User GetUserById(string userId)
        {
            return TryGetGatewayUser(userId, out var gatewayUser)
                ? gatewayUser
                : GetLocalUser(userId);
        }

        public Task<User> GetUserByIdAsync(string userId)
        {
            return ExecuteRepositoryAsync(() => GetUserById(userId));
        }

        public Task<List<User>> GetUsersByIdsAsync(IEnumerable<string> userIds)
        {
            return ExecuteRepositoryAsync(() =>
            {
                var distinctIds = userIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                if (distinctIds.Count == 0)
                {
                    return new List<User>();
                }

                return distinctIds
                    .Select(id => TryGetGatewayUser(id, out var gatewayUser) ? gatewayUser : GetLocalUser(id))
                    .Where(user => user != null)
                    .ToList();
            });
        }

        // 聊天功能
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
            IReadOnlyList<PendingSocialAttachment> attachments,
            bool isGroupConversation = false)
        {
            RichMessageContent content;
            var imageAttachments = attachments?.Where(a => a.Type == MediaAttachmentType.Image).ToList() ?? new List<PendingSocialAttachment>();
            var videoAttachment = attachments?.FirstOrDefault(a => a.Type == MediaAttachmentType.Video);
            var fileAttachment = attachments?.FirstOrDefault(a => a.Type == MediaAttachmentType.File);

            if (imageAttachments.Count > 0)
            {
                var firstStored = await Task.Run(() => LocalMediaStore.PersistAttachment(imageAttachments[0].SourcePath, MediaAttachmentType.Image)).ConfigureAwait(false);
                _ = PreviewImageService.Instance.LoadAsync(firstStored.PreviewPath);

                content = _previewService.CreateImageFromLocalPath(firstStored.MediaPath, draftText, firstStored.PreviewPath);
                content.Attachments.Add(new MessageAttachment
                {
                    MediaUrl = firstStored.MediaPath,
                    PreviewImageUrl = firstStored.PreviewPath,
                    AttachmentType = "image"
                });

                for (var i = 1; i < imageAttachments.Count; i++)
                {
                    var stored = await Task.Run(() => LocalMediaStore.PersistAttachment(imageAttachments[i].SourcePath, MediaAttachmentType.Image)).ConfigureAwait(false);
                    _ = PreviewImageService.Instance.LoadAsync(stored.PreviewPath);
                    content.Attachments.Add(new MessageAttachment
                    {
                        MediaUrl = stored.MediaPath,
                        PreviewImageUrl = stored.PreviewPath,
                        AttachmentType = "image"
                    });
                }
            }
            else if (videoAttachment != null)
            {
                var storedAsset = await Task.Run(() => LocalMediaStore.PersistAttachment(videoAttachment.SourcePath, MediaAttachmentType.Video)).ConfigureAwait(false);
                _ = PreviewImageService.Instance.LoadAsync(storedAsset.PreviewPath);
                content = _previewService.CreateVideoFromLocalPath(storedAsset.MediaPath, draftText, storedAsset.PreviewPath);
            }
            else if (fileAttachment != null)
            {
                content = RichMessageContent.CreateFile(fileAttachment.SourcePath, draftText);
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

        public async Task<Horizon.Game.GengDi.Models.IMMessage> SendEmojiAsync(
            string senderId,
            string receiverId,
            string emoji,
            bool isGroupConversation = false)
        {
            var emojiId = EmojiRegistry.GetEmojiId(emoji);
            if (emojiId < 0)
            {
                throw new ArgumentException($"未知表情：{emoji}", nameof(emoji));
            }

            var contentId = EmojiRegistry.BuildContentId(emojiId);
            return await SendForwardedMessageAsync(senderId, receiverId, contentId, MessageType.Emoji, isGroupConversation).ConfigureAwait(false);
        }

        public List<Horizon.Game.GengDi.Models.IMMessage> GetMessages(string userId, string otherId, int limit = 50)
        {
            return _messageRepository.GetMessagesBetweenUsers(userId, otherId, limit);
        }

        public Task<List<Horizon.Game.GengDi.Models.IMMessage>> GetMessagesAsync(string userId, string otherId, int limit = 50)
        {
            return ExecuteRepositoryAsync(() => GetMessages(userId, otherId, limit));
        }

        public Task<Horizon.Game.GengDi.Models.IMMessage> SaveIncomingGatewayPrivateMessageAsync(
            string currentUserId,
            IMPrivateChatNotifyMessage notify,
            bool markAsRead = false)
        {
            return ExecuteRepositoryAsync(() => SaveIncomingGatewayPrivateMessage(currentUserId, notify, markAsRead));
        }

        public Task MarkConversationAsReadAsync(string currentUserId, string friendId)
        {
            return ExecuteRepositoryAsync(() => MarkConversationAsRead(currentUserId, friendId));
        }

        /// <summary>
        /// 向 IM 网关查询服务端存储的会话列表（含服务端侧未读计数），用于同步离线期间积累的未读消息数量。
        /// 失败时静默返回空列表，不影响主流程。
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
                    $"[SocialService] 获取服务端会话列表失败（将使用本地缓存）：{ex.Message}");
                return System.Array.Empty<IMConversationInfo>();
            }
        }

        /// <summary>
        /// 从 IM 网关拉取指定私聊会话的服务端聊天记录，将本地缺失的消息持久化到 LiteDB，
        /// 并返回经过去重合并后的最终消息列表（按时间升序）。
        /// 用于上线后补齐离线期间积累的消息，对于本地已存在的消息不会重复写入。
        /// 失败时静默忽略，回退到本地缓存，不影响主流程。
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
                    $"[SocialService] 拉取服务端离线消息失败（将使用本地缓存）：{ex.Message}");
            }

            if (serverMessages != null && serverMessages.Count > 0)
            {
                foreach (var notify in serverMessages)
                {
                    // 仅持久化当前用户作为接收方的消息；发送方的消息在本地已记录为发出消息，无需再次写入。
                    if (notify.ReceiverId != userId)
                    {
                        continue;
                    }

                    try
                    {
                        // SaveIncomingGatewayPrivateMessageAsync 内部已通过 GetById 对消息 ID 去重，无需预扫描。
                        await SaveIncomingGatewayPrivateMessageAsync(currentUserId, notify, markAsRead: false)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[SocialService] 持久化离线消息失败（ServerMessageId={notify.ServerMessageId}）：{ex.Message}");
                    }
                }
            }

            return await GetMessagesAsync(currentUserId, friendId).ConfigureAwait(false);
        }

        /// <summary>
        /// 向 IM 网关发送私聊已读回执，重置当前用户与指定对端私聊会话在服务端侧的未读计数。当前实现仅适用于私聊会话，不包含群聊未读计数重置。
        /// 失败时静默忽略，不影响主流程。
        /// </summary>
        public async Task<IMContactGroupUpdateResponse> UpdateContactGroupAsync(
            string currentUserId,
            string action,
            string groupName,
            string newGroupName,
            List<ulong> contactUserIds)
        {
            if (!ImIdentity.TryResolveUserId(currentUserId, out var userId))
                throw new InvalidOperationException("当前用户缺少有效通行证。");

            var request = new IMContactGroupUpdateRequest
            {
                UserId = userId,
                Action = action,
                GroupName = groupName ?? string.Empty,
                NewGroupName = newGroupName ?? string.Empty,
                ContactUserIds = contactUserIds ?? new List<ulong>()
            };

            return await _imGatewayContactClient.UpdateContactGroupAsync(request);
        }

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
                    $"[SocialService] 发送已读回执失败：{ex.Message}");
            }
        }

        public Task<Dictionary<string, DirectConversationState>> GetDirectConversationStatesAsync(
            string currentUserId,
            IEnumerable<string> friendIds)
        {
            return ExecuteRepositoryAsync(() => GetDirectConversationStates(currentUserId, friendIds));
        }

        public Task<Horizon.Game.GengDi.Models.IMMessage> SaveIncomingGatewayGroupMessageAsync(
            string currentUserId,
            IMGroupChatNotifyMessage notify,
            bool markAsRead = false)
        {
            return ExecuteRepositoryAsync(() => SaveIncomingGatewayGroupMessage(currentUserId, notify, markAsRead));
        }

        public Task MarkGroupConversationAsReadAsync(string currentUserId, string groupId)
        {
            return ExecuteRepositoryAsync(() => MarkGroupConversationAsRead(currentUserId, groupId));
        }

        public Task<Dictionary<string, GroupConversationState>> GetGroupConversationStatesAsync(IEnumerable<string> groupIds, string currentUserId)
        {
            return ExecuteRepositoryAsync(() => GetGroupConversationStates(groupIds, currentUserId));
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

            return _messageRepository.GetDirectConversationStates(currentUserId, friendIds)
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

        private Horizon.Game.GengDi.Models.IMMessage SaveIncomingGatewayGroupMessage(
            string currentUserId,
            IMGroupChatNotifyMessage notify,
            bool markAsRead)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);
            ArgumentNullException.ThrowIfNull(notify);

            var groupId = notify.GroupId.ToString();
            EnsureGatewayGroupProjection(currentUserId, groupId, notify.GroupName);
            CacheGatewayPrivateSender(new IMPrivateChatNotifyMessage
            {
                SenderId = notify.SenderId,
                SenderName = notify.SenderName,
                SenderAvatar = notify.SenderAvatar
            });

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

        private Dictionary<string, GroupConversationState> GetGroupConversationStates(IEnumerable<string> groupIds, string currentUserId)
        {
            var groupIdSet = new HashSet<string>(
                groupIds?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            if (groupIdSet.Count == 0)
            {
                return new Dictionary<string, GroupConversationState>(StringComparer.Ordinal);
            }

            return _messageRepository.GetGroupConversationStates(groupIds)
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

        // 群聊功能
        public Group CreateGroup(string creatorId, string groupName)
        {
            var group = new Group
            {
                Id = Guid.NewGuid().ToString(),
                Name = groupName,
                CreatorId = creatorId,
                Description = "在这里共享图片、视频、本地素材和主流站点链接卡片。",
                CreatedAt = DateTime.Now
            };

            group.Members = new List<string> { creatorId };

            _groupRepository.Add(group);
            SyncUserGroupMembership(creatorId, group.Id, true);
            return group;
        }

        public Task<Group> CreateGroupAsync(string creatorId, string groupName)
        {
            // 检查当前用户是否已经拥有同名群组（存在本地的话）。同名则提示用户修改群名。
            var trimmedName = groupName?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedName))
            {
                var existingGroup = GetUserGroups(creatorId)
                    .FirstOrDefault(g => string.Equals(g?.Name, trimmedName, StringComparison.Ordinal));
                if (existingGroup != null)
                {
                    throw new InvalidOperationException(
                        $"群组已存在(同名)「{trimmedName}」，已帮您更新到它。若这不是你想要的请修改群名。");
                }
            }

            if (ImIdentity.TryResolveUserId(creatorId, out var creatorUserId))
            {
                return CreateGroupViaGatewayAsync(creatorId, creatorUserId, groupName);
            }

            return ExecuteRepositoryAsync(() => CreateGroup(creatorId, groupName));
        }

        private async Task<Group> CreateGroupViaGatewayAsync(string creatorId, ulong creatorUserId, string groupName)
        {
            var response = await _imGatewayContactClient
                .CreateGroupAsync(creatorUserId, groupName)
                .ConfigureAwait(false);

            if (!response.Success || response.GroupId == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(response.Message) ? "通过 IM 网关创建群组失败。" : response.Message);
            }

            var groupId = response.GroupId.ToString();
            return await ExecuteRepositoryAsync(() =>
            {
                var existing = _groupRepository.GetById(groupId);
                if (existing != null)
                {
                    return existing;
                }

                var group = new Group
                {
                    Id = groupId,
                    Name = groupName,
                    CreatorId = creatorId,
                    Description = "在这里共享图片、视频、本地素材和主流站点链接卡片。",
                    CreatedAt = DateTime.Now
                };
                group.Members = new List<string> { creatorId };
                _groupRepository.Add(group);
                SyncUserGroupMembership(creatorId, group.Id, true);
                return group;
            }).ConfigureAwait(false);
        }

        public bool AddMemberToGroup(string groupId, string userId)
        {
            var group = _groupRepository.GetById(groupId);
            if (group == null)
            {
                return false;
            }

            var members = group.Members;
            if (!members.Contains(userId))
            {
                members.Add(userId);
                group.Members = members;
                _groupRepository.Update(group);
                SyncUserGroupMembership(userId, groupId, true);
            }

            return true;
        }

        public Task<bool> AddMemberToGroupAsync(string groupId, string userId)
        {
            return ExecuteRepositoryAsync(() => AddMemberToGroup(groupId, userId));
        }

        public bool RemoveMemberFromGroup(string groupId, string userId)
        {
            var group = _groupRepository.GetById(groupId);
            if (group == null)
            {
                return false;
            }

            var members = group.Members;
            if (members.Contains(userId))
            {
                members.Remove(userId);
                group.Members = members;
                _groupRepository.Update(group);
                SyncUserGroupMembership(userId, groupId, false);
            }

            return true;
        }

        public Task<bool> RemoveMemberFromGroupAsync(string groupId, string userId)
        {
            return ExecuteRepositoryAsync(() => RemoveMemberFromGroup(groupId, userId));
        }

        public List<Horizon.Game.GengDi.Models.IMMessage> GetGroupMessages(string groupId, int limit = 50)
        {
            return _messageRepository.GetGroupMessages(groupId, limit);
        }

        public Task<List<   Horizon.Game.GengDi.Models.IMMessage>> GetGroupMessagesAsync(string groupId, int limit = 50)
        {
            return ExecuteRepositoryAsync(() => GetGroupMessages(groupId, limit));
        }

        public List<Group> GetUserGroups(string userId)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return new List<Group>();
            }

            var membershipKey = ResolvePreferredUserKey(user, userId);

            var groups = _groupRepository.GetAll()
                .Where(group => group.Members.Contains(membershipKey))
                .OrderBy(group => group.Name)
                .ToList();

            user.Groups = groups.Select(group => group.Id).ToList();
            _userRepository.Update(user);
            return groups;
        }

        public Task<List<Group>> GetUserGroupsAsync(string userId)
        {
            return ExecuteRepositoryAsync(() => GetUserGroups(userId));
        }

        public async Task SyncGroupMemberCountsAsync(string userId, IEnumerable<Group> groups)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
                return;

            foreach (var group in groups)
            {
                if (string.IsNullOrWhiteSpace(group.Id) || !ulong.TryParse(group.Id, out var groupId))
                    continue;

                try
                {
                    var count = await _imGatewayContactClient.GetGroupMemberCountAsync(currentUserId, groupId).ConfigureAwait(false);
                    if (count > 0)
                        group.ServerMemberCount = count;
                }
                catch
                {
                }
            }
        }

        public async Task<List<IMGroupMemberInfo>> GetGroupMembersAsync(string userId, string groupId)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
                return new List<IMGroupMemberInfo>();

            if (!ulong.TryParse(groupId, out var numericGroupId))
                return new List<IMGroupMemberInfo>();

            try
            {
                return await _imGatewayContactClient.GetGroupMemberListAsync(currentUserId, numericGroupId).ConfigureAwait(false);
            }
            catch
            {
                return new List<IMGroupMemberInfo>();
            }
        }

        // 在线状态管理
        public void UpdateUserStatus(string userId, UserStatus status)
        {
            var user = GetLocalUser(userId);
            if (user != null)
            {
                user.Status = status;
                _userRepository.Update(user);
            }
        }

        public Task UpdateUserStatusAsync(string userId, UserStatus status)
        {
            return ExecuteRepositoryAsync(() => UpdateUserStatus(userId, status));
        }

        public UserStatus GetUserStatus(string userId)
        {
            var user = GetLocalUser(userId);
            return user?.Status ?? UserStatus.Offline;
        }

        public Task<UserStatus> GetUserStatusAsync(string userId)
        {
            return ExecuteRepositoryAsync(() => GetUserStatus(userId));
        }

        // 个人资料管理
        public bool UpdateProfile(string userId, string bio, string avatar)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return false;
            }

            user.Bio = bio;
            user.Avatar = avatar;
            _userRepository.Update(user);
            return true;
        }

        public Task<bool> UpdateProfileAsync(string userId, string bio, string avatar)
        {
            return ExecuteRepositoryAsync(() => UpdateProfile(userId, bio, avatar));
        }

        public User GetUserProfile(string userId)
        {
            return GetLocalUser(userId);
        }

        public Task<User> GetUserProfileAsync(string userId)
        {
            return ExecuteRepositoryAsync(() => GetUserProfile(userId));
        }

        private bool ShouldUseGatewayContacts(string userId)
        {
            return ImIdentity.TryResolveUserId(userId, out _);
        }

        private async Task<bool> SendFriendRequestViaGatewayAsync(string senderId, string targetIdentity)
        {
            if (!ImIdentity.TryResolveUserId(senderId, out var senderUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ImIdentity.TryResolveUserId(targetIdentity, out var targetUserId))
            {
                throw new InvalidOperationException("请输入对方通行证号。当前网关联系人链路按 PassportId 发起好友申请。");
            }

            var response = await _imGatewayContactClient.AddContactAsync(senderUserId, targetUserId).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                    ? "发送好友申请失败。"
                    : response.Message);
            }

            return true;
        }

        private async Task<bool> RemoveFriendViaGatewayAsync(string userId, string friendId)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ImIdentity.TryResolveUserId(friendId, out var targetUserId))
            {
                throw new InvalidOperationException("当前联系人没有有效通行证号，无法从网关移除。");
            }

            var response = await _imGatewayContactClient.RemoveContactAsync(currentUserId, targetUserId).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                    ? "移除好友失败。"
                    : response.Message);
            }

            _gatewayUsersById.TryRemove(friendId, out _);
            return true;
        }

        private async Task<bool> HandleFriendRequestViaGatewayAsync(string userId, string requesterId, bool accept)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            if (!ImIdentity.TryResolveUserId(requesterId, out var requesterUserId))
            {
                throw new InvalidOperationException("当前好友申请缺少有效通行证号，无法处理。");
            }

            var response = await _imGatewayContactClient
                .HandleContactRequestAsync(currentUserId, requesterUserId, accept)
                .ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                    ? "处理好友申请失败。"
                    : response.Message);
            }

            return true;
        }

        private async Task<List<User>> GetFriendsViaGatewayAsync(string userId)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            var contacts = await _imGatewayContactClient.GetContactListAsync(currentUserId).ConfigureAwait(false);
            var friends = contacts
                .Select(MapGatewayContact)
                .OrderByDescending(friend => friend.IsAvailable)
                .ThenBy(friend => friend.Username)
                .ToList();

            CacheGatewayUsers(friends);
            return friends;
        }

        private async Task<List<User>> GetPendingFriendRequestsViaGatewayAsync(string userId, int limit)
        {
            if (!ImIdentity.TryResolveUserId(userId, out var currentUserId))
            {
                throw new InvalidOperationException("当前用户缺少有效通行证。请重新登录后再试。");
            }

            var pendingRequests = await _imGatewayContactClient
                .GetPendingContactRequestsAsync(currentUserId, limit)
                .ConfigureAwait(false);

            var pendingUsers = pendingRequests
                .Select(MapGatewayPendingRequest)
                .ToList();

            CacheGatewayUsers(pendingUsers);
            return pendingUsers;
        }

        private void CacheGatewayUsers(IEnumerable<User> users)
        {
            foreach (var user in users.Where(user => user != null && !string.IsNullOrWhiteSpace(user.Id)))
            {
                _gatewayUsersById[user.Id] = user;
            }
        }

        private bool TryGetGatewayUser(string userId, out User user)
        {
            var currentUser = App.CurrentUser;
            var currentPassportId = ImIdentity.ResolvePassportId(currentUser);
            if (currentUser != null && string.Equals(currentPassportId, userId, StringComparison.Ordinal))
            {
                user = CreateCurrentUserProjection(currentUser, currentPassportId);
                return true;
            }

            return _gatewayUsersById.TryGetValue(userId, out user);
        }

        private User GetLocalUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return _userRepository.GetById(userId) ?? _userRepository.GetByPassportId(userId);
        }

        private static bool MatchesUserKey(User user, string userId)
        {
            return user != null
                && ((!string.IsNullOrWhiteSpace(user.Id) && string.Equals(user.Id, userId, StringComparison.Ordinal))
                    || (!string.IsNullOrWhiteSpace(user.PassportId) && string.Equals(user.PassportId, userId, StringComparison.Ordinal)));
        }

        private static string ResolvePreferredUserKey(User user, string requestedKey)
        {
            if (user == null)
            {
                return requestedKey ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(requestedKey))
            {
                var trimmedKey = requestedKey.Trim();
                if (string.Equals(user.Id, trimmedKey, StringComparison.Ordinal)
                    || string.Equals(user.PassportId, trimmedKey, StringComparison.Ordinal))
                {
                    return trimmedKey;
                }
            }

            return user.Id ?? string.Empty;
        }

        private static User MapGatewayContact(IMContactInfo contact)
        {
            var passportId = contact.UserId.ToString();

            return new User
            {
                Id = passportId,
                PassportId = passportId,
                Username = passportId,
                Nickname = contact.Nickname ?? string.Empty,
                RemarkName = contact.RemarkName ?? string.Empty,
                Avatar = contact.Avatar ?? string.Empty,
                Bio = "已通过 IM 网关同步",
                Status = MapGatewayStatus(contact.OnlineStatus),
                GroupName = contact.GroupName ?? string.Empty
            };
        }

        private User MapGatewayPendingRequest(IMPendingContactRequest request)
        {
            var passportId = request.RequesterId.ToString();
            _gatewayUsersById.TryGetValue(passportId, out var cachedUser);

            var displayName = string.IsNullOrWhiteSpace(request.RequesterName)
                ? cachedUser?.Username ?? passportId
                : request.RequesterName;

            return new User
            {
                Id = passportId,
                PassportId = passportId,
                Username = displayName,
                Avatar = cachedUser?.Avatar ?? string.Empty,
                Bio = BuildPendingRequestSummary(request),
                Status = cachedUser?.Status ?? UserStatus.Offline
            };
        }

        private static string BuildPendingRequestSummary(IMPendingContactRequest request)
        {
            var timestampText = request.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(request.Timestamp).LocalDateTime.ToString("MM-dd HH:mm")
                : string.Empty;

            // 3天过期提示（客户端兜底显示）
            var expiryHint = string.Empty;
            if (request.Timestamp > 0)
            {
                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - request.Timestamp;
                var remainingHours = (3 * 24) - (elapsedMs / (1000.0 * 60 * 60));
                if (remainingHours <= 0)
                {
                    expiryHint = " · 已过期";
                }
                else if (remainingHours <= 24)
                {
                    expiryHint = $" · {remainingHours:F0}小时后过期";
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Message) && !string.IsNullOrWhiteSpace(timestampText))
            {
                return $"附言：{request.Message} · {timestampText}{expiryHint}";
            }

            if (!string.IsNullOrWhiteSpace(request.Message))
            {
                return $"附言：{request.Message}{expiryHint}";
            }

            if (!string.IsNullOrWhiteSpace(timestampText))
            {
                return $"于 {timestampText} 发来好友申请{expiryHint}";
            }

            return $"发来了一条好友申请{expiryHint}";
        }

        private static User CreateCurrentUserProjection(User currentUser, string passportId)
        {
            return new User
            {
                Id = passportId,
                PassportId = string.IsNullOrWhiteSpace(currentUser.PassportId) ? passportId : currentUser.PassportId,
                Username = currentUser.Username,
                Email = currentUser.Email,
                Avatar = currentUser.Avatar,
                Bio = currentUser.Bio,
                Status = currentUser.Status
            };
        }

        private static UserStatus MapGatewayStatus(IMOnlineStatus onlineStatus)
        {
            return onlineStatus switch
            {
                IMOnlineStatus.Online => UserStatus.Online,
                IMOnlineStatus.Away => UserStatus.Away,
                IMOnlineStatus.Busy => UserStatus.Busy,
                IMOnlineStatus.Invisible => UserStatus.Invisible,
                _ => UserStatus.Offline
            };
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

        /// <summary>
        /// 确保指定群组在本地数据库中存在，并将当前用户加入其成员列表。
        /// 用于处理群组邀请通知时同步本地群组数据，使群组列表能即时显示新加入的群组。
        /// 通过 <see cref="ClientAsyncDispatcher"/> 序列化执行，保证与其他 LiteDB 操作互斥。
        /// </summary>
        public Task EnsureGroupInLocalDatabaseAsync(string currentUserId, string groupId, string groupName)
        {
            return ExecuteRepositoryAsync(() => EnsureGatewayGroupProjection(currentUserId, groupId, groupName));
        }

        private void EnsureGatewayGroupProjection(string currentUserId, string groupId, string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var existingGroup = _groupRepository.GetById(groupId);
            if (existingGroup == null)
            {
                var group = new Group
                {
                    Id = groupId,
                    Name = string.IsNullOrWhiteSpace(groupName) ? $"群聊 {groupId}" : groupName,
                    Description = "已通过 IM 网关同步",
                    Icon = string.Empty,
                    CreatorId = string.Empty,
                    CreatedAt = DateTime.Now
                };
                group.Members = new List<string> { currentUserId };
                _groupRepository.Add(group);
                SyncUserGroupMembership(currentUserId, groupId, true);
                return;
            }

            var changed = false;
            if (string.IsNullOrWhiteSpace(existingGroup.Name) && !string.IsNullOrWhiteSpace(groupName))
            {
                existingGroup.Name = groupName;
                changed = true;
            }

            var members = existingGroup.Members;
            if (!members.Contains(currentUserId))
            {
                members.Add(currentUserId);
                existingGroup.Members = members;
                changed = true;
            }

            if (changed)
            {
                _groupRepository.Update(existingGroup);
                SyncUserGroupMembership(currentUserId, groupId, true);
            }
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

        private static bool LooksLikeTemporaryTestUser(User candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            var username = candidate.Username?.Trim() ?? string.Empty;
            var email = candidate.Email?.Trim() ?? string.Empty;

            return username.StartsWith("im_smoke_", StringComparison.OrdinalIgnoreCase)
                || username.StartsWith("im_numeric_", StringComparison.OrdinalIgnoreCase)
                || email.EndsWith("@local.test", StringComparison.OrdinalIgnoreCase);
        }

        private List<User> EnsureDemoContacts()
        {
            var allUsers = _userRepository.GetAll();
            var demoUsers = new List<User>();

            foreach (var seed in DemoContacts)
            {
                var existingUser = allUsers.FirstOrDefault(user =>
                    string.Equals(user.Username, seed.Username, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(user.Email, seed.Email, StringComparison.OrdinalIgnoreCase));

                if (existingUser == null)
                {
                    existingUser = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Username = seed.Username,
                        Email = seed.Email,
                        PasswordHash = Guid.NewGuid().ToString("N"),
                        Status = seed.Status,
                        Avatar = string.Empty,
                        Bio = seed.Bio
                    };
                    _userRepository.Add(existingUser);
                    allUsers.Add(existingUser);
                }
                else
                {
                    existingUser.Status = seed.Status;
                    if (string.IsNullOrWhiteSpace(existingUser.Bio))
                    {
                        existingUser.Bio = seed.Bio;
                    }
                    _userRepository.Update(existingUser);
                }

                demoUsers.Add(existingUser);
            }

            return demoUsers;
        }

        private void EnsureDemoGroup(User currentUser, IReadOnlyList<User> demoMembers)
        {
            if (string.IsNullOrWhiteSpace(currentUser?.Id))
            {
                return;
            }

            var existingGroup = _groupRepository.GetAll()
                .FirstOrDefault(group =>
                    string.Equals(group.Name, "远征前线", StringComparison.OrdinalIgnoreCase) &&
                    group.Members.Contains(currentUser.Id));

            if (existingGroup == null)
            {
                existingGroup = CreateGroup(currentUser.Id, "远征前线");
                existingGroup.Description = "用于分享主流视频站情报、组队邀请和素材卡片。";
                _groupRepository.Update(existingGroup);
            }

            foreach (var demoMember in demoMembers)
            {
                AddMemberToGroup(existingGroup.Id, demoMember.Id);
            }
        }

        private void SyncUserGroupMembership(string userId, string groupId, bool includeGroup)
        {
            var user = GetLocalUser(userId);
            if (user == null)
            {
                return;
            }

            var groups = user.Groups;
            if (includeGroup)
            {
                if (!groups.Contains(groupId))
                {
                    groups.Add(groupId);
                    user.Groups = groups;
                    _userRepository.Update(user);
                }

                return;
            }

            if (groups.Contains(groupId))
            {
                groups.Remove(groupId);
                user.Groups = groups;
                _userRepository.Update(user);
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

        private sealed record DemoContactSeed(string Username, string Email, string Bio, UserStatus Status);
    }
}
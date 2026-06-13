using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    internal class FriendService
    {
        private readonly UserRepository _userRepository;
        private readonly ImGatewayContactClient _imGatewayContactClient;
        private readonly ConcurrentDictionary<string, User> _gatewayUsersById;

        public FriendService(
            UserRepository userRepository,
            ImGatewayContactClient imGatewayContactClient)
        {
            _userRepository = userRepository;
            _imGatewayContactClient = imGatewayContactClient;
            _gatewayUsersById = new ConcurrentDictionary<string, User>(StringComparer.Ordinal);
        }

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

        public Task<List<User>> GetSuggestedFriendsAsync(string userId, int limit = 5)
        {
            if (ShouldUseGatewayContacts(userId))
            {
                return GetPendingFriendRequestsViaGatewayAsync(userId, limit);
            }

            return ExecuteRepositoryAsync(() => GetSuggestedFriends(userId, limit));
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

        private bool RejectFriendRequest(string userId, string requesterId)
        {
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
                GroupName = contact.GroupName ?? string.Empty,
                Avatar = contact.Avatar ?? string.Empty,
                Bio = "已通过 IM 网关同步",
                Status = MapGatewayStatus(contact.OnlineStatus)
            };
        }

        private User MapGatewayPendingRequest(IMPendingContactRequest request)
        {
            var passportId = request.RequesterId.ToString();
            _gatewayUsersById.TryGetValue(passportId, out var cachedUser);

            return new User
            {
                Id = passportId,
                PassportId = passportId,
                Username = passportId,
                Nickname = string.IsNullOrWhiteSpace(request.RequesterName) ? cachedUser?.Nickname ?? string.Empty : request.RequesterName,
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

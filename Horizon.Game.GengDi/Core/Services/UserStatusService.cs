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
    internal class UserStatusService
    {
        private readonly UserRepository _userRepository;
        private readonly ConcurrentDictionary<string, User> _gatewayUsersById;

        public UserStatusService(UserRepository userRepository)
        {
            _userRepository = userRepository;
            _gatewayUsersById = new ConcurrentDictionary<string, User>(StringComparer.Ordinal);
        }

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

        public User GetUserProfile(string userId)
        {
            return GetLocalUser(userId);
        }

        public Task<User> GetUserProfileAsync(string userId)
        {
            return ExecuteRepositoryAsync(() => GetUserProfile(userId));
        }

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

        public void HandleContactOnlineStatus(IMContactOnlineStatusMessage statusMessage)
        {
            if (statusMessage == null || statusMessage.UserId == 0)
            {
                return;
            }

            var passportId = statusMessage.UserId.ToString();

            if (_gatewayUsersById.TryGetValue(passportId, out var existingUser))
            {
                existingUser.Status = MapGatewayStatus(statusMessage.OnlineStatus);
            }
            else
            {
                var newUser = new User
                {
                    Id = passportId,
                    PassportId = passportId,
                    Username = passportId,
                    Avatar = string.Empty,
                    Bio = "已通过 IM 网关同步",
                    Status = MapGatewayStatus(statusMessage.OnlineStatus)
                };

                _gatewayUsersById[passportId] = newUser;
            }
        }

        public void HandleContactProfileUpdate(IMContactProfileUpdateMessage updateMessage)
        {
            if (updateMessage == null || updateMessage.UserId == 0)
            {
                return;
            }

            var passportId = updateMessage.UserId.ToString();
            var newUser = new User
            {
                Id = passportId,
                PassportId = passportId,
                Username = string.IsNullOrWhiteSpace(updateMessage.Nickname)
                    ? passportId
                    : updateMessage.Nickname,
                Avatar = updateMessage.Avatar ?? string.Empty,
                Bio = string.IsNullOrWhiteSpace(updateMessage.Bio)
                    ? "已通过 IM 网关同步"
                    : updateMessage.Bio,
                Status = _gatewayUsersById.TryGetValue(passportId, out var cachedUser)
                    ? cachedUser.Status
                    : UserStatus.Online
            };

            _gatewayUsersById[passportId] = newUser;
        }

        public User TryGetGatewayUser(string userId)
        {
            var currentUser = App.CurrentUser;
            var currentPassportId = ImIdentity.ResolvePassportId(currentUser);
            if (currentUser != null && string.Equals(currentPassportId, userId, StringComparison.Ordinal))
            {
                return CreateCurrentUserProjection(currentUser, currentPassportId);
            }

            return _gatewayUsersById.TryGetValue(userId, out var user) ? user : null;
        }

        private User GetLocalUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return _userRepository.GetById(userId) ?? _userRepository.GetByPassportId(userId);
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

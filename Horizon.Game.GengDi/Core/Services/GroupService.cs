using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Enums;
using Horizon.IM.Message;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    internal class GroupService
    {
        private readonly GroupRepository _groupRepository;
        private readonly UserRepository _userRepository;
        private readonly ImGatewayContactClient _imGatewayContactClient;

        public GroupService(
            GroupRepository groupRepository,
            UserRepository userRepository,
            ImGatewayContactClient imGatewayContactClient)
        {
            _groupRepository = groupRepository;
            _userRepository = userRepository;
            _imGatewayContactClient = imGatewayContactClient;
        }

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

        /// <summary>
        /// 邀请好友入群。
        /// </summary>
        public async Task InviteToGroupAsync(string userId, string groupId, List<string> friendIds)
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

        /// <summary>
        /// 确保指定群组在本地数据库中存在，并将当前用户加入其成员列表。
        /// </summary>
        public Task EnsureGroupInLocalDatabaseAsync(string currentUserId, string groupId, string groupName)
        {
            return ExecuteRepositoryAsync(() => EnsureGatewayGroupProjection(currentUserId, groupId, groupName));
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

        private User GetLocalUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return _userRepository.GetById(userId) ?? _userRepository.GetByPassportId(userId);
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

        private bool ShouldUseGatewayContacts(string userId)
        {
            return ImIdentity.TryResolveUserId(userId, out _);
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

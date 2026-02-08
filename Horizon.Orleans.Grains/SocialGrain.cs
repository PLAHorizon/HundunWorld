using Horizon.Game.Message.Enums;
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
    /// 社交系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SocialState
    {
        /// <summary>
        /// 好友列表（好友ID -> 好友信息）
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<Guid, FriendInfo> Friends { get; set; } = new();

        /// <summary>
        /// 好友申请列表（申请ID -> 申请信息）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<Guid, FriendRequest> FriendRequests { get; set; } = new();

        /// <summary>
        /// 聊天历史（频道类型 -> 消息列表）
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public Dictionary<int, List<ChatMessage>> ChatHistory { get; set; } = new();

        /// <summary>
        /// 黑名单
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public HashSet<Guid> BlockedPlayers { get; set; } = new();

        /// <summary>
        /// 最大好友数量
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int MaxFriends { get; set; } = 100;

        /// <summary>
        /// 每个频道最大缓存消息数量
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxChatHistoryPerChannel { get; set; } = 200;
    }

    /// <summary>
    /// 好友申请信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class FriendRequest
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid RequestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid RequesterId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public Guid TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(4)]
        [Id(4)]
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 社交系统Grain实现 - 负责好友、聊天、社交管理
    /// </summary>
    public class SocialGrain : Grain, ISocialGrain
    {
        private const int MaxChatMessageLength = 2000;
        private const int MaxFriendRequestMessageLength = 200;
        private const int MaxBatchChannels = 20;
        private const int MaxBatchCountPerChannel = 100;

        private readonly ILogger<SocialGrain> _logger;
        private readonly IPersistentState<SocialState> _socialState;

        public SocialGrain(
            ILogger<SocialGrain> logger,
            [PersistentState("social", "GameStore")] IPersistentState<SocialState> socialState)
        {
            _logger = logger;
            _socialState = socialState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SocialGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_socialState.State.Friends == null)
                _socialState.State.Friends = new Dictionary<Guid, FriendInfo>();

            if (_socialState.State.FriendRequests == null)
                _socialState.State.FriendRequests = new Dictionary<Guid, FriendRequest>();

            if (_socialState.State.ChatHistory == null)
                _socialState.State.ChatHistory = new Dictionary<int, List<ChatMessage>>();

            if (_socialState.State.BlockedPlayers == null)
                _socialState.State.BlockedPlayers = new HashSet<Guid>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> SendChatMessageAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("发送聊天消息: Channel={Channel}, Sender={SenderId}",
                    message.ChannelType, message.SenderId);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("聊天消息内容为空");
                    return false;
                }

                if (message.Content.Length > MaxChatMessageLength)
                {
                    _logger.LogWarning("聊天消息内容过长: Length={Length}", message.Content.Length);
                    return false;
                }

                if (string.IsNullOrEmpty(message.MessageId))
                {
                    message.MessageId = Guid.NewGuid().ToString();
                }

                if (message.Timestamp == 0)
                {
                    message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                // 存储到聊天历史
                var channelKey = (int)message.ChannelType;
                if (!_socialState.State.ChatHistory.TryGetValue(channelKey, out var history))
                {
                    history = new List<ChatMessage>();
                    _socialState.State.ChatHistory[channelKey] = history;
                }

                history.Add(message);

                // 限制历史消息数量
                if (history.Count > _socialState.State.MaxChatHistoryPerChannel)
                {
                    history.RemoveRange(0, history.Count - _socialState.State.MaxChatHistoryPerChannel);
                }

                await _socialState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送聊天消息失败");
                throw;
            }
        }

        public Task<List<ChatMessage>> GetChatHistoryAsync(int channelType, int count = 50)
        {
            try
            {
                if (count <= 0) count = 50;
                if (count > MaxBatchCountPerChannel) count = MaxBatchCountPerChannel;

                if (!_socialState.State.ChatHistory.TryGetValue(channelType, out var history))
                {
                    return Task.FromResult(new List<ChatMessage>());
                }

                var result = history
                    .OrderByDescending(m => m.Timestamp)
                    .Take(count)
                    .OrderBy(m => m.Timestamp)
                    .ToList();

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取聊天历史失败: ChannelType={ChannelType}", channelType);
                throw;
            }
        }

        public async Task<bool> ReceiveChatMessageAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("接收聊天消息: MessageId={MessageId}, Sender={SenderId}",
                    message.MessageId, message.SenderId);

                var channelKey = (int)message.ChannelType;
                if (!_socialState.State.ChatHistory.TryGetValue(channelKey, out var history))
                {
                    history = new List<ChatMessage>();
                    _socialState.State.ChatHistory[channelKey] = history;
                }

                history.Add(message);

                if (history.Count > _socialState.State.MaxChatHistoryPerChannel)
                {
                    history.RemoveRange(0, history.Count - _socialState.State.MaxChatHistoryPerChannel);
                }

                await _socialState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接收聊天消息失败");
                throw;
            }
        }

        public Task<Dictionary<ChatChannel, List<ChatMessage>>> GetBatchChatHistoryAsync(
            List<ChatChannel> channels, int countPerChannel = 20)
        {
            try
            {
                if (countPerChannel <= 0) countPerChannel = 20;
                if (countPerChannel > MaxBatchCountPerChannel) countPerChannel = MaxBatchCountPerChannel;

                var result = new Dictionary<ChatChannel, List<ChatMessage>>();

                // 限制批量查询的频道数量
                var channelsToQuery = channels.Take(MaxBatchChannels);

                foreach (var channel in channelsToQuery)
                {
                    var channelKey = (int)channel;
                    if (_socialState.State.ChatHistory.TryGetValue(channelKey, out var history))
                    {
                        result[channel] = history
                            .OrderByDescending(m => m.Timestamp)
                            .Take(countPerChannel)
                            .OrderBy(m => m.Timestamp)
                            .ToList();
                    }
                    else
                    {
                        result[channel] = new List<ChatMessage>();
                    }
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量获取聊天历史失败");
                throw;
            }
        }

        public async Task<bool> AddFriendAsync(Guid friendId)
        {
            try
            {
                var state = _socialState.State;

                if (state.Friends.ContainsKey(friendId))
                {
                    _logger.LogWarning("好友已存在: FriendId={FriendId}", friendId);
                    return false;
                }

                if (state.Friends.Count >= state.MaxFriends)
                {
                    _logger.LogWarning("好友列表已满: Count={Count}, Max={Max}",
                        state.Friends.Count, state.MaxFriends);
                    return false;
                }

                if (state.BlockedPlayers.Contains(friendId))
                {
                    _logger.LogWarning("目标玩家在黑名单中: FriendId={FriendId}", friendId);
                    return false;
                }

                var friendInfo = new FriendInfo
                {
                    FriendId = GuidToUInt64(friendId),
                    IsOnline = false,
                    Intimacy = 0,
                    LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                state.Friends[friendId] = friendInfo;
                await _socialState.WriteStateAsync();

                _logger.LogInformation("添加好友成功: FriendId={FriendId}", friendId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加好友失败: FriendId={FriendId}", friendId);
                throw;
            }
        }

        public async Task<bool> RemoveFriendAsync(Guid friendId)
        {
            try
            {
                var state = _socialState.State;

                if (!state.Friends.Remove(friendId))
                {
                    _logger.LogWarning("好友不存在: FriendId={FriendId}", friendId);
                    return false;
                }

                await _socialState.WriteStateAsync();
                _logger.LogInformation("删除好友成功: FriendId={FriendId}", friendId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除好友失败: FriendId={FriendId}", friendId);
                throw;
            }
        }

        public Task<List<FriendInfo>> GetFriendsAsync()
        {
            try
            {
                var friends = _socialState.State.Friends.Values.ToList();
                return Task.FromResult(friends);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取好友列表失败");
                throw;
            }
        }

        public async Task<bool> SendFriendRequestAsync(Guid targetId, string message)
        {
            try
            {
                var state = _socialState.State;

                if (state.Friends.ContainsKey(targetId))
                {
                    _logger.LogWarning("目标已是好友: TargetId={TargetId}", targetId);
                    return false;
                }

                // 检查是否已有发给该玩家的申请
                if (state.FriendRequests.Values.Any(r => r.RequesterId == this.GetPrimaryKey() && r.TargetId == targetId))
                {
                    _logger.LogWarning("已存在发给该玩家的好友申请: TargetId={TargetId}", targetId);
                    return false;
                }

                var requestMessage = message ?? "";
                if (requestMessage.Length > MaxFriendRequestMessageLength)
                {
                    requestMessage = requestMessage[..MaxFriendRequestMessageLength];
                }

                var request = new FriendRequest
                {
                    RequestId = Guid.NewGuid(),
                    RequesterId = this.GetPrimaryKey(),
                    TargetId = targetId,
                    Message = requestMessage,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                state.FriendRequests[request.RequestId] = request;
                await _socialState.WriteStateAsync();

                _logger.LogInformation("发送好友申请成功: TargetId={TargetId}", targetId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送好友申请失败: TargetId={TargetId}", targetId);
                throw;
            }
        }

        public async Task<bool> HandleFriendRequestAsync(Guid requestId, bool accept)
        {
            try
            {
                var state = _socialState.State;

                if (!state.FriendRequests.TryGetValue(requestId, out var request))
                {
                    _logger.LogWarning("好友申请不存在: RequestId={RequestId}", requestId);
                    return false;
                }

                state.FriendRequests.Remove(requestId);

                if (accept)
                {
                    if (state.Friends.Count >= state.MaxFriends)
                    {
                        _logger.LogWarning("好友列表已满，无法接受申请");
                        await _socialState.WriteStateAsync();
                        return false;
                    }

                    var friendInfo = new FriendInfo
                    {
                        FriendId = GuidToUInt64(request.TargetId),
                        IsOnline = false,
                        Intimacy = 0,
                        LastLoginTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    state.Friends[request.TargetId] = friendInfo;
                    _logger.LogInformation("接受好友申请: RequestId={RequestId}, FriendId={FriendId}",
                        requestId, request.TargetId);
                }
                else
                {
                    _logger.LogInformation("拒绝好友申请: RequestId={RequestId}", requestId);
                }

                await _socialState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理好友申请失败: RequestId={RequestId}", requestId);
                throw;
            }
        }

        public Task OnFriendAddedAsync(long friendId)
        {
            _logger.LogInformation("好友被添加回调: FriendId={FriendId}", friendId);
            return Task.CompletedTask;
        }

        public Task OnFriendRemovedAsync(long friendId)
        {
            _logger.LogInformation("好友被移除回调: FriendId={FriendId}", friendId);
            return Task.CompletedTask;
        }

        public Task OnFriendRequestHandledAsync(Guid requestId, bool accepted)
        {
            _logger.LogInformation("好友申请被处理回调: RequestId={RequestId}, Accepted={Accepted}",
                requestId, accepted);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 将Guid确定性转换为ulong（使用前8个字节）
        /// </summary>
        private static ulong GuidToUInt64(Guid guid)
        {
            return BitConverter.ToUInt64(guid.ToByteArray(), 0);
        }
    }
}

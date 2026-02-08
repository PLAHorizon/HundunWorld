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
    #region MessageChannelGrain

    /// <summary>
    /// 消息频道Grain实现 - 通用消息频道（世界频道、附近频道等）
    /// </summary>
    public class MessageChannelGrain : Grain, IMessageChannelGrain
    {
        private const int MaxMessageContentLength = 2000;

        private readonly ILogger<MessageChannelGrain> _logger;
        private readonly IPersistentState<MessageChannelState> _channelState;

        public MessageChannelGrain(
            ILogger<MessageChannelGrain> logger,
            [PersistentState("messageChannel", "GameStore")] IPersistentState<MessageChannelState> channelState)
        {
            _logger = logger;
            _channelState = channelState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MessageChannelGrain {GrainKey} activating.", this.GetPrimaryKeyString());

            if (_channelState.State.Subscribers == null)
                _channelState.State.Subscribers = new HashSet<long>();

            if (_channelState.State.RecentMessages == null)
                _channelState.State.RecentMessages = new List<ChatMessage>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> BroadcastMessageAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("频道广播消息: Channel={ChannelKey}, SenderId={SenderId}",
                    this.GetPrimaryKeyString(), message.SenderId);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("消息内容为空");
                    return false;
                }

                if (message.Content.Length > MaxMessageContentLength)
                {
                    _logger.LogWarning("消息内容过长: Length={Length}", message.Content.Length);
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

                var state = _channelState.State;
                state.RecentMessages.Add(message);
                state.TotalMessageCount++;

                // 限制缓存消息数量
                if (state.RecentMessages.Count > state.MaxCachedMessages)
                {
                    state.RecentMessages.RemoveRange(0, state.RecentMessages.Count - state.MaxCachedMessages);
                }

                await _channelState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "频道广播消息失败");
                throw;
            }
        }

        public async Task<bool> SubscribeAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Subscribers.Add(playerId))
                {
                    _logger.LogWarning("玩家已订阅频道: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("玩家订阅频道: PlayerId={PlayerId}, Channel={Channel}",
                    playerId, this.GetPrimaryKeyString());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅频道失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> UnsubscribeAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Subscribers.Remove(playerId))
                {
                    _logger.LogWarning("玩家未订阅频道: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("玩家取消订阅频道: PlayerId={PlayerId}, Channel={Channel}",
                    playerId, this.GetPrimaryKeyString());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订阅频道失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<List<long>> GetSubscribersAsync()
        {
            try
            {
                return Task.FromResult(_channelState.State.Subscribers.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订阅者列表失败");
                throw;
            }
        }
    }

    #endregion

    #region GuildChannelGrain

    /// <summary>
    /// 公会频道Grain实现 - 公会成员专属频道
    /// </summary>
    public class GuildChannelGrain : Grain, IGuildChannelGrain
    {
        private const int MaxMessageContentLength = 2000;

        private readonly ILogger<GuildChannelGrain> _logger;
        private readonly IPersistentState<GroupChannelState> _channelState;

        public GuildChannelGrain(
            ILogger<GuildChannelGrain> logger,
            [PersistentState("guildChannel", "GameStore")] IPersistentState<GroupChannelState> channelState)
        {
            _logger = logger;
            _channelState = channelState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GuildChannelGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_channelState.State.Members == null)
                _channelState.State.Members = new HashSet<long>();

            if (_channelState.State.RecentMessages == null)
                _channelState.State.RecentMessages = new List<ChatMessage>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> BroadcastToMembersAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("公会频道广播: GuildId={GuildId}, SenderId={SenderId}",
                    this.GetPrimaryKey(), message.SenderId);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("消息内容为空");
                    return false;
                }

                if (message.Content.Length > MaxMessageContentLength)
                {
                    _logger.LogWarning("消息内容过长: Length={Length}", message.Content.Length);
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

                var state = _channelState.State;
                state.RecentMessages.Add(message);

                if (state.RecentMessages.Count > state.MaxCachedMessages)
                {
                    state.RecentMessages.RemoveRange(0, state.RecentMessages.Count - state.MaxCachedMessages);
                }

                await _channelState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "公会频道广播失败");
                throw;
            }
        }

        public async Task<bool> AddMemberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Members.Add(playerId))
                {
                    _logger.LogWarning("玩家已在公会频道中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("添加公会频道成员: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加公会频道成员失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> RemoveMemberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Members.Remove(playerId))
                {
                    _logger.LogWarning("玩家不在公会频道中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("移除公会频道成员: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除公会频道成员失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<List<long>> GetMembersAsync()
        {
            try
            {
                return Task.FromResult(_channelState.State.Members.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取公会频道成员列表失败");
                throw;
            }
        }
    }

    #endregion

    #region TeamChannelGrain

    /// <summary>
    /// 队伍频道Grain实现 - 队伍内即时通讯
    /// </summary>
    public class TeamChannelGrain : Grain, ITeamChannelGrain
    {
        private const int MaxMessageContentLength = 2000;

        private readonly ILogger<TeamChannelGrain> _logger;
        private readonly IPersistentState<GroupChannelState> _channelState;

        public TeamChannelGrain(
            ILogger<TeamChannelGrain> logger,
            [PersistentState("teamChannel", "GameStore")] IPersistentState<GroupChannelState> channelState)
        {
            _logger = logger;
            _channelState = channelState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TeamChannelGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_channelState.State.Members == null)
                _channelState.State.Members = new HashSet<long>();

            if (_channelState.State.RecentMessages == null)
                _channelState.State.RecentMessages = new List<ChatMessage>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> BroadcastToMembersAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("队伍频道广播: TeamId={TeamId}, SenderId={SenderId}",
                    this.GetPrimaryKey(), message.SenderId);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("消息内容为空");
                    return false;
                }

                if (message.Content.Length > MaxMessageContentLength)
                {
                    _logger.LogWarning("消息内容过长: Length={Length}", message.Content.Length);
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

                var state = _channelState.State;
                state.RecentMessages.Add(message);

                if (state.RecentMessages.Count > state.MaxCachedMessages)
                {
                    state.RecentMessages.RemoveRange(0, state.RecentMessages.Count - state.MaxCachedMessages);
                }

                await _channelState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "队伍频道广播失败");
                throw;
            }
        }

        public async Task<bool> AddMemberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Members.Add(playerId))
                {
                    _logger.LogWarning("玩家已在队伍频道中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("添加队伍频道成员: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加队伍频道成员失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> RemoveMemberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Members.Remove(playerId))
                {
                    _logger.LogWarning("玩家不在队伍频道中: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("移除队伍频道成员: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除队伍频道成员失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<List<long>> GetMembersAsync()
        {
            try
            {
                return Task.FromResult(_channelState.State.Members.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取队伍频道成员列表失败");
                throw;
            }
        }
    }

    #endregion

    #region SystemChannelGrain

    /// <summary>
    /// 系统频道Grain实现 - 系统公告、活动通知
    /// </summary>
    public class SystemChannelGrain : Grain, ISystemChannelGrain
    {
        private const int MaxMessageContentLength = 2000;

        private readonly ILogger<SystemChannelGrain> _logger;
        private readonly IPersistentState<SystemChannelState> _channelState;

        public SystemChannelGrain(
            ILogger<SystemChannelGrain> logger,
            [PersistentState("systemChannel", "GameStore")] IPersistentState<SystemChannelState> channelState)
        {
            _logger = logger;
            _channelState = channelState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SystemChannelGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_channelState.State.Subscribers == null)
                _channelState.State.Subscribers = new HashSet<long>();

            if (_channelState.State.SystemMessages == null)
                _channelState.State.SystemMessages = new List<ChatMessage>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> BroadcastSystemMessageAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("系统广播消息: Content={Content}", message.Content);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("系统消息内容为空");
                    return false;
                }

                if (message.Content.Length > MaxMessageContentLength)
                {
                    _logger.LogWarning("系统消息内容过长: Length={Length}", message.Content.Length);
                    return false;
                }

                message.IsSystemMessage = true;
                message.ChannelType = ChatChannel.System;

                if (string.IsNullOrEmpty(message.MessageId))
                {
                    message.MessageId = Guid.NewGuid().ToString();
                }

                if (message.Timestamp == 0)
                {
                    message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                var state = _channelState.State;
                state.SystemMessages.Add(message);

                if (state.SystemMessages.Count > state.MaxCachedMessages)
                {
                    state.SystemMessages.RemoveRange(0, state.SystemMessages.Count - state.MaxCachedMessages);
                }

                await _channelState.WriteStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "系统广播消息失败");
                throw;
            }
        }

        public async Task<bool> AddSubscriberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Subscribers.Add(playerId))
                {
                    _logger.LogWarning("玩家已订阅系统频道: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("订阅系统频道: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订阅系统频道失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> RemoveSubscriberAsync(long playerId)
        {
            try
            {
                var state = _channelState.State;

                if (!state.Subscribers.Remove(playerId))
                {
                    _logger.LogWarning("玩家未订阅系统频道: PlayerId={PlayerId}", playerId);
                    return false;
                }

                await _channelState.WriteStateAsync();
                _logger.LogInformation("取消订阅系统频道: PlayerId={PlayerId}", playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订阅系统频道失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }
    }

    #endregion

    #region MessageRouterGrain

    /// <summary>
    /// 消息路由器Grain实现 - 负责消息路由分发
    /// </summary>
    public class MessageRouterGrain : Grain, IMessageRouterGrain
    {
        private readonly ILogger<MessageRouterGrain> _logger;
        private readonly IPersistentState<MessageRouterState> _routerState;

        public MessageRouterGrain(
            ILogger<MessageRouterGrain> logger,
            [PersistentState("messageRouter", "GameStore")] IPersistentState<MessageRouterState> routerState)
        {
            _logger = logger;
            _routerState = routerState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MessageRouterGrain {GrainKey} activating.", this.GetPrimaryKeyLong());
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> RouteMessageAsync(ChatMessage message)
        {
            try
            {
                _logger.LogInformation("路由消息: Channel={Channel}, SenderId={SenderId}",
                    message.ChannelType, message.SenderId);

                if (string.IsNullOrEmpty(message.Content))
                {
                    _logger.LogWarning("消息内容为空，拒绝路由");
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

                // 根据频道类型路由到对应的频道Grain
                var channelKey = $"channel_{message.ChannelType}";
                var channelGrain = GrainFactory.GetGrain<IMessageChannelGrain>(channelKey);
                var result = await channelGrain.BroadcastMessageAsync(message);

                if (result)
                {
                    _routerState.State.TotalRoutedMessages++;
                }
                else
                {
                    _routerState.State.FailedRoutedMessages++;
                }

                await _routerState.WriteStateAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "路由消息失败");
                _routerState.State.FailedRoutedMessages++;
                await _routerState.WriteStateAsync();
                throw;
            }
        }

        public async Task<int> RouteBatchMessagesAsync(List<ChatMessage> messages)
        {
            try
            {
                _logger.LogInformation("批量路由消息: Count={Count}", messages.Count);

                int successCount = 0;
                foreach (var message in messages)
                {
                    try
                    {
                        var result = await RouteMessageAsync(message);
                        if (result) successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "批量路由中单条消息失败: MessageId={MessageId}", message.MessageId);
                    }
                }

                _logger.LogInformation("批量路由完成: Success={Success}, Total={Total}",
                    successCount, messages.Count);
                return successCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量路由消息失败");
                throw;
            }
        }
    }

    #endregion

    #region SocialSystemMonitorGrain

    /// <summary>
    /// 社交系统监控Grain实现 - 系统统计与健康检查
    /// </summary>
    public class SocialSystemMonitorGrain : Grain, ISocialSystemMonitorGrain
    {
        private readonly ILogger<SocialSystemMonitorGrain> _logger;
        private readonly IPersistentState<SocialSystemMonitorState> _monitorState;

        public SocialSystemMonitorGrain(
            ILogger<SocialSystemMonitorGrain> logger,
            [PersistentState("socialSystemMonitor", "GameStore")] IPersistentState<SocialSystemMonitorState> monitorState)
        {
            _logger = logger;
            _monitorState = monitorState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SocialSystemMonitorGrain {GrainKey} activating.", this.GetPrimaryKeyLong());
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> ResetStatsAsync()
        {
            try
            {
                _logger.LogInformation("重置社交系统统计");

                var state = _monitorState.State;
                state.TotalMessagesRouted = 0;
                state.TotalChannels = 0;
                state.ActiveUsers = 0;
                state.LastResetTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await _monitorState.WriteStateAsync();

                _logger.LogInformation("社交系统统计已重置: ResetTime={ResetTime}", state.LastResetTime);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置社交系统统计失败");
                throw;
            }
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;

using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 消息路由器Grain接口
    /// </summary>
    public interface IMessageRouterGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 路由单条消息
        /// </summary>
        Task<bool> RouteMessageAsync(ChatMessage message);
        
        /// <summary>
        /// 批量路由消息
        /// </summary>
        Task<int> RouteBatchMessagesAsync(List<ChatMessage> messages);
        
        /// <summary>
        /// 获取频道统计信息
        /// </summary>
       // Task<Dictionary<Horizon.Game.Message.Enums.ChatChannel, ChannelStats>> GetChannelStatsAsync();
        
        /// <summary>
        /// 获取路由器性能指标
        /// </summary>
       // Task<MessageRoutingMetrics> GetMetricsAsync();
    }
    
    /// <summary>
    /// 消息频道Grain接口
    /// </summary>
    public interface IMessageChannelGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 广播消息到频道
        /// </summary>
        Task<bool> BroadcastMessageAsync(ChatMessage message);
        
        /// <summary>
        /// 订阅频道
        /// </summary>
        Task<bool> SubscribeAsync(long playerId);
        
        /// <summary>
        /// 取消订阅频道
        /// </summary>
        Task<bool> UnsubscribeAsync(long playerId);
        
        /// <summary>
        /// 获取订阅者列表
        /// </summary>
        Task<List<long>> GetSubscribersAsync();
        
        /// <summary>
        /// 获取频道统计
        /// </summary>
      //  Task<ChannelStats> GetStatsAsync();
    }
    
    /// <summary>
    /// 公会频道Grain接口
    /// </summary>
    public interface IGuildChannelGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 广播消息给公会成员
        /// </summary>
        Task<bool> BroadcastToMembersAsync(ChatMessage message);
        
        /// <summary>
        /// 添加公会成员
        /// </summary>
        Task<bool> AddMemberAsync(long playerId);
        
        /// <summary>
        /// 移除公会成员
        /// </summary>
        Task<bool> RemoveMemberAsync(long playerId);
        
        /// <summary>
        /// 获取公会成员列表
        /// </summary>
        Task<List<long>> GetMembersAsync();
    }
    
    /// <summary>
    /// 队伍频道Grain接口
    /// </summary>
    public interface ITeamChannelGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 广播消息给队伍成员
        /// </summary>
        Task<bool> BroadcastToMembersAsync(ChatMessage message);
        
        /// <summary>
        /// 添加队伍成员
        /// </summary>
        Task<bool> AddMemberAsync(long playerId);
        
        /// <summary>
        /// 移除队伍成员
        /// </summary>
        Task<bool> RemoveMemberAsync(long playerId);
        
        /// <summary>
        /// 获取队伍成员列表
        /// </summary>
        Task<List<long>> GetMembersAsync();
    }
    
    /// <summary>
    /// 系统频道Grain接口
    /// </summary>
    public interface ISystemChannelGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 广播系统消息
        /// </summary>
        Task<bool> BroadcastSystemMessageAsync(ChatMessage message);
        
        /// <summary>
        /// 添加系统消息订阅者
        /// </summary>
        Task<bool> AddSubscriberAsync(long playerId);
        
        /// <summary>
        /// 移除系统消息订阅者
        /// </summary>
        Task<bool> RemoveSubscriberAsync(long playerId);
    }
    
    /// <summary>
    /// 社交系统监控Grain接口
    /// </summary>
    public interface ISocialSystemMonitorGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 获取系统性能统计
        /// </summary>
      //  Task<SystemPerformanceStats> GetSystemStatsAsync();
        
        /// <summary>
        /// 获取频道详细统计
        /// </summary>
      //  Task<List<ChannelDetailStats>> GetChannelDetailStatsAsync();
        
        /// <summary>
        /// 获取玩家活跃度统计
        /// </summary>
       // Task<PlayerActivityStats> GetPlayerActivityStatsAsync();
        
        /// <summary>
        /// 重置系统统计
        /// </summary>
        Task<bool> ResetStatsAsync();
        
        /// <summary>
        /// 执行系统健康检查
        /// </summary>
     //   Task<SystemHealthReport> PerformHealthCheckAsync();
    }
}

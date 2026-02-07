using Orleans;
using System;
using System.Threading.Tasks;

using Horizon.Game.Message.Enums;
using System.Collections.Generic;
using System.Numerics;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 社交系统Grain接口 - 负责好友、聊天、公会管理
    /// </summary>
    public interface ISocialGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 发送聊天消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否发送成功</returns>
        Task<bool> SendChatMessageAsync(ChatMessage message);

        /// <summary>
        /// 获取聊天历史
        /// </summary>
        /// <param name="channelType">频道类型</param>
        /// <param name="count">消息数量</param>
        /// <returns>聊天消息列表</returns>
        Task<List<ChatMessage>> GetChatHistoryAsync(int channelType, int count = 50);
        
        /// <summary>
        /// 接收聊天消息
        /// </summary>
        /// <param name="message">聊天消息</param>
        /// <returns>是否接收成功</returns>
        Task<bool> ReceiveChatMessageAsync(ChatMessage message);
        
        /// <summary>
        /// 批量获取聊天历史
        /// </summary>
        /// <param name="channels">频道列表</param>
        /// <param name="countPerChannel">每个频道的消息数量</param>
        /// <returns>分频道的消息列表</returns>
        Task<Dictionary<ChatChannel, List<ChatMessage>>> GetBatchChatHistoryAsync(
            List<ChatChannel> channels, int countPerChannel = 20);

        /// <summary>
        /// 添加好友
        /// </summary>
        /// <param name="friendId">好友角色ID</param>
        /// <returns>是否添加成功</returns>
        Task<bool> AddFriendAsync(Guid friendId);

        /// <summary>
        /// 删除好友
        /// </summary>
        /// <param name="friendId">好友角色ID</param>
        /// <returns>是否删除成功</returns>
        Task<bool> RemoveFriendAsync(Guid friendId);

        /// <summary>
        /// 获取好友列表
        /// </summary>
        /// <returns>好友列表</returns>
        Task<List<FriendInfo>> GetFriendsAsync();

        /// <summary>
        /// 发送好友申请
        /// </summary>
        /// <param name="targetId">目标角色ID</param>
        /// <param name="message">申请消息</param>
        /// <returns>是否发送成功</returns>
        Task<bool> SendFriendRequestAsync(Guid targetId, string message);

        /// <summary>
        /// 处理好友申请
        /// </summary>
        /// <param name="requestId">申请ID</param>
        /// <param name="accept">是否接受</param>
        /// <returns>是否处理成功</returns>
        Task<bool> HandleFriendRequestAsync(Guid requestId, bool accept);
        
        ///// <summary>
        ///// 接收好友申请
        ///// </summary>
        ///// <param name="request">好友申请</param>
        ///// <returns>是否接收成功</returns>
        //Task<bool> ReceiveFriendRequestAsync(FriendRequest request);
        
        /// <summary>
        /// 好友被添加时的回调
        /// </summary>
        /// <param name="friendId">好友ID</param>
        Task OnFriendAddedAsync(long friendId);
        
        /// <summary>
        /// 好友被移除时的回调
        /// </summary>
        /// <param name="friendId">好友ID</param>
        Task OnFriendRemovedAsync(long friendId);
        
        /// <summary>
        /// 好友申请被处理时的回调
        /// </summary>
        /// <param name="requestId">申请ID</param>
        /// <param name="accepted">是否被接受</param>
        Task OnFriendRequestHandledAsync(Guid requestId, bool accepted);
        
        ///// <summary>
        ///// 获取Grain性能统计
        ///// </summary>
        ///// <returns>性能统计信息</returns>
        //Task<SocialGrainPerformanceStats> GetPerformanceStatsAsync();
    }
    }

    /// <summary>
    /// 公会系统Grain接口
    /// </summary>
    public interface IGuildGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 创建公会
        /// </summary>
        /// <param name="guildName">公会名称</param>
        /// <param name="creatorId">创建者ID</param>
        /// <returns>是否创建成功</returns>
        Task<bool> CreateGuildAsync(string guildName, Guid creatorId);

        /// <summary>
        /// 申请加入公会
        /// </summary>
        /// <param name="playerId">申请者ID</param>
        /// <param name="message">申请消息</param>
        /// <returns>是否申请成功</returns>
        Task<bool> ApplyToJoinAsync(Guid playerId, string message);

        /// <summary>
        /// 处理入会申请
        /// </summary>
        /// <param name="applicationId">申请ID</param>
        /// <param name="approverId">审批者ID</param>
        /// <param name="approve">是否通过</param>
        /// <returns>是否处理成功</returns>
        Task<bool> ProcessApplicationAsync(Guid applicationId, Guid approverId, bool approve);

        /// <summary>
        /// 踢出公会成员
        /// </summary>
        /// <param name="operatorId">操作者ID</param>
        /// <param name="targetId">目标成员ID</param>
        /// <returns>是否踢出成功</returns>
        Task<bool> KickMemberAsync(Guid operatorId, Guid targetId);

        /// <summary>
        /// 离开公会
        /// </summary>
        /// <param name="memberId">成员ID</param>
        /// <returns>是否离开成功</returns>
        Task<bool> LeaveGuildAsync(Guid memberId);

        /// <summary>
        /// 获取公会信息
        /// </summary>
        /// <returns>公会信息</returns>
        Task<GuildInfo> GetGuildInfoAsync();

        /// <summary>
        /// 获取公会成员列表
        /// </summary>
        /// <returns>成员列表</returns>
        Task<List<GuildMember>> GetMembersAsync();

        /// <summary>
        /// 任命公会职位
        /// </summary>
        /// <param name="operatorId">操作者ID</param>
        /// <param name="targetId">目标成员ID</param>
        /// <param name="position">职位</param>
        /// <returns>是否任命成功</returns>
        Task<bool> AppointPositionAsync(Guid operatorId, Guid targetId, int position);
    }

    /// <summary>
    /// 地图管理Grain接口 - 负责地图实例、传送、区域管理
    /// </summary>
    public interface IMapGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 角色进入地图
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="position">进入位置</param>
        /// <returns>是否成功进入</returns>
        Task<bool> EnterMapAsync(Guid characterId, Vector3 position);

        /// <summary>
        /// 角色离开地图
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否成功离开</returns>
        Task<bool> LeaveMapAsync(Guid characterId);

        /// <summary>
        /// 更新角色位置
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="position">新位置</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdatePositionAsync(Guid characterId, Vector3 position);

        /// <summary>
        /// 获取地图内所有玩家
        /// </summary>
        /// <returns>玩家列表</returns>
        Task<List<MapPlayer>> GetPlayersAsync();

        /// <summary>
        /// 广播消息给地图内所有玩家
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否广播成功</returns>
        Task<bool> BroadcastAsync(object message);

        /// <summary>
        /// 在指定范围内广播消息
        /// </summary>
        /// <param name="center">中心位置</param>
        /// <param name="range">范围</param>
        /// <param name="message">消息内容</param>
        /// <returns>是否广播成功</returns>
        Task<bool> BroadcastInRangeAsync(Vector3 center, float range, object message);
    }

  


using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 区域/场景管理Grain接口 - 负责场景实例创建/销毁、跨服传送、副本入口
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IAreaGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 初始化区域信息
        /// </summary>
        Task<bool> InitializeAreaAsync(string areaName, string areaType, int maxPlayers);

        /// <summary>
        /// 创建场景实例
        /// </summary>
        Task<SceneInstanceInfo> CreateSceneInstanceAsync(string sceneName, int maxPlayers);

        /// <summary>
        /// 销毁场景实例
        /// </summary>
        Task<bool> DestroySceneInstanceAsync(long instanceId);

        /// <summary>
        /// 玩家进入场景实例
        /// </summary>
        Task<bool> PlayerEnterInstanceAsync(long instanceId, Guid playerId);

        /// <summary>
        /// 玩家离开场景实例
        /// </summary>
        Task<bool> PlayerLeaveInstanceAsync(long instanceId, Guid playerId);

        /// <summary>
        /// 获取场景实例信息
        /// </summary>
        Task<SceneInstanceInfo> GetSceneInstanceAsync(long instanceId);

        /// <summary>
        /// 获取所有场景实例列表
        /// </summary>
        Task<List<SceneInstanceInfo>> GetAllInstancesAsync();

        /// <summary>
        /// 请求跨服传送
        /// </summary>
        Task<TeleportResult> RequestTeleportAsync(Guid playerId, int targetAreaId, long targetInstanceId);

        /// <summary>
        /// 获取区域信息
        /// </summary>
        Task<AreaInfo> GetAreaInfoAsync();
    }

    /// <summary>
    /// 活动系统Grain接口 - 负责定时活动调度、奖励发放、参与记录
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IActivityGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 创建活动
        /// </summary>
        Task<bool> CreateActivityAsync(string name, string description, DateTime startTime, DateTime endTime, int maxParticipants);

        /// <summary>
        /// 获取活动信息
        /// </summary>
        Task<ActivityInfo> GetActivityInfoAsync();

        /// <summary>
        /// 玩家参与活动
        /// </summary>
        Task<bool> JoinActivityAsync(Guid playerId);

        /// <summary>
        /// 玩家退出活动
        /// </summary>
        Task<bool> LeaveActivityAsync(Guid playerId);

        /// <summary>
        /// 发放活动奖励给指定玩家
        /// </summary>
        Task<bool> DistributeRewardAsync(Guid playerId, int rewardTemplateId, int quantity);

        /// <summary>
        /// 获取玩家参与记录
        /// </summary>
        Task<ActivityParticipation> GetParticipationAsync(Guid playerId);

        /// <summary>
        /// 获取所有参与者列表
        /// </summary>
        Task<List<ActivityParticipation>> GetAllParticipantsAsync();

        /// <summary>
        /// 结束活动
        /// </summary>
        Task<bool> EndActivityAsync();

        /// <summary>
        /// 检查活动是否在进行中
        /// </summary>
        Task<bool> IsActiveAsync();
    }
}

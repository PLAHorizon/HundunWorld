using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;

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

    #region 区域管理数据模型

    /// <summary>
    /// 场景实例信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class SceneInstanceInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public long InstanceId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string SceneName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public int CurrentPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public HashSet<Guid> Players { get; set; } = new();

        [MemoryPackOrder(5)]
        [Id(5)]
        public DateTime CreatedTime { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 传送结果
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TeleportResult
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public int TargetAreaId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long TargetInstanceId { get; set; }
    }

    /// <summary>
    /// 区域信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class AreaInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int AreaId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string AreaName { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string AreaType { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public int MaxPlayers { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public int TotalPlayers { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int InstanceCount { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public bool IsInitialized { get; set; }
    }

    #endregion

    #region 活动系统数据模型

    /// <summary>
    /// 活动信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ActivityInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int ActivityId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(2)]
        [Id(2)]
        public string Description { get; set; } = "";

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime StartTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime EndTime { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public int MaxParticipants { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public int CurrentParticipants { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int Status { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public bool IsCreated { get; set; }
    }

    /// <summary>
    /// 活动参与记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class ActivityParticipation
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid PlayerId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public DateTime JoinTime { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public List<RewardRecord> Rewards { get; set; } = new();

        [MemoryPackOrder(3)]
        [Id(3)]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 奖励记录
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class RewardRecord
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RewardTemplateId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public int Quantity { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public DateTime DistributedTime { get; set; }
    }

    /// <summary>
    /// 活动状态枚举
    /// </summary>
    public enum ActivityStatus
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// 进行中
        /// </summary>
        Active = 1,

        /// <summary>
        /// 已结束
        /// </summary>
        Ended = 2,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 3
    }

    #endregion
}

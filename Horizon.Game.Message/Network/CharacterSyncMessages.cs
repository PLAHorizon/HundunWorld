using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region Phase 8 - 角色管理与合成网络集成消息

    /// <summary>
    /// 角色状态同步请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterStateSyncRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 同步类型（0=全量, 1=增量）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int SyncType { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.CharacterStateSync;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 角色状态同步响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterStateSyncResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        /// <summary>
        /// 当前等级
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Level { get; set; }

        /// <summary>
        /// 当前经验值
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public long Experience { get; set; }

        /// <summary>
        /// 当前金币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long Gold { get; set; }

        /// <summary>
        /// 当前生命值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float Health { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.CharacterStateSync;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 合成金币同步请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CraftingGoldSyncRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 消耗金币数量
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long GoldCost { get; set; }

        /// <summary>
        /// 配方ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int RecipeId { get; set; }

        /// <summary>
        /// 合成次数
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int CraftCount { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.CraftingGoldSync;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 合成金币同步响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CraftingGoldSyncResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Message { get; set; } = "";

        /// <summary>
        /// 剩余金币
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long RemainingGold { get; set; }

        /// <summary>
        /// 合成产出物品ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int OutputItemId { get; set; }

        /// <summary>
        /// 合成产出数量
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int OutputCount { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public MessageType Type { get; set; } = MessageType.CraftingGoldSync;
        [MemoryPackOrder(6)]
        [Id(6)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能目标验证请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillTargetValidationRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 施法者角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CasterId { get; set; }

        /// <summary>
        /// 目标实体网络ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong TargetNetworkId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int SkillId { get; set; }

        /// <summary>
        /// 施法者位置X
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float CasterPositionX { get; set; }

        /// <summary>
        /// 施法者位置Y
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float CasterPositionY { get; set; }

        /// <summary>
        /// 施法者位置Z
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CasterPositionZ { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public MessageType Type { get; set; } = MessageType.SkillTargetValidation;
        [MemoryPackOrder(7)]
        [Id(7)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 技能目标验证响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class SkillTargetValidationResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool IsValid { get; set; }

        /// <summary>
        /// 验证失败原因
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string Reason { get; set; } = "";

        /// <summary>
        /// 目标实体网络ID
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetNetworkId { get; set; }

        /// <summary>
        /// 技能ID
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int SkillId { get; set; }

        /// <summary>
        /// 校正后的目标位置X
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float CorrectedPositionX { get; set; }

        /// <summary>
        /// 校正后的目标位置Y
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float CorrectedPositionY { get; set; }

        /// <summary>
        /// 校正后的目标位置Z
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float CorrectedPositionZ { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.SkillTargetValidation;
        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 截图通知消息
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class ScreenshotNotifyMessage : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 截图文件路径
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public string FilePath { get; set; } = "";

        /// <summary>
        /// 截图时间戳
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 截图类型（0=普通, 1=角色预览, 2=场景截图）
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int ScreenshotType { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public MessageType Type { get; set; } = MessageType.ScreenshotNotify;
        [MemoryPackOrder(5)]
        [Id(5)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 角色属性刷新请求
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterAttributeRefreshRequest : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 刷新原因（0=登录, 1=升级, 2=装备变更, 3=Buff变更, 4=手动刷新）
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int RefreshReason { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageType Type { get; set; } = MessageType.CharacterAttributeRefresh;
        [MemoryPackOrder(3)]
        [Id(3)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    /// <summary>
    /// 角色属性刷新响应
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class CharacterAttributeRefreshResponse : MessageUnion, INetworkMessage
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>
        /// 角色ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 攻击力
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public float Attack { get; set; }

        /// <summary>
        /// 防御力
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public float Defense { get; set; }

        /// <summary>
        /// 最大生命值
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public float MaxHealth { get; set; }

        /// <summary>
        /// 最大能量值
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public float MaxEnergy { get; set; }

        /// <summary>
        /// 战力评分
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int CombatPower { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(8)]
        [Id(8)]
        public MessageType Type { get; set; } = MessageType.CharacterAttributeRefresh;
        [MemoryPackOrder(9)]
        [Id(9)]
        public ServiceType ServiceType { get; set; } = ServiceType.Game;
    }

    #endregion
}

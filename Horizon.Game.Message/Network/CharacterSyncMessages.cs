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
    [NetworkMessage(212, Enums.ServiceType.Game)]
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
    [NetworkMessage(213, Enums.ServiceType.Game)]
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
    [NetworkMessage(214, Enums.ServiceType.Game)]
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
    [NetworkMessage(215, Enums.ServiceType.Game)]
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
    [NetworkMessage(216, Enums.ServiceType.Game)]
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
    [NetworkMessage(217, Enums.ServiceType.Game)]
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
    [NetworkMessage(218, Enums.ServiceType.Game)]
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
    [NetworkMessage(219, Enums.ServiceType.Game)]
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
    [NetworkMessage(220, Enums.ServiceType.Game)]
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

        // ── 以下 10+ 为 2026-09-29 追加：权威五行三行与画像口径 ──
        //
        // 这条通道此前只有消息定义、测试和枚举值，没有任何服务端实现、也没有任何客户端订阅者，
        // 所以面板的五行三行一直是客户端自己那套"加点 + 装备硬编码表"的镜像。
        // 属性表里 *_attack / *_resistance / *_penetration 三组列只有服务端读得到，装备的权威数值
        // 也在物品模板表里——不下发就必然出现"运营改配表后面板显示一套、服务端结算另一套"。

        /// <summary>五行攻击力（各来源求和后再与五行属性值兜底逐行取较高）。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public Horizon.Game.Message.Combat.ElementStatVector ElementAttack { get; set; }

        /// <summary>五行防御力（已高者计）。</summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public Horizon.Game.Message.Combat.ElementStatVector ElementDefense { get; set; }

        /// <summary>五行破防值（已高者计，存攻方本行）。</summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public Horizon.Game.Message.Combat.ElementStatVector ElementPenetration { get; set; }

        /// <summary>五行属性值（亲和）：兜底地板的输入，与雷达图同一份数字。</summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public Horizon.Game.Message.Combat.ElementStatVector Affinity { get; set; }

        /// <summary>自身命行（1..5 = 金木水火土，0 = 无属性）。</summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public int WuxingElement { get; set; }

        // ── 以下 15+ 为 2026-10-29 追加：宠物反哺的归因量 ──
        //
        // 刻意**不**把宠物加成混进上面的 Attack/Defense/MaxHealth：那些是"合算后的权威值"，
        // 战斗裁决与面板都读它；混进去就再也回答不了"这 1160 攻击里有多少是宠给的"。
        // 数字出自 PetAssistCore.ApplyToOwnerStats 的 FromPets* 四项——同一个出口，不在此处重算。
        // 0 的含义是"没有出战/附体的宠"，与"宠物加成算不出来"由 PetBonusIssues 区分。

        /// <summary>宠物贡献的攻击（已含借力类特技与附体位力量/灵气折算）。</summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public int FromPetsAttack { get; set; }

        /// <summary>宠物贡献的生命上限。</summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public int FromPetsHealth { get; set; }

        /// <summary>宠物贡献的防御。</summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public int FromPetsDefense { get; set; }

        /// <summary>宠物贡献的命中。</summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public int FromPetsHit { get; set; }

        /// <summary>宠物替主人承受会心的概率（bps，多只取最大而非相加）。</summary>
        [MemoryPackOrder(19)]
        [Id(19)]
        public int PetCritTankChanceBps { get; set; }

        /// <summary>参与反哺的宠物只数（出战 1 + 附体至多 3）。</summary>
        [MemoryPackOrder(20)]
        [Id(20)]
        public int PetBonusContributors { get; set; }

        /// <summary>名册里的总只数（含不打宠的、蛋、兽栏寄存）——面板能说"6 只里 2 只在反哺"。</summary>
        [MemoryPackOrder(21)]
        [Id(21)]
        public int PetRosterCount { get; set; }

        /// <summary>装载/聚合过程中的问题描述；空串 = 一切正常。不许静默吞掉（记忆 skipped-checks-must-surface-in-results）。</summary>
        [MemoryPackOrder(22)]
        [Id(22)]
        public string PetBonusIssues { get; set; } = "";
    }

    #endregion
}

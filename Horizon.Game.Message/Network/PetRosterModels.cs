using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 一名主人的宠物名册快照（服务端内部 DTO：Grain → CharacterGrain → 下行消息）。
    /// <para>
    /// 为什么和 <c>CharacterAttributeRefreshResponse</c> 里的 <c>FromPets*</c> 字段分开：
    /// 前者是"宠物侧的原始账"（五维点数、几只参与、哪里被挡下），后者是"主人面板的归因量"
    /// （攻击/生命/防御/命中各加了多少）。中间那一步换算只允许发生在
    /// <c>PetAssistCore.ApplyToOwnerStats</c>，所以 DTO 里不许出现合算后的战斗属性。</para>
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class PetRosterSnapshotDto
    {
        /// <summary>名册总只数（有效行）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public int RosterCount { get; set; }

        /// <summary>参与反哺的只数（出战 + 附体）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Contributors { get; set; }

        /// <summary>聚合时被挡下的条目数（超出附体位上限、编号未分配…）。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Excluded { get; set; }

        /// <summary>整条作废的个体数（编号与物种不符）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int Rejected { get; set; }

        /// <summary>列解码出问题、但条目仍在用的个体数。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Degraded { get; set; }

        /// <summary>问题描述（挡下原因 + 首条解码问题）；空串 = 一切正常。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public string Issues { get; set; } = "";

        /// <summary>力量（外功）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public int Strength { get; set; }

        /// <summary>灵气（内功）。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public int Spirit { get; set; }

        /// <summary>体力。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int Vitality { get; set; }

        /// <summary>身法。</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int Agility { get; set; }

        /// <summary>定力。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int Steadiness { get; set; }

        /// <summary>生命上限加成（点，共生类技能直接给的那一份）。</summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public int HpFlat { get; set; }

        /// <summary>内力回复加成（点/秒）。</summary>
        [MemoryPackOrder(12)]
        [Id(12)]
        public int ManaRegen { get; set; }

        /// <summary>替主人承受会心的概率（bps）；多只取最大。</summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public int CritTankChanceBps { get; set; }

        /// <summary>宠物物理伤害转化主人生命的比例（bps）；多只取最大。</summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public int DamageLeechBps { get; set; }

        /// <summary>读侧版本，用于对账"这条快照是哪一版结构产出的"。</summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public int ReadSideVersion { get; set; }
    }

    /// <summary>个体状态变更的结果（出战/附体/收回）。</summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class PetDeployResultDto
    {
        /// <summary>是否改成功。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public bool Success { get; set; }

        /// <summary>结果码（0 = 成功；其余见 <c>PetRosterGrain</c> 的码表注释）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Code { get; set; }

        /// <summary>人话说明（界面直接念，不要自己拼）。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public string Message { get; set; } = "";

        /// <summary>改完之后的名册快照（省一次往返，也让客户端能当场看到新账）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public PetRosterSnapshotDto? Snapshot { get; set; }
    }
}

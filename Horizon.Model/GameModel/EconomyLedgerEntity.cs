using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 经济逐笔流水（P1）：每次钱包增减记一行，**只追加、不修改**。
    /// <para>
    /// 与 <see cref="CurrencyEntity"/> 的累计列分工：累计列回答"现在一共多少"，本表回答"这笔钱从哪来/到哪去"。
    /// 两者都写成功才算一次可对账的钱包操作；任何一侧写失败都要留日志，不能静默。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_EconomyLedger"), TableDescription(Name = "Game_HunduShijie_EconomyLedger", Order = "HunduShijie_900", Description = "经济逐笔流水")]
    [Comment("经济逐笔流水表")]
    [Index(nameof(CharacterId), nameof(CreatedAt))]
    [Index(nameof(CurrencyType), nameof(CreatedAt))]
    [EntityStorage("Game")]
    public class EconomyLedgerEntity : BaseGameModel<long>
    {
        /// <summary>自增ID。</summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1)]
        public new long Id { get; set; }

        /// <summary>有效标记（与其它游戏表同列；插入时置 true）。</summary>
        [Column("IsValid")]
        public bool IsValid { get; set; } = true;

        /// <summary>角色ID。</summary>
        [Column("character_id")]
        public ulong CharacterId { get; set; }

        /// <summary>货币类型（1 = 金币）。</summary>
        [Column("currency_type")]
        public int CurrencyType { get; set; }

        /// <summary>本次变动（正数产出、负数消耗）。</summary>
        [Column("delta")]
        public long Delta { get; set; }

        /// <summary>变动后的余额。</summary>
        [Column("balance_after")]
        public long BalanceAfter { get; set; }

        /// <summary>来源/去向短码（<c>npc_buy:…</c> / <c>npc_sell:…</c> / <c>quest_reward:…</c>）。</summary>
        [Column("reason")]
        [MaxLength(96)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>发生时间（UTC）。</summary>
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 经济周期快照（P1）：定期把"总供给 / 累计产出 / 累计消耗 / 账户数 / 账不平行数"落一行，
    /// 供 <c>EconomyCli</c> 算通胀率（本期相对上期）。**只追加**，不在老行上改。
    /// </summary>
    [Table("Game_HunduShijie_EconomySnapshot"), TableDescription(Name = "Game_HunduShijie_EconomySnapshot", Order = "HunduShijie_901", Description = "经济周期快照")]
    [Comment("经济周期快照表")]
    [Index(nameof(CurrencyType), nameof(TakenAt))]
    [EntityStorage("Game")]
    public class EconomySnapshotEntity : BaseGameModel<long>
    {
        [Key]
        [Column("id", TypeName = "bigint", Order = 1)]
        public new long Id { get; set; }

        /// <summary>有效标记（与其它游戏表同列；插入时置 true）。</summary>
        [Column("IsValid")]
        public bool IsValid { get; set; } = true;

        [Column("currency_type")]
        public int CurrencyType { get; set; }

        /// <summary>当前流通总量（所有账户余额之和）。</summary>
        [Column("supply")]
        public long Supply { get; set; }

        /// <summary>累计产出。</summary>
        [Column("earned")]
        public long Earned { get; set; }

        /// <summary>累计消耗。</summary>
        [Column("spent")]
        public long Spent { get; set; }

        /// <summary>有钱包行的账户数。</summary>
        [Column("accounts")]
        public int Accounts { get; set; }

        /// <summary>余额 ≠ 累计获得−累计消耗 的行数（0 = 账平）。</summary>
        [Column("rows_off_book")]
        public int RowsOffBook { get; set; }

        [Column("taken_at")]
        public DateTime TakenAt { get; set; }
    }
}

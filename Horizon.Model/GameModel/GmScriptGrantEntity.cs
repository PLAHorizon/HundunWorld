using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 脚本化世界生成（DS 怪物/NPC/特殊事件）的授权记录（SQLServer 权威层）。
    /// <para>
    /// 双轨定位：Redis/Orleans 持久化是<b>热层</b>（裁决读路径，秒级），本表是<b>权威层</b>
    /// （授予/回收的最终事实与重启重建来源）。写入顺序固定"先本表后热层"，
    /// 热层缺失时由 Grain 激活流程从本表重建。
    /// </para>
    /// </summary>
    [Table("gm_script_grant")]
    public class GmScriptGrantEntity : BaseIdentityModel<long>
    {
        /// <summary>被授权的角色 ID（一个角色同一时刻只保留一条有效记录）。</summary>
        [Column("operator_character_id", TypeName = "bigint")]
        [Comment("被授权角色ID")]
        public long OperatorCharacterId { get; set; }

        /// <summary>权限位（<c>ScriptPermissionFlags</c> 的整数值）。</summary>
        [Column("flags", TypeName = "bigint")]
        [Comment("权限位")]
        public long Flags { get; set; }

        /// <summary>授予者角色 ID（0 = 系统引导）。</summary>
        [Column("granted_by", TypeName = "bigint")]
        [Comment("授予者角色ID，0表示系统")]
        public long GrantedBy { get; set; }

        /// <summary>授予时刻（UTC）。</summary>
        [Column("granted_at_utc")]
        [Comment("授予时刻UTC")]
        public DateTime GrantedAtUtc { get; set; }

        /// <summary>到期时刻（UTC）；<see cref="DateTime.MaxValue"/> 表示不过期。</summary>
        [Column("expires_at_utc")]
        [Comment("到期时刻UTC")]
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>回收时刻（UTC）；null 表示未回收。</summary>
        [Column("revoked_at_utc", TypeName = "datetime2")]
        [Comment("回收时刻UTC，null表示未回收")]
        public DateTime? RevokedAtUtc { get; set; }

        /// <summary>授予理由（审计可读）。</summary>
        [Column("reason", TypeName = "nvarchar", Order = 6)]
        [MaxLength(200)]
        [Comment("授予理由")]
        public string Reason { get; set; } = string.Empty;
    }
}

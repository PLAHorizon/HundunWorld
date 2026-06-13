using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// World diff append-only 日志实体，映射 <c>dbo.diff_log</c>。<br/>
    /// 利用 SQL Server IDENTITY 列提供跨 silo 单调 seq，供客户端增量同步拉取。
    /// </summary>
    [Table("diff_log")]
    [Comment("世界 Diff 追加日志表（IDENTITY seq，按 Morton 键哈希分区）")]
    public class DiffLogEntity
    {
        /// <summary>
        /// 全局单调序列号，由 SQL Server IDENTITY(1,1) 生成。
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("seq", TypeName = "bigint", Order = 1)]
        [Comment("全局单调序列号（IDENTITY）")]
        public long Seq { get; set; }

        /// <summary>
        /// Morton 键（chunk 的空间编码）。
        /// </summary>
        [Column("morton_key", TypeName = "bigint", Order = 2)]
        [Comment("Morton 编码键（chunk 空间坐标）")]
        public long MortonKey { get; set; }

        /// <summary>
        /// Morton 哈希桶（morton_key % N，分区列）。
        /// </summary>
        [Column("morton_bucket", TypeName = "tinyint", Order = 3)]
        [Comment("Morton 哈希桶（分区列）")]
        public byte MortonBucket { get; set; }

        /// <summary>
        /// VoxelOp 操作类型（对应 VoxelOpKind 枚举的底层 byte 值）。
        /// </summary>
        [Column("op_kind", TypeName = "tinyint", Order = 4)]
        [Comment("VoxelOp 操作类型（VoxelOpKind）")]
        public byte OpKind { get; set; }

        /// <summary>
        /// MemoryPack 序列化的原始 VoxelOp 字节；客户端直接透传到 WorldChunkDiffPacket.Payload。
        /// DDL 约束：NOT NULL。
        /// </summary>
        [Required]
        [Column("payload", TypeName = "varbinary(max)", Order = 5)]
        [Comment("MemoryPack 序列化的 VoxelOp payload")]
        public byte[] Payload { get; set; }

        /// <summary>
        /// 写入时间戳（精度 3 位毫秒），用于过期清理 job。
        /// </summary>
        [Column("created_at", TypeName = "datetime2(3)", Order = 6)]
        [Comment("写入 UTC 时间（供过期清理使用）")]
        public DateTime CreatedAt { get; set; }
    }
}

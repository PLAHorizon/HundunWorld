using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// Chunk 权威状态快照实体，映射 <c>dbo.chunk_state</c>。<br/>
    /// 每行保存一个 Morton 键对应 chunk 的最新状态（版本号 + JSON 诊断副本 + MemoryPack 二进制副本），
    /// 供 ZoneShardGrain 批量加载及断线重连快照使用。
    /// </summary>
    [Table("chunk_state")]
    [Comment("世界 Chunk 权威状态快照表（按 Morton 键哈希分区）")]
    public class ChunkStateEntity
    {
        /// <summary>
        /// Morton 键（chunk 的空间编码，64 位无符号，以 long 存储）。
        /// </summary>
        [Column("morton_key", TypeName = "bigint", Order = 1)]
        [Comment("Morton 编码键（chunk 空间坐标）")]
        public long MortonKey { get; set; }

        /// <summary>
        /// Morton 哈希桶（morton_key % N，供分区与索引使用，范围 0-7）。
        /// </summary>
        [Column("morton_bucket", TypeName = "tinyint", Order = 2)]
        [Comment("Morton 哈希桶（morton_key % N，分区列）")]
        public byte MortonBucket { get; set; }

        /// <summary>
        /// 状态版本号；每次应用 op 递增，用于乐观并发校验。
        /// </summary>
        [Column("version", TypeName = "bigint", Order = 3)]
        [Comment("状态版本号，乐观并发")]
        public long Version { get; set; }

        /// <summary>
        /// 最近一次写入的服务器 UTC 时间（精度 3 位毫秒）。
        /// </summary>
        [Column("updated_at", TypeName = "datetime2(3)", Order = 4)]
        [Comment("最近写入 UTC 时间")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// JSON 序列化的 ChunkCellState（OpLog + 元数据），用于诊断与人工查阅。
        /// DDL 约束：NOT NULL。
        /// </summary>
        [Required]
        [Column("state_json", TypeName = "nvarchar(max)", Order = 5)]
        [Comment("JSON 状态副本（诊断用）")]
        public string StateJson { get; set; }

        /// <summary>
        /// MemoryPack 二进制副本，供快速批量加载；优先读此列，state_json 仅作诊断。
        /// 可为 null（历史数据或仅写 JSON 的行）。
        /// </summary>
        [Column("state_bin", TypeName = "varbinary(max)", Order = 6)]
        [Comment("MemoryPack 二进制状态副本（快速批量加载）")]
        public byte[] StateBin { get; set; }
    }
}

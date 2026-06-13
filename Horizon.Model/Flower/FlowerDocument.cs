using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Model.Flower
{
    /// <summary>
    /// 花卉文档
    /// </summary>
    [Table("Flower_Document")]
    [EntityStorage("Flower")]
    public class FlowerDocument : BaseIdentityAggregateRootModel<long>, ISoftDeleted
    {
        /// <summary>
        /// 标题
        /// </summary>
        [StringLength(256), Column(TypeName = "nvarchar(256)")]
        [Comment("标题")]
        public string Title { get; set; }

        /// <summary>
        /// 内容
        /// </summary>
        [Column(TypeName = "nvarchar(max)")]
        [Comment("内容")]
        public string Content { get; set; }

        /// <summary>
        /// 来源
        /// </summary>
        [StringLength(128), Column(TypeName = "nvarchar(128)")]
        [Comment("来源")]
        public string Source { get; set; }

        /// <summary>
        /// 是否已索引
        /// </summary>
        [Comment("是否已索引")]
        public bool IsIndexed { get; set; }

        /// <summary>
        /// 索引时间
        /// </summary>
        [Comment("索引时间")]
        public DateTime? IndexedAt { get; set; }

        /// <summary>
        /// 分块数量
        /// </summary>
        [Comment("分块数量")]
        public int ChunkCount { get; set; }

        /// <summary>
        /// 是否已删除
        /// </summary>
        [Comment("是否已删除")]
        public bool IsDeleted { get; set; }
    }
}

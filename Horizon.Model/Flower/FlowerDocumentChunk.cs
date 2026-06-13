using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;

namespace Horizon.Model.Flower
{
    [Table("Flower_DocumentChunk")]
    [EntityStorage("Flower")]
    public class FlowerDocumentChunk : BaseIdentityModel<long>
    {
        [Column("DocumentId")]
        public long DocumentId { get; set; }

        [Column("ChunkIndex")]
        public int ChunkIndex { get; set; }

        [Column("Content")]
        public string Content { get; set; }

        [Column("TokenCount")]
        public int TokenCount { get; set; }

        [Column("IsIndexed")]
        public bool IsIndexed { get; set; }

        [Column("EmbeddingVector")]
        public byte[] EmbeddingVector { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }
    }
}

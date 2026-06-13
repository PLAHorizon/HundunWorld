using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Horizon.Core.Abstract;

namespace Horizon.Model.Flower
{
    [Table("Flower_ShoppingCart")]
    [EntityStorage("Flower")]
    public class FlowerShoppingCart : BaseIdentityModel<long>
    {
        [Column("UserId")]
        public string UserId { get; set; }

        [Column("ProductId")]
        public long ProductId { get; set; }

        [Column("SKUId")]
        public long? SKUId { get; set; }

        [Column("ProductName")]
        public string ProductName { get; set; }

        [Column("Quantity")]
        public int Quantity { get; set; }

        [Column("Price")]
        public decimal Price { get; set; }

        [Column("ImageUrl")]
        public string ImageUrl { get; set; }

        [Column("MerchantId")]
        public long MerchantId { get; set; }

        [Column("SpeciesId")]
        public int SpeciesId { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        [Column("UpdatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }
}

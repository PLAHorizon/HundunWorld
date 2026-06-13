using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerProductController : OrleansControllerBase
    {
        private readonly ILogger<FlowerProductController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;
        public FlowerProductController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerProductController> logger,
            IClusterClient clusterClient, IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _passportCurrent = passportCurrent;
            _logger = logger;
        }

        [HttpGet("{productId}")]
        public async Task<ResultVM<ProductState>> GetProductAsync(long productId)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.GetProductAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "获取商品详情失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<ProductState>> CreateProductAsync([FromBody] CreateProductRequest request)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (request?.Product == null)
                {
                    result.ErrorMessage = "商品数据不能为空";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, request.Product.MerchantId, userId))
                {
                    _logger.LogWarning("创建商品归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", request.Product.MerchantId, userId);
                    result.ErrorMessage = "无权操作此商户的商品";
                    return result;
                }

                var tempGrainId = DateTimeOffset.UtcNow.Ticks;
                var grain = client.GetGrain<IProductGrain>(tempGrainId);

                var product = await grain.CreateProductAsync(request.Product);
                if (product == null)
                {
                    result.ErrorMessage = "创建商品失败";
                    return result;
                }

                var productGrain = client.GetGrain<IProductGrain>(product.ProductId);

                if (request.Skus != null)
                {
                    foreach (var sku in request.Skus)
                    {
                        sku.ProductId = product.ProductId;
                        await productGrain.AddProductSKUAsync(sku);
                    }
                }

                if (request.LadderPrices != null && request.LadderPrices.Count > 0)
                {
                    await productGrain.SetLadderPricesAsync(product.ProductId, request.LadderPrices);
                }

                result.Data = product;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                result.ErrorMessage = "创建商品失败";
            }
            return result;
        }

        [HttpPut("{productId}")]
        public async Task<ResultVM<ProductState>> UpdateProductAsync(long productId, [FromBody] ProductState product)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("更新商品归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                product.ProductId = productId;
                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.UpdateProductAsync(product);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "更新商品失败";
            }
            return result;
        }

        [HttpGet("merchant/{merchantId}")]
        public async Task<ResultVM<List<ProductState>>> MerchantProductsAsync(long merchantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<ProductState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, merchantId, userId))
                {
                    _logger.LogWarning("查询商户商品归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", merchantId, userId);
                    result.ErrorMessage = "只能查询自己店铺的商品";
                    return result;
                }

                var queryGrain = client.GetGrain<IFlowerQueryGrain>(0);
                var skip = (page - 1) * pageSize;
                result.Data = await queryGrain.QueryProductsByMerchantAsync(merchantId, skip, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商户商品失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "查询商户商品失败";
            }
            return result;
        }

        [HttpGet("active")]
        public async Task<ResultVM<List<ProductState>>> ActiveProductsAsync([FromQuery] int speciesId = 0, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<ProductState>>();
            try
            {
                var client = await OrleansConnectClient();
                var queryGrain = client.GetGrain<IFlowerQueryGrain>(0);
                var skip = (page - 1) * pageSize;
                result.Data = await queryGrain.QueryActiveProductsAsync(speciesId, skip, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询活跃商品失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "查询活跃商品失败";
            }
            return result;
        }

        [HttpPost("{productId}/toggle-active")]
        public async Task<ResultVM<bool>> ToggleProductActiveAsync(long productId, [FromBody] bool isActive)
        {
            var result = new ResultVM<bool>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("切换商品状态归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                var grain = client.GetGrain<IProductGrain>(productId);
                var success = await grain.SetProductActiveAsync(productId, isActive);
                result.Data = success;
                result.IsSuccess = success;
                if (!success) result.ErrorMessage = "操作失败";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换商品状态失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "切换商品状态失败";
            }
            return result;
        }

        [HttpDelete("{productId}")]
        public async Task<ResultVM<bool>> DeleteProductAsync(long productId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("删除商品归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.DeleteProductAsync(productId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "删除商品失败";
            }
            return result;
        }

        [HttpPost("{productId}/audit")]
        public async Task<ResultVM<ProductState>> AuditProductAsync(long productId, [FromBody] AuditProductRequest request)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("审核商品归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.AuditProductAsync(productId, request.Approved, request.Reason ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核商品失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "审核商品失败";
            }
            return result;
        }

        [HttpGet("{productId}/skus")]
        public async Task<ResultVM<List<ProductSKUState>>> GetProductSKUsAsync(long productId)
        {
            var result = new ResultVM<List<ProductSKUState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.GetProductSKUsAsync(productId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品SKU失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "获取SKU失败";
            }
            return result;
        }

        [HttpPost("{productId}/skus")]
        public async Task<ResultVM<ProductSKUState>> AddProductSKUAsync(long productId, [FromBody] AddSKURequest request)
        {
            var result = new ResultVM<ProductSKUState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("添加SKU归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.AddProductSKUAsync(new ProductSKUState
                {
                    ProductId = productId,
                    Color = request.Color,
                    Size = request.Size,
                    Version = request.Version,
                    SalePrice = request.SalePrice,
                    Stock = request.Stock
                });
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加商品SKU失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "添加SKU失败";
            }
            return result;
        }

        [HttpDelete("{productId}/skus/{skuId}")]
        public async Task<ResultVM<bool>> DeleteProductSKUAsync(long productId, long skuId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsProductOwnerAsync(client, productId, userId))
                {
                    _logger.LogWarning("删除SKU归属权校验失败: ProductId={ProductId}, UserId={UserId}", productId, userId);
                    result.ErrorMessage = "无权操作此商品";
                    return result;
                }

                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.DeleteProductSKUAsync(skuId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品SKU失败: ProductId={ProductId}, SKUId={SKUId}", productId, skuId);
                result.ErrorMessage = "删除SKU失败";
            }
            return result;
        }

        [HttpGet("suggested-price/{speciesId}")]
        public async Task<ResultVM<SuggestedPriceRange>> GetSuggestedPriceAsync(int speciesId)
        {
            var result = new ResultVM<SuggestedPriceRange>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductGrain>(speciesId);
                result.Data = await grain.GetSuggestedPriceAsync(speciesId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取建议价格失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "获取建议价格失败";
            }
            return result;
        }

        [HttpGet("price-suggestions/{merchantId}")]
        public async Task<ResultVM<List<PriceAdjustmentSuggestion>>> GetPriceAdjustmentSuggestionsAsync(long merchantId)
        {
            var result = new ResultVM<List<PriceAdjustmentSuggestion>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductGrain>(merchantId);
                result.Data = await grain.GetPriceAdjustmentSuggestionsAsync(merchantId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取调价建议失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取调价建议失败";
            }
            return result;
        }

        [HttpPost("presale")]
        public async Task<ResultVM<ProductState>> CreatePresaleProductAsync([FromBody] CreatePresaleProductRequest request)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (request?.Product == null)
                {
                    result.ErrorMessage = "商品数据不能为空";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, request.Product.MerchantId, userId))
                {
                    _logger.LogWarning("创建预售商品归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", request.Product.MerchantId, userId);
                    result.ErrorMessage = "无权操作此商户的商品";
                    return result;
                }

                var tempGrainId = DateTimeOffset.UtcNow.Ticks;
                var grain = client.GetGrain<IProductGrain>(tempGrainId);

                request.Product.IsPresale = true;
                request.Product.RelatedBatchId = request.RelatedBatchId;
                request.Product.PresaleDeliveryDate = request.PresaleDeliveryDate;

                var product = await grain.CreateProductAsync(request.Product);
                if (product == null)
                {
                    result.ErrorMessage = "创建预售商品失败";
                    return result;
                }

                var productGrain = client.GetGrain<IProductGrain>(product.ProductId);

                if (request.Skus != null)
                {
                    foreach (var sku in request.Skus)
                    {
                        sku.ProductId = product.ProductId;
                        await productGrain.AddProductSKUAsync(sku);
                    }
                }

                if (request.LadderPrices != null && request.LadderPrices.Count > 0)
                {
                    await productGrain.SetLadderPricesAsync(product.ProductId, request.LadderPrices);
                }

                result.Data = product;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建预售商品失败");
                result.ErrorMessage = "创建预售商品失败";
            }
            return result;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsMerchantOwnerAsync(IClusterClient client, long merchantId, Guid userId)
        {
            if (merchantId <= 0 || userId == Guid.Empty) return false;
            var merchantGrain = client.GetGrain<IMerchantGrain>(merchantId);
            var merchant = await merchantGrain.GetMerchantAsync();
            return merchant != null && merchant.UserId == userId;
        }

        private async Task<bool> IsProductOwnerAsync(IClusterClient client, long productId, Guid userId)
        {
            if (productId <= 0 || userId == Guid.Empty) return false;
            var productGrain = client.GetGrain<IProductGrain>(productId);
            var product = await productGrain.GetProductAsync();
            if (product == null || product.MerchantId <= 0) return false;
            return await IsMerchantOwnerAsync(client, product.MerchantId, userId);
        }
    }

    public class CreateProductRequest
    {
        public ProductState Product { get; set; }
        public List<ProductSKUState> Skus { get; set; } = new();
        public List<ProductLadderPriceState> LadderPrices { get; set; } = new();
    }

    public class AuditProductRequest
    {
        public bool Approved { get; set; }
        public string Reason { get; set; } = "";
    }

    public class AddSKURequest
    {
        public long ProductId { get; set; }
        public string Color { get; set; } = "";
        public string Size { get; set; } = "";
        public string Version { get; set; } = "";
        public decimal SalePrice { get; set; }
        public long Stock { get; set; }
    }

    public class CreatePresaleProductRequest
    {
        public ProductState Product { get; set; }
        public long? RelatedBatchId { get; set; }
        public DateTime? PresaleDeliveryDate { get; set; }
        public List<ProductSKUState> Skus { get; set; } = new();
        public List<ProductLadderPriceState> LadderPrices { get; set; } = new();
    }
}

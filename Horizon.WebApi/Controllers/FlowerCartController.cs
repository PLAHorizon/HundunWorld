using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;
using Horizon.Core.Abstract;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerCartController : OrleansControllerBase
    {
        private readonly ILogger<FlowerCartController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerCartController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerCartController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private async Task<Guid> ResolveUserGuid(IClusterClient client, string passportId)
        {
            var queryGrain = client.GetGrain<IFlowerQueryGrain>(0);
            return await queryGrain.GetUserIdAsync(passportId);
        }

        [HttpGet]
        public async Task<ResultVM<List<CartItemDto>>> GetCartAsync([FromQuery] string? passportId = null)
        {
            var result = new ResultVM<List<CartItemDto>>();
            try
            {
                var pid = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrWhiteSpace(pid))
                {
                    result.ErrorMessage = "请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var userId = await ResolveUserGuid(client, pid);
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户不存在";
                    return result;
                }

                var grain = client.GetGrain<IShoppingCartGrain>(userId);
                var cartState = await grain.GetCartAsync();

                var items = new List<CartItemDto>();
                if (cartState?.Items != null)
                {
                    foreach (var ci in cartState.Items)
                    {
                        var productGrain = client.GetGrain<IProductGrain>(ci.ProductId);
                        var product = await productGrain.GetProductAsync();

                        string merchantName = "";
                        if (product != null && product.MerchantId > 0)
                        {
                            var merchantGrain = client.GetGrain<IMerchantGrain>(product.MerchantId);
                            var merchant = await merchantGrain.GetMerchantAsync();
                            merchantName = merchant?.ShopName ?? "";
                        }

                        items.Add(new CartItemDto
                        {
                            CartItemId = ci.ProductId,
                            ProductId = ci.ProductId,
                            ProductName = product?.ProductName ?? "",
                            Price = product?.Price ?? 0,
                            Quantity = ci.Quantity,
                            MerchantName = merchantName,
                            MerchantId = product?.MerchantId ?? 0,
                            Stock = product?.Stock ?? 0
                        });
                    }
                }

                result.Data = items;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取购物车失败");
                result.ErrorMessage = "获取购物车失败";
            }
            return result;
        }

        [HttpPost("add")]
        public async Task<ResultVM<CartState>> AddItemAsync([FromBody] AddCartItemRequest request, [FromQuery] string? passportId = null)
        {
            var result = new ResultVM<CartState>();
            try
            {
                var pid = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrWhiteSpace(pid))
                {
                    result.ErrorMessage = "请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var userId = await ResolveUserGuid(client, pid);
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户不存在";
                    return result;
                }

                var grain = client.GetGrain<IShoppingCartGrain>(userId);
                result.Data = await grain.AddItemAsync(request.ProductId, request.Quantity);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加购物车商品失败");
                result.ErrorMessage = "添加购物车商品失败";
            }
            return result;
        }

        [HttpPut("update")]
        public async Task<ResultVM<CartState>> UpdateItemQuantityAsync([FromBody] UpdateCartItemRequest request, [FromQuery] string? passportId = null)
        {
            var result = new ResultVM<CartState>();
            try
            {
                var pid = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrWhiteSpace(pid))
                {
                    result.ErrorMessage = "请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var userId = await ResolveUserGuid(client, pid);
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户不存在";
                    return result;
                }

                var grain = client.GetGrain<IShoppingCartGrain>(userId);
                result.Data = await grain.UpdateItemQuantityAsync(request.ProductId, request.Quantity);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车商品数量失败");
                result.ErrorMessage = "更新购物车商品数量失败";
            }
            return result;
        }

        [HttpDelete("remove/{productId}")]
        public async Task<ResultVM<CartState>> RemoveItemAsync(long productId, [FromQuery] string? passportId = null)
        {
            var result = new ResultVM<CartState>();
            try
            {
                var pid = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrWhiteSpace(pid))
                {
                    result.ErrorMessage = "请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var userId = await ResolveUserGuid(client, pid);
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户不存在";
                    return result;
                }

                var grain = client.GetGrain<IShoppingCartGrain>(userId);
                result.Data = await grain.RemoveItemAsync(productId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除购物车商品失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "移除购物车商品失败";
            }
            return result;
        }

        [HttpDelete("clear")]
        public async Task<ResultVM<bool>> ClearCartAsync([FromQuery] string? passportId = null)
        {
            var result = new ResultVM<bool>();
            try
            {
                var pid = _passportCurrent?.PassportId ?? passportId;
                if (string.IsNullOrWhiteSpace(pid))
                {
                    result.ErrorMessage = "请先登录";
                    return result;
                }

                var client = await OrleansConnectClient();
                var userId = await ResolveUserGuid(client, pid);
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户不存在";
                    return result;
                }

                var grain = client.GetGrain<IShoppingCartGrain>(userId);
                await grain.ClearCartAsync();
                result.Data = true;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空购物车失败");
                result.ErrorMessage = "清空购物车失败";
            }
            return result;
        }
    }

    public class CartItemDto
    {
        public long CartItemId { get; set; }
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string MerchantName { get; set; } = "";
        public long MerchantId { get; set; }
        public int Stock { get; set; }
    }

    public class AddCartItemRequest
    {
        public long ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateCartItemRequest
    {
        public long ProductId { get; set; }
        public int Quantity { get; set; }
    }
}

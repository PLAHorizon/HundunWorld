using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerCouponService : FlowerApiServiceBase
{
    public FlowerCouponService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> CreateCouponAsync(object data) => await PostAsync<JsonElement?>("FlowerCoupon", data);

    public async Task<ResultVM<JsonElement?>?> GetCouponAsync(long couponId) => await GetAsync<JsonElement?>($"FlowerCoupon/{couponId}");

    public async Task<ResultVM<JsonElement?>?> GetShopCouponsAsync(long shopId) => await GetAsync<JsonElement?>($"FlowerCoupon/shop/{shopId}");

    public async Task<ResultVM<JsonElement?>?> ReceiveCouponAsync(long couponId, Guid userId) => await PostAsync<JsonElement?>($"FlowerCoupon/{couponId}/receive?userId={userId}");

    public async Task<ResultVM<JsonElement?>?> UseCouponAsync(long recordId, long orderId) => await PostAsync<JsonElement?>($"FlowerCoupon/use/{recordId}?orderId={orderId}");

    public async Task<ResultVM<JsonElement?>?> DisableCouponAsync(long couponId) => await PostAsync<JsonElement?>($"FlowerCoupon/{couponId}/disable");

    public async Task<ResultVM<JsonElement?>?> GetUserCouponsAsync(Guid userId) => await GetAsync<JsonElement?>($"FlowerCoupon/user/{userId}");
}

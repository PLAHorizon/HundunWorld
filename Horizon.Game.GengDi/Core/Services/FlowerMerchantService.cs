using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class MerchantInfo
    {
        public long MerchantId { get; set; }
        public string ShopName { get; set; } = "";
        public string Description { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public bool IsVerified { get; set; }
        public string MerchantType { get; set; } = "";
    }

    public class MerchantStateResponse
    {
        public long MerchantId { get; set; }
        public Guid UserId { get; set; }
        public int MerchantType { get; set; }
        public string ShopName { get; set; } = "";
        public string ShopDescription { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string BusinessLicense { get; set; } = "";
        public bool IsVerified { get; set; }
        public DateTime? VerifiedAt { get; set; }
    }

    public class ShopGradeInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int ProductLimit { get; set; }
        public int ImageLimit { get; set; }
        public int TemplateLimit { get; set; }
        public decimal ChargeStandard { get; set; }
        public string Remark { get; set; } = "";
    }

    public class ShopGradeStateResponse
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int ProductLimit { get; set; }
        public int ImageLimit { get; set; }
        public int TemplateLimit { get; set; }
        public decimal ChargeStandard { get; set; }
        public string Remark { get; set; } = "";
    }

    public class ShipperInfo
    {
        public long Id { get; set; }
        public string ShipperTag { get; set; } = "";
        public string ShipperName { get; set; } = "";
        public int RegionId { get; set; }
        public string Address { get; set; } = "";
        public string TelPhone { get; set; } = "";
        public bool IsDefaultSendGoods { get; set; }
    }

    public class ShopShipperState
    {
        public long Id { get; set; }
        public string ShipperTag { get; set; } = "";
        public string ShipperName { get; set; } = "";
        public int RegionId { get; set; }
        public string Address { get; set; } = "";
        public string TelPhone { get; set; } = "";
        public bool IsDefaultSendGoods { get; set; }
    }

    public class BrandInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Logo { get; set; } = "";
        public string Description { get; set; } = "";
        public int AuditStatus { get; set; }
    }

    public class CouponInfo
    {
        public long Id { get; set; }
        public long ShopId { get; set; }
        public string CouponName { get; set; } = "";
        public int CouponType { get; set; }
        public decimal Denomination { get; set; }
        public decimal UseCondition { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalCount { get; set; }
        public int ReceivedCount { get; set; }
        public int UsedCount { get; set; }
        public bool IsActive { get; set; }
        public int Status { get; set; }
        public string DisplayName => CouponType == 0 ? $"¥{Denomination}元券" : $"{Denomination}折券";
    }

    public class FullDiscountRuleInfo
    {
        public long Id { get; set; }
        public long ShopId { get; set; }
        public string RuleName { get; set; } = "";
        public decimal LimitValue { get; set; }
        public decimal DiscountValue { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
    }

    public class BusinessCategoryInfo
    {
        public long Id { get; set; }
        public long ShopId { get; set; }
        public long CategoryId { get; set; }
        public decimal CommissionRate { get; set; }
        public int AuditStatus { get; set; }
        public string AuditRemark { get; set; } = "";
    }

    public class CashDepositInfo
    {
        public long Id { get; set; }
        public long ShopId { get; set; }
        public long CategoryId { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public bool NoReasonReturn { get; set; }
    }

    public class PendingSettlementInfo
    {
        public long Id { get; set; }
        public long OrderId { get; set; }
        public long ShopId { get; set; }
        public decimal OrderAmount { get; set; }
        public decimal PlatformCommission { get; set; }
        public decimal SettleableAmount { get; set; }
        public int Status { get; set; }
    }

    public class AccountItemInfo
    {
        public long Id { get; set; }
        public long ShopId { get; set; }
        public int AccountType { get; set; }
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class SettlementAccountInfo
    {
        public long Id { get; set; }
        public long MerchantId { get; set; }
        public string BankName { get; set; } = "";
        public string AccountNo { get; set; } = "";
        public string AccountName { get; set; } = "";
    }

    public class SettlementBillInfo
    {
        public long Id { get; set; }
        public long MerchantId { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SettledAt { get; set; }
        public string Remark { get; set; } = "";
    }

    public class SettlementAccountState
    {
        public long Id { get; set; }
        public long MerchantId { get; set; }
        public string BankName { get; set; } = "";
        public string AccountNo { get; set; } = "";
        public string AccountName { get; set; } = "";
    }

    public class SettlementBillState
    {
        public long Id { get; set; }
        public long MerchantId { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SettledAt { get; set; }
        public string Remark { get; set; } = "";
    }

    public class FlowerMerchantService
    {
        public async Task<MerchantInfo?> GetMerchantAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMerchant/{merchantId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<MerchantStateResponse>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true || result.Data == null) return null;

                return MapMerchantStateToInfo(result.Data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMerchantService] {nameof(GetMerchantAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<MerchantInfo?> GetMyMerchantAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMerchant/my?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine($"[FlowerMerchant] 获取我的商户HTTP失败: {(int)response.StatusCode} Body={errorBody}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<MerchantStateResponse>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true || result.Data == null)
                {
                    Console.WriteLine($"[FlowerMerchant] 获取我的商户业务失败: ErrorMessage={result?.ErrorMessage}");
                    return null;
                }

                return MapMerchantStateToInfo(result.Data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 获取我的商户异常: {ex}");
                return null;
            }
        }

        public async Task<MerchantInfo?> RegisterMerchantAsync(string shopName, string description, string contactPhone, string merchantType)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var body = JsonSerializer.Serialize(new { PassportId = passportId, ShopName = shopName, ShopDescription = description, Description = description, ContactPhone = contactPhone, MerchantType = merchantType }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerMerchant/register", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine($"[FlowerMerchant] 注册商户HTTP失败: {(int)response.StatusCode} {response.StatusCode} Body={errorBody}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<MerchantStateResponse>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true || result.Data == null)
                {
                    Console.WriteLine($"[FlowerMerchant] 注册商户业务失败: IsSuccess={result?.IsSuccess}, ErrorMessage={result?.ErrorMessage}, DataIsNull={result?.Data == null}");
                    return null;
                }

                return MapMerchantStateToInfo(result.Data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerMerchant] 注册商户异常: {ex}");
                return null;
            }
        }

        public async Task<MerchantInfo?> UpdateMerchantAsync(long merchantId, string shopName, string description, string contactPhone)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopName = shopName, ShopDescription = description, ContactPhone = contactPhone }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerMerchant/{merchantId}", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<MerchantStateResponse>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true || result.Data == null) return null;

                return MapMerchantStateToInfo(result.Data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMerchantService] {nameof(UpdateMerchantAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> AuditMerchantAsync(long merchantId, bool approved, string refuseReason)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Approved = approved, RefuseReason = refuseReason }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerMerchant/{merchantId}/audit", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(AuditMerchantAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<ShopGradeInfo>?> GetShopGradesAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerShopGrade").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ShopGradeStateResponse>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapShopGradeToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopGradesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<BrandInfo>?> GetBrandsAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerBrand").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<BrandInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetBrandsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> ApplyBrandAsync(long shopId, string brandName, string proofMaterial)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, BrandName = brandName, ProofMaterial = proofMaterial }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerBrand/apply", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(ApplyBrandAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> ApplyBusinessCategoryAsync(long shopId, long categoryId, decimal commissionRate)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, CategoryId = categoryId, CommissionRate = commissionRate }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerBusinessCategory/apply", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(ApplyBusinessCategoryAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<BusinessCategoryInfo>?> GetShopBusinessCategoriesAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerBusinessCategory/shop/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<BusinessCategoryInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopBusinessCategoriesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<CashDepositInfo>?> GetShopCashDepositsAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCashDeposit/shop/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CashDepositInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopCashDepositsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> PayCashDepositAsync(long shopId, long categoryId, decimal amount, bool noReasonReturn)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, CategoryId = categoryId, Amount = amount, NoReasonReturn = noReasonReturn }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerCashDeposit/pay", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(PayCashDepositAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<CouponInfo>?> GetShopCouponsAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCoupon/shop/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CouponInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopCouponsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> CreateCouponAsync(long shopId, string name, int couponType, decimal denomination, decimal useCondition, DateTime startDate, DateTime endDate, int totalCount)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, CouponName = name, CouponType = couponType, Denomination = denomination, UseCondition = useCondition, StartDate = startDate, EndDate = endDate, TotalCount = totalCount }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerCoupon", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(CreateCouponAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> ReceiveCouponAsync(long couponId, long userId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerCoupon/{couponId}/receive?userId={userId}", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(ReceiveCouponAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<CouponInfo>?> GetUserCouponsAsync(Guid userId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCoupon/user/{userId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CouponInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetUserCouponsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<FullDiscountRuleInfo>?> GetShopFullDiscountRulesAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerFullDiscount/shop/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<FullDiscountRuleInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopFullDiscountRulesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> CreateFullDiscountRuleAsync(long shopId, string ruleName, decimal limitValue, decimal discountValue, DateTime startDate, DateTime endDate)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, RuleName = ruleName, LimitValue = limitValue, DiscountValue = discountValue, StartDate = startDate, EndDate = endDate, IsActive = true }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerFullDiscount", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(CreateFullDiscountRuleAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> DeleteFullDiscountRuleAsync(long ruleId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerFullDiscount/{ruleId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(DeleteFullDiscountRuleAsync)}: {ex.Message}"); return false; }
        }

        public async Task<decimal> CalculateFullDiscountAsync(long shopId, decimal orderAmount)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, OrderAmount = orderAmount }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerFullDiscount/calculate", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<decimal>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data ?? 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(CalculateFullDiscountAsync)}: {ex.Message}"); return 0; }
        }

        public async Task<List<ShipperInfo>?> GetShippersAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMerchant/{merchantId}/shippers").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ShopShipperState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapShipperToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShippersAsync)}: {ex.Message}"); return null; }
        }

        public async Task<ShipperInfo?> AddShipperAsync(long merchantId, string tag, string name, int regionId, string address, string phone, bool isDefault)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShipperTag = tag, ShipperName = name, RegionId = regionId, Address = address, TelPhone = phone, IsDefaultSendGoods = isDefault }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerMerchant/{merchantId}/shippers", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<ShopShipperState>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data != null ? MapShipperToInfo(result.Data) : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(AddShipperAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> DeleteShipperAsync(long merchantId, long shipperId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerMerchant/{merchantId}/shippers/{shipperId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(DeleteShipperAsync)}: {ex.Message}"); return false; }
        }

        public async Task<SettlementAccountInfo?> GetSettlementAccountAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSettlement/{merchantId}/account").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SettlementAccountState>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.Data == null) return null;
                return new SettlementAccountInfo
                {
                    Id = result.Data.Id,
                    MerchantId = result.Data.MerchantId,
                    BankName = result.Data.BankName ?? "",
                    AccountNo = result.Data.AccountNo ?? "",
                    AccountName = result.Data.AccountName ?? ""
                };
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetSettlementAccountAsync)}: {ex.Message}"); return null; }
        }

        public async Task<SettlementAccountInfo?> SaveSettlementAccountAsync(long merchantId, string bankName, string accountNo, string accountName)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { MerchantId = merchantId, BankName = bankName, AccountNo = accountNo, AccountName = accountName }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerSettlement/{merchantId}/account", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SettlementAccountState>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.Data == null) return null;
                return new SettlementAccountInfo
                {
                    Id = result.Data.Id,
                    MerchantId = result.Data.MerchantId,
                    BankName = result.Data.BankName ?? "",
                    AccountNo = result.Data.AccountNo ?? "",
                    AccountName = result.Data.AccountName ?? ""
                };
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(SaveSettlementAccountAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<SettlementBillInfo>?> GetSettlementBillsAsync(long merchantId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSettlement/{merchantId}/bills?page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<SettlementBillState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(s => new SettlementBillInfo
                {
                    Id = s.Id,
                    MerchantId = s.MerchantId,
                    Amount = s.Amount,
                    Status = s.Status,
                    CreatedAt = s.CreatedAt,
                    SettledAt = s.SettledAt,
                    Remark = s.Remark ?? ""
                }).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetSettlementBillsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<PendingSettlementInfo>?> GetPendingSettlementsAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerShopBilling/pending/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PendingSettlementInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetPendingSettlementsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> RequestWithdrawAsync(long shopId, decimal amount, string bankName, string accountNo, string accountName)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ShopId = shopId, Amount = amount, BankName = bankName, AccountNo = accountNo, AccountName = accountName }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerShopBilling/withdraw", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(RequestWithdrawAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<AccountItemInfo>?> GetShopAccountItemsAsync(long shopId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerShopBilling/account-items/{shopId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<AccountItemInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(GetShopAccountItemsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> FreezeMerchantAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerAdmin/merchant/{merchantId}/freeze", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(FreezeMerchantAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> UnfreezeMerchantAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerAdmin/merchant/{merchantId}/unfreeze", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(UnfreezeMerchantAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> AuditProductAsync(long productId, bool approved, string reason)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Approved = approved, Reason = reason }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerAdmin/product/{productId}/audit", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMerchantService] {nameof(AuditProductAsync)}: {ex.Message}"); return false; }
        }

        private static MerchantInfo MapMerchantStateToInfo(MerchantStateResponse s)
        {
            return new MerchantInfo
            {
                MerchantId = s.MerchantId,
                ShopName = s.ShopName,
                Description = s.ShopDescription,
                ContactPhone = s.ContactPhone,
                IsVerified = s.IsVerified,
                MerchantType = s.MerchantType == 1 ? "Enterprise" : "Individual"
            };
        }

        private static ShopGradeInfo MapShopGradeToInfo(ShopGradeStateResponse s)
        {
            return new ShopGradeInfo
            {
                Id = s.Id,
                Name = s.Name ?? "",
                ProductLimit = s.ProductLimit,
                ImageLimit = s.ImageLimit,
                TemplateLimit = s.TemplateLimit,
                ChargeStandard = s.ChargeStandard,
                Remark = s.Remark ?? ""
            };
        }

        private static ShipperInfo MapShipperToInfo(ShopShipperState s)
        {
            return new ShipperInfo
            {
                Id = s.Id,
                ShipperTag = s.ShipperTag ?? "",
                ShipperName = s.ShipperName ?? "",
                RegionId = s.RegionId,
                Address = s.Address ?? "",
                TelPhone = s.TelPhone ?? "",
                IsDefaultSendGoods = s.IsDefaultSendGoods
            };
        }
    }
}

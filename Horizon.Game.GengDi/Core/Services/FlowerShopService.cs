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
    public class RelatedProduct
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Price { get; set; }
        public decimal? MarketPrice { get; set; }
        public string MerchantName { get; set; } = "";
        public int Stock { get; set; }
        public string Unit { get; set; } = "束";
        public string ImageUrl { get; set; } = "";
        public long MerchantId { get; set; }
        public int SpeciesId { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsPresale { get; set; }
        public DateTime? PresaleDeliveryDate { get; set; }
        public int AuditStatus { get; set; }
    }

    public class CartItem
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

    public class ProductSKUInfo
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string SkuCode { get; set; } = "";
        public string Color { get; set; } = "";
        public string Size { get; set; } = "";
        public string Version { get; set; } = "";
        public decimal SalePrice { get; set; }
        public decimal CostPrice { get; set; }
        public long Stock { get; set; }
        public long? SafeStock { get; set; }
        public string ShowPic { get; set; } = "";
        public string DisplayName => $"{Color}{(string.IsNullOrEmpty(Size) ? "" : " / " + Size)}{(string.IsNullOrEmpty(Version) ? "" : " / " + Version)}";
    }

    public class LadderPriceItem
    {
        public int MinBatch { get; set; }
        public int MaxBatch { get; set; }
        public decimal Price { get; set; }
    }

    public class CategoryInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Depth { get; set; }
        public long ParentCategoryId { get; set; }
        public List<CategoryInfo> Children { get; set; } = new();
    }

    public class FreightTemplateInfo
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int ValuationMethod { get; set; }
        public bool IsFree { get; set; }
        public decimal FirstUnit { get; set; }
        public decimal FirstPrice { get; set; }
        public decimal ContinueUnit { get; set; }
        public decimal ContinuePrice { get; set; }
        public decimal? FreeConditionAmount { get; set; }
    }

    public class ShippingAddressInfo
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
        public string ShipTo { get; set; } = "";
        public string Phone { get; set; } = "";
        public string ProvinceName { get; set; } = "";
        public string CityName { get; set; } = "";
        public string DistrictName { get; set; } = "";
        public string Address { get; set; } = "";
        public bool IsDefault { get; set; }
        public string Passport { get; set; } = "";
        public string FullAddress => $"{ProvinceName}{CityName}{DistrictName}{Address}";
    }

    public class ProductCategoryState
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int Depth { get; set; }
        public long ParentCategoryId { get; set; }
    }

    public class FreightTemplateState
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int ValuationMethod { get; set; }
        public bool IsFree { get; set; }
        public decimal FirstUnit { get; set; }
        public decimal FirstPrice { get; set; }
        public decimal ContinueUnit { get; set; }
        public decimal ContinuePrice { get; set; }
        public decimal? FreeConditionAmount { get; set; }
    }

    public class FlowerShopService
    {
        public async Task<List<RelatedProduct>?> GetActiveProductsAsync(int speciesId = 0, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/active?speciesId={speciesId}&page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<RelatedProduct>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(GetActiveProductsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<RelatedProduct>?> GetMerchantProductsAsync(long merchantId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/merchant/{merchantId}?page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<RelatedProduct>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(GetMerchantProductsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<ProductSKUInfo>?> GetProductSKUsAsync(long productId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/{productId}/skus").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ProductSKUInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(GetProductSKUsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<ProductSKUInfo?> AddProductSKUAsync(long productId, string color, string size, string version, decimal salePrice, long stock)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ProductId = productId, Color = color, Size = size, Version = version, SalePrice = salePrice, Stock = stock }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProduct/{productId}/skus", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<ProductSKUInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(AddProductSKUAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> DeleteProductSKUAsync(long productId, long skuId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerProduct/{productId}/skus/{skuId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(DeleteProductSKUAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> AuditProductAsync(long productId, bool approved, string reason)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Approved = approved, Reason = reason }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProduct/{productId}/audit", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(AuditProductAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> AddToCartAsync(Guid userId, long productId, int quantity)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var body = JsonSerializer.Serialize(new { ProductId = productId, Quantity = quantity }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerCart/add?passportId={Uri.EscapeDataString(passportId)}", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<object>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        public async Task<List<CartItem>?> GetCartItemsAsync(Guid userId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCart?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CartItem>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(GetCartItemsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UpdateCartItemAsync(Guid userId, long productId, int quantity)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var body = JsonSerializer.Serialize(new { ProductId = productId, Quantity = quantity }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerCart/update?passportId={Uri.EscapeDataString(passportId)}", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<object>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(UpdateCartItemAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveFromCartAsync(Guid userId, long productId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerCart/remove/{productId}?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<object>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(RemoveFromCartAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ClearCartAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerCart/clear?passportId={Uri.EscapeDataString(passportId)}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<bool>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(ClearCartAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<decimal> CalculateFreightAsync(long templateId, decimal quantity, string regionId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Quantity = quantity, RegionId = regionId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerFreightTemplate/{templateId}/calculate", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<decimal>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data ?? 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(CalculateFreightAsync)}: {ex.Message}"); return 0; }
        }

        public async Task<List<FreightTemplateInfo>?> GetFreightTemplatesAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerFreightTemplate/merchant/{merchantId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<FreightTemplateState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapFreightTemplateToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(GetFreightTemplatesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> AddFreightTemplateAsync(long merchantId, string name, bool isFree, decimal firstPrice, decimal continuePrice)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { MerchantId = merchantId, Name = name, IsFree = isFree, FirstPrice = firstPrice, ContinuePrice = continuePrice }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerFreightTemplate", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(AddFreightTemplateAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> DeleteFreightTemplateAsync(long templateId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerFreightTemplate/{templateId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(DeleteFreightTemplateAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<CategoryInfo>?> GetCategoriesAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProductCategory/tree").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ProductCategoryState>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data?.Select(MapCategoryToInfo).ToList();
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(GetCategoriesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<LadderPriceItem>?> GetProductLadderPricesAsync(long productId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/{productId}/ladder-prices").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<LadderPriceItem>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(GetProductLadderPricesAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> CreateProductAsync(long merchantId, int speciesId, string productName, string description, decimal price, int stock,
            string unit = "", string images = "", long? categoryId = null, long? freightTemplateId = null,
            long? brandId = null, decimal? marketPrice = null, int maxBuyCount = 0, bool isOpenLadder = false,
            int productType = 0, List<ProductSKUInfo> skus = null, List<LadderPriceItem> ladderPrices = null)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new
                {
                    Product = new
                    {
                        MerchantId = merchantId, SpeciesId = speciesId, ProductName = productName,
                        Description = description, Price = price, Stock = stock, Unit = unit,
                        Images = images, CategoryId = categoryId, FreightTemplateId = freightTemplateId,
                        BrandId = brandId, MarketPrice = marketPrice, MaxBuyCount = maxBuyCount,
                        IsOpenLadder = isOpenLadder, ProductType = productType
                    },
                    Skus = skus,
                    LadderPrices = ladderPrices
                }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProduct", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(CreateProductAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ToggleProductActiveAsync(long productId, bool isActive)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(isActive.ToString().ToLowerInvariant(), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProduct/{productId}/toggle-active", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerShopService] {nameof(ToggleProductActiveAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteProductAsync(long productId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerProduct/{productId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(DeleteProductAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> UpdateProductAsync(long productId, string name, decimal price, int stock, string description, string images, long? categoryId, long? freightTemplateId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ProductName = name, Price = price, Stock = stock, Description = description, Images = images, CategoryId = categoryId, FreightTemplateId = freightTemplateId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerProduct/{productId}", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(UpdateProductAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> UpdateProductSortOrderAsync(long productId, int sortOrder, string productCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { SortOrder = sortOrder, ProductCode = productCode }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerProduct/{productId}/sort", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(UpdateProductSortOrderAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<ShippingAddressInfo>?> GetUserAddressesAsync(Guid userId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerShippingAddress/user/{userId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<ShippingAddressInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(GetUserAddressesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<ShippingAddressInfo?> AddShippingAddressAsync(Guid userId, string shipTo, string phone, string provinceName, string cityName, string districtName, string address, bool isDefault)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var body = JsonSerializer.Serialize(new { UserId = userId, ShipTo = shipTo, Phone = phone, ProvinceName = provinceName, CityName = cityName, DistrictName = districtName, Address = address, IsDefault = isDefault, Passport = passportId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerShippingAddress", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine($"[FlowerShop] 添加收货地址HTTP失败: {(int)response.StatusCode} {response.StatusCode} Body={errorBody}");
                    return null;
                }
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<ShippingAddressInfo>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true)
                {
                    Console.WriteLine($"[FlowerShop] 添加收货地址业务失败: ErrorMessage={result?.ErrorMessage}");
                    return null;
                }
                return result?.Data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerShop] 添加收货地址异常: {ex}");
                return null;
            }
        }

        public async Task<ShippingAddressInfo?> UpdateShippingAddressAsync(long addressId, Guid userId, string shipTo, string phone, string provinceName, string cityName, string districtName, string address, bool isDefault)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var passportId = AccountService.GetPassportId();
                var body = JsonSerializer.Serialize(new { Id = addressId, UserId = userId, ShipTo = shipTo, Phone = phone, ProvinceName = provinceName, CityName = cityName, DistrictName = districtName, Address = address, IsDefault = isDefault, Passport = passportId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerShippingAddress", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine($"[FlowerShop] 更新收货地址HTTP失败: {(int)response.StatusCode} Body={errorBody}");
                    return null;
                }
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<ShippingAddressInfo>>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true)
                {
                    Console.WriteLine($"[FlowerShop] 更新收货地址业务失败: ErrorMessage={result?.ErrorMessage}");
                    return null;
                }
                return result?.Data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerShop] 更新收货地址异常: {ex}");
                return null;
            }
        }

        public async Task<bool> DeleteShippingAddressAsync(Guid userId, long addressId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerShippingAddress/{userId}/{addressId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(DeleteShippingAddressAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> SetDefaultShippingAddressAsync(Guid userId, long addressId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerShippingAddress/{userId}/{addressId}/set-default", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(SetDefaultShippingAddressAsync)}: {ex.Message}"); return false; }
        }

        public async Task<ShippingAddressInfo?> GetDefaultShippingAddressAsync(Guid userId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerShippingAddress/user/{userId}/default").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<ShippingAddressInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.Data;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerShopService] {nameof(GetDefaultShippingAddressAsync)}: {ex.Message}"); return null; }
        }

        private static CategoryInfo MapCategoryToInfo(ProductCategoryState c)
        {
            return new CategoryInfo
            {
                Id = c.Id,
                Name = c.Name ?? "",
                Depth = c.Depth,
                ParentCategoryId = c.ParentCategoryId
            };
        }

        private static FreightTemplateInfo MapFreightTemplateToInfo(FreightTemplateState t)
        {
            return new FreightTemplateInfo
            {
                Id = t.Id,
                Name = t.Name ?? "",
                ValuationMethod = t.ValuationMethod,
                IsFree = t.IsFree,
                FirstUnit = t.FirstUnit,
                FirstPrice = t.FirstPrice,
                ContinueUnit = t.ContinueUnit,
                ContinuePrice = t.ContinuePrice,
                FreeConditionAmount = t.FreeConditionAmount
            };
        }
    }
}

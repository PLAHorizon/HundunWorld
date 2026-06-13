using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    public class FlowerMarketOverview
    {
        public decimal AvgPrice { get; set; }
        public decimal PriceChange { get; set; }
        public int AlertCount { get; set; }
        public int ActiveSpeciesCount { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public List<FlowerPriceSnapshot> Snapshots { get; set; } = new();
    }

    public class DashboardStatsDTO
    {
        public decimal TodayTradeAmount { get; set; }
        public int TradeCount { get; set; }
        public int ActiveSpeciesCount { get; set; }
        public int OnlineMerchantCount { get; set; }
    }

    public class RegionalTradeDataDTO
    {
        public string RegionName { get; set; } = "";
        public double DemandIndex { get; set; }
    }

    public class SupplyDemandDataDTO
    {
        public string SpeciesName { get; set; } = "";
        public int Supply { get; set; }
        public int Demand { get; set; }
        public decimal SupplyDemandRatio { get; set; }
    }

    public class RecentTransactionDTO
    {
        public string TradeTime { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string Market { get; set; } = "";
    }

    public class SuggestedPriceRangeInfo
    {
        public int SpeciesId { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public decimal AvgForecastPrice { get; set; }
        public string Reason { get; set; } = "";
    }

    public class PriceAdjustmentSuggestionInfo
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal CurrentPrice { get; set; }
        public decimal SuggestedPrice { get; set; }
        public decimal ChangePercent { get; set; }
        public string Reason { get; set; } = "";
    }

    public class FlowerMarketService : IDisposable
    {
        private readonly CancellationTokenSource _pollingCts = new();

        private sealed class FlowerMarketOverviewResult
        {
            public bool IsSuccess { get; set; }
            public FlowerMarketOverview? Data { get; set; }
        }

        private sealed class FlowerSpeciesListApiResult
        {
            public bool IsSuccess { get; set; }
            public List<FlowerSpeciesListItem> Data { get; set; }
        }

        private sealed class FlowerSpeciesListItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public async Task<FlowerMarketOverview?> GetMarketOverviewAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMarket/overview").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerMarketOverviewResult>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess != true) return null;

                return result.Data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetMarketOverviewAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<FlowerPriceSnapshot>?> GetPriceHistoryAsync(int speciesId, int days)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var endTime = DateTime.UtcNow;
                var startTime = endTime.AddDays(-days);
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMarket/species/{speciesId}/price-history?startTime={Uri.EscapeDataString(startTime.ToString("o"))}&endTime={Uri.EscapeDataString(endTime.ToString("o"))}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<FlowerPriceSnapshot>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetPriceHistoryAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<FlowerPriceForecast?> GetPriceForecastAsync(int speciesId, int horizonDays)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMarket/species/{speciesId}/forecast?horizonDays={horizonDays}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<FlowerPriceForecast>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetPriceForecastAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<RelatedProduct>?> GetRelatedProductsAsync(int speciesId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerMarket/species/{speciesId}/products").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<RelatedProduct>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetRelatedProductsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task StartPricePollingAsync(Action<FlowerPriceSnapshot> onPriceUpdate)
        {
            while (!_pollingCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _pollingCts.Token).ConfigureAwait(false);
                    var overview = await GetMarketOverviewAsync().ConfigureAwait(false);
                    if (overview?.Snapshots != null)
                    {
                        foreach (var snapshot in overview.Snapshots)
                        {
                            onPriceUpdate(snapshot);
                        }
                    }
                }
                catch (OperationCanceledException) when (_pollingCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FlowerMarketService] {nameof(StartPricePollingAsync)}: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), _pollingCts.Token).ConfigureAwait(false);
                }
            }
        }

        public Task StopPricePollingAsync()
        {
            _pollingCts.Cancel();
            return Task.CompletedTask;
        }

        public async Task<DashboardStatsDTO?> GetDashboardStatsAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerDashboard/stats").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<DashboardStatsDTO>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetDashboardStatsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<RegionalTradeDataDTO>?> GetRegionalTradeDataAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerDashboard/regional").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<RegionalTradeDataDTO>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetRegionalTradeDataAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<SupplyDemandDataDTO>?> GetSupplyDemandDataAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerDashboard/supply-demand").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<SupplyDemandDataDTO>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetSupplyDemandDataAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<RecentTransactionDTO>?> GetRecentTransactionsAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerDashboard/transactions").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<RecentTransactionDTO>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetRecentTransactionsAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<SuggestedPriceRangeInfo?> GetSuggestedPriceAsync(int speciesId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/suggested-price/{speciesId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SuggestedPriceRangeInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMarketService] {nameof(GetSuggestedPriceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<PriceAdjustmentSuggestionInfo>?> GetPriceAdjustmentSuggestionsAsync(long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerProduct/price-suggestions/{merchantId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PriceAdjustmentSuggestionInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerMarketService] {nameof(GetPriceAdjustmentSuggestionsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> CreatePresaleProductAsync(long merchantId, int speciesId, string productName, string description, decimal price, int stock,
            string unit = "", string images = "", long? categoryId = null, long? freightTemplateId = null,
            long? relatedBatchId = null, DateTime? presaleDeliveryDate = null,
            List<ProductSKUInfo> skus = null, List<LadderPriceItem> ladderPrices = null)
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
                        Images = images, CategoryId = categoryId, FreightTemplateId = freightTemplateId
                    },
                    RelatedBatchId = relatedBatchId,
                    PresaleDeliveryDate = presaleDeliveryDate,
                    Skus = skus,
                    LadderPrices = ladderPrices
                }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerProduct/presale", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(CreatePresaleProductAsync)}: {ex.Message}");
                return false;
            }
        }

        public async Task<Dictionary<int, string>> GetSpeciesListAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSpecies/list").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return GetDefaultSpecies();

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerSpeciesListApiResult>(json, FlowerHttpConfig.JsonOptions);
                if (result?.IsSuccess == true && result.Data != null && result.Data.Count > 0)
                {
                    var dict = new Dictionary<int, string>();
                    foreach (var item in result.Data)
                        dict[item.Id] = item.Name;
                    return dict;
                }

                return GetDefaultSpecies();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerMarketService] {nameof(GetSpeciesListAsync)}: {ex.Message}");
                return GetDefaultSpecies();
            }
        }

        private static Dictionary<int, string> GetDefaultSpecies() => new()
        {
            { 1, "红玫瑰" },
            { 2, "百合" },
            { 3, "康乃馨" },
            { 4, "混合花束" },
            { 5, "红绿搭配" }
        };

        public void Dispose()
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
        }
    }
}

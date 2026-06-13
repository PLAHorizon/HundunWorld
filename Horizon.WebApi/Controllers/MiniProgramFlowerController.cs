using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("api/miniprogram/[controller]")]
    public class MiniProgramFlowerController : ControllerBase
    {
        private readonly ILogger<MiniProgramFlowerController> _logger;
        private readonly IClusterClient _clusterClient;

        public MiniProgramFlowerController(
            ILogger<MiniProgramFlowerController> logger,
            IClusterClient clusterClient)
        {
            _logger = logger;
            _clusterClient = clusterClient;
        }

        [HttpGet("home")]
        public async Task<ResultVM<MiniProgramHomeData>> GetHomeDataAsync()
        {
            var result = new ResultVM<MiniProgramHomeData>();
            try
            {
                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(0);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                var homeData = new MiniProgramHomeData
                {
                    HotSpecies = new List<SpeciesCard>(),
                    Alerts = new List<AlertCard>()
                };

                if (snapshots != null && snapshots.Count > 0)
                {
                    homeData.AvgPrice = snapshots.Average(s => s.AvgPrice);
                    homeData.PriceChange = 0;
                    homeData.SpeciesCount = snapshots.Select(s => s.SpeciesId).Distinct().Count();

                    foreach (var snap in snapshots.Take(6))
                    {
                        homeData.HotSpecies.Add(new SpeciesCard
                        {
                            SpeciesId = (int)snap.SpeciesId,
                            AvgPrice = snap.AvgPrice,
                            MinPrice = snap.MinPrice,
                            MaxPrice = snap.MaxPrice,
                            Volume = snap.Volume
                        });
                    }
                }

                result.Data = homeData;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "小程序首页数据获取失败");
                result.ErrorMessage = "获取首页数据失败";
            }
            return result;
        }

        [HttpGet("species/{speciesId}/detail")]
        public async Task<ResultVM<SpeciesDetailData>> GetSpeciesDetailAsync(int speciesId)
        {
            var result = new ResultVM<SpeciesDetailData>();
            try
            {
                var marketGrain = _clusterClient.GetGrain<IFlowerMarketGrain>(0);
                var latest = await marketGrain.GetLatestSnapshotAsync(speciesId);

                var detail = new SpeciesDetailData
                {
                    SpeciesId = speciesId,
                    CurrentPrice = latest?.AvgPrice ?? 0,
                    MinPrice = latest?.MinPrice ?? 0,
                    MaxPrice = latest?.MaxPrice ?? 0,
                    Volume = latest?.Volume ?? 0
                };

                var speciesGrain = _clusterClient.GetGrain<IFlowerSpeciesGrain>(speciesId);
                var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);
                if (forecast?.PredictedPrices != null)
                {
                    detail.ForecastDays = forecast.PredictedPrices.Count;
                    detail.ForecastTrend = forecast.PredictedPrices.Count >= 2
                        ? (forecast.PredictedPrices.Last().PredictedPrice > forecast.PredictedPrices.First().PredictedPrice ? "up" : "down")
                        : "stable";
                    detail.ForecastConfidence = forecast.Confidence;
                }

                result.Data = detail;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "小程序品种详情获取失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "获取品种详情失败";
            }
            return result;
        }

        [HttpGet("products")]
        public async Task<ResultVM<List<ProductCard>>> GetProductsAsync([FromQuery] int speciesId = 0, [FromQuery] int page = 1, [FromQuery] int size = 10)
        {
            var result = new ResultVM<List<ProductCard>>();
            try
            {
                var productGrain = _clusterClient.GetGrain<IProductGrain>(0);
                var product = await productGrain.GetProductAsync();

                var products = new List<ProductCard>();
                if (product != null)
                {
                    products.Add(new ProductCard
                    {
                        ProductId = product.ProductId,
                        ProductName = product.ProductName,
                        Price = product.Price,
                        Stock = product.Stock
                    });
                }

                result.Data = products;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "小程序商品列表获取失败");
                result.ErrorMessage = "获取商品列表失败";
            }
            return result;
        }
    }

    public class MiniProgramHomeData
    {
        public decimal AvgPrice { get; set; }
        public decimal PriceChange { get; set; }
        public int SpeciesCount { get; set; }
        public List<SpeciesCard> HotSpecies { get; set; } = new();
        public List<AlertCard> Alerts { get; set; } = new();
    }

    public class SpeciesCard
    {
        public int SpeciesId { get; set; }
        public decimal AvgPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public int Volume { get; set; }
    }

    public class AlertCard
    {
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SpeciesDetailData
    {
        public int SpeciesId { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public int Volume { get; set; }
        public int ForecastDays { get; set; }
        public string ForecastTrend { get; set; }
        public double ForecastConfidence { get; set; }
    }

    public class ProductCard
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }
}

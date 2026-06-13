using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerProductGrain : Grain, IProductGrain
    {
        private readonly ILogger<FlowerProductGrain> _logger;
        private readonly IPersistentState<ProductState> _productState;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerInventoryChangeLog, long> _logContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProductSKU, long> _skuContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProductLadderPrice, long> _ladderPriceContext;
        private readonly IDataContext<FlowerEntityContext, FlowerMerchant, long> _merchantContext;

        public FlowerProductGrain(
            ILogger<FlowerProductGrain> logger,
            [PersistentState("product", "FlowerStore")] IPersistentState<ProductState> productState,
            IDataContext<FlowerEntityContext, FlowerProduct, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerInventoryChangeLog, long> logContext,
            IDataContext<FlowerEntityContext, FlowerProductSKU, long> skuContext,
            IDataContext<FlowerEntityContext, FlowerProductLadderPrice, long> ladderPriceContext,
            IDataContext<FlowerEntityContext, FlowerMerchant, long> merchantContext)
        {
            _logger = logger;
            _productState = productState;
            _dataContext = dataContext;
            _logContext = logContext;
            _skuContext = skuContext;
            _ladderPriceContext = ladderPriceContext;
            _merchantContext = merchantContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerProductGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_productState.State.ProductId == 0)
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == this.GetPrimaryKeyLong());
                if (entity != null)
                {
                    _productState.State = new ProductState
                    {
                        ProductId = entity.Id,
                        MerchantId = entity.MerchantId,
                        SpeciesId = entity.SpeciesId,
                        ProductName = entity.ProductName ?? "",
                        Description = entity.Description ?? "",
                        Price = entity.Price,
                        Stock = entity.Stock,
                        Unit = entity.Unit ?? "",
                        Images = entity.Images ?? "",
                        IsActive = entity.IsActive,
                        Version = entity.Version,
                        CategoryId = entity.CategoryId,
                        TypeId = entity.TypeId,
                        BrandId = entity.BrandId,
                        FreightTemplateId = entity.FreightTemplateId,
                        Weight = entity.Weight,
                        Volume = entity.Volume,
                        MaxBuyCount = entity.MaxBuyCount,
                        IsOpenLadder = entity.IsOpenLadder,
                        ProductType = entity.ProductType,
                        MarketPrice = entity.MarketPrice,
                        MinSalePrice = entity.MinSalePrice,
                        AuditStatus = entity.AuditStatus
                    };
                    _logger.LogInformation("FlowerProductGrain {GrainKey} loaded from database.", this.GetPrimaryKeyLong());
                }
            }

            await base.OnActivateAsync(cancellationToken);
        }

        public Task<ProductState> GetProductAsync()
        {
            return Task.FromResult(_productState.State);
        }

        public async Task<ProductState> CreateProductAsync(ProductState product)
        {
            try
            {

                var merchantEntity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == product.MerchantId);
                var passport = merchantEntity?.Passport ?? $"MERCHANT_{product.MerchantId}";

                var entity = new FlowerProduct
                {
                    MerchantId = product.MerchantId,
                    SpeciesId = product.SpeciesId,
                    ProductName = product.ProductName,
                    Description = product.Description,
                    Price = product.Price,
                    Stock = product.Stock,
                    Unit = product.Unit,
                    Images = product.Images,
                    IsActive = product.IsActive,
                    Version = 1,
                    CategoryId = product.CategoryId,
                    TypeId = product.TypeId,
                    BrandId = product.BrandId,
                    FreightTemplateId = product.FreightTemplateId,
                    Weight = product.Weight,
                    Volume = product.Volume,
                    MaxBuyCount = product.MaxBuyCount,
                    IsOpenLadder = product.IsOpenLadder,
                    ProductType = product.ProductType,
                    MarketPrice = product.MarketPrice,
                    MinSalePrice = product.MinSalePrice,
                    Passport = passport
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建商品失败: 数据库保存返回null");
                    return null;
                }

                product.ProductId = result.Id;
                product.Version = 1;
                _productState.State = product;
                await _productState.WriteStateAsync();

                _logger.LogInformation("创建商品: ProductId={ProductId}, MerchantId={MerchantId}, Passport={Passport}", result.Id, product.MerchantId, passport);
                return _productState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                throw;
            }
        }

        public async Task<ProductState> UpdateProductAsync(ProductState product)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == product.ProductId);
                if (entity == null) return null;
                entity.ProductName = product.ProductName;
                entity.Description = product.Description;
                entity.Price = product.Price;
                entity.Stock = product.Stock;
                entity.Unit = product.Unit;
                entity.Images = product.Images;
                entity.CategoryId = product.CategoryId;
                entity.TypeId = product.TypeId;
                entity.BrandId = product.BrandId;
                entity.FreightTemplateId = product.FreightTemplateId;
                entity.Weight = product.Weight;
                entity.Volume = product.Volume;
                entity.MaxBuyCount = product.MaxBuyCount;
                entity.IsOpenLadder = product.IsOpenLadder;
                entity.ProductType = product.ProductType;
                entity.MarketPrice = product.MarketPrice;
                entity.MinSalePrice = product.MinSalePrice;
                var wasInactive = !entity.IsActive;
                entity.IsActive = product.IsActive;
                if (wasInactive && product.IsActive)
                {
                    entity.AuditStatus = (int)Horizon.Game.Message.Enums.ProductAuditStatus.Pending;
                }
                await _dataContext.UpdateAsync(entity, entity.Id);
                _productState.State = product;
                _productState.State.IsActive = entity.IsActive;
                await _productState.WriteStateAsync();
                return _productState.State;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新商品失败: {ProductId}", product.ProductId);
                throw;
            }
        }

        public async Task<bool> SetProductActiveAsync(long productId, bool isActive)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == productId);
                if (entity == null)
                {
                    _logger.LogWarning("商品不存在: ProductId={ProductId}", productId);
                    return false;
                }
                entity.IsActive = isActive;
                await _dataContext.UpdateAsync(entity, entity.Id);
                _logger.LogInformation("商品状态已更新: ProductId={ProductId}, IsActive={IsActive}", productId, isActive);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换商品状态失败: ProductId={ProductId}", productId);
                return false;
            }
        }

        public async Task<bool> DeductStockAsync(int quantity, long orderId)
        {
            var lockKey = CacheConst.FlowerInventoryLockKey(this.GetPrimaryKeyLong());
            using (await Cache.AcquireLockAsync(lockKey, TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == this.GetPrimaryKeyLong());
                    if (entity == null || !entity.IsActive)
                    {
                        _logger.LogWarning("商品不存在或未上架: ProductId={ProductId}", this.GetPrimaryKeyLong());
                        return false;
                    }

                    if (entity.Stock < quantity)
                    {
                        _logger.LogWarning("库存不足: ProductId={ProductId}, Stock={Stock}, Required={Quantity}", this.GetPrimaryKeyLong(), entity.Stock, quantity);
                        return false;
                    }

                    var beforeQuantity = entity.Stock;
                    entity.Stock -= quantity;
                    entity.Version++;
                    await _dataContext.UpdateAsync(entity, entity.Id);

                    await _logContext.AddAsync(new FlowerInventoryChangeLog
                    {
                        ProductId = entity.Id,
                        BeforeQuantity = beforeQuantity,
                        AfterQuantity = entity.Stock,
                        ChangeReason = "OrderDeduct",
                        OrderId = orderId,
                        ChangedAt = DateTime.Now
                    });

                    _productState.State.Stock = entity.Stock;
                    _productState.State.Version = entity.Version;
                    await _productState.WriteStateAsync();

                    _logger.LogInformation("扣减库存: ProductId={ProductId}, Before={Before}, After={After}, OrderId={OrderId}",
                        this.GetPrimaryKeyLong(), beforeQuantity, entity.Stock, orderId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "扣减库存失败: ProductId={ProductId}", this.GetPrimaryKeyLong());
                    throw;
                }
            }
        }

        public async Task<bool> AddStockAsync(int quantity, string reason)
        {
            var lockKey = CacheConst.FlowerInventoryLockKey(this.GetPrimaryKeyLong());
            using (await Cache.AcquireLockAsync(lockKey, TimeSpan.FromSeconds(30)))
            {
                try
                {
                    var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == this.GetPrimaryKeyLong());
                    if (entity == null)
                    {
                        _logger.LogWarning("商品不存在: ProductId={ProductId}", this.GetPrimaryKeyLong());
                        return false;
                    }

                    var beforeQuantity = entity.Stock;
                    entity.Stock += quantity;
                    entity.Version++;
                    await _dataContext.UpdateAsync(entity, entity.Id);

                    await _logContext.AddAsync(new FlowerInventoryChangeLog
                    {
                        ProductId = entity.Id,
                        BeforeQuantity = beforeQuantity,
                        AfterQuantity = entity.Stock,
                        ChangeReason = reason,
                        OrderId = null,
                        ChangedAt = DateTime.Now
                    });

                    _productState.State.Stock = entity.Stock;
                    _productState.State.Version = entity.Version;
                    await _productState.WriteStateAsync();

                    _logger.LogInformation("增加库存: ProductId={ProductId}, Before={Before}, After={After}, Reason={Reason}",
                        this.GetPrimaryKeyLong(), beforeQuantity, entity.Stock, reason);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "增加库存失败: ProductId={ProductId}", this.GetPrimaryKeyLong());
                    throw;
                }
            }
        }

        public async Task<bool> DeleteProductAsync(long productId)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == productId);
                if (entity == null) return false;
                entity.IsDeleted = true;
                entity.IsActive = false;
                await _dataContext.UpdateAsync(entity, entity.Id);
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: {ProductId}", productId);
                throw;
            }
        }

        public async Task<ProductState> AuditProductAsync(long productId, bool approved, string reason)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == productId);
                if (entity == null) return null;
                entity.AuditStatus = approved ? (int)Horizon.Game.Message.Enums.ProductAuditStatus.Approved : (int)Horizon.Game.Message.Enums.ProductAuditStatus.Refused;
                await _dataContext.UpdateAsync(entity, entity.Id);
                return new ProductState { ProductId = entity.Id, AuditStatus = entity.AuditStatus };
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "审核商品失败: {ProductId}", productId);
                throw;
            }
        }

        public async Task<System.Collections.Generic.List<ProductSKUState>> GetProductSKUsAsync(long productId)
        {
            var entities = await _skuContext.QueryAsync(e => e.ProductId == productId);
            return entities.Select(e => new ProductSKUState
            {
                Id = e.Id,
                ProductId = e.ProductId,
                SkuCode = e.SkuCode ?? "",
                Color = e.Color ?? "",
                Size = e.Size ?? "",
                Version = e.Version ?? "",
                SalePrice = e.SalePrice,
                CostPrice = e.CostPrice,
                Stock = e.Stock,
                SafeStock = e.SafeStock,
                ShowPic = e.ShowPic ?? ""
            }).ToList();
        }

        public async Task<ProductSKUState> AddProductSKUAsync(ProductSKUState sku)
        {
            var productEntity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == sku.ProductId);
            var passport = productEntity?.Passport ?? $"MERCHANT_{sku.ProductId}";
            var entity = new Horizon.Model.Flower.FlowerProductSKU
            {
                ProductId = sku.ProductId,
                SkuCode = sku.SkuCode,
                Color = sku.Color,
                Size = sku.Size,
                Version = sku.Version,
                SalePrice = sku.SalePrice,
                CostPrice = sku.CostPrice,
                Stock = sku.Stock,
                SafeStock = sku.SafeStock,
                ShowPic = sku.ShowPic,
                Passport = passport
            };
            var result = await _skuContext.AddAsync(entity);
            return result != null ? new ProductSKUState
            {
                Id = result.Id,
                ProductId = result.ProductId,
                SkuCode = result.SkuCode ?? "",
                Color = result.Color ?? "",
                Size = result.Size ?? "",
                Version = result.Version ?? "",
                SalePrice = result.SalePrice,
                CostPrice = result.CostPrice,
                Stock = result.Stock,
                SafeStock = result.SafeStock,
                ShowPic = result.ShowPic ?? ""
            } : null;
        }

        public async Task<ProductSKUState> UpdateProductSKUAsync(ProductSKUState sku)
        {
            var entity = await _skuContext.QueryFirstOrDefaultAsync(e => e.Id == sku.Id);
            if (entity == null) return null;
            entity.SkuCode = sku.SkuCode;
            entity.Color = sku.Color;
            entity.Size = sku.Size;
            entity.Version = sku.Version;
            entity.SalePrice = sku.SalePrice;
            entity.CostPrice = sku.CostPrice;
            entity.Stock = sku.Stock;
            entity.SafeStock = sku.SafeStock;
            entity.ShowPic = sku.ShowPic;
            await _skuContext.UpdateAsync(entity, entity.Id);
            return sku;
        }

        public async Task<bool> DeleteProductSKUAsync(long skuId)
        {
            var entity = await _skuContext.QueryFirstOrDefaultAsync(e => e.Id == skuId);
            if (entity == null) return false;
            return await _skuContext.DeletedAsync<FlowerProductSKU, long>(skuId);
        }

        public async Task<System.Collections.Generic.List<ProductLadderPriceState>> GetLadderPricesAsync(long productId)
        {
            var entities = await _ladderPriceContext.QueryAsync(e => e.ProductId == productId);
            return entities.Select(e => new ProductLadderPriceState
            {
                Id = e.Id,
                ProductId = e.ProductId,
                MinBatch = e.MinBatch,
                MaxBatch = e.MaxBatch,
                Price = e.Price
            }).ToList();
        }

        public async Task SetLadderPricesAsync(long productId, System.Collections.Generic.List<ProductLadderPriceState> prices)
        {
            var productEntity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == productId);
            var passport = productEntity?.Passport ?? $"MERCHANT_{productId}";
            var existing = await _ladderPriceContext.QueryAsync(e => e.ProductId == productId);
            foreach (var item in existing)
            {
                await _ladderPriceContext.DeletedAsync<FlowerProductLadderPrice, long>(item.Id);
            }
            foreach (var price in prices)
            {
                var entity = new Horizon.Model.Flower.FlowerProductLadderPrice
                {
                    ProductId = productId,
                    MinBatch = price.MinBatch,
                    MaxBatch = price.MaxBatch,
                    Price = price.Price,
                    Passport = passport
                };
                await _ladderPriceContext.AddAsync(entity);
            }
        }

        public async Task<SuggestedPriceRange> GetSuggestedPriceAsync(int speciesId)
        {
            try
            {
                var speciesGrain = GrainFactory.GetGrain<IFlowerSpeciesGrain>(speciesId);
                var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);

                if (forecast == null || forecast.PredictedPrices == null || forecast.PredictedPrices.Count == 0)
                {
                    return new SuggestedPriceRange
                    {
                        SpeciesId = speciesId,
                        MinPrice = 0,
                        MaxPrice = 0,
                        AvgForecastPrice = 0,
                        Reason = "暂无预测数据"
                    };
                }

                var avgPrice = forecast.PredictedPrices.Average(p => p.PredictedPrice);
                var minPrice = avgPrice * 0.9m;
                var maxPrice = avgPrice * 1.1m;

                var trend = forecast.PredictedPrices.Last().PredictedPrice - forecast.PredictedPrices.First().PredictedPrice;
                var trendDesc = trend > 0 ? "预测价格呈上涨趋势" : trend < 0 ? "预测价格呈下跌趋势" : "预测价格保持稳定";

                return new SuggestedPriceRange
                {
                    SpeciesId = speciesId,
                    MinPrice = Math.Round(minPrice, 2),
                    MaxPrice = Math.Round(maxPrice, 2),
                    AvgForecastPrice = Math.Round(avgPrice, 2),
                    Reason = $"基于7日短期预测，置信度{forecast.Confidence:P0}，{trendDesc}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取建议价格失败: SpeciesId={SpeciesId}", speciesId);
                return new SuggestedPriceRange
                {
                    SpeciesId = speciesId,
                    Reason = $"获取预测数据失败: {ex.Message}"
                };
            }
        }

        public async Task<List<PriceAdjustmentSuggestion>> GetPriceAdjustmentSuggestionsAsync(long merchantId)
        {
            var suggestions = new List<PriceAdjustmentSuggestion>();

            try
            {
                var products = await _dataContext.QueryAsync(e => e.MerchantId == merchantId && e.IsActive && !e.IsDeleted);

                foreach (var product in products)
                {
                    try
                    {
                        if (product.SpeciesId <= 0) continue;

                        var speciesGrain = GrainFactory.GetGrain<IFlowerSpeciesGrain>(product.SpeciesId);
                        var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);

                        if (forecast == null || forecast.PredictedPrices == null || forecast.PredictedPrices.Count == 0)
                            continue;

                        var forecastPrice = forecast.PredictedPrices.Average(p => p.PredictedPrice);

                        if (product.Price <= 0) continue;

                        var changePercent = (forecastPrice - product.Price) / product.Price * 100;

                        if (Math.Abs(changePercent) > 15)
                        {
                            var direction = changePercent > 0 ? "上涨" : "下跌";
                            suggestions.Add(new PriceAdjustmentSuggestion
                            {
                                ProductId = product.Id,
                                ProductName = product.ProductName ?? "",
                                CurrentPrice = product.Price,
                                SuggestedPrice = Math.Round(forecastPrice, 2),
                                ChangePercent = Math.Round(changePercent, 1),
                                Reason = $"预测价格{direction}{Math.Abs(changePercent):F1}%，建议调整售价以适应市场趋势（置信度{forecast.Confidence:P0}）"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取商品{ProductId}调价建议失败", product.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商户调价建议失败: MerchantId={MerchantId}", merchantId);
            }

            return suggestions;
        }
    }
}

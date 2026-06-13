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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerShoppingCartGrain : Grain, IShoppingCartGrain
    {
        private readonly ILogger<FlowerShoppingCartGrain> _logger;
        private readonly IPersistentState<CartState> _cartState;
        private readonly IDataContext<FlowerEntityContext, FlowerShoppingCart, long> _dbCartContext;

        public FlowerShoppingCartGrain(
            ILogger<FlowerShoppingCartGrain> logger,
            [PersistentState("cart", "FlowerStore")] IPersistentState<CartState> cartState,
            IDataContext<FlowerEntityContext, FlowerShoppingCart, long> dbCartContext)
        {
            _logger = logger;
            _cartState = cartState;
            _dbCartContext = dbCartContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerShoppingCartGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_cartState.State.Items == null)
                _cartState.State.Items = new List<CartItemState>();

            _cartState.State.UserId = this.GetPrimaryKey();

            try
            {
                var cachedCart = await Cache.GetAsync<CartState>(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()));
                if (cachedCart != null && cachedCart.Items != null && cachedCart.Items.Count > 0)
                {
                    _cartState.State = cachedCart;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis读取购物车缓存失败，降级从数据库加载: UserId={UserId}", this.GetPrimaryKey());
                await LoadCartFromDbAsync();
            }

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<CartState> GetCartAsync()
        {
            try
            {
                var cachedCart = await Cache.GetAsync<CartState>(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()));
                if (cachedCart != null && cachedCart.Items != null)
                {
                    return cachedCart;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis读取购物车失败，降级从数据库读取: UserId={UserId}", this.GetPrimaryKey());
            }

            // Redis缓存为空或读取失败，从数据库加载
            try
            {
                var dbItems = await _dbCartContext.QueryAsync(e => e.UserId == this.GetPrimaryKey().ToString());
                if (dbItems != null && dbItems.Any())
                {
                    var state = new CartState
                    {
                        UserId = this.GetPrimaryKey(),
                        Items = dbItems.Select(i => new CartItemState
                        {
                            ProductId = i.ProductId,
                            Quantity = i.Quantity,
                            AddedTime = i.CreatedAt
                        }).ToList()
                    };
                    // 写入内存状态
                    _cartState.State = state;
                    return state;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库读取购物车失败: UserId={UserId}", this.GetPrimaryKey());
            }

            // 确保返回空Items而非null
            if (_cartState.State?.Items == null)
            {
                _cartState.State = new CartState
                {
                    UserId = this.GetPrimaryKey(),
                    Items = new List<CartItemState>()
                };
            }

            return _cartState.State;
        }

        public async Task<CartState> AddItemAsync(long productId, int quantity)
        {
            try
            {
                var state = _cartState.State;
                var existingItem = state.Items.Find(i => i.ProductId == productId);

                if (existingItem != null)
                {
                    existingItem.Quantity += quantity;
                }
                else
                {
                    state.Items.Add(new CartItemState
                    {
                        ProductId = productId,
                        Quantity = quantity,
                        AddedTime = DateTime.Now
                    });
                }

                await _cartState.WriteStateAsync();

                try
                {
                    await Cache.InsertAsync(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()), state, 60);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Redis写入购物车缓存失败，降级写入数据库: UserId={UserId}", this.GetPrimaryKey());
                    await PersistCartToDbAsync(state);
                }

                _logger.LogInformation("添加购物车: UserId={UserId}, ProductId={ProductId}, Quantity={Quantity}", this.GetPrimaryKey(), productId, quantity);
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加购物车失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<CartState> UpdateItemQuantityAsync(long productId, int quantity)
        {
            try
            {
                var state = _cartState.State;
                var existingItem = state.Items.Find(i => i.ProductId == productId);

                if (existingItem == null)
                {
                    _logger.LogWarning("购物车商品不存在: ProductId={ProductId}", productId);
                    return state;
                }

                if (quantity <= 0)
                {
                    state.Items.RemoveAll(i => i.ProductId == productId);
                }
                else
                {
                    existingItem.Quantity = quantity;
                }

                await _cartState.WriteStateAsync();

                try
                {
                    await Cache.InsertAsync(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()), state, 60);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Redis更新购物车缓存失败，降级写入数据库: UserId={UserId}", this.GetPrimaryKey());
                    await PersistCartToDbAsync(state);
                }

                _logger.LogInformation("更新购物车: UserId={UserId}, ProductId={ProductId}, Quantity={Quantity}", this.GetPrimaryKey(), productId, quantity);
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新购物车失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<CartState> RemoveItemAsync(long productId)
        {
            try
            {
                var state = _cartState.State;
                state.Items.RemoveAll(i => i.ProductId == productId);

                await _cartState.WriteStateAsync();

                try
                {
                    await Cache.InsertAsync(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()), state, 60);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Redis移除购物车缓存失败，降级写入数据库: UserId={UserId}", this.GetPrimaryKey());
                    await PersistCartToDbAsync(state);
                }

                try
                {
                    var dbItems = await _dbCartContext.QueryAsync(e => e.UserId == this.GetPrimaryKey().ToString() && e.ProductId == productId);
                    if (dbItems != null && dbItems.Any())
                    {
                        var ids = dbItems.Select(i => i.Id).ToList();
                        await _dbCartContext.DeletedsAsync<FlowerShoppingCart, long>(ids);
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "数据库移除购物车商品失败: UserId={UserId}, ProductId={ProductId}", this.GetPrimaryKey(), productId);
                }

                _logger.LogInformation("移除购物车商品: UserId={UserId}, ProductId={ProductId}", this.GetPrimaryKey(), productId);
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除购物车商品失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task ClearCartAsync()
        {
            try
            {
                _cartState.State.Items.Clear();
                await _cartState.WriteStateAsync();

                try
                {
                    await Cache.RemoveAsync(CacheConst.FlowerCartKey(this.GetPrimaryKey().ToString()));
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Redis清空购物车缓存失败，降级清空数据库: UserId={UserId}", this.GetPrimaryKey());
                }

                try
                {
                    var dbItems = await _dbCartContext.QueryAsync(e => e.UserId == this.GetPrimaryKey().ToString());
                    if (dbItems != null && dbItems.Any())
                    {
                        var ids = dbItems.Select(i => i.Id).ToList();
                        await _dbCartContext.DeletedsAsync<FlowerShoppingCart, long>(ids);
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "数据库清空购物车失败: UserId={UserId}", this.GetPrimaryKey());
                }

                _logger.LogInformation("清空购物车: UserId={UserId}", this.GetPrimaryKey());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空购物车失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        private async Task LoadCartFromDbAsync()
        {
            try
            {
                var dbItems = await _dbCartContext.QueryAsync(e => e.UserId == this.GetPrimaryKey().ToString());
                if (dbItems != null && dbItems.Any())
                {
                    _cartState.State.Items = dbItems.Select(i => new CartItemState
                    {
                        ProductId = i.ProductId,
                        Quantity = i.Quantity,
                        AddedTime = i.CreatedAt
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从数据库加载购物车失败: UserId={UserId}", this.GetPrimaryKey());
            }
        }

        private async Task PersistCartToDbAsync(CartState state)
        {
            try
            {
                var userId = this.GetPrimaryKey().ToString();
                var existingItems = await _dbCartContext.QueryAsync(e => e.UserId == userId);
                var existingDict = existingItems?.ToDictionary(i => i.ProductId) ?? new Dictionary<long, FlowerShoppingCart>();

                foreach (var item in state.Items)
                {
                    if (existingDict.TryGetValue(item.ProductId, out var existing))
                    {
                        existing.Quantity = item.Quantity;
                        existing.UpdatedAt = DateTime.Now;
                        await _dbCartContext.UpdateAsync(existing, existing.Id);
                    }
                    else
                    {
                        var newEntity = new FlowerShoppingCart
                        {
                            UserId = userId,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            CreatedAt = item.AddedTime,
                            UpdatedAt = DateTime.Now
                        };
                        await _dbCartContext.AddAsync(newEntity);
                    }
                }

                var currentProductIds = state.Items.Select(i => i.ProductId).ToHashSet();
                foreach (var existing in existingDict.Values)
                {
                    if (!currentProductIds.Contains(existing.ProductId))
                    {
                        await _dbCartContext.DeletedAsync<FlowerShoppingCart, long>(existing.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "购物车持久化到数据库失败: UserId={UserId}", this.GetPrimaryKey());
            }
        }
    }
}

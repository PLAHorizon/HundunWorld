using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 交易系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class TradeState
    {
        /// <summary>
        /// 卖方ID
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Guid SellerId { get; set; }

        /// <summary>
        /// 买方ID
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Guid BuyerId { get; set; }

        /// <summary>
        /// 卖方物品列表
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public List<TradeItem> SellerItems { get; set; } = new();

        /// <summary>
        /// 买方物品列表
        /// </summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public List<TradeItem> BuyerItems { get; set; } = new();

        /// <summary>
        /// 卖方出价货币
        /// </summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long SellerCurrency { get; set; }

        /// <summary>
        /// 买方出价货币
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long BuyerCurrency { get; set; }

        /// <summary>
        /// 卖方是否确认
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool SellerConfirmed { get; set; }

        /// <summary>
        /// 买方是否确认
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public bool BuyerConfirmed { get; set; }

        /// <summary>
        /// 交易状态
        /// </summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int Status { get; set; } = (int)TradeStatus.Created;

        /// <summary>
        /// 创建时间
        /// </summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 是否已创建
        /// </summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public bool IsCreated { get; set; }
    }

    /// <summary>
    /// 交易系统Grain实现 - 负责面对面交易管理
    /// </summary>
    public class TradeGrain : Grain, ITradeGrain
    {
        /// <summary>
        /// 面对面交易税率（5%）
        /// </summary>
        private const decimal TradeTaxRate = 0.05m;

        private readonly ILogger<TradeGrain> _logger;
        private readonly IPersistentState<TradeState> _tradeState;

        public TradeGrain(
            ILogger<TradeGrain> logger,
            [PersistentState("trade", "GameStore")] IPersistentState<TradeState> tradeState)
        {
            _logger = logger;
            _tradeState = tradeState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TradeGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_tradeState.State.SellerItems == null)
                _tradeState.State.SellerItems = new List<TradeItem>();
            if (_tradeState.State.BuyerItems == null)
                _tradeState.State.BuyerItems = new List<TradeItem>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> CreateTradeAsync(Guid sellerId, Guid buyerId)
        {
            try
            {
                var state = _tradeState.State;

                if (state.IsCreated)
                {
                    _logger.LogWarning("交易已创建: TradeId={TradeId}", this.GetPrimaryKey());
                    return false;
                }

                if (sellerId == Guid.Empty || buyerId == Guid.Empty)
                {
                    _logger.LogWarning("交易参与者ID无效");
                    return false;
                }

                if (sellerId == buyerId)
                {
                    _logger.LogWarning("不能与自己交易: PlayerId={PlayerId}", sellerId);
                    return false;
                }

                state.SellerId = sellerId;
                state.BuyerId = buyerId;
                state.Status = (int)TradeStatus.Created;
                state.CreatedTime = DateTime.UtcNow;
                state.IsCreated = true;
                state.SellerConfirmed = false;
                state.BuyerConfirmed = false;

                await _tradeState.WriteStateAsync();

                _logger.LogInformation("创建交易成功: TradeId={TradeId}, SellerId={SellerId}, BuyerId={BuyerId}",
                    this.GetPrimaryKey(), sellerId, buyerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建交易失败: SellerId={SellerId}, BuyerId={BuyerId}", sellerId, buyerId);
                throw;
            }
        }

        public async Task<bool> AddTradeItemAsync(Guid playerId, long itemId, int quantity)
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated || state.Status != (int)TradeStatus.Created)
                {
                    _logger.LogWarning("交易状态无效，无法添加物品: Status={Status}", (TradeStatus)state.Status);
                    return false;
                }

                if (itemId <= 0 || quantity <= 0)
                {
                    _logger.LogWarning("物品参数无效: ItemId={ItemId}, Quantity={Quantity}", itemId, quantity);
                    return false;
                }

                List<TradeItem> items;
                if (playerId == state.SellerId)
                {
                    items = state.SellerItems;
                }
                else if (playerId == state.BuyerId)
                {
                    items = state.BuyerItems;
                }
                else
                {
                    _logger.LogWarning("玩家不是交易参与者: PlayerId={PlayerId}", playerId);
                    return false;
                }

                // Reset confirmations when items change
                state.SellerConfirmed = false;
                state.BuyerConfirmed = false;

                var existing = items.FirstOrDefault(i => i.ItemId == itemId);
                if (existing != null)
                {
                    existing.Quantity += quantity;
                }
                else
                {
                    items.Add(new TradeItem
                    {
                        ItemId = itemId,
                        Quantity = quantity,
                        ItemName = ""
                    });
                }

                await _tradeState.WriteStateAsync();

                _logger.LogInformation("添加交易物品: PlayerId={PlayerId}, ItemId={ItemId}, Quantity={Quantity}",
                    playerId, itemId, quantity);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加交易物品失败: PlayerId={PlayerId}, ItemId={ItemId}", playerId, itemId);
                throw;
            }
        }

        public async Task<bool> RemoveTradeItemAsync(Guid playerId, long itemId)
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated || state.Status != (int)TradeStatus.Created)
                {
                    _logger.LogWarning("交易状态无效，无法移除物品: Status={Status}", (TradeStatus)state.Status);
                    return false;
                }

                List<TradeItem> items;
                if (playerId == state.SellerId)
                {
                    items = state.SellerItems;
                }
                else if (playerId == state.BuyerId)
                {
                    items = state.BuyerItems;
                }
                else
                {
                    _logger.LogWarning("玩家不是交易参与者: PlayerId={PlayerId}", playerId);
                    return false;
                }

                var removed = items.RemoveAll(i => i.ItemId == itemId);
                if (removed == 0)
                {
                    _logger.LogWarning("交易物品不存在: ItemId={ItemId}", itemId);
                    return false;
                }

                // Reset confirmations when items change
                state.SellerConfirmed = false;
                state.BuyerConfirmed = false;

                await _tradeState.WriteStateAsync();

                _logger.LogInformation("移除交易物品: PlayerId={PlayerId}, ItemId={ItemId}", playerId, itemId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除交易物品失败: PlayerId={PlayerId}, ItemId={ItemId}", playerId, itemId);
                throw;
            }
        }

        public async Task<bool> SetTradeCurrencyAsync(Guid playerId, long amount)
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated || state.Status != (int)TradeStatus.Created)
                {
                    _logger.LogWarning("交易状态无效，无法设置货币: Status={Status}", (TradeStatus)state.Status);
                    return false;
                }

                if (amount < 0)
                {
                    _logger.LogWarning("货币金额无效: Amount={Amount}", amount);
                    return false;
                }

                if (playerId == state.SellerId)
                {
                    state.SellerCurrency = amount;
                }
                else if (playerId == state.BuyerId)
                {
                    state.BuyerCurrency = amount;
                }
                else
                {
                    _logger.LogWarning("玩家不是交易参与者: PlayerId={PlayerId}", playerId);
                    return false;
                }

                // Reset confirmations when currency changes
                state.SellerConfirmed = false;
                state.BuyerConfirmed = false;

                await _tradeState.WriteStateAsync();

                _logger.LogInformation("设置交易货币: PlayerId={PlayerId}, Amount={Amount}", playerId, amount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置交易货币失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> ConfirmTradeAsync(Guid playerId)
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated || state.Status != (int)TradeStatus.Created)
                {
                    _logger.LogWarning("交易状态无效，无法确认: Status={Status}", (TradeStatus)state.Status);
                    return false;
                }

                if (playerId == state.SellerId)
                {
                    state.SellerConfirmed = true;
                }
                else if (playerId == state.BuyerId)
                {
                    state.BuyerConfirmed = true;
                }
                else
                {
                    _logger.LogWarning("玩家不是交易参与者: PlayerId={PlayerId}", playerId);
                    return false;
                }

                if (state.SellerConfirmed && state.BuyerConfirmed)
                {
                    state.Status = (int)TradeStatus.BothConfirmed;
                }

                await _tradeState.WriteStateAsync();

                _logger.LogInformation("确认交易: PlayerId={PlayerId}, SellerConfirmed={SellerConfirmed}, BuyerConfirmed={BuyerConfirmed}",
                    playerId, state.SellerConfirmed, state.BuyerConfirmed);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认交易失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public async Task<bool> CancelTradeAsync(Guid playerId)
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated)
                {
                    _logger.LogWarning("交易未创建");
                    return false;
                }

                if (state.Status == (int)TradeStatus.Completed || state.Status == (int)TradeStatus.Cancelled)
                {
                    _logger.LogWarning("交易已完成或已取消: Status={Status}", (TradeStatus)state.Status);
                    return false;
                }

                if (playerId != state.SellerId && playerId != state.BuyerId)
                {
                    _logger.LogWarning("玩家不是交易参与者: PlayerId={PlayerId}", playerId);
                    return false;
                }

                state.Status = (int)TradeStatus.Cancelled;
                await _tradeState.WriteStateAsync();

                _logger.LogInformation("取消交易: TradeId={TradeId}, PlayerId={PlayerId}", this.GetPrimaryKey(), playerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消交易失败: PlayerId={PlayerId}", playerId);
                throw;
            }
        }

        public Task<TradeInfo> GetTradeInfoAsync()
        {
            try
            {
                var state = _tradeState.State;

                var info = new TradeInfo
                {
                    TradeId = this.GetPrimaryKey(),
                    SellerId = state.SellerId,
                    BuyerId = state.BuyerId,
                    SellerItems = state.SellerItems.ToList(),
                    BuyerItems = state.BuyerItems.ToList(),
                    SellerCurrency = state.SellerCurrency,
                    BuyerCurrency = state.BuyerCurrency,
                    SellerConfirmed = state.SellerConfirmed,
                    BuyerConfirmed = state.BuyerConfirmed,
                    Status = state.Status,
                    CreatedTime = state.CreatedTime
                };

                return Task.FromResult(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取交易信息失败");
                throw;
            }
        }

        public async Task<TradeResult> ExecuteTradeAsync()
        {
            try
            {
                var state = _tradeState.State;

                if (!state.IsCreated)
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "交易未创建",
                        TradeId = this.GetPrimaryKey()
                    };
                }

                if (state.Status != (int)TradeStatus.BothConfirmed)
                {
                    return new TradeResult
                    {
                        Success = false,
                        Message = "双方尚未确认交易",
                        TradeId = this.GetPrimaryKey()
                    };
                }

                // Calculate 5% tax on currency
                long totalCurrency = state.SellerCurrency + state.BuyerCurrency;
                long tax = (long)(totalCurrency * TradeTaxRate);

                state.Status = (int)TradeStatus.Completed;
                await _tradeState.WriteStateAsync();

                _logger.LogInformation("交易执行成功: TradeId={TradeId}, TotalAmount={TotalAmount}, Tax={Tax}",
                    this.GetPrimaryKey(), totalCurrency, tax);

                return new TradeResult
                {
                    Success = true,
                    Message = "交易完成",
                    TradeId = this.GetPrimaryKey(),
                    TotalAmount = totalCurrency,
                    Tax = tax
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行交易失败: TradeId={TradeId}", this.GetPrimaryKey());

                var state = _tradeState.State;
                state.Status = (int)TradeStatus.Failed;
                await _tradeState.WriteStateAsync();

                throw;
            }
        }
    }
}

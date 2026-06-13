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
    public class FlowerRepurchaseReminderGrain : Grain, IFlowerRepurchaseReminderGrain
    {
        private readonly ILogger<FlowerRepurchaseReminderGrain> _logger;
        private readonly IPersistentState<RepurchaseReminderState> _state;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderItem, long> _itemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerRepurchaseRecord, long> _repurchaseContext;

        private const int MinPurchaseCountForCycle = 2;
        private const int DefaultCycleDays = 30;
        private const int MaxRemindersPerScan = 200;

        public FlowerRepurchaseReminderGrain(
            ILogger<FlowerRepurchaseReminderGrain> logger,
            [PersistentState("repurchase-reminder", "FlowerStore")] IPersistentState<RepurchaseReminderState> state,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerOrderItem, long> itemContext,
            IDataContext<FlowerEntityContext, FlowerRepurchaseRecord, long> repurchaseContext)
        {
            _logger = logger;
            _state = state;
            _orderContext = orderContext;
            _itemContext = itemContext;
            _repurchaseContext = repurchaseContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerRepurchaseReminderGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_state.State.PendingReminders == null)
                _state.State.PendingReminders = new List<RepurchaseReminderInfo>();

            RegisterTimer(async _ => await TriggerRepurchaseScanAsync(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(24));

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task TriggerRepurchaseScanAsync()
        {
            try
            {
                var now = DateTime.Now;
                _logger.LogInformation("复购提醒扫描开始: Time={Time}", now);

                var completedOrders = await _orderContext.QueryAsync(o => o.IsValid);
                var buyerOrders = completedOrders
                    .GroupBy(o => o.BuyerId)
                    .ToList();

                var newReminders = new List<RepurchaseReminderInfo>();

                foreach (var buyerGroup in buyerOrders)
                {
                    var buyerId = buyerGroup.Key;
                    var orders = buyerGroup
                        .Where(o => o.Status == (int)OrderStatus.Completed || o.Status == (int)OrderStatus.Delivered)
                        .OrderBy(o => o.CreateTime)
                        .ToList();

                    if (orders.Count < MinPurchaseCountForCycle)
                        continue;

                    var allItems = new List<(long OrderId, int SpeciesId, DateTime CreateTime)>();
                    foreach (var order in orders)
                    {
                        var items = await _itemContext.QueryAsync(i => i.OrderId == order.Id);
                        foreach (var item in items)
                        {
                            allItems.Add((order.Id, item.SpeciesId, order.CreateTime));
                        }
                    }

                    var speciesGroups = allItems
                        .GroupBy(x => x.SpeciesId)
                        .ToList();

                    foreach (var speciesGroup in speciesGroups)
                    {
                        var speciesId = speciesGroup.Key;
                        var purchaseTimes = speciesGroup
                            .Select(x => x.CreateTime)
                            .OrderBy(t => t)
                            .Distinct()
                            .ToList();

                        if (purchaseTimes.Count < MinPurchaseCountForCycle)
                            continue;

                        var intervals = new List<double>();
                        for (int i = 1; i < purchaseTimes.Count; i++)
                        {
                            intervals.Add((purchaseTimes[i] - purchaseTimes[i - 1]).TotalDays);
                        }

                        var averageCycleDays = intervals.Average();
                        var lastPurchaseTime = purchaseTimes.Last();
                        var daysSinceLastPurchase = (now - lastPurchaseTime).TotalDays;

                        if (daysSinceLastPurchase > averageCycleDays)
                        {
                            var lastOrderId = speciesGroup
                                .OrderByDescending(x => x.CreateTime)
                                .First().OrderId;

                            newReminders.Add(new RepurchaseReminderInfo
                            {
                                BuyerId = buyerId,
                                SpeciesId = speciesId,
                                LastOrderId = lastOrderId,
                                LastPurchaseTime = lastPurchaseTime,
                                AverageCycleDays = Math.Round(averageCycleDays, 1),
                                DaysSinceLastPurchase = (int)daysSinceLastPurchase,
                                ReminderMessage = $"您购买的品种(SpeciesId={speciesId})已超过平均复购周期({Math.Round(averageCycleDays, 1)}天)，距上次购买已{(int)daysSinceLastPurchase}天，欢迎再次选购！"
                            });
                        }
                    }

                    if (newReminders.Count >= MaxRemindersPerScan)
                        break;
                }

                _state.State.PendingReminders = newReminders;
                _state.State.LastScanTime = now;
                _state.State.LastScanReminderCount = newReminders.Count;
                await _state.WriteStateAsync();

                foreach (var reminder in newReminders)
                {
                    _logger.LogInformation("复购提醒推送: BuyerId={BuyerId}, SpeciesId={SpeciesId}, AverageCycleDays={AverageCycleDays}, DaysSinceLastPurchase={DaysSinceLastPurchase}, Message={Message}",
                        reminder.BuyerId, reminder.SpeciesId, reminder.AverageCycleDays, reminder.DaysSinceLastPurchase, reminder.ReminderMessage);
                }

                _logger.LogInformation("复购提醒扫描完成: TotalReminders={Count}, Time={Time}", newReminders.Count, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "复购提醒扫描失败");
                throw;
            }
        }

        public Task<DateTime> GetLastScanTimeAsync()
        {
            return Task.FromResult(_state.State.LastScanTime);
        }

        public Task<List<RepurchaseReminderInfo>> GetPendingRemindersAsync()
        {
            return Task.FromResult(_state.State.PendingReminders ?? new List<RepurchaseReminderInfo>());
        }
    }

    [GenerateSerializer]
    public class RepurchaseReminderState
    {
        [Id(0)]
        public DateTime LastScanTime { get; set; }

        [Id(1)]
        public int LastScanReminderCount { get; set; }

        [Id(2)]
        public List<RepurchaseReminderInfo> PendingReminders { get; set; } = new();
    }
}

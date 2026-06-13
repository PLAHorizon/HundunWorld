using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains
{
    public class FlowerCashDepositGrain : Grain, ICashDepositGrain
    {
        private readonly ILogger<FlowerCashDepositGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerCashDeposit, long> _context;

        public FlowerCashDepositGrain(
            ILogger<FlowerCashDepositGrain> logger,
            IDataContext<FlowerEntityContext, FlowerCashDeposit, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<CashDepositState> GetCashDepositAsync(long depositId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == depositId);
            return MapToState(entity);
        }

        public async Task<List<CashDepositState>> GetShopCashDepositsAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapToState).ToList();
        }

        public async Task<CashDepositState> PayCashDepositAsync(CashDepositState deposit)
        {
            var entity = new FlowerCashDeposit
            {
                ShopId = deposit.ShopId,
                CategoryId = deposit.CategoryId,
                Amount = deposit.Amount,
                Status = 1,
                PaidAt = System.DateTime.Now,
                NoReasonReturn = deposit.NoReasonReturn
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<CashDepositState> DeductCashDepositAsync(long depositId, decimal amount)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == depositId);
            if (entity == null) return null;
            entity.Amount -= amount;
            entity.Status = 2;
            entity.DeductedAt = System.DateTime.Now;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        private CashDepositState MapToState(FlowerCashDeposit entity)
        {
            if (entity == null) return null;
            return new CashDepositState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                CategoryId = entity.CategoryId,
                Amount = entity.Amount,
                Status = entity.Status,
                PaidAt = entity.PaidAt,
                DeductedAt = entity.DeductedAt,
                NoReasonReturn = entity.NoReasonReturn
            };
        }
    }
}

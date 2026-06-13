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
    public class FlowerSettledConfigGrain : Grain, ISettledConfigGrain
    {
        private readonly ILogger<FlowerSettledConfigGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerSettledConfig, long> _context;

        public FlowerSettledConfigGrain(
            ILogger<FlowerSettledConfigGrain> logger,
            IDataContext<FlowerEntityContext, FlowerSettledConfig, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<SettledConfigState> GetSettledConfigAsync()
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id > 0);
            return MapToState(entity);
        }

        public async Task<SettledConfigState> UpdateSettledConfigAsync(SettledConfigState config)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == config.Id);
            if (entity == null)
            {
                entity = new FlowerSettledConfig
                {
                    BusinessType = config.BusinessType,
                    SettlementAccountType = config.SettlementAccountType,
                    TrialDays = config.TrialDays,
                    IsCity = config.IsCity,
                    IsPeopleNumber = config.IsPeopleNumber,
                    IsAddress = config.IsAddress,
                    IsBusinessLicenseCode = config.IsBusinessLicenseCode,
                    IsBusinessScope = config.IsBusinessScope,
                    IsBusinessLicense = config.IsBusinessLicense
                };
                var result = await _context.AddAsync(entity);
                return MapToState(result);
            }
            entity.BusinessType = config.BusinessType;
            entity.SettlementAccountType = config.SettlementAccountType;
            entity.TrialDays = config.TrialDays;
            entity.IsCity = config.IsCity;
            entity.IsPeopleNumber = config.IsPeopleNumber;
            entity.IsAddress = config.IsAddress;
            entity.IsBusinessLicenseCode = config.IsBusinessLicenseCode;
            entity.IsBusinessScope = config.IsBusinessScope;
            entity.IsBusinessLicense = config.IsBusinessLicense;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        private SettledConfigState MapToState(FlowerSettledConfig entity)
        {
            if (entity == null) return null;
            return new SettledConfigState
            {
                Id = entity.Id,
                BusinessType = entity.BusinessType,
                SettlementAccountType = entity.SettlementAccountType,
                TrialDays = entity.TrialDays,
                IsCity = entity.IsCity,
                IsPeopleNumber = entity.IsPeopleNumber,
                IsAddress = entity.IsAddress,
                IsBusinessLicenseCode = entity.IsBusinessLicenseCode,
                IsBusinessScope = entity.IsBusinessScope,
                IsBusinessLicense = entity.IsBusinessLicense
            };
        }
    }
}

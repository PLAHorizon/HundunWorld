using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerSpeciesController : OrleansControllerBase
    {
        private readonly ILogger<FlowerSpeciesController> _logger;

        public FlowerSpeciesController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerSpeciesController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("list")]
        public async Task<ResultVM<List<FlowerSpeciesListItem>>> GetSpeciesListAsync()
        {
            var result = new ResultVM<List<FlowerSpeciesListItem>>();
            try
            {
                var client = await OrleansConnectClient();
                var marketGrain = client.GetGrain<IFlowerMarketGrain>(0);
                var snapshots = await marketGrain.GetMarketOverviewAsync();

                if (snapshots != null && snapshots.Count > 0)
                {
                    result.Data = new List<FlowerSpeciesListItem>();
                    foreach (var s in snapshots)
                    {
                        result.Data.Add(new FlowerSpeciesListItem { Id = (int)s.SpeciesId, Name = $"品种{s.SpeciesId}" });
                    }
                }
                else
                {
                    result.Data = GetDefaultSpeciesList();
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取花卉品种列表失败");
                result.Data = GetDefaultSpeciesList();
                result.IsSuccess = true;
            }
            return result;
        }

        private static List<FlowerSpeciesListItem> GetDefaultSpeciesList() => new()
        {
            new() { Id = 1, Name = "红玫瑰" },
            new() { Id = 2, Name = "百合" },
            new() { Id = 3, Name = "康乃馨" },
            new() { Id = 4, Name = "混合花束" },
            new() { Id = 5, Name = "红绿搭配" }
        };
    }

    public class FlowerSpeciesListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}

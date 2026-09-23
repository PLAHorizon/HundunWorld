using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.WebApi.Configs;
using Horizon.WebApi.Services;
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
        private readonly IFlowerSpeciesCatalogService _catalogService;

        public FlowerSpeciesController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerSpeciesController> logger,
            IClusterClient clusterClient,
            IFlowerSpeciesCatalogService catalogService)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _catalogService = catalogService;
        }

        [AllowAnonymous]
        [HttpGet("list")]
        public async Task<ResultVM<List<FlowerSpeciesListItem>>> GetSpeciesListAsync()
        {
            var result = new ResultVM<List<FlowerSpeciesListItem>>();
            try
            {
                // 品种目录来自 Redis 缓存（平台目录 + 斗南公开行情）：始终返回全部品种，
                // 即使行情 Grain 尚未落库，也能保证「商品发布」模块可选项（恢复花卉品种模拟数据）。
                var catalog = await _catalogService.GetAllAsync();

                result.Data = catalog.Select(item => new FlowerSpeciesListItem
                {
                    Id = (int)item.Id,
                    Name = item.Name,
                    Category = item.Category,
                    Origin = item.Origin,
                    Grade = item.Grade
                }).ToList();

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取花卉品种列表失败");
                result.IsSuccess = false;
                result.ErrorMessage = "获取花卉品种列表失败";
            }
            return result;
        }
    }

    public class FlowerSpeciesListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>品类（如 玫瑰 / 菊花）</summary>
        public string Category { get; set; } = "";
        /// <summary>花源产地（如 云南斗南）</summary>
        public string Origin { get; set; } = "";
        /// <summary>包装规格（如 18支/把）</summary>
        public string Grade { get; set; } = "";
    }
}

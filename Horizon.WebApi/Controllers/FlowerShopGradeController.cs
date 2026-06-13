using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [Route("FlowerShopGrade")]
    [ApiController]
    [Authorize]
    public class FlowerShopGradeController : OrleansControllerBase
    {
        private readonly ILogger<FlowerShopGradeController> _logger;
        private readonly IClusterClient _clusterClient;
        public FlowerShopGradeController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerShopGradeController> logger,
                                IClusterClient clusterClient)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _clusterClient = clusterClient;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ResultVM<List<ShopGradeState>>> GetAllGrades()
        {
            var result = new ResultVM<List<ShopGradeState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopGradeGrain>(0);
                result.Data = await grain.GetAllShopGradesAsync();
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取店铺等级列表失败");
                result.ErrorMessage = "获取店铺等级列表失败";
            }
            return result;
        }

        [HttpGet("{gradeId}")]
        public async Task<ResultVM<ShopGradeState>> GetGrade(long gradeId)
        {
            var result = new ResultVM<ShopGradeState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopGradeGrain>(0);
                result.Data = await grain.GetShopGradeAsync(gradeId);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取店铺等级失败: {GradeId}", gradeId);
                result.ErrorMessage = "获取店铺等级失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<ShopGradeState>> AddGrade([FromBody] ShopGradeState grade)
        {
            var result = new ResultVM<ShopGradeState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopGradeGrain>(0);
                result.Data = await grain.AddShopGradeAsync(grade);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "添加店铺等级失败");
                result.ErrorMessage = "添加店铺等级失败";
            }
            return result;
        }

        [HttpPut("{gradeId}")]
        public async Task<ResultVM<ShopGradeState>> UpdateGrade(long gradeId, [FromBody] ShopGradeState grade)
        {
            var result = new ResultVM<ShopGradeState>();
            try
            {
                grade.Id = gradeId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopGradeGrain>(0);
                result.Data = await grain.UpdateShopGradeAsync(grade);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新店铺等级失败: {GradeId}", gradeId);
                result.ErrorMessage = "更新店铺等级失败";
            }
            return result;
        }

        [HttpDelete("{gradeId}")]
        public async Task<ResultVM<bool>> DeleteGrade(long gradeId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopGradeGrain>(0);
                result.Data = await grain.DeleteShopGradeAsync(gradeId);
                result.IsSuccess = result.Data;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "删除店铺等级失败: {GradeId}", gradeId);
                result.ErrorMessage = "删除店铺等级失败";
            }
            return result;
        }
    }
}

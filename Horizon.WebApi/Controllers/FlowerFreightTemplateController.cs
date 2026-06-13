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
    [Route("FlowerFreightTemplate")]
    [ApiController]
    [Authorize]
    public class FlowerFreightTemplateController : OrleansControllerBase
    {
        private readonly ILogger<FlowerFreightTemplateController> _logger;
        private readonly IClusterClient _clusterClient;
        public FlowerFreightTemplateController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerFreightTemplateController> logger,
                                IClusterClient clusterClient)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _clusterClient = clusterClient;
            _logger = logger;
        }

        [HttpGet("merchant/{merchantId}")]
        public async Task<ResultVM<List<FreightTemplateState>>> GetMerchantTemplates(long merchantId)
        {
            var result = new ResultVM<List<FreightTemplateState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.GetMerchantTemplatesAsync(merchantId);
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取运费模板列表失败: {MerchantId}", merchantId);
                result.ErrorMessage = "获取运费模板列表失败";
            }
            return result;
        }

        [HttpGet("{templateId}")]
        public async Task<ResultVM<FreightTemplateState>> GetTemplate(long templateId)
        {
            var result = new ResultVM<FreightTemplateState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.GetTemplateAsync(templateId);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取运费模板失败: {TemplateId}", templateId);
                result.ErrorMessage = "获取运费模板失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<FreightTemplateState>> AddTemplate([FromBody] FreightTemplateState template)
        {
            var result = new ResultVM<FreightTemplateState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.AddTemplateAsync(template);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "添加运费模板失败");
                result.ErrorMessage = "添加运费模板失败";
            }
            return result;
        }

        [HttpPut("{templateId}")]
        public async Task<ResultVM<FreightTemplateState>> UpdateTemplate(long templateId, [FromBody] FreightTemplateState template)
        {
            var result = new ResultVM<FreightTemplateState>();
            try
            {
                template.Id = templateId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.UpdateTemplateAsync(template);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新运费模板失败: {TemplateId}", templateId);
                result.ErrorMessage = "更新运费模板失败";
            }
            return result;
        }

        [HttpDelete("{templateId}")]
        public async Task<ResultVM<bool>> DeleteTemplate(long templateId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.DeleteTemplateAsync(templateId);
                result.IsSuccess = result.Data;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "删除运费模板失败: {TemplateId}", templateId);
                result.ErrorMessage = "删除运费模板失败";
            }
            return result;
        }

        [HttpPost("{templateId}/calculate")]
        public async Task<ResultVM<decimal>> CalculateFreight(long templateId, [FromBody] CalculateFreightRequest request)
        {
            var result = new ResultVM<decimal>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFreightTemplateGrain>(0);
                result.Data = await grain.CalculateFreightAsync(templateId, request.Quantity, request.RegionId);
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "计算运费失败: {TemplateId}", templateId);
                result.ErrorMessage = "计算运费失败";
            }
            return result;
        }
    }

    public class CalculateFreightRequest
    {
        public decimal Quantity { get; set; }
        public string RegionId { get; set; }
    }
}

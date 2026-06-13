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
    [Route("FlowerProductCategory")]
    [ApiController]
    [Authorize]
    public class FlowerProductCategoryController : OrleansControllerBase
    {
        private readonly ILogger<FlowerProductCategoryController> _logger;
        private readonly IClusterClient _clusterClient;
        public FlowerProductCategoryController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerProductCategoryController> logger,
                                IClusterClient clusterClient)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _clusterClient = clusterClient;
            _logger = logger;
        }

        [HttpGet("tree")]
        public async Task<ResultVM<List<ProductCategoryState>>> GetCategoryTree()
        {
            var result = new ResultVM<List<ProductCategoryState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.GetCategoryTreeAsync();
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取分类树失败");
                result.ErrorMessage = "获取分类树失败";
            }
            return result;
        }

        [HttpGet("{categoryId}")]
        public async Task<ResultVM<ProductCategoryState>> GetCategory(long categoryId)
        {
            var result = new ResultVM<ProductCategoryState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.GetCategoryAsync(categoryId);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取分类失败: {CategoryId}", categoryId);
                result.ErrorMessage = "获取分类失败";
            }
            return result;
        }

        [HttpGet("{parentCategoryId}/children")]
        public async Task<ResultVM<List<ProductCategoryState>>> GetSubCategories(long parentCategoryId)
        {
            var result = new ResultVM<List<ProductCategoryState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.GetSubCategoriesAsync(parentCategoryId);
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取子分类失败: {ParentCategoryId}", parentCategoryId);
                result.ErrorMessage = "获取子分类失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<ProductCategoryState>> AddCategory([FromBody] ProductCategoryState category)
        {
            var result = new ResultVM<ProductCategoryState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.AddCategoryAsync(category);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "添加分类失败");
                result.ErrorMessage = "添加分类失败";
            }
            return result;
        }

        [HttpPut("{categoryId}")]
        public async Task<ResultVM<ProductCategoryState>> UpdateCategory(long categoryId, [FromBody] ProductCategoryState category)
        {
            var result = new ResultVM<ProductCategoryState>();
            try
            {
                category.Id = categoryId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.UpdateCategoryAsync(category);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新分类失败: {CategoryId}", categoryId);
                result.ErrorMessage = "更新分类失败";
            }
            return result;
        }

        [HttpDelete("{categoryId}")]
        public async Task<ResultVM<bool>> DeleteCategory(long categoryId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCategoryGrain>(0);
                result.Data = await grain.DeleteCategoryAsync(categoryId);
                result.IsSuccess = result.Data;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "删除分类失败: {CategoryId}", categoryId);
                result.ErrorMessage = "删除分类失败";
            }
            return result;
        }
    }
}

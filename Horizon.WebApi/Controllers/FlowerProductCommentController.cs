using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.Core.Abstract;
using Orleans;

namespace Horizon.WebApi.Controllers
{
    [Route("FlowerProductComment")]
    [ApiController]
    [Authorize]
    public class FlowerProductCommentController : OrleansControllerBase
    {
        private readonly ILogger<FlowerProductCommentController> _logger;

        public FlowerProductCommentController(ILogger<FlowerProductCommentController> logger)
        {
            _logger = logger;
        }

        [HttpGet("product/{productId}")]
        public async Task<ResultVM<List<ProductCommentState>>> GetProductComments(long productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<ProductCommentState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCommentGrain>(0);
                result.Data = await grain.GetProductCommentsAsync(productId, page, pageSize);
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取商品评价失败: {ProductId}", productId);
                result.ErrorMessage = "获取商品评价失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<ProductCommentState>> SubmitComment([FromBody] ProductCommentState comment)
        {
            var result = new ResultVM<ProductCommentState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCommentGrain>(0);
                result.Data = await grain.SubmitCommentAsync(comment);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "提交评价失败");
                result.ErrorMessage = "提交评价失败";
            }
            return result;
        }

        [HttpPost("{commentId}/reply")]
        public async Task<ResultVM<ProductCommentState>> ReplyComment(long commentId, [FromBody] ReplyCommentRequest request)
        {
            var result = new ResultVM<ProductCommentState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCommentGrain>(0);
                result.Data = await grain.ReplyCommentAsync(commentId, request.ReplyContent);
                result.IsSuccess = result.Data != null;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "回复评价失败: {CommentId}", commentId);
                result.ErrorMessage = "回复评价失败";
            }
            return result;
        }

        [HttpGet("merchant/{merchantId}")]
        public async Task<ResultVM<List<ProductCommentState>>> GetMerchantComments(long merchantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<ProductCommentState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductCommentGrain>(0);
                result.Data = await grain.GetMerchantCommentsAsync(merchantId, page, pageSize);
                result.IsSuccess = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "获取商户评价列表失败: {MerchantId}", merchantId);
                result.ErrorMessage = "获取评价列表失败";
            }
            return result;
        }
    }

    public class ReplyCommentRequest
    {
        public string ReplyContent { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;
using Horizon.Core.Abstract;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerAdminController : OrleansControllerBase
    {
        private readonly ILogger<FlowerAdminController> _logger;
        private readonly IPassportCurrentUser _passportCurrentUser;
        public FlowerAdminController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerAdminController> logger,
            IClusterClient clusterClient, IPassportCurrentUser passportCurrentUser)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrentUser = passportCurrentUser;
        }

        [HttpPost("merchant/{merchantId}/audit")]
        public async Task<ResultVM<MerchantState>> AuditMerchantAsync(long merchantId, [FromBody] AuditMerchantRequest request)
        {
            var result = new ResultVM<MerchantState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.AuditMerchantAsync(merchantId, request.Approved, request.Reason ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "平台审核商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "审核商户失败";
            }
            return result;
        }

        [HttpPost("merchant/{merchantId}/freeze")]
        public async Task<ResultVM<bool>> FreezeMerchantAsync(long merchantId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.FreezeMerchantAsync(merchantId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "冻结商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "冻结商户失败";
            }
            return result;
        }

        [HttpPost("merchant/{merchantId}/unfreeze")]
        public async Task<ResultVM<bool>> UnfreezeMerchantAsync(long merchantId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IMerchantGrain>(merchantId);
                result.Data = await grain.UnfreezeMerchantAsync(merchantId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解冻商户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "解冻商户失败";
            }
            return result;
        }

        [HttpPost("product/{productId}/audit")]
        public async Task<ResultVM<ProductState>> AuditProductAsync(long productId, [FromBody] AuditProductRequest request)
        {
            var result = new ResultVM<ProductState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IProductGrain>(productId);
                result.Data = await grain.AuditProductAsync(productId, request.Approved, request.Reason ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "平台审核商品失败: ProductId={ProductId}", productId);
                result.ErrorMessage = "审核商品失败";
            }
            return result;
        }

        [HttpPost("refund/{refundId}/platform-audit")]
        public async Task<ResultVM<OrderRefundState>> PlatformAuditRefundAsync(long refundId, [FromBody] PlatformAuditRefundRequest request)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.PlatformAuditRefundAsync(refundId, request.Approved, request.Remark ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "平台审核退款失败: RefundId={RefundId}", refundId);
                result.ErrorMessage = "审核退款失败";
            }
            return result;
        }

        [HttpGet("statistics")]
        public async Task<ResultVM<AdminStatisticsState>> GetStatisticsAsync()
        {
            var result = new ResultVM<AdminStatisticsState>();
            try
            {
                var client = await OrleansConnectClient();
                var dashboardGrain = client.GetGrain<IDashboardGrain>(0);
                var overview = await dashboardGrain.GetOverviewAsync();
                result.Data = new AdminStatisticsState
                {
                    TotalMerchants = 0,
                    TotalProducts = 0,
                    TotalOrders = overview?.TotalOrderCount ?? 0,
                    TotalRevenue = overview?.TotalTransactionAmount ?? 0
                };
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取平台统计失败");
                result.ErrorMessage = "获取平台统计失败";
            }
            return result;
        }

        private bool CheckAdmin()
        {
            var user = HttpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            var roleClaim = user.FindFirst("Role") ?? user.FindFirst("role") ?? user.FindFirst(ClaimTypes.Role);
            if (roleClaim != null && (roleClaim.Value == "Admin" || roleClaim.Value == "System" || roleClaim.Value == $"{(int)PassportType.System}"))
                return true;

            var passportTypeClaim = user.FindFirst("PassportType");
            if (passportTypeClaim != null && passportTypeClaim.Value == $"{(int)PassportType.System}")
                return true;

            return false;
        }

        [HttpGet("merchants")]
        public async Task<ResultVM<List<MerchantState>>> GetMerchantsAsync()
        {
            var result = new ResultVM<List<MerchantState>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: IFlowerQueryGrain 缺少 QueryAllMerchantsAsync 方法，后续需要添加
                result.Data = new List<MerchantState>();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商户列表失败");
                result.ErrorMessage = "获取商户列表失败";
            }
            return result;
        }

        [HttpGet("orders")]
        public async Task<ResultVM<List<OrderState>>> GetOrdersAsync([FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<OrderState>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: IFlowerQueryGrain 缺少 QueryAllOrdersAsync 方法，后续需要添加
                result.Data = new List<OrderState>();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单列表失败");
                result.ErrorMessage = "获取订单列表失败";
            }
            return result;
        }

        [HttpGet("payments")]
        public async Task<ResultVM<List<JsonElement>>> GetPaymentsAsync()
        {
            var result = new ResultVM<List<JsonElement>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: 暂无支付列表查询Grain方法，后续需要添加
                result.Data = new List<JsonElement>();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取支付列表失败");
                result.ErrorMessage = "获取支付列表失败";
            }
            return result;
        }

        [HttpGet("refunds")]
        public async Task<ResultVM<List<OrderRefundState>>> GetRefundsAsync()
        {
            var result = new ResultVM<List<OrderRefundState>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: IOrderRefundGrain 缺少 ListAllRefundsAsync 方法，后续需要添加
                result.Data = new List<OrderRefundState>();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取退款列表失败");
                result.ErrorMessage = "获取退款列表失败";
            }
            return result;
        }

        [HttpGet("iot-devices")]
        public async Task<ResultVM<List<FlowerIoTDeviceInfo>>> GetIoTDevicesAsync()
        {
            var result = new ResultVM<List<FlowerIoTDeviceInfo>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(_passportCurrentUser.PassportId);
                result.Data = await grain.ListAllDevicesAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取IoT设备列表失败");
                result.Data = new List<FlowerIoTDeviceInfo>();
                result.ErrorMessage = "获取IoT设备列表失败";
            }
            return result;
        }

        [HttpGet("forecast-models")]
        public async Task<ResultVM<List<JsonElement>>> GetForecastModelsAsync()
        {
            var result = new ResultVM<List<JsonElement>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: 暂无预测模型列表查询Grain方法，后续需要添加
                result.Data = new List<JsonElement>();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取预测模型列表失败");
                result.ErrorMessage = "获取预测模型列表失败";
            }
            return result;
        }

        [HttpGet("ai-documents")]
        public async Task<ResultVM<List<long>>> GetAIDocumentsAsync()
        {
            var result = new ResultVM<List<long>>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IKnowledgeBaseGrain>(0);
                result.Data = await grain.GetUnindexedDocumentsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取AI文档列表失败");
                result.ErrorMessage = "获取AI文档列表失败";
            }
            return result;
        }

        [HttpPost("ai-documents")]
        public async Task<ResultVM<long>> CreateAIDocumentAsync([FromBody] AdminCreateDocumentRequest request)
        {
            var result = new ResultVM<long>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IKnowledgeBaseGrain>(0);
                result.Data = await grain.UploadDocumentAsync(request.Title, request.Content, request.Source);
                result.IsSuccess = result.Data > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建AI文档失败");
                result.ErrorMessage = "创建AI文档失败";
            }
            return result;
        }

        [HttpDelete("ai-documents/{documentId}")]
        public async Task<ResultVM<bool>> DeleteAIDocumentAsync(long documentId)
        {
            var result = new ResultVM<bool>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: IKnowledgeBaseGrain 缺少删除文档方法，后续需要添加
                result.Data = false;
                result.ErrorMessage = "暂不支持删除文档";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除AI文档失败: DocumentId={DocumentId}", documentId);
                result.ErrorMessage = "删除AI文档失败";
            }
            return result;
        }

        [HttpPost("ai-documents/{documentId}/reindex")]
        public async Task<ResultVM<int>> ReindexAIDocumentAsync(long documentId)
        {
            var result = new ResultVM<int>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IEmbeddingGrain>(0);
                result.Data = await grain.EmbedDocumentsAsync(new List<long> { documentId });
                result.IsSuccess = result.Data > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重建AI文档索引失败: DocumentId={DocumentId}", documentId);
                result.ErrorMessage = "重建AI文档索引失败";
            }
            return result;
        }

        [HttpPost("forecast-models/{speciesId}/toggle")]
        public async Task<ResultVM<bool>> ToggleForecastModelAsync(int speciesId)
        {
            var result = new ResultVM<bool>();
            try
            {
                if (!CheckAdmin())
                {
                    result.ErrorMessage = "无权限访问";
                    HttpContext.Response.StatusCode = 401;
                    return result;
                }

                // TODO: 暂无切换预测模型Grain方法，后续需要添加
                result.Data = false;
                result.ErrorMessage = "暂不支持切换预测模型";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换预测模型失败: SpeciesId={SpeciesId}", speciesId);
                result.ErrorMessage = "切换预测模型失败";
            }
            return result;
        }
    }

    public class AdminCreateDocumentRequest
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public class PlatformAuditRefundRequest
    {
        public bool Approved { get; set; }
        public string Remark { get; set; } = "";
    }

    public class AdminStatisticsState
    {
        public int TotalMerchants { get; set; }
        public int TotalProducts { get; set; }
        public int TotalOrders { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}

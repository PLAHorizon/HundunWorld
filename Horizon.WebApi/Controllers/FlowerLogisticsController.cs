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
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerLogisticsController : OrleansControllerBase
    {
        private readonly ILogger<FlowerLogisticsController> _logger;

        public FlowerLogisticsController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerLogisticsController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{orderId}")]
        public async Task<ResultVM<LogisticsTrackState>> GetTrack(long orderId)
        {
            var result = new ResultVM<LogisticsTrackState>();
            try
            {
                var client = await OrleansConnectClient();
                var orderGrain = client.GetGrain<IOrderGrain>(orderId);
                var order = await orderGrain.GetOrderAsync();
                if (order == null)
                {
                    result.ErrorMessage = "订单不存在";
                    return result;
                }
                var logisticsGrain = client.GetGrain<ILogisticsGrain>(orderId);
                result.Data = await logisticsGrain.QueryTrackAsync(orderId, order.ExpressCompanyName ?? "", order.ShipOrderNumber ?? "");
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询物流信息失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "查询物流信息失败";
            }
            return result;
        }

        [HttpGet("{orderId}/map")]
        public async Task<ResultVM<LogisticsMapData>> GetMapData(long orderId)
        {
            var result = new ResultVM<LogisticsMapData>();
            try
            {
                var client = await OrleansConnectClient();
                var orderGrain = client.GetGrain<IOrderGrain>(orderId);
                var order = await orderGrain.GetOrderAsync();
                if (order == null)
                {
                    result.ErrorMessage = "订单不存在";
                    return result;
                }
                var logisticsGrain = client.GetGrain<ILogisticsGrain>(orderId);
                var trackState = await logisticsGrain.QueryTrackAsync(orderId, order.ExpressCompanyName ?? "", order.ShipOrderNumber ?? "");

                var mapData = new LogisticsMapData
                {
                    OrderId = orderId,
                    ExpressCompanyName = trackState.ExpressCompanyName,
                    ShipOrderNumber = trackState.ShipOrderNumber,
                    OriginCity = trackState.OriginCity,
                    DestinationCity = trackState.DestinationCity,
                    LogisticsStatus = trackState.LogisticsStatus
                };

                if (!string.IsNullOrEmpty(trackState.TrackData))
                {
                    try
                    {
                        var traces = JArray.Parse(trackState.TrackData);
                        foreach (var trace in traces)
                        {
                            var node = new LogisticsMapNode
                            {
                                Time = trace["time"]?.Value<DateTime>() ?? DateTime.MinValue,
                                Description = trace["context"]?.Value<string>() ?? "",
                                Location = trace["location"]?.Value<string>() ?? ""
                            };
                            var latToken = trace["latitude"];
                            var lngToken = trace["longitude"];
                            if (latToken != null && latToken.Type != JTokenType.Null)
                                node.Latitude = latToken.Value<double>();
                            if (lngToken != null && lngToken.Type != JTokenType.Null)
                                node.Longitude = lngToken.Value<double>();
                            mapData.Nodes.Add(node);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                result.Data = mapData;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询物流地图数据失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "查询物流地图数据失败";
            }
            return result;
        }

        [HttpGet("{orderId}/history")]
        public async Task<ResultVM<List<LogisticsTrackState>>> GetTrackHistory(long orderId)
        {
            var result = new ResultVM<List<LogisticsTrackState>>();
            try
            {
                var client = await OrleansConnectClient();
                var logisticsGrain = client.GetGrain<ILogisticsGrain>(orderId);
                result.Data = await logisticsGrain.GetTrackHistoryAsync(orderId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询物流历史失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "查询物流历史失败";
            }
            return result;
        }

        [HttpGet("return/{refundId}")]
        public async Task<ResultVM<LogisticsTrackState>> GetReturnTrack(long refundId, [FromQuery] string expressCompanyName, [FromQuery] string shipOrderNumber)
        {
            var result = new ResultVM<LogisticsTrackState>();
            try
            {
                var client = await OrleansConnectClient();
                var logisticsGrain = client.GetGrain<ILogisticsGrain>(refundId);
                result.Data = await logisticsGrain.QueryReturnTrackAsync(refundId, expressCompanyName ?? "", shipOrderNumber ?? "");
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询退货物流失败: RefundId={RefundId}", refundId);
                result.ErrorMessage = "查询退货物流失败";
            }
            return result;
        }

        [HttpGet("companies")]
        public ActionResult<List<string>> GetExpressCompanies()
        {
            return Ok(new List<string>
            {
                "顺丰速运",
                "中通快递",
                "圆通速递",
                "韵达快递",
                "申通快递",
                "百世快递",
                "极兔速递",
                "邮政EMS"
            });
        }
    }
}

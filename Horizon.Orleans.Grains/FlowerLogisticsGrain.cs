using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerLogisticsGrain : Grain, ILogisticsGrain
    {
        private readonly ILogger<FlowerLogisticsGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerLogisticsTrack, long> _trackContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly KdniaoApiClient _kdniaoApiClient;

        public FlowerLogisticsGrain(
            ILogger<FlowerLogisticsGrain> logger,
            IDataContext<FlowerEntityContext, FlowerLogisticsTrack, long> trackContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            KdniaoApiClient kdniaoApiClient)
        {
            _logger = logger;
            _trackContext = trackContext;
            _orderContext = orderContext;
            _kdniaoApiClient = kdniaoApiClient;
        }

        public async Task<LogisticsTrackState> QueryTrackAsync(long orderId, string expressCompanyName, string shipOrderNumber)
        {
            if (string.IsNullOrEmpty(expressCompanyName) || string.IsNullOrEmpty(shipOrderNumber))
            {
                return new LogisticsTrackState { OrderId = orderId, LogisticsStatus = (int)LogisticsStatus.NoTrack };
            }

            var existing = await _trackContext.QueryFirstOrDefaultAsync(
                t => t.OrderId == orderId && !t.IsReturn);

            if (existing != null && existing.LastQueriedAt.HasValue &&
                (DateTime.Now - existing.LastQueriedAt.Value).TotalMinutes < 30)
            {
                return MapToState(existing);
            }

            var (trackData, originCity, destinationCity, currentLocation) = await QueryAndParseThirdPartyApiAsync(expressCompanyName, shipOrderNumber);
            var logisticsStatus = DetermineLogisticsStatus(trackData);

            if (existing != null)
            {
                existing.TrackData = trackData;
                existing.LastQueriedAt = DateTime.Now;
                existing.LogisticsStatus = logisticsStatus;
                existing.OriginCity = originCity;
                existing.DestinationCity = destinationCity;
                existing.CurrentLocation = currentLocation;
                await _trackContext.UpdateAsync(existing, existing.Id);
                return MapToState(existing);
            }

            var entity = new FlowerLogisticsTrack
            {
                OrderId = orderId,
                ExpressCompanyName = expressCompanyName,
                ShipOrderNumber = shipOrderNumber,
                TrackData = trackData,
                LastQueriedAt = DateTime.Now,
                LogisticsStatus = logisticsStatus,
                IsReturn = false,
                OriginCity = originCity,
                DestinationCity = destinationCity,
                CurrentLocation = currentLocation
            };
            var result = await _trackContext.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<LogisticsTrackState> QueryReturnTrackAsync(long refundId, string expressCompanyName, string shipOrderNumber)
        {
            if (string.IsNullOrEmpty(expressCompanyName) || string.IsNullOrEmpty(shipOrderNumber))
            {
                return new LogisticsTrackState { RefundId = refundId, IsReturn = true, LogisticsStatus = (int)LogisticsStatus.NoTrack };
            }

            var existing = await _trackContext.QueryFirstOrDefaultAsync(
                t => t.RefundId == refundId && t.IsReturn);

            if (existing != null && existing.LastQueriedAt.HasValue &&
                (DateTime.Now - existing.LastQueriedAt.Value).TotalMinutes < 30)
            {
                return MapToState(existing);
            }

            var (trackData, originCity, destinationCity, currentLocation) = await QueryAndParseThirdPartyApiAsync(expressCompanyName, shipOrderNumber);
            var logisticsStatus = DetermineLogisticsStatus(trackData);

            if (existing != null)
            {
                existing.TrackData = trackData;
                existing.LastQueriedAt = DateTime.Now;
                existing.LogisticsStatus = logisticsStatus;
                existing.OriginCity = originCity;
                existing.DestinationCity = destinationCity;
                existing.CurrentLocation = currentLocation;
                await _trackContext.UpdateAsync(existing, existing.Id);
                return MapToState(existing);
            }

            var entity = new FlowerLogisticsTrack
            {
                OrderId = 0,
                RefundId = refundId,
                ExpressCompanyName = expressCompanyName,
                ShipOrderNumber = shipOrderNumber,
                TrackData = trackData,
                LastQueriedAt = DateTime.Now,
                LogisticsStatus = logisticsStatus,
                IsReturn = true,
                OriginCity = originCity,
                DestinationCity = destinationCity,
                CurrentLocation = currentLocation
            };
            var result = await _trackContext.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<List<LogisticsTrackState>> GetTrackHistoryAsync(long orderId)
        {
            var entities = await _trackContext.QueryAsync(t => t.OrderId == orderId);
            return entities.OrderByDescending(t => t.LastQueriedAt).Select(MapToState).ToList();
        }

        public async Task CheckAndUpdateTrackAsync(long orderId)
        {
            var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null || string.IsNullOrEmpty(order.ExpressCompanyName) || string.IsNullOrEmpty(order.ShipOrderNumber))
                return;

            await QueryTrackAsync(orderId, order.ExpressCompanyName, order.ShipOrderNumber);
        }

        private async Task<(string TrackData, string OriginCity, string DestinationCity, string CurrentLocation)> QueryAndParseThirdPartyApiAsync(string expressCompanyName, string shipOrderNumber)
        {
            var shipperCode = KdniaoExpressMapping.GetCode(expressCompanyName);
            if (string.IsNullOrEmpty(shipperCode))
            {
                _logger.LogWarning("未找到快递公司编码映射: {Company}", expressCompanyName);
                return ("", "", "", "");
            }

            string rawResponse;
            try
            {
                rawResponse = await _kdniaoApiClient.QueryAsync(shipperCode, shipOrderNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "快递鸟API调用异常: {Company} {Number}", expressCompanyName, shipOrderNumber);
                return ("", "", "", "");
            }

            if (string.IsNullOrEmpty(rawResponse))
            {
                _logger.LogWarning("快递鸟API返回空响应: {Company} {Number}", expressCompanyName, shipOrderNumber);
                return ("", "", "", "");
            }

            try
            {
                var jObj = JObject.Parse(rawResponse);
                var success = jObj["Success"]?.Value<bool>() ?? false;
                if (!success)
                {
                    var reason = jObj["Reason"]?.Value<string>() ?? "未知原因";
                    _logger.LogWarning("快递鸟API返回失败: {Reason}", reason);
                    return ("", "", "", "");
                }

                var traces = jObj["Traces"] as JArray;
                if (traces == null || traces.Count == 0)
                {
                    return ("", "", "", "");
                }

                var enhancedTraces = new List<object>();
                string originCity = "";
                string destinationCity = "";
                string currentLocation = "";

                foreach (var trace in traces)
                {
                    var acceptTime = trace["AcceptTime"]?.Value<string>() ?? "";
                    var acceptStation = trace["AcceptStation"]?.Value<string>() ?? "";
                    var location = trace["Location"]?.Value<string>() ?? "";

                    var cityName = CityGeoMapping.ExtractCityName(location);
                    var coords = CityGeoMapping.GetCoordinates(cityName);

                    var enhancedTrace = new Dictionary<string, object>
                    {
                        { "time", acceptTime },
                        { "context", acceptStation },
                        { "location", location }
                    };

                    if (coords.HasValue)
                    {
                        enhancedTrace["lat"] = coords.Value.Lat;
                        enhancedTrace["lng"] = coords.Value.Lng;
                    }

                    enhancedTraces.Add(enhancedTrace);
                }

                var firstTrace = traces.First();
                var lastTrace = traces.Last();

                var firstLocation = firstTrace["Location"]?.Value<string>() ?? "";
                originCity = CityGeoMapping.ExtractCityName(firstLocation);

                var lastLocation = lastTrace["Location"]?.Value<string>() ?? "";
                var lastStation = lastTrace["AcceptStation"]?.Value<string>() ?? "";

                if (lastStation.Contains("签收"))
                {
                    destinationCity = CityGeoMapping.ExtractCityName(lastLocation);
                }
                else
                {
                    destinationCity = CityGeoMapping.ExtractCityName(lastLocation);
                }

                currentLocation = lastStation;

                var trackData = JsonConvert.SerializeObject(enhancedTraces);
                return (trackData, originCity, destinationCity, currentLocation);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析快递鸟响应失败: {Company} {Number}", expressCompanyName, shipOrderNumber);
                return ("", "", "", "");
            }
        }

        private int DetermineLogisticsStatus(string trackData)
        {
            if (string.IsNullOrEmpty(trackData)) return (int)LogisticsStatus.NoTrack;
            if (trackData.Contains("已签收")) return (int)LogisticsStatus.Signed;
            if (trackData.Contains("派送中")) return (int)LogisticsStatus.Delivering;
            if (trackData.Contains("转运") || trackData.Contains("发出") || trackData.Contains("到达")) return (int)LogisticsStatus.InTransit;
            if (trackData.Contains("揽收")) return (int)LogisticsStatus.Collected;
            return (int)LogisticsStatus.InTransit;
        }

        private LogisticsTrackState MapToState(FlowerLogisticsTrack entity)
        {
            if (entity == null) return null;
            return new LogisticsTrackState
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                ExpressCompanyName = entity.ExpressCompanyName ?? "",
                ShipOrderNumber = entity.ShipOrderNumber ?? "",
                TrackData = entity.TrackData ?? "",
                LastQueriedAt = entity.LastQueriedAt,
                LogisticsStatus = entity.LogisticsStatus,
                IsReturn = entity.IsReturn,
                RefundId = entity.RefundId,
                OriginCity = entity.OriginCity ?? "",
                DestinationCity = entity.DestinationCity ?? "",
                CurrentLocation = entity.CurrentLocation ?? ""
            };
        }
    }
}

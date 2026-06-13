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
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerDataPoolController : OrleansControllerBase
    {
        private readonly ILogger<FlowerDataPoolController> _logger;

        public FlowerDataPoolController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerDataPoolController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost]
        public async Task<ResultVM<long>> WriteAsync([FromBody] DataPoolEntry entry)
        {
            var result = new ResultVM<long>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.WriteAsync(entry);
                result.IsSuccess = result.Data > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入数据池失败: DataType={DataType}", entry?.DataType);
                result.ErrorMessage = "写入数据池失败";
            }
            return result;
        }

        [HttpGet]
        public async Task<ResultVM<List<DataPoolEntry>>> QueryAsync([FromQuery] int? dataType, [FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime, [FromQuery] int pageNo = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<DataPoolEntry>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                var skip = (pageNo - 1) * pageSize;

                List<DataPoolEntry> allEntries = new();

                if (dataType.HasValue && dataType.Value >= 0)
                {
                    var dt = (DataPoolDataType)dataType.Value;
                    allEntries = await grain.QueryByTypeAsync(dt, startTime, endTime, skip, pageSize);
                }
                else
                {
                    var allTypes = Enum.GetValues<DataPoolDataType>();
                    foreach (var dt in allTypes)
                    {
                        var entries = await grain.QueryByTypeAsync(dt, startTime, endTime, 0, pageSize * 2);
                        allEntries.AddRange(entries);
                    }
                    allEntries = allEntries.OrderByDescending(e => e.Timestamp).Skip(skip).Take(pageSize).ToList();
                }

                result.Data = allEntries;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询数据池失败: DataType={DataType}", dataType);
                result.ErrorMessage = "查询数据池失败";
            }
            return result;
        }
    }
}

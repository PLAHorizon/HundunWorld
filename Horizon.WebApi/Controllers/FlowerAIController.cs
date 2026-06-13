using System;
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
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("flower-ai")]
    [Authorize]
    public class FlowerAIController : OrleansControllerBase
    {
        private readonly ILogger<FlowerAIController> _logger;

        public FlowerAIController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerAIController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpPost("chat")]
        public async Task<ResultVM<AIChatResponse>> ChatAsync([FromBody] AIChatRequest request)
        {
            var result = new ResultVM<AIChatResponse>();
            try
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                {
                    result.ErrorMessage = "问题不能为空";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IDashboardGrain>(0);

                var summary = await grain.GetAIMarketSummaryAsync();

                result.Data = new AIChatResponse
                {
                    Answer = summary,
                    Timestamp = DateTime.UtcNow
                };
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI聊天失败: UserId={UserId}", request.UserId);
                result.ErrorMessage = "AI服务暂时不可用";
            }
            return result;
        }
    }

    public class AIChatRequest
    {
        public long UserId { get; set; }
        public string Question { get; set; }
        public string ConversationId { get; set; }
    }

    public class AIChatResponse
    {
        public string Answer { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

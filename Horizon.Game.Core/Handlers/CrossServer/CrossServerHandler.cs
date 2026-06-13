using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.CrossServer;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Handlers.CrossServer
{
    public class CrossServerHandler : MessageHandlerBase
    {
        private readonly ICrossServerService _crossServerService;

        public CrossServerHandler(
            ILogger<CrossServerHandler> logger, 
            global::Orleans.IClusterClient clusterClient, 
            HorizonMessageAdapter adapter,
            ICrossServerService crossServerService) 
            : base(logger, clusterClient, adapter)
        {
            _crossServerService = crossServerService;
        }

        public override List<MessageType> MessageTypes => new()
        {
            MessageType.CrossServerTransferRequest
        };

        public override ServiceType ServiceType => ServiceType.CrossServer;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            var header = message.Header;
            
            return header.MessageType switch
            {
                MessageType.CrossServerTransferRequest => await HandleCrossServerTransferAsync(message),
                _ => (false, null)
            };
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCrossServerTransferAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is not CrossServerTransferRequest request) return (false, null);

                var response = new CrossServerTransferResponse();
                
                try
                {
                    if (int.TryParse(request.TargetSceneId, out int islandId))
                    {
                        var success = await _crossServerService.TransferPlayerToIslandAsync(request.CharacterId, islandId);
                        response.Success = success;
                        response.Message = success ? "转移请求已发送" : "转移失败";
                        response.NodeAddress = "Unknown";
                    }
                    else
                    {
                        response.Success = false;
                        response.Message = "岛屿ID无效";
                        response.NodeAddress = "";
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.Message = ex.Message;
                }
                
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理跨服转移请求时发生错误");
                return (false, CreateHorizonMessage(new CrossServerTransferResponse { Success = false, Message = "系统错误" }));
            }
        }
    }
}

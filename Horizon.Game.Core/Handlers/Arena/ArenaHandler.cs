using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Arena;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Core.Handlers.Arena
{
    public class ArenaHandler : MessageHandlerBase
    {
        private readonly IArenaService _arenaService;

        public ArenaHandler(
            ILogger<ArenaHandler> logger, 
            global::Orleans.IClusterClient clusterClient, 
            HorizonMessageAdapter adapter,
            IArenaService arenaService) 
            : base(logger, clusterClient, adapter)
        {
            _arenaService = arenaService;
        }

        public override List<MessageType> MessageTypes => new()
        {
            MessageType.ArenaJoinRequest,
            MessageType.ArenaLeaveRequest,
            MessageType.ArenaInfoRequest
        };

        public override ServiceType ServiceType => ServiceType.Arena;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            var header = message.Header;
            
            return header.MessageType switch
            {
                MessageType.ArenaJoinRequest => await HandleArenaJoinAsync(message),
                MessageType.ArenaLeaveRequest => await HandleArenaLeaveAsync(message),
                MessageType.ArenaInfoRequest => await HandleArenaInfoAsync(message),
                _ => (false, null)
            };
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleArenaJoinAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is not ArenaJoinRequest request) return (false, null);

                await _arenaService.JoinMatchmakingAsync(request.CharacterId);
                
                var response = new ArenaJoinResponse 
                {
                    Success = true, 
                    Message = ""
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "加入匹配队列时发生错误");
                return (false, CreateHorizonMessage(new ArenaJoinResponse { Success = false, Message = "处理失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleArenaLeaveAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is not ArenaLeaveRequest request) return (false, null);

                await _arenaService.CancelMatchmakingAsync(request.CharacterId);
                
                var response = new ArenaLeaveResponse 
                {
                    Success = true, 
                    Message = ""
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "取消匹配队列时发生错误");
                return (false, CreateHorizonMessage(new ArenaLeaveResponse { Success = false, Message = "处理失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleArenaInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is not ArenaInfoRequest request) return (false, null);

                var info = await _arenaService.GetPlayerArenaInfoAsync(request.CharacterId);
                
                var response = new ArenaInfoResponse 
                {
                    MMR = info.CurrentRating,
                    RankName = "Unranked",
                    Wins = info.Wins,
                    Losses = info.TotalMatches - info.Wins
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取竞技场信息时发生错误");
                return (false, null);
            }
        }
    }
}

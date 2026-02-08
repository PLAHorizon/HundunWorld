using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class GameGrain : IGameGrain
    {
        private readonly IDataContext<GameEntityContext, GameEntity, int> _gContext;
        private readonly IDataContext<GameEntityContext, ZoneEntity, int> _gzContext;
        private readonly IDataContext<GameEntityContext, ServerEntity, int> _gsContext;
        private readonly IMapper _mapper;
        private readonly ILogger<GameGrain>? _logger;

        public GameGrain(IDataContext<GameEntityContext, GameEntity, int> gContext,
            IDataContext<GameEntityContext, ZoneEntity, int> gzContext,
            IDataContext<GameEntityContext, ServerEntity, int> gsContext,
            IMapper mapper,
            ILogger<GameGrain>? logger = null)
        {
            _gContext = gContext;
            _gzContext = gzContext;
            _gsContext = gsContext;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<List<ServerInfo>> GetServerListAsync(GameQueryDto gameQueryDto)
        {
            try
            {
                _logger?.LogInformation("查询服务器列表: GameId={GameId}", gameQueryDto.GameId);

                var zones = await _gzContext.QueryAsync(z => z.GameId == gameQueryDto.GameId);
                if (zones == null || !zones.Any())
                {
                    _logger?.LogWarning("未找到游戏区域: GameId={GameId}", gameQueryDto.GameId);
                    return new List<ServerInfo>();
                }

                var zoneIds = zones.Select(z => z.Id).ToList();
                var servers = await _gsContext.QueryAsync(s => zoneIds.Contains(s.ZoneId));

                _logger?.LogInformation("查询服务器列表成功: GameId={GameId}, ZoneCount={ZoneCount}, ServerCount={ServerCount}",
                    gameQueryDto.GameId, zones.Count(), servers.Count());

                return _mapper.Map<List<ServerInfo>>(servers.ToList());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "查询服务器列表失败: GameId={GameId}", gameQueryDto.GameId);
                throw;
            }
        }
    }
}

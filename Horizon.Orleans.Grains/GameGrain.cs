using Amazon.Runtime.Internal.Util;
using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.EntityFrameworkCore;
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
        public GameGrain(IDataContext<GameEntityContext, GameEntity, int> gContext,
            IDataContext<GameEntityContext, ZoneEntity, int> gzContext,
            IDataContext<GameEntityContext, ServerEntity, int> gsContext,
            IMapper mapper)
        {
            _gContext = gContext;
            _gzContext = gzContext;
            _gsContext = gsContext;
            _mapper = mapper;
        }
        public async Task<List<ServerInfo>> GetServerListAsync(GameQueryDto gameQueryDto)
        {
           
            var zones =await  _gzContext.QueryAsync(z => z.GameId == gameQueryDto.GameId);
            var servers =await  _gsContext.QueryAsync(s => zones.Select(z => z.Id).Contains(s.ZoneId));
            return _mapper.Map<List<ServerInfo>>( servers.ToList());
        }
    }
}

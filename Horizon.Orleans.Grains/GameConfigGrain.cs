using Horizon.Core;
using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Entities;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏配置Grain实现
    /// </summary>
    public class GameConfigGrain : Grain, IGameConfigGrain
    {
        private readonly IDataContext<GameEntityContext, GameEntity, int> _gameDataContext;
        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly ILogger<GameConfigGrain> _logger;

        public GameConfigGrain(
            IDataContext<GameEntityContext, GameEntity, int> gameDataContext,
            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            ILogger<GameConfigGrain> logger)
        {
            _gameDataContext = gameDataContext;
            _gameUserContext = gameUserContext;
            _logger = logger;
        }

        public async Task<GameServersDto> GetGameServersAsync(int gameId)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == gameId && m.IsValid);
                if (game == null)
                {
                    return new GameServersDto { AppId = gameId };
                }

                var gameUserEntities = await _gameUserContext.QueryAsync(m => m.GameId == gameId && !m.IsDeleted);

                var dto = new GameServersDto
                {
                    AppType = AppType.Game,
                    AppId = game.Id,
                    AppName = game.GameName,
                    AppDescritpion = game.GameDescription,
                    Areas = new List<ServerAreaDto>
                    {
                        new ServerAreaDto
                        {
                            Id = 0,
                            GameId = game.Id,
                            Name = "默认分区",
                            Description = "默认游戏分区",
                            Servers = gameUserEntities.Select(u => new ServerDto
                            {
                                AreaId = 0,
                                GameId = game.Id,
                                Id = (int)u.Id,
                                Name = u.AccountName,
                                Description = string.Empty,
                                Ip = string.Empty,
                                Port = 0,
                                Status = (GameAreaServerStatus)u.Status
                            }).ToList()
                        }
                    }
                };

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取游戏服务器列表失败: GameId={GameId}", gameId);
                return new GameServersDto { AppId = gameId };
            }
        }

        public async Task<bool> AddGameAsync(GameServersDto dto)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == dto.AppId && m.IsValid);
                if (game != null)
                {
                    _logger.LogWarning("游戏已存在: GameId={GameId}", dto.AppId);
                    return false;
                }

                var newGame = new GameEntity
                {
                    Id = dto.AppId,
                    GameName = dto.AppName ?? string.Empty,
                    GameDescription = dto.AppDescritpion ?? string.Empty,
                    GameVersion = "1.0.0",
                    Developer = string.Empty,
                    Publisher = string.Empty,
                    Genre = string.Empty,
                    Platform = string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsValid = true
                };

                await _gameDataContext.AddAsync(newGame);
                _logger.LogInformation("游戏添加成功: GameId={GameId}, Name={Name}", newGame.Id, newGame.GameName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加游戏失败");
                return false;
            }
        }

        public async Task<bool> AddServerAsync(ServerDto dto)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == dto.GameId && m.IsValid);
                if (game == null)
                {
                    _logger.LogWarning("添加服务器失败，游戏不存在: GameId={GameId}", dto.GameId);
                    return false;
                }

                var serverEntity = new UserEntity
                {
                    GameId = dto.GameId,
                    AccountName = dto.Name,
                    Status = (int)dto.Status,
                    CreateTime = DateTime.Now,
                    LastLoginTime = DateTime.Now,
                    IsDeleted = false,
                    ServerId = dto.Id,
                    AreaId = dto.AreaId
                };

                await _gameUserContext.AddAsync(serverEntity);
                _logger.LogInformation("服务器添加成功: GameId={GameId}, ServerId={ServerId}", dto.GameId, dto.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加服务器失败");
                return false;
            }
        }

        public async Task<List<GameInfoDto>> GetGamesAsync()
        {
            try
            {
                var games = await _gameDataContext.QueryAsync(m => m.IsValid);
                var dtos = games.Select(g => new GameInfoDto
                {
                    id = g.Id,
                    name = g.GameName,
                    description = g.GameDescription,
                    version = g.GameVersion,
                    genre = g.Genre,
                    platform = g.Platform,
                    developer = g.Developer,
                    publisher = g.Publisher,
                    coverUrl = g.CoverUrl,
                    downloadUrl = g.GameAssetsUrl.Length > 0 ? g.GameAssetsUrl[0] : string.Empty,
                    tags = g.Tags,
                    features = g.Features,
                    screenshots = g.Screenshots
                }).ToList();

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取游戏列表失败");
                return new List<GameInfoDto>();
            }
        }

        public async Task<GameInfoDto> GetGameAsync(int gameId)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == gameId && m.IsValid);
                if (game == null)
                {
                    return new GameInfoDto { id = gameId };
                }

                return new GameInfoDto
                {
                    id = game.Id,
                    name = game.GameName,
                    description = game.GameDescription,
                    version = game.GameVersion,
                    genre = game.Genre,
                    platform = game.Platform,
                    developer = game.Developer,
                    publisher = game.Publisher,
                    coverUrl = game.CoverUrl,
                    downloadUrl = game.GameAssetsUrl.Length > 0 ? game.GameAssetsUrl[0] : string.Empty,
                    tags = game.Tags,
                    features = game.Features,
                    screenshots = game.Screenshots
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取游戏详情失败: GameId={GameId}", gameId);
                return new GameInfoDto { id = gameId };
            }
        }

        public async Task<bool> UpdateGameAsync(int gameId, GameInfoDto dto)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == gameId && m.IsValid, isTracking: true);
                if (game == null)
                {
                    _logger.LogWarning("更新游戏失败，游戏不存在: GameId={GameId}", gameId);
                    return false;
                }

                game.GameName = dto.name ?? game.GameName;
                game.GameDescription = dto.description ?? game.GameDescription;
                game.GameVersion = dto.version ?? game.GameVersion;
                game.Genre = dto.genre ?? game.Genre;
                game.Platform = dto.platform ?? game.Platform;
                game.Developer = dto.developer ?? game.Developer;
                game.Publisher = dto.publisher ?? game.Publisher;
                game.CoverUrl = dto.coverUrl ?? game.CoverUrl;
                game.Tags = dto.tags ?? game.Tags;
                game.Features = dto.features ?? game.Features;
                game.Screenshots = dto.screenshots ?? game.Screenshots;
                game.UpdatedAt = DateTime.UtcNow;

                await _gameDataContext.UpdateAsync(game, game.Id);
                _logger.LogInformation("游戏更新成功: GameId={GameId}", gameId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新游戏失败: GameId={GameId}", gameId);
                return false;
            }
        }

        public async Task<bool> DeleteGameAsync(int gameId)
        {
            try
            {
                var game = await _gameDataContext.QueryFirstOrDefaultAsync(m => m.Id == gameId && m.IsValid, isTracking: true);
                if (game == null)
                {
                    _logger.LogWarning("删除游戏失败，游戏不存在: GameId={GameId}", gameId);
                    return false;
                }

                game.IsValid = false;
                game.UpdatedAt = DateTime.UtcNow;

                await _gameDataContext.UpdateAsync(game, game.Id);
                _logger.LogInformation("游戏删除成功: GameId={GameId}", gameId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除游戏失败: GameId={GameId}", gameId);
                return false;
            }
        }
    }
}

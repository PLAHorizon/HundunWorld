using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameConfigService : GameApiServiceBase
{
    public GameConfigService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetServersAsync(int gameId)
        => await GetAsync<JsonElement>($"/GameConfig/servers?gameId={gameId}");

    public async Task<ResultVM<JsonElement>?> GetGameListAsync()
        => await GetAsync<JsonElement>("/GameConfig/games");

    public async Task<ResultVM<JsonElement>?> GetGameDetailAsync(int gameId)
        => await GetAsync<JsonElement>($"/GameConfig/games/{gameId}");

    public async Task<ResultVM<bool>?> AddGameAsync(object dto)
        => await PostAsync<bool>("/GameConfig/AddGame", dto);

    public async Task<ResultVM<bool>?> UpdateGameAsync(int gameId, object dto)
        => await PutAsync<bool>($"/GameConfig/games/{gameId}", dto);

    public async Task<ResultVM<bool>?> DeleteGameAsync(int gameId)
        => await DeleteAsync<bool>($"/GameConfig/games/{gameId}");

    public async Task<ResultVM<bool>?> AddServerAsync(object dto)
        => await PostAsync<bool>("/GameConfig/AddServer", dto);
}
